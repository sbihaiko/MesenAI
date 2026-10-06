using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using Mesen.Services;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0249 Decision 5, wave 2 (Play sheets): the render gate for W-P5, W-P6,
//W-P7, W-P11, the shared replays sheet, the Save states slot grid, W-P13,
//W-P14 and W-P16. Each test opens the real sheet in a 1100 x 740 MainWindow
//(the renders' window), writes the Skia PNG (PlayerRender.OutputFolder,
//printed) and asserts the render's identity: a light sheet (white, radius 14)
//out of the Dark scope (#716), Inter, the type sizes, the 32 px footer
//buttons, the tint, the inset lists and switches, and the render's "N
//controls at rest" where the fixture shows the render's state.
[Collection(NativeCoreCollection.Name)]
public class PlaySheetsRenderTests : IDisposable
{
	private static readonly Color Text = Color.Parse("#1D1D1F");
	private static readonly Color Text2 = Color.Parse("#6E6E73");
	private static readonly Color Card = Colors.White;
	private static readonly Color PlayTint = Color.Parse("#007AFF");
	private static readonly Color ShareTint = Color.Parse("#34C759");
	private static readonly Color InsetFill = Color.Parse("#F8F8FA");
	private static readonly Color NoticeFill = Color.Parse("#FFF4E1");
	private const string Sha1 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
	private const string ThreePacks =
		"aaa\tContra 80s\t1.2\tTastic\tCC BY-NC 4.0\ttextures,audio\t1\t0\tissue-1\tc1\n" +
		"bbb\tContra HD Remix\t1.0\t\t\ttextures\t1\t0\tissue-2\tc2\n" +
		"ccc\tContra Arcade\t2.0\t\t\taudio\t1\t0\tissue-3\tc3\n";
	//The render's own two packs; W-P5 adds the "No pack" row.
	private const string TwoPacks =
		"aaa\tContra 80s\t1.2\tTastic\tCC BY-NC 4.0\ttextures,audio\t1\t0\tissue-1\tc1\n" +
		"bbb\tContra HD Remix\t1.0\t\t\ttextures\t1\t0\tissue-2\tc2\n";
	private const string OnePack = "aaa\tContra 80s\t1.2\tTastic\tCC BY-NC 4.0\ttextures,audio\t1\t0\tissue-1\tc1\n";

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-play-sheets-" + Guid.NewGuid().ToString("N"));

	public PlaySheetsRenderTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private static (MainWindow Window, MainWindowViewModel Model) Show(bool overlay = true)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
		while(model.MainMenu.HelpMenuItems.Count == 0 && clock.ElapsedMilliseconds < 30000) {
			Dispatcher.UIThread.RunJobs();
			System.Threading.Thread.Sleep(20);
		}
		//A game is running behind the overlay's sheets; W-P13/W-P14 sit on the home.
		//The overlay pauses it: in the app EmuApi.Pause() comes back as the
		//GamePaused notification, which sets IsGamePaused (the shell bar's input).
		if(overlay) {
			model.RomInfo = new RomInfo() { RomPath = "/roms/Contra (USA).nes", ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
			model.OpenPauseOverlay();
			model.IsGamePaused = true;
		}
		Settle(window);
		return (window, model);
	}

	private static void Settle(Window window)
	{
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
	}

	private static TextBlock LabelOf(Control control) => control.FindAll<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));

	private static int ControlsOnScreen(Control root) =>
		root.FindAll<Control>().Count(c => c.IsOnScreen() && c is Button or RadioButton or ComboBox or CheckBox && c.Focusable);

	//The light sheet: white, radius 14, its width, its text in TEXT (#716).
	private static void AssertSheet(Border sheet, double width)
	{
		Assert.True(sheet.IsOnScreen(), sheet.Name + " is not on screen");
		Assert.Contains("sheet", sheet.Classes);
		Assert.Equal(Card, PlayerRender.SolidColor(sheet.Background));
		Assert.Equal(new CornerRadius(14), sheet.CornerRadius);
		Assert.Equal(width, sheet.Bounds.Width, 0.5);
		Assert.Null(sheet.FindAncestorOfType<ThemeVariantScope>());
	}

	private static void AssertTitle(TextBlock title, double size)
	{
		Assert.Equal("Inter", title.FontFamily.Name);
		Assert.Equal(size, title.FontSize);
		Assert.Equal(FontWeight.Bold, title.FontWeight);
		Assert.Equal(Text, PlayerRender.SolidColor(title.Foreground));
	}

	//A sheet footer button: 32 px, radius 8, 13 px Inter.
	private static void AssertFooterButton(Button button, Color background)
	{
		Assert.Equal(32, button.Bounds.Height, 0.5);
		Assert.Equal(new CornerRadius(8), button.CornerRadius);
		TextBlock label = LabelOf(button);
		Assert.Equal("Inter", label.FontFamily.Name);
		Assert.Equal(13, label.FontSize);
		Assert.Equal(background, PlayerRender.SolidColor(button.Background));
	}

	private static void AssertSwitch(CheckBox box, bool on)
	{
		Assert.Contains("switch", box.Classes);
		Border track = box.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Track");
		Assert.Equal(38, track.Bounds.Width, 0.5);
		Assert.Equal(22, track.Bounds.Height, 0.5);
		Color expected = on ? Color.Parse("#34C759") : Color.Parse("#DCDCE1");
		Assert.Equal(expected, PlayerRender.SolidColor(track.Background));
	}

	private static TextBlock TitleOf(Border sheet) => sheet.FindAll<TextBlock>().First(t => t.IsOnScreen() && !string.IsNullOrEmpty(t.Text));

	//PlayerRender.Pixel reads BGRA; the headless frame can be RGBA, which only
	//shows on a colour whose red and blue differ (W-P14's warm fill).
	private static void AssertPixel(Color expected, Bitmap frame, Point at)
	{
		Color c = PlayerRender.Pixel(frame, (int)at.X, (int)at.Y);
		bool close = Math.Abs(c.R - expected.R) <= 3 && Math.Abs(c.G - expected.G) <= 3 && Math.Abs(c.B - expected.B) <= 3;
		Assert.True(close, $"pixel ({at.X:0},{at.Y:0}) is {c}, expected {expected}");
	}

	private static Bitmap Render(Window window, string name, Border sheet)
	{
		//The renders keep the shell bar and the status line around the scrim.
		AssertShellChrome(window);
		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, name);
		Point inside = sheet.TranslatePoint(new Point(sheet.Bounds.Width - 6, sheet.Bounds.Height / 2), window)!.Value;
		AssertPixel(Card, frame, inside);
		return frame;
	}

	private static void AssertShellChrome(Window window)
	{
		Assert.True(window.FindNamed<WorkspaceShellBar>("ShellBar").IsOnScreen(), "the shell bar is hidden behind the sheet");
		Assert.True(window.FindNamed<Border>("ShellStatusLine").IsOnScreen(), "the status line is hidden behind the sheet");
	}

	//W-P5 on first start (final audit): the picker opens by itself over the
	//running, un-enhanced game - nothing pauses it - and the render still
	//draws the shell bar and the status line around its scrim.
	[AvaloniaFact]
	public void First_start_pack_picker_keeps_the_shell_bar()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(overlay: false);
		model.RomInfo = new RomInfo() { RomPath = "/roms/Contra (USA).nes", ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		Settle(window);
		Assert.False(window.FindNamed<WorkspaceShellBar>("ShellBar").IsOnScreen());

		Assert.True(model.EvaluatePlayerPackPicker(ThreePacks, Sha1));
		Settle(window);
		Assert.True(window.FindNamed<Border>("PlayerPackPicker").IsOnScreen());
		Assert.False(model.IsGamePaused);
		AssertShellChrome(window);

		model.DismissPlayerPackPicker();
		Settle(window);
		Assert.False(window.FindNamed<WorkspaceShellBar>("ShellBar").IsOnScreen());
	}

	//W-P5: option cards (selected = soft blue + tint outline), Cancel and Use
	//This Pack; 2 packs + "No pack" + 2 buttons = the render's 5 controls at rest.
	[AvaloniaFact]
	public void Pack_picker_renders_as_W_P5()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show();
		Assert.True(model.OpenPackFromOverlay(TwoPacks, Sha1, _folder, "", installedSourceSha256: null));
		Settle(window);

		Border sheet = window.FindNamed<Border>("PlayerPackPicker");
		AssertSheet(sheet, 500);
		AssertTitle(window.FindNamed<TextBlock>("PackPickerTitle"), 17);
		RadioButton[] options = sheet.FindAll<RadioButton>().Where(r => r.IsOnScreen()).ToArray();
		Assert.Equal(3, options.Length);
		Assert.All(options, o => Assert.Contains("option", o.Classes));
		RadioButton selected = options.Single(o => o.IsChecked == true);
		Assert.Equal(Color.Parse("#F0F6FF"), PlayerRender.SolidColor(selected.Background));
		Assert.Equal(PlayTint, PlayerRender.SolidColor(selected.BorderBrush));
		Assert.Equal(new CornerRadius(12), selected.CornerRadius);
		RadioButton other = options.First(o => o.IsChecked != true);
		Assert.Equal(Card, PlayerRender.SolidColor(other.Background));
		Assert.Equal(14.5, other.FindAll<TextBlock>().First(t => t.Classes.Contains("option-title")).FontSize);
		AssertFooterButton(window.FindNamed<Button>("PackPickerUseButton"), PlayTint);
		AssertFooterButton(window.FindNamed<Button>("PackPickerCancelButton"), Card);
		Assert.Equal(5, ControlsOnScreen(sheet));

		Render(window, "W-P5", sheet);
	}

	//W-P6: the 48 px share-green badge, the 19 px name, the three layer
	//switch rows in an inset (this game only), the orange music notice,
	//Restore (destructive) and Done; 5 controls besides the layer rows.
	[AvaloniaFact]
	public void Pack_detail_renders_as_W_P6()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string pack = Path.Combine(_folder, "aaa");
		Directory.CreateDirectory(pack);
		List<string> lines = new();
		for(int i = 1; i <= 17; i++) {
			lines.Add($"<bgm>0,{i},track{i}.ogg");
			if(i > 3) {
				File.WriteAllText(Path.Combine(pack, $"track{i}.ogg"), "");
			}
		}
		File.WriteAllLines(Path.Combine(pack, "hires.txt"), lines);
		//A wired audio patch makes the missing tracks a notice (ADR-0240).
		File.WriteAllText(Path.Combine(pack, "pack.json"), "{\"patches\":[{\"file\":\"music.bps\"}]}");
		File.WriteAllText(Path.Combine(pack, "music.bps"), "");
		(MainWindow window, MainWindowViewModel model) = Show();
		Assert.False(model.OpenPackFromOverlay(OnePack, Sha1, _folder, "", installedSourceSha256: "abc123"));
		Settle(window);

		Border sheet = window.FindNamed<Border>("PlayerPackDetailSheet");
		AssertSheet(sheet, 500);
		AssertTitle(window.FindNamed<TextBlock>("PackDetailTitle"), 19);
		Assert.Equal(Text2, PlayerRender.SolidColor(window.FindNamed<TextBlock>("PackDetailByline").Foreground));
		Border badge = sheet.FindAll<Border>().First(b => b.Classes.Contains("badge"));
		Assert.Equal(48, badge.Bounds.Width, 0.5);
		Assert.Equal(ShareTint, PlayerRender.SolidColor(badge.Background));
		Border layers = window.FindNamed<StackPanel>("PackDetailLayers").FindAll<Border>().First(b => b.Classes.Contains("inset"));
		Assert.Equal(InsetFill, PlayerRender.SolidColor(layers.Background));
		Assert.Equal(new CornerRadius(12), layers.CornerRadius);
		//The notice needs a wired audio patch (ADR-0240), so this pack has all
		//three layers, each on for this game.
		CheckBox[] switches = { window.FindNamed<CheckBox>("PackDetailTexturesSwitch"), window.FindNamed<CheckBox>("PackDetailAudioSwitch"), window.FindNamed<CheckBox>("PackDetailPatchSwitch") };
		Assert.All(switches, s => AssertSwitch(s, on: true));
		Assert.All(switches, s => Assert.True(s.IsEnabled));
		Assert.Equal(new[] { "Textures", "Music", "ROM Patch" }, switches.Select(s => s.Content as string).ToArray());
		Assert.Equal(46, switches[0].FindAncestorOfType<Border>()!.Bounds.Height, 1);
		Assert.Equal("The switches apply to this game only.", window.FindNamed<TextBlock>("PackDetailLayersHint").Text);
		Border notice = window.FindNamed<Border>("PackDetailNotice");
		Assert.True(notice.IsOnScreen());
		Assert.Equal(NoticeFill, PlayerRender.SolidColor(notice.Background));
		Assert.Equal(Color.Parse("#965500"), PlayerRender.SolidColor(window.FindNamed<TextBlock>("PackDetailNoticeBody").Foreground));
		AssertFooterButton(window.FindNamed<Button>("PackDetailRestoreButton"), Color.Parse("#FFEBEA"));
		AssertFooterButton(window.FindNamed<Button>("PackDetailDoneButton"), PlayTint);
		Assert.Equal(90, window.FindNamed<Button>("PackDetailDoneButton").Bounds.Width, 0.5);
		Assert.Equal(5, sheet.FindAll<Button>().Count(b => b.IsOnScreen() && b is not CheckBox));
		Assert.Equal(8, ControlsOnScreen(sheet));
		//The render's orange warning icon leads the notice title (no glyph).
		Assert.Equal("Some music is missing", window.FindNamed<TextBlock>("PackDetailNoticeTitle").Text);
		Assert.True(notice.FindAll<PathIcon>().Single(p => p.Classes.Contains("warning")).IsOnScreen());
		//"Show Pack in Finder" on macOS (the render), "Show Pack Folder" elsewhere.
		Assert.Equal(OperatingSystem.IsMacOS() ? "Show Pack in Finder" : "Show Pack Folder",
			LabelOf(window.FindNamed<Button>("PackDetailFolderButton")).Text);

		Render(window, "W-P6", sheet);

		//W-X1: Restore asks in place with the shared warning banner.
		window.FindNamed<Button>("PackDetailRestoreButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
		Settle(window);
		Border confirm = window.FindNamed<Border>("PackDetailRestoreConfirm");
		Assert.True(confirm.IsOnScreen());
		Assert.Contains("banner", confirm.Classes);
		Assert.Contains("warning", confirm.Classes);
		Assert.Equal(Color.Parse("#FFF8EC"), PlayerRender.SolidColor(confirm.Background));
		Assert.True(confirm.FindAll<PathIcon>().Single(p => p.Classes.Contains("banner-icon")).IsOnScreen());
		Assert.Equal("Restore original files? Your edits to this pack will be lost.", confirm.FindAll<TextBlock>().First(t => t.Classes.Contains("banner-text")).Text);
		Button[] answers = confirm.FindAll<Button>().Where(b => b.IsOnScreen()).ToArray();
		Assert.Equal(new[] { "Keep Edits", "Restore" }, answers.Select(b => LabelOf(b).Text).ToArray());
		Assert.Contains("secondary", answers[0].Classes);
		Assert.Contains("destructive", answers[1].Classes);
		Render(window, "W-X1-restore", sheet);
	}

	//W-P7: the four switches and the Pack row in an inset list, Apply & Reload; 6 controls.
	[AvaloniaFact]
	public void Enhancements_render_as_W_P7()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show();
		model.OpenEnhancementsPanel();
		model.EnhModernInstruments = true;
		model.EnhBorder = true;
		model.EnhWidescreen = false;
		Settle(window);

		Border sheet = window.FindNamed<Border>("PlayerEnhancementsPanel");
		AssertSheet(sheet, 460);
		AssertTitle(TitleOf(sheet), 17);
		Border inset = sheet.FindAll<Border>().First(b => b.Classes.Contains("inset"));
		Assert.Equal(InsetFill, PlayerRender.SolidColor(inset.Background));
		Assert.Equal(new CornerRadius(12), inset.CornerRadius);
		CheckBox modern = window.FindNamed<CheckBox>("EnhancementsModernCheckBox");
		AssertSwitch(modern, on: true);
		Assert.NotNull(window.FindNamed<Button>("EnhancementsPackButton"));
		AssertSwitch(window.FindNamed<CheckBox>("EnhancementsWidescreenCheckBox"), on: false);
		Assert.Equal(13.5, modern.FontSize);
		Assert.Equal(46, modern.FindAncestorOfType<Border>()!.Bounds.Height, 1);
		AssertFooterButton(window.FindNamed<Button>("EnhancementsApplyButton"), PlayTint);
		Assert.Equal(6, ControlsOnScreen(sheet));

		Render(window, "W-P7", sheet);
	}

	//W-P11: the search field, the cheats as 50 px switch rows in an inset,
	//Add a Code… and Done; at rest 3 controls besides the rows.
	[AvaloniaFact]
	public void Cheats_render_as_W_P11()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show();
		CheatDbGame contra = new("Contra (USA)", Sha1, new[] {
			new CheatDbCode("Infinite lives - 1P game", "SZKGPAVG"),
			new CheatDbCode("Start with 30 lives", "AEKGPAZE"),
			new CheatDbCode("Keep weapon after dying", "GZUVXVSE"),
			new CheatDbCode("Start on stage 5", "PAKGZAAA"),
			new CheatDbCode("Invincibility (RAM)", "00B0:FF"),
		});
		model.CheatsSheet.Open(ConsoleType.Nes, Sha1, new[] { contra }, Array.Empty<StoredCheat>(), recordingArt: false, disableAll: false, _ => { });
		Settle(window);

		Border sheet = window.FindNamed<Border>("PlayerCheatsSheet");
		AssertSheet(sheet, 500);
		AssertTitle(TitleOf(sheet), 17);
		Assert.Equal(30, window.FindNamed<TextBox>("CheatsSearchBox").Bounds.Height, 0.5);
		Border inset = sheet.FindAll<Border>().First(b => b.Classes.Contains("inset"));
		Assert.Equal(InsetFill, PlayerRender.SolidColor(inset.Background));
		CheckBox[] rows = window.FindNamed<ItemsControl>("CheatsList").FindAll<CheckBox>().ToArray();
		Assert.Equal(5, rows.Length);
		AssertSwitch(rows[0], on: false);
		Assert.Equal(50, rows[0].FindAncestorOfType<Border>()!.Bounds.Height, 1);
		AssertFooterButton(window.FindNamed<Button>("CheatsAddCodeButton"), Card);
		AssertFooterButton(window.FindNamed<Button>("CheatsDoneButton"), PlayTint);
		Assert.Equal(Text2, PlayerRender.SolidColor(window.FindNamed<TextBlock>("CheatsStatusLine").Foreground));
		//search + Add a Code… + Done (the TextBox is not a Button/CheckBox).
		Assert.Equal(2 + rows.Length, ControlsOnScreen(sheet));

		Render(window, "W-P11", sheet);
	}

	//Shared replays: W-P11's sheet shape and inset list, Done primary.
	[AvaloniaFact]
	public void Shared_replays_render_in_W_P11s_sheet_language()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show();
		CommunityReplay replay = new(301, "https://github.com/user-attachments/files/301/run.mmo", new string('c', 64), 4096, "nes", "Contra (USA)", "alice", "stage skip", 3600,
			new[] { new CommunityReplayCheat("NesCustom", "0032:00") }, 2);
		CommunityReplay clean = new(302, "https://github.com/user-attachments/files/302/run.mmo", new string('d', 64), 4096, "nes", "Contra (USA)", "bob", "no-death run", 7200,
			Array.Empty<CommunityReplayCheat>(), 7);
		CommunityReplayGame[] catalog = { new(Sha1, "Contra (USA)", new[] { clean, replay }) };
		model.CommunityReplaysLastKnown = () => catalog;
		model.CommunityReplaysSource = () => Task.FromResult<IReadOnlyList<CommunityReplayGame>?>(null);
		model.ReplayRomSha1 = () => Sha1;
		model.ReplayWatchReasonSource = () => ReplayWatchReason.None;
		model.OpenSaveStatesSheet();
		Settle(window);
		window.FindNamed<Button>("SaveStatesReplaysButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Settle(window);

		Border sheet = window.FindNamed<Border>("PlayerReplaysSheet");
		AssertSheet(sheet, 500);
		AssertTitle(TitleOf(sheet), 17);
		Border inset = sheet.FindAll<Border>().First(b => b.Classes.Contains("inset"));
		Assert.Equal(InsetFill, PlayerRender.SolidColor(inset.Background));
		Button watch = sheet.FindAll<Button>().First(b => b.Name == "ReplaysWatchButton");
		Assert.Contains("secondary", watch.Classes);
		AssertFooterButton(window.FindNamed<Button>("ReplaysDoneButton"), PlayTint);

		Render(window, "replays", sheet);
	}

	//The Save states slot grid (Load): a light sheet of slot tiles (W-P2's
	//tile language), the 17 px title, 12 slots; Advanced keeps the classic grid.
	[AvaloniaFact]
	public void Slot_grid_renders_as_a_light_sheet_of_tiles()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show();
		//#909: W-P4's Save states row is its own grid now; this is the classic slot
		//grid the quick load shortcut opens over a paused game.
		model.RecentGames.Init(GameScreenMode.LoadState);
		Settle(window);

		Border sheet = window.FindNamed<Border>("PlayHomeSlotSheet");
		Assert.True(sheet.IsOnScreen());
		Assert.Equal(Card, PlayerRender.SolidColor(sheet.Background));
		Assert.Equal(new CornerRadius(14), sheet.CornerRadius);
		StateGrid grid = sheet.FindAll<StateGrid>().Single();
		Assert.Contains("tiles", grid.Classes);
		StateGridEntry[] slots = grid.FindAll<StateGridEntry>().ToArray();
		Assert.Equal(12, slots.Length);
		Assert.All(slots, s => Assert.Contains("slot", s.Classes));
		Button tile = slots[0].FindNamed<Button>("TileButton");
		Assert.Equal(new CornerRadius(10), tile.CornerRadius);
		TextBlock title = grid.FindNamed<TextBlock>("StateGridTitle");
		AssertTitle(title, 17);
		TextBlock slotTitle = slots[0].FindAll<TextBlock>().First(t => t.Classes.Contains("title"));
		Assert.Equal("Inter", slotTitle.FontFamily.Name);
		//The grid's own close button: the window holds more than one StateGrid.
		Assert.True(grid.FindNamed<Button>("StateGridCloseButton").IsOnScreen());
		//Player copy (final audit): not Mesen's "Load State Menu" / "Slot #1" / "<empty>".
		Assert.Equal("Load a Slot", title.Text);
		Assert.Equal("Slot 1", slotTitle.Text);
		Assert.Equal("Empty", slots[0].FindAll<TextBlock>().First(t => t.Classes.Contains("subtitle")).Text);
		Assert.Equal("Auto-save", slots[10].FindAll<TextBlock>().First(t => t.Classes.Contains("title")).Text);

		//#909: the shortcut opens this grid over the Play home, whose scrim sits
		//over the sheet's own fill (the old W-P4 door opened it over the game
		//view). The card colour is asserted on the sheet itself just above; what
		//the frame has to show is the tile below.
		AssertShellChrome(window);
		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "slot-grid");
		//An empty slot is a FILL tile, not the classic black picture.
		Assert.False(slots[0].Enabled);
		Point centre = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;
		AssertPixel(PlayerRender.SolidColor(tile.Background), frame, centre);
	}

	//W-P13: the 40 px grey lock badge, the drop zone, the orange wrong-file
	//line, Cancel and Choose File…; 3 controls at rest.
	[AvaloniaFact]
	public void Bios_sheet_renders_as_W_P13()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(overlay: false);
		_ = model.RequestBios(FirmwareType.FDS, "disksys.rom", 8192, 0, "Zelda no Densetsu");
		string wrong = Path.Combine(_folder, "wrong.rom");
		File.WriteAllBytes(wrong, new byte[16 * 1024]);
		model.BiosSheet.TryFile(wrong);
		Settle(window);

		Border sheet = window.FindNamed<Border>("BiosSheet");
		AssertSheet(sheet, 480);
		Border badge = sheet.FindAll<Border>().First(b => b.Classes.Contains("badge"));
		Assert.Equal(40, badge.Bounds.Width, 0.5);
		Assert.Equal(Color.Parse("#8E8E93"), PlayerRender.SolidColor(badge.Background));
		Button drop = window.FindNamed<Button>("BiosSheetDropZone");
		Assert.Equal(InsetFill, PlayerRender.SolidColor(drop.Background));
		Assert.Equal(new CornerRadius(12), drop.CornerRadius);
		Assert.Equal(13.5, window.FindNamed<TextBlock>("BiosSheetDropTitle").FontSize);
		Border error = window.FindNamed<Border>("BiosSheetError");
		Assert.True(error.IsOnScreen());
		//W-X2: the wrong file is the shared warning banner, its mark the drawn icon.
		Assert.Contains("banner", error.Classes);
		Assert.Contains("warning", error.Classes);
		Assert.Equal(Color.Parse("#FFF8EC"), PlayerRender.SolidColor(error.Background));
		Assert.True(error.FindAll<PathIcon>().Single(p => p.Classes.Contains("banner-icon")).IsOnScreen());
		Assert.StartsWith("That file is 16 KB", error.FindAll<TextBlock>().First(t => t.Classes.Contains("banner-text")).Text);
		AssertFooterButton(window.FindNamed<Button>("BiosSheetCancel"), Card);
		AssertFooterButton(window.FindNamed<Button>("BiosSheetChooseFile"), PlayTint);
		Assert.Equal(3, ControlsOnScreen(sheet));

		Render(window, "W-P13", sheet);

		//W-X1: an unknown dump of the right size asks in place, same banner.
		string unknown = Path.Combine(_folder, "unknown.rom");
		File.WriteAllBytes(unknown, new byte[8192]);
		model.BiosSheet.TryFile(unknown);
		Settle(window);
		Border confirm = window.FindNamed<Border>("BiosSheetConfirm");
		Assert.True(confirm.IsOnScreen());
		Assert.Contains("banner", confirm.Classes);
		Assert.True(confirm.FindAll<PathIcon>().Single(p => p.Classes.Contains("banner-icon")).IsOnScreen());
		Assert.Equal("This is not a copy MesenAI knows. Use it anyway?", confirm.FindAll<TextBlock>().First(t => t.Classes.Contains("banner-text")).Text);
		Render(window, "W-P13-confirm", sheet);
	}

	//W-P16: share badge, the file box, the drop zone, Show Folder (plain),
	//Play Without It and Choose File…; 4 controls at rest.
	[AvaloniaFact]
	public void Pack_file_sheet_renders_as_W_P16()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show();
		model.PackDepSheet.SetPending("Contra Arcade Music", new[] { new CommunityPackDepPrompt("arcade-soundtrack", "Arcade soundtrack (MP3 set, 23 files)", "", Path.Combine(_folder, "drop"), "") });
		model.PackDepSheet.Open();
		Settle(window);

		Border sheet = window.FindNamed<Border>("PackDepSheet");
		AssertSheet(sheet, 500);
		//W-P16's wording.
		Assert.Equal("Contra Arcade Music needs one file", window.FindNamed<TextBlock>("PackDepSheetTitle").Text);
		Border badge = sheet.FindAll<Border>().First(b => b.Classes.Contains("badge"));
		Assert.Equal(40, badge.Bounds.Width, 0.5);
		Assert.Equal(ShareTint, PlayerRender.SolidColor(badge.Background));
		Border fileBox = window.FindNamed<TextBlock>("PackDepSheetFile").FindAncestorOfType<Border>()!;
		Assert.Equal(InsetFill, PlayerRender.SolidColor(fileBox.Background));
		Assert.Equal(InsetFill, PlayerRender.SolidColor(window.FindNamed<Button>("PackDepSheetDropZone").Background));
		Assert.Contains("plain", window.FindNamed<Button>("PackDepSheetShowFolder").Classes);
		AssertFooterButton(window.FindNamed<Button>("PackDepSheetPlayWithout"), Card);
		AssertFooterButton(window.FindNamed<Button>("PackDepSheetChooseFile"), PlayTint);
		Assert.Equal(4, ControlsOnScreen(sheet));

		Render(window, "W-P16", sheet);
	}

	//W-P14: the warm alert card (radius 12, amber outline) with the title in
	//TEXT, the body in TEXT2 and Open Another… (secondary).
	[AvaloniaFact]
	public void Load_failure_alert_renders_as_W_P14()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(overlay: false);
		model.RecentGames.Init(GameScreenMode.RecentGames);
		model.RecentGames.ShowLoadFailure(LoadFailureCause.NotAGame, "Contra.txt");
		Settle(window);

		Border alert = window.FindNamed<Border>("PlayHomeLoadAlert");
		Assert.True(alert.IsOnScreen());
		Assert.Equal(Color.Parse("#FFF8EC"), PlayerRender.SolidColor(alert.Background));
		Assert.Equal(Color.Parse("#F5D6A0"), PlayerRender.SolidColor(alert.BorderBrush));
		Assert.Equal(new CornerRadius(12), alert.CornerRadius);
		TextBlock title = window.FindNamed<TextBlock>("PlayHomeLoadAlertTitle");
		Assert.Equal("Inter", title.FontFamily.Name);
		Assert.Equal(13.5, title.FontSize);
		Assert.Equal(Text, PlayerRender.SolidColor(title.Foreground));
		Assert.Equal(Text2, PlayerRender.SolidColor(window.FindNamed<TextBlock>("PlayHomeLoadAlertBody").Foreground));
		Button another = window.FindNamed<Button>("PlayHomeOpenAnother");
		Assert.Contains("secondary", another.Classes);
		Assert.Equal(Card, PlayerRender.SolidColor(another.Background));
		//The render: the orange warning icon (not a glyph), the curly-quoted file
		//name, and Open Another… as the alert's one control (any open clears it).
		Assert.Equal("\u201cContra.txt\u201d is not a game MesenAI can open.", title.Text);
		Assert.True(alert.FindAll<PathIcon>().Single(p => p.Classes.Contains("warning")).IsOnScreen());
		Assert.Equal(new[] { another }, alert.FindAll<Button>().Where(b => b.IsOnScreen()).ToArray());

		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "W-P14");
		Point inside = alert.TranslatePoint(new Point(alert.Bounds.Width / 2, alert.Bounds.Height - 6), window)!.Value;
		AssertPixel(Color.Parse("#FFF8EC"), frame, inside);
	}
}
