using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0249 Decision 5, wave 2 (Share and the shell's two popovers): the render
//gate for W-H1-W-H4 (ShareWorkspaceView, ShareRecordingStrip), W-S3 (the
//profile switcher) and W-S2 (the Tools ⋯ menu's chrome). Each test writes its
//PNG (PlayerRender.OutputFolder) and asserts font, size, radius, tint,
//background and the render's "N controls at rest". W-H1 and the popovers run
//in the real MainWindow (1100 x 740, the renders' window box); W-H2-W-H4 run
//the view in a 1100 x 660 Player/Share host (the renders' content box) with
//fakes, so a build result and a saved replay can be shown without Python or
//the core.
[Collection(NativeCoreCollection.Name)]
public class ShareThemeRenderTests : IDisposable
{
	private static readonly Color Text = Color.Parse("#1D1D1F");
	private static readonly Color Text2 = Color.Parse("#6E6E73");
	private static readonly Color WindowBackground = Color.Parse("#F5F5F7");
	private static readonly Color Card = Colors.White;
	private static readonly Color ShareTint = Color.Parse("#34C759");
	private static readonly Color ShareTintText = Color.Parse("#248A3D");
	private static readonly Color PlayTint = Color.Parse("#007AFF");
	private static readonly Color RemasterTint = Color.Parse("#AF52DE");
	private static readonly Color Indigo = Color.Parse("#5856D6");
	private static readonly Color Red = Color.Parse("#FF3B30");

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _noticeShown = ConfigManager.Config.Preferences.ClassicMenuNoticeShown;
	private readonly bool _showClassicMenuBar = ConfigManager.Config.Preferences.ShowClassicMenuBar;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly List<string> _paths = new();

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ClassicMenuNoticeShown = _noticeShown;
		prefs.ShowClassicMenuBar = _showClassicMenuBar;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		foreach(string path in _paths) {
			try {
				Directory.Delete(path, true);
			} catch(IOException) {
			} catch(UnauthorizedAccessException) {
			}
		}
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowWindow(UiMode mode, Workspace workspace)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = mode;
		prefs.Workspace = Workspace.Play;
		prefs.ClassicMenuNoticeShown = true;
		prefs.ShowClassicMenuBar = false;
		prefs.PauseWhenInMenusAndConfig = false;
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus");
		model.SelectWorkspace(workspace);
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

	private static void AssertPlain(Button button, Color text)
	{
		Assert.Equal(Colors.Transparent, PlayerRender.SolidColor(button.Background));
		Assert.Equal(text, PlayerRender.SolidColor(LabelOf(button).Foreground));
		Assert.Equal("Inter", LabelOf(button).FontFamily.Name);
	}

	private static void AssertText(TextBlock text, double size, Color color, FontWeight? weight = null)
	{
		Assert.Equal("Inter", text.FontFamily.Name);
		Assert.Equal(size, text.FontSize);
		Assert.Equal(color, PlayerRender.SolidColor(text.Foreground));
		if(weight != null) {
			Assert.Equal(weight, text.FontWeight);
		}
	}

	//The render's "N controls at rest": the interactive controls on screen
	//inside a surface (buttons, fields, pickers), enabled or not.
	//The drawn fill of a button (4 px in from its left edge, mid-height): the
	//Background property alone does not prove the template paints it.
	private static void AssertFill(Bitmap frame, Visual root, Control control, Color expected)
	{
		Point origin = control.TranslatePoint(new Point(4, control.Bounds.Height / 2), root) ?? throw new InvalidOperationException("not in the tree");
		PlayerRender.AssertPixel(expected, frame, (int)origin.X, (int)origin.Y, 6);
	}

	private static int ControlsAtRest(Visual root)
	{
		return root.FindAll<Control>().Count(c => c.IsOnScreen() && c is Button or TextBox or ComboBox && c.FindAncestorOfType<Button>() == null);
	}

	private static void AssertReadable(Border surface, string what)
	{
		Color back = PlayerRender.SolidColor(surface.Background);
		foreach(TextBlock text in surface.FindAll<TextBlock>().Where(t => t.IsOnScreen() && !string.IsNullOrEmpty(t.Text))) {
			Button? owner = text.FindAncestorOfType<Button>();
			if(owner != null && (owner.Classes.Contains("primary") || owner.Classes.Contains("destructive"))) {
				continue;
			}
			double contrast = PlayerRender.Contrast(PlayerRender.SolidColor(text.Foreground), back);
			Assert.True(contrast >= 4.5, $"'{text.Text}' has contrast {contrast:0.0} on {what}");
		}
	}

	// ---------------------------------------------------------------- harness
	private sealed class FakeLauncher : IJobProcessLauncher
	{
		public Action<int> Exit = _ => { };

		public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
		{
			Exit = onExit;
			return new Job();
		}

		private sealed class Job : IJobProcess
		{
			public void Kill() { }
		}
	}

	private sealed class FakeRecorder : IReplayRecorder
	{
		public string File = "";
		public bool IsSharing { get; private set; }
		public string? Start() { IsSharing = true; return File; }
		public string? StopAndKeep() { IsSharing = false; return File; }
		public string IssueUrl() => ReplayShare.BuildIssueUrl("");
	}

	private sealed record Host(Window Window, Border Scope, ShareWorkspaceViewModel Model, FakeLauncher Launcher, FakeRecorder Recorder, List<string> Known);

	private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, "/tools");

	private static IReadOnlyList<CommunityPackHostEntry> EmbeddedAllowlist()
	{
		using Stream stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Mesen.pack_host_allowlist.json")
			?? throw new InvalidOperationException("the allow-list is not embedded");
		using StreamReader reader = new(stream);
		return CommunityPackHostAllowlist.Parse(reader.ReadToEnd());
	}

	//The view as MainWindow hosts it in Player mode: a `player share` scope.
	private static Host ShowShareView(Control? content = null, ShareWorkspaceViewModel? shared = null)
	{
		FakeLauncher launcher = new();
		FakeRecorder recorder = new();
		List<string> known = new();
		ShareWorkspaceViewModel model = shared ?? new(EmbeddedAllowlist(), launcher, () => Ready, () => known, recorder, _ => { }, _ => { }, () => (false, false));
		Control view = content ?? new ShareWorkspaceView { DataContext = model };
		view.Classes.Add("player");
		view.Classes.Add("share");
		Border scope = new() { Classes = { "player", "share" }, Child = view };
		Window window = new() { Content = scope, Width = 1100, Height = 660 };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", "");
		Dispatcher.UIThread.RunJobs();
		return new Host(window, scope, model, launcher, recorder, known);
	}

	private string TempFolder()
	{
		string folder = Path.Combine(Path.GetTempPath(), "mesen-share-theme-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_paths.Add(folder);
		return folder;
	}

	// ---------------------------------------------------------------- W-H1
	//W-H1 in the real MainWindow: Share's host is the Player scope with the
	//share tint; two hero cards on WINBG with 56 px badges, the 26 px title,
	//a green and an indigo 36 px primary, the TEXT2 trust line; 2 controls.
	[AvaloniaFact]
	public void W_H1_share_home_renders_with_the_player_theme()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowWindow(UiMode.Player, Workspace.Share);
		model.Share.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", "");
		Dispatcher.UIThread.RunJobs();

		ShareWorkspaceView view = window.FindNamed<ShareWorkspaceView>("ShareWorkspace");
		Assert.Contains("player", view.Classes);
		Assert.Contains("share", view.Classes);
		AssertText(window.FindNamed<TextBlock>("ShareHomeTitle"), 26, Text, FontWeight.Bold);

		foreach(string name in new[] { "SharePackCard", "ShareReplayCard" }) {
			Border card = window.FindNamed<Border>(name);
			Assert.Equal(Card, PlayerRender.SolidColor(card.Background));
			Assert.Equal(new CornerRadius(16), card.CornerRadius);
			Assert.Equal(284, card.Bounds.Height, 1);
		}
		Border packBadge = window.FindNamed<Border>("SharePackBadge");
		Assert.Equal(56, packBadge.Bounds.Width, 0.5);
		Assert.Equal(ShareTint, PlayerRender.SolidColor(packBadge.Background));
		Assert.Equal(Indigo, PlayerRender.SolidColor(window.FindNamed<Border>("ShareReplayBadge").Background));
		AssertText(window.FindNamed<TextBlock>("SharePackCardTitle"), 20, Text, FontWeight.Bold);
		AssertText(window.FindNamed<TextBlock>("SharePackCardBody"), 13.5, Text2);
		AssertButton(window.FindNamed<Button>("ShareAPackButton"), 36, 11, 14, ShareTint);
		AssertButton(window.FindNamed<Button>("RecordAndShareButton"), 36, 11, 14, Indigo);
		AssertText(window.FindNamed<TextBlock>("ShareTrustLine"), 12.5, Text2);
		Assert.Equal("Submissions open on GitHub in your browser. MesenAI never uploads anything or signs in for you.", window.FindNamed<TextBlock>("ShareTrustLine").Text);
		Assert.Equal(2, ControlsAtRest(view));

		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "W-H1");
		PlayerRender.AssertPixel(WindowBackground, frame, 20, 600);
		PlayerRender.AssertPixel(Card, frame, 300, 220);
		AssertFill(frame, window, window.FindNamed<Button>("ShareAPackButton"), ShareTint);
		AssertFill(frame, window, window.FindNamed<Button>("RecordAndShareButton"), Indigo);
	}

	// ---------------------------------------------------------------- W-H2
	//W-H2: ‹ Share is a plain green button, the 26 px title, the three fields
	//(30 px link and game, a 24 px console popup), Package a Project… plain,
	//the 36 px green Continue on GitHub; 6 controls.
	[AvaloniaFact]
	public void W_H2_pack_link_page_renders_with_the_player_theme()
	{
		Host h = ShowShareView();
		h.Model.OpenPackPage();
		Dispatcher.UIThread.RunJobs();
		h.Window.FindNamed<TextBox>("SharePackLink").Focus();

		AssertPlain(h.Window.FindNamed<Button>("SharePackBackButton"), ShareTintText);
		Assert.Equal("Share", LabelOf(h.Window.FindNamed<Button>("SharePackBackButton")).Text);
		AssertText(h.Window.FindNamed<TextBlock>("SharePackTitle"), 26, Text, FontWeight.Bold);
		AssertText(h.Window.FindNamed<TextBlock>("SharePackIntro"), 13.5, Text2);
		TextBox link = h.Window.FindNamed<TextBox>("SharePackLink");
		Assert.Equal(30, link.Bounds.Height, 0.5);
		Assert.Equal(600, link.Bounds.Width, 0.5);
		Assert.Equal(new CornerRadius(7), link.CornerRadius);
		Assert.Equal(420, h.Window.FindNamed<TextBox>("SharePackGame").Bounds.Width, 0.5);
		ComboBox console = h.Window.FindNamed<ComboBox>("SharePackConsole");
		Assert.Equal(24, console.Bounds.Height, 0.5);
		Assert.Equal(160, console.Bounds.Width, 0.5);
		Assert.Equal(new CornerRadius(6), console.CornerRadius);
		Assert.Equal(Card, PlayerRender.SolidColor(console.Background));
		AssertPlain(h.Window.FindNamed<Button>("PackageAProjectButton"), ShareTintText);
		Button go = h.Window.FindNamed<Button>("SharePackContinueButton");
		Assert.Equal(36, go.Bounds.Height, 0.5);
		Assert.Equal(ShareTint, PlayerRender.SolidColor(go.Background));
		Assert.Equal("Continue on GitHub", LabelOf(go).Text);
		Assert.Equal(6, ControlsAtRest(h.Window.FindNamed<StackPanel>("SharePackPage")));

		Bitmap frame = PlayerRender.Capture(h.Window);
		PlayerRender.Save(frame, "W-H2");
		PlayerRender.AssertPixel(WindowBackground, frame, 20, 600);
		//The render's empty field keeps Continue disabled with its reason (rule 4);
		//with a link it is the green primary.
		h.Model.PackLink = "https://github.com/you/your-pack/releases/download/v1/pack.zip";
		Dispatcher.UIThread.RunJobs();
		AssertFill(PlayerRender.Capture(h.Window), h.Window, go, ShareTint);
	}

	// ---------------------------------------------------------------- W-H3
	//W-H3 after a build: three green 28 px step circles, Build Pack .zip
	//secondary, the result, Show in Finder plain, Open Google Drive secondary,
	//the link field and Continue on GitHub; 6 controls.
	[AvaloniaFact]
	public void W_H3_package_host_submit_renders_with_the_player_theme()
	{
		Host h = ShowShareView();
		string project = Path.Combine(TempFolder(), "Contra (USA)");
		Directory.CreateDirectory(Path.Combine(project, "auto", "rec-001", "textures"));
		File.WriteAllText(Path.Combine(project, "auto", "rec-001", "textures", "hires.txt"), "<ver>107\n");
		File.WriteAllText(Path.Combine(project, ".bootstrap"), "generator=mesence-bootstrap/1\nsha1=00\nrom=Contra (USA)\n");
		Directory.CreateDirectory(Path.Combine(project, "mep", "textures"));
		File.WriteAllText(Path.Combine(project, "mep", "textures", "hires.txt"), "<ver>107\n");
		h.Known.Add(project);
		h.Model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", project);
		Assert.True(h.Model.OpenProject(project));
		Assert.True(h.Model.BuildZip());
		File.WriteAllBytes(Path.Combine(project, "contra-usa-mep.zip"), new byte[38 * 1024 * 1024]);
		h.Launcher.Exit(0);
		WaitFor(() => h.Model.IsZipReady, "the build never finished");
		h.Window.FindNamed<TextBox>("ShareProjectLink").Focus();
		Dispatcher.UIThread.RunJobs();

		AssertPlain(h.Window.FindNamed<Button>("ShareProjectBackButton"), ShareTintText);
		AssertText(h.Window.FindNamed<TextBlock>("ShareProjectTitle"), 26, Text, FontWeight.Bold);
		Border[] steps = h.Window.FindNamed<StackPanel>("ShareProjectPage").FindAll<Border>().Where(b => b.Classes.Contains("step")).ToArray();
		Assert.Equal(3, steps.Length);
		foreach(Border step in steps) {
			Assert.Equal(28, step.Bounds.Width, 0.5);
			Assert.Equal(ShareTint, PlayerRender.SolidColor(step.Background));
		}
		AssertText(h.Window.FindNamed<TextBlock>("ShareStep1Title"), 16, Text, FontWeight.SemiBold);
		Assert.Equal("Package it", h.Window.FindNamed<TextBlock>("ShareStep1Title").Text);
		AssertButton(h.Window.FindNamed<Button>("ShareBuildZipButton"), 28, 8, 13, Card);
		AssertPlain(h.Window.FindNamed<Button>("ShareShowZipButton"), ShareTintText);
		AssertButton(h.Window.FindNamed<Button>("ShareOpenDriveButton"), 28, 8, 13, Card);
		Assert.Equal(600, h.Window.FindNamed<TextBox>("ShareProjectLink").Bounds.Width, 0.5);
		Button go = h.Window.FindNamed<Button>("ShareProjectContinueButton");
		Assert.Equal(36, go.Bounds.Height, 0.5);
		Assert.Equal(ShareTint, PlayerRender.SolidColor(go.Background));
		Assert.Equal(6, ControlsAtRest(h.Window.FindNamed<StackPanel>("ShareProjectPage")));

		Bitmap frame = PlayerRender.Capture(h.Window);
		PlayerRender.Save(frame, "W-H3");
		PlayerRender.AssertPixel(WindowBackground, frame, 20, 600);
		h.Model.ProjectLink = "https://github.com/you/your-pack/releases/download/v1/pack.zip";
		Dispatcher.UIThread.RunJobs();
		AssertFill(PlayerRender.Capture(h.Window), h.Window, go, ShareTint);
	}

	// ---------------------------------------------------------------- W-H4
	//W-H4's two sheets over the dimmed home: white radius-14 cards with a
	//40 px badge and a 17 px bold title; Cancel (secondary) and a red Start
	//Recording, then Show in Finder and a green Continue on GitHub, all 32 px.
	//Two controls each; every text readable on its card.
	[AvaloniaFact]
	public void W_H4_record_and_share_sheets_render_with_the_player_theme()
	{
		Host h = ShowShareView();
		h.Recorder.File = Path.Combine(TempFolder(), "contra-usa-2026-10-02.mmo");
		File.WriteAllBytes(h.Recorder.File, new byte[] { 1 });
		h.Model.OpenReplaySheet();
		Dispatcher.UIThread.RunJobs();

		Border before = h.Window.FindNamed<Border>("ShareReplayStartCard");
		Assert.Equal(Card, PlayerRender.SolidColor(before.Background));
		Assert.Equal(new CornerRadius(14), before.CornerRadius);
		Assert.Equal(400, before.Bounds.Width, 0.5);
		Assert.Equal(Indigo, PlayerRender.SolidColor(h.Window.FindNamed<Border>("ShareReplayStartBadge").Background));
		Assert.Equal(40, h.Window.FindNamed<Border>("ShareReplayStartBadge").Bounds.Width, 0.5);
		AssertText(h.Window.FindNamed<TextBlock>("ShareReplaySheetTitle"), 17, Text, FontWeight.Bold);
		AssertButton(h.Window.FindNamed<Button>("ShareReplayCancelButton"), 32, 8, 13, Card);
		AssertButton(h.Window.FindNamed<Button>("ShareReplayStartButton"), 32, 8, 13, Red);
		Assert.Equal(2, ControlsAtRest(before));
		AssertReadable(before, "the Record and share sheet");
		Bitmap frame = PlayerRender.Capture(h.Window);
		PlayerRender.Save(frame, "W-H4-before");
		//The home behind is dimmed by the scrim.
		Color dimmed = PlayerRender.Pixel(frame, 20, 600);
		Assert.True(dimmed.R < 230, $"the home behind the sheet is not dimmed: {dimmed}");
		AssertFill(frame, h.Window, h.Window.FindNamed<Button>("ShareReplayStartButton"), Red);

		Assert.True(h.Model.StartReplay());
		h.Model.StopReplay();
		Dispatcher.UIThread.RunJobs();
		Border after = h.Window.FindNamed<Border>("ShareReplaySavedCard");
		Assert.Equal(Card, PlayerRender.SolidColor(after.Background));
		Assert.Equal(new CornerRadius(14), after.CornerRadius);
		Assert.Equal(ShareTint, PlayerRender.SolidColor(h.Window.FindNamed<Border>("ShareReplaySavedBadge").Background));
		Assert.Equal("Replay saved", h.Window.FindNamed<TextBlock>("ShareReplaySavedTitle").Text);
		AssertText(h.Window.FindNamed<TextBlock>("ShareReplayFileName"), 13.5, Text, FontWeight.SemiBold);
		AssertButton(h.Window.FindNamed<Button>("ShareReplayShowFileButton"), 32, 8, 13, Card);
		AssertButton(h.Window.FindNamed<Button>("ShareReplayContinueButton"), 32, 8, 13, ShareTint);
		Assert.Equal(2, ControlsAtRest(after));
		AssertReadable(after, "the Replay saved sheet");
		Bitmap afterFrame = PlayerRender.Capture(h.Window);
		PlayerRender.Save(afterFrame, "W-H4-after");
		AssertFill(afterFrame, h.Window, h.Window.FindNamed<Button>("ShareReplayContinueButton"), ShareTint);
	}

	//W-H4 while recording (as W-R2): the strip is a dark HUD with the red dot,
	//the white semibold pill and a grey Stop.
	[AvaloniaFact]
	public void W_H4_recording_strip_renders_as_a_hud()
	{
		ShareWorkspaceViewModel model = new(EmbeddedAllowlist(), new FakeLauncher(), () => Ready, () => Array.Empty<string>(), new FakeRecorder(), _ => { }, _ => { }, () => (false, false));
		Host h = ShowShareView(new ShareRecordingStrip { DataContext = model, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top }, model);
		model.OpenReplaySheet();
		Assert.True(model.StartReplay());
		Dispatcher.UIThread.RunJobs();
		Assert.False(string.IsNullOrEmpty(model.RecordingPill));

		Border strip = h.Window.FindNamed<Border>("ShareRecordingStripBorder");
		Color hud = PlayerRender.SolidColor(strip.Background);
		Assert.True(hud.R < 60 && hud.G < 60 && hud.B < 60, $"the strip is not a dark HUD: {hud}");
		TextBlock pill = h.Window.FindNamed<TextBlock>("ShareRecordingPill");
		Assert.Equal("Inter", pill.FontFamily.Name);
		Assert.Equal(Colors.White, PlayerRender.SolidColor(pill.Foreground));
		Button stop = h.Window.FindNamed<Button>("ShareStopRecordingButton");
		Assert.Equal(28, stop.Bounds.Height, 0.5);
		Assert.Equal("Stop", LabelOf(stop).Text);
		PlayerRender.Save(PlayerRender.Capture(h.Window), "W-H4-recording");
	}

	// ---------------------------------------------------------------- W-S3
	//W-S3: the switcher is a light popover (radius 14) with one row per
	//profile: a 32 px badge in that profile's tint, the name, the description
	//in TEXT2, the shortcut, and the active row highlighted with a tinted
	//check. The profile button shows its pressed pill while it is open.
	[AvaloniaFact]
	public void W_S3_profile_switcher_renders_with_the_player_theme()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowWindow(UiMode.Player, Workspace.Play);
		Button profile = window.FindNamed<Button>("ProfileButton");
		profile.Flyout!.ShowAt(profile);
		Dispatcher.UIThread.RunJobs();
		StackPanel panel = Assert.IsType<StackPanel>(Assert.IsType<Flyout>(profile.Flyout).Content);
		FlyoutPresenter presenter = Assert.IsType<FlyoutPresenter>(panel.GetVisualAncestors().OfType<FlyoutPresenter>().First());

		Assert.Equal(Color.Parse("#FCFCFD"), PlayerRender.SolidColor(presenter.Background));
		Assert.Equal(new CornerRadius(14), presenter.CornerRadius);
		Button[] rows = panel.FindNamed<ItemsControl>("SwitcherRows").GetRealizedContainers().SelectMany(c => c.FindAll<Button>()).ToArray();
		Assert.Equal(3, rows.Length);
		Color[] tints = { PlayTint, RemasterTint, ShareTint };
		for(int i = 0; i < 3; i++) {
			Border badge = rows[i].FindAll<Border>().First(b => b.Classes.Contains("badge"));
			Assert.True(badge.IsOnScreen());
			Assert.Equal(32, badge.Bounds.Width, 0.5);
			Assert.Equal(tints[i], PlayerRender.SolidColor(badge.Background));
			Assert.Equal(58, rows[i].Bounds.Height, 0.5);
		}
		Assert.Equal(Color.Parse("#EBF3FF"), PlayerRender.SolidColor(rows[0].Background));
		Assert.Equal(Colors.Transparent, PlayerRender.SolidColor(rows[1].Background));
		TextBlock name = rows[0].FindAll<TextBlock>().First(t => t.Classes.Contains("switcher-name"));
		AssertText(name, 15, Text, FontWeight.SemiBold);
		AssertText(rows[0].FindAll<TextBlock>().First(t => t.Classes.Contains("switcher-description")), 12, Text2);
		Assert.True(rows[0].FindAll<PathIcon>().First(p => p.Classes.Contains("switcher-check")).IsOnScreen());
		Assert.False(rows[1].FindAll<PathIcon>().First(p => p.Classes.Contains("switcher-check")).IsOnScreen());
		Assert.DoesNotContain(panel.FindAll<TextBlock>(), t => t.IsOnScreen() && (t.Text == "✔" || t.Text == "▶"));
		AssertText(panel.FindNamed<TextBlock>("SwitcherFooter"), 12, Text2);
		Assert.Equal(Color.Parse("#E8E8EC"), PlayerRender.SolidColor(profile.FindAll<Border>().First(b => b.Name == "PART_Background").Background));
		Assert.Equal(3, rows.Length);

		SaveWithPopup(window, presenter, "W-S3");
	}

	//W-S2: the Tools ⋯ menu keeps its contents; in Player mode its popup is a
	//light rounded panel (radius 10) with 26 px Inter rows, no icon gutter, a
	//right chevron, the highlighted row filled with the tint.
	[AvaloniaFact]
	public void W_S2_tools_menu_chrome_renders_with_the_player_theme()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowWindow(UiMode.Player, Workspace.Play);
		MenuItem tools = window.FindNamed<MenuItem>("ToolsMenuButton");
		tools.IsSubMenuOpen = true;
		Dispatcher.UIThread.RunJobs();
		MenuItem toolsItem = tools.Items.OfType<MenuItem>().Single(m => m.Name == "ToolsMenuTools");
		toolsItem.IsSelected = true;
		Dispatcher.UIThread.RunJobs();

		Border panel = toolsItem.GetVisualAncestors().OfType<Border>().First(b => b.Name == "PlayerMenuPanel");
		Assert.Equal(Color.Parse("#FAFAFC"), PlayerRender.SolidColor(panel.Background));
		Assert.Equal(new CornerRadius(10), panel.CornerRadius);
		MenuItem[] items = tools.Items.OfType<MenuItem>().ToArray();
		Assert.Equal(8, items.Length);
		foreach(MenuItem item in items.Take(6)) {
			Assert.Equal(26, item.Bounds.Height, 0.5);
			TextBlock label = LabelOf(item);
			Assert.Equal("Inter", label.FontFamily.Name);
			Assert.Equal(13.5, label.FontSize);
			Assert.True(item.FindAll<PathIcon>().Any(p => p.Classes.Contains("chevron") && p.IsOnScreen()), $"{label.Text} has no chevron");
		}
		//The open ⋯ shows the grey pressed pill, not Fluent's accent selection.
		Assert.Equal(Color.Parse("#E8E8EC"), PlayerRender.SolidColor(tools.FindAll<Border>().First(b => b.Name == "PART_LayoutRoot").Background));
		Border highlight = toolsItem.FindAll<Border>().First(b => b.Name == "PART_LayoutRoot");
		Assert.Equal(PlayTint, PlayerRender.SolidColor(highlight.Background));
		Assert.Equal(Colors.White, PlayerRender.SolidColor(LabelOf(toolsItem).Foreground));
		TextBlock hint = LabelOf(window.FindNamed<MenuItem>("ToolsMenuHint"));
		Assert.Equal(11.5, hint.FontSize);
		Assert.Equal(Text2, PlayerRender.SolidColor(hint.Foreground));

		SaveWithPopup(window, panel, "W-S2");
	}

	//Decision 3: in Advanced mode the switcher and the Tools ⋯ menu keep the
	//classic look.
	[AvaloniaFact]
	public void Advanced_mode_keeps_the_classic_switcher_and_tools_menu()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowWindow(UiMode.Advanced, Workspace.Play);
		Button profile = window.FindNamed<Button>("ProfileButton");
		profile.Flyout!.ShowAt(profile);
		Dispatcher.UIThread.RunJobs();
		StackPanel panel = Assert.IsType<StackPanel>(Assert.IsType<Flyout>(profile.Flyout).Content);
		FlyoutPresenter presenter = panel.GetVisualAncestors().OfType<FlyoutPresenter>().First();
		Assert.NotEqual(new CornerRadius(14), presenter.CornerRadius);
		Assert.DoesNotContain(panel.FindAll<Border>(), b => b.Classes.Contains("badge") && b.IsOnScreen());
		profile.Flyout.Hide();

		MenuItem tools = window.FindNamed<MenuItem>("ToolsMenuButton");
		tools.IsSubMenuOpen = true;
		Dispatcher.UIThread.RunJobs();
		MenuItem file = tools.Items.OfType<MenuItem>().First();
		Assert.DoesNotContain(file.GetVisualAncestors().OfType<Border>(), b => b.Name == "PlayerMenuPanel");
		Assert.NotEqual("Inter", LabelOf(file).FontFamily.Name);
	}

	//A popup is its own top level in the headless platform: draw its frame
	//over the window's at the popup's position, so the PNG shows both.
	private static void SaveWithPopup(Window window, Visual popupContent, string name)
	{
		Bitmap frame = PlayerRender.Capture(window);
		TopLevel popup = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(popupContent));
		Bitmap popupFrame = PlayerRender.Capture(popup);
		PixelPoint p = popup.PointToScreen(new Point(0, 0)), w = window.PointToScreen(new Point(0, 0));
		PixelPoint at = new(p.X - w.X, p.Y - w.Y);
		RenderTargetBitmap composite = new(frame.PixelSize);
		using(DrawingContext ctx = composite.CreateDrawingContext()) {
			ctx.DrawImage(frame, new Rect(0, 0, frame.Size.Width, frame.Size.Height));
			ctx.DrawImage(popupFrame, new Rect(at.X, at.Y, popupFrame.Size.Width, popupFrame.Size.Height));
		}
		PlayerRender.Save(composite, name);
	}
}
