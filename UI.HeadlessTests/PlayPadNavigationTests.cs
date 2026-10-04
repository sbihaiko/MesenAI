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

	private static (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
		window.ShowStarted();
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
		Assert.Equal("SaveStatesSaveButton", FocusedName(window));
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

		model.TogglePlayerOverlay();
		WaitFor(() => model.IsGamePaused, "Esc did not open W-P4");
		Click(window, "OverlaySaveStatesButton");
		Click(window, "SaveStatesSaveButton");
		WaitFor(() => model.RecentGames.Visible, "the Save states sheet's Save did not open the slot grid");
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
}
