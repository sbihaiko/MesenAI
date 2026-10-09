using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using Mesen.Controls;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0249 Decision 5: the render gate for the theme's pilot screens - the
//shell bar and status line (W-S1), the Play home (W-P1, W-P2) and the pause
//overlay (W-P4). Each test renders the real MainWindow with Skia, writes the
//PNG (PlayerRender.OutputFolder, printed) and asserts what a structural test
//cannot: resolved font, size, radius, tint and background. The window is the
//renders' window size (1100 x 740 logical px), so a PNG lines up with the
//window box of the matching docs/media/gui-redesign/W-*.png.
[Collection(NativeCoreCollection.Name)]
public class PlayerThemeRenderTests : IDisposable
{
	private static readonly Color Text = Color.Parse("#1D1D1F");
	private static readonly Color Text2 = Color.Parse("#6E6E73");
	private static readonly Color WindowBackground = Color.Parse("#F5F5F7");
	private static readonly Color Card = Colors.White;
	private static readonly Color PlayTint = Color.Parse("#007AFF");
	private static readonly Color Red = Color.Parse("#FF3B30");

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}
		ConfigManager.Config.PlayerEnhancements.Favorites.Paths = _favorites;
		if(_favoritesFolder != null && Directory.Exists(_favoritesFolder)) {
			Directory.Delete(_favoritesFolder, true);
		}
		//Only a folder this test created is removed; a real pack is never touched.
		if(_seededPack != null && Directory.Exists(_seededPack)) {
			Directory.Delete(_seededPack, true);
		}
	}

	private string? _seededPack;
	private string? _favoritesFolder;
	private readonly List<string> _favorites = ConfigManager.Config.PlayerEnhancements.Favorites.Paths.ToList();

	//A recent-game file as the Core writes it: a zip holding the screenshot.
	private static void WriteRecentWithScreenshot(string game, Color color)
	{
		string file = Path.Combine(ConfigManager.RecentGamesFolder, game + ".rgd");
		WriteableBitmap shot = new(new PixelSize(256, 240), new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Opaque);
		using(Avalonia.Platform.ILockedFramebuffer buffer = shot.Lock()) {
			byte[] row = new byte[buffer.RowBytes];
			for(int x = 0; x < 256; x++) {
				row[x * 4] = color.R;
				row[x * 4 + 1] = color.G;
				row[x * 4 + 2] = color.B;
				row[x * 4 + 3] = 255;
			}
			for(int y = 0; y < 240; y++) {
				System.Runtime.InteropServices.Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
			}
		}
		DateTime written = File.GetLastWriteTime(file);
		using MemoryStream encoded = new();
		shot.Save(encoded);
		using(FileStream fs = File.Create(file))
		using(System.IO.Compression.ZipArchive zip = new(fs, System.IO.Compression.ZipArchiveMode.Create)) {
			using Stream png = zip.CreateEntry("Screenshot.png").Open();
			png.Write(encoded.ToArray());
		}
		Assert.NotNull(PlayHome.ReadScreenshot(file));
		File.SetLastWriteTime(file, written);
	}

	private static (MainWindow Window, MainWindowViewModel Model) Show(UiMode mode, params string[] recentGames) => Show(mode, null, recentGames);

	//seed runs after the recent-game files are written, before the home reads them.
	private static (MainWindow Window, MainWindowViewModel Model) Show(UiMode mode, Action? seed, string[] recentGames)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = mode;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInMenusAndConfig = false;

		string folder = ConfigManager.RecentGamesFolder;
		foreach(string stale in Directory.GetFiles(folder, "*.rgd")) {
			File.Delete(stale);
		}
		DateTime written = DateTime.Now;
		foreach(string game in recentGames) {
			string file = Path.Combine(folder, game + ".rgd");
			File.WriteAllText(file, "");
			File.SetLastWriteTime(file, written);
			written = written.AddMinutes(-1);
		}
		seed?.Invoke();

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus");
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	//The render's ▶ is a drawn play icon before the label, not a glyph in the copy.
	private static void AssertPlayIcon(Button button, string label)
	{
		Assert.Equal(label, LabelOf(button).Text);
		PathIcon icon = button.FindAll<PathIcon>().Single(p => p.Classes.Contains("leading"));
		Assert.True(icon.IsOnScreen());
		Assert.Same(Application.Current!.FindResource("PlayerIconPlay"), icon.Data);
	}

	private static TextBlock LabelOf(Control control) => control.FindAll<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));

	private static void AssertButton(Button button, double height, double radius, double fontSize, Color background)
	{
		Assert.Equal(height, button.Bounds.Height, 0.5);
		Assert.Equal(new CornerRadius(radius), button.CornerRadius);
		TextBlock label = LabelOf(button);
		Assert.Equal("Inter", label.FontFamily.Name);
		Assert.Equal(fontSize, label.FontSize);
		Assert.Equal(background, PlayerRender.SolidColor(button.Background));
	}

	//#951: the regions that match the wireframe today must keep matching, and
	//each known deviation (PlayerWireframe.KnownDeviationsOf, with its reason)
	//must still fail on its named kind, so the fix that closes it also promotes
	//it to a gated region. The rule is host-free; this only feeds it the render.
	//#974: the fresh render must also match its committed copy in
	//UI.Tests/Theme/PlayerRenders/, which is all CI can gate (ADR-0131); a
	//drift fails here with a "re-commit the render" line per region.
	private static void AssertWireframeRegions(Bitmap frame, string wId)
	{
		RgbFrame fresh = PlayerRender.Rgb(frame);
		IReadOnlyList<RegionResult> results = PlayerWireframe.Compare(wId, fresh, RgbFrame.FromPng(PlayerRender.WireframePath(wId)));
		List<string> violations = PlayerWireframe.Gate(wId, results, DeviationsOnThisHost(wId)).ToList();
		string committed = PlayerRender.DriftBaselinePath(wId);
		Assert.True(File.Exists(committed), $"{wId} has no committed render at {committed}; commit {Path.Combine(PlayerRender.OutputFolder, wId + ".png")} there");
		violations.AddRange(PlayerWireframe.Drift(wId, fresh, RgbFrame.FromPng(committed), committed));
		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	//#968: the wireframes draw the macOS window, where the shell bar extends
	//under the traffic lights and starts 80 px in (ShellTitleBar.ExtendsIntoTitleBar).
	//Off macOS - the Linux render-gate runner, ADR-0191 - the bar is not
	//inset, so its badge sits left of the title-bar region and the ink box
	//moves (16 px on the runner). Only that kind is tolerated, only there:
	//the region's colour and text lines stay gated against the wireframe, and
	//the drift check holds the whole region to the Linux baseline.
	private const string NoTrafficLightInset = "no traffic-light inset off macOS";

	private static IReadOnlyList<KnownDeviation> DeviationsOnThisHost(string wId)
	{
		IReadOnlyList<KnownDeviation> known = PlayerWireframe.KnownDeviationsOf(wId);
		if(OperatingSystem.IsMacOS() || known.Any(k => k.Region == "title bar" && k.Kind == PlayerWireframe.InkBox)) {
			return known;
		}
		return known.Append(new KnownDeviation("title bar", PlayerWireframe.InkBox, NoTrafficLightInset, false)).ToArray();
	}

	//W-S1's chrome on W-P1: light bar with the tinted Play badge and the
	//15 px semibold name, the 11.5 px status line, and the first-run home on
	//the light window background with its one 44 px primary button.
	[AvaloniaFact]
	public void Shell_and_first_run_home_render_with_the_player_theme()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = Show(UiMode.Player);

		Assert.Contains("player", window.FindNamed<Panel>("PlayWorkspace").Classes);
		Assert.Equal(Color.Parse("#FAFAFB"), PlayerRender.SolidColor(window.FindNamed<DockPanel>("TitleBarArea").Background));
		Assert.Equal(PlayTint, PlayerRender.SolidColor(window.FindNamed<Border>("ProfileBadge").Background));
		TextBlock name = window.FindNamed<TextBlock>("ProfileButtonName");
		Assert.Equal("Inter", name.FontFamily.Name);
		Assert.Equal(15, name.FontSize);
		Assert.Equal(FontWeight.SemiBold, name.FontWeight);
		Assert.Equal(Text, PlayerRender.SolidColor(name.Foreground));
		TextBlock status = window.FindNamed<TextBlock>("ShellStatusText");
		Assert.Equal(11.5, status.FontSize);
		Assert.Equal(Text2, PlayerRender.SolidColor(status.Foreground));

		TextBlock title = window.FindNamed<TextBlock>("PlayHomeDropTitle");
		Assert.Equal(28, title.FontSize);
		Assert.Equal(FontWeight.Bold, title.FontWeight);
		Assert.Equal(Text, PlayerRender.SolidColor(title.Foreground));
		AssertButton(window.FindNamed<Button>("PlayHomeOpenRomPrimary"), 44, 11, 16, PlayTint);
		Assert.Equal(220, window.FindNamed<Button>("PlayHomeOpenRomPrimary").Bounds.Width, 0.5);
		Assert.Equal(PlayTint, PlayerRender.SolidColor(window.FindNamed<Border>("PlayHomeFirstRunBadge").Background));

		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "W-P1");
		//The home's background is the theme's, not the classic #181818.
		PlayerRender.AssertPixel(WindowBackground, frame, 30, 300);
		PlayerRender.AssertPixel(WindowBackground, frame, 1070, 600);
		//The bar is the light chrome.
		PlayerRender.AssertPixel(Color.Parse("#FAFAFB"), frame, 600, 10);
		//The drop block (badge, title, subtitle, button) sits ~20 px below the
		//wireframe's, the primary button carries a focus outline, the hint is
		//one line where the wireframe has two, and the status line ends in the
		//P1-P4 port chips the wireframe does not draw.
		AssertWireframeRegions(frame, "W-P1");
	}

	//W-P2: the Continue card (white, radius 16) with its 36 px primary
	//Continue, the 28 px secondary Open a ROM… and the tiles on WINBG.
	[AvaloniaFact]
	public void Home_with_recents_renders_with_the_player_theme()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		//The Continue card shows the recent entry's own screenshot, and a tile
		//whose game has an HD pack in HdPacks/<game> carries the pack badge.
		Color shot = Color.Parse("#C83228");
		//A game name no real library uses, so the seeded HdPacks folder can
		//never be (or overwrite) a pack the user has.
		const string packedGame = "MesenAI Theme Test Game (Seeded Pack)";
		string seeded = Path.Combine(ConfigManager.HdPackFolder, packedGame);
		Assert.False(Directory.Exists(seeded), "a folder named like the test fixture already exists: " + seeded);
		_seededPack = seeded;
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, () => {
			WriteRecentWithScreenshot("Contra (USA)", shot);
			Directory.CreateDirectory(seeded);
			File.WriteAllText(Path.Combine(seeded, "hires.txt"), "<ver>106\n");
		}, new[] { "Contra (USA)", packedGame, "Metroid (USA)", "Mega Man (USA)" });

		Border card = window.FindNamed<Border>("PlayHomeContinueCard");
		Assert.Equal(Card, PlayerRender.SolidColor(card.Background));
		Assert.Equal(new CornerRadius(16), card.CornerRadius);
		AssertButton(window.FindNamed<Button>("PlayHomeContinueButton"), 36, 11, 14, PlayTint);
		AssertPlayIcon(window.FindNamed<Button>("PlayHomeContinueButton"), "Continue");
		AssertButton(window.FindNamed<Button>("PlayHomeOpenRomSecondary"), 28, 8, 13, Card);
		TextBlock continueTitle = window.FindNamed<TextBlock>("PlayHomeContinueTitle");
		Assert.Equal(20, continueTitle.FontSize);
		Assert.Equal(Text, PlayerRender.SolidColor(continueTitle.Foreground));
		Assert.Equal(Text2, PlayerRender.SolidColor(window.FindNamed<TextBlock>("PlayHomeContinueSubtitle").Foreground));
		WaitFor(() => model.RecentGames.ContinuePreview != null && !StateGridEntry.ThumbnailsInFlight, "the Continue preview never loaded");
		Image preview = window.FindNamed<Image>("PlayHomeContinuePreview");
		Assert.True(preview.IsOnScreen());
		StateGridEntry[] tiles = window.FindAll<StateGridEntry>().Where(t => t.IsOnScreen()).ToArray();
		StateGridEntry withPack = tiles.Single(t => t.Title == packedGame);
		Border badge = withPack.FindAll<Border>().Single(b => b.Name == "TilePackBadge");
		Assert.True(badge.IsOnScreen());
		Assert.Equal(22, badge.Bounds.Width, 0.5);
		Assert.Equal(Card, PlayerRender.SolidColor(badge.Background));
		Assert.Equal(new CornerRadius(6), badge.CornerRadius);
		Assert.All(tiles.Where(t => t != withPack), t => Assert.False(t.FindAll<Border>().Single(b => b.Name == "TilePackBadge").IsOnScreen()));

		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "W-P2");
		PlayerRender.AssertPixel(WindowBackground, frame, 12, 300);
		Point art = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), window)!.Value;
		PlayerRender.AssertPixel(shot, frame, (int)art.X, (int)art.Y, 6);
		//The seeded data, not the layout, differs: three tiles where the
		//wireframe draws five and a subtitle without the wireframe's pack name.
		//The status line ends in the P1-P4 port chips.
		AssertWireframeRegions(frame, "W-P2");
	}

	//W-P20 (ADR-0268): Home with a Favorites shelf between Continue and Recent.
	//The shelf is a row of W-P2 tiles under its own header; the Continue card
	//keeps its place above it and the Recent row moves down.
	[AvaloniaFact]
	public void Home_with_favorites_renders_the_shelf_between_Continue_and_Recent()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string folder = Path.Combine(Path.GetTempPath(), "mesen-1110-render-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_favoritesFolder = folder;
		string[] favorites = { "Castlevania", "The Legend of Zelda", "Metroid", "Mega Man 2" };
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, () => {
			WriteRecentWithScreenshot("Super Mario Bros. 3", Color.Parse("#C83228"));
			foreach(string game in favorites) {
				File.WriteAllText(Path.Combine(folder, game + ".nes"), "rom");
			}
			ConfigManager.Config.PlayerEnhancements.Favorites.Paths = favorites.Select(g => Path.Combine(folder, g + ".nes")).ToList();
		}, new[] { "Super Mario Bros. 3", "Contra", "Punch-Out!!", "Kirby's Adventure", "Excitebike" });

		WaitFor(() => model.RecentGames.ContinuePreview != null && !StateGridEntry.ThumbnailsInFlight, "the Continue preview never loaded");
		Control header = window.FindNamed<TextBlock>("PlayHomeFavoritesHeader");
		Panel shelf = window.FindNamed<Panel>("PlayHomeFavoritesGrid");
		Panel recent = window.FindNamed<Panel>("PlayHomeRecentGrid");
		Border card = window.FindNamed<Border>("PlayHomeContinueCard");
		Assert.True(header.IsOnScreen());
		Assert.True(shelf.IsOnScreen());
		Assert.True(recent.IsOnScreen());
		double Top(Control c) => c.TranslatePoint(new Point(0, 0), window)!.Value.Y;
		Assert.True(Top(card) < Top(header) && Top(header) < Top(shelf) && Top(shelf) < Top(recent), "the shelf is not between Continue and Recent");
		Assert.Equal(favorites, shelf.FindAll<StateGridEntry>().Where(t => t.IsOnScreen()).Select(t => t.Title));
		Assert.Equal(Text, PlayerRender.SolidColor(((TextBlock)header).Foreground));

		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "W-P20");
		PlayerRender.AssertPixel(WindowBackground, frame, 12, 300);
	}

	//W-P4: the light overlay card (radius 18) with the 44 px tinted Resume,
	//grouped rows with tinted badges, the destructive Quit Game, and every
	//text readable on its surface - the overlay and the Save states sheet it
	//opens (#716: no dark-on-dark).
	[AvaloniaFact]
	public void Pause_overlay_renders_with_the_player_theme()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player);
		//A loaded game: the overlay's title is its name (the render's "Contra (USA)").
		model.RomInfo = new RomInfo() { RomPath = "/roms/Contra (USA).nes", ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.OpenPauseOverlay();
		//The overlay pauses the game (EmuApi.Pause -> GamePaused -> IsGamePaused),
		//and the render keeps the shell bar and the status line above the scrim.
		model.IsGamePaused = true;
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<Mesen.Views.WorkspaceShellBar>("ShellBar").IsOnScreen());
		Assert.True(window.FindNamed<Border>("ShellStatusLine").IsOnScreen());
		Assert.Equal("Contra (USA)", window.FindNamed<TextBlock>("OverlayGameTitle").Text);
		Assert.True(window.FindNamed<TextBlock>("OverlayGameTitle").IsOnScreen());

		Border overlay = window.FindNamed<Border>("PlayerOverlay");
		Color overlayBackground = PlayerRender.SolidColor(overlay.Background);
		Assert.Equal(Color.Parse("#FAFAFC"), overlayBackground);
		Assert.Equal(new CornerRadius(18), overlay.CornerRadius);
		Assert.Equal(380, overlay.Bounds.Width, 0.5);
		TextBlock title = window.FindNamed<TextBlock>("OverlayGameTitle");
		Assert.Equal(20, title.FontSize);
		Assert.Equal(Text, PlayerRender.SolidColor(title.Foreground));
		AssertButton(window.FindNamed<Button>("OverlayResumeButton"), 44, 11, 16, PlayTint);
		AssertPlayIcon(window.FindNamed<Button>("OverlayResumeButton"), "Resume");

		Button pack = window.FindNamed<Button>("OverlayPackButton");
		Assert.Equal(50, pack.Bounds.Height, 0.5);
		Assert.Equal(Color.Parse("#34C759"), PlayerRender.SolidColor(pack.FindAll<Border>().First(b => b.Classes.Contains("badge")).Background));
		TextBlock packValue = window.FindNamed<TextBlock>("OverlayPackValue");
		Assert.Equal(Text2, PlayerRender.SolidColor(packValue.Foreground));
		Assert.Equal(13.5, pack.FindAll<TextBlock>().First(t => t.Classes.Contains("title")).FontSize);

		Button quit = window.FindNamed<Button>("OverlayQuitGameButton");
		Assert.Equal(36, quit.Bounds.Height, 0.5);
		Assert.Equal(Color.Parse("#FFEBEA"), PlayerRender.SolidColor(quit.Background));
		Assert.Equal(Red, PlayerRender.SolidColor(LabelOf(quit).Foreground));
		//The render's Title Case (final audit): "Save States", "Quit Game".
		Assert.Equal("Quit Game", LabelOf(quit).Text);
		Assert.Contains(window.FindNamed<Button>("OverlaySaveStatesButton").FindAll<TextBlock>(), t => t.Text == "Save States");

		//Text on the card itself (not on a filled button) meets WCAG AA 4.5:1,
		//the bar PlaySheetsContrastTests holds every Play sheet to (#716); the
		//Esc hint uses TEXT2 for that reason, not the render's TEXT3 (2.5:1).
		foreach(TextBlock text in overlay.FindAll<TextBlock>().Where(t => t.IsOnScreen() && !string.IsNullOrEmpty(t.Text))) {
			Button? owner = text.FindAncestorOfType<Button>();
			if(owner != null && (owner.Classes.Contains("primary") || owner.Classes.Contains("destructive"))) {
				continue;
			}
			double contrast = PlayerRender.Contrast(PlayerRender.SolidColor(text.Foreground), overlayBackground);
			Assert.True(contrast >= 4.5, $"'{text.Text}' has contrast {contrast:0.0} on the overlay");
		}

		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "W-P4");
		//The card sits ~38 px below the wireframe's (so its Resume button and
		//rows are off too) over a flat dimmed home rather than the blurred game
		//frame, and the status line carries the P1-P4 port chips.
		AssertWireframeRegions(frame, "W-P4");
		//#1089: 8 px in from the card's left edge, not its middle. The Resume
		//press is the card's one focused control, so PlayerFocusRing draws a
		//bloom ~19 px around it, and a sample on the card's centre line at this
		//height reads that bloom (#F2F5FB) instead of the card's own surface.
		//The point is still on the card and off the press by 22 px.
		Point card = overlay.TranslatePoint(new Point(0, 0), window)!.Value;
		PlayerRender.AssertPixel(Color.Parse("#FAFAFC"), frame, (int)card.X + 8, (int)card.Y + 70);

		window.FindNamed<Button>("OverlaySaveStatesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
		Border sheet = window.FindNamed<Border>("PlayerSaveStatesSheet");
		Assert.True(sheet.IsOnScreen());
		Color sheetBackground = PlayerRender.SolidColor(sheet.Background);
		foreach(TextBlock text in sheet.FindAll<TextBlock>().Where(t => t.IsOnScreen() && !string.IsNullOrEmpty(t.Text) && t.FindAncestorOfType<Button>() == null)) {
			double contrast = PlayerRender.Contrast(PlayerRender.SolidColor(text.Foreground), sheetBackground);
			Assert.True(contrast >= 4.5, $"'{text.Text}' has contrast {contrast:0.0} on the Save states sheet");
		}
		PlayerRender.Save(PlayerRender.Capture(window), "W-P4-save-states");
	}

	//Decision 3, MainWindow side: Advanced mode keeps the classic look - no
	//`player` scope on the Play host or the chrome. ADR-0250: Advanced is the
	//Classic door, which shows no shell bar at all.
	[AvaloniaFact]
	public void Advanced_mode_has_no_player_scope()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Advanced);

		Assert.Equal(Workspace.Classic, model.Shell.Active);
		Assert.DoesNotContain("player", window.FindNamed<Panel>("PlayWorkspace").Classes);
		Assert.DoesNotContain("player", window.FindNamed<Views.WorkspaceShellBar>("ShellBar").Classes);
		Assert.DoesNotContain("player", window.FindNamed<Border>("ShellStatusLine").Classes);
		Assert.False(window.FindNamed<Views.WorkspaceShellBar>("ShellBar").IsOnScreen());
	}
}
