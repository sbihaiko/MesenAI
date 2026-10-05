# Cold read: Contra artist kit (F12.18-F12.19 re-record)

> Evaluator per ADR-0214: a fresh Opus agent with no builder context, briefed only by `f1219-contra-kit-coldread-briefing.md` (verbatim), sandboxed to a copy of the kit regenerated in `f1218-f1219-contra-rerecord-2026-09-23.md` plus `docs/remastering-a-game.md` and `docs/hd-pack-authoring.md`. No stops. This is the F12.19 (4) cold-read row. The defects it found are filed as #399 (painted figures never routed back into the pack), #400 (the cycle label says 6 phases, the grid shows 5 columns, with no phase order given) and #401 (the rest grid shows player-plus-enemy composites; visual judgement, not yet measured).

- Start: 2026-09-23 22:05:33 -03
- Found Bill's run: 22:05:50 -03 (from `kit/ARTIST.md`, section "1. Figures", on the first read)
- End: 2026-09-23 22:07:00 -03 (writing this log took a few minutes more)
- Reader: an artist opening the kit for the first time, using only the sandbox folder.

## Path taken

1. `kit/ARTIST.md`. The "What is in here" table sends you to "1. Figures". In section 1, `sheets/usr000.png`
   is "cycle000 - loop of 6 phases, 3x6, x26, driver port1". "driver port1" is the only clue that this is
   the player. Nothing says "Bill" or "run".
2. A later bullet in the same section says `figures/usr*-figure.png` shows "the same rows as one
   composed view at pixel precision ... Paint either surface, not both". Because of that, I treated
   `figures/usr000-figure.png` as the file to paint.
3. I viewed the four `figures/usr00N-figure.png` files, then the four `sheets/usr00N.png` twins,
   upscaled over a teal background (the copies went to scratchpad `view/`, and `kit/` was not touched).
4. I read these sidecars: `figures/usr00N-figure.json`, `sheets/usr00N.json` and `kit-part-sprites.json` (for `ids` and `dropped[]`).
5. I searched `guides/remastering-a-game.md` for figure, mirror and fused-figure guidance and read
   "Mark a figure and paint it whole" and "If something looks wrong".

**File I would paint for Bill's run: `kit/figures/usr000-figure.png`** (facing right). The
left-facing copy looks like `kit/figures/usr001-figure.png`. The alternative is
`kit/sheets/usr000.png` / `usr001.png`, the per-tile grids.

## Figure files

### figures/ (composed views)

| file | what it shows | frames | complete figure | notes |
|---|---|---|---|---|
| `figures/usr000-figure.png` | Bill (shirtless, blue pants, red bandana) running, facing right | 5 columns (label says "loop of 6 phases") | yes, all 5 | Head, torso and legs join and nothing is gapped. The upper body varies: frame 1 holds the gun low and diagonal, frames 2-3 hold it at the chest, frames 4-5 raise it steeply up. Legs: frame 1 is a wide stride, 2 = legs together, 3 = scissor, 4 repeats the legs of 2, 5 repeats the legs of 3. This reads as a run, but not as one clean cycle. It looks like two or three aim states mixed together (run-forward and run-aim-up?). Frame 1's rear foot touches the left edge of the box (x=0), so it may be clipped. |
| `figures/usr001-figure.png` | Same as usr000, apparently facing left | 5 | yes, all 5 | The poses match usr000 with a mirrored silhouette. Nothing says it is the mirror, or whether painting one updates the other. The JSON has no `mirror` field. It has its own pose ids (pose002/008/010/011/009), so I assume it is painted separately. The label is word for word the same as usr000's. |
| `figures/usr002-figure.png` | Running figure with a red/brown helmet, pink torso and black-and-white legs. Probably the running enemy soldier | 5 (label says 6 phases) | yes, all 5 | Reads as a whole, attached figure. The label ("2x4, x3", no driver) is the only hint that it is not the player. |
| `figures/usr003-figure.png` | Grab bag of 10 figures "no cycle or sequence ordered" | 10 across 6 rows | mixed | Row 1: Bill standing and firing, facing right, then facing left (complete). Row 1 col 3 and row 6: Bill somersault tiles with the soldier's black-and-white legs hanging under them, which looks like **two figures fused**. Row 2: Bill prone (complete). Row 3: two figures, each Bill's blue legs flipped upward on top of a soldier's torso and legs, again **fused-looking**. Rows 4-5: Bill somersault balls (complete). The kit says fused poses (pose021/022) were removed, but at least 4 of these 10 look like fused Bill+soldier figures that were not flagged. |

### sheets/ (per-tile grids, the twins of the above)

| file | what it shows | frames | complete figure | notes |
|---|---|---|---|---|
| `sheets/usr000.png` | Bill run, facing right, as 8x8 tiles on a grid with a 1 px gutter | 5 | no (by design) | Every figure is cut by the gutter lines. The upper body sits on a different 8 px grid from the legs, so in frame 1 the foot floats apart and the legs look offset. Nobody can paint a character well on this. The figure view fixes it. |
| `sheets/usr001.png` | Bill run, left-facing variant | 5 | no | The tiles here look horizontally mirrored compared with the figure view (the head is on the other side). That is confusing when you compare the two surfaces. |
| `sheets/usr002.png` | Soldier run | 5 | no | Same gutter effect. |
| `sheets/usr003.png` | The 10 ungrouped figures | 10 | no | Same gutter effect. The fused-looking figures are easier to spot here, because they have blue Bill tiles next to black-and-white soldier tiles. |
| `sheets/usr004-020.png` (scenery) | obj000-obj012 plus bg063/077/079/083 | n/a | n/a | I could recognise them: mountains (004, 005), grass-on-rock ledges (006, 007), jungle trees (008, 009, 010, 017, 019), rock faces (011, 012, 013, 016, 020), water surface (014, 015) and a mountain top (018). **Near-duplicates:** 004/005, 006/007, 014/015 and 011/012 look almost identical, with no word on how they differ. usr004 mixes a mountain and a jungle tree in one "element". All captions are bare ids (`obj003`, `bg077 + 1 cells...`). |

**Run cycle identifiable and paintable unaided: no.** I found it quickly, and the composed figures are
whole and paintable, but I could not tell unaided whether usr000 is one run animation. The upper body
changes between frames, and the kit does not say which of the "6 phases" were merged into 5 columns.
Getting the painted figure back into the pack needs `mep_figure.py import <pack>`. That tool and the pack are not in the kit.

## Guesses and friction points

1. **No names.** The kit never says "Bill", "player", "run" or "enemy". I picked the run from
   "cycle000 ... driver port1" and from looking at the pictures. `names.json` is mentioned, but none exists.
2. **"Loop of 6 phases" but 5 columns.** ARTIST.md explains that a repeated silhouette is folded into
   one column. It does not say *which* phase is missing, or what order the 6 phases play in
   (for example 1-2-3-4-5-?), so I cannot preview the loop or check it.
3. **The run mixes upper-body poses.** Frames 2-3 and 4-5 share the same legs but hold the gun
   differently. My guess is that the recording caught Bill switching aim while running. Nothing in
   the kit confirms whether this is "the run" or two animations fused into one row.
4. **Two surfaces, one choice.** "Paint either surface, not both". Figure view or tile sheet? I guessed
   the figure view. But the "When you are done" copy recipe only copies `sheets/*.png` and `chr/*.png`,
   never `figures/`. A painted figure only reaches the pack through `python3 scripts/mep_figure.py
   import <pack> ...`, and the kit names no concrete pack path for it (the sheets rebuild bullet does name an
   absolute path outside the kit). An artist following the "When you are done" recipe would lose their figure work without being told.
5. **usr000 vs usr001.** They carry the same label and look like mirror images. I guessed that usr001 is the left-facing
   run. It is unclear whether I must paint both, and whether a mirrored game draws usr001 from usr000's tiles
   ("a tile shared by two phases is one tile" suggests shared tiles, but these ids differ).
6. **Fused figures still on usr003.** At least 4 of the 10 figures look like Bill+soldier composites,
   yet the kit says fused poses were removed. The guide admits "the kit's box is a grouping, not a
   truth", so I would have to guess which pixels belong to Bill.
7. **Bits of the doc do not match the files:** ARTIST.md says "files[].ids" and "dropped[] in this fragment".
   These live in `kit-part-sprites.json`, which ARTIST.md never names. `sheets/usr*.json` has `poses`, not `files`.
8. **Scale vs. figure size:** `figures/usr000-figure.json` gives `pixelSize` 131x48 at `scale` 4, but the PNG is
   524x192. I guessed that pixelSize is the 1x size. The `.orig.png` name suggests the 1x reference too (the guide says so), but it is still
   not obvious whether I paint at 4x or 1x.
9. **Clipping:** frame 1 of usr000-figure touches the box edge. I cannot tell whether the leg is clipped or the box is just tight.
10. The scenery near-duplicates (for example 014/015) have no note on how they differ (palette? phase? position?).
11. Instructions point at `scripts/mep_build.py`, `scripts/mep_lint.py` and `scripts/mep_figure.py`. None of these is in the kit,
    so an artist with only this folder cannot rebuild or import.

## Stops (files read outside the sandbox)

None. All reads were inside `coldread-contra-kit/`. Upscaled viewing copies went to the scratchpad's
`view/` folder, which is outside the sandbox but write-only scratch, not a source read. `kit/` was not modified.
