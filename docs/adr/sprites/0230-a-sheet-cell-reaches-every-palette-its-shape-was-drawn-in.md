# ADR-0230: A sheet cell reaches every palette its shape was drawn in

- Status: accepted 2026-09-24, decided as the hybrid in "Decision" below and
  implemented by F14.9 the same day (PRD Part A, Phase 14,
  `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` §3). User's decision and
  go-ahead, verbatim: *"aceito sua sugestão. pode aplicar e rodar em
  paralelo"*. Unit tests: `SheetColourways.h` and the recorder's host-free
  headers.
- Date: 2026-09-24
- Related: ADR-0229 (superseded by this ADR; its "Measured 2026-09-23" section
  is the evidence), ADR-0209 ("What (k) actually closed"; Q4 and the
  `unsorted` sheet), ADR-0183 (§2 the four surfaces, §3 evidence vs inference,
  §4 the round-trip acceptance test), ADR-0153 (the sheet sidecar,
  `cells[].tiles[].tile` / `.palette`), ADR-0154 §5 and ADR-0161 (recolouring
  a palette variant from one generation), ADR-0178 (mirrored sprites and the
  `source` key), ADR-0194 (the pattern pages are the kit's union), ADR-0210
  ("`defaultTile` is the wildcard"), ADR-0137 (the `HdPackBuilder.cpp` line
  ceiling), issue #415 (a condition built from the first-seen palette),
  `docs/validation/slices/f14.4-adr0229-option-i-measurement-2026-09-23.md` and
  `docs/validation/slices/f14.4-adr0230-palette-gap-measurement-2026-09-24.md`.
- Supersedes: ADR-0229

## Decision

1. **Colourways get their own cell (c).** For every `(shape, palette)` the
   recording drew whose palette is a colourway of the cell's palette
   (`artist_chr_kit`'s test: a painted entry changes hue, or the residual fails
   the 25° gate), the recorder emits a variant cell, rendered in that palette
   from the recorded art, beside its base cell on the same organized sheet, in
   the variant layout ADR-0183 §2 item 1 uses, as its own exact key. A sprite
   cycle grid keeps its phase order: variants follow their base phase, never
   interleave phases. The emission lives in the host-free `MesenSheets::`
   headers; `HdPackBuilder.cpp` only calls into it, and if it must grow past
   its ADR-0137 ceiling, that ceiling is amended explicitly, never silently.
2. **Folds ride on the cell (d, with Brightness).** A drawn palette that is a
   fold of the cell's palette (same picture, another brightness: every painted
   entry keeps its hue column) gets no cell. The cell's sidecar tile entry
   lists it and its brightness, e.g. `"folds": [{"palette": "0F122A30",
   "brightness": 0.75}]`; `mep_build` emits one exact `defaultTile=N` rule per
   listed fold at the cell's crop and that Brightness, above 1 when the fold is
   brighter than the cell, painted or not. Unpainted, each rule must render
   what the recording drew for its key, so the round trip holds; every key
   emitted is observed, so there is no wildcard. A fold qualifies only when
   its Brightness reproduces the recorded key exactly — the cell's crop scaled
   by that multiplier equals the crop the recording drew for the fold's
   palette. `fold_measure` is a least-squares fit and can leave a residual, so
   a fold admitted only by the 25° gate with a nonzero residual (the measured
   Zelda fade steps) becomes its own cell under item 1 instead (refined
   2026-09-24 on the #448 review; the counts move accordingly).
3. **The fold test measures against the cell.** `compute_folds`' drift test
   measures a step against the brightest step of its group, refusing 83 of
   Zelda's fade steps; it is measured against the cell's own palette instead,
   for both the pattern pages and the sheets, so the two surfaces agree on what
   a fold is.

## Context

ADR-0229 asked why the organized and `unsorted` sheets reach only about 18 % of
a recorded pack's keys; its "Measured 2026-09-23" prototype found the premise
does not hold. Most of the pack is the bootstrap's PRG-scan `defaultTile=Y`
export at the neutral palette (ADR-0043, ADR-0210): those tiles are reachable
on the CHR pattern pages (ADR-0194) and nowhere else, by design (ADR-0229
(iii)). The real gap is drawn keys whose shape is on a sheet under another
palette, and it is large on Zelda. A sheet cell carries one palette.
`HdPackBuilder::ShapeIdFor` interns a shape palette-wildcarded (`GetKey(true)`),
and the first exact `(tileData, palette)` it sees becomes the shape's drawable
art, `_shapeTiles[id]`. The sheets name that one key in their sidecar
(`cells[].tiles[].palette`, ADR-0153), and `mep_build` emits a `<tile>` only
for the sidecar's key, so a rebuilt `hires.txt` carries the sheet-reachable set
alone (532 of 3 209 keys on Castlevania). The shape's other palettes, which
`ProcessTile` recorded as their own keys (up to `MaxPaletteVariantsPerTile` =
32 per shape), live on the CHR pattern pages `SaveHdPack` spreads by usage
(`artist_chr_kit.py` pulls lower-ranked ones up as `borrowed`); the same
first-seen rule gave issue #415 its `spriteNearby` condition. A missing palette
is a fold (same picture, another brightness; `compute_folds` rebuilds it from
the `<tile>` row's Brightness column) or a colourway (a hue change, "two
pictures, not one", ADR-0183 §3). `sheet_repaint.py` already recolours
variants from one generation (ADR-0154 §5); the rejected sidecar-list option
would have used `"palettes": ["0F162A30", "0F122A30"]` and `mep_add_cell.py`.
Non-goals: no tile the recording never drew (ADR-0229 (iii)), no key the PPU
did not draw (ADR-0183 §3), no change to how a pose, scenery group or stage map
is found.

## Consequences

- Kits regenerated after F14.9 are not drop-in replacements for earlier painted
  kits: sheets gain variant cells and the sidecar gains `folds`, so painted
  work returns through the round trip, not a copy. A painted base cell also
  changes what its folds render — a fade step is the same picture.
- The sidecar schema change touches ADR-0153's cell shape, `mep_lint` and the
  docs' "What a cell is"; an old sidecar without `folds` stays valid.
- Under (a), every artist-facing doc would have to say a shape's other palettes
  are on the pattern pages — `docs/remastering-a-game.md` says it. Under (b) or
  (d), a painted cell changes what several keys render, so painting a red enemy
  also repaints its blue twin unless they split it off. The measurement ruled
  (b) out: under the layered load (`NesConsole` loads `textures/` over
  `auto/`) it serves none of the missing keys, and (d) "emits the same painted
  crop" renders a fade step at full brightness, so it must write each fold's
  Brightness, above 1 where the fold is brighter. (b) and (d) were prototyped
  in scratch copies of `mep_build.py`; `HdPackLoader` and `MergeLowerLayer`
  serving were measured in a scratch harness.

## Record

- 2026-09-24 — opened `proposed` on ADR-0229, verbatim
  *"Reenquadrar (Recommended)"*; F14.4 measured the gap the same day
  ("Measured 2026-09-24",
  `docs/validation/slices/f14.4-adr0230-palette-gap-measurement-2026-09-24.md`) on
  `main` `90703652`, Castlevania 60 s / Zelda 85 s
  (`scripts/stages/zelda/mint-stage1.txt` + `stage1-run.txt`).
- 2026-09-24 — accepted as the (c)+(d) hybrid; (b) rejected (0 missing keys
  under the layered load). F14.9 the same day
  (`docs/validation/slices/f14.9-adr0230-implementation-2026-09-24.md`): 100 %
  drawn-key coverage (Castlevania 628/628, Zelda 574/574, 0 unobserved); +67
  variant cells (61 colourway + 6 cells for residual folds) / +127 (45
  colourway + 82 cells for residual folds, 125 residual-fold keys); exact folds
  29 / 142; byte-identical second build; `HdPackBuilder.cpp` ceiling 2428 →
  2438 (ADR-0137, twelfth amendment). `sheets/`: (c) +96 cells (+30.5 KB) /
  +312 cells (+95.7 KB), (c) folded +61 cells / +76 cells, (d) +1.6 KB / +5.0
  KB.
- 2026-09-24 — item 2 refined on the #448 review: a residual fold becomes its
  own cell.
- #415 fixed on `main` since (`10176a10`); the first-seen palette still decides
  the cell's palette. F14.4 provenance: `MesenSheets::SpriteNearbyPalettes`.
