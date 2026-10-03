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

//G.8 (PRD Part B §8, ADR-0241, §13.4, §13.5.4 W-H1-W-H4): the Share workspace's
//XAML wiring. The rules (the form's three fields, the host check, packaging,
//the replay gate, Esc) are pinned host-free in UI.Tests/Share; this checks the
//crossing into ShareWorkspaceView and MainWindow.axaml with fakes for the
//browser, the reveal, the job and the recorder - and, with the real core and a
//synthetic NROM, that Record and Share records a replay inside Share and keeps
//it on Stop.
[Collection(NativeCoreCollection.Name)]
public class ShareWorkspaceTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _showClassicMenuBar = ConfigManager.Config.Preferences.ShowClassicMenuBar;
	private readonly bool _noticeShown = ConfigManager.Config.Preferences.ClassicMenuNoticeShown;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly bool _bootstrap = ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
	private readonly List<string> _paths = new();

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
		if(NativeCore.IsAvailable) {
			ConfigManager.Config.EnhancementPacks.ApplyConfig();
		}
		ConfigManager.Config.Save();
		foreach(string path in _paths) {
			try {
				if(Directory.Exists(path)) {
					Directory.Delete(path, true);
				} else if(File.Exists(path)) {
					File.Delete(path);
				}
			} catch(IOException) {
			} catch(UnauthorizedAccessException) {
			}
		}
	}

	private string TempFolder()
	{
		string folder = Path.Combine(Path.GetTempPath(), "mesen-g8-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_paths.Add(folder);
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

	private static void Type(TextBox box, string text)
	{
		box.Text = text;
		Dispatcher.UIThread.RunJobs();
	}

	private sealed class FakeLauncher : IJobProcessLauncher
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

	private sealed class FakeRecorder : IReplayRecorder
	{
		public string File = "";
		public bool IsSharing { get; set; }
		public int Starts;

		public string? Start()
		{
			Starts++;
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

	private sealed record Harness(Window Window, ShareWorkspaceViewModel Model, FakeLauncher Launcher, FakeRecorder Recorder, List<string> Opened, List<string> Revealed, List<string> Known);

	private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, "/tools");

	//The app's embedded copy of scripts/pack_host_allowlist.json, the one
	//MainWindowViewModel.InitShare hands to Share (ADR-0138 §41).
	private static IReadOnlyList<CommunityPackHostEntry> EmbeddedAllowlist()
	{
		using Stream stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Mesen.pack_host_allowlist.json")
			?? throw new InvalidOperationException("the allow-list is not embedded");
		using StreamReader reader = new(stream);
		return CommunityPackHostAllowlist.Parse(reader.ReadToEnd());
	}

	private Harness ShowShare(RemasterFeasibility? feasibility = null)
	{
		IReadOnlyList<CommunityPackHostEntry> hosts = EmbeddedAllowlist();
		Assert.NotEmpty(hosts);
		FakeLauncher launcher = new();
		FakeRecorder recorder = new();
		List<string> opened = new();
		List<string> revealed = new();
		List<string> known = new();
		ShareWorkspaceViewModel model = new(hosts, launcher, () => feasibility ?? Ready, () => known, recorder, opened.Add, revealed.Add, () => (false, false));
		Window window = new() { Content = new ShareWorkspaceView { DataContext = model }, Width = 1000, Height = 800 };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return new Harness(window, model, launcher, recorder, opened, revealed, known);
	}

	private string Project(string name, bool built)
	{
		string project = Path.Combine(TempFolder(), name);
		Directory.CreateDirectory(Path.Combine(project, "auto", "rec-001", "textures"));
		File.WriteAllText(Path.Combine(project, "auto", "rec-001", "textures", "hires.txt"), "<ver>107\n");
		File.WriteAllText(Path.Combine(project, ".bootstrap"), "generator=mesence-bootstrap/1\nsha1=00\nrom=" + name + "\n");
		if(built) {
			Directory.CreateDirectory(Path.Combine(project, "mep", "textures"));
			File.WriteAllText(Path.Combine(project, "mep", "textures", "hires.txt"), "<ver>107\n");
		}
		return project;
	}

	private static Dictionary<string, string> Query(string url)
	{
		return new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
	}

	[AvaloniaFact]
	public void W_H1_is_two_cards_and_the_trust_sentence()
	{
		Harness h = ShowShare();
		Assert.True(h.Window.FindNamed<StackPanel>("ShareHome").IsOnScreen());
		Assert.True(h.Window.FindNamed<Button>("ShareAPackButton").IsOnScreen());
		Assert.True(h.Window.FindNamed<Button>("RecordAndShareButton").IsOnScreen());
		Assert.Contains("never uploads anything or signs in for you", h.Window.FindNamed<TextBlock>("ShareTrustLine").Text);
		Assert.False(h.Window.FindNamed<StackPanel>("SharePackPage").IsOnScreen());
		Assert.False(h.Window.FindNamed<Panel>("ShareReplayStartSheet").IsOnScreen());
	}

	//W-H2: the three fields, prefilled from the running game; a host the CI
	//would refuse is flagged inline and blocks Continue; Continue opens the
	//pre-filled form and nothing else (no upload, no credential).
	[AvaloniaFact]
	public void W_H2_prefills_the_game_flags_a_foreign_host_and_opens_the_prefilled_form()
	{
		Harness h = ShowShare();
		h.Model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", "");
		Click(h.Window.FindNamed<Button>("ShareAPackButton"));

		Assert.True(h.Window.FindNamed<StackPanel>("SharePackPage").IsOnScreen());
		Assert.False(h.Window.FindNamed<StackPanel>("ShareHome").IsOnScreen());
		Assert.Equal("Contra (USA)", h.Window.FindNamed<TextBox>("SharePackGame").Text);
		Assert.Equal("NES", h.Window.FindNamed<ComboBox>("SharePackConsole").SelectedItem);
		Button go = h.Window.FindNamed<Button>("SharePackContinueButton");
		Assert.False(go.IsEffectivelyEnabled);
		Assert.Equal("Paste the pack's download link first.", h.Window.FindNamed<TextBlock>("SharePackContinueReason").Text);

		Type(h.Window.FindNamed<TextBox>("SharePackLink"), "https://example.com/contra.zip");
		Assert.True(h.Window.FindNamed<TextBlock>("SharePackHostError").IsOnScreen());
		Assert.StartsWith("⚠ This host is not accepted.", h.Window.FindNamed<TextBlock>("SharePackHostError").Text);
		Assert.False(go.IsEffectivelyEnabled);

		const string link = "https://github.com/someone/contra-80s/releases/download/v1.2/contra.zip";
		Type(h.Window.FindNamed<TextBox>("SharePackLink"), link);
		Assert.False(h.Window.FindNamed<TextBlock>("SharePackHostError").IsOnScreen());
		Click(go);

		string url = Assert.Single(h.Opened);
		Assert.StartsWith("https://github.com/sbihaiko/MesenAI/issues/new?template=community-pack.yml", url);
		Dictionary<string, string> q = Query(url);
		Assert.Equal(link, q["pack_link"]);
		Assert.Equal("Contra (USA)", q["rom_target"]);
		Assert.Equal("NES", q["console"]);

		//A game change never closes the screen nor overwrites what was typed (rule 5).
		h.Model.UpdateGame(true, ConsoleType.Gameboy, "Tetris (World)", "/roms/Tetris (World).gb", "");
		Dispatcher.UIThread.RunJobs();
		Assert.True(h.Window.FindNamed<StackPanel>("SharePackPage").IsOnScreen());
		Assert.Equal("Contra (USA)", h.Window.FindNamed<TextBox>("SharePackGame").Text);

		Click(h.Window.FindNamed<Button>("SharePackBackButton"));
		Assert.True(h.Window.FindNamed<StackPanel>("ShareHome").IsOnScreen());
	}

	//#666: a .gbc game inside a .zip runs on the Game Boy core; the console is
	//told by the archive's inner file name, not the archive's, while
	//mep_build.py pack's --rom keeps the file the core opened.
	[AvaloniaFact]
	public void W_H2_prefills_GBC_for_a_zipped_gbc_game()
	{
		Harness h = ShowShare();
		ResourcePath rom = new() { Path = "/roms/Zelda DX.zip", InnerFile = "Zelda DX (USA).gbc" };
		h.Model.UpdateGame(true, ConsoleType.Gameboy, "Zelda DX (USA)", rom, "");
		Click(h.Window.FindNamed<Button>("ShareAPackButton"));

		Assert.Equal("GBC", h.Window.FindNamed<ComboBox>("SharePackConsole").SelectedItem);

		//W-H3: the running game's project, with no pack.json target yet.
		string project = Project("Zelda DX (USA)", built: true);
		h.Known.Add(project);
		h.Model.UpdateGame(true, ConsoleType.Gameboy, "Zelda DX (USA)", rom, project);
		h.Model.OpenProject(project);
		Dispatcher.UIThread.RunJobs();
		Click(h.Window.FindNamed<Button>("ShareBuildZipButton"));
		Assert.Equal("/roms/Zelda DX.zip", h.Launcher.Argv[h.Launcher.Argv.ToList().IndexOf("--rom") + 1]);
		Type(h.Window.FindNamed<TextBox>("ShareProjectLink"), "https://drive.google.com/file/d/abc/view?usp=sharing");
		Click(h.Window.FindNamed<Button>("ShareProjectContinueButton"));
		Assert.Equal("GBC", Query(h.Opened.Last())["console"]);
	}

	//W-H2 › Package a Project… lists Remaster's projects inside Share and opens
	//W-H3; step 1 runs mep_build.py pack as a job; step 2 opens Drive; step 3
	//sends Game/Console taken from the project.
	[AvaloniaFact]
	public void Package_a_project_opens_W_H3_which_builds_the_zip_and_submits_the_project()
	{
		Harness h = ShowShare();
		string project = Project("Contra (USA)", built: true);
		h.Known.Add(project);
		h.Model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", project);
		Click(h.Window.FindNamed<Button>("ShareAPackButton"));
		Click(h.Window.FindNamed<Button>("PackageAProjectButton"));

		Assert.True(h.Window.FindNamed<Border>("ShareProjectList").IsOnScreen());
		Button row = h.Window.FindNamed<ItemsControl>("ShareProjectChoices").FindAll<Button>().Single();
		Assert.Equal("Contra (USA)", row.Content);
		Click(row);

		Assert.True(h.Window.FindNamed<StackPanel>("ShareProjectPage").IsOnScreen());
		Assert.False(h.Window.FindNamed<StackPanel>("SharePackPage").IsOnScreen());
		Assert.Equal("Share your Contra (USA) project", h.Window.FindNamed<TextBlock>("ShareProjectTitle").Text);

		Click(h.Window.FindNamed<Button>("ShareBuildZipButton"));
		Assert.Equal(new[] { "/usr/bin/env", "python3", Path.Combine("/tools", "mep_build.py"), "pack", Path.Combine(project, "mep"),
			"--out", Path.Combine(project, "contra-usa-mep.zip"), "--rom", "/roms/Contra (USA).nes", "--quiet" }, h.Launcher.Argv);
		WaitFor(() => h.Window.FindNamed<ProgressBar>("ShareBuildProgress").IsOnScreen(), "the job card never showed");
		Assert.False(h.Window.FindNamed<Button>("ShareBuildZipButton").IsOnScreen());

		File.WriteAllBytes(Path.Combine(project, "contra-usa-mep.zip"), new byte[4096]);
		File.WriteAllText(Path.Combine(project, "mep", "pack.json"), "{\"targets\": [{\"system\": \"nes\", \"sha1\": \"AB\"}]}");
		h.Launcher.Last!.OnLine("OK: contra-usa-mep.zip lints clean", false);
		h.Launcher.Last.OnExit(0);
		WaitFor(() => !h.Model.IsBuildRunning, "the job never ended");
		Assert.Equal("✔ contra-usa-mep.zip · 4 KB · no problems", h.Window.FindNamed<TextBlock>("ShareBuildResult").Text);
		Click(h.Window.FindNamed<Button>("ShareShowZipButton"));
		Assert.Equal(new[] { Path.Combine(project, "contra-usa-mep.zip") }, h.Revealed);

		Click(h.Window.FindNamed<Button>("ShareOpenDriveButton"));
		Assert.Equal(ShareProjectPackage.GoogleDriveUrl, h.Opened.Last());

		Button go = h.Window.FindNamed<Button>("ShareProjectContinueButton");
		Assert.False(go.IsEffectivelyEnabled);
		Type(h.Window.FindNamed<TextBox>("ShareProjectLink"), "https://drive.google.com/file/d/abc/view?usp=sharing");
		Click(go);
		Dictionary<string, string> q = Query(h.Opened.Last());
		Assert.Equal("community-pack.yml", q["template"]);
		Assert.Equal("https://drive.google.com/file/d/abc/view?usp=sharing", q["pack_link"]);
		Assert.Equal("Contra (USA)", q["rom_target"]);
		Assert.Equal("NES", q["console"]);
	}

	[AvaloniaFact]
	public void A_failed_package_shows_its_problem_and_an_unbuilt_project_says_why_build_waits()
	{
		Harness h = ShowShare();
		string built = Project("Contra (USA)", built: true);
		h.Model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", built);
		Assert.True(h.Model.OpenProject(built));
		Dispatcher.UIThread.RunJobs();
		Click(h.Window.FindNamed<Button>("ShareBuildZipButton"));
		h.Launcher.Last!.OnLine("ERROR textures/hires.txt: <img> chr/0.png does not resolve", true);
		h.Launcher.Last.OnExit(1);
		WaitFor(() => !h.Model.IsBuildRunning, "the failed job never ended");
		Assert.Equal("The pack has problems:", h.Window.FindNamed<TextBlock>("ShareBuildResult").Text);
		Assert.Contains("does not resolve", h.Window.FindNamed<TextBlock>("ShareBuildFailure").Text);
		Assert.False(h.Window.FindNamed<Button>("ShareShowZipButton").IsOnScreen());

		Assert.True(h.Model.OpenProject(Project("Castlevania (USA)", built: false)));
		Dispatcher.UIThread.RunJobs();
		Assert.False(h.Window.FindNamed<Button>("ShareBuildZipButton").IsEffectivelyEnabled);
		Assert.StartsWith("Nothing to package yet", h.Window.FindNamed<TextBlock>("ShareBuildReason").Text);
	}

	//W-H4 with a fake recorder: the first sheet, the recording (Esc stops), and
	//the second sheet whose two buttons reveal the file and open the R.1 form.
	[AvaloniaFact]
	public void W_H4_starts_from_its_sheet_stops_on_esc_and_hands_over_the_file_on_click()
	{
		Harness h = ShowShare();
		h.Recorder.File = Path.Combine(TempFolder(), "Contra (USA) 2026-10-02 10.00.00.mmo");
		File.WriteAllBytes(h.Recorder.File, new byte[] { 0x50, 0x4B });
		Click(h.Window.FindNamed<Button>("RecordAndShareButton"));
		Assert.True(h.Window.FindNamed<Panel>("ShareReplayStartSheet").IsOnScreen());
		//No game: Start is disabled with its reason (rule 4); Esc closes the sheet.
		Assert.False(h.Window.FindNamed<Button>("ShareReplayStartButton").IsEffectivelyEnabled);
		Assert.Equal("Open a game first.", h.Window.FindNamed<TextBlock>("ShareReplayStartReason").Text);
		Assert.True(h.Model.HandleEsc());
		Assert.False(h.Window.FindNamed<Panel>("ShareReplayStartSheet").IsOnScreen());

		h.Model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", "/roms/Contra (USA).nes", "");
		Click(h.Window.FindNamed<Button>("RecordAndShareButton"));
		Assert.Equal("Record and share — Contra (USA)", h.Window.FindNamed<TextBlock>("ShareReplaySheetTitle").Text);
		Click(h.Window.FindNamed<Button>("ShareReplayStartButton"));
		Assert.Equal(1, h.Recorder.Starts);
		Assert.True(h.Model.IsRecording);
		Assert.False(h.Window.FindNamed<Panel>("ShareReplayStartSheet").IsOnScreen());

		Assert.True(h.Model.HandleEsc());
		Assert.False(h.Model.IsRecording);
		Assert.True(h.Window.FindNamed<Panel>("ShareReplaySavedSheet").IsOnScreen());
		Assert.Equal(Path.GetFileName(h.Recorder.File), h.Window.FindNamed<TextBlock>("ShareReplayFileName").Text);
		//Nothing opened by itself: both are the user's clicks.
		Assert.Empty(h.Opened);
		Assert.Empty(h.Revealed);
		Click(h.Window.FindNamed<Button>("ShareReplayShowFileButton"));
		Assert.Equal(new[] { h.Recorder.File }, h.Revealed);
		Click(h.Window.FindNamed<Button>("ShareReplayContinueButton"));
		Assert.StartsWith("https://github.com/sbihaiko/MesenAI/issues/new?template=replay.yml", Assert.Single(h.Opened));
	}

	//The stop rule against the real core: Share replaces G.1's placeholder,
	//Record and Share records inside Share (the game view and the strip, nothing
	//of Play), switching profile keeps it recording, and Stop keeps the .mmo.
	[AvaloniaFact]
	public void Record_and_share_records_inside_share_survives_a_switch_and_keeps_the_file_on_stop()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ShowClassicMenuBar = false;
		prefs.ClassicMenuNoticeShown = true;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = false;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = false;
		ConfigManager.Config.EnhancementPacks.ApplyConfig();
		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");

		string folder = TempFolder();
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, BuildSyntheticNrom());
		try {
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
			EmuApi.Resume();

			model.SelectWorkspace(Workspace.Share);
			Dispatcher.UIThread.RunJobs();
			Assert.True(window.FindNamed<Panel>("ShareWorkspaceHost").IsOnScreen());
			Assert.True(window.FindNamed<StackPanel>("ShareHome").IsOnScreen());
			Assert.False(window.FindNamed<Panel>("PlayWorkspace").IsOnScreen());
			Assert.False(model.IsNativeRendererVisible);

			Click(window.FindNamed<Button>("RecordAndShareButton"));
			Assert.Equal("Record and share — synthetic-nrom", window.FindNamed<TextBlock>("ShareReplaySheetTitle").Text);
			Click(window.FindNamed<Button>("ShareReplayStartButton"));

			Assert.True(new CoreReplayRecorder().IsSharing, "the core is not recording a shared replay");
			Assert.True(model.IsGameViewVisible);
			Assert.True(window.FindNamed<Panel>("ShareRecordingStripHost").IsOnScreen());
			Assert.False(window.FindNamed<Panel>("ShareWorkspaceHost").IsOnScreen());
			Assert.False(window.FindNamed<Panel>("PlayWorkspace").IsOnScreen());
			WaitFor(() => window.FindNamed<TextBlock>("ShareRecordingPill").Text?.StartsWith("Recording a replay 00:") == true, "the pill never showed the clock");

			//§13.6: switching never stops a recording.
			model.SelectWorkspace(Workspace.Play);
			Dispatcher.UIThread.RunJobs();
			Assert.False(window.FindNamed<Panel>("ShareRecordingStripHost").IsOnScreen());
			UInt32 before = EmuApi.GetTimingInfo(CpuType.Nes).FrameCount;
			WaitFor(() => EmuApi.GetTimingInfo(CpuType.Nes).FrameCount > before + 5, "no frames were emulated while recording");
			Assert.True(new CoreReplayRecorder().IsSharing, "switching to Play stopped the replay");

			model.SelectWorkspace(Workspace.Share);
			Dispatcher.UIThread.RunJobs();
			Click(window.FindNamed<Button>("ShareStopRecordingButton"));

			Assert.False(new CoreReplayRecorder().IsSharing);
			Assert.True(window.FindNamed<Panel>("ShareWorkspaceHost").IsOnScreen());
			Assert.True(window.FindNamed<Panel>("ShareReplaySavedSheet").IsOnScreen());
			string name = window.FindNamed<TextBlock>("ShareReplayFileName").Text ?? "";
			Assert.StartsWith("synthetic-nrom ", name);
			string file = Path.Combine(ConfigManager.MovieFolder, "Shared", name);
			_paths.Add(file);
			Assert.True(File.Exists(file), "no .mmo was kept at " + file);
		} finally {
			if(new CoreReplayRecorder().IsSharing) {
				ShareRecordingSession.StopAndKeep();
			}
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//Remaster's "Share this project — opens Share" hook: the click is the
	//switch, and Share lands on W-H3 for that project (rule 11, §13.6).
	[AvaloniaFact]
	public void Share_this_project_switches_to_share_on_W_H3()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Preferences.Workspace = Workspace.Remaster;
		ConfigManager.Config.Preferences.ClassicMenuNoticeShown = true;
		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		string project = Project("Contra (USA)", built: true);
		Assert.True(model.Remaster.OpenProjectFolder(project));

		model.Remaster.RequestShareProject();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(Workspace.Share, model.Shell.Active);
		Assert.True(window.FindNamed<StackPanel>("ShareProjectPage").IsOnScreen());
		Assert.Equal("Share your Contra (USA) project", window.FindNamed<TextBlock>("ShareProjectTitle").Text);
	}

	//scripts/gen_synthetic_nrom.py, byte for byte (as RemasterWorkspaceTests).
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
