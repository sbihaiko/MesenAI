# ADR-0190: `tileNearby` is auto-attached from a directed co-occurrence table, gated on both-ways frame support

- Status: accepted (2026-09-14, implemented in the same change — `MesenSheets::SelectTileNearby` in `Core/NES/HdPacks/SheetGrouping.cpp` and `BuildObjectSheets` in `HdPackBuilder`, covered by `make core-unit-tests`)
- Date: 2026-09-14
- Related: ADR-0189 (the bare-twin policy this rests on, and the `tileNearby` deferral it closes), ADR-0183 §3 (evidence and inference are never confused), ADR-0153 (the retired co-occurrence clustering that left this table behind), ADR-0127 (host-free analysis, unit-tested away from the emulator)
- Amends: ADR-0189 §4's list of deferrals, by removing `tileNearby` from it. The three refusals ADR-0189 states in §4 — `frameRange`, `tileAtPosition`, `memoryCheckConstant` — are untouched and still stand.

## Context

ADR-0189 deferred `tileNearby` on the strength of the in-code comment guarding `BuildObjectSheets` since F5.4e: *"Never auto-attached - a wrong inference must not make a tile fail to render."* Of Contra80s' 864 conditions 34 are `tileNearby` (gating 98 `<tile>` lines) and we emitted zero — computed the candidates and threw them away as `# inferred ... attach by hand` comments nobody ever wired up. Full study: `docs/validation/measurements/tilenearby-evidence-study.md`.

**A wrong `tileNearby` costs nothing.** ADR-0189 §3 always serializes a conditioned `<tile>` twice, the conditioned line first and a byte-identical bare twin after, because `HdNesPack::GetMatchingTile` returns the first entry whose conditions pass. Measured on a recorded Contra pack (387 conditions, 517 conditioned lines, 3606 frames): the pack as written and the same pack with **all 186 `tileNearby` targets replaced by a pattern the game never draws** — every one failing at run time — produce the identical frame, checksum `0x80267D82`. It has no cost `spriteNearby` has either: `HdPackLoader` sets `ForceDisableCache` for `spriteNearby` unconditionally, but for `tileNearby` only off the 8 px grid, and every offset we emit is `(8,0)` or `(0,8)`. A/B over `7212 frames` × 3 reps: `32.1 s` vs `32.2 s`, inside the noise — against `spriteNearby`'s −16.4%.

**The evidence was not weak; it was stated weakly.** Two defects in how the table was built:

1. The key was `{min(a, b), max(a, b)}`, throwing away which shape was left/top. The table could say "these two are horizontally adjacent"; the emitted condition, which must say "the target sits 8 px east of me", then took its sign from hash ordering — roughly half pointed the wrong way. The same key folded E and S into one `ECount`/`SCount` pair and resolved two claims by majority vote.
2. The gate was `ECount + SCount >= 3`, read as if it meant `spriteNearby`'s "≥ 3 frames". It counts *cells*: one accumulated frame of a static screen raises it by up to 30. It rejects essentially nothing — on four of five games every candidate that passed object-membership already passed it, on Mega Man 2 it removed 19 of 342.

With the key directed and support per frame, the both-ways conditional probability is bimodal on four of five games: on Contra 466 candidates, 185 in `[0.95, 1.00]` and 3 in the next bucket. Zelda is weak (39 vs 35), hence the threshold at 0.80 rather than at the gap (tuning to four samples). Non-goals: no renderer change, no condition editor, no sub-cell offsets (ours stay multiples of 8, as ADR-0189 noted), no revisit of ADR-0189 §4's other refusals.

## Decision

### 1. The co-occurrence key is ordered and carries its direction

`_coOccurrence` is keyed by `HdPackCoOccurrenceKey{A, B, South}`, `A` always the left/top cell and `B` the cell one step east (`South = false`) or south (`South = true`) of it. "B is east of A" and "B is south of A" are separate statements with their own tallies. The edge carries a cell `Count` and a per-frame `Frames`, incremented at most once per accumulated frame.

### 2. An edge is emitted only when it predicts both ways

`MesenSheets::SelectTileNearby` keeps an adjacency when all of: both shapes are inside an inferred object (`_sheetObjectShapes`); `Frames >= kTileNearbyMinFrames` (3); `Frames / FramesA >= kTileNearbyMinProbability` **and** `Frames / FramesB >= kTileNearbyMinProbability` (0.80), `FramesX` being the frames shape X was on screen; `A != B` (a tile adjacent to a copy of itself scores 100% both ways saying nothing — 5–16% of the survivors before this clause). Reading the probability both ways keeps a merely common shape from becoming everyone's neighbor: it holds `P(edge | rare shape)` and fails `P(edge | common shape)`. The probability gate does the work; the frame floor guards a one-transition pair — every survivor at 0.80 already had ≥ 60 frames`. The selection is host-free and unit-tested (ADR-0127): `HdPackBuilder` turns its table into `TileAdjacency` records, `SelectTileNearby` decides, the builder writes bytes.

### 3. The survivors are attached, under ADR-0189 §3 unchanged

Each survivor becomes one `HdPackTileNearbyCondition` at `(8,0)` or `(0,8)` with `ignorePalette` set — a shape id is `GetKey(true)`, so evidence and condition must both be palette-wildcarded or the pair stops matching once recoloured. Attached to every palette variant of the source shape that is real art (`_paletteVariantsByShape`, skipping `DefaultTile`) through the same `_tileGateConditions` path `spriteNearby` uses, which is what guarantees the bare twin; ADR-0189 §3 governs it in full (conditioned lines first, a byte-identical bare line last, `<condition>` definitions above the first `<tile>`). Per ADR-0189 §5 each ships `# inferred tileNearby [name] requires tile NN 8px east - held in N frames, N% / N% both ways`, stating what the recorder observed.

### 4. Volume

Conditions emitted, 120 emulated seconds per game: Castlevania 242, Contra 186, Mega Man 2 117, Zelda 77, Excitebike 16. Same order of magnitude as ADR-0189's spanning tree (165/162/161/51) and as the 34 a human wrote, bounded by object membership not vocabulary size.

## Consequences

- **The old `# inferred ... attach by hand` comment is gone**, replaced by what *was* attached and on what evidence. Nothing reads it, so no migration.
- **Two silent defects are fixed, and packs recorded before this carry them**: the undirected key (roughly half the advisory comments pointed wrong) and `Count() >= 3` (never rejected anything). Re-take any decision read from them.
- **A `tileNearby`-gated tile keeps the tile cache**, unlike a `spriteNearby`-gated one, while the offset stays cell-aligned; a later sub-cell slice spends that first, in `HdPackLoader`'s `TileNearby` case.
- **The gate is checkable and was checked**, ADR-0189's three ways plus one: `mep_build.py build` on a recorded Contra pack at 0 errors; 0 `(tileData, palette)` keys lost (2350 in, 537 carried, 1813 dropped, byte-identical, ADR-0172's accepted shape); conditions parse back through `HdPackLoader` with 0 errors, a negative control producing `Condition not found`; and the all-targets-broken variant rendering an identical frame.
- **`MESEN_TILENEARBY_EVIDENCE` dumps the whole table** as CSV before any threshold, writing nothing when unset.
- **The thresholds are two constants in one place** (`kTileNearbyMinFrames`, `kTileNearbyMinProbability` in `HdPackBuilder.h`); moving them is a measurement, not a taste call.

## Record

- 2026-09-14 — accepted and implemented in the same change. Amends ADR-0189 §4 by removing `tileNearby` from its deferrals; the other three refusals stand.
