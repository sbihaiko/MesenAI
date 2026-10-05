# ADR-0150: Avalonia.Headless for XAML-wiring tests (Welcome/Continue cards, menu visibility, enhancements panel)

- Status: accepted (2026-09-03, by the user) — reflected in `UI.HeadlessTests/` (landed the same day as wave 2 of `docs/validation/manual-validation-automation-plan.md`; PRD Part A ADR table row ADR-0150); no PRD slice pending
- Date: 2026-09-03
- Origin: `docs/validation/manual-validation-automation-plan.md` (step 7 of "Proposed execution order"; the plan's "Rejected suggestions" table lists `Avalonia.Headless` as the right long-term answer for XAML wiring but explicitly refuses to adopt it as a plan step, because it changes unit-test/CI wiring and CLAUDE.md routes that through an ADR).
- Related: ADR-0123 (UI/Logic host-free firewall: the dual-compile is the authoritative gate), ADR-0130 (core unit-test binary gitignored), ADR-0131 (`unit-tests.yml` contract invariants), ADR-0137 (repo-hygiene checks wired into make/CI), PRD Part B §5–§6 (Player shell, pack picker, enhancements quick-toggle panel).
- Decision taken 2026-09-03: the user chose to adopt the headless Avalonia test host. The "Proposed decision (not decided)" wording below is kept as written for the record; it is now the accepted decision, and the "Unknowns to resolve" list is the implementation's checklist.

## Context

`UI.Tests/UI.Tests.csproj` is deliberately host-free (ADR-0123): no `RuntimeIdentifier`, no `ProjectReference` to `UI/UI.csproj`, no Avalonia package. It dual-compiles exactly three things — `../UI/Logic/**/*.cs`, `../UI/Interop/InteropEnums.cs`, and its own tests — so an accidental `Avalonia`/`EmuApi` dependency fails `dotnet test` on any OS with no SDL2 and no `MesenCore` native library present. `make unit-tests` and the `ui-tests` job of `.github/workflows/unit-tests.yml` both run that one project (preceded by `scripts/verify-ui-logic-firewall.sh`) on a plain `ubuntu-latest` runner.

That boundary buys cheap, portable tests and forced every recent Player shell rule into a host-free helper — `PlayerEnhancementsToggle`, `PlayerPackPicker`, `UiModeDefaultRule`, `PackPreferenceResolver`, `CommunityCatalogUpdateDecision`, all covered. What is **not** covered is the step after the rule: whether the XAML consumes it. `ShouldShowWelcomeCard` / `ShouldShowContinueCard` are unit-tested, but not that the cards in `MainWindow.axaml` are bound to them, in the visual tree and toggling visible/collapsed. `PlayerChrome.IsMenuVisible(...)` (consumed by both `MainWindowViewModel` and `MouseManager.UpdateMainMenuVisibility()`) will be tested, but not that `IsMenuVisible` reaches the menu bar's `IsVisible`. `SupportsOverclock` is tested, but the SMS Overclock checkbox binding it to `IsEnabled` (disabled, not hidden) is only "a one-line code-review item, not a test". A grep for the binding string proves the markup was written, not that the path runs (the plan's first standing rule), so PRD Part B items keep landing with "manual" in their Acceptance column and every `MainWindow.axaml` edit re-opens checks already passed by hand once.

`Avalonia.Headless` (and `Avalonia.Headless.XUnit`, which supplies `[AvaloniaFact]`/`[AvaloniaTheory]` and a UI-thread dispatcher) runs a real Avalonia application with a null windowing/rendering backend, in-process, under `dotnet test`: controls instantiated, styles applied, bindings evaluated, visual tree walked, no display server. The app already targets Avalonia 12.1.1 (`UI/UI.csproj`), whose headless packages exist at the matching version. The blocker is the wiring: testing `MainWindow.axaml` means referencing the assembly that contains it, and `UI/UI.csproj` also contains `UI/Interop/EmuApi.cs` — ~60 `DllImport`s against `MesenCore`. A `ProjectReference` therefore drags the native build, the desktop Avalonia stack and the platform RID into a job whose entire value today is needing none of them — a change to the unit-test and CI wiring, exactly the class CLAUDE.md routes through an ADR.

## Proposed decision (not decided)

Adopt a **second, separate** test project — `UI.HeadlessTests` — rather than adding Avalonia to `UI.Tests`:

1. **`UI.Tests` is untouched.** Its csproj keeps no Avalonia package, no `ProjectReference` and no `RuntimeIdentifier`; ADR-0123's dual-compile stays the authoritative host-free gate and `scripts/verify-ui-logic-firewall.sh` keeps scanning the same set.
2. **`UI.HeadlessTests/UI.HeadlessTests.csproj`** references `Avalonia.Headless` + `Avalonia.Headless.XUnit` at the app's Avalonia version and `ProjectReference`s `UI/UI.csproj`. Its scope is *wiring* only: instantiate a control or window, set the view-model properties the host-free rule would produce, and assert on the visual tree (`IsVisible`, `IsEnabled`, presence of the named card). It never asserts a rule `UI.Tests` can assert host-free — duplicating the rule there would put the logic behind an Avalonia dependency for no gain.
3. **P/Invoke containment.** The tests must not call `EmuApi`. Whether a headless `MainWindow` can be constructed without the native library loading is the open feasibility question (below); the fallback is to test the smaller `UserControl`s (the enhancements panel, the Welcome/Continue cards) in isolation instead of the whole window.
4. **Wiring.** A new `make headless-ui-tests` target, and a **separate job** in `.github/workflows/unit-tests.yml` — not a step appended to `ui-tests`, so a headless-host failure never reds the cheap host-free leg and ADR-0131's contract invariants for that job stay readable. The job installs .NET and, if the reference forces it, builds or stubs the native library; if it cannot run without a real `MesenCore` build, it is `continue-on-error: false` but gated to the platforms that already build it, and this ADR must say so explicitly before it is accepted.
5. **Scope cap.** Headless tests cover binding/visibility wiring. They do **not** replace the two checks the plan classifies as genuinely visual — 16:9 stretch (viewport geometry in `VideoRenderer`, not in the Avalonia tree) and the F6.5 installer GUI flow.

### Unknowns to resolve before this can be accepted

- Whether `MainWindow`/`MainWindowViewModel` can be constructed under the headless backend without `MesenCore` loadable, or only leaf controls are reachable — decides whether item 3's fallback is the main path.
- Whether the CI job can stay on `ubuntu-latest` without a native build, or becomes a matrix leg attached to an existing build job.
- Whether AOT/trim parity (`IsAotCompatible`, the `JsonSerializerIsReflectionEnabledByDefault=false` line `UI.Tests` carries) is meaningful for a project that references the app rather than dual-compiling it.

A spike answering the first two, on a throwaway branch, is the cheapest way to make this ADR decidable.

## Consequences

- Welcome/Continue cards, menu visibility and the enhancements panel move from "manual GUI pass" to an assertion on every push; the PRD Part B rows citing them can name a test instead of a human.
- Future `MainWindow.axaml` edits get a regression net; the recurring "manual GUI run pending" note in P.4/P.5/P.7 stops accumulating.
- Cost: a second test project kept at Avalonia-version parity with `UI/UI.csproj`, a CI job materially more expensive than the host-free one, and a `ProjectReference` coupling test runtime to the native library's build state.
- Risk: a flaky or `MesenCore`-dependent headless job will be muted rather than fixed, worse than the honest manual pass — ADR-0137's "a documented-but-unrun guardrail is worse than none" applies unchanged.
- Ongoing pressure to write new rules directly against the visual tree instead of extracting them into `UI/Logic/`. The §2 scope rule ("wiring only, never the rule") is the mitigation and would need restating in `UI.Tests/AGENTS.md` and the new project's `AGENTS.md`.

If not adopted, those three checks stay in a written manual checklist, re-run on each release rather than each push.

## Alternatives considered

- **Status quo — manual GUI pass** (the plan's step 6). Zero cost and honest, but it does not scale: the same three checks are re-run by hand after every shell change, and nothing catches a binding deleted in an unrelated refactor. The real fallback if the spike shows the headless host cannot run without a native build.
- **Drive the real macOS window via Accessibility/AppleScript** and screenshot it. Rejected in `docs/validation/manual-validation-automation-plan.md`: Avalonia exposes no native AppKit control hierarchies, so element targeting is unreliable for a non-native toolkit; macOS-only, while CI is Linux.
- **Add Avalonia.Headless to `UI.Tests` itself.** Rejected: it would put an Avalonia package reference and a `ProjectReference` to `UI/UI.csproj` into the very project whose absence of both is ADR-0123's guarantee, and `verify-ui-logic-firewall.sh` checks for exactly those two markers.
- **Extract more into `UI/Logic/` and accept that markup is untested.** Worth continuing, but it cannot reach this ADR's question — the untested step is the one crossing from logic into XAML.
- **Screenshot comparison via the headless frame capture.** Possible once a host exists, but a pixel baseline is brittle across themes, fonts and Avalonia versions. Out of scope; visibility and enabled-state assertions are what the three checks need.
