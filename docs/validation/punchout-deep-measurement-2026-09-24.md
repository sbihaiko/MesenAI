# Punch-Out!! deep measurement (2026-09-24)

> Re-measured on `main` `ceab80a9` (2026-09-25): see [deep-remeasure-ceab80a9-2026-09-25.md](deep-remeasure-ceab80a9-2026-09-25.md).

This puts Mike Tyson's Punch-Out!! through the same deep measurements already
run on Contra, Castlevania and Zelda:

1. a reproducible route and two recordings;
2. the F14.4-style palette-gap measurement;
3. the artist kit;
4. a painted round trip.

It is the first CHR ROM game with bank switching (MMC2, mapper 9) to be measured
this way.

- **Baseline:** `main` at `46b9136a5a6e5b47f6144d19414b56c814b91a83`
  (merge of #445). F14.9 (ADR-0230) was not on `main` and is not measured.
- **Binaries:** built in the worktree with `make capture-tool`.
  - `MesenCore.dylib` sha256 `048baa12…c895913`.
  - `headless_record` sha256 `723af926…655e6d`. `otool -L` resolves to the
    worktree dylib, and `nm` finds `SpriteNearbyPalettes`, which is how we know
    the binary includes #415, the newest `Core/NES/HdPacks` commit.
- **ROM:** "Mike Tyson's Punch-Out!! (1987) (Nintendo).nes" from the local
  library.
  - Whole-file SHA1 `0F0C2C2E294FF8CE255A618964728C935FD8963B`.
  - No-Intro SHA1 `B6F6BD9D78CDD264A117D4B647AEE3309993E9A9`.
  - 128 KiB PRG and 128 KiB CHR ROM, so hires.txt uses `<ver>109` index keys
    (ADR-0172).

The working files are in an unversioned scratch directory and are cited below
by relative name. Nothing from that directory is committed.

## Results at a glance

| Step | Result |
|---|---|
| 1. Route | `scripts/stages/punchout/{mint-fight1,fight1}.txt` plus `stage-set.json`. From the minted state, the recording runs 70 s (4208 frames). Two passes produced a byte-identical `hires.txt` (sha256 `215a3351…7830e6`) and an identical `auto/` tree. |
| 2. Palette gap | 1655 drawn keys (516 sprite, 1139 BG) over 1009 drawn shapes. 60.6 % of drawn keys are on a sheet and 30.3 % are on an organised sheet. The 652 missing keys fold 315 and stay their own colourway 337. |
| 3. Kit | Every `--verify` passes, with 0 keys lost and 0 invented. There are 13 figure sheets holding all 34 poses and 6 cycles, 66 pattern pages covering 100 % of the CHR, and 3 scene screens. **Glass Joe comes out whole. Little Mac does not become a figure, because he is drawn as BG.** Two poses are torn (bug B). |
| 4. Round trip | Magenta reaches the game at the painted spot: 1024 px, the full 32x32 cell. That only happens with the backdrop spike for bug A applied. Without it, the rebuilt layer hides Glass Joe, and only a 32x4 px sliver (128 px) of the paint shows. An unpainted rebuild keeps the key set: 1010 = 1010 = 1010. |
| Bugs | A (P1): BG crops bake the backdrop opaque, so a rebuilt layer hides behind-BG sprites. B (P1): the flip is not un-baked on index-keyed packs. C (P2): the registry names a different MMC2 bank than the one the PPU drew. #447 is noted only. |

## 1. Route and recording

`mint-fight1.txt` (1990 f) does the following:

- waits out the boot;
- presses Start ten times, 120 f apart, which takes it past the title and the
  password/new-game screen to the Minor Circuit card;
- idles through the ring introduction.

It was run as:

```sh
scripts/headless_record <work>/PunchOut.nes 34 <work>/mint/mint screenshot \
  input=scripts/stages/punchout/mint-fight1.txt save-state=<work>/fight1.mss
```

That stops at frame 2044, at the bell, in 4.0 s of wall time.

The duration `34` matters. Before #465 `scripts/record_library.sh` did not run this
command: it ran every mint for the batch's `<seconds>` (60 s by default), and
`save-state=` is written at the run's frame target. Run that way (checked with
the job itself on 2026-09-24), `fight1.mss` is saved at frame 3 607, about 26 s
into the round, and the job's 60 s `fight1` recording reaches the count-out and
keeps only 355 frames. Padding the mint so the bell lands on frame 3 607 does
not fix it: the RNG state differs (35 RAM bytes at the bell), and a 70 s
recording from the padded state misses 255 of the 1 655 drawn keys measured
here. The job needs to end a mint where its script ends; that is a tooling
issue (#465), not a property of this set. **Fixed 2026-09-25:** the job now
runs this mint for 34 s, stops at frame 2 044 with the same RAM as the command
above run from the repository root, and its 60 s `fight1` run keeps 633 frames
(`docs/validation/issue-465-mint-at-script-end-2026-09-25.md`).

`fight1.txt` is a blind combination loop: jabs and body blows to both sides,
dodges and a duck, as an 18-line, 310-frame block. The measurement below ran
18 blocks (5580 f). The versioned file keeps only their first 3582 frames (208
lines, byte for byte), so it fits the 3600-frame budget the library job runs a
route for (`scripts/stages/README.md`). That trimmed route stops before the
count-out; a 60 s recording from it was not measured, so the numbers below are
the 70 s script's. To rebuild that script, repeat the block 18 times:
`for i in $(seq 18); do grep -v '^#' scripts/stages/punchout/fight1.txt | head -n 18; done > <run>/input.txt`,
and pass it as `input=` in the command below. Because the route never reacts
to Glass Joe's tells, it loses:

- Little Mac goes down around 60 s after the state;
- he is counted out around 70 s.

A recording of 70 s is the useful maximum; after that it records the "you lost"
screen. Both passes used this command:

```sh
MESEN_SHEET_GRID_DUMP=… MESEN_OAM_STREAM_DUMP=… MESEN_POSE_TRACK_DUMP=… \
scripts/headless_record runs/<n>/PunchOut.nes 70 runs/<n>/rec bootstrap hdpack-off log \
  input=<run>/input.txt state=<work>/fight1.mss
```

Each pass covered 4208 frames (2044–6251), taking 17.3 s of capture and 19.7 s
of wall time. The two passes matched on the `hires.txt` sha256
(`215a335144b44b36dea06712fe1b7705d971b297ca83bd09befed4de0c7830e6`), on
`diff -rq` of `auto/`, and on the OAM and pose-track dumps.

The bootstrap exported all 8192 ROM tiles. Three static screens were captured,
all of them the knockdown count with the referee.

**Who is drawn how.** This is the opposite of what was expected before
measuring:

- **Glass Joe** and his gloves are sprites, drawn *behind* the background.
- **Little Mac and the referee** are BG tiles. Little Mac changes colour when he
  is tired, using three BG palettes: `112A0F36`, `112A1436` and `112A2536`.

## 2. Palette gap

These numbers are for pass A. We used the F14.4 script, adapted for CHR ROM:
it credits a key by `_index_token(index)` and reads each bitmap from the ROM at
`index * 16`.

| Measure | Value |
|---|---|
| Keys in `hires.txt` | 9847. Of these, 8192 are `defaultTile=Y` bootstrap keys, one per ROM tile. |
| Drawn keys | 1655 (516 sprite, 1139 BG) |
| Shapes (all / drawn) | 8192 / 1009 |
| Sheet entries / distinct sheet keys | 1448 / 1010. 7 of the sheet keys are not in the recording (bug C). |
| Drawn keys on any sheet | 1003 = **60.6 %** (sprite 411/516, BG 592/1139) |
| Drawn keys on an organised sheet | 501 = **30.3 %** |
| Drawn keys missing from the sheets | 652, on 402 shapes. The count of missing keys per shape is 1:179, 2:205, 3:13, 4:3, 5:1, 7:1. |
| Missing keys with no cell for their shape | 9 keys, on 6 shapes |
| Maximum palettes per shape | drawn 8, on sheet 1 |

Each missing key was classified by comparing it with its shape's cell:

| Class | Keys |
|---|---|
| inert | 83 |
| fade | 411 |
| brighter | 5 |
| fade, refused | 47 |
| brighter, refused | 5 |
| colourway | 92 |
| no cell | 9 |

Run through `compute_folds`, 315 of the 652 missing keys fold and 337 stay
their own colourway.

The split by side, first as fold *candidates* (the pairwise class above:
inert, fade or brighter) and then as the folds `compute_folds` actually
accepts. The accepted columns add up to the headline 315 / 337; the 9 no-cell
keys count as their own colourway there.

| Side | missing keys | fold candidates | colourway (incl. refused) | no cell | **accepted folds** | **own colourway** (incl. no cell) |
|---|---|---|---|---|---|---|
| BG | 547 | 475 | 69 | 3 | **304** | **243** |
| Sprite | 105 | 24 | 75 | 6 | **11** | **94** |
| Total | 652 | 499 | 144 | 9 | **315** | **337** |

Of the 499 candidates, `compute_folds` accepts 315 and keeps 184 as their own
colourway (171 BG, 13 sprite). No key outside the candidates folds.

- **On the BG side the gap is mostly fade.** 475 of 547 missing keys are fade
  candidates and 304 of them fold. That fits the fight's palette fades and
  Little Mac's tired recolour of the same tiles.
- **On the sprite side it is mostly true colourways.** Only 11 of 105 missing
  keys fold. That fits Glass Joe's palette changes, for example when he is
  hit. The keys were not attributed to individual events one by one.

The fold statistics were inert 54, fade 279, kept 1262 and refused 52.

On the measure scale:

- the grid dump is 58.3 MB (4 046 508 lines; corrected 2026-09-25, was "592 K lines");
- the registry lower bound is 1010;
- `sheets/` holds 140 files, 2 283 050 B in all, and 46 sheets have a PNG;
- `hires.txt` is 364 212 B;
- the unsorted sheet has 516 cells.

## 3. Artist kit

Commands, run from the pass-A pack: `artist_kit.py --scratch --verify`,
`artist_bg_kit.py --verify`, `artist_chr_kit.py --rom … --verify`,
`artist_map.py`, `artist_kit_assemble.py --title "Punch-Out!!, Glass Joe fight 1"`
and `mep_lint.py`.

- **Sprite kit.** It carries 9847 recording keys and 1010 keys after the control
  rebuild, and the kit build also has 1010. None were lost and none invented,
  and the kit added 39 files. The build reports "7 new key(s)", which are the
  bug-C keys.
- **BG kit.** It found 0 objects, 2 elements and 3 scenes. One object was
  dropped (obj000: 2 cells of uniform `#155FD9`, seen 5 times). Keys went from
  1010 to 1010.
- **CHR kit.** It has 66 pages covering 35 banks. The 32 real banks give
  32 × 256 = 8192 tiles:
  - 7562 recorded (92 %);
  - 630 filled from the ROM;
  - 0 unrecoverable, so coverage is 100 %.

  The pages also show 316 violet (folded) cells, 25 folds refused on hue, and 3
  `FFFFFFFF` blank-bucket pages.
- **`artist_map.py` refused** (exit 1). This is expected for a CHR-index pack:
  the grid dump does not carry the tile index.
- **Assembled kit.** It has 3 parts, 84 files and 16 940 cells. `mep_lint`
  reports 0 errors and 0 warnings.

**Poses (ADR-0170).** There are 34 poses, 0 of them fused, forming 6 cycles
and 15 sequences, plus 4 input attributions.

| Cycle | Period | Repeats | Driver |
|---|---|---|---|
| 1 | 6 | 15 | |
| 2 | 4 | 8 | port 1 |
| 3 | 2 | 6 | |
| 4 | 3 | 4 | |
| 5 | 2 | 3 | |
| 6 | 4 | 2 | |

**Figures.** There are 13 sheets:

- `usr000`–`usr005` are the six cycles;
- `usr006`–`usr011` are sequences seq000, seq001, seq002, seq005, seq012 and
  seq014;
- `usr012` holds the 8 unordered figures.

All 34 poses are placed and no sprite is dropped. **The opponent comes out
whole:** guard, jab, the hit reactions and the dazed stagger are all there
as complete figures.

**Little Mac is not a figure.** Figures are built only from sprites, and Mac is
background. He appears only on the pattern pages and on the unsorted/metatile
BG sheets.

**Scenery.**

- `usr013`: the crowd band, bg076 plus 1 follower.
- `usr014`: bg082 plus 4.
- `scene/screen001–003`: the three knockdown-count screens, with the referee and
  no Glass Joe.

**Two figures are torn:** pose005 on `usr009` and pose023 on `usr011`.

- Every tile in them carries `mirror: H` with a `source`, and all of them
  resolve into bank `1E`.
- In game the same pose is coherent.
- Mirroring each tile back in place makes the figure coherent.

The kit therefore stores the H cells already mirrored, which is the same defect
as bug B.

**Contact sheet** (scratch): `kit-contact-sheet.png`. It shows all figure
sheets, usr013 and usr014, the three scene screens, and pattern pages Chr_00_0,
Chr_00_1, Chr_05_0 and Chr_1E_0.

## 4. Painted round trip

We followed the #435 recipe order:

1. copy the kit;
2. build;
3. `mep_figure import` of every figure;
4. build again;
5. lint.

Every step exited 0.

**The painted cell.** It is `usr000-figure` pose011, node 11, at figure (16,8)
at 1x. That tile is shared by all 6 poses of the cycle and has 64 opaque px. We
painted it magenta, 1024 px at 4x. The import wrote that one cell into the
figure's sheet ("no rule moves").

**What changes after the build.**

- `hires.txt` is byte-identical to the control build.
- Exactly 2 rules, both for key `1DE0` / `FF352708`, now draw magenta.
- That key is drawn in 2004 of the 4208 recorded frames, at about (135,95).

**Key sets (ADR-0183 §4).**

- The control build, the painted build and the unpainted plain rebuild each
  have 1010 keys.
- 1003 of them are drawn keys carried over from the recording. The other 7 are
  the bug-C keys.

**In game.** Each arm was a loose install as `HdPacks/PunchOut` with `mep-off`,
screenshotted 4.6 s after the state:

| Arm | Magenta px | Notes |
|---|---|---|
| `auto` (the recording) | 0 | Glass Joe drawn normally |
| painted | 128, bbox (540,380)–(571,383) | Glass Joe **hidden** except his hair, so only a 32x4 sliver of the paint shows (bug A) |
| painted + backdrop spike | **1024**, bbox (540,380)–(571,411) | the full cell, on Glass Joe's face |
| layered: sibling `auto` + `mep/textures` painted + spike (ADR-0147) | **1024** | same spot |

The layered install without the spike loaded 10 067 tiles with 98 % BG match
and still drew 0 magenta at 1.2 s, because the mep layer's floor tiles cover
Glass Joe. Evidence: `ingame/roundtrip-4.6.png` (auto | painted |
painted + spike), `ingame/cmp-1.2.png`, `ingame/spike-backdrop.png` and
`ingame/zoom-joe.png`.

The paint therefore round-trips to the right key and the right screen spot. A
rebuilt textures layer for this game, painted or not, can only be *seen* once
bug A is fixed.

## Bugs found (drafts, not filed)

**A. A rebuilt BG layer bakes the backdrop colour opaque and hides behind-BG
sprites (Punch-Out!!: Glass Joe disappears)**

The sheet BG crops store colour 0 (the backdrop, RGB 21,95,217) as opaque.
`mep_build` then emits rules for blank floor tiles with fully opaque blue crops,
from `metatiles`, `obj000` and `unsorted.png`:

- 13 rules in all;
- tiles `02FE`, `02FF`, `FE`, `FF`, `01FE`, `01FF`, `03FE`, `03FF`, `01FD`.

The recording's own `auto/` crops for the same tiles are transparent. Glass Joe
is drawn behind the background, so every rebuilt `textures/` layer (control,
painted, or a plain rebuild) paints the floor over him. In the loose install
only his hair shows. Stacked over `auto` in the ADR-0147 layered install, the
mep layer still wins.

- **Spike:** making the backdrop RGB transparent in the rebuilt PNGs brings
  Glass Joe back, and the painted cell then shows in full.
- **Why ADR-0224 does not help:** its `<bgPreservesBehindBgSprites>` covers only
  `<background>` captures, not `<tile>` rules built from sheets.
- **Repro:** record `fight1.txt` from `fight1.mss`, run the kit and build the
  sheets, install the built `textures/` loose, and screenshot at 4.6 s.
- **Expected:** Glass Joe drawn. **Observed:** only his hair.
- Suggested priority: P1.

**B. The flip baked into sprite crops is not un-baked on index-keyed (CHR ROM)
packs**

In `mep_build`, the `index_keyed` branch (`data = _index_token(index)`) skips
the `elif unflipped is not None` branch. That branch is where the ADR-0178
un-bake (`pending_unflips`) happens. As a result:

- The rebuilt pack has **70 sprite rules whose crop is the mirror** of the ROM
  tile, for example `1E2B` from `spr012.png`.
- `auto/` has no mirrored crops.
- The renderer then applies the OAM flip a second time, so Glass Joe's
  H-flipped poses draw torn in game (`ingame/zoom-joe.png`: game | auto |
  rebuilt).
- The kit's figure view shows the same tear for pose005 and pose023, whose 57 H
  cells are stored mirrored in `sprites.json`. That view may have a second cause
  in the recorder's H flag or the pose offsets; this was not isolated.

Suggested priority: P1. It affects every CHR ROM game whose sprites use
H-flip.

**C. Under MMC2 the sheet registry names the tile from a different CHR bank
than the one the PPU drew**

- The sheet keys `1E80`, `1E81` and `1E82` (`FF163008`, sprite cells 331, 379
  and 394) hold the ROM art of bank `1E`. The renderer drew `0580`, `0581` and
  `0582`, which are bank `05` tiles with different bitmaps; the recording's
  `hires.txt` crop matches ROM `0x580`.
- Likewise, blank duplicates `1CFD`, `1DFD`, `1FFD` and `1FFF` stand in for the
  drawn `05FD` and `05FE`.
- `FD` in 3 BG palettes has no cell at all.

The effect is 7 keys that the rebuild emits but the recording never drew (the
build's "7 new key(s) the sheets brought") and 9 drawn keys with no cell. This
is probably the MMC2 latch switching banks mid-frame, between the moment the
recorder resolves the index and the fetch. Suggested priority: P2.

**#447** (nearest vs xBRZ scaling) is known and was only noted here. It does
not affect the magenta pixel count, because the paint is a flat colour.

No duplicates of A, B or C were found among the open issues.

## Limits

- The route is blind and loses at about 70 s. It covers one opponent, round 1,
  and no Star Punch. Later fighters are out of scope.
- Little Mac and the referee are BG, so no kit figure covers them. A painter
  edits them on the pattern pages or the BG sheets.
- `artist_map.py` does not support CHR-index packs.
- The round trip was proved on a sprite cell only, and it can only be seen with
  the bug-A spike.
- F14.9 (ADR-0230), which was being implemented in parallel, is not included.
  The palette-gap numbers are the baseline it should reduce, mainly the 411
  fade keys and the 92 colourway keys.
