# A land closed an issue whose source changes were never committed on its branch

Status: pending deployment — the fix is ready in the fork (`agent-squad` plugin
0.2.30, branch `fix/1821-land-tip-not-final-state` at `f3579f0`, pull request
https://github.com/sbihaiko/agent-squad/pull/2 against that fork's `local/next`),
and is not merged into the live `local/next`: #1821 counts as resolved for this
record, not as deployed in the running loop. Nothing in this repository changes:
the land operation is fork-only tooling and no file here carries it. GitHub
issue #1821. Measured 2026-10-11, against the fork's
`contrib/squad-goal/scripts/forkclose.sh`.

## What was measured

#1809's source half was never on `local/next` although the issue was closed and
its card reached Done. Its branch `gui/1809` was locked in a mid-work commit: a
partial strip (`hidline=3`, no `hid.size?`, no chip builder) while the run's
final code — `hidchip`, `fmtN`, the `data-col` color rules — sat uncommitted in
`~/wt-as-1809`'s index. The land merged the branch commit, not the index, and the
merge commit hid it: `d01cd53` is a single-parent commit titled
`Merge gui/1809 (Refs #1809)` carrying only the three new test files, while
`local/next`'s copy had 0 occurrences of `hidline`.

The failing test written for this issue, run against the base `forkclose.sh`
(`bash contrib/squad-goal/scripts/test-forkclose-dirtybranch.sh
/tmp/orig1821/forkclose.sh`), exit 1:

```
ok   the branch tip is the mid-work commit; the final state is only in the worktree's index
ok   the child worktree holds staged, uncommitted work
FAIL the pass did not name the branch as unlanded: 'FORKCLOSE #1 closed (gui/1 9c75b7790 -> 0cc731bc9, test-1-20261011-191514 green)'
FAIL silent about where the work is: 'FORKCLOSE #1 closed (gui/1 9c75b7790 -> 0cc731bc9, test-1-20261011-191514 green)'
FAIL the mid-work tip was merged into the base (that is the bug)
FAIL the issue was closed with its code absent from local/next
ok   the uncommitted work is still there, untouched
ok   once the tip is the final state the branch lands
ok   the base got the run's final code
ok   the issue closes once its code is in the base
```

The pass reports `FORKCLOSE #1 closed (…)` — a success, and a closed issue —
while the run's final state sits staged in the child's worktree and the base
holds only the mid-work commit. That is the whole bug: the pass merges the branch
HEAD, so the branch HEAD has to be the run's final state, and nothing asked
whether it was.

## Why it happened

- `forkclose.sh` merges `$br` — the branch HEAD — and a child commits on the
  branch when it is done. A child that stops before committing leaves a tip that
  is a mid-work commit; the merge succeeds, git reports success and the close
  follows.
- The one check that looked at uncommitted work (`needbase`) reads the **shared**
  fork checkout, not the branch's own worktree, so a child working in its own
  `~/wt-as-<n>` tree — the documented way for a fork-only child to work — is
  invisible to it.
- Nothing downstream can catch it either: the fork suite runs the tree the pass
  just produced, and the work that never reached the tree is not part of it.

## The correction (fork, not this repository)

- `contrib/squad-goal/scripts/forkclose.sh` gains `dirtywt()`, next to the other
  branch-state helpers: it finds the worktree holding `$br` and reports it when
  its tracked files are modified or staged.
- Before merging, the pass asks it. When it answers, the pass prints
  `FORKCLOSE UNLANDED #<n>`, naming the branch and the worktree that holds the
  work, and lands nothing: no merge, no test pass, no close. The refusal is once
  per issue (an `auto/forkunlanded-<n>` marker) and it never commits, checks out
  or resets that work — the child committing on the branch releases it on the
  next pass, like the existing checkout wait (`FORKCLOSE CHECKOUT`).
- Untracked files are deliberately not read as work in progress: a gate tree is
  full of them and one would park a finished branch forever, the same reason the
  checkout wait uses `--untracked-files=no`. The bound that leaves: an untracked
  source file a child never `git add`ed is still invisible to the pass.

## Evidence

`contrib/squad-goal/scripts/test-forkclose-dirtybranch.sh` in the fork, written
before the fix: a branch whose tip is the mid-work commit with the final state
staged in its own worktree is refused by name, nothing is merged, the issue does
not close and the staged work is left untouched; once the child commits, the same
pass lands it and the base gets the final code. At the pushed head `f3579f0` all
ten assertions pass, and so do the other ten `test-forkclose-*.sh` suites
(`test-forkclose-batch.sh` and `test-forkclose-suites.sh` are red on the
unmodified `local/next` as well: this machine has the GUI suite switched off,
`~/.squad-gui-tests-off`, and both assert on GUI runs).

The fix is not merged into `local/next` here: the live loop executes
`forkclose.sh` from that tree while runs are in flight, so landing it is the
operator's call. Until it lands, the pass keeps closing issues whose branch tip
is not the run's final state.
