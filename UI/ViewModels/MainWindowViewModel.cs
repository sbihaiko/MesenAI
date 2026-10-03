using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Controls;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Services;
using Mesen.Utilities;
using Mesen.Windows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	public partial class MainWindowViewModel : DisposableViewModel
	{
		public static MainWindowViewModel Instance { get; private set; } = null!;

		[ObservableProperty] public partial MainMenuViewModel MainMenu { get; set; }
		[ObservableProperty] public partial RomInfo RomInfo { get; set; }
		[ObservableProperty] public partial AudioPlayerViewModel? AudioPlayer { get; private set; }
		[ObservableProperty] public partial RecentGamesViewModel RecentGames { get; private set; }

		[ObservableProperty] public partial string WindowTitle { get; private set; } = "MesenAI";
		[ObservableProperty] public partial Size RendererSize { get; set; }

		[ObservableProperty] public partial bool IsMenuVisible { get; set; }

		//G.1 (PRD Part B §8, ADR-0241): the shell around the active workspace
		//(profile button + switcher, Tools ⋯, status line). IsPlayWorkspace
		//gates every Play surface (renderer, home, overlays, music player): under
		//Remaster/Share none of them is on screen, but none is closed or reset -
		//switching back shows them as they were, and the game keeps running.
		public WorkspaceShellViewModel Shell { get; }
		[ObservableProperty] public partial bool IsPlayWorkspace { get; private set; } = true;

		//G.3 (PRD Part B §13.5.3, ADR-0243): the Remaster workspace. Its project
		//screen (W-R0/W-R1) covers the content area; while it records (W-R2) the
		//game is shown inside Remaster with the recording strip above it, and
		//nothing of Play is on screen (rule 11). Switching profile never stops a
		//recording or a job (§13.6).
		public RemasterWorkspaceViewModel Remaster { get; }
		[ObservableProperty] public partial bool IsRemasterProjectScreenVisible { get; private set; }
		[ObservableProperty] public partial bool IsRemasterGameView { get; private set; }
		//The game picture's layer: Play, or Remaster while recording.
		[ObservableProperty] public partial bool IsGameViewVisible { get; private set; } = true;

		//G.1: tracked from the core's GamePaused/GameResumed notifications
		//(MainWindow.OnNotification), for the bar-visibility rule and status line.
		[ObservableProperty] public partial bool IsGamePaused { get; set; }

		//P.4/G.2 (PRD Part B §6, §13.5.2 W-P4): the Player-mode pause overlay
		//(Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game),
		//shown on top of the game while UiMode == Player. Opening it pauses the
		//game so the couch user can navigate; Esc or Resume closes it and resumes
		//(rule 8: game → W-P4 → resume).
		[ObservableProperty] public partial bool IsPlayerOverlayVisible { get; set; }

		//P.5 (PRD Part B §5): the Player-mode pack picker. Opens once over
		//the un-enhanced game when 2+ competing pack_ids exist and no effective
		//per-ROM preference is stored; picking stores the choice (P.3) and power
		//cycles to apply it; dismissing stores nothing, so the next launch asks
		//again. A sibling-folder pack always suppresses it (§4).
		[ObservableProperty] public partial bool IsPlayerPackPickerVisible { get; set; }
		[ObservableProperty] public partial List<PlayerPackChoice> PlayerPackChoices { get; set; } = new();

		//P.7 (PRD Part B §6.1): the Player-mode "Enhancements" quick-toggle
		//panel. Reached from the overlay, same as Pack/Settings - the overlay
		//already paused the game, so opening this panel does not pause again.
		//On/off state is derived from existing config (VideoConfig.AspectRatio/
		//VideoFilter, the per-console overclock field), refreshed whenever the
		//panel opens, never a second source of truth.
		[ObservableProperty] public partial bool IsEnhancementsPanelVisible { get; set; }
		[ObservableProperty] public partial bool IsTexturesEnabled { get; set; }
		[ObservableProperty] public partial bool IsAudioEnabled { get; set; }
		[ObservableProperty] public partial bool IsBorderEnabled { get; set; }
		[ObservableProperty] public partial bool IsWideScrnEnabled { get; set; }
		[ObservableProperty] public partial bool IsOverclockEnabled { get; set; }
		[ObservableProperty] public partial bool IsOverclockSupported { get; set; }

		//P.5 (PRD Part B §6): the currently-applied pack's name/layers for
		//the overlay chip and the "Applied ..." toast.
		[ObservableProperty] public partial string CurrentPackName { get; private set; } = "";
		//W-S1: the status line's "pack Contra 80s 1.2"; set before CurrentPackName,
		//whose change refreshes the line.
		private string _currentPackVersion = "";
		private bool _currentPackAutoOnly;
		[ObservableProperty] public partial string CurrentPackLayers { get; private set; } = "";

		[ObservableProperty] public partial bool IsNativeRendererVisible { get; private set; }
		[ObservableProperty] public partial bool IsSoftwareRendererVisible { get; private set; }

		public SoftwareRendererViewModel SoftwareRenderer { get; } = new();

		public Configuration Config { get; }
		public NativeRenderer? Renderer { get; internal set; }

		//P.5: the ROM sha1 the current picker evaluation ran against (the key of
		//the stored per-ROM preference, §4 step 1 - before any patches[] apply).
		private string _pickerRomSha1 = "";

		public MainWindowViewModel()
		{
			Instance = this;

			Config = ConfigManager.Config;
			//Before RomInfo: OnRomInfoChanged feeds the shell's status line.
			Shell = new WorkspaceShellViewModel(Config.Preferences.Workspace, OperatingSystem.IsMacOS());
			Shell.WorkspaceChanged += OnWorkspaceChanged;
			IsPlayWorkspace = Shell.IsPlay;
			Remaster = new RemasterWorkspaceViewModel(Config.Remaster, cfg => RemasterFeasibilityProbe.Measure(cfg.PythonPath, cfg.ToolsFolder),
				new JobProcessLauncher(), OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64);
			Remaster.ActivityChanged += OnRemasterActivityChanged;
			Shell.FollowPaintedCells(Remaster);
			InitShare();

			MainMenu = new MainMenuViewModel(this);
			RomInfo = new RomInfo();
			RecentGames = new RecentGamesViewModel();
			WatchPlaySurfaces();
			UpdateShellState();
			UpdateRemasterSurfaces();

			UpdateMenuVisibility();
		}

		//G.1 (W-S3): the switcher rows and ⌘1/⌘2/⌘3 land here. Switching only
		//changes what the window shows (§13.2): it never pauses, stops or resets
		//the emulator, never picks a pack and rewrites no other setting - the one
		//write is the persisted Workspace key itself.
		public bool SelectWorkspace(Workspace target)
		{
			return Shell.Select(target);
		}

		private void OnWorkspaceChanged(Workspace workspace)
		{
			IsPlayWorkspace = Shell.IsPlay;
			UpdateRemasterSurfaces();
			Config.Preferences.Workspace = workspace;
			Config.Save();
		}

		//G.1 (§13.6, rule 11): the one silent switch - a ROM opened from the
		//operating system lands in Play.
		public void LandInPlayForOsOpen()
		{
			SelectWorkspace(Workspace.Play);
		}

		//G.1 (W-S2): the Tools ⋯ "Show classic menu bar" checkbox, the only home
		//of ShowClassicMenuBar (rule 12). The checkbox binds one-way; this is the
		//single writer, then re-applies the chrome and persists the value.
		public void ToggleClassicMenuBar()
		{
			Config.Preferences.ShowClassicMenuBar = !Config.Preferences.ShowClassicMenuBar;
			UpdateMenuVisibility();
			Config.Save();
		}

		//G.1 (§13.2, §13.8 Q4): an upgraded install gets "your menus are under
		//Tools ⋯" once, then never again. Returns true when the toast was due
		//(the caller displays it); the flag is persisted before returning.
		public bool ConsumeClassicMenuNotice()
		{
			if(!ClassicMenuNotice.ShouldShow(Config.Preferences.ClassicMenuNoticeShown, Config.Preferences.ShowClassicMenuBar)) {
				return false;
			}
			Config.Preferences.ClassicMenuNoticeShown = true;
			Config.Save();
			return true;
		}

		private void UpdateShellState()
		{
			bool gameLoaded = RomInfo.Format != RomFormat.Unknown;
			Shell.UpdateGameState(gameLoaded, IsGamePaused, RomInfo.GetRomName(), CurrentPackName, _currentPackVersion, _currentPackAutoOnly, IsPlayerPackPickerVisible);
		}

		partial void OnIsGamePausedChanged(bool value) => UpdateShellState();
		//W-P5's first-start picker sits over the running game: the bar shows.
		partial void OnIsPlayerPackPickerVisibleChanged(bool value) => UpdateShellState();
		partial void OnCurrentPackNameChanged(string value) => UpdateShellState();

		//P.4/G.1 (PRD Part B §6, §13.2): with ShowClassicMenuBar off the menu
		//bar is hidden entirely (AutoHideMenu is ignored - the menus are under
		//Tools ⋯); with it on, the classic AutoHideMenu rule applies. Re-evaluated
		//whenever ShowClassicMenuBar changes (Tools ⋯ checkbox).
		//The rule itself lives in PlayerChrome, shared with
		//MouseManager.UpdateMainMenuVisibility() so the two cannot drift. At
		//construction there is no window or cursor state yet, so the fullscreen /
		//menu-open / hover-band inputs are all false, which reduces to the
		//"ShowClassicMenuBar && !AutoHideMenu".
		private void UpdateMenuVisibility()
		{
			IsMenuVisible = PlayerChrome.IsMenuVisible(Config.Preferences.ShowClassicMenuBar, false, Config.Preferences.AutoHideMenu, false, false);
		}

		//P.4/G.2: the overlay shortcut lands in TogglePlayerOverlay
		//(MainWindowViewModel.PauseOverlay.cs), which routes Esc through the
		//host-free PlayEsc order: game → W-P4 → resume. P.4's "Advanced GUI"
		//overlay item is gone (W-P4): the UiMode choice is reached from Tools ⋯ ›
		//Settings › Preferences, in the bar the overlay reveals.

		//P.5 (PRD Part B §5): decides whether the Player picker opens for
		//the loaded ROM and, when it does, fills the competing choices. Data is
		//injected (pack list + ROM sha1 from the code-behind) so the decision
		//stays host-free: MepPackListParser -> PackPreferenceResolver (content_id
		//merge + preference) -> PlayerPackPicker.ShouldOpen. Returns true when
		//the picker is showing. Also refreshes the current-pack chip/toast data.
		public bool EvaluatePlayerPackPicker(string packListText, string romSha1)
		{
			_pickerRomSha1 = romSha1;

			if(Config.Preferences.UiMode != UiMode.Player) {
				IsPlayerPackPickerVisible = false;
				return false;
			}

			BuildPackPickerData(packListText, romSha1, out PackPreferenceResolver.Resolution resolution, out bool hasSibling);
			UpdateCurrentPack(resolution);

			//W-P5's "No pack" is a stored choice too: it applies silently.
			bool open = PlayerPackPicker.ShouldOpen(hasSibling, PlayerPackPicker.DistinctPackIdCount(resolution.Candidates), resolution.HasEffectivePreference);
			IsPlayerPackPickerVisible = open;
			return open;
		}

		//P.5 §5: the overlay chip click ("changing the choice later") - always
		//offers to change the current pick, so it opens the picker whenever 2+
		//distinct pack_ids exist, even with a stored preference. Returns true
		//when the picker is showing.
		//#736: hasCommunityOffer keeps W-P4's Pack row on W-P6, which holds the
		//offer (PackRowRoute.For); W-P6's own Change Pack… passes false.
		public bool OpenPlayerPackPickerForChange(string packListText, string romSha1, bool hasCommunityOffer = false)
		{
			_pickerRomSha1 = romSha1;

			if(Config.Preferences.UiMode != UiMode.Player) {
				return false;
			}

			BuildPackPickerData(packListText, romSha1, out PackPreferenceResolver.Resolution resolution, out bool hasSibling);
			UpdateCurrentPack(resolution);

			//2+ packs, or "No pack" with a pack to go back to (W-P5); an offer
			//stays on W-P6 (#736).
			if(hasCommunityOffer || !PlayerPackPicker.CanChangeChoice(hasSibling, PlayerPackPicker.DistinctPackIdCount(resolution.Candidates), resolution.PrefersNoPack)) {
				return false;
			}
			IsPlayerPackPickerVisible = true;
			return true;
		}

		//Shared: parse the pack list, run the §5 content_id merge + preference,
		//and refresh the picker choices. Sibling detection comes from the parser
		//(origin column 2).
		private void BuildPackPickerData(string packListText, string romSha1, out PackPreferenceResolver.Resolution resolution, out bool hasSibling)
		{
			MepPackListResult parsed = MepPackListParser.Parse(packListText);
			//#693: a disabled pack is neither offered nor counted.
			List<PackPreferenceResolver.Candidate> candidates = OfferedCandidates(parsed);

			Dictionary<string, MepPackListEntry> entriesByContainer = new(StringComparer.OrdinalIgnoreCase);
			foreach(MepPackListEntry e in parsed.Packs) {
				entriesByContainer[e.Container] = e;
			}

			resolution = PackPreferenceResolver.Resolve(candidates, Config.EnhancementPacks.GetRomPackPreference(romSha1));
			//Issue #150: the §4 sibling-suppresses-picker rule targets a human-authored
			//sibling pack; an auto/-only sibling (the F5 bootstrap's machine layer) is
			//not a user choice and must not suppress the picker (same human-vs-auto
			//distinction already applied in core via the isAutoOnly column).
			hasSibling = parsed.Packs.Any(e => e.Source == "sibling" && !e.IsAutoOnly && e.Enabled);

			//P.6 §5: the picker sorts by community 👍 (catalog MEI votes) first,
			//then by name - local-only packs (votes 0) fall back to name order.
			PlayerPackChoices = resolution.Candidates
				.Select(c => new PlayerPackChoice(c, entriesByContainer.TryGetValue(c.Container, out MepPackListEntry? entry) ? entry : null,
					CommunityPackInstallService.GetVotes(PackPreferenceResolver.DerivePackId(c)),
					CommunityPackInstallService.GetErrata(PackPreferenceResolver.DerivePackId(c))))
				.OrderByDescending(c => c.Votes)
				.ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();
			//W-P5: "No pack" is the last row, offered whenever a pack is listed.
			if(PlayerPackPicker.OffersNoPack(PlayerPackChoices.Count)) {
				PlayerPackChoices = PlayerPackChoices.Append(PlayerPackChoice.NoPackRow(
					ResourceHelper.GetMessage("PackPickerNoPackTitle"),
					ResourceHelper.GetMessage(PackPickerRow.NoPackDetailKey(Config.Audio.EnableEnhancedAudio)))).ToList();
			}
			//G.4 (W-P5): one radio starts selected - the stored choice, "No pack" included.
			SelectInitialPackChoice(resolution.PrefersNoPack ? PackPreferenceResolver.NoPack : resolution.PreferredContainer);
		}

		private static List<PackPreferenceResolver.Candidate> OfferedCandidates(MepPackListResult parsed)
		{
			return PlayerPackPicker.Offered(parsed.Packs.Select(e => new PackPreferenceResolver.Candidate {
				Container = e.Container,
				Name = e.Name,
				PackId = e.PackId,
				ContentId = e.ContentId,
				Version = e.Version,
				Enabled = e.Enabled,
				IsAutoOnly = e.IsAutoOnly,
				IsSibling = e.Source == PackOrigin.Sibling
			}));
		}

		//The current pack (chip/toast): the one the core renders (#703,
		//PlayerPackPicker.CurrentContainer), not the picker's display order.
		private PlayerPackChoice? RenderedPackChoice(PackPreferenceResolver.Resolution resolution)
		{
			string? container = PlayerPackPicker.CurrentContainer(resolution.Candidates, resolution.PreferredContainer, resolution.PrefersNoPack);
			return container == null ? null : PlayerPackChoices.FirstOrDefault(c => c.Container.Equals(container, StringComparison.OrdinalIgnoreCase));
		}

		private void UpdateCurrentPack(PackPreferenceResolver.Resolution resolution)
		{
			_currentPackVersion = "";
			_currentPackAutoOnly = false;
			CurrentPackName = "";
			CurrentPackLayers = "";

			PlayerPackChoice? current = RenderedPackChoice(resolution);
			if(current != null) {
				_currentPackVersion = current.Version;
				_currentPackAutoOnly = current.IsAutoOnly;
				CurrentPackName = current.Name;
				CurrentPackLayers = current.Layers;
			}
		}

		//P.5: the picker's "Apply" - stores the per-ROM-sha1 choice (P.3) and
		//reloads so the chosen pack applies; the next load sees the stored
		//preference and never re-opens the picker (silent). ADR-0244 (P.9): the
		//reload keeps the player's place where PackChangePolicy allows it, and
		//falls back to the power cycle it always was everywhere else.
		public void PickPlayerPack(string container)
		{
			PlayerPackChoice? choice = PlayerPackChoices.FirstOrDefault(c => c.Container.Equals(container, StringComparison.OrdinalIgnoreCase));
			if(choice == null || string.IsNullOrEmpty(_pickerRomSha1)) {
				return;
			}

			Config.EnhancementPacks.SetRomPackPreference(_pickerRomSha1, choice.PackId);
			Config.ApplyConfig();
			Config.Save();
			IsPlayerPackPickerVisible = false;
			//The chosen pack is enabled (#693), so it is what the core renders
			//next; "No pack" renders none (W-P4's Pack row and the status line).
			_currentPackVersion = choice.IsNoPack ? "" : choice.Version;
			_currentPackAutoOnly = !choice.IsNoPack && choice.IsAutoOnly;
			CurrentPackName = choice.IsNoPack ? "" : choice.Name;
			CurrentPackLayers = choice.IsNoPack ? "" : choice.Layers;
			//#691: from W-P4, back to W-P4 (in place) or to the game (restart).
			PackPickReturn back = PackPickClose.After(_packPickerFromOverlay, LayerChangeKeepsPlace);
			_packPickerFromOverlay = false;
			PackPickApplied = LoadRomHelper.ApplyPackChange(RomInfo.ConsoleType, LoadRomHelper.PowerCycle);
			if(back == PackPickReturn.Overlay) {
				OpenPauseOverlay();
			} else if(back == PackPickReturn.Game) {
				IsPlayerOverlayVisible = false;
				EmuApi.Resume();
			}
		}

		//The last pick's in-place swap (LoadRomHelper.ApplyPackChange): the
		//headless tests wait for it, like ShareGateRefresh.
		public Task PackPickApplied { get; private set; } = Task.CompletedTask;

		//P.5: dismissing stores nothing - the game keeps playing un-enhanced this
		//session and, with no preference on disk, the picker asks again next launch.
		public void DismissPlayerPackPicker()
		{
			IsPlayerPackPickerVisible = false;
			//G.2 (rule 8): opened from W-P4's Pack row, it closes back to W-P4.
			if(_packPickerFromOverlay) {
				_packPickerFromOverlay = false;
				OpenPauseOverlay();
			}
		}

		//P.7 (PRD Part B §6.1): the overlay's "Enhancements" item - refreshes
		//the panel's on/off state from whatever is currently configured (never
		//stale from a previous ROM/session) and shows it in place of the
		//overlay (same replace-not-stack behavior as the Pack picker). The
		//overlay already paused the game to open, so this does not pause again.
		public void OpenEnhancementsPanel()
		{
			IsPlayerOverlayVisible = false;
			RefreshEnhancementsState();
			//G.4 (W-P7): the switches edit a draft that the Apply button applies.
			LoadEnhancementsDraft();
			IsEnhancementsPanelVisible = true;
		}

		//Also feeds W-P4's "Enhancements · N on" row (G.2).
		private void RefreshEnhancementsState()
		{
			IsTexturesEnabled = Config.EnhancementPacks.EnableTextures;
			IsAudioEnabled = Config.EnhancementPacks.EnableAudio;
			IsBorderEnabled = Config.EnhancementPacks.EnableBorder;
			IsWideScrnEnabled = Config.Video.AspectRatio == VideoAspectRatio.Widescreen;
			IsOverclockSupported = PlayerEnhancementsToggle.SupportsOverclock(RomInfo.ConsoleType);
			IsOverclockEnabled = RomInfo.ConsoleType switch {
				ConsoleType.Nes => PlayerEnhancementsToggle.IsNesOverclockOn(Config.Nes.PpuExtraScanlinesBeforeNmi, Config.Nes.PpuExtraScanlinesAfterNmi),
				ConsoleType.Gameboy => PlayerEnhancementsToggle.IsScanlineOverclockOn(Config.Gameboy.OverclockScanlineCount),
				ConsoleType.Gba => PlayerEnhancementsToggle.IsScanlineOverclockOn(Config.Gba.OverclockScanlineCount),
				_ => false
			};
		}

		public void CloseEnhancementsPanel()
		{
			IsEnhancementsPanelVisible = false;
			OpenPauseOverlay();
		}

		//Texture/Audio/Border (§6.1, ADR-0149): plain passthrough to the existing MEP layer
		//switches - applies through a ROM reload, like the rest of
		//EnhancementPackConfig; ADR-0244 (P.9) makes that reload keep the
		//player's place where PackChangePolicy allows it.
		public void ToggleTextures()
		{
			IsTexturesEnabled = ToggleLayer(v => Config.EnhancementPacks.EnableTextures = v, Config.EnhancementPacks.EnableTextures);
		}

		public void ToggleAudio()
		{
			IsAudioEnabled = ToggleLayer(v => Config.EnhancementPacks.EnableAudio = v, Config.EnhancementPacks.EnableAudio);
		}

		public void ToggleBorder()
		{
			IsBorderEnabled = ToggleLayer(v => Config.EnhancementPacks.EnableBorder = v, Config.EnhancementPacks.EnableBorder);
		}

		//Flips one MEP layer switch, persists it and applies it (in place where
		//ADR-0244 allows, else by reloading the ROM); returns the new value.
		private bool ToggleLayer(Action<bool> setter, bool current)
		{
			bool next = !current;
			setter(next);
			Config.EnhancementPacks.ApplyConfig();
			Config.Save();
			LoadRomHelper.ApplyPackChange(RomInfo.ConsoleType, LoadRomHelper.ReloadRom);
			return next;
		}

		//WideScrn (§6.1; Hi-res filter moved to Settings › Look, ADR-0246): restore-not-clobber via the host-free
		//PlayerEnhancementsToggle.ToggleEnumPreset - turning on stashes whatever
		//Advanced had configured (unless it's already the preset), turning off
		//restores exactly that. Applies immediately (renderer-only, no reload).
		public void ToggleWideScrn()
		{
			(VideoAspectRatio newCurrent, VideoAspectRatio newPrior) = PlayerEnhancementsToggle.ToggleEnumPreset(
				Config.Video.AspectRatio, Config.PlayerEnhancements.WideScrnPriorAspectRatio, VideoAspectRatio.Widescreen, !IsWideScrnEnabled);
			Config.Video.AspectRatio = newCurrent;
			Config.PlayerEnhancements.WideScrnPriorAspectRatio = newPrior;
			Config.Video.ApplyConfig();
			Config.Save();
			IsWideScrnEnabled = newCurrent == VideoAspectRatio.Widescreen;
		}

		//Overclock (§6.1): plain 0/preset toggle (no restore-not-clobber - see
		//PlayerEnhancementsToggle's comment), per console; no-op where
		//unsupported (SMS has no overclock knob). Needs a reset to take effect.
		public void ToggleOverclock()
		{
			bool turningOn = !IsOverclockEnabled;
			switch(RomInfo.ConsoleType) {
				case ConsoleType.Nes:
					Config.Nes.PpuExtraScanlinesBeforeNmi = turningOn ? PlayerEnhancementsToggle.NesOverclockBeforeNmi : 0;
					Config.Nes.PpuExtraScanlinesAfterNmi = turningOn ? PlayerEnhancementsToggle.NesOverclockAfterNmi : 0;
					Config.Nes.ApplyConfig();
					break;
				case ConsoleType.Gameboy:
					Config.Gameboy.OverclockScanlineCount = turningOn ? PlayerEnhancementsToggle.ScanlineOverclockPreset : 0;
					Config.Gameboy.ApplyConfig();
					break;
				case ConsoleType.Gba:
					Config.Gba.OverclockScanlineCount = turningOn ? PlayerEnhancementsToggle.ScanlineOverclockPreset : 0;
					Config.Gba.ApplyConfig();
					break;
				default:
					//Not supported on this console (SMS) - the panel keeps the
					//checkbox disabled, but guard here too in case it's ever reached.
					return;
			}
			Config.Save();
			IsOverclockEnabled = turningOn;
			LoadRomHelper.PowerCycle();
		}

		protected override void DisposeView()
		{
			base.DisposeView();
			MainMenu.Dispose();
		}

		public void Init(MainWindow wnd)
		{
			MainMenu.Initialize(wnd);
			RecentGames.Init(GameScreenMode.RecentGames);

			AddDisposable(RecentGames.ObserveProp(nameof(RecentGamesViewModel.Visible), () => {
				if(!RecentGames.Visible) {
					//G.2: a slot grid opened from W-P4 is gone (slot picked or closed).
					_stateGridFromOverlay = false;
				}
				UpdateRendererVisibility();
			}));

			AddDisposable(SoftwareRenderer.ObserveProp(nameof(SoftwareRendererViewModel.FrameSurface), () => {
				UpdateRendererVisibility();
			}));

			AddDisposable(this.ObserveProp(nameof(MainWindowViewModel.RendererSize), UpdateWindowTitle));

			AddDisposable(ReactiveHelper.RegisterForeignObserver([(() => Config, nameof(Configuration.Video)), (() => Config.Video, nameof(VideoConfig.AspectRatio))], UpdateWindowTitle));
			AddDisposable(ReactiveHelper.RegisterForeignObserver([(() => Config, nameof(Configuration.Video)), (() => Config.Video, nameof(VideoConfig.VideoFilter))], UpdateWindowTitle));
			AddDisposable(ReactiveHelper.RegisterForeignObserver([(() => Config, nameof(Configuration.Preferences)), (() => Config.Preferences, nameof(PreferencesConfig.ShowTitleBarInfo))], UpdateWindowTitle));
			//P.4: UiMode switches (the Preferences combo, under Tools ⋯ › Settings
			//since G.2 removed the overlay's "Advanced GUI" item) re-evaluate the chrome immediately - the overlay hides when
			//leaving Player. G.1: the menu bar follows ShowClassicMenuBar instead.
			AddDisposable(ReactiveHelper.RegisterForeignObserver([(() => Config.Preferences, nameof(PreferencesConfig.ShowClassicMenuBar))], UpdateMenuVisibility));
			AddDisposable(ReactiveHelper.RegisterForeignObserver([(() => Config.Preferences, nameof(PreferencesConfig.UiMode))], () => {
				if(Config.Preferences.UiMode != UiMode.Player) {
					ClosePlaySurfaces();
				}
			}));

			UpdateWindowTitle();
		}

		//G.3: which Remaster surface is on screen, then the game picture.
		private void UpdateRemasterSurfaces()
		{
			bool remaster = Shell.Active == Workspace.Remaster;
			//G.6: and while a build is shown in the game.
			IsRemasterGameView = remaster && Remaster.ShowsGame;
			IsRemasterProjectScreenVisible = remaster && !Remaster.ShowsGame;
			if(remaster) {
				//W-R0b: the feasibility gate is measured once, when Remaster is first shown.
				Remaster.EnsureFeasibilityMeasured();
			}
			//G.8: Share's surfaces, then the game picture's layer and the renderer.
			UpdateShareSurfaces();
		}

		private void OnRemasterActivityChanged()
		{
			if(IsRemasterGameView != (Shell.Active == Workspace.Remaster && Remaster.ShowsGame)) {
				UpdateRemasterSurfaces();
			}
			Shell.UpdateRemasterActivity(Remaster.Activity, Remaster.ActivityStatus());
		}

		private void UpdateRendererVisibility()
		{
			//G.1: the native renderer is a native child view drawn above Avalonia
			//content, so it is hidden explicitly outside Play (the emulator keeps
			//running; only the picture is not shown). G.3: Remaster's recording
			//view (W-R2) shows it too. The same reason hides it under W-P4 and the
			//sheets opened over the game (PlayGameLayer).
			IsNativeRendererVisible = PlayGameLayer.ShowsNativeRenderer(IsGameViewVisible, RecentGames.Visible, SoftwareRenderer.FrameSurface != null, IsPlaySurfaceOverGame);
			IsSoftwareRendererVisible = IsGameViewVisible && !RecentGames.Visible && SoftwareRenderer.FrameSurface != null;

			if(Renderer != null) {
				Dispatcher.UIThread.Post(() => {
					Renderer.IsVisible = IsNativeRendererVisible;
				});
			}
		}

		partial void OnRomInfoChanged(RomInfo value)
		{
			bool showAudioPlayer = RomInfo.Format == RomFormat.Nsf || RomInfo.Format == RomFormat.Spc || RomInfo.Format == RomFormat.Gbs || RomInfo.Format == RomFormat.PceHes;
			if(showAudioPlayer) {
				AudioPlayer ??= new AudioPlayerViewModel();
			} else {
				//Drop the live player only when leaving a music ROM; switching between
				//music ROMs keeps it (it mirrors AudioPlayerConfig, not per-ROM state)
				AudioPlayer?.Dispose();
				AudioPlayer = null;
			}

			UpdateWindowTitle();
			UpdateShellState();
			ClosePauseSurfacesOnGameChange();

			bool gameLoaded = RomInfo.Format != RomFormat.Unknown;
			//#689: the jobs get an archive's inner ROM, written out (RemasterRomFile).
			ResourcePath rom = RomInfo.RomPath;
			string romForJobs = gameLoaded ? RemasterRomFile.ForJobs(rom.Path, rom.InnerFile, rom, System.IO.Path.GetTempPath(), EmuApi.ExtractRomFile) : rom.Path;
			Remaster?.UpdateGame(gameLoaded, RomInfo.ConsoleType, RomInfo.GetRomName(), romForJobs,
				gameLoaded ? EmuApi.GetMepSiblingFolder() : "", ConfigManager.EnhancementPackFolder);
			UpdateShareGame(gameLoaded, gameLoaded ? EmuApi.GetMepSiblingFolder() : "");
		}

		private void UpdateWindowTitle()
		{
			string title = "MesenAI";
			string romName = RomInfo.GetRomName();
			if(!string.IsNullOrWhiteSpace(romName)) {
				title += " - " + romName;
				if(ConfigManager.Config.Preferences.ShowTitleBarInfo) {
					FrameInfo baseSize = EmuApi.GetBaseScreenSize();
					double scale = (double)RendererSize.Height / baseSize.Height;
					title += string.Format(" - {0}x{1} ({2:0.###}x, {3})",
						Math.Round(RendererSize.Width),
						Math.Round(RendererSize.Height),
						scale,
						ResourceHelper.GetEnumText(ConfigManager.Config.Video.VideoFilter));
				}
			}
			WindowTitle = title;
		}
	}

	//P.5 (PRD Part B §5): one row of the Player pack picker - a
	//content-merged competing pack. Name/author/version/license/layers come
	//from the core's GetPackListText columns; PackId is the effective pack_id
	//(ADR-0140 id, else the local:<container> rule-4 fallback) that P.3 stores.
	//G.4 (W-P5): one radio row of the picker. IsSelected is the radio; the
	//rows of one picker are exclusive (the sheet's GroupName).
	public sealed partial class PlayerPackChoice : ObservableObject
	{
		[ObservableProperty] public partial bool IsSelected { get; set; }
		//"👍 41"; empty for a local-only pack (no catalog row, votes 0).
		public string VotesText => Votes > 0 ? "👍 " + Votes : "";
		public bool HasVotes => Votes > 0;
		public string Origin { get; } = "";
		public string ContentId { get; } = "";
		//The F5 bootstrap's machine-only sibling (ADR-0049), not a human's pack.
		public bool IsAutoOnly { get; }

		public string Container { get; }
		public string PackId { get; }
		public string Name { get; }
		public string Author { get; }
		public string Version { get; }
		public string License { get; }
		public string Layers { get; }
		//P.6 §5: community 👍 count (catalog MEI `votes`); 0 for local-only packs,
		//which sort by name. Not a download ranking - it only orders the picker.
		public int Votes { get; }
		//One-line metadata row for the picker: "by Author · 1.0 · textures, audio"
		public string Detail { get; }
		//ADR-0152: the picker's known-missing line, e.g. "1 known-missing asset —
		//declared by MesenCE validation, not by the author". Empty when the row's
		//artifact carries no errata, which is the normal case.
		public string KnownMissingNote { get; }
		public bool HasKnownMissing => KnownMissingNote.Length > 0;

		public PlayerPackChoice(PackPreferenceResolver.Candidate candidate, MepPackListEntry? entry, int votes = 0, CommunityPackErrata? errata = null)
		{
			Container = candidate.Container;
			PackId = PackPreferenceResolver.DerivePackId(candidate);
			Name = candidate.Name;
			Version = candidate.Version;
			Author = entry?.Author ?? "";
			License = entry?.License ?? "";
			Layers = string.IsNullOrEmpty(entry?.Sections) ? "" : entry.Sections.Replace(",", ", ");
			Votes = Math.Max(0, votes);
			Origin = entry?.Source ?? "";
			ContentId = candidate.ContentId ?? "";
			IsAutoOnly = candidate.IsAutoOnly;

			//G.4 (W-P5): "by Tastic · 1.2 · textures, audio"; the license moved
			//to the pack detail (W-P6, rule 3). GetMessage always formats, so the
			//"by {0}" pattern is read back by formatting it with its own "{0}".
			Detail = PackPickerRow.Detail(Author, Version, Layers, ResourceHelper.GetMessage("PackByAuthor", "{0}"), ResourceHelper.GetMessage("PackAuthorUnknown"));
			KnownMissingNote = BuildKnownMissingNote(errata);
		}

		private static string BuildKnownMissingNote(CommunityPackErrata? errata)
		{
			int count = errata?.Count ?? 0;
			if(count == 0) {
				return "";
			}
			string who = string.IsNullOrWhiteSpace(errata!.DeclaredBy) ? "MesenCE validation" : errata.DeclaredBy!;
			string assets = count == 1 ? "asset" : "assets";
			return count + " known-missing " + assets + " — declared by " + who + ", not by the author";
		}

		//W-P5's "No pack" row: its container and pack_id are the sentinel the
		//preference stores (PackPreferenceResolver.NoPack), so Use This Pack
		//stores it like any pick.
		public bool IsNoPack { get; }

		private PlayerPackChoice(string name, string detail)
		{
			Container = PackPreferenceResolver.NoPack;
			PackId = PackPreferenceResolver.NoPack;
			Name = name;
			Detail = detail;
			Author = "";
			Version = "";
			License = "";
			Layers = "";
			KnownMissingNote = "";
			IsNoPack = true;
		}

		public static PlayerPackChoice NoPackRow(string name, string detail) => new(name, detail);

		public override string ToString() => Name;
	}
}
