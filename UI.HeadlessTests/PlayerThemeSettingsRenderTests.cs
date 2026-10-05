using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Controls;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0249 Decision 5, wave 2 "settings": the render gate for Settings ›
//Display (W-P8) and Look (W-P10) in Player mode, the pad-driven controller
//setup (W-P15) and the shared in-place banner the
//confirmations, errors and interruptions use (W-X1, W-X2, W-X3). Each test
//renders with Skia, writes the PNG (PlayerRender.OutputFolder, printed) and
//asserts font, size, radius, tint, background and the render's "N controls at
//rest" where it states one.
[Collection(NativeCoreCollection.Name)]
public class PlayerThemeSettingsRenderTests : IDisposable
{
	private static readonly Color Text = Color.Parse("#1D1D1F");
	private static readonly Color Text2 = Color.Parse("#6E6E73");
	private static readonly Color Card = Colors.White;
	private static readonly Color Fill = Color.Parse("#E9E9EC");
	private static readonly Color GroupInset = Color.Parse("#F8F8FA");
	private static readonly Color OverlayCard = Color.Parse("#FAFAFC");
	private static readonly Color PlayTint = Color.Parse("#007AFF");
	private static readonly Color RemasterTint = Color.Parse("#AF52DE");
	private static readonly Color Red = Color.Parse("#FF3B30");
	private static readonly Color Orange = Color.Parse("#FF9F0A");
	private static readonly Color BannerWarning = Color.Parse("#FFF8EC");
	private static readonly Color BannerInfo = Color.Parse("#EEF5FF");
	private static readonly Color BannerStop = Color.Parse("#FAF0F0");

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly VideoFilterType _filter = ConfigManager.Config.Video.VideoFilter;
	private readonly string _shader = ConfigManager.Config.Video.ShaderFile;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		ConfigManager.Config.Video.VideoFilter = _filter;
		ConfigManager.Config.Video.ShaderFile = _shader;
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

	private static void AssertText(TextBlock text, double size, FontWeight weight, Color color)
	{
		Assert.Equal("Inter", text.FontFamily.Name);
		Assert.Equal(size, text.FontSize);
		Assert.Equal(weight, text.FontWeight);
		Assert.Equal(color, PlayerRender.SolidColor(text.Foreground));
	}

	private static void AssertPopup(Control control, double width)
	{
		Assert.Contains("popup", control.Classes);
		Assert.Equal(width, control.Bounds.Width, 0.5);
		ComboBox combo = control as ComboBox ?? control.FindAll<ComboBox>().Single();
		Assert.Equal(24, combo.Bounds.Height, 0.5);
		Assert.Equal(new CornerRadius(6), combo.CornerRadius);
		Assert.Equal(Card, PlayerRender.SolidColor(combo.Background));
	}

	private static void AssertInsetGroup(Border group)
	{
		Assert.Equal(GroupInset, PlayerRender.SolidColor(group.Background));
		Assert.Equal(new CornerRadius(12), group.CornerRadius);
	}

	//Controls a person can act on: the render's "N controls at rest" counts
	//the segmented strip as one control.
	private static int ControlsAtRest(Control root)
	{
		int controls = root.FindAll<Control>().Count(c => c.IsOnScreen() && c.Focusable && c.IsEffectivelyEnabled && c is Button or ComboBox or ToggleButton && c is not TabItem);
		return controls + root.FindAll<TabControl>().Count(t => t.IsOnScreen());
	}

	private static void ShowPlayerGame()
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		MainWindow main = new();
		main.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(main.DataContext);
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
	}

	//W-P8 / W-P10 are a sheet in the main window over W-P4 (the renders' 1100 x
	//740 window): open the overlay over a game, press its Settings row, pick
	//the tab. A MainWindow is never closed in a test (closing it shuts the core
	//down for the rest of the run), so the sheet is closed instead.
	private static (MainWindow Window, MainWindowViewModel Model, Border Sheet) ShowSettings(ConfigWindowTab tab)
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		MainWindow main = new() { Width = 1100, Height = 740 };
		main.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(main.DataContext);
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		main.FindNamed<Button>("OverlaySettingsButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
		main.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(tab);
		Dispatcher.UIThread.RunJobs();
		main.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
		return (main, model, main.FindNamed<Border>("PlayerSettingsSheet"));
	}

	//W-P8 / W-P10's chrome: a white sheet (radius 14, 480 wide) in the Player
	//scope titled Settings, the segmented Display | Look | Audio | Controls
	//strip (96 px segments on FILL), and the 32 px Done, 90 wide.
	private static void AssertSettingsChrome(Border sheet)
	{
		Assert.True(sheet.IsOnScreen());
		Assert.Contains("sheet", sheet.Classes);
		Assert.Contains(sheet.GetSelfAndLogicalAncestors().OfType<Control>(), c => c.Classes.Contains("player"));
		Assert.Equal(Card, PlayerRender.SolidColor(sheet.Background));
		Assert.Equal(new CornerRadius(14), sheet.CornerRadius);
		Assert.Equal(480, sheet.Bounds.Width, 0.5);
		AssertText(sheet.FindNamed<TextBlock>("lblPlayerSettingsTitle"), 17, FontWeight.Bold, Text);

		TabControl strip = sheet.FindNamed<TabControl>("PlayerSettingsTabs");
		Assert.Contains("segmented", strip.Classes);
		Border track = strip.FindAll<Border>().First(b => b.Name == "PART_Track");
		Assert.Equal(Fill, PlayerRender.SolidColor(track.Background));
		Assert.Equal(new CornerRadius(8), track.CornerRadius);
		foreach(TabItem tab in strip.Items.Cast<TabItem>()) {
			Assert.Equal(96, tab.Bounds.Width, 0.5);
			Assert.Equal(22, tab.Bounds.Height, 0.5);
			Assert.Equal(12.5, LabelOf(tab).FontSize);
			Assert.Equal("Inter", LabelOf(tab).FontFamily.Name);
		}
		TabItem selected = strip.Items.Cast<TabItem>().Single(t => t.IsSelected);
		Assert.Equal(Card, PlayerRender.SolidColor(selected.FindAll<Border>().First(b => b.Name == "PART_Segment").Background));

		Button done = sheet.FindNamed<Button>("btnPlayerSettingsDone");
		AssertButton(done, 32, 8, 13, PlayTint);
		Assert.Contains("primary", done.Classes);
		Assert.Equal(90, done.Bounds.Width, 0.5);
	}

	//The frame: the sheet is white inside, over the dimmed game.
	private static void Render(MainWindow window, Border sheet, string name)
	{
		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, name);
		Point inside = sheet.TranslatePoint(new Point(sheet.Bounds.Width - 6, sheet.Bounds.Height / 2), window)!.Value;
		PlayerRender.AssertPixel(Card, frame, (int)inside.X, (int)inside.Y);
	}

	[AvaloniaFact]
	public void Settings_display_renders_as_the_W_P8_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, Border sheet) = ShowSettings(ConfigWindowTab.Display);
		try {
			AssertSettingsChrome(sheet);
			AssertInsetGroup(sheet.FindNamed<Border>("DisplaySettingsGroup"));
			Assert.IsType<ToggleSwitch>(sheet.FindNamed<ToggleButton>("chkDisplayFullscreen"));
			AssertPopup(sheet.FindNamed<EnumComboBox>("cboDisplayAspectRatio"), 120);
			AssertPopup(sheet.FindNamed<ComboBox>("cboDisplayScale"), 120);
			//#audit: the Scale popup is never blank (the headless window is under 1×).
			Assert.NotNull(sheet.FindNamed<ComboBox>("cboDisplayScale").SelectedItem);
			AssertText(sheet.FindNamed<TextBlock>("lblDisplayFullscreenRow"), 13.5, FontWeight.Medium, Text);
			Assert.Equal("Full screen", sheet.FindNamed<TextBlock>("lblDisplayFullscreenRow").Text);
			TextBlock hint = sheet.FindNamed<TextBlock>("lblPlayerSettingsEverythingElse");
			Assert.True(hint.IsOnScreen());
			Assert.Equal(12.5, hint.FontSize);
			Assert.Equal(Text2, PlayerRender.SolidColor(hint.Foreground));
			//The group fills the sheet's width (the first capture used ~40 %).
			Border group = sheet.FindNamed<Border>("DisplaySettingsGroup");
			Assert.True(group.Bounds.Width >= sheet.Bounds.Width - 41, $"the group is {group.Bounds.Width} wide in a {sheet.Bounds.Width} sheet");
			Assert.Equal(5, ControlsAtRest(sheet));
			//W-P8's sheet is 340 high, and the hint sits right under the group,
			//on its own line above Done (not at the foot of Look's height).
			Assert.Equal(340, sheet.Bounds.Height, 0.5);
			double groupBottom = group.TranslatePoint(new Point(0, group.Bounds.Height), sheet)!.Value.Y;
			double hintTop = hint.TranslatePoint(new Point(0, 0), sheet)!.Value.Y;
			double doneTop = sheet.FindNamed<Button>("btnPlayerSettingsDone").TranslatePoint(new Point(0, 0), sheet)!.Value.Y;
			Assert.InRange(hintTop - groupBottom, 8, 26);
			Assert.True(hintTop + hint.Bounds.Height <= doneTop, $"the hint ({hintTop}) shares Done's row ({doneTop})");

			Render(window, sheet, "W-P8");
		} finally {
			model.ClosePlayerSettings();
		}
	}

	[AvaloniaFact]
	public void Settings_look_renders_as_the_W_P10_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";
		(MainWindow window, MainWindowViewModel model, Border sheet) = ShowSettings(ConfigWindowTab.Look);
		try {
			AssertSettingsChrome(sheet);
			foreach(string name in new[] { "LookArtGroup", "LookPixelsGroup", "LookScreenGroup" }) {
				AssertInsetGroup(sheet.FindNamed<Border>(name));
			}
			TextBlock header = sheet.FindNamed<TextBlock>("lblLookArtHeader");
			AssertText(header, 11.5, FontWeight.Bold, Text2);
			Assert.Equal("ART", header.Text);
			Assert.Equal("drawn by an artist", sheet.FindNamed<TextBlock>("lblLookArtHint").Text);
			//W-P10's sheet is 480 high.
			Assert.Equal(480, sheet.Bounds.Height, 0.5);
			AssertPopup(sheet.FindNamed<ComboBox>("cboLookPixels"), 200);
			AssertPopup(sheet.FindNamed<ComboBox>("cboLookScreen"), 200);
			Assert.Equal("Sharp — original pixels", Assert.IsType<LookChoice>(sheet.FindNamed<ComboBox>("cboLookPixels").SelectedItem).Label);
			AssertButton(sheet.FindNamed<Button>("btnLookAdjust"), 28, 8, 13, Card);
			AssertButton(sheet.FindNamed<Button>("btnLookHoldToCompare"), 32, 8, 13, Card);
			Assert.Equal(11.5, sheet.FindNamed<TextBlock>("txtLookPixelsMark").FontSize);
			//Hold to Compare and Done share the footer row.
			Button compare = sheet.FindNamed<Button>("btnLookHoldToCompare");
			Button done = sheet.FindNamed<Button>("btnPlayerSettingsDone");
			Assert.Equal(done.TranslatePoint(new Point(0, 0), sheet)!.Value.Y, compare.TranslatePoint(new Point(0, 0), sheet)!.Value.Y, 1);
			//On Look the footer is Hold to Compare and Done; the Options hint
			//belongs to the other tabs (W-P10 has no room for it).
			Assert.False(sheet.FindNamed<TextBlock>("lblPlayerSettingsEverythingElse").IsOnScreen());
			//W-P10 counts 7 with a pack's Art row; without a pack the row is text,
			//and with no shader and no running core Adjust and Hold to Compare
			//are off: the segmented tabs, Smoothing, Effect and Done remain.
			Assert.False(sheet.FindNamed<Button>("btnLookAdjust").IsEnabled);
			Assert.Equal(4, ControlsAtRest(sheet));

			Render(window, sheet, "W-P10");
		} finally {
			model.ClosePlayerSettings();
		}
	}

	//The Player sheet's caps labels and hints are Player-only: Advanced's Look
	//tab keeps the classic "Art" / "Pixels" / "Screen" headers.
	[AvaloniaFact]
	public void Advanced_look_keeps_the_classic_group_labels()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShowPlayerGame();
		ConfigManager.Config.Preferences.UiMode = UiMode.Advanced;
		ConfigWindow window = new(ConfigWindowTab.Look);
		window.Show();
		Dispatcher.UIThread.RunJobs();
		try {
			LookConfigView look = window.FindAll<LookConfigView>().Single(v => v.IsOnScreen());
			string[] shown = look.FindAll<TextBlock>().Where(t => t.IsOnScreen() && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToArray();
			Assert.Contains("Art", shown);
			Assert.Contains("Pixels", shown);
			Assert.Contains("Screen", shown);
			Assert.DoesNotContain("ART", shown);
			Assert.DoesNotContain("PIXELS", shown);
			Assert.DoesNotContain("drawn by an artist", shown);
		} finally {
			window.Close();
		}
	}

	[AvaloniaFact]
	public void Controller_setup_renders_as_the_W_P15_sheet()
	{
		NesControllerConfig port = ConfigManager.Config.Nes.Port1;
		NesKeyMapping[] saved = { port.Mapping1, port.Mapping2, port.Mapping3, port.Mapping4 };
		port.Mapping1 = new NesKeyMapping() { A = 0x0101 };
		port.Mapping2 = new NesKeyMapping();
		port.Mapping3 = new NesKeyMapping();
		port.Mapping4 = new NesKeyMapping();
		const int device = 7;
		ushort Key(int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);
		PlayControllerSetupViewModel setup = new() {
			//The device label is the key name's first word (ControllerDevices.Label).
			KeyName = k => k == Key(9) ? "Pad8 Start" : "Pad8 But" + (k & 0xFF),
			//W-P15's title names the controller itself, not the key prefix.
			DeviceName = d => d == device ? "8BitDo SN30" : "",
			CurrentConsole = () => ConsoleType.Nes,
			IsPaused = () => false,
			Pause = () => { },
			Resume = () => { },
		};
		PlayControllerSetupView view = new() { DataContext = setup };
		Window window = new() {
			Width = 1100, Height = 740,
			Content = new Panel { Classes = { "player" }, Background = new SolidColorBrush(Color.Parse("#0B1430")), Children = { view } }
		};
		window.Show();
		try {
			TimeSpan t = setup.Now;
			setup.Tick(new[] { Key(1) }, t);
			Dispatcher.UIThread.RunJobs();
			Border pill = window.FindNamed<Border>("ControllerSetupPill");
			Assert.True(pill.IsOnScreen());
			Assert.Contains("hud", pill.Classes);
			Assert.Equal(new CornerRadius(10), pill.CornerRadius);
			TextBlock pillText = LabelOf(pill);
			AssertText(pillText, 13, FontWeight.SemiBold, Card);
			Assert.Equal("New controller. Press Start on it to set it up.", pillText.Text);
			PlayerRender.Save(PlayerRender.Capture(window), "W-P15-pill");

			setup.Tick(Array.Empty<ushort>(), t += TimeSpan.FromMilliseconds(100));
			setup.Tick(new[] { Key(9) }, t += TimeSpan.FromMilliseconds(100));
			Dispatcher.UIThread.RunJobs();
			Border sheet = window.FindNamed<Border>("ControllerSetupSheet");
			Assert.True(sheet.IsOnScreen());
			Assert.Equal(OverlayCard, PlayerRender.SolidColor(sheet.Background));
			Assert.Equal(new CornerRadius(18), sheet.CornerRadius);
			Assert.Equal(460, sheet.Bounds.Width, 0.5);
			AssertText(window.FindNamed<TextBlock>("ControllerSetupTitle"), 20, FontWeight.Bold, Text);
			Assert.Equal("Set up \u201c8BitDo SN30\u201d", window.FindNamed<TextBlock>("ControllerSetupTitle").Text);
			AssertText(window.FindNamed<TextBlock>("ControllerSetupPrompt"), 15, FontWeight.SemiBold, Text);
			Border pad = window.FindNamed<Border>("ControllerSetupPad");
			Assert.Equal(new CornerRadius(40), pad.CornerRadius);
			Assert.Equal(340, pad.Bounds.Width, 0.5);
			Assert.Equal(140, pad.Bounds.Height, 0.5);
			//The step's button is lit in the tint; the rest are the pad's grey.
			Border[] keys = pad.FindAll<Border>().Where(b => b.Name == "ControllerSetupKey").ToArray();
			Assert.Equal(8, keys.Length);
			Assert.Equal(PlayTint, PlayerRender.SolidColor(keys[0].Background));
			Assert.NotEqual(PlayTint, PlayerRender.SolidColor(keys[1].Background));
			ProgressBar progress = window.FindNamed<ProgressBar>("ControllerSetupProgress");
			Assert.Contains("track", progress.Classes);
			//"Step 1 of 8" over a bar one eighth full (final audit: it was empty).
			Assert.Equal(1.0 / 8, progress.Value, 3);
			Assert.Equal(PlayTint, PlayerRender.SolidColor(progress.Foreground));
			AssertButton(window.FindNamed<Button>("ControllerSetupSkip"), 36, 11, 14, Card);
			AssertButton(window.FindNamed<Button>("ControllerSetupCancel"), 36, 11, 14, Card);
			Assert.Equal(2, ControlsAtRest(sheet));

			Bitmap frame = PlayerRender.Capture(window);
			PlayerRender.Save(frame, "W-P15");
			Point inside = sheet.TranslatePoint(new Point(12, 12), window)!.Value;
			PlayerRender.AssertPixel(OverlayCard, frame, (int)inside.X, (int)inside.Y + 30);
		} finally {
			setup.Cancel();
			port.Mapping1 = saved[0];
			port.Mapping2 = saved[1];
			port.Mapping3 = saved[2];
			port.Mapping4 = saved[3];
			window.Close();
		}
	}

	//One banner of the pattern sheets, built from the theme's classes.
	private static Border Banner(string kind, string? workspace, Geometry icon, string text, params Button[] buttons)
	{
		StackPanel actions = new() { Orientation = Orientation.Horizontal, Classes = { "banner-actions" } };
		foreach(Button b in buttons) {
			actions.Children.Add(b);
		}
		DockPanel.SetDock(actions, Avalonia.Controls.Dock.Right);
		PathIcon glyph = new() { Data = icon, Classes = { "banner-icon" } };
		DockPanel.SetDock(glyph, Avalonia.Controls.Dock.Left);
		Border banner = new() {
			Classes = { "banner", kind },
			Child = new DockPanel { Children = { glyph, actions, new TextBlock { Text = text, Classes = { "banner-text" } } } }
		};
		if(workspace != null) {
			banner.Classes.Add(workspace);
		}
		return banner;
	}

	private static Button Action(string label, params string[] classes)
	{
		Button b = new() { Content = label };
		b.Classes.AddRange(classes);
		return b;
	}

	private static Geometry Icon(string key) => Assert.IsAssignableFrom<Geometry>(Application.Current!.FindResource(key));

	private static Window PatternSheet(string title, string subtitle, IEnumerable<Control> rows)
	{
		StackPanel list = new() { Spacing = 24, Margin = new Thickness(40, 24, 40, 32) };
		list.Children.Add(new StackPanel { Children = { new TextBlock { Text = title, Classes = { "title1" } }, new TextBlock { Text = subtitle, Classes = { "callout", "secondary" }, FontWeight = FontWeight.Normal } } });
		list.Children.AddRange(rows);
		Window window = new() {
			Width = 1100, SizeToContent = SizeToContent.Height,
			Content = new Border { Classes = { "player", "card" }, Child = list }
		};
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return window;
	}

	[AvaloniaFact]
	public void Confirmation_and_error_banners_render_as_the_W_X1_and_W_X2_pattern_sheets()
	{
		Border restore = Banner("warning", null, Icon("PlayerIconWarning"), "Restore original files? Your edits to this pack will be lost.", Action("Keep Edits", "secondary"), Action("Restore", "destructive"));
		Border switchPack = Banner("info", null, Icon("PlayerIconPack"), "Use Contra HD Remix instead? The game restarts.", Action("Cancel", "secondary"), Action("Switch Pack", "primary"));
		Border stop = Banner("stop", "remaster", Icon("PlayerIconStop"), "Stop recording? Your figures are made from what you played so far.", Action("Keep Going", "secondary"), Action("Stop", "primary", "neutral"));
		Window x1 = PatternSheet("Confirmations", "Shown in place, in the profile that asks. Navigation never asks.", new[] { restore, switchPack, stop });
		try {
			Assert.Equal(BannerWarning, PlayerRender.SolidColor(restore.Background));
			Assert.Equal(BannerInfo, PlayerRender.SolidColor(switchPack.Background));
			Assert.Equal(BannerStop, PlayerRender.SolidColor(stop.Background));
			foreach(Border banner in new[] { restore, switchPack, stop }) {
				Assert.Equal(new CornerRadius(12), banner.CornerRadius);
				Assert.True(banner.Bounds.Height >= 56 - 0.5, $"a banner is {banner.Bounds.Height} high");
				AssertText(banner.FindAll<TextBlock>().First(t => t.Classes.Contains("banner-text")), 13.5, FontWeight.Medium, Text);
			}
			Assert.Equal(Orange, PlayerRender.SolidColor(restore.FindAll<PathIcon>().Single().Foreground));
			Assert.Equal(PlayTint, PlayerRender.SolidColor(switchPack.FindAll<PathIcon>().Single().Foreground));
			Assert.Equal(Red, PlayerRender.SolidColor(stop.FindAll<PathIcon>().Single().Foreground));
			Assert.Equal(Text, PlayerRender.SolidColor(stop.FindAll<Button>().Last().Background));
			PlayerRender.Save(PlayerRender.Capture(x1), "W-X1");
		} finally {
			x1.Close();
		}

		Border download = Banner("warning", null, Icon("PlayerIconWarning"), "This pack could not be downloaded (the host did not answer). Playing without it.", Action("Try Again", "secondary"));
		Border wrongGame = Banner("warning", "remaster", Icon("PlayerIconWarning"), "This is not the game the project was recorded from.", Action("Open the Right Game…", "primary"));
		Border host = Banner("warning", "share", Icon("PlayerIconWarning"), "This host is not accepted. Use a GitHub release, Google Drive, MediaFire, Dropbox or MEGA.");
		Window x2 = PatternSheet("Errors", "A sentence and the next step. Codes and logs stay behind “Show Log”.", new[] { download, wrongGame, host });
		try {
			Assert.Equal(RemasterTint, PlayerRender.SolidColor(wrongGame.FindAll<Button>().Single().Background));
			//The warning glyph stays orange whatever the workspace.
			Assert.Equal(Orange, PlayerRender.SolidColor(host.FindAll<PathIcon>().Single().Foreground));
			PlayerRender.Save(PlayerRender.Capture(x2), "W-X2");
		} finally {
			x2.Close();
		}
	}

	//W-X3: the app's one interruption question (InterruptionBar) is the shared
	//banner - its look follows the question (UI/Logic/InterruptionBanner).
	[AvaloniaFact]
	public void Interruption_bar_renders_as_the_W_X3_banner()
	{
		(InterruptionKind Kind, string Game)[] asks = {
			(InterruptionKind.QuitWhileRecording, ""),
			(InterruptionKind.QuitWhileJob, ""),
			(InterruptionKind.OpenWhileRecording, "Castlevania"),
			(InterruptionKind.OpenWhileClassicBuilder, "Castlevania"),
		};
		List<InterruptionBar> bars = new();
		foreach((InterruptionKind kind, string game) in asks) {
			InterruptionViewModel model = new();
			model.Ask(kind, game, 3, true, () => { });
			bars.Add(new InterruptionBar { DataContext = model });
		}
		Window window = PatternSheet("Interruptions", "Only lost work asks. Switching profile never asks: the work keeps running where it started.", bars);
		try {
			Border[] banners = bars.Select(b => b.FindNamed<Border>("InterruptionBarBorder")).ToArray();
			Assert.Equal(BannerStop, PlayerRender.SolidColor(banners[0].Background));
			Assert.Equal(BannerWarning, PlayerRender.SolidColor(banners[1].Background));
			Assert.Equal(BannerStop, PlayerRender.SolidColor(banners[2].Background));
			Assert.Equal(BannerWarning, PlayerRender.SolidColor(banners[3].Background));
			Assert.Equal(Red, PlayerRender.SolidColor(bars[0].FindNamed<PathIcon>("InterruptionIcon").Foreground));
			Assert.Equal(Orange, PlayerRender.SolidColor(bars[1].FindNamed<PathIcon>("InterruptionIcon").Foreground));
			//Quit goes on with the dark button; open with the asking workspace's tint.
			Assert.Equal(Text, PlayerRender.SolidColor(bars[0].FindNamed<Button>("InterruptionGoButton").Background));
			Assert.Equal(Text, PlayerRender.SolidColor(bars[1].FindNamed<Button>("InterruptionGoButton").Background));
			Assert.Equal(RemasterTint, PlayerRender.SolidColor(bars[2].FindNamed<Button>("InterruptionGoButton").Background));
			Assert.Equal(PlayTint, PlayerRender.SolidColor(bars[3].FindNamed<Button>("InterruptionGoButton").Background));
			Assert.Equal(Card, PlayerRender.SolidColor(bars[0].FindNamed<Button>("InterruptionKeepButton").Background));
			TextBlock text = bars[0].FindNamed<TextBlock>("InterruptionText");
			AssertText(text, 13.5, FontWeight.Medium, Text);
			//The glyph is drawn by the banner, not typed into the sentence.
			Assert.Equal("Quit while recording? What you recorded so far is kept as recording 3.", text.Text);
			PlayerRender.Save(PlayerRender.Capture(window), "W-X3");
		} finally {
			window.Close();
		}
	}

	//Decision 3: the interruption bar is in the theme's scope in Player mode
	//only; Advanced keeps the classic bar. (A MainWindow is never closed in
	//a test: closing it shuts the core down for the rest of the run.)
	[AvaloniaTheory]
	[InlineData(UiMode.Player)]
	[InlineData(UiMode.Advanced)]
	public void The_interruption_bar_host_carries_the_player_scope_only_in_player_mode(UiMode mode)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Preferences.UiMode = mode;
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(mode == UiMode.Player, window.FindNamed<Panel>("InterruptionBarHost").Classes.Contains("player"));
	}
}
