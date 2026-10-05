# ADR-0154: The optional AI repaint is an external, backend-pluggable script; its output lands in `auto/repaint/`, is labelled `generated`, and is never catalog-eligible

- Status: accepted
- Date: 2026-09-05
- Related: PRD Part A §4 "Phase 9" (slice F9.6, validation test 8), ADR-0153 §4 (sidecar `cells[].tiles[].tile` / `.palette`), ADR-0049, ADR-0147, ADR-0050, ADR-0005, ADR-0140, ADR-0152, ADR-0148, ADR-0138 §41, ADR-0192, MEP-v1 §3.1/§3.2, CLAUDE.md "Community HD/MEP Pack triage"
- Consolidates: ADR-0161
- Superseded by: ADR-0192 for §2 Option A only

## Decision

1. **One external script, one pluggable backend seam.** `scripts/sheet_repaint.py` — argparse, standard library only (`struct` + `zlib` for PNG, no Pillow, no numpy), like `mep_build.py` and `sheet_report.py`. It reads a sheet and its ADR-0153 §4 sidecar JSON (`textures/sheets/*.json`), builds the nearest-upscaled control image and the per-cell crop list from `cells[]` (contact sheets) or `placements[]` (maps), applies palette-variant recolouring (§5), the seam pass (§6) and alpha from the source (§7), and writes to `auto/repaint/` labelled `generated` (§3). Backends sit behind `RepaintBackend`; `RepaintRequest` carries the 1x source, the nearest-upscaled control image, the scale factor, the sheet's `kind` and the crop list. The backend returns one RGB or RGBA image at the control image's exact size; anything else is a hard error. Alpha, recolour, seams and I/O run *after* the backend, so a backend that drops alpha or shifts the palette is corrected, not trusted. `--backend` selects `passthrough` (alias `null`; deterministic, the control arm of PRD test 8), `classical` (in-repo Scale2x/Scale3x), `esrgan` (a local Real-ESRGAN), `diffusion` (§2). A backend that cannot run MUST fail before anything is written: `esrgan` and `diffusion` probe availability and fail with `RepaintError` naming what is missing, and **never download anything**; `diffusion` also refuses a non-loopback endpoint, so "nothing leaves the machine" is enforced in code.

2. **Local-only backends; Option B rejected; Option C kept as the baseline.** Decided 2026-09-05 as Option A — a `diffusion` backend driving a **locally running** ComfyUI or `diffusers` with the §1 control image as the ControlNet hint, weights a download this repo, script and CI never perform. **Superseded 2026-09-15 by ADR-0192** (Phase 11 C.8): diffusion is retired until a measured run exists; `passthrough` and `classical` are the supported backends. Output licensing was never verified (SD 1.5, SDXL 1.0, ControlNet, `diffusers`, Real-ESRGAN; no `LICENSE.md`); it held because this project publishes nothing — `docs/community-packs.json` catalogs links (ADR-0148), and the repaint lives in `auto/repaint/`. Option B (a hosted API, Stability's "Control: Structure" or equivalent) stays **rejected**: "We do not publish" does not rehabilitate uploading `repo/ROM` content to a third party; if ever wanted it is an explicit `--backend stability` needing an API key from the environment and an explicit `--allow-upload` flag (pricing ~5 credits / ~40 credits, unverified). Option C ships as the baseline because PRD test 8 is a blind A/B needing a B: `classical`, an in-repo pure-Python `hq2x/xBRZ`-family scaler (Scale2x/Scale3x), has **zero** dependencies and cannot add a colour not already in the cell, so "it looks better than the raw pixels" is never the only result. First target is the captured screen, not the metatile sheet: `sheet_repaint.py` takes `--target sheets|screens|both`; the `--target screens` path reads the recorder's `hires.txt`, repaints every `<background>` target it resolves (`backgrounds/screenNNN.png`, with `tileAtPosition` anchors) and re-emits a self-contained `hires.txt` with the same condition-prefixed lines and `<scale>` multiplied by the repaint factor; the sheet path (`metatiles.png`, `map-NNN.png`) is secondary.

3. **The label: a `generated` object at the root of `pack.json` — disclosure, not a gate.** MEP-v1 §3.2 makes unknown fields ignorable, so this is additive:

   ```json
   {
     "mep": "1.5.0", "name": "Zelda — machine repaint", "version": "0.1.0",
     "generated": { "by": "sheet_repaint", "backend": "passthrough",
                    "date": "2026-09-05", "scale": 4, "source": "auto/textures/sheets" }
   }
   ```

   Presence of the object is the label; `by` and `backend` are required. The field is deliberately not `ai` — `classical` output is labelled all the same, because the catalog cares about "no human drew this", not which algorithm did not. `sections` describes the tree written; `targets` is **inherited** from the nearest manifest (`<Game>/mep/pack.json`, `<Game>/pack.json`, …), never invented — `mep_lint` errors on a missing `targets`, so a repaint with nothing to inherit names the fix (`mep_build pack --rom <ROM>`). `generated` carries no verdict: `scripts/mei_catalog_entry.py` copies it into the row metadata alongside `deps` and `scripts/generate_community_pack_catalog.py` renders it as a column next to the `author` column `docs/community-packs.md` carries. "Local only" is one submission from false (every accepted pack auto-installs under ADR-0146), so the column keeps a generated pack from being listed silently beside hand-painted ones. An early draft made the field a `mep_lint` error (`invalid`), taking the artist's decision away; a submitter can still delete the field — an honesty mechanism, not a detector. Accepting this bumped MEP-v1 to **v1.6** (§3.1); no `mep_lint` rule, no verdict, no de-listing.

4. **Output goes to `auto/repaint/`, never over the recorder's sheets.**

```
<Game>/
  auto/
    textures/sheets/      # ADR-0153 recorder output — untouched, wiped by the
                          # next bootstrap_auto_packs.sh run
    repaint/              # pack.json (§3 `generated`), textures/sheets/:
                          #   metatiles.png, metatiles.orig.png, metatiles.json
  mep/                    # the artist's pack — this tool never writes here
```

`auto/` because ADR-0049 makes provenance a matter of location ("under `auto/` is machine-made; outside it is human-made") and ADR-0147 keeps `auto/` and `mep/` siblings. A **sub**folder, not `auto/textures`, because `scripts/bootstrap_auto_packs.sh` does `rm -rf "$folder/auto/textures"` on the next bootstrap and overwriting the recorder's sheets destroys the reference PRD test 8 compares against. The `*.orig.png` twin is copied on purpose: `mep_build.py` decides whether a cell *claims* a tile key by diffing against that 1x twin (ADR-0153 §4), so a repainted cell differs and claims its keys. Building needs the recorder's keys — a documented two-step:

```
python3 scripts/mep_build.py build <Game>/auto/repaint \
    --source <Game>/auto/textures/hires.txt
```

The tool never writes into `mep/`.

5. **Palette variants are recoloured from one generation; the correspondence is read positionally.** Cells sharing the `tiles[].tile` tuple (palette ignored) are one shape group; the same 8×8 shape under several NES palettes (`tiles[].palette`) would otherwise get a different silhouette per variant, breaking every seam. The highest-`count` member is canonical and the only one generated; others are recoloured from the canonical *generated* pixels:

   1. Read the canonical and variant colours in NES colour-index order (0..3); index *i* pairs with index *i*. **Read positionally, not from the palette bytes** (ADR-0161): a sheet renders through `Core/NES/HdPacks/SheetRender.h` and `HdPackBuilder::_palette`, the emulator's replaceable 512-entry master palette, so the sidecar's `tiles[].palette` (a byte string like `"0F20210F"`) cannot become RGB here (`scripts/spike_tile_sheets.py`'s fixed table disagrees under a custom palette). `shape_key` groups on `tiles[].tile`: both cells share a CHR bitmap, so the pixel at offset `(dx, dy)` carries the same 2-bit index in both — **same offset is same index**. `palette_correspondence(img, canon, variant)` returns `canon_palette` (every opaque canonical colour, first-appearance order; step 2's nearest search uses all of it), `mapping` (canonical RGB → variant RGB where unambiguous) and `missing` (no counterpart). `missing` colours degrade to identity, announced **on stderr** unconditionally, not under `--verbose` — it says so on stderr rather than inventing a mapping. The prior frequency ranking tied on every shape-group cell (same bitmap ⇒ same counts) and broke ties by RGB (canonical `(10,10,10)`/`(200,200,200)` vs variant `(250,0,0)`/`(0,0,5)`), seeing only "the variant uses fewer colours than the canonical" and dropping the tail; positional pairing catches the `ambiguous` case.
   2. For each generated pixel, keep the residual `pixel - palette[i]` to the nearest canonical colour in RGB.
   3. Emit `variant_palette[i] + residual`, clamped to 0..255.

   Shading and dithering survive as residuals; the silhouette is identical (one generation, one alpha mask); transparent pixels are skipped. The trap: a variant's own pixels are discarded — its geometry is the canonical cell's, a no-op while the group holds and wrong once two cells share a shape tuple (the smaller-`count` variant inherits the bigger one's art). `--no-variants` generates every cell independently; `count` makes the victim predictable.

6. **The seam pass symmetrises the border band of adjacent cells.** A naive per-cell repaint fails PRD test 4 (a continuous diagonal stripe across a map, replayed with no doubled or missing column). Adjacency is read from ADR-0153's data: a map's `placements[]` gives origins and vocabulary indexes, so border-sharing placements give an ordered pair `(vocab_a, side, vocab_b)`; objects contribute the same through `cells[].metatile`. The pair table applies on the map and back on the contact sheets (cells separated by ADR-0153's 1-cell gutter but must still tile in game). For each pair and offset `j` in `0..W-1` from the border (`--seam-width`, default 1, in **1x** pixels × scale), with `W` the band width:

   ```
   f = 0.5 * (W - j) / W ;  a[edge-j] = (1-f) * a[edge-j] + f * b[j]
   b[j] = (1-f) * b[j] + f * a[edge-j]     (pre-blend values)
   ```

   At `j = 0` this is the plain average of the touching columns, so the sides agree at the border; both writes use pre-blend values (symmetric, order-independent). The pass **only** writes inside a cell's `W`-pixel border band — never the interior, never the gutter (transparent; `mep_build` does not slice it) — and skips a pair where either side is fully transparent, so it cannot grow or erode a silhouette. A cell with several neighbours on one side gets their mean, the cost of not keeping a per-adjacency copy (a copy per neighbour would break ADR-0153's vocabulary).

7. **Alpha comes from the source, always.** Backends are assumed to lose alpha. The script takes the alpha from the nearest-upscaled source, applies it to what the backend returned, and zeroes the RGB of fully transparent pixels so no halo leaks into a crop. Nearest-neighbour, never resampled (a soft alpha edge on an 8×8 NES tile is wrong by construction; PRD test 8 fails the slice on "any visible alpha loss on sprites"). A backend that returns RGBA does not override this — its alpha is discarded, said once per sheet at `--verbose`.

## Context

The PRD lists this as F9.6 and guards it with validation test 8 (a blind A/B against the artist pack, three reviewers, five screens), explicitly "go/no-go by the user". Three constraints: (1) **copyrighted art** — a sheet is a local per-user artifact (what `auto/` already is), exactly what must not be pushed to a third party or published; (2) **catalog provenance rules** — the catalog lists *artists'* packs (`docs/community-packs.md` reads an "Author" column off the pack, never the submitter's login; ADR-0148 wants a self-contained, verifiable artifact), so the PRD says the repaint is "labelled as generated in `pack.json`, never eligible for the community catalog as an artist pack"; (3) **no AI inside the emulator** — nothing links into `Core/`, loads weights at runtime, or adds a setting.

**Non-goals.** Training a model, shipping weights, bundling a model, calling a model from the emulator, generating audio, generating *new* art — this repaints existing cells with the original as the structure constraint. Cross-machine reproducibility is also not a goal; only `passthrough` and `classical` are deterministic.

## Consequences

- Footprint is the Python scaffold plus tests: no `Core/` file, no setting, no runtime dependency, no installer weight.
- A green suite says nothing about the *generated* art; only PRD test 8 (human) does. The `diffusion` backend has **never been executed**.
- The two-step build is a papercut; hiding it would mean teaching `mep_build` a repaint layout, which is worse.
- A `generated` pack that reaches the catalog with the label stripped is not detected — a known, accepted hole; ADR-0148's de-listing rules the social remedy.
- A generative backend is not reproducible: `content_id` (ADR-0139/0141) over a repainted folder changes every run, so a repaint must never feed an automated update loop.
- Option A's RAIL restrictions travel with any redistribution, though this project distributes nothing from `auto/`; anyone who publishes a repaint inherits the RAIL terms and the copyright in the original art.

## Record

- 2026-09-05 — written `proposed` (§2 was an either/or); accepted the same day with Option A, reflected in `scripts/sheet_repaint.py`; the first draft wrongly made `generated` a `mep_lint` error (`invalid`), corrected to disclosure in §3.
- 2026-09-06 — ADR-0161 (`docs/adr/sprites/0161-palette-variant-correspondence-is-positional.md`) recorded §5 step 1's positional correspondence (mechanism, not result), tests in `scripts/test_sheet_repaint.py`; consolidated here.
- 2026-09-15 — §2 Option A superseded by ADR-0192 (Phase 11 C.8): diffusion retired until a measured run; `passthrough`/`classical` remain; §4 and the `generated` label stand.
