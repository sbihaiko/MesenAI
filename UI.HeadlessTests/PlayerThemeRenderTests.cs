using System;
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
	private readonly bool _noticeShown = ConfigManager.Config.Preferences.ClassicMenuNoticeShown;
	private readonly bool _showClassicMenuBar = ConfigManager.Config.Preferences.ShowClassicMenuBar;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ClassicMenuNoticeShown = _noticeShown;
		prefs.ShowClassicMenuBar = _showClassicMenuBar;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}
	}

	private static (MainWindow Window, MainWindowViewModel Model) Show(UiMode mode, params string[] recentGames)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = mode;
		prefs.Workspace = Workspace.Play;
		prefs.ClassicMenuNoticeShown = true;
		prefs.ShowClassicMenuBar = false;
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
	}

	//W-P2: the Continue card (white, radius 16) with its 36 px primary
	//Continue, the 28 px secondary Open a ROM… and the tiles on WINBG.
	[AvaloniaFact]
	public void Home_with_recents_renders_with_the_player_theme()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = Show(UiMode.Player, "Contra (USA)", "Castlevania (USA)", "Metroid (USA)", "Mega Man (USA)");

		Border card = window.FindNamed<Border>("PlayHomeContinueCard");
		Assert.Equal(Card, PlayerRender.SolidColor(card.Background));
		Assert.Equal(new CornerRadius(16), card.CornerRadius);
		AssertButton(window.FindNamed<Button>("PlayHomeContinueButton"), 36, 11, 14, PlayTint);
		AssertButton(window.FindNamed<Button>("PlayHomeOpenRomSecondary"), 28, 8, 13, Card);
		TextBlock continueTitle = window.FindNamed<TextBlock>("PlayHomeContinueTitle");
		Assert.Equal(20, continueTitle.FontSize);
		Assert.Equal(Text, PlayerRender.SolidColor(continueTitle.Foreground));
		Assert.Equal(Text2, PlayerRender.SolidColor(window.FindNamed<TextBlock>("PlayHomeContinueSubtitle").Foreground));

		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "W-P2");
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
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();

		Border overlay = window.FindNamed<Border>("PlayerOverlay");
		Color overlayBackground = PlayerRender.SolidColor(overlay.Background);
		Assert.Equal(Color.Parse("#FAFAFC"), overlayBackground);
		Assert.Equal(new CornerRadius(18), overlay.CornerRadius);
		Assert.Equal(380, overlay.Bounds.Width, 0.5);
		TextBlock title = window.FindNamed<TextBlock>("OverlayGameTitle");
		Assert.Equal(20, title.FontSize);
		Assert.Equal(Text, PlayerRender.SolidColor(title.Foreground));
		AssertButton(window.FindNamed<Button>("OverlayResumeButton"), 44, 11, 16, PlayTint);

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
		PlayerRender.AssertPixel(Color.Parse("#FAFAFC"), frame, 550, (int)(overlay.TranslatePoint(new Point(0, 0), window)!.Value.Y + 70));

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
	//`player` scope on the Play host or the chrome.
	[AvaloniaFact]
	public void Advanced_mode_has_no_player_scope()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = Show(UiMode.Advanced);

		Assert.DoesNotContain("player", window.FindNamed<Panel>("PlayWorkspace").Classes);
		Assert.DoesNotContain("player", window.FindNamed<Views.WorkspaceShellBar>("ShellBar").Classes);
		Assert.DoesNotContain("player", window.FindNamed<Border>("ShellStatusLine").Classes);
		Assert.NotEqual("Inter", window.FindNamed<TextBlock>("ProfileButtonName").FontFamily.Name);
	}
}
