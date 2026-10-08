using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1080: Esc must open W-P4 from the focus state the game view is left in when it
//shows - the player presses Esc while the picture is up and nothing has asked
//for the focus since. The bug was that the key never reached the Play overlay
//router in that state: the window's own key path fed the core, which is not who
//answers a key in Player mode (P.4: the overlay shortcut owns Esc there), and
//re-focusing the window was what made the press work.
//
//The real window and the real core, and no Focus() call anywhere: a test that
//requested the focus first would be testing its own request. Startup does ask
//for the focus on its own (MainWindow.OnOpened, and GameResumed when the game
//resumes), so the arrangement takes it back before anything is pressed and the
//press is made from there - the renderer up, the keyboard outside it.
[Collection(NativeCoreCollection.Name)]
public class PlayEscOpensOverlayTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-esc-" + Guid.NewGuid().ToString("N"));
	private KeyCombination? _overlayBinding;
	private KeyCombination? _overlayBinding2;
	private bool _overlayWasMissing;

	public void Dispose()
	{
		//CI has no native core (ADR-0131): the test skips, and Stop would turn
		//the skip into a DllNotFoundException failure.
		if(NativeCore.IsAvailable) {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
		if(_overlayWasMissing) {
			ConfigManager.Config.Preferences.ShortcutKeys.RemoveAll(sk => sk.Shortcut == EmulatorShortcut.ToggleOverlay);
		} else if(_overlayBinding != null) {
			ShortcutKeyInfo? overlay = OverlayShortcut();
			overlay.KeyCombination = _overlayBinding;
			overlay.KeyCombination2 = _overlayBinding2!;
		}
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Save();
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private static ShortcutKeyInfo OverlayShortcut()
	{
		return ConfigManager.Config.Preferences.ShortcutKeys.Find(sk => sk.Shortcut == EmulatorShortcut.ToggleOverlay)!;
	}

	//#1080: the key the window answers is ToggleOverlay's own binding, so the test
	//moves it the way a player does - onto F1, with the controller chord out of the
	//keyboard's way. It is put back in Dispose: ConfigManager.Config is the process's
	//one config and the other headless classes read it.
	//
	//The arrangement writes the binding the test is about, and only when the config
	//does not already hold one (`onlyWhenUnbound`), so a test that rebound the overlay
	//itself is not undone by the arrangement the running game makes. The codes are the
	//shared key table's, which Avalonia's Key enum mirrors and which this window feeds
	//the core with (Esc 13, F1 90) - a headless run registers no keyboard backend
	//(InputApi.GetKeyCode answers 0 for every name), so the shortcut list this test
	//finds is the one a config that never seeded it has: empty.
	private void ArrangeOverlayKey(Key key, bool onlyWhenUnbound)
	{
		List<ShortcutKeyInfo> shortcuts = ConfigManager.Config.Preferences.ShortcutKeys;
		ShortcutKeyInfo? overlay = shortcuts.Find(sk => sk.Shortcut == EmulatorShortcut.ToggleOverlay);
		if(overlay == null) {
			overlay = new ShortcutKeyInfo { Shortcut = EmulatorShortcut.ToggleOverlay };
			shortcuts.Add(overlay);
			_overlayWasMissing = true;
		} else if(onlyWhenUnbound && !overlay.KeyCombination.IsEmpty) {
			return;
		}

		if(_overlayBinding == null && !_overlayWasMissing) {
			_overlayBinding = overlay.KeyCombination;
			_overlayBinding2 = overlay.KeyCombination2;
		}
		overlay.KeyCombination = new KeyCombination() { Key1 = (UInt16)key };
		overlay.KeyCombination2 = new KeyCombination();
	}

	private (MainWindow Window, MainWindowViewModel Model, Panel Renderer) ShowRunningGame()
	{
		ArrangeOverlayKey(Key.Escape, onlyWhenUnbound: true);
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus (MainMenuViewModel.Initialize).");

		Directory.CreateDirectory(_folder);
		string rom = Path.Combine(_folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");

		//#1080: the state the player's Esc arrives in - the game up on the native
		//renderer, and nothing inside the window holding the keyboard. The startup
		//above asked for that focus twice (MainWindow.OnOpened focuses RendererPanel,
		//and GameResumed focuses it again when the game resumes), so a press made
		//from where those requests left the focus would be proving them, not the
		//wiring. The focus is taken back here, and the caller asserts that it is
		//still out before it presses: no request is outstanding when the Esc comes.
		Panel renderer = window.GetControl<Panel>("RendererPanel");
		window.FocusManager?.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
		Dispatcher.UIThread.RunJobs();
		Assert.False(renderer.IsKeyboardFocusWithin, "the arrangement left the keyboard inside the renderer: the start-up focus request is still holding it");
		return (window, model, renderer);
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

	[AvaloniaFact]
	public void Esc_opens_the_pause_sheet_from_the_game_views_own_focus_state()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, Panel renderer) = ShowRunningGame();

		//The premise, stated rather than assumed: W-P4 is not up, and nothing has
		//asked for the focus since the arrangement above - the renderer is there,
		//running, and the keyboard is not inside it. This is the state the press
		//below is made from, and the state the player's Esc arrives in.
		Assert.False(window.IsPauseCardActive());
		Assert.False(renderer.IsKeyboardFocusWithin, "a focus request landed before the Esc: the press below would be testing that request");

		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		WaitFor(() => model.IsGamePaused, "Esc never reached the Play overlay router: the game was not paused (#1080)");
		WaitFor(() => window.IsPauseCardActive(), "Esc did not put W-P4 on screen (#1080)");
		Assert.True(model.IsPlayerOverlayVisible);
	}

	[AvaloniaFact]
	public void A_second_esc_resumes_and_closes_the_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, _) = ShowRunningGame();

		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
		WaitFor(() => model.IsGamePaused && window.IsPauseCardActive(), "the first Esc did not open W-P4 (#1080)");
		//Released in between: a press is a down and an up, and the window answers
		//a key it has not seen released as the press still being held (below).
		window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		//The router is still the one Esc answers to, and Esc still means one
		//thing: the second press is the way back to the game.
		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
		WaitFor(() => !EmuApi.IsPaused(), "the second Esc did not resume the game (#1080)");
		Assert.False(model.IsPlayerOverlayVisible);
	}

	//#1080: the OS repeats a key the player holds, and every repeat arrives as
	//another KeyDown of the same key with no KeyUp between. TogglePlayerOverlay has
	//no repeat guard of its own, so answering each down toggles the overlay at the
	//repeat rate: a held Esc opens W-P4 and closes it again, and in Settings it
	//closes the sheet and opens the overlay. Only the first down of a key is a
	//press.
	[AvaloniaFact]
	public void Esc_is_answered_once_while_the_key_is_held()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, _) = ShowRunningGame();

		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
		WaitFor(() => model.IsGamePaused && window.IsPauseCardActive(), "the first Esc did not open W-P4 (#1080)");

		//No release in between: this is the auto-repeat of the key still held.
		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.IsPauseCardActive(), "the auto-repeat of a held Esc closed W-P4 (#1080): the overlay toggles at the repeat rate");
		Assert.True(model.IsGamePaused, "the auto-repeat of a held Esc resumed the game (#1080)");

		//Letting go is what makes the next down a press again.
		window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
		WaitFor(() => !EmuApi.IsPaused(), "Esc after the key was released did not resume the game (#1080)");
		Assert.False(model.IsPlayerOverlayVisible);
	}

	//#1080: reopening found the window's Esc arm running before the menu check
	//ProcessFullscreenShortcut applies, so a press made while the keyboard was in a
	//menu opened W-P4 behind it instead of reaching the menu - and the arm marked
	//the press handled, so the menu never saw the key it closes on.
	[AvaloniaFact]
	public void Esc_while_a_menu_has_the_keyboard_is_not_the_overlays_press()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, _) = ShowRunningGame();

		//The shell bar is on screen with W-P4 (WorkspaceShell.IsBarVisible), and
		//its Tools ⋯ is the menu a player can have open over a paused game.
		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
		WaitFor(() => window.IsPauseCardActive(), "the first Esc did not open W-P4 (#1080)");
		window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		Menu tools = window.FindNamed<Menu>("ToolsMenu");
		MenuItem button = tools.GetRealizedContainers().OfType<MenuItem>().First(m => m.Name == "ToolsMenuButton");
		button.Open();
		button.Focus();
		Dispatcher.UIThread.RunJobs();

		//The premise, and the window's own answer to it: _focusInMenu is what its
		//poll (TimerUpdateBackgroundFlag) writes, and the poll runs on a timer, so
		//it is waited for rather than assumed.
		WaitFor(() => WindowSaysTheKeyboardIsInAMenu(window), "the window never saw the keyboard move into the Tools menu");

		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		//The press was the menu's, and the overlay never moved on it. This is the
		//defect: the arm answered a press whose keyboard was not the game's, so
		//W-P4 went away behind the menu that had asked for the key.
		Assert.True(window.IsPauseCardActive(), "Esc with a menu open closed W-P4 (#1080): the overlay answered a press that was the menu's");
		Assert.True(model.IsGamePaused, "Esc with a menu open resumed the game (#1080)");
	}

	//#1080: same guard, the other half - a focused text input owns its keys, and
	//the overlay's arm must not swallow them. The tool sheet's barcode field is the
	//case that puts a text box in front of the player over a running game: the
	//sheet focuses it on open (PlayFocusOnOpen), so this is the state a press
	//arrives in.
	[AvaloniaFact]
	public void Esc_while_a_text_box_has_the_keyboard_is_not_the_overlays_press()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, _) = ShowRunningGame();

		new ShortcutHandler(window).InputBarcode();
		Dispatcher.UIThread.RunJobs();
		TextBox box = window.FindNamed<TextBox>("ToolSheetBarcode");
		Assert.True(model.ToolSheet.IsVisible && box.IsOnScreen(), "the arrangement did not put the barcode sheet's field on screen");
		box.Focus();
		Dispatcher.UIThread.RunJobs();
		Assert.True(box.IsFocused, "the arrangement did not put the keyboard in the text box");
		Assert.False(window.IsPauseCardActive());

		//Read where the press arrives at the focused control itself, which is the
		//claim: the box gets its key, and the window has not already answered it.
		bool theBoxSawThePress = false;
		bool takenBeforeTheBox = true;
		box.AddHandler(InputElement.KeyDownEvent, (_, e) => { theBoxSawThePress = true; takenBeforeTheBox = e.Handled; }, RoutingStrategies.Bubble, true);
		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		Assert.True(theBoxSawThePress, "the press never reached the focused text box");
		Assert.False(takenBeforeTheBox, "the window took Esc before the text box it was focused on could (#1080)");
		Assert.False(window.IsPauseCardActive(), "Esc with a text box focused opened W-P4 behind it (#1080)");
		Assert.False(model.IsPlayerOverlayVisible);
	}

	//The window's own view of the keyboard, read off the field its poll writes
	//(TimerUpdateBackgroundFlag). A real menu cannot be driven far enough here to
	//observe it any other way: once its popup is up the popup is its own top level,
	//and the press below is sent to the window, which is the arm under test.
	private static bool WindowSaysTheKeyboardIsInAMenu(MainWindow window)
	{
		return (bool)typeof(MainWindow).GetField("_focusInMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
	}

	//#1080: Esc by name is the bug - the overlay's key is the binding's, so moving
	//the binding to F1 moves the key with it, and F1 must open W-P4.
	[AvaloniaFact]
	public void The_overlay_rebound_to_F1_opens_on_F1()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ArrangeOverlayKey(Key.F1, onlyWhenUnbound: false);
		(MainWindow window, MainWindowViewModel model, _) = ShowRunningGame();

		window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		WaitFor(() => window.IsPauseCardActive(), "F1 did not open W-P4 after the overlay was rebound to it (#1080)");
		Assert.True(model.IsPlayerOverlayVisible);
	}

	//...and Esc is then nobody's here: the press goes on to the core's own shortcut
	//path. On macOS that path is the native key monitor (this window's arm returns
	//before InputApi.SetKeyState there by design), so what a headless run can show
	//is the half the window owns - the press is neither taken nor marked handled.
	[AvaloniaFact]
	public void Esc_is_left_to_the_core_while_the_overlay_is_bound_to_F1()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ArrangeOverlayKey(Key.F1, onlyWhenUnbound: false);
		(MainWindow window, MainWindowViewModel model, _) = ShowRunningGame();

		bool handled = false;
		window.AddHandler(InputElement.KeyDownEvent, (_, e) => handled = e.Handled, RoutingStrategies.Bubble, true);

		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		Assert.False(handled, "the window took Esc while the overlay was bound to F1 (#1080)");
		Assert.False(model.IsPlayerOverlayVisible);
		Assert.False(window.IsPauseCardActive());
	}

	//#1080: the default binding is bare Esc, so a press carrying Ctrl is a different
	//press - the modified Esc the player bound to something else must not be
	//swallowed by the overlay's arm.
	[AvaloniaFact]
	public void Ctrl_Esc_is_not_the_overlays_press()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model, _) = ShowRunningGame();

		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.Control);
		Dispatcher.UIThread.RunJobs();

		Assert.False(model.IsPlayerOverlayVisible, "Ctrl+Esc was swallowed by the overlay's arm (#1080)");
		Assert.False(window.IsPauseCardActive());
	}

}
