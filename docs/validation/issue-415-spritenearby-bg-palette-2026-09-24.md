# Issue #415: `spriteNearby` anchors keyed on a background palette (2026-09-24)

The recorder emitted `spriteNearby` conditions whose palette field was a
background palette. Zelda's example on `main`:
`spr018_n0,spriteNearby,0,8,0000…0000,09010001,Y`. A condition that tests OAM
has to name what OAM showed: a sprite palette, `FFxxxxxx`. This log records
the reproduction, the root cause, the fix and the before/after counts. Raw
material (packs, dumps, logs, scripts) lived in a session scratchpad and is not
versioned.

## Root cause

`HdPackBuilder::AttachSpriteNearbyConditions` took the anchor's palette from
`_shapeTiles[targetShape].PaletteColors`, which is the palette of the shape's
**first-seen art**. `ShapeIdFor` interns shapes palette-wildcarded
(`GetKey(true)`) in first-sight order from both the background path
(`RecordGridFrame`) and the sprite path (`RecordSprite`). So a tile the PPU
drew as background before it ever reached OAM keeps that background palette
as its art. On Zelda that tile is the blank tile, shape 0: the grid and OAM
dumps both open with `K 0 0000…0000 09010001`. The OAM stream itself only ever
shows shape 0 with sprite palettes. Summed over the retained frames
(RepeatCount-weighted), those were `FF292717` 4 453, `FF081A28` 3 864,
`FF303B22` 1 932, `FF303B16` 1 900 and `FF152730` 841.

This is the "first-seen variant is the art" mechanism the
[F14.4 measurement](f14.4-adr0229-option-i-measurement-2026-09-23.md) pointed
at. Changing which path interns a shape first changes the count, which is why
the ADR-0229 (i) prototype raised it from 1 to 6.

### What the wrong palette did and did not break

Every emitted condition carries `ignorePalette` (`,Y`), and both evaluators
skip the palette word when it is set: `HdPackSpriteNearbyCondition` at run
time and `mep_conditions.py` in lint. So in today's packs the condition
**did** fire, and the issue's "never fires" does not hold for a pack we emit.
The defect is that the condition misstated the observation. ADR-0189 §5 says a
condition we emit states what the recorder observed, and no sprite was ever
observed with `09010001`. The condition would also become truly dead if a
reader or a later edit dropped the `,Y`, because the palette-strict path
compares the palette word first.

## Fix

- `MesenSheets::SpriteNearbyPalettes` (`Core/NES/HdPacks/SpriteGrouping.h/.cpp`,
  host-free and linked into `core-unit-tests`) makes one pass over the
  retained `OamFrame` stream. For each shape it returns the sprite palette
  word it was seen with on the most frames, weighted by RepeatCount, with
  ties going to the palette seen first. It counts only words whose top byte
  is `0xFF`, which `HdBuilderPpu` writes for every sprite and which no
  background word can have, since palette RAM bytes are 6-bit. `0` means no
  sprite palette was observed.
- `AttachSpriteNearbyConditions` writes that word as the anchor's palette. When
  the word is `0` it emits no condition, so the bare twin (ADR-0189 §3)
  renders as before. `HdPackBuilder.cpp` stays at 2 427 lines, under its
  ADR-0137 ceiling of 2 428. The call and the skip reuse existing lines.

The tile half of the condition is unchanged: `SourceTileData` / `TileIndex`
of the shape (ADR-0178).

## Binaries

Worktree on `main` `56d49588`, built with the CommandLineTools toolchain
(`make capture-tool`, `SDKROOT=…/CommandLineTools/SDKs/MacOSX.sdk`, Homebrew
`sdl2-config` on `PATH`). Every `Core/` and `InteropDLL/` `.o` was deleted
before the fix build because the fix touches a header. Each `headless_record`
was relinked to a private copy of its dylib (`install_name_tool` + ad-hoc
`codesign`).

| | `MesenCore.dylib` sha256 | `headless_record` sha256 |
|---|---|---|
| main (before) | `c5125bff756e80aba4537c46441ca2b927ade7cd3dfa66cdb06f17fb0b83fd36` | `76094e3835d60f6a90ceedd3f5926f34c63888a7318a10f30edaa8f6cd05b4c4` |
| fix (after) | `fae0c66319698f02673033b5549375c8d2cc688c9397cf2058372e3af7d69fe3` | `4c01da3a4e4a4cf403c5775d3433e7ec110de61b46c93dbba7eae8c46dc21fd5` |

Provenance: `nm` finds `SpriteNearbyPalettes` in the fix dylib and not in the
main one.

## Commands

```sh
# every run: a fresh sandbox dir holding only a copy of the ROM
export MESEN_SHEET_GRID_DUMP=<dir>/grid-dump.txt MESEN_OAM_STREAM_DUMP=<dir>/oam-dump.txt
# Castlevania (1987) (Konami).nes (file sha1 7a20c44f…), idle
headless_record <dir>/Castlevania.nes 60 <dir>/rec bootstrap hdpack-off log
# Zelda: roms/Zelda.nes (the dump stage-set.json pins; file sha1 3701381a…);
# input = mint-stage1.txt followed by stage1-run.txt (4 739 frames), idle to 85 s
headless_record <dir>/Zelda.nes 85 <dir>/rec bootstrap hdpack-off log input=<mint+run>.txt
```

Emulated frames are identical before and after: Castlevania 3 607, Zelda 5 109.
The count is the `<condition>` lines of `auto/textures/hires.txt` of type
`spriteNearby` whose palette field does not start with `FF`.

## Results

| | Castlevania before | Castlevania after | Zelda before | Zelda after |
|---|---|---|---|---|
| `spriteNearby` definitions | 183 | 183 | 85 | 85 |
| of which non-sprite palette | **0** | **0** | **1** | **0** |
| `hires.txt` sha256 | `75f28649…` | `75f28649…` (byte-identical) | `7d08bf0f…` | `724a07ff…` |
| `mep_build` round trip | 0 errors, rebuild byte-identical | same | 0 errors, rebuild byte-identical | same, 0 non-sprite palettes in the rebuilt pack |

The only `hires.txt` difference between the two binaries is that one line
(Zelda):

```diff
-<condition>spr018_n0,spriteNearby,0,8,00000000000000000000000000000000,09010001,Y
+<condition>spr018_n0,spriteNearby,0,8,00000000000000000000000000000000,FF292717,Y
```

`FF292717` is shape 0's most-seen OAM palette (above). Every other file of
both packs is byte-identical (`diff -r`), including the sheets. No condition
was dropped: every anchor on these two runs had at least one observed sprite
palette. Castlevania shows no difference because none of its anchors was
first interned as background.

## Checks

- `make core-unit-tests`: 1 046 / 1 046 pass. The new case is
  `TestSpriteNearbyPaletteComesFromOam` (most-seen OAM palette wins, a tie
  goes to first sight, a background word or unknown id is never returned, a
  shape absent from OAM gets 0). Mutation-proven twice:
  - Disabling the `0xFF` filter fails 2 checks ("a background palette is never
    a spriteNearby palette", "every emitted palette is a sprite palette").
  - Picking the first-seen palette instead of the most-seen fails 1 check.
- `make doc-checks`: passes. `HdPackBuilder.cpp` is 2 427 lines against its
  2 428 ceiling.

## Caveats

- Two recordings, and only one of them had the defect.
- Not addressed, and noted for a follow-up: the anchor's **tile data** also
  comes from the first-seen art (`SourceTileData`). For a CHR RAM tile first
  drawn as background and later shown in OAM *flipped*, the un-baked data of
  the background sighting is not the data the run time compares for the
  sprite, so that condition would not match. Whether either run hits it was
  not measured (Zelda's case, the blank tile, is flip-invariant). The OAM
  stream does not retain flip bits, so fixing it needs the recorder to keep
  them.
