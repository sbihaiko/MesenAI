# ADR-0230: A sheet cell reaches every palette its shape was drawn in

- Status: **accepted 2026-09-24**, decided as the hybrid in "Decision"
  below and pending implementation as PRD Part A slice **F14.9**
  (`docs/roadmap/PRD-mesence-enhancement-ecosystem.md`, Phase 14). User's
  decision and go-ahead, verbatim: *"aceito sua sugestão. pode aplicar e
  rodar em paralelo"*. The same-turn implementation must ship with unit
  tests covering the decision, and this quote goes in its PR body.
  History: opened `proposed` by the user's decision on ADR-0229, verbatim
  *"Reenquadrar (Recommended)"*. The measurement slice F14.4 was delivered
  2026-09-24 ("Measured 2026-09-24" below,
  `docs/validation/f14.4-adr0230-palette-gap-measurement-2026-09-24.md`).
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
  `docs/validation/f14.4-adr0229-option-i-measurement-2026-09-23.md`,
  `docs/validation/f14.4-adr0230-palette-gap-measurement-2026-09-24.md`
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

### Measured 2026-09-24 (not a decision)

Slice F14.4, on `main` `90703652`, with dylib provenance proven (the
binary carries #415's `MesenSheets::SpriteNearbyPalettes`). Both runs were
deterministic across two passes and reproduce ADR-0229's baseline exactly.
Log: `docs/validation/f14.4-adr0230-palette-gap-measurement-2026-09-24.md`.

**Step 1, folds vs colourways.** Pairwise means against the palette the
sheet cell carries, with `artist_chr_kit`'s own predicates and its 25° gate.

| | Castlevania (96) | Zelda (312) |
|---|---|---|
| pairwise: inert / fade / brighter than the cell / **colourway** | 29 / 0 / 6 / **61** | 133 / 94 / 40 / **45** |
| `compute_folds` grouping: folds onto the cell / **own picture** | 35 / **61** | 184 / **128** |
| colourways that are sprites | 58 of 61 | 20 of 45 |
| shapes affected; max drawn palettes per shape | 53; 13 | 124; 17 |

Castlevania's gap is sprite colourways. Zelda's is mostly a 4-step screen
fade: 79 shapes miss 3 palettes each. The two criteria differ on Zelda
because `compute_folds` measures a step's drift against the brightest step,
not against the cell. Colourways are a real share on both games, so a
fold-only (b) or (d) is not enough on its own.

**Steps 2–4.**

- (b) and (d) were prototyped in scratch copies of `mep_build.py`.
- (c) was *simulated* in Python: variant cells appended to the owner sheet,
  holding the recording's own art. The recorder was not changed.
- (b) serving was measured with the Core's `HdPackLoader` and
  `MergeLowerLayer` in a scratch harness.

| | Castlevania | Zelda |
|---|---|---|
| today / (a): drawn keys carried | 84.7 % | 45.6 % |
| (b) painted, `textures/` alone: missing keys served by the painted crop | 96/96 | 312/312 |
| (b) painted, `textures/` over `auto/`: missing keys served by the painted crop | **0/96** | **0/312** |
| (b) painted: unobserved palettes served by the painted crop | 53/53 | 124/124 |
| (c) simulated, unpainted: drawn keys carried | **100 %** | **100 %** |
| (d) unpainted / painted: drawn keys carried | 84.7 % / **100 %** | 45.6 % / **100 %** |
| unobserved keys in any rebuilt `hires.txt` | 0 | 0 |
| `sheets/` growth: (c) | +96 cells, +30.5 KB (+1.3 %) | +312 cells, +95.7 KB (+12.2 %) |
| `sheets/` growth: (c) folded | +61 cells | +76 cells |
| `sheets/` growth: (d) | +1.6 KB (sidecar only) | +5.0 KB |

Round trip:

- Every arm builds with 0 errors, and the second build is byte-identical.
- Unpainted, (b) and (d) rebuild byte-identical to today.
- (c) renders today's rules identically and adds the missing keys with the
  recording's own art.

The measurement contradicts two premises of this ADR:

- **(b)'s premise does not hold in a layered pack.** `NesConsole` loads
  `textures/` over `auto/`. `auto/` keeps an exact `defaultTile=N` rule for
  every drawn palette, and an exact rule beats the wildcard. So (b) reaches
  none of the missing keys and only recolours palettes the recording never
  drew, which §3 of ADR-0183 forbids. It does beat the bootstrap's
  neutral-palette `Y` rule (the human layer registers first), and without
  `auto/` it serves every palette. A wildcard also carries one Brightness,
  so of the folds it reproduces only the inert ones.
- **(d) "emits the same painted crop"** renders a fade step at full
  brightness. To render folds correctly it has to write each fold's
  Brightness, and above 1 for the *brighter* case (Castlevania 6, Zelda 40).

Also, #415 has been fixed on `main` since this ADR was written (`10176a10`).
The first-seen palette still decides which palette a cell carries.

**What the numbers point to (neutral, for the human deciding).**

- (b) as written is ruled out by the layered load.
- (c) and (d) both reach 100 % of drawn keys with no invented key and a
  clean round trip, and they trade differently:
  - (c) shows every colourway and needs no painting. It costs up to +12 %
    sheet bytes on Zelda, and a recorder change against a full
    `HdPackBuilder.cpp` ceiling.
  - (d) is a small build and schema change that keeps today's sheets
    byte-for-byte. It leaves colourways implicit until split and needs
    per-fold Brightness.
- The numbers also suggest a split, measured only as counts: (d) for folds,
  with Brightness, and (c) for colourways (61 / 45–128 extra cells).

## Decision

A hybrid of (c) and (d), chosen on the 2026-09-24 measurement. Option (b) is
rejected: under the layered load it serves none of the missing keys and
recolours only unobserved palettes (ADR-0183 §3).

1. **Colourways get their own cell (c).** For every `(shape, palette)` the
   recording drew whose palette is a colourway of the cell's palette
   (`artist_chr_kit`'s own test: some painted entry changes hue, or the
   residual fails the 25° gate), the recorder emits a variant cell, rendered
   in that palette from the recorded art. It sits beside its base cell on the
   same organised sheet, in the variant layout ADR-0183 §2 item 1 already
   uses, and is its own exact key. A sprite cycle grid keeps its phase order:
   variants follow their base phase, never interleave phases. The emission
   lives in the host-free `MesenSheets::` headers. `HdPackBuilder.cpp` only
   calls into it; if it still has to grow past its ADR-0137 ceiling, that
   ceiling is amended explicitly, never raised in silence.
2. **Folds ride on the cell (d, with Brightness).** A drawn palette that is
   a fold of the cell's palette (same picture at another brightness: every
   painted entry keeps its hue column) gets no cell. The cell's sidecar tile
   entry lists it with the brightness that reproduces it, e.g.
   `"folds": [{"palette": "0F122A30", "brightness": 0.75}]`. `mep_build`
   emits one exact `defaultTile=N` rule per listed fold, pointing at the
   cell's crop with that Brightness, above 1 where the fold is brighter than
   the cell. The rules are emitted painted or not. Unpainted, each rule must
   render what the recording drew for that key, so the round trip holds.
   Every emitted key is an observed one, so there is no wildcard.
3. **The fold test measures against the cell.** `compute_folds`' drift test
   currently measures a step against the brightest step of its group, and
   so refuses 83 of Zelda's fade steps. It is measured against the cell's
   own palette instead, for both the pattern pages and the sheets, so the two
   surfaces agree on what a fold is.

Acceptance for F14.9, on the Castlevania 60 s and Zelda 85 s runs of the
F14.4 log:

- drawn-key coverage 100 % on both games, with 0 unobserved keys in the
  rebuilt `hires.txt`;
- cell growth at most the measured "(c) folded" count (+61 / +76), give or
  take the criterion change of item 3, stated;
- the ADR-0183 §4 round trip: 0 build errors, a byte-identical second build,
  and an unpainted kit that renders what the recording drew;
- unit tests in the recorder's host-free headers and in `mep_build`,
  `mep_lint` and `artist_chr_kit`.

## Consequences

- As decided: kits regenerated after F14.9 are not drop-in replacements for
  painted kits from before. The sheets gain variant cells and the sidecar
  gains `folds`, so painted work comes back through the round trip, not a
  copy. A painted base cell now also changes what its folds render, which is
  the intent: a fade step is the same picture.
- The sidecar schema change touches ADR-0153's cell shape, `mep_lint` and
  the docs' "What a cell is". An old sidecar without `folds` stays valid and
  simply carries no fold rules.

Consequences as written for each option before the decision:

- Under (a), every artist-facing doc has to say that a shape's other palettes
  are on the pattern pages. `docs/remastering-a-game.md` says it already.
- Under (b) or (d), a painted cell changes what several keys render, so an
  artist who paints a red enemy also repaints its blue twin unless they split
  it off.
- Under (c), kits regenerated after the change are not drop-in replacements
  for painted kits from before. The first-seen palette stops deciding what the
  artist sees, which is also the mechanism behind issue #415.
