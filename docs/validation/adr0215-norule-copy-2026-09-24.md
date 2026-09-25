# ADR-0215 amendment (NoRule): a tile the pack does not hold copies with the live palette

**Date:** 2026-09-24
**Base:** `main` @ `b1c62733` (includes PR #434, the #431 fix), one worktree,
clean core build. No Core file changes. `InteropDLL/obj.osx-arm64/MesenCore.dylib`
sha256 prefix `9459422c5a04edb1`, `scripts/headless_record` `6097deea0bd1b4a2`.
**Decision:** ADR-0215, amendment "2026-09-24 (NoRule)". The user's go-ahead,
verbatim: *"aceito sua sugestao. pode aplicar e rodar em paralelo"*.

## Change

`NesPackTilePalette.Resolve` answered `NoRule` (the loaded pack keys the tile
under no palette at all) with a refusal: palette `0`, `IsRefusal` true, and
the receipt "no palette can make the paste match at run time". Now:

- `NoRule` carries the live palette and is not a refusal. `IsRefusal` is true
  for `Ambiguous` only.
- The receipt reads *the loaded pack holds no rule for this tile, so the live
  `XXXXXXXX` is kept and the paste adds a new key*. `HdPackCopyHelper` already
  appends a successful verdict's reason to the OSD receipt, so it needed only a
  comment.
- `Ambiguous` (two or more palettes that palette RAM holds, none live) still
  refuses. The ADR amendment explains why.

## Red → green

`UI.Tests/Mep/NesPackTilePaletteTests.cs`:

- `A_tile_the_pack_does_not_hold_is_refused` asserted the old refusal
  (`IsRefusal` true). It is **replaced** because the decision it pinned was
  reversed.
- New: `A_tile_the_pack_does_not_hold_copies_with_the_live_palette` checks the
  status, the live palette, non-refusal and the receipt clauses (`holds no
  rule`, the live word, `adds a new key`).
- New: `A_sprite_the_pack_does_not_hold_copies_with_its_live_sprite_palette`,
  where the live word leads with `FF`.
- `Several_recorded_palettes_and_no_match_is_refused_with_the_list` is
  unchanged. It still pins `Ambiguous` as a refusal.

No other test asserted the `NoRule` refusal. A grep of `UI.Tests` and
`UI.HeadlessTests` for `NoRule`, `holds no rule` and `IsRefusal` found only
this file and the drawn-tile resolver's own refusals, which this change does
not touch.

Red, run against the unchanged `Resolve`:

```
[FAIL] A_tile_the_pack_does_not_hold_copies_with_the_live_palette          Assert.False() Failure  Expected: False  Actual: True
[FAIL] A_sprite_the_pack_does_not_hold_copies_with_its_live_sprite_palette Assert.False() Failure  Expected: False  Actual: True
Failed!  - Failed: 2, Passed: 9, Total: 11
```

Green: 11/11. The whole `UI.Tests` run: 517 passed, 0 failed.
`make headless-ui-tests`: 20 passed, 4 skipped, 0 failed. The #432 race did
not fire on this run.

### Mutations (each reverted in isolation)

| Mutant | Result |
|---|---|
| `IsRefusal` includes `NoRule` again (the old behaviour) | killed, 2 FAIL |
| `NoRule` carries palette `0` | killed, 2 FAIL |
| `NoRule` receipt back to the old text | killed, 2 FAIL |
| `NoRule` branch never taken (`Count < 0`), so the empty list falls through to `RecordedNotDrawn` | killed, 2 FAIL |
| `IsRefusal => false` (`Ambiguous` stops refusing too) | killed, 1 FAIL (`Several_recorded_palettes_and_no_match_is_refused_with_the_list`) |

A first attempt at the fourth mutant, `if(false)`, did not compile, so it
measured nothing. It was replaced by `Count < 0`.

## E2E: Tetris 2 (1993), the F14.2 re-score's frame

Inputs, the same as the #431 log
(`docs/validation/issue-431-fade-palette-copy-2026-09-24.md`):

- state `~/f14.2r-opus-sandbox/frames/Tetris_2__1993___Nintendo_.mss`
  (sha256 prefix `444cc7c9a0b94cc1`);
- the library ROM (`e11ce367c353a14b`);
- the pack: the sandbox work copy `~/f14.2r-opus-sandbox/games/Tetris_2__1993___Nintendo_`
  (518 `<tile>` rules).

Each scan is the real
`The_dispatcher_can_read_every_tile_of_a_paused_frame_as_a_cell` run through
`dotnet test --filter`. The ROM is copied to a scratch folder with the pack
installed as its sibling `mep/`. "before" is this branch with the first mutant
applied, which restores the old refusal.

**The pack unmodified.** Its only `NoRule` tile on this frame is the blank tile
`0x1100` (palette `0F262320`), and only in column 32, the 33rd fetched column:

| Config | Lines |
|---|---|
| before | 960 |
| after | 990 (+30, all `4352 / 0F262320` in column 32) |
| no pack (`Unchecked`) | 990, identical to "after" |

**A drawn tile the pack does not hold.** Column 32 is off screen on this frame,
so painting it would prove nothing. Tile `0x1170` (index 4464) is drawn in 268
cells, and the pack's only rule for it is `<tile>23,1170,0F0F0F0F,…`.
Removing that one line from the loaded copy of `hires.txt` makes `0x1170` a
tile the pack does not hold:

| Config | Lines | New lines |
|---|---|---|
| before (stripped pack) | 692 | — |
| **after** (stripped pack) | 990 | 268 × `4464 / 0F281807`, 30 × `4352 / 0F262320` |

"After" is identical to the no-pack scan, line for line. Cell `1,1` now copies
as
`{"count": 1, "tiles": [{"tile": "FC0202020202FC0000FEFEFEFEFEFC00", "palette": "0F281807", "index": 4464}]}`.
Before, it was refused.

**Render.** That line went through `scripts/mep_add_cell.py` into the work
copy: `unsorted.json`, cell 374, painted at 508,652 on `unsorted.png`. The
32×32 slot was painted `#FF00FF`, then built with `mep_build.py build` and
linted with `mep_lint.py`: 0 errors each. The 24 warnings are the recorder's
own sheet sizes. Each pack was installed as `mep/` without
`textures/backgrounds/` and without its `<background>` lines. In the test pack,
the `1170,0F0F0F0F` rule was removed too, so the only rule for `0x1170` is the
key the copy handed out. Rendered with
`headless_record <rom> 0 <prefix> screenshot state=<mss>`:

| Pack | Rules for `0x1170` | Magenta pixels |
|---|---|---|
| stripped pack (control) | none | 0 |
| + the pasted cell | `<tile>23,1170,0F281807,508,652,1,N` | **274 432** |

274 432 = 268 cells × 32×32, the same 268 cells the "after" scan added. The
old behaviour refused every one of them.

## Not measured

- No other game was scanned. The rule does not depend on the game, and the
  unit tests cover it.
- The GUI flow (right-click → copy → OSD toast) was not driven by a human. The
  receipt text is asserted in the unit test, and `HdPackCopyHelper` appends a
  successful verdict's reason to the receipt unchanged.
