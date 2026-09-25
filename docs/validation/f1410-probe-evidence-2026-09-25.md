# F14.10 — a probe's evidence is the tile the run time reads (ADR-0235 §2)

**Date:** 2026-09-25
**Slice:** PRD F14.10 = ADR-0235 option 2 (the recorder reads each probe at the
pixel the run time reads, per row) + the refusal it makes load-bearing.
Issue #499 (Ninja Gaiden HUD frozen by a captured `<background>`).
**Status: option 2 measured, not shipped.** The owner picked option 3
(render-time guard) on 2026-09-25, verbatim *"Opção 3: guarda no render
(Recommended)"*, recorded as ADR-0236 (slice F14.11). This document is the
measurement record of option 2, and it stands alone: the code it describes is
commit `f178b232c` on branch `feat/f1410-probe-evidence` (one commit on
`origin/main` `daf4a72e1`, pushed and **not merged**; the `~/…` paths below
were local worktrees), and nothing below depends on that code being merged or
kept. The 2026-09-25
verification of this record found the numbers below intact (219→87,
68 883→35 265, 2 986→119, Mega Man 2 13→0, the 97/35 attribution) and four
defects, all corrected in place.
**Builds measured** (each `MesenCore.dylib`, sha256[:32], built with the
CommandLineTools make/clang++ and `-isysroot …/MacOSX.sdk`, rc 0):

| arm | tree | dylib sha256[:32] | what it is |
|---|---|---|---|
| `before` | `~/f1410-base` | `7038a1b1b27c4e04e3ae470bd916c0d8` | `origin/main` `daf4a72e1`, no ADR-0235 |
| `shown` | `~/f1410-flipoff` | `392e5a61ef62c5cca50b42013db2ab04` | ADR-0235 §2 shown-evidence read only (no missing-evidence rule) |
| `after` | `~/f1410` | `410aecd28a8778ee1dd89c6b9faf290e` | the slice as implemented (shown-evidence read + missing-evidence rule + refusal) |
| `final` | `~/f1410` | `97e55f507378206bdbad412bf7e54b67` | `after` plus one comment-only edit (a stale script path in a log comment), rebuilt after every measurement; re-running Ninja Gaiden's route on it gives the same row as `after` (14 written, 42 refused, 246 drawn, 0 stale, `screen001` 0) and `1221/1221` unit cases |
| replay | `~/f1410-trace` | `ce2e9d0d4abbaec5d175fcdf3be6677c` | a copy of **this branch's** tree as of the shown-evidence read (RowFineX, `CellOriginX`, the `SatisfiesProbe` that returns `false` for a pixel the grid cannot show — the pre-closing-rule variant) plus the env-gated trace switch `MESEN_TRACE_BG_LAYER`: one renderer replays both arms' packs |

That last row is not `origin/main`, and it cannot move a replay number:

- the recorder never runs at replay. `headless_record` enables the builder only
  on the `bootstrap` flag (`scripts/headless_record.cpp:770`; the default is
  `mep.BootstrapEnhancementFolder = false`, `:655`), and a replay passes
  `capture` (`:714`, which only pulls the final frame into the process' memory),
  `screenshot`, `mep-off`, `log` — no `bootstrap`. Anchor selection, the part of
  this branch that is a decision, is therefore dead code in those runs, and the
  trace switch only reads pixels and prints them.
- the same dylib replayed both arms, so the two columns differ only by the pack.
- measured: one pack (Donkey Kong `before`, 31 s) replayed on the `origin/main`
  build and on the trace build gives a **byte-identical** screenshot
  (`b9d335f5c81a446b637e04f9ea5e93ea`), so the two renderers agree pixel for
  pixel on a pack-driven frame.

`strings` finds the refusal log line (`refused for a surviving rival`) once in
`after`'s dylib and zero times in `before`'s; `after`'s unit-test binary passes
`1221/1221`. The same string is in the trace build's dylib, which is how the
point above was noticed.
Artifacts: `runs/f1410/` (git-ignored): `score.json`, `score.md`,
`{before,after,flipoff}-rec|play/`, `ng-{before,flipoff,after,final,*-hud31}-*`,
`provenance.txt`, `mutations.txt`.

> **Verdict.** ADR-0235 §2 as written **does not close #499**. Measured on the
> route the ADR measured: with the per-row, shown-evidence read in place and the
> refusal rule running, `screen001`'s gate is still written and still fires on
> the 31 s frame — the greedy picks the bar's last tile, whose cell a frame at
> fine 7 cannot record, and reads the grid's silence as a separation. Closing
> #499 needed one more rule (§4: a pixel the grid cannot show is not a
> separation), and that rule is what made option 2 close it in this worktree; on
> its own it makes Ninja Gaiden's route clean (2 257 → 246 drawn frames, 1 487 → 0 stale,
> `screen001` never drawn, the 31 s HUD live).
>
> The price is the refusal rule the option makes load-bearing, and it is much
> larger than the ADR's "Against" section assumed: **219 → 87 written captures
> across the 30-ROM library, 68 883 → 35 265 drawn frames, stale 2 986 → 119.**
> Of the 132 captures lost, 97 are ADR-0235 §2's own refusal rule and 35 are
> the closing rule §4 adds; 28 of those 35 are in nine games whose stream
> carries a frame **with no background runs on most of its scanlines** (a stage
> transition or black screen), where the recorder has no evidence at all and
> refuses by default (§4). **This is a stop
> point, not a merge:** the decision closes the bug and costs about half the
> art, and the closing rule needs a formulation that can tell "the recorder
> cannot place this pixel" from "the game drew nothing here" before either can
> ship. Option 3 (a render-time guard, ADR-0221 option D) keeps the captures
> and would need the same measurement.

## 1. What changed (on the branch, not on `main`)

Four files, all local to anchor selection; no run-time cost. The paths and line
numbers below are the branch's; on `main` these members and helpers do not
exist.

| File | Change |
|---|---|
| `Core/NES/HdPacks/TileSheetTypes.h` | `GridFrame::RowFineX[kGridRows]` — the phase each row's cells were fetched at, filled by `LayOutGridRuns` from the runs' own `X % 8` (majority per row, `kRowFineXDominant` = "no evidence" reads as `FineX`); `RowPhase(row)`; `SamePalettedCells` also compares the plane; `CellOriginX`/`CoveringGridCol`, the inverse of the layout: a cell's absolute pixel, and the column that covers a given pixel |
| `Core/NES/HdPacks/ScreenStitcher.cpp` | `SatisfiesProbe` reads the rival's cell at the probe's **absolute pixel** (per row) instead of the same column; every frame at another fine scroll is a rival; a pick with a rival still standing is reported (`Rejected`) |
| `Core/NES/HdPacks/ScreenStitcher.h` | `AnchorChoice::Rejected` |
| `Core/NES/HdPacks/HdPackBuilder.cpp` / `.h` | candidates derived at the row's own phase; `FinalizeScreenAnchors` withholds the `<background>` line of a rejected screen (the PNG stays on disk, ADR-0156's cells route vanilla, exactly like a gate collision) and logs its own summary line |

`scripts/core_unit_tests.cpp`: four `F14.10` cases (three before this session's
closing rule, one for it).

**Why the read has two halves.** The layout places every row relative to the
frame's **single** dominant `FineX`, so a row fetched at another phase (Ninja
Gaiden's fixed status bar over a playfield at fine 7) has its cells shifted in
the dumped coordinate system: the tile the run time reads at pixel 32 sits at
column 4, and the column the old read used (4, the same index) *looked* right
only because both frames laid the row out the same way. The inverse mapping is
what makes the read phase-proof; `RowFineX` is what makes it possible.

## 2. Unit tests (TDD)

The two REDs are saved: `runs/f1410-red.txt` (the slice's own, run in
`~/f1410-base` with only the two API-compatible cases inserted) and
`runs/f1410-red-trailing-cell.txt` (the closing rule, run against this
worktree before it was added).

| RED | What it asserts | Failure reproduced |
|---|---|---|
| `TestAnchorReadsAProbeAtTheRowItsOwnFetchPhase` | a frame at another fine scroll whose bar row still holds the probe is a rival | `rivals=0` |
| `TestAnchorReadsARowAtAnotherPhaseThanTheFramesOwn` | a bar row fetched at another phase is read at that phase | `rivals=0` |
| `TestAnchorRefusesAGateThatStillMatchesARecordedFrame` | a pick with a rival still standing is refused; a separated pair is still written | (part of the implementation) |
| `TestAnchorKeepsARivalWhosePixelTheGridCannotShow` | a probe whose pixel the grid cannot show in a rival is not evidence that the gate separates | `rivals=0` |

GREEN: `make core-unit-tests` → **1221/1221 cases passed**, rc 0. Python:
`test_mep_conditions.py` 43/43, `test_mep_build.py`,
`test_measure_capture_draw_rate.py`, `test_mep_build_recorded.py`,
`test_sheet_keys_audit.py` all pass; `make doc-checks` rc 0 (file-size
ceilings included — `HdPackBuilder.cpp` 2427 lines against a 2438 ceiling).

**Mutations** (`runs/f1410/mutations.txt`), each reverted after the run:

| Mutation | Result |
|---|---|
| `RowPhase` ignores the plane (`return FineX`) | 3 of the 4 `F14.10` cases fail (`rivals=0` twice, refusal once) |
| missing evidence read as a difference (the pre-fix rule) | the closing case fails, `rivals=0` — this is the saved RED |
| `CellOriginX` drops its coarser-than-the-row adjustment | **no** case fails — see §6 |

## 3. Ninja Gaiden `stage1-run` (issue #499, stop condition 1)

Route and state as `~/fix-499/runs/retain/stage1-run.mss` / `stage1-run.txt`,
60 s recordings, 31 s screenshots. Trace cut to frames 1083–3599 (**2 517
frames**) in every arm; the trace's own first two `F` lines (frames 1–2, before
the state loads) are outside the window and carry no background.

| arm | captures written | `Rivals>0` screens | refused | trace drawn | trace stale (>2 000 px) | `screen001` drawn | frame 2946 |
|---|---|---|---|---|---|---|---|
| `before` | 15 | 46 | — | 2 257 | 1 487 | 137 | `screen001` at a 25 348-px difference |
| `shown` (ADR-0235 §2 alone) | 15 | 41 | 41 | 363 | 66 | 137 | **still `screen001`, 25 348 px** |
| `after` (the slice) | 14 | 42 | 42 | 246 | **0** | **0** | nothing drawn |

The `final` rebuild reproduces the `after` row exactly (14 written, 42 refused,
2 517 / 246 / 0, `screen001` 0, frame 2946 empty).

The 31 s screenshot: `before` freezes the HUD at **SCORE 000000 / TIMER 149**
over art shifted by the scroll; `after` shows the live HUD (**SCORE 000100 /
TIMER 120**) and no background, pixel-identical in content to the no-pack
control (`runs/f1410/ng-{before,after,none}-hud31/…/Screenshots/`).

**What this says.** ADR-0235 §2 as written (per-row evidence) is *not* enough:
with the shown-evidence read, `screen001`'s gate survives because the greedy
picks `(248,0)` — the bar's last tile, whose cell a frame at fine 7 cannot
record (the layout drops it past dumped 248) — and reads the grid's silence as
a separation. The three arms' own `screen001` conditions say it in one line
each:

```
before   screen001_A 40,184,013E   screen001_B 32,24,100B    screen001_C 152,24,100D
shown    screen001_A 40,184,013E   screen001_B 248,0,10FF    screen001_C 32,24,100B
after    (no screen001 conditions: the capture is refused)
```

That is why the closing rule below exists, and on this route it is exactly one
screen: `shown` refuses 41 of 56, `after` refuses 42, and the extra one is
`screen001`. **The mass refusal is the decision, not the closing rule.**

## 4. The closing rule (one screen on the route, 35 captures in the library)

`SatisfiesProbe` returns *satisfied* when the grid carries no cell at the
probe's pixel in the other frame — out of range, or an empty cell. Missing
evidence is not a separation, the same way `PaletteMayMatch` already reads an
unknown palette as "may match" a few lines below: the recorder only ever draws
a conclusion it can see. Before the rule, `screen001`'s gate is kept; after it,
the pick has no separating set left, the screen is refused, and the 31 s frame
falls back to the game's own tiles.

Cost, measured: +1 refusal of 56 on Ninja Gaiden's route, +35 captures across
the library (see §5) — 28 of them in the nine games whose stream contains a
frame with runs on only a few of its scanlines:

| game | entries with a near-empty grid (few of 960 cells) | captures `shown` → `after` |
|---|---|---|
| Mega Man 2 | entry 9: 32 cells | 13 → 0 |
| Ninja Gaiden (power-on) | entries 4, 5, 663: 64, 31, 62 | 3 → 0 |
| Zelda | entries 1, 2, 71: 32, 64, 32 | 4 → 1 |
| Tetris 2 | entries 7, 15: 32, 160 | 2 → 0 |
| Super Mario Bros. 3 | entry 551: 96 | 2 → 0 |
| Contra | entries 225, 1451, 1677: 32 each | 3 → 1 |
| Ice Climber | entries 0, 3, 106: 32 each | 4 → 3 |
| Lifeforce | entries 1, 799: 128 each | 1 → 0 |
| Bubble Bobble | entry 0: 0 cells | 1 → 0 |
| **subtotal, those nine** | | **35 → 7** (28 captures) |

**What the empty cells are** (corrected 2026-09-25; an earlier draft of this
document blamed `ShapeIdFor` skipping all-zero tiles, which it does not): a cell
is written only where a run covers its pixel (`LayOutGridRuns`,
`TileSheetTypes.h:605`), and it keeps `kEmptyCell` = `0xFFFF`
(`TileSheetTypes.h:369`) wherever no run reached. `ShapeIdFor` interns an
all-zero tile like any other (`HdPackBuilder.cpp:1044`) and returns
`kEmptyCell` only when the 65 535-shape id space runs out (`:1051`). So the
frames in the table above are frames whose background the game turns off for
most of their scanlines — a stage transition, a black screen. Mega Man 2's entry
9 is exactly that: 32 cells, every one of them on row 0, out of 960.

The extra rule reads that emptiness as "no evidence" and keeps such a frame as a
rival, so those captures are refused. The run time there reads the PPU's own
screen-tile entry for the pixel — a tile the recorder never saw; whether the gate
would in fact separate on such a frame is **not measured here**, and the model
refuses without evidence. That is the conservative side of the wrong-draw
question, and it is a class apart from the misalignment #499 is about. It is
what turns Mega Man 2's 13 captures into 0. The remaining 7 captures
(Castlevania 6 → 3, Excitebike 2 → 1, Bomberman 11 → 9, Super Mario Bros.
1 → 0) come from a hole this run did not isolate; the coverage tail §3 was
written for is the first candidate.

## 5. The 30-ROM library sweep

60 s of power-on per ROM per arm, no state, same command in both arms;
`runs/f1410/sweep.py`. Traces replayed on one renderer for both arms (the
`replay` row of the provenance table: the branch's tree as of the shown-evidence
read plus the trace switch, with the recorder off — see the three proofs there),
so the two columns differ only by the pack. `stale` = a drawn background whose
pixels differ from the live screen on more than 2 000 of 61 440.

| Game (30) | captures b→a | drawn b→a | stale b→a |
|---|---|---|---|
| Bomberman | 16 → 9 | 2 811 → 2 462 | 62 → 0 |
| Bubble Bobble | 1 → 0 | 3 352 → 0 | 0 → 0 |
| Castlevania | 21 → 3 | 3 066 → 1 704 | 593 → 78 |
| Contra | 3 → 1 | 969 → 72 | 38 → 0 |
| Donkey Kong | 30 → 14 | 3 572 → 1 328 | 0 → 0 |
| Double Dragon | 2 → 1 | 3 565 → 67 | 1 748 → 0 |
| Dr. Mario | 1 → 1 | 1 027 → 1 027 | 0 → 0 |
| Excitebike | 2 → 1 | 1 666 → 1 225 | 56 → 0 |
| F-1 Race | 5 → 5 | 2 756 → 2 794 | 3 → 0 |
| Gauntlet | 2 → 2 | 130 → 123 | 5 → 3 |
| Golf | 7 → 7 | 3 562 → 3 562 | 0 → 0 |
| Ice Climber | 23 → 3 | 3 253 → 318 | 6 → 6 |
| Lemmings | 1 → 1 | 2 455 → 2 455 | 0 → 0 |
| Lifeforce | 1 → 0 | 591 → 0 | 117 → 0 |
| Mario Bros. | 3 → 2 | 3 510 → 1 421 | 28 → 28 |
| Mega Man | 1 → 1 | 3 591 → 3 591 | 0 → 0 |
| Mega Man 2 | 13 → 0 | 2 446 → 0 | 0 → 0 |
| Metroid | 11 → 11 | 3 322 → 3 322 | 0 → 0 |
| Punch-Out!! | 9 → 4 | 2 012 → 1 263 | 3 → 3 |
| Ninja Gaiden (power-on) | 4 → 0 | 947 → 0 | 62 → 0 |
| Pac-Man | 25 → 10 | 3 238 → 1 581 | 0 → 0 |
| Super Mario Bros. | 1 → 0 | 1 577 → 0 | 21 → 0 |
| Super Mario Bros. 3 | 4 → 0 | 506 → 0 | 223 → 0 |
| Tennis | 5 → 4 | 3 579 → 3 052 | 0 → 0 |
| Tetris | 17 → 3 | 3 567 → 1 834 | 0 → 0 |
| Tetris 2 | 4 → 0 | 3 493 → 0 | 0 → 0 |
| The Flintstones | 3 → 3 | 3 467 → 2 064 | 1 → 1 |
| The Legend of Zelda | 4 → 1 | 853 → 0 | 20 → 0 |
| 1942, Zelda II | 0 → 0 | 0 → 0 | 0 → 0 |
| **Total** | **219 → 87** | **68 883 → 35 265** | **2 986 → 119** |

Every game whose numbers move, and why:

- **Refused screens** (`N screen(s) refused for a surviving rival (ADR-0235)`
  in each `mesen-home/mesen.log`): Bomberman 7, Bubble Bobble 1, Castlevania
  18, Contra 2, Donkey Kong 33, Double Dragon 2, Excitebike 1, Ice Climber 22,
  Lifeforce 1, Mario Bros. 1, Mega Man 2 13, Punch-Out!! 6, Ninja Gaiden 4,
  Pac-Man 16, Super Mario Bros. 1, Super Mario Bros. 3 4, Tennis 1, Tetris 14,
  Tetris 2 4, Zelda 3. Each of those was a capture whose three probes could not
  be shown to separate it from some frame the run time would apply it to, after
  the stable, volatile and flat/emptiness passes (the anchored line reports
  how many picks were widened: 55 of 56 on the Ninja Gaiden route).
- **Drawn frames follow the captures**, with one exception that is not one:
  Castlevania and Punch-Out!! keep a third to a half of their draws while
  losing most of their captures, because a kept gate fires on many frames.
- **Stale frames collapse** (2 986 → 119, 4.3 % → 0.3 % of drawn frames). The
  119 that remain are the model's other blind spots, not the fine scroll: rows
  whose runs disagree with the row's majority phase *within* the row (§6), the
  tool's index→CHR-bytes model (the ADR's own consequence), and Castlevania's
  78, whose gate fires where the game changes cells the capture holds.
- **Games that do not move** are the ones the wider rival universe does not
  make ambiguous: static screens with one capture (Dr. Mario, Lemmings, Mega
  Man, Metroid 11/11, Golf 7/7) and F-1 Race, the one game that improves
  (2 756 → 2 794 drawn, 3 → 0 stale).
- **1942 and Zelda II write no captures in either arm** (their 60 s streams
  end before any screen is captured), so they carry no signal here.

Attribution between the two halves of the change — the shown-evidence read
(the ADR's decision) and the closing rule (§4) — measured by a third recording
arm, `shown` (the slice with §4 reverted; recordings only, no replays):

| Game | captures `before` | `shown` (§2 only) | `after` (the slice) | refused `shown` | refused `after` |
|---|---|---|---|---|---|
| Bomberman | 16 | 11 | 9 | 5 | 7 |
| Bubble Bobble | 1 | 1 | 0 | 0 | 1 |
| Castlevania | 21 | 6 | 3 | 15 | 18 |
| Contra | 3 | 3 | 1 | 0 | 2 |
| Donkey Kong | 30 | 14 | 14 | 33 | 33 |
| Double Dragon | 2 | 1 | 1 | 2 | 2 |
| Dr. Mario | 1 | 1 | 1 | 0 | 0 |
| Excitebike | 2 | 2 | 1 | 0 | 1 |
| F-1 Race | 5 | 5 | 5 | 0 | 0 |
| Gauntlet | 2 | 2 | 2 | 0 | 0 |
| Golf | 7 | 7 | 7 | 0 | 0 |
| Ice Climber | 23 | 4 | 3 | 21 | 22 |
| Lemmings | 1 | 1 | 1 | 0 | 0 |
| Lifeforce | 1 | 1 | 0 | 0 | 1 |
| Mario Bros. | 3 | 2 | 2 | 1 | 1 |
| Mega Man | 1 | 1 | 1 | 0 | 0 |
| Mega Man 2 | 13 | 13 | 0 | 0 | 13 |
| Metroid | 11 | 11 | 11 | 0 | 0 |
| Punch-Out!! | 9 | 4 | 4 | 6 | 6 |
| Ninja Gaiden (power-on) | 4 | 3 | 0 | 1 | 4 |
| Pac-Man | 25 | 10 | 10 | 16 | 16 |
| Super Mario Bros. | 1 | 1 | 0 | 0 | 1 |
| Super Mario Bros. 3 | 4 | 2 | 0 | 2 | 4 |
| Tennis | 5 | 4 | 4 | 1 | 1 |
| Tetris | 17 | 3 | 3 | 14 | 14 |
| Tetris 2 | 4 | 2 | 0 | 2 | 4 |
| The Flintstones | 3 | 3 | 3 | 0 | 0 |
| The Legend of Zelda | 4 | 4 | 1 | 0 | 3 |
| 1942, Zelda II | 0 | 0 | 0 | 0 | 0 |
| **Total** | **219** | **122** | **87** | **119** | **154** |

Reading the columns: the ADR's decision as written (per-row evidence, every
other-fine frame a rival, a pick with a rival standing refused) costs 97
captures and 119 refusals on its own; the closing rule costs 35 more. On Ninja
Gaiden's `stage1-run` route the same split is 15 written (before) → 15 (`shown`,
41 refused) → 14 (`after`, 42 refused), i.e. there the closing rule costs
exactly the one screen #499 is about.

Stream cost: none on this route — Ninja Gaiden's stream is 950 entries over
3 607 played frames in both arms, so comparing `RowFineX` inside
`SamePalettedCells` collapses nothing that was not already collapsed.

## 6. What this does not settle

- **Whether to ship it.** The decision closes #499 only with the closing rule,
  and the two together cost 132 of 219 library captures. Option 3 (render-time
  guard, ADR-0221 option D) keeps the captures; a narrower closing rule that
  separates "the recorder cannot place this pixel" (the coverage tail §3 was
  written for) from "no run covered it at all" (§4) would keep Mega Man 2's 13.
  Both are design decisions this slice does not take.
- **The blank-frame hole.** A frame with runs on only some scanlines keeps
  `kEmptyCell` (`0xFFFF`) on the rest (`TileSheetTypes.h:369`, written only from
  a run at `:605`), and the grid cannot tell "no run covered this pixel" from
  "the layout could not place it" — the class §4 refuses by default and the
  largest single source of its cost (28 of the 35 captures).
- **`CellOriginX`'s coarser-than-the-row branch is live but untested**
  (branch `Core/NES/HdPacks/TileSheetTypes.h:509`). A row fetched at a phase above the
  frame's dominant has no placeable pixel for column 0 (`phase − 8 < 0`), which
  is reachable — a row at a coarser phase with a run at `X = 0` — and reads "no
  evidence"; no fixture reaches it, so the mutation that deletes the adjustment
  leaves 1221/1221 green. Any follow-up on this model should have a case for
  it.
- **Rows whose runs disagree inside the row.** The read uses the row's majority
  phase; the layout uses each run's own. A row that changes phase mid-scanline
  is therefore read at the wrong pixel for its minority runs — a residual
  source of both false separations and false survivals, and a candidate
  explanation for the 119 stale frames that remain.
- **The draw-rate tooling still resolves probes through the ROM file** (ADR-0235
  §Consequences), so its numbers and this document's are different questions.
- **Whether a wider probe search would find separators.** The slice already
  runs the stable, volatile and flat/emptiness passes; a search over more than
  three probes, or over cells the capture holds as flat, was not tried.
