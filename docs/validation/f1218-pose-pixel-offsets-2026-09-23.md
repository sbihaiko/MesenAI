# F12.18 — pose pixel offsets on Contra (2026-09-23)

Measurement of PRD slice F12.18 (ADR-0225) against its bounded input: the
Contra stage-1 recording driven by `scripts/stages/contra/stage1-probe.txt`
and the phases of its run cycles
(`docs/validation/contra-pose-offsets-and-flicker-2026-09-23.md` §1 is the
"before"). Raw material (OAM dump, `tracks.txt`, pack, kit) lived in a session
scratchpad and is not versioned; this log keeps the numbers.

## Binary

- Branch `feat/f1218-pose-pixel-offsets` on `f0b9bb5e`, uncommitted F12.18
  diff; `make capture-tool` with the CommandLineTools toolchain.
- `InteropDLL/obj.osx-arm64/MesenCore.dylib` sha256 prefix `9b250b1d0b025f2e`;
  `grep -a` finds the new `"px": ` serialisation literal in it, and
  `scripts/headless_record` links that dylib by absolute path (checked with
  `otool -L`) — the recording below ran on the change.
- The ADR-0226 linker change (F12.19) is **not** in this binary.

## Recording

- ROM `Contra (1988) (Konami).nes`, sha1 `C9EA66BB…319B` (the dump
  `scripts/stages/contra/navigation.json` pins).
- State minted fresh (`.mss` is never versioned):
  `headless_record Contra.nes 16 <m> input=mint-stage1.txt save-state=stage1-probe.mss`
  (963 frames).
- Recording: `headless_record Contra.nes 70 <rec> bootstrap hdpack-off
  state=stage1-probe.mss input=stage1-probe.txt` with
  `MESEN_OAM_STREAM_DUMP` and `MESEN_POSE_TRACK_DUMP` set — 5 171 emulated
  frames, 16.5 s wall clock, 4 096 retained OAM frames (the cap).
- `poses.json`: 27 poses, 3 period-6 cycles (`repeats` 26 `port1`, 24
  `port1`, 3), 0 sequences; 74 tracks, 66 of them single-frame (the flicker
  F12.19 addresses — unchanged here, as expected).

## Stop conditions

| # | Condition | Result |
|---|---|---|
| 1 | every written tile carries `px`/`py`; `dx == ToCells(px)` in a unit test; an old sidecar reads through the fallback | **met** — 290/290 tiles carry `px`/`py`, 0 violate `dx == ToCells(px)` / `dy == ToCells(py)`; 118 tiles (8 overlapping poses) carry `z`. Core unit tests cover +4 px → `dx 1, px 4`, +3 px → `dx 0, px 3`, legs at `py 14` → `dy 2`, most-seen layout (RepeatCount-weighted) and its lexicographic tie-break, `z` from the earliest frame of the winning layout, no `z` on a non-overlapping pose. `scripts/test_pose_pixel_offsets.py` covers the reader fallback. `mep_lint.py` on the pack: 0 errors, 0 warnings. |
| 2 | `mep_figure.py export` of each run phase matches the recording's OAM frame pixel for pixel | **met** — table below: 10/10 distinct poses of the two player cycles. |
| 3 | the kit's figure row shows the phases with no intra-figure gutter and the row baseline intact | **met**, with a reading note — `kit/figures/usr000-figure.png` and `usr001-figure.png`: every tile of every figure at its `px`/`py` from the figure's origin (50/50 tiles), all figures bottom at y 48 (one baseline), 8–10 px between boxes. A row shows **5** figures for the 6 phases because the kit lays a repeated silhouette out once (phase 1 and phase 4 are the same pose, `pose001` / `pose002`) — the kit's existing rule, not a loss. |
| 4 | a painted export round-trips through `import` and `mep_build.py build`, 0 errors, key set unchanged, overlap handled as decided | **met** — `pose001` (overlapping, `z` on all 10 cells) exported, every opaque pixel painted magenta, `import --verify`: 9 cells written (the 10th is a blank tile), build errors 0, keys 172 → 172, 0 lost, 0 added. A re-export after the import equals the painted export pixel for pixel. |
| 5 | the Contra kit is regenerated from a fresh recording (shared with F12.19) | **partly** — regenerated from this fresh recording (`artist_kit.py --verify`: 4 sheets, 25 figures, verify PASS, 0 lost / 0 invented) and it carries the new Figures views. The recording does not include F12.19's linker, so the shared re-record the PRD names is still due once both slices are on one binary. |

### Condition 2 in detail

Method (stdlib script, not versioned): map every OAM-dump shape to its sprite
vocabulary node by `(tile, palette)` (148/148 shapes map exactly, none through
a flip), segment each retained frame the way `SpriteGrouping` does (DSU within
8 px, clusters of ≥ 4 tiles, identity = the rounded `(node, dx, dy)` set), and
for each pose of `cycles[0]`/`cycles[1]` tally the pixel layouts it was drawn
with, RepeatCount-weighted. Then export the pose with `mep_figure.py export`
and compare its `.orig.png` twin with a composite of one retained frame that
drew the written layout: the same vocabulary art at the OAM positions relative
to the cluster's top-left, drawn back to front in OAM order.

| Pose | Tiles | `z` | Written = most-seen layout | Layouts seen (frames) | Old `dx*8` error | Export = OAM composite |
|---|---|---|---|---|---|---|
| pose001 | 10 | yes | yes | 1 (447) | 4 px | yes, 0 px differ |
| pose005 | 10 | no | yes | 1 (228) | 3 px | yes, 0 px differ |
| pose004 | 10 | no | yes | 1 (229) | 2 px | yes, 0 px differ |
| pose006 | 10 | no | yes | 1 (228) | 4 px | yes, 0 px differ |
| pose007 | 10 | no | yes | 1 (224) | 3 px | yes, 0 px differ |
| pose002 | 10 | yes | yes | 1 (435) | 4 px | yes, 0 px differ |
| pose008 | 10 | no | yes | 1 (220) | 4 px | yes, 0 px differ |
| pose010 | 10 | no | yes | 1 (212) | 2 px | yes, 0 px differ |
| pose011 | 10 | no | yes | 1 (206) | 4 px | yes, 0 px differ |
| pose009 | 10 | no | yes | 1 (220) | 3 px | yes, 0 px differ |

"Old `dx*8` error" is the largest distance between a tile's `dx*8`/`dy*8`
(what every composed view drew before this slice) and its real pixel offset —
the same 2–4 px the "before" log measured. Every run pose was drawn with a
single pixel layout on this stream, so the most-seen rule had no competitor to
break here; its tie and weighting behaviour is covered by the unit tests.

## Scope notes

- Sheets (`sprites.png`, `sprNNN.png`, `usrNNN.png`, their sidecars) and
  `mep_build.py` are unchanged; the kit's Figures view is an additional
  composed surface under `<kit>/figures/`, not a sheet, and `artist_kit.py
  --verify` copies only `sheets/usr*` into the rebuild.
- The composition editor's `Pack.pose_art` (pose rendering and picker) draws
  at pixel precision; its exported sheet (`pose_cells`) stays on the cell grid.
