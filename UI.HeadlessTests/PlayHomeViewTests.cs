using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Controls;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//G.2 (PRD Part B §8, §13.5.2 W-P1/W-P2): the Play home's XAML wiring. The rules
//(which home, the Recent grid without the Continue game, "last played", the
//first-run sentence) are pinned host-free in UI.Tests/Play; this checks they
//reach the realized tree, that the primary action has keyboard focus, and that
//Advanced keeps its classic grid. Replaces P.7's PlayerHomeCardsTests: W-P1
//replaces the Welcome card and W-P2's Continue card replaces P.7's.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class PlayHomeViewTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _audio = ConfigManager.Config.EnhancementPacks.EnableAudio;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
	private readonly GameSelectionMode _selection = ConfigManager.Config.Preferences.GameSelectionScreenMode;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;

	public void Dispose()
	{
		//#619: the recent games go first. Dispose runs after SettleMainWindows,
		//so a restore below that rebuilds the home's grid must find no file to
		//start a preview load for - that load would post to Dispatcher.UIThread
		//from the thread pool after the test.
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		ConfigManager.Config.EnhancementPacks.EnableAudio = _audio;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		ConfigManager.Config.Preferences.GameSelectionScreenMode = _selection;
		ConfigManager.Config.Preferences.ConfirmExitResetPower = _confirm;
		ConfigManager.Config.Preferences.PauseWhenInBackground = _pauseInBackground;
		ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig = _pauseInMenus;
	}

	//recentGames are listed newest first.
	internal static (MainWindow Window, MainWindowViewModel Model) ShowHome(UiMode uiMode, params string[] recentGames)
	{
		ConfigManager.Config.Preferences.UiMode = uiMode;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		//#625: the once-per-upgrade classic-menu notice hides the home for ~3 s
		//(DisplayMessageHelper) when the window's post-init block shows it. Other
		//classes restore the flag they found, so settings.json can carry `false`
		//into this one; pin it, or the home is off screen whenever that block
		//runs inside a test. The notice path itself is pinned below.

		string folder = ConfigManager.RecentGamesFolder;
		foreach(string stale in Directory.GetFiles(folder, "*.rgd")) {
			File.Delete(stale);
		}
		DateTime written = DateTime.Now;
		foreach(string game in recentGames) {
			string file = Path.Combine(folder, game + ".rgd");
			File.WriteAllText(file, "");
			File.SetLastWriteTime(file, written);
			written = written.AddMinutes(-1);
		}

		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		//The same call MainWindow makes whenever it returns to the home screen.
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	private static IReadOnlyList<Button> ButtonsOnScreen(Control root) => root.FindAll<Button>().Where(b => b.IsOnScreen()).ToList();

	//W-P1: on a true first run the home has one control, Open a ROM…, and it
	//has focus - a first-run user reaches a playing game with it plus the file
	//dialog's pick (two actions), or with one drop.
	[AvaloniaFact]
	public void First_run_home_has_one_focused_primary_action()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.EnhancementPacks.EnableAudio = true;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = true;
		(MainWindow window, _) = ShowHome(UiMode.Player);

		StackPanel firstRun = window.FindNamed<StackPanel>("PlayHomeFirstRun");
		Assert.True(firstRun.IsOnScreen());
		Assert.False(window.FindNamed<DockPanel>("PlayHomeWithRecents").IsOnScreen());
		Assert.False(window.FindNamed<Panel>("PlayHomePlainGrid").IsOnScreen());

		Button open = Assert.Single(ButtonsOnScreen(firstRun));
		Assert.Same(window.FindNamed<Button>("PlayHomeOpenRomPrimary"), open);
		Assert.True(open.IsFocused);
		//Rule 10: the sentence says what happens next.
		Assert.Contains("community pack", window.FindNamed<TextBlock>("PlayHomeOrientation").Text);
	}

	[AvaloniaFact]
	public void First_run_sentence_is_hidden_when_neither_setting_makes_it_true()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.EnhancementPacks.EnableAudio = false;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = false;
		(MainWindow window, _) = ShowHome(UiMode.Player);

		Assert.False(window.FindNamed<TextBlock>("PlayHomeOrientation").IsOnScreen());
	}

	//W-P2: Continue playing names the newest game and has focus; the Recent
	//grid lists the others; two controls at rest besides the tiles.
	[AvaloniaFact]
	public void Home_with_recents_continues_the_newest_game_and_lists_the_others()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowHome(UiMode.Player, "Contra", "Zelda", "Metroid");

		Assert.False(window.FindNamed<StackPanel>("PlayHomeFirstRun").IsOnScreen());
		Assert.True(window.FindNamed<DockPanel>("PlayHomeWithRecents").IsOnScreen());
		Assert.Equal("Contra", window.FindNamed<TextBlock>("PlayHomeContinueTitle").Text);
		Assert.Equal("Last played today", window.FindNamed<TextBlock>("PlayHomeContinueSubtitle").Text);

		Panel gridHost = window.FindNamed<Panel>("PlayHomeRecentGrid");
		Assert.True(gridHost.IsOnScreen());
		StateGrid grid = gridHost.FindAll<StateGrid>().Single();
		Assert.Equal(new[] { "Zelda", "Metroid" }, grid.Entries.Select(e => e.Name).ToArray());
		Assert.Equal(3, model.RecentGames.GameEntries.Count);

		Assert.True(window.FindNamed<Button>("PlayHomeContinueButton").IsFocused);
		Assert.True(window.FindNamed<Button>("PlayHomeOpenRomSecondary").IsOnScreen());
	}

	//#625: the home also comes back without a home-kind change - after Quit
	//game with recents already listed (GameLoaded hid it and focused
	//RendererPanel; EmulationStopped shows it again). Continue has focus again,
	//so A/Enter acts with no pointer (rule 9). Run after other tests, it also
	//pins that the earlier tests' MainWindows - still open, and seeing the same
	//core notifications - do not take the app's one keyboard focus to their own
	//home (the real-app analogue: a debugger or tool window keeps its focus).
	[AvaloniaFact]
	public void Home_that_returns_after_a_game_focuses_continue_again()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Preferences.ConfirmExitResetPower = false;
		ConfigManager.Config.Preferences.PauseWhenInBackground = false;
		ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig = false;
		(MainWindow window, MainWindowViewModel model) = ShowHome(UiMode.Player, "Contra", "Zelda");
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished its background Init");
		Assert.True(window.FindNamed<Button>("PlayHomeContinueButton").IsFocused);

		string folder = Path.Combine(Path.GetTempPath(), "mesen-625-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		try {
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			WaitFor(() => EmuApi.IsRunning() && !model.RecentGames.Visible, "the game never replaced the home");
			Assert.IsNotType<Button>(window.FocusManager?.GetFocusedElement());

			LoadRomHelper.PowerOff();
			WaitFor(() => !EmuApi.IsRunning() && model.RecentGames.Visible, "the home did not come back after Quit game");
			Assert.True(window.FindNamed<DockPanel>("PlayHomeWithRecents").IsOnScreen());
			Assert.Same(window.FindNamed<Button>("PlayHomeContinueButton"), window.FocusManager?.GetFocusedElement());
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
			try {
				Directory.Delete(folder, true);
			} catch(IOException) {
			}
		}
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			Assert.True(clock.ElapsedMilliseconds < 30000, failure);
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	//Rule 9: from the keyboard alone, focus reaches both buttons and the grid.
	[AvaloniaFact]
	public void Keyboard_reaches_continue_open_and_the_recent_grid()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowHome(UiMode.Player, "Contra", "Zelda");

		StateGrid grid = window.FindNamed<Panel>("PlayHomeRecentGrid").FindAll<StateGrid>().Single();
		HashSet<string> reached = new();
		for(int i = 0; i < 12; i++) {
			object? focused = window.FocusManager?.GetFocusedElement();
			if(ReferenceEquals(focused, grid)) {
				reached.Add("grid");
			} else if(focused is Control { Name: { } name }) {
				reached.Add(name);
			}
			window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
			Dispatcher.UIThread.RunJobs();
		}
		Assert.Contains("PlayHomeContinueButton", reached);
		Assert.Contains("PlayHomeOpenRomSecondary", reached);
		Assert.Contains("grid", reached);
	}

	[AvaloniaFact]
	public void Only_one_recent_game_hides_the_recent_grid()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowHome(UiMode.Player, "Contra");

		Assert.True(window.FindNamed<Border>("PlayHomeContinueCard").IsOnScreen());
		Assert.False(window.FindNamed<Panel>("PlayHomeRecentGrid").IsOnScreen());
	}

	//Advanced keeps the classic game-selection grid of every entry.
	[AvaloniaFact]
	public void Advanced_keeps_the_classic_grid()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Preferences.GameSelectionScreenMode = GameSelectionMode.ResumeState;
		(MainWindow window, _) = ShowHome(UiMode.Advanced, "Contra", "Zelda");

		Panel plainHost = window.FindNamed<Panel>("PlayHomePlainGrid");
		Assert.True(plainHost.IsOnScreen());
		StateGrid plain = plainHost.FindAll<StateGrid>().Single();
		Assert.Equal(2, plain.Entries.Count);
		Assert.False(window.FindNamed<DockPanel>("PlayHomeWithRecents").IsOnScreen());
		Assert.False(window.FindNamed<StackPanel>("PlayHomeFirstRun").IsOnScreen());
	}
}
