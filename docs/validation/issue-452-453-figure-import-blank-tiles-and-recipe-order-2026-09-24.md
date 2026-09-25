# Issues #452 and #453 — figure import skips blank tiles; the sprite notes give the #435 recipe order (2026-09-24)

Validation record for the fixes to
[#452](https://github.com/sbihaiko/MesenAI/issues/452) and
[#453](https://github.com/sbihaiko/MesenAI/issues/453). Both came out of the
2026-09-24 deep measurements (`docs/validation/castlevania-deep-measurement-2026-09-24.md`,
bug B3, on branch `docs/castlevania-deep-measurement` when this was written,
and the Excitebike run). The work is based on `feat/f149-adr0230-colourways`
(PR #461), because that branch was not merged yet.

## #452: root cause

`mep_figure.py import` decided whether a figure cell was painted only by
comparing the pixels that cell owns (ADR-0225 §3) against the figure's
`.orig.png` twin. Nothing asked whether the cell's own recorded tile draws
anything. Castlevania parks a blank sprite tile
(`00000000000000000000000000000000` / `FF23340F`, `sprites.json` cell 0) inside
four of Simon's walk poses (`pose124`, `pose236`, `pose062`, `pose246`). At
pixel precision its 8x8 rect overlaps 2–4 body tiles. ADR-0225 §3 gives a
shared pixel to the frontmost cell whose original art is opaque there, and
otherwise to the frontmost cell. The blank tile won 11, 4, 17 and 40 pixels in
the four poses, and **every one of them is opaque in the figure's own 1x
twin** (measured with `diag2.py`; the `diag*.py` scripts are in the session
scratchpad and are not versioned). The body tile that draws those
pixels failed the "opaque here" test, because it reads the cell's art from the
pack as it is after the first build. That build un-bakes the recorder's
flip-baked crops (ADR-0178). The kit's figure was composed before it, so
**122 of the figure's 144 cells now read as the mirror of what the figure
shows** (`diag3.py`). With the body tile ruled out, the pixel fell to the
frontmost cell, which was the blank tile. So painting the whole figure put
paint on the blank tile. Import then wrote it as a painted cell, into
`sprites.png` directly and into `usr017` by the #413 route. The two copies
were different pictures, so `mep_build` failed with the #253 guard:
`painted tile 0000…/FF23340F lost to sheets/usr017.png`.

The skip below fixes the issue as filed: whatever wins its pixels, a blank
tile is never written. The mirrored ownership is a separate, broader
pre-existing bug, listed under follow-ups.

## #452: fix

`import_figure` looks up each painted cell's recorded art: its crop of the
sheet's `*.orig.png` twin, the same crop `Sheet.cell_image` returns. When
every pixel of that crop is transparent (colour 0 only, a tile the NES never
draws), the cell is not written. It is listed under the report's new
`blank` array, with its node, pose, sheet, cell index and `tileData/palette`
keys, and it is not counted as `painted`. The CLI prints one line with the
count, then one line per skipped tile. A sheet with no usable twin is never
called blank.

The check is per cell. A sprite figure is unit 8, so a cell is one tile. A
unit-16 object cell is skipped only when all four of its sub-tiles are blank.

## #453: root cause and fix

In `scripts/artist_kit.py` `_notes`, the "Rebuild after painting" bullet
(ARTIST.md, sprite surfaces section) still said copy → import → build. Since
#435/#444, `import` refuses (exit 2) a copy that was never built with the kit
sheets, so an artist who followed that bullet was stopped. The bullet now
reads copy → build (naming #435) → import → build again, which is the order of
the "When you are done" recipe. A grep of `scripts/` and `docs/` for
`mep_figure` import recipes found no other place with the old order.
`artist_kit_assemble.py` `_recorded_done_steps`,
`docs/remastering-a-game.md` §4 and §5, and `scripts/AGENTS.md` already give
build → import → build.

## Red before the fix

`python3 scripts/test_artist_kit.py`, new case
`test_the_rebuild_bullet_gives_the_recipe_order_copy_build_import_build`,
against the unmodified `artist_kit.py`:

```
FAIL the bullet reads copy, build, import, build: [24, 183, 103, 183] in 'Rebuild after painting: copy sheets/usr* into "PACK/textures/sheets/", import each painted figure with python3 scripts/mep_figure.py import "PACK" figures/usrNNN-figure.png, then run python3 scripts/mep_build.py build "PACK" - a figure left out of the import never reaches the pack (#399).'
FAIL it names the build twice, before and after the import: ...
FAIL and says why the first build comes before the import: ...
13/16 cases passed
```

`python3 scripts/test_mep_figure.py`, new case
`test_a_fully_transparent_tile_is_never_painted_and_the_skip_is_reported`,
against the unmodified `mep_figure.py`:

```
FAIL the all-transparent tile is skipped and named in the report: {"figure": "pose000", "scale": 2, "cells": 5, "painted": 5, "written": 5, ...}
FAIL every other painted cell is still written: {... "painted": 5, "written": 5, ...}
FAIL no sheet carries paint on the blank tile's cell: ['spr000.png']
FAIL the CLI reports the skipped tile with its key: pose000: 5 cells, 5 painted, 0 written, 5 already on the sheet
4 failure(s)
```

## Mutations

| Mutation | Result |
|---|---|
| `_is_blank` returns `False` (the blank check is gone) | `test_mep_figure.py`: the same 4 checks of the new case fail |
| `_is_blank` returns `True` (every cell is skipped) | `test_mep_figure.py`: 30 failures across the suite, including "while a body tile carries the paint" |
| `artist_kit.py` restored to the base branch (the old bullet) | `test_artist_kit.py`: the 3 order checks fail, 13/16 |

Each mutation was reverted, and both suites are green again.

## E2E: Castlevania, whole-figure paint

The kit and recording are the ones from the deep measurement: `runs/p1`
(80 s of stage 1) and its 4-part kit. The recipe is "When you are done": copy
`auto/`, add the kit sheets, CHR pages and scenes, build, import every
`figures/usr*-figure.png`, build twice. `figures/usr002-figure.png` (Simon's
walk) has every opaque pixel set to `#FF00FF` (70 960 px). The control arm
paints nothing. The pre-fix arm is a copy of the same `scripts/` with the base
branch's `mep_figure.py`.

| | Pre-fix | Fixed | Control |
|---|---|---|---|
| build 1 | 0 errors | 0 errors | 0 errors |
| `usr002-figure` import | 142 painted, 84 written | **138 painted, 80 written, 4 blank skipped** (all `0000…/FF23340F`, one per pose) | 0 painted |
| build 2 / build 3 | **exit 1** (#253: `painted tile 0000…/FF23340F lost to sheets/usr017.png`) | **0 errors / 0 errors**, byte-identical | 0 / 0, byte-identical |
| keys | — | 348 → 348 carried, 0 dropped; same key set as the control | 348 |
| rules whose crop holds `#FF00FF` | — | 98 rules, 33 distinct keys (usr005, usr013, usr015, usr016, usr002, sprites) | 0 |
| in game, frame 2766 | — | 5 120 px `#FF00FF`, on Simon and the two wall candles his variants carry | 0 |

The sheets the fixed import writes are byte-identical to the pre-fix
import's, except `sprites.png`, where cell 0 (the blank tile) is no longer
painted.

## Follow-ups observed, not fixed here (drafts, not filed)

- **P1 candidate: after the #435 build, a kit figure is the mirror of most of
  the cells it imports into.** The kit exports `figures/usr*-figure.png` from
  the recording, where the recorder baked the flip into the crop. The recipe
  then builds the copy before the import (#435), and that build un-bakes
  those crops (ADR-0178). On Castlevania's `usr002`, 120 of the 140 non-blank
  cells' figure silhouette matches the mirror of the built cell art, 16 match
  it as-is, and 4 are symmetric (`diag4.py`). Import pastes the figure's
  pixels as they are and computes ADR-0225 §3 ownership against the
  un-baked art. Three things follow:
  1. Ownership goes wrong. That is how the blank tile won body pixels here.
  2. Pixels a cell does not own are refilled from mirrored art. The likely
     cause of the holes below.
  3. Painted art that is not a solid fill would land mirrored in game. A
     solid magenta fill cannot show that. It may also explain the 176
     non-magenta changed pixels the deep measurement left unanalysed in its
     one-cell proof.

  A fix needs a decision: mirror on import, re-export the figures after the
  first build, or export figures from built sheets. That calls for a design
  choice, not an in-passing change.
- **A blank key re-pointed onto a shared `usr017` crop.** `usr017.json`
  places the blank tile (cell 1) and a spark tile `000044EE…/FF16250F`
  (cell 2) at the same 1x position (1, 10). Painting the spark writes that
  crop, as import should. `mep_build` then treats the crop as painted for the
  blank key too, and moves its 22 rules from `hud.png` (4, 36) to
  `usr017.png` (4, 40), which holds 480 magenta px. The build passes, but the
  blank key now carries art. This is a build-precedence or kit-layout
  question, not import's.
- **Holes in the painted Simon.** In game, the whole-figure paint shows Simon
  with transparent gaps where the control is opaque. For example, `usr005`
  cell 2 has 848 opaque px in the control and 672 after the import, 400 of
  them magenta. `usr005.png` is byte-identical with and without this fix, so
  the gaps come from the existing import, not from this change. The first
  follow-up is the likely cause, but this was not proven.
- Build 2 also warns (#343) that 2 painted `sprites.png` keys were already
  claimed by another crop. The warning is the same before and after the fix.

## Suites

- `test_mep_figure.py`: 14 cases, 90 checks, all ok. `test_artist_kit.py`:
  16/16. `test_artist_kit_assemble.py`: 16/16.
- `make python-tests`: 59 passed, 0 failed, 0 skipped.
- `make doc-checks`: exit 0.
