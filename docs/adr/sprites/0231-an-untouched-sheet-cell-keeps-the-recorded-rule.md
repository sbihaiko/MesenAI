# ADR-0231: An untouched sheet cell keeps the recorded rule, so an unpainted rebuild renders exactly what was recorded

- Status: accepted 2026-09-24 — implemented the same turn (#447), unit tests in
  `scripts/test_mep_build_recorded.py`. Question, pick and go-ahead, verbatim:
  *"Como o rebuild de um kit sem pintura deve voltar a renderizar exatamente o
  que foi gravado (#447)?"*; *"Célula intocada = regra gravada (Recommended)"* —
  an untouched cell re-emits the recording's own rule, pointing at the `auto/`
  xBRZ page, while painting in nearest-neighbour; only a painted cell points at
  the sheet crop; and *"resolva em paralelo o bug 447"*.
- Date: 2026-09-24
- Amended: 2026-09-25 (A4) — the first stroke swaps the whole 8×8 tile from the
  filtered page to the sheet cell, moving pixels the artist never touched
  (1 painted pixel = 106 on-screen px; a rebuild without painting = 0). Method:
  "A4 follow-up" of
  `docs/validation/measurements/smb3-ninjagaiden-deep-measurement-2026-09-25.md`.
- Related: ADR-0183 §4, ADR-0153 §4 ("Precedence when two sheets claim the same
  tile key", the `*.orig.png` twin), ADR-0005, ADR-0178 (the `source` key),
  ADR-0212, ADR-0230 (fold and variant rules), ADR-0172, ADR-0189 §3, ADR-0197,
  ADR-0219, ADR-0137, issues #447, #218, #413. Log:
  `docs/validation/issues/issue-447-untouched-cell-keeps-recorded-rule-2026-09-24.md`.
- Amends ADR-0183 §4 (pixel parity for untouched cells) and ADR-0153 §4 (the
  untouched cell no longer emits its crop). Supersedes nothing.

## Context

A recording's `textures/hires.txt` points every key at the xBRZ pattern pages
(`chr/Chr_*.png`, `HdPackBuilder::GenerateHdTile`); a sheet crop is the raw
tile upscaled nearest-neighbour (`SheetRender::RenderTile`), the surface
painted on. `mep_build build` pointed every cell-carried key at the crop,
painted or not, so an unpainted rebuild drew other pixels (901 of 912
Castlevania rules, 360 of 367 Zelda rules; F14.4 runs, #447). `MergeLowerLayer`
skips an `auto/` tile whenever `textures/` already has the key; a loose
`HdPacks/<rom>/` pack has no `auto/` layer anyway. ADR-0183 §4 checked only the
key set, which the rebuild kept, so the round trip passed while the picture
changed.

## Decision

### 1. The rule

When `mep_build` emits the rule for a sheet cell **not painted** (equal to its
`*.orig.png` twin, ADR-0153 §4) and the recording has that key, it emits the
recording's own line instead — its condition, fields and trailing values byte
for byte, except the `<img>` index, remapped to the recorded page. Only a
**painted** cell points at its sheet crop.

- The re-emitted rules follow the sheet rules, under `# mep_build: rules kept as
  recorded for untouched cells, from <source> (ADR-0231, #447)`.
- Their `<img>` lines follow that comment, in the recording's image order, and
  the rules keep the recording's order, so a conditional rule stays above its
  bare twin.
- A sheet with no usable twin counts every cell painted (ADR-0153 §4,
  `_EditedProbe`), so nothing changes for it.

Painting a cell and reverting it brings the recorded rule back.

### 2. Where the recording is read from

First hit wins (`scripts/mep_recorded.py`): (1) `textures/hires.recorded.txt`,
a snapshot this ADR introduces; (2) `auto/textures/hires.txt`, the layered
`mep_import` project (ADR-0183 §4), when it looks like a recording; (3) the
build's own key source `textures/hires.txt` when a build did not write it (the
ARTIST.md recipe `cp -R auto painted`; the first build copies its bytes to (1)
first); (4) `textures/hires.txt` when a `--source` that is not a recording
keyed the build but that file is one. The copy to (1) precedes any recorded-rule
check, so a build that can use none of them yet keeps the recording (PR #472
review).

A manifest counts as a recording when it has `<tile>` rules, names no `sheets/`
image, does not carry the comment above, and is not pages-only (ADR-0219). A
recorded page resolves under `textures/` first, then beside the manifest it was
read from; a page found only beside it (as `auto/textures/chr/Chr_N.png` is in
every `mep_import` project) is copied up into `textures/` when a kept rule names
it, as a `<background>` is copied up from `auto/textures/` (#344), so the
emitted `<img>` resolves in the layer that names it and `mep_lint` passes (PR
#472 review). The `<img>` line uses `/` separators (`mep_carry`); the loader
reads `\` as `/`. The kit's `chr/Chr_N.png` pages overwrite the recorded pages
there, holding every recorded crop byte for byte at the same position (3 779 of
3 779 on Castlevania), so the recorded rules still resolve.

### 3. Fallbacks are counted, never silent

A recorded rule is used only where it still means what it meant; otherwise the
untouched cell falls back to its sheet crop, and the build prints how many
cells did and why. Cases: the page is missing under `textures/` and beside the
recording (§2); its crop is outside that page; the recording's `<scale>` is not
the build's; the key was never recorded (the sheets brought it — 7 keys on the
Castlevania measurement), including a condition authored after recording; there
is no recording left (a pack built before this ADR whose `textures/hires.txt`
was already overwritten and which has no `auto/` layer).

### 4. The key set is unchanged; ADR-0183 §4 gains pixel parity

This changes only which image a key points at, never which keys are emitted. The
key set stays the sheets' key set, and an index-keyed cell on a CHR ROM game
(ADR-0172) keeps its recorded index rule. ADR-0183 §4 is amended: every rule for
an untouched cell must also render the recorded pixels, not just keep the key.

### 5. Mirrored cells

A mirrored cell (ADR-0178) contributes its unflipped `source` key; when
untouched, that key's recorded rule is emitted, so it is never baked from the
flipped crop.

### 6. ADR-0230 fold and variant rules

A fold or palette-variant rule ADR-0230 derives from an untouched cell follows
the same rule: when the recording has that key, the recorded line wins. This
works through `mep_recorded.Recorded.take`, which sees every entry the emission
loop keeps; F14.9 emits each fold as its own entry of the cell, carrying the
cell's painted flag, and a variant cell is an ordinary cell, so both reach
`take` with no extra wiring. `fold_follows_recorded_test` in
`scripts/test_mep_build_recorded.py` pins it for a fold.

### 7. `check-coverage`

The `<img>` lines declared under the section comment count as build output, the
same as `sheets/` images (#218), so an unpainted build is still a valid
sheet-derived baseline.

### 8. Painting a pattern page

Because untouched keys point at the recorded `chr/` pages again, painting those
pages reaches them, and *Reload Repainted Images* shows it in place (ADR-0212).

## Consequences

- The first paint of a cell re-points its key, so rebuild and reopen the ROM
  once (likewise to revert); after that, repainting reloads in place (#413,
  ADR-0212). The rule cannot point at the crop ahead of time without bringing
  #447 back; `mep_figure import` reports it.
- A painted cell swaps the whole tile, not the pixels you touched: the first
  stroke takes the entire 8×8 cell from the filtered xBRZ art
  (`HdPackBuilder::GenerateHdTile`) to the nearest-neighbour sheet art
  (`SheetRender::RenderTile`). On Super Mario Bros. 3 at 4×, one key, against a
  control rebuild: rebuild without painting **0 px** differ; **1 pixel** painted
  changes **106** on-screen px (1 magenta + 105 untouched); one 4×4 block
  (16 px) changes **116** (16 + 100); the whole cell (576 px) changes **598**
  (576 + 22). The 22 px a whole-cell stroke drops are px the sheet cell leaves
  transparent and the recorded page inks — **attributed, not proven**.
- The snapshot `textures/hires.recorded.txt` (about 369 KB on Castlevania)
  ships in the pack zip; the loader ignores it, since only `hires.txt` is a
  manifest.
- On an unpainted kit, the per-sheet "contributes no tile of its own" info
  lines appear, because the recording supplies those tiles now.
- `mep_build.py`'s audio helpers moved to `mep_carry.py` to stay under its
  ADR-0137 line ceiling, which was not raised.

## Record

- 2026-09-24 — Castlevania, an 80 s stage-1 recording plus the castlevania-deep
  kit, ARTIST.md recipe (method in the log):

  | Build | Rebuilt rules | Differ | Same | Never recorded |
  |---|---|---|---|---|
  | Before (origin/main), unpainted | 782 | 764 | 11 | 7 |
  | After, unpainted | 782 | 0 | 775 | 7 |

  Key set 782 both; a third build is byte-identical; in the layered sibling pack
  the after build's screenshots match the recording at t=40 / t=80 (before:
  40 115 px = 4.08 %, 2 900 px = 0.30 %); painted arm 3 rules at the crop, 772
  recorded, the t=40 diff exactly the 848 painted pixels (a property of that
  cell, not the rule; A4).
- 2026-09-25 — A4 amendment: the first stroke swaps the whole 8×8 tile (filtered
  page → sheet cell), moving pixels never touched (1 painted pixel = 106
  on-screen px; unpainted rebuild = 0). "A4 follow-up (2026-09-25)" in
  `docs/validation/measurements/smb3-ninjagaiden-deep-measurement-2026-09-25.md`.
