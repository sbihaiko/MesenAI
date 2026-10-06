# Issue #401 — Contra rest-grid composites measured and labelled as fusions (2026-09-23)

Issue #401 reported a cold reader's visual judgement: in the Contra stage-1
kit, `sheets/usr003.png` / `figures/usr003-figure.png` (10 loose poses) show
Bill fused with an enemy soldier, although `ARTIST.md` said only 2 fused poses
were left out. This log measures the claim, records the root cause, and
measures ADR-0228's fix before and after on the same state and driver. The raw
material (recordings, kits, the scan script) lives in a session scratchpad and
is not versioned; this log keeps the numbers.

## Input

- "Before": the `full2` recording and `kit-clean` kit of
  `f1218-f1219-contra-rerecord-2026-09-23.md` (on the branch
  `docs/f1218-f1219-contra-rerecord`), binary at `c16f7c4c`. Copied into this
  session's scratchpad and not modified.
- "After": a fresh recording on this branch's binary, same ROM
  (`c9ea66bb7cb30ad5343f1721b1d4d3219859319b`), same minted `stage1.mss`
  (frame 1804), same driver:
  `headless_record Contra.nes 61 <after>/rec bootstrap hdpack-off
  input=scripts/stages/contra/stage1-probe.txt state=stage1.mss` — 5471
  frames, `result: ok`.

## Measurement: what each `usr003` pose is

Nodes are sprite-vocabulary indexes (`poses.json` `tiles[].node`), and their
palettes come from `adjacency.json` `sprites.nodes[]`. Bill wears `FF36160F`
over `FF37120F`, as do the walking/aiming poses 000–011. The running soldier
(cycle002: poses 012, 014–017) wears `FF36160F` over `FF20000F`
(black/white legs).

| pose | frames | tiles | contents | verdict |
|---|---|---|---|---|
| `pose001` | 439 | 12 | Bill aiming (nodes 34–38, 5–8, 3) | one figure |
| `pose003` | 388 | 12 | Bill aiming, mirrored (60–64, 9–12, 4) | one figure |
| `pose026` | 3 | 16 | all of `pose017` (soldier, 81–88) plus a Bill death-tumble frame (115–121 and the blank tile) | **two figures** |
| `pose013` | 58 | 8 | Bill lying dead (89–93) | one figure |
| `pose018` | 7 | 16 | all of `pose016` (soldier, 69–72, 85–88) plus a tumble frame (94–100 and the blank tile) | **two figures** |
| `pose020` | 5 | 16 | all of `pose012` (soldier, 73–80) plus a tumble frame (101–107 and the blank tile) | **two figures** |
| `pose019` | 6 | 8 | Bill tumbling, alone (122–128) | one figure |
| `pose023` | 4 | 8 | Bill tumbling, alone (129–135) | one figure |
| `pose024` | 4 | 6 | Bill tumbling, alone (136–141) | one figure |
| `pose025` | 3 | 16 | all of `pose017` plus the same tumble tiles as `pose026`, at another offset | **two figures** |

Supporting evidence:

- **Timing.** Along `pose025 → pose026 → pose018` the soldier half steps
  through cycle002's own phases (017, 017, 016) while the Bill half steps
  through tumble frames.
- **Input.** The driver never pressed A (`poses.json` `input.never`), so the
  ball is the death tumble, not a jump.
- **Why the tumble frames never stood alone.** Bill dies on contact, so the
  first tumble frames are always drawn over the soldier who killed him.
- **What cannot separate the halves.** Palette and OAM rank don't: both
  halves use `FF36160F`, and the ranks interleave in 8x16 pairs (in
  `pose020` the soldier holds z 0,1,4,5,8,9,12,13 and Bill holds
  2,3,6,7,10,11,14,15).
- **No OAM dump.** The run directory has none.
- **Visual check.** The enlarged sheet and figure view show the same thing:
  four Bill balls on soldier legs.

The reader said "at least 4"; the measurement is exactly 4.

## Root cause

The detector did what ADR-0177 says, but no ADR covered this case. ADR-0177
§1 marks a fusion only when the remainder is itself a kept pose, and ADR-0179
§4 marks a variant only when the remainder has fewer than `kPoseMinTiles` (4)
tiles. A kept pose plus an 8-tile remainder that was never kept alone falls
between the two rules. A scan of every non-fused pose, on both the `dx`/`dy`
and the `px`/`py` layouts, finds exactly the four entries above in that gap.
The user picked "treat as fused", which became ADR-0228.

## Binary

- `make core`, `make capture-tool`, CommandLineTools clang/SDK. `SpriteGrouping.o`
  (22:21:19) is newer than the final `SpriteGrouping.cpp` (22:19:46), and
  `InteropDLL/obj.osx-arm64/MesenCore.dylib` (22:21:48) is newer than both.
  The dylib's sha256 is
  `84cba9d2ad3eecc7f9200903aa980bc019dc0290037efb23b89f7603ed202351`
  (the before binary's was `6f210da4…`). `otool -L scripts/headless_record`
  resolves the dylib to this worktree.
- Behavioural proof: the new recording writes one-id `fusionOf` lists, which
  only the ADR-0228 branch produces (below).

## Before / after

| metric | before (`full2`, `kit-clean`) | after (this binary) |
|---|---|---|
| retained frames / poses / tiles | 3667 / 27 / 292 | 3667 / 27 / 292 |
| cycles (period, repeats) / sequences | (6,26) (6,26) (6,3) / 0 | identical / 0 |
| `poses.json` apart from `fusionOf` and `label` | — | identical, entry by entry |
| fused poses | 2: `pose021` [004, 012], `pose022` [013, 014] | **6**: those two plus `pose018` [016], `pose020` [012], `pose025` [017], `pose026` [017] |
| variants | 0 | 0 |
| `usr003` (rest sheet) | 10 poses, 114 cells | **6 poses** (001, 003, 013, 019, 023, 024), 50 cells, all Bill alone (checked visually) |
| `ARTIST.md` | "2 fused pose(s)" | "6 fused pose(s)", the four new ones citing ADR-0228 and naming their one part |
| tumble tiles (21) on the pack's `sprites.json` | 21 | 21 (cells or aliases); also on `spr003`/`spr004`/`spr006` |
| tumble tiles on the kit's CHR pages | 21 (with flips) | 21 (with flips) |
| `unsorted` sheet | not written | not written (every sprite shape is claimed by `sprites.png`, so the tumble tiles never reach it) |
| `sprNNN` `poses[]` | `spr003` [018], `spr004` [020], `spr006` [025, 026], and the soldier sheets citing the composites | `spr003`/`spr004`/`spr006` cite none; `spr002`/`spr022`/`spr023`/`spr024` cite only the soldier poses (ADR-0177 §6) |
| `artist_kit.py --verify` | 172 → 172, 0 lost / 0 invented | 172 → 172, 0 lost / 0 invented, PASS |
| background / CHR `--verify` | 0/0, 0/0 | 172 → 172 0/0; 1834 → 1834 0/0 |
| `mep_lint.py` on the recorded pack | 0 errors, 0 warnings | 0 errors, 0 warnings |
| `mep_build.py build` | 0 errors (29 sheet-size warnings) | 0 errors (29 sheet-size warnings) |

Sheet PNGs and `hires.txt` also differ byte-wise between before and after.
That is run-to-run noise, not this change: the two recordings of the source
log (`full`, `full2`), on the same binary and driver, already differ in 49
files. Poses, tracks (9) and cycles reproduce exactly.

## Tests

- `make core-unit-tests`: 1037/1037. New cases (Bloco P): a kept pose plus
  an 8-tile remainder that never stands alone is a one-part fusion
  (`FusionOf == {0}`), not a variant, and serialises as
  `"fusionOf": ["pose000"]`. The same holds at a remainder of exactly
  `kPoseMinTiles`. A kept pose plus a 3-tile remainder is still a variant.
  The ADR-0177 two-part cases are unchanged.
- Mutation: disabling the ADR-0228 fallback in `LabelPoseFusions` fails 4
  cases (1033/1037): both one-part fusion checks and both one-id `fusionOf`
  serialisation checks.
- `python3 scripts/test_artist_kit.py`: 14/14, including the new
  one-part-fusion case. Mutation: disabling the one-part branch in
  `_dropped` fails it (13/14), because the message then claims "both halves
  are laid out". `python3 scripts/test_compose_engine.py`: 37/37.
