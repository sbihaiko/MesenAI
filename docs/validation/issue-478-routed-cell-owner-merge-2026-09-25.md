# Issue #478: a routed overlapped cell keeps the paint already on its owner crop (2026-09-25)

Validation record for the fix to
[#478](https://github.com/sbihaiko/MesenAI/issues/478). The branch is based on
`main` at `2c464ec4`, which includes #482 (#463, figure mirror) and #472
(#447, untouched keeps recorded).

## Root cause

In `scripts/mep_figure.py` `import_figure`, an overlapped cell (ADR-0225 §3)
is merged as `_merge_owned(current, fig_cell, owned)`. Here `current` is the
crop of the figure's source sheet (`sprites.png`). When `plan_targets` routes
the cell (#413), that merged cell is pasted onto the owner crop, a `usrNNN`
row. So the pixels the cell does not own came from the untouched source
crop. They overwrote whatever an earlier instance of the same key, in the
same figure, had already routed there. A kit figure repeats a key across
poses, so this is common: Castlevania's `usr002-figure` had 25 of 140
non-blank cells partially lost after the #463 fix.

## Fix

For each routed 8x8 sub-tile of an overlapped cell, import now merges
against the owner crop as it currently stands on the canvas being written.
It keeps the owner's pixels where the cell does not own them and takes the
figure's pixels where it does (`_merge_owned` on the sub-tile, with
`_sub_mask` slicing the ownership mask). Other paths are unchanged:

- A cell written to its source sheet already merged against the crop being
  written.
- A cell with no overlap has no mask.
- The ADR-0225 §3 ownership rules are unchanged.
- The #463 un-baking still applies. The paint and the mask are flipped
  before the merge, and the merge uses the flipped paint.

## Red before the fix

New case in `scripts/test_mep_figure.py`:
`test_a_routed_overlapped_cell_keeps_the_paint_an_earlier_instance_routed`.

The case puts node 1 in one figure twice. The first instance is alone. The
second is behind node 0, which covers its right half and is painted green.
Both instances route to `spr000`. The case was run against `main`'s
`mep_figure.py`:

```
FAIL node 1's drawn crop keeps every painted pixel, the right half included: 128 of 256 pixels lost, e.g. [(8, 0), (9, 0), (10, 0)] = (37, 91, 17, 255)
1 failure(s): ["node 1's drawn crop keeps every painted pixel, the right half included"]
```

`(37, 91, 17, 255)` is the recorded art of the source crop. It replaced the
magenta the first instance had routed there.

## Mutations (all caught)

| Mutation | Result |
|---|---|
| M1: merge the routed sub-tile against the source crop (`current`) again | 1 failure: the new case, 128 of 256 pixels lost to the recorded art |
| M2: drop the new merge block (the pre-fix code) | 1 failure: the same |
| M3: paste the figure's sub-tile unmerged (ownership ignored) | 2 failures: the new case (128 pixels are node 0's green) and the #463 case `node 1 takes paint only where its tile is opaque` |

Each mutation was reverted, and the suite is green again.

## E2E: Castlevania, `usr002-figure` (Simon's walk)

The #463 recipe and check were re-run unchanged, from
`docs/validation/issue-463-figure-mirror-2026-09-25.md`:

- **The recipe.** Regenerate the p1 kit, copy `auto/`, add the kit sheets,
  CHR pages and scenes, build, import `figures/usr002-figure.png`, then
  build twice.
- **The paint.** The paint is the same mirror-coherent per-key design.
- **The check.** The drawn-crop check is the same.

Two script eras were measured.

**On `main` (`2c464ec4`).** Since #472, the untouched `usrNNN` rows no longer
own these keys. The built `hires.txt` draws them from `sprites.png`. The control's manifest names
4 `sheets/` images, down from 52 before #472. So the import writes only `sprites.png`, and no cell
is routed. The fix cannot change anything here, and it does not:

| | main | main + fix | control |
|---|---|---|---|
| import | 140 painted, 35 written, 105 already on the sheet | same | 0 painted |
| build 2 / build 3 | 0 / 0, byte-identical | same | 0 / 0, byte-identical |
| `<tile>` rules | 348 | 348 | 348 |
| cells right / mirrored / partial | 130 / 0 / 0 | 130 / 0 / 0 | — |
| unchecked / blank | 10 / 4 | 10 / 4 | — |

In this table, "main" and "main + fix" produce byte-identical `hires.txt`
and `sprites.png`.

**On `main` before #472 (`33f92049`, #482 merged).** This is the pack state
#478 was filed against. `usr005` owns the keys, and the routed path runs:

| | before the fix | with the fix | control |
|---|---|---|---|
| import | 140 painted, 65 written, 75 already on the sheet (61 rerouted) | 140 painted, 35 written, 105 already on the sheet (31 rerouted) | 0 painted |
| build 2 / build 3 | 0 / 0, byte-identical | 0 / 0, byte-identical | 0 / 0, byte-identical |
| `<tile>` rules | 348 | 348 | 348 |
| cells drawn right | **105** | **130** | — |
| cells drawn mirrored | **0** | **0** | — |
| other (partial) | **25** | **0** | — |
| unchecked / blank | 10 / 4 | 10 / 4 | — |

In this table, "before the fix" and "with the fix" produce byte-identical
`hires.txt`: the fix moves no rule. Before the fix, the partial cells
include the issue's example. `E010B8F8…/FF273707` (`pose149`) kept 36 of
its 39 checkable pixels, with none matching the mirror. With the fix it keeps
all 39.

Fewer cells are written with the fix (35, not 65). A later instance of a key
whose earlier instance already routed the same design now finds its paint on
the owner crop. So it counts as already applied instead of overwriting it
with recorded art.

## Suites

- `test_mep_figure.py`: 19 cases, all ok.
- `make python-tests` and `make doc-checks`: see the PR body.
