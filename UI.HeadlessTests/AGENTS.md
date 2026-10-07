# AGENTS.md — UI.HeadlessTests

Headless Avalonia XAML-wiring tests (ADR-0150, accepted 2026-09-03). This
project is the deliberate opposite of `UI.Tests`.

## What this project is for

Instantiate the app's real windows/views under `Avalonia.Headless` (null
windowing + Skia render, no display server) and assert on the realized
visual tree: a named card is on screen, a tab is hidden, focus moves on a
key press, a style class restyles a control. It covers the step that no
host-free test can: the crossing from a rule into XAML.

## The firewall (do not blur this)

- `UI.Tests` dual-compiles `UI/Logic/**` and is host-free (ADR-0123): no
  Avalonia package, no `ProjectReference`, no `RuntimeIdentifier`. Its
  guarantee must stay intact.
- This project **does** reference `UI/UI.csproj`. The two are separate on
  purpose and must never be merged.
- **Scope rule: wiring only.** Assert `IsVisible`/`IsEnabled`/focus/
  bindings/presence on the visual tree. NEVER write a rule assertion here
  that `UI.Tests` can make host-free — that would put the logic behind an
  Avalonia dependency for no gain. New rules go into `UI/Logic/` first,
  where `UI.Tests` asserts them; this project only checks the wiring.
- The rules themselves are already covered host-free. If a test here is
  the *only* thing asserting a decision, it is in the wrong project.

## Render gate (ADR-0249)

`TestAppBuilder` renders with Skia (`UseHeadlessDrawing = false`) and loads
Inter, so `CaptureRenderedFrame` returns a real bitmap.
`PlayerRender.Save` writes it to `player-renders/` beside the test assembly
(or `$MESEN_PLAYER_RENDERS`) and prints the path; `PlayerRender.Pixel`
reads it back. `PlayerThemeRenderTests` (W-P1, W-P2, W-P4) and
`PlayerThemeScopeTests` (the classic/Player boundary) assert font, size,
radius, tint and background against the theme tokens; the PNGs are for a
person to compare with `docs/media/gui-redesign/W-*.png` and are not
pixel-diffed.

## Native core containment

`MainWindow`'s constructor calls `EmuApi.InitDll()` before its XAML loads,
and some views call into the native `MesenCore` on construction or focus.
This project does **not** fake the core (a stub would answer with values
the real core never returns). Instead `NativeCore.cs` loads the REAL
library when this checkout has one built and, when it does not, the
affected tests skip with an explicit reason via `Assert.SkipWhen`.

`unit-tests.yml` never builds `MesenCore` (ADR-0131), so on CI only the
core-free wiring tests run (`PlayerSettingsTabsTests`,
`ControllerHighlightTests`) and the rest self-skip. That is the intended
posture — a documented skip is not a muted failure. Run `make core` and
then `make headless-ui-tests` locally to exercise every case.

## Running

```sh
make core             # once, so NativeCore finds a built library
make headless-ui-tests
# or, to reproduce the CI runner's "no core" state:
MESEN_CORE_LIB=none dotnet test UI.HeadlessTests/UI.HeadlessTests.csproj \
  -p:RuntimeIdentifier=$(MESENPLATFORM) --nologo
```

`UI/UI.csproj` hardcodes `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`;
override it with `-p:RuntimeIdentifier=<rid>` (the makefile does this via
`$(MESENPLATFORM)`). `DefineConstants=TRACE` (DEBUG off) matters:
`App.Initialize()` attaches Avalonia developer tools under `#if DEBUG`, and
headless builds a fresh `Application` per test — the second attach throws.

## Real-coordinator install proof

`PackAudioNoticeInstallTests` (ADR-0240 / F6.9) drives the real
`CommunityPackInstallCoordinator.Install` over a fixture hd-legacy zip and a
synthetic NROM (needs a built core; skips otherwise) and asserts the notice on
the outcome and in `EmuApi.GetLog()`. It points `ConfigManager._homeFolder` (by
reflection) at a temp folder so the install registry and cache never touch the
user's real home. The decision itself is pinned in `UI.Tests`; this checks
wiring only. `CommunityPackInstallStaleLoadTests` (#657) swaps
`CommunityPackInstallCoordinator.ReadCurrentLoad` to open game B while game
A's Restore/auto-install is in flight and checks B's `mep/` and registry key
survive (restore the probe in `Dispose`).

## Test hygiene

- **Every test class that reaches the native core carries
  `[Collection(NativeCoreCollection.Name)]`** (#432). The core is one
  process-global emulator, and xUnit v3 runs classes in parallel, so two
  classes calling `InitializeEmu`/`LoadRom`/`Stop` at once swap each other's
  ROM. `NativeCoreCollectionGuardTests` walks the IL (into app code too) and
  fails when a class that reaches `NativeCore` or an `Interop` P/Invoke type
  lacks it. Core-free classes stay out of it and keep running in parallel.
  When the only path the walk finds is an app-code branch the test's
  arguments never take, mark the class `[NativeCoreFree("<why>")]` instead;
  CI runs this project with no core, so a wrong claim fails there.

- **Open a `MainWindow` with `window.ShowStarted()`, never bare `Show()`**
  (#619). Its startup, recent-game previews and Remaster/Share gate
  measurement post to `Dispatcher.UIThread` from the thread pool. Avalonia
  resets the UI dispatcher between tests and re-creates it on whichever
  thread reads it first, so a post that outlives its test hands the
  dispatcher to a pool thread and the next test's setup throws "The calling
  thread cannot access this object". `ShowStarted` waits for the startup;
  the assembly-level `SettleMainWindows` attribute waits for the rest after
  each test (`MainWindowStartup.cs`). New background work a test triggers
  that posts back needs the same wait. A test that observes the startup
  itself uses `ShowUnstarted` and waits for `Startup` on its own; the
  settle step waits for it too.

- A headless test must detect the defect it targets. If it would pass
  without the XAML under test, it is a property-getter assertion in
  disguise — strengthen it or delete it.
- Prefer `IsEffectivelyVisible`/`IsOnScreen()` over `IsVisible` when the
  assertion is about the user seeing something: `IsVisible` is a local flag
  and a collapsed parent hides a control with `IsVisible=true`.
- `MainWindow.axaml` changes that only add `x:Name`/automation ids so a
  test can find a control are acceptable here; a change that alters layout
  or behaviour to satisfy a test is not.

**Wireframe regions (#951).** The comparator is host-free in
`UI/Logic/PlayerWireframe.cs` (with `RgbFrame` and a BCL-only PNG reader) and
its rules are asserted in `UI.Tests/Theme/PlayerWireframeTests.cs`: it crops
the wireframe's window box, scales it to the render and compares named
regions — dominant colour (CIE76 ΔE ≤ 10), ink-box edges (≤ 8 logical px) and
text-line bands (same count, centres ≤ 8 px). Never a pixel diff. Boxes are
clamped to the render, so a smaller window reports instead of throwing. This
project only feeds it real renders: `PlayerRender.Save` of a `W-P*` render
writes `<name>.wireframe.md` against the wireframe its name resolves to by
W-id prefix (`W-P4-save-states` → `W-P4`), or a "no wireframe" line, and
never fails the test over the report. `PlayerThemeRenderTests` gates its
screens through `PlayerWireframe.Gate`: a region passes, or it is a known
deviation (`PlayerWireframe.KnownDeviationsOf`) that must still fail on its
named failure kind — closing the gap means promoting the region; failures of
other kinds on that region (seeded data, port chips) are not gated. CI has no
core, so `UI.Tests` gates the committed renders in
`UI.Tests/Theme/PlayerRenders/` instead; re-commit a render there when its
screen changes.
