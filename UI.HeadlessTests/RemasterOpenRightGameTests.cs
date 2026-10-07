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

//#969 (PRD Part B §13.5.5 W-X2, §13.3 rules 5 and 10): with project A open
//and game B loaded, W-R1 says "This is not the game the project was recorded
//from." with Open the Right Game…. The button loads A's ROM (named like the
//project folder) and the workspace stays Remaster; with no such ROM it opens
//the ROM picker on the games folder. The rule is pinned host-free in
//UI.Tests/Remaster/RemasterRightGameTests.
[Collection(NativeCoreCollection.Name)]
public class RemasterOpenRightGameTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly bool _bootstrap = ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
	private readonly string _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;
	private readonly List<string> _folders = new();

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		prefs.GameFolder = _gameFolder;
		prefs.OverrideGameFolder = _overrideGameFolder;
		ConfigManager.Config.EnhancementPacks.BootstrapEnhancementFolder = _bootstrap;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		if(NativeCore.IsAvailable) {
			EmuApi.Stop();
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
		string folder = Path.Combine(Path.GetTempPath(), "mesen-969-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_folders.Add(folder);
		return folder;
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

	//A project is a folder with auto/ (RemasterProjectReader.IsProjectFolder).
	private static string MakeProject(string folder, string game)
	{
		string project = Path.Combine(folder, game);
		Directory.CreateDirectory(Path.Combine(project, RemasterProjectReader.AutoFolder));
		return project;
	}

	//Loads `rom`, opens Remaster and picks `project` with Choose Folder….
	private static void OpenForeignProject(MainWindowViewModel model, string rom, string project)
	{
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.GetRomName() == Path.GetFileNameWithoutExtension(rom), "the ROM never reported as loaded");
		model.SelectWorkspace(Workspace.Remaster);
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.Remaster.OpenProjectFolder(project), $"{project} was not read as a project");
		Dispatcher.UIThread.RunJobs();
	}

	[AvaloniaFact]
	public void The_wrong_game_shows_its_sentence_and_the_button_loads_the_projects_rom_and_stays_in_remaster()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		string folder = TempFolder();
		string romA = Path.Combine(folder, "game-a.nes");
		string romB = Path.Combine(folder, "game-b.nes");
		File.WriteAllBytes(romA, BuildSyntheticNrom(0xEA));
		File.WriteAllBytes(romB, BuildSyntheticNrom(0x00));
		string projectA = MakeProject(folder, "game-a");
		model.Remaster.RecentRoms = () => new[] { romB, romA };
		model.Remaster.GamesFolder = () => null;

		OpenForeignProject(model, romB, projectA);

		Assert.True(window.FindNamed<StackPanel>("RemasterProjectScreen").IsOnScreen());
		Assert.True(window.FindNamed<Border>("RemasterWrongGame").IsOnScreen(), "W-R1 does not show the wrong-game row");
		Assert.Equal("This is not the game the project was recorded from.", window.FindNamed<TextBlock>("RemasterWrongGameText").Text);
		Button open = window.FindNamed<Button>("RemasterOpenRightGameButton");
		Assert.Equal("Open the Right Game…", open.Content);
		Assert.False(window.FindNamed<Button>("RemasterRecordButton").IsEffectivelyEnabled);
		PlayerRender.Save(PlayerRender.Capture(window), "W-X2-remaster");

		Click(open);

		WaitFor(() => model.RomInfo.GetRomName() == "game-a", "Open the Right Game… did not load the project's ROM");
		WaitFor(() => window.FindNamed<Button>("RemasterRecordButton").IsEffectivelyEnabled, "Record stayed disabled with the project's own game loaded");
		Assert.Equal(Workspace.Remaster, ConfigManager.Config.Preferences.Workspace);
		Assert.True(window.FindNamed<StackPanel>("RemasterProjectScreen").IsOnScreen(), "loading the right game left W-R1 (rule 5)");
		Assert.False(window.FindNamed<Border>("RemasterWrongGame").IsOnScreen(), "the wrong-game row stayed after the right game loaded");
	}

	//#984 (rule 10): the wrong game is said once, in the row with its button;
	//the five controls stay disabled without repeating it under each.
	[AvaloniaFact]
	public void The_wrong_game_sentence_is_shown_once_and_the_per_control_reasons_are_hidden()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		string folder = TempFolder();
		string romB = Path.Combine(folder, "game-b.nes");
		File.WriteAllBytes(romB, BuildSyntheticNrom(0x00));
		string projectA = MakeProject(TempFolder(), "game-a");
		model.Remaster.RecentRoms = () => new[] { romB };
		model.Remaster.GamesFolder = () => null;

		OpenForeignProject(model, romB, projectA);

		const string sentence = "This is not the game the project was recorded from.";
		TextBlock said = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsOnScreen() && t.Text == sentence);
		Assert.Equal("RemasterWrongGameText", said.Name);
		foreach(string name in new[] { "RemasterRecordReason", "RemasterTasReason", "RemasterAiReason", "RemasterPrepareReason", "RemasterBuildReason" }) {
			Assert.False(window.FindNamed<TextBlock>(name).IsOnScreen(), $"{name} repeats a reason under the wrong-game row");
		}
		Assert.False(window.FindNamed<Button>("RemasterRecordButton").IsEffectivelyEnabled);
		Assert.False(window.FindNamed<Button>("RemasterPrepareButton").IsEffectivelyEnabled);
	}

	[AvaloniaFact]
	public void With_no_matching_rom_the_button_opens_the_rom_picker_on_the_games_folder()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		string folder = TempFolder();
		string romB = Path.Combine(folder, "game-b.nes");
		File.WriteAllBytes(romB, BuildSyntheticNrom(0x00));
		string projectA = MakeProject(TempFolder(), "game-a");
		ConfigManager.Config.Preferences.GameFolder = folder;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		model.Remaster.RecentRoms = () => new[] { romB };
		PlayerRomPickerViewModel picker = model.Remaster.RightGamePicker;
		picker.RunScanInline = true;
		picker.SuggestionSource = _ => Array.Empty<RomPickerHit>();
		picker.VolumeSource = () => Array.Empty<string>();

		OpenForeignProject(model, romB, projectA);
		Assert.False(window.FindNamed<Control>("RemasterRightGamePicker").IsOnScreen());

		Click(window.FindNamed<Button>("RemasterOpenRightGameButton"));

		Assert.True(window.FindNamed<Control>("RemasterRightGamePicker").IsOnScreen(), "the ROM picker did not open");
		Assert.Contains(picker.Rows, r => r.Path == romB);
		Assert.Equal("game-b", model.RomInfo.GetRomName());
		Assert.Equal(Workspace.Remaster, ConfigManager.Config.Preferences.Workspace);
	}

	//scripts/gen_synthetic_nrom.py (as RemasterWorkspaceTests); `fill` sets
	//the CHR bytes so two ROMs differ.
	private static byte[] BuildSyntheticNrom(byte fill)
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
		for(int i = 0; i < chrSize; i++) {
			rom[16 + prgSize + i] = fill;
		}
		return rom;
	}
}
