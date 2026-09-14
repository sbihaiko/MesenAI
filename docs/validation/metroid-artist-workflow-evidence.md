# Metroid: a third-game pipeline run, and the first direct look at an artist's real working surface

Two things came out of one session, and the second is the more valuable.

The first is a portability result: the artist pipeline documented in
[`../remastering-a-game.md`](../remastering-a-game.md) ran end to end on
Metroid (USA) with **no code change at all**. Metroid is the third game the
pipeline has been taken through, and the first that is neither Contra (the
flagship) nor Castlevania.

The second is evidence. The most prolific NES HD-pack author reportedly works
in Excel, and he ships the spreadsheet next to his Metroid pack. It is on this
machine. It is 9.9 MB, 34 sheets and 113,855 declared rows, and reading it answers a
question the project has so far only argued about from the outside: *what is
the artist's real bottleneck?* The answer the file gives is not "drawing".

---

## 1. The pilot run

### Setup

- ROM: `Metroid (USA).nes`, SHA1 `ecf39ec5a33e6a6f832f03e8ffc61c5d53f4f90b`.
  iNES header `4e45531a 08 00 10 00 ...` — mapper 1, 8 x 16KB PRG,
  **CHR bank count 0, i.e. CHR RAM**. This matters in §2.
- Route: `scripts/stages/metroid/stage1-run.txt`, written for this run — boot
  through the title and password screens into the Brinstar entry shaft, then
  shoot, jump, morph and run the corridor in both directions.
- Everything below was run in a throwaway worktree, against a copy of the ROM
  placed at `out/Metroid.nes` (the bootstrap builder writes the pack *beside
  the ROM*).

### Commands

```sh
caffeinate -dimsu make core && caffeinate -dimsu make capture-tool

MESEN_SHEET_GRID_DUMP=$PWD/out/grid.txt caffeinate -dimsu ./scripts/headless_record \
  out/Metroid.nes 60 out/rec bootstrap hdpack-off \
  input=scripts/stages/metroid/stage1-run.txt

python3 scripts/artist_kit.py     out/Metroid/auto --out out/kit --verify
python3 scripts/artist_bg_kit.py  out/Metroid/auto --out out/kit --verify
python3 scripts/artist_chr_kit.py out/Metroid/auto --rom out/Metroid.nes --out out/kit --verify
python3 scripts/artist_map.py --out out/kit --stage brinstar --dump out/grid.txt \
  --pack out/Metroid/auto --scale 4 --verify
python3 scripts/artist_kit_assemble.py out/kit --title "Metroid, Brinstar start"

cp -R out/Metroid/auto out/painted
cp out/kit/sheets/*.png out/kit/sheets/*.json out/painted/textures/sheets/
cp out/kit/chr/*.png    out/kit/chr/*.json    out/painted/textures/chr/
cp out/kit/map/*.png    out/kit/map/*.json    out/painted/textures/sheets/

python3 scripts/mep_build.py build out/painted
python3 scripts/mep_lint.py out/painted
```

### The recording

| | |
| --- | --- |
| frames | **3607** (target 3606, 60 emulated seconds) |
| wall clock | 30.9 s |
| emulated fps | 60.099 |
| pack `<scale>` | **4** |
| sheets | **125** |
| backgrounds | 36 |
| CHR pages | 54 |
| `<tile>` rules | **2347** |
| `(tileData, palette)` keys | **2211** |
| grid dump | 42.8 MB |

The `--scale` trap the guide warns about is real on Metroid too and behaves as
documented: the recorded `textures/hires.txt` declares `<scale>4`, and
`artist_map.py` must be given `--scale 4` or the build rejects the panorama.

### The four `--verify` round trips

All four **PASS**.

| generator | verify output |
| --- | --- |
| `artist_kit.py` | build exit 0; **2211** keys in the recording, **143** after a control rebuild, **143** with the kit; 0 lost, 0 invented, 51 files added. Built 143 tiles / 14 sheets at scale 4 (58 ADR-0153 sheets). `verify: PASS` |
| `artist_bg_kit.py` | 3 objects (0 uniform cells removed), 4 elements recovered from `adjacency.json`, 18 scenes, **0 dropped**; 0 new errors (0 the pack already had); **92** keys before, **92** after, 0 lost, 0 added |
| `artist_chr_kit.py` | **2211** recorded cells byte-identical; keys **2211 → 2211** (0 lost, 0 added); `mep_build build` 0 errors; rebuilt `hires.txt` identical to the untouched pack's: **True**. 27 pages in 12 banks; real CHR 6 x 256 = 1536 tiles — **545 recorded (35%)**, 708 ROM fill, 283 unrecoverable → **82% complete** |
| `artist_map.py` | region 0, 768x328 at 1x, **3232 cells**, horizontal, 1037 frames stitched, 959806 re-sightings agreed, 960 of 3232 positions ever showed something else; **3232/3232** panorama cells byte-identical to the pack's own tile art; `mep_build` exit 0; keys **143 → 150** (0 lost, 7 added, each already in the pack's manifest); coverage 50 of 2211 keys (**2.3%**) |

Worth recording for the next game: Metroid's HUD is an overlay in the top-left,
not a band, and `artist_map.py` reported **HUD bands 0 top / 0 bottom rows
excluded** — the band heuristic correctly found nothing to exclude rather than
guessing.

`artist_chr_kit.py` handles Metroid's CHR RAM correctly: it locates the PRG
block each recorded cell came from, so the 708 "ROM fill" cells are real
evidence and not PRG code misread as tile bitmaps.

### Assemble, build, lint

```
out/kit: 4 part(s), 70 file(s), 10229 cell(s); wrote kit.json and ARTIST.md
  sprites     passed: 0 error(s), 143 keys before, 143 after, 0 lost, 0 added
  background  passed: 0 error(s),  92 keys before,  92 after, 0 lost, 0 added
  map         passed: 0 error(s), 143 keys before, 150 after, 0 lost, 7 added
  chr         passed: 0 error(s), 2211 keys before, 2211 after, 0 lost, 0 added
```

`mep_build.py build out/painted` — 150 tiles, 18 sheets, scale 4 (66 ADR-0153
sheets). **0 errors, 16 warnings, exit 0.** Every one of the 16 warnings is
the recorder's own sheet sizes not being a whole number of cells, which the
tool groups and labels as its own output.

`mep_lint.py out/painted` — **0 errors, 16 warnings, exit 0.**

That is the guide's stated acceptance test, met in full: *build exit 0,
`mep_lint.py` exit 0, and every generator's `--verify` PASS.*

### What the screenshot showed

A blind route is a route that dies, so the run was confirmed visually rather
than asserted. `scripts/headless_record out/Metroid.nes 45 out/final screenshot
hdpack-off input=...` writes the final frame to
`out/mesen-home/Screenshots/Metroid_000.png`.

That frame shows **Samus in her orange-and-red power suit, standing on a blue
rocky Brinstar floor**, with the matching rock ceiling above, a cyan horizontal
platform at mid-right, and the HUD reading `EN··06` in white at the top left
against black. Real game content, correct palette, correct room.

Probing the same route at other budgets found where it dies: at 20–40 s the
same room with `EN··30` falling to `EN··22` and yellow flying enemies on
screen; at 60 s a `GAME OVER` card; at 90 s the green `PASS WORD` screen. So
60 emulated seconds is this route's survivable window, and a longer run would
have recorded the game-over and password art instead of the stage. This is now
written into the guide as a step, because the failure is invisible in the file
counts.

---

## 2. The reference pack, and why the measurement is zero

There is a large community Metroid HD pack installed on this machine:

```
~/Library/Application Support/MesenCE/HdPacks/Metroid (USA)/
```

**1.0 GB.** 277 PNG, 44 OGG, a 760.4 MB `MetroidCompleteMap.psd`, an
`mmm.ips` patch, and a **25.3 MB `hires.txt` of 260,146 lines** holding:

| directive | count |
| --- | --- |
| `<tile>` | **150,250** |
| `<background>` | **50,903** |
| `<condition>` | **4,426** |
| `<fallback>` | 747 |
| `<img>` | 67 |
| `<addition>` | 1,987 |
| `<sfx>` / `<bgm>` | 31 / 14 |
| `<patch>` | 5 |

It declares `<scale>2` and
`<supportedRom>ecf39ec5a33e6a6f832f03e8ffc61c5d53f4f90b` — **byte for byte the
SHA1 of the ROM we recorded**.

The measurement is nevertheless zero:

```
$ python3 scripts/artist_cover.py \
    '~/Library/Application Support/MesenCE/HdPacks/Metroid (USA)/hires.txt' \
    out/Metroid/auto

artist: 67 images, 150199 tile rules, 8401 keys (tileData+palette), 1465 distinct tileData
recorded: 1 packs, 2211 keys, 1506 tileData, 168 sprite tileData on sheets
artist tileData on screen in some state: 0/1465; exact keys 0/8401
  sprite     images   0  tileData     0  seen     0
  background images   0  tileData     0  seen     0
  unseen     images  67  tileData  3063  seen     0
```

All 67 images `unseen`, 0 exhibited, no warning.

### The cause, decoded from the patch

The two files do not key tiles in the same namespace.

- Stock Metroid is **CHR RAM**, so the recorder writes the full 16-byte pattern
  as tileData:
  `<tile>0,3E7FFF7007FFFC1E00061F000007D01E,0F22121C,0,0,1,N,0,0`
- The pack ships `mmm.ips` and declares
  `<patch>mmm.ips,ecf39ec5a33e6a6f832f03e8ffc61c5d53f4f90b` (plus four further
  source hashes). Against the patched ROM it keys tiles by **CHR ROM index**:
  `<tile>0,00,0F313101,0,0,1,Y` and `<tile>47,A55,0F281604,32,208,1,N`

This was confirmed by parsing the IPS rather than inferred from the string
widths. `mmm.ips` has **1367 records, and its first record rewrites the iNES
header**:

```
offset 4, 3 literal bytes: 10 10 13
```

- header byte 4 `0x10` — 16 x 16KB PRG (stock `0x08`)
- header byte 5 `0x00` → **`0x10`** — 16 x 8KB CHR = **128 KB CHR ROM**, where
  the stock cartridge had none
- header byte 6 `0x13`

It also grows the ROM from 131,088 bytes to at least 393,232.

So the patch converts stock Metroid from a CHR RAM cartridge into a 128 KB CHR
ROM one. A 32-hex-character pattern can never equal a 2-to-4-character bank
index, so the intersection is necessarily empty and the 0% is vacuous.

That the tool reports this as a clean-looking zero, with no diagnostic, is
filed as **[issue #225](https://github.com/sbihaiko/MesenCE/issues/225)** (P2).
`scripts/artist_cover.py` is 109 lines and contains no reference to `patch`,
to CHR RAM/ROM, or to tileData width. A silent 0% is the worst available
failure mode here, because §2 of the guide tells an artist to steer on that
number.

**This conversion is not incidental to the artist's method.** It is what makes
his pack authorable at all: it buys him a short, stable, human-readable tile
id per graphic, which is the thing his spreadsheet is built to manipulate.

---

## 3. The spreadsheet

`SourceWorkForComplexHires_TXTCoding.xlsx`, shipped inside the pack folder.

**9.9 MB. 34 sheets. 113,855 declared rows, of which 97,399 carry a `hires.txt`
directive** (`<tile>`, `<condition>`, `<background>` or `<addition>`).

The 113,855 is the sum of the sheets' own declared `dimension` ranges. The
number of `<row>` elements actually present is **106,757** — the two differ
because a declared range runs to the last row ever touched, including rows whose
cells were later cleared. Anyone unzipping the workbook and counting rows will
get the second number, so both are stated rather than one being mistaken for an
error.

It is not an art tool. It is a **hand-rolled code generator for `hires.txt`**,
and its own column header says so.

### A row is one line of `hires.txt`

Sheets are named by subject, and their sizes track how much rule text each
subject costs:

| sheet | rows x cols |
| --- | --- |
| `map top layer add graphics` | 16604 x 24 |
| `Map Coding Top Layer` | 16215 x 18 |
| `Kraid Boss Expanded` | 13372 x 20 |
| `Kraid Boss Expanded Optimized` | 12853 x 20 |
| `End Screen Graphics` | 11644 x 20 |
| `MB Boss` | 10527 x 20 |
| `Power Brinstar Samus for Hires` | 8267 x 20 |
| `Enemy Graphics` | 4078 x 15 |
| `Environment Tiles` | 3765 x 18 |
| `Ridley Boss` | 2992 x 20 |
| `MB Boss Explosion` | 2947 x 20 |
| `Green Skree 1` / `Green Skree 2` | 994 / 989 x 11 |
| `Kraid Skree` / `Kraid Skree Dec Block` | 989 / 979 x 11 |
| `Kraid2 Skree Dec Block` / `Kraid2 Skree Metal Block` | 989 / 989 x 11 |
| `Hud Coding` | 926 x 18 |
| `Pause Screen Item Indicators` | 470 x 17 |
| `Power Brinstar Samus LandFall F` | 431 x 20 |
| `Zero Suit Carryover` | 365 x 5 |
| `Armor Suit Carryover After IPS` | 339 x 13 |
| `Zero Suit Carryover After IPS` | 338 x 13 |
| `Item Graphics` | 293 x 15 |
| `Armor Suit Carryover` | 292 x 5 |
| `Zero Hair Fix Coding` | 275 x 20 |
| `Samus Sprite Codes` | 245 x 8 |
| `Title Screen` | 219 x 15 |
| `Samus Carryover` | 178 x 25 |
| `Statue Room Graphics` | 165 x 20 |
| `Bottom Right Map Fix` | 72 x 11 |
| `background layer numbers` | 39 x 2 |
| `MB Boss Glass Break` | 21 x 20 |
| `dragon fireball y coding` | 7 x 3 |

`background layer numbers` is a plain two-column crib sheet — `1 |
mmtitletexttopfade2`, `2 | doortransitoin`, `3 | titleexplosion`, `4 | empty`,
`6 | mmtitle`, `7 | mmtitle & map bottom layer`, `8 | EndingBackgroundShip` —
i.e. he is keeping the layer-number namespace in his head, in a tab, by hand.

### The columns are the fields of a `<tile>` rule

`Power Brinstar Samus for Hires` row 1 is fully labelled, in plain English,
and the labels are exactly the rule's grammar:

| cell | header |
| --- | --- |
| B1 | `Condition` |
| C1 | `Type of Action or Tile Sheet Reference` |
| D1 | `Graphic Address Code` |
| E1 | `Comma` |
| F1 | `X Coordinate` (+ the note quoted below) |
| G1 | `Comma` |
| H1 | `Y Coordinate` |
| I1 | `Comma` |
| J1 | `Brightness` |
| K1 | `Comma` |
| L1 | `Apply to all Palettes?` |
| M1 | `Comma` |
| N1 | `X Direciton of Addition` *(sic)* |
| O1 | `Comma` |
| P1 | `Y Direciton of Addition` *(sic)* |
| Q1 | `Comma` |
| R1 | `Addition Unique Identifyer` *(sic)* |
| T1 | **`Hires.txt File Code Results`** |

The **commas are their own columns**, holding a literal `,`. He is laying out
the serialized text of the format as a grid, one glyph-group per cell.

This fully-labelled header is the exception, not the rule: only three sheets
carry it (`Power Brinstar Samus for Hires`, `Zero Hair Fix Coding`, `Power
Brinstar Samus LandFall F`). The others either begin directly with data — the
first row of `Kraid Boss Expanded` is already `<tile>0 | <tile>0 | , | 14EB | ,
| FF271B36 | , | 0 | , | 0 | , | 1 | , | Y` — or carry a different layout.
The grid is the same either way; only the labels are missing.

### The last column is the product

**25 of the 34 sheets carry a concatenation column**, found by scanning every
sheet part for a formula that chains four or more cell references with `&`. The
nine that do not are the six carryover/mirror tables, `background layer
numbers` (the crib sheet above), and two sheets with their own layout (`map top
layer add graphics`, `dragon fireball y coding`). That count is the strongest
single fact in this section because it is checkable without reading the art:
unzip the `.xlsx` and grep `xl/worksheets/*.xml` for `<f>` bodies containing
`&`.

Only three sheets *title* the column — `T1` is headed `Hires.txt File Code
Results` — and every row under it is a
string concatenation of the row:

```
T2 = A2&B2&C2&D2&E2&F2&G2&H2&I2&J2&K2&L2&M2&N2&O2&P2&Q2&R2&S2
```

On the 18-column sheets the same job lands in column R:

```
R2 = D2&E2&F2&G2&H2&I2&J2&K2&L2&M2&N2&O2&P2
```

producing, from `Environment Tiles` row 2:

```
[NorfairFrame8]<tile>47,A55,0F281604,32,208,1,N
```

That column is the deliverable. He fills it down and pastes it into
`hires.txt`. 97,399 rows of it.

### The spreadsheet arithmetic is doing domain work

This is the part that matters. The formulas are not bookkeeping; they are the
animation layout, expressed as fill-down arithmetic.

On `Environment Tiles`, three formulas per row:

```
B2 = B7+1          # animation frame number
D2 = A2&B2&C2      # splice that number into the condition name
J2 = J7-32         # X offset into the sheet
```

- `B2 = B7+1` takes the frame number from the block five rows below and adds
  one. `B7` is `7`, so `B2` is `8`.
- `D2 = A2&B2&C2` glues `'[NorfairFrame'` + `8` + `']<tile>47'` into
  `[NorfairFrame8]<tile>47`. **The condition name is computed from the frame
  index by string concatenation.**
- `J2 = J7-32` steps the X coordinate by −32 from the same block below. `J7`
  is `64`, so `J2` is `32`.

Read together: **each animation frame increments a frame counter by 1 and walks
32 pixels along the sheet, and the frame counter is spliced into the condition
name.** One fill-down emits an entire animation strip, correctly named and
correctly addressed. That is the whole trick, and it is why Excel is the tool.

He also wrote his own coordinate maths into the header, as a note to himself,
in the `X Coordinate` cell:

> *"To determine x value of mirrored sprite in this section, take the x value
> of what a non-mirrored sprite would be and subtract that from 1296."*

`dragon fireball y coding` (7 rows, 3 columns: `145 | | 150`, `153 | | 158`,
`120 | | 125`, `128 | | 133`) is the same thing at the smallest scale — a
scratch pad for working out one enemy's Y offsets by hand.

### The carryover sheets are lookup tables feeding `<fallback>`

`Armor Suit Carryover After IPS` is five parallel columns of hex tile ids,
headed `Brinstar Coding | Norfair Coding | Tourian Coding | Kraid Coding |
Ridley Coding`:

```
Brinstar   Norfair   Tourian   Kraid   Ridley
200        0800      E00       1400    1A00
201        0801      E01       1401    1A01
205        0805      E05       1405    1A05
...
```

Each row says "this graphic, in these five regions". Pairing the Brinstar
column with another region's column emits the `<fallback>` lines, and the pack
file shows exactly that, under his own comment headings:

```
#Armor Suit Carryover Norfair
<fallback>0800,200
<fallback>0801,201
<fallback>0805,205
#Armor Suit Carryover Tourian
<fallback>E00,200
<fallback>E01,201
```

747 `<fallback>` lines in the shipped pack, generated from four carryover
sheets (`Armor Suit Carryover`, `Zero Suit Carryover`, and their two
`After IPS` variants, 1334 rows between them).

### The conclusion the file supports

**His bottleneck is emitting roughly 200,000 lines of rule text with correct
coordinates, frame indices and condition names. It is not drawing.**

Every structure in the workbook is a device for producing that text at volume:
comma columns, concatenation columns, coordinate steppers, frame counters
spliced into names, region lookup tables, a tab remembering what layer number 7
means. Nothing in the 34 sheets is about pixels. The 760 MB `.psd` is where the
drawing lives, and it needed no spreadsheet.

Excel wins this job because fill-down gives him **arithmetic over the fields of
a serialization format**. That is an unusually specific thing to be competing
with, and it is now measured rather than assumed.

---

## 4. What this implies — proposal, not decision

*Nothing in this section is decided. It is the reading of the evidence, written
down so the decision can be made deliberately.*

### What the kit already answers

Most of the workbook is work our pipeline does not ask an artist to do at all:

- **Coordinates.** Every kit surface ships a `.json` sidecar naming which cell
  is which tile. Nobody hand-computes an X offset, and nobody writes a note to
  themselves about subtracting from 1296.
- **Condition text.** ADR-0189 emits conditions; the artist does not
  concatenate `'[NorfairFrame'` with a number.
- **The serialized line itself.** `mep_build.py build` regenerates
  `textures/hires.txt` from the sheets. The guide says plainly not to hand-edit
  it. The entire comma-column apparatus has no counterpart in our flow because
  there is no text for a human to assemble.
- **Region carryover.** What the carryover sheets do by hand,
  `artist_chr_kit.py`'s borrowed/donated precedence does from evidence.
- **Fade variants.** The pack carries `LettersFade1/2/3`,
  `PowerupMessage*Fade1/2/3`, `MapTopLayerRidleyLightBridgeFade1..4` — dozens
  of near-duplicate images that exist only because a fade re-keys every tile.
  The CHR kit's fold collapses exactly this class (4712 cells to 3439 on the
  flagship).

### What it does not answer

- **Getting the art in.** We generate paintable surfaces; he has a 760 MB PSD
  and a workflow for turning it into a pack. Nothing here measured the import
  direction.
- **Scale.** 2211 keys from a 60-second recording against 8401 keys in his
  shipped pack. Our surfaces are correct at the size we have tested them, not
  demonstrated at his.
- **The patched-ROM case.** He changed the cartridge's CHR arrangement to make
  the job tractable. Our tools assume the stock ROM (see §2 and issue #225).

### The one sharp open question

He hand-rolls a frame index with `=B7+1` and splices it into a condition name.
That is, precisely, the job a generated `frameRange` condition would do.

**[ADR-0189](../adr/0189-a-sprite-group-edge-is-serialized-as-a-spritenearby-condition-and-a-conditioned-tile-always-keeps-a-bare-twin.md)
§4 deliberately declines to emit it**, and states its reason:

> **`frameRange` from `PoseStats.Cycles`.** We observe a *period*; the
> condition needs a *phase*, and it is tested as `FrameNumber % A >= B` against
> the emulator's **global** frame counter, which has no relation to the
> recording's phase. Correct only for an animation locked to that counter, and
> silently wrong for anything the player triggers.

The reason is sound on its face, and this document does not dispute it. What
the evidence adds is the other side of the ledger, which ADR-0189 could not
have had: the most productive author in this space is **doing that arithmetic
by hand, at scale, in a spreadsheet**, and his `<condition>Samusframe9,frameRange,35,31`
rows show he is supplying the phase himself — from knowledge of the animation,
not from the recording.

Note also that the phase objection is weakest exactly where he is strongest:
his `NorfairFrame1..8` environment animations are scenery cycles, the
"animation locked to that counter" case the ADR names as the correct one; his
`Samusframe0..9` rows are the player-triggered case the ADR names as silently
wrong. The evidence therefore does not simply overturn the decision — it
suggests the decision may be splitting on the wrong axis.

**The measured argument to revisit ADR-0189's `frameRange` decision is now on
the table. It needs an ADR, which is a human decision.** Do not implement
anything on the strength of this section.

---

## 5. What this run does not cover

- **Nothing was painted.** The `--verify` round trip copies the unpainted kit
  back into a pack copy, which is what it is designed to check. No claim is
  made here about painting ergonomics.
- **Drivers not exercised:** `--also` (second-recording donation), `movie=`
  (TAS), `cheat=` (RAM cheat), `cdl=` (code/data logger). One route, one
  60-second run, one `input=` script.
- **Steps not run:** `mep_render_audio.py`, `audio_cleanup_suggest.py`,
  `artist_ai_review.py`, and `mep_build.py pack` (the ship step).
- **`mmm.ips` was never applied.** The IPS was parsed, not executed; no
  recording was made against the patched ROM. So coverage against the
  community Metroid pack is **unmeasured**, not proven to be zero in
  principle. Whether recording the patched ROM would produce a meaningful
  number is an open question, and is the first thing to try if anyone picks up
  issue #225.
- **One route, one room.** The panorama covers 2.3% of the pack's keys and the
  CHR kit reached 35% of real CHR by recording. This is a portability check,
  not a coverage result.
- **The workbook was read, not audited.** Sheet names, row counts, headers and
  the formulas quoted above were read directly from the file. Six sheets were
  inspected cell by cell; the "25 of 34 carry a concatenation column" count was
  obtained mechanically, by scanning every sheet part for a formula chaining
  four or more cell references with `&`, and is exhaustive over the file — but
  it says only that the column exists, not that its inputs are correct. No
  claim is made that the sheets without a fully-labelled header row use the
  same column order as the ones with it.
