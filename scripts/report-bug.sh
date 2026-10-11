#!/usr/bin/env bash
# Files a bug as a GitHub Issue and adds it to the "MesenCE Bug Tracker" board
# (github.com/users/sbihaiko/projects/1), already with Status = "To triage".
#
# Usage: scripts/report-bug.sh "<title>" "<body>" [P0|P1|P2]
#
# Requires `gh` authenticated with the `project` scope (gh auth refresh -s project).
set -euo pipefail

REPO="sbihaiko/MesenAI"
OWNER="sbihaiko"
PROJECT_NUMBER=1
PROJECT_ID="PVT_kwHOB1MsbM4BhjX3"
STATUS_FIELD_ID="PVTSSF_lAHOB1MsbM4BhjX3zhgesr0"
STATUS_TRIAGE_OPTION_ID="f971fb55"
PRIORITY_FIELD_ID="PVTSSF_lAHOB1MsbM4BhjX3zhges5M"

usage() {
  echo "Usage: $0 <title> <body> [P0|P1|P2]" >&2
  exit 1
}

[ $# -ge 2 ] || usage
TITLE="$1"
BODY="$2"
PRIORITY="${3:-}"

case "$PRIORITY" in
  ""|P0|P1|P2) ;;
  *) echo "Invalid priority: $PRIORITY (use P0, P1, or P2)" >&2; exit 1 ;;
esac

TITLE="${TITLE#"${TITLE%%[![:space:]]*}"}"

# Every issue this helper files carries exactly one prefix, chosen from the
# body and never guessed: "[SQUAD] " when the body carries the fork-only marker
# line (the work is the squad's own tooling), "[MesenAI] " otherwise (the work
# lands in this repository).
#
# The marker test is the ONE rule forkonly.sh applies, read from the same
# pattern, character for character (forkonly.sh, `sg_fork_only`):
#
#   ^[[:space:]>*+-]*\**[Ff]ork-only work
#
# A MARKER LINE — the phrase owning a line, bare, indented, bulleted, bolded or
# quoted — is fork-only work. A body that only mentions the phrase inside a
# sentence is a report about MesenAI, and routing one down the fork costs it the
# PR that is its only deliverable. Matching the phrase anywhere in the body is
# the shape #1563 shipped, and it sent prose down the fork; anchoring it at
# column 0 (the shape #1706's review asked for) misses the bulleted, bolded and
# quoted marker forms real bodies write. Citing this one pattern on both sides
# is what keeps the two rules from drifting apart again.
#
# A title that already starts with "[" keeps the prefix its caller named, so it
# never gets two — the whitespace stripped above can never hide the "[".
if [[ "$TITLE" != \[* ]]; then
  if grep -qE '^[[:space:]>*+-]*\**[Ff]ork-only work' <<<"$BODY"; then
    TITLE="[SQUAD] $TITLE"
  else
    TITLE="[MesenAI] $TITLE"
  fi
fi

ISSUE_URL=$(gh issue create --repo "$REPO" --title "$TITLE" --body "$BODY" --label bug)
echo "Issue created: $ISSUE_URL"

ITEM_ID=$(gh project item-add "$PROJECT_NUMBER" --owner "$OWNER" --url "$ISSUE_URL" --format json -q '.id')

gh project item-edit --id "$ITEM_ID" --project-id "$PROJECT_ID" \
  --field-id "$STATUS_FIELD_ID" --single-select-option-id "$STATUS_TRIAGE_OPTION_ID" >/dev/null

if [ -n "$PRIORITY" ]; then
  case "$PRIORITY" in
    P0) OPT_ID="79628723" ;;
    P1) OPT_ID="0a877460" ;;
    P2) OPT_ID="da944a9c" ;;
  esac
  gh project item-edit --id "$ITEM_ID" --project-id "$PROJECT_ID" \
    --field-id "$PRIORITY_FIELD_ID" --single-select-option-id "$OPT_ID" >/dev/null
fi

echo "Added to board: https://github.com/users/$OWNER/projects/$PROJECT_NUMBER"
