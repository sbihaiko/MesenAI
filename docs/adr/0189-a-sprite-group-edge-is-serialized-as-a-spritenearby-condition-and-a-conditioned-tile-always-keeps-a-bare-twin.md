# ADR-0189: A sprite-group edge is serialized as a `spriteNearby` condition, and a conditioned tile always keeps a bare twin

- Status: accepted (2026-09-14, implemented in the same change — `PlanSpriteNearby`
  in `Core/NES/HdPacks/SpriteGrouping.cpp` and `AttachSpriteNearbyConditions` in
  `HdPackBuilder`, covered by `make core-unit-tests`)
- Date: 2026-09-14
- Related: ADR-0183 (the artist kit — §3 "evidence and inference are never
  confused"), ADR-0178 (`SourceTileData`, the flip-baked key), ADR-0179 (poses),
  ADR-0153 (the retired co-occurrence clustering), ADR-0156 and issue #164
  (screen anchors)

## Context

Mesen's HD Pack format has 13 condition types. Our builder has attached a
condition to a `<tile>` line **zero times**: it constructs two condition objects
and neither reaches a tile rule (`BuildObjectSheets`' `tileNearby`, commented
"Never auto-attached"; `FinalizeScreenAnchors`' `tileAtPosition`, pushed onto
`bg.Conditions` only). `HdPackTileInfo::Conditions` is never assigned, so every
`<tile>` line we emit is bare.

Meanwhile the reference pack Contra80s carries 864 condition definitions, all
hand-written. Splitting them by *what they gate* corrects a framing we had
wrong: it is not one gap of 782, it is **~310 tile-level** conditions
(`spriteNearby` 277, `tileNearby` 32) and **~490 scene-level** `<background>`
switches. `memoryCheckConstant` is overwhelmingly a background gate — 13647
references from `<background>` lines against 19 from `<tile>` lines. Only the
first group is a tile-condition question, and it is almost entirely
`spriteNearby`.

And we already compute that lattice. Since F9.5, `SelectSpriteEdges` finds pairs
of sprite shapes holding one constant offset over at least 3 retained frames,
accounting for at least 80% of the frames each shape appeared in, in both
directions; `BuildSprites` lays the survivors out and `sprNNN.json` publishes
them as `evidence[]`. We detect the structure and discard it at serialization
time.

**What a human actually means by the condition.** Contra80s' `predcloaked1/2/3`
share one anchor shape and gate three sibling tiles on it at offsets (13,10),
(13,2), (5,10), whose targets are (0,0), (0,16), (16,0) of one PNG. The
statement is not "an enemy is near me". It is **"I am the top-left / bottom-left
/ top-right cell of this metasprite"** — a cell disambiguating *which* cell it is
by its offset to a co-moving neighbour. That is exactly what `evidence[]`
already asserts, one abstraction step up.

Non-goals: this does not add a condition editor, does not change the renderer,
and does not decide the remaining 11 condition types beyond the three refusals
in §4.

## Decision

### 1. Emit `spriteNearby` from `SheetGroup::Edges`, one per non-root cell

Per `sprNNN` group, build a spanning tree over the group's **own** edges, rooted
at its most-seen cell. Each non-root cell yields one `spriteNearby` citing
**exactly one real `GroupEdge`**. No offset is ever composed transitively: a
chain A–B–C yields B-from-A and C-from-B, never a synthesized A→C that nobody
observed.

Node→tile resolution goes through `SourceTileData` (ADR-0178), not `TileData` —
the OAM recorder bakes the flip bits in and the run time does not.

### 2. The volume policy is the spanning tree, and it is the decision

Three policies were measured before choosing:

| policy | Contra | Mega Man 3 | Excitebike | Zelda |
|---|---|---|---|---|
| every kept offset in `adjacency.json` | 35 256 | 52 730 | 10 132 | 3 400 |
| grouping thresholds re-applied to the pair table | 2 022 | 1 228 | 1 202 | 550 |
| **spanning tree over `SheetGroup::Edges`** | **165** | **162** | **161** | **51** |

The tree is bounded by `kSheetMaxObjectCells` × groups rather than by vocabulary
size, which is why it stays flat across four unlike games and lands in the same
order of magnitude as the 277 a human wrote.

### 3. A conditioned tile is emitted twice, and the bare twin is mandatory

`HdNesPack::GetMatchingTile` walks a key's entries in **file order** and returns
the first whose conditions pass. A conditioned entry therefore cannot replace
the plain one — it must precede it.

A tile carrying conditions MUST be serialized as two lines naming the **same**
PNG cell: the conditioned line first, a byte-identical bare line immediately
after. This is not an optimization, it is the safety property the whole slice
rests on. A condition that is wrong, or that the run time cannot evaluate — the
OAM evidence includes sprites the 8-per-scanline limit hid — falls through to
the bare twin, and the pack renders exactly as it did before. **The failure mode
is "no improvement", never a hole.** Any change that drops the bare twin removes
that guarantee and is a defect.

Correspondingly, in a `hires.txt` we write, every `<condition>` definition MUST
appear **above the first `<tile>` line**. `HdPackLoader` resolves a `[name]`
prefix at read time; a definition placed after its consumers is silently too
late to bind.

### 4. Three types are deliberately not emitted

- **`frameRange` from `PoseStats.Cycles`.** We observe a *period*; the condition
  needs a *phase*, and it is tested as `FrameNumber % A >= B` against the
  emulator's **global** frame counter, which has no relation to the recording's
  phase. Correct only for an animation locked to that counter, and silently
  wrong for anything the player triggers.
- **`tileAtPosition` from the screen sites.** `FinalizeScreenAnchors` already
  spends that evidence on `<background>` gating; spending it twice multiplies
  the failure mode ADR-0156 and issue #164 exist to contain.
- **`memoryCheckConstant`.** The builder retains no CPU or PPU memory stream at
  all. Emitting one would require first recording addresses, and then *choosing
  which address to watch* — an act of interpretation, not observation. A memory
  condition can only be invented here, never observed, and ADR-0183 §3 forbids
  it.

### 5. What a condition may state

A condition we emit states **what the recorder observed**: two shapes held this
offset, in this many frames, at this probability both ways — the same predicate
`sprNNN.json` already publishes. It never states what either shape *means*. The
plan carries the edge's `Count`, `ProbAB` and `ProbBA`, so every emitted
condition ships with its own evidence.

## Consequences

- **`spriteNearby` forces the tile cache off, for the whole key bucket.**
  `HdPackLoader` sets `ForceDisableCache` for any tile carrying it —
  unconditionally, cell-aligned or not — and `HdNesPack` applies it to every
  entry under that key, so the bare twin does not escape it either. Measured:
  55 of 1906 keys on Zelda (98% of its sprite keys), 144 of 9774 on Mega Man 3.
  A/B over 7212 frames × 3 reps: **Zelda 344 vs 400 fps, −16.4%**; Mega Man 3
  356 vs 352, noise. Both still run ~5.7× real time headless, so there is no
  visible cost at 60 fps on this hardware — but it is a real bite out of
  headroom on a sprite-heavy game, it scales with how many sprite keys a pack
  gates, and **it is the number that moves first if the per-group budget ever
  rises.**
- **Our offsets are cell-quantised; the human's were pixels.** `SpriteGrouping`
  rounds to the nearest cell, so we emit multiples of 8 where Contra80s has 13,
  10, 2, 5. The exact pixel offset is still in the OAM stream and is recoverable
  by a later slice.
- **`mep_build check-coverage` fails on a conditioned pack** — and fails
  identically on packs recorded before this change, because it compares a
  repaint against a recorder baseline while a first sheets-only `build`
  legitimately carries only routed keys. Not a gate this decision can pass or
  fail; noted so it is not read as a regression.
  *Closed 2026-09-14 (#218, PR #223):* the diagnosed cause was right and the
  remedy was not a matter for this ADR. The check resolved every `<tile>` key of
  its baseline, while `build` re-derives only the keys a `textures/sheets/` cell
  claims — so the recorder manifest, which also keys every CHR tile it saw out
  of `textures/chr/` (ADR-0043), was compared against a universe it was never
  part of. It now narrows the baseline to sheet-derived keys, refuses one that
  has none, and reads a baseline kept outside the pack against the pack under
  test. The limitation above is no longer live; the paragraph is left in place
  because the decision it was recorded under is unchanged.
- **Builder ordering changed.** `FinalizeScreenAnchors`, `BuildSheets` and
  `BuildObjectSheets` now run *before* the tile-serialization loop, because they
  must run before the lines they annotate. Nothing they read comes from that
  loop.
- **The gate is checkable and is checked.** Acceptance is three separate
  things, and the third is new: `mep_build.py build` with 0 errors; 0
  `(tileData, palette)` keys lost; and **the emitted conditions parse back
  through `HdPackLoader` with 0 errors**. We had never emitted a condition, so
  we had never proven we can read our own output. A negative control (two
  condition names broken by hand) must produce `Condition not found`, or the
  gate is vacuous.
