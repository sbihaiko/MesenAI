using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1067 (a follow-up to #1039/#1057): AskShowing walks the grid it is handed - one
//ContainerFromIndex per tile the scan landed, up to twenty thousand - and the two
//events that fire it fire while the sheet is not showing the library at all: the
//grid is a live collection whatever the sheet's visibility, so a scan landing or a
//search under a closed sheet, or the browser taking the sheet over, still asks
//about every tile in it.
//
//What is asserted here is the walk itself, because nothing else can see it: the
//tiles that come out of a pass are handed to AskVisible, which drops them when the
//sheet is not showing the library (PlayerRomPickerViewModel.BoxArt.cs), so a closed
//sheet asks for no cover either way - the cost of the walk is the whole of the
//symptom, and PlayerRomPickerView.TilesExamined is where it is counted.
//
//The instrument is proved live in both cases before it is used as evidence: the
//sheet is up and has already walked the grid it filled, so the count that does NOT
//move afterwards is being read as a count, not as a constant zero.
[Collection(NativeCoreCollection.Name)]
public class PlayRomPickerAskTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;
	private readonly bool _confirmExitResetPower = ConfigManager.Config.Preferences.ConfirmExitResetPower;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1067-" + Guid.NewGuid().ToString("N"));

	public PlayRomPickerAskTests()
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
		prefs.GameFolder = _gameFolder ?? "";
		prefs.OverrideGameFolder = _overrideGameFolder;
		prefs.ConfirmExitResetPower = _confirmExitResetPower;
		ConfigManager.Config.Save();

		try {
			Directory.Delete(_folder, true);
		} catch {
			//A case that failed before it built its tree leaves nothing to remove.
		}
	}

	//A games folder with `count` ROMs under it and the settings pointed at it, so the
	//configured folder IS the library folder on any machine. Every ROM is its own
	//byte, so the grid is a grid of distinct games rather than one dump repeated.
	private void LibraryRoot(int count)
	{
		string root = Path.Combine(_folder, "games");
		string nes = Path.Combine(root, "NES");
		Directory.CreateDirectory(nes);
		for(int i = 0; i < count; i++) {
			byte[] rom = SyntheticNrom.Build();
			rom[32 + i] = 0x01;
			File.WriteAllBytes(Path.Combine(nes, $"Game {i:00} (USA).nes"), rom);
		}
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
	}

	//The library sheet, up, with its grid filled: the scan runs in the Open() turn,
	//so the tiles are all there before a case reads the walk.
	private (MainWindow Window, MainWindowViewModel Model, PlayerRomPickerView View) ShowLibrary()
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

		model.RomPicker.RunLibraryScanInline = true;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		Assert.True(model.RecentGames.ShowFirstRunHome, "the home is not the first-run one, so this case would prove nothing");

		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		window.FindNamed<Button>("PlayHomeOpenRomPrimary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Pump();
		Assert.True(model.RomPicker.IsVisible, "the library sheet did not open");
		WaitFor(() => model.RomPicker.Tiles.Count > 0, "the library scan landed no tiles");

		return (window, model, window.FindNamed<PlayerRomPickerView>("PlayerRomPickerHost"));
	}

	//The grid changing under a sheet that is not showing the library: the same
	//notification the open sheet answers with a pass (OnTilesChanged -> PostAskShowing).
	private static void Regrid(PlayerRomPickerViewModel picker) => picker.Tiles.Add(picker.Tiles[0]);

	//#1067: a closed sheet walks nothing. The library is up and the walk has already
	//happened once; closing the sheet and changing the grid under it must not walk it
	//again - this is the ContainerFromIndex loop over the whole scan, and it runs on
	//the layout the closed sheet still gets.
	[AvaloniaFact]
	public void A_closed_sheet_walks_no_tile_of_the_grid()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot(count: 24);
		(MainWindow _, MainWindowViewModel model, PlayerRomPickerView view) = ShowLibrary();

		//The instrument is live: the pass that filled the grid walked the tiles it
		//landed. Without this, the count that stays put below would prove nothing.
		WaitFor(() => view.TilesExamined > 0, "the sheet never walked the grid it is showing");

		//The real dismiss - what Back from the library does (PlayerRomPickerViewModel.Back).
		model.RomPicker.Back();
		Pump();
		Assert.False(model.RomPicker.IsVisible, "the library sheet did not close");
		Assert.True(model.RomPicker.Tiles.Count > 0, "the grid emptied on close, so there was nothing left for a walk to find");
		int walkedWhileClosedTheFirstTime = view.TilesExamined;

		Regrid(model.RomPicker);
		Pump();

		Assert.Equal(walkedWhileClosedTheFirstTime, view.TilesExamined);
	}

	//#1067: the second way the sheet is not the library - the folder browser, which is
	//the same sheet (ADR-0256 Decision 9/Decision 11) with the grid out of sight and
	//its tiles still in the collection. Walking them there answers a question nobody
	//asked, so it is not walked either.
	[AvaloniaFact]
	public void The_folder_browser_walks_no_tile_of_the_library()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot(count: 24);
		(MainWindow _, MainWindowViewModel model, PlayerRomPickerView view) = ShowLibrary();

		WaitFor(() => view.TilesExamined > 0, "the sheet never walked the grid it is showing");

		model.RomPicker.BrowseFile();
		Pump();
		Assert.Equal(RomPickerMode.BrowseFile, model.RomPicker.Mode);
		Assert.True(model.RomPicker.Tiles.Count > 0, "the browser emptied the grid, so there was nothing left for a walk to find");
		int walkedOnTheLibrary = view.TilesExamined;

		Regrid(model.RomPicker);
		Pump();

		Assert.Equal(walkedOnTheLibrary, view.TilesExamined);
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
}
