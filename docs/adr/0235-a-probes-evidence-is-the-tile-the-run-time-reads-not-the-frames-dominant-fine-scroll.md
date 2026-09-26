# ADR-0235: A probe's evidence is the tile the run time reads, not the frame's dominant fine scroll

- Status: superseded (2026-09-25) — option 2 was picked first
  (*"Opção 2: corrigir o gravador (Recommended)"*; build go-ahead *"pode
  implementar a F14.10 com o DeepSeek"*), implemented on branch
  `feat/f1410-probe-evidence` and measured
  (`docs/validation/f1410-probe-evidence-2026-09-25.md`): per-row reading
  alone refuses 97 of 219 library captures and does **not** close #499; it
  closed only with an extra "missing evidence is not separation" rule, at
  219 → 87 captures and 68 883 → 35 265 drawn frames. That branch is **not
  merged** and slice F14.10 is dropped; the owner then picked option 3.
  Earlier, verbatim: *"Não mergear A; medir retenção (Recommended)"* —
  retaining more frames (option 1) was measured and does not fix #499; the
  option-A branch (`feat/adr0233-fine-scroll-rivals`) is **not to be merged**.
- Superseded by: ADR-0236 (option 3, the render-time guard). This ADR's
  Context and measurements stay the evidence it rests on.
- Supersedes: ADR-0233 (its option A is dropped, and its premise — that the
  frames the gate wrongly fires on were never retained — is falsified here).
- Date: 2026-09-25
- Related: issue #499 (Ninja Gaiden HUD frozen by a captured
  `<background>`), issue #339 (the same lineage, a different cause),
  ADR-0050 (bootstrap `<background>` capture), ADR-0153 §5 (the stream cap),
  ADR-0156 (a captured screen owns the cells it covers), ADR-0159 §1/§3
  (save-time anchors, stability filter, same-`FineX` rival universe),
  ADR-0217/ADR-0218 (gate collisions), ADR-0221 (variant kind test; option D),
  ADR-0223 (emptiness probes), ADR-0233 (a capture's gate vs the fine scrolls
  its probes were never tested at), `Core/NES/HdPacks/HdPackBuilder.cpp`
  (`OnFrameEnd`, `RecordGridFrame`, `FinalizeScreenAnchors`),
  `Core/NES/HdPacks/ScreenStitcher.cpp` (`SelectScreenAnchors`,
  `GreedyAnchors`), `Core/NES/HdPacks/TileSheetTypes.h` (`LayOutGridRuns`,
  `SamePalettedCells`, `HdTileKey`), `Core/NES/HdPacks/HdNesPack.cpp`
  (`GetLayerIndex`), `Core/NES/HdPacks/HdPackConditions.h`
  (`HdPackTileAtPositionCondition`)
- Amends: nothing in force — option 2's amendment of ADR-0159 §3 (per-row
  probe column) did not ship.

## Context

### The retention rule (what enters the grid-frame stream)

`HdPackBuilder::OnFrameEnd` calls `RecordGridFrame` on **every** emulated frame
while screen capture is on (`EnableScreenCapture`). `RecordGridFrame` drops a
frame in exactly two cases, then de-duplicates:

1. `_frameRuns` is empty — nothing was drawn on the background plane, so there
   is no cell to lay out; or the stream is at its cap,
   `MesenSheets::kMaxSheetFrames` = 4096 (ADR-0153 §5).
2. The stream's last entry has the same `FineX` **and**
   `SamePalettedCells(frame)` — the same 960 shape cells *and* the same 960
   palette ids (ADR-0159's 2026-09-05 amendment). The frame is not pushed;
   `_gridFrames.back().RepeatCount++`.

So the stream is a list of *runs of consecutive identical frames* collapsed to
one entry, and a dropped frame is by construction a byte-identical twin —
same cells, same palettes, same `FineX` — of the entry before it. For a
CHR-ROM game a shape id *is* the CHR index (`HdTileKey::operator==` compares
`TileIndex` and `PaletteColors`; `GetKey(true)` wildcards the palette), which
is the same pair `HdPackTileAtPositionCondition::InternalCheckCondition`
compares at run time.

Measured on Ninja Gaiden `stage1-run`, 60 s: **950 entries covering 3 607
played frames**.

### What the measurement was for

ADR-0233 accepted option A (every retained frame at another fine scroll is a
rival, each probe evaluated at the cell covering its absolute pixel), measured
it, and it did not close #499. Its branch text draws the conclusion that the
frames `screen001` wrongly fires on "were never retained (950 of 3 607), so
every option that reasons over retained frames (A, B, C) is blind to them",
and the owner then asked for retention to be measured before anything else.

**That is not what the stream says.** Frame 2946 — the 31 s frame of issue
#499 — is stream entry **597** of 950; it was retained all along, and in that
stretch every played frame is its own entry (played indexes 1863-1868 map to
entries 597-602). Option A therefore did test `screen001`'s probes against it,
each at the cell covering its pixel, and still reported `rivals=0` with the
same three probes (`picked=3:23,5;3,4;3,19;`) under both rules and both
retention settings.

Retention cannot be the hole for a structural reason: the rival *content* is
already in the stream, because every dropped frame is a twin of the entry
before it. What more retention changes is the rival **multiset**.

### Measured: four arms on Ninja Gaiden `stage1-run` (60 s)

Binary `origin/main`-rule (branch point `7565c4868`, option A absent) and
option A, each with `MESEN_RETAIN_ALL_FRAMES` unset and set to 1 (a scratch
switch that bypasses the de-duplication of `RecordGridFrame`; see
"Reproducing"). Both switches are env-gated, both are out of the tree.

| arm | stream entries / played | captures written | tool mean exact rate | replay covering (of which other-fine) | render trace, frames 1083-4680 drawn / stale >2000 | `screen001` drawn / stale | 31 s frame checksum | replay-vs-trace winner agreement | anchor phase | save total |
|---|---|---|---|---|---|---|---|---|---|---|
| main rule, today | 950 / 3 607 | 15 | 0.0963 | 10 064 (4 853) | 3 208 / 2 307 | 167 / 90 | `0x78B3AA36` | 3 497 / 3 607 | 12 ms | 3.42 s |
| main rule, full | **3 607 / 3 607** | 15 | 0.0963 | 10 064 (4 853) | 3 208 / 2 307 | 167 / 90 | `0x78B3AA36` | 3 497 / 3 607 | 135 ms | 3.90 s |
| A, today | 950 / 3 607 | 13 | 0.0598 | 5 119 (2 315) | 3 208 / 2 307 | 167 / 90 | `0x78B3AA36` | 3 394 / 3 607 | 90 ms | 3.59 s |
| A, full | **3 607 / 3 607** | 13 | 0.0598 | 5 119 (2 315) | 3 208 / 2 307 | 167 / 90 | `0x78B3AA36` | 3 394 / 3 607 | 368 ms | 4.08 s |

Each arm was recorded and replayed in one window (four recordings at once), so
the two time columns are comparable; `main rule, today` and `A, today`
reproduce ADR-0233's before/after numbers and packs byte for byte.

**Full retention changes no rendered pixel and no capture.** The render traces
are byte-identical, line for line, within each rule (3 609 common lines, `cmp`
clean); `screen001`'s 167 drawn / 90 stale frames, the 31 s frame's checksum,
the capture count and the per-gate firing counts are the same in all four arms.
The 31 s frame is still `backgrounds/screen001.png` at a 25 348-pixel
difference, and the HUD still reads **TIMER 149 / SCORE 000000** where the game
shows 120 / 000100.

What full retention *does* move is counters and the greedy's tie order, and
only under option A:

- main rule: per-screen `Rivals` sums 1 264 → 1 264 (unchanged — twins at the
  capture's own `FineX` are *variants*, so they never reach the rival list),
  and the written pack is byte-identical;
- option A: `Rivals` sums 2 575 → **21 808** (8.5×), `AdditionRivals` on the
  Punch-Out!! card route 228 → 1 450 and on F-1 Race 143 → 713, and 35 of 56
  screens pick the **same cells in a different order** (`screen015`'s two
  `tileAtPosition` lines swap). No screen picks a different cell set in any
  arm, and the pack stays semantically identical.

Cost of the wider stream, measured in the same window: the four recordings'
`FinalizeScreenAnchors` phase goes 12 → 135 ms (main) and 90 → 368 ms (A),
the whole save 3.42 → 3.90 s and 3.59 → 4.08 s, the stream 2.7 MB → 10.4 MB,
and the 4096-entry cap 13 % closer (a 68 s route now fills it).

### The same on the other three routes (option A, today vs full)

| route (60 s) | stream entries today → full (played) | captures | tool mean exact rate | replay covering (other-fine) | render trace drawn / stale >2000 |
|---|---|---|---|---|---|
| Castlevania `stage1-run` | 1 925 → 3 555 (3 555) | 39 → 39 | 0.0142 | 2 135 (170) | 2 018 / 433 both |
| Punch-Out!! card route | 592 → 3 351 (3 351) | 9 → 9 | 0.0865 | 2 608 (0) | 2 012 / 3 both |
| F-1 Race, power-on | 716 → 3 568 (3 568) | 5 → 5 | 0.1570 | 2 801 (0) | 3 000 / 205 both |

Traces byte-identical, capture counts identical, condition counts identical,
`Rivals` totals and the `AdditionRivals` counter scaling as above. **Retaining
more frames does not close #499, and on these routes it changes nothing that
reaches the screen.**

### Why the gate still fires: the evidence model, not the retention

Two measurements at emulated frame 2946 (played index 1863 in the dump, = trace
frame 1083 + 1863; the alignment was checked by maximising replay-vs-trace
agreement over offsets −3..+3, which is maximal and equal to the tool's own
3 497/3 607 at offset 0).

1. **At run time all three probes match.** A scratch trace of the condition
   read (`MESEN_TRACE_BG_PROBE=screen001`; the comparison
   `HdPackTileAtPositionCondition` makes, index *and* palette) prints
   `want=013E/0F001032 live=013E/0F001032 match=1`,
   `want=100B/0F25300F live=100B/0F25300F match=1`,
   `want=100D/0F25300F live=100D/0F25300F match=1`. The gate fires by its own
   rule; the art under it is misaligned by the scroll, which is the 25 348-pixel
   difference and the frozen HUD.
2. **The recorder's covering cell for those probes is the neighbouring tile.**
   In the same frame's grid, row 24 carries cells at dumped x = 23, 31, 39, 47;
   the pack's probe tile (index `100B`, bytes `FFE1CC9F9F9FCCE1…`) sits at
   dumped x = **39**, while the covering window the replay and option A both
   use for pixel x = 32 (cells whose dumped origin is in [25, 32]) reads
   x = 31, a different letter. Row 24 is fetched at x ≡ 0 (mod 8) — the HUD
   does not scroll — while the frame's dominant fine scroll, taken from the
   playfield rows, is 7, and `LayOutGridRuns` places every row's cells
   relative to that one value: it shifts the HUD row's cells by +7 in the
   dumped coordinate system. The fence probe (x = 40, y = 184, a playfield
   row) is unaffected and matches.

So `rivals=0` for `screen001` is the model's blind spot, not evidence that the
gate separates. The 90 frames where the trace draws `screen001` and the
covering replay says it does not fire are exactly `screen001`'s 90 stale
frames, and they are 90 of the 110 frames on which the two disagree at all (the
other 20 are `screen029`). The recorder's evidence points at the wrong tile on
any frame whose rows disagree about alignment — a status bar, a split-scroll
HUD, a raster effect — and no amount of retained frames changes that.

## Decision

**Superseded by ADR-0236 (option 3).** The four options as measured: (The options below are the ones the
measurement leaves standing; A and B are not combinable into a fix for #499
in the way ADR-0233 assumed.)

### 1. Retain more frames

Keep the de-duplication as it is, or drop it so every played frame is its own
entry (the switch measured here).

- **For:** it is the honest upper bound of the evidence a stream can carry,
  and it makes `Rivals` a count of *frames* rather than of frame runs, which
  is what the run time actually sees.
- **Against, measured:** it cannot change a pick's cell set, because every
  dropped frame is a twin of a retained one; it does not move one pixel of the
  four routes; it multiplies the counters by the run lengths (A's `Rivals`
  8.5×), which makes an already-ambiguous number harder to read; and it costs
  3.8× the stream memory and, on these recordings, +0.5 s of save time. It is
  not a fix for #499.

### 2. Fix the evidence model: a probe's pixel must find the tile the run time reads

Every row of a frame needs its own fine scroll — or, cheaper and equivalent for
this purpose, the anchor pick must read the tile at the probe's absolute pixel
the way the run time does, per row, instead of deriving one column from the
frame's dominant `FineX`.

- **For:** it is the measured hole. It is what makes `Rivals` mean what the
  gate will do, and it is a recorder-side change with no run-time cost. It
  would at least have made `screen001`'s gate visibly ambiguous (that frame
  satisfies all three probes; the recorder said nothing did).
- **Against:** it does not by itself make a separating probe exist. Whether any
  cell in the stable pool could separate `screen001` from the fine-scroll
  frames is unmeasured, and the probes this capture has are the two HUD letters
  (stable, identical at every scroll) and a fence tile that repeats along the
  stage — ADR-0233 §1's own finding. It also re-opens ADR-0159 §3's
  comparability argument (`GridFrame` carries one `FineX`), and it does not
  touch packs already written, community packs included.

### 3. Guard at render time (ADR-0221 option D)

A capture never overwrites a live cell whose content it does not carry.

- **For:** the only option that fixes what is already shipped without a
  re-record, and the only one that does not depend on the recorder's evidence
  model being right — which is exactly what this measurement shows it is not.
  The trace shows the per-frame comparison is measurable.
- **Against:** unchanged from ADR-0221: it amends ADR-0156, puts a per-cell
  compare in the render hot path, and changes what an artist's painted capture
  draws.

### 4. Leave it

- **For:** the recorder keeps writing the gates it writes, and ADR-0233's
  option A is dropped (its only measured effects are 2 captures fewer on
  Ninja Gaiden and a F-1 Race regression).
- **Against:** #499 stays player-visible on every auto-installed pack
  (ADR-0146), and the class is not Ninja Gaiden's: any game that scrolls
  horizontally with a fixed status bar can produce it.

## What a human has to pick

*Resolved 2026-09-25 by ADR-0236: option 3.*

1. Whether #499 is fixed by option 3 (render time), by option 2 (a recorder
   whose evidence matches the run time, and a refusal or a wider probe search
   when it does not), or not fixed at all.
2. If option 2: whether the fix is a per-row fine scroll in `GridFrame` (and
   what that does to `IsScreenVariant` and to every stored pack) or a
   capture-time record of each probe's own tile.
3. Whether the already-shipped packs are re-recorded; the branch's option A is
   not merged, so an accepted option 2 would re-open option A's numbers.

## Consequences

- **ADR-0233's branch text needs the correction** if that branch is ever
  revisited: the frames `screen001` fires on were retained (frame 2946 is
  stream entry 597), and option A reports `rivals=0` on that screen because the
  model reads the wrong cell on a misaligned row, not because the frames are
  absent.
- **A capture's anchors are chosen from evidence that can be off by a cell.**
  Any status bar or split-scroll row makes a frame's cells disagree, for the
  recorder, with the pixels the run time tests its conditions against.
- **The draw-rate tooling inherits the same blind spot.**
  `scripts/measure_capture_draw_rate.py` and
  `runs/499-measure/replay_gates.py` resolve a probe's index to CHR bytes
  through the ROM file, which is a different question from the index comparison
  the run time makes; on this route their covering model misses exactly the 90
  frames `screen001` draws stale. Published draw rates (ADR-0223, ADR-0233)
  are upper bounds of a model that cannot see these frames.
- **Doing nothing** keeps #499 as it is: 90 stale frames of a 60 s route on
  Ninja Gaiden stage 1, and a gate that reports itself unambiguous.
- **Reproducing.** Two env-gated scratch switches: `MESEN_RETAIN_ALL_FRAMES=1`
  drops the `SamePalettedCells` collapse in `RecordGridFrame`; the existing
  `MESEN_TRACE_BG_LAYER` renders the per-frame trace, and a third
  (`MESEN_TRACE_BG_PROBE=<png stem>`) prints the run-time condition read.
  `MESEN_LOG_SCREEN_RIVALS=1` adds one `#499RET` line per pending screen
  (`rivals=`, `picked=` cells, `stream=`) plus a stream summary, which is also
  the proof that a binary carries the switch. None of it is in the tree: the
  sessions' scripts, commands and raw outputs are in the git-ignored
  `runs/retain/` and `runs/499-measure/` of the working copy that produced
  them.
