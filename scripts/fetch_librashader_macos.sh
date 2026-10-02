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
# What it is: a prebuilt binary, not a source build (ADR-0237 §3 as amended
# 2026-10-02). SourMesen/librashader, branch "Mesen" - the fork and branch the
# Windows and Linux legs fetch (.github/actions/get-librashader-action) -
# publishes a macOS arm64 artifact, `librashader-arm-macos`, from its "build
# librashader-capi" workflow. Unlike the Windows/Linux fetch this one is
# PINNED, so the renderer never changes under a build without a person moving
# the pin:
#   librashader commit  01febce641d9e97ff99bfa266bcb17343609e296 (branch head)
#   workflow run        33975397584 (success, 2026-09-05)
#   artifact zip sha256 ARTIFACT_ZIP_SHA256 below (GitHub's digest for it)
#   librashader.dylib   PIN_SHA256 below
#
# Where it comes from, in order:
#   1. the mirror - the same dylib, byte for byte, as an asset of this repo's
#      release MIRROR_TAG, next to the MPL-2.0/GPL-3.0 texts and the source
#      archive of the pinned commit. The mirror does not resolve until a
#      maintainer has created that release.
#   2. the original artifact of run 33975397584 through nightly.link (a run
#      URL, not the branch URL, so a newer build on the branch is never
#      served). GitHub expires it on ARTIFACT_EXPIRES; from that day on this
#      source is skipped and the mirror is the only one.
# Either way the dylib must hash to PIN_SHA256; the artifact zip must also
# hash to ARTIFACT_ZIP_SHA256. To move the pin, mirror the new build first
# (new release tag), then update all four values together and rerun
# `make metal-presenter-tests`.
#
# Usage: scripts/fetch_librashader_macos.sh [--from <dylib>] [--dest <dir>]
#                                           [--source auto|mirror|artifact]
#   --from    take a local dylib instead of downloading (e.g. a source build).
#             It is checked for arm64 and the Metal entry points, not against
#             the pin - a rebuilt binary never matches a published one byte
#             for byte - and the script says so.
#   --source  auto (default): the mirror, then the artifact while it exists;
#             mirror / artifact: that source only.
# LIBRASHADER_MIRROR_URL / LIBRASHADER_ARTIFACT_URL override the two URLs
# (any URL curl takes, file:// included); the pins still apply.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/UI/Dependencies"
FROM=""

PIN_COMMIT="01febce641d9e97ff99bfa266bcb17343609e296"
PIN_SHA256="9c47230f42a412c1952e344707787ece606d49c5b3290e0019f8657c8a1591ca"
MIRROR_TAG="librashader-macos-arm64-01febce6"
MIRROR_URL="${LIBRASHADER_MIRROR_URL:-https://github.com/sbihaiko/MesenAI/releases/download/$MIRROR_TAG/librashader.dylib}"
ARTIFACT_RUN="33975397584"
ARTIFACT_ZIP_SHA256="081f6a23a57a52fdb63ebd5b5bcd85d4827d2b8b8fabd64802115031f5a83475"
ARTIFACT_EXPIRES="2026-12-04"
ARTIFACT_URL="${LIBRASHADER_ARTIFACT_URL:-https://nightly.link/SourMesen/librashader/actions/runs/$ARTIFACT_RUN/librashader-arm-macos.zip}"
SOURCE="auto"

while [[ $# -gt 0 ]]; do
	case "$1" in
		--from) FROM="$2"; shift ;;
		--dest) DEST="$2"; shift ;;
		--source) SOURCE="$2"; shift ;;
		*) echo "error: unknown option: $1" >&2; exit 1 ;;
	esac
	shift
done
case "$SOURCE" in
	auto|mirror|artifact) ;;
	*) echo "error: --source must be auto, mirror or artifact (got $SOURCE)" >&2; exit 1 ;;
esac

# /usr/bin/lipo and /usr/bin/nm are xcrun shims that refuse to run without an
# accepted Xcode licence; the Command Line Tools carry the real binaries.
CLT=/Library/Developer/CommandLineTools/usr/bin
LIPO="$CLT/lipo"; [[ -x "$LIPO" ]] || LIPO=lipo
NM="$CLT/nm"; [[ -x "$NM" ]] || NM=nm

sha256_of() { shasum -a 256 "$1" | cut -d' ' -f1; }

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Each fetcher leaves a pin-verified dylib at $TMP/<name>/librashader.dylib and
# returns 0, or returns 1 when its source is unreachable (so auto tries the
# next). Bytes that resolve but miss a pin end the script: that is a wrong or
# tampered file, never a reason to try somewhere else.
fetch_mirror() {
	local out="$TMP/mirror"; mkdir -p "$out"
	echo "==> downloading $MIRROR_URL"
	if ! curl -fsSL --retry 3 -o "$out/librashader.dylib" "$MIRROR_URL"; then
		echo "warning: the mirror (release $MIRROR_TAG) did not resolve" >&2
		return 1
	fi
	local got; got="$(sha256_of "$out/librashader.dylib")"
	if [[ "$got" != "$PIN_SHA256" ]]; then
		echo "error: the mirror's librashader.dylib sha256 is $got; the pin (librashader $PIN_COMMIT) is $PIN_SHA256" >&2
		exit 1
	fi
	CANDIDATE="$out/librashader.dylib"; ORIGIN="mirror $MIRROR_TAG"
}

fetch_artifact() {
	local out="$TMP/artifact"; mkdir -p "$out"
	if [[ -z "${LIBRASHADER_ARTIFACT_URL:-}" && ! "$(date -u +%Y-%m-%d)" < "$ARTIFACT_EXPIRES" ]]; then
		echo "warning: the artifact of run $ARTIFACT_RUN expired on $ARTIFACT_EXPIRES; skipped" >&2
		return 1
	fi
	echo "==> downloading $ARTIFACT_URL"
	if ! curl -fsSL --retry 3 -o "$out/librashader.zip" "$ARTIFACT_URL"; then
		echo "warning: the artifact of run $ARTIFACT_RUN did not resolve" >&2
		return 1
	fi
	local got; got="$(sha256_of "$out/librashader.zip")"
	if [[ "$got" != "$ARTIFACT_ZIP_SHA256" ]]; then
		echo "error: the artifact zip sha256 is $got; the pin (run $ARTIFACT_RUN) is $ARTIFACT_ZIP_SHA256" >&2
		exit 1
	fi
	unzip -oq "$out/librashader.zip" librashader.dylib -d "$out"
	got="$(sha256_of "$out/librashader.dylib")"
	if [[ "$got" != "$PIN_SHA256" ]]; then
		echo "error: the artifact's librashader.dylib sha256 is $got; the pin (librashader $PIN_COMMIT) is $PIN_SHA256" >&2
		exit 1
	fi
	CANDIDATE="$out/librashader.dylib"; ORIGIN="artifact of run $ARTIFACT_RUN"
}

CANDIDATE=""; ORIGIN=""
if [[ -n "$FROM" ]]; then
	CANDIDATE="$FROM"
	[[ -f "$CANDIDATE" ]] || { echo "error: no dylib at $CANDIDATE" >&2; exit 1; }
else
	case "$SOURCE" in
		mirror) fetch_mirror || true ;;
		artifact) fetch_artifact || true ;;
		auto) fetch_mirror || fetch_artifact || true ;;
	esac
	if [[ -z "$CANDIDATE" ]]; then
		echo "error: no pinned librashader.dylib from --source $SOURCE (see above). Create release $MIRROR_TAG, re-pin, or pass --from <dylib>" >&2
		exit 1
	fi
fi
GOT="$(sha256_of "$CANDIDATE")"
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
	echo "ok: $DEST/librashader.dylib (arm64, sha256 $GOT, librashader $PIN_COMMIT, from the $ORIGIN)"
fi
