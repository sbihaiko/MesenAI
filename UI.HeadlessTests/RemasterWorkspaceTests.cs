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
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//G.3 (PRD Part B §8, ADR-0241/ADR-0243, §13.5.3 W-R0-W-R3): the Remaster
//workspace's XAML wiring. The rules (which screen, each control's reason, the
//job's progress, the Python gate) are pinned host-free in UI.Tests/Remaster;
//this checks the crossing into MainWindow.axaml and RemasterWorkspaceView, and
//- with the real core and a synthetic NROM - that Record While I Play writes
//auto/rec-001/ plus its project.json entry, keeps recording across a profile
//switch, and lists the recording on W-R1 after Stop.
[Collection(NativeCoreCollection.Name)]
public class RemasterWorkspaceTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _showClassicMenuBar = ConfigManager.Config.Preferences.ShowClassicMenuBar;
	private readonly bool _noticeShown = ConfigManager.Config.Preferences.ClassicMenuNoticeShown;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
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
		ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = _bootstrap;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		//The VM-only tests below never load the core; pushing the config to it
		//is only possible (and only needed) when it is there.
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
		string folder = Path.Combine(Path.GetTempPath(), "mesen-g3-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_folders.Add(folder);
		return folder;
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowShell()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ShowClassicMenuBar = false;
		prefs.ClassicMenuNoticeShown = true;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		//ADR-0243 Q3: no recording by itself on load; Remaster's Record is the only start.
		ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = false;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = false;
		ConfigManager.Config.EnhancementPacks.ApplyConfig();

		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		return (window, model);
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

	[AvaloniaFact]
	public void Remaster_without_a_game_is_W_R0_whose_primary_opens_a_rom_and_nothing_of_play_shows()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();

		model.SelectWorkspace(Workspace.Remaster);
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<StackPanel>("RemasterNoProject").IsOnScreen());
		Assert.False(window.FindNamed<StackPanel>("RemasterProjectScreen").IsOnScreen());
		Assert.Equal("Open a ROM to Start", window.FindNamed<Button>("RemasterStartButton").Content);
		Assert.True(window.FindNamed<Button>("RemasterStartButton").IsEffectivelyEnabled);
		Assert.True(window.FindNamed<Button>("RemasterChooseFolderButton").IsOnScreen());
		//Neither Share's screens nor Play's surfaces are on screen.
		Assert.False(window.FindNamed<Panel>("ShareWorkspaceHost").IsOnScreen());
		Assert.False(window.FindNamed<Panel>("PlayWorkspace").IsOnScreen());
		Assert.False(window.FindNamed<Panel>("RemasterRecordingStripHost").IsOnScreen());
		Assert.False(model.IsNativeRendererVisible);
	}

	//The stop rule, against the real core: Record While I Play creates
	//auto/rec-001/ and its project.json entry, switching profile keeps the
	//recording running (§13.6, with the red dot and the status sentence), and
	//Stop lists it on W-R1 with TAS and AI disabled with their reasons.
	[AvaloniaFact]
	public void Record_while_i_play_writes_rec_001_survives_a_profile_switch_and_lists_it_after_stop()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();

		string folder = TempFolder();
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, BuildSyntheticNrom());
		string project = Path.Combine(folder, "synthetic-nrom");
		try {
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
			EmuApi.Resume();
			Assert.False(EmuApi.IsMepBootstrapping(), "the load recorded by itself (ADR-0243 Q3)");

			model.SelectWorkspace(Workspace.Remaster);
			Dispatcher.UIThread.RunJobs();
			Assert.True(window.FindNamed<StackPanel>("RemasterNoProject").IsOnScreen());
			Assert.Equal("synthetic-nrom", window.FindNamed<TextBlock>("RemasterStartGame").Text);
			Button start = window.FindNamed<Button>("RemasterStartButton");
			Assert.Equal("Start Recording", start.Content);

			Click(start);

			//The core starts it off the UI thread (Record_and_stop_run_off_the_ui_thread...).
			WaitFor(() => model.Remaster.IsRecording, "the recording never started");
			Assert.True(EmuApi.IsMepBootstrapping());
			Assert.True(Directory.Exists(Path.Combine(project, "auto", "rec-001")), "no auto/rec-001/ next to the ROM");
			//W-R2: the game fills the content area inside Remaster, one strip on top.
			Assert.True(window.FindNamed<Panel>("RemasterRecordingStripHost").IsOnScreen());
			Assert.False(window.FindNamed<Panel>("RemasterWorkspaceHost").IsOnScreen());
			Assert.True(model.IsGameViewVisible);
			Assert.False(window.FindNamed<Panel>("PlayWorkspace").IsOnScreen());
			WaitFor(() => window.FindNamed<TextBlock>("RemasterRecordingPill").Text?.StartsWith("Recording 00:") == true, "the pill never showed the clock");

			//§13.6: switching never stops a recording; the button carries the red dot.
			model.SelectWorkspace(Workspace.Play);
			Dispatcher.UIThread.RunJobs();
			Assert.True(EmuApi.IsMepBootstrapping(), "switching to Play stopped the recording");
			Assert.True(model.Shell.ShowsRecordingDot);
			Assert.Contains("Remaster: recording synthetic-nrom", model.Shell.StatusText);
			Assert.False(window.FindNamed<Panel>("RemasterRecordingStripHost").IsOnScreen());
			UInt32 before = EmuApi.GetTimingInfo(CpuType.Nes).FrameCount;
			WaitFor(() => EmuApi.GetTimingInfo(CpuType.Nes).FrameCount > before + 5, "no frames were emulated while recording");

			model.SelectWorkspace(Workspace.Remaster);
			Dispatcher.UIThread.RunJobs();
			Assert.False(model.Shell.ShowsRecordingDot);
			Click(window.FindNamed<Button>("RemasterStopRecordingButton"));

			WaitFor(() => !model.Remaster.IsRecording, "the recording never stopped");
			Assert.False(EmuApi.IsMepBootstrapping());
			string manifest = File.ReadAllText(Path.Combine(project, "project.json"));
			Assert.Contains("\"id\": \"rec-001\"", manifest);
			Assert.Contains("\"source\": \"play\"", manifest);

			//W-R1, the project screen, lists the recording.
			Assert.True(window.FindNamed<StackPanel>("RemasterProjectScreen").IsOnScreen());
			Assert.Equal("synthetic-nrom", window.FindNamed<TextBlock>("RemasterProjectName").Text);
			Assert.Equal("1 recording", window.FindNamed<TextBlock>("RemasterRecordSummary").Text);
			//ADR-0252 §1: the shapes the real recording drew, read off its hires.txt.
			WaitFor(() => model.Remaster.ShapesSettled.IsCompleted, "the shapes count never settled");
			Assert.Matches(@"^(No shapes|1 shape|[0-9,]+ shapes) seen while you played$", window.FindNamed<TextBlock>("RemasterRecordDetail").Text);
			Assert.False(window.FindNamed<Button>("RemasterTasButton").IsEffectivelyEnabled);
			Assert.Equal("Coming in a later version.", window.FindNamed<TextBlock>("RemasterTasReason").Text);
			Assert.False(window.FindNamed<Button>("RemasterAiButton").IsEffectivelyEnabled);
			Assert.Equal("Not available yet: the AI recorder is still being tested.", window.FindNamed<TextBlock>("RemasterAiReason").Text);
			Assert.False(window.FindNamed<Button>("RemasterBuildButton").IsEffectivelyEnabled);

			//Whatever the kit run after Stop did, it ends as a card with a result
			//(the synthetic ROM may have nothing to project), never a hang.
			WaitFor(() => !model.Remaster.IsJobRunning, "the kit job never finished", 180000);
		} finally {
			if(EmuApi.IsMepBootstrapping()) {
				EmuApi.StopMepRecording();
			}
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//W-R0b and W-R3 over a fake child process: the banner with its reason,
	//recording still enabled without Python, and the job card's progress, Stop
	//and result. The VM is fed directly; no ROM is involved.
	private sealed class HangingLauncher : IJobProcessLauncher
	{
		public sealed class Job : IJobProcess
		{
			public Action<string, bool> OnLine = (_, _) => { };
			public Action<int> OnExit = _ => { };
			public bool Killed;

			public void Kill()
			{
				Killed = true;
				OnExit(-9);
			}
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

	private (Window Window, RemasterWorkspaceViewModel Model, HangingLauncher Launcher, string Project) ShowProjectView(RemasterFeasibility feasibility)
	{
		string root = TempFolder();
		string project = Path.Combine(root, "Contra (USA)");
		Directory.CreateDirectory(Path.Combine(project, "auto", "rec-001", "textures"));
		File.WriteAllText(Path.Combine(project, "auto", "rec-001", "textures", "hires.txt"), "<ver>107\n");
		HangingLauncher launcher = new();
		RemasterWorkspaceViewModel model = new(new RemasterConfig(), _ => feasibility, launcher, hasHeadlessRecorder: false);
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", Path.Combine(root, "Contra (USA).nes"), project, Path.Combine(root, "EnhancementPacks"));
		model.EnsureFeasibilityMeasured();
		WaitFor(() => model.Feasibility != null, "the gate was never measured");

		Window window = new() { Content = new RemasterWorkspaceView { DataContext = model }, Width = 1000, Height = 800 };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return (window, model, launcher, project);
	}

	private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, "/tools");

	[AvaloniaFact]
	public void Python_missing_shows_W_R0b_with_its_reason_and_recording_stays_enabled()
	{
		(Window window, _, _, _) = ShowProjectView(Ready with { Python = PythonGate.Missing, PythonExecutable = "" });

		Assert.True(window.FindNamed<Border>("RemasterFeasibilityBanner").IsOnScreen());
		Assert.StartsWith("Painting needs Python 3.10 or newer", window.FindNamed<TextBlock>("RemasterFeasibilityText").Text);
		Assert.True(window.FindNamed<Button>("LocatePythonButton").IsOnScreen());
		Assert.True(window.FindNamed<Button>("RemasterRecordButton").IsEffectivelyEnabled);
		Assert.False(window.FindNamed<Button>("RemasterPrepareButton").IsEffectivelyEnabled);
		Assert.Equal("Needs Python 3.10 or newer.", window.FindNamed<TextBlock>("RemasterPrepareReason").Text);
		//Off the macOS arm64 zip, TAS says why it cannot work here (ADR-0243 Decision 6).
		Assert.Equal("Not in this build.", window.FindNamed<TextBlock>("RemasterTasReason").Text);
	}

	[AvaloniaFact]
	public void Prepare_figures_runs_the_kit_as_a_card_with_progress_and_stop()
	{
		(Window window, RemasterWorkspaceViewModel model, HangingLauncher launcher, string project) = ShowProjectView(Ready);
		Assert.False(window.FindNamed<Border>("RemasterFeasibilityBanner").IsOnScreen());
		Assert.True(window.FindNamed<Button>("RemasterBuildButton").IsOnScreen());

		Click(window.FindNamed<Button>("RemasterPrepareButton"));

		Assert.Equal(new[] { "/usr/bin/env", "python3", Path.Combine("/tools", "mep_project.py"), "kit", project, "--rom" }, launcher.Argv.Take(6).ToArray());
		Assert.True(window.FindNamed<StackPanel>("RemasterJobCard").IsOnScreen());
		//The card replaces the Build button; the record button waits (W-R3).
		Assert.False(window.FindNamed<Button>("RemasterBuildButton").IsOnScreen());
		Assert.False(window.FindNamed<Button>("RemasterRecordButton").IsEffectivelyEnabled);
		Assert.Equal("Preparing your figures…", window.FindNamed<TextBlock>("RemasterJobTitle").Text);

		launcher.Last!.OnLine("== artist_kit.py -> kit/rec-001", false);
		launcher.Last.OnLine("ok   artist_kit.py -> kit/rec-001", false);
		launcher.Last.OnLine("== artist_bg_kit.py -> kit/rec-001", false);
		WaitFor(() => window.FindNamed<ProgressBar>("RemasterJobProgress").Value == 20, "the bar never moved to 1 of 5");
		Assert.Equal("step 2 of 5 · scenery", window.FindNamed<TextBlock>("RemasterJobDetail").Text);

		Click(window.FindNamed<Button>("RemasterJobStopButton"));
		WaitFor(() => !model.IsJobRunning, "Stop never ended the job");

		Assert.True(launcher.Last.Killed);
		Assert.StartsWith("Stopped.", window.FindNamed<TextBlock>("RemasterJobTitle").Text);
		Assert.True(window.FindNamed<Button>("RemasterRecordButton").IsEffectivelyEnabled);
	}

	[AvaloniaFact]
	public void A_failed_kit_shows_a_plain_failure_line()
	{
		(Window window, RemasterWorkspaceViewModel model, HangingLauncher launcher, _) = ShowProjectView(Ready);
		Click(window.FindNamed<Button>("RemasterPrepareButton"));
		launcher.Last!.OnLine("FAIL artist_chr_kit.py -> kit/pages", false);
		launcher.Last.OnExit(1);
		WaitFor(() => !model.IsJobRunning, "the failed job never ended");

		Assert.Equal("Preparing the figures failed.", window.FindNamed<TextBlock>("RemasterJobTitle").Text);
		Assert.Equal("FAIL artist_chr_kit.py -> kit/pages", window.FindNamed<TextBlock>("RemasterJobDetail").Text);
	}

	//The user's rule (2026-10-03): every wait moves. Record and Stop block in
	//the core (the builder exports every ROM tile, 0.4-1 s on real games), so
	//they run off the UI thread: right after the click the wait is on screen,
	//the button is off (no double start), and the UI thread keeps running
	//jobs while the core works; the recording view comes when it answers.
	[AvaloniaFact]
	public void Record_and_stop_run_off_the_ui_thread_behind_a_moving_wait()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		string folder = TempFolder();
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, BuildSyntheticNrom());
		try {
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
			EmuApi.Resume();
			model.SelectWorkspace(Workspace.Remaster);
			Dispatcher.UIThread.RunJobs();

			Button start = window.FindNamed<Button>("RemasterStartButton");
			//No RunJobs before the checks: the core's answer comes back as a
			//posted job, so whatever is seen here is what the click left.
			start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			Assert.False(model.Remaster.IsRecording, "Record blocked the UI thread until the core answered");
			Control wait = window.FindNamed<Control>("RemasterRecordWait");
			Assert.True(wait.IsOnScreen());
			Assert.True(wait.FindAll<ProgressBar>().Single().IsIndeterminate);
			Assert.Equal("Starting the recording…", window.FindNamed<TextBlock>("RemasterRecordWaitText").Text);
			Assert.False(start.IsEffectivelyEnabled, "a second click could start a second recording");

			WaitFor(() => model.Remaster.IsRecording, "the recording never started");
			Assert.True(EmuApi.IsMepBootstrapping());
			Assert.False(wait.IsOnScreen());
			Assert.True(window.FindNamed<Panel>("RemasterRecordingStripHost").IsOnScreen());

			Button stop = window.FindNamed<Button>("RemasterStopRecordingButton");
			stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			Assert.True(model.Remaster.IsRecording, "Stop blocked the UI thread until the core answered");
			Control stopWait = window.FindNamed<Control>("RemasterStopWait");
			Assert.True(stopWait.IsOnScreen());
			Assert.True(stopWait.FindAll<ProgressBar>().Single().IsIndeterminate);
			Assert.False(stop.IsEffectivelyEnabled);

			WaitFor(() => !model.Remaster.IsRecording, "the recording never stopped");
			Assert.False(EmuApi.IsMepBootstrapping());
			Assert.True(File.Exists(Path.Combine(folder, "synthetic-nrom", "project.json")));
			WaitFor(() => !model.Remaster.IsJobRunning, "the kit job never finished", 180000);
		} finally {
			if(EmuApi.IsMepBootstrapping()) {
				EmuApi.StopMepRecording();
			}
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//A step reports nothing until it ends: the card's bar moves meanwhile.
	[AvaloniaFact]
	public void The_kit_cards_bar_moves_within_a_step()
	{
		(Window window, _, _, _) = ShowProjectView(Ready);
		Click(window.FindNamed<Button>("RemasterPrepareButton"));
		ProgressBar bar = window.FindNamed<ProgressBar>("RemasterJobProgress");
		Assert.True(bar.IsOnScreen());
		Assert.True(bar.IsIndeterminate, "the bar sat still at 0 % through the first step");
	}

	//The Python/tools probe: until it answers, the jobs say why they wait and
	//a moving line says it is checking (a click used to do nothing).
	[AvaloniaFact]
	public void The_tools_probe_shows_a_moving_wait_and_the_jobs_say_why()
	{
		string root = TempFolder();
		string project = Path.Combine(root, "Contra (USA)");
		Directory.CreateDirectory(Path.Combine(project, "auto", "rec-001", "textures"));
		File.WriteAllText(Path.Combine(project, "auto", "rec-001", "textures", "hires.txt"), "<ver>107\n");
		using ManualResetEventSlim gate = new();
		RemasterWorkspaceViewModel model = new(new RemasterConfig(), _ => {
			gate.Wait(10000);
			return Ready;
		}, new HangingLauncher(), hasHeadlessRecorder: false);
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", Path.Combine(root, "Contra (USA).nes"), project, Path.Combine(root, "EnhancementPacks"));
		Window window = new() { Content = new RemasterWorkspaceView { DataContext = model }, Width = 1000, Height = 800 };
		window.Show();
		try {
			model.EnsureFeasibilityMeasured();
			Dispatcher.UIThread.RunJobs();

			Control checking = window.FindNamed<Control>("RemasterFeasibilityChecking");
			Assert.True(checking.IsOnScreen());
			Assert.True(checking.FindAll<ProgressBar>().Single().IsIndeterminate);
			Assert.False(window.FindNamed<Button>("RemasterPrepareButton").IsEffectivelyEnabled);
			Assert.Equal("Checking for Python and MesenAI's tools…", window.FindNamed<TextBlock>("RemasterPrepareReason").Text);
		} finally {
			gate.Set();
		}
		WaitFor(() => model.Feasibility != null, "the gate was never measured");
		Assert.False(window.FindNamed<Control>("RemasterFeasibilityChecking").IsOnScreen());
		Assert.True(window.FindNamed<Button>("RemasterPrepareButton").IsEffectivelyEnabled);
	}

	//scripts/gen_synthetic_nrom.py, byte for byte (as WorkspaceShellTests).
	private static byte[] BuildSyntheticNrom()
	{
		const int prgSize = 32 * 1024;
		const int chrSize = 8 * 1024;
		byte[] rom = new byte[16 + prgSize + chrSize];
		rom[0] = (byte)'N';
		rom[1] = (byte)'E';
		rom[2] = (byte)'S';
		rom[3] = 0x1A;
		rom[4] = prgSize / 16384;
		rom[5] = chrSize / 8192;

		int prg = 16;
		for(int i = 0; i < prgSize; i++) {
			rom[prg + i] = 0xEA;
		}
		rom[prg + 0x0000] = 0x4C;
		rom[prg + 0x0001] = 0x00;
		rom[prg + 0x0002] = 0x80;
		rom[prg + 0x0003] = 0x40;
		rom[prg + 0x7FFA] = 0x03;
		rom[prg + 0x7FFB] = 0x80;
		rom[prg + 0x7FFC] = 0x00;
		rom[prg + 0x7FFD] = 0x80;
		rom[prg + 0x7FFE] = 0x03;
		rom[prg + 0x7FFF] = 0x80;
		return rom;
	}
}
