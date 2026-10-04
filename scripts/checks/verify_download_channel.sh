#!/usr/bin/env bash
# ADR-0204: the README's download links are fixed-name assets of a rolling
# pre-release tagged `ci-latest`, published by the `publish` job in
# `build.yml`. They used to be nightly.link URLs, which can only ever resolve
# against a PUSH run - and this workflow has had no push trigger since
# ADR-0191, so every one of them was a guaranteed 404 regardless of how many
# times the workflow was dispatched.
#
# The ADR was accepted and implemented in the same change, so per CLAUDE.md
# these greps ARE its unit tests. They fail the moment:
#   - the `publish` job loses its shape (needs / event guard / contents write),
#   - one of the six asset names changes on either side of the translation
#     between artifact name and asset name,
#   - the README gains or loses a download link, or the two sides stop naming
#     the same six files,
#   - nightly.link comes back into the README,
#   - or the rolling tag stops being the same string in both files.
#
# ADR-0204 §6 (2026-10-03, "pode seguir com a A") adds the second publisher,
# `ci-channel-publish.yml`, which republishes a promotion pull request's own
# build when prod's merged tree is the tree that build compiled, and falls back
# to dispatching build.yml otherwise. The artifact-to-asset translation and the
# release call moved out of build.yml into two scripts both publishers run, so
# the six names and the pre-release flag are asserted once, in the scripts, and
# both workflows are asserted to call them. Sections 6-8 below hold that half;
# the behaviour of the three scripts is unit-tested by
# scripts/test_ci_channel_scripts.py.
#
# What this file deliberately does NOT assert: that the links currently resolve.
# That needs the network and a published release; it is checked by
# `scripts/checks/verify_download_links.sh`, which is not part of doc-checks
# for exactly that reason.
set -euo pipefail

WORKFLOW=".github/workflows/build.yml"
MERGE_WORKFLOW=".github/workflows/ci-channel-publish.yml"
STAGE="scripts/stage_ci_channel_assets.sh"
PUBLISH_SCRIPT="scripts/publish_ci_channel.sh"
FIND_BUILD="scripts/ci_channel_find_build.sh"
README="README.md"
TAG="ci-latest"
FAIL=0

fail() {
  echo "FAIL: $1" >&2
  FAIL=1
}

# Comment lines are prose about the code, and prose names the flags it
# explains; every assertion below reads code lines only.
code_of() {
  if [ -f "$1" ]; then
    grep -vE '^[[:space:]]*#' "$1" || true
  fi
}

for f in "$MERGE_WORKFLOW" "$STAGE" "$PUBLISH_SCRIPT" "$FIND_BUILD"; do
  if [ ! -f "$f" ]; then
    fail "$f is missing; the download channel's merge-time publisher depends on it (ADR-0204 §6)"
  fi
done
STAGE_CODE="$(code_of "$STAGE")"
PUBLISH_SCRIPT_CODE="$(code_of "$PUBLISH_SCRIPT")"
FIND_BUILD_CODE="$(code_of "$FIND_BUILD")"
MERGE_CODE="$(code_of "$MERGE_WORKFLOW")"

# The `publish` job's block: from its top-level key to the next one. Anchored
# on two-space indentation, so `    steps:` and friends stay inside it.
PUBLISH="$(awk '/^  publish:/{f=1} f && /^  [a-z]/ && !/^  publish:/{f=0} f' "$WORKFLOW")"

# The job's own comments are prose about these flags, and one of them names
# `--prerelease` to explain why it is load-bearing - so a search over the raw
# block is satisfied by the explanation after the flag itself is deleted.
# Everything below matches against the code only. (Found by mutating this
# section; it is the third comment-read-as-code blind spot in this file's
# short life.)
PUBLISH_CODE="$(printf '%s\n' "$PUBLISH" | grep -vE '^[[:space:]]*#' || true)"

if [ -z "$PUBLISH" ]; then
  fail "$WORKFLOW has no 'publish' job; the download channel has nothing to publish with (ADR-0204)"
else
  # 1. Shape: it runs after every build job, publishes nothing on a pull
  #    request, and is allowed to write releases.
  for need in windows macos linux appimage; do
    if ! printf '%s\n' "$PUBLISH_CODE" | grep -E "^    needs:.*\b$need\b|^    needs: \[.*\b$need\b" >/dev/null; then
      fail "$WORKFLOW's publish job does not depend on '$need'; it would publish a partial matrix (ADR-0204 §1)"
    fi
  done
  if ! printf '%s\n' "$PUBLISH_CODE" | grep "github.event_name != 'pull_request'" >/dev/null; then
    fail "$WORKFLOW's publish job lost its event guard; a pull request run executes at refs/pull/N/merge and would publish unmerged code (ADR-0204 §1)"
  fi
  if ! printf '%s\n' "$PUBLISH_CODE" | grep -E "^      contents: write" >/dev/null; then
    fail "$WORKFLOW's publish job has no 'contents: write' permission; gh release upload fails with 403 without it (ADR-0204 §1)"
  fi
  # The translation and the release call are the shared scripts' now; the
  #    job only has to run them, in that order, from a checkout.
  if ! printf '%s\n' "$PUBLISH_CODE" | grep -E "run: .*$STAGE artifacts out|^[[:space:]]+$STAGE artifacts out" >/dev/null; then
    fail "$WORKFLOW's publish job does not stage through $STAGE; the artifact-to-asset mapping would exist twice and drift (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$PUBLISH_CODE" | grep -E "$PUBLISH_SCRIPT out" >/dev/null; then
    fail "$WORKFLOW's publish job does not publish through $PUBLISH_SCRIPT (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$PUBLISH_CODE" | grep "uses: actions/checkout@" >/dev/null; then
    fail "$WORKFLOW's publish job has no checkout; the shared scripts it runs would not exist on the runner (ADR-0204 §6)"
  fi
fi

# 1b. The release call: pre-release, --clobber, one tag.
if ! printf '%s\n' "$PUBLISH_SCRIPT_CODE" | grep -- "--prerelease" >/dev/null; then
  fail "$PUBLISH_SCRIPT no longer marks the rolling release a pre-release; releases/latest would start resolving to a CI build instead of mesence-v0.1.0 (ADR-0204 §2)"
fi
if ! printf '%s\n' "$PUBLISH_SCRIPT_CODE" | grep -- "--clobber" >/dev/null; then
  fail "$PUBLISH_SCRIPT no longer uploads with --clobber; the second publish fails on the asset already existing (ADR-0204 §2)"
fi

# 2. The tag is one string, used by the workflow and named by the README.
if ! printf '%s\n' "$PUBLISH_SCRIPT_CODE" | grep -E "^TAG=$TAG$" >/dev/null; then
  fail "$PUBLISH_SCRIPT does not set TAG=$TAG; the README's URLs would point at a release nobody publishes (ADR-0204 §2)"
fi

# 3. The six asset names, exactly, on both sides of the translation. The
#    artifact names carry matrix coordinates and may change; these may not.
#    The staging script is the only place they are written; a workflow that
#    names one itself has started a second copy of the mapping.
workflow_assets="$(printf '%s\n' "$STAGE_CODE" | grep -oE '\$OUT/MesenAI-ci-[A-Za-z0-9._-]+' | sed 's#\$OUT/##' | sort -u || true)"
for wf in "$WORKFLOW" "$MERGE_WORKFLOW"; do
  if code_of "$wf" | grep "MesenAI-ci-" >/dev/null; then
    fail "$wf names a MesenAI-ci-* asset itself; the mapping lives in $STAGE only, so both publishers stage the same six files (ADR-0204 §6)"
  fi
done
readme_assets="$(grep -oE "releases/download/$TAG/[A-Za-z0-9._-]+" "$README" | sed "s#releases/download/$TAG/##" | sort -u)"

if [ -z "$workflow_assets" ]; then
  fail "$STAGE stages no assets under fixed names; the README's URLs have nothing to bind to (ADR-0204 §3)"
fi
if [ "$workflow_assets" != "$readme_assets" ]; then
  fail "the asset names staged by $STAGE and the ones the README links are not the same set (ADR-0204 §3):
--- staging script
$workflow_assets
--- README
$readme_assets"
fi
for asset in MesenAI-ci-linux-x64.zip MesenAI-ci-linux-x64.AppImage \
             MesenAI-ci-linux-arm64.zip MesenAI-ci-linux-arm64.AppImage \
             MesenAI-ci-macos-arm64.zip MesenAI-ci-windows-x64-aot.zip; do
  if ! printf '%s\n' "$workflow_assets" | grep -x "$asset" >/dev/null; then
    fail "$STAGE no longer stages '$asset' (ADR-0204 §3); the README links it"
  fi
done

# 4. nightly.link is gone from the README. It is not a fallback: it resolves a
#    branch against a push run, and there is no push trigger to resolve.
if grep -q "nightly\.link" "$README"; then
  fail "$README links nightly.link again; it resolves a branch against a PUSH run and this workflow has no push trigger, so the link 404s (ADR-0204 §4)"
fi

# 5. The release the assets live on must be reachable by a plain HTTP GET: a
#    pre-release is, an Actions artifact is not (that one needs a session).
if grep -qE "\[Download\]\(https://github\.com/sbihaiko/MesenAI/actions/" "$README"; then
  fail "$README links an Actions run/artifact for download; those require a GitHub session and are not public downloads (ADR-0204 §4)"
fi

# 6. build.yml records the tree each run compiles. The merge-time publisher
#    compares prod's merged tree against it; without the record it can only
#    fall back to a rebuild. The job must run on a pull request - that is the
#    run whose tree matters - so it carries no event guard.
PROVENANCE="$(awk '/^  provenance:/{f=1} f && /^  [a-z]/ && !/^  provenance:/{f=0} f' "$WORKFLOW" | grep -vE '^[[:space:]]*#' || true)"
if [ -z "$PROVENANCE" ]; then
  fail "$WORKFLOW has no 'provenance' job; the merge-time publisher cannot tell which tree a pull request build compiled (ADR-0204 §6)"
else
  if ! printf '%s\n' "$PROVENANCE" | grep "name: ci-channel-provenance" >/dev/null; then
    fail "$WORKFLOW's provenance job does not upload the 'ci-channel-provenance' artifact $FIND_BUILD reads (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$PROVENANCE" | grep "github.sha" >/dev/null; then
    fail "$WORKFLOW's provenance job does not record github.sha, the merge-preview commit the build jobs check out (ADR-0204 §6)"
  fi
  if printf '%s\n' "$PROVENANCE" | grep -E "^    if:" >/dev/null; then
    fail "$WORKFLOW's provenance job is conditional; it must run on the pull request build, which is the run being compared (ADR-0204 §6)"
  fi
fi

# 7. The merge-time publisher's shape.
if [ -n "$MERGE_CODE" ]; then
  if ! printf '%s\n' "$MERGE_CODE" | grep -E "^  pull_request:$" >/dev/null; then
    fail "$MERGE_WORKFLOW has no pull_request trigger (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$MERGE_CODE" | grep -E "^    types: \[closed\]$" >/dev/null; then
    fail "$MERGE_WORKFLOW does not fire on 'types: [closed]' only; any other activity type runs it on an unmerged pull request (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$MERGE_CODE" | grep -E "^    branches: \[prod\]$" >/dev/null; then
    fail "$MERGE_WORKFLOW is not filtered to the 'prod' base branch; a merge into main would republish prod's channel (ADR-0204 §6)"
  fi
  if printf '%s\n' "$MERGE_CODE" | grep -E "^  (push|workflow_dispatch|schedule|pull_request_target):" >/dev/null; then
    fail "$MERGE_WORKFLOW has a trigger besides pull_request: closed (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$MERGE_CODE" | grep "github.event.pull_request.merged == true" >/dev/null; then
    fail "$MERGE_WORKFLOW lost its merged guard; a pull request closed without merging would publish its build (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$MERGE_CODE" | grep "github.event.pull_request.head.repo.full_name == github.repository" >/dev/null; then
    fail "$MERGE_WORKFLOW lost its same-repository guard (ADR-0204 §6)"
  fi
  for perm in "contents: write" "actions: write"; do
    if ! printf '%s\n' "$MERGE_CODE" | grep -E "^      $perm$" >/dev/null; then
      fail "$MERGE_WORKFLOW's job lacks '$perm'; it needs contents to upload and actions to read the build and dispatch the fallback (ADR-0204 §6)"
    fi
  done
  if ! printf '%s\n' "$MERGE_CODE" | grep -E "$FIND_BUILD .*merge_commit_sha|$FIND_BUILD \"\\\$MERGE_SHA\" \"\\\$HEAD_SHA\"" >/dev/null; then
    fail "$MERGE_WORKFLOW does not pick the build through $FIND_BUILD with the merge commit and the head sha (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$MERGE_CODE" | grep "MERGE_SHA: \${{ github.event.pull_request.merge_commit_sha }}" >/dev/null ||
     ! printf '%s\n' "$MERGE_CODE" | grep "HEAD_SHA: \${{ github.event.pull_request.head.sha }}" >/dev/null; then
    fail "$MERGE_WORKFLOW does not take MERGE_SHA/HEAD_SHA from the event's merge_commit_sha and head.sha (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$MERGE_CODE" | grep -E "$STAGE artifacts out" >/dev/null; then
    fail "$MERGE_WORKFLOW does not stage through $STAGE (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$MERGE_CODE" | grep -E "$PUBLISH_SCRIPT out" >/dev/null; then
    fail "$MERGE_WORKFLOW does not publish through $PUBLISH_SCRIPT (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$MERGE_CODE" | grep -E "gh workflow run build.yml .*--ref prod" >/dev/null; then
    fail "$MERGE_WORKFLOW has no fallback 'gh workflow run build.yml ... --ref prod'; a merge whose build cannot be reused would publish nothing (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$MERGE_CODE" | grep "GITHUB_STEP_SUMMARY" >/dev/null; then
    fail "$MERGE_WORKFLOW does not say in the job summary which path it took (ADR-0204 §6)"
  fi
fi

# 8. The tree check itself: the candidate builds are the successful
#    pull_request runs of build.yml for this head, and the verdict is tree
#    equality against the merge commit - not a commit, branch or time match.
if [ -n "$FIND_BUILD_CODE" ]; then
  if ! printf '%s\n' "$FIND_BUILD_CODE" | grep "actions/workflows/build.yml/runs" >/dev/null; then
    fail "$FIND_BUILD does not list build.yml's runs (ADR-0204 §6)"
  fi
  for q in "event=pull_request" "head_sha=" "status=success"; do
    if ! printf '%s\n' "$FIND_BUILD_CODE" | grep -- "$q" >/dev/null; then
      fail "$FIND_BUILD's run query lost '$q'; it could pick a failed, cancelled or unrelated run (ADR-0204 §6)"
    fi
  done
  if ! printf '%s\n' "$FIND_BUILD_CODE" | grep -- "-n ci-channel-provenance" >/dev/null; then
    fail "$FIND_BUILD does not read the ci-channel-provenance artifact (ADR-0204 §6)"
  fi
  if ! printf '%s\n' "$FIND_BUILD_CODE" | grep '\.tree\.sha' >/dev/null; then
    fail "$FIND_BUILD does not compare against the merge commit's tree (ADR-0204 §6)"
  fi
fi

if [ "$FAIL" -ne 0 ]; then
  exit 1
fi

echo "PASS: ADR-0204 (build.yml and ci-channel-publish.yml publish the six fixed-name assets of the ci-latest pre-release through one staging script; build.yml publishes nothing on a pull request and records the tree it built; ci-channel-publish.yml reuses a merged promotion PR's build only on tree equality and dispatches build.yml otherwise; the README links exactly those assets with no nightly.link left)"
