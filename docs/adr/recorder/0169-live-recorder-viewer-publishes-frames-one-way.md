# ADR-0169: The recorder publishes its frames one way, and the live viewer never blocks the run

- Status: accepted 2026-09-08, shipped in `e652f83f`: the `live=<ms>` tap and the
  sprite-layer record in `scripts/headless_record.cpp`, the viewer `scripts/record_viewer.py`,
  and the interactive producer `Core/Shared/LiveFrameRecorder` + the Tools-menu toggle
  (section 4); all are on `main`. Section 4 amended 2026-09-23, accepted and implemented in the
  same change on the user's decision and go-ahead, quoted verbatim: "manter o script como
  ferramenta de desenvolvimento e diagnóstico, e tirar o "Open Viewer" do menu que o jogador ou
  artista vê. O Record/Stop continua, porque alimenta o kit. Seria uma emenda à ADR-0169 §4, que
  foi quem colocou o viewer no menu, e não um bug. Não medi uso real, então é uma leitura do
  fluxo atual, não um dado." Covered by `UI.HeadlessTests/LiveRecorderMenuTests.cs`.
- Date: 2026-09-08
- Amended by: ADR-0243 (accepted 2026-10-02) — the `LiveRecording` slot feeds
  `record_viewer.py`, not the artist kit. No kit, compose or `mep_*` script reads it; the kit's
  only source is the bootstrap recording.
- Related: ADR-0050 (bootstrap screen backgrounds), ADR-0157 (headless input in emulated
  frames), ADR-0164 (adjacency sidecar), ADR-0165 (the composition editor is an external stdlib
  Python tool), ADR-0167 (HUD-only capture seam), ADR-0168 (the sprite composition unit;
  `superseded` 2026-09-11 by ADR-0171)

## Record

- 2026-09-08 — the sprite-layer read channel switched from the debugger-based
  `GetMemoryState` to direct console exports under `Emulator::Lock()` (Context bullet 3,
  Decision 2, Consequences bullet 1); the record shape and the measured hold cost now match the
  implementation.
- 2026-09-08 — the viewer may launch and stop `headless_record` itself, as a subprocess it
  controls (Decision section 3). It never writes into `<prefix>-live/`; it only owns the child
  process's lifecycle.
- 2026-09-08 — the interactive emulator is the second producer (`LiveFrameRecorder`, Decision
  section 4) and supersedes the viewer-launch panel of section 3: the viewer no longer starts
  processes, fields, or knows a ROM; it auto-attaches by convention to the emulator's live slot.
- 2026-09-08 — "capture every layer": both producers now also read the background layer under
  the same `Lock()` hold as the sprite layer — `nametables.bin` (the mapper-resolved
  `$2000-$2FFF` bytes) and `background.json` (pattern table, mask/enable bits, and loopy "t" +
  fine X — the scroll the game wrote, not loopy "v"). The viewer reconstructs the background and
  multiplexes it with the sprite layer using the 2C02's own priority rule (front sprites always
  win; a behind-background sprite shows only through a transparent background pixel). Verified
  against Mega Man 3's title screen and Zelda's file-select screen: 98.7-99.3% of native pixels
  byte-identical.
- 2026-09-08 — "MMC2/MMC4 CHR-latch": Mike Tyson's Punch-Out (mapper 9, mapper 10/MMC4 by
  inheritance) swaps a 4KB CHR bank per pattern-table half mid-frame via a tile-index latch
  (`BaseMapper::
  HasChrBankLatch`, overridden only in `MMC2`): fetching tile 0xFD selects that half's "FD"
  bank, fetching tile 0xFE selects its "FE" bank. Both producers also publish, only when
  `BaseMapper::HasChrBankLatch()` is true, `chrfull.bin` (raw, latch-independent CHR-ROM) and
  `chrlatch.json` (each half's two bank numbers + the page size); the viewer walks the frame in
  the 2C02's fetch order updating each half's latch as `MMC2::NotifyVramAddressChange` does and
  draws under `build_planes_with_chr_latch`, the starting latch guessed by
  `deduce_initial_chr_latch` (from comparing `chr.bin` against the two candidate banks; self-healing:
  the assignment is an unconditional overwrite). What "não vejo o player1, não vejo as fontes"
  turned out to be for this mapper. A related bug fixed alongside: both producers had copied
  `Mask.BackgroundMask`/`Mask.SpriteMask` verbatim into the wire fields
  `BackgroundLeftColumnClip`/`LeftColumnClip`, whose polarity is the opposite — those PPU mask
  bits are a "show in the leftmost 8 pixels" flag, so the assignment is now inverted. Verified
  on a Punch-Out "Mike is waiting for your challenge" portrait screen: 85.54% exact-pixel match
  before either fix, 92.15% after; the residual is NTSC-style blend colours no palette entry can
  produce (e.g. `(47,61,42)`).
- 2026-09-08 — "frame/state capture race" (from the /goal "testar as duas telas do player por
  similaridade para todas as ROMs", a 30-ROM batch): both producers captured the composite
  pixels (CaptureScreenshot/HeadlessCaptureFrame) before taking `Emulator::Lock()`; fixed by
  locking first, because `Lock()` is what parks the emulation thread at an end-of-frame
  boundary. `Core/Shared/LiveFrameRecorder.cpp`'s `CaptureSnapshot` calls `_emu->Lock()` before
  `CaptureScreenshot`; scripts/headless_record.cpp's `CaptureLiveSnapshot` has no direct
  `Emulator*`, so two new exports `HeadlessLockEmulator`/`HeadlessUnlockEmulator` (thin wrappers
  over `_emu->Lock()/Unlock()`, InteropDLL/EmuApiWrapperHeadless.cpp) park the thread around
  both `HeadlessCaptureFrame` and `HeadlessCaptureNesSpriteLayer` in one hold; `SimpleLock` is
  reentrant per thread (`_lockCount`), so this nests safely. It also explains "nem todos os
  sprites estao com borda". Measured (30-ROM batch, mep-off): SMB 43.09% → 99.80%, Golf 12.85%
  → 37.65%, Punch-Out 92.15% → 98.29%; 24 of 30 at 97%+.
- 2026-09-08 — "HD packs are not a reconstruction target": ADR-0146 auto-loads community HD
  packs, so the composite frame is community replacement art the reconstruction cannot know
  from CHR-ROM/VRAM — visually confirmed ("METROID -HIGH DEFINITION-", "CONTRA 80's Reimagined
  By: Tastic") — the matcher `HdNesPack::GetMatchingTile` is stateful and runs on the video
  thread. The recorder publishes the *condition* instead: `LiveSnapshot::HdPackActive` (from
  `NesConsole::IsHdPackVideoActive`, the same `_hdData && HasVideoContent()` test that swaps in
  `HdNesPpu`) goes out in status.json from both producers, and record_viewer.py replaces its
  caveat with an explicit "substitution is ON, these two panes are not comparable, turn Enable
  HD Packs off" instead of a score that reads as a fidelity failure.
- 2026-09-08 — "hdpack-off is the switch, not mep-off": measuring reconstruction fidelity needs
  substitution off at the gate the player itself uses, `NesConfig::EnableHdPacks` (checked first
  in `NesConsole::LoadHdPack`); `mep-off` only takes MEP-installed packs out of discovery and a
  loose `HdPacks/<rom>/` pack (MEP-v1 §5.1) still loads. `headless_record` gained `hdpack-off`;
  the 30-ROM batch re-run with it — each line asserting the run's own published
  `hdPackActive=false`, so "substitution really was off" is measured, not assumed — lands every
  one of the 30 ROMs at 98.10% or better.
- 2026-09-08 — "mid-frame raster splits are still out of reach": six of 30 titles (Golf,
  Zelda II's status-bar split, Life Force/Super Mario Bros. 3's title, Gauntlet, Lemmings) remain
  low, every one a mid-frame PPU/mapper register write a single end-of-frame snapshot cannot
  see. Seeing it needs capturing every register write with its scanline/cycle timestamp — a
  materially bigger format than this ADR's. Left as a disclosed limitation alongside the
  8-sprites-per-scanline cap and the NTSC-blend residual.
- 2026-09-08 — "Record opens the viewer, and the slot re-targets per ROM": (a) the Tools entry
  became a submenu "Live Recorder (viewer)" with Record / Stop / Open Viewer children;
  (b) Record started the recorder and opened `scripts/record_viewer.py` as a detached process,
  located by `UI/Logic/
  RecordViewerLocator.cs` (host-free per ADR-0123), overridable by `MESENCE_RECORD_VIEWER`;
  `Open Viewer` exists for the case where the human closed it; (c) `status.json` gained a
  `"rom"` field (`ComposeStatusJson`'s last argument, escaped through `ComposeJsonString`),
  published by both producers; the UI announces the name across interop
  (`LiveRecordingSetRom`, called on `GameLoaded` and cleared on `EmulationStopped`) because the
  recorder cannot read `Emulator::GetRomInfo()` from its own thread — it returns a reference to a
  string `InternalLoadRom` reassigns. **(a)'s Open Viewer child and all of (b) are withdrawn by
  the 2026-09-23 amendment below; (c) stands.**
- 2026-09-22 — "ADR-0222 option A, F12.14": the save-time `MESEN_OAM_STREAM_DUMP` gained a
  palette id per entry; the live wire format (`Utilities/LiveRecordFormat.h`, `sprites.json`) is
  **not** changed — it carries the raw 64 OAM entries as `[y, tile, attr, x]` plus the 32 bytes
  of palette RAM, so the sprite palette is already on the wire. The dump and the wire describe
  the same sprite in two encodings on purpose: the dump interns `(tileData, palette)`, the wire
  ships the console's own bytes.
- Amended: 2026-09-23 — "the viewer leaves the player's menu": section 4's Tools-menu surface
  shrinks to Record / Stop. What changes: (a) the "Live Recorder" submenu (caption no longer
  "(viewer)")
  has exactly two children, Record and Stop, with no Open Viewer entry; (b) Record and the
  `--recordlive` command-line switch start the recorder and open nothing — the emulator no
  longer spawns `scripts/record_viewer.py`, so `UI/Logic/RecordViewerLocator.cs`, its unit
  tests, the `MESENCE_RECORD_VIEWER` override and the "Open Viewer" string are deleted. What
  does not change: the interactive recorder still publishes into `<HomeFolder>/LiveRecording`
  (`ConfigManager.LiveRecordingFolder`) and still re-targets the slot per ROM; the wire format;
  `scripts/record_viewer.py`, which stays a developer/diagnostic tool run by hand
  (`python3 scripts/record_viewer.py`) attaching to the convention slot with zero fields; and
  `scripts/render_record_viewer.py`. Rationale — a reading of the current artist flow, **not
  measured usage**: record → the kit's `.ora` surfaces → paint in GIMP/Krita →
  `mep_build.py build` → reload the repainted images (F12.3), with a tile's key picked via
  F12.2's "Copy as MEP sheet cell"; the live viewer answers a developer's questions (did the run
  reach gameplay, does the reconstruction agree), so its entry point moves to a terminal.
  Consequence: a player who wants to watch a live session must run the script by hand.

## Context

A recording is a black box. `scripts/headless_record` prints a frame counter and, at the end,
writes a pack; everything an artist can look at — the `backgrounds/screenNNN.png` screens of
ADR-0050, the sheets, the `adjacency.json` of ADR-0164 — exists only after the run is over. In
the 30-game sweep, five games never left a menu, each discovered only after a full
400-emulated-second run; Gauntlet produced 202 distinct sprite nodes entirely from its title
screen. Three facts make a live view cheap:

- The recording loop is already awake. `waitForPause("recording")` polls the frame counter
  every 2 ms for the stall watchdog, so a tap needs no thread.
- In-memory frame capture already exists — `HeadlessCaptureFrame` /
  `HeadlessReadCapturedPixels` (F9.15, `InteropDLL/EmuApiWrapperHeadless.cpp`), today called
  once at the end of a run.
- `NesConfig.DisableBackground` and `NesConfig.DisableSprites` (`Core/Shared/SettingTypes.h`)
  can separate the two planes by rendering, and the sprite layer can be read off the console
  directly. Reading it through `GetMemoryState` (`InteropDLL/DebugApiWrapper.cpp`) was tried
  and rejected: it goes through `WithDebugger`, and a headless run under a live debugger never
  parks on its target frame (`HeadlessInputEngine::ApplyFrame` refuses to pause when
  `IsDebugging()`), so the run would spin at full speed forever. The realized channel is a
  direct export, `HeadlessCaptureNesSpriteLayer` (`InteropDLL/EmuApiWrapperHeadless.cpp`), which
  holds the emulation thread at an end-of-frame boundary with `Emulator::Lock()/Unlock()` and
  reads OAM, palette RAM, the mapper-resolved CHR and the $2000 sprite-control bits — no
  debugger is attached, so the run still parks on its declared frame.

Non-goals: not a debugger and not a second emulator front end — the viewer is read-only, the run
stays scripted (ADR-0157), there is no rewind, no input, no seeking, and nothing here runs in CI.

## Decision

### 1. The recording never depends on the viewer

`headless_record` gains `live=<ms>`, off by default. Every `<ms>` of wall clock, inside the
existing poll loop, it captures the current frame and publishes it by **atomic file swap**:
write `frame.ppm.tmp` into `<output-prefix>-live/`, then `rename()` over `frame.ppm`. Alongside
it, `status.json` — frame number, target frames, elapsed wall clock, and a `done` flag — written
the same way, plus a final `done: true` status when the run parks on its target frame so the
viewer can show "finished" rather than stale data.

A FIFO or a socket is rejected, and the reason is not taste: both make the recorder's progress
depend on a reader (a FIFO with no consumer blocks the writer; a socket with a slow consumer
fills its buffer and then blocks), which would stall the emulator and let the stall watchdog kill
the run — so watching a recording could change or destroy it. A file swap is lossy-latest, the
correct semantics for a live view: a viewer that falls behind must skip frames, never queue them.
It also survives a viewer restart, admits several viewers at once, and can be inspected with any
image tool. If the live directory cannot be written, the run logs one line and continues.

### 2. The sprite layer is read, not rendered

The composed frame is published as pixels. The sprite layer is published as **data**: alongside
each frame, the run writes the sprite layer as the console sees it — OAM, palette RAM and the
$2000 control bits read under an `Emulator::Lock()` hold (Context bullet 3) — and the viewer
draws the sprites itself from those bytes plus the CHR of that moment.

The rejected alternative was to alternate `NesConfig.DisableSprites` and
`NesConfig.DisableBackground` between captures — cheaper, and no debugger. It is rejected
because the pack builder is watching the same frames: a frame rendered with its background
disabled would teach the metatile vocabulary a blank screen, and the approach would have to be
mutually exclusive with `bootstrap` and `hdpack`, making the layer view unavailable in exactly
the run someone wants to watch. Reading OAM perturbs nothing.

Reading the table also produces, per frame, the set of OAM entries on screen at that instant —
the grouping ADR-0168 argues the sprite composition unit should be. This ADR does not decide
ADR-0168; it stops the live viewer from foreclosing it.

The published sprite record is, per capture: the frame number, and for each of the 64 OAM
entries its Y, tile index, attributes and X, plus the 32 bytes of NES palette RAM (background
and sprite sub-palettes as the PPU stores them, first-slot mirror semantics included) and the
$2000 control bits the reconstruction needs (sprite pattern table, 8x16, sprite enable,
left-column mask). The CHR needed to resolve the referenced tiles is published alongside each
capture, and the run's 64-color RGB table once at startup. Sprite pixels the viewer draws are
therefore reconstructed, not captured — they can disagree with the composed frame at the edges
the PPU clips (the 8-sprite-per-scanline limit, the left-column mask). The viewer labels the
layer as reconstructed.

### 3. The viewer

`scripts/record_viewer.py`, stdlib plus tkinter, following ADR-0165: an external Python tool in
`scripts/`, never linked into the emulator. It polls the live directory, draws what it finds, and
writes nothing into it. Given no run in progress it says so rather than failing.

> Superseded in part by section 4 (interactive producer): the launch panel below was the first
> design. It is removed — the viewer no longer launches a recorder, and section 4 is the reason.
> It is kept here to record the trade that was walked back, and because Attach-to-a-path (the
> last bullet below) still stands unchanged.

The viewer may also launch `headless_record` itself, as a control panel with traditional
recorder buttons (⏺ Record / ■ Stop) over a small form — ROM, duration, an optional input
script, the live-publish interval, a realtime checkbox — instead of requiring a separate
terminal command. This does not change the protocol of section 1: the viewer still never writes
a byte into `<prefix>-live/`, and the recorder still never blocks on, or even knows about, a
reader. What is new is process lifecycle, owned entirely by the viewer:

- **Record** derives an output prefix under `runs/viewer-launched/` from the ROM name and a
  timestamp, builds the same command line a human would type, and starts it with
  `subprocess.Popen`. The live directory the viewer polls switches to that run's
  `<prefix>-live/`.
- **Stop** sends the child process `SIGTERM` and nothing else. `headless_record` has no
  graceful-shutdown handler, so this kills the run outright — the same outcome as a crash from
  the viewer's perspective.
- The viewer keeps polling the live files after the child exits. If `status.json` never reached
  `done: true`, the last published frame stays on screen and the status bar reports the run as
  interrupted rather than clearing to "no run in progress" — the frame before a crash is exactly
  what helps diagnose it.
- Attaching to a recording the viewer did not launch keeps working: the live-directory field
  stays a plain path, independent of the launch form, so several viewers can still watch the
  same run (section 1).

### 4. The interactive emulator is the second producer — by convention, not by form

The launch panel of section 3 existed to remove the need for a terminal command. It is itself
removed by a better answer: the **emulator already has the ROM open and the controller in
hand**, so the producer moves into it. A `LiveFrameRecorder`
(`Core/Shared/LiveFrameRecorder.h/.cpp`), owned by `Emulator` (`GetLiveFrameRecorder()`) like
`VideoRenderer`/`SoundMixer`, publishes the same wire format to a **single convention slot**,
`<HomeFolder>/LiveRecording` (`ConfigManager.LiveRecordingFolder`), while a human plays. A
Tools-menu toggle starts and stops it; nothing is typed.

Decisions that follow:

- **The recorder runs on its own timer thread**, not on the video decode thread. The NES
  sprite-layer read parks the emulation thread with `Emulator::Lock()` for an instant (section
  2's channel); doing that from `VideoRenderer`'s decode thread would introduce an unmeasured
  wait into the render pipeline. A dedicated thread keeps the risk identical to the
  already-measured headless case (Consequences bullet 1). `StartRecording` spawns and
  `StopRecording` joins it; destruction order in `Emulator` puts `_liveFrameRecorder` before the
  `VideoDecoder`/settings it reads.
- **There is no target frame.** A scripted run has `targetFrames`; a human-run session ends when
  the human says so. The recorder writes
  `ComposeStatusJson(..., targetFrames=0, ...)`, which the viewer renders as `frame N · live`
  rather than `frame N/0`, and StopRecording writes one final `done: true` status.
- **The interval is fixed** (250 ms in the menu toggle) — one less field.
- **The sprite layer is NES-only.** `LiveFrameRecorder::CaptureSnapshot` publishes frames for
  any console; the OAM/palette-RAM/CHR record and the one-time `palette.json` (captured from the
  NES config's `UserPalette`, the base 64-color table the 2C02 filter copies verbatim at zero
  emphasis) only for a `NesConsole`. GB/SMS/GG live sessions are frame-only, as in
  `headless_record`.
- **The viewer auto-attaches by convention.** With no argument it watches
  `<HomeFolder>/LiveRecording` (mirroring the C# home-folder rule in Python), so opening it
  beside a running emulator shows the live session with zero fields. The path box stays only as
  the manual override of section 3's last bullet. It never launches a process now, so section 3's
  subprocess machinery is gone. The "Amended: 2026-09-23" line in the header records the change.

> Amended 2026-09-23: the Tools-menu toggle is the Record / Stop pair only. The emulator does
> not open the viewer — not from the menu, not on Record, not from `--recordlive` — and offers
> no Open Viewer entry; `scripts/record_viewer.py` is a developer/diagnostic tool run by hand,
> which attaches to this slot by the convention above. The recorder and the slot are unchanged,
> because they feed the artist kit.

## Consequences

- The debugger-based read path is unusable rather than merely expensive:
  `GetMemoryState`/`GetPpuState` attach the debugger, and a headless run under a live debugger
  never parks on its target frame (`HeadlessInputEngine` `ApplyFrame` logs "not pausing" when
  `IsDebugging()`), so the run would never end. The realized channel attaches no debugger: both
  producers read under `Emulator::Lock()`, which parks the emulation thread at an end-of-frame
  boundary (it spins in `WaitForLock` with `_threadPaused` set) without touching the run's own
  pause/stop machinery. The cost of that hold, measured on a 5-second NES smoke, a 200 ms live
  tap added ~0.1 s over the plain run (1.0 s vs 0.9 s) — noise against the 2-minute budget.
- The interactive producer is a new Core source pair, `Core/Shared/LiveFrameRecorder.{h,cpp}`,
  registered in `Core.vcxproj` and its filters, so ADR-0007 is triggered and re-run (it passes).
  `Emulator` owns one instance (`GetLiveFrameRecorder()`) and exposes it to the UI through
  `InteropDLL/RecordApiWrapper.cpp` (`LiveRecordingStart/Stop/IsRecording`) and
  `UI/Interop/RecordApi.cs`, mirroring the AVI/WAV recorder exports.
- The live directory is still scratch and single-slot under the home folder:
  `<HomeFolder>/LiveRecording`. The C# side derives it from the same
  `ConfigManager.LiveRecordingFolder` the viewer mirrors, so the ROM name, the interval and the
  path never appear in either UI — the "ROM open in the emulator" and "the joystick in the
  human's hand" are the only inputs the recorder needs, and section 3's "it never launches a process" holds.
- `headless_record` grows a flag and a publish step in the poll loop. The publish must stay
  cheap: at 250 ms a 256x240 PPM is about 180 KB, noise next to a run that already writes a
  pack, but a millisecond-scale interval would not be.
- The live directory is scratch. It lives beside the run's output prefix, is never part of the
  pack, and nothing downstream may read it — a consumer would make the recording depend on the
  viewer again through the back door.
- The viewer now owns a CHR and palette decoder — NES knowledge in Python that the Core already
  has in C++. That duplication is the price of not perturbing the render path, and it is the
  first place to look when the sprite layer and the composed frame disagree.
- The viewer is the first thing in this project that shows a recording while it happens. Once it
  exists, the honest place to answer "did the run reach gameplay" is there, not in a post-hoc
  contact sheet.
- Launching from the viewer made it, for the first time, a thing that can start a child process
  rather than only read files — one command line, one process, killed with one signal; the
  boundary that matters (no bytes written into `<prefix>-live/`, no dependency of the recorder
  on the viewer) is unchanged. Runs launched this way land under `runs/viewer-launched/`.
  **Removed with the launch panel (section 4 supersedes it)** — the viewer owns no child process
  any more; the process that records is the emulator itself.
