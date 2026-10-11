#!/usr/bin/env bash
# Issue #1563: scripts/report-bug.sh must prefix every issue title it files
# with exactly one marker chosen from the body — "[SQUAD] " when the body
# carries the "Fork-only work" marker and "[MesenAI] " otherwise (the work
# lands in this repository).
#
# The marker rule is not invented here: forkonly.sh is the ONE rule that
# decides fork-only work, and its body test is a plain substring test,
# `contains("Fork-only work")` — not a line-anchored "Fork-only work:" form.
# So a body carrying the marker in any shape it is normally written (`Fork-only
# work:` at the start of a line, `**Fork-only work:**`, `- Fork-only work:`,
# `> Fork-only work`, `Fork-only work —`) is the squad's own tooling and takes
# [SQUAD], and so is a body that merely mentions the phrase in prose, because
# forkonly.sh cannot tell the two apart. This check asserts that parity
# directly against forkonly.sh's own rule whenever that script is on the
# machine (it lives in the agent-squad fork, not in this checkout, so the
# parity half is skipped where the fork is absent — e.g. in CI).
#
# A title that already starts with "[" is left alone, so a caller that names
# its own prefix never gets two. Leading whitespace never changes what a title
# says, so it is dropped before that test: "  [x]" is a prefixed title, not an
# unprefixed one. A title that is empty or whitespace-only keeps taking the
# default prefix — validating the title is the caller's business, and the rule
# is unconditional.
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

# Print the value that follows <flag> inside the recorded `=== <sub> <cmd>`
# block. The flag must follow its own command, not a later call.
value_after_flag() {
  local sub="$1" cmd="$2" flag="$3"
  awk -v open="=== $sub $cmd" -v flag="$flag" '
    $0 == open { seen = 1; want = 0; next }
    /^=== /    { seen = 0; want = 0; next }
    seen && $0 == flag { want = 1; next }
    want               { print; exit }
  ' "$GH_STUB_LOG"
}

# Every argument line recorded inside the `=== <sub> <cmd>` block.
block_args() {
  local sub="$1" cmd="$2"
  awk -v open="=== $sub $cmd" '
    $0 == open { seen = 1; next }
    /^=== /    { seen = 0; next }
    seen       { print }
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
  got=$(value_after_flag issue create --title)
  [[ -n "$got" ]] || fail "no 'gh issue create --title' call recorded for title '$title' (log: $GH_STUB_LOG)"
  printf '%s' "$got"
}

# --- forkonly.sh's own rule, when this machine has the fork ----------------
# SG_FORKONLY_SH overrides the search. The candidate list covers the two places
# the squad's scripts are installed.
FORKONLY_SH=""
for candidate in "${SG_FORKONLY_SH:-}" \
                 "$HOME/.claude/skills/squad-goal/scripts/forkonly.sh" \
                 "$HOME/VSCodeProjects/agent-squad/contrib/squad-goal/scripts/forkonly.sh"; do
  if [[ -n "$candidate" && -f "$candidate" ]]; then
    FORKONLY_SH="$candidate"
    break
  fi
done

FORKONLY_FILTER=""
if [[ -n "$FORKONLY_SH" ]]; then
  # The rule is the single-quoted argument of the one `jq -e` call in the file.
  FORKONLY_FILTER=$(sed -n "s/.*jq -e '\(.*\)' >\/dev\/null.*/\1/p" "$FORKONLY_SH")
  [[ -n "$FORKONLY_FILTER" ]] \
    || fail "could not read the fork-only rule out of $FORKONLY_SH — re-point the extraction in this check"
  command -v jq >/dev/null 2>&1 \
    || fail "forkonly.sh's rule needs jq, which is not on PATH"
fi

# Ask forkonly.sh's rule what prefix a body earns. Only the body half of the
# rule runs: report-bug.sh files the issue, so no `fork-only` label exists yet.
forkonly_prefix() {
  local body="$1"
  if jq -e "$FORKONLY_FILTER" <<<"$(jq -n --arg b "$body" '{body:$b}')" >/dev/null 2>&1; then
    printf '[SQUAD]'
  else
    printf '[MesenAI]'
  fi
}

# One body case: the helper must file "<prefix> <title>", and forkonly.sh's own
# rule must agree with that prefix.
body_case() {
  local label="$1" title="$2" body="$3" want="$4" got filed_prefix
  got=$(run_case "$title" "$body")
  [[ "$got" == "$want $title" ]] \
    || fail "$label: expected '$want $title', got '$got'"
  if [[ -n "$FORKONLY_FILTER" ]]; then
    filed_prefix="${got%% *}"
    [[ "$filed_prefix" == "$(forkonly_prefix "$body")" ]] \
      || fail "$label: report-bug.sh filed $filed_prefix but forkonly.sh's own rule disagrees for body '$body'"
  fi
}

BODY_PLAIN="Repro: run the widget. Expected: it saves. Observed: it crashes."
# The marker in each shape it is normally written in. forkonly.sh matches the
# substring, so every one of them is the squad's own tooling.
BODY_FORK="Fork-only work: this is the squad's own tooling, not MesenAI."
BODY_FORK_BOLD="**Fork-only work:** this is the squad's own tooling, not MesenAI."
BODY_FORK_BULLET="- Fork-only work: this is the squad's own tooling, not MesenAI."
BODY_FORK_QUOTED="> Fork-only work"
BODY_FORK_NO_COLON="Fork-only work — this is the squad's own tooling, not MesenAI."
# Ordinary report prose that happens to mention the phrase. forkonly.sh matches
# the substring, so it cannot tell this apart from the marker — and neither may
# this helper, or the two rules diverge.
BODY_MENTION="The report explains why Fork-only work is out of scope."
# The marker on its own line of a multi-line body, which is how a real body
# carries it.
BODY_FORK_MULTILINE="Repro: the child dies on launch.
Fork-only work: this is the squad's own tooling, not MesenAI.
Observed: exit 1."

body_case "plain body" "Widget crashes on save" "$BODY_PLAIN" "[MesenAI]"
body_case "marker at column 0" "Squad tooling drops a child" "$BODY_FORK" "[SQUAD]"
body_case "bold marker" "Squad tooling drops a child" "$BODY_FORK_BOLD" "[SQUAD]"
body_case "bullet marker" "Squad tooling drops a child" "$BODY_FORK_BULLET" "[SQUAD]"
body_case "quoted marker" "Squad tooling drops a child" "$BODY_FORK_QUOTED" "[SQUAD]"
body_case "marker with no colon" "Squad tooling drops a child" "$BODY_FORK_NO_COLON" "[SQUAD]"
body_case "prose mentioning the phrase" "Widget report mentions fork-only work" "$BODY_MENTION" "[SQUAD]"
body_case "marker on its own line" "Squad child dies on launch" "$BODY_FORK_MULTILINE" "[SQUAD]"

# A caller that names its own prefix keeps it — including when the title only
# reaches the "[" after leading whitespace, which must not earn a second one.
got=$(run_case "[MesenAI] Already prefixed" "$BODY_PLAIN")
[[ "$got" == "[MesenAI] Already prefixed" ]] \
  || fail "pre-prefixed title: expected it passed through unchanged, got '$got'"

got=$(run_case "[SQUAD] Prefixed and fork-only" "$BODY_FORK")
[[ "$got" == "[SQUAD] Prefixed and fork-only" ]] \
  || fail "pre-prefixed title on a fork-only body: expected it passed through unchanged, got '$got'"

got=$(run_case "  [x]" "$BODY_PLAIN")
[[ "$got" == "[x]" ]] \
  || fail "leading-space title: expected the leading whitespace dropped and no second prefix, got '$got'"

got=$(run_case "  Widget crashes on save" "$BODY_PLAIN")
[[ "$got" == "[MesenAI] Widget crashes on save" ]] \
  || fail "leading-space unprefixed title: expected '[MesenAI] Widget crashes on save', got '$got'"

got=$(run_case "   " "$BODY_PLAIN")
[[ "$got" == "[MesenAI] " ]] \
  || fail "whitespace-only title: expected the default prefix and no crash, got '$got'"

# The prefix rule must not disturb the rest of the call: the issue still
# carries the `bug` label, and the board wiring (item-add, then status, then
# priority) is untouched.
: > "$GH_STUB_LOG"
bash "$REPORT_BUG" "Board wiring still intact" "$BODY_PLAIN" P1 >/dev/null 2>&1 \
  || fail "scripts/report-bug.sh exited non-zero with a priority"

[[ "$(value_after_flag issue create --label)" == "bug" ]] \
  || fail "the 'gh issue create' call no longer passes '--label bug'"

item_add_args="$(block_args project item-add)"
grep -qx '1' <<<"$item_add_args" || fail "'gh project item-add' no longer passes the project number '1'"
grep -qx -- '--owner' <<<"$item_add_args" || fail "'gh project item-add' no longer passes '--owner'"
grep -qx 'sbihaiko' <<<"$item_add_args" || fail "'gh project item-add' no longer passes the owner 'sbihaiko'"
grep -qx 'https://github.com/sbihaiko/MesenAI/issues/9999' <<<"$item_add_args" \
  || fail "'gh project item-add' no longer passes the created issue URL"

item_edit_count=$(grep -c '^=== project item-edit$' "$GH_STUB_LOG")
[[ "$item_edit_count" -eq 2 ]] \
  || fail "expected 2 'gh project item-edit' calls (status, then priority), got $item_edit_count"
grep -qx 'f971fb55' < <(block_args project item-edit) \
  || fail "the status edit no longer sets the \"To triage\" option"
grep -qx '0a877460' < <(block_args project item-edit) \
  || fail "the P1 edit no longer sets the P1 option"

if [[ -n "$FORKONLY_SH" ]]; then
  echo "PASS: report-bug.sh prefixes every issue it files, one prefix chosen from the body"
  echo "      (marker parity checked against $FORKONLY_SH)"
else
  echo "PASS: report-bug.sh prefixes every issue it files, one prefix chosen from the body"
  echo "      (SKIPPED forkonly.sh parity: forkonly.sh not found on this machine)"
fi
