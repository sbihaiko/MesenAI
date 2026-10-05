# ADR-0233: Decide how a capture is gated when its probes were never tested against the frames it draws on (other fine scrolls, surviving rivals)

- Status: superseded (2026-09-25) by ADR-0235 — its premise that the frames
  the gate wrongly fires on were never retained was falsified by the retention
  measurement (frame 2946 is retained entry 597; the recorder misreads the
  fixed status bar's row at the playfield's fine scroll). History: accepted
  (2026-09-25) — option A alone; B and C are re-opened only
  on A's measured numbers. Owner's pick, verbatim: *"A sozinha, mede, depois
  decide B (Recommended)"*; build go-ahead, verbatim: *"pode implementar a
  opção A com o DeepSeek"*. **Option A was implemented and measured, and is
  not shipped** ([validation record](../validation/adr0233-option-a-2026-09-25.md);
  code kept on branch `feat/adr0233-fine-scroll-rivals`). It does not close
  #499 — the 31 s frame is byte-identical — and it regresses F-1 Race (stale
  frames 3 → 205, a same-`FineX` greedy path change, not a remap bug), for a
  library draw rate of 0.6380 → 0.6397. The cause A cannot reach: after A,
  `screen001`'s gate matches **no retained frame** at another fine scroll; the
  frames it wrongly fires on were never retained (950 of 3 607), so every
  option that reasons over retained frames (A, B, C) is blind to them. Owner's
  decision on those numbers, verbatim: *"Não mergear A; medir retenção
  (Recommended)"* — next, measure whether retaining more frames closes #499,
  with option D as the fallback. The env-gated measurement switches are not
  part of any change (see "Reproducing").
- Superseded by: ADR-0235
- Date: 2026-09-25
- Related: issue #499 (Ninja Gaiden HUD frozen by a captured `<background>`), issue #339 (Punch-Out!! card, the same lineage, a different cause), ADR-0050 (bootstrap `<background>` capture), ADR-0156 (a captured screen owns the cells it covers), ADR-0159 (anchors chosen at save time, §1 stability filter and §3 same-`FineX` universe), ADR-0217 and ADR-0218 (gate collisions), ADR-0221 (variant kind test, option B), ADR-0223 (emptiness probes), `Core/NES/HdPacks/ScreenStitcher.cpp` (`SelectScreenAnchors`, `GreedyAnchors`), `Core/NES/HdPacks/HdPackBuilder.cpp` (`FinalizeScreenAnchors`), `Core/NES/HdPacks/HdNesPack.cpp` (`GetLayerIndex`), `scripts/measure_capture_draw_rate.py`
- Supersedes / amends: ADR-0159 §3 (the rival universe no longer stops at the capture's `FineX`). For the record, the options not picked: option A amends ADR-0159 §3. Option B would amend ADR-0159 §1 ("rather than ship an ambiguous set" becomes a refusal). Option C would re-open ADR-0221 option C. Option D would amend ADR-0156.

## Context

### The mechanism (#499)

On the 60 s Ninja Gaiden stage-1 route, at 31 s (emulated frame 2946), `screen001`'s gate fires and paints a frozen frame over the live one: TIMER 149 and SCORE 000000 where the game shows 120 and 000100. Two rules, each working as written, combine to cause it.

1. **The probes identify nothing.** Two of `screen001`'s three `tileAtPosition` probes sit on HUD letters (`SCORE`, `STAGE`) that never change; the third sits on a fence tile that repeats along the whole stage. `HdPackTileAtPositionCondition` compares CHR index and palette, so a uniform band passes at any scroll.
2. **The gate is tested against 5.6 % of the frames it runs on.** ADR-0159 §3 counts a frame as a variant or rival only when it has the capture's `FineX`; `SelectScreenAnchors` skips every other frame, while the run-time condition reads `ScreenTiles` on every frame, at every fine scroll. `screen001` was captured at fine 0; 201 of the 3 607 played frames share it (5.6 %). Frame 2946 is at fine 7.

The lineage is #339: ADR-0217/0218 separated each capture from the other captures, ADR-0221 separated it from frames that *add* content, ADR-0223 let a flat cell be a probe — each measured, each fixing what it targeted, but every one works inside the same-`FineX` universe. None can see #499.

### What was measured (2026-09-25)

Binary: `origin/main` at `7565c4868` plus two env-gated switches, both off when unset. `MESEN_TRACE_BG_LAYER=<file>` makes `HdNesPack::OnBeforeApplyFilter` log each rendered frame's active `<background>`, with the number of the 61 440 native pixels where the drawn image differs from the live background plane. `MESEN_HDPACK_REFUSE_RIVAL_GATES=1` is option B: in `FinalizeScreenAnchors`, a screen whose `choice.Rivals > 0` gets no `<background>`.

A frame is **stale** when the drawn capture differs from the live plane on more than 2 000 px; the distribution is bimodal — a matching frame reads 55 to 1 362 px (upscale smoothing), every other drawn frame reads 2 800 px or more, and none of the 7 521 drawn frames in the four traces falls between. Routes: Ninja Gaiden `scripts/stages/ninjagaiden` `stage1-run` and Castlevania `scripts/stages/castlevania` `stage1-run` (freshly minted), both 60 s; Punch-Out!! power-on 60 s with no input (the ADR-0223 card route); and the Glass Joe fight recording of the 2026-09-25 re-measure as step-1 control (`fight1` state, 70 s).

**Stale frames on the recordings as they stand.**

| recording | frames | a capture drawn | stale |
|---|---|---|---|
| Ninja Gaiden `stage1-run` (the #499 pack, 15 captures) | 3 608 | 3 218 | **2 317** |
| Punch-Out!! fight route (3 captures) | 4 208 | 247 | 48 |
| Punch-Out!! card route (9 captures, re-recorded) | 3 606 | 2 012 | 3 |

On Ninja Gaiden, **2 316 of the 2 318 stale frames** (re-recorded pack, identical gates) are at a fine scroll other than the drawing capture's own; two could not be aligned to a grid frame. On Castlevania it is 312 of 448, the other 136 same-`FineX` frames the capture owns as variants (ADR-0159 §1). Punch-Out!!'s 3 are same-`FineX`; ADR-0223 closed the card, and this confirms it.

**Option B's cost.** The recorder's summary line reads `N screen(s) anchored (… K still matching another recorded screen …)`, and `K` is exactly the number of screens with `Picked` non-empty and `choice.Rivals > 0` (`FinalizeScreenAnchors`, counted before the ADR-0217 collision skip) — "Another recorded screen" undersells it, because `Rivals` counts retained frames at the capture's `FineX` that still pass the gate, not only other captures. On the 30-ROM bounded library as recorded for F12.16 (28 ROMs with captures), `K` sums to **104 of 241 pending screens, in 12 ROMs**; of the 219 captures written, B keeps **at least 118** (a lower bound). Worst cases: Donkey Kong (30 written, 33 flagged), Ice Climber (23 and 21), Pac-Man (25 and 16), Tetris (17 and 14), Double Dragon (2 and 2). Re-recorded with and without the switch:

| game | captures | tool mean draw rate | frames drawn (render) | stale frames | written but never reaching the screen | 31 s HUD |
|---|---|---|---|---|---|---|
| Ninja Gaiden, today | 15 | 0.0963 | 3 219 / 3 609 | 2 318 | 3 | TIMER 149 / SCORE 000000 |
| Ninja Gaiden, B | 10 | 0.0096 | 747 / 3 609 | 433 | 0 | **unchanged**, same frame checksum `0x78B3AA36` |
| Punch-Out!! card, today | 9 | 0.0865 | 2 012 / 3 606 | 3 | 0 | — |
| Punch-Out!! card, B | 4 | 0.0942 | 1 263 / 3 606 | 3 | 0 | — |
| Castlevania, today | 39 | 0.0142 | 2 043 / 3 607 | 448 | 4 | — |
| Castlevania, B | 36 | 0.0118 | 1 897 / 3 610 | 446 | 0 | — |

"Tool mean draw rate" is ADR-0223's stop-condition metric (`scripts/measure_capture_draw_rate.py --rom`); its own never-firing count is 0 in every arm, and it undercounts because it looks a probe up at its exact pixel, so it can fire only on frames at the capture's own fine scroll — the recorder's blind spot. An offline replay that reads the covering cell instead, as the run time does, agrees with the render trace on 3 497 of 3 607 Ninja Gaiden frames; it finds the shipped gates firing on 4 853 (gate, frame) pairs at another fine scroll against 5 211 at their own (Castlevania: 430 and 1 967; Punch-Out!! card: 0 and 2 608). "Written but never reaching the screen" comes from the trace: a gate that fires only where an earlier gate already won `GetLayerIndex`.

**B does not close #499.** `screen001` has `Rivals == 0`: its probes separate it from every retained frame at fine 0, so B keeps it and the 31 s frame is byte-identical. B removes 1 885 of Ninja Gaiden's stale frames by removing five captures that were never the #499 capture, dropping frames drawn 3 219 → 747; Castlevania's stale count does not move (448 → 446) and Punch-Out!! loses 5 of 9 captures for no change (3 → 3).

**Option A's cost, estimated and not built.** Per written capture, the recorder compares against the retained frames at its own `FineX` today, and against all of them under A: Ninja Gaiden 1 832 → 14 250 frame comparisons (**7.8×**, 950 retained frames spread over all eight fine scrolls), Castlevania 9 451 → 75 075 (**7.9×**), Punch-Out!! card 1 887 → 5 328 (**2.8×**, only fine 0 and 1 occur). `FinalizeScreenAnchors` logs its 56 Ninja Gaiden screens within about 0.12 s, about 2 ms per screen, so A projects to about 1 s per save. The bound is ADR-0159's own: 300 screens × 4 096 retained frames, at most 8× today's comparisons.

## Decision

**Picked 2026-09-25: option A alone.**

1. **A.** Every retained frame at another fine scroll is a rival of every capture, evaluated at the cell covering the probe's absolute pixel (see A below). B is not combined now; it is re-opened only if A's measurement leaves `screen001` or other gates ambiguous. C and D stay unpicked.
2. **Variants stay same-`FineX`.** "A capture owns its variants" (ADR-0159 §1, ADR-0217 answer (1)) is unchanged; only rivals widen.
3. **Stop condition.** The slice ships unit tests covering the remap and the widened universe and publishes, before and after: Ninja Gaiden's 31 s frame showing TIMER 120 / SCORE 000100; the stale-frame count (render trace, > 2 000 px) for Ninja Gaiden, Castlevania, the Punch-Out!! card route and the 30-ROM library; and the draw rate from the render trace, not only `measure_capture_draw_rate.py`. If A drops the library's render-trace draw rate by more than 25 %, the slice stops and returns to the owner. Vertical fine scroll stays out of scope (`GridFrame` has no `FineY`).

### A. The rival universe stops at `FineX` (amends ADR-0159 §3)

Every retained frame at another fine scroll becomes a rival of every capture; none can be a variant, because at another fine scroll the capture's image is misaligned with the live plane, and a grid cell `Cells[r][c]` is not the pixel `HdPackTileAtPositionCondition` reads (`ScreenTiles` at the probe's absolute offset). The probe on such a rival is evaluated as the run time does: the cell covering the probe's absolute pixel, `x = col × 8 + capture.FineX`, which is column `(x − rival.FineX) div 8` of the rival's grid.

- **What changes:** `SelectScreenAnchors` (the `FineX` skip becomes "rival, skip `IsScreenVariant`/`AddsContent`") and `GreedyAnchors` with `PaletteMayMatch` (a per-rival column remap at the probe cell); ADR-0217's forced rivals at another `FineX` take the same remap. `IsScreenVariant`, `AddsContent` and the stability filter are untouched, because variants stay same-`FineX`. ADR-0223's last pass keeps its code but runs `GreedyAnchors` over the widened rival set. The evidence model is the existing grid with one lookup that knows the fine scroll, not a second model.
- **For:** it targets the measured hole — 100 % of Ninja Gaiden's stale frames and 70 % of Castlevania's are at another fine scroll, and 4 853 gate firings on Ninja Gaiden are rivals A adds that today's gates do not separate. It keeps captures rather than refusing them, and costs up to 8× in save time, about 1 s here.
- **Against:** unmeasured on the pack — whether the greedy search *can* separate these rivals is the unknown; a uniform band plus HUD glyphs may leave `Rivals > 0`, and a capture then ships ambiguous (or is refused, if combined with B). Vertical fine scroll is not modelled (`GridFrame` has no `FineY`), so a vertically scrolling game keeps the same hole on its other axis. It needs the library re-record and ADR-0223's published numbers before and after.

### B. A gate with surviving rivals is not written

Refuse the `<background>` when `choice.Rivals > 0`, exactly as a collision is refused today, and log it. **For:** one predicate on a number already computed; it turns a known ambiguous gate into a visible miss, which is ADR-0159 §1's own preference. **Against, measured:** it does not fix #499; library-wide it keeps at least 118 of 219 captures, and on the re-recorded trio 15 → 10, 9 → 4 and 39 → 36. Of the stale frames it removes nothing on Castlevania or Punch-Out!!, and it removes 1 885 on Ninja Gaiden only by removing the captures that drew them. Useful only as a complement to A.

### C. One probe must sit on a cell that distinguishes (ADR-0221 option C, re-opened)

Require at least one probe on a cell that differs between the capture and some frame the gate would otherwise fire on. **For:** it would reach Castlevania's 136 same-`FineX` stale frames, which neither A nor B touches — variants the capture owns under ADR-0159 §1 and ADR-0217 answer (1). **Against:** for the other-fine-scroll frames it needs A's remap to know which frames the gate fires on, so for the #499 hole it is A plus a predicate, not a cheaper alternative; C alone still reaches only the 136 same-`FineX` frames. It revokes "a capture owns its variants", and its draw-rate cost is unmeasured, which is why ADR-0221 did not pick it.

### D. Render time (ADR-0221 option D)

A capture never overwrites a live cell whose content it does not carry. **For:** the only option that fixes packs already shipped, community packs included, without a re-record; the trace shows the comparison is cheap enough to measure per frame. **Against:** it amends ADR-0156, puts a per-cell compare in the render hot path, and changes what an artist's painted capture draws — ADR-0221 weighed it and rejected it for those reasons.

## Consequences

- **The draw-rate numbers ADR-0223 published are same-`FineX` numbers.** Whichever option is picked, the acceptance for this class needs a render trace, or a replay that reads the covering cell. The existing tool undercounts by construction, for example Ninja Gaiden 0.0963 against 0.1860 per gate.
- **Doing nothing leaves #499 player-visible.** ADR-0146 auto-installs every accepted pack, and the recorder writes this class of gate on any game that scrolls horizontally.
- **Reproducing.** The two switches are about 40 lines in `HdNesPack.cpp` and `HdPackBuilder.cpp`, described exactly above; they were kept out of the tree, because no accepted decision needs them yet. The slice that implements an option should add the trace (or an equivalent) as its acceptance tool. The session's scripts, commands and raw outputs are in the git-ignored `runs/499-measure/` of the working copy that produced them.
