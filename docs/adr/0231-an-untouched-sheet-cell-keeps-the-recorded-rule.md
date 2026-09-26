# ADR-0231: An untouched sheet cell keeps the recorded rule, so an unpainted rebuild renders exactly what was recorded

- Status: **accepted 2026-09-24 — implemented the same turn** (issue #447),
  with unit tests covering the decision in
  `scripts/test_mep_build_recorded.py`. The user's question, pick and
  go-ahead, verbatim:
  - question: *"Como o rebuild de um kit sem pintura deve voltar a
    renderizar exatamente o que foi gravado (#447)?"*
  - pick: *"Célula intocada = regra gravada (Recommended)"* — option text:
    *"Quando a célula não foi pintada (igual ao .orig), o mep_build reemite a
    regra da gravação sem mudar nada, apontando para a página xBRZ do auto/.
    Só célula pintada aponta para o crop da sheet. Mantém o conjunto de
    chaves e os pixels, e o artista continua pintando em
    nearest-neighbour."*
  - go-ahead: *"resolva em paralelo o bug 447"*

  This quote goes in the PR body too.
- Date: 2026-09-24
- Amended: 2026-09-25 (A4) — the first stroke on a cell swaps the whole 8×8
  tile from the filtered page to the sheet cell, so it moves pixels the
  artist never touched (1 painted pixel = 106 on-screen px; a rebuild
  without painting = 0). Arms and method in "A4 follow-up" of
  `docs/validation/smb3-ninjagaiden-deep-measurement-2026-09-25.md`.
- Related: ADR-0183 (§4 the round-trip acceptance test), ADR-0153 (§4
  "Precedence when two sheets claim the same tile key", the `*.orig.png`
  twin), ADR-0178 (mirrored sprites and the `source` key), ADR-0212 (Reload
  Repainted Images re-decodes images only), ADR-0230 (fold and palette
  variant rules, slice F14.9), ADR-0172 (index-keyed tiles on a CHR ROM
  game), ADR-0189 §3 (a conditioned tile keeps its bare twin, whose order
  the re-emitted rules preserve), ADR-0197 (hand-authored conditions, which
  the recording cannot have), ADR-0219 (pages-only manifests), ADR-0137
  (per-file line ceilings), issues #447, #218, #413. Log:
  `docs/validation/issue-447-untouched-cell-keeps-recorded-rule-2026-09-24.md`.
- Supersedes / amends: amends ADR-0183 §4 (adds pixel parity for untouched
  cells to the key parity it asks for) and ADR-0153 §4 (the untouched cell
  no longer emits its crop). Supersedes nothing.

## Context

A recorded pack's `textures/hires.txt` points every key at the recorder's
pattern pages (`chr/Chr_*.png`). Those pages went through the pack's scale
filter — xBRZ by default (`HdPackBuilder::GenerateHdTile`). A sheet crop is
the raw tile upscaled nearest-neighbour (`SheetRender::RenderTile`), because
that is the surface an artist paints on.

`mep_build build` pointed every key a sheet cell carries at that cell's crop,
painted or not. So a kit rebuilt without a single painted pixel drew other
pixels than the recording: 901 of 912 Castlevania rules and 360 of 367 Zelda
rules (F14.4 runs, issue #447). The layered MEP sibling pack did not hide the
change either: `MergeLowerLayer` skips an `auto/` tile whenever `textures/`
already has the same key, so the nearest-neighbour crop won over the recorded
xBRZ page.

ADR-0183 §4 checked only the key set, which the rebuild did keep. So the
round trip passed while the picture changed.

## Options considered

1. **Render the sheet crops through the pack's filter.** The artist would no
   longer paint on the pixels the game draws, and a painted cell would be
   filtered twice. Rejected.
2. **Let `auto/` win in the loader for untouched keys.** This is a Core change
   to precedence that every existing pack depends on (ADR-0005), and a loose
   `HdPacks/<rom>/` pack has no `auto/` layer to win from. Rejected.
3. **Document key parity only.** This leaves #447 in place. Rejected.
4. **Re-emit the recorded rule for an untouched cell (chosen).**

## Decision

### 1. The rule

When `mep_build` emits the rule for a sheet cell that was **not painted** (it
equals its `*.orig.png` twin, as decided under ADR-0153 §4) and the recording
has that key, it emits the recording's own line instead. That line keeps its
condition, fields and trailing values byte for byte, except the `<img>` index,
which is remapped. It points at the recorded page. Only a **painted** cell
points at its sheet crop.

- The re-emitted rules follow the sheet rules, under the comment line
  `# mep_build: rules kept as recorded for untouched cells, from <source>
  (ADR-0231, #447)`.
- Their `<img>` lines follow that comment, in the recording's image order,
  and the rules keep the recording's order. So a conditional rule stays above
  its bare twin.
- A sheet with no usable twin still counts every cell as painted (ADR-0153
  §4, `_EditedProbe`), so this ADR changes nothing for it.

Painting a cell and then reverting it brings the recorded rule back.

### 2. Where the recording is read from

First hit wins (`scripts/mep_recorded.py`):

1. **`textures/hires.recorded.txt`** — a snapshot this ADR introduces.
2. **`auto/textures/hires.txt`** — the layered project (`mep_import`, the
   ADR-0183 §4 layout), which no build rewrites. It is used only when it
   looks like a recording.
3. **The build's own key source** (`textures/hires.txt`), used only when a
   build did not write it. This is the ARTIST.md recipe (`cp -R auto
   painted`), where the recording lives nowhere else. The first build
   overwrites that file, so before it does, its bytes are copied to (1) and
   the build says so.
4. **`textures/hires.txt`** when a `--source` that is not a recording keyed
   the build but that file is one: the build overwrites it all the same.

The copy to (1) is taken before any recorded rule is checked, so a build
that can use none of them right now (its only page missing, every crop out
of bounds) still keeps the recording, and restoring the page brings the
rules back (PR #472 review).

A manifest counts as a recording when it has `<tile>` rules, names no
`sheets/` image, does not carry the section comment above, and is not a
pages-only manifest (ADR-0219).

A recorded page is looked up under `textures/` first, then beside the
manifest the recording was read from. HdPackLoader resolves an `<img>`
against the folder of the `hires.txt` that names it, and a pack zip's
`textures/` section cannot reach the `auto/` layer next to it. So a page
found only beside the recording, as `auto/textures/chr/Chr_N.png` is in
every `mep_import` project, is copied up into `textures/` when a kept rule
names it, the same way a `<background>` is copied up from `auto/textures/`
(#344). The emitted `<img>` then resolves inside the layer that names it,
and `mep_lint` passes on the result (PR #472 review). The `<img>` line is
written with `/` separators, as `mep_carry` writes a carried name; the
loader reads `\` as `/`, so this changes nothing at run time.

The kit's `chr/Chr_N.png` pages overwrite the recorded pages in that recipe.
They hold every recorded crop byte for byte at the same position (3 779 of
3 779 on Castlevania), so the recorded rules still resolve.

### 3. Fallbacks are counted, never silent

A recorded rule is used only where it still means what it meant. Otherwise
the untouched cell falls back to its sheet crop, as before this ADR, and the
build prints how many cells did and why. Fallback cases:

- the page the rule names is missing both under `textures/` and beside the
  recording (§2);
- its crop lies outside that page;
- the recording's `<scale>` is not the build's;
- the key was never recorded (the sheets brought it — 7 keys on the
  Castlevania measurement), including a condition authored after recording;
- there is no recording left to read, for example a pack built before this
  ADR whose `textures/hires.txt` was already overwritten and which has no
  `auto/` layer.

### 4. The key set is unchanged; ADR-0183 §4 gains pixel parity

This ADR changes only which image a key points at, never which keys are
emitted. The rebuilt key set stays the sheets' key set, and an index-keyed
cell on a CHR ROM game (ADR-0172) keeps its recorded index rule. ADR-0183
§4 is amended: every rule for an untouched cell must also render the recorded
pixels, not just keep the key.

### 5. Mirrored cells

A mirrored cell (ADR-0178) contributes its unflipped `source` key. When that
cell is untouched, the source key's recorded rule is the one emitted, so it
is never baked from the flipped crop.

### 6. ADR-0230 fold and variant rules

A fold or palette-variant rule that ADR-0230 derives from an untouched cell
follows the same rule: when the recording has that key, the recorded line
wins. This works through `mep_recorded.Recorded.take`, which sees every entry
the emission loop keeps. F14.9 emits each fold as its own entry of the
cell, carrying the cell's painted flag, and a variant cell is an ordinary
cell, so both reach `take` with no extra wiring.
`fold_follows_recorded_test` in `scripts/test_mep_build_recorded.py` pins
it for a fold.

### 7. `check-coverage`

`check-coverage` counts the `<img>` lines declared under the section comment
as build output, the same as `sheets/` images (#218). So an unpainted build
is still a valid sheet-derived baseline.

### 8. Painting a pattern page

Because untouched keys point at the recorded `chr/` pages again, painting
those pages reaches them, and *Reload Repainted Images* shows it in place
(ADR-0212).

## Consequences

- **The first paint of a cell re-points its key, so rebuild and reopen the
  ROM once.** The same applies to reverting a cell. After that, repainting an
  already-painted cell only changes pixels and reloads in place (#413,
  ADR-0212). The rule cannot be pointed at the crop ahead of time without
  bringing #447 back. `mep_figure import` already reports it: "N painted
  cell(s) will re-point a key in hires.txt at the next build — reopen the ROM
  to see them".
- **A painted cell swaps the whole tile, not the pixels you touched.** The
  first stroke takes the entire 8×8 cell from the filtered (xBRZ) recorded
  art (`HdPackBuilder::GenerateHdTile`) to the nearest-neighbour art on the
  sheet (`SheetRender::RenderTile`), so pixels of that cell the artist never
  painted change with it. Measured on Super Mario Bros. 3 at 4×, one key,
  against a control rebuild: **rebuild without painting** (the figure PNG
  written back unchanged) **0 px** differ; **1 pixel** painted changes **106**
  on-screen px (1 magenta + 105 untouched); one 4×4 block (16 px) changes
  **116** (16 + 100); the whole cell (576 px) changes **598** (576 + 22).
  The counts are the whole-tile swap's own arithmetic: 83 of the tile's px
  are where the sheet's nearest-neighbour and the page's xBRZ disagree, and
  22 are px the sheet cell does not draw at all. Arms, method and the
  caveats: "A4 follow-up (2026-09-25)" in
  `docs/validation/smb3-ninjagaiden-deep-measurement-2026-09-25.md`.
- The 22 px a whole-cell stroke drops are, measured, px the sheet cell
  leaves fully transparent (alpha 0) and the recorded page inks, so the
  painted tile draws the backdrop there. Calling that page ink the scale
  filter's soft edge is **attributed, not proven** — by analogy with
  Excitebike's A4, and the opaque-count gap between the `chr/` and sheet
  crops is 12, not 22. Do not restate it as measured.
- The snapshot `textures/hires.recorded.txt` (about 369 KB on Castlevania)
  ships in the pack zip. The loader ignores it, since only `hires.txt` is a
  manifest.
- On an unpainted kit, the per-sheet "contributes no tile of its own" info
  lines appear, because the recording supplies those tiles now.
- `mep_build.py`'s audio-manifest helpers moved to `mep_carry.py` so the file
  stays under its ADR-0137 line ceiling. The ceiling was not raised.

## Measured 2026-09-24

Castlevania, an 80 s stage-1 recording plus the castlevania-deep kit, built
with the ARTIST.md recipe. Full method in the validation log.

| Build | Rebuilt rules | Pixels or fields differ from the recording | Same | Never recorded |
|---|---|---|---|---|
| Before (origin/main), unpainted | 782 | 764 | 11 | 7 |
| After, unpainted | 782 | 0 | 775 | 7 |

- The key set is 782 before and after, and the control and painted arms
  match.
- A third build is byte-identical to the second.
- In the layered sibling pack, the after build's screenshots are identical
  to the recording at t=40 and t=80. The before build differed by 40 115 px
  (4.08 %) and 2 900 px (0.30 %).
- Painted arm (one figure cell painted magenta, then `mep_figure import`): 3
  rules point at the sheet crop and draw magenta, and 772 stay recorded. The
  t=40 screenshot differs from the recording by exactly the 848 painted
  pixels. **That equality is a property of that cell, not of the rule**
  (amended 2026-09-25, A4): it held because the painted region covered every
  pixel where the nearest-neighbour crop and the recorded xBRZ page
  disagree. On a cell where it does not, the diff is larger than the paint
  and includes px outside it — Super Mario Bros. 3's Mario head, one key:
  **1 painted pixel, 106 on-screen px, 105 of them never touched**; the
  whole cell, 598 (576 + 22). Arms and method: "A4 follow-up (2026-09-25)"
  in `docs/validation/smb3-ninjagaiden-deep-measurement-2026-09-25.md`.
