using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1032 (ADR-0264): the Play *Open a game* sheet is the flat library, and this
//is the wiring half - the pad drives it. The rules (the scan, the titles, the
//order) are host-free in UI.Tests/Play/GameLibraryTests; what is proved here is
//that the sheet the Play home opens is that grid, that the d-pad moves the ring
//across it, that A plays the focused game and B leaves, that *Browse a file…*
//still reaches the folder browser ADR-0256 Decision 9 built, and that a player
//with no library folder is told what to do instead of shown a blank grid.
//
//The library scan is stubbed to a fake tree in every case: its default reads
//the real disk, so without this each case would depend on whatever ROMs the
//machine running the suite happens to keep - and on a scan landing mid-press.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1032-" + Guid.NewGuid().ToString("N"));

	//The stand-in pad backend, built the way PlayRomPickerTests builds it: a
	//headless window gives InitializeEmu no platform handle, so no key manager
	//exists to name a pad.
	private const ushort PadBase = 0x1000;
	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };
	private static readonly Dictionary<ushort, string> Backend = BuildBackend();
	private static readonly Dictionary<string, ushort> BackendCodes = Backend.ToDictionary(pair => pair.Value, pair => pair.Key);

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

	public PlayerLibraryTests()
	{
		//#790: the core is process-global and a case that ran a game leaves its
		//console loaded.
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

	//The path of the tile the ring is on, or null when the ring is somewhere
	//else. This is the whole observable the pad cases turn on: A plays whatever
	//this answers.
	private static string? FocusedTilePath(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.DataContext is PlayerLibraryTile tile ? tile.Path : null;
	}

	private static string Focused(MainWindow window)
	{
		Control? focused = window.FocusManager?.GetFocusedElement() as Control;
		return focused is null ? "focus=<none>" : $"focus={focused.GetType().Name}#{focused.Name} dc={focused.DataContext?.GetType().Name}";
	}

	//A games folder with three ROMs under it, one of them nested - the tree the
	//scan exists for - and the settings pointed at it, so the configured folder
	//IS the library folder on any machine.
	private string LibraryRoot()
	{
		string root = Path.Combine(_folder, "games");
		string nes = Path.Combine(root, "NES");
		string gb = Path.Combine(root, "Handheld", "GB");
		Directory.CreateDirectory(nes);
		Directory.CreateDirectory(gb);
		File.WriteAllBytes(Path.Combine(nes, "Contra (U) [!].nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(nes, "Metroid (USA).nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(gb, "Tetris (World) (Rev A).gb"), SyntheticNrom.Build());
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		return root;
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

		//The library scan runs in the Open() turn so the grid is complete before
		//the case asserts anything about it.
		model.RomPicker.RunLibraryScanInline = true;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		Assert.True(model.RecentGames.ShowFirstRunHome, "the home is not the first-run one, so this case would prove nothing");
		return (window, model);
	}

	//#1032: the sheet the home's own action opens is the LIBRARY - every
	//openable ROM under the library folders at once, nested folders included -
	//and not the folder browser it used to be.
	[AvaloniaFact]
	public void The_home_opens_the_flat_library_with_its_games_in_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");

		Press(window, PadNavAction.Confirm);
		Pump();

		Assert.True(model.RomPicker.IsVisible, "the pad's Confirm opened no sheet");
		Assert.Equal(RomPickerMode.Library, model.RomPicker.Mode);
		Assert.True(window.FindNamed<Border>("PlayerRomPickerSheet").IsOnScreen(), "the sheet is not on screen");
		//Three games, two of them one level down and one two levels down: the
		//folders shape the scan, they are never rows.
		Assert.Equal(new[] { "Contra", "Metroid", "Tetris" }, model.RomPicker.Tiles.Select(t => t.Title).ToArray());
		Assert.Equal("Your library", model.RomPicker.HeaderText);
		Assert.Contains("3 games", model.RomPicker.CountText);
		Assert.True(window.FindNamed<ItemsControl>("RomPickerGrid").IsOnScreen(), "the grid is not on screen");
	}

	//#1032 (ADR-0264 Decision 3): the d-pad moves the ring across the grid, one
	//tile at a time, and it starts on the first tile - A has to have something
	//to play the moment the sheet opens.
	[AvaloniaFact]
	public void The_d_pad_moves_the_focus_across_the_grid()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.Tiles.Count == 3, $"the grid never filled ({Focused(window)})");
		WaitFor(() => FocusedTilePath(window) == model.RomPicker.Tiles[0].Path,
			$"the sheet did not open on its first tile ({Focused(window)})");

		Press(window, PadNavAction.Right);
		Assert.Equal(model.RomPicker.Tiles[1].Path, FocusedTilePath(window));

		Press(window, PadNavAction.Right);
		Assert.Equal(model.RomPicker.Tiles[2].Path, FocusedTilePath(window));

		Press(window, PadNavAction.Left);
		Assert.Equal(model.RomPicker.Tiles[1].Path, FocusedTilePath(window));
	}

	//#1032 (ADR-0264 Decision 3): A plays the FOCUSED game - not the first one,
	//and not a folder. The path reaches the same LoadRomHelper call the native
	//dialog's result took.
	[AvaloniaFact]
	public void A_plays_the_focused_game()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => FocusedTilePath(window) == model.RomPicker.Tiles[0].Path, $"the sheet did not open on its first tile ({Focused(window)})");

		Press(window, PadNavAction.Right);
		Press(window, PadNavAction.Confirm);
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown,
			"confirming the focused tile did not load it");

		//The sheet is gone: the pick IS the open, not a step before one.
		Assert.False(model.RomPicker.IsVisible);
	}

	//#1032 (ADR-0264 Decision 3): B leaves the sheet, and nothing is loaded -
	//the sheet is reversible from the pad alone (ADR-0256's stop rule).
	[AvaloniaFact]
	public void B_closes_the_library_without_playing_anything()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.IsVisible, "the sheet did not open");

		Press(window, PadNavAction.Back);
		WaitFor(() => !model.RomPicker.IsVisible, "B did not close the library");

		Assert.False(EmuApi.IsRunning());
		//And the home has its ring back: the sheet left nothing focused behind.
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			$"the ring did not return to the home after B ({Focused(window)})");
	}

	//#1032 (ADR-0264 Decision 3, as AMENDED on #1040): the grid is not a trap.
	//Up from the top row steps into the header - the sheet's own controls, which
	//a pad-only player otherwise cannot reach, and ADR-0256's rule that the Play
	//GUI works from a controller alone is not one this ADR supersedes - and Down
	//comes back to the grid. B leaves the sheet from either side.
	[AvaloniaFact]
	public void Up_from_the_top_grid_row_reaches_the_header_and_down_comes_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => FocusedTilePath(window) == model.RomPicker.Tiles[0].Path,
			$"the sheet did not open on its first tile ({Focused(window)})");

		//The grid holds three tiles in one row, so this tile IS the top row.
		Press(window, PadNavAction.Up);
		WaitFor(() => HeaderFocused(window),
			$"Up from the top grid row did not reach the header ({Focused(window)})");

		Press(window, PadNavAction.Down);
		WaitFor(() => FocusedTilePath(window) is not null,
			$"Down out of the header did not come back to the grid ({Focused(window)})");

		//And B is the dismiss from the header too, not only from a tile.
		Press(window, PadNavAction.Up);
		WaitFor(() => HeaderFocused(window), $"the header ring did not come back ({Focused(window)})");
		Press(window, PadNavAction.Back);
		WaitFor(() => !model.RomPicker.IsVisible, "B from the header did not close the sheet");
	}

	//True when the ring is on one of the sheet's header controls. The search
	//field and *Library folders…* are later slices (#1034, #1035); what this
	//slice ships in the header is *Browse a file…* and Back, and the amendment
	//has to reach the ones that are there.
	private static bool HeaderFocused(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.Name is "RomPickerBrowseFile" or "RomPickerBack";
	}

	//#1032 (ADR-0264 Decision 11): *Browse a file…* opens the folder browser
	//ADR-0256 Decision 9 built, unchanged - its roots, its walk, and the
	//first-row focus guard that keeps a stray Confirm off *Make this my games
	//folder*. The browser is inside the sheet, so B walks back out of it to the
	//library rather than closing the sheet.
	[AvaloniaFact]
	public void Browse_a_file_opens_the_folder_browser_and_back_returns_to_the_library()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomPicker.SuggestionSource = _ => Array.Empty<RomPickerHit>();
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.Mode == RomPickerMode.Library, "the sheet did not open on the library");

		Button browse = window.FindNamed<Button>("RomPickerBrowseFile");
		Assert.True(browse.IsOnScreen(), "Browse a file… is not on the sheet");
		browse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Pump();

		Assert.Equal(RomPickerMode.BrowseFile, model.RomPicker.Mode);
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.DataContext is PlayerRomPickerRow,
			$"the browser opened with the ring off its list ({Focused(window)})");
		//The browser leads with the configured folder, and the ring is on it -
		//never on an Action row, which is what the first-row guard is for.
		Assert.True(window.FindNamed<ItemsControl>("RomPickerList").IsOnScreen(), "the browser's list is not on screen");
		Assert.True(model.RomPicker.Rows.Any(r => r.Path == root),
			$"the browser did not lead with the configured folder (root={root} path='{model.RomPicker.PathText}' rows=[{string.Join("; ", model.RomPicker.Rows.Select(r => r.Kind + "/" + r.Label + "/" + r.Path))}])");
		Assert.Equal("Your games", (window.FocusManager?.GetFocusedElement() as Control)?.DataContext is PlayerRomPickerRow first ? first.Label : "");

		//Down into a folder under the configured one: its list leads with the
		//action row, and the guard keeps the ring off that row - a stray Confirm
		//there would silently repoint *Your games* (ADR-0256 Decision 9, which
		//ADR-0264 Decision 11 keeps).
		//
		//The configured folder itself carries no action row - it IS *Your games*
		//- so the guard is proved one level further down, where there is a row to
		//protect.
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.PathText.Length > 0, "Confirm did not descend into the configured folder");
		Assert.DoesNotContain(model.RomPicker.Rows, r => r.Kind == RomPickerRowKind.Action);
		int nes = model.RomPicker.Rows.IndexOf(model.RomPicker.Rows.First(r => r.Label == "NES"));
		for(int i = 0; i < nes; i++) {
			Press(window, PadNavAction.Down);
		}
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.Rows.Any(r => r.Kind == RomPickerRowKind.Action),
			"a folder that can become Your games is missing its action row");
		Assert.Equal(RomPickerRowKind.Game,
			((window.FocusManager?.GetFocusedElement() as Control)?.DataContext as PlayerRomPickerRow)?.Kind);

		//B walks out of the browser, one level at a time, and lands back on the
		//library rather than out of the sheet.
		Press(window, PadNavAction.Back);
		Pump();
		Press(window, PadNavAction.Back);
		Pump();
		Press(window, PadNavAction.Back);
		WaitFor(() => model.RomPicker.Mode == RomPickerMode.Library, "B out of the browser's roots did not return to the library");
		Assert.True(model.RomPicker.IsVisible, "B out of the browser closed the whole sheet");
	}

	//#1032 (ADR-0264 Decision 8): a player who has set no library folder is told
	//the next step - a named state, never a blank grid.
	[AvaloniaFact]
	public void With_no_library_folder_the_sheet_says_what_to_do()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Preferences.GameFolder = "";
		ConfigManager.Config.Preferences.OverrideGameFolder = false;

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		Press(window, PadNavAction.Confirm);
		Pump();

		Assert.True(model.RomPicker.IsVisible, "the sheet did not open");
		Assert.Empty(model.RomPicker.Tiles);
		Assert.True(model.RomPicker.EmptyText.Length > 0, "the sheet says nothing about the missing library folder");
		Assert.DoesNotContain("[[", model.RomPicker.EmptyText);
		TextBlock sentence = window.FindNamed<TextBlock>("RomPickerLibraryEmpty");
		Assert.True(sentence.IsOnScreen(), "the empty state is not on screen");
		//And it names the next step rather than the problem.
		Assert.Contains("Browse a file", sentence.Text ?? "");
	}
}
