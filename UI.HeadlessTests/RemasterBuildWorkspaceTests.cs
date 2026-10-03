using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

//G.6 (PRD Part B §13.5.3 W-R1 zone ③, W-R3, W-R4; §13.5.5 W-X3): the XAML
//wiring of Build & show in game, its problems and the interruptions. The rules
//are pinned host-free in UI.Tests/Remaster (RemasterBuild*, RemasterInterruptions);
//this checks the crossing into RemasterWorkspaceView, the strip, and - with
//the real core and a synthetic NROM - that quitting or opening another game
//while recording asks once, inline, instead of cutting the recording.
[Collection(NativeCoreCollection.Name)]
public class RemasterBuildWorkspaceTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _showClassicMenuBar = ConfigManager.Config.Preferences.ShowClassicMenuBar;
	private readonly bool _noticeShown = ConfigManager.Config.Preferences.ClassicMenuNoticeShown;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly bool _confirmExit = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _bootstrap = ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
	private readonly List<string> _folders = new();

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ShowClassicMenuBar = _showClassicMenuBar;
		prefs.ClassicMenuNoticeShown = _noticeShown;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		prefs.ConfirmExitResetPower = _confirmExit;
		ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = _bootstrap;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		if(NativeCore.IsAvailable) {
			ConfigManager.Config.EnhancementPacks.ApplyConfig();
		}
		ConfigManager.Config.Save();
		foreach(string folder in _folders) {
			try {
				Directory.Delete(folder, true);
			} catch(IOException) {
			} catch(UnauthorizedAccessException) {
			}
		}
	}

	private string TempFolder()
	{
		string folder = Path.Combine(Path.GetTempPath(), "mesen-g6-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_folders.Add(folder);
		return folder;
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
		Assert.True(button.IsEffectivelyEnabled, $"{button.Name} is disabled");
		button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
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
		public IReadOnlyList<string> Argv = Array.Empty<string>();

		public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
		{
			Argv = argv;
			Last = new Job { OnLine = onLine, OnExit = onExit };
			return Last;
		}
	}

	private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, "/tools");

	//A Contra project with one recording and its kit (one painted figure and
	//the map sheet, captioned in kit.json), shown as W-R1 for the running game.
	private (Window Window, RemasterWorkspaceViewModel Model, FakeLauncher Launcher, string Project, List<RemasterShowAction> Shown) ShowBuildableProject()
	{
		string root = TempFolder();
		string project = Path.Combine(root, "Contra (USA)");
		Directory.CreateDirectory(Path.Combine(project, "auto", "rec-001", "textures"));
		File.WriteAllText(Path.Combine(project, "auto", "rec-001", "textures", "hires.txt"), "<ver>107\n");
		string kit = Path.Combine(project, "kit", "rec-001");
		Directory.CreateDirectory(Path.Combine(kit, "figures"));
		File.WriteAllText(Path.Combine(kit, "figures", "usr001-figure.png"), "x");
		File.WriteAllText(Path.Combine(kit, "kit.json"),
			"{\"parts\":[{\"files\":[{\"path\":\"sheets/usr001.png\",\"caption\":\"run\",\"figure\":\"figures/usr001-figure.png\"},{\"path\":\"sheets/map-000.png\",\"caption\":\"stage 1 map\"}]}]}");

		FakeLauncher launcher = new();
		List<RemasterShowAction> shown = new();
		RemasterWorkspaceViewModel model = new(new RemasterConfig(), _ => Ready, launcher, hasHeadlessRecorder: false);
		model.ShowInGame = action => {
			shown.Add(action);
			return true;
		};
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", Path.Combine(root, "Contra (USA).nes"), project, Path.Combine(root, "EnhancementPacks"));
		model.EnsureFeasibilityMeasured();
		WaitFor(() => model.Feasibility != null, "the gate was never measured");

		Window window = new() { Content = new RemasterWorkspaceView { DataContext = model }, Width = 1000, Height = 900 };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return (window, model, launcher, project, shown);
	}

	[AvaloniaFact]
	public void Build_and_show_runs_the_build_job_on_the_card_and_shows_the_result_in_the_game()
	{
		(Window window, RemasterWorkspaceViewModel model, FakeLauncher launcher, string project, List<RemasterShowAction> shown) = ShowBuildableProject();
		Button build = window.FindNamed<Button>("RemasterBuildButton");
		Assert.True(build.IsEffectivelyEnabled);
		Assert.False(window.FindNamed<TextBlock>("RemasterBuildReason").IsOnScreen());
		Assert.Equal("Not built yet.", window.FindNamed<TextBlock>("RemasterBuildFreshness").Text);

		Click(build);

		Assert.Equal(new[] { "/usr/bin/env", "python3", Path.Combine("/tools", "mep_project.py"), "build", project, "--rom" }, launcher.Argv.Take(6).ToArray());
		Assert.True(window.FindNamed<StackPanel>("RemasterJobCard").IsOnScreen());
		Assert.False(build.IsOnScreen());
		Assert.Equal("Building your project…", window.FindNamed<TextBlock>("RemasterJobTitle").Text);
		launcher.Last!.OnLine("steps: 4", false);
		launcher.Last.OnLine("recording: rec-001", false);
		launcher.Last.OnLine("== copy", false);
		launcher.Last.OnLine("ok   copy", false);
		launcher.Last.OnLine("== build", false);
		WaitFor(() => window.FindNamed<TextBlock>("RemasterJobDetail").Text == "step 2 of 4 · building the pack", "the card never showed step 2 of 4");
		Assert.Equal(25, window.FindNamed<ProgressBar>("RemasterJobProgress").Value);

		launcher.Last.OnLine("ok   build", false);
		launcher.Last.OnLine("show: images", false);
		launcher.Last.OnExit(0);
		WaitFor(() => !model.IsJobRunning, "the build never ended");

		Assert.Equal(new[] { RemasterShowAction.ReloadImages }, shown);
		Assert.True(model.IsShowingBuild);
		Assert.True(model.ShowsGame);
		Assert.Equal("Built · no problems · showing in game", window.FindNamed<TextBlock>("RemasterJobTitle").Text);
		//A later Changed (the 5 s collapse) never shows it twice.
		model.Refresh();
		Assert.Single(shown);

		model.LeaveGameView();
		Assert.False(model.ShowsGame);
	}

	[AvaloniaFact]
	public void A_failed_build_replaces_the_card_with_its_problems_named_after_the_painted_surface()
	{
		(Window window, RemasterWorkspaceViewModel model, FakeLauncher launcher, string project, List<RemasterShowAction> shown) = ShowBuildableProject();
		Click(window.FindNamed<Button>("RemasterBuildButton"));
		launcher.Last!.OnLine("steps: 4", false);
		launcher.Last.OnLine("recording: rec-001", false);
		launcher.Last.OnLine("== figures", false);
		launcher.Last.OnLine("error: usr001-figure.png: 644x128 is not a whole multiple of the 160x32 twin — the canvas was resized; repaint at the size you were given", true);
		launcher.Last.OnLine("error   textures/sheets/map-000.png  cell index 0 at (1, 10) contains the guide sentinel #FF00FD — hide both layers", true);
		launcher.Last.OnLine("error: something the reader does not know", true);
		launcher.Last.OnLine("FAIL figures", false);
		launcher.Last.OnExit(1);
		WaitFor(() => !model.IsJobRunning, "the failed build never ended");

		Assert.Empty(shown);
		Assert.False(window.FindNamed<StackPanel>("RemasterJobCard").IsOnScreen());
		Assert.True(window.FindNamed<StackPanel>("RemasterBuildProblems").IsOnScreen());
		Assert.Equal("⚠ 3 problems stopped the build.", window.FindNamed<TextBlock>("RemasterBuildProblemsTitle").Text);
		string[] rows = window.FindNamed<ItemsControl>("RemasterBuildProblemList").FindAll<TextBlock>().Select(t => t.Text ?? "").ToArray();
		Assert.Contains("· \"run\" — the canvas was resized (was 640×128). Undo the resize and save again.", rows);
		Assert.Contains(rows, r => r.StartsWith("· \"stage 1 map\" — a pink marker is still on the image."));
		Assert.DoesNotContain(rows, r => r.Contains("something the reader"));
		Assert.Equal("1 more only in the log (Show Log).", window.FindNamed<TextBlock>("RemasterBuildProblemsNote").Text);
		Assert.Equal(2, window.FindNamed<ItemsControl>("RemasterBuildProblemList").FindAll<Button>().Count(b => b.IsVisible));
		Assert.Equal(Path.Combine(project, "kit", "rec-001", "figures", "usr001-figure.png"), model.BuildProblems[0].FilePath);

		//The raw lines live behind Show Log only.
		Assert.False(window.FindNamed<TextBox>("RemasterBuildLog").IsOnScreen());
		Click(window.FindNamed<Button>("RemasterShowBuildLogButton"));
		Assert.True(window.FindNamed<TextBox>("RemasterBuildLog").IsOnScreen());
		Assert.Contains("error: something the reader does not know", window.FindNamed<TextBox>("RemasterBuildLog").Text);

		//Try Again is the Build button again: a new job, the card back.
		Click(window.FindNamed<Button>("RemasterTryBuildAgainButton"));
		Assert.True(model.IsJobRunning);
		Assert.False(window.FindNamed<StackPanel>("RemasterBuildProblems").IsOnScreen());
		Assert.True(window.FindNamed<StackPanel>("RemasterJobCard").IsOnScreen());
		launcher.Last.OnExit(-9);
	}

	//#701: a result belongs to the project it ran on (RemasterJobs.ShownFor).
	//After Switch Project, A's failed build - its card and its problem rows,
	//which open A's kit files - is not shown on B; back on A it is.
	[AvaloniaFact]
	public void A_failed_build_of_one_project_is_not_shown_on_another()
	{
		(Window window, RemasterWorkspaceViewModel model, FakeLauncher launcher, string project, _) = ShowBuildableProject();
		Click(window.FindNamed<Button>("RemasterBuildButton"));
		launcher.Last!.OnLine("steps: 4", false);
		launcher.Last.OnLine("recording: rec-001", false);
		launcher.Last.OnLine("error: usr001-figure.png: 644x128 is not a whole multiple of the 160x32 twin — the canvas was resized; repaint at the size you were given", true);
		launcher.Last.OnExit(1);
		WaitFor(() => !model.IsJobRunning, "the failed build never ended");
		Assert.True(window.FindNamed<StackPanel>("RemasterBuildProblems").IsOnScreen());

		string other = Path.Combine(TempFolder(), "Metroid (USA)");
		Directory.CreateDirectory(Path.Combine(other, "auto", "rec-001", "textures"));
		File.WriteAllText(Path.Combine(other, "auto", "rec-001", "textures", "hires.txt"), "<ver>107\n");
		Assert.True(model.OpenProjectFolder(other));
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(other, model.ProjectFolder);
		Assert.False(window.FindNamed<StackPanel>("RemasterBuildProblems").IsOnScreen(), "A's build problems are shown on B");
		Assert.False(window.FindNamed<StackPanel>("RemasterJobCard").IsOnScreen(), "A's job card is shown on B");

		Assert.True(model.OpenProjectFolder(project));
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<StackPanel>("RemasterBuildProblems").IsOnScreen(), "A's own build problems are gone");
		Assert.Equal(Path.Combine(project, "kit", "rec-001", "figures", "usr001-figure.png"), model.BuildProblems[0].FilePath);
	}

	[AvaloniaFact]
	public void Without_a_kit_build_is_disabled_with_its_reason()
	{
		(Window window, RemasterWorkspaceViewModel model, _, string project, _) = ShowBuildableProject();
		Directory.Delete(Path.Combine(project, "kit"), true);
		model.Refresh();
		Dispatcher.UIThread.RunJobs();

		Assert.False(window.FindNamed<Button>("RemasterBuildButton").IsEffectivelyEnabled);
		Assert.Equal("Prepare your figures first.", window.FindNamed<TextBlock>("RemasterBuildReason").Text);
	}

	[AvaloniaFact]
	public void The_interruption_bar_asks_once_and_runs_the_continuation_only_on_the_second_button()
	{
		InterruptionViewModel question = new();
		Window window = new() { Content = new InterruptionBar { DataContext = question }, Width = 900, Height = 80 };
		window.Show();
		int ran = 0;

		question.Ask(InterruptionKind.OpenWhileClassicBuilder, "Castlevania", 0, false, () => ran++);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("Open Castlevania? HD Pack Builder (classic) stops; what it wrote is kept.", window.FindNamed<TextBlock>("InterruptionText").Text);
		Assert.Equal("Cancel", window.FindNamed<Button>("InterruptionKeepButton").Content);
		Click(window.FindNamed<Button>("InterruptionKeepButton"));
		Assert.False(question.IsVisible);
		Assert.Equal(0, ran);

		question.Ask(InterruptionKind.QuitWhileJob, "", 0, true, () => ran++);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("A build is running. Quit anyway? It stops, and nothing you painted is lost.", window.FindNamed<TextBlock>("InterruptionText").Text);
		Assert.Equal("Quit", window.FindNamed<Button>("InterruptionGoButton").Content);
		Click(window.FindNamed<Button>("InterruptionGoButton"));
		Assert.Equal(1, ran);
		Assert.False(question.IsVisible);
	}

	//The stop rule for W-X3, against the real core: closing the window while
	//recording asks instead of cutting the recording; opening another game
	//asks, and Stop and Open keeps the recording and lands the new game in Play.
	[AvaloniaFact]
	public void Quitting_or_opening_a_game_while_recording_asks_first()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ShowClassicMenuBar = false;
		prefs.ClassicMenuNoticeShown = true;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		prefs.ConfirmExitResetPower = false;
		ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = false;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = false;
		ConfigManager.Config.EnhancementPacks.ApplyConfig();

		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");

		string folder = TempFolder();
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		string other = Path.Combine(folder, "other-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		File.WriteAllBytes(other, SyntheticNrom.Build());
		try {
			Assert.False(window.FindNamed<Panel>("InterruptionBarHost").IsOnScreen());
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
			EmuApi.Resume();
			model.SelectWorkspace(Workspace.Remaster);
			Dispatcher.UIThread.RunJobs();
			Assert.True(model.Remaster.StartRecording());
			Assert.True(EmuApi.IsMepBootstrapping());

			window.Close();
			Dispatcher.UIThread.RunJobs();
			Assert.True(window.IsVisible, "the window closed with a recording running");
			Assert.True(EmuApi.IsMepBootstrapping(), "closing cut the recording");
			Assert.True(window.FindNamed<Panel>("InterruptionBarHost").IsOnScreen());
			Assert.Equal("Quit while recording? What you recorded so far is kept as recording 1.", window.FindNamed<TextBlock>("InterruptionText").Text);
			Click(window.FindNamed<Button>("InterruptionKeepButton"));
			Assert.False(window.FindNamed<Panel>("InterruptionBarHost").IsOnScreen());
			Assert.True(EmuApi.IsMepBootstrapping());

			LoadRomHelper.LoadFile(other);
			WaitFor(() => model.Interruption.IsVisible, "opening another game never asked");
			Assert.Equal("Open other-nrom? This recording stops and is kept as recording 1.", window.FindNamed<TextBlock>("InterruptionText").Text);
			Assert.True(EmuApi.IsMepBootstrapping(), "the question stopped the recording before an answer");
			Click(window.FindNamed<Button>("InterruptionGoButton"));
			Assert.False(EmuApi.IsMepBootstrapping());
			Assert.False(model.Remaster.IsRecording);
			Assert.Equal(Workspace.Play, model.Shell.Active);
			WaitFor(() => model.RomInfo.GetRomName() == "other-nrom", "the other game never opened");
			Assert.Contains("\"id\": \"rec-001\"", File.ReadAllText(Path.Combine(folder, "synthetic-nrom", "project.json")));
			WaitFor(() => !model.Remaster.Job.IsRunning, "the kit job never finished", 180000);
		} finally {
			if(EmuApi.IsMepBootstrapping()) {
				EmuApi.StopMepRecording();
			}
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}
}
