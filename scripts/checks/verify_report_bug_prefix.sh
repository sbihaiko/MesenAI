#!/usr/bin/env bash
# Issue #1563: scripts/report-bug.sh must prefix every issue title it files
# with exactly one marker chosen from the body — "[SQUAD] " when the body
# carries the "Fork-only work" marker line (the same line the squad's
# forkonly.sh reads; the work is the squad's own tooling) and "[MesenAI] "
# otherwise (the work lands in this repository). A title that already starts
# with "[" is left alone, so a caller that names its own prefix never gets two.
#
# Host-free: the real scripts/report-bug.sh runs, but the only `gh` on its
# PATH is a stub in a temp dir that records its argv and answers just enough
# to reach the end. No network, no real `gh`, no GitHub calls.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
REPORT_BUG="$REPO_ROOT/scripts/report-bug.sh"

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

[[ -f "$REPORT_BUG" ]] || fail "scripts/report-bug.sh not found at $REPORT_BUG"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

STUB_DIR="$TMP_DIR/bin"
mkdir -p "$STUB_DIR"
export GH_STUB_LOG="$TMP_DIR/gh-argv.log"
: > "$GH_STUB_LOG"

cat > "$STUB_DIR/gh" <<'STUB'
#!/usr/bin/env bash
# Records its argv (one argument per line, each call opened by "=== sub cmd")
# then answers just enough for scripts/report-bug.sh to run to the end.
{
  printf '=== %s %s\n' "${1:-}" "${2:-}"
  for arg in "$@"; do printf '%s\n' "$arg"; done
} >> "$GH_STUB_LOG"
case "${1:-} ${2:-}" in
  "issue create")      echo "https://github.com/sbihaiko/MesenAI/issues/9999" ;;
  "project item-add")  echo '{"id":"PVTI_stub0000000000000000"}' ;;
esac
exit 0
STUB
chmod +x "$STUB_DIR/gh"

export PATH="$STUB_DIR:$PATH"

# Echo the value the script passed to `--title` on its `gh issue create` call.
filed_title() {
  awk '
    /^=== issue create$/ { seen = 1; want = 0; next }
    /^=== /              { seen = 0; want = 0; next }
    seen && $0 == "--title" { want = 1; next }
    want                 { print; exit }
  ' "$GH_STUB_LOG"
}

# Run the real helper with the stub `gh` first on PATH and return the title
# it filed.
run_case() {
  local title="$1" body="$2" out
  : > "$GH_STUB_LOG"
  if ! out=$(bash "$REPORT_BUG" "$title" "$body" 2>&1); then
    fail "scripts/report-bug.sh exited non-zero for title '$title': $out"
  fi
  local got
  got=$(filed_title)
  [[ -n "$got" ]] || fail "no 'gh issue create --title' call recorded for title '$title' (log: $GH_STUB_LOG)"
  printf '%s' "$got"
}

BODY_PLAIN="Repro: run the widget. Expected: it saves. Observed: it crashes."
BODY_FORK="Fork-only work: this is the squad's own tooling, not MesenAI."

got=$(run_case "Widget crashes on save" "$BODY_PLAIN")
[[ "$got" == "[MesenAI] Widget crashes on save" ]] \
  || fail "plain body: expected '[MesenAI] Widget crashes on save', got '$got'"

got=$(run_case "Squad tooling drops a child" "$BODY_FORK")
[[ "$got" == "[SQUAD] Squad tooling drops a child" ]] \
  || fail "Fork-only body: expected '[SQUAD] Squad tooling drops a child', got '$got'"

got=$(run_case "[MesenAI] Already prefixed" "$BODY_PLAIN")
[[ "$got" == "[MesenAI] Already prefixed" ]] \
  || fail "pre-prefixed title: expected it passed through unchanged, got '$got'"

got=$(run_case "[SQUAD] Prefixed and fork-only" "$BODY_FORK")
[[ "$got" == "[SQUAD] Prefixed and fork-only" ]] \
  || fail "pre-prefixed title on a fork-only body: expected it passed through unchanged, got '$got'"

echo "PASS: report-bug.sh prefixes every issue it files, one prefix chosen from the body"
