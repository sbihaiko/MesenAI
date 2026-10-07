using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#939 (PRD Part B §13.5.2 W-P16): W-P6 shows a pack's pending file as its
//orange line with Add the File…, which opens W-P16's add flow; the line goes
//once the file is in place, and a complete pack shows neither. The rules are
//pinned host-free in UI.Tests/Play/PackDetailPendingFileTests; this checks the
//realized sheet binds them against a real game (synthetic NROM).
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class PackDetailPendingFileTests : IDisposable
{
	//Container, Name, Version, Author, License, Sections, Enabled, Origin(0=folder),
	//PackId, ContentId - see MepPackListParser.
	private const string OnePack = "aaa\tContra Remastered\t1.2\tTastic\tCC BY-NC 4.0\ttextures,audio\t1\t0\tissue-1\tc1\n";
	private const string Sha1 = "0000000000000000000000000000000000000000";

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-939-" + Guid.NewGuid().ToString("N"));

	public PackDetailPendingFileTests()
	{
		Directory.CreateDirectory(_folder);
		//#790: a game left loaded by another class in the serial collection.
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitFor(() => !EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
		}
	}

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	private static void Click(Control root, string button)
	{
		root.FindNamed<Button>(button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowPlayWithGame()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		//The add's in-place completion would reach the catalog; the flow up to
		//it is what this class checks (#938's own tests cover the apply).
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = false;
		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus (MainMenuViewModel.Initialize).");

		string rom = Path.Combine(_folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");
		return (window, model);
	}

	private static void OpenDetail(MainWindowViewModel model)
	{
		Assert.False(model.OpenPackFromOverlay(OnePack, Sha1, "/packs", "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();
	}

	[AvaloniaFact]
	public void A_pending_file_shows_its_line_and_add_the_file_completes_it_through_the_pack_file_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		try {
			string drop = Path.Combine(_folder, "drop");
			string expected = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }));
			model.SetPendingPackDeps("Contra Remastered", new[] { new CommunityPackDepPrompt("contra-usa", "Contra (USA).nes", "", drop, expected) });

			//The first overlay opens W-P16; Play Without It leaves the pack
			//partial and returns to W-P4, whose Pack row opens W-P6.
			model.TogglePlayerOverlay();
			WaitFor(() => model.IsGamePaused, "Esc did not pause the game");
			Click(window, "PackDepSheetPlayWithout");
			Assert.True(model.IsPlayerOverlayVisible);
			OpenDetail(model);

			Border line = window.FindNamed<Border>("PackDetailPendingFile");
			Assert.True(line.IsOnScreen());
			Assert.Contains("warning", line.Classes);
			Assert.Equal("Contra Remastered needs one file", window.FindNamed<TextBlock>("PackDetailPendingFileTitle").Text);
			Button add = window.FindNamed<Button>("PackDetailAddFileButton");
			Assert.Equal("Add the File…", add.Content);
			//Keyboard and pad: the sheet opens with the focus on it (rule 9).
			WaitFor(() => add.IsFocused, "W-P6 did not focus Add the File… on open");

			Click(window, "PackDetailAddFileButton");
			Assert.False(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
			Assert.True(window.FindNamed<Panel>("PackDepSheetBackdrop").IsOnScreen());
			Assert.True(model.PackDepSheet.IsVisible);

			//W-P16's own add: checked by hash, copied, the notice cleared.
			string right = Path.Combine(_folder, "right.nes");
			File.WriteAllBytes(right, new byte[] { 1, 2, 3 });
			Task adding = model.PackDepSheet.TryFile(right);
			WaitFor(() => adding.IsCompleted, "the file check never finished");
			adding.GetAwaiter().GetResult();
			Assert.True(File.Exists(Path.Combine(drop, "right.nes")));
			Assert.False(model.PackDepSheet.IsVisible);

			//Back in W-P6: the file is in place, the line is gone.
			model.TogglePlayerOverlay();
			WaitFor(() => model.IsGamePaused, "Esc did not pause the game");
			OpenDetail(model);
			Assert.True(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
			Assert.False(line.IsOnScreen());
			Assert.False(add.IsOnScreen());
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	[AvaloniaFact]
	public void A_complete_pack_shows_neither_the_line_nor_add_the_file()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		try {
			model.TogglePlayerOverlay();
			WaitFor(() => model.IsGamePaused, "Esc did not pause the game");
			OpenDetail(model);

			Assert.True(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
			Assert.False(window.FindNamed<Border>("PackDetailPendingFile").IsOnScreen());
			Assert.False(window.FindNamed<Button>("PackDetailAddFileButton").IsOnScreen());
			WaitFor(() => window.FindNamed<Button>("PackDetailDoneButton").IsFocused, "W-P6 did not focus Done on open");
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}
}
