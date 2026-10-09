using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1127 (spec #1102 slice 6, ADR-0270 D10): the keyboard's move, confirm and back
//on the Play surfaces raise the SAME menu-sound hook the pad path does - one
//gate (MenuSounds.ShouldPlay), one sink (MenuSoundOutput), judged on the state
//the press left. The sink is the one seam (a headless build has no device to
//hear) and everything between it and the key is the shipping path: the real
//MainWindow, its own key handler, and the real focused controls of the Play
//home. The rules the press obeys are already pinned host-free - which action a
//sound belongs to and when one plays at all (UI.Tests/Play/MenuSoundsTests);
//what is proved HERE is that the keyboard reaches the same call the pad does.
[Collection(NativeCoreCollection.Name)]
public class PlayKeyboardMenuSoundsTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _menuSounds = ConfigManager.Config.Audio.MenuSounds;
	private readonly List<MainWindow> _windows = new();
	private readonly List<MenuSoundKind> _played = new();
	private KeyCombination? _overlayBinding;
	private KeyCombination? _overlayBinding2;

	//The stand-in pad backend (see MenuSoundsTests): a headless window has no key
	//manager to name a pad. Only the third case below presses a pad, and it is
	//there to put both input paths through the one sink in the same run.
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

	public PlayKeyboardMenuSoundsTests()
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
		if(_overlayBinding != null) {
			ShortcutKeyInfo overlay = ConfigManager.Config.Preferences.ShortcutKeys.Find(sk => sk.Shortcut == EmulatorShortcut.ToggleOverlay)!;
			overlay.KeyCombination = _overlayBinding;
			overlay.KeyCombination2 = _overlayBinding2!;
		}
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
		BindOverlayToEsc();
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

	//A real key press, released: the press is what the window answers, and the
	//release is what ends a held key (one press per press).
	private static void PressKey(MainWindow window, PhysicalKey key)
	{
		window.KeyPressQwerty(key, RawInputModifiers.None);
		window.KeyReleaseQwerty(key, RawInputModifiers.None);
		Pump();
	}

	//The window answers Esc off ToggleOverlay's own binding, and a headless run
	//registers no keyboard backend (InputApi.GetKeyCode answers 0 for every name),
	//so the binding the config carries is empty and the press would match nothing.
	//The test binds it to Esc the way a key manager would have - the same
	//arrangement PlayEscOpensOverlayTests makes (#1080) - and puts it back in
	//Dispose, because ConfigManager.Config is the process's one config.
	private void BindOverlayToEsc()
	{
		List<ShortcutKeyInfo> shortcuts = ConfigManager.Config.Preferences.ShortcutKeys;
		ShortcutKeyInfo? overlay = shortcuts.Find(sk => sk.Shortcut == EmulatorShortcut.ToggleOverlay);
		if(overlay == null) {
			overlay = new ShortcutKeyInfo { Shortcut = EmulatorShortcut.ToggleOverlay };
			shortcuts.Add(overlay);
		} else {
			_overlayBinding = overlay.KeyCombination;
			_overlayBinding2 = overlay.KeyCombination2;
		}
		overlay.KeyCombination = new KeyCombination() { Key1 = (ushort)Avalonia.Input.Key.Escape };
		overlay.KeyCombination2 = new KeyCombination();
	}

	private void PressPad(MainWindow window, PadNavAction action)
	{
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(Mapping, action) }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
	}

	[AvaloniaFact]
	public void Arrow_enter_and_escape_each_reach_the_same_sound_call_as_the_pad()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.MenuSounds = true;
		MainWindow window = ShowPlayHome();

		PressKey(window, PhysicalKey.ArrowDown);
		PressKey(window, PhysicalKey.Escape);
		PressKey(window, PhysicalKey.Enter);

		//The pad's own case asserts this same sequence for its three presses
		//(MenuSoundsTests): one Down, one Back, one Confirm, in that order.
		Assert.Equal(new[] { MenuSoundKind.Move, MenuSoundKind.Back, MenuSoundKind.Confirm }, _played);
	}

	[AvaloniaFact]
	public void With_the_row_off_no_key_reaches_the_sound_call()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.MenuSounds = false;
		MainWindow window = ShowPlayHome();

		PressKey(window, PhysicalKey.ArrowDown);
		PressKey(window, PhysicalKey.Escape);
		PressKey(window, PhysicalKey.Enter);

		Assert.Empty(_played);
	}

	//The point of the slice: not a second sound path next to the pad's, but the
	//same one. Both presses land in the same sink list, in the order they were
	//made, so a keyboard move and a pad move are indistinguishable to it.
	[AvaloniaFact]
	public void The_pad_and_the_keyboard_reach_the_one_sink()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.MenuSounds = true;
		MainWindow window = ShowPlayHome();

		PressPad(window, PadNavAction.Down);
		PressKey(window, PhysicalKey.ArrowDown);

		Assert.Equal(new[] { MenuSoundKind.Move, MenuSoundKind.Move }, _played);
	}
}
