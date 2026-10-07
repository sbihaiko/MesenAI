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

//#1038 (ADR-0266, spec #1030): the canonical-title pass on the real sheet. The
//rule itself (a hash the table knows becomes the database's title, everything
//else keeps the cleaned file name) is pinned host-free in
//UI.Tests/PlayerLibraryCanonicalTitleTests; what is proved HERE is the wiring a
//claim about the rule cannot reach:
//
//  - a tile on the screen actually reads the database's title, through the real
//    grid and the real DataTemplate - not only the view-model's copy of it;
//  - the grid is complete and readable BEFORE any hash answers, which is the
//    whole of "hashing never blocks the grid";
//  - a ROM whose hash cannot be computed leaves its tile exactly as the scan
//    named it.
//
//Both seams the pass reads are injected in every case (the table and the hash),
//so nothing here depends on a real dump, on this machine's library, or on the
//player's hash cache: the assertions are about the wiring and never about
//whatever the suite happened to find on a disk.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryCanonicalTitlesTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1038-titles-" + Guid.NewGuid().ToString("N"));

	//A hash the synthetic table below holds, and one no table holds: the two
	//answers the rule has to tell apart. Nothing recomputes them - they are
	//strings this file chose, and the file it injects is what maps them.
	private const string KnownSha1 = "AA11BB22CC33DD44EE55FF660011223344556677";
	private const string UnknownSha1 = "0000000000000000000000000000000000000000";

	//A hash the table holds is a ROMs' payload hash. The `.nes` files here are
	//not real dumps and their real hash is not known to any table, which is
	//exactly why the hash source is injected: the case is about what the sheet
	//DOES with an answer, not about how the answer is computed (that is
	//RomHashCacheTests' subject).
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

	public PlayerLibraryCanonicalTitlesTests()
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

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	//A bounded settle, for the cases that assert something does NOT happen: the
	//stale work is given a fixed window to show up in rather than being asserted
	//on a snapshot taken the instant the current work finished.
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

	//The titles the grid's own containers show, read off the visual tree rather
	//than off the view-model: a rename that never reached the template would
	//leave this empty while Tiles read perfectly.
	private static List<string> GridTitles(MainWindow window)
	{
		return window.FindNamed<ItemsControl>("RomPickerGrid")
			.GetVisualDescendants()
			.OfType<TextBlock>()
			.Select(block => block.Text ?? "")
			.ToList();
	}

	private static string Titles(PlayerRomPickerViewModel picker) => string.Join(", ", picker.Tiles.Select(t => t.Title));

	//Two ROMs under one library folder, one of which the injected table knows.
	//The cleaned titles the scan gives them are "Contra" and "Metroid" - and, with
	//`withArchive`, a third entry that is the same game zipped, which is how a
	//cabinet's library usually looks (ADR-0264 Decision 9).
	private string LibraryRoot(bool withArchive = false)
	{
		string root = Path.Combine(_folder, "games");
		Directory.CreateDirectory(root);
		File.WriteAllBytes(Path.Combine(root, "Contra (U) [!].nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(root, "Metroid (USA).nes"), SyntheticNrom.Build());
		if(withArchive) {
			//The archive whose name the console classifier recognises: one game on
			//the grid, and the entry whose bytes are an archive rather than a ROM.
			File.WriteAllBytes(Path.Combine(root, "Contra (U) [!].nes.zip"), SyntheticNrom.Build());
		}
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		return root;
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowHome()
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
		return (window, model);
	}

	//The sheet, open on a library of two ROMs, with the scan inline and both
	//seams injected. The pass itself is left real - a background task posting
	//its batches - so every case below waits for it the way the app does.
	//The table is a parameter because one case needs a SECOND row: a ROM the table
	//knows *behind* the one whose hash throws, which is what turns "it survived"
	//into something the grid can be waited on.
	private (MainWindow Window, MainWindowViewModel Model, PlayerRomPickerViewModel Picker) ShowLibrary(
		Func<string, RomConsole, CancellationToken, Task<string>> hash, params string[] tableRows)
	{
		LibraryRoot();
		(MainWindow window, MainWindowViewModel model) = ShowHome();
		PlayerRomPickerViewModel picker = model.RomPicker;
		picker.RunLibraryScanInline = true;
		picker.NoIntroTable = TableOf(tableRows.Length > 0
			? tableRows
			: new[] { KnownSha1 + "\tnes\tContra (USA)" });
		picker.RomHashSource = hash;
		model.OpenRomPicker();
		WaitFor(() => picker.Tiles.Count == 2, $"the grid never filled ({picker.Tiles.Count} tiles)");
		return (window, model, picker);
	}

	private static Task<string> ByName(string path)
	{
		return Task.FromResult(Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal) ? KnownSha1 : UnknownSha1);
	}

	//The rule, end to end: a ROM the table knows takes the database's own title -
	//"Contra", not "Contra (USA)" and not the file name it was downloaded as -
	//and a ROM it does not know keeps the cleaned file name the scan gave it.
	//Both are asserted on the tile AND on the grid, because a title the
	//view-model holds and the sheet does not show is not a title.
	[AvaloniaFact]
	public void A_matched_rom_takes_the_databases_title_and_an_unmatched_one_keeps_its_file_name()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		(MainWindow window, _, PlayerRomPickerViewModel picker) = ShowLibrary((path, _, _) => ByName(path));

		WaitFor(() => picker.Tiles[0].Title == "Contra",
			$"the matched ROM never took the database's title (titles=[{Titles(picker)}])");
		Assert.Equal(new[] { "Contra", "Metroid" }, picker.Tiles.Select(t => t.Title).ToArray());

		WaitFor(() => GridTitles(window).Contains("Contra"),
			$"the renamed title is not on the sheet ([{string.Join("|", GridTitles(window))}])");
		Assert.Contains("Metroid", GridTitles(window));
	}

	//ADR-0264 Decisions 7 and 10, the half a rule test cannot reach: the grid is
	//complete and readable BEFORE any hash answers. The hash source is held open
	//here, so the sheet is looked at in exactly the state a player sees on a cold
	//library - every tile named by its file, on screen, usable - and the rename
	//only happens after the answer arrives.
	[AvaloniaFact]
	public void The_grid_is_up_and_readable_before_any_hash_answers()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		TaskCompletionSource<bool> held = new();
		(MainWindow window, _, PlayerRomPickerViewModel picker) = ShowLibrary(async (path, _, _) => {
			await held.Task;
			return Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal) ? KnownSha1 : UnknownSha1;
		});

		//Nothing has answered yet, and the sheet is already the library: two
		//tiles, named by their cleaned file names, on the screen.
		Assert.Equal(new[] { "Contra", "Metroid" }, picker.Tiles.Select(t => t.Title).ToArray());
		Assert.True(window.FindNamed<Border>("PlayerRomPickerSheet").IsOnScreen(), "the sheet is not on screen while the hashes are outstanding");
		WaitFor(() => GridTitles(window).Contains("Metroid"), "the grid shows no tile while the hashes are outstanding");

		held.SetResult(true);

		WaitFor(() => picker.Tiles[0].Title == "Contra",
			$"the title never arrived once the hash did (titles=[{Titles(picker)}])");
	}

	//#1038 review finding 2 (ADR-0264 Decisions 7 and 9): one pass per scan, and
	//the sheet is what decides how long it lives. A second scan REPLACES the
	//first - the tiles it holds belong to a grid nobody sees - so the pass the
	//first scan started has to stop, and stop where it stands: at the tile it has
	//not begun, never a third one.
	//
	//Both passes are held inside their first read here, so the stale one is alive
	//and blocked exactly when the second scan lands on it - the state a library
	//of 20 000 ROMs is in for seconds at a time.
	[AvaloniaFact]
	public void A_pass_the_next_scan_replaced_stops_at_the_tile_it_never_started()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		TaskCompletionSource<bool> held = new();
		int calls = 0;
		(MainWindow window, MainWindowViewModel model, PlayerRomPickerViewModel picker) = ShowLibrary(async (path, _, _) => {
			Interlocked.Increment(ref calls);
			await held.Task;
			return Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal) ? KnownSha1 : UnknownSha1;
		});
		WaitFor(() => Volatile.Read(ref calls) == 1, $"the pass never asked for a hash ({calls} calls)");

		//The player closes the sheet and opens it again. That is a second scan, and
		//with it a second pass over tiles that replace the first one's.
		picker.Hide();
		model.OpenRomPicker();
		WaitFor(() => Volatile.Read(ref calls) == 2, $"the second scan's pass never asked for a hash ({calls} calls)");

		held.SetResult(true);

		//The pass that is CURRENT finishes its own tiles: the read it was holding,
		//then the second tile. The replaced one stops at the tile it never started,
		//so three calls is where the walk ends and where it stays.
		WaitFor(() => Volatile.Read(ref calls) >= 3, $"the current pass never finished its own tiles ({calls} calls)");
		Settle(400);
		Assert.Equal(3, Volatile.Read(ref calls));
	}

	//The other end of the same rule: the sheet closing stops the pass it started.
	//A player who leaves the library must not leave a hash walk behind them - the
	//sheet is gone, and there is no tile left for the walk's answer to name.
	[AvaloniaFact]
	public void A_closed_sheet_stops_the_pass_it_started()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		TaskCompletionSource<bool> held = new();
		int calls = 0;
		CancellationToken? passToken = null;
		(MainWindow _, _, PlayerRomPickerViewModel picker) = ShowLibrary(async (path, _, token) => {
			Interlocked.Increment(ref calls);
			passToken = token;
			await held.Task;
			return Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal) ? KnownSha1 : UnknownSha1;
		});
		WaitFor(() => Volatile.Read(ref calls) == 1, $"the pass never asked for a hash ({calls} calls)");

		picker.Hide();

		//The token the read was handed is the one that fires: the pass is stopped
		//at the source, not only around it - a read already inside a file is what
		//this is for.
		WaitFor(() => passToken?.IsCancellationRequested == true,
			"closing the sheet never cancelled the token the pass's read was handed");

		held.SetResult(true);

		//The read the pass was holding answers after the sheet is gone, and the
		//pass has no second tile to ask for: the count stays where the close left
		//it.
		Settle(400);
		Assert.Equal(1, Volatile.Read(ref calls));
	}

	//#1038 review finding 4 (ADR-0264 Decision 11): *Browse a file…* leaves the
	//library for the sheet's SECOND surface without closing the sheet, so neither
	//trigger the pass had - a new scan, IsVisible false - fires, and the walk keeps
	//hashing the library behind a player who is now in the folder browser. Every
	//batch it posts from there is thrown away by the `Mode != Library` guard, and
	//coming back to the library starts a fresh scan and a fresh pass anyway: up to
	//MaxEntries files read for nothing, while the browser's own scan wants the same
	//disk.
	[AvaloniaFact]
	public void A_pass_the_player_left_the_library_from_stops_at_the_tile_it_never_started()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		TaskCompletionSource<bool> held = new();
		int calls = 0;
		CancellationToken? passToken = null;
		(MainWindow _, _, PlayerRomPickerViewModel picker) = ShowLibrary(async (path, _, token) => {
			Interlocked.Increment(ref calls);
			passToken = token;
			await held.Task;
			return Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal) ? KnownSha1 : UnknownSha1;
		});
		WaitFor(() => Volatile.Read(ref calls) == 1, $"the pass never asked for a hash ({calls} calls)");

		//The player steps into the browser. The sheet stays up, so the library's
		//surface is left rather than closed - which is exactly the state the two
		//older triggers cannot see.
		picker.BrowseFile();
		Assert.Equal(RomPickerMode.BrowseFile, picker.Mode);

		//The token the read was handed is the one that fires: the pass is stopped at
		//the source, not only around it - a read already inside a file is what this
		//is for.
		WaitFor(() => passToken?.IsCancellationRequested == true,
			"stepping into the browser never cancelled the token the pass's read was handed");

		held.SetResult(true);

		//The read the pass was holding answers after the player left the library, and
		//the pass has no second tile to ask for: the count stays where the step left
		//it.
		Settle(400);
		Assert.Equal(1, Volatile.Read(ref calls));
	}

	//The grid's tile for one game, by data context - the same way the focus
	//arbiter finds them, so this reads the container the player's ring would be on.
	private static Button? TileButton(MainWindow window, PlayerLibraryTile tile)
	{
		return window.FindNamed<ItemsControl>("RomPickerGrid")
			.GetVisualDescendants()
			.OfType<Button>()
			.FirstOrDefault(button => ReferenceEquals(button.DataContext, tile));
	}

	//#1038 review finding 5 (ADR-0264 Decisions 1 and 7): the grid is ordered by
	//TITLE, and the displayed title IS the canonical one - so an order kept on the
	//cleaned file name diverges from the ADR exactly when a canonical title lands.
	//The table here names the ROM a title that sorts on the other side of the other
	//tile, so the order has to change when the hash answers.
	//
	//And it changes under the player's finger: the ring stays on the SAME GAME -
	//the tile the player selected travels with the reorder - never on whatever ends
	//up in the place it held.
	[AvaloniaFact]
	public void A_canonical_title_that_changes_the_order_carries_the_ring_with_its_game()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		const string ZeldaSha1 = "4444444444444444444444444444444444444444";
		TaskCompletionSource<bool> held = new();
		(MainWindow window, MainWindowViewModel model, PlayerRomPickerViewModel picker) = ShowLibrary(async (path, _, _) => {
			await held.Task;
			return Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal) ? ZeldaSha1 : UnknownSha1;
		}, ZeldaSha1 + "\tnes\tZelda II - The Adventure of Link (USA)");

		//The scan's order: by the titles the tiles carry before any hash answers.
		Assert.Equal(new[] { "Contra", "Metroid" }, picker.Tiles.Select(t => t.Title).ToArray());

		//The player is on the FIRST tile - the one whose game is about to be renamed
		//and to change place, so the place it held is a different game afterwards.
		//That is what makes the ring assertion below mean something: a re-claim that
		//simply keeps the ring on the leading tile would answer the wrong game.
		PlayerLibraryTile selected = picker.Tiles[0];
		Button? selectedButton = TileButton(window, selected);
		Assert.NotNull(selectedButton);
		selectedButton!.Focus();
		Pump();
		Assert.True(selectedButton.IsFocused, $"the case never got the ring onto a tile (focusTile={picker.FocusTile?.Title})");

		held.SetResult(true);

		//The canonical title sorts after "Metroid", so the grid now leads with the
		//game the player was on.
		WaitFor(() => picker.Tiles.Select(t => t.Title).SequenceEqual(new[] { "Metroid", "Zelda II - The Adventure of Link" }),
			$"the grid never re-sorted by the canonical titles (titles=[{Titles(picker)}])");

		Pump();
		//The game moved - it is the second tile now - and it is still the player's.
		Assert.Same(selected, picker.Tiles[1]);
		//The ring is read off the focus manager rather than off the button the case
		//focused: a re-sort rebuilds the container, so the control that holds the
		//ring may legitimately be a new one - what must not change is the GAME under
		//it.
		Assert.Same(selected, (window.FocusManager?.GetFocusedElement() as Control)?.DataContext);
	}

	//#1038 review finding 4 (ADR-0264 Decision 9): a zipped game is an entry of the
	//library, and its archive path is what the grid holds - but RomHashCache hashes
	//the bytes of the file it is handed (ADR-0003's No-Intro payload range is a
	//ROM's), so an archive can never answer a table lookup. Reading one is reading
	//the whole archive to the end for a title it cannot have, on every cold cache.
	//The pass skips archives entirely; the tile keeps the cleaned file name the
	//scan gave it, which is the same title the archive's own ROM tile gets.
	[AvaloniaFact]
	public void An_archive_is_never_read_for_a_title()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		LibraryRoot(withArchive: true);
		(MainWindow _, MainWindowViewModel model) = ShowHome();
		PlayerRomPickerViewModel picker = model.RomPicker;
		picker.RunLibraryScanInline = true;
		picker.NoIntroTable = TableOf(KnownSha1 + "\tnes\tContra (USA)");
		List<string> asked = new();
		picker.RomHashSource = (path, _, _) => {
			lock(asked) {
				asked.Add(path);
			}
			return Task.FromResult(Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal) ? KnownSha1 : UnknownSha1);
		};
		model.OpenRomPicker();
		WaitFor(() => picker.Tiles.Count == 3, $"the grid never filled ({picker.Tiles.Count} tiles)");

		//Both loose ROMs are hashed, so the pass ran to the end of the walk.
		WaitFor(() => asked.Count(p => p.EndsWith(".nes", StringComparison.Ordinal)) == 2,
			$"the pass did not read both ROMs ([{string.Join(", ", asked)}])");
		Settle(400);

		Assert.DoesNotContain(asked, p => p.EndsWith(".zip", StringComparison.Ordinal));
		//The archive's tile is still on the grid, named by the scan - skipped is not
		//dropped (Decision 9: the archive path IS the entry).
		Assert.Equal(3, picker.Tiles.Count);
		Assert.Equal(new[] { "Contra", "Contra", "Metroid" }, picker.Tiles.Select(t => t.Title).ToArray());
	}

	//A ROM whose hash cannot be computed - the file went away between the scan
	//and the pass, a permission, a disk that refused to answer - is a tile the
	//scan already named, and nothing else. It must not empty the tile, and it
	//must not take the pass down with it: the library keeps working.
	//
	//#1038 review finding 3: this case used to wait for "Metroid", which is on the
	//grid before the pass runs at all, so it passed whether the pass started,
	//crashed, or never asked for a hash. The ROM that throws is asked first and
	//its call is counted, and the ROM BEHIND it in the walk is one the table knows
	//under another name - so the wait below is the pass surviving the throw and
	//carrying on to the next tile, which is the claim the case is named for.
	[AvaloniaFact]
	public void A_rom_whose_hash_cannot_be_computed_keeps_its_file_name()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		const string MetroidSha1 = "3333333333333333333333333333333333333333";
		int thrown = 0;
		(MainWindow window, _, PlayerRomPickerViewModel picker) = ShowLibrary((path, _, _) => {
			//The scan orders the grid by title, so "Contra" is walked first.
			if(Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal)) {
				Interlocked.Increment(ref thrown);
				throw new InvalidOperationException("unreadable ROM");
			}
			return Task.FromResult(MetroidSha1);
		},
		KnownSha1 + "\tnes\tContra (USA)",
		MetroidSha1 + "\tnes\tMetroid II - Return of Samus (USA)");

		//The title of the ROM behind the throwing one, which the pass can only
		//reach by having survived it.
		WaitFor(() => picker.Tiles.Any(t => t.Title == "Metroid II - Return of Samus"),
			$"the pass did not survive the throwing ROM (titles=[{Titles(picker)}])");

		Assert.Equal(1, Volatile.Read(ref thrown));
		//The tile the pass could not name keeps exactly what the scan gave it, and
		//neither tile is blank.
		Assert.Contains(picker.Tiles, t => t.Title == "Contra");
		Assert.True(picker.IsVisible, "a failed hash took the sheet down");
		Assert.DoesNotContain("", picker.Tiles.Select(t => t.Title));
		//And it reached the sheet, not only the view-model.
		Assert.Contains("Metroid II - Return of Samus", GridTitles(window));
	}
}
