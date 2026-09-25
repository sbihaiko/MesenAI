# Issue #460 — a later CHR RAM tile no longer evicts an earlier drawn key (2026-09-24)

Based on `feat/f149-adr0230-colourways` (PR #461, F14.9).

## Spike: measured loss

A temporary debug counter was added to `SaveHdPack`, and removed before the
final build. It listed every recorded (non-`defaultTile`) `HdPackTileInfo`,
which is one per distinct drawn `(tileData, palette)`, that held no CHR page
slot. The recipes are those of the F14.9 log: Castlevania 60 s from power-on,
Zelda 85 s (`mint-stage1.txt` + `stage1-run.txt`), and Contra stage 1
(mint, then `stage1-probe.txt` from the state, 5471 frames).

| | Castlevania | Zelda | Contra |
|---|---|---|---|
| drawn keys (recorder ground truth) | 630 | 575 | 259 |
| `N` rows in `hires.txt` before the fix | 628 | 574 | 259 |
| **lost drawn keys** | **2** | **1** | **0** |

Before the fix, the `hires.txt` sha256 values (`75f28649…`, `724a07ff…`)
match the F14.4/F14.9 logs.

## Root cause

- `HdBuilderPpu` declares `WriteRAM`, but the PPU's virtual is `WriteRam`.
  The spelling has differed since the builder was ported to Mesen2
  (`e96d6f3c`, 2022).
- So the method overrides nothing, and `_needChrHash` is never set by a CHR
  RAM write. It is set only at construction and after a state load.
- The CHR RAM "bank id" is therefore the hash of whatever the bank held at the
  first drawn pixel. From power-on, that is all zeros, so the hash is `0`.
- When the game later rewrites a tile index, the new tile has the same bank
  id, the same palette and the same `TileIndex` as the old one.
- `AddTile` then overwrote the slot `paletteMap[palette][TileIndex % 256]`.
  The earlier tile stayed in `_hdData.Tiles` but lost its `<tile>` line.
- Every lost key had bank id `00`:
  - Castlevania: `FF16250F` at indices 95 and 11;
  - Zelda: `FF202C08` at index 0, a blank tile.
- Contra starts from a save state, so its hash was taken after the stage's CHR
  was loaded, and it lost nothing.

## Fix

`AddTile` now files tiles through `MesenSheets::PlaceTileOnChrPage`, in the new
host-free header `Core/NES/HdPacks/ChrPageSlots.h`.

- A taken slot is never overwritten. The newcomer takes the free slot of its
  page whose column (that index across the bank's pages) holds the fewest
  tiles.
- `SortByUsageFrequency` packs each column into the first pages. So the
  least-filled column keeps the bank's PNG count unchanged whenever any column
  has room.
  - A first-free-slot variant was measured first. It added one
    `chr/Chr_*.png` on Castlevania (21 → 22).
- A full page refuses the tile. `AddTile` counts the refused tile in
  `_droppedTiles`, the existing ADR-0160 §3 guard.
- A loaded CHR RAM tile (index -1) keeps its first-free-slot behavior.
- On CHR RAM, `hires.txt` keys a tile by data and palette. So the slot only
  moves the cell inside the PNG. The format does not change, and no page or
  memory is added.

### Not done: repairing the override

Renaming the override to `WriteRam` restores a bank id per CHR state. It was
measured as an experiment and not kept:

| | Castlevania | Zelda | Contra |
|---|---|---|---|
| lost drawn keys | 0 | 0 | 0 |
| `chr/Chr_*.png` pages | 21 → **29** | 19 → **26** | 16 → 16 |

- This option adds `auto/` pages.
- It re-lays out every CHR RAM recording.
- The rotating-sum bank hash can still collide, so the slot guard above
  would still be needed.

Whether to take this option is a decision for the user and is left open here.

## Tests (strict TDD)

- **Red first.**
  - The first version of the header extracted the old `AddTile` behavior
    unchanged. `make core-unit-tests` then ran 1096/1101, with these failures:

    ```
    FAIL  #460: a later tile does not evict the earlier one from its slot
    FAIL  #460: the later tile takes the first free slot instead
    FAIL  #460: a loaded CHR RAM tile (index -1) takes the first free slot
    FAIL  #460: a full page reports the tile it could not file, so AddTile counts it as dropped
    FAIL  #460: a full page keeps every tile it already holds
    ```

  - The column rule was added after the page count above. Its test was red
    against the first-free rule:

    ```
    FAIL  #460: a displaced tile skips a column another page already fills
    1102/1103 cases passed
    ```

- **Green.** `make core-unit-tests` passes 1103/1103.
- **Mutations.** Each mutation was applied, run and reverted:

| Mutation | Result |
|---|---|
| a taken slot is overwritten again (`if(true)`, the original behavior) | killed, 7 failures |
| a taken slot drops the tile instead of trying a free slot | killed, 2 failures (first-free version) |
| the column count is ignored (first free slot) | killed, 1 failure |

- `make python-tests`: 59 passed, 0 failed.
- `make doc-checks`: passes.

## E2E (final binary, without the debug counter)

The coverage and round-trip method is the F14.9 log's.

| | Castlevania | Zelda | Contra |
|---|---|---|---|
| drawn keys in `hires.txt` | **630** | **575** | 259 |
| lost drawn keys (630/575/259 = the ground truth) | **0** | **0** | **0** |
| drawn keys on sheets | 630/630 (100 %) | 575/575 (100 %) | 259/259 (100 %) |
| unobserved sidecar keys | 0 | 0 | 0 |
| `chr/Chr_*.png` pages | 21 (unchanged) | 19 (unchanged) | 16 (unchanged) |
| `mep_build` twice | exit 0/0, 0 errors, byte-identical | same | same |
| recorder `hires.txt` sha256 | `e51ca7a2…` | `c665f942…` | `6f8e6c4e…` (= before the fix) |

- **Added keys.** Against the pre-fix recordings, the added `N` keys are
  exactly the keys the counter listed as lost: 2 and 1. None was removed.
- **Reproducibility.** Each recorder `hires.txt` is byte-identical to the one
  from the instrumented run of the fixed code. That makes two passes per
  game.

## Binary provenance

- The `HdPackBuilder.o` object was deleted before each rebuild, because the
  makefile does not track headers. Then `make capture-tool` ran with exit 0.
- `otool -L scripts/headless_record` resolves to this worktree's
  `InteropDLL/obj.osx-arm64/MesenCore.dylib`, sha256 `ca36b7b6…`.
- `strings` on that dylib finds no trace of the debug counter.
- Castlevania's 630 drawn keys, against 628 on the unmodified binary, show
  that this dylib contains the fix.

## Files

- `Core/NES/HdPacks/ChrPageSlots.h` (new)
- `Core/NES/HdPacks/HdPackBuilder.cpp`: 2438 → 2425 lines, under its
  ceiling. The ceiling itself is unchanged.
- `Core/NES/HdPacks/SheetColourways.h` (comment)
- `scripts/core_unit_tests.cpp`: 3 tests.
- `Core/Core.vcxproj`: lists the new header.
