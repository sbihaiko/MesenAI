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
  `TileSheetTypes.h`). Decisions live in `docs/adr/` (ADR-0153, 0164,
  0170, 0171, 0173, 0174, 0177, 0179, 0181, 0189, 0190); this file only
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
  from `PoseStats` and nothing in it is computed at serialisation time.
  Optional fields a reader must tolerate being absent: per entry
  `fusionOf[]`, `next[]`, `variantOf`; top-level `cycles[]`, `sequences[]`
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
- **Save-time debug dumps**, env-gated, never pack files:
  `MESEN_SHEET_GRID_DUMP` (per retained frame: `F` opens it, `K`/`P` intern a
  shape and a palette word, then `x y shape palette` per cell - the palette
  field and the `P` lines are F9.24's per-cell palette plane, since the shape
  ids wildcard the palette), `MESEN_OAM_STREAM_DUMP` (per retained frame:
  index, repeat count, port 1 and 2 button bytes, then `node,x,y` per
  sprite), `MESEN_POSE_TRACK_DUMP` (one ADR-0179 track per line as
  `frame:pose:held` triples in retained-frame indexes) and
  `MESEN_TILENEARBY_EVIDENCE` (the whole co-occurrence table as CSV:
  `a,b,dir,count,frames,framesA,framesB,aIsObject,bIsObject`, written **before**
  any threshold is applied, so the candidate population can be re-thresholded
  offline without a rebuild — ADR-0190's numbers came from it). The last one is
  written from `BuildObjectSheets` *ahead of* its early-outs, because a
  recording with a populated table and no inferred objects is exactly the one
  worth studying.
- Host-free rule (ADR-0127): `SpriteGrouping` and `SheetRender` take data
  and return data; file, env and log access stay in `HdPackBuilder`, so
  `scripts/core_unit_tests.cpp` can cover the rules without an emulator.

## Child DOX Index

- (none) — sub-trees follow this file and the root DOX.
