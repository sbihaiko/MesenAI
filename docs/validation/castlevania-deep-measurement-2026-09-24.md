# Castlevania deep measurement: gameplay recording, palette gap, kit, round trip (2026-09-24)

Castlevania joins the deep measurements. Until now its only numbers came from
the F14.4 palette-gap run
(`docs/validation/f14.4-adr0230-palette-gap-measurement-2026-09-24.md`). That run was
60 s from power-on with no input, i.e. the title screen and the attract demo. This
log records **stage 1 played**: Simon walks right from the castle gate through
the entrance hall, whipping, crouch-whipping and jumping, past candles,
braziers, zombies, panthers and a bat. It measures four things on that
recording: the palette gap with the F14.4 method, the artist kit, a painted
round trip proven in game, and determinism. It decides nothing and ships
nothing. The raw material (packs, kit, logs, scratch scripts) is in the
session scratchpad and is not versioned. The input scripts are versioned
in `scripts/stages/castlevania/`.

## Headline

| Step | Result |
|---|---|
| 1. Record | 80 s of stage 1 from a minted state, 4 809 frames (4 768 scripted; the versioned route keeps the first 3 576, see Step 1). Two passes produced **byte-identical packs** (`diff -r` clean; `hires.txt` sha256 `1b84b252…a298`). Simon loses one life at about 55 s and respawns at the start of the hall. |
| 2. Palette gap | Drawn-key coverage is **75.4 %** (341 of 452; 111 missing). Idle was **84.7 %** (532 of 628; 96 missing), reproduced exactly on this binary. Colourways are **91 of 111** (88 of them sprites). Idle had 61 of 96. |
| 3. Kit | 4 parts, 148 files, 14 312 cells. Simon's walk is a **4-phase loop, 7 frames per phase (28-frame period), ×30**. The whip is 3 figures plus 4 loose whip-segment figures. The CHR RAM pattern pages are 93 % complete (302 recorded, 176 ROM fill, 34 red). The sprites, background and CHR parts all verify. **The map part fails `--verify`** (bug B2). |
| 4. Round trip | One figure cell painted magenta (848 px) and imported after a first build (#435). Both rebuilds had 0 errors. `hires.txt` is byte-identical to the unpainted control and the key set is the same (348 = 348). **In game, 848 px change and 672 are exactly `#FF00FF` on Simon's torso at frame 2766.** Painting the whole figure instead fails the build (bug B3). |
| Bugs | Three drafts below, none filed: B1 recorder sheet keys never drawn, B2 `artist_map.py --verify`, B3 `mep_figure import` into blank member tiles. #447 is present as known and was not touched. |

## Binary and ROM

- Worktree at `main` `46b9136a`, fresh `make capture-tool` with CommandLineTools
  clang. Private copies, relinked with `install_name_tool` and ad-hoc
  `codesign`: `MesenCore.dylib` sha256
  `f2d90bd17c96cf73aa2f216985e3f2871210cdbf80dc55fdb40602ab4bfc13dc`,
  `headless_record`
  `5c3e2765b99baa11f06c862a62cf4d46873e4eafff3914ddff56037882f825a8`.
  `otool -L` resolves the private dylib.
- **Provenance.** The newest `Core/` commits on `main` are `a85bd17a`
  (#429, upstream merge), `583ffd1d` (#419) and `10176a10` (#415). All three are
  ancestors of the built HEAD, and every object was compiled in this session.
  `nm -gU` finds #415's `SpriteNearbyPalettes` in the dylib. The idle baseline
  below reproduces F14.4's numbers exactly.
- ROM `Castlevania (1987) (Konami).nes` from the user's library, whole-file
  sha1 `7a20c44f…`, No-Intro sha1 `3DCB69A8C861C041AEB56C04E39ADF6D332EDA3A`.
  This is the dump F14.4 and the ADR-0229 log measured.

## Step 1: recording

The routes are new, in `scripts/stages/castlevania/` with a `stage-set.json`.
`test_library_job.py` passes with the new set (30/30):

- `mint-stage1.txt`: `120f -`, `20f T`, `180f -`. That reaches the
  player-controlled gate screen of stage 1. Minted with
  `headless_record Castlevania.nes 6 <mint>/mint input=mint-stage1.txt save-state=stage1.mss`
  (frame 361). The `.mss` is not versioned.
- **The library job does not mint at frame 361.** `scripts/record_library.sh`
  runs every mint for the batch's `<seconds>` (60 s by default), and
  `save-state=` is written at the run's frame target, so the job's
  `stage1-run.mss` is at frame 3 607, 54 s of idle later, with the timer at
  0255 instead of 0300 and a different screen showing. Run through the job on 2026-09-24, the
  set records the whole 3 576-frame route from there (3 586 retained frames,
  487 silhouettes, 90.6 % seen), but that is not the recording measured below.
  Ending a mint where its script ends is a tooling fix (#465), not a change to
  this set. Until #465 is fixed, mint this set by hand with the 6 s command
  above, saving to `<work>/stage1-run.mss` in a copy of the set, then record
  with `scripts/record_stages.sh <rom> <work> <out> 60`. The header of
  `mint-stage1.txt` says the same.
- `stage1-run.txt`: 12 repeats of a 298-frame block, `60f R` / `2f B` /
  `24f -` / `60f R` / `2f DB` / `24f D` / `60f R` / `10f RA` / `30f R` /
  `2f B` / `24f -`. That is walk, whip, walk, crouch-whip, walk, jump, whip.
  3 576 frames (59.5 s), so it fits the 60 s (3 606-frame) budget
  `scripts/record_library.sh` and `record_stages.sh` run every route for
  (`scripts/stages/README.md`: a route is <= 3 600 frames).
- **The 80 s measurement below used 16 repeats of the same block (4 768
  frames, 79.3 s).** Its first 3 576 frames are the versioned route byte for
  byte; the other 1 192 frames are 4 more copies of the block, which a 60 s
  batch run would cut. To reproduce the 80 s numbers exactly, write the block
  16 times into a scratch input script, e.g.
  `{ cat scripts/stages/castlevania/stage1-run.txt; head -n 44 scripts/stages/castlevania/stage1-run.txt; } > <run>/input.txt`
  (the 12 blocks plus 44 lines = 4 more blocks), and pass that as `input=`.

```sh
export MESEN_SHEET_GRID_DUMP=<run>/grid-dump.txt MESEN_OAM_STREAM_DUMP=<run>/oam-dump.txt \
       MESEN_POSE_TRACK_DUMP=<run>/pose-dump.txt
headless_record <run>/Castlevania.nes 80 <run>/rec bootstrap hdpack-off log \
  state=stage1.mss input=<run>/input.txt   # the 16-block script above
```

- Both passes: `result: ok`, frames 361 → 5 170, wall clock 23.8 s and 24.7 s.
  The two pack folders are identical file for file, `poses.json` included
  (sha256 `481fe77d…7208`).
- Route, checked with screenshots every 10 s: courtyard at 10–20 s, gate
  at 20 s, entrance hall from 30 s, the stairs section at 50 s. The life is lost
  between 50 s and 60 s (lives 03 → 02, timer back to 0297), and the run
  replays the hall until 80 s. Enemies seen: zombies, panthers, a bat. Also
  seen: wall candles, courtyard braziers, a money bag, the 100-point popup and
  the dagger subweapon frame in the HUD.
- **Retention cap.** The OAM stream holds **4 096 frames, each with repeat
  count 1**. The gameplay never repeats a frame, so it fills
  `kMaxSheetFrames` (4 096) at about 68 s. The last ~12 s reach `hires.txt`
  but not the sheet or pose streams. This is the designed cap, not a bug. An
  80 s gameplay run is past it, and the idle run is not (3 478 frames).

## Step 2: palette gap, gameplay against idle

The method is F14.4's, with its scratch scripts copied unchanged except for
the worktree path (neither script is versioned; the F14.4 log describes what
they count): `measure.py` for coverage and `f144_analyze.py` for the
fold/colourway split with `artist_chr_kit`'s own predicates. The idle column
is F14.4's 60 s power-on run, **re-recorded on this binary** (wall 17.2 s). It
reproduces F14.4 exactly.

| | Idle 60 s (F14.4) | Gameplay 80 s (this run) |
|---|---|---|
| `<tile>` keys / drawn keys | 3 209 / 628 | 3 033 / 452 |
| drawn shapes, all on a sheet | 532, 100 % | 342, 100 % |
| drawn keys on a sheet | 532 = **84.7 %** | 341 = **75.4 %** |
| missing drawn keys | 96 | **111** |
| pairwise: inert / fade / brighter / colourway | 29 / 0 / 6 / **61** | 13 / 2 / 5 / **91** |
| `compute_folds`: same group as the cell / own picture | 35 / 61 | 19 / **92** |
| sprite: fold / colourway | 10 / 58 | 11 / **88** |
| background: fold / colourway | 25 / 3 | 9 / 3 |
| shapes with a missing key | 53 | 53 |
| missing keys per shape (shapes) | 1: 36, 3: 16, 12: 1 | 1: 26, 2: 2, 3: 24, 9: 1 |
| drawn palettes per shape, max / sheet palettes per shape, max | 13 / 1 | 10 / **2** |
| (c) extra cells, unfolded / folded | 96 / 61 | 111 / 92 |
| sheets (json with a PNG), `sheets/` files, bytes | 85, 257, 2 355 708 | 115, 347, 3 474 243 |
| sheet keys absent from `hires.txt` | 0 | **7 keys (13 entries)**, bug B1 |

Reading it:

- **Gameplay widens the gap and makes it more of a colourway problem.** Nine
  more points of drawn keys have no cell (84.7 → 75.4 %). Colourways go from
  61 to 91, almost all sprites. The missing keys are Castlevania's alternate
  sprite palettes: `FF16250F` 41, `FF37110F` 30, `FF23340F` 19, `FF273707` 9.
  The cells carry `FF273707` or `FF23340F`, so the same enemy or effect shape
  seen in another sprite palette has no cell of its own.
- **The two recordings miss different keys.** 37 missing keys are common to
  both, 59 are idle-only and 74 gameplay-only. Each recording's gap is a
  sample of the game's palette use. Neither run is an upper bound.
- For ADR-0230's F14.9 slice (colourway cells plus folds on the sidecar):
  on this run it would add 92 colourway cells (folded) and fold 19 keys.
  This is more than the idle run's 61, and it is the same order as Zelda's
  folded 76.

## Step 3: artist kit

The generators are the same as `f1219-contra-kit-coldread-rerun-2026-09-24.md`,
plus `artist_map.py`, because this is a CHR RAM game (`docs/remastering-a-game.md` §3):

```sh
artist_kit.py      $PACK --out $KIT --verify
artist_bg_kit.py   $PACK --out $KIT --verify
artist_chr_kit.py  $PACK --rom Castlevania.nes --out $KIT --verify
artist_map.py --out $KIT --stage stage1 --dump grid-dump.txt --pack $PACK --scale 4 --verify
artist_kit_assemble.py $KIT --title "Castlevania, stage 1 (gameplay)"
```

- Result: 4 parts, 148 files, 14 312 cells, 180 surfaces left out.
  `mep_lint.py` on the recorded pack: 0 errors, 0 warnings.
- The sprites, background and CHR parts pass `--verify`: sprites and
  background 348 keys before and after, 0 lost, 0 added; CHR 3 033 → 3 033,
  rebuilt `hires.txt` identical.
- **The map part fails** (B2): `mep_build` refuses the panorama twin at
  6016×1016 against the 1504×200 grid its sidecar describes.
  - Every one of the 8 875 panorama cells is byte-identical to the pack's art.
  - With the twin cropped to its grid the same verify gives 0 errors, 0 lost
    and 10 keys added (each already in the manifest).
  - `--slice`, the artist's actual return path, is unaffected.

### Figures (`sheets/usr000`–`usr017`, 18 files, 100 figures)

The recorder kept 273 poses. The kit lays out 100 of them. It excludes 172
fusions (ADR-0177 two touching figures, ADR-0228 a figure plus a remainder
never seen alone) and 1 HUD element. It found 29 cycles and 22 sequences.
**No cycle carries a `driver`**, because the run holds Right most of the time
and there is no `stage1-probe.txt` yet (F9.23: only a probe attributes a
driver).

| Figure | Sheet | Run | Timing |
|---|---|---|---|
| **Simon, walk** | `usr002` (15 figures, 12 variants) | `cycle002`, loop of 4, plays columns 1 7 1 11 (column 1 twice) | hold 7/7/7/7 = **28-frame period**, ×30. Its two-phase subset `cycle004` ×10 |
| Simon, walk (other phase set) | `usr005` | `cycle012`, loop of 4, plays 1 2 1 3 | hold 7 each, ×4 |
| **Simon, whip** | `usr011` (wind-up, `pose030`), `usr013` (arm forward and whip out, `pose026`, `pose019`) | `seq001` 030→005→004→008, `seq003` 008→004→026→019 | wind-up 1–4 frames, strike 5–8, recovery 7 |
| Whip segments | `usr006`, `usr007`, `usr008`, `usr010` (1 figure each) | 2-phase "cycles" with hold 1 | on/off flicker, see below |
| Simon, crouch and crouch-whip | `usr015` (7 figures) | `seq005` 011→090→185→061 | hold 14/5/3/6 |
| Simon, death, jump, stand | `usr016` rest grid | none | 26 figures seen in 1 822 frames |
| Zombie | `usr001` (9 figures, 8 variants), `usr003` (12, 10 variants) | `cycle001` ×46, `cycle010` ×4 | hold 17/17 = 34-frame period |
| Zombie rising | `usr012` | `seq002` | |
| Panther, run | `usr004` (9, 6 variants), `usr009` (6, 4 variants) | `cycle011` ×4, `cycle025` ×2 | hold 7/7/7 = 21 frames |
| Panther, crouched on a ledge | `usr014` | `seq004` | first phase held 84 frames |
| Brazier flame | `usr000` | `cycle000` ×50 | hold 9/9 |
| Pickups and effects | `usr016`, `usr017` | none | money bag, hit sparks and candle flames, "100", a bat, a wall candle |

- **Variants are mostly bystanders.** Most `usr001`–`usr004` variant columns
  are the figure plus a wall candle (two small red flames) that the recorder
  saw attached to it (ADR-0179). There are also Simon + whip-hit spark
  variants.
- **Hold-1 two-phase "cycles" are sprite flicker, not animation.** 20 of the
  29 cycles (for example `cycle003`, `cycle005`–`009` and `cycle013`–`024`)
  alternate a pose with itself plus or minus a member on consecutive frames.
  This is Castlevania rotating OAM when too many sprites share a line. Only the
  whip-segment sheets end up laid out from them.
- Left out, with the reason written into ARTIST.md for each: 172 fusions
  (ADR-0177 / ADR-0228) and 1 HUD element. ARTIST.md's "Left out" list names
  every pose id.

### Scenery (`usr018`–`usr071` plus `scene/`, 107 files, 217 cells)

- 48 objects (`objNNN`) and 6 elements recovered from `adjacency.json`
  ("bgNNN + cells that always follow it", up to 20 cells, e.g. the hall's
  column capitals and curtain folds), plus 53 whole-screen captures in
  `scene/`.
- 7 groups were dropped for having no ink. 7 `obj` groups (`obj025`–`028`,
  `obj034`, `obj040`, `obj048`) are left out because each is one phase of a
  14-cell, 7-phase element that is emitted whole.

### Stage maps (`map/`, 3 panoramas)

- Three horizontal regions: 1504×200 (the hall and stairs), 768×200 (the
  courtyard up to the gate) and 568×200 (the hall again after the respawn),
  at 1×.
- 5 HUD rows are excluded at the top (#221's case). There are 0 disagreeing
  positions.
- They cover 95 of the pack's 3 033 keys (3.1 %).

### Pattern pages (`chr/`, 20 pages)

- 13 banks: the 2 real CHR RAM banks (512 tiles) and 11 synthetic PRG-scan
  pages copied through.
- The real banks: **302 recorded (59 %), 176 filled from the ROM, 34
  unrecoverable (red) → 93 % complete**. The idle kit is not in this log.
- Rank-0 pages:
  - `Chr_0` (background bank): 178 to paint, 4 folded, 74 fill, 0 empty.
  - `Chr_15` (sprite bank): 110 to paint, 10 folded, 102 fill, 34 empty.
- 40 cells are violet (folded, do not paint). 148 fill cells wear a palette
  that was guessed. 58 fills have an ambiguous PRG source.
- `--fill-rules none` emitted 0 rules. Folding is brightness-only, and 0
  candidates were refused.

### Contact sheet

The contact sheet is not versioned. It is in the scratchpad:
`…/scratchpad/castlevania-deep/contact-kit.png` (2400×2515). It shows every
figure view, every `usr*` sheet, the 20 CHR pages, the 3 panoramas and the 53
scenes. `contact-figures.png` has the figure views only.

## Step 4: painted round trip

The recipe is ARTIST.md's "When you are done" block, in #435 order. Copy
`auto/` to `painted/`, drop in the kit's sheets, CHR pages and scenes, build,
import every `figures/usr*-figure.png`, then build twice more. There are two
arms, each from a clean copy.

- **Control**: nothing painted.
- **Painted**: one cell of `figures/usr002-figure.png` (Simon's walk,
  `pose004`, the cell at 1× (10, 13)). Every opaque pixel in that 8×8 rect
  (848 px at 4×) set to `#FF00FF`, not the `#FF00FD` guide sentinel. The cell
  is the torso tile `7FFEFC7C…` / `FF273707`. Its run-time key is the
  unflipped `FE7F3F3E2D43433FA74F1F3F3E7C7D3F` / `FF273707` (ADR-0178).

| | Control | Painted |
|---|---|---|
| build 1 (before import) | 0 errors; 3 033 → 341 carried + **7 new** (B1) | same |
| import | 0 painted | 1 cell written into `usr005.png` ("no rule moves") |
| build 2 / build 3 | 0 errors / 0 errors, byte-identical | 0 errors / 0 errors, byte-identical |
| `hires.txt` vs control | — | **byte-identical** |
| key set | 348 | 348 (0 only here, 0 only there) |
| rules whose crop holds `#FF00FF` | 0 | 3 (`[spr001_n6]`, `[spr008_n4]`, bare), 1 key |

**In game.**

- Each arm's `textures/*` was installed as the loose pack
  `<run>/mesen-home/HdPacks/Castlevania/`. The run replayed `stage1-run.txt`
  from `stage1.mss` with `capture screenshot mep-off`.
- Every run logs `loaded loose NES HD pack from HdPacks/Castlevania/hires.txt
  (782 tiles, scale 4)`.
- At **frame 2766** (40 s) the painted screenshot differs from the control on
  **848 px**. **672 of them are exactly `#FF00FF`**, all in the 32×32 box
  (512, 740)–(543, 771), which is Simon's torso as he walks through the hall.
- At frames 1564 and 5170 both arms show 0 magenta. Simon is in a phase that
  does not use that tile.
- The control and painted captures are identical outside that box.

**ADR-0183 §4.** The unpainted rebuild keeps the key set: 348 before, 348 after
in the kit's verify and in the control arm. Measured against the recording
instead, the rebuild carries 341 of its keys and adds 7 the recording never
drew. That is B1, not the kit.

**#447 (known, not fixed).** The rebuilt layer draws the sheets'
nearest-neighbour crops over `auto/`'s xBRZ pages for the same key. So even
the unpainted control does not render like the recording. Not measured here.

**Whole-figure paint.** The Contra re-run painted a whole figure. The same
thing on `usr002-figure.png` (70 960 px) fails:

- The import writes 74 cells.
- Build 2 exits 1 with `error: sheets/sprites.png: painted tile
  00000000000000000000000000000000/FF23340F lost to sheets/usr017.png … (#253)`.

This is B3.

## Bugs found (drafts, not filed)

### B1 (P2): recorder sheet cells name sprite keys the frame never drew, after a mid-frame PPU change at a screen cut

**Observed.**

- `scripts/sheet_keys_audit.py` (#183's own repro) reports **13 leftover
  sprite tile entries of 505** (7 distinct keys) on the 80 s gameplay pack.
  Examples: `0000000000FF00FF0000000000FFFFFF` / `FF273707` on `sprites.json`
  cell 96 and `spr041`; `1F23C3C7DBF3C1011F3FFF7F7933C101` / `FF23340F` on
  `spr036`.
- `hires.txt` holds each of those shapes only under another palette.
- `mep_build` then emits all 7 as keys the recording never drew (`7 new
  key(s) the sheets brought`). Painting such a cell does nothing in game.
- The idle run has 0 leftovers.
- Every gameplay spike has them, whether it starts from the state or from
  power-on: 20 s → 13, 50 s → 12, 86 s from power-on → 42.

**Where.**

- Every leftover comes from **one retained OAM frame**, index 297 of the
  stream (emulated frame 658). That is the cut from the intro gate screen to
  stage 1: screenshots at frames 657 and 659 show the gate, and 661 is black.
- That frame holds 23 entries, where its neighbours hold 46 and 42. Every
  entry is a single 8×8 half, as if `LargeSprites` were off.

**Likely cause.**

- `HdBuilderPpu::CaptureOam` reads `_control.LargeSprites`,
  `SpritePatternAddr` and palette RAM at frame end (`OnBeforeSendFrame`).
- The game rewrote PPUCTRL and the palettes for the transition after the
  frame's sprites were drawn.
- #183's per-pixel sprite-enable gate admits the frame, because sprites did
  draw earlier in it.

**Expected.** A sheet cell only for a `(tile, palette)` the frame drew, i.e.
the OAM decode and palette of the frame as rendered.

**Repro.**

```sh
scripts/stages/castlevania/mint-stage1.txt → stage1.mss
headless_record … 20 … bootstrap hdpack-off state=stage1.mss input=stage1-run.txt
scripts/sheet_keys_audit.py <pack>/Castlevania/auto
```

### B2 (P2): `artist_map.py --verify` always fails since the ADR-0220 context band

**Observed.**

- On any panorama, `--verify` reports `mep_build.py build exit 2 (baseline 0)`
  with `error: pano-stage1-000.png: 6016x1016 is not an integer multiple of
  the 1504x200 sheet pano-stage1-000.json describes`.
- Its key check then reads a failed build as "7 lost, 2692 added".
  `artist_kit_assemble.py` puts "map FAILED" in ARTIST.md.

**Cause.**

- `ora_writer.write_surface` grows the panorama PNG and its `.orig.png` twin
  by the context band below the grid (ADR-0220 §3). Here that is 254 rows at
  1× for a 200-row grid.
- `verify()` upscales that grown twin into the throwaway pack, and
  `mep_build._sheet_scale` sizes the sheet from the cells.

**Spike.** The same `verify()`, with the twin cropped to the lowest cell,
gives 0 errors, 0 lost, 10 added and 8 875/8 875 cells byte-identical.

**Scope and why it went unseen.**

- `--slice`, the artist's return path, reads the grown twin consistently and
  works (2 400 cells, "copy the three into a pack").
- `test_artist_map.py` has no `verify` case, so the suite stays green
  (19/19).

### B3 (P2): `mep_figure.py import` sends a figure's paint into its fully transparent member tiles

**Observed.**

- Painting every opaque pixel of Castlevania's `figures/usr002-figure.png`
  (Simon's walk) and importing after a build writes 74 cells.
- Among them is the blank tile `00000000000000000000000000000000` /
  `FF23340F`, a member of 4 poses (`pose124`, `pose236`, `pose062`,
  `pose246`). It is written into `sprites.png` cell 0 and `usr017`.
- The next `mep_build` fails with #253's guard: `painted tile
  0000…/FF23340F lost to sheets/usr017.png`.

**Cause.**

- At pixel precision (ADR-0225) each blank member's 8×8 rect overlaps 2–4 of
  Simon's body tiles: 960 opaque px inside the first one.
- The import slices every member rect independently, so the neighbours' ink
  becomes paint on the blank key. It is a different picture per figure, hence
  the conflict.

**Expected.** A member whose recorded tile is all colour 0 is not painted
from pixels that belong to another member. Even if the build accepted it, a
painted blank sprite tile would draw opaque art where the NES draws nothing.

**Workaround.** Painting a single cell works, as step 4 shows.

## Caveats

- **One route, one life lost.** The recording covers the courtyard and the
  first hall and stairs, not the whole stage or its boss. The kit reflects
  that, and so does the 3.1 % panorama coverage.
- **Retention.** 713 of the 4 809 recorded frames are past `kMaxSheetFrames`,
  so they feed `hires.txt` but not the sheets or poses. A 68 s gameplay run
  would stay within the cap.
- **No probe.** The cycle timings are from a stage run with no `driver`
  attribution.
- The round trip proves one cell in one frame. The 176 non-magenta changed
  pixels in the proof box were not analysed.
- The ADR-0230 figures are the F14.4 method on a second recording. No option
  was prototyped here.
