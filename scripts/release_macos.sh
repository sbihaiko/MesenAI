#!/usr/bin/env bash
# release_macos.sh - build the macOS Apple Silicon release of MesenAI from this
# working tree and lay the artifacts out under out/release/.
#
# Invoked by `make release-macos` (VERSION=v0.1.0 by default). Everything it
# produces is reproducible from one command, so the release is a build, not a
# sequence of remembered steps:
#
#   out/release/MesenAI-<version>-macos-arm64.zip   Mesen.app + headless_record
#   out/release/mesenai-tools-<version>.zip         the Python tools the guide uses
#   out/release/SHA256SUMS                          hashes of both
#
# Why local and not CI: the project's CI compiles Linux only, and build.yml has
# no macOS leg left to run — ADR-0191 deleted it, so no trigger of that
# workflow can produce this artifact. The first release is therefore
# cut on a maintainer's Mac; docs/releases/mesence-v0.1.0.md says so and says
# from which commit.
#
# The trap this script exists to close: `dotnet publish -t:BundleApp` does not
# refresh MesenCore.dylib inside the .app when the C# build is already up to
# date, so a bundle can carry a stale core while every timestamp looks right.
# The freshly built dylib is copied in (verified byte for byte), the bundle is
# ad-hoc codesigned, and then the linker's LC_UUID inside the signed bundle is
# compared with the built one - signing rewrites the file, but not that stamp.
# The script FAILS if they differ. A check, not a hope.
#
# Prerequisites: macOS on Apple Silicon, the Xcode Command Line Tools, SDL2
# (`brew install sdl2`) and a .NET SDK matching UI/UI.csproj (this machine has
# it in ~/.dotnet). Use the Command Line Tools make - the /usr/bin shim fails
# with an Xcode-license error:
#
#   /Library/Developer/CommandLineTools/usr/bin/make release-macos VERSION=v0.1.0
#
# Comments are en-US per the project convention (CLAUDE.md).

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

VERSION="${VERSION:-v0.1.0}"
SKIP_BUILD="${SKIP_BUILD:-0}"

while [[ $# -gt 0 ]]; do
	case "$1" in
		--version) VERSION="$2"; shift ;;
		--skip-build) SKIP_BUILD=1 ;;
		--help|-h)
			cat <<-'EOF'
				Usage: scripts/release_macos.sh [--version vX.Y.Z] [--skip-build]
				  --version     names the artifacts (default: v0.1.0, or $VERSION)
				  --skip-build  package what is already built (for re-running the packaging only)
			EOF
			exit 0
			;;
		*) echo "error: unknown option: $1" >&2; exit 1 ;;
	esac
	shift
done

if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "arm64" ]]; then
	echo "error: this target builds the macOS Apple Silicon release; host is $(uname -s)/$(uname -m)" >&2
	exit 1
fi

PLATFORM="osx-arm64"
SHAREDLIB="MesenCore.dylib"
CORE_DYLIB="$ROOT/InteropDLL/obj.$PLATFORM/$SHAREDLIB"
PUBLISH_APP="$ROOT/bin/$PLATFORM/Release/$PLATFORM/publish/Mesen.app"
RECORDER="$ROOT/scripts/headless_record"

OUT="$ROOT/out/release"
# ADR-0202: the artifact names come from the product name, not from the tag
# prefix - `ditto --keepParent` writes the staged folder's name into the zip,
# so these two decide what a user sees on the download page and after
# unpacking.
APP_STAGE="$ROOT/out/stage/MesenAI-$VERSION-macos-arm64"
TOOLS_STAGE="$ROOT/out/stage/mesenai-tools-$VERSION"

# The Command Line Tools make; the /usr/bin shim is an xcrun wrapper that dies
# with "You have not agreed to the Xcode license agreements" before compiling
# anything, on a machine that only has the Command Line Tools installed.
CLT="/Library/Developer/CommandLineTools"
MAKE_BIN="${MAKE_BIN:-$CLT/usr/bin/make}"
[[ -x "$MAKE_BIN" ]] || MAKE_BIN="make"

# Same for the compiler, and the CLT clang needs an explicit -isysroot or it
# cannot find <cstdint>. These have to be command-line overrides: the Makefile
# assigns CC/CXX with `:=`, which wins over the environment.
CC_OVERRIDE=()
if [[ -x "$CLT/usr/bin/clang" && -d "$CLT/SDKs/MacOSX.sdk" ]]; then
	CC_OVERRIDE=(
		"CC=$CLT/usr/bin/clang -isysroot $CLT/SDKs/MacOSX.sdk"
		"CXX=$CLT/usr/bin/clang++ -isysroot $CLT/SDKs/MacOSX.sdk"
	)
fi

# /usr/bin/otool and friends are xcrun shims that refuse to run without an
# accepted Xcode license; the Command Line Tools carry the real binaries. And
# `codesign` shells out to `codesign_allocate` through xcrun, which hits the
# same wall - CODESIGN_ALLOCATE points it at the CLT copy instead.
OTOOL="$CLT/usr/bin/otool"; [[ -x "$OTOOL" ]] || OTOOL="otool"
INSTALL_NAME_TOOL="$CLT/usr/bin/install_name_tool"
[[ -x "$INSTALL_NAME_TOOL" ]] || INSTALL_NAME_TOOL="install_name_tool"
if [[ -x "$CLT/usr/bin/codesign_allocate" ]]; then
	export CODESIGN_ALLOCATE="$CLT/usr/bin/codesign_allocate"
fi

# .NET lives outside /usr/local on this machine.
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
	export PATH="$HOME/.dotnet:$PATH"
	export DOTNET_ROOT="$HOME/.dotnet"
fi

COMMIT="$(git rev-parse HEAD)"
DIRTY=""
git diff --quiet HEAD -- || DIRTY=" (working tree has uncommitted changes)"

echo "==> MesenAI $VERSION - macOS arm64 release"
echo "    commit: $COMMIT$DIRTY"
echo "    make:   $MAKE_BIN"

# --------------------------------------------------------------------------
# 1. Build
# --------------------------------------------------------------------------
if [[ "$SKIP_BUILD" == "1" ]]; then
	echo "==> --skip-build: packaging what is already in the tree"
else
	JOBS="$(getconf _NPROCESSORS_ONLN)"
	echo "==> make -j$JOBS core"
	"$MAKE_BIN" -j"$JOBS" ${CC_OVERRIDE[@]+"${CC_OVERRIDE[@]}"} core
	echo "==> make ui DEBUG=0"
	"$MAKE_BIN" ${CC_OVERRIDE[@]+"${CC_OVERRIDE[@]}"} ui DEBUG=0
	echo "==> make capture-tool"
	"$MAKE_BIN" ${CC_OVERRIDE[@]+"${CC_OVERRIDE[@]}"} capture-tool
fi

[[ -f "$CORE_DYLIB" ]] || { echo "error: no built core at $CORE_DYLIB" >&2; exit 1; }
[[ -d "$PUBLISH_APP" ]] || { echo "error: no app bundle at $PUBLISH_APP" >&2; exit 1; }
[[ -f "$RECORDER" ]] || { echo "error: no headless_record at $RECORDER" >&2; exit 1; }

# A headless_record left with a bare `MesenCore.dylib` install name aborts at
# startup with a dyld error - which also breaks the pack smoke tests in
# `make doc-checks`. Since issue #268 the makefile prefers the Command Line
# Tools `install_name_tool` and no longer hides a failed rewrite behind
# `2>/dev/null || true`, so `make capture-tool` fails loud instead of shipping
# a broken tool. Keep the repair as belt and suspenders: the checkout may carry
# a binary built before that fix, or built on a machine with neither the CLT
# nor an accepted Xcode licence. A release build must not leave the working
# tree worse than it found it.
RECORDER_REF="$("$OTOOL" -L "$RECORDER" | tail -n +2 | awk -v lib="$SHAREDLIB" '$1 ~ lib { print $1 }' | head -n 1)"
if [[ -n "$RECORDER_REF" && "$RECORDER_REF" != "$CORE_DYLIB" ]]; then
	echo "==> repairing the checkout's headless_record (install name was \"$RECORDER_REF\")"
	"$INSTALL_NAME_TOOL" -change "$RECORDER_REF" "$CORE_DYLIB" "$RECORDER"
	codesign -f -s - "$RECORDER"
fi

# --------------------------------------------------------------------------
# 2. Inject the freshly built core, sign, and PROVE the bundle carries it
# --------------------------------------------------------------------------
echo "==> injecting the freshly built $SHAREDLIB into the .app"
cp -f "$CORE_DYLIB" "$PUBLISH_APP/Contents/MacOS/$SHAREDLIB"

BUILT_SUM="$(shasum -a 256 "$CORE_DYLIB" | cut -d' ' -f1)"
if ! cmp -s "$CORE_DYLIB" "$PUBLISH_APP/Contents/MacOS/$SHAREDLIB"; then
	echo "error: the copy into the bundle did not land byte for byte" >&2
	exit 1
fi
echo "ok: the .app now carries the freshly built core ($BUILT_SUM)"

echo "==> ad-hoc codesigning the .app"
codesign --force --deep --sign - "$PUBLISH_APP"

# Signing rewrites the dylib (the signature lives inside the Mach-O), so the
# sha256 above no longer matches by design. LC_UUID does not move: the linker
# stamps it per build, and it survives codesigning. Comparing it is what proves
# the SIGNED bundle still carries the core this run compiled, and nothing older.
macho_uuid() { "$OTOOL" -l "$1" | awk '/LC_UUID/ { f = 1 } f && $1 == "uuid" { print $2; exit }'; }
BUILT_UUID="$(macho_uuid "$CORE_DYLIB")"
BUNDLED_UUID="$(macho_uuid "$PUBLISH_APP/Contents/MacOS/$SHAREDLIB")"
if [[ -z "$BUILT_UUID" ]]; then
	echo "error: the built core has no LC_UUID, so the bundle cannot be checked" >&2
	exit 1
fi
if [[ "$BUILT_UUID" != "$BUNDLED_UUID" ]]; then
	echo "error: the core inside the signed .app ($BUNDLED_UUID) is not the one just built ($BUILT_UUID)" >&2
	exit 1
fi
echo "ok: the signed .app carries build $BUILT_UUID"

# The bundle also embeds UI/Dependencies.zip, from which the app extracts a
# core on first run. The loose file above shadows it, but a stale member is
# still worth naming out loud - it is the same staleness, one layer down.
if [[ -f "$ROOT/UI/Dependencies.zip" ]]; then
	# `grep -q` would exit on the first match, SIGPIPE the producer and, under
	# `set -o pipefail`, make the whole pipeline look like a failure. Every grep
	# in this script therefore reads its input to the end.
	if unzip -l "$ROOT/UI/Dependencies.zip" | grep "[[:space:]]$SHAREDLIB$" >/dev/null; then
		TMP_ZIP_LIB="$(mktemp -t mesencore)"
		unzip -p "$ROOT/UI/Dependencies.zip" "$SHAREDLIB" > "$TMP_ZIP_LIB"
		ZIP_UUID="$(macho_uuid "$TMP_ZIP_LIB")"
		rm -f "$TMP_ZIP_LIB"
		if [[ "$ZIP_UUID" != "$BUILT_UUID" ]]; then
			echo "note: UI/Dependencies.zip carries an older core ($ZIP_UUID); the loose copy shadows it"
		else
			echo "ok: UI/Dependencies.zip carries the same core"
		fi
	fi
fi

codesign --verify --deep --strict "$PUBLISH_APP"
echo "ok: codesign --verify passes"

# --------------------------------------------------------------------------
# 3. Relocate headless_record so it runs from a download
# --------------------------------------------------------------------------
# `make capture-tool` links it for use from a checkout: the install name of the
# core is an absolute $(CURDIR) path. That is useless in a zip, so point it at
# the copy shipped beside the binary.
echo "==> staging headless_record with a relocatable core reference"
rm -rf "$APP_STAGE"
mkdir -p "$APP_STAGE"
ditto "$PUBLISH_APP" "$APP_STAGE/Mesen.app"
cp -f "$RECORDER" "$APP_STAGE/headless_record"
cp -f "$CORE_DYLIB" "$APP_STAGE/$SHAREDLIB"

CURRENT_REF="$("$OTOOL" -L "$APP_STAGE/headless_record" | awk -v lib="$SHAREDLIB" '$1 ~ lib {print $1}' | head -n 1)"
if [[ -n "$CURRENT_REF" && "$CURRENT_REF" != "@executable_path/$SHAREDLIB" ]]; then
	"$INSTALL_NAME_TOOL" -change "$CURRENT_REF" "@executable_path/$SHAREDLIB" "$APP_STAGE/headless_record"
fi
codesign -f -s - "$APP_STAGE/headless_record"
codesign -f -s - "$APP_STAGE/$SHAREDLIB"

# `tail -n +2` drops otool's echo of the file's own path - the staged copy
# lives under out/, which is inside $ROOT, and would match on its own name.
if "$OTOOL" -L "$APP_STAGE/headless_record" | tail -n +2 | grep "$ROOT" >/dev/null; then
	echo "error: headless_record still references the build workspace:" >&2
	"$OTOOL" -L "$APP_STAGE/headless_record" >&2
	exit 1
fi
echo "ok: headless_record resolves its core through @executable_path"

cp -f "$ROOT/docs/releases/macos-zip-README.md" "$APP_STAGE/README.md"
printf '%s\n' "$VERSION" > "$APP_STAGE/VERSION"
printf '%s\n' "$COMMIT" > "$APP_STAGE/COMMIT"

# --------------------------------------------------------------------------
# 4. The tools zip
# --------------------------------------------------------------------------
echo "==> checking the tools manifest against the real import closure"
python3 "$ROOT/scripts/check_tools_zip_closure.py"

echo "==> staging the tools zip"
rm -rf "$TOOLS_STAGE"
mkdir -p "$TOOLS_STAGE/scripts" "$TOOLS_STAGE/docs"
while read -r module; do
	case "$module" in ''|'#'*) continue ;; esac
	cp "$ROOT/scripts/$module" "$TOOLS_STAGE/scripts/$module"
done < <(sed 's/#.*//' "$ROOT/scripts/tools-zip-manifest.txt")
cp "$ROOT/scripts/record_stages.sh" "$TOOLS_STAGE/scripts/record_stages.sh"
ditto "$ROOT/scripts/stages" "$TOOLS_STAGE/scripts/stages"
for doc in remastering-a-game hd-pack-authoring enhancement-ecosystem; do
	cp "$ROOT/docs/$doc.md" "$TOOLS_STAGE/docs/$doc.md"
done
# Pillow and numpy are the only non-stdlib imports in the closure above;
# check_tools_zip_closure.py fails the build if a third one ever appears.
printf 'Pillow==12.3.0\nnumpy==2.2.6\n' > "$TOOLS_STAGE/requirements.txt"
cp "$ROOT/docs/releases/tools-zip-README.md" "$TOOLS_STAGE/README.md"
printf '%s\n' "$VERSION" > "$TOOLS_STAGE/VERSION"
printf '%s\n' "$COMMIT" > "$TOOLS_STAGE/COMMIT"

echo "==> byte-compiling every packed tool"
python3 -m compileall -q "$TOOLS_STAGE/scripts"
find "$TOOLS_STAGE" -name __pycache__ -type d -exec rm -rf {} +

# --------------------------------------------------------------------------
# 5. Zip, hash, report
# --------------------------------------------------------------------------
rm -rf "$OUT"
mkdir -p "$OUT"

APP_ZIP="$OUT/MesenAI-$VERSION-macos-arm64.zip"
TOOLS_ZIP="$OUT/mesenai-tools-$VERSION.zip"

echo "==> zipping $(basename "$APP_ZIP")"
# ditto keeps the bundle's symlinks, permissions and signature intact; `zip`
# does not, and an unzipped .app that fails codesign is a broken download.
ditto -c -k --sequesterRsrc --keepParent "$APP_STAGE" "$APP_ZIP"

echo "==> zipping $(basename "$TOOLS_ZIP")"
ditto -c -k --sequesterRsrc --keepParent "$TOOLS_STAGE" "$TOOLS_ZIP"

( cd "$OUT" && shasum -a 256 "$(basename "$APP_ZIP")" "$(basename "$TOOLS_ZIP")" > SHA256SUMS )

echo
echo "==> release ready: $OUT"
echo "    version: $VERSION"
echo "    commit:  $COMMIT$DIRTY"
echo
( cd "$OUT" && ls -l && echo && cat SHA256SUMS )
