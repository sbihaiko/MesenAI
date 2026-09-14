# ADR-0183: A recording produces an artist kit of four surfaces, generated as a projection over the recorded pack, and every surface round-trips

- Status: accepted (2026-09-13, by the user: "garanta que o artista terá um
  material fácil de trabalhar para remasterizar o jogo, na linha do contra80,
  gerando a base do material pela gravação e análise do jogo") — being
  implemented as PRD Part A slice F9.24.
- Date: 2026-09-13
- Related: ADR-0153 (sheet slicing; §3 `sprites.png` is the raw OAM
  vocabulary, not a painting surface), ADR-0160 (`textures/chr/` CHR-order
  layer), ADR-0164/ADR-0165/ADR-0171 (composition queries, the editor, the
  pose as the unit), ADR-0170/ADR-0179 (the pose sidecar; cycles, phases and
  variants — the rows and columns of a figure grid), ADR-0174 (`sprNNN` ↔
  poses), ADR-0182 (what a recording is expected to cover), the Phase 9
  human panel log and the acceptance spec in
  `runs/golden-20260913-f922/` (`panel-human-2026-09-13.md`,
  `artist-kit-spec.md`, `artist-kit-contract.md`)
- Supersedes / amends: nothing. It adds a consumer-facing layer on top of the
  recorded pack; the recorded pack's own format is untouched.

## Context

The Phase 9 human panel of 2026-09-13 read a recorded Contra pack cold and
then side by side with the fan pack `Contra80s 1.1`. Sprites passed: every
figure the artist had painted and that the recording had seen was one
`poses.json` entry or one cycle of entries. Everything else did not. The
`objNNN` groups of the base pack are 19, 17 and 8 cells of solid black; the
one scenery element worth its own file, the base door sensor, is five loose
cells inside `metatiles.png`; and `sprites.png`, opened first, reads as noise
because nothing tells the reader it is a vocabulary dump.

Counting the reference pack made the gap concrete. Its 232 PNGs are four
families and nothing else: pattern pages (`Chr_0.png`…`Chr_23.png`, the ROM's
own pages), figure grids (`BillRizer.png`, rows = an animation, columns = its
phases), named scenery (`Stage1BaseDoor1.png`, one recognisable object per
file), and stage panoramas (`Stage1a.png` at 6696x480, a whole stage in one
image; `Stage3-Ground-v1b3.png` at 512x4360 for the vertical stage). A
recording today emits the first family (`textures/chr/`, full of holes: 47 %
of the reference pack's distinct tiles were ever seen, per ADR-0182's
measurement) and none of the other three — it emits `sprNNN` crops, `objNNN`
groups and one `screenNNN` capture per static screen (one, for the Contra
base; two for stage 1).

The recorded pack is not wrong. It is a record of what the emulator saw,
keyed the way the renderer matches: by `(tileData, palette)`. What is missing
is a *view* of it addressed to a person holding a brush.

## Decision

### 1. A kit is a projection, never a second source of truth

The artist kit is generated from an already-recorded pack, by scripts under
`scripts/`, into a folder beside it (`kit/` by default) — never into the
recording, and never by changing what the recorder captures at run time. The
recorded pack stays the evidence; a kit can be regenerated, thrown away and
regenerated differently without any recording being repeated.

### 2. Four surfaces, in this reading order

1. **Figures** — one sheet per animation: a row is a cycle the recording saw
   (ADR-0179), a column one of its phases, cells aligned on a common baseline
   so an artist can paint across a row. Fusions are excluded; a variant sits
   next to the pose it varies.
2. **Scenery** — one sheet per background element that is actually nameable.
   A group whose cells carry no art is dropped, and every drop is listed with
   the count behind it. Elements the recording scattered across `metatiles.png`
   are recovered by co-occurrence in `adjacency.json` (ADR-0164).
3. **Stage maps** — the stage stitched into one image from the replayable
   recording, horizontal or vertical as the stage scrolls, with a slicer that
   cuts a painted panorama back into the pack's tiles.
4. **Pattern pages** — the existing `textures/chr/` pages, completed from the
   ROM where the recording saw nothing.

Reading order is part of the deliverable: `ARTIST.md` says what to open
first, and says in words that `sprites.png` is a vocabulary dump and not a
surface. The Phase 9 cold read failed on exactly that omission.

### 3. Evidence and inference are never confused

Anything a generator infers rather than recorded — a tile filled from the ROM,
a guessed palette, a stretch of panorama not walked — is marked `seen: false`
in the kit manifest and labelled in `ARTIST.md`. A rule that would change what
a rebuilt pack renders is only emitted when the key it carries was actually
observed; the safe form is measured and preferred over the complete one.

### 4. Round-trip is the acceptance test, and it is mechanical

A surface counts as delivered when a copy of the pack carrying it rebuilds
with `python3 scripts/mep_build.py build` reporting 0 errors, and the set of
`(tileData, palette)` keys in the rebuilt `textures/hires.txt` is unchanged —
nothing lost, nothing invented (a superset is allowed only for §3's marked
fills, and only for observed keys). Every generator implements `--verify` and
prints the counts. This is the same check the composition editor's export
already passes (measured 2026-09-13: 618 keys before and after, 0 errors).

### 5. Naming comes from the data, or from a human, never from a generator

A caption is built from the recording's own ids and counts. A human-written
`names.json` may supply better captions; nothing else may. No generator
invents a character name.

## Consequences

- The kit is throwaway by construction, which is what makes it safe to iterate
  on: the expensive artefact is the recording, and ADR-0182 already fixed how
  far recording goes.
- The four generators are independent and parallelisable, joined only by a
  manifest fragment per part and one assembler — so a kit assembled from one
  generator is still a kit, and a part that fails to verify says so in the
  page the artist reads rather than being silently dropped.
- Completing pattern pages from the ROM raises coverage above what play can
  reach, but only for games whose pattern data is CHR ROM. Where it is CHR RAM
  filled from packed PRG data (Contra), the hole stays and must be stated.
- The kit does not make the recorded pack redundant, and it does not become an
  input to one: painting a kit produces sheets that rebuild the pack it came
  from, and nothing in the kit is ever read back as evidence about the game.
