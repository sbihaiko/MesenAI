using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

//#1105 (spec #1102 slice 6): Settings > Audio > Menu sounds, as the player
//meets it. The rules (which action sounds, never over a running game) are
//host-free in UI.Tests/Play/MenuSoundsTests; what is proved HERE is the wiring:
//a pad press on the Play home reaches the sound call with the row on, and does
//not with it off. The sink is the one seam - a headless build has no device to
//hear - and everything above it is the real pad path.
[Collection(NativeCoreCollection.Name)]
public class MenuSoundsTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _menuSounds = ConfigManager.Config.Audio.MenuSounds;
	private readonly List<MainWindow> _windows = new();
	private readonly List<MenuSoundKind> _played = new();

	//The stand-in pad backend (see PlayerLibraryConsoleFilterTests): a headless
	//window has no key manager to name a pad.
	private const ushort PadBase = 0x1000;
	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };
	private static readonly Dictionary<ushort, string> Backend = BuildBackend();
	private static readonly Dictionary<string, ushort> BackendCodes = Backend.ToDictionary(pair => pair.Value, pair => pair.Key);

	private static Dictionary<ushort, string> BuildBackend()
	{
		Dictionary<ushort, string> names = new();
		for(int pad = 1; pad <= 20; pad++) {
			for(int button = 0; button < ButtonNames.Length; button++) {
				names[(ushort)(PadBase + (pad - 1) * 0x100 + button)] = "Pad" + pad + " " + ButtonNames[button];
			}
		}
		return names;
	}

	private static string BackendName(ushort keyCode) => Backend.TryGetValue(keyCode, out string? name) ? name : "";

	private static ushort BackendCode(string name) => BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	private PadNavMapping? _mapping;
	private PadNavMapping Mapping => _mapping ??= PadNavControls.Resolve(PadFamily.Xbox, 0, BackendCode)
		?? throw new InvalidOperationException("the stand-in table does not answer the Xbox preset's names");

	public MenuSoundsTests()
	{
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			Stopwatch clock = Stopwatch.StartNew();
			while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
				Thread.Sleep(10);
			}
			Assert.False(EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
		}
		MenuSoundOutput.SetSinkForTest(kind => _played.Add(kind));
	}

	public void Dispose()
	{
		MenuSoundOutput.SetSinkForTest(null);
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		ConfigManager.Config.Audio.MenuSounds = _menuSounds;
		ConfigManager.Config.Save();
	}

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	private MainWindow ShowPlayHome()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		Stopwatch clock = Stopwatch.StartNew();
		while(model.MainMenu.HelpMenuItems.Count == 0) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException("MainWindow never finished building its menus.");
			}
			Pump();
			Thread.Sleep(20);
		}
		Pump();
		return window;
	}

	private void Press(MainWindow window, PadNavAction action)
	{
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(Mapping, action) }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
	}

	[AvaloniaFact]
	public void With_the_row_on_move_confirm_and_back_each_reach_the_sound_call()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.MenuSounds = true;
		MainWindow window = ShowPlayHome();

		Press(window, PadNavAction.Down);
		Press(window, PadNavAction.Back);
		Press(window, PadNavAction.Confirm);

		Assert.Equal(new[] { MenuSoundKind.Move, MenuSoundKind.Back, MenuSoundKind.Confirm }, _played);
	}

	[AvaloniaFact]
	public void With_the_row_off_no_press_reaches_the_sound_call()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.MenuSounds = false;
		MainWindow window = ShowPlayHome();

		Press(window, PadNavAction.Down);
		Press(window, PadNavAction.Back);
		Press(window, PadNavAction.Confirm);

		Assert.Empty(_played);
	}

	//The press that hands the console back (Confirm on the overlay's Resume)
	//leaves a game running unpaused; the sound is judged after it, so no blip.
	[AvaloniaFact]
	public void A_confirm_that_resumes_the_game_does_not_sound()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.MenuSounds = true;
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		bool pauseInBackground = prefs.PauseWhenInBackground;
		bool pauseInMenus = prefs.PauseWhenInMenusAndConfig;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		MainWindow window = ShowPlayHome();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);

		string folder = Path.Combine(Path.GetTempPath(), "mesen-menusounds-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		try {
			string rom = Path.Combine(folder, "synthetic-nrom.nes");
			File.WriteAllBytes(rom, SyntheticNrom.Build());
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			WaitUntil(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
			EmuApi.Resume();
			WaitUntil(() => !EmuApi.IsPaused(), "the game never ran unpaused");

			EmuApi.Pause();
			WaitUntil(() => EmuApi.IsPaused(), "the game never paused");
			model.OpenPauseOverlay();
			//The bridge grants the overlay's focus from its own tick.
			WaitUntil(() => {
				PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
				return (window.FocusManager?.GetFocusedElement() as Avalonia.Controls.Control)?.Name == "OverlayResumeButton";
			}, "the overlay opened without the focus on Resume");
			_played.Clear();

			Press(window, PadNavAction.Confirm);

			WaitUntil(() => !EmuApi.IsPaused(), "Confirm on Resume did not resume the game");
			Assert.Empty(_played);
		} finally {
			prefs.PauseWhenInBackground = pauseInBackground;
			prefs.PauseWhenInMenusAndConfig = pauseInMenus;
			EmuApi.Stop();
			Directory.Delete(folder, true);
		}
	}

	private static void WaitUntil(Func<bool> condition, string message)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 10000) {
				throw new XunitException(message);
			}
			Pump();
			Thread.Sleep(20);
		}
	}

	[Fact]
	public void The_row_is_off_on_a_new_install()
	{
		Assert.False(new AudioConfig().MenuSounds);
	}
}
