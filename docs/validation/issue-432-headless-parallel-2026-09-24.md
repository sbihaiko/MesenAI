# Issue #432: UI.HeadlessTests ran native-core tests in parallel

**Date:** 2026-09-24
**Base:** `main` @ `58ea58d7`, in a fresh worktree. The core was built in that
worktree (`make core`), and `NativeCore` loaded
`InteropDLL/obj.osx-arm64/MesenCore.dylib` from it.
**Scope:** test wiring only. No app or core code changed.
**Consistent with:** ADR-0150 (the headless project, its §3 containment: native
tests self-skip when no core is built) and ADR-0131 (`unit-tests.yml` never
builds the core). Nothing in either ADR covers parallelism, so this is a fix
under them, not a new decision.

## Root cause

- The native `MesenCore` behind `EmuApi`, `DebugApi`, `ConfigApi` and
  `InputApi` is one emulator per process. `InitializeEmu`, `LoadRom`,
  `LoadStateFile`, `InitializeDebugger`/`ReleaseDebugger` and `Stop` act on
  it whoever calls them.
- `UI.HeadlessTests` uses xUnit v3 (3.2.2). The project had no `[Collection]`,
  no `CollectionBehavior` and no `xunit.runner.json`. So every test class was
  its own collection, and collections run in parallel.
- `[AvaloniaFact]` tests are marshalled onto the one headless UI thread, so
  they rarely overlap each other. Plain `[Fact]` tests run on the thread
  pool, next to them. Two of the native classes use `[Fact]`:
  `CopyAfterStateLoadTests` (#419) and `ChrBankDiagnosticTests`.
- TRX timings of one baseline run show it. `CopyAfterStateLoadTests` ran from
  0.000 s to 2.699 s, and over it 5 native tests of 5 other classes started:

  ```
    0.000 ->   2.699  Passed   CopyAfterStateLoadTests.A_copy_after_a_state_load_refuses_until_a_frame_is_drawn
    0.572 ->   0.854  Passed   RendererLetterboxTests.Force_integer_scale_does_not_apply_to_a_normal_window
    0.872 ->   1.074  Passed   LiveRecorderMenuTests.Realized_live_recorder_submenu_is_exactly_record_and_stop
    1.081 ->   1.166  Passed   PlayerHomeCardsTests.Welcome_cta_dismisses_the_card_for_good
    1.174 ->   1.306  Passed   GamepadTestTabTests.Deadzone_ring_and_dot_follow_the_view_model
    1.321 ->   3.905  Failed   CopyAsMepSheetCellTests.The_scan_offers_a_line_for_every_cell_the_frame_drew_from_any_nametable
  native tests executed: 17; cross-class overlapping pairs: 5
  ```

  The failing scan read `NesScanlineTraceStatus.Unavailable`. The other test's
  `ReleaseDebugger`/`Stop` had run under it.

## Fix

- `UI.HeadlessTests/NativeCoreCollection.cs`: one
  `[CollectionDefinition(Name, DisableParallelization = true)]`. xUnit runs
  such a collection on its own, after the parallel collections finish. So
  its tests never overlap each other, nor a core-free test.
- `[Collection(NativeCoreCollection.Name)]` on the 8 classes that reach the
  core: `ChrBankDiagnosticTests`, `CopyAfterStateLoadTests`,
  `CopyAsMepSheetCellTests`, `GamepadTestTabTests`, `LiveRecorderMenuTests`,
  `PlayerHomeCardsTests`, `PlayerPackPickerTests`, `RendererLetterboxTests`.
- The core-free classes stay parallel: `ControllerHighlightTests`,
  `HdPackCopyReceiptTests`, `PlayerSettingsTabsTests`, and the guard itself.
- Why not turn off parallelization for the whole assembly: that is also
  correct, but the guard below gives the same safety and keeps the core-free
  tests parallel. The cost of the collection is small: suite duration went
  from a median of 13 s (runs shortened by failures) to 15–17 s.
- `UI.HeadlessTests/AGENTS.md` states the rule under "Test hygiene".

## The guard: `NativeCoreCollectionGuardTests`

- It walks the IL of every test class: its methods, its nested types (lambdas,
  async state machines), and any method of this assembly they call, whatever
  the depth. It flags a class that references `NativeCore` or a
  `Mesen.Interop` type holding P/Invoke methods.
- `Every_test_class_that_reaches_the_native_core_runs_in_the_serial_collection`:
  - every flagged class must carry `[Collection(NativeCoreCollection.Name)]`;
  - it also asserts that `CopyAfterStateLoadTests` and
    `CopyAsMepSheetCellTests` are flagged, so the scan cannot pass vacuously.
- `The_serial_collection_disables_parallelization` reads the definition's
  attribute data.
- It needs no core, so it also runs on CI. There the native tests self-skip
  (ADR-0150 §3) and the race could never show.

### Red → green

| Step | Result |
|---|---|
| Red: guard added, no attributes (current `main`) | `Failed`: "…not in [Collection(NativeCoreCollection.Name)]…: ChrBankDiagnosticTests, CopyAfterStateLoadTests, CopyAsMepSheetCellTests, GamepadTestTabTests, LiveRecorderMenuTests, PlayerHomeCardsTests, PlayerPackPickerTests, RendererLetterboxTests". It flags exactly those 8. |
| Green: attributes added | both guard tests pass |
| Mutation: `[Collection]` removed from `RendererLetterboxTests` | `Failed`: "…(#432): RendererLetterboxTests (collection: <none>)" |
| Mutation: `DisableParallelization = false` | `Failed`: "NativeCoreCollection must set DisableParallelization = true (#432)." |

Both mutations were reverted after the run.

## Before/after: repeated `make headless-ui-tests`

Environment for every run:

- `MESEN_NES_ROMS` = the local NES library (enables the three ROM-gated
  `CopyAsMepSheetCellTests` cases).
- `MESEN_F122_COPY_SCAN` = The Legend of Zelda (1987).
- A fresh `MESEN_F122_COPY_OUT` per run.
- "with state": `MESEN_F122_COPY_STATE` = a Zelda stage-1 `.mss` from
  `runs/golden-20260913-f923/probe-stages/zelda/`. That is the F12.2 prepare's
  configuration (#432's repro).

"Before" runs had the guard present. The guard's own failure is not counted.
Only other test failures and crashes are.

| Batch | Runs | Clean | Test failures | Host crash (134/139) |
|---|---|---|---|---|
| Before, no state | 15 | 1 | 13 | 1 (139) |
| Before, with state | 14 (see note) | 0 | 12 | 2 (134) |
| After, no state | 15 | 15 | 0 | 0 |
| After, with state | 15 | 14 | 1 (see residual) | 0 |
| After, with state (second batch) | 15 | 15 | 0 | 0 |

Note: a 15th before-with-state run is excluded. The wrapper script it ran
through was overwritten mid-batch by another session sharing the scratch
folder, so that run did not execute this worktree's suite.

Failures before the fix, by test:

- `CopyAsMepSheetCellTests.The_scan_offers_a_line_for_every_cell_the_frame_drew_from_any_nametable`:
  25 of 29 runs.
- `CopyAfterStateLoadTests.A_copy_after_a_state_load_refuses_until_a_frame_is_drawn`:
  4 runs.
- `CopyAsMepSheetCellTests.The_dispatcher_can_read_every_tile_of_a_paused_frame_as_a_cell`
  (the F12.2 scan): 4 runs. It wrote no table in any of them.
- `PlayerPackPickerTests` (both cases): 5 runs.

The dispatcher's copy table, with state:

- After the fix: 30 of 30 tables were byte-identical (990 lines, md5
  `ef8c815577eaaa98d42320bcd9e70a49`).
- Before the fix, the 10 tables that were written matched it too. The scan
  failed or the host crashed in the other 4.
- This batch did not reproduce #432's "table of another frame". It did
  reproduce the crashes and the missing tables.
- Without a state, the table legitimately varies (584 or 990 lines) both
  before and after. That path lets the core run 2 s of wall clock at maximum
  speed, so which frame it stops on is timing-dependent by design. It is not
  evidence either way.
- `PlayerHomeCardsTests.Continue_card_is_on_screen_once_a_game_was_played`
  (#432 "Observed") did not fail in any of the 74 runs here, before or after.

## Residual (not caused by the race, not fixed here)

- One of the 45 after-fix runs failed
  `PlayerHomeCardsTests.Welcome_cta_dismisses_the_card_for_good`.
- It failed with a `[Test Case Cleanup Failure]`, inside Avalonia's per-test
  isolation (`HeadlessUnitTestSession.EnsureIsolatedApplication` →
  `DefaultRenderLoop.Add` → "The calling thread cannot access this object
  because a different thread owns it").
- It is Avalonia's headless session, not two tests on the core. The native
  collection was running serially.
- An unconfirmed lead: `MainWindow`s left by earlier tests keep their
  `NotificationListener`. A core notification could then reach
  `Dispatcher.UIThread` from the core's thread between two isolated
  applications.
- Seen once in 45 runs, so it was not filed as a bug.

## Review follow-up (PR #441)

- **Finding.** The guard followed calls only inside `UI.HeadlessTests`.
  `EmuApi`, `DebugApi`, `ConfigApi` and `InputApi` live in the UI assembly.
  So a test that reached the core only through app code was invisible, e.g.
  one that builds `MainWindow`, whose constructor calls `EmuApi.InitDll()`.
  All 8 serial classes happened to name `NativeCore` directly.
- **Fix.** The walk now also follows direct calls into the UI assembly:
  call/callvirt/newobj/ldftn operands, the static constructor of any app type
  it touches, and the `MoveNext` of app async/iterator state machines. It
  keeps a visited set and stops at 6 app-code calls deep
  (`AppCallDepth`). The deepest real chain today is 4. A failure now prints
  the call chain.
- **What the walk does not model, on purpose.** It does not guess virtual
  overrides or event handlers the framework may call later. Modeling them
  would flag `ControllerHighlightTests`, because
  `KeyBindingButton.OnPropertyChanged` calls `InputApi` for a property that
  test never sets. It is also blind to which branch a test's arguments pick.
- **Two classes are newly flagged. Neither reaches the core at runtime.**
  - `PlayerSettingsTabsTests`: `ConfigWindow` → `ConfigViewModel` →
    `SelectTab` → `AudioConfigViewModel` → `ConfigApi.GetAudioDevices`. The
    test opens the Input tab, so the Audio branch never runs.
  - `HdPackCopyReceiptTests`: `HdPackCopyHelper.CopyAsMepSheetCell` → `Copy`
    → `TryReadTileKey` → `DebugApi.GetAbsoluteAddress`. With
    `HdPackCopyContext.None()` it refuses before that read.
  - Both now carry the new `[NativeCoreFree("<reason>")]` attribute
    (`NativeCoreCollection.cs`) and stay parallel. The guard accepts it only
    for a path through app code. It rejects the attribute on a class that
    names the core itself, on a class that is also in the serial collection,
    and on a class where the walk no longer finds any path (stale).
  - CI backs the claim: `checks.yml` runs this project with no core built. A
    `[NativeCoreFree]` class that really called `MesenCore` would fail there
    with `DllNotFoundException`.
- **No class newly needs the serial collection.** `ControllerHighlightTests`
  stays unflagged.

| Step | Result |
|---|---|
| Red: new self-test `A_class_that_reaches_the_core_only_through_app_code_is_flagged`, old walk | `Failed`. The fixture (`new MainWindow()`, no `[Fact]`, never run) was not flagged. |
| Green: new walk | all 4 guard tests pass (main scan 34 ms, the rest ≤ 17 ms) |
| Mutation: app-code following disabled | 3 guard tests fail, one of them reporting the stale `[NativeCoreFree]` |
| Mutation: `[NativeCoreFree]` removed from `PlayerSettingsTabsTests` | `Failed`, printing the chain down to `ConfigApi.GetAudioDevices` |
| `make headless-ui-tests`, twice (core built, no ROM env) | 24 passed, 4 skipped (ROM-gated), 0 failed. 4 s of test time each, 7.0 s and 6.5 s wall |
| `make doc-checks` | pass |

Both mutations were reverted after the run.
