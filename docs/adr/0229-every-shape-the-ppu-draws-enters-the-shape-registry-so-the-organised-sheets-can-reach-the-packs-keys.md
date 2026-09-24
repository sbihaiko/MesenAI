# ADR-0229: Every shape the PPU draws enters the shape registry, so the organised sheets can reach the pack's keys

- Status: **superseded 2026-09-24**, closed as option (iii) by the user's
  decision of 2026-09-24, answering "What do we do with ADR-0229?" with the
  option, verbatim, *"Reenquadrar (Recommended)"*: close this ADR as (iii)
  and open a new `proposed` ADR for the real gap. Option (i) measured no
  gain (pack shape coverage 19.9 → 19.9 % on Castlevania, 16.0 → 16.0 % on
  Zelda; see "Measured 2026-09-23" below) and is not implemented. See
  "Amendment 2026-09-24" at the end. Earlier status, kept as written:
  **proposed 2026-09-23** — user leans (i), 2026-09-23, pending
  measurement. Opened by the user's decision on the 2026-09-23 Phase 12
  review, verbatim: *"Sim, como recomendado (Recommended)"*, which accepted
  opening this ADR as `proposed` with the three options below. Acceptance
  waits on the measurement in "What decides it": pack coverage, grid-dump
  size and wall clock on the Castlevania 60 s and Zelda 85 s runs. Nothing is
  implemented; the implementing slice would be PRD Part A **F14.4**
  (`docs/roadmap/PRD-mesence-enhancement-ecosystem.md`, Phase 14), which does
  not start until this is accepted.
- Date: 2026-09-23
- Related: ADR-0209 ("What (k) actually closed", the question this answers;
  Q4 and the `unsorted` sheet, F12.8), ADR-0183 (§2 the four surfaces, §3
  evidence vs inference, §4 the round-trip acceptance test), ADR-0194 (the
  pattern pages are the kit's cross-recording union), ADR-0170 (the retained
  stream and `kMaxSheetFrames`), ADR-0210 (static coverage sources),
  ADR-0137 (the `HdPackBuilder.cpp` line ceiling),
  `docs/validation/metroid-artist-workflow-evidence.md` ("Scale")
- Supersedes / amends: none yet. Option (i) would narrow ADR-0209's
  "What (k) actually closed" from an open question to a decided one; it
  changes no sheet layout and no sidecar schema.
- Superseded by: ADR-0230 (a sheet cell reaches every palette its shape was
  drawn in, the gap this ADR's measurement found; `proposed`)

## Context

ADR-0209 (k) gave every shape in the recorder's registry a sheet cell (the
`unsorted` remainder sheet, F12.8), and then measured that the registry
itself is the gap:

| | Castlevania (60 s) | Zelda (85 s) |
|---|---|---|
| `<tile>` keys in `hires.txt` | 3 209 | 2 171 |
| distinct shapes in `hires.txt` | 2 673 | 1 615 |
| shapes in the registry (`_shapeTiles`) | 514 | 277 |
| pack coverage from any sheet | 19.2 % | 17.2 % |

The cause is upstream of every sheet. `HdPackBuilder::ShapeIdFor` is the only
thing that appends to `_shapeTiles`, and it is reached from `RecordGridFrame`
and `RecordSprite` only, both gated on `_captureScreens` and both fed by the
retained frame stream (capped at `kMaxSheetFrames`, consecutive duplicates
collapsed). `HdPackBuilder::ProcessTile`, which emits the `<tile>` rules,
runs on everything the PPU draws and answers to neither. The 81–83 % of pack
keys that no sheet shows were drawn and keyed, but never offered to the
sheet pipeline. ADR-0209 left "reaching 100 % of the *pack*" open on purpose,
as a question about what the recorder retains rather than how sheets are
laid out. The Metroid evidence gives the same gap at scale: a 60 s recording
yields 2 211 keys against the 8 401 of the shipped Metroid pack.

Non-goals: this ADR does not change how a pose, a scenery group or a stage
map is found (those need frame context and stay on the retained stream); it
does not add a key the PPU did not draw (ADR-0183 §3; static fill stays
ADR-0210's); and it does not change the sidecar schema or `mep_build.py`.

## Options

**(i) Register every shape `ProcessTile` draws.** `ProcessTile` also interns
the tile through `ShapeIdFor`, so the registry becomes the set of shapes the
PPU drew during the recording, not the set the retained stream happened to
keep. The extra shapes carry no organisation: no pose, no scenery group, no
map position. They land where the remainder already lands, the `unsorted`
sheet, as `seen: true` cells (they were drawn). The organised sheets are
unchanged. Costs to measure: registry size (ids are `uint16_t`, with
`kEmptyCell` = 0xFFFF as the cap), the grid dump's `K` lines, the size of the
`unsorted` sheet, and per-tile interning cost on the PPU path. The lines land
in `HdPackBuilder.cpp`, which sits under a line ceiling, so either the change
fits or the ceiling amendment is part of this ADR (ADR-0137).

**(ii) Retune retention.** Raise `kMaxSheetFrames`, or stop collapsing
duplicates, so the retained stream holds more frames and therefore more
shapes. This keeps every registered shape attached to a frame, but it only
moves the cap: a retained grid frame is about 2.8 KB plus the 2 KB RAM
window F12.6b keeps beside it (about 19.5 MB at today's 4 096), it grows
with the run, and a shape drawn between retained frames is still
missed.

**(iii) Leave it and document the split.** The CHR pattern pages already
carry every key the pack holds (ADR-0194: the pattern pages are the kit's
union), so an artist can reach any key there, just not organised. The
docs would say that the organised sheets cover what the retained stream saw
and the pattern pages cover the rest. Costs nothing in code. The artist still
meets 81–83 % of the pack's keys only as an unordered page.

## What decides it

The user leans (i). Before acceptance, measure option (i) on the two runs
ADR-0209 measured (Castlevania 60 s idle; Zelda 85 s from
`scripts/stages/zelda/mint-stage1.txt` + `stage1-run.txt`), on one binary
with dylib provenance proven before recording:

1. **Pack coverage**: the share of the emitted `hires.txt`'s distinct
   `(tileData, palette)` keys reachable from a sheet cell, against today's
   19.2 % and 17.2 %.
2. **Dump size**: the grid dump and the `sheets/` folder, before and after.
3. **Wall clock**: the recording's wall time, before and after, over the same
   emulated frame count.

and confirm that `mep_build` round-trips the kit byte-identically
(ADR-0183 §4). If (i)'s cost is out of proportion to its coverage, (ii) or
(iii) are measured next. The decision is written here, by hand, after those
numbers.

### Measured 2026-09-23 on a prototype of (i) (not a decision)

The prototype is a one-line hook in `ProcessTile` that calls `ShapeIdFor` on
each new exact key. It is switched by `MESEN_ADR0229_REGISTER_ALL`, so both
arms ran on one binary; it was not merged, and the log reproduces its diff. Full log, hashes and
commands: `docs/validation/f14.4-adr0229-option-i-measurement-2026-09-23.md`.

| | Castlevania before | after (i) | Zelda before | after (i) |
|---|---|---|---|---|
| pack coverage, shapes (this ADR's ratio) | 19.9 % | 19.9 % | 16.0 % | 16.0 % |
| pack coverage, keys | 16.6 % | 16.6 % | 12.2 % | 12.2 % |
| coverage of *drawn* shapes | 100 % | 100 % | 100 % | 100 % |
| coverage of *drawn* keys | 84.7 % | 84.7 % | 45.6 % | 45.8 % |
| registry size | 592 | 611 | 294 | 294 |
| `unsorted` cells | 135 | 154 | 79 | 79 |
| grid dump bytes (`K` lines) | 43.99 MB (342) | 43.82 MB (342) | 65.98 MB (190) | 65.93 MB (190) |
| `sheets/` bytes | 2 355 708 | 2 361 332 | 781 664 | 780 254 |
| wall clock, median of 4 (s) | 19.5 | 19.7 | 23.6 | 24.6 (inside the noise) |
| `mep_build` round trip | 0 errors, byte-identical | same, same keys | 0 errors, byte-identical | same |

What the numbers show:

- The Context's premise does not hold on these runs. The 81–83 % the table
  above calls the gap is almost entirely the bootstrap's PRG-scan
  `defaultTile` export: tiles the recording never drew (Castlevania: 532 of
  2 673 shapes drawn, Zelda: 260 of 1 620). Today's sheets already reach
  every drawn shape.
- The drawn keys that are still unreachable are extra palettes of shapes that
  are already on a sheet. A cell carries one palette. (i) does not address
  that gap.
- The 19 shapes (i) added on Castlevania are unflipped forms of mirrored
  sprites that are already reachable through the sidecar's `source` key
  (ADR-0178). ADR-0209's 19.2 % did not credit `source`, and 19.9 % is the
  same baseline with it credited.
- As prototyped, (i) also shifts the organised output (ids and first-seen
  palettes). Zelda's never-firing `spriteNearby` conditions, the ones that
  name a background palette, go from 1 to 6.
- The hook fits `HdPackBuilder.cpp`'s ADR-0137 ceiling with 0 lines to spare.

## Decision

Not decided. The user leans (i) (2026-09-23); acceptance waits on the
measurement above.

## Consequences

- Under (i), the `unsorted` sheet grows by the shapes the retained stream
  missed, roughly 2 000 cells on Castlevania and 1 300 on Zelda at the
  ADR-0209 numbers. That is one larger unordered sheet, which is still
  better than a key with no surface.
- Under (i) or (ii), every kit regenerated after the change differs from its
  predecessor, so artists' painted sheets from before must be re-imported
  through the round trip, not copied.
- Under (iii), the organised-sheet coverage figure stays near 18 % by design,
  and every artist-facing doc has to say so.

## Amendment 2026-09-24: closed as (iii), superseded by ADR-0230

Appended on the user's decision of 2026-09-24, verbatim *"Reenquadrar
(Recommended)"*. The text above is kept as written.

- **Option (i) is not adopted.** Its prototype reached no key today's sheets
  do not already reach (19.9 → 19.9 % of shapes on Castlevania, 16.0 → 16.0 %
  on Zelda), and it moved the organised output (ids, first-seen palettes). It
  was never merged.
- **Option (iii) is what this ADR closes as.** The organised and `unsorted`
  sheets already give a cell to every shape the recording drew (100 % on both
  games). The rest of the pack's keys are tiles the recording never drew: the
  bootstrap's PRG-scan `defaultTile=Y` export (Castlevania 2 141 of 2 673
  shapes, Zelda 1 360 of 1 620). They are reachable on the CHR pattern pages,
  which are the kit's union (ADR-0194), and nowhere else by design.
  `docs/remastering-a-game.md` ("Unpack the recording into a kit") documents
  this split. Nothing changes in code.
- **The remaining gap is a different question.** Drawn-key coverage is
  84.7 % (Castlevania) and 45.6 % (Zelda) because a sheet cell carries the
  shape's first-seen palette only. ADR-0230, `proposed`, takes that question,
  with its own options and measurement. PRD slice F14.4 now measures for
  ADR-0230.
