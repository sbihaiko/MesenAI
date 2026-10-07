using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0249 Decision 5, the ROM picker's wave (ADR-0256 Decision 9, amended):
//five states of the in-app picker a person would want to look at, each written
//as a Skia PNG beside the W-P* renders. The states are the ones the amendment
//adds and the ones a reviewer has to see to judge the sheet: the roots with a
//discovered suggestion, the roots while the scan still runs, a folder whose
//first row is the action, the same folder after the action was taken, and a
//folder with nothing to open, where Back and not the action row holds the ring.
//
//Every case also asserts the render's identity - what is on screen and what is
//not - so a PNG that stops being what it was named for fails here rather than
//being noticed by whoever opens it next. The window is the renders' 1100 x 740
//MainWindow; the output folder is PlayerRender.OutputFolder (MESEN_PLAYER_RENDERS,
//printed by Save).
[Collection(NativeCoreCollection.Name)]
public class PlayRomPickerRenderTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;
	private readonly List<string>? _libraryFolders = ConfigManager.Config.Preferences.LibraryFolders;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-rom-picker-renders-" + Guid.NewGuid().ToString("N"));

	public PlayRomPickerRenderTests()
	{
		Directory.CreateDirectory(_folder);
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.GameFolder = "";
		prefs.OverrideGameFolder = false;
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

	private (MainWindow Window, MainWindowViewModel Model) Show()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;

		//#999: the stale recents go BEFORE the window starts. The window's own
		//startup Init reads them, and a second Init in the same mode with entries
		//on screen returns early (the anti-flicker guard), so deleting them
		//afterwards left the recents home up whenever an earlier class had left
		//an .rgd behind.
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");

		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		Assert.True(model.RecentGames.ShowFirstRunHome, "the home is not the first-run one, so the render would be of the wrong surface");
		return (window, model);
	}

	//A folder with a game in it, and the configured folder pointed at it: the
	//roots then lead with "Your games" on any machine.
	private string GamesRoot()
	{
		string root = Path.Combine(_folder, "games");
		string sub = Path.Combine(root, "nes");
		Directory.CreateDirectory(sub);
		File.WriteAllBytes(Path.Combine(sub, "Contra.nes"), SyntheticNrom.Build());
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		ConfigManager.Config.Preferences.LibraryFolders = null;
		return root;
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

	//Every case below asserts its state's identity structurally - what is on
	//screen, what is not, which row leads and where the ring is - and then writes
	//the PNG. The assertions are what keeps a PNG from quietly becoming a picture
	//of some other state; they are not a substitute for looking at it.
	private static void AssertSheetIsUp(MainWindow window, MainWindowViewModel model)
	{
		Assert.True(model.RomPicker.IsVisible, "the picker is not open, so this render would be of the home");
		Assert.True(window.FindNamed<Border>("PlayerRomPickerSheet").IsOnScreen(), "the sheet is not on screen");
	}

	//(a) The roots with a discovered suggestion on them, and the searching line
	//gone. This is the state the amendment exists for: the scan found a library
	//the player never configured, and it is one press away.
	[AvaloniaFact]
	public void Roots_with_a_discovered_suggestion()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string lib = Path.Combine(_folder, "lib");
		Directory.CreateDirectory(lib);
		File.WriteAllBytes(Path.Combine(lib, "Contra.nes"), SyntheticNrom.Build());

		(MainWindow window, MainWindowViewModel model) = Show();
		model.RomPicker.SuggestionSource = _ => new[] { new RomPickerHit(lib, 30) };
		model.RomPicker.RunScanInline = true;
		model.OpenRomPicker();
		//#1032 (ADR-0264 Decision 11): these renders are of the BROWSER, which
		//now lives inside the library sheet behind *Browse a file…*.
		model.RomPicker.BrowseFile();
		Pump();

		AssertSheetIsUp(window, model);
		PlayerRomPickerRow suggestion = Assert.Single(model.RomPicker.Rows, r => r.Path == lib);
		Assert.Equal(RomPickerRowKind.Folder, suggestion.Kind);
		Assert.Equal(model.RomPicker.Rows[^1], suggestion);
		//The line is gone: the scan is done, and the state on screen is the answer.
		Assert.False(window.FindNamed<TextBlock>("RomPickerSearching").IsOnScreen());
		Assert.Contains(suggestion.Label, VisibleTexts(window));

		PlayerRender.Save(PlayerRender.Capture(window), "rom-picker-roots");
	}

	//(b) The roots while the scan still runs: the sheet is already usable - the
	//roots are rows - and the line says what is happening. A player never waits
	//for a blank sheet.
	[AvaloniaFact]
	public void Roots_while_the_scan_runs()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show();
		using SemaphoreSlim hold = new(0);
		model.RomPicker.SuggestionSource = _ => {
			hold.Wait(TimeSpan.FromSeconds(30));
			return Array.Empty<RomPickerHit>();
		};
		try {
			model.OpenRomPicker();
		//#1032 (ADR-0264 Decision 11): these renders are of the BROWSER, which
		//now lives inside the library sheet behind *Browse a file…*.
		model.RomPicker.BrowseFile();
			Pump();

			WaitFor(() => model.RomPicker.SearchingText.Length > 0, "the scan never announced itself");
			AssertSheetIsUp(window, model);
			TextBlock line = window.FindNamed<TextBlock>("RomPickerSearching");
			Assert.True(line.IsOnScreen(), "the searching line is not on screen while the scan runs");
			//The roots are rows underneath it: the sheet is usable while it works.
			Assert.True(model.RomPicker.Rows.Count > 0, "the sheet offers nothing to press while the scan runs");
			Assert.True(window.FindNamed<Button>("RomPickerBack").IsOnScreen(), "Back is not on screen while the scan runs");

			PlayerRender.Save(PlayerRender.Capture(window), "rom-picker-scanning");
		} finally {
			hold.Release();
			Pump();
		}
	}

	//(c) A folder whose first row is the action: the state the amendment adds to
	//the folder view. The row is accent and leads the list; the ring is on the
	//content below it, one Up away.
	[AvaloniaFact]
	public void A_folder_with_the_action_row_first()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		GamesRoot();

		(MainWindow window, MainWindowViewModel model) = Show();
		model.RomPicker.SuggestionSource = _ => Array.Empty<RomPickerHit>();
		model.RomPicker.RunScanInline = true;
		model.OpenRomPicker();
		//#1032 (ADR-0264 Decision 11): these renders are of the BROWSER, which
		//now lives inside the library sheet behind *Browse a file…*.
		model.RomPicker.BrowseFile();
		Pump();
		model.RomPicker.Choose(model.RomPicker.Rows.First(r => r.Label == "Your games"));
		Pump();
		model.RomPicker.Choose(model.RomPicker.Rows.First(r => r.Label == "nes"));
		Pump();

		AssertSheetIsUp(window, model);
		PlayerRomPickerRow action = Assert.Single(model.RomPicker.Rows, r => r.Kind == RomPickerRowKind.Action);
		Assert.Equal(model.RomPicker.Rows[0], action);
		Assert.Contains("nes", model.RomPicker.PathText);
		Assert.False(window.FindNamed<TextBlock>("RomPickerEmpty").IsOnScreen());

		PlayerRender.Save(PlayerRender.Capture(window), "rom-picker-folder");
	}

	//(d) The same folder after the action was taken: the action row is gone (this
	//folder IS the games folder now), the path line reads "Your games", and the
	//notice says what the press did. The sheet stayed up, which is the point.
	[AvaloniaFact]
	public void A_folder_after_the_action_was_taken()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		GamesRoot();

		(MainWindow window, MainWindowViewModel model) = Show();
		model.RomPicker.SuggestionSource = _ => Array.Empty<RomPickerHit>();
		model.RomPicker.RunScanInline = true;
		model.OpenRomPicker();
		//#1032 (ADR-0264 Decision 11): these renders are of the BROWSER, which
		//now lives inside the library sheet behind *Browse a file…*.
		model.RomPicker.BrowseFile();
		Pump();
		model.RomPicker.Choose(model.RomPicker.Rows.First(r => r.Label == "Your games"));
		Pump();
		model.RomPicker.Choose(model.RomPicker.Rows.First(r => r.Label == "nes"));
		Pump();
		model.RomPicker.Choose(Assert.Single(model.RomPicker.Rows, r => r.Kind == RomPickerRowKind.Action));
		Pump();

		AssertSheetIsUp(window, model);
		Assert.Equal("Your games", model.RomPicker.PathText);
		Assert.DoesNotContain(model.RomPicker.Rows, r => r.Kind == RomPickerRowKind.Action);
		Assert.True(model.RomPicker.NoticeText.Length > 0, "the save left no notice");
		Assert.True(window.FindNamed<TextBlock>("RomPickerNotice").IsOnScreen(), "the notice line is not on screen");
		Assert.False(window.FindNamed<TextBlock>("RomPickerSearching").IsOnScreen());

		PlayerRender.Save(PlayerRender.Capture(window), "rom-picker-saved");
	}

	//(e) A folder with nothing openable: only the action row is in the list, and
	//the ring has to be on Back, not on the action - a stray Confirm on the action
	//row would silently repoint "Your games" away from a library that worked.
	[AvaloniaFact]
	public void A_folder_with_nothing_openable_focuses_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Path.Combine(_folder, "games");
		Directory.CreateDirectory(Path.Combine(root, "empty"));
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		ConfigManager.Config.Preferences.LibraryFolders = null;

		(MainWindow window, MainWindowViewModel model) = Show();
		model.RomPicker.SuggestionSource = _ => Array.Empty<RomPickerHit>();
		model.RomPicker.RunScanInline = true;
		model.OpenRomPicker();
		//#1032 (ADR-0264 Decision 11): these renders are of the BROWSER, which
		//now lives inside the library sheet behind *Browse a file…*.
		model.RomPicker.BrowseFile();
		Pump();
		model.RomPicker.Choose(model.RomPicker.Rows.First(r => r.Label == "Your games"));
		Pump();
		model.RomPicker.Choose(model.RomPicker.Rows.First(r => r.Label == "empty"));
		Pump();

		AssertSheetIsUp(window, model);
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "RomPickerBack",
			"an empty folder did not send the ring to Back");
		Assert.Single(model.RomPicker.Rows, r => r.Kind == RomPickerRowKind.Action);
		Assert.True(model.RomPicker.EmptyText.Length > 0, "the empty folder says nothing");
		//Nothing openable, so the action row is the whole list.
		Assert.DoesNotContain(model.RomPicker.Rows, r => r.Kind != RomPickerRowKind.Action);
		TextBlock sentence = window.FindNamed<TextBlock>("RomPickerEmpty");
		Assert.True(sentence.IsOnScreen(), "the empty sentence is not on screen");

		//And it sits UNDER the list rather than over it. This is the one folder
		//whose list is a single row, so it is the only state where the sentence and
		//a row compete for the same pixels - the first render of it had the sentence
		//drawn straight through "Make this my games folder".
		Button row = window.FindNamed<ItemsControl>("RomPickerList").FindAll<Button>().Single();
		Point? rowBottom = row.TranslatePoint(new Point(0, row.Bounds.Height), window);
		Point? sentenceTop = sentence.TranslatePoint(new Point(0, 0), window);
		Assert.NotNull(rowBottom);
		Assert.NotNull(sentenceTop);
		Assert.True(sentenceTop!.Value.Y >= rowBottom!.Value.Y,
			$"the empty sentence starts at y={sentenceTop.Value.Y}, above the row's bottom edge y={rowBottom.Value.Y}");

		PlayerRender.Save(PlayerRender.Capture(window), "rom-picker-empty");
	}

	//The text a person would read off the frame, in one place: TextBlock.Text for
	//what is on screen. A label that never got laid out is not in here.
	private static string[] VisibleTexts(Control root)
	{
		return root.FindAll<TextBlock>().Where(t => t.IsOnScreen()).Select(t => t.Text ?? "").ToArray();
	}
}
