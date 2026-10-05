# ADR-0153: Artist-legible sheets — metatile vocabulary as the unit, mutual-predictability grouping, stitched maps as an artist surface

- Status: accepted
- Date: 2026-09-05
- Related: PRD Part A §4 "Phase 9", ADR-0005, ADR-0007, ADR-0043, ADR-0049, ADR-0050, ADR-0127, ADR-0132, ADR-0147, ADR-0149, ADR-0154, ADR-0156, ADR-0159, ADR-0160, ADR-0161, ADR-0164, ADR-0231, MEP-v1 §5.1
- Supersedes / amends: amends ADR-0050 (the bootstrap's artist surface gains `textures/sheets/`); retires the F5.4e grouping criterion (co-occurrence union-find over "adjacent ≥ 2 times") described in `HdPackBuilder.h`, replacing `BuildObjectSheets`' clustering while keeping its inert `# inferred … tileNearby` output contract
- Amended by: ADR-0231 (§4); ADR-0156 (§3 — the scene sheet loses the cells a screen owns) and ADR-0160 (CHR-order fragments move under `textures/chr/`); see also ADR-0159 (anchors chosen at save time)

## Decision

### 1. The unit is the game's grid, detected automatically

A **metatile** is a 2×2 tuple of background tile *shapes* (`HdTileKey` with `PaletteColors` wildcarded) at a fixed cell parity. For each phase `(x0,y0) ∈ {0,1}²` the builder counts the **distinct** 2×2 tuples the distinct stable screens produce; the phase with the fewest wins — a real grid repeats itself, an arbitrary cut does not:

```
phaseAdvantage = 1 - distinct(bestPhase) / mean(distinct(other three phases))
hasGrid        = phaseAdvantage >= kGridPhaseAdvantage (0.15)
```

Unit **16**, unless fewer than `kMinMetatilePlacements (64)` aligned placements (then **8**, each tile its own one-cell "metatile"). `hasGrid` selects the *parity*, not the unit. Automatic, never a user setting; unit, phase, `phaseAdvantage`, `hasGrid` go to the sidecar JSON and `mesen.log`. Zelda 1 `phaseAdvantage` 0.34 (`hasGrid` true, phase (0,0)); Excitebike 0.03 (`hasGrid` false); both unit 16.

*Amended 2026-09-05.* Accepted with `consistency(phase)` = placements whose 2×2 tuple occurs `>= 3` times / total, with a `>= 0.60` winner threshold. That saturates (0.930–0.994 Zelda, 0.954–1.000 Excitebike) and decided nothing, and the 8×8 alternative scores higher by construction. Compressivity (distinct tuples / placements) and neighbour determinism were measured and rejected; phase advantage separated the golden games by 13×. `kGridConsistencyThreshold` and the `gridConsistency` JSON field survive as diagnostics — they decide nothing.

### 2. Grouping is by mutual predictability, not by raw counts

Two adjacent metatiles A and B join an object only when, for a direction `d ∈ {E, S}` with `n = count(A d B)`:

```
n >= kSheetMinPairCount (3)
  and n / out_d(A) >= kSheetMinPairProb (0.80)
  and n / in_d(B)  >= kSheetMinPairProb (0.80)
```

B is the usual thing east of A **and** A the usual thing west of B. Sand next to everything fails; a 2×2 boss door passes. Surviving edges feed a DSU; components with `2..kSheetMaxObjectCells (32)` cells become objects, laid out by BFS at their dominant offsets. The same test over OAM entries that move together frame to frame gives sprite sheets (F9.5): denominator "every appearance of A", offsets within `kSpriteMaxOffset` (32 px) counted — it finds *figures*, not co-actors (ADR-0164 records separate statistics). F5.4e's `# inferred … tileNearby` candidates keep their contract exactly: inert definitions, never auto-attached to a `<tile>`, so a wrong grouping can never make a tile fail to render (ADR-0050's "nothing inferred breaks rendering" rule).

### 3. Sheets are for humans, and they are split by context

Under `<pack>/textures/sheets/`, every sheet is RGBA with a **transparent** background, one **1-cell gutter**, and **no baked labels** (labels live in the sidecar JSON). Files: `metatiles.png`/`.json` (scene vocabulary); `hud.png` (cells seen only in status-bar rows); `font.png` (HUD-region cells whose tiles use ≤ 2 colour indexes); `misc.png` (unaligned or *isolated* cells — the noise budget); `map-NNN.png` (one stitched region per connected map); `objNNN.png` (one object, ≥ 2 cells); `sprites.png` (the whole OAM vocabulary, most-seen first, singletons included — F9.16, added 2026-09-07; `kind` is `"sprites"`); `sprNNN.png` (one sprite group, F9.5). Cells laid out **most-seen first** (`count` descending, ties in vocabulary order). `sprNNN` only reaches a shape that holds a constant offset to another, so a lone projectile, a pickup or a shape-changing explosion had only its CHR-order fragment under `textures/chr/` (ADR-0160) before 2026-09-07. Each sheet gets its pixel-exact `*.orig.png` twin under F5.4d `_writeReferences`.

*Amended 2026-09-05,* on the rules real recordings broke:

- **`misc` is isolation, not rarity.** A cell is `misc` when unaligned, **or** when `count == 1` *and* no east/south neighbour is a scene cell with `count > 1` (the old `count == 1 → misc` put 89 % of Zelda's vocabulary in `misc.png`; Zelda 52.7 % → 3.2 %, Excitebike 28.8 % → 0 %).
- **A status-bar row is mostly frozen, not byte-identical.** Blank on every screen, or ≥ `kHudRowFrozenRatio (0.50)` of columns identical across screens *and* ≥ `kHudRowDrawnRatio (0.25)` drawn (the second keeps a sky band out).
- **A status bar needs a quorum, not unanimity.** A column is frozen when its **most common** shape covers `kHudRowScreenAgreement (0.60)` of the screens. Known false positive: a **frozen scenery band** (Excitebike's crowd), bounded to one cell that still reaches the stitched map.
- **A band is a status bar or nothing — never truncated** (*amended 2026-09-05*, issue #162). The old 6-row cap welded the rows it left into `map-000.png` per stitched screen; Zelda's bar is 8 rows / 64 px, yet the cap reported 6: its rupee counter and heart row were repeated at the top of every `map-000.png` screen, and `hud.png` got three quarters of a bar. Bound is now 8 rows per side; a deeper band is dropped whole, like the "nothing ever moves" case.

### 4. Sidecar JSON schema (version 1) and cell precedence

`cells[]` is the slicing contract `mep_build.py` reads back:

```json
{"version": 1, "kind": "metatiles", "gridUnit": 16, "gridPhase": { "x": 0, "y": 0 },
 "hasGrid": true, "phaseAdvantage": 0.3447,
 "gridConsistency": { "chosen": 0.9938, "alt8x8": 0.9994 },
 "cell": { "w": 16, "h": 16 }, "gutter": 1, "columns": 16,
 "sheet": "metatiles.png", "reference": "metatiles.orig.png",
 "cells": [ { "index": 0, "x": 1, "y": 1, "count": 431, "context": "scene", "label": "",
   "tiles": [ { "tile": "<32 hex chars>", "palette": "0F162A30" } ] } ]}
```

`x`/`y` are the cell's top-left in sheet pixels; `tiles[]` is row-major (length 4 at `gridUnit` 16, 1 at 8) and carries the exact `hires.txt` key of each 8×8 tile, so a crop maps back to tile entries with no guessing. `"label"` is always emitted and always empty from the builder — the artist's field, preserved by `mep_build.py`. `"gridConsistency"`'s `alt8x8` member being the larger of the two is expected, not a bug. A map adds `"mode": "screen" | "continuous"`, `"hudRows"`, and `placements[]` of `{ "x", "y", "cell" }`; an object or sprite adds `"cells[].metatile"` and `"evidence": { "dir": "E", "count": 7, "pAB": 0.9, "pBA": 0.86 }` per joined edge.

**Precedence when two sheets claim the same tile key.** A cell claims a tile key only when it was actually painted: its crop in `<sheet>.png` is diffed against the same cell of the 1x `*.orig.png` twin, upscaled by the sheet's scale factor N. Painted always beats untouched. Between two painted cells — and, when none painted, between the untouched ones — the static kind rank decides (`metatiles`/`sprites` < `misc` < `map` < `object`/`sprite` < `hud`/`font`, ties by sidecar file name, later wins). A sheet with no usable twin (`"reference": ""`, a missing file, or a twin not 1/N of the sheet) counts every cell painted. Overrides are logged `(painted)`, `(untouched)` or `(precedence)`. A static rank alone fails: `map > metatiles` breaks PRD Phase 9 validation test 3 ("make every bush purple"), `metatiles > map` breaks test 4. A **blank sprite key** (all 32 hex digits zero under a sprite palette key, first byte `FF` — the NES draws nothing) never claims by paint, even when its cell differs from its twin; among its untouched crops one whose cell nobody painted wins before the kind rank, and a background tile with the same data is excluded (its colour 0 is the backdrop, which the NES draws). *Amended 2026-09-24 by ADR-0231 (#447):* an untouched cell that wins a key re-emits the recording's own rule, pointing at the recorded pattern page, not its nearest-neighbour crop; only a painted cell points at its crop.

### 5. The analysis lives in host-free modules, the I/O stays in the builder

Following the ADR-0127 / ADR-0149-F8.3b precedent (`BorderLayout`), the inference is **not** written inside `HdPackBuilder.cpp`. New files under `Core/NES/HdPacks/`, none of which may include `pch.h`, `Emulator` or any console type: `TileSheetTypes.h` (PODs `SheetTileKey`, `GridFrame`, `MetatileKey`, `SheetCell`, `SheetImage`, `SheetPlacement`), `MetatileVocabulary.{h,cpp}`, `ScreenStitcher.{h,cpp}`, `SheetGrouping.{h,cpp}`, `SheetRender.{h,cpp}` — all link into `make core-unit-tests` and are in `Core.vcxproj` (ADR-0007 drift guard). `HdPackBuilder` only feeds them the recorded grid stream and writes the bytes they return (`PNGHelper::WritePNG`, `std::ofstream`). It retains, during recording, a de-duplicated stream of per-frame background grids: `GridFrame` = 30×32 `uint16_t` shape ids + the fine x-scroll, consecutive duplicates collapsed, capped at `kMaxSheetFrames = 4096` (≈ 8 MB). Inference runs once, in `SaveHdPack`, never per frame.

### 6. Stitching mode is chosen from the data, and a map is never a runtime layer

The screen-based stitcher returns `SheetPlacement[]` and runs first; when it places fewer than 2 screens **and** the continuous stitcher (accumulated per-frame x-shift) spans more than 512 px, the continuous result is emitted; a cut always starts a new `map-NNN.png`. Either way the map is a *paint surface*: nothing in `hires.txt` references it; `mep_build.py` slices it via `placements[]`; the runtime keeps using `<tile>`/`<background>` only. A map whose 1x canvas exceeds `kMaxMapPixels` (64 Mi pixels, `TileSheetTypes.h`) is skipped with a log line instead of being rendered and upscaled (a 16×8-screen overworld at scale 4 would hold ~500 MB live on the emu thread).

*Amended 2026-09-05 (F9.12).* "Cut on a match below 0.5" is replaced by a two-tier bar. Super Mario Bros.' title screen over world 1-1 agrees with the level's first screen over **0.700** at `dx == 0` (measured on the 21 stable screens under `auto/textures/backgrounds/screen*.orig.png`), while consecutive level screens agree over **0.996–0.999**; no cut fired, `cum` stayed 0, and `PaintFrame`'s first-writer-wins baked the logo, "ONE PLUMBER / TWO PLUMBERS" and "TOP- 000000" into the map's sky. Rule: a step that **claims a shift** (non-zero `dx` beating standing still by `kStitchStillMargin`) is cut below `kMinMatch` (0.5); a step that does not is cut when the *still* score at `dx == 0` falls below `kStitchWorldAgree` (**0.85**). A region narrower than 512 px is dropped. Scrolling steps are left alone, keeping Excitebike's continuous track in one piece.

### 7. The spike's grid dump stays, as a debug flag

`MESEN_SPIKE_GRID_DUMP` is renamed `MESEN_SHEET_GRID_DUMP` and moves **out of `OnFrameEnd`**: written once from the retained grid stream at `SaveHdPack` time, in the format `scripts/spike_tile_sheets.py` already parses. Threshold tuning is a human, per-game judgement; the hot path keeps no dump code.

## Context

The bootstrap's `auto/` pack is unreadable: `Chr_N.png` sheets are emitted in CHR order — thousands of 8×8 fragments with no neighbourhood. Next to a hand-made pack (the Zelda 1 reference `mep/`) the difference is not resolution, it is *subject*. F5.4e was meant to bridge that with spatial co-occurrence, but its criterion collapses any contiguous scene into one component, so `textures/sheets/object*.png` was **never emitted on a real game**. The 2026-09-04 spike (`scripts/spike_tile_sheets.py`, env-gated grid dump in `HdPackBuilder::OnFrameEnd`, evidence under `runs/spike-sheets/`) measured it: Zelda 1 puts 59/59 shapes in **one** component while 62 metatiles (bush, tree, rock, sand, forest edge) stitch 5 screens into one map; Excitebike 132/132 in one versus 48 objects, a 23 712 px continuous strip with ramps, largest 24 cells. So the unit an artist recognises is the game's own building block (16×16 on an attribute-aligned grid, 8×8 when there is none), the *figures* built from it, and the *map* they compose. Routing is urgent: with adjacency evidence required before stitching, 23 of the 30 recorded packs write no map at all, leaving `backgrounds/screenNNN.png` as their only whole-screen surface.

Non-goals: a tile-map editor; a game-specific level format; any change to `hires.txt` semantics (MEP `textures/` stays an envelope over HD Pack, ADR-0005); a new runtime construct — stitched maps are a paint surface; AI generation inside the emulator.

## Consequences

- The bootstrap gains a second artist surface next to ADR-0050's screens (what the game *showed* vs what it is *made of*). Vanilla-looking output stays in `auto/` (ADR-0049/ADR-0147), so community art is never masked; an installed accepted pack still wins.
- `BuildObjectSheets`' output changes shape (`object<NNN>.png` → `objNNN.png` + a sidecar); a pre-ADR pack keeps its old files until re-recorded — no migration, `auto/` is regenerable.
- Recording costs up to ~8 MB of retained grids (~11 MB with ADR-0159's palette plane) plus one save-time inference pass (`kMaxSheetFrames` bounds it), on the `SaveHdPack` caller's thread, so a GUI save stalls the UI; the headless bootstrap does not care.
- Grid detection can pick 8 on a mostly non-aligned recording, and a grid-less 8×8 game is still cut into 2×2 blocks at phase `(0,0)`. Both cost legibility, never rendering — every 8×8 key still reaches `hires.txt`.
- HUD detection is **rows only** (`hudRows`, `hudBottomRows`); a vertical side scoreboard spills into `metatiles.png`. `hud.png`/`font.png` come out empty on both golden games (`font` is HUD-region-scoped).
- `GridFrame` grows one `bool`; `scripts/sheet_report.py` (scene cell count, distinct screens, noise budget) and the builder's log line tell a bad recording from a bad inference.
- `mep_build.py` gains a round-trip it must keep pixel-exact (PRD test 6): the identity round-trip of untouched sheets must reproduce the captured screens under `scripts/headless_record`.
- Five host-free files mean five new entries in both build manifests; the ADR-0007 check fails loudly if only one side is updated.

## Record

- 2026-09-05 — accepted.
- 2026-09-05 — first measurements on real recordings contradicted the served grid criterion; §1 grid criterion, §3 `misc`/`hud` rules and §4 schema amended, §6 replaced by the two-tier bar (F9.12).
- 2026-09-05 — F9.9 screen residency (ADR-0156) and F9.10 CHR-order fragments under `textures/chr/` (ADR-0160) were split off into their own ADRs, shipped in `a2139da6`, legacy sweep in `2b12c5e2`.
- 2026-09-05 — a capture's anchors moved to save time and the grid stream gained a palette plane (ADR-0159, `--recolour` measurement: 190 of 4333 anchors).
- 2026-09-06 — code-review pass: `Core.vcxproj` opts the five host-free modules out of `pch.h`; §6 skips a map over `kMaxMapPixels`.
- 2026-09-07 — review of ADR-0164's premises: the `sprites.png` vocabulary sheet added (F9.16); `AccumulateCoOccurrence` kept as a reviewed hot-path cost.
- 2026-09-24 — ADR-0231 (#447): an untouched cell that wins a key re-emits the recording's own rule, not its crop (§4).
- 2026-09-25 — issue #464 (§4): a blank sprite key never claims its tile key by paint. Castlevania `usr017` places a blank tile and a spark in the same rect (1, 10); painting the spark showed 480 magenta pixels. The rule matches `mep_figure.py import`, which never writes paint onto a blank tile (#452). Implemented in `scripts/mep_build.py`; covered by `scripts/test_mep_build_blank_key.py`, logged in `docs/validation/issues/issue-464-blank-sprite-key-shared-crop-2026-09-25.md`.
