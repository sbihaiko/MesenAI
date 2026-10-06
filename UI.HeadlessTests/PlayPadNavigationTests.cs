using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Controls;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0256 (accepted 2026-10-04), the host half of Decisions 2 and 3, plus the
//held repeat the slice was dispatched with: the pad drives the Play GUI, one
//surface holds the focus at a time, and a held D-pad repeats. The rules are
//pinned host-free in UI.Tests/Play (PadInHand, PadNaming,
//PadNavControls, PlayPadNavigation, PadNavRepeat); this checks the realized
//window feeds them and applies what they answer.
//
//Two steps cannot be real here, and TickForTest hands both in: the press
//(MacOSKeyManager::GetPressedKeys reads a pad's buttons off a live GCController
//and SetKeyState ignores any code above the keyboard's range, so a headless test
//cannot make a pad look pressed), and the backend's two lookups (a headless build
//has no key manager at all - see the stand-in table below). Everything above
//them is the production path: PadInHand, PadNaming, PadNavControls.Resolve, the
//authority rule, the repeat's timing, and the focus application. The one test
//that reads the live backend instead skips here, and says why.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class PlayPadNavigationTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-0256-" + Guid.NewGuid().ToString("N"));

	//The windows this case opened, closed in Dispose (#838): a window that outlives
	//its case is a second top level, and the focus the next case waits for is asked
	//of whichever top level the focus manager answers for. Every window this class
	//shows is registered by the two fixtures below, so no case can forget one.
	private readonly List<MainWindow> _windows = new();

	//The backend this suite does not have. InitializeEmu registers a key manager
	//only when the window and the viewer both hand it a platform handle, and a
	//headless window has neither, so KeyManager's null check answers "" for every
	//code and 0 for every name - nothing in the window can resolve a pad. These
	//two stand in for it, built the way the real backends build their tables
	//(MacOSKeyManager, WindowsKeyManager: "Pad{N} {Button}", N 1-based, the
	//buttons in this order at the family's base + (N-1)*0x100). The DirectInput
	//family is not here: only Windows has it, and its names are pinned host-free
	//in UI.Tests/Play/PadNamingTests.
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

	private static string BackendName(ushort keyCode)
	{
		return Backend.TryGetValue(keyCode, out string? name) ? name : "";
	}

	private static ushort BackendCode(string name)
	{
		return BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;
	}

	//The pad in hand for every test below: device 0 of the XInput-shaped family,
	//which is the block every backend names "Pad{N} " - macOS and Linux name
	//every pad that way, and Windows names its XInput pads so too - resolved
	//through the same PadNavControls.Resolve the window calls.
	private PadNavMapping? _mapping;
	private PadNavMapping Mapping => _mapping ??= PadNavControls.Resolve(PadFamily.Xbox, 0, BackendCode)
		?? throw new InvalidOperationException("the stand-in table does not answer the Xbox preset's names");

	private static PadNavAction[] Directions => new[] { PadNavAction.Up, PadNavAction.Down, PadNavAction.Left, PadNavAction.Right };

	public PlayPadNavigationTests()
	{
		Directory.CreateDirectory(_folder);

		//#790: the core is process-global and a case that ran a game leaves its
		//console loaded, so EmuApi.IsRunning() was still true when the next case
		//started - which is this bridge's authority input, so a leaked game would
		//silently turn the pad off in a case that never loaded one.
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitUntilStopped();
		}
	}

	private static void WaitUntilStopped()
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
			Thread.Sleep(10);
		}
		Assert.False(EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
	}

	public void Dispose()
	{
		//#838: the case's own windows go first, and they are the leak that made this
		//class's failures order-dependent. A window that outlives its case is a
		//second top level for the next case, and the slot grid's focus is asked of
		//whichever top level the focus manager answers for: the grid opened, stayed
		//visible and never became the focused element (its case timed out saying
		//"focus=Panel#RendererPanel; grid: visible=True effectively=False"), while
		//the case passed on its own. Measured on main 383238501: 1 failed / 13
		//passed / 1 skipped with the window left open, 14 passed / 1 skipped with it
		//closed - and the same suite reported a *different* case red on every
		//ordering, which is what a leak between cases looks like.
		//
		//Closing runs MainWindow's exit path, which releases the process-global core
		//(EmuApi.Release cannot be undone in one process), so ReleaseCore is set
		//first: MainWindow.axaml.cs names the hook for exactly this, and the next
		//case still needs the core. EmuApi.Stop still runs, so a game this case
		//loaded is stopped here rather than by the next case's constructor.
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();

		//The seeded recents live in the app's real folder, not in this class's temp
		//folder, so they are cleared here rather than left for the next case.
		ClearRecents();

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Save();
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus (MainMenuViewModel.Initialize).");
		return (window, model);
	}

	//The other door: Advanced keeps the classic look and the classic StateGrid
	//(PlayHomeView's plain grid) as its game-selection and Save/Load screens. The
	//bridge is attached in every window, so its Back edge has to be told which
	//door it is in.
	private (MainWindow Window, MainWindowViewModel Model) ShowAdvanced()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Advanced;
		prefs.Workspace = Workspace.Classic;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus (MainMenuViewModel.Initialize).");
		return (window, model);
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		WaitFor(condition, () => failure);
	}

	//The same, with the message built only when it is needed. A diagnostic that
	//measures the window has to be lazy: DescribeResume focuses the button it is
	//describing, so a message interpolated at the call site would take the focus
	//the wait is about to look for - and the case would pass for the wrong
	//reason (measured: it did).
	private static void WaitFor(Func<bool> condition, Func<string> failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure());
			}
			Pump();
			Thread.Sleep(20);
		}
		Pump();
	}

	//#707: Avalonia's dispatcher promotes a due DispatcherTimer from the OS timer
	//callback, which only the main loop runs - never a test body. The empty job
	//stands in for that wake-up, so the window's real poll (PlayPadNavigationWiring's
	//50 ms timer, PlayEdgeFlowsWiring's) behaves here as it does in the app. The
	//injected ticks below never pump between them, so a feed sequence is exact.
	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	private static void Click(MainWindow window, string button)
	{
		window.FindNamed<Button>(button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Pump();
	}

	private static string? FocusedName(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.Name;
	}

	private static bool GridHasFocus(MainWindow window)
	{
		Visual? focused = window.FocusManager?.GetFocusedElement() as Visual;
		return focused is StateGrid || (focused is not null && focused.GetVisualAncestors().OfType<StateGrid>().Any());
	}

	//What holds the focus now, and what the content area looks like, for a
	//failure message that says where it went instead of only that it went
	//somewhere else.
	private static string Focused(MainWindow window, MainWindowViewModel model)
	{
		Visual? focused = window.FocusManager?.GetFocusedElement() as Visual;
		StateGrid? grid = window.FindDescendantOfType<StateGrid>();
		return $"{Describe(focused)}; content: Visible={model.RecentGames.Visible} Mode={model.RecentGames.Mode} "
			+ $"playWorkspace={model.IsPlayWorkspace} surfaceOverGame={model.IsPlaySurfaceOverGame}; "
			+ $"grid: {(grid is null ? "<not in the tree>" : $"visible={grid.IsVisible} effectively={grid.IsEffectivelyVisible} attached={grid.IsAttachedToVisualTree()} focusable={grid.Focusable}")}";
	}

	private static string Describe(Visual? focused)
	{
		return focused is null ? "focus=<none>" : $"focus={focused.GetType().Name}#{((focused as Control)?.Name ?? "")}";
	}

	//The overlay's Resume button, measured rather than assumed: when the focus
	//does not come back to it, the question is always whether the arbiter picked
	//the wrong surface or called Focus() on a control that could not take it.
	//The last field answers that one directly - a focus that succeeds here and
	//not in the bridge means the bridge asked at a moment the button was not
	//ready, and one that fails here means the button itself was not focusable.
	private static string DescribeResume(MainWindow window)
	{
		return window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "OverlayResumeButton") is not Button resume
			? "resume: <not in the tree>"
			: $"resume: visible={resume.IsEffectivelyVisible} enabled={resume.IsEffectivelyEnabled} focusable={resume.Focusable} attached={resume.IsAttachedToVisualTree()} windowActive={window.IsActive} manual={resume.Focus(NavigationMethod.Directional)}";
	}

	//One tick of the bridge with this code down. The delta is the real poll's
	//(50 ms) unless a test is reasoning about the repeat's numbers.
	private void Feed(MainWindow window, PadNavAction action, int milliseconds = 50)
	{
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(Mapping, action) }, TimeSpan.FromMilliseconds(milliseconds), BackendName, BackendCode);
	}

	//The release: the same tick with nothing down, which is what makes the next
	//press a new edge (the bridge records the pressed set every tick).
	private static void Release(MainWindow window, int milliseconds = 50)
	{
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(milliseconds), BackendName, BackendCode);
	}

	private void LoadSyntheticGame(MainWindowViewModel model)
	{
		string rom = Path.Combine(_folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");
	}

	//Decision 2's probe, against the live backend - and therefore a test that
	//cannot run in this suite: a headless build has no key manager, so the two
	//lookups it reads are stubs above and there is nothing real to check them
	//against. It skips rather than passing, and it is the only reason the six
	//cases below can run at all. What it pins where a key manager does exist:
	//the codes the pad navigates with are the codes the backend names "Pad1 ...",
	//and on Windows a DirectInput code in the same namespace would be "Joy1 ..."
	//at a base that block arithmetic reads as pad 17.
	//
	//This is a PERMANENT skip, not coverage. The repo runs this suite headless
	//(the Avalonia headless runner is the only host it has), and a headless build
	//never registers a key manager - InitializeEmu does so only when the window
	//and the viewer both hand it a platform handle, which a headless window does
	//not. So the guard below can never pass here and this test cannot be made to
	//execute anywhere the repo runs tests; the rule it would check is pinned
	//host-free (UI.Tests/Play/PadNamingTests and PadNavigationTests' stand-in
	//table). It is kept, not deleted, because it is the one place the live name
	//table is asserted, for whichever environment ever does have a key manager.
	[AvaloniaFact]
	public void The_backend_names_every_navigation_code_for_the_pad_in_hand()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Assert.SkipWhen(InputApi.GetKeyCode("Pad1 Down") == 0,
			"this build has no key manager (a headless window gives InitializeEmu no platform handle), so there is no name table to read it against; PadNaming's rule is pinned host-free in UI.Tests/Play/PadNamingTests.");

		(MainWindow window, _) = ShowPlay();
		PadNavMapping live = PadNavControls.Resolve(PadFamily.Xbox, 0, InputApi.GetKeyCode)
			?? throw new XunitException($"the backend names no Xbox-shaped pad: GetKeyName(0x1009)=\"{InputApi.GetKeyName(0x1009)}\"");

		foreach(PadNavAction action in PadNavControls.Navigation) {
			ushort code = PlayPadNavigation.CodeOf(live, action);
			Assert.NotEqual(0, code);
			Assert.Equal(new PadId(0, PadFamily.Xbox), PadNaming.Of(code, InputApi.GetKeyName));
		}

		//A direction, a confirm and a back are three different buttons, so a
		//mapping that collapsed two of them would be caught here rather than
		//showing up as a press that both moves and activates.
		ushort[] codes = PadNavControls.Navigation.Select(a => PlayPadNavigation.CodeOf(live, a)).ToArray();
		Assert.Equal(codes.Length, codes.Distinct().Count());
	}

	//Decision 2: the pad walks the overlay. Resume holds the focus when W-P4
	//opens (Decision 3), and one press steps one control - down and back up.
	[AvaloniaFact]
	public void A_pad_press_steps_the_focus_on_the_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		model.OpenPauseOverlay();
		WaitFor(() => FocusedName(window) == "OverlayResumeButton", "W-P4 opened without the focus on Resume");

		//A tap: press, release, press again. A held button is Decision 7's case
		//(the repeat), and a press that is still down is not a second press.
		Feed(window, PadNavAction.Down);
		Assert.Equal("OverlaySaveStatesButton", FocusedName(window));

		Release(window);
		Feed(window, PadNavAction.Down);
		Assert.Equal("OverlayPackButton", FocusedName(window));

		Release(window);
		Feed(window, PadNavAction.Up);
		Assert.Equal("OverlaySaveStatesButton", FocusedName(window));
	}

	//Decision 7: the press steps at once - a player tapping the D-pad never
	//waits - then 400 ms of hold owe nothing and one step follows every 100 ms.
	//The numbers are written out here rather than read off PadNavRepeat's
	//constants, so this pins the wired cadence and not the rule's agreement with
	//itself: 7 x 50 ms is still the delay, the 8th tick steps, the 9th does not,
	//the 10th does.
	[AvaloniaFact]
	public void A_held_direction_repeats_after_the_delay_and_every_interval()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		model.OpenPauseOverlay();
		WaitFor(() => FocusedName(window) == "OverlayResumeButton", "W-P4 opened without the focus on Resume");

		Feed(window, PadNavAction.Down);
		Assert.Equal("OverlaySaveStatesButton", FocusedName(window));

		//350 ms into the hold: still inside RepeatDelay.
		for(int i = 0; i < 7; i++) {
			Feed(window, PadNavAction.Down);
		}
		Assert.Equal("OverlaySaveStatesButton", FocusedName(window));

		//400 ms: the first repeat. 450 ms: inside RepeatInterval.
		Feed(window, PadNavAction.Down);
		Assert.Equal("OverlayPackButton", FocusedName(window));

		Feed(window, PadNavAction.Down);
		Assert.Equal("OverlayPackButton", FocusedName(window));

		//500 ms: the next one.
		Feed(window, PadNavAction.Down);
		Assert.Equal("OverlayEnhancementsButton", FocusedName(window));

		//A release stops it dead, however long the button was held before it: no
		//step follows, and the next press is a fresh tap of its own rather than a
		//resumed burst.
		for(int i = 0; i < 10; i++) {
			Release(window);
		}
		Assert.Equal("OverlayEnhancementsButton", FocusedName(window));

		Feed(window, PadNavAction.Down);
		Assert.Equal("OverlayCheatsButton", FocusedName(window));
	}

	//Decision 2: Confirm activates what the focus is on - the ring is the
	//cursor, so what it is drawn around is what A presses. Here the ring is on
	//W-P4's Save states row and A opens that sheet, whose own first control then
	//takes the focus (Decision 3, through the same one path).
	[AvaloniaFact]
	public void Confirm_activates_the_focused_control()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		model.OpenPauseOverlay();
		WaitFor(() => FocusedName(window) == "OverlayResumeButton", "W-P4 opened without the focus on Resume");

		Feed(window, PadNavAction.Down);
		Feed(window, PadNavAction.Confirm);
		WaitFor(() => model.IsSaveStatesSheetVisible, "Confirm on W-P4's Save states row did not open its sheet");

		Assert.True(window.FindNamed<Border>("PlayerSaveStatesSheet").IsOnScreen());
		Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
		//#909: the sheet is a grid of slot rows; with no game there is no row to
		//land on, so its own first control takes the ring (the fallback every
		//surface uses). The case below drives the grid with a game loaded.
		Assert.Equal("SaveStatesReplaysButton", FocusedName(window));
	}

	//#909 (W-P4): the Save states sheet is ONE grid, so what the pad walks is its
	//own rows - the ring opens on the row the rule names (no state yet: the first
	//slot), Right reaches the *Load* beside it, Down the next slot's row, and a
	//Confirm on *Save here* writes that slot over the real core.
	[AvaloniaFact]
	public void The_save_states_grid_is_one_pad_step_per_slot_action()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		LoadSyntheticGame(model);

		model.TogglePlayerOverlay();
		WaitFor(() => FocusedName(window) == "OverlayResumeButton", "W-P4 opened without the focus on Resume");
		Click(window, "OverlaySaveStatesButton");
		WaitFor(() => model.IsSaveStatesSheetVisible, "W-P4's Save states row did not open its sheet");

		//Eleven rows: the ten slots and the auto-save.
		Assert.Equal(11, model.SaveStateSlots.Count);

		//The ring lands on the grid's own focus row, on its first action - the same
		//row the rule answers (a slot with no state reads as slot 1).
		WaitFor(() => FocusedRow(window) == model.FocusSaveStateSlot() && FocusedName(window) == "SlotSaveButton",
			"the grid did not take the ring on its own row");
		int first = model.FocusSaveStateSlot()!.Slot;

		//Right: the *Load* on the same row, one press away.
		Release(window);
		Feed(window, PadNavAction.Right);
		WaitFor(() => FocusedRow(window)?.Slot == first && FocusedName(window) == "SlotLoadButton",
			"the D-pad did not reach the row's own Load");

		//Left, back onto that row's *Save here*, and Confirm: the state lands in the
		//slot the rule opened the sheet on, and that row's own Load is armed.
		Release(window);
		Feed(window, PadNavAction.Left);
		WaitFor(() => FocusedRow(window)?.Slot == first && FocusedName(window) == "SlotSaveButton",
			"the D-pad did not come back to the row's Save here");
		Release(window);
		Feed(window, PadNavAction.Confirm);
		WaitFor(() => model.SaveStateSlot(first)!.HasState, "Confirm on Save here did not write the slot");
		Assert.True(model.SaveStateSlot(first)!.LoadEnabled);
		Assert.True(File.Exists(model.SaveStateSlot(first)!.FileName));
	}

	//The row the ring is on, read off the control's own DataContext - a grid of
	//rows has no per-slot names to look up.
	private static SaveStateSlotViewModel? FocusedRow(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.DataContext as SaveStateSlotViewModel;
	}

	//Decision 2's Back: the same shortcut ADR-0251 gave the pad's chord, so a
	//pad walks ADR-0249's Esc order through the one router that implements it -
	//sheet, then W-P4, then the game - and closing a surface hands the focus back
	//to the one under it (Decision 3, read the other way round).
	[AvaloniaFact]
	public void Back_walks_the_esc_order_and_hands_the_focus_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		model.OpenPauseOverlay();
		WaitFor(() => FocusedName(window) == "OverlayResumeButton", "W-P4 opened without the focus on Resume");
		Feed(window, PadNavAction.Down);
		Feed(window, PadNavAction.Confirm);
		WaitFor(() => model.IsSaveStatesSheetVisible, "Confirm on W-P4's Save states row did not open its sheet");

		//One Back: the sheet closes back to W-P4, which takes the focus again.
		Release(window);
		Feed(window, PadNavAction.Back);
		Pump();
		WaitFor(() => !model.IsSaveStatesSheetVisible && model.IsPlayerOverlayVisible, "Back did not close the sheet back to W-P4");
		WaitFor(() => FocusedName(window) == "OverlayResumeButton",
			() => $"the overlay came back without the focus ({Focused(window, model)}; {DescribeResume(window)})");
	}

	//Decision 1: the pad is also player 1's controller, so it has no authority
	//while a game runs unpaused - the rule owns that test, and the same press
	//that does nothing during play resumes the game once W-P4 is up.
	[AvaloniaFact]
	public void The_pad_has_no_authority_while_a_game_runs_unpaused()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		LoadSyntheticGame(model);

		//Running, unpaused, nothing over it: Back is the game's button.
		Feed(window, PadNavAction.Back);
		Pump();
		Assert.False(model.IsPlayerOverlayVisible, "the pad opened W-P4 while the game ran unpaused (ADR-0256 Decision 1)");
		Assert.False(EmuApi.IsPaused());

		//W-P4 up: the game is paused, the surface is the pad's, and the same
		//press resumes.
		model.TogglePlayerOverlay();
		WaitFor(() => model.IsGamePaused && model.IsPlayerOverlayVisible, "Esc did not open W-P4");
		Release(window);
		Feed(window, PadNavAction.Back);
		Pump();
		WaitFor(() => !EmuApi.IsPaused(), "Back on W-P4 did not resume the game");
		Assert.False(model.IsPlayerOverlayVisible);
	}

	//Decision 3's open choice, taken as "scope the bridge to exclude the grid":
	//StateGrid already moves its own SelectedIndex from the pad, off player 1's
	//port mappings, in its own timer (and that same loop serves Advanced, where
	//no Play mapping exists), so the bridge must not also move the focus there.
	//Back is still the bridge's, because the grid's own loop has no exit and a
	//player who cannot leave the slot grid is the failure ADR-0256 exists to
	//prevent.
	[AvaloniaFact]
	public void The_slot_grid_keeps_the_pad_and_back_still_leaves()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		LoadSyntheticGame(model);

		//#909: W-P4's Save states sheet is its own grid of slots now, so this classic
		//grid is the one the load/save shortcut opens over the game - the door the
		//rule below is about (the grid, not the door, is what must keep the pad).
		model.RecentGames.Init(GameScreenMode.SaveState);
		WaitFor(() => model.RecentGames.Visible, "the save shortcut did not open the slot grid");
		WaitFor(() => GridHasFocus(window), $"the slot grid opened without the focus ({Focused(window, model)})");

		//The D-pad is the grid's: the focus stays where it is.
		foreach(PadNavAction direction in Directions) {
			Feed(window, direction);
			Assert.True(GridHasFocus(window), $"the bridge moved the focus off the slot grid on {direction}");
		}

		//Confirm is the grid's too (its own loop reads the pad's A/B/X/Y/Select/
		//Start as "load this slot"), so the sheet must still be up.
		Release(window);
		Feed(window, PadNavAction.Confirm);
		Pump();
		Assert.True(model.RecentGames.Visible, "Confirm took the slot grid's own load button away from it");
		Assert.False(model.IsPlayerOverlayVisible);

		//Back is not: it leaves, like Esc, back to W-P4.
		Release(window);
		Feed(window, PadNavAction.Back);
		Pump();
		WaitFor(() => !model.RecentGames.Visible && model.IsPlayerOverlayVisible, "Back did not close the slot grid back to W-P4");
		WaitFor(() => FocusedName(window) == "OverlayResumeButton", "W-P4 came back without the focus");
		Assert.True(EmuApi.IsPaused());
	}

	//Decision 3, the case the claims' ORDER exists for: two Play surfaces up at
	//once. Look's Adjust… opens the shader sheet OVER the Settings sheet, which
	//stays open beneath (InWindowSheets.cs), and the arbiter hands the focus to
	//the one Esc would close first - the same precedence TogglePlayerOverlay
	//walks. Closing the shader has to give the focus back to Settings, not to the
	//game: the close re-arbitrates, which is what makes the pair behave like a
	//stack instead of like two surfaces that each grabbed the keyboard once.
	[AvaloniaFact]
	public void The_topmost_surface_holds_the_focus_and_hands_it_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		//Display is the tab Settings opens on in Player mode (the essentials
		//clamp), and the sheet's claim focuses that same strip - so no designer
		//constructor and no dependence on which tab the clamp picks.
		model.OpenPlayerSettings(new ConfigViewModel(ConfigWindowTab.Display, playerMode: true));
		WaitFor(() => FocusedName(window) == "tabPlayerWindow", $"the Settings sheet opened without the focus on its first tab ({Focused(window, model)})");
		Assert.True(model.IsPlayerSettingsVisible);

		model.OpenShaderSheet(new ShaderConfigViewModel(false, ""));
		WaitFor(() => FocusedName(window) == "ShaderSheetOk", $"the shader sheet opened over Settings without the focus ({Focused(window, model)})");

		model.CloseShaderSheet(false);
		WaitFor(() => FocusedName(window) == "tabPlayerWindow", $"closing the shader did not hand the focus back to the Settings sheet underneath ({Focused(window, model)})");
	}

	//Defect 1: a Play surface that does NOT pause the game must not take the pad
	//from the console. The barcode tool sheet is reachable while a game runs
	//unpaused - the Core allows InputBarcode while running and ShortcutHandler
	//opens the sheet - and it never pauses anything. Before the fix the bridge
	//read IsPlaySurfaceOverGame ("something is drawn over the game"), which
	//counts the sheet, so Back closed it out from under the running game while
	//the pad was also the console's.
	[AvaloniaFact]
	public void A_surface_that_does_not_pause_the_game_does_not_take_the_pad()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		LoadSyntheticGame(model);
		Assert.False(EmuApi.IsPaused(), "the game is not running unpaused");

		model.ToolSheet.OpenBarcode();
		Assert.True(model.ToolSheet.IsBarcode, "the barcode tool sheet did not open");
		Assert.True(model.IsPlaySurfaceOverGame, "the tool sheet is not counted as a surface drawn over the game");

		Feed(window, PadNavAction.Back);
		Pump();
		Assert.True(model.ToolSheet.IsBarcode, "the pad took the barcode tool sheet, which never paused the game (ADR-0256 Decision 1)");
		Assert.False(EmuApi.IsPaused(), "the pad paused a game it does not own");
	}

	//Defect 1, the load card: it is a surface over the game but it pauses nothing
	//and has no focusable control of its own, so granting authority while it is up
	//only let a pad Confirm activate whatever the home still held the focus on -
	//a way to launch a game through the card. The predicate must not grant
	//authority while a load is in progress.
	[AvaloniaFact]
	public void The_load_card_does_not_grant_the_pad_authority()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		LoadSyntheticGame(model);
		Assert.False(EmuApi.IsPaused(), "the game is not running unpaused");

		model.BeginLoadWait("Synthetic", keepsHome: true);
		Assert.True(model.IsLoadWaitActive, "the load card is not up");

		Feed(window, PadNavAction.Back);
		Pump();
		Assert.False(model.IsPlayerOverlayVisible, "the pad opened W-P4 over the load card (ADR-0256 Decision 1: the card pauses nothing and has no focusable control)");
	}

	//Defect 2: the Load/Save-state shortcuts open the slot grid directly
	//(ShortcutHandler -> RecentGames.Init(LoadState/SaveState)), never through
	//W-P4, so _stateGridFromOverlay stays false, CurrentPlaySheet() is None and
	//the authority rule answers false. Before the fix the bridge never called
	//Apply, and Back - the grid's only way out from a pad, whose own 50 ms loop
	//has no exit - did nothing: the player was stuck on the grid, the very
	//failure the bridge's grid comment claims to prevent. Back must leave the
	//grid however it was opened.
	[AvaloniaFact]
	public void Back_leaves_a_slot_grid_opened_by_the_shortcut()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		LoadSyntheticGame(model);

		//The emulator shortcut's own path, verbatim.
		model.RecentGames.Init(GameScreenMode.LoadState);
		WaitFor(() => model.RecentGames.Visible, "the Load-state shortcut did not open the slot grid");
		WaitFor(() => GridHasFocus(window), () => $"the slot grid opened without the focus ({Focused(window, model)})");

		Release(window);
		Feed(window, PadNavAction.Back);
		Pump();
		WaitFor(() => !model.RecentGames.Visible, "Back did not leave the slot grid opened by the shortcut (ADR-0256 Decision 2)");
	}

	//Finding 1: the W-P13 BIOS sheet is shown inside EmuApi.LoadRom -
	//LoadRomHelper.BeginLoad runs it after BeginLoadWait (so IsLoadWaitActive is
	//true) and before the console exists (EmuApi.IsRunning false) - and
	//BiosSheetChooseFile is a focusable control the bridge claims. The load card
	//and the BIOS sheet share that load, but the card has no focusable control of
	//its own and the sheet does, so the coarse load flag that used to refuse the
	//whole load must not refuse the sheet. Back is the observable: with authority
	//the pad walks ADR-0249's Esc order and cancels the sheet; without it the
	//press is dropped.
	[AvaloniaFact]
	public void The_bios_sheet_is_the_pads_even_inside_a_load()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		Assert.False(EmuApi.IsRunning(), "this test starts from no game");

		model.BeginLoadWait("Zelda", keepsHome: true);
		Assert.True(model.IsLoadWaitActive, "the load card is not up");
		_ = model.BiosSheet.Request(FirmwareType.Gameboy, "gb_bios.bin", 0x900, 0, "Zelda");
		WaitFor(() => model.BiosSheet.IsVisible, "the BIOS sheet did not open");

		Release(window);
		Feed(window, PadNavAction.Back);
		Pump();
		WaitFor(() => !model.BiosSheet.IsVisible,
			"Back did not cancel the BIOS sheet: the pad has no authority under a load, but the sheet is a no-game surface the ADR says the pad drives (ADR-0256 Decision 2)");
	}

	//Finding 2: W-P5 opened by itself over an un-enhanced first start is up over a
	//game that is NOT paused (EvaluatePlayerPackPicker never calls EmuApi.Pause),
	//so the pause pair alone refuses it. It is the one unpaused surface a first-run
	//cabinet must be able to act on - the pack has to be chosen before the game is
	//played - so the pad drives it, and Back dismisses it (ADR-0249's Esc order).
	[AvaloniaFact]
	public void The_on_load_pack_picker_is_the_pads()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		LoadSyntheticGame(model);
		Assert.False(EmuApi.IsPaused(), "the game is not running unpaused");

		//The on-load open: Player mode, no stored preference, a competing pack -
		//the model path that never pauses (not W-P4's own picker, which sits over
		//W-P4's pause and is already covered by the pause pair).
		model.IsPlayerPackPickerVisible = true;
		Assert.False(model.IsPlayerOverlayVisible, "W-P4 is up: this is not the on-load picker");

		Release(window);
		Feed(window, PadNavAction.Back);
		Pump();
		WaitFor(() => !model.IsPlayerPackPickerVisible,
			"Back did not dismiss the on-load pack picker: a first-run cabinet cannot choose a pack over an unpaused game (ADR-0256 Decision 2)");
	}

	//#848: the pad can walk W-P5's choices. The claim targets a *row*
	//(PackPickerChoice - the stored choice, else the first one), and the search
	//root the arbiter infers is the nearest ancestor that row and the focused
	//control share, walked from the target's own parent: while the focus is on the
	//target that is the row's own item container, holding one row, so a D-pad
	//press had nowhere to go and only the first choice could ever be confirmed.
	//
	//`PlayerPackPickerTests.Keyboard_focus_moves_between_the_pack_choices` is not
	//this case: it presses the arrow *keys*, which go through Avalonia's own XY
	//focus and never reach the bridge, so it could not have caught it.
	[AvaloniaFact]
	public void The_pad_walks_the_pack_choices()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		//Two packs. Set directly, so W-P5's own "No pack" row - which
		//MainWindowViewModel appends when it builds the list - is not among them;
		//this case is about the walk, and two rows are enough to have one.
		model.PlayerPackChoices = new() {
			new PlayerPackChoice(new PackPreferenceResolver.Candidate { Container = "/packs/aaa", Name = "Aaa Pack", PackId = "issue-1", Enabled = true }, null, 0, null),
			new PlayerPackChoice(new PackPreferenceResolver.Candidate { Container = "/packs/bbb", Name = "Bbb Pack", PackId = "issue-2", Enabled = true }, null)
		};
		model.IsPlayerPackPickerVisible = true;
		Pump();
		Assert.True(model.IsPlayerPackPickerVisible, "the picker is not up, so this case would prove nothing");
		WaitFor(() => FocusedChoice(window) == "Aaa Pack",
			() => $"the picker did not put the ring on its first choice ({Focused(window, model)})");

		Release(window);
		Feed(window, PadNavAction.Down);
		Pump();
		WaitFor(() => FocusedChoice(window) == "Bbb Pack",
			() => $"the pad could not leave the first pack choice ({Focused(window, model)})");

		Release(window);
		Feed(window, PadNavAction.Up);
		Pump();
		WaitFor(() => FocusedChoice(window) == "Aaa Pack",
			() => $"the pad could not come back to the first choice ({Focused(window, model)})");
	}

	//The label of the pack row the ring is on: the first text its own row draws.
	private static string? FocusedChoice(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?
			.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
	}

	//Finding 3: the Back-edge branch fires on a closeable grid with no door check,
	//but Advanced (Classic) shows the same classic StateGrid - PlayHomeView's plain
	//grid - as its game-selection and Save/Load screens. The bridge is attached in
	//every window, so without the door gate a pad Back would close an Advanced
	//screen the Play door does not own. The ADR is the Play GUI's; Advanced keeps
	//its own behavior, so the branch is scoped to Play and this pins it.
	[AvaloniaFact]
	public void Back_does_not_close_an_advanced_grid()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowAdvanced();
		Assert.False(model.IsPlayerMode, "this test is not in Player mode");
		Assert.False(model.IsPlayWorkspace && model.IsPlayerMode, "the Play door is unexpectedly the one that is open");

		model.RecentGames.Init(GameScreenMode.LoadState);
		WaitFor(() => model.RecentGames.Visible, "the Advanced slot grid did not open");
		WaitFor(() => GridHasFocus(window), () => $"the Advanced slot grid opened without the focus ({Focused(window, model)})");

		Release(window);
		Feed(window, PadNavAction.Back);
		Pump();
		Assert.True(model.RecentGames.Visible, "the bridge closed an Advanced slot grid: the Back edge must be scoped to the Play door (ADR-0256 Decision 2)");
	}

	//ADR-0256 Decisions 2 and 3: the tool sheet is a Play surface for the pad's
	//authority (IsPlaySurfaceOverGame counts ToolSheet.IsVisible), but its claim
	//opened only for the barcode kind - so About, Command Line, Check for Updates
	//and the video recorder's settings opened with no claim open, and the arbiter
	//put the ring back on the content under the sheet, where a pad Confirm fired
	//the home's action through it. Command Line is the kind reachable here with
	//no game and no network: the door's Help › Command Line
	//(MainMenuViewModel.OpenCommandLineHelp).
	[AvaloniaFact]
	public void A_non_barcode_tool_sheet_takes_the_focus_when_it_opens()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		model.MainMenu.OpenCommandLineHelp(window);
		Pump();
		Assert.True(model.ToolSheet.IsCommandLine, "the Command Line tool sheet never opened");
		Assert.True(window.FindNamed<Border>("ToolSheet").IsOnScreen());

		WaitFor(() => FocusedInside(window, "ToolSheet"),
			() => $"the Command Line tool sheet opened without the focus ({Focused(window, model)})");
		//The sheet's own first focusable control for this kind: the footer's
		//Done. The tool sheet is a DockPanel whose footer is declared before its
		//content, so Done is what the sheet's own focus order reaches first - and
		//the ring lands on it rather than on the content under the sheet.
		Assert.Equal("ToolSheetDone", FocusedName(window));
	}

	//True when the focus is on a control inside the named surface - the sheet's
	//own tree, and nowhere else.
	private static bool FocusedInside(MainWindow window, string surface)
	{
		return window.FocusManager?.GetFocusedElement() is Visual focused
			&& focused.GetVisualAncestors().OfType<Control>().Any(c => c.Name == surface);
	}

	//The home that has recents, which is the screen a real report was made
	//against (2026-10-05: "nao consigo controlar a janela usando o joystick",
	//with a screenshot of this layout - Continue card, Recent row, "No game
	//loaded"). Every case in this file either puts a surface over the home or
	//loads a game first, so none of them asks whether the pad moves the ring on
	//the home itself: the first-run home is a one-control screen (rule 2) and
	//there is nothing there to move *to*, while this one has three focusables
	//and a traversal to make.
	[AvaloniaFact]
	public void The_pad_walks_the_home_that_has_recents()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		SeedRecents("Contra", "Zelda", "Metroid");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();

		Assert.True(model.RecentGames.ShowRecentsHome, "the seeded recents did not put the home in its recents layout");
		Assert.False(EmuApi.IsRunning(), "this test is about the home with no game loaded");
		WaitFor(() => FocusedName(window) == "PlayHomeContinueButton",
			() => $"the home opened without its primary action focused ({Focused(window, model)})");
		Release(window);

		//Down is the walk this layout is built for: Continue sits at the left of the
		//card, above the row of tiles, and the grid keeps the D-pad for its own
		//selection (ADR-0256 Decision 3) - so the ring lands in the grid and stays
		//there for the left/right presses.
		Feed(window, PadNavAction.Down);
		Assert.True(GridHasFocus(window), $"Down off the Continue card did not reach the row of tiles ({Focused(window, model)})");
		Release(window);

		//Up is the press that has to come back, and #896 is why it is asserted and
		//not assumed: the row is *one* row, so it has nothing above it and the grid
		//has nothing to do with this press - but the bridge refused every direction
		//while a grid held the focus, and this grid's Back has no close box to leave
		//by. One D-pad Down therefore left the player unable to reach the card, or
		//anything on it, again, on the screen a cabinet boots into.
		Feed(window, PadNavAction.Up);
		Assert.True(FocusedName(window) == "PlayHomeContinueButton",
			$"Up did not bring the ring back to the card (now {FocusedName(window) ?? "<nothing>"}; {Focused(window, model)})");
	}

	//The recents the home reads are `.rgd` files in the app's own folder, and the
	//first-run/recents split is decided by how many of them there are (PlayHome's
	//own rule), so seeding them is what reaches this layout. An empty file is
	//enough - the entry only has to exist - and the timestamps are written newest
	//first so the newest game is the Continue card and the rest are the row.
	private static void SeedRecents(params string[] games)
	{
		string folder = ConfigManager.RecentGamesFolder;
		Directory.CreateDirectory(folder);
		foreach(string stale in Directory.GetFiles(folder, "*.rgd")) {
			File.Delete(stale);
		}
		DateTime written = DateTime.Now;
		foreach(string game in games) {
			string file = Path.Combine(folder, game + ".rgd");
			File.WriteAllText(file, "");
			File.SetLastWriteTime(file, written);
			written = written.AddMinutes(-1);
		}
	}

	//The seeded entries are in the app's real recents folder, not in this class's
	//temp folder, so they outlive the case unless they are removed here - and a
	//stray one would put the *next* case's home in the recents layout.
	private static void ClearRecents()
	{
		string folder = ConfigManager.RecentGamesFolder;
		if(!Directory.Exists(folder)) {
			return;
		}
		foreach(string file in Directory.GetFiles(folder, "*.rgd")) {
			File.Delete(file);
		}
	}
}
