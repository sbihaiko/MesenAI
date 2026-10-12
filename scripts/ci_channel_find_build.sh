#!/usr/bin/env bash
# ADR-0204 §6: find the pull request build of build.yml whose binaries ARE a
# build of what just landed on `prod`, so ci-channel-publish.yml can republish
# them instead of rebuilding the whole matrix.
#
# Usage: scripts/ci_channel_find_build.sh <merge-commit-sha> <pr-head-sha>
# Env:   GH_TOKEN (actions: read, contents: read), GITHUB_REPOSITORY
#
# The exit code separates a verdict from the absence of one:
#
#   0  a qualifying run was found; its id is the only thing on stdout
#   1  the lookup ran to completion and no candidate qualifies
#   2  bad usage: wrong argument count, or a sha that is not 40 lowercase hex
#   3  no verdict: a read was refused or otherwise failed, so this script does
#      not know whether a candidate exists. GitHub's own message, and the
#      request id and timestamp it returned, go to stderr.
#
# A refusal must never be reported as "nothing qualifies". That sentence is a
# claim about the repository - the caller writes it into the job summary and
# dispatches the full matrix on the strength of it - so a rate limit collapsed
# into exit 1 turns a throttle into a fact nobody verified.
#
# Why the reads ask for the response headers: `gh api --include` writes the
# status line and the headers to stdout ahead of the body, and `--jq` still
# filters the body on a 2xx. The headers are the only place GitHub's request id
# and timestamp appear - gh's own error line is just "<message> (HTTP 403)" -
# and they are what lets the log prove the throttle rather than assert it.
#
# Why tree equality, and why it is read from the build rather than inferred:
#
# A pull_request run of build.yml compiles `refs/pull/N/merge` - GitHub's merge
# preview of the head INTO THE BASE AS IT WAS WHEN THE EVENT FIRED. That base is
# not exposed by the runs API (a run carries only head_sha), the preview commit
# is not kept on any branch, and `prod` can move between the build and the
# merge without firing a new pull_request event. So neither "same head sha" nor
# "merge-tree(first parent of the merge, head) == merge tree" proves anything
# about the tree the run compiled: the second is true by construction for any
# merge commit and says nothing about the base the build saw. Reconstructing
# that base from timestamps (run created_at vs when `prod` last moved) needs the
# ref's history, which git does not keep and the API keeps only partly.
#
# Instead build.yml's `provenance` job records `github.sha` - the very commit
# every build job checked out - and its tree, as the `ci-channel-provenance`
# artifact. A run qualifies when that recorded tree equals the merge commit's
# tree. Equal trees are equal inputs, whatever the merge method (merge commit,
# squash or rebase) and whatever `prod` did in between; a run that predates the
# provenance job, or whose artifacts expired, simply does not qualify, and the
# caller falls back to dispatching build.yml.
#
# Only successful runs are candidates (status=success, re-checked on the
# conclusion), so a failed or canceled matrix can never feed a partial
# channel. Candidates are tried newest first.
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <merge-commit-sha> <pr-head-sha>" >&2
  exit 2
fi
MERGE_SHA="$1"
HEAD_SHA="$2"
REPO="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY must name owner/repo}"

# Both shas are spliced into a jq program and a query string below; accept
# nothing but a full lowercase hex object id.
for sha in "$MERGE_SHA" "$HEAD_SHA"; do
  if ! [[ "$sha" =~ ^[0-9a-f]{40}$ ]]; then
    echo "FAIL: '$sha' is not a 40-hex commit sha" >&2
    exit 2
  fi
done

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# gh api --include writes the status line and the response headers to stdout,
# then a blank line, then the body. tr strips the CR of the wire's CRLF so that
# blank line really is blank, and the two halves split there.
headers_of() { tr -d '\r' < "$1" | sed -n '1,/^$/p'; }
body_of() { tr -d '\r' < "$1" | sed -e '1,/^$/d'; }

# A read that was refused or otherwise failed reached no verdict: exit 3, and
# name what GitHub said, plus the request id and timestamp it returned. $1 is
# what was being read, $2 the captured stdout (headers, when there are any) and
# $3 the captured stderr.
no_verdict() {
  echo "NO VERDICT: the read of $1 was refused or failed, so no qualifying run could be established" >&2
  echo "NO VERDICT: GitHub said: $(tr '\n' ' ' < "$3" | tr -s ' ')" >&2
  local hdr rid date
  hdr="$(headers_of "$2")"
  rid="$(printf '%s\n' "$hdr" | grep -i '^x-github-request-id:' | tail -1 | sed -e 's/^[^:]*:[[:space:]]*//' || true)"
  date="$(printf '%s\n' "$hdr" | grep -i '^date:' | tail -1 | sed -e 's/^[^:]*:[[:space:]]*//' || true)"
  echo "NO VERDICT: GitHub request id ${rid:-<none returned>}, GitHub timestamp ${date:-<none returned>}" >&2
  exit 3
}

if ! gh api "repos/$REPO/git/commits/$MERGE_SHA" --include --jq '.tree.sha' \
     >"$tmp/merge.out" 2>"$tmp/merge.err"; then
  no_verdict "merge commit $MERGE_SHA" "$tmp/merge.out" "$tmp/merge.err"
fi

merge_tree="$(body_of "$tmp/merge.out")"
if ! [[ "$merge_tree" =~ ^[0-9a-f]{40}$ ]]; then
  echo "NO VERDICT: the read of merge commit $MERGE_SHA returned no tree, so no qualifying run could be established" >&2
  exit 3
fi

if ! gh api "repos/$REPO/actions/workflows/build.yml/runs?event=pull_request&status=success&head_sha=$HEAD_SHA&per_page=30" \
     --include \
     --jq "[.workflow_runs[] | select(.conclusion == \"success\" and .head_sha == \"$HEAD_SHA\")] | sort_by(.created_at) | reverse | .[].id" \
     >"$tmp/runs.out" 2>"$tmp/runs.err"; then
  no_verdict "the runs of build.yml for head $HEAD_SHA" "$tmp/runs.out" "$tmp/runs.err"
fi

runs="$(body_of "$tmp/runs.out")"
if [ -z "$runs" ]; then
  echo "no successful pull_request run of build.yml for head $HEAD_SHA" >&2
  exit 1
fi

for id in $runs; do
  dir="$tmp/$id"
  if ! gh run download "$id" --repo "$REPO" -n ci-channel-provenance -D "$dir" >/dev/null 2>"$tmp/download.err"; then
    # gh run download has no --include, so a refusal reaches us only as gh's
    # error line. A 403/429/5xx, a rate or abuse limit, or a transport failure
    # is a read that reached no verdict. An artifact that is simply not there -
    # built before the provenance job, or expired after 90 days - is a
    # completed read that answers "no" about this candidate, and the next
    # candidate is still worth trying.
    if grep -qiE 'HTTP (403|429|5[0-9][0-9])|rate limit|abuse|timed out|timeout|connection|dial tcp|no such host|unexpected EOF' "$tmp/download.err"; then
      no_verdict "the ci-channel-provenance artifact of run $id" /dev/null "$tmp/download.err"
    fi
    echo "run $id: no ci-channel-provenance artifact (built before ADR-0204 §6, or expired)" >&2
    continue
  fi
  built_tree=""
  if [ -f "$dir/ci-channel-provenance.txt" ]; then
    built_tree="$(sed -n 's/^tree=//p' "$dir/ci-channel-provenance.txt")"
  fi
  if [ "$built_tree" = "$merge_tree" ]; then
    echo "run $id: compiled tree $built_tree, the merge commit's tree" >&2
    echo "$id"
    exit 0
  fi
  echo "run $id: compiled tree ${built_tree:-<unreadable>}, merge commit $MERGE_SHA has $merge_tree" >&2
done

echo "no successful pull_request run of build.yml for head $HEAD_SHA compiled tree $merge_tree" >&2
exit 1
