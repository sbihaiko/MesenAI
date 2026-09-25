# Excitebike deep measurement (2026-09-24)

This log adds Excitebike (1984) (Nintendo) to the per-game deep measurements
that Contra, Castlevania and Zelda already have. It uses the same protocol:
a recording of real play with a determinism check, the F14.4 palette-gap
split, the artist kit, and the painted round trip into the running game.
Excitebike is the first **CHR ROM** game in the set (NROM, 16 KB PRG, 8 KB
CHR). The other three are CHR RAM. Its layout is a horizontally scrolling
track under a raster-split HUD.

It is a baseline on `main` `46b9136a`. F14.9 (ADR-0230's colourway/fold
change) is in flight and will be re-measured against this log. Nothing
under `Core/`, `scripts/` or the PRD changed. The raw material (state,
recordings, kit, round-trip packs, screenshots, scratch scripts) lived in a
session scratchpad and is not versioned. This log keeps the numbers.

## Headline

1. **Deterministic.** Two 90 s passes wrote byte-identical `hires.txt`,
   grid dumps and OAM dumps, and the whole `auto/` tree matched too.
2. **The palette gap is small, and most of it is background.** 356 keys
   were drawn. 337 are on a sheet cell (**94.7 %**), and every drawn shape is
   on one (100 %). Of the 19 missing keys, 16 are background and 3 are
   sprite. Resolving each index against the ROM's CHR bytes gives 10 folds
   (4 inert, 2 fade, 4 brighter) and 9 colourways (8, plus 1 brighter step
   over the 25° gate). The CHR kit's own criterion, which assumes all four
   palette entries on a CHR ROM row, calls all 19 colourways.
3. **The kit is built and verified**, 3 parts, 25 files, 2 144 cells, and every
   `--verify` passes. The figures are the rider and bike: a period-2 wheel
   cycle seen **772** times, 8 more cycles, 16 sequences and an 18-figure
   rest grid. **The ramps have no scenery surface.** Their tiles sit only on
   the recorder's `unsorted` sheet and the pattern pages. `artist_bg_kit`
   recovers 0 elements, and `artist_map` refuses a CHR ROM pack as
   documented.
4. **The round trip holds.** One painted figure cell reached the game: 144
   magenta pixels on the rider at frame 1323, and the paint is the only
   difference from the unpainted control. The painted and unpainted
   rebuilds have byte-identical `hires.txt` and the same 337 keys
   (ADR-0183 §4), and a third build is byte-identical to the second.
5. **One real defect (draft below, not filed).** On a CHR ROM recording,
   `artist_chr_kit.py` counts the bootstrap's own ROM export
   (`defaultTile=Y`, never drawn) as recorded evidence. It marks 175 cells
   `seen: true` that belong to indices the run never drew, and reports
   "recorded 498 (97%)" where the run drew 337 indices.

## Summary by step

| Step | Result |
|---|---|
| 1. Record 90 s, two passes | 5 410 emulated frames after the minted state, `result: ok` both passes. `hires.txt` sha256 `3bc3c068…`, identical both passes, as are the grid dump, the OAM dump and the full `auto/` tree |
| 2. Palette gap (F14.4 method) | 868 keys / 356 drawn; 512 shapes / 337 drawn; drawn-shape coverage 100 %, drawn-key coverage **94.7 %** (19 missing). Missing keys: background 16 / sprite 3. With patterns resolved: fold 10 / colourway 9. Kit criterion: 0 / 19 |
| 3. Artist kit | sprites 337 → 337, background 337 → 337, CHR 868 → 868, all PASS. 10 figure sheets (53 figures), 6 scenery objects plus 1 screen, 8 pattern pages. Rank-0 pages complete: 498 cells from the pack plus 14 ROM fills, 0 unrecoverable. Contact sheet rendered (scratch only) |
| 4. Round trip (#435 recipe) | Build, then import, then build: 0 errors. 1 cell written into `usr009.png`, "no rule moves". 1 rule / 1 key draws magenta. Painted `hires.txt` byte-identical to the unpainted control; 337 keys both. In game: 144 magenta px at frame 1323, and 144 px differ from the control |
| 5. This log | `make doc-checks` passes |

## Binary

- Fresh worktree, fast-forwarded to `origin/main` `46b9136a` (#445).
  `make capture-tool` was built from a clean object tree with the
  CommandLineTools toolchain (`SDKROOT=…/CommandLineTools/SDKs/MacOSX.sdk`).
- `InteropDLL/obj.osx-arm64/MesenCore.dylib` sha256
  `9ce7c626537a8c15b8543dee3db97eee4133e45111f03668f1baaff495795183`.
  `scripts/headless_record` sha256
  `e03c63cb8fcfe550e4f6568b8a004df30f02b5cafcb2ddd9f2ec490db4757c65`, and
  `otool -L` resolves it to this worktree's dylib.
- **Provenance.** The newest `Core/NES/HdPacks/` commit on `main` is
  `10176a10` (#415). `nm -gU` finds its `SpriteNearbyPalettes` symbol in
  the dylib, so the binary carries the current recorder.

## Recording

- ROM: `Excitebike (1984) (Nintendo).nes` from the user's library. Whole-file
  sha1 `2e9897846e54a4a9865e87de7517c6710bdec255`, No-Intro
  `BA8D9227…`. This is the dump `scripts/stages/excitebike/stage-set.json`
  pins. iNES header: 1 × 16 KB PRG, 1 × 8 KB CHR, mapper 0.
- The existing stage dir was reused unchanged. `mint-stage1.txt` presses
  Start twice: Selection A, then track 1. `stage1-probe.txt` holds A with
  short A+B, A+Up and A+Down bursts. `stage1-run.txt` holds A in pulses.
  The recording's input is probe followed by run (7 080 frames), and
  90 s uses 5 410 of them. Per `poses.json`: A held for 4 174 frames, B for
  60, Up for 121, Down for 60. Select, Start, Left and Right were never
  pressed.
- Minted fresh, because `.mss` is never versioned:

```sh
headless_record <mint>/Excitebike.nes 10 <mint>/out \
  input=scripts/stages/excitebike/mint-stage1.txt save-state=<mint>/stage1.mss
#   -> 602 frames, result: ok; a 1 s screenshot from it shows the track-1 start grid
cat scripts/stages/excitebike/stage1-probe.txt scripts/stages/excitebike/stage1-run.txt > <run>/input.txt
MESEN_SHEET_GRID_DUMP=<run>/grid-dump.txt MESEN_OAM_STREAM_DUMP=<run>/oam-dump.txt \
MESEN_POSE_TRACK_DUMP=<run>/pose-dump.txt \
headless_record <run>/Excitebike.nes 90 <run>/rec bootstrap hdpack-off log \
  input=<run>/input.txt state=<mint>/stage1.mss
#   -> 6012 frames (602 pre-loaded + 5410 emulated), result: ok
```

- It is racing throughout. A plain screenshot at the same 90 s shows the
  rider still on track 1 with the race clock at 1:24:41. The race had not
  finished, and no results screen is in the recording.
- Wall clock 23.6 s and 25.2 s. `[HDPack] ROM tile export: 512 new
  defaultTile entries from 512 CHR ROM tiles` (the CHR ROM path,
  `AddRomTiles`, where a CHR RAM game gets the PRG scan).
- `<ver>109`, scale 4. Tiles are keyed by CHR index (ADR-0043). The sidecar
  `index` field equals `int(<hires field>, 16)`, and the tile data it
  carries matches the ROM's CHR bytes for that index on all 855 sidecar
  entries.

| Metric | Value |
|---|---|
| `<tile>` lines / conditions | 1 204 / 235 |
| distinct keys `(index, palette)` / drawn (`defaultTile=N`) | 868 / 356 |
| distinct indices / drawn | 512 / 337 |
| distinct CHR bitmaps / drawn | 457 / 310 |
| drawn palettes | `FF20160F` 191, `29271836` 87, `29220F20` 37, `29272230` 28, `29220F16` 10, `FF311C0F` 2, `FF20190F` 1 |
| `sheets/`: files / bytes | 98 / 1 103 721 |
| sheet cells | sprites 174, sprite (22 sheets) 120, unsorted 92, metatiles 59, object (6) 42, map 1 sheet (3 503 placements) |
| grid dump: `K` lines / bytes | 146 / 64 614 233 |
| OAM stream dump | 887 KB, 4 040 frame lines |
| `mep_lint.py` on the pack | 0 errors, 0 warnings |

## Step 2: the palette gap (F14.4 method)

The method is the one in
`docs/validation/f14.4-adr0230-palette-gap-measurement-2026-09-24.md` and
`docs/validation/f14.4-adr0229-option-i-measurement-2026-09-23.md`. Their
scratch scripts are not versioned (those logs describe what they count); they
were copied from the session scratchpad and adapted to a CHR ROM key:

- A key is a distinct `(index, palette)` on a `<tile>` line, with condition
  prefixes ignored.
- *Drawn* means the key has a `defaultTile=N` row.
- A sheet entry is credited through its `index` field. On a CHR ROM game an
  H/V-mirrored sprite cell keeps the unflipped index, so the ADR-0178
  `source` credit happens by itself.
- Folds versus colourways use `artist_chr_kit`'s own predicates
  (`painted_indices`, `fade_related`, `fold_measure`, 25° gate), in the same
  two forms as the ADR-0230 log: pairwise to the cell, and `compute_folds`
  pack-wide.
- Two criteria for which palette entries a pattern paints:
  - **resolved**: `painted_indices` on the ROM's 16 bytes for the index;
  - **kit**: what `artist_chr_kit` does with a CHR ROM row today, which is to
    assume all four entries.

| | Excitebike |
|---|---|
| keys / drawn | 868 / 356 |
| drawn keys on a sheet cell | 337 = **94.7 %** (sprite 191/194, background 146/162) |
| drawn shapes on a sheet cell | 337/337 = **100 %** |
| all keys / all shapes on a sheet cell | 38.8 % / 65.8 % (the other shapes are the 512-tile ROM export, never drawn) |
| missing drawn keys | **19** on 16 shapes (1 key: 14 shapes, 2: 1, 3: 1) |
| pairwise, resolved: inert / fade / brighter / brighter over the gate / colourway | 4 / 2 / 4 / 1 / **8** |
| `compute_folds`, resolved: same group as the cell / own picture | 10 / 9 |
| sprite: fold / colourway (resolved) | 2 / 1 |
| background: fold / colourway (resolved) | 8 / 8 |
| kit criterion (all four entries): colourway | **19 / 19** |
| drawn palettes per shape, max / sheet palettes per shape, max | 4 / 1 |
| (c)-style extra cells: unfolded / folded (resolved) | 19 / 9 |

Reading it:

- **The gap is 5 % of drawn keys, against 15 % (Castlevania) and 54 %
  (Zelda).** Excitebike draws with only seven palettes, and 321 of its 337
  drawn shapes wear exactly one.
- **The sprite side is almost closed.** Two keys are inert: the rider tiles
  `0xF6`/`0xF7` under `FF311C0F`, where the two painted entries have
  identical RGB to the sheet's `FF20160F`. One key is a colourway: `0xFE`
  under `FF20190F`, a painted entry going red to green.
- **The background misses are the track's palette changes.** They are the
  banner and track tiles `0x100`–`0x11D`, `0x1B7` and `0x1FB`–`0x1FE` under the
  `29220F16` / `29220F20` / `29272230` / `29271836` quartet. There are 4 brighter
  steps (`29220F20` → `29272230`), plus 1 over the gate, and 8 colourways
  (mostly `29272230` ↔ `29220F16`).
- **On a CHR ROM pack, the kit criterion folds nothing.** Assuming all four
  entries means palette entry 0 (the universal background colour) and every
  unpainted entry must also agree. So all 19 misses, and every other
  multi-palette index on the pattern pages, stay separate cells. The CHR kit's
  own `fold` block confirms this: 608 cells on CHR pages, 0 folded. Its
  docstring calls this the conservative direction for a CHR ROM row. With
  `--rom` the bytes are available, and resolving them would fold 10 of the
  19. This observation is relevant to ADR-0230 / F14.9. It is not a defect.

## Step 3: the artist kit

The commands are the ones in `docs/remastering-a-game.md` §3 and the
Contra kit re-run log:

```sh
python3 scripts/artist_kit.py     <pack> --out <kit> --verify
python3 scripts/artist_bg_kit.py  <pack> --out <kit> --verify
python3 scripts/artist_chr_kit.py <pack> --rom <run>/Excitebike.nes --out <kit> --verify
python3 scripts/artist_map.py --out <kit> --stage stage1 --dump <run>/grid-dump.txt \
  --pack <pack> --scale 4 --verify        # refuses, as documented (below)
python3 scripts/artist_kit_assemble.py <kit> --title "Excitebike, Selection A track 1"
```

- `artist_kit.py`: 868 keys in the recording → 337 after a control
  rebuild, 337 with the kit, 0 lost, 0 invented: **PASS**. The 531 dropped
  keys are the ROM export (ADR-0172). The tool says so.
- `artist_bg_kit.py`: 6 objects, **0 elements** recovered from
  `adjacency.json`, 1 scene, 0 dropped. 337 → 337.
- `artist_chr_kit.py`: 8 pages in 3 banks (2 real banks plus the blank-tile
  bucket). The log line is "recorded 498 (97%), ROM fill 14, unrecoverable 0
  -> 100% complete". 868 → 868, and the rebuilt `hires.txt` is identical to the
  untouched pack's.
- `artist_map.py` exits 1 with the documented refusal: "this pack keys its
  tiles by CHR index (ADR-0172) and the grid dump carries no index".
- `artist_kit_assemble.py`: 3 parts, 25 files, 2 144 cells, and all three
  parts passed.

### Figures and cycles (ADR-0170 poses)

`poses.json` has 5 410 frames, 58 poses and 9 cycles. None of the 9 cycles
carries a `driver`, which matches ADR-0181's Excitebike counter-case. It
also has 16 sequences. The kit lays out 53 figures on 10 sheets:

| Sheet | Run | Period / repeats / holds | Figures | Reads as (by eye) |
|---|---|---|---|---|
| `usr000` | `cycle000` | 2 / **772** / 2,2 | 14 (12 variants), plays columns 1 8 | the rider cruising, wheels alternating; variants carry a small marker sprite or a neighbour |
| `usr001` | `cycle001` | 2 / 23 / 10,2 | 4 (3 variants) | wheelie |
| `usr002` | `cycle005` | 2 / 4 / 26,3 | 4 (3 variants) | airborne / leaning |
| `usr003` | `cycle007` | 2 / 2 / 14,8 | 2 | crash (rider down) |
| `usr004` | `cycle008` | 2 / 2 / 4,4 | 2 (2 variants) | wide (5×3) bike pose |
| `usr005`–`usr008` | `seq000`, `seq005`, `seq011`, `seq013` | sequences, 3–4 phases, x2–x4 | 2, 2, 4, 1 | lean / landing transitions; `-` columns are phases drawn on an earlier sheet |
| `usr009` | rest grid | no run | 18 (3 rows) | row 1: the rider **on foot** after a crash; rows 2–3: bikes, tumbles, fallen bike |

Cycles that got no row of their own had every pose already laid out on
an earlier sheet: `cycle002` (020/021, x6), `cycle003` (period 4, x5),
`cycle004` and `cycle006`. Left out, with the kit's reasons:
`pose049`, `pose050` and `pose057` are fusions (ADR-0228). `pose000` and
`pose011` are HUD, pinned for the whole capture (ADR-0173).

Two things a reader should know, neither of them a defect:

- **Rotated duplicates.** `seq010`, `seq012`, `seq014` and `seq015` are
  the four rotations of one 4-phase loop (`pose029` → `048` → `042` → `045`,
  holds 2,2,2,2) seen twice each, below the cycle threshold. `seq000`–`seq004`
  and `seq009` similarly rotate `004`/`014`/`009`/`015`. The kit lays out
  each figure once, so the sheets are not duplicated.
- **The run on foot is not a cycle.** The rider running back to the bike
  after a crash lands in the rest grid as 6 unordered figures.

### Scenery groups

| Sheet | Object | Cells | What it is |
|---|---|---|---|
| `usr010` | `obj000` | 17 | the raster-split HUD: `3RD 1:24:00`, the `TEMP` gauge, `TIME` |
| `usr011` | `obj001` | 8 | the `NINTENDO` banner on the 16 px grid, x61 |
| `usr012` | `obj002` | 7 | the same banner one 8 px phase over, x3 |
| `usr013` | `obj003` | 6 | a vertical cross-section of the track: infield hay bale, dirt lanes, lower hay bale, grass verge |
| `usr014` | `obj004` | 2 | pennant bunting over sky and grass |
| `usr015` | `obj005` | 2 | the lower hay-bale strip |
| `scene/screen001` | | 1 | the one whole-screen capture (the start grid) |

- **Crowd.** The crowd is a frozen scenery band (ADR-0153). It is in the
  recorder's `metatiles` sheet, which the kit does not re-cut, and on the
  pattern pages. No object holds it.
- **Ramps.** They are **not** in any kit surface except the pattern pages.
  In the recorded pack their tiles, mostly palette `29271836`, are 75 of
  `unsorted`'s 92 cells. The recorder's `map-000` strip (283 × 14 cells of
  16 px, 4 528 px of track) has 459 cells with no placement, and those holes
  are where the ramps, the mud and the banner overlaps stand. `artist_bg_kit`
  recovered no element, and `artist_map` cannot serve a CHR ROM pack. So an
  artist who wants the ramps as ramps has only the 8×8 cells. This is the
  ADR-0209 coverage gap on a new kind of scenery, not a new defect.

### Pattern pages

| Page | Palette | To paint / ROM fill / empty |
|---|---|---|
| `Chr_00_0` (rank 0) | `0F001030` | 255 / 1 / 0 |
| `Chr_00_1` | `FF20160F` | 188 / 0 / 68 |
| `Chr_00_2` | `FF20160F` | 2 / 0 / 254 |
| `Chr_01_0` (rank 0) | `0F001030` | 243 / 13 / 0 |
| `Chr_01_1` | `29220F20` | 134 / 0 / 122 |
| `Chr_01_2` | `29220F16` | 12 / 0 / 244 |
| `Chr_01_3` | `29271836` | 2 / 0 / 254 |
| `Chr_FFFFFFFF_0` (blank bucket) | `0F001030` | 32 / 0 / 224 |

- The two rank-0 pages are full, as a CHR ROM game should be: 0 red cells,
  0 unrecoverable. Lower ranks are not completed, by design, and their empty
  cells show the recorder's magenta.
- The rank-0 pages wear `0F001030`, the bootstrap's neutral palette. That
  is the palette of the ROM export. ARTIST.md marks both pages "inferred -
  check it". **But their cells claim to be evidence** (defect draft below).

### Contact sheet

`excitebike-kit-contact-sheet.png` is a 2 640 × 1 766 render of every kit
surface: the 10 figure views, the 6 scenery sheets, `screen001` at half
size, and each pattern page beside its legend at half size. It lives in
the session scratchpad and is not versioned.

## Step 4: the painted round trip

This is the post-#435 "When you are done" recipe from the kit's own
ARTIST.md, run on a copy:

```sh
cp -R <run>/Excitebike/auto <p>/painted        # plus the kit's sheets/, chr/, scene/ per ARTIST.md
python3 scripts/mep_build.py build <p>/painted
for f in <kit>/figures/usr*-figure.png; do
  python3 scripts/mep_figure.py import <p>/painted "$f" --verify
done
python3 scripts/mep_build.py build <p>/painted   # and once more, to check idempotence
```

- **The painted cell.** In `figures/usr000-figure.png`, every opaque pixel
  of `pose001` node 3 was painted `#FF00FF` (144 px at 4x). This is the
  rider's back, CHR index `0x0A`, in the 772-repeat wheel cycle. The
  unpainted control ran the same recipe with the figure untouched.
- **Import.** `usr000-figure: 132 cells, 1 painted, 1 written`. It went to
  `usr009.png`, the rest-grid row that owns the key in the built pack: "no
  rule moves". The other 9 figures wrote 0 cells. Every import's
  `--verify` reported 337 → 337, 0 lost, 0 added, with `hires.txt`
  unchanged.
- **Builds.** 0 errors each time. The third build is byte-identical to the
  second on both arms.
- **ADR-0183 §4.** The painted and control `hires.txt` are
  **byte-identical** (sha256 `1bc6438a…`). Both carry the same 337 keys,
  which is exactly the recording's drawn, sheet-reached set. 569 rules,
  and every one draws from `sheets/`. In the painted pack exactly 1 rule /
  1 key (`0A` / `FF20160F`, `sheets/usr009.png` at (4, 184)) draws magenta.
  The control has 0.
- **In game.** Each built `textures/` was installed as the loose pack
  `<home>/HdPacks/Excitebike/`. It was replayed from the minted state with
  the recording's own input:
  `headless_record <g>/Excitebike.nes <s> <g>/out capture screenshot mep-off log input=… state=…`.
  Every run logs `[MEP] textures: loaded loose NES HD pack from
  HdPacks/Excitebike/hires.txt (569 tiles, scale 4)` and captures 1024×960.
  - At **frame 1323** (12 s) the painted run shows **144 magenta pixels** at
    (240, 628)–(255, 651), on the rider's back. The control shows 0. The two
    frames differ in exactly those 144 pixels.
  - At 10, 11, 13 and 14 s the frame shows 0 magenta pixels. The wheel
    cycle alternates `pose001` and `pose002` every 2 frames, and `0x0A` is
    drawn only in `pose001`. So a single frame catches the cell about half
    the time, and a different pose hides it altogether. The OAM stream
    confirms nodes 2–8 in one phase and 82–88 in the other.
- **#447 (nearest vs xBRZ), noted and not fixed.** All 569 rules of a
  rebuilt pack draw the nearest-neighbour sheet crops, where the auto layer
  draws xBRZ-smoothed pages. So the unpainted control renders crisper than
  the recording, as the F14.4 log found on Castlevania and Zelda.
- **The kit's per-sheet note** (the `artist_kit.py` bullet "Rebuild after
  painting") still gives the pre-#435 order, which is copy, import, build,
  aimed at the recording itself. On Excitebike that order is harmless. The
  first build rewrites no sheet, so the #435 guard does not refuse. The
  import writes the same single cell, and a build then gives the same
  byte-identical `hires.txt`. It only disagrees with ARTIST.md's own "When
  you are done" block. It is a documentation inconsistency and was not
  filed.

## Defect found (draft, not filed)

**Title:** `artist_chr_kit counts the bootstrap's CHR ROM defaultTile export as recorded evidence (seen: true)`

**Body (draft):**

> On a CHR ROM recording, the bootstrap's `AddRomTiles` writes one
> `defaultTile=Y` row per CHR tile under the neutral palette `0F001030`.
> Those rows land on the real bank pages (`Chr_00_0`, `Chr_01_0`), not on a
> synthetic page the way the CHR RAM PRG scan does (`0x504247xx`, which the
> kit leaves alone). `artist_chr_kit.py` treats them as this recording's
> evidence.
>
> Repro (Excitebike, `main` `46b9136a`): record 90 s from
> `scripts/stages/excitebike` (mint, then `stage1-probe` + `stage1-run`,
> `bootstrap hdpack-off`). Then run `artist_chr_kit.py <pack> --rom
> <rom> --out <kit> --verify` and compare each `chr/Chr_*.json` cell with
> `state: evidence` against the pack's `hires.txt` flags for its
> `(tileIndex, palette)`.
>
> Expected: a cell whose key has only `defaultTile=Y` rows, and whose index
> the run never drew in any palette, is not `seen: true`. It is not green
> in the legend ("a cell the run recorded"), and the "recorded" total counts
> only drawn indices.
>
> Observed: 175 cells (64 on `Chr_00_0`, 97 on `Chr_01_0`, 14 in the blank
> bucket) belong to indices never drawn in any palette, yet carry
> `state: evidence, seen: true`. Another 337 are the `Y` row of an index that
> was drawn, but only under another palette. The log says "recorded 498 (97%)"
> while the run drew 337 of 512 indices (66 %). ARTIST.md does label both rank-0
> pages "inferred - check it", but the per-cell contract (`seen: false` for
> anything never seen in play) is broken for the whole page. Pixels and the
> round trip are unaffected: `--verify` still passes 868 → 868.
>
> Suggested priority: P2 (misleading provenance on the artist's surface).

The spike script compared every `evidence` cell's key with the pack's
`defaultTile` flags: 356 cells `N`, 337 `Y` only for an index drawn in
another palette, and 175 `Y` only for an index never drawn.

## Caveats

- One track, one route (Selection A, track 1, 90 s of held A with lean and
  turbo bursts). The recording contains a crash and the run on foot. It has
  no finish, no results screen and no Selection B or Design mode, so their
  tiles are only the ROM export.
- The fold criteria are the kit's predicates applied in scratch code. The
  resolved criterion is what the kit *would* do with the ROM bytes. It is
  not current behaviour.
- The in-game proof is a single-frame screenshot. The frame was picked by
  scanning 10–14 s for the pose that draws the painted tile. That is not
  chance: the cell is visible in about half the frames of that pose's cycle.
- Excitebike numbers in older ADRs (ADR-0153, ADR-0176, ADR-0177, ADR-0181)
  come from different routes and binaries. For example, the wheel cycle was
  seen 469 times in ADR-0181 and 772 here. They are not comparable row for
  row.
