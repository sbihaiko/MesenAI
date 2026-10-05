# Issue #419: Copy as MEP sheet cell after a state load

**Date:** 2026-09-24
**Base:** `main` @ `9a0ff899`, in a fresh worktree. The "before" core was a
full build of that tree, with every object compiled in the worktree. The
"after" core is the same tree plus this change. Its
`InteropDLL/obj.osx-arm64/MesenCore.dylib` sha256 is
`7e3e4fc5cca951fcbf8b039b31565c931a2d7ea258c31f89eb436701fbcd15fc`. Before
every measurement below, all 20 objects whose `.d` names
`Core/NES/BaseNesPpu.h` were checked to be newer than the header.
**Evidence it was about:** `f14.2-cold-read-rescore-2026-09-24.md`, cause A
and bug entry 1.

## Root cause

- `Copy as MEP sheet cell` and `Copy tile (HD pack format)` resolve a key
  through BaseNesPpu's per-scanline traces (ADR-0215), via
  `HdPackCopyHelper` → `DebugApi.GetNesScanlineTrace` →
  `NesDrawnTileResolver`.
- Neither trace is part of a save state. After a `LoadStateFile` with nothing
  emulated since, they still describe the frame drawn before the load.
- Nothing told the copy so. The export returned `true` and the copy resolved
  that leftover as if it were the frame on screen.
- The F12.2/F14.2 scan copies right after loading its `.mss`, and so does a
  person who pauses, loads a state and copies.

## Fix

- **Core:** `Core/NES/NesScanlineTraceValidity.h` is host-free.
  - `NesPpu` invalidates it on a state restore (`Serialize`, loading side)
    and on `Reset`.
  - It is armed at row 0's capture (the pre-render line, cycle 257) and
    committed at scanline 240.
  - So the trace is "current" only once a whole frame has been traced since
    the last load or reset. A load that lands mid-frame waits for the next
    whole frame.
- **Export:** `GetNesScanlineTrace` returns `int32` instead of `bool`: 0
  unavailable, 1 current, 2 not drawn since the load.
- **UI:** `NesDrawnTileResolver.Resolve(status, …)` is the one entry
  `HdPackCopyHelper` now uses.
  - It refuses a stale trace with `NotDrawnSinceLoad` before anything
    resolves.
  - This holds in all three viewers.
- **Receipt:** the refusal reaches the OSD toast like every other ADR-0215
  refusal:
  `MEP sheet cell not copied: no frame has been drawn since the last state
  load or reset, so nothing says which CHR bank drew this tile - run one frame
  (the debugger's Run one frame, or unpause) and copy again`.
  - The UI ships only `resources.en.xml`. There is no pt-BR resource file,
    and no receipt in this path is localized.
  - So the string is inline en-US, the same convention as the other
    refusals.
- **Scan harness:** `CopyAsMepSheetCellTests.cs` draws one deterministic
  frame period after the load, or a second when the state was saved
  mid-frame. It asserts the trace is current before it walks.

## TDD: red → green

The tests were written first against skeletons that kept today's behaviour:

- the header trusted the trace;
- `Resolve` ignored the status;
- the export returned 1 whenever a trace existed.

| Test | Red (before the fix) | Green |
|---|---|---|
| `scripts/core_unit_tests.cpp`, 5 `#419` functions (7 checks) | 3 FAIL: "a trace no frame has written yet describes no drawn frame", "after a state load the trace left from before it describes no drawn frame", "the end of a frame whose row 0 was captured before the load does not revalidate the trace" (1050/1053) | 1053/1053 |
| `NesDrawnTileResolverTests.A_trace_left_from_before_a_state_load_is_refused_not_resolved` | `Assert.Equal() Failure: Expected: NotDrawnSinceLoad, Actual: Resolved` | pass |
| `NesDrawnTileResolverTests.A_sprite_copy_after_a_state_load_is_refused_too` | same failure | pass |
| `NesDrawnTileResolverTests.A_current_trace_resolves_through_the_same_entry`, `No_published_trace_is_still_its_own_refusal` | pass: they guard that the gate changes no answer | pass |
| `CopyAfterStateLoadTests.A_copy_after_a_state_load_refuses_until_a_frame_is_drawn` (real core, synthetic NROM, real `SaveStateFile`/`LoadStateFile`) | `Expected: NotDrawnSinceLoad, Actual: Current` | pass |

Full suites after the change:

- `UI.Tests`: 512/512.
- `UI.HeadlessTests`: 20 passed, 3 env-gated skips.
- The ROM-gated `Copying_a_tilemap_tile_emits_the_one_line_cell_the_panel_promises`
  passes (no state load involved).

### Mutations, each reverted after the run

| Mutation | Caught by |
|---|---|
| `NesPpu::Serialize` no longer invalidates on restore | `CopyAfterStateLoadTests`: `Expected: NotDrawnSinceLoad, Actual: Current` |
| `NesPpu` no longer arms at row 0 | `CopyAfterStateLoadTests`: `Expected: Current, Actual: NotDrawnSinceLoad` (never revalidates) |
| `OnVisibleFrameEnd` commits without the row-0 arm | `core_unit_tests`: "the end of a frame whose row 0 was captured before the load does not revalidate the trace" |
| `Resolve` treats a stale trace as current (the bug) | both `NesDrawnTileResolverTests` refusal cases, and `CopyAfterStateLoadTests` ("the copy resolved through a trace from before the state load: … "index": 0") |

## E2E: the seven cause-A games

**States:** F14.2's own states, from `~/f14.2-opus-sandbox/frames/`. They are
not versioned. sha256 prefixes:

| Game | State sha256 prefix |
|---|---|
| Dr. Mario | `559a0410b4967a8b` |
| Gauntlet | `cbdaa8d62da2ccc6` |
| Ninja Gaiden | `fe73514f761f1114` |
| Super Mario Bros. | `02f99719dc388f3e` |
| Tetris 2 | `444cc7c9a0b94cc1` |
| The Flintstones | `336ce8d3c87fc251` |
| The Legend of Zelda | `94499cfe299dd1f0` |

ROMs are from the local library.

**Command, per game:**

```sh
MESEN_F122_COPY_SCAN=<rom> MESEN_F122_COPY_STATE=<mss> MESEN_F122_COPY_OUT=<table> \
  dotnet test UI.HeadlessTests/UI.HeadlessTests.csproj -p:RuntimeIdentifier=osx-arm64 \
  --filter FullyQualifiedName~The_dispatcher_can_read_every_tile_of_a_paused_frame_as_a_cell
```

This is `make headless-ui-tests` narrowed to the scan.

**Before and after:**

- "Before" is unmodified `main`.
- "After" is the fix with the harness stepping a frame.
- "Wrong bank" counts copied cells whose CHR bank is not the one the frame
  drew with. The reference is F14.2's fresh-trace column (one frame emulated
  after the load).
- "Before" differs from F14.2's own table on some rows. The warm-up before
  the load is wall clock, so the stale trace changes between runs. F14.2's
  cause A predicted this.

| Game | Before: cells | Before: (0,0) | Before: wrong bank | After: cells | After: (0,0) | After: wrong bank |
|---|---|---|---|---|---|---|
| Dr. Mario | 960 | 1276 (`$04000`) | 960 | 928 | 508 (`$01000`) | 0 (see note) |
| Gauntlet | 960 | 2317 (`$09000`) | 960 | 576 | not copied | 0 of 562 in `$04000` (1029 at (0,12)); 14 in `$0B000` unverified |
| Ninja Gaiden | 0 (scan failed: "no tile … produced a cell") | -- | -- | 118 | not copied | 0 (all `$1E000`, F14.2's 7930 bank) |
| Super Mario Bros. | 349 | 292 | 0 (NROM) | 574 | 292 | 0 (NROM) |
| Tetris 2 | 960 | 112 (`$00000`) | 960 | 960 | 4464 (`0x1170`) | 0 |
| The Flintstones | 960 | 0 (`$00000`) | 960 | 960 | 14336 (`$38000`) | 0 |
| The Legend of Zelda | 0 (scan failed) | -- | -- (CHR RAM) | 556 | not copied | -- (CHR RAM) |

**With the fix but the old harness:** the core refuses every cell (Dr.
Mario, measured). The scan fails loudly with "no tile of the paused frame
produced a cell" instead of writing 960 wrong keys.

**Dr. Mario note.** The frame the scan now reads is frame 2406, the one right
after the state's (2405). It really does switch CHR bank around scanline 40:

- rows 0–4 were drawn under `$01000`, so (0,0) is 508;
- row 5 straddles the switch and is refused (its 32 cells are the 928/960
  gap);
- rows 6–29 were drawn under `$00000`, so (0,6) is 252.

`ChrBankDiagnosticTests`' flow lets one more frame run before its step. It
lands on frame 2407, which is uniform `$00000`, and there (0,0) is 252. That
matches F14.2's "~100 ms after the load" figure and the pack's rule. So 508 is
the honest ADR-0215 answer for the frame the scan reads, not a stale key. The
pack's 252 is the steady-state frame. It is the reason the scan can no longer
promise "the frame the screenshot shows": the trace can only describe a frame
drawn after the load.

**What remains is #420/#421, not #419:**

- cells missing on Gauntlet (nametable 2 rows 0–11), Ninja Gaiden and Super
  Mario Bros. are the one-nametable walk (#421) and the palette check against
  the bootstrap `auto/` pack (#420);
- Gauntlet's 14 `$0B000` cells were not checked against a fresh diagnostic.
