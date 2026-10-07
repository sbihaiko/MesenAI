using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

//#1033 (ADR-0264 Decision 4): search on the flat library sheet, which is the
//wiring half. The match RULE - what `zel` finds, why a dump tag matches nothing,
//why an accent-less keyboard still finds Pokémon - is LibrarySearch's and is
//pinned host-free in UI.Tests/Play/LibrarySearchTests; this file checks that the
//sheet the player is looking at is that rule: the box narrows the grid as it is
//typed, Y puts the ring on the box and opens ADR-0262's shared pad keyboard, a
//query that keeps nothing is a NAMED state with the query in it, and Clear puts
//the whole library back.
//
//The library scan is stubbed to a fake tree in every case, the way
//PlayerLibraryTests does it: the default reads the real disk, so without this
//each case would depend on whatever ROMs the machine running the suite happens
//to keep - and on a scan landing mid-keystroke.
//
//W-P19b is the target picture of this half (ADR-0264 Decision 12): the library
//with a query active, the grid narrowed and the header still reading the
//LIBRARY's own count. The last case renders it AND compares the render to the
//wireframe, and W-P19b sits in the render gate's EXPECTED_WIREFRAME_RENDERS
//beside W-P19 - the picture was already drawn when #1033 was cut, so the same
//ticket that builds the surface is the one that starts holding it to it.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibrarySearchTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1033-" + Guid.NewGuid().ToString("N"));

	//The stand-in pad backend, built the way PlayerLibraryTests builds it: a
	//headless window gives InitializeEmu no platform handle, so no key manager
	//exists to name a pad. "Y" is on the list because this slice's own control is
	//the pad's Y.
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

	public PlayerLibrarySearchTests()
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

	//One press and its release, the way PlayerLibraryTests feeds them.
	private void Press(MainWindow window, PadNavAction action)
	{
		PressCode(window, PlayPadNavigation.CodeOf(Mapping, action));
	}

	//The sheet's own control (ADR-0264 Decision 3): Y, resolved through the same
	//table the bridge resolves it through - never a code typed into the test.
	private void PressSearch(MainWindow window)
	{
		PressCode(window, PadNavControls.SheetCode(PadFamily.Xbox, 0, PadSheetControl.Search, BackendCode) ?? 0);
	}

	private static void PressCode(MainWindow window, ushort code)
	{
		PlayPadNavigationWiring.TickForTest(window, new[] { code }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
	}

	private static string? FocusedName(MainWindow window) => (window.FocusManager?.GetFocusedElement() as Control)?.Name;

	private static PadKeyboard OpenKeyboard(MainWindow window) =>
		PlayPadNavigationWiring.KeyboardForTest(window) ?? throw new XunitException("the pad keyboard is not open");

	//The D-pad walks the keyboard's cursor onto each key, A presses it: a player's
	//path to a query, with nothing typed on a keyboard.
	private void TypeOnPad(MainWindow window, string text)
	{
		foreach(char c in text) {
			int index = OpenKeyboard(window).IndexOf(c);
			Assert.True(index >= 0, $"the pad keyboard has no '{c}' key");
			for(int steps = 0; OpenKeyboard(window).Cursor != index; steps++) {
				Assert.True(steps < 100, "the D-pad never reached the key");
				Press(window, PadNavAction.Right);
			}
			Press(window, PadNavAction.Confirm);
		}
	}

	//A library with four games, one of two levels down, and the settings pointed
	//at it - the tree the scan exists for, and the titles a query has to pick
	//between: two of them answer `mario`, one of them only answers `super`, and
	//one of them only answers `zel` - the query ADR-0264 Decision 4 names as the
	//acceptance criterion and W-P19b draws (`zel` leaves *The Legend of Zelda*
	//and nothing else).
	private string LibraryRoot()
	{
		string root = Path.Combine(_folder, "games");
		string nes = Path.Combine(root, "NES");
		string gb = Path.Combine(root, "Handheld", "GB");
		Directory.CreateDirectory(nes);
		Directory.CreateDirectory(gb);
		File.WriteAllBytes(Path.Combine(nes, "Super Mario Bros. 3 (U) [!].nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(nes, "Metroid (USA).nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(nes, "The Legend of Zelda (USA).nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(gb, "Super Mario Land (World).gb"), SyntheticNrom.Build());
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		return root;
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowOpenLibrary()
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

		//The library scan runs in the Open() turn, so the grid is complete before
		//the case asserts anything about it.
		model.RomPicker.RunLibraryScanInline = true;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();

		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.Tiles.Count == 4, "the library did not fill with the fake tree's four games");
		Assert.True(window.FindNamed<TextBox>("RomPickerSearch").IsOnScreen(), "the search box is not on the sheet");
		return (window, model);
	}

	private static string[] TileTitles(MainWindowViewModel model) => model.RomPicker.Tiles.Select(t => t.Title).ToArray();

	//The game list the fixture holds, in the order the scan answers it: by the
	//clean title, with the leading article sorted out - so *The Legend of Zelda*
	//sorts under L and leads the four.
	private static readonly string[] WholeLibrary = { "The Legend of Zelda", "Metroid", "Super Mario Bros. 3", "Super Mario Land" };

	//#1033 (ADR-0264 Decision 4): the query narrows the grid as it is typed, and
	//the match is a substring of the CLEAN title - `mario` finds both halves of
	//the collection, and the query is asked through LibrarySearch rather than
	//through a second rule written here.
	[AvaloniaFact]
	public void Typing_in_the_box_narrows_the_grid_as_it_is_typed()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowOpenLibrary();
		TextBox box = window.FindNamed<TextBox>("RomPickerSearch");

		Assert.Equal(WholeLibrary, TileTitles(model));

		//Through the control, which is the path a keyboard takes: the binding is
		//what carries the query to the grid.
		box.Text = "mario";
		Pump();
		Assert.Equal("mario", model.RomPicker.SearchQuery);
		Assert.Equal(new[] { "Super Mario Bros. 3", "Super Mario Land" }, TileTitles(model));
		Assert.True(model.RomPicker.HasQuery, "the Clear action is not offered while a query is on");
		Assert.Equal("", model.RomPicker.EmptyText);

		//Narrower still, and the grid follows one keystroke at a time rather than
		//on a commit: this is the whole of "narrows live".
		box.Text = "super mario land";
		Pump();
		Assert.Equal(new[] { "Super Mario Land" }, TileTitles(model));

		//The header reads the LIBRARY, not the grid (Decision 8): a search narrows
		//which games are on screen and never how many the player owns.
		Assert.Equal("4 games in 1 folder", model.RomPicker.CountText);

		//And the tiles the query kept are the tiles the grid draws.
		Assert.Equal(new[] { "Super Mario Land" },
			window.FindNamed<ItemsControl>("RomPickerGrid").GetVisualDescendants().OfType<Button>()
				.Where(b => b.DataContext is PlayerLibraryTile).Select(b => ((PlayerLibraryTile)b.DataContext!).Title).ToArray());
	}

	//#1033 (ADR-0264 Decision 4): the rule is LibrarySearch's, so a dump tag cannot
	//be searched for - `usa` is a tag on `Metroid (USA).nes`, not a word in the
	//game's name - while a word of the clean title still is.
	[AvaloniaFact]
	public void A_dump_tag_is_not_searchable_but_the_clean_title_is()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowOpenLibrary();
		TextBox box = window.FindNamed<TextBox>("RomPickerSearch");

		box.Text = "usa";
		Pump();
		Assert.Empty(model.RomPicker.Tiles);

		box.Text = "metro";
		Pump();
		Assert.Equal(new[] { "Metroid" }, TileTitles(model));
	}

	//#1033 (ADR-0264 Decision 4): an empty result is a NAMED state with the query
	//shown and a way to clear it, never an empty grid - and Clear is that way out.
	[AvaloniaFact]
	public void An_empty_result_is_a_named_state_and_Clear_puts_the_library_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowOpenLibrary();
		TextBox box = window.FindNamed<TextBox>("RomPickerSearch");
		Button clear = window.FindNamed<Button>("RomPickerSearchClear");

		//Nothing to clear yet, so the action is not offered: a Clear the player
		//cannot see the need for is noise on the sheet.
		Assert.False(clear.IsVisible, "Clear is offered with an empty box");

		box.Text = "zzzz";
		Pump();

		Assert.Empty(model.RomPicker.Tiles);
		Assert.Contains("zzzz", model.RomPicker.EmptyText);
		Assert.DoesNotContain("[[", model.RomPicker.EmptyText);
		TextBlock sentence = window.FindNamed<TextBlock>("RomPickerLibraryEmpty");
		Assert.True(sentence.IsOnScreen(), "the empty result is not on screen");
		Assert.Contains("zzzz", sentence.Text ?? "");
		Assert.True(clear.IsOnScreen(), "the Clear action is not on the sheet");

		//A press, not a call: the button is what the player has.
		clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Pump();

		Assert.Equal("", model.RomPicker.SearchQuery);
		Assert.False(model.RomPicker.HasQuery);
		Assert.Equal(WholeLibrary, TileTitles(model));
		Assert.Equal("", model.RomPicker.EmptyText);
		Assert.False(window.FindNamed<TextBlock>("RomPickerLibraryEmpty").IsOnScreen(), "the empty result stayed on screen after Clear");
	}

	//#1033 (ADR-0264 Decisions 3 and 4): the Clear action is the library's, so it
	//is not on the folder browser - where the search box is hidden, a Clear for a
	//query nobody can see is a control acting on a surface that is not up. Stepping
	//into the browser with a query on clears it, and the box is empty when the
	//player comes back.
	[AvaloniaFact]
	public void Clear_is_not_offered_on_the_folder_browser_and_stepping_into_it_empties_the_box()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowOpenLibrary();
		Button clear = window.FindNamed<Button>("RomPickerSearchClear");
		TextBox box = window.FindNamed<TextBox>("RomPickerSearch");

		box.Text = "zzzz";
		Pump();
		Assert.True(clear.IsOnScreen(), "Clear is not on the sheet while the library holds a query");

		//Browse a file…, through the view: the same press the player makes.
		window.FindNamed<Button>("RomPickerBrowseFile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Pump();

		Assert.Equal(RomPickerMode.BrowseFile, model.RomPicker.Mode);
		Assert.False(window.FindNamed<TextBox>("RomPickerSearch").IsOnScreen(), "the search box stayed on screen over the folder browser");
		Assert.False(window.FindNamed<Button>("RomPickerSearchClear").IsOnScreen(), "the folder browser offers a Clear for a search box it does not show");
		Assert.Equal("", model.RomPicker.SearchQuery);

		//And the box the player comes back to is empty - the query did not survive
		//a trip through a surface that cannot show it.
		model.RomPicker.Back();
		Pump();
		Assert.Equal(RomPickerMode.Library, model.RomPicker.Mode);
		Assert.True(window.FindNamed<TextBox>("RomPickerSearch").IsOnScreen(), "the search box did not come back with the library");
		Assert.Equal("", model.RomPicker.SearchQuery);
		Assert.False(model.RomPicker.HasQuery);
		Assert.Equal(WholeLibrary, TileTitles(model));
	}

	//#1033 (ADR-0264 Decision 4, ADR-0256 Decision 3): the Clear action hides
	//ITSELF - the box empties, so the button has nothing left to be shown for -
	//and it did so while it held the ring, leaving a pad player with no focus at
	//all: the next D-pad press had nowhere to move from. The ring goes to the
	//search box instead, which is still on screen and is where the player who
	//just undid a search is.
	[AvaloniaFact]
	public void Clear_hands_the_ring_to_the_search_box_instead_of_hiding_under_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowOpenLibrary();
		TextBox box = window.FindNamed<TextBox>("RomPickerSearch");
		Button clear = window.FindNamed<Button>("RomPickerSearchClear");

		box.Text = "zel";
		Pump();
		Assert.Equal(new[] { "The Legend of Zelda" }, TileTitles(model));

		//The pad walks to Clear and presses A on it, which is the path the defect
		//was found on: the button is the thing with the ring when it disappears.
		clear.Focus(NavigationMethod.Directional);
		Pump();
		Assert.Equal("RomPickerSearchClear", FocusedName(window));

		Press(window, PadNavAction.Confirm);
		Pump();

		Assert.Equal("", model.RomPicker.SearchQuery);
		Assert.False(clear.IsOnScreen(), "Clear stayed on screen with nothing to clear");
		Assert.Equal("RomPickerSearch", FocusedName(window));
		Assert.True(box.IsFocused, "the ring did not land on the search box");
		Assert.Equal(WholeLibrary, TileTitles(model));
	}

	//#1033 (ADR-0264 Decision 3): Y opens search. It puts the ring on the box and
	//opens the shared on-screen keyboard ADR-0262 owns, so the query is typed with
	//the pad alone - and the grid narrows live under it, one key at a time.
	[AvaloniaFact]
	public void Y_opens_search_and_the_pad_keyboard_types_the_query()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		(MainWindow window, MainWindowViewModel model) = ShowOpenLibrary();
		Assert.Null(PlayPadNavigationWiring.KeyboardForTest(window));

		PressSearch(window);

		Assert.Equal("RomPickerSearch", FocusedName(window));
		Assert.True(window.FindNamed<TextBox>("RomPickerSearch").IsFocused, "Y did not put the ring on the search box");
		PadKeyboard keyboard = OpenKeyboard(window);
		Assert.Equal(PadKeyboardShape.Text, keyboard.Shape);
		Assert.True(window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "PadKeyboardPanel"),
			"the pad keyboard was not drawn");

		//`zel`, which is ADR-0264 Decision 4's own acceptance criterion ("`zel`
		//finds *The Legend of Zelda*") and the query W-P19b draws: typing it leaves
		//exactly one tile, and the three games it did not match are gone.
		TypeOnPad(window, "zel");

		Assert.Equal("zel", model.RomPicker.SearchQuery);
		Assert.Equal(new[] { "The Legend of Zelda" }, TileTitles(model));
		Assert.DoesNotContain("Super Mario Bros. 3", TileTitles(model));

		//The box still holds the focus while the query is typed, which is what
		//lets the next key land in it: the grid rebuilt under the ring and did not
		//take it.
		Assert.Equal("RomPickerSearch", FocusedName(window));
	}

	//The W-P19b render case lives in PlayerLibraryRenderTests, beside W-P19.
	//#1033 (ADR-0264 Decision 12): the render gate runs only the cases in a
	//`*RenderTests` class (.github/workflows/render-gate.yml filters on
	//`FullyQualifiedName~RenderTests`), so a render case in this file would be
	//filtered out of the job that verifies the PNGs and the gate would fail on a
	//PNG nobody wrote. The search's own behaviour is what this class pins.
}
