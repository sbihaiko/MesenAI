#!/usr/bin/env bash
# ADR-0202: the release artifacts are named after the product, not after the
# tag. `scripts/release_macos.sh` produces `MesenAI-<version>-macos-arm64.zip`
# and `mesenai-tools-<version>.zip`, while the release itself keeps the tag
# `mesence-v0.1.0` - so the two deliberately disagree, and a later reader who
# "finishes the rename" in either direction breaks something.
#
# The tag is a published URL and stays; the file name is what a person reads on
# the download page and after unpacking, and follows the product (ADR-0201 §1).
#
# The ADR was accepted and implemented in the same change, so per CLAUDE.md
# these greps ARE its unit tests: they fail the moment an old name returns to
# one of the four definitions in the release script, the moment `SHA256SUMS`
# stops being derived from the zip basenames (a stale one would list two files
# that no longer exist), or the moment a document that names an artifact names
# one that is not there.
set -euo pipefail

RELEASE="scripts/release_macos.sh"
RELEASE_BODY="docs/releases/mesence-v0.1.0.md"
FAIL=0

fail() {
  echo "FAIL: $1" >&2
  FAIL=1
}

# 1. The four definitions that decide what a user downloads and what they see
#    first after unpacking (`ditto --keepParent` writes the staged folder's
#    name into the zip).
for var in APP_STAGE TOOLS_STAGE APP_ZIP TOOLS_ZIP; do
  line="$(grep -E "^${var}=" "$RELEASE" || true)"
  if [ -z "$line" ]; then
    fail "$RELEASE no longer defines \$$var; ADR-0202 asserts its value"
    continue
  fi
  case "$line" in
    *MesenCE*|*mesence-tools*)
      fail "\$$var in $RELEASE carries the old name: $line (ADR-0202)"
      ;;
  esac
done

# 2. And they carry the new one, spelled as the ADR spells it.
if ! grep -q '^APP_ZIP="\$OUT/MesenAI-\$VERSION-macos-arm64\.zip"$' "$RELEASE"; then
  fail "$RELEASE's \$APP_ZIP is not \$OUT/MesenAI-\$VERSION-macos-arm64.zip (ADR-0202 §1)"
fi
if ! grep -q '^TOOLS_ZIP="\$OUT/mesenai-tools-\$VERSION\.zip"$' "$RELEASE"; then
  fail "$RELEASE's \$TOOLS_ZIP is not \$OUT/mesenai-tools-\$VERSION.zip (ADR-0202 §1)"
fi
if ! grep -q '^APP_STAGE="\$ROOT/out/stage/MesenAI-\$VERSION-macos-arm64"$' "$RELEASE"; then
  fail "$RELEASE's \$APP_STAGE does not fold out as MesenAI-\$VERSION-macos-arm64 (ADR-0202 §2)"
fi
if ! grep -q '^TOOLS_STAGE="\$ROOT/out/stage/mesenai-tools-\$VERSION"$' "$RELEASE"; then
  fail "$RELEASE's \$TOOLS_STAGE does not fold out as mesenai-tools-\$VERSION (ADR-0202 §2)"
fi

# 3. SHA256SUMS is generated from the zip basenames, which is what makes the
#    rename harmless to the hashes and what a stale copy would get wrong.
#
#    The assertion is on the hashing line itself. `basename` also appears in
#    the two progress echoes, so a file-wide search for it passes even when the
#    hash line has been changed to name full paths - found by mutation-testing
#    this check, and the reason it counts on the line instead.
SHASUM_LINE="$(grep -E 'shasum -a 256 .*SHA256SUMS' "$RELEASE" || true)"
if [ -z "$SHASUM_LINE" ]; then
  fail "$RELEASE no longer writes SHA256SUMS beside the artifacts (ADR-0202 §3)"
elif [ "$(printf '%s' "$SHASUM_LINE" | grep -o 'basename' | wc -l | tr -d ' ')" -ne 2 ]; then
  fail "$RELEASE hashes something other than the two zip basenames into SHA256SUMS: $SHASUM_LINE (ADR-0202 §3)"
fi

# 3b. The release body's file name follows the tag (ADR-0202 §5), so it is
#     asserted to exist before the checks below read it.
if [ ! -f "$RELEASE_BODY" ]; then
  fail "$RELEASE_BODY is gone; its name follows the tag mesence-v0.1.0, which ADR-0202 §5 keeps (ADR-0201 §4)"
fi

# 4. Every document that names an artifact names one that exists. The release
#    body is the page a downloader reads; the two zip READMEs travel inside
#    the zips and are copied in by the script.
if ! grep -q 'mesenai-tools-<version>\.zip' README.md; then
  fail "README.md no longer names mesenai-tools-<version>.zip (ADR-0202 §5)"
fi
if ! grep -q 'mesenai-tools-<version>\.zip' docs/releases/macos-zip-README.md; then
  fail "docs/releases/macos-zip-README.md no longer names mesenai-tools-<version>.zip (ADR-0202 §5)"
fi
if ! grep -q 'MesenAI-<version>-macos-arm64\.zip' docs/releases/tools-zip-README.md; then
  fail "docs/releases/tools-zip-README.md no longer names MesenAI-<version>-macos-arm64.zip (ADR-0202 §5)"
fi
if ! grep -q 'MesenAI-v0\.1\.0-macos-arm64\.zip' "$RELEASE_BODY"; then
  fail "$RELEASE_BODY no longer names MesenAI-v0.1.0-macos-arm64.zip; it is the published release body (ADR-0202 §3)"
fi
if ! grep -q 'mesenai-tools-v0\.1\.0\.zip' "$RELEASE_BODY"; then
  fail "$RELEASE_BODY no longer names mesenai-tools-v0.1.0.zip; it is the published release body (ADR-0202 §3)"
fi

# 5. The old names are gone from all of them - a document that names a file
#    nobody can download is worse than one that names the old file.
for doc in README.md docs/releases/macos-zip-README.md docs/releases/tools-zip-README.md "$RELEASE_BODY"; do
  if grep -q 'MesenCE-<version>\|mesence-tools-\|MesenCE-v0\.1\.0-macos\|mesence-tools-v0\.1\.0' "$doc"; then
    fail "$doc still names an artifact by its pre-ADR-0202 name"
  fi
done

# 6. The tag is NOT renamed. ADR-0202 keeps the tag and the artifact prefix
#    apart on purpose; this asserts the half that must not move, so a future
#    "make it consistent" edit fails here instead of breaking every link into
#    the release.
if ! grep -q 'mesence-vMAJOR\.MINOR\.PATCH' "$RELEASE_BODY"; then
  fail "$RELEASE_BODY no longer states the tag scheme mesence-vMAJOR.MINOR.PATCH (ADR-0201 §4)"
fi

if [ "$FAIL" -ne 0 ]; then
  exit 1
fi

echo "PASS: ADR-0202 (the release artifacts are MesenAI-<version>-macos-arm64.zip and mesenai-tools-<version>.zip, their staging folders fold out under the same names, SHA256SUMS is derived from the zip basenames, and the tag mesence-v0.1.0 is untouched)"
