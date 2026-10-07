using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#953: a file dropped anywhere on the Player window (MainWindow.OnDrop, the
//PlayHomeView note) reaches LoadRomHelper.LoadFile - a ROM opens, and an IPS
//patch beside a single ROM opens that ROM patched. #986: a pack archive or
//folder dropped there is installed into EnhancementPacks/. MainWindow needs the real
//core (InitDll), so this class skips without one; the core-free drop sheets
//are PlayDragDropSheetTests. ConfigManager's home points at this case's temp
//folder (the PackAudioNoticeInstallTests pattern) so the recent-games entry the
//load writes never lands in the user's real home.
[Collection(NativeCoreCollection.Name)]
public class PlayDragDropRomTests : IDisposable
{
	private static readonly FieldInfo HomeField = typeof(ConfigManager).GetField("_homeFolder", BindingFlags.NonPublic | BindingFlags.Static)!;

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly object? _home = HomeField.GetValue(null);
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-953-" + Guid.NewGuid().ToString("N"));
	private readonly List<MainWindow> _windows = new();

	public PlayDragDropRomTests()
	{
		Directory.CreateDirectory(Path.Combine(_folder, "home"));
		Directory.CreateDirectory(Path.Combine(_folder, "games"));
		if(NativeCore.IsAvailable) {
			//#790: a case that ran a game leaves its console loaded.
			if(EmuApi.IsRunning()) {
				EmuApi.Stop();
			}
			Stopwatch clock = Stopwatch.StartNew();
			while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
				Thread.Sleep(10);
			}
			Assert.False(EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
			HomeField.SetValue(null, Path.Combine(_folder, "home"));
		}
	}

	public void Dispose()
	{
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		_windows.Clear();
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitFor(() => !EmuApi.IsRunning(), "EmuApi.Stop() left this case's game loaded");
		}
		Pump();

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		HomeField.SetValue(null, _home);
		try {
			Directory.Delete(_folder, true);
		} catch {
			//The temp folder going is not what the case was proving.
		}
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		Assert.False(EmuApi.IsRunning());
		return (window, model);
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Pump();
			Thread.Sleep(20);
		}
		Pump();
	}

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	private string Rom()
	{
		string rom = Path.Combine(_folder, "games", "Contra.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		return rom;
	}

	[AvaloniaFact]
	public void A_rom_dropped_on_the_player_window_opens_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string rom = Rom();
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		PlayDragDrop.DropFile(window, window, rom);

		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the dropped ROM never opened");
		Assert.Equal(rom, model.RomInfo.RomPath);
	}

	//An IPS dropped beside a single ROM opens that ROM with the patch applied.
	[AvaloniaFact]
	public void A_patch_dropped_on_the_player_window_opens_the_rom_beside_it_patched()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string rom = Rom();
		string patch = Path.Combine(_folder, "games", "Contra.ips");
		//PATCH, one record (offset 0x000010, one byte = 0xEA), EOF.
		File.WriteAllBytes(patch, new byte[] { (byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H', 0x00, 0x00, 0x10, 0x00, 0x01, 0xEA, (byte)'E', (byte)'O', (byte)'F' });
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		PlayDragDrop.DropFile(window, window, patch);

		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the dropped patch never opened its ROM");
		RomInfo info = model.RomInfo;
		Assert.Equal(rom, info.RomPath);
		Assert.Equal(patch, info.PatchPath);
	}

	//#986: the pack's files, under one top-level folder named like the pack.
	private string PackFolder()
	{
		string pack = Path.Combine(_folder, "downloads", "Contra Remastered");
		Directory.CreateDirectory(Path.Combine(pack, "textures"));
		File.WriteAllText(Path.Combine(pack, "pack.json"), "{\"name\": \"Contra Remastered\"}");
		File.WriteAllText(Path.Combine(pack, "textures", "hires.txt"), "<ver>106\n");
		return pack;
	}

	[AvaloniaFact]
	public void A_pack_archive_dropped_on_the_player_window_is_installed()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string archive = Path.Combine(_folder, "downloads", "Contra Remastered.zip");
		ZipFile.CreateFromDirectory(PackFolder(), archive, CompressionLevel.Fastest, true);
		(MainWindow window, _) = ShowPlay();

		PlayDragDrop.DropFile(window, window, archive);

		string installed = Path.Combine(ConfigManager.EnhancementPackFolder, "Contra Remastered.zip");
		WaitFor(() => File.Exists(installed), "the dropped pack archive never reached EnhancementPacks/");
		Assert.False(EmuApi.IsRunning(), "a pack archive went to the ROM loader");
	}

	[AvaloniaFact]
	public void A_pack_folder_dropped_on_the_player_window_is_installed()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string pack = PackFolder();
		(MainWindow window, _) = ShowPlay();

		PlayDragDrop.DropFile(window, window, pack);

		string installed = Path.Combine(ConfigManager.EnhancementPackFolder, "Contra Remastered.zip");
		WaitFor(() => File.Exists(installed), "the dropped pack folder never reached EnhancementPacks/");
		using(ZipArchive zip = ZipFile.OpenRead(installed)) {
			Assert.NotNull(zip.GetEntry("Contra Remastered/pack.json"));
		}
	}
}
