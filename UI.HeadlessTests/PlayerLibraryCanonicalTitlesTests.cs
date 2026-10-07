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
	//The cleaned titles the scan gives them are "Contra" and "Metroid".
	private string LibraryRoot()
	{
		string root = Path.Combine(_folder, "games");
		Directory.CreateDirectory(root);
		File.WriteAllBytes(Path.Combine(root, "Contra (U) [!].nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(root, "Metroid (USA).nes"), SyntheticNrom.Build());
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
	private (MainWindow Window, MainWindowViewModel Model, PlayerRomPickerViewModel Picker) ShowLibrary(
		Func<string, RomConsole, Task<string>> hash)
	{
		LibraryRoot();
		(MainWindow window, MainWindowViewModel model) = ShowHome();
		PlayerRomPickerViewModel picker = model.RomPicker;
		picker.RunLibraryScanInline = true;
		picker.NoIntroTable = TableOf(KnownSha1 + "\tnes\tContra (USA)");
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

		(MainWindow window, _, PlayerRomPickerViewModel picker) = ShowLibrary((path, _) => ByName(path));

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
		(MainWindow window, _, PlayerRomPickerViewModel picker) = ShowLibrary(async (path, _) => {
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

	//A ROM whose hash cannot be computed - the file went away between the scan
	//and the pass, a permission, a disk that refused to answer - is a tile the
	//scan already named, and nothing else. It must not empty the tile, and it
	//must not take the pass down with it: the library keeps working.
	[AvaloniaFact]
	public void A_rom_whose_hash_cannot_be_computed_keeps_its_file_name()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		(MainWindow window, _, PlayerRomPickerViewModel picker) = ShowLibrary((_, _) => throw new InvalidOperationException("unreadable ROM"));

		WaitFor(() => GridTitles(window).Contains("Metroid"), "the grid never filled");
		//The pass has run and answered nothing for either tile: both keep the
		//names the scan gave them, and neither is blank.
		Assert.Equal(new[] { "Contra", "Metroid" }, picker.Tiles.Select(t => t.Title).ToArray());
		Assert.True(picker.IsVisible, "a failed hash took the sheet down");
		Assert.DoesNotContain("", picker.Tiles.Select(t => t.Title));
	}
}
