using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

//#734 follow-up: "every wait must have an animation so it does not look
//broken". PlayLoadWaitTests covers an open from the Play home; this covers
//the waits over a game already on screen - another open, a power cycle, an
//in-place pack change (W-P7, the picker, Remaster's Build & Show) - and the
//W-P9 install pill. On macOS the game picture is a native view drawn above
//every Avalonia control, so each indicator is checked to be on screen and
//never under that picture: either the picture is hidden while it shows, or
//the indicator is its own top level (a popup window), above the game.
[Collection(NativeCoreCollection.Name)]
public class PlayWaitsTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-waits-" + Guid.NewGuid().ToString("N"));

	//The games are synthetic, named to match no catalog row, and the
	//auto-install is off: a real install's pill must not race the one driven here.
	public PlayWaitsTests()
	{
		Directory.CreateDirectory(_folder);
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = false;
	}

	public void Dispose()
	{
		if(EmuApi.IsRunning()) {
			EmuApi.Stop();
		}
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private static void WaitFor(Func<bool> condition, string failure, Action? eachPoll = null, int timeoutMs = 30000)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > timeoutMs) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
			Dispatcher.UIThread.RunJobs();
			eachPoll?.Invoke();
			Thread.Sleep(5);
		}
		Dispatcher.UIThread.RunJobs();
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowPlayWithGame(string name = "Pixel Quest")
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
		if(EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitFor(() => !EmuApi.IsRunning() && model.RecentGames.Visible, "the previous test's game never stopped");
		}
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Dispatcher.UIThread.RunJobs();

		LoadRomHelper.LoadFile(WriteRom(name));
		Border card = window.FindNamed<Border>("PlayLoadWaitCard");
		WaitFor(() => EmuApi.IsRunning() && !card.IsOnScreen() && model.IsNativeRendererVisible, "the first game never showed its picture");
		return (window, model);
	}

	private string WriteRom(string name)
	{
		string rom = Path.Combine(_folder, name + ".nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		return rom;
	}

	//While the card is on screen the native picture must be hidden, every
	//poll; the card must show, and then leave with the picture back.
	private static void AssertCardAboveAHiddenPicture(MainWindow window, MainWindowViewModel model, string text)
	{
		Border card = window.FindNamed<Border>("PlayLoadWaitCard");
		Assert.True(card.IsOnScreen(), "no load card on screen during the wait");
		Assert.Equal(text, window.FindNamed<TextBlock>("PlayLoadWaitText").Text);
		Assert.True(window.FindNamed<ProgressBar>("PlayLoadWaitBar").IsIndeterminate, "the load card's bar does not move");
		Assert.False(model.IsNativeRendererVisible, "the native game picture is shown over the load card (invisible on macOS)");
		WaitFor(() => !card.IsOnScreen() && model.IsNativeRendererVisible, "the load card never gave the picture back", () => {
			Assert.False(card.IsOnScreen() && model.IsNativeRendererVisible, "the native game picture is shown over the load card (invisible on macOS)");
		});
	}

	//Tools › Open, a dropped file or a recent from the overlay, with a game
	//on screen: the card used to sit under the picture.
	[AvaloniaFact]
	public void Opening_a_game_over_a_running_one_shows_the_card_above_a_hidden_picture()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		LoadRomHelper.LoadFile(WriteRom("Pixel Quest II"));

		AssertCardAboveAHiddenPicture(window, model, "Opening Pixel Quest II…");
		Assert.True(EmuApi.IsRunning());
	}

	[AvaloniaFact]
	public void A_power_cycle_shows_the_reloading_card_until_the_picture()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		LoadRomHelper.PowerCycle();

		AssertCardAboveAHiddenPicture(window, model, "Reloading Pixel Quest…");
		Assert.True(EmuApi.IsRunning());
	}

	//ADR-0244's in-place swap - W-P7's Apply, a pick in W-P5, and Remaster's
	//Build & Show all take it.
	[AvaloniaFact]
	public void An_in_place_pack_change_shows_the_reloading_card_until_the_picture()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		Task swap = LoadRomHelper.ApplyPackChange(model.RomInfo.ConsoleType, () => { });

		AssertCardAboveAHiddenPicture(window, model, "Reloading Pixel Quest…");
		WaitFor(() => swap.IsCompleted, "the in-place swap never finished");
	}

	//A paused game draws nothing after a reload: the card must not spin until
	//its timeout over a game that is not coming.
	[AvaloniaFact]
	public void A_reload_of_a_paused_game_does_not_leave_the_card_spinning()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		EmuApi.Pause();
		WaitFor(() => EmuApi.IsPaused(), "the game never paused");

		Task swap = LoadRomHelper.ApplyPackChange(model.RomInfo.ConsoleType, () => { });

		WaitFor(() => swap.IsCompleted, "the in-place swap never finished");
		Border card = window.FindNamed<Border>("PlayLoadWaitCard");
		WaitFor(() => !card.IsOnScreen(), "the load card kept spinning over a paused game", timeoutMs: 5000);
		Assert.False(model.LoadWait.IsActive);
	}

	//W-P9: the pill over the running game. It is a popup - its own native
	//window on the desktop platforms, so the native game picture, which stays
	//shown, cannot cover it - with the render's moving bar.
	[AvaloniaFact]
	public void The_install_pill_floats_above_the_game_picture_with_a_moving_bar()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		window.Width = 1000;
		window.Height = 700;

		model.OnPackInstallStarted("Contra 80s");
		Dispatcher.UIThread.RunJobs();

		Popup popup = window.FindNamed<Popup>("PackInstallPillPopup");
		Assert.True(popup.IsOpen, "no install pill while the pack installs (W-P9)");
		Control pill = Assert.IsAssignableFrom<Control>(popup.Child);
		//Never the window's overlay layer (under the native picture): the
		//desktop platforms give a popup its own native window. The headless
		//platform has no popup windows and falls back to the overlay layer.
		Assert.False(popup.ShouldUseOverlayLayer, "the install pill would draw under the native game picture");
		//The game keeps the keyboard while the pill is up.
		Assert.False(popup.TakesFocusFromNativeControl, "the install pill takes the keyboard from the game");
		TopLevel pillTop = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(pill));
		if(!popup.IsUsingOverlayLayer) {
			Assert.NotSame(window, pillTop);
		}
		Assert.True(model.IsNativeRendererVisible, "the game is hidden while its pack installs");
		Assert.Equal("Installing Contra 80s…", pill.FindNamed<TextBlock>("PackInstallPillText").Text);
		ProgressBar bar = pill.FindNamed<ProgressBar>("PackInstallPillBar");
		Assert.True(bar.IsOnScreen(), "the install pill has no bar");
		Assert.True(bar.IsIndeterminate, "the install pill's bar does not move");
		Assert.Equal(Avalonia.Media.Color.Parse("#007AFF"), PlayerRender.SolidColor(bar.Foreground));
		Avalonia.Media.Imaging.Bitmap first = PlayerRender.Capture(pillTop);
		PlayerRender.Save(first, "W-P9-installing");
		Stopwatch elapsed = Stopwatch.StartNew();
		while(elapsed.ElapsedMilliseconds < 700) {
			Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(16);
		}
		Avalonia.Media.Imaging.Bitmap second = PlayerRender.Capture(pillTop);
		Assert.NotEqual(BarRow(first, bar, pillTop), BarRow(second, bar, pillTop));

		//A download that reports its size fills the bar.
		model.OnPackInstallProgress(50, 200);
		Dispatcher.UIThread.RunJobs();
		Assert.False(bar.IsIndeterminate);
		Assert.Equal(25, bar.Value, 0.5);

		//The failure sentence replaces it, without a bar.
		model.OnPackInstallFinished(installed: false, silent: false);
		Dispatcher.UIThread.RunJobs();
		Assert.True(popup.IsOpen);
		Assert.Equal("The pack could not be downloaded. Playing without it.", pill.FindNamed<TextBlock>("PackInstallPillText").Text);
		Assert.False(bar.IsOnScreen());
		PlayerRender.Save(PlayerRender.Capture(pillTop), "W-P9-failed");

		model.OnPackInstallStarted("Contra 80s");
		model.OnPackInstallFinished(installed: true, silent: false);
		Dispatcher.UIThread.RunJobs();
		Assert.False(popup.IsOpen, "the pill stayed after the pack installed");
	}

	//Outside Play (W-P9 is a Play render) the status line carries it.
	[AvaloniaFact]
	public void The_install_pill_leaves_with_the_Play_workspace()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		model.OnPackInstallStarted("Contra 80s");
		Dispatcher.UIThread.RunJobs();
		Popup popup = window.FindNamed<Popup>("PackInstallPillPopup");
		Assert.True(popup.IsOpen);

		model.SelectWorkspace(Workspace.Share);
		Dispatcher.UIThread.RunJobs();
		Assert.False(popup.IsOpen, "the Play pill floats over another workspace");

		model.SelectWorkspace(Workspace.Play);
		Dispatcher.UIThread.RunJobs();
		Assert.True(popup.IsOpen);
		model.OnPackInstallFinished(installed: true, silent: false);
		Dispatcher.UIThread.RunJobs();
	}

	private static string BarRow(Avalonia.Media.Imaging.Bitmap frame, ProgressBar bar, TopLevel top)
	{
		Point origin = bar.TranslatePoint(new Point(0, 0), top)!.Value;
		int y = (int)(origin.Y + bar.Bounds.Height / 2);
		System.Text.StringBuilder row = new();
		for(int x = (int)origin.X + 1; x < (int)(origin.X + bar.Bounds.Width) - 1; x += 4) {
			row.Append(PlayerRender.Pixel(frame, x, y).ToString());
		}
		return row.ToString();
	}
}
