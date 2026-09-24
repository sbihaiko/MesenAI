# ADR-0230: A sheet cell reaches every palette its shape was drawn in

- Status: **proposed 2026-09-24**. Opened by the user's decision of
  2026-09-24 on ADR-0229, answering "What do we do with ADR-0229?" with the
  option, verbatim, *"Reenquadrar (Recommended)"*: close ADR-0229 as its
  option (iii) and open this ADR, `proposed`, for the gap the ADR-0229
  measurement actually found, with options and a measurement before
  deciding. Nothing is implemented. The measurement slice is PRD Part A
  **F14.4** (`docs/roadmap/PRD-mesence-enhancement-ecosystem.md`, Phase 14),
  not started; no implementing slice exists until this is accepted.
- Date: 2026-09-24
- Related: ADR-0229 (superseded by this ADR; its "Measured 2026-09-23"
  section is the evidence), ADR-0209 ("What (k) actually closed"; Q4 and
  the `unsorted` sheet), ADR-0183 (§2 the four surfaces, §3 evidence vs
  inference, §4 the round-trip acceptance test), ADR-0153 (the sheet
  sidecar, `cells[].tiles[].tile` / `.palette`), ADR-0154 §5 and ADR-0161
  (recolouring a palette variant from one generation), ADR-0178 (mirrored
  sprites and the `source` key), ADR-0194 (the pattern pages are the kit's
  union), ADR-0210 ("`defaultTile` is the wildcard"), ADR-0137 (the
  `HdPackBuilder.cpp` line ceiling), issue #415 (a condition built from the
  first-seen palette),
  `docs/validation/f14.4-adr0229-option-i-measurement-2026-09-23.md`
- Supersedes / amends: none. It takes over the open question ADR-0229 was
  opened for, reframed by that ADR's measurement.

## Context

ADR-0229 asked why the organised and `unsorted` sheets reach only about
18 % of a recorded pack's keys, and proposed registering every shape the PPU
draws. Its measurement on a prototype
(`docs/validation/f14.4-adr0229-option-i-measurement-2026-09-23.md`) found
that the premise does not hold:

| | Castlevania (60 s) | Zelda (85 s) |
|---|---|---|
| distinct shapes in `hires.txt` / of which drawn | 2 673 / 532 | 1 620 / 260 |
| `<tile>` keys / of which drawn | 3 209 / 628 | 2 153 / 574 |
| coverage of drawn shapes by a sheet cell | 100 % | 100 % |
| coverage of drawn keys by a sheet cell | 84.7 % (96 missing) | 45.6 % (312 missing) |
| coverage of all keys (ADR-0229's ratio, shapes) | 19.9 % | 16.0 % |

Two different things made up the "gap":

1. **Tiles the recording never drew.** Most of the pack is the bootstrap's
   PRG-scan `defaultTile=Y` export at the neutral palette (ADR-0043,
   ADR-0210). Those tiles are reachable on the CHR pattern pages (ADR-0194)
   and nowhere else, by design. ADR-0229 closed as its option (iii), which
   documents exactly that.
2. **Drawn keys whose shape is on a sheet under another palette.** This is
   the real gap, and it is large on Zelda. A sheet cell carries one palette.
   `HdPackBuilder::ShapeIdFor` interns a shape palette-wildcarded
   (`GetKey(true)`), and the first exact variant it sees becomes the shape's
   drawable art, `_shapeTiles[id]`. Every sheet the recorder emits renders
   and names that one `(tileData, palette)` in its sidecar
   (`cells[].tiles[].palette`, ADR-0153). `mep_build` emits a `<tile>` for the
   sidecar's key and nothing else, so a rebuilt pack carries exactly the
   sheet-reachable set (532 of 3 209 keys on Castlevania). The other palettes
   of the same shape, which `ProcessTile` recorded as their own keys (up to
   `MaxPaletteVariantsPerTile` = 32 per shape), exist on the recorded CHR
   pattern pages, where `SaveHdPack` spreads them across pages by usage
   (`artist_chr_kit.py` pulls lower-ranked ones up as `borrowed`). An artist
   can paint them there, but only as an unordered page, away from the
   figure, element or map the other palette sits in.

The same first-seen rule also produced issue #415, a `spriteNearby` condition
named after a background palette.

A missing palette is not always a new picture. `artist_chr_kit.py` already
tells two cases apart on the pattern pages. A **fold** is the same picture at
another brightness, typically a step of a screen fade, and the renderer can
rebuild it from the `<tile>` row's Brightness column (`compute_folds`). A
**colourway** changes a painted entry's hue, like a red and a blue enemy.
Colourways are "two pictures, not one" (ADR-0183 §3): an artist may want
different paint on each, and a recolour is not the same art. How the 96 and
312 missing keys split between the two cases has not been measured, and it
is what most separates the options below.

Non-goals: this ADR does not reach tiles the recording never drew (those stay
on the pattern pages, ADR-0229 (iii)). It does not add a key the PPU did not
draw (ADR-0183 §3). It does not change how a pose, a scenery group or a stage
map is found.

## Options

**(a) Leave it.** Document that a shape drawn in several palettes has one
cell on the organised sheets, and that its other palettes are on the CHR
pattern pages. No code. The artist paints each extra palette out of context
on a page, and on Zelda that is more than half of the drawn keys (54.4 %).

**(b) Emit the painted cell as `defaultTile=Y`.** `mep_build` writes the
sheet crop's rule with the wildcard flag when its shape was drawn in other
palettes that no cell owns. `HdPackLoader` then registers the rule under the
exact key and under `GetKey(true)`, and the lookup falls back to it for every
other palette (ADR-0210). No sheet or sidecar change. Costs:

- one painting serves every palette. HD replacement art is full colour, so a
  red and a blue enemy render identically. That is right for a fold and wrong
  for a colourway;
- it also serves palettes the recording never saw, which ADR-0183 §3 forbids
  for a rule that changes what a rebuilt pack renders;
- the bootstrap's neutral-palette `Y` rule for the same shape registers the
  same wildcard key, so which one wins is load order. That has to be
  measured, not assumed;
- ADR-0183 §4 compares exact key sets. A key served through the wildcard is
  not in the rebuilt set, so the round trip would need a definition that
  credits wildcard service;
- an unpainted kit must still rebuild to what it rendered before. The flag can
  only be set on an edited crop, or an untouched cell recolours every other
  palette of its shape.

**(c) One cell per drawn palette.** The recorder emits a cell for every
`(shape, palette)` the recording drew, not one per shape. A variant cell
sits beside its base cell on the same organised sheet (the layout ADR-0183
§2 item 1 already uses for a figure's variants), rendered in its own palette.
Each cell is its own exact key, so a colourway can be painted differently.
The tooling for painting one and deriving the rest already exists:
`sheet_repaint.py` groups a sheet's cells by shape and recolours the
variants from one generation (ADR-0154 §5, correspondence read positionally
per ADR-0161), and folds can be collapsed the way the pattern pages collapse
them. Costs:

- the sheets grow by at most the missing keys, +96 cells on Castlevania and
  +312 on Zelda, fewer after folding;
- every organised sheet's layout changes, so every kit regenerated afterwards
  differs from its predecessor and painted sheets must be re-imported
  through the round trip, not copied;
- the change lives in the recorder's sheet emission (the host-free
  `MesenSheets::` headers and `HdPackBuilder.cpp`, which has one line of
  headroom under its ADR-0137 ceiling);
- a sprite sheet's cycle grid, or a map, that gains variant cells has to keep
  its phase order and positions readable.

**(d) A palette list on the cell.** The sidecar's tile entry gains an
optional list of the other palettes the recording drew the shape in, for
example `"palettes": ["0F162A30", "0F122A30"]`. `mep_build` emits the same
painted crop under each listed palette as an exact `N` rule. When the artist
wants one palette painted differently, they split it into its own cell
(`mep_add_cell.py`). Every emitted key is an observed one, so there is no
wildcard, no collision with the bootstrap's rule and no change to ADR-0183
§4's exact comparison. Costs:

- a sidecar schema change (ADR-0153's cell shape, `mep_lint`, `mep_build`,
  the docs' "What a cell is");
- the default is still one painting for every listed palette, which is the
  colourway problem of (b), only confined to observed palettes;
- as in (b), the list may only be applied to an edited crop. For an untouched
  cell, `mep_build` has to keep each listed key's own recorded pixels;
- the sheet itself does not show the other colourways, so the artist learns
  about them from the sidecar or a legend.

(b) and (d) could also apply only to folds, with colourways left to (a) or
(c). The measurement below says whether that split is worth having.

## What decides it

A measurement, on the two runs ADR-0229 measured (Castlevania 60 s idle;
Zelda 85 s from `scripts/stages/zelda/mint-stage1.txt` + `stage1-run.txt`),
on one binary with dylib provenance proven before recording:

1. **Split the missing drawn keys** (96 on Castlevania, 312 on Zelda) into
   folds and colourways, using `compute_folds`' criterion. If they are almost
   all folds, (b) or (d) restricted to folds is enough. If colourways are a
   real share, (c) is the only option that lets the artist paint them apart.
2. **Drawn-key coverage** under a prototype of each option still standing
   after step 1, against today's 84.7 % and 45.6 %. The target is 100 % of
   drawn keys, with no unobserved key added.
3. **Sheet size**: cells and bytes of `sheets/` before and after, per option.
4. **Round trip** (ADR-0183 §4): `mep_build` reports 0 errors, a second build
   is byte-identical, and an unpainted kit rebuilds to the same rendered
   pack. Under (b), also which wildcard rule wins against the bootstrap's.

The decision is written here, by hand, after those numbers.

## Decision

Not decided.

## Consequences

- Under (a), every artist-facing doc has to say that a shape's other palettes
  are on the pattern pages. `docs/remastering-a-game.md` says it already.
- Under (b) or (d), a painted cell changes what several keys render, so an
  artist who paints a red enemy also repaints its blue twin unless they split
  it off.
- Under (c), kits regenerated after the change are not drop-in replacements
  for painted kits from before. The first-seen palette stops deciding what the
  artist sees, which is also the mechanism behind issue #415.
