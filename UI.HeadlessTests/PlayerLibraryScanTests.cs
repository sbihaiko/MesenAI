using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

//#1037 (ADR-0264 Decision 9): the library scan is BOUNDED, BACKGROUNDED and
//VISIBLE. The rules half - the caps and the stream itself - is host-free in
//UI.Tests/PlayerLibraryScanTests; what is proved here is the half a player can
//see and touch:
//
//- the scan runs OFF the UI thread, so a slow one leaves the sheet usable: the
//  grid fills as batches arrive and the pad still moves the ring while the walk
//  is held open;
//- an animated indicator is on screen for exactly as long as the scan runs;
//- when the sheet reopens it does so on the game the player was on last time.
//
//The scan is stubbed in both cases - the real one reads the disk this suite
//happens to run on, and a case about a SLOW scan has to own its own timing.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryScanTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1037-" + Guid.NewGuid().ToString("N"));

	//The stand-in pad backend, built the way PlayerLibraryTests builds it: a
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

	public PlayerLibraryScanTests()
	{
		Directory.CreateDirectory(_folder);
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

	private void Press(MainWindow window, PadNavAction action)
	{
		PressOnly(window, action);
		Pump();
	}

	//The press without the turn that follows it: what a case needs to look at is
	//the state the press itself left behind, before the dispatcher runs whatever
	//the handler posted behind it.
	private void PressOnly(MainWindow window, PadNavAction action)
	{
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(Mapping, action) }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
	}

	//The path of the tile the ring is on, or null when the ring is somewhere
	//else - which is what "the sheet reopened on that game" is measured with.
	private static string? FocusedTilePath(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.DataContext is PlayerLibraryTile tile ? tile.Path : null;
	}

	private static string Focused(MainWindow window)
	{
		Control? focused = window.FocusManager?.GetFocusedElement() as Control;
		return focused is null ? "focus=<none>" : $"focus={focused.GetType().Name}#{focused.Name} dc={focused.DataContext?.GetType().Name}";
	}

	private static LibraryEntry Entry(string path, RomConsole console, string title) => new(path, console, title);

	//The sheet the Play home opens, with the scan replaced by the caller's own:
	//the home's own Confirm is what opens it, so the surface under test is the
	//one a player reaches and not one a test assembled.
	private (MainWindow Window, MainWindowViewModel Model) OpenLibrary(
		Func<IReadOnlyList<string>, FolderLister, Action<IReadOnlyList<LibraryEntry>>, LibraryScanResult> scan,
		bool inline, bool pumpAfterOpen = true)
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

		model.RomPicker.LibraryFolderSource = () => new[] { _folder };
		model.RomPicker.LibraryScanStreamSource = scan;
		model.RomPicker.RunLibraryScanInline = inline;

		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();

		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		//The open's own turn, and then the turns behind it - unless the caller
		//needs to look at the first one before it is over.
		if(pumpAfterOpen) {
			Press(window, PadNavAction.Confirm);
			Pump();
		} else {
			PressOnly(window, PadNavAction.Confirm);
		}

		Assert.True(model.RomPicker.IsVisible, "the pad's Confirm opened no sheet");
		Assert.Equal(RomPickerMode.Library, model.RomPicker.Mode);
		return (window, model);
	}

	//A library scan is seconds of disk work on a cold cache, and the sheet has to
	//stay usable through all of it. The scan here is slow ON PURPOSE: it hands
	//over one batch, then holds the walk until the case releases it. Everything
	//asserted while it is held - the tiles that are already up, the ring that
	//already moves - is a thing a scan running on the UI thread could not do.
	[AvaloniaFact]
	public void The_grid_fills_while_a_slow_scan_runs_and_the_sheet_stays_usable()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string metroid = Path.Combine(_folder, "Metroid (USA).nes");
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
			//The walk is held here, on the scan's own thread, for as long as the
			//case needs: the UI thread is never in this call.
			release.Wait(TimeSpan.FromSeconds(30));
			onBatch(new[] { Entry(tetris, RomConsole.GameBoy, "Tetris") });
			return new LibraryScanResult(
				new[] { Entry(contra, RomConsole.Nes, "Contra"), Entry(metroid, RomConsole.Nes, "Metroid"), Entry(tetris, RomConsole.GameBoy, "Tetris") },
				1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(SlowScan, inline: false);

		try {
			WaitFor(() => model.RomPicker.Tiles.Count == 2, $"the first batch never reached the grid ({Focused(window)})");
			Assert.True(firstBatchPublished.IsSet, "the scan's first batch was never published");
			Assert.True(model.RomPicker.IsScanning, "the scan is over, so this case proves nothing about the wait");
			//Ordered by title while it fills, not in whatever order the disk
			//answered: Decision 1 holds at every instant of the scan.
			Assert.Equal(new[] { "Contra", "Metroid" }, model.RomPicker.Tiles.Select(t => t.Title).ToArray());

			//The wait is on screen and it moves: an indeterminate bar IS the
			//animation (#734, every visible wait needs one).
			ProgressBar indicator = window.FindNamed<ProgressBar>("RomPickerScanProgress");
			Assert.True(indicator.IsIndeterminate, "the scan's indicator does not move");
			Assert.True(indicator.IsOnScreen(), "the scan is running with no indicator on screen");

			//And the sheet answers the pad while the walk is held open: the ring
			//is on the first game and Right takes it to the second one.
			WaitFor(() => FocusedTilePath(window) == contra, $"the ring never landed on the first game ({Focused(window)})");
			Press(window, PadNavAction.Right);
			Assert.Equal(metroid, FocusedTilePath(window));

			//The rest of the library arrives, and the wait goes with it.
			release.Set();
			WaitFor(() => !model.RomPicker.IsScanning, "the scan's indicator never cleared");
			Assert.Equal(new[] { "Contra", "Metroid", "Tetris" }, model.RomPicker.Tiles.Select(t => t.Title).ToArray());
			Assert.False(window.FindNamed<ProgressBar>("RomPickerScanProgress").IsOnScreen(),
				"the indicator is still on screen after the scan ended");
			Assert.Contains("3 games", model.RomPicker.HeaderText);
		} finally {
			//Never leave the scan's thread parked, whatever the case did above.
			release.Set();
		}
	}

	//#1037 (ADR-0264 Decision 1): "the entry the player focused last time is
	//focused again when the sheet reopens". A player who comes back to the shelf
	//comes back to the game they were on, not to the top of it.
	[AvaloniaFact]
	public void The_sheet_reopens_on_the_game_the_player_focused_last_time()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string metroid = Path.Combine(_folder, "Metroid (USA).nes");

		LibraryScanResult InstantScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			onBatch(new[] {
				Entry(contra, RomConsole.Nes, "Contra"),
				Entry(metroid, RomConsole.Nes, "Metroid")
			});
			return new LibraryScanResult(
				new[] { Entry(contra, RomConsole.Nes, "Contra"), Entry(metroid, RomConsole.Nes, "Metroid") }, 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(InstantScan, inline: true);

		WaitFor(() => FocusedTilePath(window) == contra, $"the sheet did not open on its first game ({Focused(window)})");
		Press(window, PadNavAction.Right);
		Assert.Equal(metroid, FocusedTilePath(window));

		//B leaves the sheet from anywhere (ADR-0256's stop rule), and the ring
		//goes back to the home with it.
		Press(window, PadNavAction.Back);
		Pump();
		Assert.False(model.RomPicker.IsVisible, "B did not close the sheet");

		//The same door the player came in by, and the sheet is on the game they
		//left on rather than on the first one the scan lists.
		Press(window, PadNavAction.Confirm);
		Pump();
		Assert.True(model.RomPicker.IsVisible, "the pad's Confirm did not reopen the sheet");
		WaitFor(() => FocusedTilePath(window) == metroid,
			$"the sheet reopened on {Focused(window)} instead of the game the player left on");
	}

	//The ring is in the sheet's header - *Browse a file…* or Back - rather than
	//in the grid. This is what "the scan did not take the ring off the player"
	//is measured with.
	private static bool InHeader(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.Name is "RomPickerBrowseFile" or "RomPickerBack";
	}

	//#1037 (ADR-0264 Decision 1): "the entry the player focused last time is
	//focused again when the sheet reopens" has to hold for the scan that runs in
	//the BACKGROUND, which is the one a real library gets. The remembered game
	//here is in the sheet's SECOND listing: the first batch arrives without it,
	//and the sheet is asked, at that instant, not to read the game that sorted
	//first as the player's choice.
	[AvaloniaFact]
	public void The_sheet_reopens_on_a_game_the_scan_has_not_reached_yet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string metroid = Path.Combine(_folder, "Metroid (USA).nes");
		string tetris = Path.Combine(_folder, "Tetris (World).gb");

		LibraryScanResult InstantScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry[] all = {
				Entry(contra, RomConsole.Nes, "Contra"),
				Entry(metroid, RomConsole.Nes, "Metroid"),
				Entry(tetris, RomConsole.GameBoy, "Tetris")
			};
			onBatch(all);
			return new LibraryScanResult(all, 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(InstantScan, inline: true);

		//The player walks to a game that a real scan would list LAST, and leaves
		//the sheet from there.
		WaitFor(() => FocusedTilePath(window) == contra, $"the sheet did not open on its first game ({Focused(window)})");
		Press(window, PadNavAction.Right);
		Press(window, PadNavAction.Right);
		Assert.Equal(tetris, FocusedTilePath(window));
		Press(window, PadNavAction.Back);
		Pump();
		Assert.False(model.RomPicker.IsVisible, "B did not close the sheet");
		Assert.Equal(tetris, model.RomPicker.LastFocusedTilePath);

		using ManualResetEventSlim firstBatchPublished = new(false);
		using ManualResetEventSlim release = new(false);
		LibraryScanResult SlowScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry[] first = { Entry(contra, RomConsole.Nes, "Contra"), Entry(metroid, RomConsole.Nes, "Metroid") };
			onBatch(first);
			firstBatchPublished.Set();
			release.Wait(TimeSpan.FromSeconds(30));
			LibraryEntry last = Entry(tetris, RomConsole.GameBoy, "Tetris");
			onBatch(new[] { last });
			return new LibraryScanResult(new[] { first[0], first[1], last }, 1, false);
		}

		model.RomPicker.LibraryScanStreamSource = SlowScan;
		model.RomPicker.RunLibraryScanInline = false;

		try {
			Press(window, PadNavAction.Confirm);
			Pump();
			Assert.True(model.RomPicker.IsVisible, "the pad's Confirm did not reopen the sheet");

			WaitFor(() => model.RomPicker.Tiles.Count == 2, $"the first batch never reached the grid ({Focused(window)})");
			Assert.True(firstBatchPublished.IsSet, "the scan's first batch was never published");

			//The remembered game is not on screen yet. Nothing the sheet does on
			//its own may then be read as the player's choice: the ring waits in
			//the header, and the game the player left on is still the one the
			//sheet is going to open on.
			Assert.Equal(tetris, model.RomPicker.LastFocusedTilePath);
			Assert.NotEqual(contra, FocusedTilePath(window));
			//The sheet parks the ring on Back - not on Browse a file…, which a
			//press would turn into leaving the library the player waits on.
			Assert.Equal("RomPickerBack", (window.FocusManager?.GetFocusedElement() as Control)?.Name);

			//The rest of the library lands, and the sheet lands on the game the
			//player was on rather than on whatever sorted first.
			release.Set();
			WaitFor(() => FocusedTilePath(window) == tetris,
				$"the sheet opened on {Focused(window)} instead of the game the player left on");
			Assert.Equal(tetris, model.RomPicker.LastFocusedTilePath);
		} finally {
			release.Set();
		}
	}

	//The control the sheet parked a window's ring on belongs to THAT window: a
	//second window's sheet never sees it, and it goes with the window that
	//closes.
	[AvaloniaFact]
	public void The_parked_header_control_belongs_to_its_window_and_goes_with_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string tetris = Path.Combine(_folder, "Tetris (World).gb");

		LibraryScanResult InstantScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry[] all = { Entry(contra, RomConsole.Nes, "Contra"), Entry(tetris, RomConsole.GameBoy, "Tetris") };
			onBatch(all);
			return new LibraryScanResult(all, 1, false);
		}

		(MainWindow first, MainWindowViewModel firstModel) = OpenLibrary(InstantScan, inline: true);
		WaitFor(() => FocusedTilePath(first) == contra, $"the sheet did not open on its first game ({Focused(first)})");
		Press(first, PadNavAction.Right);
		Assert.Equal(tetris, FocusedTilePath(first));
		Press(first, PadNavAction.Back);
		Pump();

		using ManualResetEventSlim release = new(false);
		LibraryScanResult HeldScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry entry = Entry(contra, RomConsole.Nes, "Contra");
			onBatch(new[] { entry });
			release.Wait(TimeSpan.FromSeconds(30));
			return new LibraryScanResult(new[] { entry }, 1, false);
		}

		firstModel.RomPicker.LibraryScanStreamSource = HeldScan;
		firstModel.RomPicker.RunLibraryScanInline = false;

		try {
			//Opened BEFORE the first window parks, so its own claim cannot be what
			//leaves it with nothing parked.
			(MainWindow second, _) = OpenLibrary(HeldScan, inline: false);

			firstModel.RomPicker.Open();
			Pump();
			WaitFor(() => firstModel.RomPicker.Tiles.Count == 1, $"the first batch never reached the grid ({Focused(first)})");
			Control? parked = PlayPadNavigationWiring.RomPickerParkedForTest(first);
			Assert.True(parked is not null && parked.Name == "RomPickerBack", "the first window's sheet did not park the ring on Back");

			//A second window that parked nothing has nothing parked.
			Assert.Null(PlayPadNavigationWiring.RomPickerParkedForTest(second));

			//And closing the first leaves nothing of its parking behind.
			first.ReleaseCore = () => { };
			first.Close();
			Pump();
			Assert.Null(PlayPadNavigationWiring.RomPickerParkedForTest(first));
		} finally {
			release.Set();
		}
	}

	//A scan still walking when the player closes the sheet and reopens it on a
	//library that has no folder any more (the drive was unplugged): the old
	//walk's finish is for a generation that is gone, so the wait must be ended
	//by the reopen itself or the bar spins over the empty state forever.
	[AvaloniaFact]
	public void Reopening_with_no_library_folder_ends_the_wait_of_the_scan_in_flight()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");

		using ManualResetEventSlim release = new(false);
		LibraryScanResult HeldScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry entry = Entry(contra, RomConsole.Nes, "Contra");
			onBatch(new[] { entry });
			release.Wait(TimeSpan.FromSeconds(30));
			return new LibraryScanResult(new[] { entry }, 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(HeldScan, inline: false);

		try {
			WaitFor(() => model.RomPicker.Tiles.Count == 1, $"the first batch never reached the grid ({Focused(window)})");
			Assert.True(model.RomPicker.IsScanning, "the scan is over, so this case proves nothing about the wait");

			Press(window, PadNavAction.Back);
			Pump();
			Assert.False(model.RomPicker.IsVisible, "B did not close the sheet");

			model.RomPicker.LibraryFolderSource = () => Array.Empty<string>();
			Press(window, PadNavAction.Confirm);
			Pump();
			Assert.True(model.RomPicker.IsVisible, "the pad's Confirm did not reopen the sheet");

			Assert.False(model.RomPicker.IsScanning, "a library with no folder is still waiting on a scan");
			Assert.False(window.FindNamed<ProgressBar>("RomPickerScanProgress").IsOnScreen(),
				"the scan's indicator spins over a library with no folder");

			//The old walk finishing late changes nothing on the new surface.
			release.Set();
			Pump();
			Assert.False(model.RomPicker.IsScanning);
		} finally {
			release.Set();
		}
	}

	//#1037 (ADR-0264 Decision 9) with Decision 3's amendment: the sheet's header
	//stays reachable by pad while the scan runs. Reaching it is worth nothing if
	//the end of the scan undoes it - a player who walked up to *Browse a file…*
	//while the library was being listed must still be there when it stops.
	[AvaloniaFact]
	public void The_end_of_a_scan_leaves_the_ring_where_the_player_put_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");

		using ManualResetEventSlim release = new(false);
		LibraryScanResult HeldScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry entry = Entry(contra, RomConsole.Nes, "Contra");
			onBatch(new[] { entry });
			release.Wait(TimeSpan.FromSeconds(30));
			return new LibraryScanResult(new[] { entry }, 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(HeldScan, inline: false);

		try {
			WaitFor(() => FocusedTilePath(window) == contra, $"the ring never landed on the first game ({Focused(window)})");
			Press(window, PadNavAction.Up);
			WaitFor(() => InHeader(window), $"Up from the grid's top row did not reach the header ({Focused(window)})");

			//The walk ends with the ring in the header, and it stays there: the
			//scan's last breath is not a claim over the player's ring.
			release.Set();
			WaitFor(() => !model.RomPicker.IsScanning, "the scan's wait never cleared");
			Pump();
			Assert.True(InHeader(window),
				$"the end of the scan pulled the ring out of the header ({Focused(window)})");
		} finally {
			release.Set();
		}
	}

	//#1037: a walk that throws is not a library with nothing in it. The tiles the
	//batches already put on screen stay, so a header counting "0 games in 0
	//folders" over them is a number the player can see is false.
	[AvaloniaFact]
	public void A_scan_that_throws_does_not_count_zero_over_the_games_it_showed()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");

		LibraryScanResult BrokenScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			onBatch(new[] { Entry(contra, RomConsole.Nes, "Contra") });
			throw new IOException("the library folder went away mid-scan");
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(BrokenScan, inline: true);

		Assert.Single(model.RomPicker.Tiles);
		Assert.False(model.RomPicker.IsScanning, "a scan that threw left its wait on screen");
		//The header is ONE sentence now (the #1036 review fix, rebased over
		//#1037): a walk that threw leaves the title standing over the tiles it
		//already showed, which is still not the false zero this case is about.
		Assert.Equal("Your library", model.RomPicker.HeaderText);
		Assert.Equal("", model.RomPicker.TruncatedText);
		Assert.NotNull(window);
	}

	//#1037 (ADR-0264 Decision 9): the scan is BOUNDED. A player who leaves the
	//sheet and comes back starts the scan they are looking at; the walk they
	//superseded must stop reading the disk there rather than run a tree nobody is
	//showing to its end - which on a library pointed at a whole disk is minutes
	//of reads per reopen.
	[AvaloniaFact]
	public void A_scan_the_player_left_stops_reading_the_library()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");

		int listings = 0;
		bool stop = false;
		LibraryScanResult EndlessScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			//Reads the library over and over, the way a walk over a large tree
			//does, until the walk is told to stop - by the exception the lister
			//raises, or by the case's own flag on a build that never raises it.
			while(!Volatile.Read(ref stop)) {
				list(_folder);
				Interlocked.Increment(ref listings);
				Thread.Sleep(5);
			}
			return new LibraryScanResult(Array.Empty<LibraryEntry>(), 0, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(EndlessScan, inline: false);

		try {
			WaitFor(() => Volatile.Read(ref listings) > 3, "the first scan never started reading the library");
			Press(window, PadNavAction.Back);
			Pump();
			Assert.False(model.RomPicker.IsVisible, "B did not close the sheet");

			//The same door back in, with a scan that answers at once.
			LibraryScanResult InstantScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
			{
				LibraryEntry entry = Entry(contra, RomConsole.Nes, "Contra");
				onBatch(new[] { entry });
				return new LibraryScanResult(new[] { entry }, 1, false);
			}
			model.RomPicker.LibraryScanStreamSource = InstantScan;
			model.RomPicker.RunLibraryScanInline = true;
			Press(window, PadNavAction.Confirm);
			Pump();
			WaitFor(() => model.RomPicker.Tiles.Count == 1, "the scan the player is looking at never answered");

			//The superseded walk is given time to prove it is still running: a
			//cancelled one never asks the disk for another folder, so the count
			//it left behind is the count it ended on.
			int afterReopen = Volatile.Read(ref listings);
			Thread.Sleep(300);
			Pump();
			Assert.Equal(afterReopen, Volatile.Read(ref listings));
		} finally {
			Volatile.Write(ref stop, true);
			Pump();
		}
	}

	//#1037 (ADR-0264 Decision 9): the sheet "stays responsive" while the grid
	//fills. One folder holding thousands of ROMs is ONE batch, and a turn that
	//takes every one of them into the grid is a turn the player spends watching
	//a frozen sheet. What the case measures is what a turn costs: the first one
	//takes part of the batch, and the rest arrive over the turns that follow.
	[AvaloniaFact]
	public void A_single_huge_batch_reaches_the_grid_over_several_turns()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		const int Total = 1200;

		LibraryEntry[] huge = Enumerable.Range(0, Total)
			.Select(i => Entry(Path.Combine(_folder, $"Game {i:D4}.nes"), RomConsole.Nes, $"Game {i:D4}"))
			.ToArray();

		LibraryScanResult HugeScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			onBatch(huge);
			return new LibraryScanResult(huge, 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(HugeScan, inline: true, pumpAfterOpen: false);

		//The open's own turn is over the moment Confirm returns, and it did not
		//take the whole folder: a turn that does is the stall this case exists to
		//catch, whatever the number of tiles it ends up with.
		Assert.True(model.RomPicker.Tiles.Count < Total,
			$"one UI turn took all {Total} of a single folder's games into the grid");

		//And none of them are lost or left in the wrong place by it: the grid is
		//the whole batch, in the module's own order (Decision 1).
		WaitFor(() => model.RomPicker.Tiles.Count == Total, "the rest of the folder never reached the grid");
		Assert.Equal("Game 0000", model.RomPicker.Tiles[0].Title);
		Assert.Equal("Game 1199", model.RomPicker.Tiles[Total - 1].Title);
		Assert.NotNull(window);
	}

	//#1037 review finding 1 on #1056: the scan is not over while a chunk of its
	//last batch is still on its way. The game the player left on sits PAST the
	//first chunk of one big folder, so a finish that ran ahead of the chunks would
	//stop the indicator over a half-filled grid and hand the ring to the first
	//tile - whose focus report then overwrites the very path being restored.
	[AvaloniaFact]
	public void The_scan_is_not_over_until_the_last_chunk_of_a_huge_batch_has_landed()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		const int Total = 1200;
		const int Remembered = 900;

		LibraryEntry[] huge = Enumerable.Range(0, Total)
			.Select(i => Entry(Path.Combine(_folder, $"Game {i:D4}.nes"), RomConsole.Nes, $"Game {i:D4}"))
			.ToArray();
		string remembered = huge[Remembered].Path;

		LibraryScanResult Scan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			onBatch(huge);
			return new LibraryScanResult(huge, 1, false);
		}

		//The first visit knows one game only: the one the player ends up on.
		LibraryScanResult OneGame(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			onBatch(new[] { huge[Remembered] });
			return new LibraryScanResult(new[] { huge[Remembered] }, 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(OneGame, inline: true);
		WaitFor(() => FocusedTilePath(window) == remembered, $"the sheet did not open on its one game ({Focused(window)})");
		Press(window, PadNavAction.Back);
		Pump();
		Assert.False(model.RomPicker.IsVisible, "B did not close the sheet");
		Assert.Equal(remembered, model.RomPicker.LastFocusedTilePath);

		model.RomPicker.LibraryScanStreamSource = Scan;
		model.RomPicker.RunLibraryScanInline = true;
		PressOnly(window, PadNavAction.Confirm);

		//The open's own turn is over and the first chunk is in; the rest is not.
		Assert.True(model.RomPicker.Tiles.Count < Total, "one UI turn took the whole folder");
		Assert.True(model.RomPicker.IsScanning, "the scan reported itself over with chunks still on the way");
		Assert.Equal(remembered, model.RomPicker.LastFocusedTilePath);

		WaitFor(() => model.RomPicker.Tiles.Count == Total, "the rest of the folder never reached the grid");
		WaitFor(() => !model.RomPicker.IsScanning, "the scan's wait never cleared");
		Assert.Equal(remembered, model.RomPicker.LastFocusedTilePath);
		WaitFor(() => FocusedTilePath(window) == remembered,
			$"the sheet reopened on {Focused(window)} instead of the game the player left on");
	}

	//#1037 review finding 2 on #1056: a restore that never lands - the remembered
	//file is gone - ends with the sheet owing the player a game under the ring,
	//but only when the ring is still where the sheet itself parked it. A player who
	//walked it to another header control meanwhile keeps it there.
	[AvaloniaFact]
	public void A_restore_that_never_lands_does_not_pull_the_ring_off_a_header_control_the_player_chose()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string metroid = Path.Combine(_folder, "Metroid (USA).nes");
		string tetris = Path.Combine(_folder, "Tetris (World).gb");

		LibraryScanResult ThreeGames(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry[] all = {
				Entry(contra, RomConsole.Nes, "Contra"),
				Entry(metroid, RomConsole.Nes, "Metroid"),
				Entry(tetris, RomConsole.GameBoy, "Tetris")
			};
			onBatch(all);
			return new LibraryScanResult(all, 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(ThreeGames, inline: true);
		WaitFor(() => FocusedTilePath(window) == contra, $"the sheet did not open on its first game ({Focused(window)})");
		Press(window, PadNavAction.Right);
		Press(window, PadNavAction.Right);
		Assert.Equal(tetris, FocusedTilePath(window));
		Press(window, PadNavAction.Back);
		Pump();
		Assert.False(model.RomPicker.IsVisible, "B did not close the sheet");

		//The game is gone from the library: the restore will never land.
		using ManualResetEventSlim release = new(false);
		LibraryScanResult TwoGames(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry[] two = { Entry(contra, RomConsole.Nes, "Contra"), Entry(metroid, RomConsole.Nes, "Metroid") };
			onBatch(two);
			release.Wait(TimeSpan.FromSeconds(30));
			return new LibraryScanResult(two, 1, false);
		}
		model.RomPicker.LibraryScanStreamSource = TwoGames;
		model.RomPicker.RunLibraryScanInline = false;

		try {
			Press(window, PadNavAction.Confirm);
			Pump();
			WaitFor(() => model.RomPicker.Tiles.Count == 2, "the held scan never showed its games");
			WaitFor(() => InHeader(window), $"the ring did not wait in the header ({Focused(window)})");

			//The player walks the ring to Browse a file… (the sheet parked it on Back) and the scan ends under it.
			Button back = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "RomPickerBrowseFile");
			back.Focus(NavigationMethod.Directional);
			Pump();
			Assert.Equal("RomPickerBrowseFile", (window.FocusManager?.GetFocusedElement() as Control)?.Name);

			release.Set();
			WaitFor(() => !model.RomPicker.IsScanning, "the scan's wait never cleared");
			Pump();
			Assert.Equal("RomPickerBrowseFile", (window.FocusManager?.GetFocusedElement() as Control)?.Name);
		} finally {
			release.Set();
		}
	}

	//#1037 review finding 3 on #1056: a scan the player has LEFT stops reading the
	//library. Closing the sheet is leaving it - nobody has to come back for the
	//walk to stop, and a walk that unzips covers during a game is the cost.
	[AvaloniaFact]
	public void Closing_the_sheet_stops_the_scan_without_a_reopen()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		int listings = 0;
		bool stop = false;
		LibraryScanResult EndlessScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			while(!Volatile.Read(ref stop)) {
				list(_folder);
				Interlocked.Increment(ref listings);
				Thread.Sleep(5);
			}
			return new LibraryScanResult(Array.Empty<LibraryEntry>(), 0, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(EndlessScan, inline: false);

		try {
			WaitFor(() => Volatile.Read(ref listings) > 3, "the scan never started reading the library");
			Press(window, PadNavAction.Back);
			Pump();
			Assert.False(model.RomPicker.IsVisible, "B did not close the sheet");

			//Nobody reopens it. The walk must still stop at its next listing.
			Thread.Sleep(100);
			int afterClose = Volatile.Read(ref listings);
			Thread.Sleep(300);
			Pump();
			Assert.Equal(afterClose, Volatile.Read(ref listings));
		} finally {
			Volatile.Write(ref stop, true);
			Pump();
		}
	}

	//#1037: a scan that a newer one replaced describes nothing. Its own answer
	//belongs to a grid that no longer exists, so the header keeps the count of
	//the scan the player is actually looking at even when the older walk finishes
	//last.
	[AvaloniaFact]
	public void A_superseded_scan_does_not_describe_the_header()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string metroid = Path.Combine(_folder, "Metroid (USA).nes");
		string tetris = Path.Combine(_folder, "Tetris (World).gb");

		using ManualResetEventSlim release = new(false);
		LibraryScanResult HeldScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry entry = Entry(contra, RomConsole.Nes, "Contra");
			onBatch(new[] { entry });
			release.Wait(TimeSpan.FromSeconds(30));
			return new LibraryScanResult(new[] { entry }, 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(HeldScan, inline: false);

		try {
			WaitFor(() => model.RomPicker.Tiles.Count == 1, "the held scan never showed its first game");
			Press(window, PadNavAction.Back);
			Pump();

			LibraryScanResult InstantScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
			{
				LibraryEntry[] all = {
					Entry(contra, RomConsole.Nes, "Contra"),
					Entry(metroid, RomConsole.Nes, "Metroid"),
					Entry(tetris, RomConsole.GameBoy, "Tetris")
				};
				onBatch(all);
				return new LibraryScanResult(all, 2, false);
			}
			model.RomPicker.LibraryScanStreamSource = InstantScan;
			model.RomPicker.RunLibraryScanInline = true;
			Press(window, PadNavAction.Confirm);
			Pump();
			Assert.Contains("3 games", model.RomPicker.HeaderText);

			//The old walk finishes last, and the header is not its to write.
			release.Set();
			Thread.Sleep(200);
			Pump();
			Assert.Contains("3 games", model.RomPicker.HeaderText);
			Assert.Contains("2 folders", model.RomPicker.HeaderText);
		} finally {
			release.Set();
		}
	}

	//#1037 review finding 1 on #1056: the remembered game landing is a claim for
	//the ring, and like the end of a scan it is the sheet finishing what it
	//promised - not a hand taken off a ring the player has since moved. A player
	//who walked to Browse a file… while the restore was pending keeps it there.
	[AvaloniaFact]
	public void The_remembered_game_landing_does_not_steal_a_ring_the_player_put_on_Browse_a_file()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string metroid = Path.Combine(_folder, "Metroid (USA).nes");
		string tetris = Path.Combine(_folder, "Tetris (World).gb");

		LibraryScanResult InstantScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry[] all = {
				Entry(contra, RomConsole.Nes, "Contra"),
				Entry(metroid, RomConsole.Nes, "Metroid"),
				Entry(tetris, RomConsole.GameBoy, "Tetris")
			};
			onBatch(all);
			return new LibraryScanResult(all, 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(InstantScan, inline: true);
		WaitFor(() => FocusedTilePath(window) == contra, $"the sheet did not open on its first game ({Focused(window)})");
		Press(window, PadNavAction.Right);
		Press(window, PadNavAction.Right);
		Assert.Equal(tetris, FocusedTilePath(window));
		Press(window, PadNavAction.Back);
		Pump();
		Assert.False(model.RomPicker.IsVisible, "B did not close the sheet");

		using ManualResetEventSlim release = new(false);
		LibraryScanResult SlowScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry[] first = { Entry(contra, RomConsole.Nes, "Contra"), Entry(metroid, RomConsole.Nes, "Metroid") };
			onBatch(first);
			release.Wait(TimeSpan.FromSeconds(30));
			LibraryEntry last = Entry(tetris, RomConsole.GameBoy, "Tetris");
			onBatch(new[] { last });
			return new LibraryScanResult(new[] { first[0], first[1], last }, 1, false);
		}
		model.RomPicker.LibraryScanStreamSource = SlowScan;
		model.RomPicker.RunLibraryScanInline = false;

		try {
			Press(window, PadNavAction.Confirm);
			Pump();
			WaitFor(() => model.RomPicker.Tiles.Count == 2, $"the first batch never reached the grid ({Focused(window)})");

			//The restore is pending and the player walks the ring to Browse a file… (the sheet parked it on Back).
			Button back = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "RomPickerBrowseFile");
			back.Focus(NavigationMethod.Directional);
			Pump();
			Assert.Equal("RomPickerBrowseFile", (window.FocusManager?.GetFocusedElement() as Control)?.Name);

			//The remembered game arrives under it.
			release.Set();
			WaitFor(() => model.RomPicker.Tiles.Count == 3, "the last batch never reached the grid");
			WaitFor(() => !model.RomPicker.IsScanning, "the scan's wait never cleared");
			Pump();
			Assert.Equal("RomPickerBrowseFile", (window.FocusManager?.GetFocusedElement() as Control)?.Name);
		} finally {
			release.Set();
		}
	}

	//#1037 review finding 2 on #1056: the library has not answered while the
	//scan runs, so a query typed over the wait must not read as a verdict on it.
	[AvaloniaFact]
	public void A_query_typed_while_the_scan_runs_is_not_answered_with_no_match()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		using ManualResetEventSlim release = new(false);
		LibraryScanResult HeldScan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			release.Wait(TimeSpan.FromSeconds(30));
			return new LibraryScanResult(Array.Empty<LibraryEntry>(), 1, false);
		}

		(MainWindow window, MainWindowViewModel model) = OpenLibrary(HeldScan, inline: false);

		try {
			Assert.True(model.RomPicker.IsScanning, "the scan is over, so this case proves nothing about the wait");
			string waiting = model.RomPicker.SearchingText;
			Assert.NotEqual("", waiting);

			model.RomPicker.SearchQuery = "zzzz";
			Pump();
			Assert.Equal("", model.RomPicker.EmptyText);
			Assert.Equal(waiting, model.RomPicker.SearchingText);

			model.RomPicker.SearchQuery = "";
			Pump();
			Assert.Equal(waiting, model.RomPicker.SearchingText);

			//Once the scan answers, the query is judged against a library that was read.
			model.RomPicker.SearchQuery = "zzzz";
			release.Set();
			WaitFor(() => !model.RomPicker.IsScanning, "the scan's wait never cleared");
			Pump();
			Assert.Contains("zzzz", model.RomPicker.EmptyText);
			Assert.NotNull(window);
		} finally {
			release.Set();
		}
	}
}
