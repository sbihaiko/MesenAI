# UI/

## Purpose

.NET (Avalonia) desktop UI application — windows, ViewModels, config, and
the native interop bridge to Core via `EmuApi`. `UI/Logic/` is a carved-out,
host-free subset consumed by `UI.Tests` (delivered in Phase 0/1 of the
now-completed unit-test plan — see git history for
`docs/roadmap/plano-testes-unitarios.md`). `UI/Services/` (F6.4b-2,
ADR-0138) is the opposite: host-aware orchestrators (network/File
I/O/`EmuApi` all allowed) that drive `UI/Logic/`'s host-free decision
types instead of reimplementing them.

## Ownership

The main UI/ViewModel codebase is owned by the application as a whole.
`UI/Logic/` specifically is owned by the completed unit-test
test-infrastructure effort: it exists so ViewModel parsing/validation logic
can be exercised by real xunit tests without Avalonia or the native
`MesenCore` library.

## Local Contracts

- **`UI/Logic/` is the host-free boundary** (ADR-0123): every file under
  `UI/Logic/*.cs`, and the dual-compiled `UI/Interop/InteropEnums.cs`, must
  stay free of `Avalonia` and `EmuApi` references — BCL (plus
  `System.IO.Compression` where needed) only. This is what lets
  `UI.Tests/UI.Tests.csproj` dual-compile the same files (via
  `<Compile Include>`, no `ProjectReference` to `UI.csproj`), so any
  accidental UI/native dependency breaks `dotnet test` immediately rather
  than only being caught in review (see `UI.Tests/AGENTS.md`). Since
  ADR-0123 aligned the test csproj with `UI/UI.csproj`'s compile strictness
  (no `ImplicitUsings`, `TreatWarningsAsErrors`), the dual-compile is the
  authoritative contract; `scripts/verify-ui-logic-firewall.sh` is the fast
  pre-check that names the offending file and dependency with a readable
  diagnostic before `dotnet test` fails opaquely (run by `make unit-tests`
  and the `ui-tests` CI job, and by `make doc-checks`).
- `UI/Services/*.cs` (ADR-0138 §37/§41, F6.4b-2) is the host-aware layer
  that drives the host-free `UI/Logic/Community*` decision classes -
  `HttpClient`/`Avalonia`/`EmuApi` are all allowed there, and
  `scripts/verify-ui-logic-firewall.sh` enforces the three-layer rule
  (ADR-0138 §53) in both directions: `UI/Logic/*.cs` must stay free of
  `Avalonia`/`EmuApi`/`HttpClient`/`Mesen.Services`, and `HttpClient` under
  `UI/` is confined to `UI/Services/*.cs` (plus the pre-existing
  `UpdatePromptViewModel`) — never Windows code-behind or ViewModels. Every
  community-pack GET goes through `CommunityPackDownloader` (§50). A
  `kind: "mediafire"` URL fetches the share page then the
  `downloadN.mediafire.com` href (`CommunityPackMediaFire.ExtractDownloadUrl`);
  the CDN hop is allow-listed via `host_ends_with`.
- **Tools > Live Recorder offers Record/Stop only** (ADR-0169 §4, amended
  2026-09-23): the emulator UI never launches `scripts/record_viewer.py`
  and has no menu entry for it — the viewer is a developer/diagnostic tool
  run by hand that attaches to the same `LiveRecording` slot. The entries
  come from the host-free `LiveRecorderMenu.Entries`, which
  `MainMenuViewModel.GetLiveRecorderMenu` maps one-to-one. Guarded by
  `UI.Tests/Recording/LiveRecorderMenuTests.cs` (the list is exactly
  Record then Stop, no viewer value) and
  `UI.HeadlessTests/LiveRecorderMenuTests.cs` (the realized MenuItem tree;
  self-skips without a built core).
- `UI/Logic/*.cs` types return plain, host-free records/DTOs — never
  `ViewModelBase`/`ObservableObject` subtypes. The owning ViewModel maps
  the result into its UI-facing type (e.g. `MepPackListEntry` →
  `MepPackEntry`).
- `MepPackListParser.Parse(string)` mirrors `EmuApi.GetMepPackList()`'s TSV
  contract exactly: newline-separated rows; a `!`-prefixed row is a
  rejection message (leading `!` stripped, accumulated into
  `MepPackListResult.RejectedInfo`); a tab-separated row maps to a
  `MepPackListEntry` (origin `2`/`1`/other → `sibling`/`zip`/`folder`);
  any row with fewer than 8 columns is silently ignored. Columns 9–10
  (P.3: `pack_id`/`content_id` from the container's `.mep-install.json`,
  empty for a stamp-less container) and column 11 (issue #150: `isAutoOnly`,
  `"1"` when a sibling folder contains only F5 bootstrap output under `auto/` with
  no `HasHuman` content, `"0"` otherwise) are optional — an 8-column row from an
  older core still parses. The `Sections` field is passed through raw — the
  `","` → `", "` display formatting stays in the ViewModel, not in this parser.
- `PackPreferenceResolver` (P.3, PRD Part B §5, ADR-0140/0141) is the
  host-free per-ROM choice resolver: `DerivePackId` (a candidate's
  `.mep-install.json` pack_id, else the `local:<container>` rule-4 fallback)
  and `Resolve` (the §5 content_id merge — a container duplicating another's
  content_id is the same pack, not a second entry — plus preference →
  winning container; lexicographic default when no preference or a stale
  one). It never touches `Avalonia`/`EmuApi`/config; the owning ViewModel
  reads the stored preference from `EnhancementPackConfig.RomPackPreference`
  (romSha1 → pack_id) and maps the result to its UI type. The core enforces
  the same decision per ROM via `MepPackManager::FindPreferredPack` (the
  preference is pushed at config-apply through `SetPreferredMepPack`/
  `ClearPreferredMepPacks`).
- `UiModeDefaultRule` (P.4, PRD Part B §6) is the host-free one-shot
  default for `PreferencesConfig.UiMode`: no settings.json at startup (the
  `Configuration.CreateConfig` path) → `Player`; an existing keyless
  settings.json (a pre-Player upgrade) keeps `Advanced` — which is also the
  property initializer value, so a missing key never degrades to Player. The
  key is written on first save. `UiMode` (enum: `Advanced`, `Player`) lives
  here too so both the UI project and UI.Tests share it.
- `UiModeShortcutPrecedence` (P.4, §6) resolves the Esc collision inside the
  shortcut config: the core fires every pressed shortcut, so in Player mode
  the `ToggleOverlay` shortcut owns its key(s) — `PreferencesConfig.ApplyConfig`
  suppresses any Pause/other binding on the same combination before pushing
  the shortcut list to the core. Advanced mode filters nothing; the overlay
  press is ignored by `ShortcutHandler` there. The enum `ToggleOverlay` is
  mirrored in the core (`Core/Shared/SettingTypes.h`) because the shortcut id
  crosses the interop boundary by value; the core also exempts it from the
  keyboard-block in `ShortcutKeyHandler::IsKeyPressed` so it stays reachable
  in keyboard games.
- `WorkspaceShell` (G.1, ADR-0241, PRD Part B §13.2) is the host-free
  shell model: the `Workspace` enum (`Play`, `Remaster`, `Share` — that
  fixed order is the switcher's and the ⌘1/⌘2/⌘3 digits'; ADR-0250 adds
  `Classic`, ⌘4), `WorkspaceState` (one active workspace; an undefined
  persisted value falls back to `Play`), `IsBarVisible` (the bar hides
  while a Play game runs unpaused, and always in Classic) and
  `ShellStatusLine` (the status line's parts). Classic owns
  `UiMode.Advanced`: entering it sets Advanced, a task door sets Player.
  Switching never pauses or stops the game; outside Play the native
  renderer and every Play surface are hidden. Each task door's Tools ⋯ is
  its own short menu from `WorkspaceMenu` (ADR-0250), whose guards are an
  entry no door places and a duplicate within a door. Its menu style
  templates `ActionIcon.Source`, because one `Image` cannot have two visual
  parents. The Player row template draws `-` separators itself: a style
  keyed on Header throws while the items attach.
  `ShellTitleBar` decides the macOS title-bar integration: only macOS
  extends the client area (height hint = the 52 px bar), the bar background
  carries `WindowDecorationProperties.ElementRole="TitleBar"` and its two
  controls `User`, the leading inset clears the traffic lights except in
  fullscreen, and `MainWindow.InitShellTitleBar` moves the bar to the top
  row so the optional classic bar sits under it.
- `PlayHome` and `PlayPauseOverlay` (G.2, ADR-0241, PRD Part B §13.5.2) are
  the host-free Play rules. `PlayHome.Classify` picks W-P1 (no recents; it
  replaced the P.7 Welcome card, so `PlayerEnhancementsConfig.WelcomeCardDismissed`
  is no longer read) or W-P2, whose Recent grid is `RecentGrid` (every entry
  but the Continue game); `LastPlayed` counts calendar days; `Orientation`
  only states what `EnableAudio`/`AutoInstallCommunityPacks` make true. The
  Player home lives in `UI/Views/PlayHomeView.axaml` (the
  `RecentGamesViewModel` DataTemplate); Advanced's game selection and the
  Save/Load slot screens keep the plain `StateGrid` there. `PauseOverlay.Controls`
  is W-P4's seven controls (rule 2: a new pause item replaces or merges one)
  and `WhereNow` maps every P.4/P.7/P.10 overlay action to its row or Tools ⋯
  path. `PlayEsc.Next` is the one Esc router `MainWindowViewModel.TogglePlayerOverlay`
  (`MainWindowViewModel.PauseOverlay.cs`) follows: game → W-P4 → resume, a
  sheet opened from W-P4 (Save states and its slot grid, Enhancements,
  Cheats, a picker from the Pack row) closes back to W-P4, the first-start
  picker is dismissed, and Esc on the home does nothing. *Quit game* powers
  the game off (`LoadRomHelper.PowerOff`, after the existing
  `ConfirmExitResetPower` prompt) and never closes the app.
- The Play edge flows (G.5, ADR-0241, PRD Part B §13.5.2 W-P13–W-P16) are
  host-free in `UI/Logic/PlayFirstRun.cs`,
  `PlayBiosPrompt.cs`, `PlayLoadFailure.cs`, `PlayPackDepPrompt.cs` and
  `PlayControllerSetup.cs`. W-P12's sheet is retired (ADR-0256 Decision 8):
  nothing runs before `MainWindow` any more, and the two questions it asked -
  the storage folder and the keyboard preset - are Settings › System
  (`PlayerSystemSettingsViewModel` + `PlaySystemSettingsPadTests`); what stays
  in `PlayFirstRun.cs` is the choice, its defaults and the mappings.
  W-P13, W-P14,
  W-P15 and W-P16 are Player-mode only, in Play; Advanced keeps the
  `FirmwareNotFound` dialog loop, the OSD load error with the home hidden,
  and the OSD pending-dep line. `PlaySheet.PackDep` closes back to W-P4; the
  BIOS and controller sheets take Esc first (`HandleEdgeFlowEsc`, as Cancel).
  The W-P15 poll lives in `UI/Windows/PlayEdgeFlowsWiring.cs` and writes only
  a free port-1 mapping slot, never over a binding; when it stops listening
  the pill goes too (#660, its 8 s only advance on ticks). The Core's load
  thread blocks on `MissingFirmware` holding its load locks, so every wait
  goes through `CoreRequestWaits` (`UI/Logic`): `CloseEmu` dismisses the BIOS
  sheet and closes the waits before `EmuApi.Stop`, and another open or a
  power off dismisses the sheet (#658). Every open
  starts with no BIOS cancel (`PlayBiosSheetViewModel.ClearCancelled`, #674),
  and a failure is reported only for the latest open
  (`PlayLoadFailure.IsCurrentOpen`), so a cancel never outlives its open. The
  recent-game path (Continue, a recent card) reports W-P14 too (#676): the
  core's `LoadRecentGame` answers nothing, so no game running afterwards is a
  failure, and `PlayRecentGameFailure` reads the `.rgd`'s `RomInfo.txt` to
  say `Missing` (the `.rgd` or its ROM is gone) or classify the ROM like any
  open. A recent card stays enabled while its `.rgd` exists. An OS file open
  (`App.OpenFromOs`, macOS open-documents) waits for `MainWindow.Startup`
  through `RunWhenStarted`, so a cold launch never loads before
  `EmuApi.InitializeEmu` (#681). A pack ROM patch forced onto another
  revision by `ApplyPatchOnHashMismatch` (#732) is read back after every
  `GameLoaded` (`EmuApi.GetForcedPackPatch`, from the core's
  `ForcedPatchGate`) and `PlayForcedPatch` puts the warning banner
  (`InterruptionKind.ForcedPatch`, docked above the game) up in Player mode
  only; *Reload Without Patch* is `EmuApi.SuppressForcedPackPatch` (that
  ROM, this session, the setting untouched) plus a power cycle.
- The pad drives the Play GUI (ADR-0256 Decisions 2 and 3, plus the held
  repeat the slice of 2026-10-04 carried). The rules are host-free in
  `UI/Logic` and tested without a pad or a window: `PadInHand` (which pad is in
  the player's hand, off the last *new* press), `PadNaming` (the family comes
  from the backend's `GetKeyName` — a code cannot say it, since
  `(code - 0x1000) >> 8` reads a Windows DirectInput `Joy` code as pad 16),
  `PadNavControls.Resolve` (the codes that pad's preset binds),
  `PlayPadNavigation.HasAuthority/Next` (when the pad is the GUI's rather than
  the console's, and what one press means) and `PadNavRepeat` (400 ms before
  the first repeat, one step every 100 ms after it; Confirm and Back never
  repeat — a repeating Confirm activates whatever it just scrolled onto).
  `UI/Windows/PlayPadNavigationWiring.cs` is the only host half: a 50 ms
  `DispatcherTimer` on the app's own poll cadence (the W-P15 poll's) samples
  `InputApi.GetPressedKeys()` — never an event, so nothing hooks
  `OnPreviewKeyDown`, whose macOS path returns early — feeds the rules and
  applies the answer through `IFocusManager.FindNextElement` +
  `PlayFocusOnOpen.Enter`. `PlayPadNavigation.HasAuthority(playSurfaceUp,
  gameLoaded, gamePaused, loadCardUp, firstRunPickerUp)` is what says whether
  the pad is the GUI's at all: the on-load pack picker outranks the load card
  (it is posted while the card is still on screen, and it has to be answerable),
  the load card itself refuses (it carries no focusable control, so authority
  over it would only let a Confirm reach the home and launch a game *through*
  the card), and otherwise the pad is the GUI's when no game is loaded, or when
  a surface that took the console away is up — the pause is what makes it the
  pad's, not the surface's, which is what keeps a surface that never pauses
  (the barcode tool sheet, Settings from a task door) from handing the pad the
  menus over a running game. `IsBackEdge` is asked **outside** that rule: the
  tick hands the authority answer to `PadNavRepeat.Next`, and only when it
  answers `None` does it test the Back edge, which needs no authority at all —
  Back is the one press no authority rule may gate, because the slot grid the
  Load/Save-state shortcuts open sits over a game `CurrentPlaySheet()` does not
  name and has no other exit. `StateGrid` is scoped **out** — it moves its own
  `SelectedIndex` from the pad in its own timer, through `GridAction` (the pad's
  own preset inside the Play door, never the console mapping the port carries,
  so rebinding or clearing the D-pad cannot change or lose grid navigation and a
  second pad can drive it — and the console mapping outside it, which is how
  Advanced draws its own game-selection and Save/Load screens from the same
  control), its slots are not individually focusable, and "owning" it here
  would mean the roving-focus container Decision 3 rules out — except for Back,
  which is the bridge's: the grid's loop has no exit, and a player stuck in the
  slot grid is the failure ADR-0256 exists to prevent.
- `PlayFocusOnOpen` (`UI/Utilities`) is Decision 3's one focus path. A Play
  surface registers a claim in the order `TogglePlayerOverlay` walks it
  (`HandleEdgeFlowEsc`, then `CurrentPlaySheet`'s chain), so two surfaces up at
  once — Look's Adjust… opens the shader sheet over Settings, which stays open
  beneath — resolve the way Esc would; the arbiter re-reads the claims on a
  close as well as an open, which is what hands the focus back down the stack.
  Every surface that chain can reach has a claim, and the claim opens on the
  same predicate the surface stands for — a claim narrower than the surface
  (the tool sheet's, which opened on the barcode kind alone, or the Controller
  sheet having none at all) leaves the arbiter focusing what is *under* the
  sheet, so the ring is drawn on a surface the player cannot reach and Confirm
  fires that surface's action instead of the sheet's.
  `Enter` is the only place focus is taken, and always with
  `NavigationMethod.Directional`: that is what makes it a `:focus-visible`
  focus, which is what paints `PlayerFocusRing`. Before it, each surface posted
  its own `Focus()` (the `MainWindow` constructor, `PlayEdgeFlowsWiring`,
  `PlayHomeView`, `StateGrid`) and they raced; #625's cross-window guard lives
  here once now.
- The Remaster workspace (G.3, ADR-0241/ADR-0243, PRD Part B §13.5.3
  W-R0–W-R3) keeps every decision host-free in `UI/Logic/Remaster*.cs`:
  `RemasterProjectReader` reads `project.json` + `auto/rec-NNN/` the way
  `scripts/mep_project.py` does (a bare `auto/textures` is rec-001);
  `PythonLocator` needs Python 3.10+ (3.9 fails on PEP 604 in
  `artist_chr_kit.py`) and never probes the macOS `/usr/bin` stub;
  `RemasterToolsLocator` finds `mep_project.py` because the app bundle ships
  no `scripts/`; `RemasterJobRunner` runs `mep_project.py kit` behind
  `IJobProcessLauncher` (real processes in `UI/Services/RemasterProcesses.cs`,
  argv through `ArgumentList`, never a joined command line) and counts
  steps from its `==`/`ok`/`FAIL` lines; `RemasterScreen.Evaluate` gives
  every control its enabled state and reason. Recording goes through
  `EmuApi.StartMepRecording("play", "")`/`StopMepRecording`; switching
  workspace never stops it (the profile button's dot and the status line
  say so). During W-R2 the renderer is shown under Remaster with one strip
  docked above it (the native renderer draws over Avalonia, so nothing is
  overlaid on the game), and Esc (`ToggleOverlay`) stops the recording.
- The Play sheets (G.4, ADR-0241, PRD Part B §13.5.2 W-P5–W-P9) keep their
  rules host-free: `PackRowRoute.For` picks W-P5 (2+ distinct `pack_id`s, no
  sibling) or W-P6; `PackPickerRow`/`PackDetail` build a row, the chips, the
  folder and the Restore visibility (catalog installs only, ADR-0147);
  `RestoreFlow` is the one in-place confirm; `PackAudioNotice.Scan` is the
  counted ADR-0240 check W-P6 re-reads when it opens. Both the folder button
  and that scan go through `MepPackLayer.Resolve` (ADR-0147): a container
  whose pack roots at `mep/` (pack.json or a convention probe there - the
  core's `HasSiblingMepPack`) is read at `mep/`, a legacy sibling or a
  central `EnhancementPacks/<container>` at its own root. `EnhancementsSheet.Pending`
  names W-P7's button from the applied state and the draft, and
  `EnhancementsSheet.Resume` (with `EnhancementsDraftVisit.Holds`) is what the
  draft does across the Pack row's detour into the pack sheets: the flips come
  back, the switches the player left alone are re-read from what is applied;
  the ViewModel
  (`MainWindowViewModel.PlaySheets.cs`) applies through `ToggleLayer`/
  `ToggleWideScrn`/`ToggleOverclock` and its `LayerChangeKeepsPlace` is the P.9
  hook. `PlayerSettingsEssentials.Tabs` is W-P8's strip, shown by
  `PlayerSettingsSheetView` in MainWindow (`MainWindowViewModel.PlayerSettings`,
  `PlaySheet.Settings` for Esc; "More in Options…" hands over to the classic
  `ConfigWindow` through `MainMenuViewModel.OpenConfig`, which has no Player
  mode any more); `ConfigWindowTab.Display`
  is Player-only (not in `ConfigWindowTabOrder`) and `ConfigViewModel` keeps
  `SelectedTabIndex`/`PlayerTabIndex` at -1 for the hidden strip, so a tab's
  content is realized once. `PackInstallPill` is W-P9's state;
  `CommunityPackInstallService.InstallStarted`/`InstallFinished` feed it on the
  UI thread and the sentence goes to the core HUD (`EmuApi.DisplayMessage`,
  the native renderer draws over Avalonia) and the status line. The sheets
  are `UI/Views/PlayerPackPickerSheetView`, `PlayerPackDetailSheetView`,
  `PlayerEnhancementsSheetView` and `PlayerWindowSettingsView`; their names
  are in the UserControls' scopes, so `MainWindow` finds them through the
  visual tree (`MainWindow.PlaySheets.cs`), not `GetControl`.
- The Share workspace (G.8, ADR-0241/ADR-0205/ADR-0154, PRD Part B
  §13.5.4 W-H1–W-H4) holds no credential and uploads nothing: every
  submission is a pre-filled issue URL opened in the browser.
  `UI/Logic/PackShare.cs` builds the `community-pack.yml` URL from exactly
  its three fields (`pack_link`, `rom_target`, `console`; a test parses the
  real form) and checks the link with `CommunityPackHostAllowlist`;
  `ShareProjectPackage.cs` reads a project's game/console (`.bootstrap`
  `rom=`, `mep/pack.json` `targets[0].system`) and builds the
  `mep_build.py pack` job (`RemasterJobKind.Pack`, run by Share's own
  `RemasterJobRunner`). The two runners are linked by
  `WorkspaceJobs.Link` (#647): a job snapshot carries its `ProjectFolder`,
  each workspace's job gate refuses, with its reason, while the other runs a
  job on the same project (`RemasterJobs.RunsOn`), and a finished job is a
  result only on the project it ran on (`RemasterJobs.ShownFor`, #648);
  `ShareScreen.cs` holds the view enum, the replay
  start gate (mirror of the core's `ShareRecordingSettings::IsSupported`)
  and the Esc router. Replays reuse `ShareRecordingSession` through
  `IReplayRecorder` (`StopAndKeep` stops without the menu's reveal and
  browser hand-off, which W-H4's after sheet does on click). Remaster
  reaches W-H3 through `RemasterWorkspaceViewModel.RequestShareProject`.
- G.6 (PRD Part B §13.5.3 W-R3/W-R4, §13.5.5 W-X3) adds Build & show:
  `RemasterBuilds.Spec` runs `mep_project.py build`; its `show:` line and
  `RemasterShow.Decide` pick `EmuApi.RequestMepImageReload` (images only) or
  `LoadRomHelper.ApplyPackChange` (manifest changed), only on the project's
  own running NES game, and the pack reload only when ADR-0244's plan is in
  place (`RemasterShow.PackReload`, #649: never a restart during a movie,
  shared replay or netplay - the build then plays next time). `RemasterBuildProblemReader` turns the runner's
  `Log` into W-R4 rows by caption (`RemasterKitIndex`, from `kit.json`);
  an untranslated line is only counted. The Build half of the VM is
  `RemasterWorkspaceViewModel.Build.cs`. W-X3's question is
  `InterruptionViewModel` on `MainWindowViewModel.Interruption`
  (`MainWindowViewModel.Interruptions.cs`), asked from
  `MainWindow.OnClosing` and from `LoadRomHelper` before any ROM opens; it
  replaces `ConfirmExit` when it asks, so quitting confirms once. Quitting
  asks about, and stops, Share's pack job too (#650); a stopped import
  removes the `<pack> (editable)` folder only when that import created it
  (`RemasterHandOff.PartialImportToRemove`).
- Remaster's tile browser and hand-offs (G.7, PRD Part B §13.5.3 W-R1 zone
  ②, W-R5–W-R7) read only what the scripts write: `RemasterKitReader` reads
  `kit/rec-NNN/kit.json` and `kit/pages/kit.json` (never
  `kit-proposals.json`, ADR-0188); `RemasterPaintProbe` calls a surface
  painted only when it differs from its `*.orig.png` twin upscaled
  nearest-neighbour, as `mep_build`'s `_EditedProbe` does, and only for the
  units whose twin is a pre-paint copy (grid, object, element, panorama) -
  pattern pages, scene captures and imported sheets say "cannot tell".
  A tile opens with the OS default through `UI/Services/RemasterFileOpener.cs`
  (ADR-0209's first user-configured launch). `RemasterHandOff` builds the
  `mep_import.py import` job and the `compose_editor.py <recording>` child;
  both tools are in `scripts/tools-zip-manifest.txt`, and a tools folder
  without them disables the control with its reason. New files only: the
  `RemasterWorkspaceViewModel.Tiles/.Import/.Compose.cs` partials and the
  `RemasterTileBrowserView`, `RemasterImportSheet`, `RemasterComposeSheet`
  views.
- **BYOK key custody** (F14.20, ADR-0242 Q1/Decision 4, ADR-0247
  Decision 3) is `IByokKeyStore` in `UI/Logic/ByokKeyStore.cs`, one entry per
  `ByokVendor` (`OpenRouter` → `OPENROUTER_API_KEY`), with
  `MacKeychainByokKeyStore` (Security framework SecItem API via P/Invoke -
  never the `security` CLI, which would put the key on a command line),
  `WindowsCredentialByokKeyStore` (advapi32 CredRead/CredWrite/CredDelete),
  Linux `UnsupportedByokKeyStore` with its reason, and the test double
  `InMemoryByokKeyStore`. `ByokJobLauncher.Start` reads the key when a job
  starts, passes it to the child through its environment only (refused on
  argv), drops it from the `ProcessStartInfo` after the start and redacts it
  from every output line; no custody type keeps it in a field. A key is
  stored and used trimmed (`ByokKey.Normalize`, in every `Write` and in
  `Start`), the bare key `scripts/jev_client.py` strips to, so redaction
  matches what the child prints (#681). Guarded by
  `UI.Tests/Byok/*` (argv, output, start-failure text as
  `MesenMsgBox.ShowException` prints it, no fields; the live Keychain
  round-trip is opt-in, `MESENAI_BYOK_LIVE=1`) and
  `UI.HeadlessTests/ByokSettingsSerializationTests` (settings.json). Any later
  BYOK feature uses this interface; none may add a second key store.
- `PlayerPackPicker` (P.5, §5) is the host-free decision for the Player pack
  picker: it opens only when 2+ distinct pack_ids exist (after the §5
  content_id merge — feed it `PackPreferenceResolver.Resolve`'s `Candidates`,
  never the raw entry list, or a dropped copy of an installed pack would
  count as a second choice), with no human-authored sibling-folder pack (a
  sibling with human content always wins, §4; an auto-only bootstrap sibling
  does not suppress, issue #150) and no effective stored per-ROM preference
  (silent apply). `DistinctPackIdCount` uses `DerivePackId` (ADR-0140 id, else
  `local:<container>`). The owning VM (`MainWindowViewModel`) injects the pack
  list + ROM sha1 (data-injected from the code-behind), builds the choices
  from the core's `GetPackListText` columns (name/author/version/licence/
  sections/origin already there), and `PickPlayerPack` stores the P.3
  preference then applies it through `LoadRomHelper.ApplyPackChange` (in
  place, or the old power cycle; see `PackChangePolicy`); `DismissPlayerPackPicker`
  stores nothing so the next launch asks again. The picker's order is
  community 👍 first (`CommunityPackInstallService.GetVotes(pack_id)`, from
  the last catalog fetch's MEI `votes`), then name — local-only packs (no
  catalog row, votes 0) fall back to name order. Only enabled packs are offered or
  counted (`PlayerPackPicker.Offered`, before `Resolve`, #693). That display
  order is never the "current pack": `PlayerPackPicker.CurrentContainer`
  mirrors the core's `GetPackForSection` (stored choice, else the first
  enabled human pack in pack-list order, else the first auto-only one, #703).
  Use This Pack opened from W-P4 returns to W-P4 when the swap is in place
  and to the game when it restarts (`PackPickClose`, #691). W-P5's last row,
  *No pack* (`PlayerPackChoice.NoPackRow`, offered whenever a pack is
  listed), stores `PackPreferenceResolver.NoPack` (`:none`, never an ADR-0140
  pack_id); `Resolution.PrefersNoPack` makes it an effective choice (silent
  load), `CurrentContainer` then returns only a sibling-folder pack (the
  core's `MepPackManager::PreferenceAllowsPack`), `CanChangeChoice` keeps the
  way back open with one pack, and `CommunityPackAutoInstallGate` skips the
  auto-install for that ROM (ADR-0146: a user disable overrides).
- `PackChangePolicy` (P.9, ADR-0244) is the host-free decision for a pack
  change — the Enhancements panel's Textures/Audio/Border toggles
  (`ToggleLayer`) and the picker's Apply: in place
  (`EmuApi.ReloadRomKeepingState`) on the consoles
  `scripts/pack_swap_exactness.py` measured exact (NES, SMS, GB), else the
  pre-ADR-0244 restart, with the HUD reason for a movie or netplay; its
  `Outcome` maps the core's answer (restored, fallback restart, ROM-patch
  restart, refused → plain restart) to a `MessageManager` key.
  `InPlaceReloadResult` mirrors Core's enum value for value (ABI; guarded by
  `scripts/test_pack_swap_exactness.py`). `LoadRomHelper.ApplyPackChange`
  is the host-aware caller: it runs the blocking export off the UI thread,
  one swap at a time; a refusal's fallback restart runs only while the
  load it was for is still loaded (`PackChangePolicy.RestartsLoadedGame`,
  #655). Overclock never goes through it (power cycle).
- `CommunityCatalogUpdateDecision` (P.6, PRD Part B §3.6) is the
  host-free verdict for the F6.4b reinstall gate, replacing the old
  source.sha256 trigger (ADR-0138 §37) with the §3.6 content_id rule: an
  installed content_id differing from the catalog slot's → `Updated`
  (reinstall, unless the installed semver is newer → `NoDowngrade`, and
  hd-legacy has no semver so any diff updates); unchanged content_id →
  `UpToDate` or `WrapperOnly` (source sha256 changed, content same — no
  reinstall); fetch-returns-null (removed slot) → `RemovedFromCatalog`
  (keep the install, silent). `ReadStampFields` reads the `.mep-install.json`
  `content_id`/`source.sha256`; `CompareSemver` is numeric (no prerelease
  parsing). The coordinator (`CommunityPackInstallCoordinator.EvaluateGates`)
  feeds it the stamp + the installed version from `GetMepPackList` column 3
  (the stamp carries no version). The container name is unchanged by an
  update, so `DisabledPacks` and the per-section flags survive a reinstall.
  The catalog DTO's additive P.2 fields (`pack_id`/`content_id`/`votes`) are
  what carry the slot identity and 👍 into these decisions.
- `CommunityPackCatalogMatcher` (F6.4b, MEI-v1 §2.3): auto-match is exact
  No-Intro `rom.sha1` / `rom.sha1s` first, then a same-game identity
  fallback (`SameGame`: core-title token multiset after stripping trailing
  region/dump tags) so a nearby dump of a catalogued title still
  auto-installs. The fallback only runs on entries that already carry a
  sha1 — `rom: {}` stays listable/manual. SHA1 always wins over the
  filename. IPS/patches stay hash-gated (ADR-0044). `CommunityPackCatalogFetcher`
  passes `EmuApi.GetRomInfo().GetRomName()` as the ROM display name.

- **New ViewModels** (Phase 3 of the plan — applied opportunistically, "in
  the code we touch", not as a retrofit of existing VMs): a *new*
  ViewModel's constructor must not call `EmuApi`/`ConfigManager` I/O
  directly — data comes in via a method parameter (e.g.
  `Refresh(string packListText, string sha1, string sibling)`). This is
  data injection, not mocking `EmuApi` — no DI container, no
  `Avalonia.Headless`, no ViewModel/UI-click testing (all explicitly out of
  scope). Dialogs (`FileDialogHelper`, `MesenMsgBox`) stay in the
  `*.axaml.cs` code-behind, as `EnhancementPacksWindow` already does for
  OK/Install. Branching logic (parse, validate, classify) is born in
  `UI/Logic/` with a test in the same PR — never inlined in the VM. A
  `RelayCommand` is not required; a thin code-behind calling a plain VM
  method stays acceptable. `EnhancementPacksViewModel` itself is NOT
  retrofitted to this shape (it still calls `EmuApi`/`ConfigManager` from
  its constructor/`Refresh`) — only its parse/validate logic was extracted
  (Phase 1); a ctor taking `EnhancementPackConfig` + a Window-fed `Refresh`
  is optional future work, only if an orchestration test justifies it.

- **`UI.Tests.csproj` is NOT a member of `Mesen.sln`.** Including it would
  expose the project to the solution-level flows in
  `.github/workflows/build.yml`/`tests.yml`
  (`dotnet restore -r win-x64 -p:PublishAot=true Mesen.sln`, Windows-only)
  and, at the time this decision was made, `dotnet-format-check.yml`
  (`dotnet format --verify-no-changes` against the whole `.sln`; that
  workflow was itself deleted 2026-09-14). This actor's environment is
  macOS/Linux, so the plan's own risk item — verifying locally that a
  RID-less `UI.Tests.csproj` survives that win-x64/AOT restore — cannot be
  executed here; per the plan ("Phase 0 — Mesen.sln"), the fallback when
  that can't be confirmed is to keep the project OUT of the `.sln` and let
  `.github/workflows/unit-tests.yml` invoke `UI.Tests/UI.Tests.csproj`
  directly instead. That is the choice recorded here. Consequence:
  `UI.Tests` code is not gated by a solution-wide `dotnet format` check — if
  it later joins the `.sln`, either confirm the win-x64/AOT restore survives
  first, or add it with `Build.0` disabled under `Release|x64`, and its code
  must then also satisfy the repo's `.editorconfig` (tabs) under
  `dotnet format`. See `.github/AGENTS.md` for how the CI split reflects
  this.

- **`UI/Services/*.cs`** (ADR-0138 §37/§38/§43/§45-§47, F6.4b-2) is
  deliberately outside the `UI/Logic` firewall: `scripts/verify-ui-logic-firewall.sh`
  only greps `UI/Logic/*.cs`, so `Avalonia`/`EmuApi`/`HttpClient`/`File` I/O
  are all fine in `UI/Services/`. Each class there is a thin, host-aware
  orchestrator over the host-free `UI/Logic/Community*` decision types —
  the network fetch, dep resolution/reinstall-gating/consent-gating and the
  `EmuApi.InstallMepRecipe` call itself all live here, never in
  `UI/Logic/`. `CommunityPackInstallCoordinator.Install()` is the seam: it
  takes the fetcher's already-verified output (matched catalog entry,
  primary artifact path, dep-id → path map) and is the only call site of
  `EmuApi.InstallMepRecipe` for this feature. An `hd-legacy` wrapper zip
  (one root-level nested zip, e.g. Zelda Remastered, or a GitHub-repo
  archive with one pack zip in the matching game folder, e.g. LiQuiDz
  `HDnes-main/1942/1942audio.zip`) is unwrapped by
  `LegacyHdPackInstall.ExtractToFolder` while the inner `ZipArchive` is
  still open — disposing it first throws `ObjectDisposedException` on
  `ZipArchiveEntry.Open`. After a successful auto-install
  (`CommunityPackInstallStatus.Installed`),
  `CommunityPackInstallService` power-cycles (`LoadRomHelper.PowerCycle`)
  when the load it captured is still loaded (`CommunityPackLoadTarget.
  IsStillLoaded`, the W-P16 rule: open generation + SHA-1, #675), so
  `HdPacks/<rom>/` applies without a second manual load; `OnGameLoaded`
  already skips power cycles, so this does not re-fetch. A ROM switch, or
  the same game reopened (Continue), during the install does not
  power-cycle. A throw between the gate's `TryEnterOrDefer` and `RunAsync`
  releases the gate (#681).
  #657: the auto-install and Restore capture the load
  (`CommunityPackInstallCoordinator.CaptureLoad` → host-free
  `CommunityPackLoadTarget`: SHA-1, whole-file SHA-1, sibling folder, ROM
  name, open generation) before the download, and the coordinator takes the
  out folder, ROM name and registry key from it, never from the game loaded
  afterwards. If another open started meanwhile (`IsStillLoaded`, the W-P16
  rule) it drops with `CommunityPackInstallStatus.Stale` before the first
  destructive step (silent, logged, retried on that game's next load). A load
  refused by the install gate is deferred (`CommunityPackInstallGate.
  TryEnterOrDefer`), and the holder's `Exit` runs it for the game loaded then.
  A failed or thrown auto-install clears the per-session ROM sha1 attempt
  so the next load retries.
  ADR-0240 / F6.9: after a successful install (MEP-recipe or hd-legacy),
  `CommunityPackInstallCoordinator.WithAudioNotice` asks the host-free
  `PackAudioNotice.Evaluate(outFolder)` whether a WIRED bundled `.ips`/`.bps`
  (referenced by a `<patch>` line or a `pack.json` `patches[]` entry, and
  present; same meaning as `scripts/mep_lint.py` `scan_bundled_patches`)
  redeems `<bgm>`/`<sfx>` refs that do not resolve in the installed pack (as
  written, then case-insensitively, like `HdPackLoader::CheckFile`). If so
  the outcome stays `Installed` and carries ONE non-fatal
  `CommunityPackInstallOutcome.Notices` entry, "audio not generated: N of M
  tracks unresolved; supply the `.ogg` files" (M = distinct referenced
  files, not lines), which is also written to the log and toasted by
  `CommunityPackInstallService.Surface`. Nothing is generated, spawned or
  patched; no patch wired, no audio, or all refs resolved means no notice.
  Pinned by `UI.Tests/CommunityPacks/PackAudioNoticeTests` and the real-coordinator wiring
  test `UI.HeadlessTests/PackAudioNoticeInstallTests`.
  `CommunityPackDownloader` follows only 301/302/303/307/308
  (`CommunityPackHttpStatus.IsFollowableRedirect`); HTTP 304 is a terminal
  catalog-cache status for `CommunityCatalogCacheDecision`, never a
  redirect.
- **Renderer size (`UI/Logic/RendererViewportFit`, P.7 plus upstream
  3924215).** `MainWindow` sizes the renderer only from its result; don't
  round inline there. Invariants, covered by `RendererViewportFitTests` and
  the headless `RendererLetterboxTests`:
  - `RealWidth`/`RealHeight` are even (no shader centre seam) and never
    exceed `floor(panel * dpi)`. The one exception is the integer-scale
    clamp to 1x on a panel shorter than one screen.
  - The binding axis rounds **down** to even. Upstream's round-up overflows
    a 375 px panel.
  - The aspect contract is `AspectErrorPixels <= 1` physical px, not an
    exact ratio.
  - The logical size is snapped back from the physical pixels, so
    `Width * dpi == RealWidth`.
- **Renderer choice and the shader group (`UI/Logic/RendererPolicy`,
  ADR-0237 / P.8).** macOS is no longer forced to the software renderer:
  only `VideoConfig.UseSoftwareRenderer` picks it, on every platform, and
  the core falls back to software by itself when Metal cannot start. The
  shader group shows when the core reports shader support, except on macOS
  with the software renderer, which never runs a shader. Covered by
  `UI.Tests/Config/RendererPolicyTests.cs`; there is no separate "use Metal"
  setting.
- **Play Cheats sheet (`UI/Logic/CheatSheet`, P.10 / ADR-0245 §1–§3, §5).**
  The overlay's *Cheats · N on* row opens W-P11
  (`UI/Views/PlayerCheatsSheetView`); every toggle is written to the same
  per-game `CheatCodes` file the classic `CheatListWindow` edits, matched by
  that window's import key (description + codes + type), so the two never
  disagree. Turning a row off keeps it, disabled. `CheatRecordingRule` is
  the ADR-0184 §1 test (a NES RAM code is `NesCustom` with every address
  below `0x0800`); `CheatConsoleScope` gives NES the bundled list and GB/SMS
  manual entry only, with the reason. `TryParseCodes` is a separate entry
  point from the parity-frozen `CheatTypeDetector` (ADR-0128). Play passes
  `recordingArt: false`; `CheatRecordingRule.IsRecordingArtContext` makes it
  `true` in Remaster or while a Remaster recording runs (W-R2 has no overlay;
  switching to Play keeps the recording). `RemasterWorkspaceViewModel.Cheats.cs`
  refuses *Record While I Play* while a non-RAM code is on, naming it
  (ADR-0184 §1), and `CheatCodes.RecordingArt` makes `ApplyCheats` - every
  path to the core, the classic window included - hold back non-RAM codes
  until Stop (`HeldForRecording`, named on the W-R2 strip). The core
  dereferences the running console in `GetRomHash`, so the hash is only read
  while `EmuApi.IsRunning()`. Rules in `UI.Tests/Cheats/`, wiring in
  `UI.HeadlessTests/PlayerCheatsSheetTests` and `RemasterCheatsTests`.
  **Community rows (R.4, ADR-0248 §2, §5).** `UI/Services/CommunityCheatCatalogFetcher`
  fetches `docs/community-cheats.json` with the pack catalog's allow-list,
  downloader and ETag cache rule (no confirmation, the GET only); the sheet
  opens on the last known catalog and `SetCommunityCatalog` adds the fetched
  one when it returns. `CommunityCheatCatalog.ForCopy` matches the exact
  cheat SHA-1 and the console, never a name; `CheatSheet.BuildRows` puts the
  rows (`CheatRowSource.Community`) between the bundled list and the user's
  own codes under the same recording rule. The 👍 count and *Share This
  Cheat ↗* (`CheatShare`, the user's own codes only) open URLs through the
  injected `openUrl`; `MainWindowViewModel.CommunityCheatsSource`/`LastKnown`
  are swapped in headless tests so none reaches the network.
- **Shared replays sheet (`UI/Logic/CommunityReplayCatalog`, `ReplayWatch`,
  R.2 / ADR-0205 §7–§9).** W-P4 › Save states › *Shared replays…* opens
  `UI/Views/PlayerReplaysSheetView` (W-P4 is at its seven controls, so it is
  not an overlay row). `UI/Services/CommunityReplayCatalogFetcher` fetches
  `docs/community-replays.json` like the cheat catalog (pack allow-list, ETag
  cache rule, no confirmation, the GET only) and downloads a row through the
  embedded `scripts/replay_host_allowlist.json` (resource
  `Mesen.replay_host_allowlist.json`, drift-checked) under the 8 MB cap,
  then checks size and sha256 before it writes `downloads/<sha256>.mmo`.
  `ForRom` matches the movie's own ROM SHA-1 (`GetRomHash(HashType.Sha1)`,
  the ROM as loaded, before a patch) and the console, never a title. Watch
  asks once in place ("Restart & watch") because `MesenMovie::Play`
  power-cycles; it is off, with the reason, with no game, during a movie or
  in netplay. `MainWindowViewModel.CommunityReplaysSource`/`LastKnown`,
  `ReplayRomSha1`, `ReplayOpenUrl`, `ReplayDownload`, `ReplayPlay` and
  `ReplayWatchReasonSource` are swapped in `UI.HeadlessTests/PlayerReplaysSheetTests`
  so none reaches the network or the core's movie player. Rules in
  `UI.Tests/Share/CommunityReplayCatalogTests` and `ReplayWatchTests`.
- **Settings › Look (`UI/Logic/LookLayers`, `NamedLookManifest`, P.13 /
  ADR-0246).** Art / Pixels / Screen in `LookConfigView`, a ConfigWindow tab
  after Video. Every rule (items, selection, enable, reason, ◉/◌ mark, what a
  pick writes) is `LookLayers`; the VM only reads inputs and writes what
  `Apply*` returns. Pack art is the core's `EmuApi.IsDrawingPackArt()`, the
  same condition each console's `GetVideoFilter` uses. A value Look does not
  list shows as the current item and is never rewritten. The ConfigWindow
  TabControl binds a position through `ConfigWindowTabOrder`, never the
  `ConfigWindowTab` id (ids have holes). Bundled looks are listed in
  `UI/Dependencies/Shaders/Looks/looks.json`; every file there needs its
  license, pinned source and sha256 (`UI.Tests/Look/NamedLookManifestTests`).
  Rules in `UI.Tests/Look/`, wiring in `UI.HeadlessTests/LookSettingsTabTests`.
- **Tools > Movies > Record and share** (ADR-0205 sec. 2/6): `ShareRecordingSession`
  calls `RecordApi.MovieRecordAndShare` (no dialog, no mode), writes under
  `<MovieFolder>/Shared/`, and on Stop reveals the file and opens the
  pre-filled `[Replay]` issue form from the host-free `ReplayShare`
  (`UI.Tests/Recording/ReplayShareTests.cs`). No upload, no credential.

## Player theme (ADR-0249)

The Player GUI follows its rendered wireframes (`docs/media/gui-redesign/W-*.png`,
drawn by `scripts/render_gui_wireframes.py`), not classic Mesen. The theme is
`UI/Styles/PlayerTheme.axaml`, included by `App.axaml` after `MesenStyles`.

- **Scope.** Every style is under the `player` class. `MainWindow` binds
  `Classes.player` to `UiMode == Player` on `PlayWorkspace`, `ShellBar` and
  `ShellStatusLine` (and `InterruptionBarHost`), on Remaster's
  `RemasterWorkspaceHost` and `RemasterRecordingStripHost` (which also carry
  `remaster`), and on the Share views themselves (`ShareWorkspace`,
  `ShareRecordingStrip`, whose DataContext is Share, hence a cast binding);
  outside MainWindow no window carries it any more — the first-run card went
  with the retired `SetupWizardWindow` (ADR-0256 Decision 8, its two questions
  are Settings › System now). Player-mode Settings is a sheet inside
  `PlayWorkspace` (`PlayerSettingsSheetView`, W-P8/W-P10), not a window. A component class outside the
  scope does nothing, so classic windows, dialogs, the debugger and Advanced
  mode keep `MesenStyles` (radius 0, MesenFont). A local `Background`,
  `FontSize` or `Foreground` on a control beats every style: put the classic
  value in the view's own `Styles` and the `.player` override after it
  (`WorkspaceShellBar.axaml`, the status line in `MainWindow.axaml`,
  `StateGridEntry.axaml`). A view that carries the class itself can key its
  classic styles off `v|View:not(.player)` (`ShareWorkspaceView.axaml`). A
  Fluent `accent` class paints over `PlayerButtonTemplate`'s fill; clear its
  `PART_ContentPresenter` background in Player if a button keeps `accent`
  for Advanced.
- **Tokens.** The script's palette (TEXT, TEXT2, TEXT3, SEP, WINBG, CARD,
  FILL, RED, ORANGE, TINT, TINT_TEXT) is transcribed as `Player*Color` /
  `Player*Brush`; `UI.Tests/Theme/PlayerThemeDriftTests` fails when either
  side moves. Also: type ramp `PlayerFont*` (LargeTitle 28 … Caption 10.5),
  radii `PlayerRadius*` (Control 8, ControlLarge 11, Card 12, Sheet 14,
  Hero 16, Overlay 18; W-P15's pad: Pad 40, PadKey 3, PadRound 17), spacing `PlayerSpacing*` and `PlayerPageMargin`, shadows `PlayerShadow*`,
  `PlayerFocusRing`. Font: Inter (`Avalonia.Fonts.Inter`, `WithInterFont()`).
  No dark variant.
- **Tint.** `c:PlayerTheme.Tint` / `TintSoft` / `TintText`
  (`UI/Controls/PlayerTheme.cs`) are inherited attached brushes: the scope
  sets Play's blue; a `remaster` or `share` class on any element inside it
  switches to purple / green below that element. Tinted components bind to
  them, so never hard-code a workspace colour.
- **Components** (classes, inside the scope):
  - Buttons: `Button.primary` (tint fill, white semibold), `.secondary`
    (white, hairline, shadow), `.tinted` (TintSoft fill, TintText label),
    `.destructive` (pale red fill, RED label), `.plain` (text only, TintText).
    Sizes: default 28 high, `.small` 24, `.medium` 36, `.large` 44 (radius 8
    up to 32 high, 11 above). A leading icon is `PathIcon Classes="leading"`.
  - Grouped list: `Border.group` holding `Button.row` items (50 high, the
    last row has no hairline). Row content: a DockPanel with
    `Border.badge` (background = a badge colour) + `PathIcon`,
    `PathIcon.chevron` docked right, `TextBlock.value` docked right,
    `TextBlock.title`. `Button.row.text` is a row without a badge.
  - Badges: `Border.badge` 26 (`.small` 22, `.medium` 32, `.xlarge` 40
    below, `.hero` 56, `.large` 80) with a white `PathIcon`; tint by default.
  - Steps: `Border.step` (below; `.step.large` is W-H3's 28 px circle with a
    13 px number, declared after the wave 2 block) and `Rectangle.step-line`
    (2 px SEP connector) - W-H3.
  - Surfaces: `Border.card` (`.hero` radius 16), `Border.sheet`,
    `Border.overlay-card`, `Border.scrim`, `Border.page` (WINBG), tokens
    `PlayerPopoverBrush`/`PlayerPopoverBorderBrush` (below) and shadows
    `PlayerShadowPopover`/`PlayerShadowMenu` for popovers and menus,
    `Separator.hairline`. Card, sheet and overlay-card set
    `TextElement.Foreground` to TEXT themselves (#716), so text on them is
    readable whatever its parent sets; build a new sheet on `Border.sheet`
    rather than a local dark background.
  - Text: `TextBlock.large-title`, `title1`, `title2`, `title3` (for 26
    bold and 16 semibold use `display` and `card-title`, below),
    `headline`, `callout`, `body`, `subhead`, `footnote`, `caption`,
    `section-header`; colour modifiers `secondary` (TEXT2), `tertiary`
    (TEXT3), `tint`.
  - Settings groups (W-P8, W-P10): `Border Classes="group inset"` (the
    play sheets' `Border.inset` fill, #F8F8FA, radius 12) holding `:is(Panel).setting-row` rows (46 high) split by
    `Separator.row-hairline`; above a group `TextBlock.group-label` (11.5
    bold TEXT2 caps) and `TextBlock.group-hint` (TEXT3); `TextBlock.reason`
    is why a control is off (11.5 semibold, dark orange).
  - In-place banner (W-X1 confirmations, W-X2 errors, W-X3 interruptions):
    `Border.banner` (pale orange warning) / `.info` (pale blue) / `.stop`
    (pale red), radius 12, 56 high, plus `remaster`/`share` for the tint;
    inside a DockPanel `PathIcon.banner-icon` (left: `PlayerIconWarning`,
    the tinted icon, or `PlayerIconStop`), `StackPanel.banner-actions`
    (right: `secondary` then `primary` / `primary neutral` (dark) /
    `destructive`, 30 high) and `TextBlock.banner-text`.
    `Views/InterruptionBar` is built on it, its look chosen by
    `Logic/InterruptionBanner`. Ask in place with a banner, never a dialog.
    The pack-detail restore confirmation (`PackDetailRestoreConfirm`), the
    BIOS sheet's wrong-file error and "use it anyway?" confirm and the pack
    dependency sheet's error are all `banner warning`.
  - Status glyphs are drawn, never text characters (no ⚠ or ✔ in copy):
    `PathIcon.warning` beside a warning, `PathIcon.done` (`PlayerIconCheck`,
    Share green) beside a finished result (W-H3 build, W-R7 layout hint).
  - Recent-game tile pack badge (W-P2): `Border#TilePackBadge` in
    `c:StateGrid Classes="tiles"` (22 card square, radius 6, top-right, 13 px
    `PlayerIconPack` in Share green), shown when an `HdPacks/<rom>` folder
    exists (`PlayHome.HasHdPack`); never on Save-state slots.
    The block sits after the wave 2: Remaster block so `banner warning`
    beats Remaster's `Border.warning`.
  - Controls: `ComboBox.popup` / `c:EnumComboBox Classes="popup"` (the
    renders' 24-high macOS pop-up button: white, hairline, radius 6, Play-blue
    up/down stepper in every workspace; its own template, greyed with no
    stepper when disabled), `TabControl.segmented`
    (a TabControl with the segmented strip, 96 px segments),
    `RadioButton.choice` (tint-filled, 13.5 medium), `Border.hud.compact`
    (W-P9/W-P15's smaller HUD pill: radius 10, 40 high, 13 semibold text,
    14 px icon; after the wave 2 block so it beats `Border.hud`), a sheet
    title's 40 px badge is `Border.badge.xlarge` and a progress bar is the
    plain `ProgressBar` (both below; `ControllerSetupProgress` keeps a
    `track` marker class with no style of its own), `ListBox.segmented` (segmented tabs), `ToggleSwitch` (green
    on), `TextBox` (30 high, radius 7, focus ring), `c:StateGrid
    Classes="tiles"` (one row of 176 x 132 recent-game tiles; add `slots`
    for the Save states grid: FILL tiles that fill their cell, title + date,
    17 px bold heading, inside `Border.sheet.slot-sheet`).
  - Play sheets (wave 2: W-P5/6/7/11/13/14/16): `Border.inset` (#F8F8FA
    list, radius 12; `.file-box` radius 10) holding `Border.switch-row`
    (46, `.tall` 50, hairline but the last) with a `CheckBox.switch` (label
    + 38 x 22 switch; `.subtitled` top-aligns it over a
    `TextBlock.row-subtitle`); `RadioButton.option` (selectable card with a
    ring); `Button.drop-zone` (bordered inset; `PathIcon.drop-icon`,
    `TextBlock.drop-title` / `drop-hint`); `Border.warning.notice` (compact
    shared `warning` banner, `.large`; `notice-title`, `notice-line`), `Border.alert`
    (W-P14), `Border.chip` (`.on` green; AA-darkened chip tokens);
    `ToggleButton.disclosure`; `Panel.scrim`; `Button.regular` (32 high
    footer buttons, `.wide`); `Border.badge.heading` 48 (40: the shared
    `badge.xlarge`); text `sheet-heading` (19), `option-title`,
    `replay-note`, `cheat-badge` / `TextBlock.warning.badge-text`,
    `SelectableTextBlock.ids`; glyphs `PathIcon.votes`, `close-glyph`
    (and the shared `PathIcon.warning`). A Play sheet is light: never put one under a Dark
    `ThemeVariantScope` (#716 is closed by these classes);
    `PlaySheetsContrastTests` lists every sheet surface.
  - What each Play sheet holds. *Enhancements* (W-P7,
    `PlayerEnhancementsSheetView`) is one inset list of four switches that
    edit a draft - Modern instruments, Border ("Applies on reload" under it
    where the change restarts), Widescreen, Overclock (grey with its reason
    where the console has no knob) - then the Pack row, `Pack: <name> ›`,
    which opens W-P6, or W-P5 with 2+ packs; one Apply button writes the
    draft. *Pack detail* (W-P6, `PlayerPackDetailSheetView`) holds this
    game's Textures / Music / ROM Patch switch rows, never a global one:
    a layer the pack lacks is grey ("Not in this pack"), one whose global
    default is off reads "Off for every game — Tools ⋯ › Enhancement
    Packs", and those three defaults live only in the Enhancement Packs
    window (Classic's Tools ▸, the Remaster door's ⋯). When the only pack is
    the bootstrap's `auto/` layer the title is *Automatic upscale* and the
    byline is the game's name plus "Made on this computer from what you
    played", naming the scaler in parentheses when the project's
    `.bootstrap` stamp does ("(xBRZ 4×)"). *Settings* (W-P8) is the
    Display | Look | Audio | Controls strip: Audio (Sound, Volume, Output
    device) and Controls (pads, Rumble, deadzone) are three-row lists whose
    "More in Options…" opens that tab's classic page, Display carries the
    "Everything else: Tools ⋯ › Options" hint, Look its own footer.
  - Icons (`StreamGeometry`, 20 x 20 box, use with `PathIcon`):
    `PlayerIconPlay`, `Remaster`, `Pencil`, `Share`, `Pack`, `SaveStates`,
    `Enhancements`, `Cheats`, `Settings`, `Folder`, `ChevronRight`,
    `ChevronDown`, `ChevronLeft`, `Record`, `Replay`, `More`, `Check`,
    `Lock`, `UpDown`, `Warning` (even-odd, so the "!" is cut out), `Stop`,
    `ArrowUpRight`, `Sparkle` (alias of `Enhancements`), `Thumb`, `Close`.
  - Wave 2 (Remaster, W-R0…W-R7), in the theme's "wave 2: Remaster" block:
    - Text: `TextBlock.display` (26 bold), `sheet-title` (18 bold),
      `card-title` (16 semibold), `emphasis` (14 semibold), `lead` (14),
      `paragraph` (13.5); tokens `PlayerFontDisplay`, `SheetTitle`,
      `CardTitle`, `Lead`.
    - Warning: `Border.warning` (soft orange `PlayerWarningFill`, radius 12,
      brown `PlayerWarningText` foreground), `TextBlock.warning`,
      `Button.plain.warning`, `PathIcon.warning` (orange 16).
    - `Border.badge.xlarge` (40, radius 10); `Border.step` (22 tint circle
      with a white 12.5 bold number: the "1 RECORD" step marker).
    - `Button.chip` (FILL chip, radius 8, 32 high: the project menu).
    - `ProgressBar` (6 high, FILL track, tint bar).
    - `Border.popover` / `FlyoutPresenter.popover` (`PlayerPopover` fill,
      hairline, radius 12, shadow `PlayerShadowPopover`); use
      `FlyoutPresenterClasses="popover"` on a `Flyout`.
    - `Border.hud` (the dark pill over the game: `PlayerHud`, radius 12,
      white text; `secondary` / `tint` text inside it read
      `PlayerHudText2` / `PlayerHudTintText`), `Button.primary.hud` (grey
      Stop); `PlayerGameBackgroundBrush` (black behind the game).
    - Tile tokens `PlayerTileFill` / `PlayerTileBorder` and `PlayerChipFill`.
- **No classic dialog from a Player flow** (ADR-0249 final audit). Quit
  game on W-P4 asks with `Views/OverlayConfirmBanner` (`banner stop` on the
  overlay card, `QuitGameConfirm`; Esc answers Keep Playing) and closing the
  window with the `InterruptionBar` (`InterruptionKind.QuitApp`), never
  `MesenMsgBox`. A `MessageBox` the Player GUI still raises takes the
  Player look (`PlayerMsgRoot`: a white sheet, the shared banner,
  `regular` footer buttons, the safe one first) when
  `Utilities/PlayerDialogScope.UsesPlayerLook` holds: Player mode **and**
  an owner that shows the `player` class (`Logic/PlayerDialog`). A
  multi-ROM archive and Look's Adjust… are sheets inside the main window
  in Player mode (user decision 2026-10-03): `Views/PlaySelectRomSheetView`
  (`SelectRomSheet`, in `BiosSheetLayer`, routed by
  `Logic/ArchiveRomPick`) and `Views/PlayerShaderSheetView`
  (`ShaderSheet`, over the Settings sheet); Esc closes either
  (`HandleInWindowSheetEsc`). `SelectRomWindow` and `ShaderConfigWindow`
  are classic-only. Under a
  classic window, the debugger or Advanced the classic tree stays. Look's
  Art row opens W-P6 in the main window
  (`MainWindow.OpenPackDetailFromSettings`). The BIOS sheet sits in the
  root-level `BiosSheetLayer`, so W-P13 asks in every workspace.
  `UI.HeadlessTests/PlayerNoClassicDialogTests` pins all of it.
- **Restyling a screen.** Keep every `Name`, binding, handler and focus
  order (the headless suites find controls by name). Swap local colours and
  sizes for classes; add a render test next to
  `UI.HeadlessTests/PlayerThemeRenderTests` that saves the PNG and asserts
  font, size, radius, tint and background of the named controls.

## Work Guidance

- New host-free helpers extracted from ViewModels go under `UI/Logic/`,
  paired with tests under the matching `UI.Tests/<Area>/` subfolder.
- **Public test-facing helpers** (ADR-0125, H6): `UI/Logic/` types may
  expose pure helpers publicly when a test needs to drive them directly
  (e.g. `MepZipValidator.IsSafePath` over `path-cases.txt`). Keep the
  production entry point the documented one; a `//` header line on the
  helper must name it as test-facing / reusable, so its public status reads
  as intentional. (Under the dual-compile, `internal` buys the tests no
  encapsulation — they compile the sources — so "public is fine if the
  header says why" is the cheapest consistent rule.)
- Never add an `Avalonia` or `EmuApi` reference to a file under
  `UI/Logic/` — if a helper needs a UI-side type (an enum, etc.), move
  that type out of its Avalonia-tainted file first (see Phase 2 of the plan
  for the `ConsoleType`/`CheatType` precedent).
- Apply the Phase 3 VM rule above (Local Contracts) when authoring a new
  ViewModel; do not retrofit it onto existing ones outside of files already
  being touched for another reason.

## Verification

- `dotnet test UI.Tests/UI.Tests.csproj --nologo`
- `./scripts/verify-ui-logic-firewall.sh`

## Child DOX Index

(none — `Logic/` and `Services/` are convention-only subfolders, not
separately governed subtrees)
