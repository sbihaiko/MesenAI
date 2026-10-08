using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1034 (ADR-0264 Decisions 3 and 5): the console filter of the flat library
//sheet, on screen.
//
//The filter's own rules are host-free and tested in
//UI.Tests/Play/LibraryConsoleFilterTests (the option set, the ring, the
//resolve, the composition). What is proved HERE is the wiring, which is the
//half no host-free case can reach: that the sheet draws the segmented row
//Decision 3 names, that LB and RB on a real pad cycle it, that the row and the
//grid always agree, and that the console filter and the search narrow the SAME
//grid at once - Decision 5's own worked example, `mario` under NES finding
//Super Mario Bros. 3 and not the Game Boy one.
//
//The library is a real folder on disk with real ROM headers under it, the way
//PlayerLibraryTests builds one: the consoles the row offers come from what the
//scan actually classified, so a stand-in for the scan would stand in for
//exactly the thing under test.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryConsoleFilterTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1034-" + Guid.NewGuid().ToString("N"));

	//The stand-in pad backend, built the way PlayerLibraryTests builds it: a
	//headless window gives InitializeEmu no platform handle, so no key manager
	//exists to name a pad, and the bridge would resolve no mapping at all. The
	//names are the ones KeyPresets writes for an Xbox-preset port, shoulders
	//included - LB/RB is what Decision 3 binds the cycle to, so the table has to
	//carry them or the case would prove nothing about the pad.
	private const ushort PadBase = 0x1000;
	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };
	private static readonly Dictionary<ushort, string> Backend = BuildBackend();
	private static readonly Dictionary<string, ushort> BackendCodes = Backend.ToDictionary(pair => pair.Value, pair => pair.Key);

	//The three games the library holds: two NES and one Game Boy, so the row has
	//two consoles to offer and Decision 5's example has both of its subjects.
	private const string NesContra = "Contra";
	private const string NesMario = "Super Mario Bros 3";
	private const string GameBoyMario = "Super Mario Land";

	private static Dictionary<ushort, string> BuildBackend()
	{
		Dictionary<ushort, string> names = new();
		for(int pad = 1; pad <= 20; pad++) {
			for(int button = 0; button < ButtonNames.Length; button++) {
				names[(ushort)(PadBase + (pad - 1) * 0x100 + button)] = "Pad" + pad + " " + ButtonNames[button];
			}
		}
		return names;
	}

	private static string BackendName(ushort keyCode) => Backend.TryGetValue(keyCode, out string? name) ? name : "";

	private static ushort BackendCode(string name) => BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	private PadNavMapping? _mapping;
	private PadNavMapping Mapping => _mapping ??= PadNavControls.Resolve(PadFamily.Xbox, 0, BackendCode)
		?? throw new InvalidOperationException("the stand-in table does not answer the Xbox preset's names");

	public PlayerLibraryConsoleFilterTests()
	{
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitUntilStopped();
		}
		Directory.CreateDirectory(_folder);
	}

	private static void WaitUntilStopped()
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
			Thread.Sleep(10);
		}
		Assert.False(EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
	}

	public void Dispose()
	{
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.GameFolder = _gameFolder ?? "";
		prefs.OverrideGameFolder = _overrideGameFolder;
		ConfigManager.Config.Save();

		try {
			Directory.Delete(_folder, true);
		} catch {
			//A case that failed before it built its tree leaves nothing to remove.
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

	private void Feed(MainWindow window, PadNavAction action, int milliseconds = 50)
	{
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(Mapping, action) }, TimeSpan.FromMilliseconds(milliseconds), BackendName, BackendCode);
	}

	private static void Release(MainWindow window, int milliseconds = 50)
	{
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(milliseconds), BackendName, BackendCode);
	}

	private void Press(MainWindow window, PadNavAction action)
	{
		Feed(window, action);
		Release(window);
		Pump();
	}

	//A shoulder press, driven the way the pad drives one: the code the backend
	//names LB/RB goes down, then comes back up. No PadNavAction covers a
	//shoulder - ADR-0264 Decision 3 binds the cycle to it outside the six the
	//nav mapping resolves - so the code is named the way the backend names it.
	private void PressShoulder(MainWindow window, string name)
	{
		ushort code = BackendCode(name);
		Assert.NotEqual((ushort)0, code);
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { code }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Release(window);
		Pump();
	}

	//The games folder, pointed at so the configured folder IS the library folder
	//on any machine: two NES games and one Game Boy game, which is what the row
	//has to offer as consoles.
	private void LibraryRoot()
	{
		string root = Path.Combine(_folder, "games");
		string nes = Path.Combine(root, "NES");
		string gb = Path.Combine(root, "Handheld", "GB");
		Directory.CreateDirectory(nes);
		Directory.CreateDirectory(gb);
		File.WriteAllBytes(Path.Combine(nes, NesContra + " (U) [!].nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(nes, NesMario + " (USA).nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(gb, GameBoyMario + " (World).gb"), SyntheticGbRom.Build());
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowFirstRunHome()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;

		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");

		model.RomPicker.RunLibraryScanInline = true;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		Assert.True(model.RecentGames.ShowFirstRunHome, "the home is not the first-run one, so this case would prove nothing");
		return (window, model);
	}

	//The sheet the Play home's own action opens, with its grid already filled.
	private (MainWindow Window, MainWindowViewModel Model) OpenLibrary()
	{
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.IsVisible && model.RomPicker.Tiles.Count == 3,
			"the library sheet did not open on its three games");
		return (window, model);
	}

	//The segments the row draws, in the order it draws them: the text of each
	//realized segment. Read off the visual tree rather than off the view-model
	//on purpose - a row that never reached the screen would answer the same as
	//one that did.
	private static string[] Segments(MainWindow window)
	{
		return window.FindNamed<ListBox>("RomPickerConsoleFilter").FindAll<TextBlock>().Select(t => t.Text ?? "").ToArray();
	}

	private static string[] TileTitles(MainWindowViewModel model) => model.RomPicker.Tiles.Select(t => t.Title).ToArray();

	//The titles the grid actually DRAWS, in the order a player reads them: the
	//visible `library-title` text of the tile buttons the grid realized. Read off
	//the visual tree rather than off the view-model on purpose - the acceptance
	//criterion of #1034 is about what the narrow grid shows, and a `Tiles` list
	//that narrowed while `RomPickerGrid` kept drawing the old tiles would answer
	//a view-model read identically. The row is read the same way, for the same
	//reason (see Segments above).
	private static string[] DrawnTileTitles(MainWindow window)
	{
		return window.FindNamed<ItemsControl>("RomPickerGrid")
			.FindAll<Button>()
			.Where(b => b.IsOnScreen())
			.SelectMany(b => b.FindAll<TextBlock>())
			.Where(t => t.Classes.Contains("library-title") && t.IsOnScreen() && !string.IsNullOrEmpty(t.Text))
			.Select(t => t.Text!)
			.ToArray();
	}

	//The grid narrows a frame after the press that asked for it, so the wait is
	//on the realized titles themselves and the assertion then re-reads them: a
	//half-drawn grid is a failure here, not a timing accident.
	private static void AssertDrawnTiles(MainWindow window, string[] expected, string failure)
	{
		WaitFor(() => DrawnTileTitles(window).SequenceEqual(expected), failure);
		Assert.Equal(expected, DrawnTileTitles(window));
	}

	//#1034 (ADR-0264 Decision 5): the row lists All first and then only the
	//consoles the library actually holds, in the product's order - two here, so
	//the player reads All | NES | Game Boy and cannot cycle onto a console the
	//scan never found.
	[AvaloniaFact]
	public void The_row_lists_All_and_the_consoles_the_library_holds()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = OpenLibrary();

		ListBox row = window.FindNamed<ListBox>("RomPickerConsoleFilter");
		Assert.True(row.IsOnScreen(), "the console filter row is not on screen");
		WaitFor(() => Segments(window).Length == 3, "the console filter row never realized its segments");
		Assert.Equal(new[] { "All", "NES", "Game Boy" }, Segments(window));
		//Nothing is narrowed while All is up: the sheet opens on the whole
		//library (Decision 1), and the row's first segment is what says so - it
		//is SELECTED, not absent, or the row would open with no segment standing
		//for the state it is showing.
		Assert.Null(model.RomPicker.SelectedConsole);
		Assert.Equal("All", (row.SelectedItem as PlayerConsoleFilterOption)?.Label);
	}

	//#1034 (ADR-0264 Decision 3): LB and RB cycle the filter from the pad, and
	//the ring wraps - the cycle is a ring, so a player who keeps pressing RB
	//ends up where they started instead of on a dead end.
	[AvaloniaFact]
	public void LB_and_RB_cycle_the_filter_and_wrap_around_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = OpenLibrary();
		ListBox row = window.FindNamed<ListBox>("RomPickerConsoleFilter");
		Assert.Null(model.RomPicker.SelectedConsole);

		//RB forward: All -> NES -> Game Boy -> All.
		PressShoulder(window, "Pad1 R1");
		Assert.Equal(RomConsole.Nes, model.RomPicker.SelectedConsole);
		Assert.Equal("NES", (row.SelectedItem as PlayerConsoleFilterOption)?.Label);

		PressShoulder(window, "Pad1 R1");
		Assert.Equal(RomConsole.GameBoy, model.RomPicker.SelectedConsole);
		Assert.Equal("Game Boy", (row.SelectedItem as PlayerConsoleFilterOption)?.Label);

		PressShoulder(window, "Pad1 R1");
		Assert.Null(model.RomPicker.SelectedConsole);
		Assert.Equal("All", (row.SelectedItem as PlayerConsoleFilterOption)?.Label);

		//LB is the same ring backwards: from All it lands on the LAST console
		//present, not on the first.
		PressShoulder(window, "Pad1 L1");
		Assert.Equal(RomConsole.GameBoy, model.RomPicker.SelectedConsole);
		Assert.Equal("Game Boy", (row.SelectedItem as PlayerConsoleFilterOption)?.Label);

		PressShoulder(window, "Pad1 L1");
		Assert.Equal(RomConsole.Nes, model.RomPicker.SelectedConsole);
	}

	//#1034 (ADR-0264 Decision 5): the row and the grid are the same decision -
	//what the segment says is what the grid draws. A segment that moved on its
	//own would be the defect this case exists to catch.
	[AvaloniaFact]
	public void The_row_and_the_grid_agree_on_which_console_is_up()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = OpenLibrary();
		AssertDrawnTiles(window, new[] { NesContra, NesMario, GameBoyMario }, "the grid never drew the whole library");
		Assert.Equal(new[] { NesContra, NesMario, GameBoyMario }, TileTitles(model));

		PressShoulder(window, "Pad1 R1");
		AssertDrawnTiles(window, new[] { NesContra, NesMario }, "the grid still draws the Game Boy tile under the NES segment");
		Assert.Equal(new[] { NesContra, NesMario }, TileTitles(model));

		PressShoulder(window, "Pad1 R1");
		AssertDrawnTiles(window, new[] { GameBoyMario }, "the grid still draws the NES tiles under the Game Boy segment");
		Assert.Equal(new[] { GameBoyMario }, TileTitles(model));

		PressShoulder(window, "Pad1 R1");
		AssertDrawnTiles(window, new[] { NesContra, NesMario, GameBoyMario }, "the grid did not return to the whole library with All");
		Assert.Equal(new[] { NesContra, NesMario, GameBoyMario }, TileTitles(model));
	}

	//#1034 (ADR-0264 Decision 5): search and the console filter COMPOSE, and
	//this is the ADR's own worked example - `mario` under the NES filter finds
	//Super Mario Bros. 3 and not the Game Boy's Super Mario Land. Either
	//narrowing alone finds both Marios or both NES games; only the two together
	//find one tile, which is what "at once" means.
	[AvaloniaFact]
	public void The_console_filter_and_the_search_narrow_the_same_grid_at_once()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = OpenLibrary();

		//The query goes through the real search box's own property, so a
		//regression in how the grid composes it with the filter shows here.
		model.RomPicker.SearchQuery = "mario";
		Pump();
		AssertDrawnTiles(window, new[] { NesMario, GameBoyMario }, "the grid did not draw the search's two Marios");
		Assert.Equal(new[] { NesMario, GameBoyMario }, TileTitles(model));

		//Decision 5's own example, on screen: `mario` under the NES segment draws
		//Super Mario Bros. 3 and NOT the Game Boy's Super Mario Land.
		PressShoulder(window, "Pad1 R1");
		AssertDrawnTiles(window, new[] { NesMario }, "the grid still draws the Game Boy Mario under the NES segment");
		Assert.Equal(new[] { NesMario }, TileTitles(model));

		PressShoulder(window, "Pad1 L1");
		AssertDrawnTiles(window, new[] { NesMario, GameBoyMario }, "the grid did not give the Game Boy Mario back with the search still up");
		Assert.Equal(new[] { NesMario, GameBoyMario }, TileTitles(model));
	}

	//A query that matches nothing leaves an empty grid under the filter rather
	//than the whole library - the narrowing the query asked for is never
	//silently dropped by the console filter.
	[AvaloniaFact]
	public void A_query_that_matches_nothing_leaves_no_tiles_under_the_filter()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = OpenLibrary();

		model.RomPicker.SearchQuery = "zelda";
		PressShoulder(window, "Pad1 R1");
		Pump();

		AssertDrawnTiles(window, Array.Empty<string>(), "the grid still draws tiles for a query that matches nothing");
		Assert.Empty(TileTitles(model));
	}

	private static string? FocusedName(MainWindow window) => (window.FocusManager?.GetFocusedElement() as Control)?.Name;

	//#1034 review finding 2: a console change rebuilds the grid, and the
	//arbiter answers a TilesRevision bump by reclaiming focus - which must keep
	//the ring on the search box when the box holds it.
	[AvaloniaFact]
	public void Cycling_the_filter_while_the_search_box_has_focus_keeps_the_focus_there()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = OpenLibrary();
		PressCode(window, PadNavControls.SheetCode(PadFamily.Xbox, 0, PadSheetControl.Search, BackendCode) ?? 0);
		Assert.Equal("RomPickerSearch", FocusedName(window));

		PressShoulder(window, "Pad1 R1");

		Assert.Equal(RomConsole.Nes, model.RomPicker.SelectedConsole);
		Assert.Equal("RomPickerSearch", FocusedName(window));
	}

	//#1034 review finding 1: the sheet opens with the first tile focused, and a
	//cycle rebuilds the grid under it. The ring must land on a tile of the
	//narrowed grid rather than on nothing.
	[AvaloniaFact]
	public void Cycling_the_filter_from_the_grid_leaves_the_focus_on_a_tile()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = OpenLibrary();
		Pump();
		Assert.IsType<Button>(window.FocusManager?.GetFocusedElement());

		PressShoulder(window, "Pad1 R1");
		Pump();

		Assert.Equal(RomConsole.Nes, model.RomPicker.SelectedConsole);
		Button? focused = window.FocusManager?.GetFocusedElement() as Button;
		Assert.NotNull(focused);
		PlayerLibraryTile tile = Assert.IsType<PlayerLibraryTile>(focused!.DataContext);
		Assert.Contains(tile, model.RomPicker.Tiles);
	}

	//A fresh visit is one rebuild of the grid, so one bump: ShowLibrary's own,
	//with the filter reset adding none.
	[AvaloniaFact]
	public void Opening_the_library_bumps_the_tiles_revision_once_before_the_scan_lands()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		ManualResetEventSlim gate = new(false);
		model.RomPicker.RunLibraryScanInline = false;
		model.RomPicker.LibraryScanStreamSource = (folders, lister, onEntries) => {
			gate.Wait(TimeSpan.FromSeconds(30));
			return GameLibrary.ScanStreaming(folders, lister, onEntries);
		};
		try {
			int before = model.RomPicker.TilesRevision;
			model.RomPicker.Open();
			Pump();
			Assert.True(model.RomPicker.SearchingText.Length > 0, "the scan is not in flight, so this case would prove nothing");
			Assert.Equal(before + 1, model.RomPicker.TilesRevision);
		} finally {
			gate.Set();
		}
		WaitFor(() => model.RomPicker.SearchingText.Length == 0 && model.RomPicker.Tiles.Count == 3, "the released scan never filled the grid");
	}

	//#1034 review finding 3: the row's two-way selection can be cleared by the
	//pointer (null), which would read as All while no segment is lit. The row
	//and the grid must keep agreeing, so the first segment comes back.
	[AvaloniaFact]
	public void Clearing_the_row_selection_falls_back_to_All_with_the_segment_lit()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = OpenLibrary();
		ListBox row = window.FindNamed<ListBox>("RomPickerConsoleFilter");
		PressShoulder(window, "Pad1 R1");
		Assert.Equal(new[] { NesContra, NesMario }, TileTitles(model));

		row.SelectedItem = null;
		Pump();

		Assert.Same(model.RomPicker.ConsoleOptions[0], model.RomPicker.SelectedConsoleOption);
		Assert.Equal("All", (row.SelectedItem as PlayerConsoleFilterOption)?.Label);
		Assert.Equal(new[] { NesContra, NesMario, GameBoyMario }, TileTitles(model));
	}

	private static void PressCode(MainWindow window, ushort code)
	{
		PlayPadNavigationWiring.TickForTest(window, new[] { code }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
	}
}
