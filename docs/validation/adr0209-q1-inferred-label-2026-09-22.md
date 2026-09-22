# ADR-0209 Q1 — the Core writes a default `label`, marked inferred (2026-09-22)

Decision under test: ADR-0209 **Q1 = (b)** — `label` is written by the Core at
record time, inferred from the grouping it already computed (`metatile`,
`context`, `count`, the pose and run tables), as a default the artist renames.
Bound by ADR-0183 §3 (inference is marked, never confused with evidence) and
§5 (a caption comes from the recording's ids or a human's `names.json`; no
generator invents a character name).

Implementation: `Core/NES/HdPacks/SheetLabels.h` (host-free scheme),
`SheetRender.cpp` (`SerializeSheet`, `SerializePoses` write `label` +
`"labelSource": "inferred"`), readers `scripts/compose_engine.py`
(`caption()`, `read_label()`, `Pose`/`PoseRun`/`Sheet.label`),
`scripts/artist_kit.py` (`grid_title`), `scripts/mep_figure.py` (`export`
writes `label`/`labelSource`, `--names`). Suites: four cases in
`scripts/core_unit_tests.cpp` (966/966 pass after the rebase onto F12.13's main; 953/953 before it), `scripts/test_sidecar_labels.py`
(31 checks, wired into `make doc-checks`).

## The scheme (ASCII, deterministic, no subject ever guessed)

| entry | label | absent when |
|---|---|---|
| sheet cell | `<ctx> #<metatile> x<count>` or `<ctx> cell <index> x<count>`; `<ctx>` is `sprite` on a sprite sheet, else the cell's `context` | count 0 and no vocabulary index (then `label` stays `""` and no `labelSource` is written) |
| `sprNNN`/`objNNN` sheet (top-level) | `<kind> group <cols>x<rows>, <n> cells[, <p> poses], x<max count>` | vocabulary sheets (`metatiles`, `sprites`, `hud`, `misc`, `unsorted`…) |
| pose (`poses.json`) | `figure <w>x<h>, <t> tiles, <f> frames[, fusion][, variant of poseNNN]` | a pose with no tiles |
| cycle / sequence | `<loop\|sequence> of <n> phases, <w>x<h>, x<repeats>[, driver portN]` (`<w>x<h>` = largest phase box) | a run with no phases |

Every label travels with `"labelSource": "inferred"`. Reader precedence, one
function (`compose_engine.caption`): **names.json > sidecar `label` > id**,
and the result says which won (`names` / the label's own `labelSource` /
`id`). An artist renames by editing the sidecar's `label` and setting
`labelSource` to anything else; the readers then attribute it to them.

## Real: Contra, the 60 s stage-1 route, fresh dylib

ROM `Contra (1988) (Konami).nes` (the user's library copy, 128 KB, UNROM, CHR
RAM), copied into the session scratchpad. Worktree dylib rebuilt clean with
the CommandLineTools toolchain; **proved to carry the change before
recording**: `nm -C InteropDLL/obj.osx-arm64/MesenCore.dylib` lists the four
`MesenSheets::Infer*Label` symbols and `strings` finds the `"labelSource"`
literal. Isolated home (`<output_prefix>/mesen-home`, the tool's own),
`caffeinate -dimsu`, `.mss` minted first (30 s), then 60 emulated seconds:

```sh
scripts/headless_record rec/Contra.nes 30 rec/mint bootstrap hdpack-off \
  input=scripts/stages/contra/mint-stage1.txt save-state=rec/stage1-run.mss
scripts/headless_record rec/run/Contra.nes 60 rec/run/rec bootstrap hdpack-off \
  input=scripts/stages/contra/stage1-long.txt state=rec/stage1-run.mss
```

5 411 frames (target 5 410), 13.6 s wall clock, `result: ok`. Pack:
`rec/run/Contra/auto`, `textures/hires.txt` sha256 prefix `2375d7a35da45098`,
1 954 `<tile>` rules, 49 sidecars (44 group sheets, `metatiles`, `sprites`,
`unsorted`, `adjacency`, `poses`).

**The first 10 figures, as the Core labelled them** (top-level `label` of
`sprNNN.json`, all `labelSource: inferred`):

| sheet | label |
|---|---|
| spr000 | `sprite group 2x5, 9 cells, 1 pose, x99` |
| spr001 | `sprite group 2x4, 8 cells, 4 poses, x255` |
| spr002 | `sprite group 3x4, 8 cells, 1 pose, x146` |
| spr003 | `sprite group 4x3, 7 cells, x193` |
| spr004 | `sprite group 3x4, 7 cells, 2 poses, x24` |
| spr005 | `sprite group 3x4, 7 cells, 4 poses, x24` |
| spr006 | `sprite group 3x4, 7 cells, 1 pose, x24` |
| spr007 | `sprite group 3x3, 7 cells, 3 poses, x21` |
| spr008 | `sprite group 2x4, 7 cells, 1 pose, x8` |
| spr009 | `sprite group 2x4, 7 cells, 1 pose, x4` |

`spr003` cites no pose (its cells belong to no kept pose), so the label
carries no pose count — an absence stated, not filled in.

Cells: `spr000` 9/9 labelled (`sprite #81 x99`, `sprite #82 x99`, …);
`metatiles` 97/97 (`scene #0 x596`, `scene #1 x92`, …); `unsorted` 22/22
(`misc #0 x1`, …). Poses: 49/49 labelled — `pose000` `figure 2x2, 4 tiles,
2601 frames`, `pose001` `figure 3x2, 6 tiles, 217 frames, variant of pose041`,
`pose004` `figure 3x6, 10 tiles, 133 frames`. Runs: `cycle000` `loop of 6
phases, 3x6, x5`, `cycle001` `loop of 6 phases, 2x4, x2`, `seq000` `sequence
of 10 phases, 6x4, x2`, `seq001` `sequence of 4 phases, 3x7, x2`.

**Readers pick the labels up.** `mep_figure.py export`:

| figure | caption | source |
|---|---|---|
| `spr000` | `sprite group 2x5, 9 cells, 1 pose, x99` | inferred |
| `spr001` | `sprite group 2x4, 8 cells, 4 poses, x255` | inferred |
| `pose000` | `figure 2x2, 4 tiles, 2601 frames` | inferred |
| `spr001` with `--names {"figures": {"spr001": "a human-named group"}}` | `a human-named group` | names |
| `pose000` with `--names {"poses": {"pose000": "the human name for pose000"}}` | `the human name for pose000` | names |

`artist_kit.py` row captions on the same pack (no names file): `usr000` →
`cycle000 — loop of 6 phases, 3x6, x5`; `usr001` → `cycle001 — loop of 6
phases, 2x4, x2`; `usr002` → `seq001 — sequence of 4 phases, 3x7, x2`; the two
`rest` grids keep their previous count-based caption (no single entry to
label). Before this slice those rows read `cycle000 — a 6-phase loop, seen 5
time(s)`: the same measured facts, now stated once by the Core and attributed.

**Round-trip (ADR-0183 §4).** On a copy: `mep_build.py build` exit 0,
`mep_lint.py` exit 0 (0 errors, 40 size warnings about the recorder's own
sheets), all 49 sidecars byte-identical after the build. Keys 1 954 → 250 —
the bootstrap-vs-sheets drift ADR-0172 accepted, not the labels' doing,
proved by a **control**: the same recording with all 579 `labelSource` fields
stripped and every `label` blanked builds to 250 keys, symmetric difference
0, and a byte-identical `textures/hires.txt`.

## What this does not verify

- **ADR-0220's `guides` layer captions** (`ora_writer.py`): not on this
  branch at the time of writing, so the "prefer `label`" rule is in
  `compose_engine.caption` for it to call, not exercised.
- **`usrNNN` sheets composed by the editor** (`compose_engine.Pack.export`)
  still write `"label": ""` per cell — the Core is the writer Q1 (b) names,
  and duplicating the scheme in Python would let the two drift.
- **A human actually renaming** a label in a paint session. The rename path
  (`labelSource` other than `inferred`, or `names.json`) is asserted by the
  suite, not by an artist.
- **Stability across re-recordings**: two saves of one recording are
  byte-identical (unit test); two recordings of the same route are not
  asserted to agree on counts, and the ADR does not ask them to.
