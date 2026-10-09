# ADR-0216: Who authors the cell around a copied tile key, and who places it

- Status: accepted (2026-09-19; the answers are OPEN 1(b), OPEN 2(a), OPEN 3(a), OPEN 4(a); tests `UI.Tests/Mep/MepSheetCellTests.cs`, `UI.HeadlessTests/CopyAsMepSheetCellTests.cs` and `scripts/test_mep_add_cell.py` (24 checks at acceptance, 32 since #503; wired into `make doc-checks`) cover the payload, sheet choice, slot arithmetic, the two-file grow and the claim report; go-ahead verbatim: *"Clipboard leva a célula, script a posiciona (Recomendado)"*, *"Por `cell.w` == 8 (Recomendado)"*, *"Crescer os dois arquivos numa operação só (Recomendado)"*, *"Avisar no momento do copy (Recomendado)"*).
- Date: 2026-09-19
- Related: issue #340 (the half still open — the clipboard carries a `tiles[]` entry, not a cell), ADR-0215 (the same action's key resolution and its OSD receipt — the half that shipped), ADR-0153 §4, ADR-0172 §2, ADR-0175, ADR-0209 Q4, ADR-0178 §6, ADR-0214, PRD Phase 12 F12.2
- Amends: nothing. OPEN 1 was answered (b), so no second writer of `cells[]` appears in the UI; ADR-0153 §4 stands as written.

## Decision

`Copy as MEP sheet cell` is named for a cell but emits one `tiles[]` entry:

```json
{"tile": "0080FFFFFFFFFFFFFF7F0000FFFF0000", "palette": "0F202909", "index": 486}
```

The four facts below (all measured over the 30 packs in `runs/f12.2-sweep/packs/`) drove the answers, picked by the user through the decision prompt on 2026-09-19:

1. **OPEN 1(b) — the clipboard carries the cell unplaced, and a new `scripts/mep_add_cell.py` places it.** `scripts/mep_add_cell.py <pack>` reads the clipboard text, picks the sheet and the slot, writes the cell and grows the PNGs when it must. The emulator never writes into the artist's tree, and the pack edit sits in the toolchain, beside every other pack edit, where the schema tests already are.
2. **OPEN 2(a) — the free-form one-tile sheet, chosen by `cell.w == 8` among the sheets that are not a named figure**, then the 16×16 free-form sheet (`misc`), then `metatiles`. See the correction: `cell.w` alone does not identify the sheet, because `sprite` sheets are 8×8 too.
3. **OPEN 3(a) — grow.** To append one row: rewrite `<sheet>.png` and `<sheet>.orig.png` together in one operation, the cell emitted only if both writes succeeded.
4. **OPEN 4(a) — say it before the build, not three steps later.** The report names the sheet that already holds the key and whether that cell is painted. Under 1(b) the sidecar reader is `mep_add_cell.py`, not the viewer, so the report lands in the placer's output at paste time; the viewer's receipt stays what ADR-0215 made it. Paste time is still before the build.

A key copied from the **Sprite Viewer** is out of scope for the placer: it refuses a sprite-sourced key rather than guessing a flip-baked destination (ADR-0178 §6). The one trap — `index` meaning two different things in a whole-cell payload — is the placer's to avoid, not the artist's: `mep_add_cell.py` sets the cell's `index` itself and carries `tiles[].index` through untouched, and the guide states which is which.

## Context

Between the clipboard and a painted square the artist did four things by hand, and the 2026-09-19 28-ROM cold read (`docs/validation/slices/f12.2-opus-sweep-2026-09-19.md`, finding 4) recorded a stop on them in **every one of the 28 runs**, costing 30 s to 2 min each: author the wrapper (`index`, `x`, `y`, `count`, `context`, optionally `label`/`metatile`); find a free slot by counting `cells[]` against `columns`, `cell` and `gutter` (a guessed slot silently repaints whatever lived there); choose a sheet; work out the pack's `scale`, stated only in `textures/hires.txt`.

**The `context` field routes nothing.** Of the cells on free-form sheets, 3 495 on `unsorted` sheets and 529 on `misc` sheets carry `"context": "misc"` — every single one, on both kinds. What separates them is the grid: `cell.w` is 8 on all 27 `unsorted` sheets and 16 on all 21 `misc` sheets. But `cell.w == 8` does **not** identify the free-form sheet on its own, so 2(b) as written — "decidable without reading `kind`" — is false: over all 1 171 sheets, `sprite` is 8×8 on 719 and `sprites` on 28, against `unsorted` on 27. The rule is therefore `cell.w == 8` **among the sheets that are not a named figure** (`sprite`, `sprites`, `object`, `font`, `hud`, `map`).

**A free slot is often not there.** `cells[]` is dense and row-major, so a sheet's free slots are the tail of its last partial row. On the `unsorted` sheet 5 of 27 packs have zero and 7 more have exactly one, so "the sheet is full" fires on the first copy for 5 games and on the second for 12.

**Growing a sheet is a two-file operation with no slack.** `mep_build` derives the pack's `<scale>` by requiring every sheet PNG to be exactly an integer multiple of the logical size its sidecar describes (`_sheet_scale`), and `_EditedProbe` requires the `*.orig.png` twin to be exactly 1/N of the PNG. Adding a cell in a **new row** invalidates both at once: grow neither and `build` fails on the integer-multiple check; grow only the `.png` and the twin no longer matches, the probe goes **blind**, every cell of that sheet counts as painted, and the static `_SHEET_RANK` decides precedence for the whole sheet (silent, and it changes cells the artist never touched); grow only the `.orig.png` and the scale error returns. Adding a cell in a free slot of the **existing last row** changes neither file — the whole difference between the cheap case and the expensive one.

**`emptySlots[]` is not scratch space.** ADR-0175 states blanks and explicitly refuses to fill them: a group sheet's blanks are the shape of a non-rectangular figure, and the slot belongs to that figure's grid. `docs/remastering-a-game.md` read `emptySlots` as the answer to "where do I put this", which is a background key written into a named figure's hole; that sentence was corrected in the change that implements this ADR, and the placer never offers a `sprite`/`object` sheet as a destination.

**What the emulator can reach today.** The sidecars ship inside a deployed pack (`mep_build pack` zips the folder unfiltered and `mep_lint` reads `sheets/` back out), so a running emulator is not structurally cut off — but it is not wired: `MepPack::RootFolder` and `MepPackManager::GetSectionPath` have no `DllExport`, so the UI can reach `EmuApi.GetMepSiblingFolder()` and `ConfigManager.EnhancementPackFolder / <ContainerName>` and nothing more precise. Any option that reads the artist's sidecars pays for one new export, plus a JSON reader and a PNG writer in the UI.

Non-goals: changing `textures/hires.txt`, the sheet schema's field names, or the two hex fields `MepSheetCell.Format` already emits; embedding the Python toolchain in the UI; anything about *which* tile is copied (ADR-0215 owns that and is not reopened); picking the art (the tool places a cell, it never paints one).

## The four OPEN questions and what was rejected

### OPEN 1 — what lands on the clipboard

- **(a) A whole cell, already placed.** The viewer reads the load's `textures/sheets/*.json` and puts one object on the clipboard; costs a new interop export, a sidecar reader in the UI, and a payload valid only for the file named in a toast.
- **(b) A whole cell, unplaced, plus a placer.** **Chosen.** The clipboard carries `count` and `tiles[]` and omits `index`/`x`/`y`.
- **(c) The action writes the cell itself.** Shortest for the artist; makes a right-click a filesystem write with no undo.
- **(d) Rename and stop.** `Copy MEP tile key`, and the cell stays the guide's job; fixes the naming half of #340 and none of the workflow half, so the 28 stops stay.

### OPEN 2 — which sheet a copied key goes on

Only live under (a), (b) or (c).

- **(a) By grid, then by kind.** **Chosen** (see the correction: `cell.w` alone does not identify the sheet).
- **(b) By `kind` alone**, in the guide's table order.
- **(c) Refuse to choose**: the receipt lists the candidates and the artist picks.

Sub-question: a key copied from the **Sprite Viewer**. ADR-0178 §6 allows a flip-baked key only on a sprite sheet, so either a sprite copy routes to the sprite sheets by its own rule, or the action refuses to place sprite keys and places background keys only.

### OPEN 3 — what happens when the sheet has no free slot

Fires on 5 of 27 packs at the first copy, 12 at the second.

- **(a) Grow.** **Chosen.** Append one row: rewrite `<sheet>.png` and `<sheet>.orig.png` together at exactly N:1, and emit the cell only if both writes succeeded.
- **(b) Refuse, with the arithmetic.** The receipt says the sheet is full and states what it would take: `unsorted.png is full (20 cells, 5 columns, 4 rows) — grow it to 5 rows and copy again`.
- **(c) Emit the cell at the next row's `x,y` anyway** and let `build` fail with its size error.

### OPEN 4 — a key another sheet already claims

`build` already reports this, but only two steps later.

- **(a) Say it at copy time.** **Chosen.**
- **(b) Leave it to `build`**, which is where it is today.

## Record

- **2026-09-19 — corrections as shipped.** The Sprite Viewer refusal is not implementable as written: under 1(b) the clipboard carries no record of the viewer the copy came from, and the only signal (a sprite's transparent color 0 packed `FF`) is not a clean marker — 131 of 3 495 `unsorted` cells and 340 of 13 932 `metatiles` cells carry an FF-leading palette — so it shipped as a refusal with an explicit `--allow-sprite-palette` override. The grow's fill color was unspecified: it is the image's own top-left pixel, which on every generated sheet is the gutter. A 16-pixel-tall sprite copies as two cells, not one cell with two `tiles[]` entries, because `mep_build._cell_crops` lays a cell's entries out row-major 2×2; the placer reads them as two cells. The Context's complaint about `emptySlots` was already stale when written, and is corrected in `docs/remastering-a-game.md`.
- **2026-09-25 (#503) — `cells[]` is not always dense and row-major.** The 16-game F14.2 retest falsified reading `len(cells)` as the next ordinal's slot: Mario Bros.'s `unsorted` holds 30 cells at `index` 0..29 while rows 1 and 2 carry only columns 0..2 of 5, so the slot after the last cell, `x1,y55`, is cell 20's own; `mep_add_cell.py` grew `unsorted.png` 184x256 -> 184x292 against a logical height of 64 and `mep_build` exited 2. **OPEN 3(a) is unchanged** — the sheet still grows one row when it must, both files with it — but *when it must* is read from the `x,y` no cell claims, taken first in row-major order, never from `len(cells)`. A hole in a row `mep_build._logical_size` already describes is a free slot like any other and grows nothing. The placer's `grid()`/`free_slot()` carry it, and `docs/remastering-a-game.md` states the same in *Find a free slot, do not guess one*.

## Consequences

- **#340 is closed by this.** The receipt shipped (ADR-0215); the cell now ships with it. The four hand steps the sweep's finding 4 named are the placer's now. Not claimed: that the 28 runs would each have been faster by a measured amount, because the protocol has not been re-run since.
- **The action does not stop being a copy.** It puts an unplaced cell on the clipboard and keeps emitting the `tiles[]` entry inside it, so a hand paste into a sheet by hand still works exactly as today; the placer is the shorter path, not the only one.
- **`mep_add_cell.py` is a new writer of `cells[]`,** beside `SheetRender.cpp` and the existing Python tools, so ADR-0153 §4 gets the shared-schema test: the same cell document must round-trip through `mep_build`. Growing is the piece that most needs it — 3(a) is the only option whose failure mode is silent and wide (#346) — so a half-grown pack has to be refused rather than written.
- **`docs/remastering-a-game.md` was edited with this ADR**: the `emptySlots` row contradicted ADR-0175 and is corrected, and *Which sheet a copied key goes on* states the rule and names the placer.
- Tests that fail on today's behavior ship with it — `UI.Tests/Mep/` for the host-free half, `UI.HeadlessTests/` for the clipboard and the receipt, `scripts/test_mep_build.py` for a grown sheet's round trip.
