#!/usr/bin/env bash
# fetch_librashader_macos.sh - put the arm64 librashader.dylib (Metal runtime)
# into UI/Dependencies/ (ADR-0237). Never committed: .gitignore lists it.
#
# From UI/Dependencies/ it reaches the app the way librashader.dll does on
# Windows: UI/UI.csproj zips the folder into the embedded Dependencies.zip, and
# the app extracts it into the home folder, next to the MesenCore.dylib it
# loads. scripts/release_macos.sh also copies it into Mesen.app/Contents/MacOS
# beside the bundled core before signing. The core looks next to its own image
# first (Utilities/Video/librashader_ld.h), so either copy is found.
#
# Where it comes from: SourMesen/librashader, branch "Mesen" - the fork and
# branch the Windows and Linux legs fetch (.github/actions/get-librashader-
# action). Its "build librashader-capi" workflow publishes a macOS arm64
# artifact, `librashader-arm-macos`, built in CI from that branch. Unlike the
# Windows/Linux fetch this one is PINNED, so the renderer never changes under
# a build without a person moving the pin:
#   librashader commit  01febce641d9e97ff99bfa266bcb17343609e296 (branch head)
#   workflow run        33975397584 (success, 2026-09-05)
#   artifact zip sha256 081f6a23... (GitHub's digest for that artifact)
#   librashader.dylib   PIN_SHA256 below
# GitHub expires that artifact on 2026-12-04. After that (or when the branch
# moves) nightly.link serves a different build and this script fails on the
# sha256: re-pin from a newer run, or pass --from with a dylib built from the
# pinned commit, then rerun `make metal-presenter-tests`.
#
# Usage: scripts/fetch_librashader_macos.sh [--from <dylib>] [--dest <dir>]
#   --from   take a local dylib instead of downloading (e.g. a source build).
#            It is checked for arm64 and the Metal entry points, not against
#            the pin - a rebuilt binary never matches a published one byte for
#            byte - and the script says so.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/UI/Dependencies"
FROM=""

PIN_COMMIT="01febce641d9e97ff99bfa266bcb17343609e296"
PIN_SHA256="9c47230f42a412c1952e344707787ece606d49c5b3290e0019f8657c8a1591ca"
URL="https://nightly.link/SourMesen/librashader/workflows/build/Mesen/librashader-arm-macos.zip"

while [[ $# -gt 0 ]]; do
	case "$1" in
		--from) FROM="$2"; shift ;;
		--dest) DEST="$2"; shift ;;
		*) echo "error: unknown option: $1" >&2; exit 1 ;;
	esac
	shift
done

# /usr/bin/lipo and /usr/bin/nm are xcrun shims that refuse to run without an
# accepted Xcode licence; the Command Line Tools carry the real binaries.
CLT=/Library/Developer/CommandLineTools/usr/bin
LIPO="$CLT/lipo"; [[ -x "$LIPO" ]] || LIPO=lipo
NM="$CLT/nm"; [[ -x "$NM" ]] || NM=nm

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
if [[ -n "$FROM" ]]; then
	CANDIDATE="$FROM"
else
	echo "==> downloading $URL"
	curl -fsSL --retry 3 -o "$TMP/librashader.zip" "$URL"
	unzip -oq "$TMP/librashader.zip" librashader.dylib -d "$TMP"
	CANDIDATE="$TMP/librashader.dylib"
fi
[[ -f "$CANDIDATE" ]] || { echo "error: no dylib at $CANDIDATE" >&2; exit 1; }

GOT="$(shasum -a 256 "$CANDIDATE" | cut -d' ' -f1)"
if [[ -z "$FROM" && "$GOT" != "$PIN_SHA256" ]]; then
	echo "error: librashader.dylib sha256 is $GOT; the pin (librashader $PIN_COMMIT) is $PIN_SHA256" >&2
	exit 1
fi
if [[ "$("$LIPO" -archs "$CANDIDATE")" != "arm64" ]]; then
	echo "error: $CANDIDATE is not an arm64-only dylib ($("$LIPO" -archs "$CANDIDATE"))" >&2
	exit 1
fi
# The three entry points MacOS/MetalPresenter.mm cannot work without.
SYMS="$("$NM" -gU "$CANDIDATE")"
for sym in _libra_mtl_filter_chain_create _libra_mtl_filter_chain_frame _libra_instance_abi_version; do
	if ! grep -F " $sym" <<<"$SYMS" >/dev/null; then
		echo "error: $CANDIDATE does not export $sym (built without runtime-metal?)" >&2
		exit 1
	fi
done

mkdir -p "$DEST"
cp -f "$CANDIDATE" "$DEST/librashader.dylib"
if [[ -n "$FROM" ]]; then
	echo "ok: $DEST/librashader.dylib (arm64, sha256 $GOT) - UNPINNED, taken from $FROM"
else
	echo "ok: $DEST/librashader.dylib (arm64, sha256 $GOT, librashader $PIN_COMMIT)"
fi
