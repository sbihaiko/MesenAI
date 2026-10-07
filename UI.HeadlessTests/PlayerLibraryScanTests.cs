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
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(Mapping, action) }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
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
		bool inline)
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
		Press(window, PadNavAction.Confirm);
		Pump();

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
			Assert.Contains("3 games", model.RomPicker.CountText);
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
}
