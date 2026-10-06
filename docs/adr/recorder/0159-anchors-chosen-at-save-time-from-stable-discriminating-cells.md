# ADR-0159: A captured screen's anchors are chosen at save time, from cells its variants do not change and its rivals do not share

- Status: accepted — reflected in `Core/NES/HdPacks/{ScreenStitcher,HdPackBuilder,TileSheetTypes}`; the code landed with the measurement
- Date: 2026-09-05
- Amended: 2026-09-05 (palette-swapped variants; see below); 2026-09-22 (ADR-0221 narrows §1's definition of *variant*; see below)
- Related: ADR-0162 (the accuracy harness reuses `FrameCapture.h`), ADR-0050 (bootstrap `<background>` capture), ADR-0153 (sheets), ADR-0156 (screen residency), ADR-0132, PRD Part A Phase 9, issue #164, `Core/NES/HdPacks/ScreenStitcher.cpp`, `Core/NES/HdPacks/HdPackBuilder.cpp`, `scripts/spike_anchor_stability.py`
- Amends: ADR-0050 §Decision, the clause "up to three `tileAtPosition` anchors (rarest non-flat tiles on screen, ≥ 64 px apart)" — both the criterion and the moment it is applied. Lifts ADR-0156 §Non-goals' exclusion of "changing what `CaptureScreen` captures, or its anchors".

## Amended 2026-09-22: a variant may not add content the capture lacks

ADR-0221 (accepted 2026-09-22, option B; PRD slice F12.13) narrows §1. "Cells that no variant of this screen changes" assumed a variant differs from the captured frame only in art the capture also carries. Issue #339 measured the case it does not: a frame agreeing on 919 of 960 cells (0.9573, above `kAnchorVariantAgree`) that draws text where the capture holds a flat white block. Under §1 those 41 cells were excluded from the anchor pool by construction, and the capture drew over the text. From ADR-0221 on, a frame is a variant only when every cell it changes is non-empty in the captured frame too; a frame that adds content to an empty cell is a rival, whatever its ratio. The threshold, §2, §3 and §4 are unchanged, and "a capture owns its variants" stands under that narrower *variant*.

## Amended 2026-09-05: the grid stream carries the palette

Consequences below left one of issue #164's three failure modes open: a variant that only **recolours** the anchor cell. The retained grid stream stored `ShapeId` = `HdTileKey::GetKey(true)`, palette wildcarded, while the `tileAtPosition` condition an anchor becomes compares the tile index **and** `PaletteColors`. A recolour was invisible to the stability filter, the cell was chosen, and the `<background>` did not draw — with ADR-0156's routing having already removed those cells from `metatiles.png`, that is art nothing replaces.

**Measured first, on the 30-pack library re-recorded 2026-09-05**, with a `--recolour` mode added to the same spike:

```
python3 scripts/spike_anchor_stability.py "<rom-library>" --recolour
```

- **190 of 4333 shipped anchors (4.4 %)** sit on a cell some variant of their own screen only recolours; **124 of 1450 screens (8.6 %)** carry at least one, and one such anchor stops the whole screen drawing;
- 210 of 109402 variant pairs (0.2 %) miss for that reason **alone** — the sampling artefact the Consequences already name, so the anchor count is the honest one;
- 4.6 % of the shape-stable candidate pool (30369 of 667164 cells) is this trap, concentrated: F-1 Race 88 of 279 anchors, Ninja Gaiden 35 of 422, Tetris 2 33 of 357, Super Mario Bros. 17 of 66, Zelda 1 12 of 64.

**Decision (amends §1 and §3, and ADR-0050's anchor clause with them).**

1. `GridFrame` gains a `PaletteId Palettes[30][32]` plane beside `Cells`: one byte per cell, interned in first-sight order (`HdPackBuilder::PaletteIdFor`). A candidate is stable only when every variant agrees on the shape **and** the palette id, and a rival is ruled out only when it disagrees on one of the two — the rival count now models what `tileAtPosition` compares.
2. `kUnknownPalette` (0xFF) means *no palette evidence* (no cell drawn there, a caller carrying none, or a recording past the 255-palette id space). It reads as "the colours may well be the same" on **both** sides, so a stream without palettes degrades to the pre-amendment pick — like an out-of-range `capturedIndex` degrading to ADR-0050's rarity greedy.
3. The recorder de-duplicates consecutive frames on `SamePalettedCells`, not `SameCells`. A frame that only recolours the screen is the evidence this amendment reads, and collapsing it into `RepeatCount` would throw it away. `MetatileVocabulary` keeps `SameCells`: a recoloured tile is still one subject (ADR-0153 §5, ADR-0132).
4. `SelectScreenAnchors` stays host-free and unit-testable; the cases are `BlocoP2` in `scripts/core_unit_tests.cpp`.

**Cost.** A retained frame goes from 1920 B to 2880 B, so a full stream (`kMaxSheetFrames` = 4096) goes from ~7.9 MB to ~11.5 MB. The palette filter leaves **1426 of 1450 screens (98.3 %)** with three stable candidates 64 px apart; the other 24 take §1's widening path.

**Alternatives rejected.**

- *Drop the palette from the condition* (`IgnorePalette`, supported by `HdPackBaseTileCondition`). Free, but aligns the condition at the loose end: **22 pairs of captured screens are shape-identical and differ only in colour**, so palette-agnostic conditions make each match the other's `<background>` — the "wrong screen drawn whole" §1 calls worse than a miss. (`HdPackBaseTileCondition::ToString` does not serialize `IgnorePalette`, so such a pack would reload without it.)
- *A palette witness map instead of a plane* — `(fineX, row, col, shape) -> palette`, with a "seen with two palettes" flag. Unbounded on a long recording and only answers globally; strictly less predictable than 960 bytes a frame.
- *Re-checking each candidate's palette at capture time.* Per frame against every pending screen (up to 300), each a 960-cell classification, on the recorder's hot path.
- *Packing the plane to 4 bits per cell* (~544 B/frame instead of 960 B). Saves 1.7 MB at the cap for nibble packing in the one structure every module reads; not worth it.

**Still not fixed.** The figures are a projection from captured screens, candidates are restricted to tile-aligned runs, save-time cost is unprofiled, and a screen captured before a mid-session `SaveHdPack` is anchored against a shorter stream. Nothing here was validated on a fresh recording.

## Context

ADR-0050 gates every captured screen on up to three `tileAtPosition` conditions, picked as the rarest non-flat tiles on the frame, at least 64 px apart, **at the moment the screen is captured**. ADR-0156 then made those conditions load-bearing: a routed scene cell leaves `metatiles.png` because the `<background>` is expected to paint it. When the condition set does not match, the cell is drawn vanilla and the routing has removed art nothing replaces.

An anchor must be **stable** — a cell no later variant of the same screen changes, or the `<background>` stops drawing on the next score digit — and it must **discriminate** — a set no unrelated screen satisfies, or the wrong screen is drawn whole.

`scripts/spike_anchor_stability.py` measured the shipped rule over the 30-pack library (1166 screens, 3487 anchors; two screens count as variants when they agree on ≥ 90 % of their 960 8x8 blocks): 1558 of 3487 anchors (44.7 %) sit on a cell a variant changes; 866 of 1166 screens (74.3 %) carry one; 21424 of 29218 variant pairs (73.3 %) therefore do not draw; 3563 of 80902 unrelated pairs (4.4 %) match falsely. Donkey Kong 128/162 anchors unstable, Tetris 2 277/357, F-1 Race 203/279, Golf 100/306.

The fix the issue proposed does not work, and the same measurement says so: preferring cells no variant changes, as a straight filter over rarity, collapses misses to 36/29218 (0.1 %) and explodes false matches to 16237/80902 (**20.1 %**) — Bomberman 861 → 8274, Tetris 2 642 → 7857. What survives every variant is the frame border every other screen also has, and a false match draws the wrong screen whole, worse than a gap in one.

**Non-goals.** This does not change what is captured, when a screen is captured, the `screenNNN` numbering, priority 20, the 300-screen cap, or ADR-0156's residency rule. It does not touch `hud`/`font`/`misc`.

## Decision

**1. Stability is the filter; discrimination is the objective.** Among the candidate cells that no variant of this screen changes, the pick greedily takes the ones leaving the fewest rival frames still matching. When the stable region cannot fill three conditions, or cannot separate the screen from its rivals, the pool widens to the volatile cells rather than ship an ambiguous set — an anchor that sometimes fails to draw beats one that draws the wrong screen. ADR-0050's three-condition cap and 64 px spread are kept.

**2. The pick happens at save time, not at capture time.** A screen is captured the first time it holds still, so every later variant is still in the future; the evidence only exists once the session is over. `HdPackBuilder::CaptureScreen` records a `PendingScreen` with up to 160 rarity-ranked, tile-aligned candidates and pushes the bitmap (numbering and ADR-0156's `GridFrame::Captured` tie unchanged); `FinalizeScreenAnchors()` runs just before `BuildSheets()`, so `<condition>` and `<background>` serialize in the same order and position as before.

**3. The rule is host-free and testable.** `MesenSheets::SelectScreenAnchors(frames, capturedIndex, candidates)` returns `AnchorChoice{Picked, Rivals, UsedVolatileCell}`. A frame is a variant or a rival only under the **same `FineX`** — a frame at another fine scroll is not the pixel a `tileAtPosition` reads. A `capturedIndex` out of range (past `kMaxSheetFrames`, or no retained frame) degrades to ADR-0050's plain rarity-and-spread greedy, never to no anchors.

**4. Constants**, in `TileSheetTypes.h` with the measurement in the comment: `kAnchorVariantAgree` 0.90, `kAnchorCandidateCap` 40, `kAnchorCount` 3, `kAnchorMinSpread` 64. Sensitivity: at 0.85 the miss rate goes 77.1 % → 30.4 %, at 0.95 72.1 % → 9.5 %; false matches drop ~5x at every setting. Projected on the same library: variant pairs missed 73.3 % → **13.4 %**, false matches 4.4 % → **0.88 %**, which rules out the stability-only rule.

## Consequences

The projection's model differs from the shipped rule in three ways: the spike compares pixels of *captured* screens while the implementation compares palette-agnostic shape ids across the whole retained grid stream (more evidence, so 13.4 % is likely pessimistic); the spike's rivals are the other captured screens while the implementation's are every retained frame at the same `FineX` (0.88 % on a smaller universe); and the condition compares tile index + palette while the spike compares pixels (so the 73.3 % shipped miss rate is a lower bound). The figures are direction, not a guarantee.

Palette-swapped variants stayed unfixed at the time of writing: `ShapeId` is `GetKey(true)`, palette wildcarded, so a variant that only recolours the anchor tile read as stable while the condition — which compares `PaletteColors` — failed; the 2026-09-05 amendment closes that gap.

Candidates are restricted to tile-aligned runs, so a screen whose every non-flat tile is off-grid yields no candidates, no `<background>` and no `Captured` flag. Save-time cost is bounded by 300 screens x `kMaxSheetFrames` of variant classification, cut by an early exit on the mismatch budget; it is not profiled on a real save. Screens captured before a mid-session `SaveHdPack` are anchored against a shorter stream.
