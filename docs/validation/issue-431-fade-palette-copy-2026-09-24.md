# #431: the copy no longer substitutes a recorded palette the frame cannot draw

**Date:** 2026-09-24
**Base:** `main` @ `58ea58d7`, one worktree, clean core build. No Core file
changes. `InteropDLL/obj.osx-arm64/MesenCore.dylib` sha256 prefix
`de83932b2d2ef0c4`, `scripts/headless_record` `9ef4776a31a6d7b8`.
**Found by:** `docs/validation/f14.2-rescore-after-419-421-2026-09-24.md`
(Gauntlet and Tetris 2 failed their cold reads on it).
**Decision:** ADR-0215, amendment of 2026-09-24.

## Root cause

`NesPackTilePalette.Resolve` (#342) asked the loaded pack which palettes it
keys the tile under. When the pack held exactly one palette and it was not the
live one, the copy returned that palette (`Substituted`). It never checked
whether the frame could draw that palette. A bootstrap recording keys most
tiles under the fade it saw while the title came in. The Tetris 2 baseline
keys 474 of its 518 `<tile>` rules under `0F0F0F0F`, including tile `0x1170`
(index 4464). On a frame fully faded in, the copy therefore handed out
`0F0F0F0F` for every cell. The runtime asks for the tile under the live
palette (`0F281807`), so the pasted key built, linted and never matched.

The copy only had evidence of the live palette at one slot. It already
receives all of palette RAM (`rawPalette`, 32 entries), so it can tell which
palettes the frame can draw at all.

## Rule

A palette the pack keys the tile under is a candidate only when palette RAM
holds it now for the same layer: one of the four background palettes, or one
of the four sprite palettes (color 0 packed as `FF`). Words are packed as
`HdTileKey` packs them (`NesPackTilePalette.PaletteWord`, moved out of
`HdPackCopyHelper` so the helper and the tests share it).

| Pack's palettes for the tile | Answer |
|---|---|
| none | `NoRule`, refuse (unchanged) |
| include the live one or the `defaultTile` wildcard | `LiveMatches`, live (unchanged) |
| exactly one candidate | `Substituted`, that candidate |
| several candidates, none live | `Ambiguous`, refuse, listing the candidates |
| no candidate | **`RecordedNotDrawn`** (new), live, and the receipt names the recorded palettes |

There is no per-scanline palette trace, so a legitimate mid-frame palette swap
whose recorded palette RAM no longer holds also gets the live palette. The
receipt names the recorded one. ADR-0215's amendment states this, and the
other residual case, in full.

## Red → green

`UI.Tests/Mep/NesPackTilePaletteTests.cs`. `Resolve` gained a third argument,
the frame's palettes. The existing six cases pass them. The Metroid
substitution runs on a frame that still holds `0F0F0F0F` and keeps passing.
Four cases are new: Tetris 2, several recorded palettes none drawable, only
drawable palettes count, and the per-layer packing.

Red. The API stub was in place and `Resolve` kept the old behaviour:

```
[FAIL] A_recorded_palette_the_frame_does_not_hold_is_not_substituted_431   Expected: RecordedNotDrawn  Actual: Substituted
[FAIL] Only_the_recorded_palettes_the_frame_holds_are_candidates_431       Expected: Substituted       Actual: Ambiguous
[FAIL] Several_recorded_palettes_the_frame_does_not_hold_keep_the_live_palette_431  Expected: RecordedNotDrawn  Actual: Ambiguous
Failed!  - Failed: 3, Passed: 7, Total: 10
```

Green: 10/10. The whole `UI.Tests` run: 516 passed. `make headless-ui-tests`:
20 passed, 4 skipped, 0 failed. The #432 race did not fire on this run.

### Mutations (each reverted in isolation)

| Mutant | Result |
|---|---|
| no frame filter (the old behaviour) | killed, 3 FAIL |
| `RecordedNotDrawn` hands out the recorded palette | killed, 2 FAIL |
| sprite palettes lead with the background colour | killed, 1 FAIL |
| sprite layer reads background palette RAM | killed, 1 FAIL |
| no-candidate case falls through to the refusal | killed, 2 FAIL |

## E2E: Tetris 2 (1993), the F14.2 re-score's frame

Inputs:

- the re-score's state, `~/f14.2r-opus-sandbox/frames/Tetris_2__1993___Nintendo_.mss`
  (sha256 prefix `444cc7c9a0b94cc1`);
- the library ROM (`e11ce367c353a14b`);
- the pack: the sandbox's work copy
  `~/f14.2r-opus-sandbox/games/Tetris_2__1993___Nintendo_`. The re-score's own
  baseline pack had already been deleted with its worktree. The work copy
  carries the same key set: 518 `<tile>` rules, 474 of them under
  `0F0F0F0F`, which matches the issue.

Each table is the real
`The_dispatcher_can_read_every_tile_of_a_paused_frame_as_a_cell` run through
`dotnet test --filter`. The ROM is copied to a scratch folder with the pack
installed as its sibling `mep/`. "before" is this branch with the no-filter
mutant applied, which is the old `Resolve`.

| Config | Lines | Palettes |
|---|---|---|
| before | 960 | `0F0F0F0F` ×960, byte-identical to the re-score's hand-over table |
| **after** | 960 | `0F281807` ×552, `0F262320` ×328, `0F262A12` ×80 |
| no pack (`Unchecked`) | 990 | `0F281807` ×552, `0F262320` ×358, `0F262A12` ×80 |

All 960 "after" lines are identical to the same positions in the no-pack table.
The 30 extra no-pack lines are column 32, the 33rd fetched column, and hold
the blank tile. The pack has no rule for it, so it stays `NoRule`, as before
this change. Cell 0,0 now copies as
`{"tile": "FC0202020202FC0000FEFEFEFEFEFC00", "palette": "0F281807", "index": 4464}`.

Render. Each cell 0,0 line went through `mep_add_cell.py`, was painted
`#FF00FF` at its slot, built, linted and installed as `mep/` with
`textures/backgrounds/` removed. The state was rendered with
`headless_record ... 0 ... screenshot state=`:

| Pack | build / lint | Magenta pixels |
|---|---|---|
| work copy, nothing added (control) | 0 / 0 | 0 |
| + the "before" line (`0F0F0F0F`) | 0 / 0 | **0** |
| + the "after" line (`0F281807`) | 0 / 0 | **274 432** |

The `0F0F0F0F` zero is caused by the palette, not by the placement.
`mep_build` warns that the new cell's key is already claimed by another crop
(#343). The crop that owns the `<tile>1170,0F0F0F0F` rule (unsorted.png at
472,652) is 1 024 of 1 024 pixels magenta in that build, and it still draws
nothing. 274 432 = 268 cells × 32×32. The issue measured the same figure.

## Not measured

- Gauntlet, Ninja Gaiden and The Flintstones were not re-scanned. The rule
  does not depend on the game, and the unit tests cover it.
- The residual cases named in ADR-0215's amendment: a candidate on another
  palette slot, and a mid-frame swap whose recorded palette RAM no longer
  holds. No game in the re-score was measured for either.
