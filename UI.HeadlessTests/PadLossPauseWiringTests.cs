using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

//#1109 (spec #1102): the window half of the pad-loss pause. The rule is
//host-free in UI.Tests/Play/PadLossPauseTests; this unplugs and replugs a pad
//through MainWindowViewModel.ConnectedGamepadCount, the seam the lamps and the
//entry toast already read, against the real core, and asserts what the player
//sees: the overlay on screen, the line written on it, the game paused.
[Collection(NativeCoreCollection.Name)]
public class PadLossPauseWiringTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly uint _speed = ConfigManager.Config.Emulation.EmulationSpeed;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1109-" + Guid.NewGuid().ToString("N"));
	private MainWindow? _window;

	//The poll's polls: three of them at its 100 ms interval.
	private static readonly TimeSpan SeveralPolls = TimeSpan.FromMilliseconds(400);

	private sealed class FakeFocus : IAppFocus
	{
		public Window? Active { get; set; }
		public Window? GetActiveWindow() => Active;
	}

	public PadLossPauseWiringTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		//CI has no native core (ADR-0131): the test skips, and Stop would turn
		//the skip into a DllNotFoundException failure.
		if(NativeCore.IsAvailable) {
			//The window's focus poll is a DispatcherTimer: an open window would
			//keep polling into the next test. Closing runs the exit path, which
			//would release the process-global core the next test still needs.
			if(_window != null) {
				_window.ReleaseCore = () => { };
				_window.SkipCloseConfirmation = true;
				_window.Close();
			}
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Emulation.EmulationSpeed = _speed;
		if(NativeCore.IsAvailable) {
			ConfigManager.Config.Emulation.ApplyConfig();
		}
		ConfigManager.Config.Save();
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	//The focus seam goes in before the preference is on: in the headless
	//lifetime the real one reports no active window, i.e. always in background.
	private (MainWindow Window, MainWindowViewModel Model, FakeFocus Focus) Show(UiMode mode, Workspace workspace)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = mode;
		prefs.Workspace = workspace;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
		_window = window;
		FakeFocus focus = new() { Active = window };
		window.AppFocus = focus;
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus (MainMenuViewModel.Initialize).");
		if(EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitFor(() => !EmuApi.IsRunning(), "the previous test's game never stopped");
		}
		prefs.PauseWhenInBackground = true;
		return (window, model, focus);
	}

	private string WriteRom()
	{
		string rom = Path.Combine(_folder, "Castlevania.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		return rom;
	}

	private void RunGame(MainWindowViewModel model)
	{
		string rom = WriteRom();
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");
		WaitFor(() => FrameCaptureApi.HeadlessGetFrameCount() > 5, "the game never rendered a frame");
	}

	private static void WaitFor(Func<bool> condition, string failure, Action? eachPoll = null)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Pump();
			eachPoll?.Invoke();
		}
		Dispatcher.UIThread.RunJobs();
	}

	//Lets the window's DispatcherTimers (the focus poll among them) fire.
	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
		Thread.Sleep(10);
	}

	private static void PumpFor(TimeSpan span, Action? eachPoll = null)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(clock.Elapsed < span) {
			Pump();
			eachPoll?.Invoke();
		}
	}

	private int _pads = 2;

	private (MainWindow Window, MainWindowViewModel Model, FakeFocus Focus) ShowWithPads(UiMode mode, Workspace workspace)
	{
		(MainWindow window, MainWindowViewModel model, FakeFocus focus) = Show(mode, workspace);
		_pads = 2;
		model.ConnectedGamepadCount = () => (uint)_pads;
		PumpFor(SeveralPolls);
		return (window, model, focus);
	}

	[AvaloniaFact]
	public void In_Play_unplugging_a_pad_pauses_into_W_P4_with_the_reason_and_replugging_does_not_resume()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, _) = ShowWithPads(UiMode.Player, Workspace.Play);
		RunGame(model);
		Border card = window.FindNamed<Border>("PlayerOverlay");
		Assert.False(card.IsOnScreen());

		_pads = 1;
		WaitFor(() => EmuApi.IsPaused() && card.IsOnScreen(), "unplugging a pad did not pause the game behind W-P4");
		//The overlay's controls are realized when it first shows.
		TextBlock line = window.FindNamed<TextBlock>("OverlayPausedLine");
		Assert.Equal("Paused — controller disconnected", line.Text);

		//Reconnecting rewrites the line; it is not a resume.
		_pads = 2;
		WaitFor(() => line.Text == "Paused — controller reconnected", "replugging did not rewrite the reason line");
		PumpFor(SeveralPolls, () => {
			Assert.True(EmuApi.IsPaused(), "replugging resumed the game behind W-P4");
			Assert.True(card.IsOnScreen(), "replugging took W-P4 away");
		});

		//Still drivable from what is left: Esc resumes, and the line is plain again.
		model.TogglePlayerOverlay();
		WaitFor(() => !EmuApi.IsPaused() && !card.IsOnScreen(), "Esc on W-P4 did not resume the game");
		WaitFor(() => line.Text == "Paused", "the reason outlived the pause");
	}

	[AvaloniaFact]
	public void In_Play_losing_the_last_pad_leaves_the_overlay_open_for_the_keyboard()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, _) = ShowWithPads(UiMode.Player, Workspace.Play);
		RunGame(model);
		Border card = window.FindNamed<Border>("PlayerOverlay");

		_pads = 0;
		WaitFor(() => EmuApi.IsPaused() && card.IsOnScreen(), "losing the last pad did not pause the game behind W-P4");
		//No pad in hand: the footer names the keyboard (ADR-0256 Decision 6).
		TextBlock hint = window.FindNamed<TextBlock>("OverlayResumeHint");
		Assert.Equal("Esc to resume", hint.Text);
		model.TogglePlayerOverlay();
		WaitFor(() => !EmuApi.IsPaused(), "the keyboard's Esc did not resume the game");
	}

	[AvaloniaFact]
	public void A_game_the_player_already_paused_stays_as_it_is_when_a_pad_goes()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, _) = ShowWithPads(UiMode.Player, Workspace.Play);
		RunGame(model);
		EmuApi.Pause();
		WaitFor(() => EmuApi.IsPaused(), "the game never paused");

		_pads = 1;
		PumpFor(SeveralPolls, () => Assert.False(model.IsPlayerOverlayVisible, "a pad loss opened W-P4 over a game the player had paused"));
		Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	[AvaloniaFact]
	public void In_Classic_unplugging_a_pad_does_not_pause()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(_, MainWindowViewModel model, _) = ShowWithPads(UiMode.Advanced, Workspace.Classic);
		RunGame(model);

		_pads = 1;
		PumpFor(SeveralPolls, () => {
			Assert.False(EmuApi.IsPaused(), "a pad loss paused Classic");
			Assert.False(model.IsPlayerOverlayVisible);
		});
	}

	[AvaloniaFact]
	public void In_Play_with_no_game_unplugging_a_pad_opens_no_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, _) = ShowWithPads(UiMode.Player, Workspace.Play);
		Assert.Equal(RomFormat.Unknown, model.RomInfo.Format);

		_pads = 1;
		PumpFor(SeveralPolls, () => Assert.False(model.IsPlayerOverlayVisible, "a pad loss with no game opened W-P4"));
		Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}
}
