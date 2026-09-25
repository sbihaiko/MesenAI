# ADR-0216: Who authors the cell around a copied tile key, and who places it

- Status: **accepted 2026-09-19**. The four OPEN questions below are
  answered (1(b), 2(a), 3(a), 4(a)); the answers are the decision. Selected
  by the user through the decision prompt, recorded verbatim as the labels
  they picked: *"Clipboard leva a célula, script a posiciona (Recomendado)"*,
  *"Por `cell.w` == 8 (Recomendado)"*, *"Crescer os dois arquivos numa
  operação só (Recomendado)"*, *"Avisar no momento do copy (Recomendado)"*.
  Implemented in the same turn as the acceptance, under the project's
  same-turn rule: the change ships with unit tests covering each of the four
  decisions, and the go-ahead is quoted verbatim both above and in the body
  of PR #348.
- **The same-turn rule holds.** This project allows implementing in the turn
  an ADR is accepted only when the change ships with unit tests covering the
  decision **and** the go-ahead is quoted verbatim in this Status line
  **and** in the PR body. All three are true: `UI.Tests/Mep/MepSheetCellTests.cs`
  and `UI.HeadlessTests/CopyAsMepSheetCellTests.cs` cover the payload,
  `scripts/test_mep_add_cell.py` (24 checks, wired into `make doc-checks`)
  covers sheet choice, slot arithmetic, the two-file grow and the claim
  report, and PR #348 carries the quote. What shipped under answer 1 of
  *Corrections as shipped*, below, is narrower than the sentence the Decision
  first carried.
- Date: 2026-09-19
- Related: issue #340 (the half still open — the clipboard carries a
  `tiles[]` entry, not a cell), ADR-0215 (the same action's key resolution
  and its OSD receipt — the half that shipped), ADR-0153 §4 (the sheet
  sidecar schema this would author into), ADR-0172 §2 (`tiles[].index`),
  ADR-0175 (`emptySlots[]` states a figure's shape; it is not scratch
  space), ADR-0209 Q4 (the `unsorted` remainder sheet), ADR-0178 §6 (only a
  sprite sheet may carry a flip-baked key), ADR-0214 (the cold-read protocol
  that measured the cost), PRD Phase 12 F12.2
- Supersedes / amends: nothing. OPEN 1 was answered (b), so no second writer
  of `cells[]` appears in the UI and ADR-0153 §4 stands as written — the new
  Python placer is a writer of the same kind as the tools already there, not
  a new surface.

## Context

`Copy as MEP sheet cell` is named for a cell and emits one `tiles[]` entry:

```json
{"tile": "0080FFFFFFFFFFFFFF7F0000FFFF0000", "palette": "0F202909", "index": 486}
```

ADR-0215 fixed what is *in* that object — the CHR bank it is read through,
the palette it is keyed under, and the on-screen receipt that says which
tile was taken. What it did not change is that the object is not a cell.
Between the clipboard and a painted square the artist still does four
things by hand, and the 2026-09-19 28-ROM cold read
(`docs/validation/f12.2-opus-sweep-2026-09-19.md`, finding 4) recorded a
stop on them in **every one of the 28 runs**, costing 30 s to 2 min each:

1. author the wrapper — `index`, `x`, `y`, `count`, `context`, and
   optionally `label`/`metatile`;
2. find a free slot by counting `cells[]` against `columns`, `cell` and
   `gutter`. A guessed slot silently repaints whatever lived there;
3. choose a sheet. The copy carries no `context`, and `context` could not
   route it anyway;
4. work out the pack's `scale`, which is stated only in `textures/hires.txt`,
   to know where on the PNG that `x,y` lands.

Four facts constrain any answer. All four are measured over the 30 packs the
sweep left in `runs/f12.2-sweep/packs/`.

**The `context` field routes nothing.** Of the cells on free-form sheets,
3 495 on `unsorted` sheets and 529 on `misc` sheets carry `"context":
"misc"` — every single one, on both kinds. 20 of the 30 packs ship both
files. What *does* separate them is the grid: `cell.w` is 8 on all 27
`unsorted` sheets and 16 on all 21 `misc` sheets. Two packs (Donkey Kong,
Zelda II) ship neither and have no free-form surface at all.

**Correction, measured while answering (2026-09-19).** `cell.w == 8` does
**not** identify the free-form sheet on its own, so option 2(b) as written —
"decidable without reading `kind`" — is false. Counting `cell.w` over all
1 171 sheets of the 30 packs: `sprite` is 8×8 on **719** sheets and `sprites`
on 28, against `unsorted` on 27. An 8×8 lookup that ignored `kind` would put
a background key on a sprite sheet, which is the one destination the same
option forbids. The rule is therefore `cell.w == 8` **among the sheets that
are not a named figure** (`sprite`, `sprites`, `object`, `font`, `hud`,
`map`), which resolves to `unsorted` on all 27 packs that have one; the
16×16 free-form sheet is `misc` (21 sheets), then `metatiles` (30).

**A free slot is often not there.** `cells[]` is dense and row-major, so the
free slots of a sheet are the tail of its last partial row. On the
`unsorted` sheet: **5 of 27 packs have zero** (Bubble Bobble, Golf, Pac-Man,
Tetris, The Flintstones) and **7 more have exactly one** (1942, Bomberman,
Double Dragon, Mario Bros., Ninja Gaiden, Super Mario Bros., Tennis). So
"the sheet is full" is not a corner case: it fires on the first copy for 5
games and on the second for 12.

**Growing a sheet is a two-file operation with no slack.** `mep_build`
derives the pack's `<scale>` by requiring every sheet PNG to be *exactly* an
integer multiple of the logical size its sidecar describes (`_sheet_scale`;
width from `columns`, height from the lowest cell), and `_EditedProbe`
requires the `*.orig.png` twin to be exactly 1/N of the PNG. Adding a cell
in a **new row** therefore invalidates both files at once:

- grow neither → `build` fails, `... is not an integer multiple of the LxH
  sheet ... describes`;
- grow only the `.png` → the twin no longer matches, the probe goes
  **blind**, and *every* cell of that sheet counts as painted. The static
  `_SHEET_RANK` then decides precedence for the whole sheet. That is silent,
  and it changes cells the artist never touched;
- grow only the `.orig.png` → back to the scale error.

Adding a cell in a free slot of the **existing last row** changes neither
file: the logical size is unchanged. That is the entire difference between
the cheap case and the expensive one.

**`emptySlots[]` is not scratch space.** ADR-0175 states blanks and
explicitly refuses to fill them: a group sheet's blanks are the shape of an
L-shaped or non-rectangular figure, and the slot belongs to that figure's
grid. `docs/remastering-a-game.md` read `emptySlots` as "the answer to 'where
do I put this'", which is a background key being written into a named
figure's hole; that sentence was corrected in the change that implements this
ADR, and the placer never offers a `sprite`/`object` sheet as a destination.

**What the emulator can reach today.** The sidecars ship *inside* a
deployed pack — `mep_build pack` zips the folder unfiltered and `mep_lint`
reads `sheets/` back out of it — so a running emulator is not structurally
cut off from them. It is, however, not wired: `MepPack::RootFolder` and
`MepPackManager::GetSectionPath` have no `DllExport`, so the UI can reach
`EmuApi.GetMepSiblingFolder()` and
`ConfigManager.EnhancementPackFolder / <ContainerName>` and nothing more
precise. Any option that reads the artist's sidecars pays for one new
export, plus a JSON reader and a PNG writer in the UI.

### Non-goals

- Changing `textures/hires.txt`, the sheet schema's field names, or the two
  hex fields `MepSheetCell.Format` already emits.
- Embedding the Python toolchain in the UI (PRD Phase 12's own non-goal
  list).
- Anything about *which* tile is copied. ADR-0215 owns that and is not
  reopened here.
- Picking the art. The tool places a cell; it never paints one.

## Decision

Answered 2026-09-19. The options each question weighed are kept below as the
record of what was rejected; the answers are:

1. **1(b) — the clipboard carries the cell unplaced, and a new
   `scripts/mep_add_cell.py` places it.** The emulator never writes into the
   artist's tree, and the pack edit sits in the toolchain, beside every other
   pack edit, where the schema tests already are.
2. **2(a) — the free-form one-tile sheet, chosen by `cell.w == 8` among the
   sheets that are not a named figure**; failing that the 16×16 free-form
   sheet; failing that `metatiles`. See the correction below: `cell.w` alone
   does not identify the sheet, because sprite sheets are 8×8 too.
3. **3(a) — grow.** The sheet gains a row, and `<sheet>.png` and
   `<sheet>.orig.png` are rewritten together in one operation, the cell
   emitted only if both writes succeeded.
4. **4(a) — say it before the build, not three steps later.** The report
   names the sheet that already holds the key and whether that cell is
   painted. Where it is said follows from answer 1, and the option text above
   assumed 1(a): under **1(b)** the sidecar reader is `mep_add_cell.py`, not
   the viewer, so the report lands in the placer's output at paste time. The
   viewer's own receipt stays what ADR-0215 made it — the tile identity the
   emulator can see without opening the artist's tree — because 1(b) exists
   precisely so the emulator never reads it. Paste time is still before the
   build, which is what the question was about.

A key copied from the **Sprite Viewer** is out of scope for the placer: it
refuses a sprite-sourced key rather than guessing a flip-baked destination
(ADR-0178 §6).

The one trap — `index` meaning two different things in a whole-cell payload —
is the placer's to avoid, not the artist's: `mep_add_cell.py` sets the cell's
`index` itself and carries `tiles[].index` through untouched, and the guide
states which is which.

### Corrections as shipped (2026-09-19, measured while implementing)

Three things the Decision above could not have known, and one it got wrong.
None changes an answer; each narrows what the answer means in code.

- **The Sprite Viewer refusal is not implementable as written.** The Decision
  said the placer "refuses a sprite-sourced key", but under 1(b) the clipboard
  carries no record of which viewer the copy came from, and adding one would
  break the payload's being a paste-ready `cells[]` entry. The only signal
  present is the palette — a sprite's transparent colour 0 is packed `FF` —
  and it is **not a clean marker**: 131 of 3 495 `unsorted` cells and 340 of
  13 932 `metatiles` cells carry an FF-leading palette. Shipped as a refusal
  with an explicit `--allow-sprite-palette` override, so the ~3.7 % of
  legitimate background cells are a speed bump rather than a wall. A clean
  fix needs either a clipboard marker or a decision to drop the refusal.
- **The grow's fill colour was unspecified.** OPEN 3 said "append one row" and
  not what it is filled with. It is the image's own top-left pixel, which on
  every generated sheet is the gutter.
- **A 16-pixel-tall sprite copies as two cells, not one cell with two
  `tiles[]` entries.** `mep_build._cell_crops` lays a cell's entries out
  row-major 2×2, so a second entry would draw to the *right* of the first
  rather than below it. The action already emitted the two halves as two
  objects; the placer reads them as two cells.
- **The Context section's complaint about `emptySlots` was already stale when
  it was written** — it is corrected in `docs/remastering-a-game.md`, in the
  same change that this ADR's Consequences describe. The register now says so
  instead of claiming otherwise.

### OPEN 1 — what lands on the clipboard

- **(a) A whole cell, already placed.** The viewer reads the loaded pack's
  `textures/sheets/*.json`, picks the sheet (OPEN 2), picks the free slot
  (OPEN 3), and puts one JSON object on the clipboard — `index`, `x`, `y`,
  `count`, `context`, `tiles[]` — while the receipt names the file to paste
  it into. Retires all four hand steps, and the name becomes true.
  Costs: a new interop export, a sidecar reader in the UI, and a clipboard
  payload that is only valid for the one file named in a toast the artist
  may already have dismissed.
- **(b) A whole cell, unplaced, plus a placer.** The clipboard carries
  `count` and `tiles[]` and omits `index`/`x`/`y`; a new
  `scripts/mep_add_cell.py <pack>` reads the clipboard text, picks the sheet
  and the slot, writes the cell and grows the PNGs when it must. The
  emulator never opens the artist's files. Same arithmetic retired, one
  command later, and the pack edit sits where every other pack edit sits.
- **(c) The action writes the cell itself.** The clipboard becomes a
  confirmation, not the payload. Shortest path for the artist; makes the
  emulator an editor of a source tree it currently only reads, and makes a
  right-click a filesystem write with no undo.
- **(d) Rename and stop.** `Copy MEP tile key`, and the cell stays the
  guide's job. Costs one string and one guide edit, fixes the naming half of
  #340 and none of the workflow half. The 28 stops stay.

### OPEN 2 — which sheet a copied key goes on

Only live under (a), (b) or (c).

- **(a) By grid, then by kind.** A single 8×8 key goes to the free-form 8×8
  sheet (`cell.w == 8` — all 27 `unsorted` sheets); failing that, the 16×16
  free-form sheet (`misc`); failing that, `metatiles`. Never a
  `sprite`/`sprites` sheet for a background key, and never an
  `object`/`font`/`hud` sheet, each of which is one named thing. This is the
  rule the guide already states and the one all 28 runs reconstructed by
  hand; `cell.w` makes it decidable without reading `kind`.
- **(b) By `kind` alone**, in the guide's table order.
- **(c) Refuse to choose**: the receipt lists the candidate files and the
  artist picks. Honest, retires nothing.

Sub-question under any of them: a key copied from the **Sprite Viewer**.
ADR-0178 §6 allows a flip-baked key only on a sprite sheet, so either a
sprite copy routes to the sprite sheets by a rule of its own, or the action
refuses to place sprite keys and places background keys only.

### OPEN 3 — what happens when the sheet has no free slot

Fires on 5 of 27 packs at the first copy, 12 at the second.

- **(a) Grow.** Append one row: rewrite `<sheet>.png` and `<sheet>.orig.png`
  together, in one step, at exactly N:1, and emit the cell only if both
  writes succeeded. Correct and invisible when it works; a partial write
  blinds `_EditedProbe` for the whole sheet, which is the one failure mode
  in this area that is both silent and wide.
- **(b) Refuse, with the arithmetic.** The receipt says the sheet is full
  and states what it would take: `unsorted.png is full (20 cells, 5 columns,
  4 rows) — grow it to 5 rows and copy again`. Nothing is written, nothing
  can be corrupted, and the artist keeps a step.
- **(c) Emit the cell at the next row's `x,y` anyway** and let `build` fail
  with its existing size error. Cheapest to implement; hands the artist a
  payload that is known not to build.

### OPEN 4 — a key another sheet already claims

`build` already reports this (the override line, and #343's "already
claimed by another crop"), but only two steps later.

- **(a) Say it at copy time.** The reader from OPEN 1(a)/(b) is already
  walking the sidecars, so the receipt can name the sheet that holds the key
  and whether that cell is painted.
- **(b) Leave it to `build`**, which is where it is today.

### One trap to settle whichever way this goes

A whole-cell payload carries the word `index` **twice** with two meanings:
the cell's ordinal in its sheet, and — inside `tiles[]` — the tile's
absolute CHR index (ADR-0172 §2). The values differ, both are small
integers, and an artist who copies the wrong one gets a cell at a slot that
already exists. Any option that emits a whole cell should state which is
which in the receipt or in the guide.

## Consequences

- **#340 is closed by this.** The receipt shipped (ADR-0215); the cell now
  ships with it. The sweep's finding 4 was the one line item that cost all 28
  runs, and the four hand steps it named — the wrapper, the free slot, the
  sheet, the `scale` — are the placer's now. What is *not* claimed: that the
  28 runs would each have been faster by a measured amount, because the
  protocol has not been re-run since.
- **The action does not stop being a copy.** It puts an unplaced cell on the
  clipboard and keeps emitting the `tiles[]` entry inside it, so a run that
  pastes the payload into a sheet by hand still works exactly as it does
  today; the placer is the shorter path, not the only one.
- **`mep_add_cell.py` is a new writer of `cells[]`,** beside `SheetRender.cpp`
  and the existing Python tools, so ADR-0153 §4 gets the shared-schema test
  the option analysis asked for: the same cell document must round-trip
  through `mep_build`. Growing is the piece that most needs it — 3(a) is the
  only option whose failure mode is silent and wide (#346), so a half-grown
  pack has to be refused rather than written.
- **`docs/remastering-a-game.md` was edited with this ADR**, in the same
  change: the `emptySlots` row contradicted ADR-0175 and is corrected, and
  *Which sheet a copied key goes on* states the rule above and names the
  placer.
- Whatever is picked ships with tests that fail on today's behaviour —
  `UI.Tests/Mep/` for the host-free half, `UI.HeadlessTests/` for the
  clipboard and the receipt, `scripts/test_mep_build.py` for a grown sheet's
  round trip. Growing a sheet in particular needs a test that a half-grown
  pack is refused rather than silently blinding the probe.
