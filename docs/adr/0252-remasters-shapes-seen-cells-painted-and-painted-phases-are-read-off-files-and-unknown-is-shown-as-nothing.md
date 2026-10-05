# ADR-0252: Remaster's shapes seen, cells painted and painted phases are read off the files the tools write, and an unknown count is shown as nothing

- Status: accepted (2026-10-03). The user asked for this ADR verbatim:
  *"ADR definindo os três"*. Implemented the same day under the session's
  go-ahead, quoted verbatim: *"implemente o que falta na GUI e corrija os
  bugs. mergeie tudo no main. limpe os brantches e WTs. Use o maximio de
  paralelismo que puder."* Same-turn implementation is allowed by CLAUDE.md
  because the change ships with unit tests covering every section below:
  `UI.Tests/Remaster/RemasterCountsTests.cs` (the rules in
  `UI/Logic/RemasterCounts.cs`, the kit reader's play order, the popover
  line, the screens cap), with the wiring in
  `UI.HeadlessTests/RemasterThemeRenderTests.cs`,
  `RemasterTileBrowserTests.cs`, `RemasterRecentProjectsRenderTests.cs` and
  `RemasterWorkspaceTests.cs`. One surface is not wired: the shell status
  line (Consequences).
- Date: 2026-10-03
- Related: PRD Part B §13.5.3 (W-R0b, W-R1, W-R2, W-R5), ADR-0241 (the
  Remaster workspace), ADR-0243 (the project and its `auto/rec-NNN`),
  ADR-0183 §2/§3 (the kit and its fragments), ADR-0153 §3 (sheet sidecars
  and the `*.orig.png` twin), ADR-0179 (runs and phases), ADR-0194 §2/§4
  (cell identity, coverage as a union), ADR-0219 (pages: seen/fill/empty),
  ADR-0225 §2 (the figure view and its sidecar), ADR-0239 §4 (drawn keys
  exclude the `defaultTile` placeholders)

## Context

The Remaster renders show numbers the app either did not show or showed without a definition:

| Render | Text | Before this ADR |
|---|---|---|
| W-R1 zone ① | "1 240 shapes seen while you played" | not shown (the line showed the newest recording) |
| W-R2 pill | "318 new shapes · 2 screens captured" | shown from the core's live coverage report, undefined |
| W-R5 popover | "Painted: 2 of 6 phases" | a yes/no "Painted" for the whole picture |
| W-R0b rows, W-R1/W-R5 status line | "412 cells painted" | not shown |

The other numbers on those renders are already defined by a file or a job and are out of scope: the recordings count (ADR-0243), the category chips and a tile's "N phases"/"N cells" (the kit fragment, G.7), "12 of 64 cells not seen" (ADR-0219's `fill`), the W-R3 step counter and W-R4's problem count (the job's own output), and "2 files changed since the last build" (G.6). Every number here could be guessed from something nearby — a count of `<tile>` rows, the share of a sheet that differs from its twin, one recording's live counter — and each guess is wrong on real data in a way the artist cannot see: a bootstrapped `hires.txt` holds the whole ROM export, so its row count is not what was played; a figure grid has gutters, so a pixel count is not a cell count; a live counter dies with its recording. Non-goals: a new pipeline or a new file. Every count below is read off files the recorder and the kit already write; a count no file supports stays unknown.

## Decision

A count is either read off the files named here, or it is **unknown**. An unknown count shows no words at all: its clause is left out, and it is never shown as 0. A known 0 has its own words. The rules are host-free in `UI/Logic/RemasterCounts.cs`.

### 1. "N shapes seen while you played" (W-R1 zone ①)

- **Counted:** the distinct tile shapes the project's recordings drew. A shape is the `tileData` field (the second field) of a `<tile>` row, upper-cased, with the palette folded away — the identity the core's `HdTileKey::GetKey(true)` uses for the W-R2 live counter. Only rows whose `defaultTile` field (the seventh) is `N` count; a `Y` row is the ROM export the bootstrap recorder seeds (`AddRomTiles`/`AddPrgScanTiles`; `artist_chr_kit.TileRow.exported`, #449). A row with a `[condition]` prefix counts like a plain one; a row too short to carry the seventh field does not.
- **From:** `auto/rec-NNN/textures/hires.txt` of every recording `RemasterProjectReader` lists (a bare `auto/textures` is `rec-001`). The project count is the **union** over recordings (ADR-0194 §4), so a shape two recordings drew counts once.
- **Updates:** on every W-R1 refresh; a file is re-read only when its size or modification time changes (`RemasterShapeCache`). A new recording adds to the count once its `hires.txt` is written, at Stop.
- **Unknown:** when no recording has a readable `textures/hires.txt` (an audio-only project, or no recording); the line then falls back to the newest recording ("Latest: Recording 2 · …").
- **Zero:** "No shapes seen while you played" (the recordings drew only ROM-seeded rows).
- **Edge cases:** a tile drawn under exactly the export's neutral palette `0F001030` bumps the export row instead of adding an `N` row, so it is not counted (an undercount, the same blind spot artist_chr_kit documents). The `tileData` field is a CHR index on a CHR ROM game and pixel data on a CHR RAM game (two forms); both are stable within one ROM, so the union is sound. A project is one ROM's folder (ADR-0243), so two different ROMs in one project cannot happen.

### 2. "N cells painted" (W-R0b recent rows, W-R1 status line)

- **Counted:** the distinct **build cells** the artist painted. A build cell is one entry of an ADR-0153 sheet sidecar's `cells[]`, a `gridUnit`-sided square (default 8) at its `x`/`y` in 1x pixels — the unit `mep_build.py` keys and reports. A cell is painted when its square in the picture differs from the `*.orig.png` twin upscaled by the whole-number width ratio: `mep_build._EditedProbe.edited`, cell by cell, so the count agrees with what the next build treats as painted. A cell is identified by `<sidecar path>#<index>`, so `usr000` of two recordings are two cells.
- **Paint on the figure view (ADR-0225)** counts too. A figure sidecar (`figures/<usrNNN>-figure.json`, `version` 2) places every cell at its 1x pixel offset with `unit`, and names its home cell on the kit sheet (`sheet` + `index`, #498). A figure cell that differs from the figure twin counts as that home cell; paint on both surfaces over one cell counts once. A figure cell without an `index` has no home cell and is left out.
- **From:** the kit's tiles (`RemasterKitReader`) whose surface keeps a pre-paint twin: units `grid`, `object`, `element` and `panorama`. Each needs a sidecar beside its PNG (`<stem>.json`) with `cells[]` and a declared twin.
- **Updates:** when the kit is read (every W-R1 refresh and after every job), off the UI thread. A tile is measured again only when one of its six files changes: picture, twin, sidecar, figure view, figure twin, figure sidecar (`RemasterCellPaintCache`). W-R0 measures each listed project the same way.
- **Unknown:** when no tile can tell — a project without a kit, an imported project (W-R6, no pre-paint twin), surfaces without a sidecar. The clause is then left out ("3 recordings", not "3 recordings · 0 cells painted").
- **Zero:** "nothing painted yet".
- **Edge cases:**
  - Pattern pages, scene captures and imported sheets are not counted: their twin is not a pre-paint copy (G.7's measurement, 2026-10-02), so comparing them would count every cell. Paint on a page is therefore not in this number until pages keep a pre-paint twin.
  - A tile that cannot tell adds nothing while the others still add up, so a partly measurable kit shows a lower bound; every counted cell really is painted.
  - A cell that reaches past the picture counts as painted, as `_EditedProbe` does.
  - A picture that is no whole multiple of its twin makes the tile unknown; the build refuses such a sheet anyway (#346).
  - Overlapping figure cells (`z`) are compared rect by rect, so paint on the front cell can also mark the cell behind it — an overcount in the direction the build's own probe would.

### 3. "Painted: N of M phases" (W-R5 popover)

- **Counted:** `M` is the tile's phase count, the length of the fragment's `playsColumns` (the "6 phases" under the caption). `N` is the number of phases whose column draws a **painted pose**. A phase's column `c` (1-based) plays the pose `ids[c-1]`, the kit's reading order; that pose is painted when any of its figure-sidecar cells is a painted build cell (§2), on the sheet or on the figure view.
- **Edge cases:** a column that plays twice (#400, "column 1 plays twice") is two phases; a phase this sheet does not draw (`null` in `playsColumns`) is never counted in `N`; a figure with a `playsColumns` entry beyond its `ids` is not painted.
- **Shown:** only when the whole-picture probe says "Painted" and `N ≥ 1`. The whole-picture probe still decides "painted at all", so phases never turn "Not painted yet" into "Painted". When the picture is painted outside every figure's cells (`N = 0`), the line stays the plain "Painted".
- **Unknown:** when the tile has no `playsColumns` (a figure no run ordered, a scenery object, or an element whose `phases` carry no column order), or no readable version-2 figure sidecar — only that sidecar says which cell draws which pose. The popover then keeps the plain "Painted" line.
- **Updates:** with the tile row, after the same cached measurement as §2.

### 4. The W-R2 pill: "N shapes seen · M screens captured"

- **N:** the core's live `HdPackCoverageReport.TilesSeen`, the distinct shapes (palette folded) the current recording has drawn. Each recording starts in a fresh `rec-NNN`, so this is per recording. It is not "new to the project": the core reports a count, not keys, so novelty against earlier recordings cannot be measured live. The label therefore drops the render's word "new" and says "shapes seen". After Stop, the recording's `N` rows join §1's union. The two numbers can differ by the §1 neutral-palette blind spot.
- **M:** the core's `ScreensSeen` (distinct stable screens), capped at the number the recorder writes (`HdPackBuilder::MaxScreensPerPack`, 300, mirrored as `RemasterScreen.MaxScreensPerRecording`). A screen past the cap is seen but not captured.
- **Unknown:** off NES, or before the builder saw anything, the report is all zero and the pill shows no counters (unchanged).

## Consequences

- The four counts agree with the build, by construction. §2 is `_EditedProbe`'s rule, and §1 is ADR-0239 §4's "drawn keys without the placeholders", with the palette folded as the core's live counter folds it.
- The first W-R1 refresh after a kit job decodes every measurable tile's PNGs off the UI thread (26 ms for a real Contra kit of 33 tiles, measured 2026-10-03). Afterwards only changed files are re-read.
- The shell status line carries the count in Remaster at rest ("Contra (USA) · playing your project · 412 cells painted"): `WorkspaceShellViewModel` follows `RemasterWorkspaceViewModel.PaintedCellsText` (`FollowPaintedCells`), composed by `ShellStatusLine.ComposeRemaster`. An unknown count adds no words, and Play and Share keep the game's own line.
- **Trap:** `RemasterScreen.MaxScreensPerRecording` mirrors a core constant. Change both together.
- **Trap:** a kit written before ADR-0225 has no version-2 figure sidecar, so its run tiles show the plain "Painted" until the kit is prepared again.
- Paint on pattern pages is invisible to §2 until pages keep a pre-paint twin. That is a kit-format decision for another ADR, not a UI one.
