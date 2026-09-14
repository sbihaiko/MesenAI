#!/usr/bin/env bash
# Run every scripts/test_*.py as its own process.
#
# Phase 11 C.1: until 2026-09-14 no CI job ran this suite. `make doc-checks`
# names a hand-picked subset file by file, which drifts the moment someone adds
# a test; `python3 -m unittest discover -s scripts` is not an option either,
# because most files here carry their own `main()` runner printing "ok" /
# "N/N passed" instead of subclassing `unittest.TestCase` (discovery collects
# roughly 40 of the ~290 cases). So: one process per file, exit code is the
# verdict, and a new `scripts/test_*.py` is picked up with no edit here.
#
# Usage: scripts/checks/run_python_tests.sh [-k PATTERN]
#   -k PATTERN   only run files whose name matches PATTERN (substring)
#
# Output: one `PASS`/`FAIL`/`SKIP <name>  <N>s` line per file, then a summary.
# Exits non-zero when any file exited non-zero.

set -u -o pipefail

cd "$(dirname "$0")/../.."

PYTHON="${PYTHON:-python3}"

# Files this runner must not execute. Keep it empty whenever possible: a test
# that cannot run in an environment should say so itself and exit 0, the way
# scripts/test_compose_editor_gui.py already skips its four windowed cases when
# `tk.Tk()` raises TclError for want of a display. Every entry below must carry
# the reason it cannot follow that rule.
#
# Format: one bare file name per line, "#" comments allowed.
SKIP_LIST=$(cat <<'EOF'
# (none)
#
# scripts/test_compose_editor_gui.py is deliberately NOT listed: it imports
# tkinter at module scope, which succeeds headlessly, and self-skips only the
# cases that need a real window. If a runner image ever ships without the
# tkinter module the import itself fails -- fix that by installing python3-tk
# in the workflow, not by adding the file here.
EOF
)

should_skip() {
  local name="$1" line
  while IFS= read -r line; do
    line="${line%%#*}"
    line="$(printf '%s' "$line" | tr -d '[:space:]')"
    [ -z "$line" ] && continue
    [ "$line" = "$name" ] && return 0
  done <<<"$SKIP_LIST"
  return 1
}

filter=""
while [ $# -gt 0 ]; do
  case "$1" in
    -k) filter="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

pass=0
fail=0
skip=0
failed_files=()
started=$(date +%s)

for path in scripts/test_*.py; do
  name="$(basename "$path")"
  if [ -n "$filter" ] && [[ "$name" != *"$filter"* ]]; then
    continue
  fi
  if should_skip "$name"; then
    printf 'SKIP %-44s  (skip list)\n' "$name"
    skip=$((skip + 1))
    continue
  fi

  t0=$(date +%s)
  # Each file is its own process: a test that calls sys.exit(), mutates
  # os.environ or leaves a Tk interpreter behind cannot poison the next one.
  output="$("$PYTHON" "$path" 2>&1)"
  status=$?
  t1=$(date +%s)
  elapsed=$((t1 - t0))

  if [ "$status" -eq 0 ]; then
    printf 'PASS %-44s  %ss\n' "$name" "$elapsed"
    pass=$((pass + 1))
  else
    printf 'FAIL %-44s  %ss  (exit %s)\n' "$name" "$elapsed" "$status"
    fail=$((fail + 1))
    failed_files+=("$name")
    printf '%s\n' "----- $name output -----"
    printf '%s\n' "$output"
    printf '%s\n' "----- end $name -----"
  fi
done

total_elapsed=$(($(date +%s) - started))

echo
echo "python suite: $pass passed, $fail failed, $skip skipped in ${total_elapsed}s"
if [ "$fail" -gt 0 ]; then
  echo "failed: ${failed_files[*]}"
  exit 1
fi
exit 0
