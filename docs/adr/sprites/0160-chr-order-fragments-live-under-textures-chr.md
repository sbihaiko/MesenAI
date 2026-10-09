# ADR-0160: The CHR-order fragments live under `textures/chr/` — `sheets/` is the front door

- Status: accepted (2026-09-05, by the user — a decision already reflected in the code: the move shipped in `a2139da6`, the §3 sweep in `2b12c5e2`)
- Date: 2026-09-05
- Related: PRD Part A §4 "Phase 9" (slice F9.10), ADR-0153 §3/§5, ADR-0050, ADR-0049, ADR-0147, ADR-0005, MEP-v1 §2.1/§5.1
- Supersedes / amends: amends ADR-0153 (the bootstrap pack's top level becomes three folders and a manifest; the CHR-order layer inherited from ADR-0049 moves one level down) and ADR-0049 (`Chr_XX_N.png` leaves the pack root)

## Context

`HdPackBuilder::SaveHdPack` writes one 16×16-cell PNG per CHR bank/page, in CHR order, named in `<img>`, always next to `hires.txt`. On the 30-ROM library that is **12993 fragments** — 7.1 MB on Punch-Out!! alone — and since ADR-0153 they sit beside the surfaces they were meant to be replaced by (`sheets/`, `backgrounds/`). `Chr_00_0.png` sorts first; an artist meets a wall of 8×8 fragments.

They cannot be deleted. `hires.txt` renders from them (every `<tile>` is `<img>` index plus a pixel offset into that image), and the sheets under `sheets/` are a *paint* surface `scripts/mep_build.py` slices back into `<tile>` lines — not a rendering layer (ADR-0153 §6). Remove the fragments and the pack renders nothing. So the question is only *where they go*, and what happens to the packs already on disk.

Non-goals: changing what the fragments contain, their geometry or CHR order; changing `hires.txt` semantics; a per-pack option; touching a third-party HD Pack's layout — an external pack keeps whatever shape its author shipped.

## Decision

### 1. They go to `<pack>/textures/chr/`, and `<img>` says so

The builder writes `chr/Chr_XX_N.png` (and, under `_writeReferences`, its `chr/Chr_XX_N.orig.png` twin) and emits `<img>chr/Chr_XX_N.png`. `chr` is exactly what the layer is (the console's CHR, in CHR order) and sorts before `sheets/` but reads as plumbing. The bootstrap top level is then:

```
textures/
  hires.txt
  sheets/        <- ADR-0153: the artist surface
  backgrounds/   <- ADR-0050: the positional surface
  chr/           <- the CHR-order rendering layer
```

### 2. Nothing in the loader changes, because `<img>` was always a path

`HdPackLoader::LoadFile` resolves an `<img>`/`<background>`/`<patch>` target against the pack root, for a folder pack (`FolderUtilities::CombinePath`) and a zip pack (`ZipReader`) alike, and normalizes backslashes before any tag is parsed. `backgrounds/screenNNN.png` has loaded through that path since ADR-0050. A subfolder in `<img>` is not new capability and needs no version bump: the manifest is self-describing. That makes the shape a **per-pack** property with no fallback rule.

### 3. Existing packs are not migrated; a pack being *re-recorded* is

- **An old pack only ever read** keeps its top-level fragments and root-relative `<img>` lines and renders as before. `auto/` is regenerable, so there is no upgrade step.
- **An old pack the builder re-records over**: the builder loads the existing `hires.txt` in its constructor and rewrites the whole pack at save time, so the new manifest names `chr/` while the old fragments stay behind, unreferenced — exactly the clutter this ADR removes. `PruneLegacyChrFiles` deletes them, after `hires.txt` is written (an interrupted save never leaves a manifest pointing at gone files), restricted to names the builder writes (`Chr_<N>.png`, `Chr_<HH>_<N>.png` and the `.orig.png` twin of either) at the top level only. `sheets/`, `backgrounds/`, `audio/` and any artist file are never candidates. The count is logged.

Migrating every pack at load time was rejected: it makes the loader write to the user's files for a cosmetic problem.

### 4. The round trip does not learn about `chr/`

`scripts/mep_build.py` reads the key source's `<tile>` lines and **drops every `<img>`**, re-emitting one per sheet under `sheets/`. Where the fragments live is invisible to it: a built pack references `sheets/` alone, whatever shape its key source was in. `scripts/test_mep_build.py` builds the same fixture twice, root-relative and `chr/`-relative, and requires byte-identical manifests.

## Consequences

- A recorded pack's top level goes from *N* fragments + `hires.txt` + two folders to **one file and three folders**. Measured on a 300-frame Mega Man 3 bootstrap: 183 entries → 4, 1.89 MB moved out of the artist's first screen.
- `sheets/` and `backgrounds/` are what an artist meets (the point of Phase 9); `chr/` reads as plumbing.
- A pack recorded before or after this ADR is valid; tooling must read `<img>`, not assume a shape — `UI/ViewModels/HdPackPreviewViewModel` scans the pack root *and* `chr/` for that reason.
- The builder now deletes files. Bounded by name and depth, but the one part of this change that could destroy an artist's work if the name test were loosened — read any future change to `PruneLegacyChrFiles` as touching user data.
- `mep_lint` needed no change: it resolves `<img>` by path and reports a real `auto/` pack in the new shape with 0 errors, all 91 images found.

## Amendments (2026-09-06, code-review pass)

- §3 guard: `PruneLegacyChrFiles()` is skipped, with a log line, whenever the export dropped a tile on the upstream ">256 tiles of the same palette" path (`HdPackBuilder::_droppedTiles`). A truncated re-emit must never delete the artist's source `Chr_*.png`.
