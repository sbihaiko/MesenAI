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

//#967, ADR-0254: the window half of the focus pause. The rule is host-free in
//UI.Tests/Play/FocusPauseTests and the default in FocusPauseDefaultTests; this
//drives MainWindow.UpdateAutoPause's own poll through its IAppFocus seam
//against the real core. In Play a focus loss opens W-P4 and the regain leaves
//it there ("Fica pausado na tela de Esc") until the player's Esc; Classic
//keeps the silent pause and its automatic resume; a Play door with no game
//has nothing for W-P4 to describe. Cmd-Tab on a real desktop stays in #926.
[Collection(NativeCoreCollection.Name)]
public class FocusPauseWiringTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly uint _speed = ConfigManager.Config.Emulation.EmulationSpeed;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-967-" + Guid.NewGuid().ToString("N"));

	//The poll's polls: three of them at its 100 ms interval.
	private static readonly TimeSpan SeveralPolls = TimeSpan.FromMilliseconds(400);

	private sealed class FakeFocus : IAppFocus
	{
		public Window? Active { get; set; }
		public Window? GetActiveWindow() => Active;
	}

	public FocusPauseWiringTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		//CI has no native core (ADR-0131): the test skips, and Stop would turn
		//the skip into a DllNotFoundException failure.
		if(NativeCore.IsAvailable) {
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
	private static (MainWindow Window, MainWindowViewModel Model, FakeFocus Focus) Show(UiMode mode, Workspace workspace)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = mode;
		prefs.Workspace = workspace;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
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

	[AvaloniaFact]
	public void In_Play_focus_loss_opens_W_P4_regain_keeps_it_and_Esc_resumes()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, FakeFocus focus) = Show(UiMode.Player, Workspace.Play);
		RunGame(model);
		Border card = window.FindNamed<Border>("PlayerOverlay");
		Assert.False(card.IsOnScreen());

		focus.Active = null;
		WaitFor(() => EmuApi.IsPaused() && model.IsPlayerOverlayVisible, "a focus loss in Play did not pause the game behind W-P4");
		Assert.True(card.IsOnScreen(), "W-P4 is open in the model but not on screen");
		Assert.True(model.MainMenu.AutoPaused);

		//"Fica pausado na tela de Esc": coming back is not a resume.
		focus.Active = window;
		PumpFor(SeveralPolls, () => {
			Assert.True(EmuApi.IsPaused(), "regaining focus resumed the game behind W-P4");
			Assert.True(card.IsOnScreen(), "regaining focus took W-P4 away");
		});

		//The way back is W-P4's own Esc (ShortcutHandler's ToggleOverlay).
		model.TogglePlayerOverlay();
		WaitFor(() => !EmuApi.IsPaused(), "Esc on W-P4 did not resume the game");
		Assert.False(model.IsPlayerOverlayVisible);
		Assert.False(card.IsOnScreen());
		//And the poll, with focus back, leaves the resumed game running.
		PumpFor(SeveralPolls, () => Assert.False(EmuApi.IsPaused(), "the poll paused the game again after Esc"));
		Assert.False(model.MainMenu.AutoPaused);
	}

	[AvaloniaFact]
	public void In_Classic_focus_loss_pauses_with_no_overlay_and_regain_resumes()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, FakeFocus focus) = Show(UiMode.Advanced, Workspace.Classic);
		RunGame(model);

		focus.Active = null;
		WaitFor(() => EmuApi.IsPaused(), "a focus loss in Classic did not pause the game");
		PumpFor(SeveralPolls, () => Assert.False(model.IsPlayerOverlayVisible, "Classic has no W-P4, but the focus pause opened it"));
		Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
		Assert.True(model.MainMenu.AutoPaused);

		focus.Active = window;
		WaitFor(() => !EmuApi.IsPaused(), "regaining focus in Classic did not resume the game");
		Assert.False(model.MainMenu.AutoPaused);
		Assert.False(model.IsPlayerOverlayVisible);
	}

	[AvaloniaFact]
	public void In_Play_with_no_game_focus_loss_opens_no_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, FakeFocus focus) = Show(UiMode.Player, Workspace.Play);
		Assert.Equal(RomFormat.Unknown, model.RomInfo.Format);

		focus.Active = null;
		PumpFor(SeveralPolls, () => Assert.False(model.IsPlayerOverlayVisible, "a focus loss with no game opened W-P4"));
		Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());

		focus.Active = window;
		PumpFor(SeveralPolls, () => Assert.False(model.IsPlayerOverlayVisible, "regaining focus with no game opened W-P4"));
	}

	//ADR-0254 Consequences: the frozen frame comes from the core's last frame,
	//so a pause during the load card must not be treated as a picture.
	[AvaloniaFact]
	public void A_focus_loss_during_the_load_card_takes_no_frozen_frame_as_a_picture()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, FakeFocus focus) = Show(UiMode.Player, Workspace.Play);
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Dispatcher.UIThread.RunJobs();
		Border loadCard = window.FindNamed<Border>("PlayLoadWaitCard");
		Image frozen = window.FindNamed<Image>("PausedGameFrame");

		//1 % speed: the first picture is seconds away, so the poll lands while
		//the card is still up.
		ConfigManager.Config.Emulation.EmulationSpeed = 1;
		ConfigManager.Config.Emulation.ApplyConfig();
		LoadRomHelper.LoadFile(WriteRom());
		Assert.True(loadCard.IsOnScreen(), "no load card on screen while the game opens (#734)");
		WaitFor(() => model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		Assert.True(loadCard.IsOnScreen(), "the load card left before the focus loss; the test needs it up");
		Assert.True(FrameCaptureApi.HeadlessGetFrameCount() < PlayLoadWait.FramesUntilShown, "the first picture was already out at the focus loss");

		//While the card is up nothing stands in for the game's picture: no
		//frozen frame is held or shown, and the native picture stays hidden.
		focus.Active = null;
		WaitFor(() => EmuApi.IsPaused() && model.IsPlayerOverlayVisible, "a focus loss during the load card did not pause the game behind W-P4", () => {
			if(loadCard.IsOnScreen()) {
				Assert.Null(model.PausedGameFrame);
				Assert.False(frozen.IsOnScreen(), "a frozen frame showed under the load card");
				Assert.False(model.IsNativeRendererVisible, "the native picture showed over the load card");
			}
		});
		Assert.True(model.MainMenu.AutoPaused);

		//The pause ends the wait (MainWindow.LoadWait, GamePaused) and W-P4 takes
		//over: the card is gone, and the frame behind the scrim is the core's
		//own capture taken after it - never an empty image in the picture's place.
		WaitFor(() => !loadCard.IsOnScreen() && !model.RecentGames.Visible, "the load card stayed up under W-P4");
		Assert.Equal(PlayLoadWaitPhase.Idle, model.LoadWait.Phase);
		Assert.Equal(frozen.IsOnScreen(), model.PausedGameFrame != null);

		//Same way back as any focus pause in Play.
		focus.Active = window;
		PumpFor(SeveralPolls, () => Assert.True(EmuApi.IsPaused() && model.IsPlayerOverlayVisible, "regaining focus resumed the game the load card was opening"));
		model.TogglePlayerOverlay();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsPlayerOverlayVisible, "Esc on W-P4 did not resume the game");
	}
}
