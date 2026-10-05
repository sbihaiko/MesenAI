# Issue #479: a sprite pixel on screen row 0 makes no `<tile>` rule (2026-09-25)

Validation record for the fix to
[#479](https://github.com/sbihaiko/MesenAI/issues/479). One Excitebike sprite
key, `<tile>1,00,FF20160F`, was drawn but had no cell on any sheet.

## Root cause

The suspect in the issue was wrong. Shape 0 is not tile `$00`:

- The OAM dump's `K 0 7F7F7F7F7F7F404033330C0C33330000 FF20160F` is CHR tile
  `$FA`. `sprites.json` carries it as cell 82, `"index": 250`.
- CHR tile `$00` is `384CC6C6C66438000000000000000000`, and no OAM or grid
  shape has that data. The sheet passes and `BuildUnsortedSheet` are correct.
  The key was never registered at all.
- The `1` in `<tile>1,00,…` is the image slot (`chr/Chr_00_1.png`, #473), not
  part of the tile index.

A temporary trace in `HdBuilderPpu::DrawPixel` located every draw of sprite
tile `$00`. All of them were in frame 9, on screen row 0, at x 0-4, with 8
sprites loaded. No OAM entry can cover row 0, because OAM draws a sprite one
line below its Y byte. What the PPU draws on row 0 comes from the pre-render
line's fetch of secondary OAM left over from line 239. In frame 9 that data
is eight copies of tile `$00` at x 0.

`OamFetchLatch` fetches its first row at line 0, for row 1, so it can never
register those pixels. `DrawPixel` still made a `<tile>` rule from them. That
rule was a drawn key with no shape, and so no cell. This broke the
#450/#470 contract that the registry and the rules name the same keys.

## Fix

- `OamFetchLatch::SpriteRowIsPlaced(scanline)` is true on rows 1-239, the
  rows an OAM entry can be latched on.
- `HdBuilderPpu::DrawPixel` writes a sprite `<tile>` rule only when that
  predicate holds. The background-priority probe (`hasBgSprite`) and the
  background rule are unchanged.
- `Core/AGENTS.md` adds the rule to the "registry and rules name the same
  keys" bullet.

## Red before the fix

This is the new test `TestTheSpriteRuleGateAdmitsExactlyTheRowsTheLatchCanPlace`.
It checks every OAM Y at 8x8 and 8x16 through the latch model and compares the
rows the latch can place with the gate. The predicate starts out with the
behaviour `DrawPixel` had before the fix, which admits every visible row. Run
against `origin/main` `8e0a22ab` (with #483), verbatim:

```
FAIL  Issue #479: the sprite rule gate admits a screen row iff an OAM entry can be latched on it: rows 0(rule only) 
FAIL  Issue #479: row 0 (leftover pre-render sprites) makes no sprite rule; rows 1-239 do
1158/1160 cases passed
```

After the fix: 1160/1160.

## Mutations

| Mutation | Result |
|---|---|
| `SpriteRowIsPlaced` admits row 0 (the old behaviour) | the 2 red checks fail (1158/1160) |
| `SpriteRowIsPlaced` also rejects row 1 | 2 fail (1158/1160): `rows 1(latch only)` |
| `SpriteRowIsPlaced` admits row 240 | 1 fails (1159/1160): `the pre-render line and row 240 are never drawn rows` |
| `DrawPixel` ignores the gate | this is the "before" binary of the E2E below: Excitebike is back to 1 drawn key with no cell |

Each mutation was reverted, and the suite is green again.

## E2E

`headless_record <rom> <s> <prefix> bootstrap hdpack-off log`, with
`MESEN_OAM_STREAM_DUMP` and `MESEN_SHEET_GRID_DUMP` on. Each binary is a
private copy: `headless_record` is relinked to its own dylib and ad-hoc signed.
Before the build, every object whose `.d` names `HdBuilderPpu.h`,
`OamFetchLatch.h`, `HdPackBuilder.h` or `TileSheetTypes.h` was deleted.

| Build | `MesenCore.dylib` sha256 |
|---|---|
| Before: `origin/main` `8e0a22ab` (#483 merged) | `2a358229…9780` |
| After: this change | `25c1da5f…e57d` |

**Excitebike**: 40 s from power-on, `mint-stage1.txt` then `stage1-run.txt`.

| | Before | After |
|---|---|---|
| Sprite keys drawn with no cell for their tile | **1** (`$00` / `FF20160F`) | **0** |
| `<tile>` keys | 1001 | 997 |
| `sheet_keys_audit.py` | 259 entries, 0 leftover | 257 entries, 0 leftover |
| OAM stream and grid dumps | — | byte-identical |

Four keys are gone, and every one of them was drawn only on row 0:

- `$00` / `FF20160F`: the issue's key.
- `$FA` / `FF20190F` and `$FE` / `FF20190F`: the row-0 copies were drawn in a
  palette the figure never showed. `$FA` / `FF20190F` was an exact fold on
  cell 82.
- `$FA` / `FF202C08`: this was variant cell 162 (ADR-0230).

Only `sprites.json` and its PNGs change among the sheets.

**Castlevania stage 1**: 80 s from `stage1.mss` with `stage1-run.txt`, the
#450 route.

| | Before | After |
|---|---|---|
| `sheet_keys_audit.py` | 587 entries, 0 leftover | 573 entries, 0 leftover |
| `<tile>` keys | 3689 | 3654 |
| OAM stream and grid dumps | — | byte-identical |

The 35 lines that are gone are 15 CHR RAM keys and their 20 `sprNNN_nK`
sprite-group condition lines. All 15 keys are under `FF16250F`, and none of
them is drawn anywhere but row 0. These are row-0 copies of the leftover
sprites in the palette of the frame that drew them. They reached the sheets
only as ADR-0230 variant cells and folds. With their rules gone, those cells
and folds go too, in `sprites.json`, 7 `sprNNN` sheets and one fold in
`misc.json`. No sheet
key is left without a rule, and the OAM registry is unchanged.

## Suites

- `make core-unit-tests`: 1160/1160.
- `make python-tests`: 62 passed, 0 failed, 0 skipped (on `f9d9d973`, with #484).
- `make doc-checks`: exit 0.
