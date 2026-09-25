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
# What this file deliberately does NOT assert: that the links currently resolve.
# That needs the network and a published release; it is checked by
# `scripts/checks/verify_download_links.sh`, which is not part of doc-checks
# for exactly that reason.
set -euo pipefail

WORKFLOW=".github/workflows/build.yml"
README="README.md"
TAG="ci-latest"
FAIL=0

fail() {
  echo "FAIL: $1" >&2
  FAIL=1
}

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
  if ! printf '%s\n' "$PUBLISH_CODE" | grep -- "--prerelease" >/dev/null; then
    fail "$WORKFLOW's publish job no longer marks the rolling release a pre-release; releases/latest would start resolving to a CI build instead of mesence-v0.1.0 (ADR-0204 §2)"
  fi
  if ! printf '%s\n' "$PUBLISH_CODE" | grep -- "--clobber" >/dev/null; then
    fail "$WORKFLOW's publish job no longer uploads with --clobber; the second publish fails on the asset already existing (ADR-0204 §2)"
  fi
fi

# 2. The tag is one string, used by the workflow and named by the README.
if ! grep -qE "^          TAG=$TAG$" "$WORKFLOW"; then
  fail "$WORKFLOW's publish job does not set TAG=$TAG; the README's URLs would point at a release nobody publishes (ADR-0204 §2)"
fi

# 3. The six asset names, exactly, on both sides of the translation. The
#    artifact names carry matrix coordinates and may change; these may not.
workflow_assets="$(grep -oE "out/MesenAI-ci-[A-Za-z0-9._-]+" "$WORKFLOW" | sed 's#out/##' | sort -u)"
readme_assets="$(grep -oE "releases/download/$TAG/[A-Za-z0-9._-]+" "$README" | sed "s#releases/download/$TAG/##" | sort -u)"

if [ -z "$workflow_assets" ]; then
  fail "$WORKFLOW stages no assets under fixed names; the README's URLs have nothing to bind to (ADR-0204 §3)"
fi
if [ "$workflow_assets" != "$readme_assets" ]; then
  fail "the asset names staged by $WORKFLOW and the ones the README links are not the same set (ADR-0204 §3):
--- workflow
$workflow_assets
--- README
$readme_assets"
fi
for asset in MesenAI-ci-linux-x64.zip MesenAI-ci-linux-x64.AppImage \
             MesenAI-ci-linux-arm64.zip MesenAI-ci-linux-arm64.AppImage \
             MesenAI-ci-macos-arm64.zip MesenAI-ci-windows-x64-aot.zip; do
  if ! printf '%s\n' "$workflow_assets" | grep -x "$asset" >/dev/null; then
    fail "$WORKFLOW no longer stages '$asset' (ADR-0204 §3); the README links it"
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

if [ "$FAIL" -ne 0 ]; then
  exit 1
fi

echo "PASS: ADR-0204 (build.yml publishes the six fixed-name assets of the ci-latest pre-release after the full matrix, publishes nothing on a pull request, and the README links exactly those assets with no nightly.link left)"
