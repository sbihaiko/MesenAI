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
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#953: every hand-off that leaves the app through an injected seam - Show in
//Finder (reveal) and the browser (openBrowser) - passes the path or URL its
//button names, and only on the user's click. ShareWorkspaceView is the surface
//whose host actions are injected (MainWindowViewModel.InitShare passes
//ShareRecordingSession.Reveal and ApplicationHelper.OpenBrowser); here they are
//stubs that record what they were handed, so nothing launches. Core-free: no
//MainWindow, no EmuApi, a fake recorder and job launcher - runs on Linux CI.
//ShareWorkspaceTests covers the same screens with the real core; this class is
//the hand-off contract on its own. The URL builders are pinned host-free in
//UI.Tests/Share.
public class PlayHandoffTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-953-" + Guid.NewGuid().ToString("N"));
	private readonly List<string> _opened = new();
	private readonly List<string> _revealed = new();
	private readonly List<string> _known = new();
	private readonly FakeLauncher _launcher = new();
	private readonly FakeRecorder _recorder = new();
	private Window? _window;

	public PlayHandoffTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		_window?.Close();
		try {
			Directory.Delete(_folder, true);
		} catch {
			//The temp folder going is not what the case was proving.
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

	private sealed class FakeRecorder : IReplayRecorder
	{
		public string File = "";
		public bool IsSharing { get; private set; }

		public string? Start()
		{
			IsSharing = true;
			return File;
		}

		public string? StopAndKeep()
		{
			IsSharing = false;
			return File;
		}

		public string IssueUrl() => ReplayShare.BuildIssueUrl("");
	}

	private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, "/tools");

	private (Window Window, ShareWorkspaceViewModel Model) ShowShare()
	{
		using Stream stream = typeof(ShareWorkspaceViewModel).Assembly.GetManifestResourceStream("Mesen.pack_host_allowlist.json")
			?? throw new InvalidOperationException("the allow-list is not embedded");
		using StreamReader reader = new(stream);
		IReadOnlyList<CommunityPackHostEntry> hosts = CommunityPackHostAllowlist.Parse(reader.ReadToEnd());
		ShareWorkspaceViewModel model = new(hosts, _launcher, () => Ready, () => _known, _recorder, _opened.Add, _revealed.Add, () => (false, false));
		_window = new Window { Content = new ShareWorkspaceView { DataContext = model }, Width = 1000, Height = 800 };
		_window.Show();
		Dispatcher.UIThread.RunJobs();
		return (_window, model);
	}

	private static void Click(Button button)
	{
		Assert.True(button.IsOnScreen(), $"{button.Name} is not on screen");
		Assert.True(button.IsEffectivelyEnabled, $"{button.Name} is disabled");
		button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	private static void Type(TextBox box, string text)
	{
		box.Text = text;
		Dispatcher.UIThread.RunJobs();
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 10000) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	private static Dictionary<string, string> Query(string url)
	{
		return new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
	}

	//W-H2 › Continue on GitHub ↗.
	[AvaloniaFact]
	public void Continue_on_GitHub_for_a_pack_hands_the_prefilled_form_url_to_the_browser()
	{
		(Window window, ShareWorkspaceViewModel model) = ShowShare();
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", "");
		Click(window.FindNamed<Button>("ShareAPackButton"));
		const string link = "https://github.com/someone/contra-80s/releases/download/v1.2/contra.zip";
		Type(window.FindNamed<TextBox>("SharePackLink"), link);
		Assert.Empty(_opened);

		Click(window.FindNamed<Button>("SharePackContinueButton"));

		string url = Assert.Single(_opened);
		Assert.StartsWith("https://github.com/sbihaiko/MesenAI/issues/new?template=community-pack.yml", url);
		Dictionary<string, string> q = Query(url);
		Assert.Equal(link, q["pack_link"]);
		Assert.Equal("Contra (USA)", q["rom_target"]);
		Assert.Equal("NES", q["console"]);
		Assert.Empty(_revealed);
	}

	//W-H3: Show in Finder reveals the built zip, Google Drive ↗ opens Drive, and
	//Continue on GitHub ↗ opens the form with the project's link.
	[AvaloniaFact]
	public void A_packaged_project_reveals_its_zip_and_opens_drive_and_the_form()
	{
		string project = Path.Combine(_folder, "Contra (USA)");
		Directory.CreateDirectory(Path.Combine(project, "mep", "textures"));
		File.WriteAllText(Path.Combine(project, "mep", "textures", "hires.txt"), "<ver>107\n");
		File.WriteAllText(Path.Combine(project, ".bootstrap"), "generator=mesence-bootstrap/1\nsha1=00\nrom=Contra (USA)\n");
		_known.Add(project);
		(Window window, ShareWorkspaceViewModel model) = ShowShare();
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", project);
		Assert.True(model.OpenProject(project));
		Dispatcher.UIThread.RunJobs();

		Click(window.FindNamed<Button>("ShareBuildZipButton"));
		string zip = Path.Combine(project, "contra-usa-mep.zip");
		File.WriteAllBytes(zip, new byte[4096]);
		_launcher.Last!.OnExit(0);
		WaitFor(() => !model.IsBuildRunning, "the job never ended");
		Assert.Empty(_revealed);

		Click(window.FindNamed<Button>("ShareShowZipButton"));
		Assert.Equal(new[] { zip }, _revealed);

		Click(window.FindNamed<Button>("ShareOpenDriveButton"));
		Assert.Equal(new[] { "https://drive.google.com/drive/my-drive" }, _opened);

		const string link = "https://drive.google.com/file/d/abc/view?usp=sharing";
		Type(window.FindNamed<TextBox>("ShareProjectLink"), link);
		Click(window.FindNamed<Button>("ShareProjectContinueButton"));
		Assert.Equal(2, _opened.Count);
		Dictionary<string, string> q = Query(_opened[1]);
		Assert.Equal("community-pack.yml", q["template"]);
		Assert.Equal(link, q["pack_link"]);
		Assert.Equal("Contra (USA)", q["rom_target"]);
	}

	//W-H4 "Replay saved": Show in Finder reveals the .mmo, Continue on GitHub ↗
	//opens the replay form - each on its own click, nothing on its own.
	[AvaloniaFact]
	public void A_saved_replay_reveals_its_file_and_continues_on_github()
	{
		_recorder.File = Path.Combine(_folder, "Contra (USA) 2026-10-06 10.00.00.mmo");
		File.WriteAllBytes(_recorder.File, new byte[] { 0x50, 0x4B });
		(Window window, ShareWorkspaceViewModel model) = ShowShare();
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", "");
		Click(window.FindNamed<Button>("RecordAndShareButton"));
		Click(window.FindNamed<Button>("ShareReplayStartButton"));
		WaitFor(() => model.IsRecording, "the replay never started");
		Assert.True(model.HandleEsc());
		WaitFor(() => window.FindNamed<Panel>("ShareReplaySavedSheet").IsOnScreen(), "the saved sheet never showed");
		Assert.Empty(_opened);
		Assert.Empty(_revealed);

		Click(window.FindNamed<Button>("ShareReplayShowFileButton"));
		Assert.Equal(new[] { _recorder.File }, _revealed);
		Assert.Empty(_opened);

		Click(window.FindNamed<Button>("ShareReplayContinueButton"));
		Assert.StartsWith("https://github.com/sbihaiko/MesenAI/issues/new?template=replay.yml", Assert.Single(_opened));
		Assert.Single(_revealed);
	}
}
