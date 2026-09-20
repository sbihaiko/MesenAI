#!/usr/bin/env bash
# Builds a disposable git worktree whose commit history is squashed to one
# generic commit, for a cold-read sweep (F12.2/ADR-0214) to be dispatched
# from.
#
# Why this exists: Claude Code injects a `gitStatus` system-reminder (branch
# name + last 5 commit subjects) into every session and every subagent it
# dispatches, based on the *dispatching session's own primary working
# directory* -- not on any path a prompt names, and not on a path a `cd` in
# the same session switches to afterwards (both were tested empirically on
# 2026-09-20; see docs/validation/f12.2-sonnet-sweep-2026-09-19.md for the
# leak this fixes). If the dispatching session's checkout has a recent commit
# like "feat(ui): Copy as MEP sheet cell puts a cell on the clipboard,
# unplaced", every evaluator it dispatches sees that line before it opens a
# single sandbox file, which answers criterion 1 (findability) for free.
#
# The fix has to happen before the coordinating session even starts, because
# a subagent inherits its parent's *fixed* primary directory -- there is no
# way to redirect it mid-session. So: run this script first, from any
# existing worktree, to produce a squashed-history worktree at the given
# path; then open a **new** top-level Claude Code session rooted at that
# path, and run the sweep (dispatch every evaluator) from inside it.
#
# Usage:
#   scripts/f122_make_blind_worktree.sh <path> [<commit-ish>]
#
# <commit-ish> defaults to HEAD. The new worktree's history is one commit,
# on a detached, unpushed, throwaway branch named "blind-eval-snapshot-<ts>"
# -- never a name that hints at what is being tested -- carrying the tree
# contents of <commit-ish> verbatim and a commit message that names nothing
# about the feature under test. It is never pushed and never merged; delete
# the worktree (and its branch, with `git worktree remove` then
# `git branch -D`) once the sweep is done.
set -euo pipefail

if [ $# -lt 1 ]; then
    echo "usage: $0 <path> [<commit-ish>]" >&2
    exit 2
fi

DEST="$1"
COMMITISH="${2:-HEAD}"
STAMP="$(date +%Y%m%d%H%M%S)"
BRANCH="blind-eval-snapshot-${STAMP}"

if [ -e "$DEST" ]; then
    echo "error: $DEST already exists -- pick an unused path" >&2
    exit 1
fi

# Detached checkout of the exact tree first; the orphan branch is created
# inside it so no other worktree's branch list is disturbed.
git worktree add --detach "$DEST" "$COMMITISH"

git -C "$DEST" checkout --orphan "$BRANCH"
git -C "$DEST" add -A
git -C "$DEST" commit --quiet -m "chore: sandboxed evaluation snapshot

History intentionally squashed and this message intentionally generic --
this worktree is a blind-dispatch base for a cold-read evaluator sweep
(ADR-0214/F12.2 and successors). See
scripts/f122_make_blind_worktree.sh for why."

echo "Blind-dispatch worktree ready at: $DEST"
echo "Branch: $BRANCH (detached history, one commit, unpushed)"
echo
echo "Next: open a NEW top-level Claude Code session with '$DEST' as its"
echo "primary working directory, and dispatch every evaluator from there --"
echo "not from this session. A subagent dispatched from this session would"
echo "still inherit THIS session's real history."
