#!/usr/bin/env bash
# ADR-0204 §3/§6: stage a build.yml run's artifacts under the six fixed asset
# names of the `ci-latest` pre-release - the names the README's download links
# are. This is the ONLY place the artifact-name -> asset-name mapping lives:
# build.yml's `publish` job and ci-channel-publish.yml both run this script, so
# the two publishers cannot drift apart. verify_download_channel.sh reads the
# asset names from here and matches them against the README.
#
# Usage: scripts/stage_ci_channel_assets.sh <artifacts-dir> <out-dir>
#
# <artifacts-dir> holds one directory per artifact, named after the artifact -
# the layout both actions/download-artifact (no `name:`) and `gh run download`
# (no `-n`) produce. <out-dir> must be absent or empty.
#
# All or nothing: every input is checked before anything is written, so a run
# with an expired, missing or renamed artifact stages no file at all and exits
# non-zero, and the caller publishes nothing rather than a partial channel.
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <artifacts-dir> <out-dir>" >&2
  exit 2
fi
ART="$1"
OUT="$2"

# The artifact names carry the matrix coordinates and may change with the
# matrix; the asset names below are the README's URLs and must not.
LINUX_X64="$ART/Mesen (Linux - ubuntu-22.04 - clang_aot)/Mesen"
LINUX_ARM64="$ART/Mesen (Linux - ubuntu-22.04-arm - clang_aot)/Mesen"
WINDOWS_AOT="$ART/Mesen (Windows - net10.0 - AoT)/Mesen.exe"
APPIMAGE_X64="$ART/Mesen (Linux x64 - AppImage)/Mesen.AppImage"
APPIMAGE_ARM64="$ART/Mesen (Linux ARM64 - AppImage)/Mesen.AppImage"
MACOS_ARM64="$ART/Mesen (macOS - macos-15 - clang_aot)/Mesen.app.zip"

missing=0
for input in "$LINUX_X64" "$LINUX_ARM64" "$WINDOWS_AOT" \
             "$APPIMAGE_X64" "$APPIMAGE_ARM64" "$MACOS_ARM64"; do
  if [ ! -f "$input" ]; then
    echo "MISSING: $input" >&2
    missing=1
  fi
done
if [ "$missing" -ne 0 ]; then
  echo "FAIL: the run is missing artifacts; nothing was staged (ADR-0204 §6: never publish a partial channel)" >&2
  exit 1
fi

if [ -e "$OUT" ] && [ -n "$(ls -A "$OUT")" ]; then
  echo "FAIL: $OUT is not empty; every file in it would be uploaded to the release" >&2
  exit 1
fi
mkdir -p "$OUT"

zip -q -j "$OUT/MesenAI-ci-linux-x64.zip"       "$LINUX_X64"
zip -q -j "$OUT/MesenAI-ci-linux-arm64.zip"     "$LINUX_ARM64"
zip -q -j "$OUT/MesenAI-ci-windows-x64-aot.zip" "$WINDOWS_AOT"
cp "$APPIMAGE_X64"   "$OUT/MesenAI-ci-linux-x64.AppImage"
cp "$APPIMAGE_ARM64" "$OUT/MesenAI-ci-linux-arm64.AppImage"
cp "$MACOS_ARM64"    "$OUT/MesenAI-ci-macos-arm64.zip"
ls -l "$OUT"
