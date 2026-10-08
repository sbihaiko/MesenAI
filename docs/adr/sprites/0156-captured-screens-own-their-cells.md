# ADR-0156: A captured screen owns the cells it covers — they leave `metatiles.png`

- Status: accepted
- Date: 2026-09-05
- Related: PRD Part A §4 "Phase 9" (slice F9.9), ADR-0050, ADR-0153 §3/§4/§5, ADR-0049, ADR-0005, ADR-0127
- Supersedes / amends: amends ADR-0153 §3 (the scene sheet is no longer the whole scene vocabulary — the cells the screen surface owns are left off it) and ADR-0050 (its screens become the primary surface for the content they cover)
- Amended by: ADR-0159 (lifts this ADR's non-goal on changing what `CaptureScreen` captures and its anchors); ADR-0236 (2026-09-25: a capture that carries the per-cell record owns a cell only on frames whose live key there equals the recorded one — pending slice F14.11)

## Context

`<background>` is the only **positional** surface the format has. A `<tile>` is keyed by content, so painting it paints every appearance of that tile everywhere; `metatiles.png`, `objNNN.png` and `sprNNN.png` are content-addressed the same way. An element spanning many repeats of one tile — a logo across a ring canvas — can only exist in a `<background>`. ADR-0050 writes one per static screen (`backgrounds/screenNNN.png`, three `tileAtPosition` anchors, priority 20).

Two measurements made this urgent. Against an artist's HD reimagining (Punch-Out!!, PRD 2026-09-05) the missing capability was not grouping but that the pipeline never routes anything to its one positional surface. And F9.8's cost: with adjacency evidence required before stitching, **23 of the 30 recorded packs write no map at all**, leaving `backgrounds/screenNNN.png` as their only whole-screen surface (Punch-Out!! among them). The load-bearing fact is in `HdNesPack::GetPixels`: the priority-20 layer (`BehindFgSpritesPriority`) is drawn **after** the background tile, so a cell whose every sighting sits under such a screen is paint the artist never sees — yet it still costs a cell on the sheet.

Non-goals: changing what `CaptureScreen` captures, or its anchors; a new `hires.txt` construct; any change to the sidecar schema (ADR-0153 §4 stands — this removes cells from a sheet, it does not describe them differently); renumbering the vocabulary; touching maps, objects or sprites.

## Decision

### 1. Screen residency

A vocabulary entry is **screen-resident** when all three hold:

1. its context is `scene` (ADR-0153 §3);
2. at least one **captured** frame — a `GridFrame` the recorder flagged after `CaptureScreen` wrote `backgrounds/screenNNN.png` — shows it; and
3. **every** sighting is *explained* (`"Every sighting is explained"`): some captured frame carries that same cell at the same `(row, col)` **and** the same `FineX`.

A sighting is the tuple `(cell, row, col, fineX)`; position and fine scroll are both part of the identity because the surface is positional — the same cell one column left, or another sub-tile offset, is a different pixel the screen PNG does not cover.

Screen-resident cells are **left off `metatiles.png`**. They keep their vocabulary index — maps, objects, sprites and the F9.7 alias list all address entries by index (ADR-0153 §4) — and the count is logged by `HdPackBuilder::BuildSheets` (`N cells routed to the captured screens`).

### 2. Why those exact clauses

- **"Every sighting", not "seen on a screen".** A cell on a captured screen *and* in gameplay must stay: the gameplay appearance is under no `<background>`, so its tiles do render. One unexplained sighting disqualifies the cell — this keeps Super Mario Bros.' ground block on the sheet.
- **The fine scroll.** It separates a scroller from a non-scroller when positions alone cannot: a scroller re-shows the same cells at the same grid positions under seven other sub-tile offsets, and `RecordGridFrame` normalizes the grid *relative to* `FineX`.
- **Position, not frame identity.** Punch-Out!! draws its fighters in the background layer; they move, the ring does not. The positional clause routes the ring and keeps the fighters — the mockup's split ("route the static screen there, leave the moving subjects to tiles").
- **`scene` only.** A status bar sits inside every captured screen, so an unrestricted rule would empty `hud.png` and `font.png`; `misc` is the noise budget PRD Phase 9 validation test 7 measures.
- **A cell on several captured screens is still routed** — covered on all of them, so painting it is invisible on all of them. The cost (the artist paints N screens, not one crop) is accepted in Consequences.

### 3. Nothing depends on those cells being on the sheet

`scripts/mep_build.py build` regenerates `textures/hires.txt` from the sheets alone, so a key no sheet claims gets no `<tile>` and renders from the ROM — already true for every key outside the vocabulary; this enlarges the set, it does not create it, safe because a routed cell was never seen where a captured screen did not already cover it. The screens survive a rebuild untouched (`_parse_source` keeps `[cond]<background>` lines and `build` copies a missing background PNG up from `auto/textures/`), and `scripts/test_mep_build.py` pins all three facts (`screen_residency_tests`). **ADR-0153 §4 needs no new precedence clause**: precedence resolves a key two sheets both claim; this removes a claimant.

### 4. Where it lives

`MesenSheets::MarkScreenResidentCells` in `Core/NES/HdPacks/MetatileVocabulary.{h,cpp}`, called at the end of `BuildVocabulary` (after `MarkIsolatedAsMisc`). Host-free, per ADR-0153 §5 and ADR-0127. `GridFrame::Captured` and `MetatileEntry::ScreenResident` join `TileSheetTypes.h`; `HdPackBuilder` sets the first when `CaptureScreen` grows `_hdData.BackgroundFileData`, and `WriteContextSheets` reads the second. Cases in `scripts/core_unit_tests.cpp` Bloco P (`make core-unit-tests`).

### 5. The routing floor

Routing is **withheld entirely** unless the recording looks like gameplay. "Every sighting is explained" is trivially true of a run that never left one screen — it captures that screen, sees nothing else, routes its whole scene vocabulary — and ships an almost empty `metatiles.png`, indistinguishable from a game that really is one screen.

The floor is two of the four clauses of `scripts/gameplay_probe.py` (F9.13), with that script's thresholds and calibration (86 hand-labeled packs; 17 of 20 menu-only recordings caught, no false alarms):

- **tile structure** — `GridDetection::Alt8x8`. Below `kGameplayTileStructure` (0.86) the run drew one-off compositions (a logo, a menu frame, a portrait).
- **misc share** — the share of the vocabulary off the grid with no adjacency support. At or above `kGameplayMiscShare` (0.32) the screen was drawn at text granularity.

The asymmetry is deliberate: a false "this is not gameplay" costs the artist a fatter sheet (what they had before F9.9); a false "this is gameplay" costs them the sheet. `Vocabulary::RoutingWithheld` keeps the two zeroes apart in the log — "withheld" and "nothing to route" are the same cell count and not the same event.

#### The sampling cap

Those clauses answer "was this a menu?"; the re-recorded library raised a case they cannot, because the recordings *were* gameplay: residency is proved against the retained frames, a **sample** (`kMaxSheetFrames`). For a combinatorial game — Tetris, where every board state is another screen — that sample says "every sighting is covered" when it means "the recording did not last long enough to see otherwise".

Measured as a share of the scene vocabulary over the 30-ROM library re-recorded 2026-09-05: Mario Bros. 106 of 106 (no `metatiles.png` at all), Tennis 206/207, Tetris 648/651, Donkey Kong 171/172, Golf 184/186, Tetris 2 376/386 — six games at 0.974–1.000, each leaving a sheet of one to ten cells. The next game down is Zelda 1 at 0.879 (still 28 usable cells), then the field is continuous (Lifeforce 0.770, Mega Man 2 0.754, Punch-Out!! 0.637, Super Mario Bros. 0.106). So above `kMaxRoutedSceneShare` (**0.93**, the midpoint of that gap) nothing is routed. The value is a measured gap, not a swept parameter; with the anchor gap (issue #164) the alternative is the worst outcome: no sheet to paint, and screens that do not draw on any variant the recording missed.

#### The sheet floor

The share is the wrong axis on its own. The gap 0.93 was cut from (0.879 to 0.974) **did not reproduce**: the next run put Bomberman at 0.899, Zelda 1 at 0.900 and Pac-Man at 0.901, straight through it, keeping sheets of 13, 21 and 23 cells. A share moves with the vocabulary and the luck of a recording; the thing the artist opens does not. So routing is also withheld when it would leave fewer than `kMinSceneSheetCells` (**30**) cells on the scene sheet — not a gap, a floor on usefulness, hence an absolute count and not a ratio. The remaining sheets in the run it was set against run 13, 21, 23, then 37, 42, 46, 55. `RoutingWithhold` names which floor fired, because a log that blames the recording for a sampling cap sends the reader to fix the wrong thing: Tetris is gameplay by any reading (26 distinct screens) and was still capped, for routing 648 of its 651 scene cells. The probe's blind spot carries over: a password or option screen that is *itself* tiled wallpaper (Punch-Out!!'s, Mega Man 2's, Dr. Mario's) clears both clauses — caught downstream where `bootstrap_auto_packs.sh` reports `MENU`.

### 6. The thresholds are provisional, and what would move them

`kMaxRoutedSceneShare` and `kMinSceneSheetCells` are **not** settled, and this ADR is accepted with that stated. Both were revised inside a single day (0.93 was walked through by the very next re-record; 30 came from one run's distribution). The rule — a captured screen owns the cells it covers — is what is accepted; the numbers are the best reading of one library. Re-measure them when any of these change:

- **the library.** All calibration is 30 NES titles, heavy on first-party Nintendo.
- **the console.** GB/GBC/SMS have different screen sizes and tile economics; `gameplay_probe.py`'s clauses already abstain off NES (its 256x240 assumption).
- **the recording length.** Residency is proved against retained frames, so a 600 s recording sees more of a combinatorial game than a 300 s one and routes *less*.
- **`kMaxSheetFrames`,** directly.

The failure mode to watch for is a pack whose `metatiles.png` is a leftover. `scripts/sheet_report.py` and the builder's log line (naming *which* floor fired) show it without opening the pack.

## Consequences

- **Measured offline on the 30-pack library** by `scripts/spike_screen_residency.py` (matches `metatiles.orig.png` cells against the captured screens, palette-agnostic). It is an **upper bound**: it cannot see the motion frames where clause 3 does its work. Mike Tyson's Punch-Out!! 71 of 334 cells sit on exactly one screen and 182 on several (21 % / 55 %); Super Mario Bros. 50 of 92 and 24 (54 % / 26 %); Metroid 7 of 62 and 37 (11 % / 60 %). Library-wide, 1258 of 4542 cells sit on exactly one captured screen. The scrollers' numbers are inflated by construction (SMB's ground block is on its captured screen *and* under every scroll offset). **No pack was re-recorded for this slice.**
- **An artist repaints screens, not crops.** A cell on twelve captured screens costs twelve painted screens — the price of a positional surface, and the workflow ADR-0050 measured the best community packs using.
- **The anchor gap.** A `<background>` is gated on three `tileAtPosition` anchors; on a *variant* where an anchor changed the background does not draw and the routed cells render vanilla, flatter than the painted neighbors. The rule tests positions, not whether the conditions hold — bounded to variants the recording did capture, and the cheapest fix is to widen the anchor choice, not the rule. Tracked as issue #164; F9.9 only makes the miss visible.
- **A thin recording routes too much**, shipping an almost empty `metatiles.png`; `scripts/sheet_report.py` and the builder's log line tell a bad recording from a bad inference.
- **Cost at save time.** One extra pass over the retained grid stream (≤ `kMaxSheetFrames` frames × ≤ 240 placements) and one `std::set` of sightings. No per-frame cost. `GridFrame` grows one `bool`; ADR-0153 §5's retention budget is unchanged (the struct is dominated by its 960 `uint16_t` cells).
