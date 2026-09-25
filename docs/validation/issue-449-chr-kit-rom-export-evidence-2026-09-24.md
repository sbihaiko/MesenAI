# Issue #449: the CHR kit counted the bootstrap's ROM export as recorded evidence

**Date:** 2026-09-24
**Base:** `origin/feat/f149-adr0230-colourways` (PR #461, F14.9), because it
modifies `scripts/artist_chr_kit.py`.
**Scope:** `scripts/artist_chr_kit.py`, `scripts/palette_folds.py` (one
guard), `scripts/test_artist_chr_kit.py`, `scripts/AGENTS.md`. No Core or UI
code changed.
**Consistent with:** ADR-0210 §1 (the recording is the only source that is
`seen: true`) and §2 (a shape taken from the ROM's CHR is emitted with
`defaultTile=Y`), and ADR-0183 §3 (evidence and inference never confused).
No new decision: this is a fix under those ADRs.

## Root cause

- `HdPackBuilder` sets `DefaultTile = true` in two places only. Both are
  static exports written before play: `AddRomTiles` (CHR ROM, onto the real
  bank pages, neutral palette `0F001030`) and `AddPrgScanTiles` (CHR RAM,
  onto the synthetic `0x504247xx` pages).
- Every tile the run draws is created by `CaptureOrCapPaletteVariant` with
  `DefaultTile = false`. `SaveHdPack` writes the flag as the last field of the
  `<tile>` row, so a `Y` row is the export and an `N` row is recorded evidence.
- `artist_chr_kit.py` ignored that field. `write_bank` marked every cell with a
  row `evidence, seen: true`, and `Bank.art` (the source of the "recorded"
  total) counted every row.
- On a CHR ROM game the export lands on the real `Chr_00_0` / `Chr_01_0`
  pages, so the whole rank-0 page read as green.
- The unit fixture hid the bug. `chr_rom_rows` wrote every recorded CHR ROM row
  as `Y`, which the recorder never does for a drawn tile.

## The fix

- `TileRow.exported` is true for a `Y` row. Its docstring cites the builder
  lines that make the flag a real signal.
- `Bank.art` now holds only drawn rows. `Bank.exported` holds the export's
  rows for indices the run never drew.
- A cell holding an export row is `state: fill`, `origin: romExport`,
  `seen: false`, and amber in the legend. Its pixels are still copied byte for
  byte, and `verify` still checks them, because its `Y` row draws from that
  cell. No fill rule is emitted for it.
- The ROM fill, the PRG fill and the donor pass skip a cell whose rank-0 slot
  already holds a row (`Bank.holds_row`).
- `recorded` counts drawn indices. `filled` adds the exported cells on rank 0.
  `rulesSafe` and `rulesPermissive` still count only the cells a rule could be
  emitted for.
- `compute_folds` skips export rows. An export is not a picture the run drew,
  so it neither folds nor serves as a fold base.
- Known blind spot, in the conservative direction: a tile drawn under exactly
  `0F001030` bumps the export's usage without adding a row, and `hires.txt`
  records no usage. Such a tile reads as a fill, never the reverse.

## Red before the fix

The fixture was changed to write drawn CHR ROM rows as `N`, and two tests were
added: `test_the_bootstraps_rom_export_is_a_rom_fill_not_evidence` and
`test_an_exported_cell_keeps_its_pixels_and_gets_no_extra_rule`. Run against
the unmodified kit:

```
FAIL a cell holding only the bootstrap's ROM export is a fill, seen: false: Counter({('evidence', True): 240})
FAIL and it says the bootstrap exported it from the ROM: {None}
FAIL no cell of an index the run never drew is seen: true: 
FAIL recorded counts only the indices the run drew; the export is ROM fill: recorded=240 filled=16 unrecoverable=0
FAIL the legend paints an exported cell amber, not green: (46, 160, 67, 255)
41/46 cases passed
```

After the fix: 46/46.

## Mutations

Each mutation was applied alone, then the suite was run and the file restored.

| Mutation | Result |
|---|---|
| `TileRow.exported` always returns `False` | 5 checks fail (the same 5 as the red run) |
| `verify` stops checking `romExport` cells | 1 fails: `cells_checked` 100, expected 340 |
| the CHR ROM fill ignores `holds_row` | 1 fails: `filled=296 unrecoverable=-140` |
| `filled` omits the exported cells | 1 fails: `filled=16 unrecoverable=140` |

## E2E: Excitebike (CHR ROM)

This used the 90 s recording from the deep measurement
(`docs/validation/excitebike-deep-measurement-2026-09-24.md`, pass 1), with
`artist_chr_kit.py <pack> --rom <rom> --out <kit> --verify`.

| | Before (deep-measurement log) | After |
|---|---|---|
| log line | recorded 498 (97%), ROM fill 14, unrecoverable 0 | recorded **337** (66%), ROM fill **175**, unrecoverable 0, 100% complete |
| `seen: true` cells of an index with no `N` row | 175 | **0** |
| `evidence` cells, all pages | 868 | 356 (exactly the pack's 356 `N` keys) |
| `Chr_00_0`: evidence / romExport / chrRom fill | 255 / 0 / 1 | 4 / 251 / 1 |
| `Chr_01_0`: evidence / romExport / chrRom fill | 243 / 0 / 13 | 14 / 229 / 13 |
| `Chr_FFFFFFFF_0` (blank bucket) | 32 evidence | 32 romExport |
| `--verify` | 868 -> 868, pass | 868 cells byte-identical, 868 -> 868, 0 errors, rebuilt `hires.txt` identical |

## E2E: Castlevania (CHR RAM)

This used the `castlevania-deep` pass-1 recording. The base branch's kit and
the fixed kit ran on the same pack.

- The real CHR RAM banks are unchanged. Both kits log "recorded 302 (59%),
  ROM fill 176, unrecoverable 34 -> 93% complete". `recorded`, `filled`,
  `unrecoverable` and the rule counts are identical.
- Every page PNG and `.orig.png` is byte-identical. `--verify` passes: 3 033
  cells byte-identical, 3 033 -> 3 033 keys, 0 errors, and the rebuilt
  `hires.txt` is identical.
- The sidecars change, for the same reason as on Excitebike. The 12
  `AddPrgScanTiles` pages (`Chr_4`..`Chr_14`, `Chr_19`) hold only `Y` rows,
  and they were green. Now they are `fill` / `romExport`. The fragment's
  `evidence` total goes 2 993 -> 433, and `fold.cellsToPaint` also goes
  2 993 -> 433.
- One drawn cell used to fold onto an export: `Chr_15` slot 37 folded onto
  PRG-scan cell `Chr_5`:183. It is now `evidence`, a cell to paint. So
  `folded` goes 40 -> 19, and the `folds` lists on `Chr_0` / `Chr_1` no longer
  name `0F001030` export cells.

## Tests

- `python3 scripts/test_artist_chr_kit.py`: 46/46 cases, including the 2 new
  tests.
- `python3 scripts/test_palette_folds.py`: 8/8.
- `make python-tests`: 59 passed, 0 failed, 0 skipped.
- `make doc-checks`: exit 0.
