#!/usr/bin/env bash
# ADR-0204 §2/§6: upload a staged directory to the rolling `ci-latest`
# pre-release, creating it on first use. Shared by build.yml's `publish` job and
# ci-channel-publish.yml, so the tag, the pre-release flag and the release's
# title and notes exist once.
#
# Usage: scripts/publish_ci_channel.sh <out-dir>
# Env:   GH_TOKEN (contents: write), GITHUB_REPOSITORY
#
# <out-dir> is what scripts/stage_ci_channel_assets.sh produced. Every file in it
# is uploaded with --clobber, so a file that is not a channel asset is refused
# rather than published under the README's release.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <out-dir>" >&2
  exit 2
fi
OUT="$1"
REPO="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY must name owner/repo}"
TAG=ci-latest

shopt -s nullglob
files=("$OUT"/*)
if [ "${#files[@]}" -eq 0 ]; then
  echo "FAIL: $OUT holds nothing to publish" >&2
  exit 1
fi
for f in "${files[@]}"; do
  case "${f##*/}" in
    MesenAI-ci-*) ;;
    *)
      echo "FAIL: $f is not a channel asset (MesenAI-ci-*); refusing to publish it" >&2
      exit 1
      ;;
  esac
done

# --prerelease is load-bearing: it keeps `releases/latest` - the badge, the
# README's release link, `gh release view` with no tag - resolving to the real
# release instead of to a CI build.
if ! gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then
  gh release create "$TAG" --repo "$REPO" \
    --prerelease \
    --title "CI builds (not a release)" \
    --notes "Rolling build of the \`prod\` branch, replaced on every publish (ADR-0204). Not a release: the tagged release is macOS Apple Silicon only. Unzip and run; see the README's Download section for each platform's prerequisite."
fi
gh release upload "$TAG" "${files[@]}" --repo "$REPO" --clobber
