#!/usr/bin/env bash
# replace_file_atomic.sh <src> <dst>
#
# Copy <src> to <dst> as a new file (new inode): write a temp file in <dst>'s
# directory, then rename it over <dst>. Use it for every signed Mach-O the
# build drops onto an existing path (issue #628): a plain `cp` rewrites the old
# inode in place, and on macOS any process that loads it afterwards is
# SIGKILLed when the old image was still mapped somewhere, because the kernel
# keeps the code signature it validated for that vnode. Same behavior on Linux,
# where the rename is equally harmless.
set -euo pipefail

if [[ $# -ne 2 ]]; then
	echo "usage: $0 <src> <dst>" >&2
	exit 2
fi
src="$1"
dst="$2"
if [[ ! -f "$src" ]]; then
	echo "error: source not found: $src" >&2
	exit 1
fi

# A new file named after the PID, so `cp` creates it with <src>'s mode.
tmp="$(dirname "$dst")/.$(basename "$dst").tmp.$$"
trap 'rm -f "$tmp"' EXIT
rm -f "$tmp"
cp "$src" "$tmp"
mv -f "$tmp" "$dst"
trap - EXIT
