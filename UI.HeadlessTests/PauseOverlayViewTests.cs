using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//G.2 (PRD Part B §8, §13.5.2 W-P4): the pause overlay's XAML wiring. The seven
//controls, where each former overlay action went and the Esc order are pinned
//host-free in UI.Tests/Play; this checks the realized overlay has exactly those
//seven controls in order, that the keyboard walks all of them, and - against
//the real core with a running game - that Esc goes game → W-P4 → resume, that
//every sheet opened from W-P4 closes back to it, and that Quit game lands on
//the Play home without closing the window.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class PauseOverlayViewTests : IDisposable
{
	private static readonly string[] OverlayButtons = {
		"OverlayResumeButton", "OverlaySaveStatesButton", "OverlayPackButton",
		"OverlayEnhancementsButton", "OverlayCheatsButton", "OverlaySettingsButton", "OverlayQuitGameButton"
	};

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Save();
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus (MainMenuViewModel.Initialize).");
		return (window, model);
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	private static void Click(MainWindow window, string button)
	{
		window.FindNamed<Button>(button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	//Rule 2: W-P4 shows seven controls, in the wireframe's order, and nothing
	//of P.4's Advanced GUI / app-closing Quit.
	[AvaloniaFact]
	public void Overlay_shows_exactly_the_seven_wireframe_controls()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();

		Border overlay = window.FindNamed<Border>("PlayerOverlay");
		Assert.True(overlay.IsOnScreen());
		string?[] names = overlay.FindAll<Button>().Where(b => b.IsOnScreen()).Select(b => b.Name).ToArray();
		Assert.Equal(OverlayButtons, names);
		Assert.Equal(PauseOverlay.MaxControls, names.Length);
		Assert.DoesNotContain(overlay.FindAll<TextBlock>(), t => t.Text == "Advanced GUI");
		//Row values are bound (no pack and no state yet on this home).
		Assert.Equal("none", window.FindNamed<TextBlock>("OverlayPackValue").Text);
		Assert.Equal("empty", window.FindNamed<TextBlock>("OverlaySaveStatesValue").Text);
		Assert.Matches("^(none|[1-6] on)$", window.FindNamed<TextBlock>("OverlayEnhancementsValue").Text ?? "");
	}

	//ADR-0256 Decision 6: the footer names the control in the player's hand, and
	//the pad navigation bridge is what answers which device that is. The rule is
	//pinned host-free in UI.Tests/Play/PlayResumeHintTests; this is the wiring -
	//the seam reaches the realized TextBlock.
	[AvaloniaFact]
	public void The_footer_names_the_control_in_the_players_hand()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		//(The seam is read when the overlay opens, so each state is opened - and
		//the footer only exists in the tree once it has been opened once.)
		model.InHandDevice = () => (PlayInputDevice.Keyboard, null);
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());

		TextBlock footer = window.FindNamed<TextBlock>("OverlayResumeHint");
		Assert.Equal("Esc to resume", footer.Text);

		model.InHandDevice = () => (PlayInputDevice.Controller, PadFamily.Xbox);
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("B to resume", footer.Text);

		model.InHandDevice = () => (PlayInputDevice.Controller, PadFamily.Ps4);
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("Circle to resume", footer.Text);

		model.InHandDevice = () => (PlayInputDevice.Controller, null);
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("Leave the menu to resume", footer.Text);
	}

	//Rule 9 (keyboard as the gamepad proxy, §6): Resume has focus on open and
	//the arrows walk all seven controls and back.
	[AvaloniaFact]
	public void Arrow_keys_walk_every_overlay_control()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<Button>(OverlayButtons[0]).IsFocused);

		for(int i = 1; i < OverlayButtons.Length; i++) {
			window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
			Dispatcher.UIThread.RunJobs();
			Assert.True(window.FindNamed<Button>(OverlayButtons[i]).IsFocused, $"ArrowDown did not reach {OverlayButtons[i]}");
		}
		for(int i = OverlayButtons.Length - 2; i >= 0; i--) {
			window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
			Dispatcher.UIThread.RunJobs();
			Assert.True(window.FindNamed<Button>(OverlayButtons[i]).IsFocused, $"ArrowUp did not reach {OverlayButtons[i]}");
		}
	}

	//W-P4's Save states row opens its sheet; Back returns to the overlay.
	[AvaloniaFact]
	public void Save_states_row_opens_its_sheet_and_back_returns()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();

		Click(window, "OverlaySaveStatesButton");
		Assert.True(window.FindNamed<Border>("PlayerSaveStatesSheet").IsOnScreen());
		Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
		//#909: the sheet is a grid of slots; with no game there is no row to land
		//on, so the sheet's own first control takes the focus (the same fallback
		//every other surface uses).
		Assert.True(window.FindNamed<Button>("SaveStatesReplaysButton").IsFocused);

		Click(window, "SaveStatesBackButton");
		Assert.False(window.FindNamed<Border>("PlayerSaveStatesSheet").IsOnScreen());
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	//#692's subject is gone with #909: the grid is no longer opened from W-P4
	//(the Save states sheet is its own grid), so a grid has no overlay to close
	//back to - the quick save/load shortcuts' own grids are the classic path.

	//The stop rule's Esc order against a running game: game → W-P4 → resume;
	//every sheet from W-P4 (Save states and its slot grid, Enhancements,
	//Cheats) closes back to W-P4; Quit game lands on the Play home.
	[AvaloniaFact]
	public void Esc_order_is_game_overlay_resume_and_quit_game_lands_home()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		string folder = Path.Combine(Path.GetTempPath(), "mesen-g2-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		try {
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
			EmuApi.Resume();
			WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");
			Border overlay = window.FindNamed<Border>("PlayerOverlay");
			Assert.False(overlay.IsOnScreen());

			//Esc 1: the overlay opens, the game pauses, the bar (Tools ⋯) is back.
			model.TogglePlayerOverlay();
			WaitFor(() => model.IsGamePaused, "the overlay did not pause the game");
			Assert.True(overlay.IsOnScreen());
			Assert.True(window.FindNamed<WorkspaceShellBar>("ShellBar").IsOnScreen());
			Assert.Equal("synthetic-nrom", window.FindNamed<TextBlock>("OverlayGameTitle").Text);

			//Esc 2: closed and resumed.
			model.TogglePlayerOverlay();
			WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused, "Esc on the overlay did not resume");
			Assert.False(overlay.IsOnScreen());

			//Each sheet from W-P4 closes back to it on Esc, still paused.
			foreach(string row in new[] { "OverlayEnhancementsButton", "OverlayCheatsButton", "OverlaySaveStatesButton" }) {
				model.TogglePlayerOverlay();
				WaitFor(() => model.IsGamePaused, "the overlay did not pause the game");
				Click(window, row);
				Assert.False(window.IsPauseCardActive(), $"{row}'s sheet left W-P4 on top");
				model.TogglePlayerOverlay();
				Dispatcher.UIThread.RunJobs();
				Assert.True(overlay.IsOnScreen(), $"Esc from {row}'s sheet did not return to the overlay");
				Assert.True(EmuApi.IsPaused());
				model.TogglePlayerOverlay();
				WaitFor(() => !EmuApi.IsPaused(), "Esc on the overlay did not resume");
			}

			//#909: the Save states sheet is one grid; its own *Save here* writes the
			//slot over the real core, and Esc returns to W-P4.
			model.TogglePlayerOverlay();
			WaitFor(() => model.IsGamePaused, "the overlay did not pause the game");
			Click(window, "OverlaySaveStatesButton");
			Assert.True(window.FindNamed<Border>("PlayerSaveStatesSheet").IsOnScreen());
			Click(window, "SlotSaveButton");
			WaitFor(() => model.SaveStateSlot(1)?.HasState == true, "Save here did not write slot 1");
			model.TogglePlayerOverlay();
			Dispatcher.UIThread.RunJobs();
			Assert.False(window.FindNamed<Border>("PlayerSaveStatesSheet").IsOnScreen());
			Assert.True(overlay.IsOnScreen());

			//Quit game: the game powers off, the window stays, the home shows.
			Click(window, "OverlayQuitGameButton");
			WaitFor(() => !EmuApi.IsRunning() && model.RomInfo.Format == RomFormat.Unknown, "Quit game did not power the game off");
			WaitFor(() => model.RecentGames.Visible, "the Play home did not come back");
			Assert.False(overlay.IsOnScreen());
			Assert.True(window.IsVisible);
			//...and Esc on the home does nothing.
			model.TogglePlayerOverlay();
			Dispatcher.UIThread.RunJobs();
			Assert.False(model.IsPlayerOverlayVisible);
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
			try {
				Directory.Delete(folder, true);
			} catch(IOException) {
			}
		}
	}
}
