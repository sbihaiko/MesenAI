using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

//#1035 (ADR-0264 Decision 6.3): a game the player has already run shows its own
//screenshot where the grid would otherwise draw the console-coloured generic
//cover. The rule that finds the screenshot - the ROM's full path in the Recent
//record - is host-free and pinned in UI.Tests/Play/RecentCoverIndexTests; what
//is proved here is the sheet: the tile the player sees draws that picture, and
//the game without a Recent entry keeps exactly the cover it had.
//
//The screenshot bytes go into the `.rgd` as real PNGs of two different sizes,
//so the case can name which picture a tile ended up with instead of merely
//asserting that it has one. The second `.rgd` names a ROM that is NOT in the
//library: a grid that matched "some recent file" rather than this game's path
//would hand that picture to the wrong tile, and this is what catches it.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryRecentCoversTests : IDisposable
{
	//Two real, decodable PNGs and the pixel sizes they are: 4x3 for the game
	//that IS in the library, 7x5 for the one that is not.
	private static readonly byte[] ContraScreenshot =
		Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAQAAAADCAIAAAA7ljmRAAAAEElEQVR42mM4IScHRww4OQD1xwwx7+oCFgAAAABJRU5ErkJggg==");

	private static readonly byte[] ElsewhereScreenshot =
		Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFCAIAAAAG+GGPAAAAEUlEQVR42mOQOyGHiRhoJAoAo+sjjVvdn4IAAAAASUVORK5CYII=");

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;
	//#1052 review finding 2: ShowLibrary turns the quit confirmation off so a
	//window can be closed without the player being asked, and Dispose has to
	//hand every preference this class touched back - this one included, or the
	//cases that follow in the shared collection inherit it, on disk too.
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _recentFolderOverride = ConfigManager.RecentGamesFolderOverride;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1035-covers-" + Guid.NewGuid().ToString("N"));
	private readonly string _recent;

	public PlayerLibraryRecentCoversTests()
	{
		//#790: the core is process-global and a case that ran a game leaves its
		//console loaded - the collection is shared with those cases.
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitUntilStopped();
		}
		Directory.CreateDirectory(_folder);
		//The Recent folder is pointed at this case's own tree, so the seeded
		//records are the only ones there and the player's real Recent list is
		//never read - nor written to.
		_recent = Path.Combine(_folder, "RecentGames");
		Directory.CreateDirectory(_recent);
		ConfigManager.RecentGamesFolderOverride = _recent;
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
		prefs.GameFolder = _gameFolder ?? "";
		prefs.OverrideGameFolder = _overrideGameFolder;
		prefs.ConfirmExitResetPower = _confirm;
		ConfigManager.Config.Save();
		ConfigManager.RecentGamesFolderOverride = _recentFolderOverride;

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

	//A `.rgd` as the Core writes it: a zip holding the screenshot and a
	//RomInfo.txt whose second line is the ROM path (PlayRecentGameFailure.ParseRomInfo).
	private static void WriteRecent(string recentName, string romPath, byte[] screenshot)
	{
		using ZipArchive zip = ZipFile.Open(Path.Combine(ConfigManager.RecentGamesFolder, recentName + ".rgd"), ZipArchiveMode.Create);
		using(Stream s = zip.CreateEntry("RomInfo.txt").Open()) {
			using StreamWriter w = new(s);
			w.Write(recentName + "\n" + romPath + "\n");
		}
		using(Stream s = zip.CreateEntry("Screenshot.png").Open()) {
			s.Write(screenshot, 0, screenshot.Length);
		}
	}

	//Two games under the library folder, one of them the game the case has played.
	private string Library()
	{
		string root = Path.Combine(_folder, "games");
		string nes = Path.Combine(root, "NES");
		Directory.CreateDirectory(nes);
		File.WriteAllBytes(Path.Combine(nes, "Contra (U) [!].nes"), SyntheticNrom.Build());
		File.WriteAllBytes(Path.Combine(nes, "Metroid (USA).nes"), SyntheticNrom.Build());
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		return root;
	}

	//The Recent list of a player who ran Contra once - and who also ran a game
	//from somewhere else entirely, which is the entry a path match must NOT take.
	private string SeedRecents(string root)
	{
		string contra = Path.Combine(root, "NES", "Contra (U) [!].nes");
		WriteRecent("Contra (U) [!]", contra, ContraScreenshot);

		string elsewhere = Path.Combine(_folder, "elsewhere", "Another Game (Japan).nes");
		Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
		File.WriteAllBytes(elsewhere, SyntheticNrom.Build());
		WriteRecent("Another Game (Japan)", elsewhere, ElsewhereScreenshot);
		return contra;
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowLibrary()
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

		model.RomPicker.RunLibraryScanInline = true;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();

		model.OpenRomPicker();
		Pump();
		WaitFor(() => model.RomPicker.Tiles.Count == 2, $"the grid never filled ({model.RomPicker.Tiles.Count})");
		return (window, model);
	}

	//#1035 (ADR-0264 Decision 6.3): the sheet draws the screenshot of a game the
	//player has run, and the tile's generic cover does not sit over it - the
	//title is written ON the colour, and a picture says the rest.
	[AvaloniaFact]
	public void A_played_game_shows_its_own_screenshot_as_the_tile_cover()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Library();
		string contraPath = SeedRecents(root);

		(MainWindow window, MainWindowViewModel model) = ShowLibrary();

		PlayerLibraryTile contra = model.RomPicker.Tiles.Single(t => t.Path == contraPath);
		ImageBrush cover = Assert.IsType<ImageBrush>(contra.Cover);
		Assert.Equal(new PixelSize(4, 3), Assert.IsType<Bitmap>(cover.Source).PixelSize);
		Assert.False(contra.ShowsTitleOnCover, "the title is still stamped across the screenshot");

		//And the picture is on the screen, not only in the view-model: the cover
		//Border the player sees is the one carrying this brush.
		Border rendered = CoverBorder(window.FindNamed<ItemsControl>("RomPickerGrid"), contra);
		Assert.Same(contra.Cover, rendered.Background);
		Assert.False(OnCoverTitle(rendered).IsVisible, "the on-cover title is visible over the screenshot");
	}

	//#1035: a game with no Recent entry keeps today's cover - the console
	//colour, with the title on it. The Recent list holding a DIFFERENT game's
	//screenshot changes nothing for it.
	[AvaloniaFact]
	public void A_game_with_no_recent_entry_keeps_the_console_cover()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Library();
		string contraPath = SeedRecents(root);

		(MainWindow window, MainWindowViewModel model) = ShowLibrary();

		PlayerLibraryTile metroid = model.RomPicker.Tiles.Single(t => t.Title == "Metroid");
		Assert.NotEqual(contraPath, metroid.Path);
		Assert.IsType<SolidColorBrush>(metroid.Cover);
		Assert.True(metroid.ShowsTitleOnCover, "a tile with no screenshot must keep the title on its cover");

		Border rendered = CoverBorder(window.FindNamed<ItemsControl>("RomPickerGrid"), metroid);
		Assert.Equal(metroid.Cover, rendered.Background);
		Assert.True(OnCoverTitle(rendered).IsVisible, "the on-cover title went missing on the generic cover");
	}

	//#1052 review finding 2: this case turns the quit confirmation off so a
	//window can be closed without the player being asked, and the next case in
	//the shared collection must not inherit that - nor may the file on disk.
	//
	//A second instance of this fixture is the leak in miniature: what the nested
	//case finds when it is constructed is what the next case would find, and one
	//Dispose is exactly the call the framework makes between cases.
	[AvaloniaFact]
	public void A_case_that_opened_the_library_puts_the_quit_confirmation_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		//True is the value that must come back: the setting's own default is
		//false, so a case that leaks would leave this assertion nothing to
		//disagree with. The host's own value goes back at the end - a case that
		//lectures about leaking may not leak itself.
		bool ambient = ConfigManager.Config.Preferences.ConfirmExitResetPower;
		ConfigManager.Config.Preferences.ConfirmExitResetPower = true;

		PlayerLibraryRecentCoversTests probe = new();
		try {
			string root = probe.Library();
			probe.SeedRecents(root);
			probe.ShowLibrary();
			Assert.False(ConfigManager.Config.Preferences.ConfirmExitResetPower,
				"opening the library never turned the quit confirmation off, so this case proves nothing");

			probe.Dispose();

			Assert.True(ConfigManager.Config.Preferences.ConfirmExitResetPower,
				"opening the library left the quit confirmation off for the cases that follow");
			//The restore has to precede the Save the same Dispose makes: the file
			//is what the next process reads, and what the next case would inherit.
			string saved = File.ReadAllText(ConfigManager.ConfigFile);
			Assert.Contains("\"ConfirmExitResetPower\": true", saved);
		} finally {
			probe.Dispose();
			ConfigManager.Config.Preferences.ConfirmExitResetPower = ambient;
			ConfigManager.Config.Save();
		}
	}

	//#1052 review finding 3: the grid decodes new pictures on every visit, and
	//the cover the previous visit drew must not be handed back while the
	//compositor can still be holding its last frame. Closing the sheet and
	//opening it again is that sequence - the Clear lands between two frames the
	//same tile has been drawn in - so the reopened grid has to draw a live
	//picture, and the render pass after it has to complete without the release
	//of the previous visit's bitmap reaching through the brush still on screen.
	[AvaloniaFact]
	public void Reopening_the_library_draws_a_live_picture_on_the_tile()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Library();
		string contraPath = SeedRecents(root);

		(MainWindow window, MainWindowViewModel model) = ShowLibrary();
		PlayerLibraryTile first = model.RomPicker.Tiles.Single(t => t.Path == contraPath);
		Assert.IsType<Bitmap>(Assert.IsType<ImageBrush>(first.Cover).Source);
		//The frame the compositor is holding when the sheet closes: the first
		//visit's cover has been drawn at least once.
		PlayerRender.Capture(window).Dispose();

		model.RomPicker.Back();
		Pump();
		model.OpenRomPicker();
		Pump();
		WaitFor(() => model.RomPicker.Tiles.Count == 2, $"the grid never came back ({model.RomPicker.Tiles.Count})");

		//The render pass over the reopened grid - where a picture released in
		//the same turn as the Clear would be reached through the last frame's
		//brush.
		PlayerRender.Capture(window).Dispose();

		PlayerLibraryTile second = model.RomPicker.Tiles.Single(t => t.Path == contraPath);
		Bitmap cover = Assert.IsType<Bitmap>(Assert.IsType<ImageBrush>(second.Cover).Source);
		Assert.Equal(new PixelSize(4, 3), cover.PixelSize);
		//Live, not merely present: a disposed Bitmap still answers PixelSize,
		//and only a read of its pixels says the picture is still there.
		PlayerRender.Rgb(cover);
		Border rendered = CoverBorder(window.FindNamed<ItemsControl>("RomPickerGrid"), second);
		Assert.Same(second.Cover, rendered.Background);
	}

	//The cover Border the template drew for one tile, found by the tile it is
	//bound to: the assertion is about THIS game's picture on the screen, not
	//about whichever cover happened to be drawn first.
	private static Border CoverBorder(ItemsControl grid, PlayerLibraryTile tile)
	{
		WaitFor(() => CoverBorders(grid).Any(b => ReferenceEquals(b.DataContext, tile)),
			"the grid never drew a cover for " + tile.Title);
		return CoverBorders(grid).First(b => ReferenceEquals(b.DataContext, tile));
	}

	private static Border[] CoverBorders(ItemsControl grid)
	{
		return grid.FindAll<Border>().Where(b => b.Name == "TileCover").ToArray();
	}

	private static TextBlock OnCoverTitle(Border cover)
	{
		return cover.FindAll<TextBlock>().First(t => t.Classes.Contains("library-cover-title"));
	}
}
