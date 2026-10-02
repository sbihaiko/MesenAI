#!/usr/bin/env bash
# ADR-0237 / PRD slice P.8 stop condition (2): every headless_record output is
# byte-identical with and without a shader configured. A shader is a display
# effect only; screenshots, captures, OAM/sprite dumps, recorded HD packs
# (hires.txt and its PNGs), sync traces and the audio exports never see it.
#
# For each mode below it runs scripts/headless_record three times on the same
# ROM, each with its own sandbox home. The ROM sits at the same path for all
# three (the path is recorded in the save state and the log) but in a folder
# that is emptied before each run and moved into the run's folder after it:
# "bootstrap" writes its pack beside the ROM, and a folder left in place would
# let one run load what the previous one wrote.
#   base     no shader
#   control  no shader again - the determinism control. A file that differs
#            between base and control is not a fact about shaders; it is
#            reported and the check FAILS, because then the comparison below
#            would prove nothing for that file.
#   shader   shader=<preset> (the fixture preset by default)
# and then compares every file the runs wrote, byte for byte, plus the tool's
# stdout with the lines that carry wall-clock time or a run-specific path
# removed. Two kinds of file carry the wall clock by construction and are
# compared on their content instead, and the report says so per file:
#   *.log         each line's leading HH:MM:SS.mmm stamp and "<n> ms" timings
#                 are dropped (core/MessageManager log)
#   *.rgd, *.zip  compared member by member (names and bytes); the zip's own
#                 DOS timestamps are not compared (RecentGames entry)
#
# Modes: "screenshot capture"  final-frame PNG + in-process capture checksum
#        "hdpack"              recorded HD pack (hires.txt, PNGs) + synctrace
#        "bootstrap"           the bootstrap recording, the one path that turns
#                              on the builder's screen capture: the auto pack
#                              beside the ROM and its sheets, plus the builder's
#                              OAM stream / sheet grid / pose track debug dumps
#                              (MESEN_*_DUMP, written into the run's folder)
#        (default)             the .mid/.vgm audio exports
# Not covered: "live=<ms>", whose publishing is lossy-latest by design and so
# differs between two runs without any shader.
#
# On-demand tool, not a CI gate: it needs a built core dylib and a ROM.
#
# Build once:  make capture-tool
# Usage:       scripts/check_headless_shader_invariance.sh <rom> [seconds] [preset] [workdir] [input-script]
# Defaults:    seconds=4  preset=tests/fixtures/shaders/scanlines.slangp
#              workdir=$(mktemp -d)  no input (attract mode); an input script
#              (headless_record input=) that reaches gameplay gives the sprite
#              dumps something to say
# Exit code:   0 = invariant holds, 1 = an output differs / setup missing.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ROM="${1:-}"
SECONDS_TO_RUN="${2:-4}"
PRESET="${3:-$REPO_ROOT/tests/fixtures/shaders/scanlines.slangp}"
WORKDIR="${4:-$(mktemp -d)}"
INPUT="${5:-}"
# HEADLESS_RECORD overrides the binary (the checker's own negative control
# runs it through a wrapper that alters an output when shader= is passed).
RECORDER="${HEADLESS_RECORD:-$REPO_ROOT/scripts/headless_record}"

if [[ -z "$ROM" || ! -f "$ROM" ]]; then
	echo "error: ROM not found: '${ROM}' (usage: $0 <rom> [seconds] [preset] [workdir])" >&2
	exit 1
fi
if [[ ! -x "$RECORDER" ]]; then
	echo "error: $RECORDER not found - run 'make capture-tool' first" >&2
	exit 1
fi
if [[ ! -f "$PRESET" ]]; then
	echo "error: shader preset not found: $PRESET" >&2
	exit 1
fi
ROM="$(cd "$(dirname "$ROM")" && pwd)/$(basename "$ROM")"
if [[ -n "$INPUT" ]]; then
	[[ -f "$INPUT" ]] || { echo "error: input script not found: $INPUT" >&2; exit 1; }
	INPUT="$(cd "$(dirname "$INPUT")" && pwd)/$(basename "$INPUT")"
fi
PRESET="$(cd "$(dirname "$PRESET")" && pwd)/$(basename "$PRESET")"
mkdir -p "$WORKDIR"

# The run's own folder is replaced by a fixed token, and lines that report wall
# clock are dropped; everything else on stdout must match exactly.
normalize_stdout() {
	sed -e "s#$1#<RUN>#g" | grep -v -E 'wall|elapsed| ms\b|[0-9]+\.[0-9]+ ?s\b' || true
}

run_variant() {
	local mode="$1" variant="$2"; shift 2
	local dir="$WORKDIR/$mode/$variant"
	rm -rf "$dir"
	mkdir -p "$dir"
	local romdir="$WORKDIR/$mode/rom"
	rm -rf "$romdir"
	mkdir -p "$romdir"
	cp -f "$ROM" "$romdir/"
	local args=()
	[[ "$mode" != "audio" ]] && read -r -a args <<<"${mode//_/ }"
	[[ -n "$INPUT" ]] && args+=("input=$INPUT")
	[[ "$variant" == "shader" ]] && args+=("shader=$PRESET")
	local dumps=()
	if [[ "$mode" == "bootstrap" ]]; then
		dumps=(MESEN_OAM_STREAM_DUMP="$dir/oam-stream.txt" MESEN_SHEET_GRID_DUMP="$dir/sheet-grid.txt" MESEN_POSE_TRACK_DUMP="$dir/pose-tracks.txt")
	fi
	if ! (cd "$REPO_ROOT" && env ${dumps[@]+"${dumps[@]}"} "$RECORDER" "$romdir/$(basename "$ROM")" "$SECONDS_TO_RUN" "$dir/out" "${args[@]+"${args[@]}"}") >"$dir.stdout" 2>"$dir.stderr"; then
		echo "error: headless_record failed (mode=$mode variant=$variant); stderr:" >&2
		cat "$dir.stderr" >&2
		exit 1
	fi
	mv "$romdir" "$dir/rom"
	normalize_stdout "$dir" <"$dir.stdout" >"$dir.stdout.norm"
	# The shader run must really have handed the preset to the core, or the
	# comparison would be no-shader against no-shader.
	if [[ "$variant" == "shader" ]] && ! grep -F "shader configured: $PRESET" "$dir.stderr" >/dev/null; then
		echo "error: the shader run did not report configuring $PRESET" >&2
		exit 1
	fi
}

# Lists every regular file under a run folder, relative to it.
list_files() { (cd "$1" && find . -type f | LC_ALL=C sort); }

FAIL=0
compare_pair() {
	local mode="$1" a="$2" b="$3" label="$4"
	local da="$WORKDIR/$mode/$a" db="$WORKDIR/$mode/$b"
	local fa fb
	fa="$(list_files "$da")"; fb="$(list_files "$db")"
	if [[ "$fa" != "$fb" ]]; then
		echo "FAIL [$mode] $label: the two runs wrote different file sets"
		diff <(echo "$fa") <(echo "$fb") | sed 's/^/       /'
		FAIL=1
		return
	fi
	local n=0 bad=0 f verdict
	while IFS= read -r f; do
		[[ -z "$f" ]] && continue
		n=$((n + 1))
		verdict="$(python3 - "$da/$f" "$db/$f" <<'PY'
import re, sys, zipfile
a, b = sys.argv[1], sys.argv[2]
if open(a, 'rb').read() == open(b, 'rb').read():
	print('bytes'); sys.exit()
if a.endswith(('.rgd', '.zip')) and zipfile.is_zipfile(a) and zipfile.is_zipfile(b):
	za, zb = zipfile.ZipFile(a), zipfile.ZipFile(b)
	same = za.namelist() == zb.namelist() and all(za.read(n) == zb.read(n) for n in za.namelist())
	print('members' if same else 'DIFF'); sys.exit()
if a.endswith('.log'):
	def norm(path):
		text = open(path, 'rb').read().decode('utf-8', 'replace')
		text = re.sub(r'(?m)^\d\d:\d\d:\d\d\.\d{3} ', '', text)
		return re.sub(r'\b\d+ ms\b', '<n> ms', text)
	print('log-content' if norm(a) == norm(b) else 'DIFF'); sys.exit()
print('DIFF')
PY
)"
		case "$verdict" in
			bytes) ;;
			members) echo "     [$mode] $label: $f - zip members identical (zip timestamps not compared)" ;;
			log-content) echo "     [$mode] $label: $f - identical once wall-clock stamps are removed" ;;
			*) echo "FAIL [$mode] $label: $f differs"; bad=$((bad + 1)) ;;
		esac
	done <<<"$fa"
	if ! cmp -s "$da.stdout.norm" "$db.stdout.norm"; then
		echo "FAIL [$mode] $label: stdout differs"
		diff "$da.stdout.norm" "$db.stdout.norm" | sed -n -e '1,20s/^/       /p'
		bad=$((bad + 1))
	fi
	if [[ $bad -gt 0 ]]; then
		FAIL=1
	else
		echo "OK   [$mode] $label: $n files identical, stdout identical"
	fi
}

echo "rom:    $ROM"
echo "preset: $PRESET"
echo "work:   $WORKDIR"
echo "input:  ${INPUT:-(none)}"
for mode in screenshot_capture hdpack bootstrap audio; do
	for variant in base control shader; do
		run_variant "$mode" "$variant"
	done
	compare_pair "$mode" base control "determinism control (no shader, twice)"
	compare_pair "$mode" base shader "no shader vs shader=$(basename "$PRESET")"
	# The PNGs/hires.txt are the point: a mode that wrote nothing proves nothing.
	count="$(list_files "$WORKDIR/$mode/base" | grep -c -E '\.(png|txt|csv|mid|vgm|json)$' || true)"
	if [[ "$count" -eq 0 ]]; then
		echo "FAIL [$mode] the run wrote no output file to compare"
		FAIL=1
	fi
done

if [[ $FAIL -ne 0 ]]; then
	echo ""
	echo "FAIL: a headless output depends on the shader setting (or on the run)"
	exit 1
fi
echo ""
echo "PASS: every headless_record output is byte-identical with and without a shader"
