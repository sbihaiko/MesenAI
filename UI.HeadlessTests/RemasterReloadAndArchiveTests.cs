using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//Against the real core and a synthetic NROM: #698, a reload of the running
//game (Reload ROM, Power Cycle, a pack switch or pick) asks before it ends a
//Remaster recording, as opening another game does (W-X3); #689, a game opened
//from a .zip gives the Remaster jobs its inner ROM, not the archive. The rules
//are pinned host-free in UI.Tests/Remaster (RemasterInterruptionsTests,
//RemasterRomFileTests); this checks the wiring.
[Collection(NativeCoreCollection.Name)]
public class RemasterReloadAndArchiveTests : IDisposable
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
		string folder = Path.Combine(Path.GetTempPath(), "mesen-rra-" + Guid.NewGuid().ToString("N"));
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

	private static (MainWindow Window, MainWindowViewModel Model) ShowShell()
	{
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
		return (window, model);
	}

	private static void Load(MainWindowViewModel model, string resource)
	{
		Assert.True(EmuApi.LoadRom(resource, string.Empty), $"the core refused to load {resource}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
	}

	//Counts the core's GameLoaded notifications: a test waits for the reload
	//it started before it stops the core (a stop racing the reload hangs it).
	private sealed class LoadCounter : IDisposable
	{
		private readonly NotificationListener _listener = new();
		private int _loads;
		public int Loads => Volatile.Read(ref _loads);

		public LoadCounter()
		{
			_listener.OnNotification += e => {
				if(e.NotificationType == ConsoleNotificationType.GameLoaded) {
					Interlocked.Increment(ref _loads);
				}
			};
		}

		public void Dispose() => _listener.Dispose();
	}

	//Each reload path asks while recording, and the recording runs on until
	//the answer; Cancel keeps it. Stop and Reload keeps the recording, as
	//Stop does, and then reloads.
	[AvaloniaFact]
	public void Reloading_the_game_while_recording_asks_first()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		string folder = TempFolder();
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		try {
			Load(model, rom);
			model.SelectWorkspace(Workspace.Remaster);
			Dispatcher.UIThread.RunJobs();
			Assert.True(model.Remaster.StartRecording());
			string recording = EmuApi.GetMepRecordingFolder();

			Action[] reloads = {
				LoadRomHelper.ReloadRom,
				LoadRomHelper.PowerCycle,
				() => LoadRomHelper.ApplyPackChange(ConsoleType.Nes, LoadRomHelper.ReloadRom),
			};
			foreach(Action reload in reloads) {
				reload();
				Dispatcher.UIThread.RunJobs();
				Assert.True(model.Interruption.IsVisible, "a reload ended the recording without asking");
				Assert.Equal("Reload synthetic-nrom? This recording stops and is kept as recording 1.", window.FindNamed<TextBlock>("InterruptionText").Text);
				Assert.Equal("Stop and Reload", window.FindNamed<Button>("InterruptionGoButton").Content);
				Thread.Sleep(300);
				Dispatcher.UIThread.RunJobs();
				Assert.True(EmuApi.IsMepBootstrapping(), "the question stopped the recording before an answer");
				Assert.Equal(recording, EmuApi.GetMepRecordingFolder());
				Click(window.FindNamed<Button>("InterruptionKeepButton"));
				Assert.False(model.Interruption.IsVisible);
				Assert.True(model.Remaster.IsRecording);
			}

			using LoadCounter loads = new();
			LoadRomHelper.ReloadRom();
			Dispatcher.UIThread.RunJobs();
			Click(window.FindNamed<Button>("InterruptionGoButton"));
			Assert.False(model.Remaster.IsRecording);
			Assert.Contains("\"id\": \"rec-001\"", File.ReadAllText(Path.Combine(folder, "synthetic-nrom", "project.json")));
			WaitFor(() => loads.Loads > 0, "the game never reloaded");
			WaitFor(() => !model.Remaster.Job.IsRunning, "the kit job never finished", 180000);
		} finally {
			if(EmuApi.IsMepBootstrapping()) {
				EmuApi.StopMepRecording();
			}
			if(CheatCodes.RecordingArt) {
				CheatCodes.SetRecordingArt(false);
			}
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//No recording: a reload never asks.
	[AvaloniaFact]
	public void Reloading_without_a_recording_never_asks()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		string folder = TempFolder();
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		try {
			Load(model, rom);
			using LoadCounter loads = new();
			LoadRomHelper.ReloadRom();
			Dispatcher.UIThread.RunJobs();
			Assert.False(model.Interruption.IsVisible);
			WaitFor(() => loads.Loads > 0, "the game never reloaded");
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//#689: kit/build/import read --rom as an iNES file. The ROM has its own
	//name, so its recent-game record never stands in for synthetic-nrom's.
	[AvaloniaFact]
	public void A_game_opened_from_a_zip_gives_the_remaster_jobs_its_inner_rom()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		string folder = TempFolder();
		string zip = Path.Combine(folder, "zipped-nrom.zip");
		using(ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
			using Stream entry = archive.CreateEntry("zipped-nrom.nes").Open();
			entry.Write(SyntheticNrom.Build());
		}
		try {
			Load(model, zip + "\x1" + "zipped-nrom.nes");

			string romForJobs = model.Remaster.RomPath;
			Assert.Equal("zipped-nrom.nes", Path.GetFileName(romForJobs));
			Assert.True(File.Exists(romForJobs), $"{romForJobs} does not exist");
			Assert.Equal(SyntheticNrom.Build(), File.ReadAllBytes(romForJobs));
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}
}
