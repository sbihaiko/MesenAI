using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

//#1065 (ADR-0264 Decisions 1 and 7): the library grid re-ordering itself by
//canonical title WHILE the sheet is open.
//
//#1038 shipped the titles as a rename in place and left the order to the next
//build, because the panel that cut it could not make the live re-sort safe: five
//review rounds on #1055 kept finding the same three defects in the path that
//rebuilt the filtered grid instead of reordering it. What is proved HERE is that
//the reorder is a MOVE and never a rebuild:
//
//  - the ring the player has placed on *Browse a file…* (or Back, or the search
//    box) is not a tile, so the re-order neither moves it nor claims it;
//  - with a query on, the tiles the grid already built keep their containers -
//    the same tile object, the same focus - while the order around them changes;
//  - a tile the query stops matching, because its canonical title is not what
//    the player searched for, leaves the grid.
//
//Both seams the title pass reads are injected in every case (the table and the
//hash), so nothing here depends on a real dump or on this machine's library.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryLiveResortTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1065-resort-" + Guid.NewGuid().ToString("N"));

	//A hash no table in this file holds, for the ROMs whose title is not the
	//subject of the case. Nothing recomputes it: the hash source below is what
	//decides which ROM the table is asked about.
	private const string UnknownSha1 = "0000000000000000000000000000000000000000";

	private static NoIntroNameTable TableOf(params string[] rows)
	{
		string header =
			"#mesen-no-intro-sha1-table\t1\n" +
			"#source\tfixture\n" +
			"#licence\tfixture\n" +
			"#hash\tfixture\n" +
			"#console\tnes\tFixture - NES\t2020.01.02\t" + new string('a', 64) + "\n";
		using MemoryStream output = new MemoryStream();
		using(GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
			byte[] bytes = Encoding.UTF8.GetBytes(header + string.Join("\n", rows) + "\n");
			gzip.Write(bytes, 0, bytes.Length);
		}
		return NoIntroNameTable.Load(new MemoryStream(output.ToArray()));
	}

	public PlayerLibraryLiveResortTests()
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

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	private static void Settle(int milliseconds)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(clock.ElapsedMilliseconds < milliseconds) {
			Pump();
			Thread.Sleep(20);
		}
		Pump();
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

	private static LibraryEntry Entry(string path, RomConsole console, string title) => new(path, console, title);

	//The titles the grid holds, in the order the grid holds them - which is the
	//whole subject of the first case.
	private static string Titles(PlayerRomPickerViewModel picker) => string.Join(", ", picker.Tiles.Select(t => t.Title));

	private static string Focused(MainWindow window)
	{
		Control? focused = window.FocusManager?.GetFocusedElement() as Control;
		return focused is null ? "focus=<none>" : $"focus={focused.GetType().Name}#{focused.Name}";
	}

	//The container a tile is drawn in, so a case can put the ring on one.
	private static Button TileButton(MainWindow window, PlayerLibraryTile tile)
	{
		return window.FindNamed<ItemsControl>("RomPickerGrid")
			.GetVisualDescendants()
			.OfType<Button>()
			.FirstOrDefault(button => ReferenceEquals(button.DataContext, tile))
			?? throw new InvalidOperationException("the grid has no container for that tile");
	}

	//The Play home, with the library opened over the caller's own scan and the
	//caller's own hash answers. Where the sheet is opened - and not one a test
	//assembled - is what makes the ring under test the ring a player would have.
	private (MainWindow Window, MainWindowViewModel Model) OpenLibrary(
		Func<IReadOnlyList<string>, FolderLister, Action<IReadOnlyList<LibraryEntry>>, LibraryScanResult> scan,
		bool inline,
		Func<string, RomConsole, CancellationToken, Task<string>> hash,
		params string[] tableRows)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");

		PlayerRomPickerViewModel picker = model.RomPicker;
		picker.LibraryFolderSource = () => new[] { _folder };
		picker.LibraryScanStreamSource = scan;
		picker.RunLibraryScanInline = inline;
		picker.NoIntroTable = TableOf(tableRows);
		picker.RomHashSource = hash;

		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		model.OpenRomPicker();
		Assert.True(picker.IsVisible, "the sheet never opened");
		Assert.Equal(RomPickerMode.Library, picker.Mode);
		return (window, model);
	}

	//The two ROMs every case below starts from, and the scan that hands them
	//over. Named so a case reads about the ORDER and not about the paths.
	private (string Contra, string Metroid) TwoRoms()
	{
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string metroid = Path.Combine(_folder, "Metroid (USA).nes");
		return (contra, metroid);
	}

	private static LibraryScanResult HandOver(IReadOnlyList<LibraryEntry> entries, Action<IReadOnlyList<LibraryEntry>> onBatch)
	{
		onBatch(entries);
		return new LibraryScanResult(entries, 1, false);
	}

	//ADR-0264 Decision 1: the grid is ordered by title, and #1038 resolved the
	//titles in place without ever re-ordering. The order is the canonical one the
	//moment the titles land, while the sheet stays open - and the ring the player
	//has already walked to *Browse a file…* is not a tile: the re-order leaves it
	//where it is rather than claiming it for the grid.
	[AvaloniaFact]
	public void The_grid_re_orders_by_canonical_title_while_the_sheet_is_open_and_the_ring_stays_on_browse_a_file()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		const string ZeldaSha1 = "4444444444444444444444444444444444444444";
		(string contra, string metroid) = TwoRoms();

		TaskCompletionSource<bool> held = new();
		LibraryScanResult Scan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			return HandOver(new[] {
				Entry(contra, RomConsole.Nes, "Contra"),
				Entry(metroid, RomConsole.Nes, "Metroid")
			}, onBatch);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(Scan, inline: true, hash: async (path, _, _) => {
			await held.Task;
			return Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal) ? ZeldaSha1 : UnknownSha1;
		}, ZeldaSha1 + "\tnes\tZelda II - The Adventure of Link (USA)");

		PlayerRomPickerViewModel picker = model.RomPicker;
		WaitFor(() => picker.Tiles.Count == 2, $"the grid never filled ({picker.Tiles.Count} tiles)");

		//The scan's own order, which is the cleaned file names: Contra then Metroid.
		Assert.Equal(new[] { "Contra", "Metroid" }, picker.Tiles.Select(t => t.Title).ToArray());

		Button browse = window.FindNamed<Button>("RomPickerBrowseFile");
		browse.Focus();
		Pump();
		Assert.True(browse.IsFocused, "the case never got the ring onto Browse a file…");

		held.SetResult(true);

		//"Zelda II - The Adventure of Link" sorts after "Metroid", so the tile the
		//canonical title renamed trades places with the one beside it.
		WaitFor(() => picker.Tiles.Any(t => t.Title == "Zelda II - The Adventure of Link"),
			$"the canonical title never landed (titles=[{Titles(picker)}])");
		WaitFor(() => Titles(picker) == "Metroid, Zelda II - The Adventure of Link",
			$"the grid never took the canonical order (titles=[{Titles(picker)}])");

		Settle(400);
		Assert.Equal("RomPickerBrowseFile", (window.FocusManager?.GetFocusedElement() as Control)?.Name);
	}

	//#1065 acceptance criterion 2: with a query on, the filtered grid is not
	//rebuilt per batch. A rebuild hands the grid a new list, so every tile is a
	//new container and the ring - which is a fact about containers - is left on
	//one the sheet threw away. The tile the player is on is the SAME tile object
	//after the next batch lands, and the ring is still on it.
	[AvaloniaFact]
	public void A_query_keeps_the_tiles_it_already_built_when_the_next_streamed_batch_lands()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		(string contra, string metroid) = TwoRoms();
		string tetris = Path.Combine(_folder, "Tetris (World).gb");

		using ManualResetEventSlim firstBatchPublished = new(false);
		using ManualResetEventSlim release = new(false);

		LibraryScanResult SlowScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			onBatch(new[] {
				Entry(contra, RomConsole.Nes, "Contra"),
				Entry(metroid, RomConsole.Nes, "Metroid")
			});
			firstBatchPublished.Set();
			release.Wait(TimeSpan.FromSeconds(30));
			onBatch(new[] { Entry(tetris, RomConsole.GameBoy, "Tetris") });
			return new LibraryScanResult(new[] {
				Entry(contra, RomConsole.Nes, "Contra"),
				Entry(metroid, RomConsole.Nes, "Metroid"),
				Entry(tetris, RomConsole.GameBoy, "Tetris")
			}, 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(SlowScan, inline: false,
			hash: (_, _, _) => Task.FromResult(UnknownSha1));
		PlayerRomPickerViewModel picker = model.RomPicker;

		WaitFor(() => picker.Tiles.Count == 2, $"the first batch never reached the grid ({Titles(picker)})");
		Assert.True(firstBatchPublished.IsSet, "the scan's first batch was never published");

		//A query that keeps both games AND the one the next batch brings, so the
		//grid is the tiles' to keep rather than the query's to rebuild.
		picker.SearchQuery = "t";
		Pump();
		Assert.Equal(2, picker.Tiles.Count);

		PlayerLibraryTile[] before = picker.Tiles.ToArray();
		Button button = TileButton(window, before[0]);
		button.Focus();
		Pump();
		Assert.True(button.IsFocused, $"the case never got the ring onto a tile (${Focused(window)})");

		release.Set();

		WaitFor(() => picker.Tiles.Count == 3, $"the second batch never reached the filtered grid ({Titles(picker)})");
		Settle(200);

		//The two tiles the grid already had are the tiles it still has.
		foreach(PlayerLibraryTile tile in before) {
			Assert.Same(tile, picker.Tiles.FirstOrDefault(shown => ReferenceEquals(shown, tile)));
		}
		//And the ring never left the one the player was on.
		Assert.Same(before[0], (window.FocusManager?.GetFocusedElement() as Control)?.DataContext);
	}

	//#1065 acceptance criterion 2, the other edge: the query is asked of the title
	//the tile ends up showing, so a canonical title that is no longer what the
	//player searched for takes the tile off the grid - and the empty result is
	//named, exactly as it is when the keystroke itself did the dropping.
	[AvaloniaFact]
	public void A_tile_stops_matching_when_its_canonical_title_is_not_what_the_query_asked_for()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		const string ZeldaSha1 = "4444444444444444444444444444444444444444";
		(string contra, string metroid) = TwoRoms();

		TaskCompletionSource<bool> held = new();
		LibraryScanResult Scan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			return HandOver(new[] {
				Entry(contra, RomConsole.Nes, "Contra"),
				Entry(metroid, RomConsole.Nes, "Metroid")
			}, onBatch);
		}

		(MainWindow _, MainWindowViewModel model) = OpenLibrary(Scan, inline: true, hash: async (path, _, _) => {
			await held.Task;
			return Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal) ? ZeldaSha1 : UnknownSha1;
		}, ZeldaSha1 + "\tnes\tZelda II - The Adventure of Link (USA)");

		PlayerRomPickerViewModel picker = model.RomPicker;
		WaitFor(() => picker.Tiles.Count == 2, $"the grid never filled ({picker.Tiles.Count} tiles)");

		picker.SearchQuery = "contra";
		Pump();
		Assert.Equal(new[] { "Contra" }, picker.Tiles.Select(t => t.Title).ToArray());

		held.SetResult(true);

		WaitFor(() => picker.Tiles.Count == 0,
			$"the tile the query no longer matches is still on the grid (tiles=[{Titles(picker)}])");
		//The named empty RESULT and never a blank grid (ADR-0264 Decision 4).
		Assert.NotEqual("", picker.EmptyText);
	}

	//#1065 acceptance criterion 4: the sheet's own idea of the grid's focus is
	//cleared the moment the ring leaves the grid.
	//
	//It is the state the live re-sort reads, and the #1055 review is why it has to
	//be cleared rather than merely set: a marker that outlives the ring cannot
	//tell a player walking the grid from one parked on *Browse a file...*, and a
	//re-sort that guesses wrong takes the ring off the control the player put it
	//on. The other half - that the ring on *Browse a file...* survives a re-sort -
	//is the first case in this file.
	[AvaloniaFact]
	public void The_sheets_idea_of_the_grids_focus_is_cleared_when_the_ring_leaves_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		(string contra, string metroid) = TwoRoms();
		const string UnresolvedSha1 = "1111111111111111111111111111111111111111";

		LibraryScanResult Scan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			return HandOver(new[] {
				Entry(contra, RomConsole.Nes, "Contra"),
				Entry(metroid, RomConsole.Nes, "Metroid")
			}, onBatch);
		}

		//The table holds a hash neither ROM answers, so the pass renames nothing:
		//this case is about the ring and not about the titles.
		(MainWindow window, MainWindowViewModel model) = OpenLibrary(Scan, inline: true,
			hash: (_, _, _) => Task.FromResult(UnresolvedSha1),
			"2222222222222222222222222222222222222222\tnes\tSomewhere Else");

		PlayerRomPickerViewModel picker = model.RomPicker;
		WaitFor(() => picker.Tiles.Count == 2, $"the grid never filled ({picker.Tiles.Count} tiles)");

		Button tile = TileButton(window, picker.Tiles[0]);
		tile.Focus();
		Pump();
		Assert.True(tile.IsFocused, "the case never got the ring onto a tile");
		Assert.Same(picker.Tiles[0], picker.FocusTile);

		//The player steps up to *Browse a file...*, which is where the ring is not
		//a tile any more.
		Button browse = window.FindNamed<Button>("RomPickerBrowseFile");
		browse.Focus();
		Pump();

		Assert.Null(picker.FocusTile);
	}
}
