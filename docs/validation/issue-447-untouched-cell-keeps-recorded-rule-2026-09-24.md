# Issue #447 — an unpainted kit rebuild renders exactly what was recorded (2026-09-24)

Scope: issue #447, decided by ADR-0231. The issue: rebuilding a kit without
painting anything changed what the pack drew. Every untouched sheet cell's
key was re-pointed from the recorder's pattern page (`chr/Chr_*.png`, run
through the xBRZ filter by `HdPackBuilder::GenerateHdTile`) to the cell's
sheet crop (nearest-neighbour, `SheetRender::RenderTile`). The F14.4 runs
counted 901 of 912 Castlevania rules and 360 of 367 Zelda rules drawing other
pixels than the recording. The layered sibling pack did not hide it, because
`MergeLowerLayer` skips an `auto/` tile whenever `textures/` has the same key.

The fix: `mep_build build` re-emits the recording's own rule for a cell equal
to its `*.orig.png` twin (`scripts/mep_recorded.py`). Only a painted cell
points at its crop.

## 1. Unit tests: red before the fix

`scripts/test_mep_build_recorded.py` (wired into `make doc-checks`) builds
synthetic recorded packs from `test_mep_build.py`'s fixtures. Its recording
includes a `memoryCheck` condition, a conditional rule with its bare twin,
a `0.5,Y` brightness rule and trailing fields. The final test file, run
against origin/main's `scripts/` (exported with `git archive`), exits 1:

```
FAIL: #447: an untouched cell's rule is not the recorded one (9 of 9): 000D1A27: sheets/obj000.png 2,000D1A2734414E5B00050A0F14191E23,0F162A30,2,2,0.5,Y,700000,0; [c0]0714212E: sheets/obj000.png ...
FAIL: #447: 9 of 9 untouched rules render other pixels than the recording
PASS: #447: a conditional recorded rule stays above its bare twin
FAIL: #447: the first build did not keep the recording as textures/hires.recorded.txt
PASS: #447: a second build of an unpainted pack is byte-identical
FAIL: #447: painting one cell moved an untouched cell off its recorded rule: ['sheets/obj000.png', ...]
FAIL: #447: a cell painted and reverted did not come back to the recorded rule: ['sheets/obj000.png', ...]
FAIL: #447/ADR-0178: a mirrored untouched cell does not keep its source key's recorded rule (off the page: [0, 1, 2, 3, 4, 5, 6, 7]; baked keys emitted: 0)
PASS: #447: the key set is the one the sheets carry, recorded rules or not (21 keys)
FAIL: #447: the build does not say that untouched cells fell back to the sheet crop:
FAIL: #447: the build does not say why the recorded rules were not used:
FAIL: #447: a pack that keeps the recording in auto/textures/ did not re-emit its rules
FAIL: #447/ADR-0172: an index-keyed untouched cell did not keep its recorded rule: ['40', '41', '41', '42', '43', '44', '45', '46', '47']
PASS: #447: check-coverage accepts an unpainted build (recorded rules) as a sheet-derived baseline
```

The four PASS lines are guards that must hold both before and after the fix:
rule order, idempotence, key-set parity and check-coverage.

After the fix, all 14 pass.

## 2. Mutation check

Each mutant was applied to the fixed code, the test file was run, and the
code was restored. All 8 mutants were killed:

| Mutant | Result |
|---|---|
| untouched cells never take the recorded rule | killed |
| painted cells take the recorded rule too | killed |
| no snapshot before the first build overwrites `hires.txt` | killed |
| the `<scale>` guard removed | killed |
| a missing page not checked | killed |
| rules emitted in reverse order, not the recording's | killed |
| `check-coverage` ignores the recorded section | killed |
| the build does not route through the recorded rules | killed |

## 3. End to end: Castlevania

Inputs:

- an 80 s stage-1 recording (the "p1" recording of the castlevania-deep run);
- its kit;
- the `headless_record` binary from the F14 deep run (md5
  `0340da73954d027de0536450e63cd252`). `Core/` and `InteropDLL/` are unchanged
  between that build's commit `90703652` and `351ee096`, and this fix is
  Python-only.

The ARTIST.md recipe was followed literally:

1. `cp -R auto painted`.
2. Copy the kit's sheets, `chr` pages and scene screens in.
3. Build.
4. `mep_figure import` every figure.
5. Build.
6. Build a third time and `cmp` it with the second.

There were two arms:

- **control** — nothing painted;
- **painted** — one `usr002` figure cell painted magenta (#FF00FF).

Each arm was built with origin/main's scripts ("before") and with the fix
("after"). The metric counts rebuilt rules whose crop renders other pixels,
or carries other fields, than the recording's rule for the same key.

### Unpainted rebuild

| Build | Rebuilt rules | Differ from recording | Same | Never recorded |
|---|---|---|---|---|
| before | 782 | 764 | 11 | 7 |
| after | 782 | 0 | 775 | 7 |

- The "never recorded" 7 are keys the sheets carry that the recording never
  had. They stay on the sheet crop, and the build says so ("7 untouched cell
  rule(s) point at the sheet crop ...").
- The key sets are equal: 782 before and 782 after, and the control and
  painted arms match.
- In every arm, build 3 is byte-identical to build 2. Build 1 printed
  "kept the recording as textures/hires.recorded.txt".
- The kit's `chr/Chr_N.png` pages overwrite the recorded pages in this
  recipe. They hold every recorded crop byte for byte at the same position:
  3 779 of 3 779 crops.

### In game

The screenshots use the layered sibling pack beside the ROM: the recording
in `Castlevania/auto/textures` and the rebuilt `textures/` over it. Each run
starts from a minted stage-1 state with a scripted input.

| Frame | After vs recording | Before vs recording |
|---|---|---|
| t=40 s | identical (md5 `a85f42b4…`) | 40 115 px differ (4.08 %) |
| t=80 s | identical (md5 `ad33688d…`) | 2 900 px differ (0.30 %) |

### Painted arm (after)

- 3 rules point at `sheets/sprites.png` and draw magenta. They share key
  `FE7F3F3E…/FF273707`, with the conditions `[spr001_n6]` and `[spr008_n4]`
  and the bare twin.
- 772 rules stay recorded.
- At t=40 the screenshot differs from the recording by exactly 848 px, the
  painted pixels, 672 of them pure magenta. At t=80 it is identical, because
  the figure is off screen.
- The import printed "note: 1 painted cell(s) will re-point a key in
  hires.txt at the next build — reopen the ROM to see them; Reload Repainted
  Images cannot (ADR-0212)". That is the ADR-0231 consequence for the first
  paint of a cell.

## 4. What changes for the artist

- An unpainted rebuild now draws exactly the recording.
- The first paint of a sheet cell, or painting it back to the original,
  moves that key from the recorded page to the sheet: rebuild and reopen the
  ROM once. After that, repainting the same cell reloads in place.
- Painting a `chr/` page reaches every untouched key again and always
  reloads in place.
- A pack whose `textures/hires.txt` was already overwritten by an earlier
  build, with no `auto/` layer, has no recording left. Its untouched cells
  fall back to the crop, and the build says why.
