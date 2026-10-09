using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
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
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1110 (spec #1102, ADR-0268, W-P20): the Favorites shelf on Home and X on a
//focused cover. The list's own rules (toggle, newest first, a missing path kept
//and not handed out) are pinned host-free in UI.Tests/Play/PlayFavoritesTests;
//this checks the realized window: X through the pad bridge toggles the list, the
//shelf appears and disappears, the bar's X entry follows the focus, and a favorite
//whose file vanished stays in the list without being drawn.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class PlayFavoritesHomeTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly List<string> _favorites = ConfigManager.Config.PlayerEnhancements.Favorites.Paths.ToList();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1110-" + Guid.NewGuid().ToString("N"));
	private readonly List<MainWindow> _windows = new();

	//The XInput-shaped backend names ("Pad{N} {Button}"), one pad: what the bridge
	//needs to know X from the buttons it navigates with.
	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };
	private static readonly Dictionary<ushort, string> Backend = ButtonNames.Select((name, i) => (Code: (ushort)(0x1000 + i), Name: "Pad1 " + name)).ToDictionary(p => p.Code, p => p.Name);
	private static readonly Dictionary<string, ushort> BackendCodes = Backend.ToDictionary(p => p.Value, p => p.Key);

	private static string BackendName(ushort code) => Backend.TryGetValue(code, out string? name) ? name : "";
	private static ushort BackendCode(string name) => BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	public PlayFavoritesHomeTests()
	{
		Directory.CreateDirectory(_folder);
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitFor(() => !EmuApi.IsRunning(), "the previous case's game never stopped");
		}
	}

	public void Dispose()
	{
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();
		ClearRecents();
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.PlayerEnhancements.Favorites.Paths = _favorites;
		ConfigManager.Config.Save();
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
			Pump();
			Thread.Sleep(20);
		}
		Pump();
	}

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	private static void ClearRecents()
	{
		string folder = ConfigManager.RecentGamesFolder;
		if(Directory.Exists(folder)) {
			foreach(string stale in Directory.GetFiles(folder, "*.rgd")) {
				File.Delete(stale);
			}
		}
	}

	//A ROM the library can name: only its existence matters to the shelf.
	private string Rom(string name)
	{
		string path = Path.Combine(_folder, name + ".nes");
		File.WriteAllText(path, "rom");
		return path;
	}

	//A recent-game file as the Core writes it, down to the RomInfo.txt that names
	//the ROM; newest first by the order given.
	private static void SeedRecents(params (string Name, string RomPath)[] games)
	{
		string folder = ConfigManager.RecentGamesFolder;
		Directory.CreateDirectory(folder);
		ClearRecents();
		DateTime written = DateTime.Now;
		foreach((string name, string romPath) in games) {
			string file = Path.Combine(folder, name + ".rgd");
			using(ZipArchive zip = ZipFile.Open(file, ZipArchiveMode.Create)) {
				using StreamWriter writer = new(zip.CreateEntry("RomInfo.txt").Open());
				writer.Write(name + "\n" + romPath + "\x1\n");
			}
			File.SetLastWriteTime(file, written);
			written = written.AddMinutes(-1);
		}
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowHome(params (string Name, string RomPath)[] recents)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		SeedRecents(recents);

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		model.ConnectedGamepadCount = () => 1;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		WaitFor(() => !StateGridEntry.ThumbnailsInFlight, "the tiles' previews never settled");
		return (window, model);
	}

	private static void Tick(MainWindow window, params string[] held)
	{
		PlayPadNavigationWiring.TickForTest(window, held.Select(BackendCode).ToArray(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
	}

	private static void PressX(MainWindow window)
	{
		Tick(window, "Pad1 X");
		Tick(window);
	}

	//What the player reads: the bar's line.
	private static string Bar(MainWindow window)
	{
		Tick(window);
		return window.FindNamed<TextBlock>("PlayActionBarText").Text ?? "";
	}

	private static void Focus(Control control)
	{
		Assert.True(control.Focus(NavigationMethod.Directional), "the control did not take the focus: " + control.Name);
		Pump();
	}

	private static Button TileButton(MainWindow window, string gridName, string title)
	{
		StateGridEntry tile = window.FindNamed<Panel>(gridName).FindAll<StateGridEntry>().Single(t => t.Title == title);
		return tile.FindAll<Button>().First();
	}

	private static IReadOnlyList<string> Saved => ConfigManager.Config.PlayerEnhancements.Favorites.Paths;

	//With no favorite Home is W-P2 as it is today: no shelf, and no X on the bar
	//over the Open a game… button, where no cover has the focus.
	[AvaloniaFact]
	public void Home_without_favorites_draws_no_shelf_and_no_X_where_no_cover_has_focus()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.PlayerEnhancements.Favorites.Paths = new List<string>();
		(MainWindow window, _) = ShowHome(("Contra", Rom("Contra")), ("Metroid", Rom("Metroid")));

		Assert.False(window.FindNamed<Panel>("PlayHomeFavoritesGrid").IsOnScreen());
		Assert.False(window.FindNamed<TextBlock>("PlayHomeFavoritesHeader").IsOnScreen());
		Focus(window.FindNamed<Button>("PlayHomeOpenRomSecondary"));
		Assert.DoesNotContain("X ", Bar(window));
		PressX(window);
		Assert.Empty(Saved);
	}

	//AC 1 and 4: X on the Continue tile toggles that game, matched by its library
	//path, and the bar's X entry reads Unfavorite while it is one.
	[AvaloniaFact]
	public void X_on_Continue_toggles_the_continue_game_and_the_shelf_follows()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.PlayerEnhancements.Favorites.Paths = new List<string>();
		string contra = Rom("Contra");
		(MainWindow window, _) = ShowHome(("Contra", contra), ("Metroid", Rom("Metroid")));

		Focus(window.FindNamed<Button>("PlayHomeContinueButton"));
		Tick(window, "Pad1 Right");
		Tick(window);
		Focus(window.FindNamed<Button>("PlayHomeContinueButton"));
		Assert.Contains("X Favorite", Bar(window));
		Assert.DoesNotContain("Unfavorite", Bar(window));

		PressX(window);
		Assert.Equal(new[] { contra }, Saved);
		Assert.True(window.FindNamed<Panel>("PlayHomeFavoritesGrid").IsOnScreen());
		Assert.Contains("X Unfavorite", Bar(window));

		PressX(window);
		Assert.Empty(Saved);
		Assert.False(window.FindNamed<Panel>("PlayHomeFavoritesGrid").IsOnScreen());
		Assert.Contains("X Favorite", Bar(window));
	}

	//AC 1: X on a Recent tile favorites that tile's ROM, and favorites are newest
	//first - the second favorite leads the shelf.
	[AvaloniaFact]
	public void X_on_a_Home_tile_favorites_its_ROM_and_the_newest_leads_the_shelf()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.PlayerEnhancements.Favorites.Paths = new List<string>();
		string contra = Rom("Contra");
		string metroid = Rom("Metroid");
		string kirby = Rom("Kirby");
		(MainWindow window, MainWindowViewModel model) = ShowHome(("Contra", contra), ("Metroid", metroid), ("Kirby", kirby));

		Focus(TileButton(window, "PlayHomeRecentGrid", "Metroid"));
		PressX(window);
		Focus(TileButton(window, "PlayHomeRecentGrid", "Kirby"));
		PressX(window);

		Assert.Equal(new[] { kirby, metroid }, Saved);
		Assert.Equal(new[] { "Kirby", "Metroid" }, model.RecentGames.FavoriteEntries.Select(e => e.Name));
	}

	//AC 3: a favorite whose ROM file vanished stays in the list, is not drawn, and
	//returns when the file does.
	[AvaloniaFact]
	public void A_favorite_whose_file_vanished_stays_in_the_list_and_returns_with_the_file()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string here = Rom("Here");
		string gone = Path.Combine(_folder, "Gone.nes");
		ConfigManager.Config.PlayerEnhancements.Favorites.Paths = new List<string> { gone, here };
		(_, MainWindowViewModel model) = ShowHome(("Contra", Rom("Contra")), ("Metroid", Rom("Metroid")));

		Assert.Equal(new[] { "Here" }, model.RecentGames.FavoriteEntries.Select(e => e.Name));
		Assert.Equal(new[] { gone, here }, Saved);

		File.WriteAllText(gone, "rom");
		model.RecentGames.RefreshFavorites();
		Assert.Equal(new[] { "Gone", "Here" }, model.RecentGames.FavoriteEntries.Select(e => e.Name));
	}
}
