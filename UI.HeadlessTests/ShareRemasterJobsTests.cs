using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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

//#647-#650: the wiring between Remaster's and Share's jobs. The rules are
//pinned host-free in UI.Tests/Remaster/WorkspaceJobsTests.cs; this checks
//that the two workspaces' views follow them - each gate shows the other's
//job on the same project, a result lands only on its own project, a build
//never restarts a movie by itself, quitting asks about Share's job, and a
//cancelled import leaves no half-written folder.
[Collection(NativeCoreCollection.Name)]
public class ShareRemasterJobsTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _noticeShown = ConfigManager.Config.Preferences.ClassicMenuNoticeShown;
	private readonly List<string> _folders = new();

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ClassicMenuNoticeShown = _noticeShown;
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
		string folder = Path.Combine(Path.GetTempPath(), "mesen-jobs-" + Guid.NewGuid().ToString("N"));
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
			public bool Killed;
			//False: like a real child, the exit arrives after Kill returns.
			public bool ExitOnKill = true;

			public void Kill()
			{
				Killed = true;
				if(ExitOnKill) {
					OnExit(-9);
				}
			}
		}

		public Job? Last;
		public bool ExitOnKill = true;
		public IReadOnlyList<string> Argv = Array.Empty<string>();

		public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
		{
			Argv = argv;
			Last = new Job { OnLine = onLine, OnExit = onExit, ExitOnKill = ExitOnKill };
			return Last;
		}
	}

	private static RemasterFeasibility Ready(string tools = "/tools") => new(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, tools);

	//A project with one recording, its kit and a built mep/: Remaster can build
	//it and Share can package it.
	private string Project(string root, string name)
	{
		string project = Path.Combine(root, name);
		Directory.CreateDirectory(Path.Combine(project, "auto", "rec-001", "textures"));
		File.WriteAllText(Path.Combine(project, "auto", "rec-001", "textures", "hires.txt"), "<ver>107\n");
		File.WriteAllText(Path.Combine(project, ".bootstrap"), "generator=mesence-bootstrap/1\nsha1=00\nrom=" + name + "\n");
		string kit = Path.Combine(project, "kit", "rec-001");
		Directory.CreateDirectory(Path.Combine(kit, "figures"));
		File.WriteAllText(Path.Combine(kit, "figures", "usr001-figure.png"), "x");
		File.WriteAllText(Path.Combine(kit, "kit.json"), "{\"parts\":[{\"files\":[{\"path\":\"sheets/usr001.png\",\"caption\":\"run\",\"figure\":\"figures/usr001-figure.png\"}]}]}");
		Directory.CreateDirectory(Path.Combine(project, "mep", "textures"));
		File.WriteAllText(Path.Combine(project, "mep", "textures", "hires.txt"), "<ver>107\n");
		return project;
	}

	private sealed record RemasterHarness(Window Window, RemasterWorkspaceViewModel Model, FakeLauncher Launcher, List<RemasterShowAction> Shown);

	private static RemasterHarness ShowRemaster(string root, string project, RemasterFeasibility? feasibility = null)
	{
		FakeLauncher launcher = new();
		List<RemasterShowAction> shown = new();
		RemasterWorkspaceViewModel model = new(new RemasterConfig(), _ => feasibility ?? Ready(), launcher, hasHeadlessRecorder: false);
		model.ShowInGame = action => {
			shown.Add(action);
			return true;
		};
		model.PlanPackChange = () => PackChangePolicy.Plan(ConsoleType.Nes, false, false);
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", Path.Combine(root, "Contra (USA).nes"), project, Path.Combine(root, "EnhancementPacks"));
		model.EnsureFeasibilityMeasured();
		WaitFor(() => model.Feasibility != null, "the gate was never measured");
		Window window = new() { Content = new RemasterWorkspaceView { DataContext = model }, Width = 1000, Height = 900 };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return new RemasterHarness(window, model, launcher, shown);
	}

	private sealed record ShareHarness(Window Window, ShareWorkspaceViewModel Model, FakeLauncher Launcher);

	private static ShareHarness ShowShare(string gameProject)
	{
		FakeLauncher launcher = new();
		ShareWorkspaceViewModel model = new(Array.Empty<CommunityPackHostEntry>(), launcher, () => Ready(), () => Array.Empty<string>(),
			new NullRecorder(), _ => { }, _ => { }, () => (false, false));
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", gameProject);
		Window window = new() { Content = new ShareWorkspaceView { DataContext = model }, Width = 1000, Height = 800 };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return new ShareHarness(window, model, launcher);
	}

	private sealed class NullRecorder : IReplayRecorder
	{
		public bool IsSharing => false;
		public string? Start() => null;
		public string? StopAndKeep() => null;
		public string IssueUrl() => "";
	}

	//#647: Remaster's build runs on the project: Share's Build Pack .zip waits,
	//with its reason, and comes back when the build ends.
	[AvaloniaFact]
	public void Share_build_waits_with_its_reason_while_remaster_builds_the_same_project()
	{
		string root = TempFolder();
		string project = Project(root, "Contra (USA)");
		RemasterHarness r = ShowRemaster(root, project);
		ShareHarness s = ShowShare(project);
		WorkspaceJobs.Link(r.Model, s.Model);
		Assert.True(s.Model.OpenProject(project));
		Dispatcher.UIThread.RunJobs();
		Button zip = s.Window.FindNamed<Button>("ShareBuildZipButton");
		Assert.True(zip.IsEffectivelyEnabled);

		Click(r.Window.FindNamed<Button>("RemasterBuildButton"));
		WaitFor(() => r.Model.IsJobRunning, "the build never started");

		Assert.False(zip.IsEffectivelyEnabled, "Share could package mep/ while Remaster builds it");
		TextBlock reason = s.Window.FindNamed<TextBlock>("ShareBuildReason");
		Assert.True(reason.IsOnScreen());
		Assert.Equal("Remaster is working on this project. Wait for it to finish.", reason.Text);
		Assert.False(s.Model.BuildZip());
		Assert.Null(s.Launcher.Last);

		r.Launcher.Last!.OnLine("show: images", false);
		r.Launcher.Last.OnExit(0);
		WaitFor(() => !r.Model.IsJobRunning, "the build never ended");
		Assert.True(zip.IsEffectivelyEnabled, "Share's Build stayed held after the build ended");
		Assert.False(reason.IsOnScreen());
	}

	//#647, the other way: Share packages the project, so Remaster's Build and
	//Prepare Figures wait with their reason; Record is untouched.
	[AvaloniaFact]
	public void Remaster_jobs_wait_with_their_reason_while_share_packages_the_same_project()
	{
		string root = TempFolder();
		string project = Project(root, "Contra (USA)");
		RemasterHarness r = ShowRemaster(root, project);
		ShareHarness s = ShowShare(project);
		WorkspaceJobs.Link(r.Model, s.Model);
		Assert.True(s.Model.OpenProject(project));
		Button build = r.Window.FindNamed<Button>("RemasterBuildButton");
		Assert.True(build.IsEffectivelyEnabled);

		Click(s.Window.FindNamed<Button>("ShareBuildZipButton"));
		WaitFor(() => s.Model.IsBuildRunning, "the pack job never started");

		Assert.False(build.IsEffectivelyEnabled, "Remaster could build mep/ while Share packages it");
		Assert.Equal("Share is packaging this project. Wait for it to finish.", r.Window.FindNamed<TextBlock>("RemasterBuildReason").Text);
		Assert.True(r.Window.FindNamed<TextBlock>("RemasterBuildReason").IsOnScreen());
		Assert.False(r.Window.FindNamed<Button>("RemasterPrepareButton").IsEffectivelyEnabled);
		Assert.Equal("Share is packaging this project. Wait for it to finish.", r.Window.FindNamed<TextBlock>("RemasterPrepareReason").Text);
		Assert.False(r.Model.StartBuild());
		Assert.False(r.Model.StartKit());
		Assert.Null(r.Launcher.Last);

		s.Launcher.Last!.OnExit(1);
		WaitFor(() => !s.Model.IsBuildRunning, "the pack job never ended");
		Assert.True(build.IsEffectivelyEnabled, "Remaster's Build stayed held after the pack job ended");
	}

	//#648: project A's pack job ends after the user opened project B. Its
	//failure, and its success, belong to A: B shows neither, even with an
	//old zip of its own on disk; A shows its fresh zip when reopened.
	[AvaloniaFact]
	public void A_pack_result_lands_only_on_the_project_it_ran_on()
	{
		string root = TempFolder();
		string a = Project(root, "Contra (USA)");
		string b = Project(root, "Castlevania (USA)");
		File.WriteAllBytes(Path.Combine(b, "castlevania-usa-mep.zip"), new byte[2048]);
		ShareHarness s = ShowShare(a);
		TextBlock result = s.Window.FindNamed<TextBlock>("ShareBuildResult");

		Assert.True(s.Model.OpenProject(a));
		Click(s.Window.FindNamed<Button>("ShareBuildZipButton"));
		Assert.True(s.Model.OpenProject(b));
		Dispatcher.UIThread.RunJobs();
		s.Launcher.Last!.OnLine("ERROR textures/hires.txt: <img> chr/0.png does not resolve", true);
		s.Launcher.Last.OnExit(1);
		WaitFor(() => !s.Model.IsBuildRunning, "A's job never ended");
		Assert.Equal("", result.Text);
		Assert.False(s.Window.FindNamed<TextBlock>("ShareBuildFailure").IsOnScreen(), "A's failure shows on B");

		Assert.True(s.Model.OpenProject(a));
		Click(s.Window.FindNamed<Button>("ShareBuildZipButton"));
		Assert.True(s.Model.OpenProject(b));
		Dispatcher.UIThread.RunJobs();
		File.WriteAllBytes(Path.Combine(a, "contra-usa-mep.zip"), new byte[4096]);
		s.Launcher.Last!.OnExit(0);
		WaitFor(() => !s.Model.IsBuildRunning, "A's second job never ended");
		Assert.False(s.Model.IsZipReady, "B's old zip reads as freshly built");
		Assert.False(s.Window.FindNamed<Button>("ShareShowZipButton").IsOnScreen());
		Assert.Equal("", result.Text);

		Assert.True(s.Model.OpenProject(a));
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("✔ contra-usa-mep.zip · 4 KB · no problems", result.Text);
	}

	//#649: a build whose manifest changed asks for a pack reload. During a
	//movie, a shared-replay recording or netplay, ADR-0244 says that reload is
	//a restart - so the build is not shown and plays next time; in place, it is.
	[AvaloniaFact]
	public void A_finished_build_never_restarts_the_game_during_a_movie_or_netplay()
	{
		string root = TempFolder();
		string project = Project(root, "Contra (USA)");
		RemasterHarness r = ShowRemaster(root, project);
		PackChangePlan plan = PackChangePolicy.Plan(ConsoleType.Nes, movieActive: true, netplayActive: false);
		r.Model.PlanPackChange = () => plan;

		Click(r.Window.FindNamed<Button>("RemasterBuildButton"));
		r.Launcher.Last!.OnLine("show: reload", false);
		r.Launcher.Last.OnExit(0);
		WaitFor(() => !r.Model.IsJobRunning, "the build never ended");
		Assert.Empty(r.Shown);
		Assert.False(r.Model.IsShowingBuild);
		Assert.Equal("Built · no problems · it plays the next time you open Contra (USA)", r.Window.FindNamed<TextBlock>("RemasterJobTitle").Text);

		//Images reload in place even then; when that fails, no pack restart.
		r.Model.ShowInGame = action => {
			r.Shown.Add(action);
			return false;
		};
		plan = PackChangePolicy.Plan(ConsoleType.Nes, movieActive: false, netplayActive: true);
		r.Model.DismissJobResult();
		Click(r.Window.FindNamed<Button>("RemasterBuildButton"));
		r.Launcher.Last!.OnLine("show: images", false);
		r.Launcher.Last.OnExit(0);
		WaitFor(() => !r.Model.IsJobRunning, "the second build never ended");
		Assert.Equal(new[] { RemasterShowAction.ReloadImages }, r.Shown);

		//Control: in place, the pack reload runs.
		r.Shown.Clear();
		r.Model.ShowInGame = action => {
			r.Shown.Add(action);
			return true;
		};
		plan = PackChangePolicy.Plan(ConsoleType.Nes, false, false);
		r.Model.DismissJobResult();
		Click(r.Window.FindNamed<Button>("RemasterBuildButton"));
		r.Launcher.Last!.OnLine("show: reload", false);
		r.Launcher.Last.OnExit(0);
		WaitFor(() => !r.Model.IsJobRunning, "the third build never ended");
		Assert.Equal(new[] { RemasterShowAction.ReloadPack }, r.Shown);
		Assert.True(r.Model.IsShowingBuild);
	}

	//#650: Stop on a running import removes the `<pack> (editable)` folder it
	//was writing; the pack itself is untouched. Both orders: the child gone
	//by the time Cancel returns, and the child exiting after the sheet closed.
	[AvaloniaTheory]
	[InlineData(true)]
	[InlineData(false)]
	public void Stopping_an_import_removes_the_folder_it_was_writing(bool exitOnKill)
	{
		string root = TempFolder();
		string tools = Path.Combine(root, "tools");
		Directory.CreateDirectory(tools);
		File.WriteAllText(Path.Combine(tools, RemasterHandOff.ImportScript), "");
		string pack = Path.Combine(root, "packs", "Contra80s");
		Directory.CreateDirectory(pack);
		File.WriteAllText(Path.Combine(pack, "hires.txt"), "<ver>107\n");
		RemasterHarness r = ShowRemaster(root, Project(root, "Contra (USA)"), Ready(tools));
		r.Launcher.ExitOnKill = exitOnKill;

		Assert.False(r.Model.OpenProjectFolder(pack));
		Dispatcher.UIThread.RunJobs();
		Click(r.Window.FindNamed<Button>("RemasterMakeEditableButton"));
		string destination = Path.Combine(root, "packs", "Contra80s (editable)");
		Assert.Contains(destination, r.Launcher.Argv);
		//mep_import has started writing the project.
		Directory.CreateDirectory(Path.Combine(destination, "auto"));
		File.WriteAllText(Path.Combine(destination, "project.json"), "{");

		Click(r.Window.FindNamed<Button>("RemasterImportStopButton"));
		Assert.True(r.Launcher.Last!.Killed);
		if(!exitOnKill) {
			Assert.True(Directory.Exists(destination), "removed while the child could still be writing");
			r.Launcher.Last.OnExit(-9);
		}
		WaitFor(() => !r.Model.Job.IsRunning, "the import never stopped");
		Assert.False(Directory.Exists(destination), "the half-written import folder was left behind");
		Assert.True(File.Exists(Path.Combine(pack, "hires.txt")), "the pack itself was touched");
	}

	//#650: quitting while Share packages asks once, inline, and Quit stops the
	//pack job before the app goes - against the real MainWindow wiring.
	[AvaloniaFact]
	public void Quitting_while_share_packages_asks_and_stops_the_job()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Preferences.Workspace = Workspace.Share;
		ConfigManager.Config.Preferences.ClassicMenuNoticeShown = true;
		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");

		//Share's runner, with a fake child in place of mep_build.py.
		RemasterJobRunner runner = (RemasterJobRunner)typeof(ShareWorkspaceViewModel).GetField("_jobs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model.Share)!;
		FakeLauncher launcher = new();
		typeof(RemasterJobRunner).GetField("_launcher", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runner, launcher);
		string project = Project(TempFolder(), "Contra (USA)");
		Assert.True(runner.Start(new RemasterJobSpec(RemasterJobKind.Pack, new[] { "python3" }, "/tools", 1, project, "Contra (USA)")));
		Assert.True(model.Share.Job.IsRunning);

		int quits = 0;
		Assert.False(model.ConfirmQuit(() => quits++), "quit went ahead with Share's job running");
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<Panel>("InterruptionBarHost").IsOnScreen());
		Assert.Equal("⚠ Your pack is being packaged. Quit anyway? It stops, and Build Pack .zip runs it again.", window.FindNamed<TextBlock>("InterruptionText").Text);
		Assert.False(launcher.Last!.Killed);

		Click(window.FindNamed<Button>("InterruptionGoButton"));
		Assert.Equal(1, quits);
		Assert.True(launcher.Last.Killed, "Share's job outlived the quit");
		Assert.False(model.Share.Job.IsRunning);
	}
}
