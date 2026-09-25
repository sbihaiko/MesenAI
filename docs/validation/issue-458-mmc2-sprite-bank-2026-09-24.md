# Issue #458: under MMC2 the sheet registry named a sprite from the wrong CHR bank (2026-09-24)

On Mike Tyson's Punch-Out!! (MMC2, mapper 9), the sheets named tiles
`1E80`–`1E82` where the game drew `0580`–`0582`. Those are the same slot in
two different 4 KB banks, and they hold different art. The deep measurement
(`docs/validation/punchout-deep-measurement-2026-09-24.md` on
`docs/punchout-deep-measurement`, bug C) counted 7 sheet keys the recording
never drew and 9 drawn keys with no cell.

This fix is stacked on the #450 fix (`OamFetchLatch`, branch
`fix/450-oam-fetch-latch`) and extends that mechanism. It does not add a
second one. The raw material (packs, dumps, spike logs, scripts) stayed in a
session scratchpad and is not versioned.

## Root cause

A sprite's `<tile>` rule and its sheet cell took the CHR bank at different
moments.

- **`<tile>` rule.** `HdBuilderPpu::DrawPixel` keys the rule by the
  `AbsoluteTileAddr` that `StoreSpriteInformation` resolves as the PPU fetches
  each sprite row.
- **Sheet cell, before #450.** `CaptureOam` decoded every OAM entry at frame
  end, with the mapping the mapper held then.
- **Sheet cell, with #450.** `OamFetchLatch` decodes each half at cycle 257,
  the start of the sprite fetch, with the mapping held at that moment.

MMC2 (`Core/NES/Mappers/Nintendo/MMC2.h`) flips a latch when the PPU fetches
pattern `$FD`/`$FE` (the left latch fires on `$0FD8`/`$0FE8`, the high plane of
row 0). The new bank then applies to every fetch after it. Those fetches land
inside the sprite fetch window (cycles 257–320), so neither moment above is
enough:

- a sprite fetched after a latch sprite on the same line is read from the new
  bank;
- a latch tile's own row 0 comes from the old bank, and its rows 1–7 come from
  the new one.

**Frame end (pre-#450) is wrong for every sprite drawn before the frame's last
switch.** That is the issue's `0580`/`1E80` pair.

**Cycle 257 (#450) fixes that part but not the rest.** On the #450 branch
alone, Punch-Out!! still has 6 undrawn sheet keys. Two of them are new
wrong-bank keys (`0501` and `05FF`, under `FF352708`).

MMC4 derives from MMC2 and uses the same latch, only with wider trigger
ranges. The recorder path does not depend on the mapper, so the fix covers
MMC4 as well.

### Spike

A temporary `stderr` trace, since removed, ran on the pre-#450 base. For each
OAM half of tiles `$80`–`$82` and `$FC`–`$FF`, it printed the bank at frame end
and the bank of each fetched row.

- For tile `$80`, 20 halves were fetched from bank `05` while the frame-end
  mapping said `1E`.
- 4 248 halves were split across two banks: 4 208 `$FE` halves and 40 `$FD`
  halves. Row 0 came from `05`, and rows 1–7 from `1C`/`1D`/`1E`/`1F`.

## Fix: one mechanism, `OamFetchLatch`, now aware of the rows

- **`Core/NES/HdPacks/SpriteFetchLog.h`** (new, host-free) logs the frame's
  sprite row fetches: scanline, sprite X, PPU tile base and absolute CHR
  address.
  - `Resolve` returns the bank of a half's topmost fetched row. A half's rows
    are drawn on lines `screenY`..`screenY+7`, so they are fetched on scanlines
    `screenY-1`..`screenY+6`.
  - `FetchedAddresses` lists every distinct bank the half's rows came from.
- **`Core/NES/HdPacks/OamFetchLatch.h`** owns a `SpriteFetchLog`:
  - `OnRowFetch` feeds the log.
  - `Clear` empties the log together with the latch, at frame end and on state
    load.
  - `resolve` now also reports the absolute address it decoded the half from
    (`Half::AbsoluteAddr`).
  - The new three-callback `ForEachLatched(emit, rebank, emitBank)` hands
    each latched half over under the bank of its topmost fetched row, using
    `rebank` when that bank differs from the cycle-257 decode. It gives every
    other bank the rows came from to `emitBank`.
  - Which halves are recorded, and with which PPUCTRL and palette, is still
    decided at cycle 257 exactly as in #450.
  - A half none of whose rows was fetched (the 8-per-line limit) keeps its
    cycle-257 bank.
- **`Core/NES/HdPacks/HdBuilderPpu.h`**:
  - `StoreSpriteInformation` calls `OnRowFetch` with the address the `<tile>`
    rule is keyed by.
  - `LatchFetchedSprites` sets `Half::AbsoluteAddr`.
  - `OnBeforeSendFrame` supplies `rebank`, which re-reads the tile data and,
    on CHR ROM, sets the index, keeping palette and flips. It also supplies
    `emitBank` as `HdPackBuilder::RecordSpriteBank`.
- **`Core/NES/HdPacks/HdPackBuilder.h`**: `RecordSpriteBank` is inline and has
  the same gates as `RecordSprite`. It interns the shape so its key reaches a
  sheet, but it never adds an OAM entry, which would place the sprite twice.
- `Core/Core.vcxproj` and `Core/Core.vcxproj.filters` list the new header.

The #450 guarantees hold, and each is covered by a test below:

- a frame fetched with sprites off records nothing;
- sprites past the 8-per-line limit are still recorded (ADR-0153 §2);
- a state load clears the latch and its rows.

`HdPackBuilder.cpp` is untouched and stays at its ADR-0137 ceiling (2438). The
`hires.txt` format and the sidecar format are unchanged. The sidecar index
(ADR-0172) is now the bank the PPU read, which is the reference ADR-0215 names.

## Tests (strict TDD)

`scripts/core_unit_tests.cpp` gains 8 test functions with 21 checks, driven by a
two-bank model of the MMC2 left latch:

- 6 functions on `SpriteFetchLog`;
- 2 on `OamFetchLatch` with the row log: the rename after a latch sprite, and
  the #450 guarantees.

**Red against the #450 branch.** This run used `OamFetchLatch.h` exactly as
on `origin/fix/450-oam-fetch-latch`, plus API-only shims that keep #450's
behaviour:

- `OnRowFetch` does nothing;
- the three-callback `ForEachLatched` forwards to the one-callback form;
- `SpriteFetchLog` keeps no row.

Verbatim:

```
PASS  Issue #450: a frame whose rendering stopped before any sprite fetch records no sprite
PASS  Issue #450: an 8x16 sprite drawn before a mid-frame PPUCTRL switch keeps both halves, and an undrawn one is absent
PASS  Issue #450: a partly drawn sprite is recorded and a sprite above the enable line is not
PASS  Issue #450: a steady 8x16 frame records the frame-end decode unchanged
PASS  Issue #450: a steady 8x8 frame records the frame-end decode unchanged
PASS  #458: the model leaves the latch on bank $1E at frame end
FAIL  #458: the sprite drawn from bank $05 is named 0580, not 1E80: got index 7808, want 1408
FAIL  #458: the copy fetched before the switch is named from bank $05
PASS  #458: the copy fetched after the switch is named from bank $1E
FAIL  #458: the latch tile itself is named from the bank it was read from, $05
FAIL  #458: a half drawn from two banks is named by the bank of its first row
FAIL  #458: the top half of an 8x16 sprite keeps the bank of its own rows
FAIL  #458: the bottom half of an 8x16 sprite keeps the bank of its own rows
PASS  #458: a tile is never matched on rows another tile was fetched on
FAIL  #458: a latch tile read from two banks names both, its top row's first: 0 address(es)
FAIL  #458: the entry itself is still named by its top row's bank
FAIL  #458: a sprite read from one bank names one address
PASS  #458: an unfetched half names no address
FAIL  #458: the latch names the sprite after a latch tile by the bank its top row read (1E80), not the cycle-257 mapping (0580): got 2 sprite(s): 05FD 0580
FAIL  #458: the rows of that sprite read from the other bank reach the sheets as 0580, once: 0 extra key(s)
PASS  #458: a half no row of which was fetched is still recorded (ADR-0153 §2), under its cycle-257 bank
PASS  #458: row fetches do not admit a half fetched with sprites off (#450)
PASS  #458: a cleared latch forgets the rows of the frame before (a state load clears it too)
PASS  #458: an entry at another x was not fetched and keeps the current mapping
PASS  #458: an entry on other lines was not fetched and keeps the current mapping
PASS  #458: a cleared log holds nothing of the previous frame
1109/1120 cases passed
```

The red run names indexes in decimal: 7808 = `0x1E80` and 1408 = `0x0580`.
The checks that pass under the shims are the ones #450 already satisfies,
along with the guarantees the fix must keep.

**Green.** 1120/1120, with the 5 #450 checks included.

**Mutations** (each applied alone, run with `core-unit-tests`):

| Mutation | Result |
|---|---|
| M1 `SpriteFetchLog::Resolve` never matches a row, so the half keeps the mapping it was decoded with | 7 fail (1113/1120) |
| M2 the row filter ignores sprite X | 2 fail (1118/1120) |
| M3 `OamFetchLatch` never renames a half by its rows (the plain #450 naming) | 1 fails (1119/1120): `the latch names the sprite after a latch tile by the bank its top row read (1E80), not the cycle-257 mapping (0580): got 2 sprite(s): 05FD 0580` |
| M4 `OamFetchLatch` drops the extra banks | 1 fails (1119/1120): `the rows of that sprite read from the other bank reach the sheets as 0580, once: 0 extra key(s)` |

## E2E

Every run was made with `headless_record <rom> <s> <prefix> bootstrap
hdpack-off log`, with the grid and OAM dumps on. Before is
`origin/fix/450-oam-fetch-latch` (`5f895faf`). After is that commit plus this
change.

Before the build, the objects of every file that includes a changed header
were deleted: `NesPpu`, `NesConsole`, `HdPackBuilder` and
`InteropDLL/EmuApiWrapper`. Each binary is a private copy: `headless_record`
is relinked to its own dylib and ad-hoc signed.

| Build | `MesenCore.dylib` sha256 (private copy) |
|---|---|
| #450 alone | `19d7471645e5cffd55df0c0f50dfc0f75b3c6d5428d64a63bb62ec7b81a976d9` |
| #450 + #458 | `c3cc783f11ea7c06cdcf332b323c312e0be50f586ed87e727d6ee9cab5ace2ba` |

**Punch-Out!!, fight 1** (70 s, `scripts/stages/punchout/fight1.txt` from
`fight1.mss`):

| | #450 alone | #450 + #458 |
|---|---|---|
| Drawn keys (`hires.txt`, defaultTile N) | 1655 | 1655 |
| Sheet keys | 1573 | 1572 |
| Drawn keys on a sheet | 1567 | 1568 |
| **Sheet keys never drawn** | **6** | **4** |
| **Drawn keys whose tile has no cell** | **4** | **3** |
| `spriteNearby` anchors on an undrawn key | 3 of 209 | 0 of 205 |

The pre-#450 base measured 7 never drawn and 9 with no cell on the same route.
The combined sheet key set is the same set that the #458 fix produced on its
own over the pre-#450 base (digest `c21369e4…`). Of the base's 7 wrong keys,
`1E80`–`1E82` are gone. `0580`–`0582`, `05FD` and `05FE` (×2) now have cells,
and the split rows `1CFE`, `1DFE` (×2), `1EFE` and `1FFE` (×2) keep theirs.
The `<tile>` rule bodies are the same 9847 in every run.

**Castlevania stage 1** (80 s from `stage1.mss`, the #450 route):

| | #450 alone | #450 + #458 |
|---|---|---|
| `sheet_keys_audit.py` | 590 entries, 0 leftover | 590 entries, 0 leftover |
| `mep_build build` | 452 carried, 0 new | 452 carried, 0 new |
| pack (`auto/`) and OAM stream | — | byte-identical to #450 alone |

**Excitebike (40 s, play script) and Super Mario Bros. (30 s, attract).** The
pack, OAM dump and grid dump are byte-identical to #450 alone.

## What is left, and why it is not this bug

The 7 remaining keys are all blank latch tiles: the bitmaps of `$FD`, `$FE`
and `$FF` are all zero in every bank involved. In each case the recorder
already names the bank the PPU read.

- **4 sheet keys the recording never drew:** `1CFD`, `1DFD`, `1FFD` and
  `1FFF` (`FF353008`). All eight rows of these halves were read from exactly
  those banks. A fully transparent sprite row becomes a `<tile>` rule only
  when it happens to be the highest-priority active shifter at its first pixel
  (`NesPpu::GetPixelColor`), so a blank sprite gets a rule only some of the
  time.
- **3 drawn keys with no cell:** background `00FD` under `112A0F36`,
  `112A1436` and `112A2536`. A second trace found this tile drawn only on a
  cell's last scanline (`y & 7 == 7`), as a one-line raster effect. The
  background grid interns shapes only from rows `y & 7 == 0`
  (`RecordGridFrame`), so it never sees this tile.

Both are follow-ups, to be filed separately (P2).
