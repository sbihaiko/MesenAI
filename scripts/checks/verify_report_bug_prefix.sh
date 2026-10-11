#!/usr/bin/env bash
# Issue #1563, reshaped by #1773: scripts/report-bug.sh must prefix every issue
# title it files with exactly one marker chosen from the body — "[SQUAD] " when
# the body carries the fork-only marker LINE and "[MesenAI] " otherwise (the
# work lands in this repository).
#
# The marker rule is not invented here: forkonly.sh is the ONE rule that decides
# fork-only work, and both sides must read the SAME pattern, character for
# character. That pattern is not written down in this file — it is read out of
# scripts/report-bug.sh below and printed with the verdict, so the pattern a
# reader sees here is the one that ran (#1807).
#
# A MARKER LINE — the phrase owning a line, plain, indented, bulleted, bolded or
# quoted — is the squad's own tooling and takes [SQUAD]. A body that only
# mentions the phrase inside a sentence is a bug report about MesenAI and takes
# [MesenAI]: matching it anywhere sent work that belonged to MesenAI down the
# fork route, where no PR is ever opened. It was wrong twice, and both times
# because one side of the pair changed alone: #1563 prescribed a plain
# substring test (`grep -Fq 'Fork-only work'`), matching the fork-only rule as
# it then was, and that test survived #1706 while forkonly.sh moved to the
# marker line — the drift this check caught on origin/main, where the helper
# filed [SQUAD] for a body forkonly.sh had already stopped calling fork-only.
#
# This check asserts that parity directly against forkonly.sh's own rule
# whenever that script is on the machine (it lives in the agent-squad fork, not
# in this checkout, so the parity half is skipped where the fork is absent —
# e.g. in CI).
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

# The helper's own marker test, read back from the file that runs it, so the
# documented pattern is bound to the executed one instead of to a copy of it
# kept here. Everything after this line compares against this string.
MARKER_PATTERN=$(sed -n "s/.*grep -qE '\(.*\)' <<<\"\$BODY\".*/\1/p" "$REPORT_BUG")
[[ -n "$MARKER_PATTERN" ]] \
  || fail "could not read the marker pattern out of scripts/report-bug.sh — re-point the extraction in this check"

# CLAUDE.md documents that pattern for the humans who write an issue by hand, so
# it must cite this exact string, once. A second copy, or a stale one, is how
# the documented rule drifts from the executed one without anything failing.
CLAUDE_MD="$REPO_ROOT/CLAUDE.md"
pattern_citations=$(grep -Fc "$MARKER_PATTERN" "$CLAUDE_MD" || true)
[[ "$pattern_citations" -eq 1 ]] \
  || fail "CLAUDE.md must cite the marker pattern '$MARKER_PATTERN' exactly once, found $pattern_citations"

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
# The marker in each shape it is normally written in: the phrase owns its line,
# which is what the shared pattern asks for. Every one of them is fork-only work.
BODY_FORK="Fork-only work: this is the squad's own tooling, not MesenAI."
BODY_FORK_BOLD="**Fork-only work:** this is the squad's own tooling, not MesenAI."
BODY_FORK_BULLET="- Fork-only work: this is the squad's own tooling, not MesenAI."
BODY_FORK_QUOTED="> Fork-only work"
BODY_FORK_NO_COLON="Fork-only work — this is the squad's own tooling, not MesenAI."
# The pattern's `[Ff]`: a marker line the author lowercased is still a marker
# line, and the plain substring test this replaces only ever saw the capital F.
BODY_FORK_LOWER="fork-only work: this is the squad's own tooling, not MesenAI."
# The pattern's leading `[[:space:]>*+-]*`: an indented marker is still a marker.
BODY_FORK_INDENTED="  Fork-only work: this is the squad's own tooling, not MesenAI."
# Ordinary report prose that merely names the phrase mid-sentence — the case
# that separates this rule from the substring test it replaces. Nothing about
# the line says "marker", so this is a bug report about MesenAI and must route
# to a PR in this repository, never down the fork.
BODY_MENTION="The report explains why Fork-only work is out of scope."
# The same, with the phrase trailing everyday report vocabulary on its line.
BODY_MENTION_TAIL="Observed: the child is Fork-only work per the decision below."
# The marker on its own line of a multi-line body, which is how a real body
# carries it, next to prose that must not be mistaken for one.
BODY_FORK_MULTILINE="Repro: the child dies on launch.
Fork-only work: this is the squad's own tooling, not MesenAI.
Observed: exit 1."

# The prose cases come first: telling a report about MesenAI apart from a marker
# is the decision, and a bug routed down the fork never gets a PR. Then the
# marker forms the same pattern must still catch.
body_case "plain body" "Widget crashes on save" "$BODY_PLAIN" "[MesenAI]"
body_case "prose mentioning the phrase" "Widget report mentions fork-only work" "$BODY_MENTION" "[MesenAI]"
body_case "prose trailing the phrase" "Widget report mentions fork-only work" "$BODY_MENTION_TAIL" "[MesenAI]"
# Anchoring is per line, not per body: a multi-line report whose later line
# merely names the phrase is prose, so it takes [MesenAI] — the body-level
# question #1563's substring test answered "yes" to by accident.
body_case "multi-line report with a prose mention" "Widget report mentions fork-only work" \
  "Repro: the widget crashes.
The report explains why Fork-only work is out of scope.
Expected: it saves." "[MesenAI]"
body_case "marker at column 0" "Squad tooling drops a child" "$BODY_FORK" "[SQUAD]"
body_case "bold marker" "Squad tooling drops a child" "$BODY_FORK_BOLD" "[SQUAD]"
body_case "bullet marker" "Squad tooling drops a child" "$BODY_FORK_BULLET" "[SQUAD]"
body_case "quoted marker" "Squad tooling drops a child" "$BODY_FORK_QUOTED" "[SQUAD]"
body_case "marker with no colon" "Squad tooling drops a child" "$BODY_FORK_NO_COLON" "[SQUAD]"
body_case "lowercase marker" "Squad tooling drops a child" "$BODY_FORK_LOWER" "[SQUAD]"
body_case "indented marker" "Squad tooling drops a child" "$BODY_FORK_INDENTED" "[SQUAD]"
body_case "marker on its own line" "Squad child dies on launch" "$BODY_FORK_MULTILINE" "[SQUAD]"

# A caller that names its own prefix keeps it — including when the title only
# reaches the "[" after leading whitespace, which must not earn a second one.
# Both title cases below are the pair the review of #1706 asked for: a title
# with leading whitespace before "[" used to come out double-prefixed, and it
# must be passed through unchanged.
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
# priority) is untouched. This is the second half of what the review of #1706
# asked for — the stub's recorded argv is asserted, not just the title.
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

echo "PASS: report-bug.sh prefixes every issue it files, one prefix chosen from the body"
# The pattern the run enforced, as read from the helper above rather than
# restated: whatever a reader sees here is what report-bug.sh matched with.
echo "      (marker pattern, read from scripts/report-bug.sh: $MARKER_PATTERN)"
if [[ -n "$FORKONLY_SH" ]]; then
  echo "      (marker parity checked against $FORKONLY_SH)"
else
  echo "      (SKIPPED forkonly.sh parity: forkonly.sh not found on this machine)"
fi
