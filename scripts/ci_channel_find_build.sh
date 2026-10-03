#!/usr/bin/env bash
# ADR-0204 §6: find the pull request build of build.yml whose binaries ARE a
# build of what just landed on `prod`, so ci-channel-publish.yml can republish
# them instead of rebuilding the whole matrix.
#
# Usage: scripts/ci_channel_find_build.sh <merge-commit-sha> <pr-head-sha>
# Env:   GH_TOKEN (actions: read, contents: read), GITHUB_REPOSITORY
#
# Prints the run id and exits 0 when one qualifies. Exits 1 with nothing on
# stdout when none does; the reason for every rejected candidate goes to stderr.
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
# conclusion), so a failed or cancelled matrix can never feed a partial
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

merge_tree="$(gh api "repos/$REPO/git/commits/$MERGE_SHA" --jq '.tree.sha')"
if ! [[ "$merge_tree" =~ ^[0-9a-f]{40}$ ]]; then
  echo "FAIL: could not read the tree of merge commit $MERGE_SHA" >&2
  exit 1
fi

runs="$(gh api "repos/$REPO/actions/workflows/build.yml/runs?event=pull_request&status=success&head_sha=$HEAD_SHA&per_page=30" \
  --jq "[.workflow_runs[] | select(.conclusion == \"success\" and .head_sha == \"$HEAD_SHA\")] | sort_by(.created_at) | reverse | .[].id")"

if [ -z "$runs" ]; then
  echo "no successful pull_request run of build.yml for head $HEAD_SHA" >&2
  exit 1
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

for id in $runs; do
  dir="$tmp/$id"
  if ! gh run download "$id" --repo "$REPO" -n ci-channel-provenance -D "$dir" >/dev/null 2>&1; then
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
