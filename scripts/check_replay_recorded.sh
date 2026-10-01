#!/usr/bin/env bash
# ADR-0205 R.1 end-to-end proof (needs a ROM and a built core; not part of
# doc-checks). Records one run with the Record-and-share action and two runs
# with the ordinary recorder, then checks scripts/replay_lint.py accepts the
# first and refuses the others naming the section 3 reason.
#
# Usage: scripts/check_replay_recorded.sh <rom.nes> <input-script> <workdir>
# Prereq: make core capture-tool
set -euo pipefail
ROM="$1"; SCRIPT="$2"; WORK="$3"
mkdir -p "$WORK"
run() { rm -rf "$WORK/mesen-home"; ./scripts/headless_record "$ROM" 25 "$WORK/run" screenshot "input=$SCRIPT" "$@" >"$WORK/last.log" 2>&1; }
run "record-share=$WORK/share.mmo"
python3 scripts/replay_lint.py "$WORK/share.mmo"
for mode in current save-data; do
  run "record-stock=$WORK/stock-$mode.mmo@$mode"
  if python3 scripts/replay_lint.py "$WORK/stock-$mode.mmo" 2>"$WORK/err-$mode.txt"; then
    echo "FAIL: stock recording ($mode) was accepted" >&2; exit 1
  fi
  grep -q "refused \[save-state\]" "$WORK/err-$mode.txt" || { echo "FAIL: reason not named ($mode)" >&2; exit 1; }
done
echo "PASS: the action's output is accepted; stock recordings are refused (save-state)"
