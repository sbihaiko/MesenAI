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

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-library-renders-" + Guid.NewGuid().ToString("N"));

	public PlayerLibraryRenderTests()
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
		Assert.Equal("Your library", model.RomPicker.HeaderText);
		Assert.Equal("11 games in 1 folder", model.RomPicker.CountText);
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

		PlayerRender.Save(PlayerRender.Capture(window), "W-P19");
	}
}
