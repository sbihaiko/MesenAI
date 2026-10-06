# Issues #407–#409 — the library job's route start states (2026-09-23)

Three bugs filed from the F14.3 run (`f14.3-route-sets-2026-09-23.md`) against
`scripts/record_library.sh` / `scripts/library_job.py`. The user's rule for them:
*"use spikes para confirmar se os bugs sao reais"*. So each claim was spiked
first, and only a confirmed claim was fixed.

- Binary: `scripts/headless_record` built on this branch (`make capture-tool`,
  CLT SDK). No Core change.
- ROM: `Contra (1988) (Konami).nes`, No-Intro `979494E7…3FBF`, the dump
  `contra/stage-set.json` pins. Archived states used as the positive control come
  from `runs/f925-20260915/contra/clean-stages/` (unversioned). Each one names
  the same ROM file in its header.
- Every spike run is 60 emulated seconds, as `record_stages.sh` runs a stage:
  `bootstrap hdpack-off input=<route>.txt [state=<x>.mss] save-state=end.mss`.
  RAM was read with `scripts/mss_ram.py` (`$0030` is the stage, 0 = stage 1).
  `$0032` is lives and `$0090` is player state.

## Spikes

### #407 — later-stage routes record the attract demo: **confirmed**

| run | start | `$0030` at start | poses line | `hires.txt` |
|---|---|---|---|---|
| `stage3-boss`, as the job ran it | power-on | – | 1 190 silhouettes / 2 970 retained | `a9c35274` |
| `stage2-base`, as the job ran it | power-on | – | 1 190 silhouettes / 2 970 retained | `a9c35274` |
| `stage3-boss` from the archived state | `stage3-boss.mss` | 2 (stage 3) | 976 / 3 607 | `a55176e2` |
| `stage2-base` from the archived state | `stage2-base.mss` | 1 (stage 2) | 358 / 3 548 | `ece98efe` |

Two different routes run from power-on recorded **byte-identical** tile sets.
Both ended with `$0032` = 98, the demo's lives count. In the F14.3 output all nine
unserved routes show the same thing: 2 973 retained frames each, and one
`hires.txt` shared by every stage script and another by every probe. From the
state they were written for, the same scripts record their stage.

### #408 — `stage1-boss` and `stage1-long` start at the stage-1 entry: **confirmed for `stage1-boss`, not for `stage1-long`**

| run | start | start RAM | poses line |
|---|---|---|---|
| `stage1-boss`, as the job ran it | the job's `mint-stage1` state | `$0064`=0, 3 lives | 18 silhouettes / 3 607 |
| `stage1-boss` from the archived state | `stage1-boss.mss` | `$0064`=12 (the wall) | 155 / 3 607 |
| `stage1-long`, as the job ran it | the job's `mint-stage1` state | stage 1 entry | 91 / 2 198 |
| `stage1-run`, as the job ran it | the same state | stage 1 entry | 91 / 2 198 (identical `hires.txt`) |

- `stage1-boss` is a real mis-start. `navigation.json`'s `rooms[]` enters that room
  from `stage1-boss.mss`, but the longest-prefix rule paired it with
  `mint-stage1`.
- `stage1-long` is **not** a bug. Every measurement that used it (F12.6b, F12.14,
  ADR-0209 Q1, F12.11) ran it from `stage1-run.mss`, which is exactly what
  `mint-stage1` produces. It matches `stage1-run` because the two scripts are
  frame-for-frame identical for their first 3 300 frames, and the run is
  already game over by then (`$0032`=0, `$0090`=0 at the end of both). Nothing
  was changed for it.

### #409 — `seen %` hides mis-started routes: **confirmed**

`artist_chr_kit.py` was re-run over the F14.3 Contra packs, keeping that
job's sprite and background parts (same primary pack):

| packs | `seen %` | `--verify` |
|---|---|---|
| all 16, including the 9 attract-demo routes | 87.6 | 0 |
| the 7 stage-1 routes only | 87.6 | 0 |

`seen` is counted per surface. The demo shows only stage-1 art, so the number
cannot move, and the report had no per-route line where the defect could show up.

## Fixes (in the job; no manifest changed)

`library_job.start_plan` replaces `mint_plan`. For each route it decides where
`<stage>.mss` comes from, in the README's terms:

- **Own state.** A state a `.chain.txt` produces, or a `navigation.json` room's
  entry state. Only an exact `mint-<stage>.txt` yields it, or replaying the chain
  from a state the job produced itself. A prefix mint never does (#408).
- **Probe.** `<stage>-probe` gets a copy of `<stage>`'s state.
- **Mint.** Otherwise, the longest mint that prefixes the route.
- **Power-on.** Only in a set that ships no mint, no chain and no room state.
  This is `metroid/`, whose route boots the game itself.
- **Skipped.** Anything else is not recorded, and the reason is kept (#407).

`record_library.sh` runs the plan's steps (mint, `replay_chain.sh`, copy), then
`library_job.py prune`. `prune` deletes the job's working copy of every route
whose `.mss` does not exist, whether the plan skipped it or its mint or chain failed
at run time. So `record_stages.sh` can no longer run a route from power-on by
accident. The repository tree is never written to.

For #409, `route_evidence` adds one table per route-driven ROM to
`library-report.md`. Each row has:

- how the route started;
- the stage its start state is in, read from `navigation.json`'s RAM byte when
  the set has one;
- its own retained frames and silhouettes;
- the routes whose `hires.txt` is byte-identical to its own.

Every skipped route is listed with its reason, and the `stages` column reads
`6 (+10 skipped)`.

**Chain replay.** No versioned chain can run from a checkout today. All three
descend from `stage3-waterfall.mss`, which the README and ADR-0182 record as the
one state that cannot be re-minted. So the branch was spiked on a synthetic
set instead: `mint-stage1` → `stage1-run`, plus `stage1-run-to-stage1-far.chain.txt`
(`300f R`), run through the real `record_library.sh`. The job minted
`stage1-run.mss` and replayed the chain to `stage1-far.mss` (player X 48 → 98,
fine scroll 0 → 70). Both routes were then recorded: `stage1-far` 2 038 retained
frames, `stage1-run` 2 198. Note for later: the real
`stage4-base-to-stage4-boss` chain is 18 957 frames (315 s emulated). That is
over the 5-minute recording budget if it ever becomes reachable.

## Before / after (Contra, 60 s per run)

| route | before (F14.3): start → retained / silhouettes | after |
|---|---|---|
| stage1-2p | mint → 3 607 / 692 | mint → 3 607 / 692 |
| stage1-2p-probe | mint → 3 607 / 17 | copy of stage1-2p → 3 607 / 17 |
| stage1-long | mint → 2 198 / 138 | mint → 2 198 / 138 (same recording as stage1-run) |
| stage1-probe | mint → 3 605 / 42 | mint → 3 605 / 42 |
| stage1-run | mint → 2 198 / 138 | mint → 2 198 / 138 |
| stage1-water | mint → 3 607 / 13 | mint → 3 607 / 13 |
| stage1-boss | mint-stage1 (**wrong state**) → 3 607 / 18 | **skipped**: room state, no mint or chain |
| stage2-base | power-on (**demo**) → 2 973 / 1 058 | **skipped**: nothing produces `stage2-base.mss` |
| stage3-waterfall, -probe | power-on (**demo**) → 2 973 / 1 059, 1 135 | **skipped**: cannot be re-minted |
| stage3-boss, -probe | power-on (**demo**) → 2 973 / 1 058, 1 135 | **skipped**: chain from `stage3-waterfall.mss` |
| stage4-base, -probe | power-on (**demo**) → 2 973 / 1 058, 1 135 | **skipped**: chain from `stage3-boss.mss` |
| stage4-boss, -probe | power-on (**demo**) → 2 973 / 1 058, 1 135 | **skipped**: chain from `stage4-base.mss` |

Every started route reads `stage1 ($0030=00)` at its start. Contra's total
retained frames fall from 49 186 to **18 822**: the 26 757 demo frames and
`stage1-boss`'s 3 607 wrong-state frames are gone. `seen %` stays at 87.6, as the
#409 spike predicts, and `--verify` stays at 0.

The other five games are unchanged, byte for byte in every number:

| game | routes | retained frames | seen % | `--verify` |
|---|---|---|---|---|
| Excitebike | 2 (mint) | 5 642 | 76.0 | 0 |
| Mega Man 3 | 2 (mint) | 6 895 | 23.5 | 0 |
| Metroid | 1 (power-on, self-booting set) | 2 121 | 78.0 | 0 |
| Zelda II | 2 (mint + probe copy) | 4 872 | 33.4 | 0 |
| Zelda | 2 (mint) | 4 416 | 83.4 | 0 |

The whole six-ROM job took 334 s of wall clock, down from 594 s.

## What is not decided here

Recording Contra's ten skipped routes from a checkout needs a new decision
that no ADR covers. The options are:

1. **Let the job read archived states.** For example, a `--states <dir>` input
   like `record_navigation_sweep.py` has. This means trusting unversioned
   `.mss` files, which have to be checked against the pinned dump (#314).
2. **Mint later-stage starts with the navigation cheat.** `$0030`, ADR-0184's
   2026-09-14 amendment. This reaches stage starts but not the bosses, and the
   route scripts were not written against those warp starts.
3. **Version a chain from power-on to `stage3-waterfall`.** This is a new
   headless search. ADR-0182 §3 allows it only when a measurement names the need.

## Tests

`python3 scripts/test_library_job.py`: **30/30**, run by `make doc-checks`, which
passes. Each fix was mutated in `library_job.py` and the suite failed every time:

| mutation | failing checks |
|---|---|
| an unresolved route falls back to power-on (#407) | 6 |
| `navigation.json` rooms ignored, so a prefix mint serves `stage1-boss` (#408) | 2 (including the real-Contra-set case) |
| a chain is never replayed | 2 |
| `prune` leaves the script in place | 1 |
| the per-route report section is not rendered (#409) | 2 |
| identical recordings are not flagged (#409) | 1 |
