# ADR-0236: A recorded capture draws only the cells whose live key matches its per-cell record

- Status: accepted (2026-09-25) — **not implemented**; pending PRD slice
  F14.11, no build go-ahead yet. Owner's pick, verbatim: *"Opção 3: guarda no
  render (Recommended)"*, re-confirmed after the cost was corrected (this does
  not fix packs already written either — none carries the record it needs)
  with *"Manter opção 3 (Recommended)"*.
- Date: 2026-09-25
- Related: issue #499, ADR-0235 (the measurement and the four options; its
  option 3), ADR-0221 option D (the guard as first proposed), ADR-0156 §1/§5,
  ADR-0172 (index-keyed CHR-ROM tiles), ADR-0004 (`hires.txt` extension spec),
  ADR-0050, `Core/NES/HdPacks/HdNesPack.cpp` (`OnBeforeApplyFilter`,
  `OnLineStart`, `GetPixels`), `Core/NES/HdPacks/HdPackConditions.h`
  (`HdPackTileAtPositionCondition`), `Core/NES/HdPacks/HdData.h`
  (`HdBackgroundInfo`), `HdPackBuilder::CaptureScreen`
- Supersedes / amends: supersedes ADR-0235 (option 2); amends ADR-0156 (a
  captured screen that carries the record owns a cell only on frames where the
  live key at that cell equals the recorded one).

## Context

ADR-0235 found why Ninja Gaiden's `screen001` freezes the HUD (#499): the
recorder's anchor evidence reads a status-bar row one tile off, so the gate
fires on frames it was never frozen for. Option 2 — make the recorder's
evidence match the run time and refuse ambiguous gates — was implemented and
measured on the 30-ROM library: per-row reading alone refuses 97 of 219
captures and still draws the stale `screen001`; closing #499 took an extra
"missing evidence is not separation" rule, at 219 → 87 captures and
68 883 → 35 265 drawn frames (`docs/validation/f1410-probe-evidence-2026-09-25.md`).
Refusing at save time costs whole captures, and the evidence a stream carries
cannot tell "the recorder could not place this pixel" from "the game drew
nothing here". A render-time guard costs cells instead, and tests the pixels
the player is about to see.

Today a loaded `<background>` (`HdBackgroundInfo`) carries only its bitmap,
conditions, scroll ratios, priority, offset and blend mode — nothing about
the frame it was captured from — so no guard is possible without new data.

## Decision

1. **The record.** For every capture it writes, the recorder also writes the
   capture's **positional cell grid**: for each of the 32 × 30 screen cells,
   the key the run time reads at that cell's origin pixel (`r*8`, `c*8`) —
   the same `HdPackTileInfo` identity `HdPackTileAtPositionCondition`
   compares (CHR-ROM index or 16-byte pattern per ADR-0172, plus palette),
   sampled on the captured frame. It is positional, not a set of keys: a set
   would pass a live HUD digit that appears elsewhere on the same capture,
   which is #499 again. It is a new `hires.txt` tag bound to its
   `<background>` line (a dictionary of the screen's distinct keys plus one
   index per cell, ~1–2 KB per screen), so it travels with the line and older
   readers ignore it; its grammar is added to the `hires.txt` extension spec
   (ADR-0004) and `mep_build`/`mep_carry` carry it with the PNG.
2. **The guard.** At run time a `<background>` that has the record draws a
   screen cell only when the live key at that cell's origin equals the
   recorded key for the same cell; a mismatched cell is left to what would
   draw without the capture. The comparison is one predicate shared with
   `HdPackTileAtPositionCondition` (a host-free header pinned by
   `make core-unit-tests`), so gate and guard cannot disagree — the lesson of
   ADR-0235's Context. The mask is built once per frame in
   `HdNesPack::OnBeforeApplyFilter` for scroll ratio 0 (every recorded
   capture: the screen-to-PNG mapping is the identity) and per scanline in
   `OnLineStart` otherwise; the per-pixel cost is one bit test.
3. **Opt-in by the data.** A `<background>` without the record draws exactly
   as today. Hand-made packs (the Contra80s pack's 3 007 `<background>`
   lines are deliberate replacement art) and every pack already installed
   stay byte-identical in output.
4. **Scope.** All `<background>` priorities, since layers 0–19 still show
   where the ROM pixel is color 0.
5. **What fills a masked cell.** Vanilla when no `<tile>` rule matches
   (screen-resident cells have none, ADR-0156 §1); when ADR-0156 §5's floors
   kept a cell routed to the sheets, its `<tile>` rule — in a recorded pack
   often the bootstrap's neutral ramp, i.e. grey, not the game. The
   verification must report which one each masked cell got.

## Consequences

- **What this does not do.** It does not fix packs already written: none
  carries the record, and no proxy in the current data closes #499. (The
  catalog's three `<background>` entries are all `pack:known-missing` on that
  very target, so none draws today.) It fixes what is recorded from now on —
  the same population option 2 would have — at a cost in cells rather than in
  captures.
- **An artist's deliberate blank is kept only where the game still shows the
  recorded tile.** The guard compares keys, not the painted PNG, so a cell
  the artist blanked on purpose stays blank on the frames it was captured
  for, and shows the game on frames where the live tile differs.
- **Hot-path cost.** At most 960 key compares per `<background>` with the
  record per frame (per scanline row when the scroll ratio is not 0), and one
  bit test per background pixel.
- **Spec and tooling.** The `hires.txt` extension spec gains the tag (semver
  bump); `mep_lint`, `mep_build` and `mep_carry` must accept and carry it, or
  a rebuilt pack silently loses the guard.
- **One predicate.** If `HdPackTileAtPositionCondition`'s key identity ever
  changes, the record's key must change with it; the shared header is what
  makes that a compile-time fact rather than a convention.
