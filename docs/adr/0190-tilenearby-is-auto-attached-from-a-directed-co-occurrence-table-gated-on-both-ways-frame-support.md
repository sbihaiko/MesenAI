# ADR-0190: `tileNearby` is auto-attached from a directed co-occurrence table, gated on both-ways frame support

- Status: accepted (2026-09-14, implemented in the same change —
  `MesenSheets::SelectTileNearby` in `Core/NES/HdPacks/SheetGrouping.cpp` and
  `BuildObjectSheets` in `HdPackBuilder`, covered by `make core-unit-tests`)
- Date: 2026-09-14
- Related: ADR-0189 (the bare-twin policy this rests on, and the `tileNearby`
  deferral it closes), ADR-0183 §3 (evidence and inference are never confused),
  ADR-0153 (the retired co-occurrence clustering that left this table behind),
  ADR-0127 (host-free analysis, unit-tested away from the emulator)
- Amends: ADR-0189 §4's list of deferrals, by removing `tileNearby` from it.
  The three refusals ADR-0189 states in §4 — `frameRange`, `tileAtPosition`,
  `memoryCheckConstant` — are untouched and still stand.

## Context

ADR-0189 attached `spriteNearby` and explicitly deferred `tileNearby`, on the
strength of the in-code comment that had guarded `BuildObjectSheets` since
F5.4e: *"Never auto-attached - a wrong inference must not make a tile fail to
render."* Of the reference pack Contra80s' 864 conditions, 34 are `tileNearby`
(gating 98 `<tile>` lines) and we emitted zero of them — we computed the
candidates and threw them away as `# inferred` comments for an artist to wire
up by hand, which nobody has ever done.

The deferral rested on two beliefs. Both were measured, and both were wrong in
a way that changes the answer. The full study, with method and raw numbers, is
`docs/validation/tilenearby-evidence-study.md`.

**A wrong `tileNearby` costs nothing.** ADR-0189 §3 established that a
conditioned `<tile>` is always serialized twice, the conditioned line first and
a byte-identical bare twin immediately after, because
`HdNesPack::GetMatchingTile` walks a key's entries in file order and returns the
first whose conditions pass. A failing condition therefore costs one predicate
evaluation and then renders the bare line. Measured on a recorded Contra pack
(387 conditions, 517 conditioned lines, loaded and played for 3606 frames): the
pack as written and the same pack with **all 186 `tileNearby` targets replaced
by a pattern the game never draws** — so every one of them fails at run time —
produce the **identical** frame, checksum `0x80267D82`, with the background tile
match rate at 100% in both. The failure mode really is "no improvement, never a
hole", and it is now demonstrated rather than argued.

It does not even cost what `spriteNearby` costs. `HdPackLoader` sets
`ForceDisableCache` for `spriteNearby` unconditionally, but for `tileNearby`
only when the offset is off the 8 px grid. Every offset we emit is `(8,0)` or
`(0,8)`, so the tile cache stays on. A/B over 7212 frames × 3 reps, same pack
with and without the `tileNearby`-gated lines: 32.1 s vs 32.2 s of wall clock,
inside the run-to-run noise — against the −16.4% ADR-0189 had to accept for
`spriteNearby` on Zelda.

**The evidence was not weak; it was stated weakly.** Two defects in how the
table was built, not in what it saw:

1. The key was `{min(a, b), max(a, b)}`, which throws away *which* of the two
   shapes was the left/top one. The table could say "these two are horizontally
   adjacent"; the emitted condition, which must say "the target sits 8 px east
   **of me**", then took its sign from hash ordering. Roughly half pointed the
   wrong way. The same key folded E and S into one `ECount`/`SCount` pair and
   resolved two different claims by a majority vote.
2. The gate was `ECount + SCount >= 3`, read as if it meant `spriteNearby`'s
   "≥ 3 frames". It counts *cells*: one accumulated frame of a static screen
   raises it by up to 30 on its own. Measured over five games, it rejects
   essentially nothing — on Contra, Castlevania, Zelda and Excitebike every
   candidate that passed the object-membership test already passed it, and on
   Mega Man 2 it removed 19 of 342. It is not a threshold, it is a formality.

With the key made directed and the support counted per frame, the candidates'
both-ways conditional probability is bimodal, sharply so on four of the five
games measured. On Contra: 466 candidates, of which 185 sit in `[0.95, 1.00]`
and the next bucket down holds 3. That mode is pairs that are two halves of one
thing. Zelda is the weak case — 39 in the top bucket against 35 in the next —
and is the reason the threshold is set at 0.80 rather than at the gap, which
would be tuning to four samples.

Non-goals: this does not change the renderer, does not add a condition editor,
does not recover sub-cell offsets (ours stay multiples of 8, as ADR-0189 noted),
and does not revisit ADR-0189 §4's other three refusals.

## Decision

### 1. The co-occurrence key is ordered and carries its direction

`_coOccurrence` is keyed by `HdPackCoOccurrenceKey{A, B, South}`, where `A` is
always the left/top cell and `B` the cell one step east (`South = false`) or
south (`South = true`) of it. "B is east of A" and "B is south of A" are two
statements with their own tallies and are judged separately. The edge carries a
cell `Count` and, new, a per-frame `Frames`, incremented at most once per
accumulated frame.

### 2. An edge is emitted only when it predicts both ways

`MesenSheets::SelectTileNearby` keeps an adjacency when all of:

- both shapes are inside an inferred object (`_sheetObjectShapes`) — unchanged,
  and still the filter doing most of the work;
- `Frames >= kTileNearbyMinFrames` (3);
- `Frames / FramesA >= kTileNearbyMinProbability` **and**
  `Frames / FramesB >= kTileNearbyMinProbability` (0.80), where `FramesX` is the
  number of accumulated frames shape X was on screen in at all;
- `A != B`. A tile adjacent to a copy of itself — a run of floor, a field of
  sky — scores 100% both ways while saying nothing about where anything is.
  It was 5–16% of the survivors before this clause.

Reading the probability in both directions is what keeps a merely *common*
shape from becoming everyone's neighbour: it would hold `P(edge | rare shape)`
and fail `P(edge | common shape)`. The probability gate is the one that does the
work; the frame floor is a cheap guard against a pair seen on one transition
screen and never subsumes it — every survivor at 0.80 already had ≥ 60 frames.

The selection is host-free and unit-tested (ADR-0127): `HdPackBuilder` turns its
table into `TileAdjacency` records, `SelectTileNearby` decides, the builder
writes bytes.

### 3. The survivors are attached, under ADR-0189 §3 unchanged

Each survivor becomes one `HdPackTileNearbyCondition` at offset `(8,0)` or
`(0,8)`, with `ignorePalette` set — a shape id is `GetKey(true)`, so the
evidence is palette-wildcarded and the condition must be too, or the pair would
stop matching itself the moment the game recoloured it.

It is attached to **every** palette variant of the source shape that is real art
(`_paletteVariantsByShape`, skipping `DefaultTile` neutral-ramp placeholders),
through the same `_tileGateConditions` path `spriteNearby` uses. That path is
what guarantees the bare twin, and ADR-0189 §3 governs it in full: conditioned
lines first, a byte-identical bare line last, `<condition>` definitions above
the first `<tile>`. Any change that drops the bare twin is a defect.

Per ADR-0189 §5, each emitted condition ships with the evidence that justified
it, as the `# inferred tileNearby [name] requires tile NN 8px east - held in N
frames, N% / N% both ways` comment. It states what the recorder observed, never
what either shape means.

### 4. Volume

Conditions emitted, 120 emulated seconds per game, one recording each:

| game | raw edges | both shapes in an object | old `>= 3` gate | emitted |
|---|---|---|---|---|
| Castlevania | 1790 | 770 | 770 | 242 |
| Contra | 1178 | 466 | 466 | 186 |
| Mega Man 2 | 1787 | 342 | 323 | 117 |
| The Legend of Zelda | 916 | 220 | 220 | 77 |
| Excitebike | 1002 | 21 | 21 | 16 |

Same order of magnitude as ADR-0189's spanning tree (165/162/161/51) and as the
34 a human wrote, and bounded by object membership rather than by vocabulary
size, which is why it stays flat across unlike games.

## Consequences

- **The old `# inferred ... attach by hand` comment is gone**, replaced by a
  statement of what *was* attached and on what evidence. A pack recorded before
  this change carries the old comments and no conditions; nothing reads them, so
  there is no migration.
- **Two silent defects are fixed, and packs recorded before this carry them.**
  The undirected key meant roughly half of the old advisory comments pointed the
  wrong way, and `Count() >= 3` never rejected anything. Any decision taken by
  reading those comments should be re-taken.
- **A `tileNearby`-gated tile keeps the tile cache**, unlike a
  `spriteNearby`-gated one, as long as the offset stays cell-aligned. If a later
  slice recovers sub-cell offsets (ADR-0189's open thread), that property is the
  first thing it spends, and `HdPackLoader`'s `TileNearby` case is where it
  happens.
- **The gate is checkable and was checked**, the same three ways as ADR-0189
  plus the new one: `mep_build.py build` on a recorded Contra pack at **0
  errors**; **0 `(tileData, palette)` keys lost** — 2350 in, 537 carried, 1813
  dropped, byte-identical to the same recording built before this change, so the
  drop is ADR-0172's accepted pack shape and not this; the emitted conditions
  **parse back through `HdPackLoader` with 0 errors**, with a negative control
  (two definitions renamed by hand) producing exactly `Condition not found:
  obj_nearby0` / `obj_nearby1` and `Loaded with 2 errors`; and the
  all-targets-broken variant rendering an identical frame.
- **`MESEN_TILENEARBY_EVIDENCE` dumps the whole table** as CSV before any
  threshold is applied, so the constants above can be re-checked on a new game
  without a rebuild. It writes nothing when unset and never affects a pack.
- **The thresholds are two constants in one place**
  (`kTileNearbyMinFrames`, `kTileNearbyMinProbability` in `HdPackBuilder.h`) and
  the study they came from is a doc, not a commit message. Moving them is a
  measurement, not a taste call.
