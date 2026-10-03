using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Avalonia;
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

//#734: every wait the player can see has an animation. In Play the wait from
//an open to the game's first picture (the pack decode and a stalled audio
//device sit inside it) shows the load card over the home. The rule (phases,
//text, when the picture counts as shown) is pinned host-free in
//UI.Tests/Play/PlayLoadWaitTests; this checks the wiring: the card is on
//screen while the open runs, never under the native game picture, and gone
//once the picture shows or the open fails.
[Collection(NativeCoreCollection.Name)]
public class PlayLoadWaitTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-734-" + Guid.NewGuid().ToString("N"));

	public PlayLoadWaitTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private static void WaitFor(Func<bool> condition, string failure, Action? eachPoll = null)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
			Dispatcher.UIThread.RunJobs();
			eachPoll?.Invoke();
			Thread.Sleep(5);
		}
		Dispatcher.UIThread.RunJobs();
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
		if(EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitFor(() => !EmuApi.IsRunning() && model.RecentGames.Visible, "the previous test's game never stopped");
		}
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	//The open runs on a pool thread; the card is up before it starts, stays
	//above the home (the native picture is hidden under it, so it is never
	//covered on macOS), and leaves only when the game's picture is shown.
	[AvaloniaFact]
	public void The_load_card_shows_from_the_open_until_the_game_picture()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		string rom = Path.Combine(_folder, "Castlevania.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());

		LoadRomHelper.LoadFile(rom);

		Border card = window.FindNamed<Border>("PlayLoadWaitCard");
		Assert.True(card.IsOnScreen(), "no load card on screen while the game opens (#734)");
		Assert.Equal("Opening Castlevania…", window.FindNamed<TextBlock>("PlayLoadWaitText").Text);
		ProgressBar bar = window.FindNamed<ProgressBar>("PlayLoadWaitBar");
		Assert.True(bar.IsOnScreen() && bar.IsIndeterminate, "the load card's bar does not move");

		bool sawLoadedUnderCard = false;
		WaitFor(() => !card.IsOnScreen() && !model.RecentGames.Visible && EmuApi.IsRunning(), "the load card never left once the game ran", () => {
			Assert.False(card.IsOnScreen() && model.IsNativeRendererVisible, "the native game picture is shown over the load card (invisible on macOS)");
			sawLoadedUnderCard |= card.IsOnScreen() && model.RomInfo.Format != RomFormat.Unknown;
		});
		Assert.True(model.IsNativeRendererVisible, "the game picture never came back after the load card");
		Xunit.TestContext.Current.TestOutputHelper?.WriteLine("card seen after GameLoaded: " + sawLoadedUnderCard);
		EmuApi.Stop();
	}

	//ADR-0249 render gate: the card is the dark HUD pill (W-P9) with Play's
	//tint on the badge and the bar, and the bar moves (#734: an animation, not
	//a still glyph) - two frames apart in time differ along the bar.
	[AvaloniaFact]
	public void The_load_card_renders_as_a_Player_HUD_pill_and_its_bar_moves()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		window.Width = 1000;
		window.Height = 700;
		model.OnOpenStarted();
		model.BeginLoadWait("Castlevania", keepsHome: true);
		try {
			Border card = window.FindNamed<Border>("PlayLoadWaitCard");
			ProgressBar bar = window.FindNamed<ProgressBar>("PlayLoadWaitBar");
			Avalonia.Media.Imaging.Bitmap first = PlayerRender.Capture(window);
			PlayerRender.Save(first, "734-load-card");
			Assert.True(card.IsOnScreen());

			Assert.Equal(PlayerRender.SolidColor((Avalonia.Media.IBrush)window.FindResource("PlayerHudBrush")!), PlayerRender.SolidColor(card.Background));
			Assert.Equal(Avalonia.Media.Color.Parse("#007AFF"), PlayerRender.SolidColor(bar.Foreground));
			Assert.Equal(PlayerRender.SolidColor((Avalonia.Media.IBrush)window.FindResource("PlayerCardBrush")!), PlayerRender.SolidColor(window.FindNamed<TextBlock>("PlayLoadWaitText").Foreground));

			Avalonia.Point origin = bar.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value;
			int y = (int)(origin.Y + bar.Bounds.Height / 2);
			string Row(Avalonia.Media.Imaging.Bitmap frame)
			{
				System.Text.StringBuilder row = new();
				for(int x = (int)origin.X + 1; x < (int)(origin.X + bar.Bounds.Width) - 1; x += 4) {
					row.Append(PlayerRender.Pixel(frame, x, y).ToString());
				}
				return row.ToString();
			}
			string before = Row(first);
			//MediaContext pulses animations with its own stopwatch from a
			//DispatcherTimer and the render timer; the headless loop needs both
			//driven (the empty post promotes a due timer, as in PlayEdgeFlowsTests).
			Stopwatch elapsed = Stopwatch.StartNew();
			while(elapsed.ElapsedMilliseconds < 700) {
				Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
				Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
				Dispatcher.UIThread.RunJobs();
				Thread.Sleep(16);
			}
			Avalonia.Media.Imaging.Bitmap second = PlayerRender.Capture(window);
			PlayerRender.Save(second, "734-load-card-later");
			Assert.NotEqual(before, Row(second));
		} finally {
			model.LoadWait.OnLoadReturned(model.OpenGeneration);
			model.RefreshLoadWait();
		}
		Dispatcher.UIThread.RunJobs();
		Assert.False(window.FindNamed<Border>("PlayLoadWaitCard").IsOnScreen());
	}

	//A file that does not open ends the wait with W-P14's alert, not a card
	//that spins forever.
	[AvaloniaFact]
	public void A_failed_open_takes_the_load_card_away_and_shows_the_alert()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		string notAGame = Path.Combine(_folder, "Contra.nes");
		File.WriteAllText(notAGame, "not a game");

		LoadRomHelper.LoadFile(notAGame);

		Border card = window.FindNamed<Border>("PlayLoadWaitCard");
		Assert.True(card.IsOnScreen(), "no load card on screen while the game opens (#734)");
		WaitFor(() => model.RecentGames.IsLoadAlertVisible, "the failed open never reached the home's alert");
		WaitFor(() => !card.IsOnScreen(), "the load card stayed after the open failed");
		Assert.True(window.FindNamed<Border>("PlayHomeLoadAlert").IsOnScreen());
	}
}
