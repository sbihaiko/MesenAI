# ADR-0220: A layered `.ora` is written beside every surface, write-only for the toolchain, its five layers ordered so `paint` is the topmost visible one — and the flat PNG over the F12.4 name stays the only return path

- Status: accepted (2026-09-22). Build go-ahead, verbatim: *"dispara as frentes 1, 2, 3 e 4 em paralelo usando workflows"*. Two deviations decided by the user: *"Emendar §3 e o PRD (Recommended)"* (the `context` layer exists only where every cell has a stage position) and *"Trocar para #FF00FD (Recommended)"* (the sentinel; `#FF00FF` collides with the recorder's unpainted fill). In code: `scripts/ora_writer.py` and `scripts/mep_sentinel.py`, called from `compose_engine.py`, `artist_chr_kit.py` and `mep_figure.py`; `mep_lint.py` fails a cell carrying the sentinel; `scripts/test_ora_writer.py`. Stop condition (2) is open by the owner's decision (2026-10-05).
- Date: 2026-09-20
- Related: PRD Part A F12.11, F12.3, F12.4, F12.9; ADR-0183 §1/§2/§3/§5; ADR-0219 §1; ADR-0210; ADR-0209 Q2/Q3; ADR-0213 §3; ADR-0212; ADR-0153 §3/§4; ADR-0164; ADR-0172; ADR-0165; ADR-0154; MEP-v1 §5; `docs/remastering-a-game.md` §4
- Amends: ADR-0183 §2 — a fourth file per surface, the layered container (a fifth file kind in the kit). §2's four **surfaces** are unchanged, and ADR-0213's name contract is not amended: the `.ora` shares a validated stem and is never a painting-surface name.

## Decision

### 1. One `.ora` per surface, in the kit, named from the surface

Beside every surface PNG the kit writes `<name>.ora`, in the folder that surface already lives in (`sheets/`, `chr/`, `map/`) and in the same pass, from the same in-memory canvas, as `<name>.png` and `<name>.orig.png` — so the `orig` layer is the twin's pixels and the merged image is the composite the flat PNG was written from; the three files cannot disagree by construction. ADR-0213 §3 already stamps `assetName`, and the `.ora` is that name's stem with `.ora` for `.png`. `check_asset_set`'s folder rule (case clash, Windows-reserved stems, the 100-character budget) covers the pair; `check_asset_name` — which refuses a name that is not exactly `*.png` — is never applied to a `.ora`, which is never a manifest target (ADR-0151) and never sits in a pack folder: the kit is beside the pack (ADR-0183 §1).

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

A `.ora` is a zip whose **first** entry must be `mimetype`, stored uncompressed, holding exactly `image/openraster` with no whitespace and no trailing newline; `stack.xml`, `mergedimage.png` and `Thumbnails/thumbnail.png` come after. Members are written in that order, with a fixed timestamp and compression, so the same inputs produce the same bytes — which makes "each `.ora` round-trips through `zipfile` unchanged" a test rather than a hope. Layer PNGs use `mep_build`'s stdlib PNG writer.

`<layer>` requires `src`, and accepts `name`, `x`, `y`, `opacity` (a float from `0.0` to `1.0`, default `1.0`), `visibility` (`visible`/`hidden`) and `composite-op` (default `svg:src-over`). `edit-locked` is not in the 0.0.5 baseline: it is a documented extension, an `xsd:boolean` defaulting to `false`, written because ours is an editing workflow, never as a boundary.

`mergedimage.png` is the composite of the layers **as the file opens** (the hidden ones hidden), so a reader that ignores the stack still shows a true image and never sees the sentinel; it is a display cache, and §5 forbids reading it back.

### 3. The five layers, exactly

Bottom to top; flags are `stack.xml` attributes. Five layers on a recorded surface, four on an F12.9 static page.

| # | name | flags | content | present |
|---|---|---|---|---|
| 1 | `orig` | `visibility="visible"`, `opacity="1.0"`, `edit-locked="true"` | the `*.orig.png` twin's own pixels, cell for cell | always |
| 2 | `context` | `visibility="visible"`, `opacity="0.5"` | the 1x stitched-map crop around the subject (ADR-0164 placements), placed in the canvas's context band | iff every cell has a stage position |
| 3 | `paint` | `visibility="visible"`, `opacity="1.0"` | fully transparent, zero alpha everywhere | always |
| 4 | `guides` | `visibility="hidden"`, `opacity="1.0"`, `edit-locked="true"` | sentinel-only drawing: cell grid on cell and gutter boundaries, captions from `names.json` or the sidecar ids (ADR-0183 §5), hatch over every `seen: false` cell (ADR-0183 §3, ADR-0210) | always |
| 5 | `palettes` | `visibility="hidden"`, `opacity="1.0"`, `edit-locked="true"` | the swatch strip, sentinel-framed (§4) | always |

- **`paint` is the only layer that is the artist's.** A stroke elsewhere is work the kit discards on the next run (ADR-0183 §1 — the recording is the expensive artefact, the kit is throwaway), which is why `orig`, `guides` and `palettes` carry `edit-locked` and `context` does not.
- **`paint` is the topmost visible layer by construction**, because the two above it are hidden — the strongest statement `stack.xml` permits; stop condition (2)'s "`paint` selected" is the reader's half.
- **Nothing we draw can reach a cell by accident.** `context` is clipped so no pixel falls inside a cell rectangle; `guides` and `palettes` may cross cells and both are caught by §4's sentinel. `mep_build._EditedProbe` decides "was this cell painted?" by comparing the cell against the upscaled `*.orig.png` twin, so a stray pixel of ours is rebuilt as art (ADR-0153 §3/§4).
- **`context` is present iff position, not iff recording.** The row's "five on a recorded surface, four on an F12.9 static page" is its own two bounded inputs; generally, a layer whose content is "the place around this cell" cannot exist for a cell with no place. Static pages have four (ADR-0219); a recorded `chr/` pattern page has four because it is index-keyed, not placed (ADR-0172, ADR-0183 §2.4); recorded figure, scenery and map sheets have five.
- **The canvas gains a context band, and the twin grows with it.** A surface with `context` is larger than `columns × stride + gutter`; the `*.orig.png` twin is written on the same canvas (blank in the band), because `_EditedProbe` refuses a mismatched pair (#346). Enlarging is safe for the reload: F12.3 refuses a *smaller* image (ADR-0212).
- **On a static page `palettes` says what the page is.** Every cell is a `fill` with no observed palette and `defaultTile = Y` (ADR-0210), so the strip states the wildcard in the sentinel, and the `guides` hatch covers the whole page — "nothing here was seen in play", the sentence `ARTIST.md` opens with (ADR-0183 §3, ADR-0219).

*Amended 2026-09-22.* `context` iff every cell has a stage position yields: today no recorded artefact gives a sprite pose or a scenery cell a stage position (`poses.json` and `adjacency.json` carry screen floor bands only; the recordings ship no `map-NNN.json` `placements[]`), so `context` is present on the `artist_map` stage panoramas and absent on figure, scenery and CHR sheets — "five on a recorded surface" reads **five on a stage panorama, four elsewhere until a sprite-position source exists**. The follow-up is named: derive a sprite's screen position from the ADR-0222 OAM dump, place it with the grid-dump camera recovery `artist_map.py` already does, and hand the 1x crop to `compose_engine.Pack.export(context=)`. The row's Contra input is a four-layer file today; stop condition (2) reads "five and four" as "panorama and figure page" once that lands.

*Amended 2026-09-23 (stop condition (2) measured; user's option (b)).* **Both readers open the file with the bottom layer active, not the topmost visible one.** On the Contra kit's `usr000.ora`, GIMP 2.10 (`file-openraster.py` inserts the layers in `stack.xml` order with no "set active" call, so the bottommost stays active) and Krita 5.3.4 both land on `orig`, whatever the order; only Krita honours `edit-locked`. The rule above stands as written — "topmost visible by construction" is still the strongest statement the file can make; this records that it is not enough for the two readers we care about. Decision **(b): keep §3's order and document the click** — `ARTIST.md` (written by `artist_kit_assemble.py`) and `docs/remastering-a-game.md` tell the artist to "select `paint` before painting", and why. Also from this date `guides` captions are **fitted, never clipped**: `ora_writer.draw_text` wraps to the canvas's right edge, at most two lines above the next cell row, at half the grid's glyph size, ending in `...`.

### 4. The sentinel, and what catches a wrong export

**The sentinel is `#FF00FD` at alpha 255 (RGBA `255,0,253,255`).** A NES palette quantised to 6 bits per channel never reaches `0xFF` (max `0xFC`; the magenta carried is `#FC00FC`, one step off). The original `#FF00FF` at `255,0,255,255` is refused on measurement: the recorder paints an unrecorded CHR cell `0xFFFF00FF` (`HdPackBuilder.cpp`, mirrored as `artist_chr_kit.UNPAINTED_RGBA`), so a recorded page with an `empty` cell carries the triplet in its own artwork. `#FF00FD` (no 6-bit channel reaches `0xFD`) collides with nothing the recorder writes. Before writing, the kit asserts the triplet occurs nowhere in the sheet or its twin — checked absent, never argued absent; a palette that made it reachable stops kit generation loudly. The triplet is spelled once in `scripts/mep_sentinel.py`, read by the assertion, the `mep_lint.py` scan and the knock-out captions. `mep_lint.py` scans each cell rectangle for exact sentinel pixels (RGBA, no tolerance) and errors, naming the sheet and the cell's `index` and `(x, y)`.

**`palettes` is sentinel-framed** — one cell-tall band, row-aligned to the first cell row, sentinel background, one-pixel insets top and bottom, one-pixel sentinel column between swatches — so leaving it visible fails the same check as a visible `guides`. A stroke over the sentinel inside a painted cell is a blend, not a failure, which is why every drawn layer is opaque (`opacity` `1.0`); a `context` band left visible is harmless by §3's clipping rule.

*Amended 2026-09-23 (follow-up, defect (b)).* The band is in first-use order and labelled: `ora_writer.first_use_palettes` lists each palette once where first used in reading order (row, then column) and knocks the first cell's label (`index`; the hex tile index on a CHR page) out of the sentinel before its group, so "row 0's colours" is no longer whichever palette sorted first. To make room a swatch is a quarter of the band wide; a group that does not fit is not drawn and `+N` is knocked out.

### 5. Write-only, and the refusal is deliberate

`.ora` is written by the kit and read by nobody in the toolchain: no script under `scripts/` opens one for reading — not `mep_build.py build`, not `sheet_repaint.py`, not a kit generator, not `mep_lint.py` (the §4 check reads the flat PNG and its sidecar), not `mep_import.py`. `mergedimage.png` is never read back either: treating it as the export would make the pack depend on which layers happened to be toggled. **The return path is the flat PNG over the F12.4 name** (ADR-0213 §3) and nothing else: the artist exports flat over `<name>.png`, `mep_build._EditedProbe` keeps only cells that differ from the twin, the rebuild is verified as ADR-0183 §4 requires, and F12.3's reload shows the result without reopening the ROM (ADR-0212). ADR-0209's Q3 stays **(h), same path, explicit re-import**; (g)'s watcher is not opened. The refusal is **enforced by test**, not habit: a test asserts the writer module exposes no read entry point and no module under `scripts/` opens a `.ora` for reading. `ARTIST.md` and `docs/remastering-a-game.md` §4 say it: the `.ora` is a starting point, the flat PNG is the deliverable.

Reading `paint` back makes the `.ora` a second source of truth (ADR-0183 §1) and breaks §4's acceptance test — the round trip is an unchanged set of `(tileData, palette)` keys in the rebuilt `hires.txt`, and an `.ora` has no such keys — and it would launder our own drawing (the grid, captions, swatch strip) into the pack as art, leaving the sheet PNG a stale copy: the drift the twin-diff mechanism was built to make impossible.

### 6. What is not shipped

No `.psd`, `.aseprite` or `.kra` writer; Photoshop and Aseprite stay on F12.4's per-layer names (ADR-0213 §3). No watcher (ADR-0209 Q3 (g)). No interactive layer tool. No Core/UI change, no new `hires.txt` construct, no new pack member, no download, no dependency (ADR-0165, ADR-0154). This path is not the default: it is a second path beside F12.4, and the measured evidence (Metroid, a spreadsheet user) shows no GIMP/Krita population.

### 7. Acceptance — the row's four stop conditions

1. `stack.xml` validates and each `.ora` round-trips through `zipfile` unchanged → §2.
2. GIMP and Krita open both files with every layer named (five and four) and `paint` selected → a person: "selected" is the reader's behaviour; the log reports it.
3. A stroke on `paint`, exported flat, reaches the game pixel-exact via F12.3 with the unchanged cells dropped → §5's return path.
4. The same export with `guides` left visible is refused by lint with the offending cell named → §4; so is `palettes`.

## Context

The F12.11 row needs an ADR before start: it adds a fifth file kind to ADR-0183 §2's surfaces, fixes the layer contract, and must state that `.ora` is write-only (reading `paint` out of it is stdlib-trivial and refused on purpose, or the sheet stops being the source of truth). Four format facts changed it: the row's file list omits the required `mimetype` member (§2); `edit-locked` is an extension, not baseline (§2); **no OpenRaster attribute selects a layer**, so "`paint` is the selected layer" is a reader's behaviour and the most the file can do is make `paint` the topmost visible (§3, §7); and the row hung the wrong-export guard on `guides` while calling `palettes` "a swatch strip … hidden" with no guard, which §4 closes.

Write-only is a decision, not a difficulty: reading `paint` back is `zipfile` plus `xml.etree.ElementTree` plus `mep_build.py`'s own PNG decoder. Nothing technical stops it; this file does. Non-goals: no `.psd`/`.aseprite`/`.kra` read or write; no watcher, editor, renderer, session, undo, or Core/UI change; no `.ora` inside a pack; no decision on F12.9 (ADR-0219) or the third-party index import (ADR-0210 §3, F12.12); F12.11 is not the default.

## Consequences

- A kit surface is now four files, one a zip containing the other: derived and throwaway like the rest of the kit (ADR-0183 §1).
- The kit asserts its own artwork before writing: a reachable sentinel fails generation loudly rather than silently disarming the guard.
- `edit-locked` may be ignored: `orig` is protected by the export target and the twin diff, never by the flag.
- An enlarged canvas is normal for a `context` surface; the sidecar's cell rectangles, not the image size, are the contract.
- The sentinel check is a per-cell lint error, so a wrong export fails loudly instead of shipping grid lines.
- The write-only rule is a coarse source-level test: it catches a module that opens a `.ora`, not a helper that reads one under another name; a later ADR must amend §5, not slip past a comment.
- A contradiction left standing: the container is hardened against stray reads, yet the row presupposes the three programs open `.ora`; no measurement here confirms MyPaint.
- The F12.4 path is untouched — an artist with Photoshop, Aseprite or a spreadsheet loses nothing.

## Alternatives

- **Write `.psd`.** Refused: no public specification to validate against, so "the file is valid" would degrade into `("the file is valid") would degrade into "Photoshop opened it once"`, and it binds the kit to Photoshop.
- **Write `.aseprite`.** Refused: one program's format; Aseprite is already served by F12.4's names.
- **One layer per cell.** Refused: it makes the layer panel the sheet's index (512 layers on a static page) and the layer count a function of coverage.
- **Bake the grid and captions into the reference PNG.** Refused: it moves every painted cell's baseline and makes the hatch indistinguishable from art.
- **Read `paint` back.** Refused, the genuinely tempting one because stdlib-trivial: it makes the `.ora` a second source of truth and reads back our own drawing.
- **Take `mergedimage.png` as the export.** Refused: the same file yields different packs depending on what was toggled mid-session.
- **Two layers: `orig` and `paint`.** Refused as a design, kept as the floor: it discards the index and the palette strip and leaves the export unguarded.
- **Ship the `.ora` as a pack member.** Refused: ADR-0183 §1's "never into the recording" already answers it.

## Record

- 2026-09-20 — proposed; deferral reason recorded (no GIMP/Krita artist population measured).
- 2026-09-22 — accepted (*"aceito o F12.11. nao implemente ainda."*); code landed the same day; stop conditions (1) and (4) met by the automated pass.
- 2026-09-22 — §3 amended (`context` only where every cell has a stage position); §4 amended (`#FF00FD`, not `#FF00FF`).
- 2026-09-23 — (3) corrected to unevaluated (PR #384), then met by F14.1 (`docs/validation/slices/f14.1-painted-round-trip-2026-09-23.md`): a stroke on `paint` of the Contra kit's `usr003.ora`, exported flat over `usr003.png`, is pixel-exact after the F12.3 reload, unchanged cells dropped; the earlier painted cell was gated by `spriteNearby`.
- 2026-09-23 — (2) partly measured; option (b) documents the click; captions fitted, `palettes` band labelled by first use (`docs/validation/slices/f12.11-stop3-and-gimp-findings-2026-09-23.md`).
