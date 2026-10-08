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
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1036 (ADR-0264 Decision 8): *Library folders…* is the sheet's own
//pad-reachable list of the folders "Your library" scans, and this is the wiring
//half - the pad drives it end to end. The rules (which folder is one folder, the
//absorb-don't-reject nesting rule, the union, the header's literal) are host-free
//in UI.Tests/Play/LibraryFoldersTests; what is proved here is that the sheet the
//header opens lists the folders, that a pad adds one through the sheet's own
//folder browser and takes one out again, that both land on the grid and on the
//header, and that removing never touches the disk.
//
//The scan is deliberately NOT stubbed here, unlike the flat-library cases: these
//drive the real remembered list over a real temp tree, which is the only way to
//prove the clause the issue spells out - that removing a folder deletes nothing
//on the player's disk. What IS pinned is the two seams that would otherwise reach
//a machine this suite is not running on: the mounted volumes (a temp folder
//stands in for one, so the pad has somewhere to walk to) and the native folder
//dialog (the mouse case answers it without a dialog owning the screen).
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryFoldersTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;
	private readonly List<string>? _libraryFolders = ConfigManager.Config.Preferences.LibraryFolders;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1036-" + Guid.NewGuid().ToString("N"));

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

	public PlayerLibraryFoldersTests()
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
		prefs.LibraryFolders = _libraryFolders;
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

	private static Control? Focused(MainWindow window) => window.FocusManager?.GetFocusedElement() as Control;

	private static string FocusedWhat(MainWindow window)
	{
		Control? focused = Focused(window);
		return focused is null ? "focus=<none>" : $"focus={focused.GetType().Name}#{focused.Name} dc={focused.DataContext?.GetType().Name}";
	}

	private static string FocusedName(MainWindow window) => Focused(window)?.Name ?? "";

	private static PlayerRomPickerRow? FocusedRow(MainWindow window) => Focused(window)?.DataContext as PlayerRomPickerRow;

	//A library of TWO folders on disk: `games` is the configured one (and so the
	//list's seeded first row), `extra` holds a game the library does not show yet -
	//the folder the pad adds below. Both keep their ROM on disk for the whole case,
	//which is what the remove case asserts afterwards.
	private (string Games, string Extra, string ExtraRom) LibraryRoots()
	{
		string games = Path.Combine(_folder, "games");
		string extra = Path.Combine(_folder, "extra");
		Directory.CreateDirectory(games);
		Directory.CreateDirectory(extra);
		File.WriteAllBytes(Path.Combine(games, "Contra (U) [!].nes"), SyntheticNrom.Build());
		string extraRom = Path.Combine(extra, "Tetris (World) (Rev A).gb");
		File.WriteAllBytes(extraRom, SyntheticNrom.Build());

		ConfigManager.Config.Preferences.GameFolder = games;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		//First run: the list has never been seeded, which is the state Decision 8
		//seeds FROM the single games folder.
		ConfigManager.Config.Preferences.LibraryFolders = null;
		return (games, extra, extraRom);
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
		//The mounted volumes are the one root a headless machine cannot answer
		//usefully: the temp tree stands in, so the pad has a way to walk to the
		//folder this case adds.
		model.RomPicker.VolumeSource = () => new[] { _folder };
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		Assert.True(model.RecentGames.ShowFirstRunHome, "the home is not the first-run one, so this case would prove nothing");
		return (window, model);
	}

	//The pad's way into the sheet, whole: the home's one action opens the library,
	//Up reaches the header, Left reaches *Library folders…*, and Confirm opens it.
	//Every case below starts here, so the steps are written once and the cases
	//differ only in what they do once the sheet is up.
	private void OpenFoldersSheetWithPad(MainWindow window, MainWindowViewModel model)
	{
		WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary", "the first-run home did not put the focus on its one action");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.Mode == RomPickerMode.Library, "the pad's Confirm did not open the library");
		WaitFor(() => model.RomPicker.Tiles.Count > 0 || model.RomPicker.EmptyText.Length > 0, "the library never settled");

		Press(window, PadNavAction.Up);
		WaitFor(() => FocusedName(window) == "RomPickerBrowseFile",
			$"Up from the top grid row did not reach the header ({FocusedWhat(window)})");

		Press(window, PadNavAction.Left);
		WaitFor(() => FocusedName(window) == "RomPickerLibraryFolders",
			$"Left from *Browse a file…* did not reach *Library folders…* ({FocusedWhat(window)})");

		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.IsFoldersSheetVisible, "*Library folders…* did not open the sheet");
		WaitFor(() => FocusedName(window) == "RomPickerAddFolder",
			$"the folders sheet did not put the ring on its own action ({FocusedWhat(window)})");
	}

	//Presses Down until the focused row answers `match`. The rows are rebuilt by
	//every step, so the ring is where the previous step left it and the walk is
	//counted in presses rather than in indexes.
	private void WalkToRow(MainWindow window, Func<PlayerRomPickerRow, bool> match, string what)
	{
		for(int i = 0; i < 30; i++) {
			PlayerRomPickerRow? row = FocusedRow(window);
			if(row is not null && match(row)) {
				return;
			}
			Press(window, PadNavAction.Down);
		}
		throw new XunitException($"the ring never reached {what} ({FocusedWhat(window)})");
	}

	//#1036 (ADR-0264 Decision 8): the pad ADDS a folder through the sheet's own
	//folder browser, and the answer lands where the player can see it - the list
	//grows a row, the grid gains the games under it, and the header counts the
	//folder. This is the case the issue asks for, start to finish.
	[AvaloniaFact]
	public void The_pad_adds_a_folder_through_the_browser_and_the_header_counts_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(string games, string extra, _) = LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);

		//The list is the seeded one: the single games folder the app already had.
		Assert.Equal(new[] { games }, model.RomPicker.LibraryFolderRows.Select(r => r.Path).ToArray());
		Assert.Contains("1 folder", model.RomPicker.HeaderText);

		//Confirm on *Add a folder…* is the PAD's door: the sheet's own browser.
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.Mode == RomPickerMode.BrowseFile && model.RomPicker.IsPickingLibraryFolder,
			"the pad's add did not open the folder browser");
		Assert.False(model.RomPicker.IsFoldersSheetVisible);

		//Walk to the temp root (the stand-in volume) and descend into it.
		WaitFor(() => FocusedRow(window) is not null, $"the browser opened off its list ({FocusedWhat(window)})");
		WalkToRow(window, row => row.Path == _folder, "the volume root");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.PathText.Length > 0, "Confirm did not descend into the volume root");

		WalkToRow(window, row => row.Label == "extra", "the folder to add");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.Rows.Any(r => r.Kind == RomPickerRowKind.Action && r.Path == extra),
			"Confirm did not descend into the folder to add");

		//Inside it, the action row says what it will do and the ring is kept OFF it
		//on the descend (the guard): it takes a deliberate Up to reach a press that
		//changes the library.
		PlayerRomPickerRow action = model.RomPicker.Rows.First(r => r.Kind == RomPickerRowKind.Action);
		Assert.Contains("Add this folder", action.Label);
		Assert.Equal(RomPickerRowKind.Game, FocusedRow(window)?.Kind);

		Press(window, PadNavAction.Up);
		WaitFor(() => FocusedRow(window)?.Kind == RomPickerRowKind.Action,
			$"Up did not reach the action row that adds the folder ({FocusedWhat(window)})");
		Press(window, PadNavAction.Confirm);

		//The add lands on the list, on the grid and on the header at once.
		WaitFor(() => model.RomPicker.IsFoldersSheetVisible && model.RomPicker.LibraryFolderRows.Count == 2,
			"the pick did not come back to the folders sheet with the folder added");
		Assert.Contains(extra, model.RomPicker.LibraryFolderRows.Select(r => r.Path));
		Assert.False(model.RomPicker.IsPickingLibraryFolder);
		WaitFor(() => model.RomPicker.Tiles.Count == 2, "the grid did not gain the games under the added folder");
		Assert.Equal("Your library · 2 games in 2 folders", model.RomPicker.HeaderText);
		//The browser's path line went with the browser: nothing of the folder the
		//pad walked into is left over the folders sheet.
		Assert.Equal("", model.RomPicker.PathText);
		Assert.Equal("", model.RomPicker.NoticeText);
		Assert.False(window.FindNamed<TextBlock>("RomPickerPath").IsOnScreen(), "the browser's path is still printed over the sheet");
	}

	//#1036 (ADR-0264 Decision 8): the pad REMOVES a folder, and removing it is a
	//list edit and nothing else - the game under it is still on the disk, which is
	//the clause the issue spells out.
	[AvaloniaFact]
	public void The_pad_removes_a_folder_and_deletes_nothing_on_disk()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(string games, _, string extraRom) = LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();

		//Both folders are in the library to begin with, so the removal takes one of
		//two rather than the only one - the state the list is really in.
		ConfigManager.Config.Preferences.LibraryFolders = new List<string> { games, Path.Combine(_folder, "extra") };
		OpenFoldersSheetWithPad(window, model);
		Assert.Equal(2, model.RomPicker.LibraryFolderRows.Count);
		WaitFor(() => model.RomPicker.Tiles.Count == 2, "the grid does not hold both folders' games to begin with");

		//Up out of the sheet's action reaches the rows, where the press is the one
		//that takes a folder out of the list.
		Press(window, PadNavAction.Up);
		WaitFor(() => FocusedName(window) == "RomPickerFolderRemove",
			$"Up from *Add a folder…* did not reach a row's Remove ({FocusedWhat(window)})");
		Press(window, PadNavAction.Confirm);

		WaitFor(() => model.RomPicker.LibraryFolderRows.Count == 1, "the press did not take the folder out of the list");
		//The grid lost that folder's game, and the header counts one folder.
		WaitFor(() => model.RomPicker.Tiles.Count == 1, "the grid did not lose the removed folder's game");
		Assert.Contains("1 folder", model.RomPicker.HeaderText);
		Assert.Contains("1 game", model.RomPicker.HeaderText);
		Assert.Contains("Nothing was deleted", model.RomPicker.FoldersNoticeText);

		//The whole point: the folder and its game are untouched on the disk.
		Assert.True(Directory.Exists(Path.Combine(_folder, "extra")), "removing a library folder removed the folder itself");
		Assert.True(File.Exists(extraRom), "removing a library folder deleted a game");
		Assert.True(File.Exists(Path.Combine(games, "Contra (U) [!].nes")), "removing a library folder deleted another folder's game");
	}

	//#1036 (ADR-0264 Decision 8): the MOUSE's door is the native folder dialog, and
	//it is the button's own press - the pad never reaches it, which is the other
	//half of the same decision. The dialog itself is the seam: a headless case
	//answers it with a path instead of a screen a pad could not drive.
	[AvaloniaFact]
	public void The_mouse_press_opens_the_native_folder_picker_and_adds_what_it_answers()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(_, string extra, _) = LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);

		bool asked = false;
		model.RomPicker.FolderPickerSource = () => {
			asked = true;
			return System.Threading.Tasks.Task.FromResult<string?>(extra);
		};

		Button add = window.FindNamed<Button>("RomPickerAddFolder");
		Assert.True(add.IsOnScreen(), "*Add a folder…* is not on the sheet");
		add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		WaitFor(() => asked, "the press did not open the native folder dialog");
		WaitFor(() => model.RomPicker.LibraryFolderRows.Count == 2, "the folder the dialog answered was not added");
		Assert.Contains(extra, model.RomPicker.LibraryFolderRows.Select(r => r.Path));
		//The mouse's door never walks the browser: the sheet stayed on its own list.
		Assert.True(model.RomPicker.IsFoldersSheetVisible);
		Assert.Equal(RomPickerMode.Library, model.RomPicker.Mode);
	}

	//#1036 review finding 4: the sheet can go while the native dialog is still up
	//(another ROM opened, the pad dropped). What the player picked is still theirs,
	//so the list is saved - but the notice and the re-scan are for a sheet nobody is
	//looking at.
	[AvaloniaFact]
	public void A_folder_picked_after_the_sheet_closed_is_saved_without_a_notice_or_a_rescan()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(_, string extra, _) = LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);

		System.Threading.Tasks.TaskCompletionSource<string?> dialog = new();
		model.RomPicker.FolderPickerSource = () => dialog.Task;
		window.FindNamed<Button>("RomPickerAddFolder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		model.RomPicker.CloseFoldersSheet();
		int rowsBefore = model.RomPicker.LibraryFolderRows.Count;
		string headerBefore = model.RomPicker.HeaderText;

		dialog.SetResult(extra);
		WaitFor(() => ConfigManager.Config.Preferences.LibraryFolders?.Contains(extra) == true,
			"the folder the dialog answered was not saved");
		for(int i = 0; i < 40; i++) { Pump(); Thread.Sleep(25); }
		Assert.Equal("", model.RomPicker.FoldersNoticeText);
		Assert.Equal(rowsBefore, model.RomPicker.LibraryFolderRows.Count);
		Assert.Equal(headerBefore, model.RomPicker.HeaderText);
	}

	//#1036 (ADR-0264 Decision 8): adding the SAME folder again changes nothing, and
	//the sheet SAYS so rather than leaving a press that appears to do nothing. A
	//folder nested inside a listed one is a root of its own (LibraryFolders.Add
	//keeps every root), so it is a third row.
	[AvaloniaFact]
	public void Adding_a_folder_already_listed_says_so_and_a_nested_one_is_its_own_row()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(string games, string extra, _) = LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);

		model.RomPicker.FolderPickerSource = () => System.Threading.Tasks.Task.FromResult<string?>(extra);
		window.FindNamed<Button>("RomPickerAddFolder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		WaitFor(() => model.RomPicker.LibraryFolderRows.Count == 2, "the first add did not land");

		//Now the same folder again - and then a folder INSIDE the one already listed.
		model.RomPicker.FolderPickerSource = () => System.Threading.Tasks.Task.FromResult<string?>(extra);
		window.FindNamed<Button>("RomPickerAddFolder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		WaitFor(() => model.RomPicker.FoldersNoticeText.Contains(extra + " is already in your library"),
			$"a folder already in the library was not answered as such ('{model.RomPicker.FoldersNoticeText}')");
		Assert.Equal(2, model.RomPicker.LibraryFolderRows.Count);

		model.RomPicker.FolderPickerSource = () => System.Threading.Tasks.Task.FromResult<string?>(Path.Combine(games, "NES"));
		window.FindNamed<Button>("RomPickerAddFolder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		WaitFor(() => model.RomPicker.LibraryFolderRows.Count == 3,
			$"a folder inside a listed one was not kept as its own root ('{model.RomPicker.FoldersNoticeText}')");
	}

	//#1036 (ADR-0264 Decision 8): B closes the folders sheet back to the library
	//and leaves the grid's ring on the header, so the sheet is reversible from the
	//pad alone - the stop rule ADR-0256 owns and ADR-0264 does not supersede.
	[AvaloniaFact]
	public void B_closes_the_folders_sheet_and_hands_the_ring_back_to_the_library()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);

		Press(window, PadNavAction.Back);
		WaitFor(() => !model.RomPicker.IsFoldersSheetVisible, "B did not close the folders sheet");
		WaitFor(() => model.RomPicker.IsLibrarySurfaceVisible, "the library surface did not come back");
		Assert.True(model.RomPicker.IsVisible, "B out of the folders sheet closed the whole picker");
		//The ring comes back to the library the sheet was over - a header control, or
		//one of the grid's tiles, whichever the arbiter lands it on - and what matters
		//here is that it is the library's and not a row of the sheet that just closed.
		WaitFor(() => FocusedName(window) is "RomPickerBrowseFile" or "RomPickerLibraryFolders" or "RomPickerBack" || Focused(window)?.DataContext is PlayerLibraryTile,
			$"the ring did not come back to the library surface ({FocusedWhat(window)})");

		//And B again is the dismiss of the picker itself: the sheet is not a trap.
		Press(window, PadNavAction.Back);
		WaitFor(() => !model.RomPicker.IsVisible, "B did not close the picker");
	}

	//#1036 (ADR-0264 Decision 8): the pick is a MODE of one open, and a mode that
	//outlives the open that armed it is a leak. A player deep in the pad's
	//add-folder walk confirms a game row; the game loads and the sheet closes -
	//but the pick stays armed, so the NEXT open offers *Add this folder to your
	//library* where *Make this my games folder* belongs, and a press that means
	//"name my games folder" pops the folders sheet instead.
	[AvaloniaFact]
	public void Confirming_a_game_row_during_a_pick_does_not_arm_the_next_open()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(string games, _, _) = LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);

		//*Add a folder…* arms the pick and opens the browser on the roots.
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.IsPickingLibraryFolder, "the pad's add did not open the folder browser");

		//The configured games folder is one of the roots, and the game under it is
		//the row this case confirms while the pick is armed.
		WalkToRow(window, row => row.Path == games, "the configured games folder");
		Press(window, PadNavAction.Confirm);
		WalkToRow(window, row => row.Kind == RomPickerRowKind.Game, "the game under the games folder");
		Press(window, PadNavAction.Confirm);

		//The game loads, which is the close path: the sheet goes, and the pick
		//goes with it rather than staying armed behind it.
		WaitFor(() => !model.RomPicker.IsVisible, "confirming a game row did not close the picker");
		Assert.False(model.RomPicker.IsPickingLibraryFolder,
			"the pick stayed armed after a game row closed the sheet - the next open offers *Add this folder…*");

		//The next open is the library's own: *Browse a file…* names the games
		//folder again, which is the affordance the leak got wrong.
		model.RomPicker.Open();
		model.RomPicker.BrowseFile();
		//The configured folder is already the games folder, so the browser offers
		//no row to name it again - armed, it offered *Add this folder to your
		//library* here instead.
		model.RomPicker.Choose(model.RomPicker.Rows.First(r => r.Path == games));
		Assert.DoesNotContain(model.RomPicker.Rows, r => r.Kind == RomPickerRowKind.Action);

		//And anywhere else the action row is the one that names the games folder,
		//not the one that adds a library folder.
		model.RomPicker.BrowseFile();
		model.RomPicker.Choose(model.RomPicker.Rows.First(r => r.Path == _folder));
		PlayerRomPickerRow action = model.RomPicker.Rows.First(r => r.Kind == RomPickerRowKind.Action);
		Assert.Contains("Make this my games folder", action.Label);

		if(EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitUntilStopped();
		}
	}

	//#1036 (ADR-0264 Decision 8) with ADR-0256's stop rule: B is a STEP, not a
	//dismiss. While the browser is up to pick a folder, B walks the same tree
	//backwards one folder at a time - it does not throw the whole walk away and
	//drop the player on *Library folders…* to start again. Only at the roots,
	//where there is nowhere left to ascend to, is B the cancel of the pick.
	[AvaloniaFact]
	public void B_while_picking_ascends_one_folder_and_cancels_only_at_the_roots()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(_, string extra, _) = LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.IsPickingLibraryFolder, "the pad's add did not open the folder browser");

		//Two folders deep: the temp volume, then a folder inside it that is nobody's
		//root. The game under that second folder is what says which of the two
		//folders is on screen, since a path line is a label and not a path.
		WalkToRow(window, row => row.Path == _folder, "the volume root");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.PathText.Length > 0, "Confirm did not descend into the volume root");
		WalkToRow(window, row => row.Path == extra, "the folder inside the volume");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.Rows.Any(r => r.Kind == RomPickerRowKind.Game),
			"Confirm did not descend into the folder inside the volume");

		//B here is one step up the walk, and the pick is still armed.
		Press(window, PadNavAction.Back);
		WaitFor(() => !model.RomPicker.Rows.Any(r => r.Kind == RomPickerRowKind.Game),
			"B did not ascend one folder while picking - the second folder is still on screen");
		Assert.Equal(_folder, model.RomPicker.Rows.First(r => r.Kind == RomPickerRowKind.Action).Path);
		Assert.True(model.RomPicker.IsPickingLibraryFolder, "B cancelled the pick instead of ascending one folder");
		Assert.False(model.RomPicker.IsFoldersSheetVisible, "B out of a folder popped the folders sheet up mid-walk");

		//B at the volume root steps back to the roots list, still picking.
		Press(window, PadNavAction.Back);
		WaitFor(() => model.RomPicker.PathText.Length == 0, "B at the volume root did not step back to the roots list");
		Assert.True(model.RomPicker.IsPickingLibraryFolder, "B at a root cancelled the pick");

		//And only there - nowhere left to ascend to - does B cancel it.
		Press(window, PadNavAction.Back);
		WaitFor(() => model.RomPicker.IsFoldersSheetVisible, "B at the roots did not cancel the pick back to the folders sheet");
		Assert.False(model.RomPicker.IsPickingLibraryFolder);
		Assert.Equal(RomPickerMode.Library, model.RomPicker.Mode);
	}

	//#1036 (ADR-0264 Decision 8): the named empty state names the NEXT STEP, and
	//after this slice the step is the folders sheet's own *Add a folder…* rather
	//than the browser's *Make this my games folder*. And an emptied library STAYS
	//emptied: the seed is the single games folder the app already had, put there
	//once for a player who never had a library - not a folder that comes back
	//every time the player takes the last one out.
	[AvaloniaFact]
	public void Removing_the_last_folder_names_the_add_folder_step_and_does_not_reseed()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);
		Assert.Single(model.RomPicker.LibraryFolderRows);
		Assert.Contains("1 folder", model.RomPicker.HeaderText);

		//Up out of *Add a folder…* reaches the rows, where the press is Remove.
		Press(window, PadNavAction.Up);
		WaitFor(() => FocusedName(window) == "RomPickerFolderRemove",
			$"Up from *Add a folder…* did not reach a row's Remove ({FocusedWhat(window)})");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.LibraryFolderRows.Count == 0, "the press did not take the last folder out of the list");

		//The grid is the named empty state, and the state names the step that now
		//exists: the folders sheet's own *Add a folder…*.
		Assert.Empty(model.RomPicker.Tiles);
		Assert.Contains("Library folders", model.RomPicker.EmptyText);
		Assert.Contains("Add a folder", model.RomPicker.EmptyText);

		//The games folder the seed came from is NOT put back on the next open.
		model.RomPicker.Hide();
		model.RomPicker.Open();
		WaitFor(() => model.RomPicker.IsVisible, "the picker did not reopen");
		Assert.Empty(model.RomPicker.Tiles);
		Assert.Contains("Add a folder", model.RomPicker.EmptyText);
	}

	//#1036: with no folder left, typing in the search box must not bring the
	//removed folder's games back from the scan that preceded the edit, and the
	//named empty state must stay.
	[AvaloniaFact]
	public void Typing_a_query_after_removing_the_last_folder_keeps_the_grid_empty()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);
		WaitFor(() => model.RomPicker.Tiles.Count == 1, "the grid does not hold the seeded folder's game to begin with");

		model.RomPicker.RemoveLibraryFolder(model.RomPicker.LibraryFolderRows.Single());
		Assert.Empty(model.RomPicker.Tiles);

		model.RomPicker.SearchQuery = "Contra";
		Pump();
		Assert.Empty(model.RomPicker.Tiles);
		Assert.Contains("Add a folder", model.RomPicker.EmptyText);

		model.RomPicker.SearchQuery = "";
		Pump();
		Assert.Empty(model.RomPicker.Tiles);
		Assert.Contains("Add a folder", model.RomPicker.EmptyText);
	}

	//#1036: while the rescan that follows a remove is still running, a query
	//must not draw the games of the folder that just left the list.
	[AvaloniaFact]
	public void A_query_typed_during_the_rescan_does_not_draw_the_removed_folders_games()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(string games, string extra, _) = LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		ConfigManager.Config.Preferences.LibraryFolders = new List<string> { games, extra };
		OpenFoldersSheetWithPad(window, model);
		WaitFor(() => model.RomPicker.Tiles.Count == 2, "the grid does not hold both folders' games to begin with");

		//The rescan goes off the UI thread now, so the removal returns with the
		//scan still in flight and the stale result is what the query would read.
		model.RomPicker.RunLibraryScanInline = false;
		PlayerLibraryFolderRow removed = model.RomPicker.LibraryFolderRows.Single(row => row.Path == extra);
		model.RomPicker.RemoveLibraryFolder(removed);

		model.RomPicker.SearchQuery = "Tetris";
		Assert.Empty(model.RomPicker.Tiles);
	}

	//#1036 (ADR-0264 Decision 8): LibraryFolderSource is a SEAM - a caller or a
	//test puts its own list there - and an edit must not overwrite it. Every add
	//and every remove used to reassign it, so whatever a caller injected was gone
	//after the first edit and the next open read the config instead; the seam
	//stays where it was put, and what the edit refreshes is the list the sheet is
	//showing, straight from the preference it just wrote.
	[AvaloniaFact]
	public void An_edit_leaves_an_injected_folder_source_alone()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(string games, _, _) = LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);

		//A caller's own source, injected the way another workspace or a test would.
		Func<IReadOnlyList<string>> injected = () => new List<string> { games };
		model.RomPicker.LibraryFolderSource = injected;

		Press(window, PadNavAction.Up);
		WaitFor(() => FocusedName(window) == "RomPickerFolderRemove",
			$"Up from *Add a folder…* did not reach a row's Remove ({FocusedWhat(window)})");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.LibraryFolderRows.Count == 0, "the remove did not land");

		//The seam is still the caller's - the edit left it alone - and the list
		//that landed is the one the preference now holds.
		Assert.Same(injected, model.RomPicker.LibraryFolderSource);
		Assert.Empty(ConfigManager.Config.Preferences.LibraryFolders!);
	}

	//The empty state of a library that HAS a stored list. Once the list is stored,
	//*Make this my games folder* no longer changes the library, so the sentence
	//must name the step that does: *Library folders…* → *Add a folder…*.
	[AvaloniaFact]
	public void A_listed_folder_with_no_game_names_the_add_folder_step()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string empty = Path.Combine(_folder, "empty");
		Directory.CreateDirectory(empty);
		File.WriteAllText(Path.Combine(empty, "notes.txt"), "not a game");
		ConfigManager.Config.Preferences.LibraryFolders = new List<string> { empty };

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomPicker.Open();
		WaitFor(() => model.RomPicker.IsVisible && model.RomPicker.EmptyText.Length > 0, "the library never answered the empty folder");

		Assert.Empty(model.RomPicker.Tiles);
		Assert.Contains("Library folders", model.RomPicker.EmptyText);
		Assert.Contains("Add a folder", model.RomPicker.EmptyText);
		Assert.DoesNotContain("Make this my games folder", model.RomPicker.EmptyText);
		Assert.DoesNotContain("Browse a file", model.RomPicker.EmptyText);
	}

	//Decision 8 "seeds the list on first run": the seed is persisted by the first
	//open, so the list is the same list from then on and a later change to the
	//single games folder does not reach it.
	[AvaloniaFact]
	public void The_first_open_persists_the_seed_and_a_later_games_folder_change_does_not_alter_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(string games, string extra, _) = LibraryRoots();
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		Assert.Null(prefs.LibraryFolders);

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomPicker.Open();
		WaitFor(() => model.RomPicker.IsVisible, "the picker did not open");

		Assert.NotNull(prefs.LibraryFolders);
		string seeded = Assert.Single(prefs.LibraryFolders!);
		Assert.Equal(Path.GetFullPath(games), Path.GetFullPath(seeded));

		prefs.GameFolder = extra;
		model.RomPicker.Hide();
		model.RomPicker.Open();
		WaitFor(() => model.RomPicker.IsVisible, "the picker did not reopen");

		Assert.Equal(new[] { seeded }, prefs.LibraryFolders!);
	}

	//Removing the last folder empties the grid through ClearTiles (#1035), so the
	//cover bitmaps the grid held are handed back with it.
	[AvaloniaFact]
	public void Removing_the_last_folder_releases_the_cover_art_the_grid_held()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoots();

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		OpenFoldersSheetWithPad(window, model);

		System.Reflection.FieldInfo field = typeof(PlayerRomPickerViewModel).GetField("_coverArt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
			?? throw new InvalidOperationException("PlayerRomPickerViewModel no longer holds its covers in _coverArt");
		CoverArtLedger ledger = Assert.IsType<CoverArtLedger>(field.GetValue(model.RomPicker));
		DisposeProbe held = new();
		ledger.Track(held);

		Press(window, PadNavAction.Up);
		WaitFor(() => FocusedName(window) == "RomPickerFolderRemove",
			$"Up from *Add a folder…* did not reach a row's Remove ({FocusedWhat(window)})");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.LibraryFolderRows.Count == 0, "the press did not take the last folder out of the list");

		Assert.True(held.Disposed, "removing the last folder left the grid's cover art held until the next ShowLibrary");
	}

	private sealed class DisposeProbe : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}
}
