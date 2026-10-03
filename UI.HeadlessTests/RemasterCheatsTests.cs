using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

//ADR-0245 §3 / ADR-0184 §1 in the Remaster context, against the real core and
//a synthetic NROM. The rule is pinned host-free in
//UI.Tests/Cheats/CheatRecordingContextTests; this checks every way a cheat
//reaches a Remaster recording: one already on refuses Record While I Play
//with the code named; one turned on during the recording through the stored
//list (what the classic window and W-P11 write) is held back from the core
//and named on the strip; W-P11 opened from Play over the running recording
//disables a Game Genie row and refuses a typed one; Stop gives the held code
//back. Play without a recording stays unrestricted.
[Collection(NativeCoreCollection.Name)]
public class RemasterCheatsTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly bool _bootstrap = ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
	private readonly bool _disableAllCheats = ConfigManager.Config.Cheats.DisableAllCheats;
	private readonly List<string> _folders = new();

	private const string GameName = "synthetic-nrom";
	private static string CheatFile => Path.Combine(ConfigManager.CheatFolder, GameName + ".json");

	private static readonly CheatCode Genie = new() { Description = "Infinite lives", Type = CheatType.NesGameGenie, Codes = "SZKGPAVG", Enabled = true };
	private static readonly CheatCode Lives = new() { Description = "Lives", Type = CheatType.NesCustom, Codes = "0032:09", Enabled = true };

	public RemasterCheatsTests()
	{
		if(File.Exists(CheatFile)) {
			File.Delete(CheatFile);
		}
	}

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = _bootstrap;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		ConfigManager.Config.Cheats.DisableAllCheats = _disableAllCheats;
		if(NativeCore.IsAvailable) {
			ConfigManager.Config.EnhancementPacks.ApplyConfig();
		}
		ConfigManager.Config.Save();
		if(File.Exists(CheatFile)) {
			File.Delete(CheatFile);
		}
		foreach(string folder in _folders) {
			try {
				Directory.Delete(folder, true);
			} catch(IOException) {
			} catch(UnauthorizedAccessException) {
			}
		}
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowShell()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = false;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = false;
		ConfigManager.Config.EnhancementPacks.ApplyConfig();
		ConfigManager.Config.Cheats.DisableAllCheats = false;

		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		//No test reaches the network; the community catalog is fixed here.
		model.CommunityCheatsLastKnown = () => Array.Empty<CommunityCheatGame>();
		model.CommunityCheatsSource = () => Task.FromResult<IReadOnlyList<CommunityCheatGame>?>(Array.Empty<CommunityCheatGame>());
		return (window, model);
	}

	private string LoadSyntheticRom(MainWindowViewModel model)
	{
		string folder = Path.Combine(Path.GetTempPath(), "mesen-rc-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_folders.Add(folder);
		string rom = Path.Combine(folder, GameName + ".nes");
		File.WriteAllBytes(rom, BuildSyntheticNrom());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		return Path.Combine(folder, GameName);
	}

	private static void SaveCheats(params CheatCode[] cheats)
	{
		new CheatCodes() { Cheats = cheats.Select(c => c.Clone()).ToList() }.Save();
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

	private static string[] VisibleTexts(Control root) => root.FindAll<TextBlock>().Where(t => t.IsOnScreen()).Select(t => t.Text ?? "").ToArray();

	private static string AddCodeInSheet(MainWindow window, string code)
	{
		Click(window.FindNamed<Button>("CheatsAddCodeButton"));
		window.FindNamed<TextBox>("CheatsNewCodeBox").Text = code;
		Dispatcher.UIThread.RunJobs();
		Click(window.FindNamed<Button>("CheatsAddCodeConfirm"));
		return window.FindNamed<TextBlock>("CheatsAddCodeError").Text ?? "";
	}

	[AvaloniaFact]
	public void A_non_ram_cheat_refuses_the_remaster_recording_and_one_turned_on_while_it_records_is_held_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		try {
			string project = LoadSyntheticRom(model);

			//Already on: Record While I Play refuses, naming the code (ADR-0184 §1).
			SaveCheats(Genie, Lives);
			model.SelectWorkspace(Workspace.Remaster);
			Dispatcher.UIThread.RunJobs();
			Click(window.FindNamed<Button>("RemasterStartButton"));

			Assert.False(model.Remaster.IsRecording);
			Assert.False(EmuApi.IsMepBootstrapping(), "the recording started with a Game Genie code on");
			Assert.False(Directory.Exists(Path.Combine(project, "auto", "rec-001")), "a refused run left a recording behind");
			TextBlock notice = window.FindNamed<TextBlock>("RemasterNotice");
			Assert.True(notice.IsOnScreen());
			Assert.Equal("Not recording: these cheats change the game itself and would corrupt the recorded art. Turn them off first: Infinite lives (SZKGPAVG)", notice.Text);
			Assert.False(CheatCodes.RecordingArt);

			//A RAM code alone does not refuse.
			SaveCheats(new CheatCode() { Description = Genie.Description, Type = Genie.Type, Codes = Genie.Codes, Enabled = false }, Lives);
			Click(window.FindNamed<Button>("RemasterStartButton"));
			WaitFor(() => model.Remaster.IsRecording, "the recording never started");
			Assert.True(EmuApi.IsMepBootstrapping());
			Assert.False(notice.IsOnScreen());
			Assert.True(CheatCodes.RecordingArt);
			Assert.Empty(CheatCodes.HeldForRecording);

			//Turned on in the stored list while recording (the classic window and
			//W-P11 write it): held back from the core, and the strip says which.
			PlayerCheatsStore.SaveAndApply(new[] { new StoredCheat(Genie.Description, Genie.Type, Genie.Codes, true), new StoredCheat(Lives.Description, Lives.Type, Lives.Codes, true) });
			Assert.Equal(Genie.Codes, Assert.Single(CheatCodes.HeldForRecording).Codes);
			WaitFor(() => model.Remaster.HeldCheatsText.Length > 0, "the strip never named the held code");
			Assert.True(window.FindNamed<TextBlock>("RemasterHeldCheats").IsOnScreen());
			Assert.Equal("Off while recording art: Infinite lives (SZKGPAVG)", window.FindNamed<TextBlock>("RemasterHeldCheats").Text);

			//W-P11 from Play over the running recording (§13.6) is the recording-art context.
			model.SelectWorkspace(Workspace.Play);
			Dispatcher.UIThread.RunJobs();
			model.TogglePlayerOverlay();
			Dispatcher.UIThread.RunJobs();
			Click(window.FindNamed<Button>("OverlayCheatsButton"));
			Assert.True(window.FindNamed<Border>("PlayerCheatsSheet").IsOnScreen());
			Assert.Contains(CheatRecordingRule.RefusedReason, VisibleTexts(window.FindNamed<ItemsControl>("CheatsList")));
			Assert.Equal(CheatRecordingRule.RefusedReason, AddCodeInSheet(window, "SXIOPO"));
			Assert.DoesNotContain(PlayerCheatsStore.LoadStored(), c => c.Codes == "SXIOPO");
			Click(window.FindNamed<Button>("CheatsDoneButton"));
			model.TogglePlayerOverlay();
			Dispatcher.UIThread.RunJobs();

			//Stop: the held code reaches the core again.
			model.SelectWorkspace(Workspace.Remaster);
			Dispatcher.UIThread.RunJobs();
			Task stopped = model.Remaster.StopRecording(prepareFigures: false);
			WaitFor(() => stopped.IsCompleted, "the recording never stopped");
			Assert.False(EmuApi.IsMepBootstrapping());
			Assert.False(CheatCodes.RecordingArt);
			Assert.Empty(CheatCodes.HeldForRecording);
			Assert.Equal("", model.Remaster.HeldCheatsText);
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

	//#706: the classic cheat window's Disable All turns every code off, the
	//ones held back for the recording too - none stays named on the strip, and
	//none is waiting to come back when the recording ends.
	[AvaloniaFact]
	public void Disable_all_in_the_classic_cheat_window_clears_the_codes_held_back_for_a_recording()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		try {
			LoadSyntheticRom(model);
			SaveCheats(Genie, Lives);
			CheatCodes.SetRecordingArt(true);
			Assert.Equal(Genie.Codes, Assert.Single(CheatCodes.HeldForRecording).Codes);

			CheatListWindowViewModel classic = new();
			classic.DisableAllCheats = true;
			classic.ApplyCheats();

			Assert.Empty(CheatCodes.HeldForRecording);
		} finally {
			if(CheatCodes.RecordingArt) {
				CheatCodes.SetRecordingArt(false);
			}
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//#690 + user decision 2026-10-03 ("Liberar e pausar gravação (Recomendado)"):
	//an install that kept the legacy "Record while I play" setting (ADR-0243 Q3)
	//records every load by itself. That passive recording never locks a cheat:
	//a code that changes the game stops it (what was saved stays), and Record
	//While I Play, the user's own recording, still holds such codes back and
	//starts as the next rec-NNN.
	[AvaloniaFact]
	public void A_non_ram_code_stops_the_automatic_recording_and_remaster_still_holds_it_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		try {
			ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = true;
			ConfigManager.Config.EnhancementPacks.ApplyConfig();
			string project = LoadSyntheticRom(model);

			Assert.True(EmuApi.IsMepBootstrapping(), "the legacy setting did not record the load");
			PlayerCheatsStore.SaveAndApply(new[] { new StoredCheat(Genie.Description, Genie.Type, Genie.Codes, true), new StoredCheat(Lives.Description, Lives.Type, Lives.Codes, true) });
			Assert.False(EmuApi.IsMepBootstrapping(), "the Game Genie code left the automatic recording running");
			Assert.False(CheatCodes.RecordingArt);
			Assert.Empty(CheatCodes.HeldForRecording);

			model.SelectWorkspace(Workspace.Remaster);
			Dispatcher.UIThread.RunJobs();
			PlayerCheatsStore.SaveAndApply(new[] { new StoredCheat(Genie.Description, Genie.Type, Genie.Codes, false), new StoredCheat(Lives.Description, Lives.Type, Lives.Codes, true) });
			Click(window.FindNamed<Button>("RemasterRecordButton"));
			//The core starts it off the UI thread.
			WaitFor(() => model.Remaster.Transition == RecordingTransition.None, "the recording never started");

			Assert.True(model.Remaster.IsRecording, "Remaster refused to record after the automatic recording stopped: " + window.FindNamed<TextBlock>("RemasterNotice").Text);
			Assert.True(EmuApi.IsMepBootstrapping());
			Assert.EndsWith("rec-002", EmuApi.GetMepRecordingFolder().TrimEnd('/', '\\'));
			Assert.True(CheatCodes.RecordingArt);
			string manifest = File.ReadAllText(Path.Combine(project, "project.json"));
			Assert.Contains("\"id\": \"rec-001\"", manifest);
			Assert.Contains("\"id\": \"rec-002\"", manifest);

			//The user's own recording holds the code back instead of stopping.
			PlayerCheatsStore.SaveAndApply(new[] { new StoredCheat(Genie.Description, Genie.Type, Genie.Codes, true), new StoredCheat(Lives.Description, Lives.Type, Lives.Codes, true) });
			Assert.True(EmuApi.IsMepBootstrapping());
			Assert.Equal(Genie.Codes, Assert.Single(CheatCodes.HeldForRecording).Codes);

			Task stopped = model.Remaster.StopRecording(prepareFigures: false);
			WaitFor(() => stopped.IsCompleted, "the recording never stopped");
			Assert.False(EmuApi.IsMepBootstrapping());
			Assert.False(CheatCodes.RecordingArt);
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

	//User decision 2026-10-03: a non-RAM cheat already on at load keeps the
	//automatic recording from running at all.
	[AvaloniaFact]
	public void A_non_ram_cheat_already_on_at_load_leaves_no_automatic_recording()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		try {
			ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = true;
			ConfigManager.Config.EnhancementPacks.ApplyConfig();
			SaveCheats(Genie, Lives);
			LoadSyntheticRom(model);

			WaitFor(() => !EmuApi.IsMepBootstrapping(), "the automatic recording ran with a Game Genie code on");
			Assert.False(CheatCodes.RecordingArt);
			Assert.Empty(CheatCodes.HeldForRecording);
		} finally {
			if(EmuApi.IsMepBootstrapping()) {
				EmuApi.StopMepRecording();
			}
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//User decision 2026-10-03: in Play, over the passive automatic recording,
	//W-P11 switches every cheat; a code that changes the game stops it.
	[AvaloniaFact]
	public void In_play_over_the_automatic_recording_the_sheet_accepts_a_game_genie_code_and_stops_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		try {
			ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = true;
			ConfigManager.Config.EnhancementPacks.ApplyConfig();
			LoadSyntheticRom(model);
			Assert.True(EmuApi.IsMepBootstrapping(), "the legacy setting did not record the load");

			model.TogglePlayerOverlay();
			Dispatcher.UIThread.RunJobs();
			Click(window.FindNamed<Button>("OverlayCheatsButton"));
			Assert.DoesNotContain(CheatRecordingRule.RefusedReason, VisibleTexts(window.FindNamed<ItemsControl>("CheatsList")));
			Assert.Equal("", AddCodeInSheet(window, "SXIOPO"));

			Assert.Contains(PlayerCheatsStore.LoadStored(), c => c.Codes == "SXIOPO" && c.Enabled);
			Assert.False(EmuApi.IsMepBootstrapping(), "the code left the automatic recording running");
			Assert.False(CheatCodes.RecordingArt);
			Assert.Empty(CheatCodes.HeldForRecording);
		} finally {
			if(EmuApi.IsMepBootstrapping()) {
				EmuApi.StopMepRecording();
			}
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//Play with no Remaster recording is unrestricted (ADR-0245 §3).
	[AvaloniaFact]
	public void In_play_without_a_remaster_recording_a_game_genie_code_is_accepted()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		try {
			LoadSyntheticRom(model);
			model.TogglePlayerOverlay();
			Dispatcher.UIThread.RunJobs();
			Click(window.FindNamed<Button>("OverlayCheatsButton"));

			Assert.Equal("", AddCodeInSheet(window, "SXIOPO"));
			Assert.Contains(PlayerCheatsStore.LoadStored(), c => c.Codes == "SXIOPO" && c.Enabled);
			Assert.DoesNotContain(CheatRecordingRule.RefusedReason, VisibleTexts(window.FindNamed<ItemsControl>("CheatsList")));
			Assert.False(CheatCodes.RecordingArt);
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
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
