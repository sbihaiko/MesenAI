using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0249 Decision 5 / ADR-0264 Decision 12: the library sheet, rendered as a
//Skia PNG beside the W-P* renders. W-P19 is the target picture the flat library
//is built against, so this case is the one the render gate holds it to: it
//renders the sheet under the name `W-P19`, which is also what makes
//PlayerRender.Save write `W-P19.wireframe.md` next to the PNG.
//
//The case asserts the render's identity before it saves - the header, a tile
//per game, the title and the console tag on each - so a PNG that stops being a
//picture of the library fails here rather than being noticed by whoever opens
//it next. It is not a substitute for looking at it.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryRenderTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;
	//#1036: the render is built from GameFolder, so a LibraryFolders list left in
	//the real config by anyone who used the feature would win over it and the
	//render's own identity asserts would fail on that machine only. Held here and
	//put back in Dispose, exactly as PlayerLibraryFoldersTests does.
	private readonly List<string>? _libraryFolders = ConfigManager.Config.Preferences.LibraryFolders;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-library-renders-" + Guid.NewGuid().ToString("N"));

	public PlayerLibraryRenderTests()
	{
		Directory.CreateDirectory(_folder);
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.GameFolder = "";
		prefs.OverrideGameFolder = false;
		//First run: the render is of the tree this case builds, never of a list a
		//previous run of the feature left in the config.
		prefs.LibraryFolders = null;
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

		//#999: the stale recents go BEFORE the window starts (the window's own
		//startup Init reads them, and a second Init in the same mode with entries
		//on screen returns early).
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");

		model.RomPicker.RunLibraryScanInline = true;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		Assert.True(model.RecentGames.ShowFirstRunHome, "the home is not the first-run one, so the render would be of the wrong surface");
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

	private static string[] VisibleTexts(Control root)
	{
		return root.FindAll<TextBlock>().Where(t => t.IsOnScreen()).Select(t => t.Text ?? "").ToArray();
	}

	//A library with enough games to fill more than one row of the grid, across
	//consoles - so the render shows what the sheet is for: a shelf, not a file
	//list. The nested `NES/` and `Handheld/GB` folders are the shapes the scan
	//has to flatten.
	private string Library()
	{
		string root = Path.Combine(_folder, "games");
		string nes = Path.Combine(root, "NES");
		string nesAction = Path.Combine(nes, "Action");
		string gb = Path.Combine(root, "Handheld", "GB");
		string sms = Path.Combine(root, "Sega");
		foreach(string folder in new[] { nes, nesAction, gb, sms }) {
			Directory.CreateDirectory(folder);
		}
		foreach(string name in new[] { "Castlevania (U) [!]", "The Legend of Zelda (USA)", "Super Mario Bros. 3 (Europe)", "Metroid (U)" }) {
			File.WriteAllBytes(Path.Combine(nes, name + ".nes"), SyntheticNrom.Build());
		}
		File.WriteAllBytes(Path.Combine(nesAction, "Mega Man 2 (U) (Rev 1).nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(nesAction, "Contra (U) [!].nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(gb, "Tetris (World) (Rev A).gb"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(gb, "Super Mario Land (World).gb"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(gb, "Donkey Kong (Japan, USA).gb"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(sms, "Sonic The Hedgehog (Europe, Brazil).sms"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(sms, "Alex Kidd in Miracle World (USA, Europe).sms"), SyntheticNrom.Build());

		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		return root;
	}

	//#1032: the render gate the review found missing. PlayerRender.Save only
	//*writes* the wireframe report, and a failure inside it is logged rather
	//than thrown, so before this the W-P19 frame was captured and never
	//compared - a wrong picture kept the suite green. This is the wiring the
	//other render-gated wireframes use (PlayerThemeRenderTests
	//.AssertWireframeRegions): compare the regions against the wireframe, gate
	//the deviations the render is known to carry, and fail on what is left.
	private static void AssertMatchesWireframe(Bitmap frame, string wId, IReadOnlyList<KnownDeviation> known)
	{
		string wireframe = PlayerRender.WireframePath(wId);
		Assert.True(File.Exists(wireframe), $"{wId} has no wireframe at {wireframe}, so there is nothing to compare the render against");
		RgbFrame fresh = PlayerRender.Rgb(frame);
		IReadOnlyList<RegionResult> results = PlayerWireframe.Compare(wId, fresh, RgbFrame.FromPng(wireframe));
		List<string> violations = PlayerWireframe.Gate(wId, results, known).ToList();
		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	//What this render is known to differ from W-P19 on, each with the cause that
	//makes it a known one. Their home is PlayerWireframe.Known, beside the other
	//renders' entries; they are built here because this file is the one the
	//slice that owns W-P19 edits. The two content kinds ratchet - their cause is
	//layout, so they must keep failing until the slices that close them land,
	//and the gate says so the day they stop. The status line's ink box is the
	//P1-P4 port chips every render draws and no wireframe does (#951), the
	//fixture kind W-P1, W-P2 and W-P4 already tolerate. Off macOS the title
	//bar's ink box moves because the shell bar is not inset for the traffic
	//lights (#968), the host deviation the shell's own render gate carries.
	private static IReadOnlyList<KnownDeviation> W_P19Deviations()
	{
		List<KnownDeviation> known = new() {
			new("content", PlayerWireframe.InkBox,
				"the tracer's sheet: the search field and the console filter of #1034 and #1035 are not in it", true),
			new("content", PlayerWireframe.TextLines,
				"the tracer's sheet: one ink band where the wireframe has ten, the same missing header", true),
			new("status line", PlayerWireframe.InkBox,
				"the P1-P4 port chips the wireframe does not draw", false),
		};
		if(!OperatingSystem.IsMacOS()) {
			known.Add(new("title bar", PlayerWireframe.InkBox, "no traffic-light inset off macOS", false));
		}
		return known;
	}

	//The wireframe's own picture, rendered: the header, the grid of vertical
	//console-coloured tiles, and the title and console tag under each.
	[AvaloniaFact]
	public void W_P19_the_library_grid()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Library();

		(MainWindow window, MainWindowViewModel model) = Show();
		model.OpenRomPicker();
		Pump();

		Assert.True(model.RomPicker.IsVisible, "the library is not open, so this render would be of the home");
		Assert.True(window.FindNamed<Border>("PlayerRomPickerSheet").IsOnScreen(), "the sheet is not on screen");
		//Eleven games, none of them a row: the folders shaped the scan.
		Assert.Equal(11, model.RomPicker.Tiles.Count);
		Assert.Equal("Your library · 11 games in 1 folder", model.RomPicker.HeaderText);
		Assert.True(window.FindNamed<ItemsControl>("RomPickerGrid").IsOnScreen(), "the grid is not on screen");
		Assert.True(window.FindNamed<Button>("RomPickerBrowseFile").IsOnScreen(), "Browse a file… is not on the sheet");
		Assert.True(window.FindNamed<Button>("RomPickerBack").IsOnScreen(), "Back is not on the sheet");

		//A tile carries a clean title - the tags are gone - and its console.
		string[] texts = VisibleTexts(window);
		Assert.Contains("Castlevania", texts);
		Assert.Contains("The Legend of Zelda", texts);
		Assert.Contains("NES", texts);
		Assert.Contains("Game Boy", texts);
		Assert.Contains("Master System", texts);
		Assert.DoesNotContain("Castlevania (U) [!].nes", texts);

		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "W-P19");
		AssertMatchesWireframe(frame, "W-P19", W_P19Deviations());
	}

	//The W-P19 fixture plus the wireframe's own `zel` titles, so the query really
	//narrows: W-P19b draws three Zelda tiles out of a library of many, and a
	//library of exactly three would make the query a no-op the picture could not
	//tell apart from an unfiltered grid.
	private string ZeldaLibrary()
	{
		string root = Library();
		string nes = Path.Combine(root, "NES");
		string gbc = Path.Combine(root, "Handheld", "GBC");
		Directory.CreateDirectory(gbc);
		File.WriteAllBytes(Path.Combine(nes, "Zelda II - The Adventure of Link (USA).nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(gbc, "The Legend of Zelda - Oracle of Ages (USA).gbc"), SyntheticNrom.Build());
		return root;
	}

	//What this render is known to differ from W-P19b on, each with the cause that
	//makes it a known one, and each measured rather than asserted. The sheet holds
	//the wireframe's own 620 px height in library mode (#1062), so the colour of
	//the content region passes (ΔE 0.0) and has no entry: a sheet that broke or
	//vanished fails there. The two layout kinds stay ratchets with their measured
	//cause - the console filter of #1034 and the *Library folders…* row of #1036
	//are not in the header yet (ink box off 55 px, one of the wireframe's seven text
	//lines matched). The status line's ink box is the P1-P4 port chips
	//every render draws and no wireframe does (#951); off macOS the title bar's ink
	//box moves because the shell bar is not inset for the traffic lights (#968).
	private static IReadOnlyList<KnownDeviation> W_P19bDeviations()
	{
		List<KnownDeviation> known = new() {
			new("content", PlayerWireframe.InkBox,
				"the console filter of #1034 and the folder row of #1036 missing from the header (box off 55 px)", true),
			new("content", PlayerWireframe.TextLines,
				"the same missing filter and folder row: 1 of the wireframe's 7 lines matched", true),
			new("status line", PlayerWireframe.InkBox,
				"the P1-P4 port chips the wireframe does not draw", false),
		};
		if(!OperatingSystem.IsMacOS()) {
			known.Add(new("title bar", PlayerWireframe.InkBox, "no traffic-light inset off macOS", false));
		}
		return known;
	}

	//#1033 (ADR-0264 Decision 12): W-P19b is W-P19's surface with search active -
	//the grid narrowed, the box holding the query `zel`, and the header still
	//reading the LIBRARY's own count (Decision 8). The case types the query rather
	//than posing the grid: a picture of a narrowed library that never asked the box
	//would stay green with the search wired to nothing. Rendered and compared
	//beside W-P19, so the render gate holds the sheet to the wireframe PR #1040
	//draws.
	[AvaloniaFact]
	public void W_P19b_the_library_narrowed_by_a_query()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ZeldaLibrary();

		(MainWindow window, MainWindowViewModel model) = Show();
		model.OpenRomPicker();
		Pump();
		Assert.True(model.RomPicker.IsVisible, "the library is not open, so this render would be of the home");

		//Through the control, which is the path a keyboard takes: the binding is
		//what carries the query to the grid.
		TextBox box = window.FindNamed<TextBox>("RomPickerSearch");
		box.Text = "zel";
		Pump();
		box.Focus(NavigationMethod.Directional);
		Pump();

		//What the picture has to be, asserted before it is saved: a PNG that stops
		//being a picture of the narrowed library fails here rather than being
		//noticed by whoever opens it next.
		Assert.True(window.FindNamed<Border>("PlayerRomPickerSheet").IsOnScreen(), "the sheet is not on screen");
		Assert.Equal("Your library · 13 games in 1 folder", model.RomPicker.HeaderText);
		Assert.Equal("zel", box.Text ?? "");
		Assert.Equal(
			new[] { "The Legend of Zelda", "The Legend of Zelda - Oracle of Ages", "Zelda II - The Adventure of Link" },
			model.RomPicker.Tiles.Select(t => t.Title).OrderBy(t => t, StringComparer.Ordinal).ToArray());
		Assert.True(window.FindNamed<ItemsControl>("RomPickerGrid").IsOnScreen(), "the grid is not on screen");
		Assert.True(window.FindNamed<Button>("RomPickerSearchClear").IsOnScreen(), "Clear is not on the sheet");

		string[] texts = VisibleTexts(window);
		Assert.Contains("The Legend of Zelda", texts);
		Assert.Contains("Zelda II - The Adventure of Link", texts);
		//The games the query left out are not drawn: the render is of the narrowed
		//grid and not of the whole library with a box on top.
		Assert.DoesNotContain("Metroid", texts);
		Assert.DoesNotContain("Castlevania", texts);

		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "W-P19b");
		AssertMatchesWireframe(frame, "W-P19b", W_P19bDeviations());
	}
}
