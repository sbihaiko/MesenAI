# Player GUI roadmap — gap survey (2026-10-06)

Read-only survey of PRD Part B (§8, §13.3, §13.5, W-P1..W-P16 plus W-P17/W-P18)
and the accepted GUI ADRs in `docs/adr/gui/`, checked against the code on
`origin/main` at `8a5a9aaa64b0cd130389d2c9978c2002246046ea`. Every `file:line`
below was read with `git show origin/main:<path>`.

## Exclusions applied

- **GitHub Project 4** (all items): #908–#926, #932, #934, #938–#942,
  #951–#955, #963.
- **Closed issues in #908–#963**: dropped.
- **Open issues that already claim a candidate** are listed here as covered,
  not as gaps:
  - #926: real-display pass. This covers the §13.5 PNG look, a real gamepad,
    Hold to Compare on Windows/Linux, W.7 on a GBA ROM, and the pad light on a
    DualShock 4/DualSense.
  - #951: render-to-wireframe comparison. `UI.HeadlessTests/PlayerWireframe.cs:47-56`
    maps only W-P1, W-P2 and W-P4 so far.
  - #952: W-P15 live highlight.
  - #953: drag-and-drop and hand-offs.
  - #934: P.12 "checked" ratification.
  - #963: ADR-0253 Status line and GBA teardown.

## Checked and found shipped (dropped)

- **ADR-0241 / §13.6:**
  - The title-bar recording and job dots: `UI/Views/WorkspaceShellBar.axaml:367-368`.
  - A ROM opened from the OS lands in Play: `UI/App.axaml.cs:145-150`.
- **ADR-0249 Decision 2:** the palette drift test, `UI/Styles/PlayerTheme.axaml:8-9`
  → `UI.Tests/Theme/PlayerThemeDriftTests`.
- **ADR-0249 Decision 5:** render tests exist for every implemented W-P/W-R/W-H/W-X
  screen, including W-P9 (`W-P9-installing`/`-failed`) and W-P17
  (`W-P17-controller-sheet`).
- **ADR-0250, 0252, 0257, 0260, 0261:** each Status line names its tests, and
  the code is present.
- **ADR-0255:** slices 1–5 and the pad light (#925).
- **ADR-0256:**
  - The bridge: `UI/Windows/PlayPadNavigationWiring.cs`.
  - Decision 8: the wizard retired.
  - Decision 9: the ROM picker, `UI/Logic/PlayRomPicker.cs:224`.
- **ADR-0242 W-R8:** disabled by its adoption criterion. This is not a gap.
- **ADR-0243 Decision 5 (Remaster consoles):** `UI/Logic/RemasterScreen.cs:94`.
- **ADR-0246:** named looks and Hold to Compare (pointer and Space).
- **§6.2:** W-P1 and the W-P2 Continue card.

---

## 1. Make the Play settings' sliders, popups and Hold to Compare operable from a pad

- **State:** PARTIAL/UNVERIFIED
- **Answers:** ADR-0256 (Status: *accepted (2026-10-04)… Decision 9 closes it…
  every Play surface is drivable from a pad*). Its stop rule (PRD Part B §8,
  W-P17–W-P18) says: *"with no keyboard and no pointer, every Play path… is
  reachable and reversible from a pad alone."* The wireframes are W-P8 / W-P8b /
  W-P8c (Display, Audio, Controls) and W-P10 (Look, *Hold to Compare*).
- **Evidence:**
  - The bridge's Confirm is `Activate` (`UI/Windows/PlayPadNavigationWiring.cs:624-645`).
    It handles only `RadioButton`, `ToggleButton` and `TabItem`. Everything else
    gets a raised `Button.ClickEvent`, which `Slider` and `ComboBox` ignore.
  - Left/Right always move the focus: `PlayPadNavigationWiring.cs:565-569` and
    `:603-611`. They never change a value.
  - The pad cannot change these controls:
    - `Slider`: `UI/Views/PlayerAudioSettingsView.axaml:25` (Volume),
      `UI/Views/PlayerControlsSettingsView.axaml:24` (Rumble) and `:30` (Deadzone).
    - `ComboBox`/`EnumComboBox`: `UI/Views/PlayerAudioSettingsView.axaml:30`
      (device), `UI/Views/PlayerWindowSettingsView.axaml:24` (aspect) and `:29`
      (scale), `UI/Views/PlayerToolSheetView.axaml:140` (codec).
  - Hold to Compare listens only for pointer press/release and `Key.Space`
    (`UI/Views/LookConfigView.axaml.cs:23-28`). A pad Confirm is a single raise,
    so a hold is impossible.
  - The theme already paints a focus ring for these popups
    (`UI/Styles/PlayerTheme.axaml:945`), so the pad can land on them but cannot
    operate them.
  - Search: `Slider|ToggleSwitch|RangeBase` under `UI/Logic UI/Windows` returned
    only `PlayerSettingsEssentials`/`PlayerSliders` value mapping. No test drives
    `sldAudioVolume|cboDisplayScale|cboAudioDevice|…` by pad: the only hits are
    existence/style asserts in `UI.HeadlessTests/PlayerSettingsEssentialListsTests.cs:64-99`
    and `PlayerThemeSettingsRenderTests.cs:221-224`.
- **What to build:**
  - In the one bridge (`PlayPadNavigationWiring`), never per view (ADR-0256
    non-goal), give the focused control's own value semantics precedence:
    - **Slider:** Left/Right step the value by its `SmallChange`, and the press
      is consumed.
    - **ComboBox/EnumComboBox:** Confirm opens the drop-down. Up/Down walk it,
      Confirm commits, Back closes it without changing the value.
    - **Hold to Compare:** Confirm held means compare on; release means off. The
      bridge already sees press and release ticks.
  - Each rule lives host-free in `UI/Logic/` (ADR-0123) next to `PlayPadNavigation`.
- **Acceptance criteria:**
  - [ ] *(unit, UI.Tests)* A host-free rule answers Step(±1), Open, Commit,
        Cancel or HoldStart/HoldEnd for each control kind and pad action. Left
        or Right on a non-value control still moves the focus.
  - [ ] *(headless, real core)* With a synthetic pad and no keyboard or pointer:
    - Volume moves 37→38→37.
    - Display scale opens, changes and closes, writing the same config as the
      pointer.
    - Back on an open popup leaves the value unchanged.
    - Holding Confirm on *Hold to Compare* calls `SetLookCompare(true)`, and
      releasing it calls `SetLookCompare(false)`.
  - [ ] *(headless)* The focus ring is visible on each of these controls while
        the pad drives it.
  - [ ] *(human at a display, real pad; can join #926)* The drop-down list is
        readable and walkable on screen.
- **blocked-by:** none (ADR-0256 accepted). The real-pad check is external (#926).
- **Actor:** AGENT. The rules and headless tests use the existing synthetic-pad
  path (`UI.HeadlessTests/PlayPadNavigationTests.cs`).

## 2. Light the Controller sheet's port lamp from the slot the pad actually holds

- **State:** PARTIAL/UNVERIFIED
- **Answers:** ADR-0255 (Status: *accepted (2026-10-04)*; slice 3 *"landed…
  (#839)"*, and the Status line itself says: *"Three limits are carried rather
  than solved: … the port light reads the first non-zero field across the port's
  four slots"*). This is W-P17's REMAP section and its two lights per row.
- **Evidence:**
  - `UI/ViewModels/ControllerSheetViewModel.Remap.cs:88-99`: `BoundCode` returns
    the first non-zero code across slots 0–3.
  - `:190-193` feeds that code to `ControllerSheetRemap.Lights`
    (`UI/Logic/ControllerSheetRemap.cs:155`) as the port side.
  - On a port holding a keyboard in slot 0 and the pad in slot 1, a pad press
    therefore lights "the pad sends" but never "the port receives", even though
    the port does receive it.
  - The rebind already joins the pad's own slot (`ControllerSheetRemap.TargetSlot`,
    `:68`, used at `ControllerSheetViewModel.Remap.cs:287`). The light and the
    write therefore disagree on which slot is "the pad's".
  - The other carried limits are already closed: same-button clearing (#941) and
    the Four Score push (#943).
- **What to build:**
  - In the REMAP refresh, resolve the slot that holds the focused pad's device
    block (the same `HoldsDevice`/`FirstSlotHolding` answer the rebind uses).
    Read the row's bound code from that slot, falling back to the first
    non-zero field only when the pad holds no slot on the port.
  - Put the choice in `ControllerSheetRemap` so it is host-free.
- **Acceptance criteria:**
  - [ ] *(unit, UI.Tests)* With keyboard in slot 0 and pad in slot 1, the pad's
        bound code is slot 1's. With no pad slot it falls back to the first
        non-zero field. With pad only, behaviour is unchanged.
  - [ ] *(headless, synthetic pad)* With a mixed keyboard+pad port, pressing the
        pad's A lights both lights on the A row.
  - [ ] *(doc)* ADR-0255's Status line drops this limit from the "carried"
        list. This follow-up is for the implementer, not this survey.
- **blocked-by:** none.
- **Actor:** AGENT. Pure rule plus the existing synthetic-pad headless harness.

## 3. Decide how a pad-only player fills a Play text field (Cheats search, *Add a Code…*)

- **State:** MISSING (no decision, no code)
- **Answers:** ADR-0256 (accepted 2026-10-04): the stop rule *"every Play path…
  reachable and reversible from a pad alone"*. ADR-0245 (accepted 2026-10-02,
  P.10): W-P11 Cheats, with search and *Add a Code…*.
- **Evidence:**
  - The bridge records the limit in place
    (`UI/Windows/PlayPadNavigationWiring.cs:619-623`): *"A TextBox falls through
    to the raise… a pad cannot type… That is a real limit, not a TODO silently
    swallowed here."*
  - The Play text fields are in `UI/Views/PlayerCheatsSheetView.axaml`:
    - `:31` `CheatsSearchBox`
    - `:39` `CheatsIntentBox`
    - `:47` `CheatsKeyBox`
    - `:116-117` `CheatsNewCodeBox` and the description box
  - Searching `docs/adr/gui/0256-*.md` for `TextBox|text field|typing` finds only
    the ROM picker's "no path typing" refusal (`:331`, `:405`, `:435`). No
    decision covers the Cheats fields.
- **What to build:** first a decision, recorded as an ADR or an ADR-0256
  amendment through `/adr`. The options:
  - (a) an on-screen pad keyboard sheet, owned by the bridge;
  - (b) declare code entry a keyboard-only path and say so on the sheet, per
    rule 10;
  - (c) pad-only entry limited to the shapes a code takes (hex digit wheel for
    Game Genie / PAR).
  - Then implement the pick in `UI/` with no per-view navigation code.
- **Acceptance criteria:**
  - [ ] *(owner)* A recorded pick with the user's words quoted, per the
        CLAUDE.md ADR rules.
  - [ ] *(headless, after the pick)* With a synthetic pad only, the Cheats
        sheet either fills and commits a code through the chosen path, or
        shows the one-line reason and the next step.
  - [ ] *(human at a display, real pad)* The chosen entry path is usable from
        the couch.
- **blocked-by:** an owner decision (ADR-0256 amendment).
- **Actor:** HUMAN. Only the owner can choose between adding an input surface
  and narrowing the stop rule.

## 4. Pin ADR-0254's window wiring (focus loss opens W-P4, resume waits for Esc) in a headless test

- **State:** PARTIAL/UNVERIFIED
- **Answers:** ADR-0254 (Status: *accepted (2026-10-04), implemented in the same
  turn*). Its Decision bullets say that `MainWindow.UpdateAutoPause` is
  `FocusPause`'s only caller, that `_focusPausedWithOverlay` holds the resume
  back, and that *"the way back is W-P4's own Esc"*. This is W-P4.
- **Evidence:**
  - The wiring exists at `UI/Windows/MainWindow.axaml.cs:113`, `:1224`,
    `:1254-1255` (opens the overlay) and `:1261` (resume gate).
  - The only tests are `UI.Tests/Play/FocusPauseTests` (host-free rule) and
    `UI.HeadlessTests/FocusPauseDefaultTests.cs:19,27`, which check only that
    the preference defaults to on.
  - Searching `FocusPause|UpdateAutoPause|_focusPausedWithOverlay` under
    `UI.HeadlessTests UI.Tests` returns only those two files. Nothing checks
    that a focus loss in Play opens W-P4, that regaining focus does not resume,
    or that Esc resumes.
  - The ADR's own Consequences flag the risk that this is *"the first time W-P4
    can appear without the player asking"* (toasts, a pause during the load
    card).
- **What to build:**
  - Add a seam for "is the app active" to the poll; today it calls
    `ApplicationHelper.GetActiveWindow()`. Then add a headless class against the
    real core that drives focus loss and regain through it.
  - Assert the overlay, the pause and the held-back resume in Play. Assert
    pause-only in Classic, and nothing with no game loaded.
- **Acceptance criteria:**
  - [ ] *(headless, real core)* In Play with a game, simulated focus loss makes
        the game paused and W-P4 visible. Simulated regain keeps it paused and
        W-P4 up. Esc resumes and closes W-P4.
  - [ ] *(headless)* In Classic: paused, no overlay, auto-resume on regain. In
        Play with no game: no overlay.
  - [ ] *(headless)* A focus loss during the load card does not take a frozen
        frame as a picture (ADR-0254 Consequences).
  - [ ] Run with `MESEN_CORE_LIB` and show a visible `Passed: N`. These classes
        self-skip on CI (see item 5).
- **blocked-by:** none.
- **Actor:** AGENT. A seam plus a headless class. Cmd-Tab on a real desktop
  stays in #926.

## 5. Run the ADR-0249 render gate against a built core in CI

- **State:** PARTIAL/UNVERIFIED
- **Answers:** ADR-0249 Decision 5 (Status: *accepted (2026-10-03)… Each slice
  ships with a render test*). It says: *"The PNGs are kept as CI artifacts and
  attached to UI pull requests, so a person compares them with
  `docs/media/gui-redesign/` before merge."*
- **Evidence:**
  - `.github/workflows/checks.yml:287-288` says the job builds no core: *"No
    MesenCore is built here… so the MainWindow-backed cases self-skip"*. The
    job runs at `:297` with `MESEN_PLAYER_RENDERS` set (`:311`). Its upload
    step (`:316-317`) admits *"tests self-skip, so the folder may be empty"*.
  - Every render class gates on `Assert.SkipWhen(!NativeCore.IsAvailable, …)`
    (for example `UI.HeadlessTests/PlaySheetsRenderTests.cs:196`, 4 guards in
    `PlayerThemeRenderTests.cs`). So the `player-renders` artifact attached to
    a normal PR is empty, and #951's per-region reports have nothing to compare.
  - ADR-0122 invariant 9 keeps core builds out of the host-free job: *"anything
    that needs the `core` makefile target belongs in `build.yml`/`tests.yml`"*.
  - `build.yml` runs only for PRs into `prod` or a manual dispatch, per its
    header and ADR-0191/0200/0203. No existing workflow builds the core for an
    ordinary UI PR.
- **What to build:**
  - A Linux job that builds `MesenCore`, runs the render and wireframe classes
    with `MESEN_CORE_LIB` and uploads the non-empty `player-renders` folder.
  - Place it within ADR-0122 (in `build.yml`, or a new `tests.yml`), on
    whatever trigger the owner picks.
- **Acceptance criteria:**
  - [ ] *(owner)* A recorded trigger choice: per UI PR (CI cost, against the
        ADR-0191/0200 cut-down) or on dispatch/`prod` only.
  - [ ] *(CI)* On that trigger the render classes report `Passed: N > 0`, not
        skipped, and the artifact holds a PNG plus a `.wireframe.md` per W-id
        that #951 maps.
  - [ ] *(CI)* A deliberately broken theme token turns the job red.
- **blocked-by:** an owner decision on the trigger. ADR-0122 (placement) and
  ADR-0191/0200/0203 (CI cost policy) constrain it.
- **Actor:** HUMAN for the trigger, which is a cost and policy pick that
  touches accepted CI ADRs. The workflow itself is agent work once picked.

## 6. Give Remaster's "not this project's game" reason its *Open the Right Game…* button

- **State:** PARTIAL/UNVERIFIED
- **Answers:**
  - W-X2 (PRD Part B §13.5.5, line 4104): *"Remaster ⚠ This is not the game the
    project was recorded from. [Open the Right Game…]"*.
  - §13.3 rule 10: *"the screen says what to do, in one sentence, with the
    button that does it"*.
  - ADR-0241 (accepted 2026-10-02) and ADR-0249 (accepted 2026-10-03), whose
    PNG/ASCII are the spec.
- **Evidence:**
  - The reason exists as text only: `UI/Localization/resources.en.xml:2315`
    (`RemasterReasonNotThisProjectsGame`). `UI/Logic/RemasterScreen.cs:161-162`
    and `:178-179` set it as the disabled reason of Record and Prepare Figures.
  - Searching `right game|RightGame|Open the Right Game` (case-insensitive)
    under `UI/` on origin/main finds no matches.
  - The other W-X2 rows do have their buttons: *Try Again* in W-P6 and the
    Share host message.
- **What to build:**
  - On W-R1, when a project is open and the loaded game is not its own, show
    the reason with *Open the Right Game…*.
  - The button loads the project's ROM, which is found by the project folder's
    ROM name (the folder is named after the ROM file) in recents or the games
    folder. When no file matches, it falls back to Play's in-app ROM picker
    (`PlayRomPicker`) rooted at the games folder.
  - Keep the rule host-free in `UI/Logic/RemasterScreen` (the next step when
    the reason is `NotThisProjectsGame`).
- **Acceptance criteria:**
  - [ ] *(unit, UI.Tests)* `NotThisProjectsGame` yields the next-step action.
        With the matching ROM in recents it resolves that path; with none it
        resolves "open the picker".
  - [ ] *(headless, real core, synthetic NROM)* With project A open and ROM B
        loaded, W-R1 shows the sentence and the button. Clicking it loads A's
        ROM, Record becomes enabled, and the workspace stays Remaster (rule 5).
  - [ ] *(render)* A `W-X2-remaster` render is saved next to the existing
        `W-X2` ones.
- **blocked-by:** none.
- **Actor:** AGENT. Host-free rule plus an existing Remaster headless harness
  (`UI.HeadlessTests/RemasterWorkspaceTests.cs`).

---

## Not listed, and why

- **ADR-0256 Decision 9 native dialogs:** W-P13 BIOS
  (`UI/Views/PlayBiosSheetView.axaml.cs:38`) and W-P16 pack file
  (`UI/Views/PlayPackDepSheetView.axaml.cs:39`) still open native dialogs. This
  is decided, not a gap: *"Every other file choice in the app… stay native."*
- **W-X1 "Stop recording? [Keep Going] [Stop]":** W-R2 and the G.3 record both
  specify a direct Stop that keeps the recording. That is a spec inconsistency
  for a doc pass, not a code gap.
- **W-R8 render:** the screen is not implemented, by ADR-0242 Q3's criterion.
- **Render coverage gaps** (W-P3, W-P10 main tab, W-S1..W-S3 in the
  comparison): these are #951's scope.
