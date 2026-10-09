using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1010: the §13.5 wireframes no other *RenderTests case renders - the shell
//frame (W-S1), the running game (W-P3), Settings › Audio and Controls (W-P8b,
//W-P8c), the install pill (W-P9) and the first-run choices (W-P12) - so the
//ADR-0249 render gate writes them (PlayerRender.Save: the PNG and, for a W-P
//name, its wireframe report) and EXPECTED_WIREFRAME_RENDERS in
//scripts/checks/verify_render_gate.py fails when one stops being written.
//Each case pins the screen's elements; the look against the PNG is a person's
//call, recorded in the PR that added the case.
[Collection(NativeCoreCollection.Name)]
public class WireframeCoverageRenderTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly int _hintsShown = ConfigManager.Config.Preferences.PlayMenuHintsShown;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-wireframes-" + Guid.NewGuid().ToString("N"));

	//The game is synthetic and named to match no catalog row, and the
	//auto-install is off: a real install's pill must not race the one driven here.
	public WireframeCoverageRenderTests()
	{
		Directory.CreateDirectory(_folder);
		//#1017: the recents this class stamps live in its own folder.
		ConfigManager.RecentGamesFolderOverride = Path.Combine(_folder, "RecentGames");
		Directory.CreateDirectory(ConfigManager.RecentGamesFolderOverride);
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = false;
	}

	public void Dispose()
	{
		//CI without a core skips: an EmuApi call here would turn the skip into a failure.
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
		}
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		prefs.PlayMenuHintsShown = _hintsShown;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		ConfigManager.RecentGamesFolderOverride = null;
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(5);
		}
		Dispatcher.UIThread.RunJobs();
	}

	//The renders' window (1100 x 740) in Player mode on Play, with no game running.
	private static (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus");
		if(EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitFor(() => !EmuApi.IsRunning() && model.RecentGames.Visible, "the previous test's game never stopped");
		}
		return (window, model);
	}

	private void StartGame(MainWindow window, MainWindowViewModel model, string name)
	{
		string rom = Path.Combine(_folder, name + ".nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		LoadRomHelper.LoadFile(rom);
		Border card = window.FindNamed<Border>("PlayLoadWaitCard");
		WaitFor(() => EmuApi.IsRunning() && !card.IsOnScreen() && model.IsNativeRendererVisible, "the game never showed its picture");
	}

	//#1167: the render has to show the ring where a player sees it, and the
	//player sees it on the tab whose page is up. Every way to a tab at runtime
	//ends with the ring on it: a pointer press on a header focuses it (Avalonia
	//focuses a focusable control on press), and the pad's Confirm is
	//PlayPadNavigationWiring's TabItem branch - `tab.IsSelected = true` - which
	//only ever runs on the tab the ring is already on. Setting SelectedIndex
	//alone is neither, so it left the ring on the tab the sheet opened on
	//(Display) and the render showed a ring on Display above the Controls page.
	private static TabItem ShownTab(MainWindow window)
	{
		TabControl strip = window.FindNamed<TabControl>("PlayerSettingsTabs");
		return Assert.IsType<TabItem>(strip.ContainerFromIndex(strip.SelectedIndex));
	}

	//The ring's owner has to be that tab, or the render lies about which page
	//is up (Directional is what paints it: PlayerTheme's :focus-visible).
	private static void AssertRingOnShownTab(MainWindow window)
	{
		TabItem shown = ShownTab(window);
		Control? focused = window.FocusManager?.GetFocusedElement() as Control;
		Assert.True(ReferenceEquals(shown, focused), $"the ring is on '{focused?.Name}' while the page under it is '{shown.Name}'");
		Assert.True(shown.IsFocused, $"the ring is not on the tab the page shows ('{shown.Name}')");
	}

	//W-P8b / W-P8c / W-P12's home: a sheet over W-P4, opened from its Settings row.
	private static Border OpenSettings(MainWindow window, MainWindowViewModel model, ConfigWindowTab tab)
	{
		model.RomInfo = new RomInfo() { RomPath = "/roms/Contra (USA).nes", ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.OpenPauseOverlay();
		//The overlay pauses the game (EmuApi.Pause -> GamePaused), which brings
		//the shell bar and the status line back above the scrim, as W-P4 does.
		model.IsGamePaused = true;
		Dispatcher.UIThread.RunJobs();
		window.FindNamed<Button>("OverlaySettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(tab);
		Dispatcher.UIThread.RunJobs();
		//#1167: the ring rides the tab whose page is up (AssertRingOnShownTab
		//checks it), as it does at runtime. SelectedIndex alone is not a move,
		//so without this the render kept the ring on the tab the sheet opened
		//on. System is the one tab that is not the strip (ADR-0256 Decision 8):
		//its claim lands the ring on the storage choice, and picking that tab
		//here re-arbitrated it already, so it must not be taken back.
		if(tab != ConfigWindowTab.System) {
			ShownTab(window).Focus(NavigationMethod.Directional);
			Dispatcher.UIThread.RunJobs();
		}
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
		Border sheet = window.FindNamed<Border>("PlayerSettingsSheet");
		Assert.True(sheet.IsOnScreen(), "the Settings sheet did not open over W-P4");
		Assert.True(window.FindNamed<Views.WorkspaceShellBar>("ShellBar").IsOnScreen(), "the bar is hidden behind the paused game's sheet");
		Assert.True(window.FindNamed<Border>("ShellStatusLine").IsOnScreen(), "the status line is hidden behind the paused game's sheet");
		return sheet;
	}

	//#1017: W-S1 stamps .rgd files to order the Recents, so the folder it writes
	//to must be this test's own, never the developer's real RecentGames.
	[AvaloniaFact]
	public void Recents_folder_is_not_the_developers_real_one()
	{
		string real = Path.Combine(ConfigManager.HomeFolder, "RecentGames");
		Assert.NotEqual(real, ConfigManager.RecentGamesFolder);
		Assert.StartsWith(_folder, ConfigManager.RecentGamesFolder);
	}

	//W-S1: the two controls at rest (the active profile, Tools ⋯) on the 52 px
	//bar, the workspace content, and the read-only status sentence, drawn over
	//the W-P2 home it frames.
	[AvaloniaFact]
	public void Shell_frame_renders_as_W_S1()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		DateTime written = DateTime.Now;
		foreach(string game in new[] { "Contra (USA)", "Castlevania (USA)", "Metroid (USA)" }) {
			string file = Path.Combine(ConfigManager.RecentGamesFolder, game + ".rgd");
			File.WriteAllText(file, "");
			File.SetLastWriteTime(file, written);
			written = written.AddMinutes(-1);
		}
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Dispatcher.UIThread.RunJobs();

		DockPanel bar = window.FindNamed<DockPanel>("TitleBarArea");
		Assert.True(bar.IsOnScreen());
		Assert.Equal(52, bar.Bounds.Height, 0.5);
		Assert.True(window.FindNamed<Button>("ProfileButton").IsOnScreen());
		Assert.Equal("Play", window.FindNamed<TextBlock>("ProfileButtonName").Text);
		Assert.True(window.FindNamed<MenuItem>("ToolsMenuButton").IsOnScreen());
		Border status = window.FindNamed<Border>("ShellStatusLine");
		Assert.True(status.IsOnScreen());
		Assert.Equal(26, status.Bounds.Height, 0.5);
		Assert.Equal("No game loaded", window.FindNamed<TextBlock>("ShellStatusText").Text);
		Assert.True(window.FindNamed<Button>("PlayHomeContinueButton").IsOnScreen(), "W-S1 frames the W-P2 home");

		PlayerRender.Save(PlayerRender.Capture(window), "W-S1");
	}

	//W-P3: the game fills the window - no bar, no status line - and the start's
	//toast teaches the way into W-P4 (ADR-0251). The toast is a core HUD
	//message drawn on the game picture, so the render shows the chrome only.
	[AvaloniaFact]
	public void Running_game_renders_as_W_P3()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		//The headless core has no key manager: hand the toast the default slots.
		model.OverlayBindingKeyNames = () => (new List<string> { "Esc" }, new List<string> { "Pad1 Select", "Pad1 Start" }, new List<string>());
		model.ConnectedGamepadCount = () => 0;
		ConfigManager.Config.Preferences.PlayMenuHintsShown = 0;
		try {
			StartGame(window, model, "Pixel Quest");
			WaitFor(() => model.LastEntryToast != null, "the game start showed no entry toast");

			Assert.Equal("Esc for the menu", model.LastEntryToast!.Message);
			Assert.False(window.FindNamed<Views.WorkspaceShellBar>("ShellBar").IsOnScreen(), "the bar shows while the game runs (W-P3)");
			Assert.False(window.FindNamed<Border>("ShellStatusLine").IsOnScreen(), "the status line shows while the game runs (W-P3)");
			Assert.False(model.IsPlayerOverlayVisible);
			Assert.True(model.IsNativeRendererVisible);

			PlayerRender.Save(PlayerRender.Capture(window), "W-P3");
		} finally {
			model.OverlayBindingKeyNames = () => (new List<string>(), new List<string>(), new List<string>());
		}
	}

	//W-P8b: Audio is one inset list - Sound, Volume, Output device - with
	//More in Options… and Done; no classic page, no scrollbar.
	[AvaloniaFact]
	public void Settings_audio_renders_as_W_P8b()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		Border sheet = OpenSettings(window, model, ConfigWindowTab.Audio);

		Assert.Equal(340, sheet.Bounds.Height, 0.5);
		Assert.True(sheet.FindNamed<Border>("AudioSettingsGroup").IsOnScreen());
		Assert.IsType<ToggleSwitch>(sheet.FindNamed<ToggleButton>("chkAudioEnabled"));
		Assert.True(sheet.FindNamed<Slider>("sldAudioVolume").IsOnScreen());
		Assert.True(sheet.FindNamed<ComboBox>("cboAudioDevice").IsOnScreen());
		Assert.False(sheet.FindNamed<ToggleButton>("chkAudioMenuSounds").IsOnScreen(), "Menu sounds stays hidden until the host reports it available (#1105)");
		Assert.True(sheet.FindNamed<Button>("btnPlayerSettingsMoreInOptions").IsOnScreen());
		Assert.True(sheet.FindNamed<Button>("btnPlayerSettingsDone").IsOnScreen());
		Assert.DoesNotContain(sheet.FindAll<ScrollBar>(), s => s.IsOnScreen());
		AssertRingOnShownTab(window);

		PlayerRender.Save(PlayerRender.Capture(window), "W-P8b");
	}

	//#1105: the Menu sounds row appears only through the host capability seam.
	[AvaloniaFact]
	public void Settings_audio_shows_the_menu_sounds_row_when_the_host_reports_it_available()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Func<bool> original = PlayerSettingsEssentials.MenuSoundsAvailable;
		try {
			PlayerSettingsEssentials.MenuSoundsAvailable = () => true;
			(MainWindow window, MainWindowViewModel model) = ShowPlay();
			Border sheet = OpenSettings(window, model, ConfigWindowTab.Audio);

			Assert.Equal(388, sheet.Bounds.Height, 0.5);
			Assert.True(sheet.FindNamed<ToggleButton>("chkAudioMenuSounds").IsOnScreen());
		} finally {
			PlayerSettingsEssentials.MenuSoundsAvailable = original;
		}
	}

	//W-P8c: Controls is the same list - the connected pads, Rumble, Stick
	//deadzone - with More in Options… and Done.
	[AvaloniaFact]
	public void Settings_controls_renders_as_W_P8c()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		//#1112: pinned here, not left to whatever pad an earlier case or the machine has in hand.
		Func<bool> originalAimable = PlayerSettingsEssentials.MenuTickAimable;
		PlayerSettingsEssentials.MenuTickAimable = () => false;
		try {
			RenderControlsSheetWithoutMenuTick();
		} finally {
			PlayerSettingsEssentials.MenuTickAimable = originalAimable;
		}
	}

	private void RenderControlsSheetWithoutMenuTick()
	{
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		Border sheet = OpenSettings(window, model, ConfigWindowTab.Input);

		Assert.Equal(340, sheet.Bounds.Height, 0.5);
		Assert.True(sheet.FindNamed<Border>("ControlsSettingsGroup").IsOnScreen());
		Assert.True(sheet.FindNamed<TextBlock>("txtControlsPads").IsOnScreen());
		Assert.True(sheet.FindNamed<Slider>("sldControlsRumble").IsOnScreen());
		Assert.True(sheet.FindNamed<Slider>("sldControlsDeadzone").IsOnScreen());
		Assert.False(sheet.FindNamed<ToggleButton>("chkControlsMenuTick").IsOnScreen(), "Menu tick stays hidden until the pad in hand is aimable (#1112)");
		Assert.True(sheet.FindNamed<Button>("btnPlayerSettingsMoreInOptions").IsOnScreen());
		Assert.True(sheet.FindNamed<Button>("btnPlayerSettingsDone").IsOnScreen());
		Assert.DoesNotContain(sheet.FindAll<ScrollBar>(), s => s.IsOnScreen());
		AssertRingOnShownTab(window);

		PlayerRender.Save(PlayerRender.Capture(window), "W-P8c");
	}

	//Runs a case with the host answering "the pad in hand is aimable" - the one
	//seam, so no hardware is needed - and puts both seams and Rumble back.
	private static void WithAimablePad(uint rumble, Action body)
	{
		Func<bool> original = PlayerSettingsEssentials.MenuTickAimable;
		uint originalRumble = ConfigManager.Config.Input.ForceFeedbackIntensity;
		try {
			HapticTickOutput.SetSeamsForTest(_ => true, null);
			HapticTickOutput.PadInHand = 0;
			PlayerSettingsEssentials.MenuTickAimable = HapticTickOutput.PadInHandAimable;
			ConfigManager.Config.Input.ForceFeedbackIntensity = rumble;
			body();
		} finally {
			PlayerSettingsEssentials.MenuTickAimable = original;
			TestAppBuilder.ResetPadSeams();
			ConfigManager.Config.Input.ForceFeedbackIntensity = originalRumble;
		}
	}

	//ADR-0249: the fresh render's regions (the settings sheet, the Menu tick row)
	//must match the wireframe save for the known deviations, and match the
	//committed baseline in UI.Tests/Theme/PlayerRenders/.
	private static void AssertWireframeRegions(Bitmap frame, string wId)
	{
		RgbFrame fresh = PlayerRender.Rgb(frame);
		IReadOnlyList<RegionResult> results = PlayerWireframe.Compare(wId, fresh, RgbFrame.FromPng(PlayerRender.WireframePath(wId)));
		List<string> violations = PlayerWireframe.Gate(wId, results, PlayerRender.DeviationsOnThisHost(wId)).ToList();
		string committed = PlayerRender.DriftBaselinePath(wId);
		//The Linux baseline is not committed yet for W-P8e (the render-gate job's
		//player-renders artifact refreshes it), so off macOS only the wireframe
		//half gates until it exists.
		if(OperatingSystem.IsMacOS() || File.Exists(committed)) {
			Assert.True(File.Exists(committed), $"{wId} has no committed render at {committed}; commit {Path.Combine(PlayerRender.OutputFolder, wId + ".png")} there");
			violations.AddRange(PlayerWireframe.Drift(wId, fresh, RgbFrame.FromPng(committed), committed));
		}
		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	//W-P8e: Controls with the Menu tick row, which exists only because the host
	//answered that the pad in hand is aimable (#1112).
	[AvaloniaFact]
	public void Settings_controls_with_menu_tick_renders_as_W_P8e()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		WithAimablePad(5, () => {
			(MainWindow window, MainWindowViewModel model) = ShowPlay();
			Border sheet = OpenSettings(window, model, ConfigWindowTab.Input);

			Assert.Equal(388, sheet.Bounds.Height, 0.5);
			Assert.True(sheet.FindNamed<Slider>("sldControlsRumble").IsOnScreen());
			Assert.True(sheet.FindNamed<Slider>("sldControlsDeadzone").IsOnScreen());
			ToggleButton tick = sheet.FindNamed<ToggleButton>("chkControlsMenuTick");
			Assert.IsType<ToggleSwitch>(tick);
			Assert.True(tick.IsOnScreen());
			Assert.True(tick.IsEnabled);
			Assert.False(tick.IsChecked, "Menu tick is off until the player turns it on");
			Assert.False(sheet.FindNamed<TextBlock>("txtControlsMenuTickReason").IsOnScreen());
			Assert.True(sheet.FindNamed<Button>("btnPlayerSettingsMoreInOptions").IsOnScreen());
			Assert.True(sheet.FindNamed<Button>("btnPlayerSettingsDone").IsOnScreen());
			Assert.DoesNotContain(sheet.FindAll<ScrollBar>(), s => s.IsOnScreen());
			AssertRingOnShownTab(window);

			Bitmap frame = PlayerRender.Capture(window);
			PlayerRender.Save(frame, "W-P8e");
			AssertWireframeRegions(frame, "W-P8e");
		});
	}

	//With Rumble at 0 the row stays but is disabled, and says why.
	[AvaloniaFact]
	public void Settings_controls_disables_menu_tick_with_its_reason_when_rumble_is_0()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		WithAimablePad(0, () => {
			(MainWindow window, MainWindowViewModel model) = ShowPlay();
			Border sheet = OpenSettings(window, model, ConfigWindowTab.Input);

			ToggleButton tick = sheet.FindNamed<ToggleButton>("chkControlsMenuTick");
			Assert.True(tick.IsOnScreen());
			Assert.False(tick.IsEnabled);
			TextBlock reason = sheet.FindNamed<TextBlock>("txtControlsMenuTickReason");
			Assert.True(reason.IsOnScreen());
			Assert.Equal("Rumble is off", reason.Text);
		});
	}

	//W-P9: while a pack installs over the running game, the pill names it and
	//its bar moves; nothing else (no button, no dialog).
	[AvaloniaFact]
	public void Install_pill_renders_as_W_P9()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		StartGame(window, model, "Pixel Quest");

		model.OnPackInstallStarted("Contra 80s");
		Dispatcher.UIThread.RunJobs();
		try {
			Popup popup = window.FindNamed<Popup>("PackInstallPillPopup");
			Assert.True(popup.IsOpen, "no install pill while the pack installs (W-P9)");
			Control pill = Assert.IsAssignableFrom<Control>(popup.Child);
			Assert.Equal("Installing Contra 80s…", pill.FindNamed<TextBlock>("PackInstallPillText").Text);
			ProgressBar bar = pill.FindNamed<ProgressBar>("PackInstallPillBar");
			Assert.True(bar.IsOnScreen() && bar.IsIndeterminate, "the install pill's bar does not move");
			Assert.DoesNotContain(pill.FindAll<Button>(), b => b.IsOnScreen());
			Assert.False(window.FindNamed<Views.WorkspaceShellBar>("ShellBar").IsOnScreen(), "the bar shows while the game runs");

			TopLevel pillTop = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(pill));
			PlayerRender.Save(PlayerRender.Capture(pillTop), "W-P9");
		} finally {
			model.OnPackInstallFinished(installed: true, silent: true);
			Dispatcher.UIThread.RunJobs();
		}
	}

	//W-P12 was retired by ADR-0256 Decision 8: the first run asks nothing
	//before the main window, and its two choices - where saves live, which
	//keys play - are answered in Settings › System. That tab is what renders
	//against the drawing, which stays the design record of the two choices.
	[AvaloniaFact]
	public void First_run_choices_render_as_W_P12_in_settings_system()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		Border sheet = OpenSettings(window, model, ConfigWindowTab.System);

		Assert.Equal(480, sheet.Bounds.Height, 0.5);
		RadioButton userFolder = sheet.FindNamed<RadioButton>("SystemStorageUserFolder");
		RadioButton portable = sheet.FindNamed<RadioButton>("SystemStoragePortable");
		Assert.True(userFolder.IsOnScreen() && portable.IsOnScreen(), "the storage choice is not on the System tab");
		Assert.NotEqual(userFolder.IsChecked, portable.IsChecked);
		Assert.False(string.IsNullOrEmpty(sheet.FindNamed<TextBlock>("txtSystemUserFolder").Text));
		RadioButton arrows = sheet.FindNamed<RadioButton>("SystemKeyboardArrows");
		RadioButton wasd = sheet.FindNamed<RadioButton>("SystemKeyboardWasd");
		Assert.True(arrows.IsOnScreen() && wasd.IsOnScreen(), "the keyboard choice is not on the System tab");

		PlayerRender.Save(PlayerRender.Capture(window), "W-P12");
	}
}
