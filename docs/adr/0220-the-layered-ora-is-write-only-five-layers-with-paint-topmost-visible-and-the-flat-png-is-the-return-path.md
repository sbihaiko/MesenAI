# ADR-0220: A layered `.ora` is written beside every surface, write-only for the toolchain, its five layers ordered so `paint` is the topmost visible one — and the flat PNG over the F12.4 name stays the only return path

- Status: **accepted 2026-09-22 — code landed the same day as PRD Part A
  F12.11; stop condition (2) still open.** Accepted with *"aceito o F12.11.
  nao implemente ainda."*; the build go-ahead came later the same day,
  verbatim *"dispara as frentes 1, 2, 3 e 4 em paralelo usando workflows"*,
  and the two deviations found while building were decided by the user with
  *"Emendar §3 e o PRD (Recommended)"* (the `context` layer exists only where
  every cell has a stage position, i.e. the `artist_map` panoramas) and
  *"Trocar para #FF00FD (Recommended)"* (the sentinel; `#FF00FF` collides with
  the recorder's unpainted fill). Both amendments are in §3 and §4, dated. In
  code: `scripts/ora_writer.py` and `scripts/mep_sentinel.py`, called from
  `compose_engine.py`, `artist_chr_kit.py` and `mep_figure.py`; `mep_lint.py`
  fails a cell carrying the sentinel. Unit tests: `scripts/test_ora_writer.py`
  (16). Stop conditions (1) and (4) are met by the automated pass; **(2)
  — GIMP and Krita opening both files with every layer named and `paint`
  selected — is logged by a person** and had not been when this landed, so the
  PRD row stays live until that log exists. **Corrected 2026-09-23:** this line
  first counted (3) as met too, but no test or log exercises a stroke on
  `paint` exported flat and reaching the game through F12.3 —
  `test_ora_writer.py` covers the container, the layers and the sentinel
  refusal only — so (3) is unevaluated (PR #384 review).
  Proposed 2026-09-20 and left so the same day by the user's decision; the
  deferral's reason (no GIMP/Krita artist population measured) is unchanged
  and F12.11 stays the second path beside F12.4, measured against it (§6).
  **Amended 2026-09-23:** stop condition (2) was partly measured by the user
  in GIMP 2.10 and Krita 5.3.4 on one four-layer sheet — every layer named,
  but both readers open the file with the bottom layer `orig` active, and no stack order makes both select
  `paint` (§3 amendment); the user's decision, option (b), keeps the order and
  documents "select `paint` before painting" in `ARTIST.md` and
  `docs/remastering-a-game.md`. Two cosmetic defects found in the same session
  are fixed with it: captions are fitted to the canvas (§3) and the `palettes`
  band follows first use and is labelled (§4). `test_ora_writer.py` is 18.
- Date: 2026-09-20
- Related: PRD Part A F12.11 (this contract) and its prerequisites F12.3 and
  F12.4, F12.9 (the static page this ADR's four-layer case rides on), ADR-0183
  §1/§2/§3/§5 (the kit is a projection; the four surfaces; inference is marked;
  captions come from data or a human), ADR-0219 §1 (a kit may project over the
  ROM alone), ADR-0210 (provenance per cell, `fill`, `defaultTile = Y`),
  ADR-0209 Q2/Q3 (what is exported and how it comes back), ADR-0213 §3
  (`assetName`, the name a flat export lands on), ADR-0212 (the in-place reload
  that shows the result), ADR-0153 §3/§4 (the `*.orig.png` twin and the diff
  keyed on it), ADR-0164/ADR-0172 (placements and the sidecar's CHR index),
  ADR-0165 and ADR-0154 (stdlib only, nothing downloaded), MEP-v1 §5,
  `docs/remastering-a-game.md` §4
- Supersedes / amends: amends ADR-0183 §2 by adding a fourth file per surface —
  the layered container, which the PRD counts as a fifth file kind in the kit.
  §2's four **surfaces** are unchanged, and §1 (a kit is a projection), §3
  (nothing inferred passes as evidence) and §4 (the rebuild is the acceptance
  test) are cited, not re-decided. ADR-0213's name contract is not amended: the
  `.ora` shares a validated stem and is never a painting-surface name.

## Context

The F12.11 row ends its decision cell with the condition for starting:

> **Needs an ADR before start** — it adds a fifth file kind to ADR-0183 §2's
> surfaces and fixes the layer contract; it must also state that `.ora` is
> **write-only** for the toolchain (reading `paint` out of it is stdlib-trivial
> and is refused on purpose, or the sheet stops being the source of truth).

and fixes the stack and the return path in two sentences this ADR adopts
verbatim:

> Layers, bottom to top — **five on a recorded surface, four on an F12.9 static
> page**: `orig` (the `*.orig.png` twin, `edit-locked`), `context` (the 1x
> stitched-map crop around a figure at 50 % opacity — only when a recording
> exists, absent on F12.9 pages), `paint` (fully transparent, the **selected**
> layer, the only one the artist touches), `guides` (cell grid, pose / cycle
> captions from `names.json` or the sidecar ids, hatch over `seen: false` cells
> — drawn in one sentinel colour outside every NES palette,
> `visibility="hidden"` for export), `palettes` (a swatch strip of the palettes
> recorded for that sheet, hidden).

> **The return path does not change:** the artist exports a flat PNG over the
> F12.4 name; `sheet_repaint` keeps only cells that differ from `orig`, and
> `mep_lint.py` fails a cell that contains the sentinel colour (the guides layer
> was left visible) naming the cell.

Everything around that is already decided, and this ADR cites it rather than
reopening it: the kit is a projection and never a second source of truth
(ADR-0183 §1), inference is marked and never passed off as evidence (§3), a
surface counts as delivered only when a copy of the pack carrying it rebuilds
with 0 errors and an unchanged key set (§4), captions come from the recording's
ids or a human's `names.json` and from nothing else (§5); MesenAI owns selection
and return and delegates the brush (ADR-0209); the return mechanism is a PNG
overwritten on disk and re-decoded in place (ADR-0212, ADR-0213 §3). What no
ADR has fixed is the container: what a `.ora` is, which layers it holds, in
which order, and what the toolchain is allowed to do with it.

Four facts about the format were checked while writing this, and three of them
change the contract the row states. They are recorded here because the row
alone would produce a file that reads correctly in our heads and not in the
three programs:

1. **The row's file list is missing a required member.** A `.ora` is a zip whose
   **first** entry must be `mimetype`, **stored uncompressed**, holding exactly
   `image/openraster` with no whitespace and no trailing newline. `stack.xml`,
   `mergedimage.png` and `Thumbnails/thumbnail.png` come after it. A zip written
   without it opens in the tolerant readers and is not a conforming file; since
   the first stop condition is that the file is valid, we write it.
2. **The layer attributes are narrower than the prose suggests.** `<layer>`
   requires `src`, and accepts `name`, `x`, `y`, `opacity` (a float from `0.0` to
   `1.0`, default `1.0`), `visibility` (`visible`/`hidden`) and `composite-op`
   (default `svg:src-over`). `edit-locked` is **not** in the 0.0.5 baseline: it is
   a documented extension, an `xsd:boolean` defaulting to `false`, implemented by
   Krita and MyPaint, and the spec's own rule is to write it for an editing
   workflow and omit it for an archival one. Ours is an editing workflow, so it is
   written — as a courtesy to the two readers that honour it, never as a
   boundary.
3. **No attribute selects a layer.** OpenRaster has no field for the active or
   selected layer; the request for one is still open in the format's tracker.
   "`paint` is the selected layer" is therefore a *reader's* behaviour, not
   something we can write, and the most the file can do is make `paint` the
   topmost **visible** layer. Stop condition (2) is a human's log anyway; §3
   below says exactly what is written and §7 what is judged.
4. **The guides guard only covers one of the two layers it must.** The row hangs
   the wrong-export guard on the sentinel in `guides`, and describes `palettes`
   as "a swatch strip … hidden" with no guard. A swatch strip drawn in palette
   colours is drawn in NES colours: leaving `palettes` visible would ship
   swatches into cells as art, and no check in the row would say a word. §4
   closes that hole without adding a second sentinel.

The trap that makes "write-only" a decision rather than an accident of
difficulty is worth stating plainly: reading `paint` back out is `zipfile` plus
`xml.etree.ElementTree` plus the PNG decoder `mep_build.py` already owns — an
afternoon, in a repo whose whole toolchain is stdlib (ADR-0165). Nothing
technical stops it. What stops it is this file.

**Non-goals.** This ADR does not write `.psd`, `.aseprite` or `.kra`, and does
not read any of them (ADR-0213, ADR-0165). It does not add a watcher (ADR-0209
Q3 (g) stays unadopted), an editor, a renderer, a session or undo model, or any
Core/UI change. It does not put a `.ora` inside a pack, or teach `mep_build.py`,
`sheet_repaint.py` or any importer to open one. It does not decide the F12.9
static projection (ADR-0219) or the third-party index import (ADR-0210 §3,
F12.12). And it does not make F12.11 the default path: F12.4's per-layer asset
names stay the default, and F12.11 is measured against it.

## Decision

### 1. One `.ora` per surface, in the kit, named from the surface

Beside every surface PNG the kit writes `<name>.ora`, in the folder that surface
already lives in (`sheets/`, `chr/`, `map/`). It is produced in the same pass and
from the same in-memory canvas as `<name>.png` and `<name>.orig.png`, so the
`orig` layer *is* the twin's pixels and the merged image *is* the composite of
the pixels the flat PNG and its twin were written from — the three files cannot
disagree by construction.

The name needs no new grammar and no manifest change: ADR-0213 §3 already stamps
`assetName` per surface, and the `.ora` is that name's stem with `.ora` in place
of `.png`, in the same folder. The pair shares a stem, so `check_asset_set`'s
folder rule (case clash, Windows-reserved stems, the 100-character budget) covers
it as it stands, and `check_asset_name` — which refuses a name that is not
exactly `*.png` — is never applied to the `.ora`, because it is not a painting
surface and is never an export target. A `.ora` is never a manifest target, so
ADR-0151's asset resolution never names one, and no pack folder ever contains
one: the kit is beside the pack (ADR-0183 §1).

### 2. The container, fixed so that stop condition (1) can be a test

```
mimetype                       # first entry, ZIP_STORED, exactly `image/openraster`
stack.xml                      # <image version="0.0.5" w="…" h="…"><stack>…
mergedimage.png                # the composite of the layers as the file opens
Thumbnails/thumbnail.png       # mergedimage.png, nearest-neighbour, max side 256
data/orig.png
data/context.png               # when present
data/paint.png
data/guides.png
data/palettes.png
```

Members are written in that order with a fixed timestamp and fixed compression,
so the same inputs produce the same bytes — which is what makes "each `.ora`
round-trips through `zipfile` unchanged" a test rather than a hope. Layer PNGs
are written by the encoder the generators already have (`mep_build`'s stdlib
PNG writer); no new image code and no re-encode of the sheet.

`mergedimage.png` is the composite of the layers **as the file opens** — the
hidden ones hidden — so a reader that ignores the stack still shows a true image,
and no reader ever sees the sentinel in it. It is a display cache: §5 forbids
reading it back.

### 3. The five layers, exactly

Bottom to top, as the row fixes them; flags are `stack.xml` attributes.

| # | name | flags | content | present |
|---|---|---|---|---|
| 1 | `orig` | `visibility="visible"`, `opacity="1.0"`, `edit-locked="true"` | the `*.orig.png` twin's own pixels, cell for cell | always |
| 2 | `context` | `visibility="visible"`, `opacity="0.5"` | the 1x stitched-map crop around the surface's subject (ADR-0164 placements), placed in the canvas's context band | iff every cell has a stage position |
| 3 | `paint` | `visibility="visible"`, `opacity="1.0"` | fully transparent, zero alpha everywhere | always |
| 4 | `guides` | `visibility="hidden"`, `opacity="1.0"`, `edit-locked="true"` | sentinel-only drawing: cell grid on cell and gutter boundaries, captions from `names.json` or the sidecar ids (ADR-0183 §5), hatch over every `seen: false` cell (ADR-0183 §3, ADR-0210) | always |
| 5 | `palettes` | `visibility="hidden"`, `opacity="1.0"`, `edit-locked="true"` | the swatch strip, sentinel-framed (§4) | always |

Five rules complete the contract:

- **`paint` is the only layer that is the artist's.** It opens empty and selected
  as far as §3's third fact allows; a stroke on any other layer is work the kit
  discards on the next run (ADR-0183 §1 — the expensive artefact is the recording,
  the kit is throwaway), which is why `orig`, `guides` and `palettes` carry
  `edit-locked` and `context` does not: a reference the artist may want to move is
  not a layer we promise to regenerate.
- **`paint` is the topmost visible layer by construction**, because the two layers
  above it are hidden. That is the strongest statement `stack.xml` permits about
  which layer the artist lands on, and stop condition (2)'s "`paint` selected" is
  the reader's half of it, logged by the person who opens the file.
- **Nothing we draw can reach a cell by accident.** `context` is clipped so that
  no pixel of it falls inside a cell rectangle; `guides` and `palettes` may cross
  cells, and both are caught by the sentinel check of §4. This is not decoration —
  `mep_build._EditedProbe` decides "was this cell painted?" by comparing the cell
  rectangle against the upscaled `*.orig.png` twin pixel for pixel (ADR-0153 §3/§4),
  so a single stray pixel of ours inside a cell is read as the artist's work and
  rebuilt into the pack as art. A layer that can leak into a cell without a guard
  is a layer that can corrupt a pack silently.
- **`context` is present iff position, not iff recording.** The row's shorthand
  ("five on a recorded surface, four on an F12.9 static page") is exactly this
  rule for its own two bounded inputs, and the rule is the one that generalises: a
  layer whose content is "the place around this cell" cannot exist for a cell that
  has no place. So an F12.9 static page has four because nothing was observed and
  nothing is placed (ADR-0219), and a recorded `chr/` pattern page has four for
  the same reason a static page does — it is index-keyed, not placed (ADR-0172,
  ADR-0183 §2.4) — while recorded figure, scenery and map sheets have five.
- **The canvas gains a context band, and the twin grows with it.** `context` lives
  outside the cell grid, so a kit surface with a `context` layer is wider or taller
  than `columns × stride + gutter`. The `*.orig.png` twin is written on the same
  canvas (blank in the band), because the pair must grow together — `_EditedProbe`
  refuses a sheet and twin of mismatched size rather than ringing a silent bell
  (#346). Enlarging a canvas is safe for the reload: F12.3 refuses a *smaller* image
  because the sidecar's crops no longer fit (ADR-0212), and every crop still fits.
- **On a static page `palettes` says what the page is.** Every cell there is a
  `fill` with no observed palette and `defaultTile = Y` (ADR-0210), so the strip
  carries no swatches and states the wildcard in the sentinel instead; and since
  every cell is `seen: false`, the `guides` hatch covers the whole page — which is
  the sheet saying "nothing here was seen in play", the same sentence `ARTIST.md`
  opens with (ADR-0183 §3, ADR-0219).

*Amended 2026-09-22 (F12.11 implementation).* The rule stands — `context` iff
every cell has a stage position — and today's data settles what it yields: no
recorded artefact gives a sprite pose or a scenery cell a stage position.
`poses.json` and `adjacency.json` carry screen floor bands only, and the
recordings the kit generators read ship no `map-NNN.json` `placements[]`. So
`context` is present on the `artist_map` stage panoramas (every cell *is* on
the stage) and absent on figure, scenery and CHR sheets: "five on a recorded
surface" reads **five on a stage panorama, four elsewhere until a
sprite-position source exists**. The follow-up is named, not implied: derive a
sprite's screen position from the ADR-0222 OAM stream dump, place it on the
stage with the grid-dump camera recovery `artist_map.py` already does, and hand
the 1x crop to `compose_engine.Pack.export(context=)`, which is the hook the
writer already exposes. The row's Contra bounded input is therefore a
four-layer file today, and stop condition (2) reads "five and four" as
"panorama and figure page" once that source lands.

*Amended 2026-09-23 (stop condition (2) measured; user's decision, option (b)).*
**Both readers open the file with the bottom layer active, not the topmost
visible one.** Measured by the user on the Contra kit's `usr000.ora`: GIMP 2.10
(`file-openraster.py` inserts the layers in `stack.xml` order with no
"set active" call, so the last inserted — the bottommost — stays active) and
Krita 5.3.4 both land on `orig`, whatever the stack order. Of the two, only
Krita honours `edit-locked`; GIMP lets a stroke land on the locked `orig`. No
order fixes it for both: the only order that makes `paint` the active layer
puts it at the *bottom*, where every stroke is hidden under `orig`. The
decision is **(b) — keep the order of §3 (`orig`, [`context`,] `paint`,
`guides`, `palettes`) and document the click**: `ARTIST.md` (written by
`artist_kit_assemble.py`) and `docs/remastering-a-game.md` tell the artist to
select `paint` in the Layers panel before the first stroke, and why. The
second rule above stands as written — "topmost visible by construction" is
still the strongest statement the file can make; this amendment records that it
is not enough for the two readers we care about, and that the kit's page
carries the missing half. The row's `guides` captions are also **fitted, never
clipped** from this date: `ora_writer.draw_text` wraps a caption to the canvas's
right edge, to at most two lines above the next cell row, at half the grid's
glyph size on a composed sheet, ending in `...` when it still does not fit —
the unwrapped fallback title of a figure sheet ran off the canvas edge over
row 0 (F12.11 follow-up log, defect (a)). A caption still sits on its row's
top-left, so the two lines do cover the top of row 0 on `guides`; that layer
is hidden and sentinel-only, and bounding the lines bounds the cover.

### 4. The sentinel, and what catches a wrong export

**The sentinel is `#FF00FF` at alpha 255, drawn opaque.** The argument for that
triple: a NES palette quantised to 6 bits per channel never reaches `0xFF` in any
channel — the tables agree on a maximum of `0xFC`, and the magenta they do carry
is `#FC00FC`, one step off. The argument is not load-bearing, and is not left as
one: before writing, the kit asserts the triplet occurs nowhere in the sheet's
artwork or in its twin, so the value is *checked* absent from what we are guarding
rather than *argued* absent from the hardware. If a palette ever made `#FF00FF`
reachable, kit generation stops loudly at write time instead of quietly disabling
the guard; the escape is a different triplet, which is a one-line change and an
amendment to this section.

**`mep_lint.py` gains the check.** For every sheet PNG the lint can pair with its
sidecar, each cell rectangle is scanned for the exact sentinel pixels (RGBA
`255,0,255,255`), and a hit is an **error** naming the sheet and the cell — its
`index` and its `(x, y)` — so stop condition (4)'s "refused by lint with the
offending cell named" is a message a person can act on. Exact equality, no
tolerance: an approximate match would fire on dark magenta art and pass on a
blended grid, which is the opposite of both.

**`palettes` is sentinel-framed for that reason.** The strip is drawn as one
cell-tall band, row-aligned to the canvas's first cell row, on a sentinel
background, with each swatch inset one pixel from the band's top and bottom edges
and separated from its neighbour by a one-pixel sentinel column. Every cell the
band crosses therefore contains exact sentinel pixels, and leaving `palettes`
visible fails the same check that catches a visible `guides` — the row's
description of the strip is tightened here on purpose, because a strip in swatch
colours alone is the one way a hidden layer could still be wrong with nothing to
catch it.

Two limits are stated rather than papered over. A stroke painted *over* the
sentinel inside a cell the artist chose to paint is a blend, not a failure: the
check names cells that still carry exact sentinel pixels, which is why every layer
we draw is opaque and why `opacity` is `1.0` on `guides` and `palettes`. And a
`context` band left visible is harmless by §3's clipping rule, so it needs no
sentinel at all.

*Amended 2026-09-22 (F12.11 implementation).* **The sentinel is `#FF00FD` at
alpha 255 (RGBA 255, 0, 253, 255), not `#FF00FF`.** The recorder paints an
unrecorded CHR cell `0xFFFF00FF` (`HdPackBuilder.cpp`, mirrored as
`artist_chr_kit.UNPAINTED_RGBA`), so every recorded page with an `empty` cell
carried the original triplet in its own artwork and twin; the write-time
assertion above fired on the first recorded CHR page the writer met, exactly as
"checked absent, never argued absent" is meant to. `#FF00FD` keeps the same
argument (no 6-bit-quantised NES channel reaches `0xFD` either) and collides
with nothing the recorder writes. The triplet is spelled once, in
`scripts/mep_sentinel.py`; the assertion, the `mep_lint.py` scan and the
knock-out captions all read it from there.

*Amended 2026-09-23 (F12.11 follow-up, defect (b)).* **The band is in first-use
order and labelled.** The sheet-wide set of palettes used to be sorted by hex
string, so the group nearest the band's left edge belonged to whichever palette
sorted first, not to the first cell — read as "row 0's colours", it was wrong.
Now `ora_writer.first_use_palettes` lists each palette once, where it is first
used in reading order (row, then column), and knocks the first cell's label
(`index`; the hex tile index on a CHR page) out of the sentinel in front of its
group, so every group traces to a cell by the number before it. To make room,
a swatch is a quarter of the band wide instead of square; the sentinel framing
of this section is unchanged (one-pixel inset top and bottom, one-pixel sentinel
column between swatches, three between groups), a group that does not fit is
not drawn and `+N` is knocked out instead, so a first-row cell still holds
exact sentinel pixels and a visible `palettes` still fails the lint.

### 5. Write-only, and the refusal is deliberate

`.ora` is written by the kit and read by nobody in the toolchain. Concretely:

- No script under `scripts/` opens one for reading: not `mep_build.py build`,
  not `sheet_repaint.py`, not a kit generator, not `mep_lint.py` (the §4 check
  reads the flat sheet PNG and its sidecar, never the `.ora`), not `mep_import.py`.
- `mergedimage.png` is never read back either, by us or as an import path: it is
  a display cache, and treating it as the export would make the pack depend on
  which layers happened to be toggled (see the alternatives).
- **The return path is the flat PNG over the F12.4 name** (ADR-0213 §3) and
  nothing else: the artist exports flat over `<name>.png`, `mep_build._EditedProbe`
  keeps only the cells that differ from the `*.orig.png` twin, the rebuild is
  verified as ADR-0183 §4 requires, and F12.3's reload puts the result on screen
  without reopening the ROM (ADR-0212). ADR-0209's Q3 stays answered as **(h),
  same path, explicit re-import**; option (g)'s watcher is not opened by this ADR.
- It is **enforced by test**, not by habit: a test asserts that the `.ora` writer
  module exposes no read entry point and that no module under `scripts/` opens a
  `.ora` for reading (a source-level assertion, which is what makes the refusal
  survive the arrival of `zipfile`-short familiarity), alongside the structural
  assertions of §2 and §3 and the lint refusal of §4. `ARTIST.md` and
  `docs/remastering-a-game.md` §4 say it in the page the artist reads: the `.ora`
  is a starting point, the flat PNG is the deliverable.

Why refuse something that costs an afternoon. Reading `paint` back makes the
`.ora` a second source of truth, which is the one thing ADR-0183 §1 exists to
prevent, and it breaks §4's acceptance test: the round trip is measured as an
unchanged set of `(tileData, palette)` keys in the rebuilt `hires.txt`, and an
`.ora` has no such keys and no pack identity — it is a document, not a pack
member. Worse, its layers carry *our* pixels: the guides grid, the captions, the
swatch strip. Reading it back would launder our own drawing into the pack as art,
and would leave the sheet PNG as a stale copy of a file we no longer own — the
exact drift the twin-diff mechanism was built to make impossible.

### 6. What is not shipped

No `.psd` writer, no `.aseprite` writer, no `.kra` writer; Photoshop and Aseprite
do not open `.ora` and stay on F12.4's per-layer asset names (ADR-0213 §3) — and
`docs/remastering-a-game.md` states the omission where the artist chooses a
program. No watcher (ADR-0209 Q3 (g)). No interactive layer tool: the kit writes
the file, the artist's own program edits it. No Core/UI change, no new
`hires.txt` construct, no new pack member, no download, no dependency (ADR-0165,
ADR-0154). And no claim that this path is the default: it is a second path beside
F12.4, and the measured artist evidence so far (Metroid, a spreadsheet user) does
not show a GIMP/Krita population.

### 7. Acceptance — the row's four stop conditions, with two corrections

1. `stack.xml` validates against the shape the three programs read, and each
   `.ora` round-trips through `zipfile` unchanged → §2, including the `mimetype`
   member the row's file list omits.
2. GIMP and Krita open both files with every layer named (five and four) and
   `paint` selected → a person, per the phase's cold-read rule. The file states
   its intent as strongly as the format allows (§3); "selected" is the reader's
   behaviour and it is the log, not the file, that reports it.
3. A stroke on `paint`, exported flat, reaches the game pixel-exact via F12.3
   with the unchanged cells dropped → §5's return path.
4. The same export with `guides` left visible is refused by lint with the
   offending cell named → §4; and, by the same check, so is `palettes`.

What we measure stays ours: file validity, layer order, refusal, pixel-exact
result.

*Amended 2026-09-23.* Stop condition (2) is **partly measured**: on one
four-layer sheet (`usr000.ora`, Contra), every layer named in both readers;
`paint` **not** selected by either (§3 amendment). Under the user's option (b)
the "`paint` selected" clause is read as "`paint` selected after the
documented click", since the file cannot achieve it. The condition is still
**open**: it needs a person's log on the regenerated files, including a
five-layer recorded surface (with `context`), which nobody has opened yet. Stop condition (4) is exercised end to end in the follow-up log
(`docs/validation/f12.11-stop3-and-gimp-findings-2026-09-23.md` §2: a
guides-visible export fails the lint one error per cell, a paint-only export
passes with the tile-key set unchanged). Stop condition (3) stays unevaluated
for the reason that log gives: the one cell painted so far is gated by
`spriteNearby`, and the final-frame capture never landed on its pose.

## Consequences

- **A kit surface is now four files**, and one of them is a zip containing the
  other one: the kit gets bigger, and both the `.ora` and its contents are
  derived, regenerable and throwaway like the rest of the kit (ADR-0183 §1).
- **The kit asserts something about its own artwork before writing.** If a
  palette ever made `#FF00FF` reachable, kit generation fails at write time; that
  is a deliberate loud failure, cheaper than shipping a guard that silently
  stopped guarding.
- **`edit-locked` may be ignored.** GIMP is not required to honour it, so `orig`
  is protected by the export target and the twin diff, never by the flag. An
  artist who paints on `orig` produces cells that differ from the twin and are
  therefore kept as painted — which is why `ARTIST.md` says what it says about
  `.orig.png` and the `.ora` repeats it.
- **An enlarged canvas is now normal for a surface with a `context` layer.** The
  twin grows with it, `_EditedProbe`'s size rule (#346) applies unchanged, and
  anything that assumed a sheet is exactly `columns × stride + gutter` wide must
  not assume it — the sidecar's cell rectangles are the contract, not the image
  size.
- **The sentinel check is a lint error, so a wrong export fails loudly instead of
  shipping grid lines**, which is the PRD's own risk row for this file. The cost
  is one new lint rule that reads sheet PNGs pixel by pixel: a bounded, per-cell
  scan, not a whole-image pass.
- **The write-only rule is enforced by a source-level test**, which is a coarse
  instrument: it catches a module that opens a `.ora`, not a future design that
  reads one inside a helper with a different name. Its value is that the refusal
  is *dated and checkable* rather than an unwritten convention — a later ADR that
  wants the read must amend this section, not slip past a comment.
- **The F12.4 path is untouched**, so an artist with Photoshop, Aseprite or a
  spreadsheet loses nothing and gains nothing: this ADR adds a file, not a
  workflow.

## Alternatives

- **Write `.psd` instead.** Refused. PSD is a proprietary layout with no
  public specification to validate against, so the first stop condition ("the
  file is valid") would degrade into "Photoshop opened it once"; the readers are
  Photoshop-centred while the population we have measured is not; and it would
  bind the kit to the one program ADR-0213 §4 already had to work around.
- **Write `.aseprite`.** Refused. Same shape of problem with less upside: it is
  one program's format, it is not what the three readers the row names open
  natively, and Aseprite is already served by F12.4's names.
- **One layer per cell.** Refused. It makes the layer panel the sheet's index,
  which is exactly the reverse-engineering the `guides` captions and the sidecar
  exist to remove: a static page would carry 512 layers, and a figure sheet one
  per cell of every cycle. It also makes the layer count a function of coverage,
  so the file's shape changes whenever coverage does, and it breaks the freedom
  ADR-0209 Q2 considered for a stroke that spans cells. One `paint` layer keeps
  the shape constant and puts the index where it belongs — in the drawing.
- **Bake the grid and captions into the reference PNG.** Refused. The
  `*.orig.png` twin is the pixel-exact reference the rebuild diffs against
  (ADR-0153 §3/§4); a baked grid would move every repainted cell's baseline,
  make the hatch indistinguishable from art, and add grid lines to every painted
  cell on export. It also cannot be toggled, which is the only reason the two
  drawn layers cost anything to the artist.
- **Read `paint` back out of the `.ora`.** Refused, and it is the alternative
  that is genuinely tempting because it is stdlib-trivial. It makes the `.ora` a
  second source of truth (ADR-0183 §1), gives the pipeline a file with no
  `(tileData, palette)` keys to round-trip against (ADR-0183 §4), and reads back
  our own drawing — grid, captions, swatches — as art. The sheet PNG would become
  a stale copy of a document we no longer own.
- **Take `mergedimage.png` as the export.** Refused. The composite contains every
  visible layer, and "visible" is the artist's transient view state, so the same
  file yields different packs depending on what was toggled mid-session — the
  precise failure the sentinel check exists to catch — and nothing in the file
  records which state produced it. One flat PNG over one name has one meaning.
- **Two layers: `orig` and `paint`.** Refused as a design and kept as the floor.
  It discards the two things the file is for — the cell index the artist would
  otherwise reverse-engineer, and the palette strip they cannot otherwise see in
  one place — and it leaves the export unguarded, since with no drawn layers there
  is nothing to catch. Both additions are hidden, so they cost the artist nothing
  at export and everything at the moment they are wanted.
- **Ship the `.ora` as the pack's own surface file** (a pack member, so
  third-party tools could read it). Refused. It puts a container we cannot
  validate for others' readers inside the artefact whose meaning is a
  `hires.txt`, and it invites exactly the read-back this ADR refuses; ADR-0183
  §1's "never into the recording" already answers it.
