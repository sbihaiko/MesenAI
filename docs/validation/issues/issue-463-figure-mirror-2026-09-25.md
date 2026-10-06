# Issue #463 — a kit figure and the cells it imports into share one orientation (2026-09-25)

Validation record for the fix to
[#463](https://github.com/sbihaiko/MesenAI/issues/463). The work builds on
PR #466 (the blank-cell skip and the twin cache) and is rebased onto `main`
at `c16b4d24`, which includes #466 and #475.

## Root cause

The recorder bakes a sprite's OAM flip into the crop it records (ADR-0178),
and the kit cuts its figures from those crops. That is the right view for an
artist: a figure shows Simon the way the game draws him. The #435 recipe then
builds the copy once before the import, and that build un-bakes the crops in
place and drops the sidecar's `mirror` (#255). After it, the figure is the
mirror of every flipped cell it maps to: 120 of the 144 cells of Castlevania's
`usr002-figure` (Simon's walk).

`mep_figure.py import` did not know about the flip, which had two effects:

1. It pasted the figure's pixels as they were, so painted art landed mirrored
   in game.
2. ADR-0225 §3 ownership, which gives a shared pixel to the frontmost cell
   whose art is opaque there, read each cell's art from the un-baked crop. So
   a mirrored front cell won the pixels where the figure shows it
   transparent. This is how the blank tile took body paint in #452.

## Design choice

The issue listed three options: mirror on import, re-export the figures after
the first build, or export from built sheets. Both re-export options would
show the artist a figure whose flipped cells face the wrong way, because once
the build has run, the sheet no longer says which crops the game flips. So
this fix mirrors on import, driven by the figure's own record:

- **Export** (`figure_cells`, and so `export_figure` and the kit's
  `export_pose_rows`) writes `mirror` on each cell whose home crop still
  carries an ADR-0178 `mirror`. The value is one `H`/`V`/`HV`/`""` per 8x8
  sub-tile, in `tiles[]` order.
- **Import** computes `pending_flips` for each cell: each recorded flip that
  the crop has since lost. A crop that still carries its `mirror` already
  matches the figure and is not flipped. This covers a pack that was never
  built (import refuses those anyway, #435) and a build that leaves the crop
  baked (index-keyed packs before #475).
  - The painted cell and its ownership mask are un-baked by those flips
    before the merge and the write, so routed crops (#413) get the same
    pixels.
  - `_owned_pixels` reads each cell's art flipped back into the figure's
    orientation.
- The report gains `unmirrored` (the number of cells un-baked on the way in),
  and the CLI prints it.
- A figure exported before this fix carries no `mirror` and still imports as
  it did. Re-export it: `artist_kit.py` writes the field.

No sheet, sidecar or build rule changes. The figure sidecar gains one optional
per-cell field, and readers that ignore it are unaffected.

## Red before the fix

`python3 scripts/test_mep_figure.py`, run against the unmodified
`mep_figure.py` with the two new behavioural cases
(`test_paint_on_a_mirrored_cell_lands_unmirrored_after_the_first_build`,
`test_pixel_ownership_reads_a_mirrored_cell_in_the_figure_orientation`):

```
FAIL the export records the flip of the mirrored cell only: {"0": null, "1": null, "2": null, "3": null, "4": null}
FAIL the mirrored cell's crop stores the paint un-baked, so the run time's H flip draws magenta|green as painted: left (255, 0, 255, 255) right (0, 255, 0, 255)
FAIL node 1 takes paint only where its tile is opaque; its transparent half stays a hole: left (255, 0, 255, 255) right (255, 0, 255, 255); cells [{"node": 1, "dx": 0, "dy": 0, "x": 0, "y": 0, "sheet": "sprites.json", "sheetX": 10, "sheetY": 1, "index": 1, "z": 0}, {"node": 0, "dx": 1, "dy": 0, "x": 4, "y": 0, "sheet": "sprites.json", "sheetX": 1, "sheetY": 1, "index": 0, "z": 1}]
3 failure(s): ['the export records the flip of the mirrored cell only', "the mirrored cell's crop stores the paint un-baked, so the run time's H flip draws magenta|green as painted", 'node 1 takes paint only where its tile is opaque; its transparent half stays a hole']
```

A third case, `test_the_pending_flip_is_the_recorded_one_the_crop_has_since_lost`,
was added with the fix. It pins the still-baked rule, the per-sub-tile unit-16
flips, and the involution.

## Mutations (all caught)

| Mutation | Result |
|---|---|
| M1: `pending_flips` never flips | 4 failures: both behavioural cases, plus two `pending_flips` checks |
| M2: ownership reads the un-baked art (no flip in `_owned_pixels`) | 1 failure: the ownership case |
| M3: flip even when the crop still carries its `mirror` | 1 failure: "recorded, still baked: no flip" |
| M4: export records no `mirror` | 3 failures: the export check and both behavioural cases |
| M5: the paint is not un-baked (the mask still is) | 1 failure: the mirrored-paint case |

Each mutation was reverted, and the suite is green again.

## E2E: Castlevania, `usr002-figure` (Simon's walk)

The recording is the deep measurement's `runs/p1` (80 s of stage 1). The kit
was regenerated with the fixed scripts: `artist_kit.py`, `artist_bg_kit.py`
and `artist_chr_kit.py`. The recipe is "When you are done": copy `auto/`, add
the kit sheets (with their twins), CHR pages and scenes, build, import
`figures/usr002-figure.png`, then build twice.

**The paint.** It is mirror-coherent, the way an artist paints a character.
Each tile key gets one design in its true orientation: a cell-local gradient
over the tile's opaque pixels. Each figure cell shows that design flipped by
the cell's recorded OAM flip, painted back to front. The pre-fix arm uses the
same kit and figure, with PR #466's `mep_figure.py`. The fixed arm was run
twice: once on the #466 branch, and again after the rebase onto `main`
(`c16b4d24`, which includes #475's build changes). Both runs gave the same
numbers.

**The check.** For every non-blank figure cell, the drawn crop is the one the
built `hires.txt` points the cell's key at. That crop is flipped by the
instance's flip, which is what `HdNesPack::DrawTile` draws, and then compared
with the figure's paint on the pixels only that cell covers. A cell counts as
right when every pixel matches, as mirrored when every pixel matches the
H-mirror, and as other otherwise.

| | Pre-fix (#466) | Fixed | Control |
|---|---|---|---|
| figure cells / with a recorded flip | 144 / 120 (the field is ignored) | 144 / 120 | 144 |
| build 1 | 0 errors | 0 errors | 0 errors |
| import | 138 painted, 80 written, 58 already on the sheet | 140 painted, 65 written, 75 already on the sheet (61 rerouted, no rule moves) | 0 painted |
| build 2 / build 3 | 0 / 0, byte-identical | 0 / 0, byte-identical | 0 / 0, byte-identical |
| `<tile>` rules | 348 | 348 | 348 |
| cells drawn right | **13** | **105** | — |
| cells drawn mirrored | **76** | **0** | — |
| other (partial) | 41 (no pixel matches the right orientation) | 25 (no pixel matches the mirror; see below) | — |
| unchecked (no solo pixel) / blank | 10 / 4 | 10 / 4 | — |

Two fewer cells count as painted before the fix (138, not 140). The likely
cause is the mirrored ownership: it gave every pixel of those cells to a
neighbour. This was not traced cell by cell.

The 25 partial cells in the fixed arm are not orientation errors. Their
matching pixels all match the right orientation and none matches the
mirror. They come from a different, pre-existing defect, filed as
[#478](https://github.com/sbihaiko/MesenAI/issues/478):

- an overlapped cell that is routed (#413) takes the pixels it does not own
  from the untouched source crop;
- when it is pasted onto the owner crop, those pixels erase the paint that an
  earlier instance of the same key had already routed there.

Example: `E010B8F8…/FF273707` keeps 36 of 39 design pixels in `usr005`, and
the 3 lost pixels are the ones cell 219 covers in `pose246`.

This log does not measure an in-game frame. The drawn-crop check
reproduces the run time's mirror of the replacement art directly.

## Suites

- `test_mep_figure.py`: 18 cases, all ok. `test_artist_kit.py`: 16/16.
  `test_artist_kit_assemble.py`: 16/16. `test_pose_pixel_offsets.py`: all ok.
- `make python-tests` and `make doc-checks`: see the PR body.
