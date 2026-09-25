# Issue #511 — the build prints the rule each cell the artist touched produced (2026-09-25)

Scope: issue #511, the tooling half of the F14.2 cold read's criterion 4. Four
of the five Grok 4.5 evaluators (1942, Excitebike, Lemmings, Ice Climber,
Castlevania) ran `grep`/`cat`/`head` on `textures/hires.txt` after
`mep_build.py build` to confirm the key they pasted had landed as a `<tile>` at
the crop they painted. Any read of that file is a criterion-4 fail, and three
of the four then logged "never opened" — they believed a post-build check was
allowed. The root cause is tooling: nothing the artist is told to run prints
which `<tile>` line the cell produced, so the only way to look was to open the
manifest. `docs/adr/0214` §4 makes criterion 5 ("the evaluator's key must come
back as a `<tile>` at the painted crop") mechanical, and this is what lets the
evaluator read that answer off the build's own output.

The fix has two halves.

1. **`mep_build build` reports the cells the artist touched.** After its
   `built …` line it prints one `report:` row per key of every sheet cell whose
   pixels differ from its `*.orig.png` twin, and per key of every cell a tool
   marked with `addedBy` — the marker `mep_add_cell.py` now writes on the cell
   it places, which is the only way to name a pasted cell *before* it is
   painted (an unpainted cell equals its twin, so paint alone cannot identify
   it). A row names the sidecar, the cell's `index`, the key as the emission
   loop keyed by, the painted crop in sheet pixels, and the `<tile>` line the
   build wrote for it, verbatim. `REPORT_CAP` (20) bounds the output, then
   `report: ... and N more`.
2. **The guides point at that row.** `docs/remastering-a-game.md` gains *The
   report: which rule your cell produced* (with the interaction ADR-0231
   creates: a row whose `<tile>` x,y is not the row's crop means the cell is
   untouched and kept its recorded rule), the paste section says what the
   marker is for, the cell field table documents `addedBy`, and the sentence
   that used to invite a read (`Reading hires.txt is fine`) now says the
   opposite. `docs/hd-pack-authoring.md` step 4 and the `ARTIST.md` steps
   `scripts/artist_kit_assemble.py` writes say the same, and the F12.2
   evaluator briefings and the panel script now define criterion 5 as the
   build's row instead of a read (§4b).

Where it lives: `report_cells` in `scripts/mep_build.py` and the marker in
`scripts/mep_add_cell.py`. `mep_build.py` is at **2065 lines of its ADR-0137
ceiling of 2070**; the ceiling was not raised and no module was added.

## 1. Unit tests: red before the fix

`scripts/test_mep_build_recorded.py` gains `cell_rule_report_test`, which
builds a synthetic recorded pack (`test_mep_build.py`'s fixtures) and pins the
four behaviours: an unpainted build prints no rows; painting one four-tile cell
prints one row per key with the sheet, the cell ordinal, the crop and the
`<tile>` text the manifest itself carries; a cell `mep_add_cell.py` placed is
reported before any paint, carrying the recorded rule ADR-0231 emits for it;
and a wholesale repaint is capped. `scripts/test_mep_add_cell.py` asserts the
marker is written.

With the report call and the marker removed — the state before the fix:

```
$ python3 scripts/test_mep_build_recorded.py   # exit code: 1
FAIL: #511: a cell mep_add_cell added is not reported:
FAIL: #511: painting one four-tile cell printed 0 row(s) and 0 more, expected 4
FAIL: #511: a wholesale repaint printed 0 row(s) and 0 more, expected 20 + 8
$ python3 scripts/test_mep_add_cell.py         # exit code: 1
FAIL: the placed cell carries no addedBy marker: None
```

Full output in `runs/511/red.txt`.

## 2. Green

```
$ python3 scripts/test_mep_build_recorded.py   # exit code: 0
PASS: #511: a build where no cell was painted prints no cell report
PASS: #511: one row per key of a painted cell — sheet, cell index, key, crop and the <tile> line itself
PASS: #511: a cell mep_add_cell placed is reported before it is painted, with the rule it produced
PASS: #511: the report is capped at 20 rows and counts the rest ("... and 8 more")
$ python3 scripts/test_mep_add_cell.py         # exit code: 0
```

Full output in `runs/511/green.txt`. The whole Python suite:
`scripts/checks/run_python_tests.sh` → **62 passed, 0 failed, 0 skipped**,
exit 0 (`runs/511/suite.txt`). One intermediate run failed
`test_compose_editor_gui.py` with exit 138 while a second `make doc-checks`
was compiling `headless_record` in parallel — the known SIGBUS flake of a
loaded machine, not this change: run alone it is 68/68, exit 0.

## 3. Mutation: drop the report and the tests fail

Replacing the `report_cells(...)` call with `pass`:

```
$ python3 scripts/test_mep_build_recorded.py   # exit code: 1
FAIL: #511: painting one four-tile cell printed 0 row(s) and 0 more, expected 4
FAIL: #511: a cell mep_add_cell added is not reported:
FAIL: #511: a wholesale repaint printed 0 row(s) and 0 more, expected 20 + 8
```

Full output in `runs/511/mutation.txt`; the call was restored and the file's
hash re-checked.

## 4. Five real packs: the row against the scorer

The sandbox's `games/` copies had been emptied when this ran, so the packs are
the evaluators' own `mep/` folders as left after each run
(`~/retest16/runs/retest16/grok45/<game>/mep-left`), copied into
`runs/511/e2e/` — the originals were never written to. Each copy was rebuilt
with the fixed tool, and the report row is compared against that game's
`score.txt`, written by `scripts/f122_score_panel.py` (the mechanical oracle of
criterion 5, which finds the painted magenta block and the `<tile>` at it).

| Game | Scorer's `painted_crop` | Report row's `<tile>` | Scorer's `roundtrip_line` | Same? |
|---|---|---|---|---|
| 1942 (Capcom) | (40, 40) | `<tile>0,0117,0F202020,40,40,1,N` | `<tile>0,0117,0F202020,40,40,1,N` | yes |
| Excitebike | (4, 72) | `<tile>0,012E,29271836,4,72,1,N` | `<tile>0,012E,29271836,4,72,1,N` | yes |
| Lemmings | (276, 548) | `<tile>0,0872,0F0B2528,276,548,1,N` | `<tile>0,0872,0F0B2528,276,548,1,N` | yes |
| Ice Climber | (148, 328) | `<tile>0,0132,0F302101,148,328,1,N` | `<tile>0,0132,0F302101,148,328,1,N` | yes |
| Castlevania | (76, 400) | `<tile>0,0002FD210105010000FCFEFEFEFEFEFE,0F302616,76,400,1,N,1445542316,107` | `[obj_nearby173]<tile>…` (same key, crop and trailing fields) | yes, bare twin |

The 1942 row in full, which is the line the guide now quotes:

```
$ python3 scripts/mep_build.py build runs/511/e2e/1942__1985___Capcom_    # exit code: 0
report: 1 key(s) from the sheet cell(s) you painted or added:
report: sheets/unsorted.json cell 3 painted — tile 0117 palette 0F202020 at crop 40,40: <tile>0,0117,0F202020,40,40,1,N
```

Castlevania is the one difference and it is the conditioner: the evaluator's
key is gated by an inherited `[obj_nearby173]` rule, so the manifest carries
that rule *and* its bare twin (ADR-0189 §3) at the same crop. The row shows the
bare twin — the rule that applies unconditionally, and the one a paste is
really asking about. The scorer reports the first line it finds at the crop,
which is the conditional one. Same key, same crop, same trailing fields.

Two more things the five packs show, both left alone because they are the
packs' own state:

- **Lemmings' cell 36 is a 16x16 cell**, so it emits four keys and the report
  prints four rows, all "at crop 276,548" — the cell's own top-left, which is
  where the evaluator painted. Each `<tile>` line carries that key's own
  crop inside the cell (276,548 / 308,548 / 276,580 / 308,580). A fifth row is
  cell 68, a different cell the pack puts at the same rectangle.
- **Ice Climber's `unsorted.json` has two cells at the same x,y** (58 and 85,
  both at 37,82), so the one painted rectangle produces two rows, one per
  cell's key. That is the pack, not the report: the sidecar really does carry
  two cells in one slot, and the report is the first place it is visible.

The scorer's `cell_index_at_crop` (`279` on 1942) is the painted tile's **CHR
index**, not the cell ordinal — 0x117 — so a reader comparing the row's `cell
3` with it is comparing two different numbers. The row's `index` is the
sidecar's cell ordinal, which is what `mep_add_cell.py` prints at paste time.

## 4b. The briefings stop asking for the forbidden read

The tool half is worthless if the document the evaluator is handed still
defines criterion 5 as *"the copied key appears in the rebuilt
`textures/hires.txt`"* — that sentence is an instruction to open the file
criterion 4 forbids, and it is why three of the five F14.2 runs logged the
read and then "never opened". The F12.2 briefings and the panel script now
say criterion 5 is read off the build's own `report:` row:

- `docs/validation/f12.2-sweep-evaluator-briefing.md` (the F14.2 briefing):
  the "Who you are" paragraph says there is no post-build exception; goal
  step 4 says the build prints the row and that it is where the round trip is
  read; the end-of-run list asks for that row verbatim; criterion 5's pass
  condition is the row naming a `<tile>` at the painted crop with the pasted
  key.
- `docs/validation/f12.2-fable-evaluator-briefing.md` (the Fable-era
  briefing) gets the same four edits.
- `docs/validation/f12.2-copy-sheet-cell-panel-script.md` (the dispatcher
  script): criterion 5's row, P13 (the build's output already carries the
  row), P15 (log it verbatim), and a paragraph under *Why criterion 5 is
  written against `build`* naming `report_cells` and saying the scorer's own
  manifest read is the machine's, not the evaluator's.
- `scripts/f122_score_panel.py` and `scripts/replay_f122_panel.py` keep
  reading `hires.txt` — they are the mechanical oracle — but now say so
  explicitly and name the build row as the evaluator's half.

Criterion 4 is untouched everywhere: any read of `hires.txt` is still a fail,
and every guide that mentioned the read as an option now points at the row
instead.

## 5. What the report deliberately does not do

- **No rows for a sheet it cannot judge.** A sheet whose `*.orig.png` twin is
  missing or unreadable gets none: every cell of it counts as painted, so a row
  per cell would be the whole sheet, and the build already prints the `info:`
  line saying the probe is blind. A map sheet gets none either — its crops come
  from `placements[]`, so there is no cell ordinal to name.
- **No rows for a legacy 16-column sheet.** It has no sidecar cells and no twin
  to compare against; every cell of it is "painted" by construction.
- **Ownership is exact, not geometric.** A row only shows a rule the cell
  itself owns: the winning entry must sit in the cell's own slot *and* be cut
  from a crop the cell produced. Two sheets of one pack can put a shape at the
  same x,y (1942's `metatiles` and `obj000` do), so a crop alone would not
  tell them apart. A key another crop took prints
  `no <tile> — this key produced no rule for this cell (#343)` instead of the
  winner's line, which is the answer the artist needs for a lost paint.
- **The key is printed as the emission keyed by it** — ADR-0172's CHR index
  token on a CHR ROM pack (1942 and Lemmings above), else ADR-0178's un-baked
  `source`, else the sidecar's `tile`. On a CHR ROM pack the sidecar's own
  32-hex `tile` therefore does not appear in the row, while the `<tile>` line
  beside it does carry the index the manifest is keyed by.
