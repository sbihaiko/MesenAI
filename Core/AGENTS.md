# Core/ — DOX

## Purpose

C++ emulation cores (NES, GB/GBC, SMS/GG, GBA) and the shared services the
UI, `InteropDLL` and the headless harness under `scripts/` build on. This
file records the durable contracts of the pack recorder that other trees
consume; per-core emulation code follows the upstream Mesen 2 structure and
needs no local rules beyond the root DOX.

## Ownership

- `Core/NES/HdPacks/` — the bootstrap HD-pack builder and the sheet /
  pose recorder (`HdPackBuilder`, `SpriteGrouping`, `SheetRender`,
  `SheetColourways.h`, `ChrPageSlots.h`, `TileSheetTypes.h`). Decisions
  live in `docs/adr/`
  (ADR-0153, 0164, 0170, 0171, 0173, 0174, 0177, 0179, 0181, 0189, 0190,
  0228, 0230); this file only
  states the contracts a consumer relies on.
- `Core/Shared/HeadlessInput*` — the `input=<script>` engine the headless
  harness drives the emulator with (see `scripts/AGENTS.md`).

## Local Contracts

- **Per-frame hook.** `NesConsole` calls
  `HdPackBuilder::OnFrameEnd(const uint8_t buttons[2])` once per emulated
  frame; `buttons` is the packed button byte of ports 1 and 2 in
  `NesController::ToByte` order (bit 0 = A … bit 7 = Right). A standard NES
  pad supplies it directly, a SNES pad on an NES port supplies the eight
  buttons it shares with the NES pad, any other device or an empty port
  supplies 0. The bytes ride on the retained `MesenSheets::OamFrame`
  (`Buttons[2]`) and are **not** part of frame identity: a repeated frame
  keeps the buttons of its first occurrence (ADR-0181 §1).
- **OAM capture is gated on sprites having been enabled while the frame
  drew.** `HdBuilderPpu::OnBeforeSendFrame` snapshots OAM into the sheet
  stream only when PPUMASK had sprites on at some pixel of the frame
  (`_spritesEnabledThisFrame`, sampled in `DrawPixel`, not the register's
  value at frame end - a game may flip it mid-frame) (#183). A frame with
  rendering off (power-on, some transitions) draws nothing, so `DrawPixel`
  never records a `<tile>` for it; capturing it put tile 0 under the boot
  palette `FF013403` on every golden sheet, a key the pack never emits.
  `scripts/sheet_keys_audit.py <pack>...` is the check: every sprite-sheet
  cell's `(index | source | tile, palette)` - alias tiles included - must
  resolve to a `<tile>` line (condition-prefixed or not) of the pack's own
  `hires.txt`.
- **`textures/sheets/poses.json`** is written by `SheetRender::SerializePoses`
  from `PoseStats` and nothing in it is computed at serialisation time — with
  one named exception: the `label` beside each pose and run is a *rendering*
  of the fields already in that entry (ADR-0209 Q1 (b),
  `Core/NES/HdPacks/SheetLabels.h`), never a new measurement, and it always
  travels with `"labelSource": "inferred"` so a reader can tell it from a
  human's name and let the human's win (ADR-0183 §5). The same two fields
  appear on every sheet sidecar cell with grouping data and, top-level, on
  every `sprNNN`/`objNNN` group sheet; a cell with no grouping data keeps the
  empty `label` and no `labelSource`.
  Optional fields a reader must tolerate being absent: per entry
  `fusionOf[]` (two kept parts, ADR-0177, or since ADR-0228 **one**: a kept
  pose plus a remainder of `kPoseMinTiles` (4) or more tiles that never stood
  alone - a reader must not assume two), `next[]`, `variantOf`; top-level
  `cycles[]`, `sequences[]`
  (ADR-0179) and `input {frames, ports, held{}, never[]}` (ADR-0181 §2,
  written only when some button was ever held; `never` lists buttons and
  direction+action pairs no single port ever held at once); per `cycles[]`
  entry `driver: "port1"|"port2"` (ADR-0181 §3, F9.23) when the
  interruption rule fired — `PoseRun::Windows`/`Stops[2]`/`Driver` hold the
  judgement, constants `kDriverMinWindows`, `kDriverStopLag`,
  `kDriverStopShareNum/Den` in `TileSheetTypes.h`; absent means not
  classified. Consumers: `scripts/compose_engine.py` (`Poses`, `PoseInput`,
  `PoseRun.driver`).
- **A `<tile>` line the recorder emits may carry an inferred condition, and it
  always keeps a bare twin.** ADR-0189 §3 (F9.24) made the serializer put a
  conditioned tile's line **first** and a byte-identical bare line immediately
  after it, naming the same PNG cell; ADR-0190 extended that to
  `tileNearby`, auto-attached from the co-occurrence table
  (`MesenSheets::SelectTileNearby`, thresholds `kTileNearbyMinFrames` /
  `kTileNearbyMinProbability` in `TileSheetTypes.h`). The twin is not an
  optimisation: `HdNesPack::GetMatchingTile` walks a key's entries in
  **file order** and returns the first whose conditions pass, so a wrong or
  unevaluable condition falls through to the bare line and the pack renders
  exactly as it did before. **The failure mode is "no improvement", never a
  hole**, and any change that drops the twin is a defect. Two consequences a
  consumer can rely on: every `<condition>` definition we write appears
  **above the first `<tile>` line** (`HdPackLoader` resolves a `[name]` prefix
  at read time, so a definition placed after its consumers never binds), and
  the emitted conditions parse back through `HdPackLoader` with 0 errors —
  checked, with a negative control, as the third leg of the slice's acceptance.
- **Sheet emission is deferred, except for maps (ADR-0230, F14.9).**
  `HdPackBuilder::WriteSheetFiles` hands every built sheet to
  `MesenSheets::QueueSheet`. A sheet `SheetWritesImmediately` accepts (a map
  with no cells) is written right away with no folds, and its canvas is never
  held. A map canvas can reach `kMaxMapPixels` (256 MB at 1x), and the
  variant pass cannot touch a map (PR #461). Every other sheet is copied
  into `_pendingSheets`. At the end of `BuildSheets`, `FlushSheetFiles`
  plans once over all of them (`PlanPaletteCells`). `FlushPendingSheets`
  then lays out, writes and frees them **one at a time**, so save-time peak
  memory stays near the old "one sheet at a time" bound. The file set and
  bytes do not depend on the order sheets are written in. The memory
  contract is pinned by
  `TestTheSheetQueueHoldsNoMapAndReleasesEachCanvasOnceWritten`.
- **Every palette a shape was drawn in reaches a sheet sidecar (ADR-0230).**
  A shape is interned palette-wildcarded, so its cell shows the first palette
  seen. Each other palette `hires.txt` carries for the same key
  (`WrittenPalettesByShape`: only a variant that holds a CHR page slot is
  drawn; since #460 the only one left off is one a full page refused)
  becomes exactly one of two things:
  - **`tiles[].folds: [{"palette", "brightness"}]`** on the cell's tile
    entry, only when the fold is **exact**. The cell's own crop, scaled by
    one loader Brightness, must rebuild every painted pixel on the render
    palette (`FoldIsExact`, with `LoaderBrightness` =
    `HdPackLoader`'s `(int)(stof(text)*255)`, applied as
    `HdNesPack::AdjustBrightness` does). `brightness` is a number in
    [0, 4], written as `"%.4f"` with trailing zeros dropped
    (`FoldBrightnessText`), and never names the entry's own palette. A fade
    whose least-squares Brightness leaves a residual is **not** a fold
    (the #448 refinement).
  - **A variant cell**: `"variantOf": <base cell index>`, no `metatile`,
    and a fresh cell `index`. It is rendered from the recorded art in its
    own palette, in rows inserted directly beneath the base cell's grid row
    and in the base cell's column, so a cycle's columns keep their order.
    It goes on the highest-ranked non-map sheet that holds a shape with that
    `hires.txt` key (first sheet, then first cell, on ties). A map placement
    never resolves to it. On an `object`/`sprite` sheet, the blanks the
    inserted rows leave are listed in `emptySlots` (ADR-0175).
  - A sidecar with neither field is byte-identical to the pre-ADR-0230
    schema. Round-trip invariant: every drawn key is named by some sidecar
    entry, as a cell key or as a fold, and no sidecar names an undrawn key.
    `mep_build` twice gives a byte-identical `hires.txt`.
  - The fold predicate `ClassifyPaletteRelation` ports
    `scripts/palette_folds.py`'s `palette_relation`. Both are checked
    against `docs/specs/golden/sheets/palette-relation-cases.txt`, by
    `core_unit_tests` and by `scripts/test_palette_folds.py`. Change the
    definition in both, and regenerate the vectors only on purpose.
- **A CHR page slot is never overwritten (#460).** `HdPackBuilder::AddTile`
  files each tile through `MesenSheets::PlaceTileOnChrPage`
  (`ChrPageSlots.h`) on its bank's 256-slot page for its palette; `SaveHdPack`
  writes one `<tile>` line per filled slot, cell by cell into
  `chr/Chr_*.png`. A tile takes the slot of its CHR index while that slot is
  free. When a different tile already holds it (on CHR RAM the bank hash
  `HdBuilderPpu` keys by is not refreshed when the game rewrites CHR RAM,
  #467), the earlier tile keeps the slot and its `<tile>` line, and the
  newcomer moves to the free slot of the same page whose column (that slot
  index across the bank's pages) holds the fewest tiles, lowest index first,
  so the bank's PNG count does not grow while any column has room. A loaded
  CHR RAM tile (index -1) takes the first free slot. Only a page's first
  `ChrRamBankSize / 16` slots are usable (64/128/256 for 1/2/4 KB), because
  `DrawTile` offsets each page of a PNG by that stride; every search above
  stays inside them, and a page is full once they are. A full page refuses the
  tile: it gets no `<tile>` line and is counted in `_droppedTiles`, which
  makes `SaveHdPack` keep the old fragments (ADR-0160 §3 guard). Consumers
  must therefore not read a tile's CHR index from its cell position in
  `chr/Chr_*.png` for a relocated tile; `hires.txt` is the authority. Pinned
  by `TestALaterTileNeverEvictsAnEarlierOneFromItsChrPageSlot`,
  `TestADisplacedTileGoesToTheLeastFilledColumnOfItsBank`,
  `TestAFullChrPageRefusesATileInsteadOfEvictingOne`,
  `TestADisplacedTileStaysInsideTheUsableSlotsOfItsPage` and
  `TestAPageWithItsUsableSlotsFullRefusesATile`.
- **Save-time debug dumps**, env-gated, never pack files:
  `MESEN_SHEET_GRID_DUMP` (per retained frame: `F` opens it, `K`/`P` intern a
  shape and a palette word, `M` carries the frame's internal RAM, then
  `x y shape palette` per cell - the palette field and the `P` lines are
  F9.24's per-cell palette plane, since the shape ids wildcard the palette,
  and the `M` line is F12.6b's `$0000`-`$07FF` window (ADR-0197 §3, one line
  per retained frame, on its first repeat, 4096 upper-case hex characters, the
  byte at address A at characters 2A/2A+1), `MESEN_OAM_STREAM_DUMP`
  (`MesenSheets::WriteOamStreamDump`, self-describing since ADR-0222 /
  F12.14: `K`/`P` intern lines exactly like the grid's, then per retained
  frame index, repeat count, port 1 and 2 button bytes, then
  `shape,x,y,pal` per sprite — `shape` is the ShapeId shared with the grid
  stream, `pal` the interned palette id `OamEntry::Palette` carries, which is
  part of entry identity so a frame that only recolours a sprite is not
  collapsed into `RepeatCount`), `MESEN_POSE_TRACK_DUMP` (one ADR-0179 track per line as
  `frame:pose:held` triples in retained-frame indexes) and
  `MESEN_TILENEARBY_EVIDENCE` (the whole co-occurrence table as CSV:
  `a,b,dir,count,frames,framesA,framesB,aIsObject,bIsObject`, written **before**
  any threshold is applied, so the candidate population can be re-thresholded
  offline without a rebuild — ADR-0190's numbers came from it). The last one is
  written from `BuildObjectSheets` *ahead of* its early-outs, because a
  recording with a populated table and no inferred objects is exactly the one
  worth studying.
- Host-free rule (ADR-0127): `SpriteGrouping`, `SheetRender`,
  `SheetColourways.h` and `ChrPageSlots.h` take data and return data; file, env and log access
  stay in `HdPackBuilder`, so
  `scripts/core_unit_tests.cpp` can cover the rules without an emulator.
- **Movie row ↔ device list need not match in width.**
  `MesenMovie::SetInput` (also used by `BizHawkMovie`) walks one
  `_inputData[row]` element per registered control device, in
  `BaseControlManager::_controlDevices` order (system action manager
  first, then the console's ports). A native `.mmo` row has exactly one
  field per device; a BizHawk `.bk2` row has only what its `LogKey`
  declared, while `NesConsole`/`SmsConsole::InitializeInputDevices` may
  still auto-configure both controller ports. Contract on mismatch: a
  leftover field is dropped when the poll counter advances; a missing
  field leaves that device at the `ClearState()` already applied for the
  poll (no input) and must **not** wrap `_deviceIndex` back into earlier
  fields of the same row (that would turn a Commands/Power+Reset column
  into a phantom later-port direction) nor treat the short row as movie
  end. Only advancing past `_inputData.size()` ends the movie. Side
  effect: assertion-enabled builds no longer abort on a real `.bk2`
  (#270); playback of a single-player `.bk2` on a two-port auto-config
  stays deterministic instead of silently feeding Commands into port 2.

## Child DOX Index

- (none) — sub-trees follow this file and the root DOX.
