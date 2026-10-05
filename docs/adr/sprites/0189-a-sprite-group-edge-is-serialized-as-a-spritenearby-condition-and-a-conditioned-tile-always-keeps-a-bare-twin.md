# ADR-0189: A sprite-group edge is serialized as a `spriteNearby` condition, and a conditioned tile always keeps a bare twin

- Status: accepted (2026-09-14, implemented in the same change — `PlanSpriteNearby` in `Core/NES/HdPacks/SpriteGrouping.cpp` and `AttachSpriteNearbyConditions` in `HdPackBuilder`, covered by `make core-unit-tests`)
- Date: 2026-09-14
- Related: ADR-0183 (the artist kit — §3 "evidence and inference are never confused"), ADR-0178 (`SourceTileData`, the flip-baked key), ADR-0179 (poses), ADR-0153 (the retired co-occurrence clustering), ADR-0156 and issue #164 (screen anchors)

## Context

Mesen's HD Pack format has 13 condition types, and our builder has attached a condition to a `<tile>` line **zero times**: `BuildObjectSheets`' `tileNearby` (commented "Never auto-attached") and `FinalizeScreenAnchors`' `tileAtPosition` (pushed onto `bg.Conditions` only) never reach a tile rule — `HdPackTileInfo::Conditions` is never assigned, so every `<tile>` line we emit is bare. Since F9.5 `SelectSpriteEdges` already finds pairs of shapes holding one offset over ≥ 3 retained frames, ≥ 80% of the frames each appeared in, both directions, and `BuildSprites` publishes them as `evidence[]`: we detect the structure and discard it at serialization.

Contra80s writes 864 conditions: ~310 tile-level (`spriteNearby` 277, `tileNearby` 32) and ~490 scene-level `<background>` switches. Only the first is a tile-condition question, almost entirely `spriteNearby`; `predcloaked1/2/3` gate sibling tiles at offsets (13,10), (13,2), (5,10) — not "an enemy is near me" but "I am the top-left / bottom-left / top-right cell of this metasprite", exactly what `evidence[]` asserts. Non-goals: no condition editor, no renderer change, nothing for the other 11 types beyond the three refusals in §4.

## Decision

### 1. Emit `spriteNearby` from `SheetGroup::Edges`, one per non-root cell

Per `sprNNN` group, a spanning tree over its own `GroupEdge`s rooted at its most-seen cell; each non-root cell cites exactly one real edge, never a transitive offset (A–B–C yields B-from-A and C-from-B, never a synthesized A→C nobody observed). Node→tile resolution goes through `SourceTileData` (ADR-0178), not `TileData` — the OAM recorder bakes the flip bits in, the run time does not.

### 2. The volume policy is the spanning tree, and it is the decision

Measured: every kept offset in `adjacency.json` (Contra 35 256 / Mega Man 3 52 730 / Excitebike 10 132 / Zelda 3 400); thresholds re-applied to the pair table (2 022 / 1 228 / 1 202 / 550); **spanning tree over `SheetGroup::Edges`** (165 / 162 / 161 / 51). The tree is bounded by `kSheetMaxObjectCells` × groups, not vocabulary size, so it stays flat across four unlike games and lands in the order of magnitude of the 277 a human wrote.

### 3. A conditioned tile is emitted twice, and the bare twin is mandatory

`HdNesPack::GetMatchingTile` walks a key's entries in **file order** and returns the first whose conditions pass, so a conditioned entry must precede the plain one, not replace it: two lines naming the **same** PNG cell, conditioned first, a byte-identical bare line immediately after. A wrong or unevaluable condition falls through to the bare twin, so **the failure mode is "no improvement", never a hole**; dropping the bare twin is a defect. Every `<condition>` definition MUST appear **above the first `<tile>` line** (`HdPackLoader` resolves a `[name]` prefix at read time; a later definition is silently too late to bind) — in a `hires.txt` we write, too.

### 4. Three types are deliberately not emitted

- **`frameRange` from `PoseStats.Cycles`.** We observe a period, the condition needs a phase, tested as `FrameNumber % A >= B` against the emulator's **global** frame counter, unrelated to the recording's phase — wrong for anything the player triggers.
- **`tileAtPosition` from the screen sites.** `FinalizeScreenAnchors` already spends that evidence on `<background>` gating, and twice multiplies the failure mode ADR-0156 and issue #164 contain.
- **`memoryCheckConstant`.** No CPU or PPU memory stream is retained, so it means recording addresses and then *choosing which address to watch* — interpretation, not observation (ADR-0183 §3).

### 5. What a condition may state

A condition states **what the recorder observed** — the edge's `Count`, `ProbAB`, `ProbBA`, the predicate `sprNNN.json` publishes — never what a shape means.

## Consequences

- **`spriteNearby` forces the tile cache off for the whole key bucket.** `HdPackLoader` sets `ForceDisableCache` unconditionally; `HdNesPack` applies it to every entry under the key, so the bare twin does not escape it: 55 of 1906 Zelda keys (98% of sprite keys), 144 of 9774 on Mega Man 3; A/B `7212 frames` × 3 reps Zelda 344 vs `400 fps` (**−16.4%**), Mega Man 3 356 vs 352 (noise). Both stay ~5.7× real time headless — nothing at `60 fps`, but it bites sprite-heavy headroom and **moves first if the per-group budget rises.**
- **Our offsets are cell-quantised; the human's were pixels.** `SpriteGrouping` rounds to the nearest cell, so we emit multiples of 8 where Contra80s has 13, 10, 2, 5; the exact offset is still in the OAM stream.
- **Builder ordering changed:** `FinalizeScreenAnchors`, `BuildSheets` and `BuildObjectSheets` now run before the tile-serialization loop they annotate.
- **The gate is checked:** `mep_build.py build` at 0 errors; 0 `(tileData, palette)` keys lost; the emitted conditions parse back through `HdPackLoader` at 0 errors, a negative control producing `Condition not found`, or the gate is vacuous.
- **`mep_build check-coverage` failed on a conditioned pack**, and identically on pre-change packs (a repaint vs a recorder baseline while a first sheets-only `build` legitimately carries only routed keys). *Closed 2026-09-14 (#218, PR #223):* it compared every `<tile>` key, including the manifest's `textures/chr/` keys (ADR-0043), against `build`'s `textures/sheets/`-derived keys, and now narrows the baseline to sheet-derived keys.

## Record

- 2026-09-14 — §§1–5 accepted and implemented in the same change; `mep_build check-coverage` closed the same day (#218, PR #223).
