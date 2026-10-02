# ADR-0241: Organize the GUI around Play, Remaster, and Share workspaces

- Status: accepted (2026-10-02). The user requires a GUI specialized for the README's three profiles, and accepted this model the same day (*"Aceitar"*), after two review rounds on PRD Part B §13. The work is cut into PRD Part B §8 slices, one at a time; each slice waits for an explicit go-ahead. **G.1 (the shell) implemented 2026-10-02** under the user's go-ahead (*"sim, pode seguir. depois que tudo estiver no main, pode implementar usando paralelismo de tudo que puder"* and *"pode implementar em paralelo tudo que puder"*, 2026-10-02): the switcher (Play, Remaster, Share), Tools ⋯, the status line and `ShowClassicMenuBar` (record in PRD Part A §3); `UI/Logic/WorkspaceShell.cs` is unit-tested and the wiring is in `UI.HeadlessTests/WorkspaceShellTests.cs`. An upgraded install also starts with the classic bar hidden, with a one-time toast (see "Amended" below and PRD Part B §13.8 Q4). G.1 also retires the P.4 rule that gated the Debug menu on `UiMode` (user's choice, *"Aceitar (Recomendado)"*, 2026-10-02): the `UiMode` values keep their meaning and are not reinterpreted as workspaces; they simply no longer gate Debug, by that explicit decision. On macOS the shell bar is drawn in the window's title bar, as in the W-S1–W-S3 wireframes (user's choice, *"Integrar agora"*, 2026-10-02); Windows and Linux keep it as an in-window strip under the system title bar. **G.2 (the Play home W-P1–W-P3 and the W-P4 pause overlay) implemented 2026-10-02** under the user's go-ahead (*"sim, pode seguir. depois que tudo estiver no main, pode implementar usando paralelismo de tudo que puder"*, *"pode implementar em paralelo tudo que puder"* and *"pode seguir com a segunda leva em paralelo"*, 2026-10-02): W-P1 replaces the P.7 Welcome card, W-P2 is *Continue playing* + *Open a ROM…* + the other recent games, and W-P4 replaces the P.4 overlay with seven controls (Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game) — *Advanced GUI* and quitting the app moved to Tools ⋯, and Esc goes game → W-P4 → resume (record in PRD Part A §3); `UI/Logic/PlayHome.cs` and `UI/Logic/PlayPauseOverlay.cs` are unit-tested and the wiring is in `UI.HeadlessTests/PlayHomeViewTests.cs` and `PauseOverlayViewTests.cs`. **G.3 (the Remaster project screen and recording) implemented 2026-10-02** under the user's go-ahead (*"sim, pode seguir. depois que tudo estiver no main, pode implementar usando paralelismo de tudo que puder"*, *"pode implementar em paralelo tudo que puder"* and *"pode seguir com a segunda leva em paralelo"*, 2026-10-02): W-R0, W-R0b, W-R1, W-R2 and W-R3 of PRD Part B §13.5.3, per ADR-0243's project folder (record in PRD Part A §3); the rules in `UI/Logic/Remaster*.cs` are unit-tested in `UI.Tests/Remaster/`, and the wiring is in `UI.HeadlessTests/RemasterWorkspaceTests.cs`. Share remains a placeholder; Remaster's build-and-show (W-R4+), tile browser and W-R8 are not implemented.
- Date: 2026-10-02
- Amended: 2026-10-02 — upgrade sentence aligned with PRD §13.8 Q4, user's choice verbatim: *"Some + toast (Recomendado)"*.
- Related: PRD Part B §6 (Player/Advanced chrome), §13 (three-workspace proposal); ADR-0146 (automatic community packs), ADR-0147 (editable packs), ADR-0183 (artist surfaces), ADR-0209 (selection and external painting), ADR-0150 (UI wiring tests).
- Supersedes / amends: amends the Player/Advanced-only navigation model in PRD Part B §6, not pack formats, discovery, identity, or community acceptance rules.

## Context

The README has three task-oriented entrances: play a game, remaster a game,
and submit a pack the contributor made or found. The GUI's Player/Advanced
split answers a different question: how many technical controls to expose.
An artist is not necessarily an emulator expert, and someone submitting a
link needs neither a ROM nor an authoring project.

Three landing-page cards alone would reproduce this mismatch after the first
click. Each entrance needs a distinct task hierarchy and next action. People
may perform all three tasks, so the entrances must not become account roles,
permissions, separate installations, or mutually exclusive settings profiles.

This is a navigation decision. It does not choose a new authoring file format,
job transport, embedded paint editor, credential store, or publishing backend.

## Decision

Recommend **three reversible task workspaces in the existing Avalonia app**:

1. **Play** centers the current game, Continue/Open ROM, game controls, and
   the effective enhancements. A ROM opened through the operating system goes
   directly here; onboarding never blocks it.
2. **Remaster** centers a local project and the loop Prepare → Record →
   Browse art → Edit externally → Build and preview → Package. The primary
   units are figures, scenery, stage maps, and pattern pages, not raw tile
   keys. Existing external authoring tools remain external.
3. **Share** centers a pack link or a locally prepared artifact, the three
   submission fields, local checks when available, and the handoff to the
   existing GitHub Issue Form. It explicitly supports “I found this pack,”
   does not require a ROM for link submission, and does not imply authorship.

**One profile at a time.** The window shows exactly one workspace. The
title bar names the active one (glyph, name, chevron); the other two appear
only inside the switcher popover that button opens, always in the order
1. Play, 2. Remaster, 3. Share (user's decision, 2026-10-02). No surface shows another
workspace's controls; a link that leads to another workspace names it ("opens
Share") and the click is the switch. The one silent switch is opening a ROM
from the operating system, which lands in Play. Showing all three as
permanent tabs was the first draft and was rejected on review (2026-10-02,
user's words: *"a GUI DEVE apresentar um profile por vez, e nao todos ao
mesmo tempo"*).

Switching changes task context without restarting the emulator, stopping a
recording or job, rewriting settings, choosing another pack, or publishing
anything. Each workspace keeps its local navigation position. While a game
runs in Play the title bar is hidden; Esc brings it back with the pause
overlay.

**Advanced tools are an escape hatch, not a fourth audience.** Preserve the
classic menus, debugger, Lua, and specialist windows. On upgrade the classic
entry experience stays reachable through Tools ⋯ and its *Show classic menu
bar* toggle; the bar itself starts hidden, and the upgraded install gets a
one-time "your menus are under Tools ⋯" toast (PRD Part B §13.8 Q4,
amended 2026-10-02); opening a specialist tool from a task
workspace does not require abandoning the workspace. Fresh installations
remain immediately playable. Persisted workspace selection is separate from
technical-tool visibility; the existing `UiMode` values must not be silently
reinterpreted as the new workspace values.

The workspaces consume the same emulator, settings, pack identity, installer,
and validation behavior. Workflow adapters may coordinate existing operations,
but must not reimplement pack matching, precedence, import, build, or lint in
view code. Native Remaster integration must pass a packaged-app feasibility
gate before promising a terminal-free authoring journey.

PRD Part B §13 specifies the proposed screens, transitions, destructive-action
boundaries, capability limits, migration behavior, and acceptance scenarios.
That section is the product proposal; this record owns the architectural
choice between task workspaces and skill-level modes.

## Alternatives

- **Keep Player/Advanced and add three home links:** least change, but the
  artist still has to translate their task into emulator internals. Links
  alone do not deliver specialized navigation.
- **Create three applications:** provides separation at the cost of duplicated
  configuration, pack state, lifecycle handling, and deployment. It makes the
  edit/preview loop cross applications for no user benefit.
- **Three permanent tabs (always-visible switcher):** the first draft of
  this record. Rejected: every screen carries two destinations its user did
  not come for, which is the complexity the redesign exists to remove.
- **Assign a permanent profile at first launch:** simple to persist, but wrong
  for a player becoming an artist or an artist submitting an existing pack.
  It also adds friction before the first ROM.

## Consequences

- One app gains three information architectures and more navigation-state
  tests. Shared commands and explicit workspace transitions limit drift.
- Play stays small while Remaster and Share gain first-class entry points;
  complexity moves to the task that needs it rather than disappearing.
- Existing artist tools need discoverable orchestration and packaged-runtime
  support. Their existence as scripts is not evidence of a complete GUI.
- Automatic download of accepted packs remains automatic (ADR-0146).
  Public submission remains an explicit browser action. These are different
  trust boundaries, not inconsistent consent rules.
- Artist edits, active recordings, and pending build output need explicit
  guards when operations would replace files or change emulation state.
  Merely switching workspaces is not such an operation.
- No claim of universal remastering support follows from console playback
  support. Each action must use the actual console/tool capabilities.
- Approval of the architecture must be followed by bounded PRD slices and
  explicit implementation authorization. A proposed screen is not a shipped
  capability, and automated UI checks do not replace human usability trials.
