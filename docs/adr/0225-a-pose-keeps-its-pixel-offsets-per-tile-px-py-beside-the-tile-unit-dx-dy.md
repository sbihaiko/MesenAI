# ADR-0225: A pose keeps its pixel offsets — per-tile `px`/`py` beside the tile-unit `dx`/`dy`, and composed views place tiles at pixel precision

- Status: **accepted 2026-09-23** — implemented by PRD Part A slice
  **F12.18** (`docs/roadmap/PRD-mesence-enhancement-ecosystem.md`,
  Phase 12), **in progress, not yet on `main`** at the time of writing.
  User's pick, verbatim: *"px/py por tile"*. Implementation go-ahead quoted
  verbatim (2026-09-23): *"pode implementar as duas ADRs em paralelo"* — the
  same-turn rule of CLAUDE.md applies: the change ships with unit tests
  covering §1–§3 and the go-ahead is quoted in the PR body.
- Date: 2026-09-23
- Related: ADR-0170 (the pose sidecar; §1 rounds offsets with `ToCells`),
  ADR-0153 (§3 sheets are 8 px cells with a 1-cell gutter), ADR-0179 (cycles
  and sequences — the rows a composed view lays out), ADR-0183 (§2 the
  Figures surface, §4 round-trip is the acceptance test), ADR-0209 (Q2
  option (e), a figure "reassembled through its offsets"; Q2/Q3 shipped as
  `scripts/mep_figure.py`), ADR-0165/ADR-0166 (the external stdlib
  composition toolchain and its adjacency records), ADR-0168 (`evidence[]`,
  the walk in tile units), `docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md`
  §1 (the measurement)
- Supersedes / amends: amends ADR-0170 §1 — a `tiles[]` entry gains optional
  `px`/`py` (and `z` where tiles overlap); `unit`, `dx`, `dy`, `size` and
  the sets-equal identity are untouched. Refines ADR-0183 §2 item 1 (how a
  figure's cells are placed) and ADR-0209 Q2 (e) (what "reassembled" means
  for a pose). Does not amend ADR-0153 §3: sheets keep their cells and
  gutter.

## Context

A NES metasprite is not on an 8 px grid. The recorder's own OAM stream says
so for Contra's player: in every phase of the run cycle the torso sits 2–4 px
to the right of the legs, the legs start at y 14 (not 16) and overlap the
torso's bottom row by 2 px, and the foot is at x 7 or 11
(`docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md` §1).

`poses.json` cannot say this. ADR-0170 §1 expresses a pose as
`(node, dx, dy)` in 8 px units, and `SpriteGrouping::ToCells` rounds the
pixel offset to the nearest cell: `(px + 4) / 8`. A +4 px torso lands a
whole cell (8 px) to the right, a +3 px torso lands at 0, y 14 becomes 16.
Two phases of one run therefore come out with the torso displaced in
opposite directions, and every consumer that lays a pose out —
`mep_figure.py export`, the kit's figure grids (`scripts/artist_kit.py`
`placements`), the composition editor — reproduces the error. The kit adds
ADR-0153's 1-cell gutter *inside* the figure on top of it, so the limbs
detach and the artist sees fragments rather than a character. The panel
image behind the measurement shows all three states side by side: real OAM,
8 px grid, 8 px grid plus gutter.

No accepted ADR states what happens to a sub-tile offset. ADR-0170 names
`ToCells` as the rule and stops; ADR-0183 §2 asks for "cells aligned on a
common baseline"; ADR-0209 Q2 (e) says a figure is "reassembled through its
`evidence[]` offsets", which are tile units too (ADR-0168). The gap is a
format gap, not a bug: the file has no field for the information.

Non-goals: changing how a pose is found, identified or counted (ADR-0170 §1
sets-equal identity on `(node, dx, dy)` stays — see Consequences for why);
changing the sheets (`sprites.png`, `sprNNN.png`, `unsorted`, ADR-0153 §3
and ADR-0209 Q4), their cell geometry or their gutter; changing
`adjacency.json` `evidence[]`/`offsets[]` (ADR-0164/ADR-0168, still tile
units); anything at run time or in `hires.txt`; a migration of packs
recorded before this ADR.

## Decision

### 1. `tiles[]` gains `px`/`py`, pixel offsets from the pose's origin

Every `poses[].tiles[]` entry written by the recorder gains two integers,
`px` and `py`: the tile's top-left in **native pixels** relative to the
pose's origin — the cluster's minimum x and minimum y, the same origin
`dx`/`dy` are rounded from — so `px, py >= 0` and, by construction,
`dx == ToCells(px)` and `dy == ToCells(py)`. `dx`/`dy` stay, written exactly
as today, for every current reader. `unit` stays `8`.

```json
{ "id": "pose000", "frames": 802, "hold": 761, "size": [3, 6],
  "tiles": [
    {"node": 16, "dx": 0, "dy": 2, "px": 0,  "py": 14},
    {"node": 14, "dx": 1, "dy": 0, "px": 4,  "py": 0},
    {"node": 2,  "dx": 2, "dy": 0, "px": 12, "py": 0}
  ] }
```

**Which occurrence supplies the pixels.** Identity is by the rounded set, so
one pose can be seen with slightly different pixel layouts (a +5 and a +6
torso both round to `dx: 1`). The recorder writes the **most-seen pixel
layout** of the pose — the `(px, py)` vector, over all its tiles, that
occurred in the most retained frames (`RepeatCount` counted, as ADR-0170 §1
counts frames); ties go to the lexicographically smallest vector. One
occurrence supplies all tiles, never a per-tile mode, so the written
silhouette is one the emulator actually drew.

**Overlap and `z`.** Tiles may overlap at pixel precision (Contra's legs
over the torso's last two rows). When any two tiles of the written layout
overlap, every tile of that pose also gets `z`: its front-to-back rank in
the supplying frame's OAM order (0 = frontmost, i.e. the lowest OAM index).
The supplying frame is the **earliest** retained
frame whose layout equals the written `(px, py)` vector: several frames can
share the vector with a different OAM order (a game that rotates sprite
priority), and the mode alone would not pick one, so `z` would be
unspecified. The earliest frame makes it deterministic and keeps the ranks
ones the emulator actually drew; OAM order is deliberately not part of the
modal key, so a priority rotation cannot split one layout's count. `z` is
omitted when nothing overlaps; a reader that finds no `z` treats the
tiles as non-overlapping. `z` is the one field beyond the user's pick, and
it exists only because §3's import needs an owner for a shared pixel.

**Reader fallback.** `px`/`py` are optional on read. A sidecar without them
(every pack recorded before this ADR) reads as `px = dx * unit`,
`py = dy * unit`, which is exactly today's layout — so every consumer has one
code path, and an old pack renders as it does now. `compose_engine.Pose`
exposes the pixel offsets beside `tiles` (cell units, unchanged); nothing in
`evidence[]` (ADR-0168) changes.

The Core side: `PoseTile` gains `Px`/`Py` (and the rank), **excluded** from
the ordering and equality `BuildPoses` uses to sort, dedupe and merge —
those stay on `(Node, Dx, Dy)`, or two frames of one pose would stop
merging. Host-free in `SpriteGrouping` and covered by `core_unit_tests`
(ADR-0126/ADR-0127): a +4 px offset writes `dx: 1, px: 4`; two frames whose
layouts differ by 1 px are one pose and the most-seen layout wins; an
overlapping pair gets `z`, a non-overlapping pose does not.

### 2. Sheets stay on the cell grid; composed views place at pixel precision

Nothing changes on a **sheet**: `sprites.png`, `sprNNN.png`, the `unsorted`
remainder and every `*.json` sidecar keep 8 px cells, ADR-0153 §3's 1-cell
gutter and their cell coordinates. `mep_build.py` reads sheets per cell and
is unaffected; the ADR-0183 §4 round-trip of a painted sheet holds without
a change.

A **composed view** — anything that draws a pose as one figure — places each
tile at `(px, py) * scale` from the pose origin, with **no gutter inside the
figure**:

- `scripts/mep_figure.py export` (ADR-0209 Q2): the figure PNG, its
  `.orig.png` twin and the `.ora` layers are composed at pixel offsets; the
  `.json` sidecar's per-cell `x`/`y` become pixel offsets (scaled) and it
  records the `z` order when present. The 1x canvas is the pose's pixel
  extent, not `size * unit`.
- the kit's Figures surface (ADR-0183 §2 item 1, `scripts/artist_kit.py`):
  every phase of a cycle row is drawn at pixel precision inside its box; the
  one-cell margin **between** figures and the row baseline stay.
- the composition editor's pose rendering and pose picker (ADR-0165,
  ADR-0179 §5): same rule; `Overflow` already speaks native pixels.

Between figures, and on every sheet, the gutter is what ADR-0153 decided.
Inside a figure there is none — the gutter exists so a brush cannot bleed
between *unrelated* cells, and the cells of one figure are exactly the ones
the artist wants to paint across.

### 3. Import of a composed figure — pixels return to the cell that owned them

`mep_figure.py import` (ADR-0209 Q3, option (i)) reads each cell back from
its pixel rectangle. Where two rectangles overlap, a pixel goes to the
**frontmost cell (lowest `z`) whose original pixel is opaque there**; if
every overlapping cell is transparent at that pixel, it goes to the
frontmost. The other cells keep their `orig` pixel at that position — the
artist could not have painted a pixel that was hidden. This is what keeps
ADR-0183 §4 mechanical for figures: the rebuilt `hires.txt` has the same
keys before and after, and a painted figure whose overlap was handled this
way re-renders in the game as the artist saw it in the export, up to the
overlapped pixels of the hidden cell.

### 4. Round-trip constraint, stated

Sheets and `mep_build` are untouched; `px`/`py`/`z` are additive and
optional; the fallback makes an old sidecar read as today. A pack recorded
after this ADR still validates against every current reader, because
`dx`/`dy`/`size`/`unit` are written unchanged. The acceptance is ADR-0183
§4's: export the run cycle's poses, rebuild, 0 errors, key set unchanged —
plus the new measurement, the composed figure matches the recording's OAM
frame pixel for pixel.

## Rejected options

- **Drop the gutter only.** Removes the visible tearing in the kit but leaves
  the 3–4 px rounding error, which is the larger half of the problem: the
  torso is still a half tile off against the legs.
- **`unit: 1`** (write `dx`/`dy` in pixels). One field, no new keys — and it
  silently breaks every reader that multiplies by 8 or assumes cells:
  `compose_engine.Pose.tiles`, `pose_band_members`, `mep_figure.py`'s
  `home_cell` join, the kit's `placements`, `evidence[]` consumers, and the
  ADR-0170 §1 identity itself (two frames 1 px apart would become two
  poses). A pack written with `unit: 1` would be read wrong by every
  existing tool without an error.
- **Round differently** (floor instead of nearest). Moves the error, does
  not remove it; still cannot say y 14.

## Consequences

- Two representations of one offset in one file. They are written from the
  same frame in the same pass and `dx == ToCells(px)` is a checkable
  invariant; a unit test and `mep_lint.py` (a warning, not an error — old
  packs have no `px`) hold it. A consumer must not mix a pose's `px` with an
  `evidence[]` `dx`; that was already true of `tiles[]` vs `offsets[]`
  (ADR-0170 Consequences).
- Identity stays on the rounded set on purpose. Making it pixel-exact would
  split a run phase into several poses whenever the game jitters a limb by a
  pixel, and ADR-0170's 2026-09-15 amendment already declined to change
  identity without a false-merge measurement. The cost is that the written
  pixels are the mode, not the only layout seen; the file says which by
  construction, not by a count (adding an occurrence count is a later
  decision if anyone needs it).
- Overlapping tiles make a composed figure lossy for the hidden pixels of
  the back cell — §3 keeps `orig` there. On Contra that is a 2-row strip
  under the torso. An artist who needs those pixels paints the sheet cell,
  which is why the sheets stay the source of truth.
- Only newly recorded packs carry `px`/`py`; the Contra kit must be
  regenerated from a fresh recording to show the change (the slice does it,
  together with ADR-0226's re-record). Recorded-before packs keep today's
  layout through the fallback — a legitimate input forever, as ADR-0170
  says of the sidecar itself.
- The figure export's `.json` changes shape (`x`/`y` in pixels, `z`); its
  `version` is bumped, and `import` refuses a sidecar it does not know.
