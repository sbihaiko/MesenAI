# ADR-0225: A pose keeps its pixel offsets — per-tile `px`/`py` beside the tile-unit `dx`/`dy`, and composed views place tiles at pixel precision

- Status: accepted (2026-09-23)
- Date: 2026-09-23
- Related: ADR-0170 (the pose sidecar; §1 rounds with `ToCells`), ADR-0153 (§3 sheets are 8 px cells with a 1-cell gutter), ADR-0179, ADR-0183 (§2 the Figures surface, §4 the round-trip), ADR-0209 (Q2 option (e), a figure "reassembled through its offsets"; Q2/Q3 shipped as `scripts/mep_figure.py`), ADR-0165/ADR-0166, ADR-0168 (`evidence[]`, tile units), ADR-0164, `docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md` §1
- Amends ADR-0170 §1: a `tiles[]` entry gains optional `px`/`py` (and `z` where tiles overlap); `unit`, `dx`, `dy`, `size` and the sets-equal identity are untouched.

## Context

A NES metasprite is not on an 8 px grid. Contra's player proves it: in every phase of the run cycle the torso sits 2–4 px to the right of the legs, the legs start at y 14 (not 16) and overlap the torso's bottom row by 2 px, and the foot is at x 7 or 11 (`docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md` §1).

`poses.json` cannot say this. ADR-0170 §1 expresses a pose as `(node, dx, dy)` in 8 px units, and `SpriteGrouping::ToCells` rounds with `(px + 4) / 8`: a +4 px torso lands a whole cell (8 px) to the right, a +3 px torso lands at 0, y 14 becomes 16. Every consumer that lays a pose out — `mep_figure.py export`, the kit's figure grids (`scripts/artist_kit.py` `placements`), the composition editor — reproduces the error, and the kit's ADR-0153 1-cell gutter inside the figure detaches the limbs. No accepted ADR states what happens to a sub-tile offset: ADR-0183 §2 asks for "cells aligned on a common baseline"; ADR-0209 Q2 (e) says a figure is "reassembled through its `evidence[]` offsets" (ADR-0168, tile units). The gap is a format gap, not a bug.

Non-goals: changing how a pose is found, identified or counted (ADR-0170 §1 sets-equal identity on `(node, dx, dy)` stays); changing the sheets (`sprites.png`, `sprNNN.png`, `unsorted`, ADR-0153 §3, ADR-0209 Q4), their geometry or gutter; `adjacency.json` `evidence[]`/`offsets[]` (ADR-0164/ADR-0168, tile units); anything at run time or in `hires.txt`; migrating packs recorded before this ADR.

## Decision

### 1. `tiles[]` gains `px`/`py`, pixel offsets from the pose's origin

Every `poses[].tiles[]` entry gains `px` and `py`: the tile's top-left in **native pixels** from the pose's origin — the cluster's min x/min y, the origin `dx`/`dy` round from — so `px, py >= 0` and, by construction, `dx == ToCells(px)` and `dy == ToCells(py)`. `dx`/`dy` stay, written as today, for every current reader; `unit` stays `8`.

```json
{ "id": "pose000", "frames": 802, "hold": 761, "size": [3, 6],
  "tiles": [
    {"node": 16, "dx": 0, "dy": 2, "px": 0,  "py": 14},
    {"node": 14, "dx": 1, "dy": 0, "px": 4,  "py": 0},
    {"node": 2,  "dx": 2, "dy": 0, "px": 12, "py": 0}
  ] }
```

**Which occurrence supplies the pixels.** Identity is by the rounded set, so one pose can be seen with slightly different layouts (a +5 and a +6 torso both round to `dx: 1`). The recorder writes the most-seen `(px, py)` vector over all tiles in the most retained frames (`RepeatCount` counted, as ADR-0170 §1 counts frames), ties lexicographically smallest; one occurrence supplies all tiles, never a per-tile mode, so the written silhouette is one the emulator actually drew.

**Overlap and `z`.** When two tiles of the written layout overlap, every tile also gets `z`: its front-to-back rank in the earliest retained frame equal to the written `(px, py)` vector (0 = frontmost, the lowest OAM index). OAM order is not in the modal key, so a priority rotation cannot split a layout's count. `z` is omitted when nothing overlaps; a reader with no `z` treats tiles as non-overlapping. It exists only so §3's import can own a shared pixel.

**Reader fallback.** `px`/`py` are optional on read: a sidecar without them reads `px = dx * unit`, `py = dy * unit`, exactly today's layout. `compose_engine.Pose` exposes the pixel offsets beside `tiles` (cell units, unchanged); `evidence[]` (ADR-0168), `adjacency.json` and `offsets[]` (ADR-0164) stay tile units.

Core: `PoseTile` gains `Px`/`Py` (and the rank), excluded from the ordering and equality `BuildPoses` uses — those stay on `(Node, Dx, Dy)`, or two frames of one pose stop merging; `poses[]` on a `sprNNN` sidecar (ADR-0174) is unchanged. Host-free in `SpriteGrouping`; covered by `core_unit_tests` (ADR-0126/ADR-0127): a +4 px offset writes `dx: 1, px: 4`; Contra's legs at y 14 write `dy: 2, py: 14`; two frames 1 px apart are one pose; an overlapping pair gets `z`, a non-overlapping pose does not.

### 2. Sheets stay on the cell grid; composed views place at pixel precision

`sprites.png`, `sprNNN.png`, the `unsorted` remainder and every `*.json` sidecar keep 8 px cells, ADR-0153 §3's 1-cell gutter and their coordinates; `mep_build.py` reads sheets per cell and is unaffected, and the ADR-0183 §4 round-trip holds. A **composed view** (a pose "reassembled through its offsets", ADR-0209 Q2) places each tile at `(px, py) * scale`, with **no gutter inside the figure**:

- `scripts/mep_figure.py export` / `mep_figure.py export` (ADR-0209 Q2): the figure PNG, its `.orig.png` twin and the `.ora` layers at pixel offsets; the `.json` per-cell `x`/`y` become pixel offsets (scaled) and it records `z`; the 1x canvas is the pose's pixel extent, not `size * unit`.
- the kit's Figures surface (ADR-0183 §2 item 1, `scripts/artist_kit.py`): every phase of a cycle row at pixel precision; the one-cell margin between figures and the row baseline stay.
- the composition editor (ADR-0165, ADR-0179 §5); `Overflow` already speaks native pixels.

### 3. Import — pixels return to the cell that owned them

`mep_figure.py import` (ADR-0209 Q3, option (i)) reads each cell back from its pixel rectangle; on overlap the pixel goes to the **frontmost cell (lowest `z`) whose original pixel is opaque there**, else the frontmost, and the other cells keep their `orig` pixel. ADR-0183 §4 stays mechanical: the rebuilt `hires.txt` has the same keys.

### 4. Round-trip constraint, stated

`px`/`py`/`z` are additive and optional; `dx`/`dy`/`size`/`unit` are written unchanged, so a new pack validates against every current reader and an old sidecar reads as today. Acceptance is ADR-0183 §4 (export, rebuild, 0 errors, key set unchanged) plus: the composed figure matches the OAM frame pixel for pixel.

## Rejected options

- **Drop the gutter only** — leaves the 3–4 px rounding error.
- **`unit: 1`** — silently breaks every reader that multiplies by 8 or assumes cells: `compose_engine.Pose.tiles`, `pose_band_members`, `mep_figure.py`'s `home_cell` join, the kit's `placements`, `evidence[]` consumers, and the ADR-0170 §1 identity.
- **Round differently** (floor) — moves the error, does not remove it.

## Consequences

- Two representations of one offset: `dx == ToCells(px)` is checkable (unit test; `mep_lint.py` a warning, not an error — old packs have no `px`). Do not mix a pose's `px` with an `evidence[]` `dx`.
- Identity stays on the rounded set: pixel-exact identity would split a run phase on a jittered limb; the written pixels are the mode, not the only layout seen.
- Overlapping tiles make a composed figure lossy for the back cell's hidden pixels; §3 keeps `orig`, and the artist paints the sheet cell — sheets stay the source of truth.
- Only a re-record carries `px`/`py`; older packs read via the fallback. The export `.json` changes shape (`x`/`y` in pixels, `z`), `version` bumps, and `import` refuses an unknown sidecar.

## Record

- 2026-09-23 — accepted. Pick, verbatim: *"px/py por tile"*. Go-ahead, verbatim: *"pode implementar as duas ADRs em paralelo"*. Shipped as PRD Part A slice **F12.18** (`docs/roadmap/PRD-mesence-enhancement-ecosystem.md`, Phase 12).
- 2026-09-24 — condition 3 met after #400; the kit was regenerated from a Contra re-record on `main` @ `89acdc10`, and captions and `playsColumns` state "plays columns 1 2 3 1 4 5". The same recording serves ADR-0226's linker. Evidence: `docs/validation/f1219-contra-kit-coldread-2026-09-23.md`, `docs/validation/f1219-contra-kit-coldread-rerun-2026-09-24.md`, `docs/validation/f1218-pose-pixel-offsets-2026-09-23.md`, `docs/validation/f1218-f1219-contra-rerecord-2026-09-23.md`, `docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md`.
