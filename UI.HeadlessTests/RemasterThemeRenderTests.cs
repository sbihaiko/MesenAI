using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0249 Decision 5, wave 2: the render gate for Remaster's screens (W-R0,
//W-R0b, W-R1 ... W-R7). Each test hosts the real view the way MainWindow does
//(a `player remaster` scope) at the renders' content size - 1100 x 660 logical
//px, the window box of docs/media/gui-redesign/W-R*.png minus its 53 px bar
//and 27 px status line - over a core-free RemasterWorkspaceViewModel fed the
//render's fixture (Contra, two recordings, seven figures). It writes the PNG
//(PlayerRender.OutputFolder, printed) and asserts font, size, radius, tint,
//background and the render's "N controls at rest". The last test checks the
//MainWindow side: the hosts carry the scope in Player mode only.
[Collection(NativeCoreCollection.Name)]
public partial class RemasterThemeRenderTests : IDisposable
{
	private const double ContentWidth = 1100;
	private const double ContentHeight = 660;
	private static readonly Color Text = Color.Parse("#1D1D1F");
	private static readonly Color Text2 = Color.Parse("#6E6E73");
	private static readonly Color WindowBackground = Color.Parse("#F5F5F7");
	private static readonly Color Card = Colors.White;
	private static readonly Color RemasterTint = Color.Parse("#AF52DE");
	private static readonly Color Red = Color.Parse("#FF3B30");
	private static readonly Color WarningFill = Color.Parse("#FFF4E1");
	private static readonly Color WarningText = Color.Parse("#965500");
	private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, "/tools");

	private readonly List<string> _folders = new();
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _noticeShown = ConfigManager.Config.Preferences.ClassicMenuNoticeShown;
	private readonly bool _showClassicMenuBar = ConfigManager.Config.Preferences.ShowClassicMenuBar;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly bool _bootstrap = ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ClassicMenuNoticeShown = _noticeShown;
		prefs.ShowClassicMenuBar = _showClassicMenuBar;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = _bootstrap;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		if(NativeCore.IsAvailable) {
			ConfigManager.Config.EnhancementPacks.ApplyConfig();
		}
		if(Application.Current != null) {
			Application.Current.RequestedThemeVariant = ThemeVariant.Light;
		}
		foreach(string folder in _folders) {
			try {
				Directory.Delete(folder, true);
			} catch(IOException) {
			} catch(UnauthorizedAccessException) {
			}
		}
	}

	private sealed class FakeLauncher : IJobProcessLauncher
	{
		public sealed class Job : IJobProcess
		{
			public Action<string, bool> OnLine = (_, _) => { };
			public Action<int> OnExit = _ => { };

			public void Kill() => OnExit(-9);
		}

		public Job? Last;

		public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
		{
			Last = new Job { OnLine = onLine, OnExit = onExit };
			return Last;
		}
	}

	//---------------------------------------------------------------- fixture

	//A tools folder with the two hand-off scripts, so W-R6/W-R7 are enabled as rendered.
	private string Tools()
	{
		string tools = TempFolder();
		File.WriteAllText(Path.Combine(tools, RemasterHandOff.ImportScript), "");
		File.WriteAllText(Path.Combine(tools, RemasterHandOff.ComposeScript), "");
		return tools;
	}

	private string TempFolder()
	{
		string folder = Path.Combine(Path.GetTempPath(), "mesen-r-theme-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_folders.Add(folder);
		return folder;
	}

	private static readonly (string Name, int Phases, bool Painted, byte[] Light, byte[] Dark)[] Figures = {
		("run", 6, true, Rgba(51, 82, 204), Rgba(240, 183, 41)),
		("jump", 2, false, Rgba(240, 183, 41), Rgba(222, 59, 52)),
		("prone", 1, false, Rgba(240, 183, 41), Rgba(52, 160, 89)),
		("climb", 4, true, Rgba(52, 160, 89), Rgba(51, 82, 204)),
		("death", 3, false, Rgba(222, 59, 52), Rgba(52, 160, 89)),
		("boss", 2, false, Rgba(52, 160, 89), Rgba(240, 183, 41)),
		("figure 7", 1, false, Rgba(240, 205, 160), Rgba(52, 160, 89)),
	};

	//The render's project: Contra (USA), two recordings, the kit of seven
	//figures (run and climb painted), one scenery object, two stage maps and a
	//pattern page with 12 cells filled from the game's data.
	private string ContraProject()
	{
		string project = Path.Combine(TempFolder(), "Contra (USA)");
		//ADR-0252 §1: three played shapes between the two recordings (AA twice,
		//under two palettes, is one); the `Y` row is the ROM export, not played.
		Write(project, "auto/rec-001/textures/hires.txt", "<ver>107\n<tile>0,AA,0F102816,0,0,1,N\n<tile>1,BB,0F102816,8,0,1,N\n<tile>2,DD,0F001030,0,8,1,Y\n");
		Write(project, "auto/rec-002/textures/hires.txt", "<ver>107\n<tile>0,AA,0F162736,0,0,1,N\n<tile>1,CC,0F102816,8,0,1,N\n");
		StringBuilder files = new();
		for(int i = 0; i < Figures.Length; i++) {
			var f = Figures[i];
			string sheet = $"sheets/usr{i:000}.png";
			string figure = $"figures/usr{i:000}-figure.png";
			WritePair(project, "kit/rec-001/" + sheet, Sprite(i, f.Light, f.Dark));
			WritePair(project, "kit/rec-001/" + figure, Sprite(i, f.Light, f.Dark));
			if(f.Painted) {
				File.WriteAllBytes(Path.Combine(project, "kit/rec-001/" + figure), Sprite(i, f.Dark, f.Light));
			}
			files.Append(i == 0 ? "" : ",").Append($"{{\"path\": \"{sheet}\", \"title\": \"{f.Name} — a loop\", \"unit\": \"grid\", \"cells\": {f.Phases}, \"seen\": true, \"figure\": \"{figure}\"}}");
		}
		WritePair(project, "kit/rec-001/sheets/usr100.png", Sprite(1, Rgba(52, 160, 89), Rgba(51, 82, 204)));
		Write(project, "kit/rec-001/kit.json", "{\"parts\": [" +
			"{\"part\": \"sprites\", \"files\": [" + files + "]}," +
			"{\"part\": \"background\", \"files\": [{\"path\": \"sheets/usr100.png\", \"title\": \"obj000 (10 cells)\", \"unit\": \"object\", \"cells\": 10, \"seen\": true}]}]}");
		WritePair(project, "kit/pages/chr/Chr_0.png", Sprite(2, Rgba(52, 160, 89), Rgba(240, 183, 41)));
		Write(project, "kit/pages/kit.json", "{\"parts\": [{\"part\": \"chr\", \"files\": [" +
			"{\"path\": \"chr/Chr_0.png\", \"title\": \"page 17 — CHR bank 1\", \"unit\": \"page\", \"cells\": 64, \"evidence\": 52, \"fill\": 12, \"empty\": 0, \"seen\": false}]}]}");
		return project;
	}

	private (RemasterWorkspaceViewModel Model, FakeLauncher Launcher) Model(RemasterFeasibility feasibility, string project, bool projectOpen)
	{
		FakeLauncher launcher = new();
		RemasterWorkspaceViewModel model = new(new RemasterConfig(), _ => feasibility, launcher, hasHeadlessRecorder: false);
		//W-R0's Recent projects lists none here (RemasterRecentProjectsRenderTests draws them).
		model.RecentRomPaths = () => Array.Empty<string>();
		model.OpenFile = _ => true;
		model.ShowInGame = _ => true;
		string root = Path.GetDirectoryName(project)!;
		string folder = projectOpen ? project : Path.Combine(root, "not-a-project-yet");
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", Path.Combine(root, "Contra (USA).nes"), folder, Path.Combine(root, "EnhancementPacks"));
		model.EnsureFeasibilityMeasured();
		WaitFor(() => model.Feasibility != null, "the gate was never measured");
		model.TilesSettled.Wait(10000);
		Dispatcher.UIThread.RunJobs();
		return (model, launcher);
	}

	//The view inside a scope like MainWindow's RemasterWorkspaceHost.
	//player: false hosts it as Advanced does (no `player` class).
	private static (Window Window, T View) Host<T>(T view, object model, bool player = true) where T : Control
	{
		view.DataContext = model;
		Panel host = new() { Classes = { "remaster" }, Children = { view } };
		if(player) {
			host.Classes.Add("player");
		}
		Window window = new() { Content = host, Width = ContentWidth, Height = ContentHeight };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return (window, view);
	}

	private static void WaitFor(Func<bool> condition, string failure, int timeoutMs = 30000)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > timeoutMs) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	private static void Click(Button button)
	{
		button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	private static Bitmap Save(Window window, string name)
	{
		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, name);
		return frame;
	}

	//---------------------------------------------------------------- asserts

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

	private static void AssertSecondary(Button button, double height = 28) => AssertButton(button, height, 8, button.Classes.Contains("small") ? 12 : 13, Card);

	private static void AssertText(TextBlock text, double size, FontWeight weight, Color color)
	{
		Assert.Equal("Inter", text.FontFamily.Name);
		Assert.Equal(size, text.FontSize);
		Assert.Equal(weight, text.FontWeight);
		Assert.Equal(color, PlayerRender.SolidColor(text.Foreground));
	}

	private static void AssertTintAt(Bitmap frame, Window window, Button button)
	{
		Point corner = button.TranslatePoint(new Point(5, button.Bounds.Height / 2), window)!.Value;
		PlayerRender.AssertPixel(RemasterTint, frame, (int)corner.X, (int)corner.Y);
	}

	private static void AssertCard(Border card, double radius = 12)
	{
		Assert.Equal(Card, PlayerRender.SolidColor(card.Background));
		Assert.Equal(new CornerRadius(radius), card.CornerRadius);
	}

	//"N controls at rest": the buttons on screen, outside the tile list (whose
	//tiles are list rows, rule 2) - disabled ones count, they show a reason.
	private static int ControlsAtRest(Visual root) => root.FindAll<Button>()
		.Count(b => b.IsOnScreen() && b is not ToggleButton && b.FindAncestorOfType<RemasterTileBrowserView>() == null);

	private static void AssertReadable(Border surface)
	{
		Color background = PlayerRender.SolidColor(surface.Background);
		foreach(TextBlock text in surface.FindAll<TextBlock>().Where(t => t.IsOnScreen() && !string.IsNullOrEmpty(t.Text))) {
			Button? owner = text.FindAncestorOfType<Button>();
			if(owner != null && (owner.Classes.Contains("primary") || !owner.IsEffectivelyEnabled)) {
				continue;
			}
			Border? fill = text.FindAncestorOfType<Border>();
			Color behind = fill != null && fill != surface && fill.Background is ISolidColorBrush b ? b.Color : background;
			double contrast = PlayerRender.Contrast(PlayerRender.SolidColor(text.Foreground), behind);
			Assert.True(contrast >= 4.5, $"'{text.Text}' has contrast {contrast:0.0} on {behind}");
		}
	}

	//---------------------------------------------------------------- screens

	//W-R0: the light page, "Remaster a game" (26 bold), two white cards
	//(radius 14) with their 40 px badges, and 2 controls: the purple Start
	//Recording and the secondary Choose Folder…
	[AvaloniaFact]
	public void W_R0_no_project_renders_with_the_player_theme()
	{
		(RemasterWorkspaceViewModel model, _) = Model(Ready, ContraProject(), projectOpen: false);
		(Window window, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model);
		Assert.True(view.FindNamed<StackPanel>("RemasterNoProject").IsOnScreen());

		AssertText(view.FindNamed<TextBlock>("RemasterTitle"), 26, FontWeight.Bold, Text);
		AssertText(view.FindNamed<TextBlock>("RemasterIntro"), 14, FontWeight.Normal, Text2);
		AssertCard(view.FindNamed<Border>("RemasterStartCard"), 14);
		AssertCard(view.FindNamed<Border>("RemasterFolderCard"), 14);
		Assert.Equal(Red, PlayerRender.SolidColor(view.FindNamed<Border>("RemasterStartBadge").Background));
		Assert.Equal(40, view.FindNamed<Border>("RemasterStartBadge").Bounds.Width, 0.5);
		AssertText(view.FindNamed<TextBlock>("RemasterStartGame"), 12.5, FontWeight.Normal, Text2);
		AssertButton(view.FindNamed<Button>("RemasterStartButton"), 30, 8, 13, RemasterTint);
		AssertSecondary(view.FindNamed<Button>("RemasterChooseFolderButton"), 30);
		Assert.Equal(2, ControlsAtRest(view));

		Bitmap frame = Save(window, "W-R0");
		PlayerRender.AssertPixel(WindowBackground, frame, 10, 10);
		PlayerRender.AssertPixel(WindowBackground, frame, 1090, 600);
	}

	//W-R0b: the soft-orange banner (radius 12) with its brown sentence, the
	//secondary Locate Python… and the plain How to Install - 4 controls.
	[AvaloniaFact]
	public void W_R0b_python_banner_renders_with_the_player_theme()
	{
		(RemasterWorkspaceViewModel model, _) = Model(Ready with { Python = PythonGate.Missing, PythonExecutable = "" }, ContraProject(), projectOpen: false);
		(Window window, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model);

		Border banner = view.FindNamed<Border>("RemasterFeasibilityBanner");
		Assert.Equal(WarningFill, PlayerRender.SolidColor(banner.Background));
		Assert.Equal(new CornerRadius(12), banner.CornerRadius);
		//The render's two lines: the bold fact, then what still works.
		TextBlock fact = view.FindNamed<TextBlock>("RemasterFeasibilityText");
		AssertText(fact, 13.5, FontWeight.SemiBold, WarningText);
		Assert.Equal("Painting needs Python 3.10 or newer, which MesenAI could not find.", fact.Text);
		TextBlock still = view.FindNamed<TextBlock>("RemasterFeasibilityDetail");
		AssertText(still, 12.5, FontWeight.Normal, WarningText);
		Assert.Equal("You can still record. Your figures are prepared once Python is available.", still.Text);
		AssertSecondary(view.FindNamed<Button>("LocatePythonButton"));
		Button howTo = view.FindNamed<Button>("HowToInstallPythonButton");
		Assert.Contains("plain", howTo.Classes);
		Assert.Equal(WarningText, PlayerRender.SolidColor(LabelOf(howTo).Foreground));
		Assert.Equal(4, ControlsAtRest(view));
		AssertReadable(banner);
		//The render's leading mark is the drawn orange warning icon, not a glyph.
		Assert.StartsWith("Painting needs Python", view.FindNamed<TextBlock>("RemasterFeasibilityText").Text);
		PathIcon warn = banner.FindAll<PathIcon>().Single(p => p.Classes.Contains("warning"));
		Assert.True(warn.IsOnScreen());
		Assert.Equal(16, warn.Bounds.Width, 0.5);

		Bitmap frame = Save(window, "W-R0b");
		PlayerRender.AssertPixel(WarningFill, frame, 60, (int)banner.TranslatePoint(new Point(0, 4), window)!.Value.Y + 2);
	}

	//W-R1: the project chip (22 bold), the three numbered steps, white cards,
	//the red Record While I Play, two secondary buttons, the figure tiles and
	//the 36 px purple Build & Show in Game - 6 controls.
	[AvaloniaFact]
	public void W_R1_project_screen_renders_with_the_player_theme()
	{
		(RemasterWorkspaceViewModel model, _) = Model(Ready, ContraProject(), projectOpen: true);
		(Window window, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model);
		Assert.True(view.FindNamed<StackPanel>("RemasterProjectScreen").IsOnScreen());

		AssertText(view.FindNamed<TextBlock>("RemasterProjectName"), 22, FontWeight.Bold, Text);
		Button menu = view.FindNamed<Button>("RemasterProjectMenu");
		Assert.Equal(Color.Parse("#EAEAEE"), PlayerRender.SolidColor(menu.Background));
		Assert.Equal(new CornerRadius(8), menu.CornerRadius);
		foreach(string step in new[] { "RemasterStepRecord", "RemasterStepPaint", "RemasterStepSeeIt" }) {
			Border circle = view.FindNamed<Border>(step);
			Assert.Equal(RemasterTint, PlayerRender.SolidColor(circle.Background));
			Assert.Equal(22, circle.Bounds.Width, 0.5);
		}
		AssertText(view.FindNamed<TextBlock>("RemasterZoneRecordLabel"), 13, FontWeight.Bold, Text2);
		AssertCard(view.FindNamed<Border>("RemasterRecordCard"));
		AssertCard(view.FindNamed<Border>("RemasterPaintCard"));
		AssertCard(view.FindNamed<Border>("RemasterSeeItCard"));
		AssertText(view.FindNamed<TextBlock>("RemasterRecordSummary"), 14, FontWeight.SemiBold, Text);
		//Zone ①: the summary and one secondary line, not a list of recordings.
		Assert.Equal("2 recordings", view.FindNamed<TextBlock>("RemasterRecordSummary").Text);
		TextBlock latest = view.FindNamed<TextBlock>("RemasterRecordDetail");
		AssertText(latest, 12.5, FontWeight.Normal, Text2);
		Assert.True(latest.IsOnScreen());
		//ADR-0252 §1: the render's "1 240 shapes seen while you played".
		Assert.Equal("3 shapes seen while you played", latest.Text);
		Assert.False(view.FindNamed<ItemsControl>("RemasterRecordingList").IsOnScreen());
		AssertButton(view.FindNamed<Button>("RemasterRecordButton"), 28, 8, 13, Red);
		AssertSecondary(view.FindNamed<Button>("RemasterTasButton"));
		AssertSecondary(view.FindNamed<Button>("RemasterAiButton"));
		AssertButton(view.FindNamed<Button>("RemasterBuildButton"), 36, 11, 14, RemasterTint);

		Button tile = view.FindNamed<ItemsControl>("RemasterTileList").FindAll<Button>().First(b => b.Classes.Contains("tile"));
		Assert.Equal(new CornerRadius(10), tile.CornerRadius);
		Assert.Equal(Color.Parse("#F6F6F8"), PlayerRender.SolidColor(tile.Background));
		Assert.Equal(118, tile.Bounds.Width, 0.5);
		AssertText(tile.FindAll<TextBlock>().First(t => t.Classes.Contains("name")), 13, FontWeight.SemiBold, Text);
		ToggleButton figures = view.FindNamed<ItemsControl>("RemasterTileCategories").FindAll<ToggleButton>().First();
		Assert.Equal(Card, PlayerRender.SolidColor(figures.Background));
		Assert.Equal(6, ControlsAtRest(view));

		Bitmap frame = Save(window, "W-R1");
		PlayerRender.AssertPixel(WindowBackground, frame, 8, 300);
	}

	//W-R3: zone ③ is the job card - title, step, the 6 px purple bar and a
	//secondary Stop; Record and its neighbours wait, faded - 6 controls.
	[AvaloniaFact]
	public void W_R3_job_card_renders_with_the_player_theme()
	{
		(RemasterWorkspaceViewModel model, FakeLauncher launcher) = Model(Ready, ContraProject(), projectOpen: true);
		(Window window, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model);
		Click(view.FindNamed<Button>("RemasterBuildButton"));
		launcher.Last!.OnLine("steps: 4", false);
		launcher.Last.OnLine("recording: rec-001", false);
		launcher.Last.OnLine("== copy", false);
		launcher.Last.OnLine("ok   copy", false);
		launcher.Last.OnLine("== figures", false);
		WaitFor(() => view.FindNamed<ProgressBar>("RemasterJobProgress").Value > 0, "the bar never moved");

		AssertText(view.FindNamed<TextBlock>("RemasterJobTitle"), 14, FontWeight.SemiBold, Text);
		AssertText(view.FindNamed<TextBlock>("RemasterJobDetail"), 12.5, FontWeight.Normal, Text2);
		ProgressBar bar = view.FindNamed<ProgressBar>("RemasterJobProgress");
		Assert.Equal(6, bar.Bounds.Height, 0.5);
		Assert.Equal(RemasterTint, PlayerRender.SolidColor(bar.Foreground));
		AssertSecondary(view.FindNamed<Button>("RemasterJobStopButton"));
		Assert.False(view.FindNamed<Button>("RemasterRecordButton").IsEffectivelyEnabled);
		Assert.Equal(6, ControlsAtRest(view));
		Save(window, "W-R3");
		launcher.Last.OnExit(-9);
	}

	//W-R4: the problems replace the card - an orange ⚠ title, one sentence
	//per problem with a small secondary Open File, then the plain Show Log and
	//the small purple Try Again.
	[AvaloniaFact]
	public void W_R4_build_problems_render_with_the_player_theme()
	{
		(RemasterWorkspaceViewModel model, FakeLauncher launcher) = Model(Ready, ContraProject(), projectOpen: true);
		(Window window, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model);
		Click(view.FindNamed<Button>("RemasterBuildButton"));
		launcher.Last!.OnLine("steps: 4", false);
		launcher.Last.OnLine("recording: rec-001", false);
		launcher.Last.OnLine("== figures", false);
		launcher.Last.OnLine("error: usr000-figure.png: 384x128 is not a whole multiple of the 192x64 twin — the canvas was resized; repaint at the size you were given", true);
		launcher.Last.OnLine("error   kit/rec-001/sheets/usr003.png  cell index 0 at (1, 10) contains the guide sentinel #FF00FD — hide both layers", true);
		launcher.Last.OnLine("FAIL figures", false);
		launcher.Last.OnExit(1);
		WaitFor(() => !model.IsJobRunning, "the failed build never ended");

		Assert.True(view.FindNamed<StackPanel>("RemasterBuildProblems").IsOnScreen());
		TextBlock problemsTitle = view.FindNamed<TextBlock>("RemasterBuildProblemsTitle");
		AssertText(problemsTitle, 14, FontWeight.SemiBold, Text);
		//The render's title: a drawn orange warning, then the words - no glyph.
		Assert.Equal("2 problems stopped the build", problemsTitle.Text);
		PathIcon warn = view.FindNamed<PathIcon>("RemasterBuildProblemsIcon");
		Assert.True(warn.IsOnScreen());
		Assert.Contains("warning", warn.Classes);
		Assert.Equal(18, warn.Bounds.Width, 0.5);
		//Each problem: the bold name, then the secondary sentence.
		ItemsControl list = view.FindNamed<ItemsControl>("RemasterBuildProblemList");
		TextBlock name = list.FindAll<TextBlock>().First(t => t.Classes.Contains("problem-name"));
		AssertText(name, 13, FontWeight.SemiBold, Text);
		Assert.Equal("\u201Crun\u201D", name.Text);
		TextBlock sentence = list.FindAll<TextBlock>().First(t => t.Classes.Contains("problem-text"));
		AssertText(sentence, 12.5, FontWeight.Normal, Text2);
		Assert.StartsWith("The canvas was resized (it was ", sentence.Text);
		Assert.DoesNotContain(view.FindAll<TextBlock>(), t => t.IsOnScreen() && (t.Text ?? "").Contains('\u26A0'));
		//At the render's 660 px Show Log and Try Again are on screen without scrolling
		//(the render itself crops the card's bottom edge, so only the buttons must fit).
		ScrollViewer page = view.FindAll<ScrollViewer>().First();
		foreach(string action in new[] { "RemasterShowBuildLogButton", "RemasterTryBuildAgainButton" }) {
			Button b = view.FindNamed<Button>(action);
			double bottom = b.TranslatePoint(new Point(0, b.Bounds.Height), page)!.Value.Y;
			Assert.True(bottom <= page.Viewport.Height, $"{action} ends at {bottom}, below the {page.Viewport.Height} px viewport");
		}
		Button open = list.FindAll<Button>().First(b => b.IsOnScreen());
		AssertButton(open, 24, 8, 12, Card);
		Button showLog = view.FindNamed<Button>("RemasterShowBuildLogButton");
		Assert.Contains("plain", showLog.Classes);
		Assert.Equal(24, showLog.Bounds.Height, 0.5);
		AssertButton(view.FindNamed<Button>("RemasterTryBuildAgainButton"), 24, 8, 12, RemasterTint);
		Save(window, "W-R4");
	}

	//W-R5: a tile's ▸ opens the light popover (radius 12) with its header,
	//its lines and the purple Open.
	[AvaloniaFact]
	public void W_R5_tile_popover_renders_with_the_player_theme()
	{
		(RemasterWorkspaceViewModel model, _) = Model(Ready, ContraProject(), projectOpen: true);
		(Window window, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model);
		Button tile = view.FindNamed<ItemsControl>("RemasterTileList").FindAll<Button>().First(b => b.Classes.Contains("tile"));
		TextBlock painted = tile.FindAll<TextBlock>().Single(t => t.Classes.Contains("badges"));
		Assert.Equal(RemasterTint, PlayerRender.SolidColor(painted.Foreground));
		//The render's pill says "Painted" in words, not a ✎.
		Assert.Equal("Painted", painted.Text);

		Button details = view.FindNamed<ItemsControl>("RemasterTileList").FindAll<Button>().First(b => b.Classes.Contains("details"));
		Flyout flyout = Assert.IsType<Flyout>(details.Flyout);
		Assert.Contains("popover", flyout.FlyoutPresenterClasses);
		flyout.ShowAt(details);
		Dispatcher.UIThread.RunJobs();
		StackPanel content = Assert.IsType<StackPanel>(flyout.Content);
		WaitFor(() => content.FindAncestorOfType<FlyoutPresenter>() != null, "the popover never opened");
		FlyoutPresenter presenter = content.FindAncestorOfType<FlyoutPresenter>()!;
		Assert.Equal(Color.Parse("#FCFCFD"), PlayerRender.SolidColor(presenter.Background));
		Assert.Equal(new CornerRadius(12), presenter.CornerRadius);
		//The render's header: the bold name, then "N phases · from recording N" in TEXT2 (this fixture's run is a 6-cell grid, so it counts cells).
		TextBlock header = content.FindAll<TextBlock>().First(t => t.Classes.Contains("header"));
		AssertText(header, 15, FontWeight.Bold, Text);
		Assert.Equal("run", header.Text);
		TextBlock headerDetail = content.FindAll<TextBlock>().First(t => t.Classes.Contains("header-detail"));
		AssertText(headerDetail, 12, FontWeight.Normal, Text2);
		Assert.Equal("6 cells · from recording 1", headerDetail.Text);
		TextBlock seen = content.FindAll<TextBlock>().First(t => t.Classes.Contains("line"));
		AssertText(seen, 12.5, FontWeight.Medium, Text);
		Assert.Equal("Seen in the game", seen.Text);
		//Each fact leads with a drawn icon: the green check, the tinted pencil.
		PathIcon[] marks = content.FindAll<PathIcon>().Where(p => p.Classes.Contains("line-icon")).ToArray();
		Assert.Contains(marks, p => p.Classes.Contains("done") && p.IsOnScreen());
		Assert.Contains(marks, p => p.Classes.Contains("painted") && p.IsOnScreen());
		Assert.DoesNotContain(content.FindAll<TextBlock>(), t => t.IsOnScreen() && (t.Text ?? "").IndexOfAny(new[] { '\u2714', '\u270E', '\u26A0' }) >= 0);
		AssertButton(content.FindAll<Button>().Single(b => b.Classes.Contains("open")), 28, 8, 13, RemasterTint);
		Save(window, "W-R5");
	}

	//W-R6: the finished-pack sheet over a dimmed W-R0 - a 480 px white sheet
	//(radius 14), the 44 px badge, 18 bold title, the warning box and the two
	//32 px buttons; every text readable under both app theme variants (#716).
	[AvaloniaTheory]
	[InlineData("Light")]
	[InlineData("Dark")]
	public void W_R6_import_sheet_renders_with_the_player_theme(string theme)
	{
		Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
		string root = TempFolder();
		string pack = Path.Combine(root, "Contra80s");
		Write(pack, "hires.txt", "<ver>106\n<patch>fix.ips,0123456789ABCDEF0123456789ABCDEF01234567\n");
		(RemasterWorkspaceViewModel model, _) = Model(Ready with { ToolsFolder = Tools() }, ContraProject(), projectOpen: false);
		(Window window, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model);
		model.OpenProjectFolder(pack);
		Dispatcher.UIThread.RunJobs();

		Assert.True(view.FindNamed<Border>("RemasterSheetScrim").IsOnScreen());
		Border sheet = view.FindNamed<Border>("RemasterImportCard");
		Assert.Contains("sheet", sheet.Classes);
		AssertCard(sheet, 14);
		Assert.Equal(480, sheet.Bounds.Width, 0.5);
		AssertText(view.FindNamed<TextBlock>("RemasterImportTitle"), 18, FontWeight.Bold, Text);
		Assert.Equal(44, view.FindNamed<Border>("RemasterImportBadge").Bounds.Width, 0.5);
		Border warning = view.FindNamed<Border>("RemasterImportPatchedBox");
		Assert.True(warning.IsOnScreen());
		Assert.Equal(WarningFill, PlayerRender.SolidColor(warning.Background));
		AssertSecondary(view.FindNamed<Button>("RemasterImportCancelButton"), 32);
		AssertButton(view.FindNamed<Button>("RemasterMakeEditableButton"), 32, 8, 13, RemasterTint);
		Assert.True(view.FindNamed<Button>("RemasterMakeEditableButton").IsEffectivelyEnabled);
		Assert.Equal(2, sheet.FindAll<Button>().Count(b => b.IsOnScreen()));
		AssertReadable(sheet);
		if(theme == "Light") {
			Bitmap frame = Save(window, "W-R6");
		//The primary paints purple edge to edge: no Fluent accent fill on the presenter.
		AssertTintAt(frame, window, view.FindNamed<Button>("RemasterMakeEditableButton"));
		}
	}

	//W-R7: the composer hand-off sheet over a dimmed W-R1 - 460 px, the green
	//check line and Cancel / Open Composer (2 controls).
	[AvaloniaTheory]
	[InlineData("Light")]
	[InlineData("Dark")]
	public void W_R7_compose_sheet_renders_with_the_player_theme(string theme)
	{
		Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
		string project = ContraProject();
		Write(project, "auto/rec-002/textures/sheets/adjacency.json", "{}");
		(RemasterWorkspaceViewModel model, _) = Model(Ready with { ToolsFolder = Tools() }, project, projectOpen: true);
		(Window window, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model);
		model.ShowCompose();
		Dispatcher.UIThread.RunJobs();

		Assert.True(view.FindNamed<Border>("RemasterSheetScrim").IsOnScreen());
		Border sheet = view.FindNamed<Border>("RemasterComposeCard");
		AssertCard(sheet, 14);
		Assert.Equal(460, sheet.Bounds.Width, 0.5);
		AssertText(view.FindNamed<TextBlock>("RemasterComposeTitle"), 18, FontWeight.Bold, Text);
		AssertSecondary(view.FindNamed<Button>("RemasterComposeCancelButton"), 32);
		AssertButton(view.FindNamed<Button>("RemasterOpenComposerButton"), 32, 8, 13, RemasterTint);
		Assert.True(view.FindNamed<Button>("RemasterOpenComposerButton").IsEffectivelyEnabled);
		//The render's check line: a drawn green check, then the sentence.
		Assert.StartsWith("This project has the layout data", view.FindNamed<TextBlock>("RemasterComposeHint").Text);
		PathIcon check = view.FindNamed<PathIcon>("RemasterComposeHintIcon");
		Assert.True(check.IsOnScreen());
		Assert.Contains("done", check.Classes);
		Assert.Equal(2, sheet.FindAll<Button>().Count(b => b.IsOnScreen()));
		AssertReadable(sheet);
		if(theme == "Light") {
			Bitmap frame = Save(window, "W-R7");
		//The primary paints purple edge to edge: no Fluent accent fill on the presenter.
		AssertTintAt(frame, window, view.FindNamed<Button>("RemasterOpenComposerButton"));
		}
	}

	//W-R2: the dark pill over the game (radius 12) - red dot, the white
	//14 semibold clock, the purple Remaster badge and the grey Stop: 1 control.
	[AvaloniaFact]
	public void W_R2_recording_strip_renders_with_the_player_theme()
	{
		(RemasterWorkspaceViewModel model, _) = Model(Ready, ContraProject(), projectOpen: true);
		//Recording needs the core; the strip only reads these two.
		typeof(RemasterWorkspaceViewModel).GetProperty(nameof(RemasterWorkspaceViewModel.IsRecording))!.SetValue(model, true);
		typeof(RemasterWorkspaceViewModel).GetProperty(nameof(RemasterWorkspaceViewModel.RecordingPill))!.SetValue(model, "Recording 01:42");
		//The core's live coverage report (TilesSeen, ScreensSeen) as the tick reads it.
		typeof(RemasterWorkspaceViewModel).GetMethod("UpdateRecordingCounters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(model, new object[] { 318u, 2u });
		RemasterRecordingStrip strip = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
		RemasterRecordingHint hint = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
		hint.DataContext = model;
		(Window window, _) = Host(new Panel { Children = { strip, hint } }, model);
		window.Background = Brushes.Black;
		Dispatcher.UIThread.RunJobs();

		Border pill = strip.FindNamed<Border>("RemasterRecordingPillBox");
		Assert.Contains("hud", pill.Classes);
		Assert.Equal(new CornerRadius(12), pill.CornerRadius);
		Assert.Equal(48, pill.Bounds.Height, 0.5);
		AssertText(strip.FindNamed<TextBlock>("RemasterRecordingPill"), 14, FontWeight.SemiBold, Card);
		Assert.Equal(RemasterTint, PlayerRender.SolidColor(strip.FindNamed<Border>("RemasterRecordingBadge").Background));
		AssertButton(strip.FindNamed<Button>("RemasterStopRecordingButton"), 28, 8, 13, Color.Parse("#5A5A5F"));
		Assert.Equal(1, strip.FindAll<Button>().Count(b => b.IsOnScreen()));
		//The render's counters sit in the pill; its hint is a separate toast at the bottom.
		TextBlock counters = strip.FindNamed<TextBlock>("RemasterRecordingCounters");
		Assert.True(counters.IsOnScreen());
		Assert.Equal("318 shapes seen · 2 screens captured", counters.Text);
		AssertText(counters, 12.5, FontWeight.Normal, Color.Parse("#C8C8CD"));
		Assert.DoesNotContain(strip.FindAll<TextBlock>(), t => t.IsOnScreen() && t.Text == "Play through what you want to repaint. Esc stops.");
		Border toast = hint.FindNamed<Border>("RemasterRecordingHintBox");
		Assert.True(toast.IsOnScreen());
		Assert.Contains("hud", toast.Classes);
		Assert.Equal(new CornerRadius(10), toast.CornerRadius);
		Assert.Equal(36, toast.Bounds.Height, 0.5);
		TextBlock hintText = hint.FindNamed<TextBlock>("RemasterRecordingHintText");
		Assert.Equal("Play through what you want to repaint. Esc stops.", hintText.Text);
		AssertText(hintText, 12.5, FontWeight.Medium, Card);
		Point toastTop = toast.TranslatePoint(new Point(0, 0), window)!.Value;
		Assert.True(toastTop.Y > ContentHeight / 2, "the hint toast is not at the bottom");
		Save(window, "W-R2");
	}

	//Decision 3, MainWindow side: in Player mode Remaster's hosts are the
	//theme's scope with the purple tint; Advanced keeps the classic look.
	[AvaloniaTheory]
	[InlineData(UiMode.Player)]
	[InlineData(UiMode.Advanced)]
	public void Remaster_hosts_carry_the_player_scope_in_player_mode_only(UiMode mode)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = mode;
		prefs.Workspace = Workspace.Play;
		prefs.ClassicMenuNoticeShown = true;
		prefs.ShowClassicMenuBar = false;
		//As RemasterWorkspaceTests.ShowShell: no pausing and no packs on load.
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = false;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = false;
		ConfigManager.Config.EnhancementPacks.ApplyConfig();
		//One MainWindow per case, never Close()d (as RemasterWorkspaceTests):
		//two windows in one test, each closed, crashed the test host (exit 139).
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus");
		model.SelectWorkspace(Workspace.Remaster);
		Dispatcher.UIThread.RunJobs();

		foreach(string host in new[] { "RemasterWorkspaceHost", "RemasterRecordingStripHost", "RemasterRecordingHintHost" }) {
			Panel panel = window.FindNamed<Panel>(host);
			Assert.Contains("remaster", panel.Classes);
			Assert.Equal(mode == UiMode.Player, panel.Classes.Contains("player"));
		}
		if(mode == UiMode.Player) {
			Assert.Equal(RemasterTint, PlayerRender.SolidColor(window.FindNamed<Button>("RemasterStartButton").Background));
			PlayerRender.Save(PlayerRender.Capture(window), "W-R0-window");
		} else {
			Assert.NotEqual("Inter", LabelOf(window.FindNamed<Button>("RemasterStartButton")).FontFamily.Name);
		}
	}

	//---------------------------------------------------------------- pixels

	private static byte[] Rgba(byte r, byte g, byte b) => new byte[] { r, g, b, 255 };

	//An 8 x 8 mirrored pixel figure in two colours, like the render's figure().
	private static byte[] Sprite(int seed, byte[] light, byte[] dark)
	{
		Random rnd = new(seed * 7 + 3);
		bool[,] on = new bool[8, 4];
		for(int y = 0; y < 8; y++) {
			for(int x = 0; x < 4; x++) {
				on[y, x] = rnd.NextDouble() < 0.55 || (y is 2 or 3 && x >= 1);
			}
		}
		return Png(8, 8, i => {
			int y = i / 8, x = i % 8;
			int half = x < 4 ? x : 7 - x;
			return on[y, half] ? (y < 4 ? light : dark) : new byte[] { 0, 0, 0, 0 };
		});
	}

	private static string Write(string root, string relative, string text)
	{
		string path = Path.Combine(root, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, text);
		return path;
	}

	private static void WritePair(string root, string relative, byte[] png)
	{
		string path = Path.Combine(root, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, png);
		File.WriteAllBytes(path.Substring(0, path.Length - 4) + ".orig.png", png);
	}

	//8-bit RGBA, filter 0 (as RemasterTileBrowserTests writes).
	private static byte[] Png(int width, int height, Func<int, byte[]> pixel)
	{
		using MemoryStream raw = new();
		for(int y = 0; y < height; y++) {
			raw.WriteByte(0);
			for(int x = 0; x < width; x++) {
				raw.Write(pixel(y * width + x));
			}
		}
		using MemoryStream z = new();
		using(ZLibStream deflate = new(z, CompressionLevel.Optimal, true)) {
			deflate.Write(raw.ToArray());
		}
		byte[] ihdr = new byte[13];
		BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
		BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
		ihdr[8] = 8;
		ihdr[9] = 6;
		using MemoryStream png = new();
		png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
		Chunk(png, "IHDR", ihdr);
		Chunk(png, "IDAT", z.ToArray());
		Chunk(png, "IEND", Array.Empty<byte>());
		return png.ToArray();
	}

	private static void Chunk(Stream s, string tag, byte[] body)
	{
		byte[] len = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(len, (uint)body.Length);
		s.Write(len);
		byte[] tagBytes = Encoding.ASCII.GetBytes(tag);
		s.Write(tagBytes);
		s.Write(body);
		uint c = 0xFFFFFFFF;
		foreach(byte b in tagBytes.Concat(body)) {
			c ^= b;
			for(int k = 0; k < 8; k++) {
				c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
			}
		}
		byte[] crc = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(crc, c ^ 0xFFFFFFFF);
		s.Write(crc);
	}
}
