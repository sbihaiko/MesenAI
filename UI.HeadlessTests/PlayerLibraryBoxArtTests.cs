using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

//#1039 (ADR-0265): the library's tiles draw the cover the box-art cache holds.
//The rules of the cache are host-free in UI.Tests/BoxArt (BoxArtCacheTests, and
//the key one tile's call is made under in BoxArtLibraryTests); what is proved
//HERE is the wiring the player sees - a cover that is already in the player's
//cache reaches its tile while the sheet is up, a tile with no art keeps the
//generic cover it was built with and the sheet never waits on the network, only
//the tiles that fit the sheet are asked about, the switch in Settings › System
//turns the requests off, and the pad reaches that switch like every other row.
//
//The transport is a fake in every case - the adapter that opens a socket is not
//what any of this is about - and the chain around it (the cache, the SHA-1 cache,
//the name table) is the shipped one, in temp folders.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryBoxArtTests : IDisposable
{
	//A real, decodable 1x1 PNG: the bytes of a cover that is already on the
	//player's disk before the sheet opens. A literal rather than a file in the
	//repository on purpose - ADR-0265 section 10 keeps every image out of the tree,
	//and a test fixture is the one place that rule is easiest to forget.
	private const string PngBase64 =
		"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;
	private readonly bool _downloadBoxArt = ConfigManager.Config.Preferences.DownloadBoxArt;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1039-" + Guid.NewGuid().ToString("N"));
	private readonly string _covers;
	private readonly string _hashes;

	public PlayerLibraryBoxArtTests()
	{
		Directory.CreateDirectory(_folder);
		_covers = Directory.CreateDirectory(Path.Combine(_folder, "cache")).FullName;
		_hashes = Directory.CreateDirectory(Path.Combine(_folder, "hashes")).FullName;
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
		prefs.DownloadBoxArt = _downloadBoxArt;
		ConfigManager.Config.Save();

		try {
			Directory.Delete(_folder, true);
		} catch {
			//A case that failed before it built its tree leaves nothing to remove.
		}
	}

	//A games folder with `count` ROMs under it and the settings pointed at it, so
	//the configured folder IS the library folder on any machine.
	private string LibraryRoot(int count = 3)
	{
		string root = Path.Combine(_folder, "games");
		string nes = Path.Combine(root, "NES");
		Directory.CreateDirectory(nes);
		for(int i = 0; i < count; i++) {
			//One byte of PRG differs per ROM, so each game hashes to its own SHA-1:
			//the collection is keyed by the dump (ADR-0265 section 3), and a case
			//about "the first tile" would prove nothing if all forty were one dump.
			//The offset is well clear of the reset vector the synthetic ROM's own
			//behaviour lives in - none of these games is ever run.
			byte[] rom = SyntheticNrom.Build();
			rom[32 + i] = 0x01;
			File.WriteAllBytes(Path.Combine(nes, $"Game {i:00} (USA).nes"), rom);
		}
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		return root;
	}

	private string RomPath(string fileName) => Path.Combine(_folder, "games", "NES", fileName);

	//The first-run home, up and focused, with the library scan running in the
	//Open() turn so the grid is complete before a case asserts anything about it.
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

		model.RomPicker.RunLibraryScanInline = true;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		Assert.True(model.RecentGames.ShowFirstRunHome, "the home is not the first-run one, so this case would prove nothing");
		return (window, model);
	}

	//The home's own action, pressed: the library is up with its grid filled. The
	//cover source has to be in place BEFORE this, because the tiles are asked about
	//as they land.
	private void OpenLibrary(MainWindow window, MainWindowViewModel model)
	{
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		window.FindNamed<Button>("PlayHomeOpenRomPrimary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Pump();
		Assert.True(model.RomPicker.IsVisible, "the library sheet did not open");
		WaitFor(() => model.RomPicker.Tiles.Count > 0, "the library scan landed no tiles");
	}

	//The chain the app builds (MainWindowViewModel.BoxArt), with the transport
	//swapped for a fake: the cache, the SHA-1 cache and the name table are the
	//shipped ones, in temp folders.
	private BoxArtLibrary Chain(RecordingSender sender, Func<string, NoIntroRomName?> names, BoxArtCacheOptions? options = null) =>
		new(new BoxArtCache(sender.Send, _covers, options), new RomHashCache(_hashes), names);

	//The SHA-1 the collection is keyed by, read from the contract the shipped chain
	//reads it from, lowercased the way the cache's own file names are.
	private string Sha1(string romPath) =>
		new RomHashCache(_hashes).GetSha1Async(romPath, RomConsole.Nes).GetAwaiter().GetResult().ToLowerInvariant();

	//A cover that is already on the player's disk, where the cache's layout says a
	//box art lives: `<cache>/nes/<sha1>.boxart.png`. Written here rather than through
	//the cache's own writer - seeding the disk with the code under test would prove
	//only that the two halves agree with each other.
	private string SeedBoxArt(string romPath)
	{
		string folder = Directory.CreateDirectory(Path.Combine(_covers, "nes")).FullName;
		string path = Path.Combine(folder, Sha1(romPath) + ".boxart.png");
		File.WriteAllBytes(path, Convert.FromBase64String(PngBase64));
		return path;
	}

	private static PlayerLibraryTile TileFor(MainWindowViewModel model, string fileName)
	{
		return model.RomPicker.Tiles.First(t => Path.GetFileName(t.Path) == fileName);
	}

	//The realized control for one tile - the half of "it is on screen" that the
	//view-model object cannot say.
	private static Image CoverImage(MainWindow window, string romPath)
	{
		ItemsControl grid = window.FindNamed<ItemsControl>("RomPickerGrid");
		Image? image = grid.GetVisualDescendants().OfType<Image>()
			.FirstOrDefault(i => i.DataContext is PlayerLibraryTile tile && tile.Path == romPath);
		if(image == null) {
			throw new XunitException($"the tile for {romPath} draws no cover image at all");
		}
		return image;
	}

	//#1039 / ADR-0265 sections 4, 6 and 9, the delivered half: a cover already in
	//the player's cache is drawn on its own tile - the picture the cache holds, on
	//screen, for the cost of one disk read.
	[AvaloniaFact]
	public void A_cached_box_art_is_drawn_on_its_tile()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();
		string rom = RomPath("Game 00 (USA).nes");
		string cached = SeedBoxArt(rom);
		Assert.True(File.Exists(cached), "the case seeded no cover, so it would prove nothing");

		RecordingSender sender = new();
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		//The name table knows this ROM, so the chain COULD ask for it: the request
		//count below is about the cover on the disk, not about a missing name.
		BoxArtLibrary library = Chain(sender, sha1 => string.Equals(sha1, Sha1(rom), StringComparison.OrdinalIgnoreCase) ? new NoIntroRomName(RomConsole.Nes, "Game 00 (USA)") : null);
		model.RomPicker.BoxArtCoverSource = library.GetCover;

		OpenLibrary(window, model);

		PlayerLibraryTile tile = TileFor(model, "Game 00 (USA).nes");
		WaitFor(() => tile.HasArt, "the cached cover never reached its tile");
		Assert.Equal(BoxArtCoverKind.Boxart, tile.DownloadedCover);
		//A request would have been a cache miss: none is a cover served from the
		//player's own disk (ADR-0265 section 6).
		Assert.Equal(0, sender.RequestCount);

		//And it is the tile ON SCREEN, not only the object behind it: the art is what
		//the cover's own Image holds, and it is drawn.
		Image art = CoverImage(window, rom);
		Assert.True(art.IsVisible || art.IsEffectivelyVisible, "the fetched art is not the picture the tile draws");
		Assert.NotNull(art.Source);
		Assert.True(art.IsOnScreen(), "the tile with the downloaded cover is not on screen");

		//And the tiles the collection was not asked about are untouched: the disk
		//read is one tile's, not the grid's.
		Assert.All(model.RomPicker.Tiles.Where(t => t.Path != rom), other => Assert.False(other.HasArt));
	}

	//ADR-0265 section 9: offline is not a wait. The transport fails, every tile
	//keeps its generic cover, and the failure is not remembered as a miss (section
	//7) - one offline session must not blank the library for thirty days.
	[AvaloniaFact]
	public void A_tile_with_no_art_keeps_its_generic_cover_and_the_sheet_does_not_wait()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();
		string rom = RomPath("Game 00 (USA).nes");

		RecordingSender sender = new() { Fails = true };
		List<string> asked = new();
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomPicker.BoxArtCoverSource = Counting(asked, Chain(sender, _ => new NoIntroRomName(RomConsole.Nes, "Game 00 (USA)")).GetCover);

		OpenLibrary(window, model);

		//The grid is whole and generic the moment it is up: nothing about it waited
		//for the network.
		Assert.Equal(3, model.RomPicker.Tiles.Count);
		Assert.False(TileFor(model, "Game 00 (USA).nes").HasArt);
		Image cover = CoverImage(window, rom);
		Assert.False(cover.IsEffectivelyVisible, "a tile with no art drew a picture anyway");
		//The tile draws its generic cover and its title on it, which is the state the
		//player reads as "no cover for this one" rather than as a blank tile.
		TextBlock title = window.FindNamed<ItemsControl>("RomPickerGrid").GetVisualDescendants().OfType<TextBlock>()
			.First(t => t.Classes.Contains("library-cover-title") && t.DataContext is PlayerLibraryTile tile && tile.Path == rom);
		Assert.True(title.IsEffectivelyVisible, "the generic cover lost its title");

		//Every tile that fits the sheet was asked for, so the answer below is the
		//collection's rather than a question nobody asked.
		WaitFor(() => AskedPaths(asked).Length >= model.RomPicker.Tiles.Count, "not every visible tile asked the collection");
		Thread.Sleep(150);
		Pump();
		Assert.Equal(3, AskedPaths(asked).Distinct().Count());
		Assert.All(model.RomPicker.Tiles, tile => Assert.False(tile.HasArt));
		//Nothing was written down: a network that was down is not evidence about the
		//game (ADR-0265 section 7).
		Assert.Empty(Directory.GetFiles(_covers, "*.miss", SearchOption.AllDirectories));
	}

	//ADR-0265 section 4: the caller drives one call per VISIBLE tile. A library
	//larger than the sheet asks about the tiles that fit it and stops there - a
	//twenty-thousand-ROM scan costs its first screenful, not its library.
	[AvaloniaFact]
	public void Only_the_tiles_that_fit_the_sheet_are_asked_about()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot(count: 40);

		RecordingSender sender = new();
		List<string> asked = new();
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomPicker.BoxArtCoverSource = Counting(asked, Chain(sender, _ => new NoIntroRomName(RomConsole.Nes, "Game 00 (USA)")).GetCover);

		OpenLibrary(window, model);
		WaitFor(() => AskedPaths(asked).Length > 0, "the sheet never asked about the tiles it can show");
		//A settling wait, so a request that was going to be made has been made before
		//the set below is read as final.
		Thread.Sleep(200);
		Pump();

		//The grid itself is whole - the window is a fetch rule, not a scan rule.
		Assert.Equal(40, model.RomPicker.Tiles.Count);

		//The tiles the sheet is SHOWING, measured off the rendered tree rather than
		//from any rule of the view-model's (see OnScreenTilePaths): this is the real
		//layout, so a count that happened to match a constant while asking about the
		//wrong tiles would not pass here.
		string[] onScreen = OnScreenTilePaths(window);
		//If every tile fitted the sheet there would be nothing left for the rule to
		//exclude, and this case would prove nothing at all.
		Assert.True(onScreen.Length < model.RomPicker.Tiles.Count,
			$"all {onScreen.Length} tiles fit the sheet, so nothing was left off screen to check");
		Assert.True(onScreen.Length > 0, "no tile is on screen at all");

		//Those tiles, and only those: the asks arrive on the threads that serve them,
		//so they are compared as the SET they are rather than in the order they
		//happened to land in.
		string[] wasAsked = AskedPaths(asked).Distinct().ToArray();
		Assert.Equal(onScreen.OrderBy(p => p, StringComparer.Ordinal), wasAsked.OrderBy(p => p, StringComparer.Ordinal));
	}

	//The tiles the sheet is showing, read off the realized controls: a tile is on
	//screen when the control it was realized into, translated into the ScrollViewer's
	//own coordinates, meets that ScrollViewer's rectangle. This is deliberately not
	//the measurement the view itself makes (content coordinates against the scroll
	//offset, in PlayerRomPickerView.axaml.cs) - a case that reproduced the rule under
	//test would pass by agreeing with it.
	private static string[] OnScreenTilePaths(MainWindow window)
	{
		ItemsControl grid = window.FindNamed<ItemsControl>("RomPickerGrid");
		ScrollViewer sheet = grid.FindAncestorOfType<ScrollViewer>()
			?? throw new XunitException("the grid is in no ScrollViewer, so nothing here is scrollable");
		Rect sheetRect = new Rect(sheet.Bounds.Size);

		List<string> showing = new();
		foreach(Control container in grid.GetRealizedContainers()) {
			if(container.DataContext is not PlayerLibraryTile tile) {
				continue;
			}
			if(container.TranslatePoint(new Point(0, 0), sheet) is { } corner
				&& new Rect(corner, container.Bounds.Size).Intersects(sheetRect)) {
				showing.Add(tile.Path);
			}
		}
		return showing.ToArray();
	}

	//The same source, with what it was asked for written down: the asks are the
	//view-model's own calls, so this is the honest count - what the transport then
	//does with them (a failure, a cover off the disk) is a different question.
	private static Func<LibraryEntry, CancellationToken, Task<BoxArtCover?>> Counting(
		List<string> asked, Func<LibraryEntry, CancellationToken, Task<BoxArtCover?>> source)
	{
		return (entry, token) => {
			lock(asked) {
				asked.Add(entry.Path);
			}
			return source(entry, token);
		};
	}

	private static string[] AskedPaths(List<string> asked)
	{
		lock(asked) {
			return asked.ToArray();
		}
	}

	//ADR-0265 section 8: with the switch off the cache makes NO request - not a
	//smaller one, none.
	[AvaloniaFact]
	public void With_the_switch_off_no_request_is_made()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();

		RecordingSender sender = new();
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		//The name table knows every ROM here, so a request is what the chain would
		//make if the switch did not stop it.
		model.RomPicker.BoxArtCoverSource = Chain(sender, _ => new NoIntroRomName(RomConsole.Nes, "Game 00 (USA)"),
			new BoxArtCacheOptions { DownloadEnabled = static () => false }).GetCover;

		OpenLibrary(window, model);
		//Long enough for the whole window to have been asked about.
		Thread.Sleep(300);
		Pump();

		Assert.Equal(0, sender.RequestCount);
		Assert.All(model.RomPicker.Tiles, tile => Assert.False(tile.HasArt));
	}

	//ADR-0265 section 8, the row itself: the switch is on Settings › System, it is
	//on unless the player turned it off, and the pad reaches it like every other row
	//(ADR-0256's rule for the whole Play GUI).
	[AvaloniaFact]
	public void The_System_row_carries_the_box_art_switch_and_defaults_on()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Preferences.DownloadBoxArt = true;

		RecordingSystem system = new();
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		Pump();
		model.OpenPlayerSettings(new ConfigViewModel(ConfigWindowTab.System, playerMode: true, createSystem: () => system));
		WaitFor(() => window.FindAll<CheckBox>().Any(c => c.Name == "SystemBoxArtSwitch"),
			"the System tab drew no box-art switch");

		CheckBox box = window.FindNamed<CheckBox>("SystemBoxArtSwitch");
		Assert.True(box.IsChecked == true, "the switch did not open on the default the preference carries");
		Assert.Equal("Download box art", box.Content);
		Assert.True(box.IsOnScreen(), "the box-art switch is not on screen on the System sheet");
		Assert.True(box.GetVisualAncestors().Contains(window.FindNamed<Border>("PlayerSettingsSheet")),
			"the box-art switch is outside the settings sheet");

		//The pad's own activation (PlayPadNavigationWiring.Activate's ToggleButton
		//case): flip, then raise - which is what a player's Confirm does.
		box.IsChecked = !(box.IsChecked ?? false);
		box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Pump();

		Assert.Equal(new[] { false }, system.BoxArtWrites.ToArray());
	}

	//The transport, with the switch a fake one: it counts what was asked and answers
	//either a 404 (the collection does not have the game) or nothing at all (offline,
	//a DNS failure, a TLS failure) - the two answers ADR-0265 sections 7 and 9 turn
	//on, and neither of them a cover.
	private sealed class RecordingSender
	{
		public bool Fails { get; init; }

		private readonly List<Uri> _requests = new();

		public int RequestCount {
			get {
				lock(_requests) {
					return _requests.Count;
				}
			}
		}

		public Task<BoxArtHttpResponse> Send(Uri url, int maxBytes, CancellationToken cancellationToken)
		{
			lock(_requests) {
				_requests.Add(url);
			}
			if(Fails) {
				throw new IOException("the collection is unreachable");
			}
			return Task.FromResult(BoxArtHttpResponse.NotFound());
		}
	}

	//The sheet's own writer, recorded instead of performed: the real one saves the
	//settings file, which a headless case must not do.
	private sealed class RecordingSystem : PlayerSystemSettingsViewModel
	{
		public readonly List<bool> BoxArtWrites = new();

		public RecordingSystem()
			: base(() => Path.GetTempPath(), () => Path.GetTempPath(), () => Path.GetTempPath())
		{
		}

		protected override void WriteDownloadBoxArt(bool value) => BoxArtWrites.Add(value);
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
