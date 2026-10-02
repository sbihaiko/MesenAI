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
  fixed order is the switcher's and the ⌘1/⌘2/⌘3 digits'), `WorkspaceState`
  (one active workspace; an undefined persisted value falls back to `Play`),
  `IsBarVisible` (the bar hides only while a Play game runs unpaused),
  `ShellStatusLine` (the one-sentence status kind) and `ClassicMenuNotice`
  (`ShowClassicMenuBar` defaults to `false` on fresh installs and upgrades,
  §13.8 Q4; the one-time toast is due only for an upgraded settings file
  whose bar is off). `PreferencesConfig.Workspace` is separate from
  `UiMode`, which is not reinterpreted. Switching never pauses or stops the
  game; outside Play the native renderer and every Play surface are hidden.
  Tools ⋯ binds the same `MainMenuViewModel` item lists as the classic bar
  (its menu style templates `ActionIcon.Source`, because one `Image` cannot
  have two visual parents). Since G.1 `PlayerChrome.IsMenuVisible` takes
  `ShowClassicMenuBar`, not `UiMode`, and the P.4 Debug-menu gate on
  `UiMode` is retired so every classic action is reachable from Tools ⋯.
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
  from every output line; no custody type keeps it in a field. Guarded by
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
  preference then power-cycles (applied on the reload); `DismissPlayerPackPicker`
  stores nothing so the next launch asks again. The picker's order is
  community 👍 first (`CommunityPackInstallService.GetVotes(pack_id)`, from
  the last catalog fetch's MEI `votes`), then name — local-only packs (no
  catalog row, votes 0) fall back to name order.
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
  when the same ROM is still loaded, so `HdPacks/<rom>/` applies without a
  second manual load; `OnGameLoaded` already skips power cycles, so this
  does not re-fetch. A ROM switch during the download does not power-cycle.
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
  `recordingArt: false`; the Remaster game view will pass `true`. The core
  dereferences the running console in `GetRomHash`, so the hash is only read
  while `EmuApi.IsRunning()`. Rules in `UI.Tests/Cheats/`, wiring in
  `UI.HeadlessTests/PlayerCheatsSheetTests`.
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
