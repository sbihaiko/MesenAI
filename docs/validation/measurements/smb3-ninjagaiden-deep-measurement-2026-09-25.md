# Super Mario Bros. 3 and Ninja Gaiden deep measurement (2026-09-25)

**Date:** 2026-09-25
**Binary under test:** `068463e3e` (`main` plus the stage sets of PR #497,
since merged as `0e37e8f8c`). Both games lack #496 (`a3c128f5`, merged
after the runs).
**Protocol:** steps 1–4 of `excitebike-deep-measurement-2026-09-24.md` and
`castlevania-deep-measurement-2026-09-24.md`, as re-run in
`deep-remeasure-ceab80a9-2026-09-25.md`.
**This is a baseline.** Neither game had a deep log before.

> **Headline caveat: coverage describes one slice, not the game.** Both
> routes pace inside the start of their first level. SMB3's never leaves
> World 1-1's first screen and a bit: 358 drawn keys. Ninja Gaiden's stays
> in the first ~1.5 screens of Act 1-1 (one small scroll toward the second
> pillar and back) and meets one enemy type: 183 drawn keys. Every figure
> below (90.5 %, 100 %, 0 colourways) is for those keys only. The map,
> later worlds and acts, power-ups and palette changes are only the ROM
> export.

## What ran and why

PR #497 added stage sets (`scripts/stages/smb3/`,
`scripts/stages/ninjagaiden/`) for two games with no deep measurement.
Both are CHR ROM, like Excitebike, on mappers not yet in the set: SMB3 is
MMC3, Ninja Gaiden MMC1 (SLROM). Each ran the same steps 1–4: mint, two
recording passes with a determinism check, the F14.4 palette gap, the
artist kit with every `--verify`, and the painted round trip with in-game
screenshots.

- The scratch scripts are copies of the `ceab80a9` Excitebike re-measure's,
  with paths changed. `classify.py` now reads the CHR offset from the iNES
  header (`16 + PRG × 16 KB`); Excitebike's copy hard-coded 16 KB PRG.
- Each game's results were checked independently by a second (Sonnet)
  pass, and every number reproduced.

## Binary and provenance

Each game ran in its own worktree at `068463e3e12a233b99778a7ea2faf8645af330f2`,
clean, with `make capture-tool` built from a clean object tree using the
CommandLineTools make/clang++ and `-isysroot …/MacOSX.sdk` (rc 0).

| Item | SMB3 | Ninja Gaiden |
|---|---|---|
| Worktree | `/Users/bihaiko/deep-new` | `/Users/bihaiko/deep-ng` |
| `MesenCore.dylib` sha256 | `946fd36237aea7e7820d49827dec183caed20da9fae33e5d43de5fb9443672d2` | `41b72cbc0b56be0813042d65ef23d3577c0a7d0f393addae6202c70ff7b7c9f5` |
| `scripts/headless_record` sha256 | `dcbd3bee175c9a0bf9d31c14a2574c9698e0e6d36268dc8d4ba4ddb42ad5c6b8` | `6560317d671486ca1cb2a7d5b53e3c1296e44dad545efd1f1ab2d0ff047a1012` |
| `otool -L` | resolves the worktree's dylib | resolves the worktree's dylib |
| #474 (`nm` + `c++filt`) | 4 `HdShapeKey` instantiations | 3 `hash_table<…HdShapeKey…>` lines |
| #488 / ADR-0232 | `MesenSheets::ChrBankHashes::BankIdOf<HdBuilderPpu::DrawPixel…>` present | same, 1 symbol |
| #479 | header-inline, no symbol; not claimed | same |
| ROM whole-file sha1 | `6bd518e85eb46a4252af07910f61036e84b020d1` | `ca513f841d75efeb33bb8099fb02beeb39f6bb9c` |
| Cartridge | mapper 4 (MMC3), 16 × 16 KB PRG, 16 × 8 KB CHR ROM | MMC1A / NES-SLROM, 128 KB PRG, 128 KB CHR ROM |

Both ROM sha1s match their `stage-set.json`.

## Super Mario Bros. 3

| Metric | Value |
|---|---|
| Mint | 1 023 frames, `result: ok`; `stage1.mss` sha256 `7b4001ad…6202fd`. A 1 s screenshot shows World 1-1 start (small Mario, 4 lives, timer 299) |
| Recording, two passes | 60 s → 4 630 frames (1 023 pre-loaded + 3 607 emulated), `result: ok` both. `diff -r` clean; grid, OAM and pose dumps `cmp`-identical. `hires.txt` sha256 `57dc7362…ebaf6c29` |
| End state (60 s) | 4 lives, score 200, timer 220, still in 1-1 near the first pipe |
| Keys / drawn keys | 8 550 / **358** (310 BG, 48 sprite) |
| Indices / drawn | 8 192 / 240 |
| Drawn keys on a sheet cell | **324/358 = 90.5 %** (sprite 48/48, BG 276/310) |
| Served by a cell or `folds` | **358/358 = 100 %** (base 240, variant 84, fold 34) |
| Drawn keys with no cell | 34, all BG, on 22 shapes, all in `folds`; resolved: 34 inert |
| Colourways (keys on a variant cell) | **84** (2 sprite, 82 BG: the 4-palette block/bush/ground set); 45 variant cells |
| `artist_kit --verify` | 8 550 → 358 → 358, **PASS** |
| `artist_bg_kit --verify` | 13 objects, 5 elements, 358 → 358, **PASS** |
| `artist_chr_kit --rom --verify` | 8 550 → 8 550 byte-identical, **PASS**; "recorded 243 (3%), ROM fill 7949" |
| `artist_map --verify` | rc 1, the documented CHR ROM refusal (ADR-0172) |
| Assemble | 3 parts, 75 files, 12 628 cells, all passed |
| Poses / fused (ADR-0228) / HUD-excluded | 37 / 13 / 0; 8 figure sheets, 130 figure cells |
| Painted cell | `usr001` `pose000` node 18 (Mario's head, `14C4`/`FF16360F`, 187-repeat walk cycle), 576 px at 4× |
| Builds / imports | rc 0, 0 errors each build; 8/8 imports verify 358 → 358; build 3 byte-identical to build 2 |
| Painted vs control `hires.txt` | 2 rules of 1 key differ, by design (ADR-0231); key sets equal (358/358) |
| Import target | **`sheets/sprites.png`**, not `usr001.png`; painted build2/3 warn "size not a whole number of cells" |
| In game, frames 1204 (3 s) and 8 s | **576 magenta px painted / 0 control** both times; 598 px differ (576 + 22) |
| #447 check (recorded pack vs control rebuild) | 0 px differ at 3 s and 8 s |

Figures by eye: two small-Mario walk cycles (`cycle000` 2 × 199 and
`cycle001` 2 × 187, both driver port1), a Goomba walk, four Piranha Plant
sequences and a 7-figure rest grid.

### A4 follow-up (2026-09-25): what one stroke on a cell costs

The painted cell above is a whole-cell stroke. Four arms on that same pack,
state and 3 s frame grade what a smaller one costs. Each re-runs the same
pipeline — kit copy, `mep_build build`, `mep_figure import`, `mep_build
build`, 3 s headless run from `stage1.mss` — and differs only in what was
painted on `figures/usr001-figure.png`, the `pose000` node 18 cell
(Mario's head, the cell of the row above).

| Arm | Painted on that cell | On-screen px that differ | magenta | never touched |
|---|---|---|---|---|
| Control (recorded rules) | — | — | — | — |
| Rebuild without painting | 0 px (the figure PNG written back unchanged) | **0** | 0 | 0 |
| One pixel | 1 px at (20, 4) | **106** | 1 | 105 |
| One 4×4 block | 16 px at (20, 4) | **116** | 16 | 100 |
| Whole cell | 576 px | **598** | 576 | 22 |

- Taken with `rt.sh <tag> <mode>`, `paint2.py` and `cmp.py` in
  `/Users/bihaiko/deep-new/runs/a4-analysis/` (the whole-cell arm is this
  measurement's own painted arm, `deep-smb3/rt/painted`). `cmp.py` counts a
  pixel as magenta only at exactly `(255, 0, 255)`; each arm's
  `SMB3_000.png` is compared against the control's. The screenshot is
  1024 × 960 (4×), and every diff sits in the same box,
  (456, 652)-(479, 679).
- The no-paint arm is a true control: its rebuilt `textures/hires.txt` is
  byte-identical to the control's (944 lines, 0 differing). What painting
  changes in the manifest is one key's rule — `[spr001_n1]<tile>0,14C4,
  FF16360F,256,40,1,N` moves from the recorded page to `sheets/sprites.png`
  — which is ADR-0231 §1: the whole 8×8 tile stops being the filtered page
  and becomes the sheet cell.
- The counts are what that whole-tile swap predicts, not a local edit:
  83 of the tile's px are where the sheet's nearest-neighbour and the
  page's xBRZ disagree, and 22 more are px the sheet cell does not draw at
  all. So 1 + 83 + 22 = 106, and 576 + 22 = 598. The 4×4 block is 116
  because 5 of its 16 px were already among the 83: 16 + (83 − 5) + 22.
  The 22 are the same 22 in all three arms — measured, a subset of both the
  onepx arm's 105 and the block arm's 100.
- Those 22 are **measured** to be pixels the sheet cell leaves fully
  transparent (alpha 0) and the recorded page inks, so the painted tile
  draws the backdrop there. That the page's ink is the scale filter's soft
  edge is **attributed, not proven** — by analogy with Excitebike's A4, and
  the opaque-count gap between the `chr/` and sheet crops is 12, not 22.
- One caveat carried from the arms: SMB3's capture frame count jitters
  between replays of the same state (see "What this does NOT prove"), but
  pixels outside the painted box were identical, and all four arms show the
  same box.

## Ninja Gaiden

| Metric | Value |
|---|---|
| Mint | 1 083 frames, `result: ok`; `stage1-run.mss` sha256 `1cb5a5b6…bdce01`. A 1 s screenshot shows Act 1-1, Ryu standing, P-02 |
| Recording, two passes | 60 s, frames 1 083 → 4 689 (3 606 emulated), `result: ok` both. `diff -r` clean; grid, OAM and pose dumps `cmp`-identical. `hires.txt` sha256 `b7520b02…b4b467` |
| Route | timer 145 → 92, score 100 → 400, no life lost; club thugs only |
| Keys / drawn keys | 8 375 / **183** (102 sprite, 81 BG) |
| Indices / drawn | 8 192 / 183, one palette each |
| Drawn keys on a sheet cell | **183/183 = 100 %** |
| Missing / colourways / variant cells / folds | **0 / 0 / 0 / 0** (trivially: no index drawn in a second palette) |
| `artist_kit --verify` | 8 375 → 183 → 183, **PASS** |
| `artist_bg_kit --verify` | 13 objects, 3 elements, 56 scenes, 183 → 183, **PASS** |
| `artist_chr_kit --rom --verify` | 8 375 → 8 375 byte-identical, **PASS**; "recorded 157 (2 %), ROM fill 8 035" |
| `artist_map --verify` | rc 1, the documented CHR ROM refusal |
| Assemble | 3 parts, 121 files, 9 927 cells, all passed |
| Poses / fused / HUD-excluded | 103 / 67 / **1 (`pose003`)**; 11 figure sheets, 35 figures |
| Painted cell | `usr000` `pose005` node 24 (Ryu's torso, `0x21`/`FF072235`), 896 px |
| Builds / imports | 0 errors, 0 warnings each build; 11/11 imports verify 183 → 183; build 3 byte-identical to build 2 |
| Painted vs control `hires.txt` | 3 rules of 1 key differ, by design (ADR-0231); key sets equal (183/183) |
| Import target | **`sheets/sprites.png`**, not `usr000.png`; 0 build warnings |
| In game, frames 1143 (1 s) and 1864 (13 s) | **896 magenta px painted / 0 control** both times; 905 px differ (896 + 9) |
| Frame 2946 (31 s) | 0 / 0; Ryu not drawn (flicker after a hit) |
| #447 check | 0 px differ at all three frames |

Figures by rendered pose: Ryu walking both facings (`cycle000` ×70,
`cycle001` ×49, driver port1), his somersault jump, the club thug
swinging, 1/1-hold flicker cycles (not animation), and `seq004` ending in
the landing pose `pose003` (hold 164, 494 frames).

## Bug classes

| Class | SMB3 | Ninja Gaiden |
|---|---|---|
| #447 (rebuild draws NN over xBRZ) | absent | absent |
| #449 (CHR kit `seen:true` on a never-drawn index) | absent: 381 cells = the 358 drawn keys, 0 never-drawn | absent: 183 cells, all drawn |
| #493 (moving figure classified as HUD) | not seen: 0 HUD-excluded; the SMB3 HUD is BG | **present**: `pose003` (8 tiles, all `screenFixed: true`) left out as HUD |
| #498 / Excitebike A3 (import lands in `sheets/sprites.png`) | **present**, with the warning | **present**, without the warning |
| Excitebike A4 (a painted cell swaps the whole tile to nearest-neighbour, so more px change than were painted) | present: whole-cell stroke 598 px (576 magenta + **22 attributed** to the xBRZ fringe) — see the A4 follow-up | present: whole-cell stroke 905 px (896 magenta + **9 attributed** to the xBRZ fringe) |
| B1 / Punch-Out!! C | absent | absent |
| B2 (`artist_map --verify`) | n/a (CHR ROM refusal) | n/a |
| B3 (blank member tiles) | not exercised | not exercised |
| #343 (painted keys claimed by another crop) | not seen | not seen |
| #339 class (captured screen over-matches) | not seen | **present** (#499) |

## Findings filed

- **#493 reproduced in Ninja Gaiden.** Ryu's landing pose `pose003` is
  pinned for its 494 frames and lands in ARTIST.md's "Left out" as HUD.
  It is fixed by #496: swapping #496's `artist_kit.py` into this tree's
  `scripts/` (`side496/`) lays `pose003` out via `seq004`, drops 0 poses
  as HUD, and still verifies PASS.
- **#498 (new): figure import writes into `sheets/sprites.png`.** In both
  games the painted key's own figure row exists (`usr001`, `usr000`), and
  neither has a HUD-excluded rider, yet the import writes the cell into
  `sprites.png`, the sheet ARTIST.md tells artists not to paint. The
  `ceab80a9` Castlevania raw results show the same landing. So the
  Excitebike re-measure's attribution of A3 to #493 is incomplete: #493
  is not needed for it. The probable cause, not proven, is ADR-0231: after
  build 1 every untouched key draws from `chr/`, so no `usr*` sheet owns
  the key at import time.
- **#499 (new, P1): a stale captured `<background>` freezes the live HUD
  in Ninja Gaiden.** At frame 2946 (31 s) the pack-less run shows TIMER
  120, SCORE 000100 and a damaged ninja bar. With the recorded pack or the
  control rebuild loaded, the same frame shows TIMER 149, SCORE 000000, a
  full bar and a different left wall. Removing the 15 `<background>` rules
  restores 120 / 100. The emulation and sprites are unaffected. Mechanism
  as in #339: 56 captures, each gated on 3 `tileAtPosition` probes, which a
  static-HUD game matches frame after frame.
- **Not a bug: Ninja Gaiden's CHR fill twins (D2).** 182 of 183 evidence
  indices also appear as a ROM-fill cell on another page, because the
  recorder's `chr/Chr_00_*` pages mix bank 0 and bank 16 tiles while
  `artist_chr_kit` completes every real bank's rank-0 page from the ROM.
  The tool's docstring documents rank-0-page completion, and with
  `--fill-rules none` (the default) a fill cell emits no rule, so painting
  it does nothing in game. ARTIST.md could say so more plainly; that is a
  clarity improvement, not a defect.

## What this does NOT prove

- Coverage beyond the two slices (headline caveat).
- The cause of #498 (the ADR-0231 ownership hypothesis) was not checked in
  the `mep_figure` source and not bisected.
- That the #496 side check matches a real #496 tree: only `artist_kit.py`
  was swapped in.
- SMB3's capture frame count jitters between replays of the same state and
  input at 8 s: 1 505 / 1 506 / 1 507 frames (target 1 504) across the
  auto, painted and control runs; at 3 s all three were 1 204. Pixels
  outside the painted box were identical. Not investigated.
- SMB3's 13 fusions, 11 of them with the Piranha Plant (`pose017`), were
  not audited pose by pose. The orange 1×2 tile pair under the plant's stem
  on the figure sheets (not seen in the game frames checked) is not
  identified.
- A4's size: the non-magenta changed px a whole-cell stroke leaves (SMB3's
  22, Ninja Gaiden's 9) are attributed to the xBRZ fringe by analogy only;
  for SMB3 the opaque-count gap between the `chr/` and sheet crops is 12,
  not 22.
- SMB3's CHR kit "recorded 243" against 240 drawn indices is not
  reconciled. #499's extent (how many of the 3 606 frames get a stale
  capture) was not measured.
- #479 is in the binary (no symbol).
- The paint looks right: the magenta is a probe that reaches the screen,
  in frames picked by a scan.
- Wall clocks: each game's two passes ran concurrently.

## Raw material

Unversioned. Each directory has a `RESULTS.md` with the exact commands,
logs, scratch scripts, packs, kits and screenshots:

- SMB3: `/Users/bihaiko/deep-new/runs/deep-smb3/` (`run1/SMB3/auto`,
  `kit/`, `rt/`, `game/`, `gap.txt`, `classify.txt`, `contact-figs.png`,
  `painted-3-crop.png`).
- Ninja Gaiden: `/Users/bihaiko/deep-ng/runs/deep-ninjagaiden/`
  (`run1/NinjaGaiden/auto`, `kit/`, `rt/`, `game/`, `route/` with the
  #499 arms, `side496/`, `provenance.txt`, `poses-a.png`, `poses-b.png`).
