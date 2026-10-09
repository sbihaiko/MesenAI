using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
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
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly List<MainWindow> _windows = new();
	private readonly List<MenuSoundKind> _played = new();
	private string? _tempFolder;
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
		//The cases that load a game leave it loaded, and the next case's window
		//would come up over a running core (#790) - so the core is stopped here,
		//while the window that was showing it is still up, the way the pad path's
		//own case does it (MenuSoundsTests).
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			Stopwatch clock = Stopwatch.StartNew();
			while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
				Thread.Sleep(10);
			}
		}
		if(_tempFolder != null && Directory.Exists(_tempFolder)) {
			Directory.Delete(_tempFolder, true);
		}
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
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Audio.MenuSounds = _menuSounds;
		ConfigManager.Config.Save();
	}

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	private MainWindow ShowPlayHome() => ShowPlayHome(out _);

	private MainWindow ShowPlayHome(out MainWindowViewModel model)
	{
		BindOverlayToEsc();
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		//The case below needs the game to KEEP running when the window is not the
		//active one, which is the state a headless run is always in.
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		model = Assert.IsType<MainWindowViewModel>(window.DataContext);
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

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	//A real game in the real core, running unpaused, the way
	//PauseOverlayViewTests arranges one (a synthetic NROM, so no ROM ships with
	//the repo).
	private void LoadGame(MainWindowViewModel model)
	{
		string folder = Path.Combine(Path.GetTempPath(), "mesen-1127-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_tempFolder = folder;
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");
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

	//The gate reads the state the press LEFT, not the state it found: a Confirm
	//that resumes a game leaves it running unpaused, and MenuSounds.ShouldPlay
	//refuses a blip over a running game. The pad's Confirm on that press is
	//silent for exactly this reason (PlayPadNavigationWiring reads the gate after
	//Apply), so the keyboard's must be too - "the same hook" means the same
	//answer, not merely the same call.
	//
	//The tunnel handler this window answers keys on runs BEFORE the focused
	//control activates, so a gate read there finds the game still paused, sounds
	//the blip, and lets the resume happen under it.
	[AvaloniaFact]
	public void Confirm_that_resumes_the_game_is_silent_like_the_pads()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.MenuSounds = true;
		MainWindow window = ShowPlayHome(out MainWindowViewModel model);
		LoadGame(model);

		//Esc over a running game: W-P4 opens and the game pauses, which is the
		//one state a blip is allowed in.
		model.TogglePlayerOverlay();
		WaitFor(() => model.IsGamePaused, "the overlay did not pause the game");
		Assert.True(window.FindNamed<Button>("OverlayResumeButton").IsFocused, "Resume did not take the focus");
		_played.Clear();

		PressKey(window, PhysicalKey.Enter);
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused, "Enter on Resume did not resume the game");
		Pump();

		Assert.Empty(_played);
	}

	//#1160: authority, not only the door. The pad sounds where its bridge
	//resolved an action, and the bridge resolves one only where
	//PlayPadNavigation.HasAuthority says the pad is the GUI's - the door, the
	//capture, and the load card / pause / surface state together. The keyboard
	//arm was gated on the door alone, so a press the pad refuses outright still
	//blipped: a game paused with nothing of Play drawn over it (the Pause
	//shortcut's result, a debugger break) is the console's, the pad is silent
	//there, and every arrow key sounded Move.
	//
	//The pad is the reference here rather than a restatement of the rule: the
	//same move is made through both paths, in one state, and both must answer
	//alike - ADR-0270 D10 binds the two to the same ANSWER, not to the same call.
	[AvaloniaFact]
	public void A_paused_game_with_no_play_surface_is_silent_on_both_paths()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.MenuSounds = true;
		MainWindow window = ShowPlayHome(out MainWindowViewModel model);
		LoadGame(model);

		//What the Pause shortcut leaves: a paused console with no Play surface
		//over it, which is the one state the pad has no authority in.
		EmuApi.Pause();
		WaitFor(() => EmuApi.IsPaused(), "the game never paused");
		Assert.False(model.IsPlaySurfaceOverGame, "a Play surface is up over the paused game");
		Pump();
		_played.Clear();

		PressPad(window, PadNavAction.Down);
		Assert.Empty(_played);

		PressKey(window, PhysicalKey.ArrowDown);
		Assert.Empty(_played);
	}

	//#1160, the Esc half of the same defect. Esc over a running game opens W-P4
	//and pauses it, but the press is made while the console still holds the pad:
	//no authority over a game that runs unpaused means the pad never produces
	//this Back at all. The keyboard sounded one, because the arm read the door
	//and let the overlay's own pause answer for it.
	//
	//The authority that decides is the one in force BEFORE the press - what the
	//pad's own tick reads - so opening the overlay cannot talk the press into a
	//blip it did not earn.
	[AvaloniaFact]
	public void Escape_over_a_running_game_opens_the_overlay_and_sounds_nothing()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.MenuSounds = true;
		MainWindow window = ShowPlayHome(out MainWindowViewModel model);
		LoadGame(model);
		_played.Clear();

		PressKey(window, PhysicalKey.Escape);

		//The press still does its job: the overlay opens and the game pauses.
		WaitFor(() => model.IsPlayerOverlayVisible && model.IsGamePaused, "Esc did not open W-P4 over the running game");
		WaitFor(() => EmuApi.IsPaused(), "the overlay did not pause the game");
		Pump();
		Assert.Empty(_played);
	}

	//A held key is one press for Confirm (#1080's rule, the one the Esc arm
	//above already applies): the OS repeats it as more KeyDowns with no KeyUp
	//between, and the pad's own Confirm does not repeat either
	//(PadNavRepeat.Held deliberately skips Confirm and Back). The arrows are left
	//to repeat on purpose - each repeat is Avalonia's own move of the ring, the
	//keyboard's counterpart of the pad's held direction.
	//
	//The row is the Save states one, whose sheet takes a button: the game stays
	//paused across both KeyDowns, so the gate stays open and the repeat guard is
	//the only thing that can silence the second one.
	[AvaloniaFact]
	public void A_held_enter_sounds_once()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.MenuSounds = true;
		MainWindow window = ShowPlayHome(out MainWindowViewModel model);
		LoadGame(model);

		model.TogglePlayerOverlay();
		WaitFor(() => model.IsGamePaused, "the overlay did not pause the game");
		PressKey(window, PhysicalKey.ArrowDown);
		Assert.True(window.FindNamed<Button>("OverlaySaveStatesButton").IsFocused, "ArrowDown did not reach the Save states row");
		_played.Clear();

		//What the OS sends for a held key: a second KeyDown, no KeyUp between.
		window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
		Pump();
		window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
		Pump();

		Assert.Equal(new[] { MenuSoundKind.Confirm }, _played);
	}
}
