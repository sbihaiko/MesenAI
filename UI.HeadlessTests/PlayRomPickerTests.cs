using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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

//#845 (ADR-0256 Decision 8/9): the Play home's *Open a ROM…* is reachable from
//the pad and activates, but what it opened was a native OS file dialog
//(FileDialogHelper -> Avalonia's StorageProvider), which the focus engine cannot
//drive - so a machine with a pad and nothing else could not load a game.
//
//This is the wiring half: the pad's Confirm on the home's own action has to open
//an in-app surface, and the choice made there has to reach the same open-ROM
//path the native dialog's result went to. The rules (roots, rows, ascend) are
//host-free in UI.Tests/Play/RomPickerTests.
[Collection(NativeCoreCollection.Name)]
public class PlayRomPickerTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;

	private readonly List<MainWindow> _windows = new();

	//A tree of its own per case: the picker's real filesystem half (roots,
	//rows, ascend) is exercised against folders this case made.
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-845-" + Guid.NewGuid().ToString("N"));

	//The backend this suite does not have, built the way PlayPadNavigationTests
	//builds it: a headless window gives InitializeEmu no platform handle, so no
	//key manager exists to name a pad.
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

	private static string BackendName(ushort keyCode)
	{
		return Backend.TryGetValue(keyCode, out string? name) ? name : "";
	}

	private static ushort BackendCode(string name)
	{
		return BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;
	}

	private PadNavMapping? _mapping;
	private PadNavMapping Mapping => _mapping ??= PadNavControls.Resolve(PadFamily.Xbox, 0, BackendCode)
		?? throw new InvalidOperationException("the stand-in table does not answer the Xbox preset's names");

	public PlayRomPickerTests()
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
		//#838: a window that outlives its case is a second top level.
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
		ConfigManager.Config.Save();

		try {
			Directory.Delete(_folder, true);
		} catch {
			//A case that failed before it built its tree leaves nothing to remove;
			//the temp folder going is not what the case was proving.
		}
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowPlay()
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

	//A first run: no recent games, so W-P1's own action is the one on screen.
	//
	//The scan is stubbed to answer nothing here. Its default walks the machine
	//this suite happens to run on - the developer's whole home folder - so without
	//this every pad case below would depend on that disk and on a scan landing in
	//the middle of a key press. The cases about the scan set their own source.
	private (MainWindow Window, MainWindowViewModel Model) ShowFirstRunHome()
	{
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.RomPicker.SuggestionSource = _ => Array.Empty<RomPickerHit>();
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		Assert.True(model.RecentGames.ShowFirstRunHome, "the home is not the first-run one, so this case would prove nothing");
		return (window, model);
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

	private static string? FocusedName(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.Name;
	}

	private static string Focused(MainWindow window)
	{
		Control? focused = window.FocusManager?.GetFocusedElement() as Control;
		return focused is null ? "focus=<none>" : $"focus={focused.GetType().Name}#{focused.Name}";
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

	//#845: with nothing but a pad, the home's own action has to open a surface
	//the pad can drive. Before this slice it reached
	//EmuApi.ExecuteShortcut(EmulatorShortcut.OpenFile), i.e. ShortcutHandler ->
	//FileDialogHelper -> Avalonia's StorageProvider - a native dialog the focus
	//engine cannot reach past.
	[AvaloniaFact]
	public void The_pad_opens_the_in_app_rom_picker_on_the_home()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowFirstRunHome();
		WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");

		Press(window, PadNavAction.Confirm);
		Pump();

		Border sheet = window.FindNamed<Border>("PlayerRomPickerSheet");
		Assert.True(sheet.IsOnScreen(), $"the pad's Confirm opened no in-app picker ({Focused(window)})");
	}

	//#845: and the choice it opens has to reach the load. The pad walks the
	//roots into a folder, confirms the game it finds, and the game is on screen -
	//the same LoadRomHelper.LoadFile the native dialog's own result went to.
	[AvaloniaFact]
	public void The_pad_walks_into_a_folder_and_opens_the_game_it_finds()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Path.Combine(_folder, "games");
		Directory.CreateDirectory(Path.Combine(root, "nes"));
		File.WriteAllBytes(Path.Combine(root, "nes", "Contra.nes"), SyntheticNrom.Build());

		//The configured game folder is the first root, so the pad's way in is the
		//same on any machine; without it the roots would be whatever volumes the
		//test machine happens to have.
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		try {
			(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
			WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
				"the first-run home did not put the focus on its one action");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "Your games",
				$"the picker did not open on its roots ({Focused(window)})");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "nes",
				$"Confirm on the configured folder did not descend into it ({Focused(window)})");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "Contra.nes",
				$"Confirm on the folder did not list the game inside it ({Focused(window)})");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown,
				"confirming the game did not load it");
			//The sheet is gone: the pick IS the open, not a step before one.
			Assert.False(model.RomPicker.IsVisible);
		} finally {
			ConfigManager.Config.Preferences.GameFolder = "";
			ConfigManager.Config.Preferences.OverrideGameFolder = false;
		}
	}

	//#845: Back ascends one folder and, on the first list, dismisses with nothing
	//opened - so a player who opened the picker by mistake is where they were.
	[AvaloniaFact]
	public void Pad_Back_ascends_the_picker_and_dismisses_it_on_the_roots()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Path.Combine(_folder, "games");
		Directory.CreateDirectory(Path.Combine(root, "nes"));
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		try {
			(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
			WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
				"the first-run home did not put the focus on its one action");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "Your games", "the picker did not open on its roots");
			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "nes", "Confirm did not descend into the configured folder");

			Press(window, PadNavAction.Back);
			WaitFor(() => FocusedRow(window) == "Your games",
				$"Back did not ascend out of the folder ({Focused(window)})");
			Assert.True(model.RomPicker.IsVisible, "Back closed the picker a level early");

			Press(window, PadNavAction.Back);
			WaitFor(() => !model.RomPicker.IsVisible, "Back on the roots did not dismiss the picker");
			Assert.False(EmuApi.IsRunning());
			Assert.False(model.IsPlayerOverlayVisible);
		} finally {
			ConfigManager.Config.Preferences.GameFolder = "";
			ConfigManager.Config.Preferences.OverrideGameFolder = false;
		}
	}

	//The review of #845 (Grok 4.6, 2026-10-05) found this one, and it is the kind
	//of defect no colour or focus case can see: the sheet's heading and its empty
	//line are the two strings the view-model reads with
	//ResourceHelper.GetMessage, and that reads the resource file's <Messages>
	//section - the four picker strings landed as <Control> entries under a
	//<Form>, which is what {l:Translate} resolves. GetMessage answers an id it
	//does not hold with "[[" + id + "]]", so the one line that says what the
	//surface is rendered as "[[RomPickerTitle]]", and a folder with nothing to
	//open said "[[RomPickerEmpty]]". Words, never a key: the same rule W-X2
	//already states for the sentences Play shows.
	[AvaloniaFact]
	public void The_sheet_says_its_own_title_in_words()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.OpenRomPicker();
		Pump();
		Assert.True(model.RomPicker.IsVisible, "the picker did not open, so this case would prove nothing");

		//The heading is a bound TextBlock, so this is what the player reads.
		TextBlock title = window.FindNamed<TextBlock>("RomPickerTitle");
		Assert.True(title.IsOnScreen(), "the sheet's heading is not on screen");
		Assert.False(string.IsNullOrWhiteSpace(title.Text), "the sheet's heading is empty");
		Assert.DoesNotContain("[[", title.Text);

		//And the sentence a folder with nothing to open shows, read the same way
		//and empty while there is something to pick.
		Assert.DoesNotContain("[[", model.RomPicker.EmptyText);
	}

	//The second review of #845 (2026-10-05) found this, and it is the one that
	//made the slice unusable on the machine it was written for: every case above
	//only ever presses Confirm on the *first* row, so none of them could see that
	//the ring never leaves it. `PlayFocusOnOpen.SearchRoot` answers with the
	//nearest ancestor the open claim's target and the focused control share,
	//walked from the target's own parent - and this sheet's target is a row,
	//alone inside its ContentPresenter, so the root is that one row and a D-pad
	//press has nowhere to go. Only the first root, or the alphabetically first
	//entry of a folder, could ever be opened with a pad.
	[AvaloniaFact]
	public void The_pad_walks_the_rows_of_the_roots_and_of_a_folder()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Path.Combine(_folder, "games");
		foreach(string name in new[] { "aaa", "bbb", "ccc" }) {
			Directory.CreateDirectory(Path.Combine(root, name));
		}
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		try {
			(MainWindow window, _) = ShowFirstRunHome();
			WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
				"the first-run home did not put the focus on its one action");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "Your games",
				$"the picker did not open on its roots ({Focused(window)})");

			//The second root is one press down: the app's own folder follows the
			//configured one.
			Press(window, PadNavAction.Down);
			WaitFor(() => FocusedRow(window) == "MesenAI's games folder",
				$"the pad could not leave the picker's first row ({Focused(window)})");

			//And back up, so the walk is a walk and not a one-way jump.
			Press(window, PadNavAction.Up);
			WaitFor(() => FocusedRow(window) == "Your games",
				$"the pad could not come back up to the first root ({Focused(window)})");

			//Inside a folder the rows are the folders it holds, in order.
			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "aaa",
				$"Confirm did not descend into the configured folder ({Focused(window)})");
			Press(window, PadNavAction.Down);
			WaitFor(() => FocusedRow(window) == "bbb",
				$"the pad could not step to the second row of a folder ({Focused(window)})");
			Press(window, PadNavAction.Down);
			WaitFor(() => FocusedRow(window) == "ccc",
				$"the pad could not step to the third row of a folder ({Focused(window)})");
		} finally {
			ConfigManager.Config.Preferences.GameFolder = "";
			ConfigManager.Config.Preferences.OverrideGameFolder = false;
		}
	}

	//The label of the row the pad's ring is on. The rows are the picker's own
	//list, so this is "which row would Confirm act on" and nothing else.
	private static string? FocusedRow(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.DataContext is PlayerRomPickerRow row ? row.Label : null;
	}

	private static RomPickerRowKind? FocusedKind(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.DataContext is PlayerRomPickerRow row ? row.Kind : null;
	}

	//The amendment's two halves, from the pad's side: the sheet discovers the
	//standard places to look, and the player can name the folder they walked into
	//as the games folder without a keyboard.

	//(1) The action row writes the config the classic Advanced Options row writes,
	//re-roots in place, and STAYS - the sheet does not dismiss, so the next Confirm
	//loads a game from the folder that just became "Your games".
	[AvaloniaFact]
	public void The_pad_makes_the_folder_it_walked_into_the_games_folder()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Path.Combine(_folder, "games");
		string sub = Path.Combine(root, "nes");
		Directory.CreateDirectory(sub);
		File.WriteAllBytes(Path.Combine(sub, "Contra.nes"), SyntheticNrom.Build());
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		try {
			(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
			//The core keeps its own list of folders a game can be found in and has
			//no getter for it, so this seam is the only place the hand-off is
			//observable; its default is the real EmuApi.AddKnownGameFolder call.
			List<string> known = new();
			model.RomPicker.KnownGameFolderSink = known.Add;
			WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
				"the first-run home did not put the focus on its one action");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "Your games", $"the picker did not open on its roots ({Focused(window)})");
			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "nes", $"Confirm did not descend into the configured folder ({Focused(window)})");
			Press(window, PadNavAction.Confirm);
			//The action row leads the list, but the ring landed on the content.
			WaitFor(() => FocusedRow(window) == "Contra.nes", $"Confirm did not list the game ({Focused(window)})");
			Assert.Equal(RomPickerRowKind.Game, FocusedKind(window));

			//One Up reaches the action row, and Confirm makes this folder the one.
			Press(window, PadNavAction.Up);
			WaitFor(() => FocusedKind(window) == RomPickerRowKind.Action,
				$"the pad could not reach the action row ({Focused(window)})");
			Press(window, PadNavAction.Confirm);

			Assert.Equal(sub, ConfigManager.Config.Preferences.GameFolder);
			Assert.True(ConfigManager.Config.Preferences.OverrideGameFolder);
			//And the core is told now, not at the next launch: that list is what
			//RomFinder resolves a ROM by name and CRC against, and the core
			//otherwise reads the configured folder only at startup.
			Assert.Equal(new[] { sub }, known);
			//It stays, re-claimed on the folder's own content: the action row is
			//gone, the path line reads "Your games".
			WaitFor(() => model.RomPicker.IsVisible && FocusedRow(window) == "Contra.nes",
				$"the save did not re-claim the ring on the folder's content ({Focused(window)})");
			Assert.Equal("Your games", model.RomPicker.PathText);
			Assert.DoesNotContain(model.RomPicker.Rows, r => r.Kind == RomPickerRowKind.Action);
		} finally {
			ConfigManager.Config.Preferences.GameFolder = "";
			ConfigManager.Config.Preferences.OverrideGameFolder = false;
		}
	}

	//The failure the amendment must not introduce: a folder with nothing in it
	//has only the action row, and a stray Confirm there would silently repoint
	//"Your games" away from a library that worked. So the ring falls to Back.
	[AvaloniaFact]
	public void An_empty_folder_sends_the_ring_to_back_not_to_the_action_row()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Path.Combine(_folder, "games");
		string empty = Path.Combine(root, "empty");
		Directory.CreateDirectory(empty);
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		try {
			(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
			WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
				"the first-run home did not put the focus on its one action");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "Your games", $"the picker did not open on its roots ({Focused(window)})");
			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "empty", $"Confirm did not descend into the configured folder ({Focused(window)})");
			Press(window, PadNavAction.Confirm);

			WaitFor(() => FocusedName(window) == "RomPickerBack",
				$"an empty folder did not send the ring to Back ({Focused(window)})");
			Assert.DoesNotContain(model.RomPicker.Rows, r => r.Kind != RomPickerRowKind.Action);
			Assert.False(string.IsNullOrEmpty(model.RomPicker.EmptyText));

			//And Back leaves the folder without having changed a thing.
			Press(window, PadNavAction.Back);
			WaitFor(() => FocusedRow(window) == "empty", $"Back did not ascend out of the empty folder ({Focused(window)})");
			Assert.Equal(root, ConfigManager.Config.Preferences.GameFolder);
		} finally {
			ConfigManager.Config.Preferences.GameFolder = "";
			ConfigManager.Config.Preferences.OverrideGameFolder = false;
		}
	}

	//(2) The scan's discovery, through the real async path (Task.Run ->
	//Dispatcher.Post): an injected hit list stands in for the disk, so the case
	//is deterministic while still exercising the thread hop the real scan uses.
	[AvaloniaFact]
	public void The_suggestions_appear_on_the_roots_and_the_pad_can_walk_into_one()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string lib = Path.Combine(_folder, "lib");
		Directory.CreateDirectory(lib);
		File.WriteAllBytes(Path.Combine(lib, "Contra.nes"), SyntheticNrom.Build());

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomPicker.SuggestionSource = _ => new[] { new RomPickerHit(lib, 30) };
		WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");

		Press(window, PadNavAction.Confirm);
		WaitFor(() => FocusedRow(window) == "MesenAI's games folder",
			$"the picker did not open on its roots ({Focused(window)})");
		WaitFor(() => model.RomPicker.Suggestions.Count == 1,
			"the scan's suggestion never landed on the roots");

		//It is a row below the known roots, a place the pad can walk into.
		PlayerRomPickerRow suggestion = Assert.Single(model.RomPicker.Rows, r => r.Path == lib);
		Assert.Equal(RomPickerRowKind.Folder, suggestion.Kind);
		int index = model.RomPicker.Rows.IndexOf(suggestion);
		for(int i = 0; i < index; i++) {
			Press(window, PadNavAction.Down);
		}
		WaitFor(() => FocusedRow(window) == suggestion.Label,
			$"the pad could not reach the suggestion ({Focused(window)})");

		Press(window, PadNavAction.Confirm);
		WaitFor(() => FocusedRow(window) == "Contra.nes",
			$"Confirm on the suggestion did not list the game in it ({Focused(window)})");
	}

	//A scan that finds nothing clears its line and adds no rows - the known roots
	//are untouched and there is no error and no nag. It is one half of a pair; the
	//other half is A_scan_that_finds_something_puts_a_row_on_the_roots below.
	[AvaloniaFact]
	public void Finding_nothing_leaves_the_roots_untouched()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomPicker.SuggestionSource = _ => Array.Empty<RomPickerHit>();
		model.RomPicker.RunScanInline = true;
		WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");

		Press(window, PadNavAction.Confirm);
		WaitFor(() => FocusedRow(window) == "MesenAI's games folder",
			$"the picker did not open on its roots ({Focused(window)})");

		Assert.Empty(model.RomPicker.Suggestions);
		Assert.Equal("", model.RomPicker.SearchingText);
		Assert.Equal("MesenAI's games folder", model.RomPicker.Rows[0].Label);
	}

	//The complement of the case above, and the reason that case is evidence at
	//all: a scan that DOES find something puts a row on the roots. On its own,
	//"finding nothing leaves the roots untouched" passes against a sheet whose
	//whole suggestion path was never written - the assertion would be about a
	//feature that is not there.
	[AvaloniaFact]
	public void A_scan_that_finds_something_puts_a_row_on_the_roots()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string lib = Path.Combine(_folder, "lib");
		Directory.CreateDirectory(lib);

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomPicker.SuggestionSource = _ => new[] { new RomPickerHit(lib, 30) };
		model.RomPicker.RunScanInline = true;
		WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");

		Press(window, PadNavAction.Confirm);
		WaitFor(() => FocusedRow(window) == "MesenAI's games folder",
			$"the picker did not open on its roots ({Focused(window)})");

		PlayerRomPickerRow row = Assert.Single(model.RomPicker.Rows, r => r.Path == lib);
		Assert.Equal(RomPickerRowKind.Folder, row.Kind);
		//Below the known roots, never above them.
		Assert.Equal(model.RomPicker.Rows[^1], row);
		Assert.Equal("", model.RomPicker.SearchingText);
	}

	//One scan, two passes (ADR-0256 Decision 9 amendment, second review of #845):
	//the shallow pass is published the moment it lands and the deep pass replaces
	//it when it returns, so a cold cache or a slow disk cannot leave the player on
	//the plain roots with no suggestion and nothing to designate.
	//
	//Both passes run off the UI thread here - the real path - and the deep one is
	//held open at the seam until this case has read the shallow answer. That is
	//the whole point: with the two passes collapsed into one turn, "the shallow
	//rows were published" and "the shallow rows were computed and thrown away"
	//look exactly the same.
	[AvaloniaFact]
	public void The_shallow_answer_lands_first_and_the_deep_one_replaces_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string shallow = Path.Combine(_folder, "shallow", "roms");
		string deep = Path.Combine(_folder, "deep", "roms");
		Directory.CreateDirectory(shallow);
		Directory.CreateDirectory(deep);

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		using SemaphoreSlim hold = new(0);
		List<RomScanPass> passes = new();
		model.RomPicker.SuggestionSource = pass => {
			lock(passes) {
				passes.Add(pass);
			}
			if(pass == RomScanPass.Deep) {
				//No assertion here: this runs inside the scan's own guard, where a
				//throw is swallowed by design. The case releases it below.
				hold.Wait(TimeSpan.FromSeconds(30));
			}
			return pass == RomScanPass.Shallow
				? new[] { new RomPickerHit(shallow, 2) }
				: new[] { new RomPickerHit(deep, 30) };
		};
		try {
			WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
				"the first-run home did not put the focus on its one action");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "MesenAI's games folder",
				$"the picker did not open on its roots ({Focused(window)})");

			//The shallow answer is on screen while the deep walk is still going.
			WaitFor(() => model.RomPicker.Suggestions.Any(s => s.Folder == shallow),
				"the shallow pass's rows never landed on the roots");
			Assert.Contains(model.RomPicker.Rows, r => r.Path == shallow);
			Assert.NotEqual("", model.RomPicker.SearchingText);

			hold.Release();

			//And the deep answer takes their place.
			WaitFor(() => model.RomPicker.Suggestions.Any(s => s.Folder == deep),
				"the deep pass's rows never replaced the shallow ones");
			Assert.DoesNotContain(model.RomPicker.Rows, r => r.Path == shallow);
			Assert.Contains(model.RomPicker.Rows, r => r.Path == deep);

			lock(passes) {
				Assert.Equal(new[] { RomScanPass.Shallow, RomScanPass.Deep }, passes);
			}
		} finally {
			hold.Release();
		}
	}

	//The other half of "the deep pass replaces the shallow one": it may not make
	//the sheet worse. A deep pass offering fewer libraries than the shallow one
	//already did would read to the player as "my libraries are gone", so a smaller
	//answer is discarded and the rows on screen stay.
	[AvaloniaFact]
	public void A_deep_pass_that_finds_less_does_not_take_the_shallow_rows_away()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string first = Path.Combine(_folder, "first");
		string second = Path.Combine(_folder, "second");
		Directory.CreateDirectory(first);
		Directory.CreateDirectory(second);

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomPicker.SuggestionSource = pass => pass == RomScanPass.Shallow
			? new[] { new RomPickerHit(first, 30), new RomPickerHit(second, 10) }
			: new[] { new RomPickerHit(first, 30) };
		model.RomPicker.RunScanInline = true;
		WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");

		Press(window, PadNavAction.Confirm);
		WaitFor(() => FocusedRow(window) == "MesenAI's games folder",
			$"the picker did not open on its roots ({Focused(window)})");

		Assert.Equal(2, model.RomPicker.Suggestions.Count);
		Assert.Contains(model.RomPicker.Rows, r => r.Path == second);
	}

	//And a pass that throws answers nothing, which leaves the other one's rows in
	//place: the scan runs against disks that are not ours - a volume pulled out
	//mid-walk, a permission, a filesystem that answers with an error - and none of
	//that is a reason to erase what the player is reading. The scanning line goes
	//anyway, because it belongs to the scan and not to a pass.
	[AvaloniaFact]
	public void A_pass_that_throws_leaves_the_other_passs_rows_in_place()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string lib = Path.Combine(_folder, "lib");
		Directory.CreateDirectory(lib);

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomPicker.SuggestionSource = pass => pass == RomScanPass.Shallow
			? new[] { new RomPickerHit(lib, 30) }
			: throw new IOException("the volume went away mid-walk");
		model.RomPicker.RunScanInline = true;
		WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");

		Press(window, PadNavAction.Confirm);
		WaitFor(() => FocusedRow(window) == "MesenAI's games folder",
			$"the picker did not open on its roots ({Focused(window)})");

		Assert.Single(model.RomPicker.Suggestions);
		Assert.Contains(model.RomPicker.Rows, r => r.Path == lib);
		Assert.Equal("", model.RomPicker.SearchingText);
	}

	//The scan is one per session (ADR-0256 Decision 9 amendment): it is a bounded
	//walk of the whole machine, and a sheet dismissed and opened again must not
	//start a second one. What it found is cached, so the second open shows the
	//rows immediately and the source is never asked again.
	//
	//Kept as a separate case from the two-pass pair above because it pins a
	//different thing: not what one scan publishes, but how many scans a session
	//gets. One shallow answer and one deep one - two in total - and no more.
	//
	//Labelled honestly: the once-per-session half is a REGRESSION PIN. The cache
	//and its _scanStarted guard came with #845 and this case exists to keep them,
	//so it passes before and after this slice and is not evidence for it. What is
	//new here is the count of two - a shallow answer and a deep one - which is
	//what the amendment changed one pass into.
	[AvaloniaFact]
	public void The_scan_runs_once_and_the_second_open_still_shows_its_rows()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string lib = Path.Combine(_folder, "lib");
		Directory.CreateDirectory(lib);

		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		int shallow = 0;
		int deep = 0;
		model.RomPicker.SuggestionSource = pass => {
			if(pass == RomScanPass.Shallow) {
				shallow++;
			} else {
				deep++;
			}
			return new[] { new RomPickerHit(lib, 30) };
		};
		model.RomPicker.RunScanInline = true;
		WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");

		model.OpenRomPicker();
		Pump();
		Assert.True(model.RomPicker.IsVisible, "the picker did not open, so this case would prove nothing");
		Assert.Contains(model.RomPicker.Rows, r => r.Path == lib);
		Assert.Equal(1, shallow);
		Assert.Equal(1, deep);

		//Dismiss and open again: the sheet is on the roots with the rows it already
		//had, and the disk is not walked a second time.
		model.RomPicker.Back();
		Pump();
		Assert.False(model.RomPicker.IsVisible, "Back on the roots did not dismiss the picker");
		model.OpenRomPicker();
		Pump();

		Assert.True(model.RomPicker.IsVisible, "the picker did not open a second time");
		Assert.Contains(model.RomPicker.Rows, r => r.Path == lib);
		Assert.Equal(1, shallow);
		Assert.Equal(1, deep);
	}

	//The whole-computer root is offered on every platform with a single root - on
	//macOS and on Linux alike - so its label may not name one of them. It read
	//"This Mac" on the Linux sheet that offers the same "/" row. The label and the
	//folder are built together in one place, so the two cannot drift apart again.
	[AvaloniaFact]
	public void The_whole_computer_root_is_named_for_a_computer()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.OpenRomPicker();
		Pump();
		Assert.True(model.RomPicker.IsVisible, "the picker did not open, so this case would prove nothing");

		PlayerRomPickerRow root = Assert.Single(model.RomPicker.Rows, r => r.Path == "/");
		Assert.Equal("This computer", root.Label);
		//Still the last of the fixed roots: the discovered ones follow it.
		Assert.Equal(model.RomPicker.Rows[^1], root);
	}

	//The three row kinds, read off the visual tree (ADR-0249 Decision 5's render
	//gate covers this surface too): the action row is not a place, so it draws the
	//sparkle and the accent - never the play icon. The template branched on
	//`!IsFolder` before this slice, which is TRUE for an action row, so the row
	//that means "make this my games folder" drew the icon that means "play this"
	//and nothing failed. The kind is what the template must branch on, and this is
	//the only case that reads what is actually drawn.
	[AvaloniaFact]
	public void The_action_row_draws_the_sparkle_and_the_accent_not_the_play_icon()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Path.Combine(_folder, "games");
		string sub = Path.Combine(root, "nes");
		Directory.CreateDirectory(sub);
		File.WriteAllBytes(Path.Combine(sub, "Contra.nes"), SyntheticNrom.Build());
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		try {
			(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
			model.OpenRomPicker();
			Pump();
			model.RomPicker.Choose(model.RomPicker.Rows.First(r => r.Label == "Your games"));
			Pump();
			model.RomPicker.Choose(model.RomPicker.Rows.First(r => r.Label == "nes"));
			Pump();

			PlayerRomPickerRow action = Assert.Single(model.RomPicker.Rows, r => r.Kind == RomPickerRowKind.Action);
			Button button = window.FindNamed<ItemsControl>("RomPickerList")
				.GetVisualDescendants().OfType<Button>()
				.Single(b => ReferenceEquals(b.DataContext, action));

			PathIcon[] icons = button.GetVisualDescendants().OfType<PathIcon>().ToArray();
			PathIcon drawn = Assert.Single(icons, i => i.IsEffectivelyVisible);
			//By identity, not by ToString: both resources parse to a StreamGeometry
			//whose ToString is the same word for every geometry in the app, so a
			//string comparison here passes whatever is drawn.
			Geometry sparkle = Assert.IsAssignableFrom<Geometry>(window.FindResource("PlayerIconSparkle"));
			Geometry play = Assert.IsAssignableFrom<Geometry>(window.FindResource("PlayerIconPlay"));
			Assert.Same(sparkle, drawn.Data);
			Assert.DoesNotContain(icons, i => i.IsEffectivelyVisible && ReferenceEquals(i.Data, play));

			//And the accent, two ways: the class the theme's tint setter hangs off,
			//and the brush that setter writes. Either alone could survive a rename
			//of the other.
			Assert.Contains("action", button.Classes);
			Assert.Equal(
				PlayerRender.SolidColor(window.FindResource("PlayerTintTextPlayBrush") as IBrush),
				PlayerRender.SolidColor(button.Foreground));
		} finally {
			ConfigManager.Config.Preferences.GameFolder = "";
			ConfigManager.Config.Preferences.OverrideGameFolder = false;
		}
	}
}
