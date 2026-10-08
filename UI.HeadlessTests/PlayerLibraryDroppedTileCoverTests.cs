using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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

//#1065 review finding 2: a query that narrows the grid drops a tile while the
//rest stay on the sheet, so the rebuild's Clear no longer owns that tile's
//decoded screenshot. The drop in PlayerRomPickerViewModel.Search has to hand it
//back (_coverArt.Release) and forget the downloaded cover (ForgetCover).
//
//The seam is the Bitmap behind the tile's ImageBrush: it is the allocation the
//ledger owns, and reading it after Dispose throws - nothing is mocked.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryDroppedTileCoverTests : IDisposable
{
	private static readonly byte[] Screenshot =
		Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAQAAAADCAIAAAA7ljmRAAAAEElEQVR42mM4IScHRww4OQD1xwwx7+oCFgAAAABJRU5ErkJggg==");

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;
	private readonly List<string>? _libraryFolders = ConfigManager.Config.Preferences.LibraryFolders;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _recentFolderOverride = ConfigManager.RecentGamesFolderOverride;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1065-drop-" + Guid.NewGuid().ToString("N"));

	public PlayerLibraryDroppedTileCoverTests()
	{
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			Stopwatch clock = Stopwatch.StartNew();
			while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
				Thread.Sleep(10);
			}
			Assert.False(EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
		}
		Directory.CreateDirectory(Path.Combine(_folder, "RecentGames"));
		ConfigManager.RecentGamesFolderOverride = Path.Combine(_folder, "RecentGames");
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
		prefs.ConfirmExitResetPower = _confirm;
		ConfigManager.Config.Save();
		ConfigManager.RecentGamesFolderOverride = _recentFolderOverride;

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

	//Two games, both with a screenshot in the Recent list, so both tiles hold a
	//decoded Bitmap.
	private (string Contra, string Metroid) SeedLibrary()
	{
		string root = Path.Combine(_folder, "games");
		string nes = Path.Combine(root, "NES");
		Directory.CreateDirectory(nes);
		string contra = Path.Combine(nes, "Contra (U) [!].nes");
		string metroid = Path.Combine(nes, "Metroid (USA).nes");
		File.WriteAllBytes(contra, SyntheticNrom.Build());
		File.WriteAllBytes(metroid, SyntheticNrom.Build());
		foreach((string name, string path) in new[] { ("Contra (U) [!]", contra), ("Metroid (USA)", metroid) }) {
			using ZipArchive zip = ZipFile.Open(Path.Combine(ConfigManager.RecentGamesFolder, name + ".rgd"), ZipArchiveMode.Create);
			using(Stream s = zip.CreateEntry("RomInfo.txt").Open()) {
				using StreamWriter w = new(s);
				w.Write(name + "\n" + path + "\n");
			}
			using(Stream s = zip.CreateEntry("Screenshot.png").Open()) {
				s.Write(Screenshot, 0, Screenshot.Length);
			}
		}
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		ConfigManager.Config.Preferences.LibraryFolders = null;
		return (contra, metroid);
	}

	[AvaloniaFact]
	public void A_tile_dropped_by_a_narrower_query_hands_its_screenshot_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(string contraPath, string metroidPath) = SeedLibrary();

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		model.RomPicker.RunLibraryScanInline = true;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		model.OpenRomPicker();
		Pump();
		WaitFor(() => model.RomPicker.Tiles.Count == 2, $"the grid never filled ({model.RomPicker.Tiles.Count})");

		PlayerLibraryTile contra = model.RomPicker.Tiles.Single(t => t.Path == contraPath);
		PlayerLibraryTile metroid = model.RomPicker.Tiles.Single(t => t.Path == metroidPath);
		Bitmap contraArt = Assert.IsType<Bitmap>(Assert.IsType<ImageBrush>(contra.Cover).Source);
		Bitmap metroidArt = Assert.IsType<Bitmap>(Assert.IsType<ImageBrush>(metroid.Cover).Source);
		Assert.Equal(4, contraArt.PixelSize.Width);

		model.RomPicker.SearchQuery = "Metroid";
		Pump();

		Assert.DoesNotContain(contra, model.RomPicker.Tiles);
		Assert.Throws<ObjectDisposedException>(() => contraArt.PixelSize);
		//The tile that stayed keeps drawing its picture.
		Assert.Equal(4, metroidArt.PixelSize.Width);
	}
}
