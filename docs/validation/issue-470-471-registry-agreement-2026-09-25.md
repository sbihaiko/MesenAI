# Issues #470 and #471: the sheet registry and the `<tile>` rules disagreed (2026-09-25)

Both bugs showed up in Punch-Out!! (MMC2, CHR ROM) after the #458 fix
(`docs/validation/issue-458-mmc2-sprite-bank-2026-09-24.md`). On the fight 1
route that fix left 4 sheet keys that were never drawn and 3 drawn keys whose
tile had no cell. This change builds on #468 (`OamFetchLatch`) and #476
(`SpriteFetchLog`). The raw material (packs, dumps, scripts) stayed in a
session scratchpad and is not versioned.

## #470: a fully transparent sprite tile

**Cause.** `NesPpu::GetPixelColor` points `_lastSprite` at the
highest-priority active shifter and only moves it to a sprite with an opaque
pixel. A sprite whose 16 bytes are all zero turns active at its x and goes
inactive after one dot. So `HdBuilderPpu::DrawPixel` writes a `<tile>` rule
for it only when it is the top active shifter on that dot. The latch
(`OamFetchLatch`) records every half fetched on a row that showed sprites. The
blank latch tiles `1CFD`, `1DFD`, `1FFD` and `1FFF` (`FF353008`) were named by
the sheets and never emitted.

**Which side is right: record neither.** The loader and the ADRs treat a blank
tile as drawing nothing:

- `HdNesPack::DrawTile` returns at once on `IsFullyTransparent`, so a rule for
  an all-zero tile draws nothing.
- `HdNesPack::InitializeFallbackTiles` skips blank tiles.
- At run time `HdNesPpu` gathers a pixel's sprites only while some shifter is
  active. A blank sprite's own shifter is active for one dot, so art painted on
  its cell would show only where another sprite happens to overlap it. The
  cell would promise the artist something the pack cannot deliver.
- ADR-0223 keeps a flat cell out of the anchor pool on its own ("a gate made
  only of emptiness probes would fire on every blank screen").
- The kit already skips a fully transparent cell on figure import (#452,
  PR #466). The blank tile Castlevania parks in four of Simon's poses is the
  one that caused #452, and it is gone from the recording now.

Emitting a rule every time would do the reverse. It would add a
fully transparent rule per palette to the pack, and each one draws nothing.

**Fix.** Both sides use one test, `OamFetchLatch::IsFullyTransparent`
(all 16 bytes zero):

- `OamFetchLatch::ForEachLatched` hands no blank half to `emit`
  (`RecordSprite`) and no blank bank to `emitBank` (`RecordSpriteBank`). A
  blank half is not an OAM entry either. A drawn bank of a blank half is still
  a key, because its rows made rules.
- `HdBuilderPpu::DrawPixel` makes no sprite rule for a blank tile.

## #471: a background tile drawn only off a cell's origin scanline

**Cause.** `RecordGridFrame` fills a grid cell from the scanline at its origin
(`y % 8 == 0`) and interned only the shapes of those scanlines. `DrawPixel`
writes a rule from every scanline. Punch-Out!! draws `00FD` only on a cell's
last scanline (`y % 8 == 7`), as a one-line raster effect. That gave 3 rules
(`112A0F36`, `112A1436`, `112A2536`) and no cell.

**Fix.**

- The layout moved, unchanged, into the new host-free
  `MesenSheets::LayOutGridRuns` (`TileSheetTypes.h`).
  `HdPackBuilder.cpp` is not in the unit-test link set, and the move takes it
  from 2425 to 2380 lines, under its ADR-0137 ceiling of 2438.
- After the origin scanlines, it interns the shape of every other run. The
  origin scanlines go first, so shapes seen there keep the ids they had.
- An off-origin tile takes no grid cell: the cell belongs to its origin's
  tile. The unsorted sheet (ADR-0209 Q4) gives its shape a cell, and ADR-0230
  gives that cell every palette the rules drew it in.

## Tests (strict TDD)

`scripts/core_unit_tests.cpp` gains two test functions:

- `TestTheGridRegistersAShapeDrawnOnlyOffACellsOriginScanline` (#471).
- `TestAFullyTransparentSpriteHalfNeverReachesTheRegistry` (#470). It uses a
  bank model in which bank `$1C` holds blank art.

The latch tests from #458 and #476 fed all-zero tile data, which the #470 rule
now drops. Their three `resolve` lambdas now give the tile one opaque row
byte. Nothing else changed in those tests.

**Red (#471).** This run used `LayOutGridRuns` moved verbatim, with no
off-origin loop. Verbatim:

```
FAIL  #471: a tile drawn only on a cell's last scanline reaches the shape registry: interned 0010 0011 
FAIL  #471: a tile drawn only inside a cell reaches the shape registry: interned 0010 0011 
PASS  #471: the origin scanlines are interned first, in draw order, as before
PASS  #471: a cell still takes the shape at its origin scanline, and only origin runs intern a palette
1137/1139 cases passed
```

**Red (#470).** This run had `IsFullyTransparent` present and no filter. Verbatim:

```
FAIL  #470: a blank sprite half (1CFD) is never recorded; its drawn neighbour (0580) is: named 1CFD 0580 extra 
FAIL  #470: a half decoded from a drawn bank but read from a blank one is not recorded: named 1CFD 0580 extra 
FAIL  #470: a half whose top row read blank art places no sprite, but the drawn bank its other rows read (05FE) still reaches the sheets: named 1CFE 0580 extra 05FE 
FAIL  #470: a drawn half keeps its entry, and the blank bank its other rows read (1CFE) is not a sheet key: named 05FE 0580 extra 1CFE 
PASS  #470: all-zero tile data is fully transparent
PASS  #470: one opaque pixel (a high-plane bit) is not
1141/1145 cases passed
```

**Green.** 1145/1145 on the first base, and 1157/1157 after the rebase onto `origin/main`.

**Mutations.** Each was applied alone and run with `core-unit-tests`.

| Mutation | Result |
|---|---|
| M1 `LayOutGridRuns` interns only origin scanlines (the pre-fix grid) | 2 fail (1143/1145), the two #471 registry checks |
| M2 the off-origin loop runs before the origin scanlines | 2 fail (1143/1145): `interned 0010 00FD 0012 0010 0011`, and the palette check |
| M3 `ForEachLatched` emits a blank half | 3 fail (1142/1145) |
| M4 `ForEachLatched` hands a blank bank to `emitBank` | 1 fails (1144/1145): `named 05FE 0580 extra 1CFE` |
| M5 `IsFullyTransparent` reads only the low plane | 1 fails (1144/1145): the high-plane check |

Two mutations were not caught, for these reasons:

- Letting the off-origin loop intern the origin runs a second time survives
  (1145/1145). `ShapeIdFor` is idempotent, so the mutation is equivalent.
- The `DrawPixel` gate is host-bound and cannot be linked into the unit
  tests. The E2E below covers it: every drawn key the fix removes is an
  all-zero sprite tile, and no drawn key is left without a cell.

## E2E

Every run used `headless_record <rom> <s> <prefix> bootstrap hdpack-off log`
with the grid and OAM dumps on.

- **Before** is `origin/main` at `33f92049`, which merges #468, #476 and #473.
- **After** is that commit plus this change.
- The same runs made on the base this change was first written on,
  `origin/fix/458-mmc2-bank` at `3447aa05` (before #473 was merged), gave the
  same numbers in every cell of the tables below.

The build rebuilt `NesPpu`, `NesConsole`, `HdPackBuilder` and
`InteropDLL/EmuApiWrapper` (every file that includes a changed header). Each
binary is a private copy: `headless_record` is relinked to its own dylib and
ad-hoc signed.

| Build | `MesenCore.dylib` sha256 (private copy) |
|---|---|
| before (`origin/main` `33f92049`) | `9759a3308030bf8b5d2241be722df9453a950c477b0cc28a9f0f2eb43292882c` |
| after | `da668f38bd3d6c96bee787e119c87c72e5260fd3a3c93ac1f17be815806c18e7` |

"Sheet keys" means every `(key, palette)` tile entry of every
`textures/sheets/*.json` cell, aliases included. "Drawn keys" means every
`<tile>` line with `defaultTile` N.

**Punch-Out!!, fight 1.** 70 s from `fight1.mss`, minted by hand
(`mint-fight1.txt`, 34 s). The route is `fight1.txt` as first authored
(`d1d3650b`), which is the #458 route.

| | before | after |
|---|---|---|
| Drawn keys | 1655 | 1633 |
| Sheet keys | 1656 | 1633 |
| Drawn keys on a sheet | 1652 | 1633 |
| **Sheet keys never drawn** | **4** (`1CFD`, `1DFD`, `1FFD`, `1FFF`) | **0** |
| **Drawn keys whose tile has no cell** | **3** (`00FD` ×3) | **0** |
| `sheet_keys_audit.py` | 730 entries, 4 leftover | 720 entries, 0 leftover |

The "before" column matches the numbers in the #458 log. All 22 drawn keys
the fix removes are all-zero sprite tiles, checked against the ROM's CHR:
`1C`–`1F`/`05` × `FD`/`FE`/`FF`. So are the 26 sheet keys it removes. The
only new keys are `00FD` under its 3 palettes. No other drawn key changed.

**Castlevania, stage 1.** 60 s from `stage1-run.mss`, minted by hand with
`mint-stage1.txt` (6 s); route `stage1-run.txt`. This is the #455 route.

| | before | after |
|---|---|---|
| Drawn / sheet keys | 420 / 420 | 416 / 416 |
| Never drawn / no cell | 0 / 0 | 0 / 0 |
| `sheet_keys_audit.py` | 550 entries, 0 leftover | 547 entries, 0 leftover |

The 4 removed keys are the all-zero CHR RAM tile under 4 sprite palettes. One
of them, `FF23340F`, is the blank tile of #452.

**Excitebike.** 40 s from power-on (`mint-stage1.txt` then `stage1-run.txt`).

| | before | after |
|---|---|---|
| Drawn / sheet keys | 393 / 392 | 392 / 391 |
| Never drawn / no cell | 0 / 1 | 0 / 1 |
| `sheet_keys_audit.py` | 260 entries, 0 leftover | 259 entries, 0 leftover |

The removed key is blank sprite tile `FC` (`FF20160F`). The one drawn key with
no cell, sprite tile `00` under `FF20160F` (not blank), is the same before and
after. This change neither causes nor touches it; it is filed as #479.

No leftover count went up on any game.
