using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0255 slice 1 (PRD Part B §13.5.2 W-P17): the Play Controller sheet, which
//is where Play › Settings › Controls › More in Options… lands now - over the
//paused game, instead of the classic Input window. The map from the pad's
//buttons to the drawn keys is host-free (UI.Tests/Play/ControllerSheetTests);
//what only a window can show is the crossing: the row reaches the sheet and a
//sheet - not a window - lands on screen, Esc and Done go back to W-P4, the
//sheet's own link still reaches the classic Input page (remapping lives there
//until slice 3), and the reads stay scoped to a visible sheet over a paused game
//(ADR-0255 Consequences).
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests - except
//ControllerSheetPadTests at the bottom, which reads no device at all.
//
//Provenance, stated plainly: the slice-1 wiring tests above (open/close, Esc,
//Done, the classic link, the poll scoping) were written alongside that
//implementation, so they characterize the wiring as built rather than a contract
//agreed first. The slice-2 findings' tests below (the assignment move, the
//keyboard gate, the device identity, the rebuild and the note) were written
//red-first against the WIP diff - their failing output is in the slice's review.
[Collection(NativeCoreCollection.Name)]
public class PlayerControllerSheetTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private bool _wasPaused;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		//Opening the sheet pauses the game, because the reads need it (ADR-0255),
		//and no test here loads a ROM to clear that flag again: a later test that
		//loads one would inherit the pause. Put the core back as it was found.
		if(NativeCore.IsAvailable && !_wasPaused) {
			EmuApi.Resume();
		}
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowPlayWithGame()
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		_wasPaused = EmuApi.IsPaused();
		//A loaded game as far as the sheets are concerned (Esc's order needs one).
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	private static void Click(Control control)
	{
		control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	//The arbiter posts its decision at Loaded priority and the sheet's controls
	//only take their place in a later layout pass, so the focus lands a turn or
	//two after the sheet opens. This pumps until `done` holds, and returns the
	//last focus it saw otherwise - which is what the red run of the test above
	//reports.
	private static Control? WaitForFocus(MainWindow window, Func<Control?, bool> done, int turns = 60)
	{
		Control? focused = null;
		for(int i = 0; i < turns; i++) {
			Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();
			focused = window.FocusManager?.GetFocusedElement() as Control;
			if(done(focused)) {
				break;
			}
		}
		return focused;
	}

	//W-P4 › Settings (W-P8), on Controls, then its link.
	private static void OpenFromSettings(MainWindow window, MainWindowViewModel model)
	{
		Click(window.FindNamed<Button>("OverlaySettingsButton"));
		Assert.True(model.IsPlayerSettingsVisible);
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Input);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Input), model.PlayerSettings!.PlayerTabIndex);
		Click(window.FindNamed<Button>("btnPlayerSettingsMoreInOptions"));
		//The sheet's keys are ItemsControl items: they exist once a layout pass has
		//built the containers.
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
	}

	//The drawn keys are placed on the ItemsControl's Canvas by its containers, so
	//where a key is drawn is its container's attached Left/Top (the style W-P15
	//writes has the same shape) - and the pair is what names the key here.
	private static Border[] Keys(Visual root) => root.FindAll<Border>().Where(b => b.Name == "ControllerSheetKey").ToArray();

	private static (double Left, double Top) At(Border key)
	{
		Control container = (Control)key.Parent!;
		return (Canvas.GetLeft(container), Canvas.GetTop(container));
	}

	private static DispatcherTimer? Poll(ControllerSheetViewModel sheet)
	{
		return (DispatcherTimer?)typeof(ControllerSheetViewModel)
			.GetField("_poll", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(sheet);
	}

	//The REMAP rows as the player sees them: the ItemsControl's realized containers,
	//which is what a view-model-level assertion cannot tell apart from a stale list.
	private static Button[] RemapRows(Visual root)
	{
		return root.FindAll<Button>().Where(b => b.Name == "ControllerSheetRemapRow").ToArray();
	}

	//A gamepad key as the key manager numbers it (0x1000 + device*0x100 + button):
	//what "which pad" is read from, since a port is only a ControllerConfig.
	private static ushort PadButton(int device, int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);

	private static Button[] PlayerRows(Visual root) => root.FindAll<Button>().Where(b => b.Name == "ControllerSheetPlayerRow").ToArray();

	//One of a REMAP row's two lights, by the row's control label (the row's own
	//DataContext), so the class on the drawn dot is what is asserted and not only
	//the view-model's answer.
	private static Border LightOf(Visual root, string name, string label)
	{
		return root.FindAll<Border>().Single(b => b.Name == name && b.DataContext is ControllerSheetRemapRow row && row.Label == label);
	}

	//The sheet writes the global config through the same ConfigManager path the
	//classic Input page uses; a test that seeds a port must put it back.
	private static NesConfig SavedNes() => ConfigManager.Config.Nes.Clone();

	[AvaloniaFact]
	public void Controls_more_in_options_opens_the_play_sheet_over_the_paused_game()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		OpenFromSettings(window, model);

		//The Settings sheet closed and the Controller sheet took its place: one
		//Play sheet is current at a time, or Esc would go to the wrong one.
		Assert.False(model.IsPlayerSettingsVisible);
		Assert.True(model.ControllerSheet.IsVisible);
		Assert.False(model.IsPlayerOverlayVisible);
		Assert.True(window.FindNamed<Border>("PlayerControllerSheet").IsOnScreen());
		//No window: the whole point of the slice. The classic Options is shown
		//unowned, so window.OwnedWindows would not see it either way.
		Assert.Null(model.MainMenu.OptionsWindow);

		//The picture is W-P15's, drawn from ControllerPadLayout: exactly its ten
		//keys, each at the coordinates that layout gives it.
		Assert.True(window.FindNamed<Border>("ControllerSheetPad").IsOnScreen());
		(int Left, int Top)[] expected = ControllerLivePad.Keys.Select(b => ControllerPadLayout.Of(b)).Select(k => ((int)k.Left, (int)k.Top)).OrderBy(p => p).ToArray();
		(int Left, int Top)[] drawn = Keys(window).Select(k => { (double l, double t) = At(k); return ((int)l, (int)t); }).OrderBy(p => p).ToArray();
		Assert.Equal(expected, drawn);

		//The reads need a paused game (ADR-0255), so opening it over one paused a
		//game that was not: the sheet never resumes on its own.
		Assert.True(EmuApi.IsPaused());
		//Nothing here has a pad to read: the sheet says so instead of drawing a
		//pad it made up.
		Assert.True(window.FindNamed<TextBlock>("ControllerSheetNoPad").IsOnScreen());
		Assert.False(window.FindNamed<Border>("ControllerSheetPadValues").IsOnScreen());
		//And the classic link is where the not-yet-here parts live.
		Assert.True(window.FindNamed<Button>("ControllerSheetMoreInOptions").IsOnScreen());
	}

	[AvaloniaFact]
	public void Esc_closes_the_controller_sheet_back_to_w_p4()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		Assert.True(model.ControllerSheet.IsVisible);

		//The house Esc order, not a second one: Esc on a sheet from W-P4 closes it
		//and comes back to W-P4.
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();

		Assert.False(model.ControllerSheet.IsVisible);
		Assert.False(window.FindNamed<Border>("PlayerControllerSheet").IsOnScreen());
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
		Assert.True(model.IsPlayerOverlayVisible);
	}

	[AvaloniaFact]
	public void Done_closes_the_controller_sheet_back_to_w_p4()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);

		Click(window.FindNamed<Button>("ControllerSheetDone"));

		Assert.False(model.ControllerSheet.IsVisible);
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	//ADR-0256 Decision 3: one place decides who holds the focus when a Play
	//surface opens, and the Controller sheet is one of the surfaces that path has
	//to drive (the ADR's Consequences name it). The claim table had no entry for
	//this sheet, so opening it left no claim open: the arbiter fell back to the
	//home underneath - the focus ring drawn on the surface *under* the sheet, and
	//the pad's Confirm firing the home's action through it. This is the sheet the
	//claim is for: Done is its own control, always on screen and focusable
	//whatever the sheet is showing (the pad picker and the PLAYERS rows only
	//exist with two or more pads; with one or with none the sheet's focusables
	//are its two footer buttons), and it is the safe target - a Confirm on it
	//closes the sheet back to W-P4, where the row's other button, More in
	//Options…, leaves for the classic Input window, which ADR-0256's non-goals
	//say a pad cannot drive.
	[AvaloniaFact]
	public void The_controller_sheet_takes_the_focus_when_it_opens()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		Assert.True(model.ControllerSheet.IsVisible);

		Control? focused = WaitForFocus(window, control => control?.Name == "ControllerSheetDone");

		Assert.Equal("ControllerSheetDone", focused?.Name);
		//Inside the sheet, not the home under it - which is where it landed before
		//the claim existed (the defect).
		Assert.Contains(window.FindNamed<Border>("PlayerControllerSheet"),
			Assert.IsAssignableFrom<Control>(focused).GetVisualAncestors().OfType<Border>());
	}

	//Slice 3 (remapping) is not here yet, so the classic page stays the way there
	//from inside the sheet - and slice 1 is done when that link is the only door
	//left to it.
	[AvaloniaFact]
	public void The_sheets_own_link_reaches_the_classic_input_page()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);

		try {
			Click(window.FindNamed<Button>("ControllerSheetMoreInOptions"));

			ConfigWindow options = Assert.IsType<ConfigWindow>(model.MainMenu.OptionsWindow);
			Assert.Equal(ConfigWindowTab.Input, Assert.IsType<ConfigViewModel>(options.DataContext).SelectedIndex);
			//The sheet is gone: the window is over the game now, not over a sheet.
			Assert.False(model.ControllerSheet.IsVisible);
		} finally {
			model.MainMenu.OptionsWindow?.Close();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//With no game there is no W-P4 to come back to and nothing for the sheet to
	//read, so the classic Input page stays that landing - and it is reachable: a
	//task door's Settings… (ADR-0250) can be up with no game at all.
	[AvaloniaFact]
	public void Without_a_game_the_link_still_reaches_the_classic_input_page()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		_wasPaused = EmuApi.IsPaused();
		window.OpenPlayerSettingsSheet();
		Dispatcher.UIThread.RunJobs();

		try {
			//No ROM: the sheet's own reason for refusing is the model's (a game is
			//what its Done and Esc would come back to).
			Assert.Equal(RomFormat.Unknown, model.RomInfo.Format);
			window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Input);
			Dispatcher.UIThread.RunJobs();
			Click(window.FindNamed<Button>("btnPlayerSettingsMoreInOptions"));

			//Posted by the window: the Settings sheet goes and the classic page
			//opens on Controls, as it did before this slice.
			Assert.False(model.IsPlayerSettingsVisible);
			Assert.False(model.ControllerSheet.IsVisible);
			ConfigWindow options = Assert.IsType<ConfigWindow>(model.MainMenu.OptionsWindow);
			Assert.Equal(ConfigWindowTab.Input, Assert.IsType<ConfigViewModel>(options.DataContext).SelectedIndex);
		} finally {
			model.MainMenu.OptionsWindow?.Close();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//The pad on screen is the tester's own readout: the same items, the same
	//chips, the same stick lines - and each of W-P15's keys lights from the button
	//the core reports for it. The pad is injected because the real source needs a
	//physical pad (as GamepadTestTabTests does); with the poll stopped, the sheet
	//holds it.
	[AvaloniaFact]
	public void The_sheet_draws_the_pad_it_reads_and_lights_the_key_of_a_pressed_button()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;

		//The poll reads the host (no pad on this machine) and would trim the
		//injected one away on its next tick.
		Poll(sheet)!.Stop();

		GamepadTestItem pad = new(0) {
			Name = "Wireless Controller",
			Backend = GamepadBackend.GameController,
			InfoText = "SDL · Pad1 · VID:054C · PID:0CE6",
			LeftStickReadout = "X: 128  Y: -64  mag 45%",
			RightX = -32,
			RightY = 200
		};
		pad.Buttons[ControllerLivePad.BitOf(SetupButton.A, GamepadBackend.GameController)!.Value].IsPressed = true;
		sheet.Tester.Gamepads.Add(pad);
		sheet.ApplyPad();
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();

		Assert.True(sheet.HasPad);
		Assert.Equal("Wireless Controller", window.FindNamed<TextBlock>("ControllerSheetPadName").Text);
		Assert.Equal("SDL · Pad1 · VID:054C · PID:0CE6", window.FindNamed<TextBlock>("ControllerSheetPadInfo").Text);
		Assert.False(window.FindNamed<TextBlock>("ControllerSheetNoPad").IsOnScreen());
		//One pad: no picker to choose between, no PLAYERS to assign (ADR-0255
		//slice 2 - there is nothing to assign).
		Assert.False(window.FindNamed<ListBox>("ControllerSheetPadPicker").IsOnScreen());
		Assert.False(window.FindNamed<StackPanel>("ControllerSheetPlayers").IsOnScreen());

		//The drawn keys are the tester's, not a second model: the pad's own buttons
		//are the chips below, and one pressed button lights exactly one key - the
		//one ControllerLivePad says that button is.
		Border lit = Assert.Single(Keys(window), k => k.Classes.Contains("lit"));
		PadKey a = ControllerPadLayout.Of(SetupButton.A);
		Assert.Equal(((int)a.Left, (int)a.Top), ((int)At(lit).Left, (int)At(lit).Top));
		Assert.Contains("round", lit.Classes);
		//A key the pad has no button for never lights, and the values keep saying
		//what the pad reports: the chips are the tester's own items, one per core
		//button, and the pressed one carries the tester's own green.
		ItemsControl chips = window.FindNamed<ItemsControl>("ControllerSheetButtons");
		Border[] chipCells = chips.FindAll<Border>().Where(b => b.Name == "ControllerSheetButtonChip").ToArray();
		Assert.Equal(pad.Buttons.Count, chipCells.Length);
		Assert.Contains(chipCells, b => PlayerRender.SolidColor(b.Background) == Color.FromArgb(0xCC, 0x33, 0xCC, 0x66));

		//The values are the tester's own readouts, verbatim.
		Assert.Equal("X: 128  Y: -64  mag 45%", window.FindNamed<TextBlock>("ControllerSheetLeftStick").Text);
		Assert.Equal("X: -32", window.FindNamed<TextBlock>("ControllerSheetRightX").Text);
		Assert.Equal("Y: 200", window.FindNamed<TextBlock>("ControllerSheetRightY").Text);

		PlayerRender.Save(PlayerRender.Capture(window), "W-P17-controller-sheet");

		//A pad that goes away leaves the drawing honest: dark keys and the line
		//that says there is nothing to read.
		sheet.Tester.Gamepads.Clear();
		sheet.ApplyPad();
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
		Assert.DoesNotContain(Keys(window), k => k.Classes.Contains("lit"));
		Assert.True(window.FindNamed<TextBlock>("ControllerSheetNoPad").IsOnScreen());
	}

	//ADR-0255 slice 2: with more than one pad the sheet lets the player say which
	//one it is showing, instead of a count of the ones it is not.
	[AvaloniaFact]
	public void A_second_pad_adds_a_picker_that_chooses_which_one_the_sheet_shows()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();

		sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad One" });
		sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad Two" });
		sheet.ApplyPad();
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal("Pad One", window.FindNamed<TextBlock>("ControllerSheetPadName").Text);
		ListBox picker = window.FindNamed<ListBox>("ControllerSheetPadPicker");
		Assert.True(picker.IsOnScreen());
		Assert.Equal(2, picker.ItemCount);

		//Picking the second pad is what the sheet then reads and draws.
		picker.SelectedIndex = 1;
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(1, sheet.SelectedPadIndex);
		Assert.Equal("Pad Two", window.FindNamed<TextBlock>("ControllerSheetPadName").Text);
	}

	//ADR-0255 Consequences: the reads are scoped. The tester's own 60 Hz poll is
	//gated on its Test tab, and a sheet that kept polling behind a running game
	//would break the same scoping, so this sheet polls on its own timer - only
	//while it is visible *and* the game is paused, which it makes true itself when
	//it opens. The one answer no test can drive (the game resuming under the
	//sheet, which only a timer tick sees) is ControllerSheetReads' own row in
	//UI.Tests; this covers the timer that follows every other one.
	[AvaloniaFact]
	public void The_sheet_reads_the_pad_only_while_visible_over_a_paused_game()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		ControllerSheetViewModel sheet = model.ControllerSheet;

		//The tester's own gate stays shut: this sheet owns its polling.
		Assert.False(sheet.Tester.IsTestTabVisible);
		Assert.Null(Poll(sheet));

		//A sheet that opens over a game that is running pauses it first - the reads
		//need that, and the poll starts from the state it finds, so the order is
		//part of the rule.
		bool paused = false;
		int pauses = 0;
		sheet.IsPaused = () => paused;
		sheet.Pause = () => { pauses++; paused = true; };

		OpenFromSettings(window, model);
		Assert.True(sheet.IsVisible);
		Assert.Equal(1, pauses);
		Assert.NotNull(Poll(sheet));

		//Esc hides it: the reads stop with it (the timer is gone, not idle).
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.False(sheet.IsVisible);
		Assert.Null(Poll(sheet));

		//Reopened over a game that is running again (a shortcut resumed it), the
		//same rule holds: pause, then read.
		paused = false;
		sheet.Open();
		Dispatcher.UIThread.RunJobs();
		Assert.True(sheet.IsVisible);
		Assert.Equal(2, pauses);
		Assert.NotNull(Poll(sheet));
		sheet.Close();
		Assert.Null(Poll(sheet));
	}

	//#816: the sheet's host lives inside PlayWorkspace, which a task door hides.
	//The Settings sheet is a sibling, reachable from every door, so before this
	//Remaster/Share › Settings › Controls › More in Options… opened the sheet
	//anyway: it paused the game under the hidden ancestor, drew nothing, and left
	//the session stuck. There the Controls row has to keep the classic Input page.
	[AvaloniaFact]
	public void A_task_door_keeps_the_classic_input_page_and_never_pauses_the_game()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowDoorWithGame(Workspace.Remaster);
		Assert.False(model.IsPlayWorkspace);

		//The sheet's own pause is what a task door must not trigger - it is the one
		//nothing resumes. (The classic window pauses too while it is up, but that is
		//its own resumable auto-pause, so the real EmuApi flag cannot tell the two
		//apart.) The delegates are the sheet's own, counted here.
		ControllerSheetViewModel sheet = model.ControllerSheet;
		bool paused = false;
		int pauses = 0;
		sheet.IsPaused = () => paused;
		sheet.Pause = () => { pauses++; paused = true; };

		window.OpenPlayerSettingsSheet();
		Dispatcher.UIThread.RunJobs();
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Input);
		Dispatcher.UIThread.RunJobs();

		try {
			Click(window.FindNamed<Button>("btnPlayerSettingsMoreInOptions"));

			//The sheet did not open and did not pause the game, and the classic Input
			//page - the behaviour before the Controller sheet landed - is what landed.
			Assert.False(sheet.IsVisible);
			Assert.Equal(0, pauses);
			ConfigWindow options = Assert.IsType<ConfigWindow>(model.MainMenu.OptionsWindow);
			Assert.Equal(ConfigWindowTab.Input, Assert.IsType<ConfigViewModel>(options.DataContext).SelectedIndex);
		} finally {
			model.MainMenu.OptionsWindow?.Close();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//#816, the door the first guard missed: IsPlayWorkspace is the *game screen*
	//gate - Play and Classic both set it - so "!IsPlayWorkspace" refused Remaster
	//and Share but admitted Classic. Classic's Esc is not the Play router
	//(ShortcutHandler routes ToggleOverlay only in Player mode), so a sheet it
	//opened had no way back. Same sequence as the task-door test above, one door
	//over: Settings opened from Remaster, the shell switcher to Classic, then the
	//Controls link - the door, not the game screen, is what the sheet needs.
	[AvaloniaFact]
	public void A_switch_to_classic_does_not_admit_the_controller_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowDoorWithGame(Workspace.Remaster);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		bool paused = false;
		int pauses = 0;
		sheet.IsPaused = () => paused;
		sheet.Pause = () => { pauses++; paused = true; };

		window.OpenPlayerSettingsSheet();
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.IsPlayerSettingsVisible);

		//Remaster -> Classic on the shell switcher. Classic shows the game screen,
		//so IsPlayWorkspace flips true - which is exactly what fooled the old guard.
		model.SelectWorkspace(Workspace.Classic);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(Workspace.Classic, model.Shell.Active);
		Assert.False(model.Shell.IsPlay);
		Assert.True(model.IsPlayWorkspace);

		//The Controls link's own call. The sheet must not open and the game must
		//not pause: Classic has no Play Esc to bring either back.
		Assert.False(model.OpenControllerSheet());
		Dispatcher.UIThread.RunJobs();
		Assert.False(sheet.IsVisible);
		Assert.False(window.FindNamed<Border>("PlayerControllerSheet").IsOnScreen());
		Assert.Equal(0, pauses);
	}

	//#821: the poll used to stop the timer the moment the game resumed
	//under the sheet, and nothing re-armed it - a Pause shortcut bound off Esc
	//resumed the game, the next tick stopped the reads, and when the player paused
	//again no tick would ever run and the drawn keys and readouts froze forever.
	//The timer has to follow the sheet, and the reads follow the stricter rule.
	[AvaloniaFact]
	public void The_poll_survives_a_game_resuming_and_pausing_again_under_the_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		ControllerSheetViewModel sheet = model.ControllerSheet;

		bool paused = false;
		sheet.IsPaused = () => paused;
		sheet.Pause = () => paused = true;

		OpenFromSettings(window, model);
		Assert.True(sheet.IsVisible);
		Assert.NotNull(Poll(sheet));
		//Driven by Tick below, not by the dispatcher clock: a real 16 ms tick between
		//the steps would read on its own and make the "no read while running" half
		//nondeterministic. Stopping the timer leaves the _poll field live, which is
		//what the lifetime assertions below are about.
		Poll(sheet)!.Stop();

		//A marker pad the reads would drop: a tick that reads rebuilds the tester's
		//list from the host and this item is gone. It is how a read is told from a
		//tick, which a poll-only assertion cannot do.
		GamepadTestItem marker = new(0) { Name = "marker" };
		sheet.Tester.Gamepads.Clear();
		sheet.Tester.Gamepads.Add(marker);
		sheet.ApplyPad();
		Assert.Same(marker, sheet.Pad);

		//The game resumes under the sheet: the reads stop, but the timer must not -
		//nothing observes the pause state, so the next tick is the only thing that
		//can notice the game pausing again.
		paused = false;
		Tick(sheet);
		Assert.False(ControllerSheetReads.Wanted(sheet.IsVisible, paused));
		Assert.NotNull(Poll(sheet));
		//The negative half, and the real risk of this change: a tick while the game
		//runs must not read (ADR-0255 scoping). An unconditional Refresh would pass
		//every poll assertion above and fail here.
		Assert.Same(marker, sheet.Pad);

		//The game pauses again: the same timer is still there to read through on its
		//next tick - and it does read, which is what re-arms the drawn keys.
		paused = true;
		Tick(sheet);
		Assert.NotNull(Poll(sheet));
		Assert.True(sheet.IsVisible);
		//The read ran on the second pause: the marker was replaced by whatever the
		//host reports (nothing, on a machine with no pad).
		Assert.NotSame(marker, sheet.Pad);
	}

	//A task door with a game loaded as far as the sheets are concerned.
	private (MainWindow Window, MainWindowViewModel Model) ShowDoorWithGame(Workspace door)
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = door;
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		_wasPaused = EmuApi.IsPaused();
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	//The poll timer's own tick, which only a real 16 ms timer would reach - driven
	//directly so the test is not racing the dispatcher clock.
	private static void Tick(ControllerSheetViewModel sheet)
	{
		typeof(ControllerSheetViewModel).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!
			.Invoke(sheet, null);
		Dispatcher.UIThread.RunJobs();
	}

	//A section's rows are ItemsControl items: their containers (and so the Buttons
	//a test clicks) exist only once a layout pass has built them.
	private static void Relayout(MainWindow window)
	{
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
	}

	//ADR-0255 slice 2: PLAYERS is one row per port, naming the device whose keys
	//live under it - read off the port's own slots, never a second table of who
	//is P1. Appears with more than one pad; with one there is nothing to assign.
	[AvaloniaFact]
	public void Players_lists_one_row_per_port_and_names_the_device_the_keys_are_under()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			//Device 1's keys live under Port1 (W-P15's write, a port already).
			ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(1, 0);

			//One pad: PLAYERS is absent - there is nothing to assign.
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero" });
			sheet.ApplyPad();
			Assert.False(sheet.ShowPlayers);
			Assert.False(window.FindNamed<StackPanel>("ControllerSheetPlayers").IsOnScreen());

			//A second pad: PLAYERS appears, one row per NES port, each naming the
			//device the port's keys carry. The pad's own slot is what its keys carry
			//(device 1), which is where the row's name comes from - not its place in
			//the host's enumeration (Index).
			sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad One", Slot = 1 });
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			Assert.True(sheet.ShowPlayers);
			Assert.True(window.FindNamed<StackPanel>("ControllerSheetPlayers").IsOnScreen());
			Assert.Equal(2, sheet.Players.Count);
			Assert.Equal("Player 1", sheet.Players[0].Label);
			Assert.Equal("Pad One", sheet.Players[0].DeviceName);
			Assert.True(sheet.Players[0].HasDevice);
			Assert.Equal("Player 2", sheet.Players[1].Label);
			//The en string for ControllerSheetNoDevice; empty ports hold no pad.
			Assert.Equal("No controller", sheet.Players[1].DeviceName);
			Assert.False(sheet.Players[1].HasDevice);
			Assert.Equal(2, PlayerRows(window).Length);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//The assignment moves a device's keys from one port's slots to another's,
	//through the same ConfigManager path and ApplyConfig() the classic Input page
	//uses. No second store is written: the keys ARE the assignment.
	[AvaloniaFact]
	public void Assigning_the_selected_pad_to_a_player_moves_its_keys_between_the_ports()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(1, 0);
			ConfigManager.Config.Nes.Port1.Mapping1.Start = PadButton(1, 7);

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero" });
			sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad One", Slot = 1 });
			sheet.SelectedPadIndex = 1;
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			//Tap Player 2: device 1's whole slot moves off Port1 and onto Port2's
			//first free slot.
			Click(PlayerRows(window)[1]);

			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.A);
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.Start);
			Assert.Equal(PadButton(1, 0), ConfigManager.Config.Nes.Port2.Mapping1.A);
			Assert.Equal(PadButton(1, 7), ConfigManager.Config.Nes.Port2.Mapping1.Start);
			//The rows say so on their own surface too.
			Assert.Equal("Player 2", sheet.Players[1].Label);
			Assert.Equal("Pad One", sheet.Players[1].DeviceName);
			Assert.Equal("No controller", sheet.Players[0].DeviceName);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//The keyboard case: with no pad the sheet says what the keyboard plays. The
	//preset button is offered only when nothing is bound anywhere (the same guard
	//Configuration.RestoreKeyboardPresetIfNothingIsBound uses), so a config the
	//player built by hand is never overwritten.
	[AvaloniaFact]
	public void With_no_pad_the_sheet_says_what_the_keyboard_plays()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;
		sheet.KeyName = key => $"K{key:X}";

		NesConfig savedNes = SavedNes();
		GameboyConfig savedGb = ConfigManager.Config.Gameboy.Clone();
		GbaConfig savedGba = ConfigManager.Config.Gba.Clone();
		SmsConfig savedSms = ConfigManager.Config.Sms.Clone();
		DefaultKeyMappingType savedFlags = ConfigManager.Config.DefaultKeyMappings;
		try {
			ClearPlayerPorts();
			//A keyboard binding (below the gamepad base index) is what plays.
			ConfigManager.Config.Nes.Port1.Mapping1.A = 0x20;
			ConfigManager.Config.Nes.Port1.Mapping1.B = 0x21;

			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			Assert.True(sheet.ShowKeyboard);
			Assert.False(sheet.ShowPlayers);
			Assert.True(window.FindNamed<StackPanel>("ControllerSheetKeyboard").IsOnScreen());
			Assert.Contains("K20", window.FindNamed<TextBlock>("ControllerSheetKeyboardText").Text);
			Assert.Contains("K21", window.FindNamed<TextBlock>("ControllerSheetKeyboardText").Text);
			//The keyboard plays, so there is no preset to offer back.
			Assert.False(sheet.CanRestoreKeyboard);
			Assert.False(window.FindNamed<Button>("ControllerSheetKeyboardRestore").IsOnScreen());

			//Nothing bound anywhere: the preset is offered, and writing it back
			//turns the keyboard on (the guard on Configuration, the load path's own).
			ClearPlayerPorts();
			ConfigManager.Config.DefaultKeyMappings = DefaultKeyMappingType.None;
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			Assert.True(sheet.CanRestoreKeyboard);
			Assert.True(window.FindNamed<Button>("ControllerSheetKeyboardRestore").IsOnScreen());

			Click(window.FindNamed<Button>("ControllerSheetKeyboardRestore"));
			Assert.True(ConfigManager.Config.DefaultKeyMappings.HasFlag(DefaultKeyMappingType.ArrowKeys));
		} finally {
			ConfigManager.Config.Nes = savedNes;
			ConfigManager.Config.Gameboy = savedGb;
			ConfigManager.Config.Gba = savedGba;
			ConfigManager.Config.Sms = savedSms;
			ConfigManager.Config.DefaultKeyMappings = savedFlags;
		}
	}

	//ADR-0255 slice 2 ("moves a device's keys from one port's slots to another's"):
	//a pad bound in more than one slot of a port must not leave half of itself
	//behind, or one physical pad plays as two players. The whole device moves.
	[AvaloniaFact]
	public void Assigning_a_pad_bound_in_two_slots_moves_both_slots_together()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			//Device 1's keys sit in two slots of Port1 (a preset plus a hand
			//binding, say).
			ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(1, 0);
			ConfigManager.Config.Nes.Port1.Mapping3.B = PadButton(1, 1);

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero" });
			sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad One", Slot = 1 });
			sheet.SelectedPadIndex = 1;
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			Click(PlayerRows(window)[1]);

			//Both slots move off Port1 and onto Port2's first two free slots.
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.A);
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping3.B);
			Assert.Equal(PadButton(1, 0), ConfigManager.Config.Nes.Port2.Mapping1.A);
			Assert.Equal(PadButton(1, 1), ConfigManager.Config.Nes.Port2.Mapping2.B);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//ADR-0255 Consequences ("the two surfaces must not disagree about which slot a
	//pad is in"): a slot that binds only the port type's custom keys is taken, so
	//the assignment must skip it and land in the next free slot - the same rule
	//W-P15's own Save reads.
	[AvaloniaFact]
	public void A_slot_that_binds_only_custom_keys_is_not_free()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			//Port2's first slot binds the Zapper's custom keys only - no base
			//key - so only a reader that looks at the custom keys sees it taken.
			ConfigManager.Config.Nes.Port2.Type = ControllerType.NesZapper;
			ConfigManager.Config.Nes.Port2.Mapping1.ZapperButtons = new ushort[] { 0x20 };
			//The pad's keys live in Port1's first slot.
			ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(1, 0);

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero" });
			sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad One", Slot = 1 });
			sheet.SelectedPadIndex = 1;
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			Click(PlayerRows(window)[1]);

			//Slot 0 is taken, so the pad lands in slot 1 and the custom keys stay.
			Assert.Equal(PadButton(1, 0), ConfigManager.Config.Nes.Port2.Mapping2.A);
			Assert.Equal(0, ConfigManager.Config.Nes.Port2.Mapping1.A);
			Assert.Equal(new ushort[] { 0x20 }, ConfigManager.Config.Nes.Port2.Mapping1.ZapperButtons);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//Finding 1: the KEYBOARD line answers what the keyboard plays, so it reads the
	//player's own keys - the fixed KeyMapping fields - and never the port type's
	//custom keys. A Zapper-typed port whose slot binds only its mouse buttons (
	//real custom keys, below the gamepad base, so no pad block either) must not be
	//reported as the keyboard's bindings - which is what a reader that handed the
	//same slots to both questions did.
	[AvaloniaFact]
	public void The_keyboard_line_does_not_report_a_ports_custom_keys()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;
		sheet.KeyName = key => $"K{key:X}";

		NesConfig savedNes = SavedNes();
		DefaultKeyMappingType savedFlags = ConfigManager.Config.DefaultKeyMappings;
		try {
			ClearPlayerPorts();
			ConfigManager.Config.DefaultKeyMappings = DefaultKeyMappingType.None;
			//NES Port2 = Zapper: its first slot binds only the port type's own
			//custom keys - a Zapper's mouse buttons, non-pad and non-keyboard codes.
			ConfigManager.Config.Nes.Port2.Type = ControllerType.NesZapper;
			ConfigManager.Config.Nes.Port2.Mapping1.ZapperButtons = new ushort[] { 0x200, 0x201 };

			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			Assert.True(sheet.ShowKeyboard);
			//The custom keys are the port device's buttons, not the keyboard's:
			//the line says nothing is bound, it does not play them as keys. (The
			//resource string is asserted as text like the sibling tests do; the
			//suite runs en-US.)
			Assert.Equal("No controller connected, and no keyboard keys are bound.", sheet.KeyboardText);
			Assert.DoesNotContain("K200", sheet.KeyboardText);
			Assert.DoesNotContain("K201", sheet.KeyboardText);
		} finally {
			ConfigManager.Config.Nes = savedNes;
			ConfigManager.Config.DefaultKeyMappings = savedFlags;
		}
	}

	//Finding 4: the assignment note names the pad it was written about. Moving the
	//picker to another pad is exactly where that stops being true, and the rows do
	//not depend on which pad is selected - so the note has to go on the pad change
	//itself, not only when the rows change.
	[AvaloniaFact]
	public void The_assignment_note_goes_away_when_the_picker_moves_to_another_pad()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero" });
			sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad One", Slot = 1 });
			sheet.SelectedPadIndex = 1;
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			//Pad One is bound nowhere: a tap says so, naming it.
			Click(PlayerRows(window)[0]);
			Assert.Contains("Pad One", sheet.AssignNote);

			//The picker moves to Pad Zero. The rows (which only name devices bound
			//to the ports) do not move with the picker, so the pad change itself has
			//to clear the note.
			sheet.SelectedPadIndex = 0;
			Assert.Equal("", sheet.AssignNote);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//#813 / ADR-0255 slice 2: the pad a row names is identified by the block its
	//keys carry - the same block its (Backend, Slot) builds - never by the host's
	//enumeration ordinal, which is global on Windows (XInput slots first, then the
	//joysticks). Index 0 with Slot 1 must match the keys' device 1.
	[AvaloniaFact]
	public void A_player_row_names_the_pad_by_its_family_slot_and_not_the_enumeration_ordinal()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			//The keys carry the family's own device slot, 1.
			ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(1, 0);

			//The pad is first in the host's enumeration (Index 0) but its own slot
			//is 1: the row must follow the slot the keys carry, not the ordinal.
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad One", Slot = 1 });
			sheet.ApplyPad();

			Assert.Equal("Pad One", sheet.Players[0].DeviceName);
			Assert.True(sheet.Players[0].HasDevice);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//ADR-0255 slice 5's own axis: a DirectInput pad (its own family, code 0x2000)
	//is not the XInput pad of the same ordinal. Only a rule that carries the
	//family matches it; a bare device index cannot tell the two apart.
	[AvaloniaFact]
	public void A_directinput_pad_is_matched_in_its_own_key_code_family()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			//A joystick's first button, in the DirectInput family.
			ConfigManager.Config.Nes.Port1.Mapping1.A = (ushort)(0x2000 + 3);

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Joystick", Slot = 0, Backend = GamepadBackend.DirectInput });
			sheet.ApplyPad();

			Assert.Equal("Joystick", sheet.Players[0].DeviceName);
			Assert.True(sheet.Players[0].HasDevice);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//ADR-0255 slice 2: PLAYERS is rebuilt every 60 Hz read today; it must not be,
	//but the rule must not become a cache that can go stale - a config change made
	//elsewhere still has to show.
	[AvaloniaFact]
	public void Players_is_not_rebuilt_when_nothing_it_shows_changed()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero" });
			sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad One", Slot = 1 });
			sheet.ApplyPad();

			IReadOnlyList<ControllerSheetPlayerRow> first = sheet.Players;
			sheet.ApplyPad();
			Assert.Same(first, sheet.Players);

			//A config change made elsewhere still shows: no stale cache.
			ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(1, 0);
			sheet.ApplyPad();
			Assert.NotSame(first, sheet.Players);
			Assert.Equal("Pad One", sheet.Players[0].DeviceName);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//ADR-0255 slice 2: the assignment note has to go when it stops being true,
	//not stay on screen for the rest of the sheet's life.
	[AvaloniaFact]
	public void The_assignment_note_goes_away_when_it_stops_being_true()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero" });
			sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad One", Slot = 1 });
			sheet.SelectedPadIndex = 1;
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			//Pad One is bound nowhere: the tap says so.
			Click(PlayerRows(window)[0]);
			Assert.NotEqual("", sheet.AssignNote);

			//It gets bound on another surface: the note's state changed, so the
			//note goes.
			ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(1, 0);
			sheet.ApplyPad();
			Assert.Equal("", sheet.AssignNote);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//Finding 1: the "Use the keyboard preset" button must ask the guard's own
	//question, not a weaker one - Configuration reads every console's ports and
	//requires DefaultKeyMappings to be None, so a button gated on the loaded
	//console's ports alone is offered where the click is a silent no-op.
	[AvaloniaFact]
	public void The_keyboard_preset_button_is_gated_by_the_guards_own_question()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig savedNes = SavedNes();
		GameboyConfig savedGb = ConfigManager.Config.Gameboy.Clone();
		GbaConfig savedGba = ConfigManager.Config.Gba.Clone();
		SmsConfig savedSms = ConfigManager.Config.Sms.Clone();
		DefaultKeyMappingType savedFlags = ConfigManager.Config.DefaultKeyMappings;
		try {
			//Nothing bound anywhere and the preset flag is None: offered.
			ClearPlayerPorts();
			ConfigManager.Config.DefaultKeyMappings = DefaultKeyMappingType.None;
			sheet.ApplyPad();
			Assert.True(sheet.CanRestoreKeyboard);

			//Another console holds a binding. The loaded console (NES) is empty,
			//but the guard reads every console - so the click would be a no-op and
			//the button must not be offered.
			ClearPlayerPorts();
			ConfigManager.Config.Gameboy.Controller.Mapping1.A = 0x20;
			sheet.ApplyPad();
			Assert.False(sheet.CanRestoreKeyboard);

			//The preset flag is not None: the guard refuses whatever the slots say.
			ClearPlayerPorts();
			ConfigManager.Config.DefaultKeyMappings = DefaultKeyMappingType.Xbox | DefaultKeyMappingType.ArrowKeys;
			sheet.ApplyPad();
			Assert.False(sheet.CanRestoreKeyboard);
		} finally {
			ConfigManager.Config.Nes = savedNes;
			ConfigManager.Config.Gameboy = savedGb;
			ConfigManager.Config.Gba = savedGba;
			ConfigManager.Config.Sms = savedSms;
			ConfigManager.Config.DefaultKeyMappings = savedFlags;
		}
	}

	//ADR-0255 slice 3: REMAP is a mode of this sheet, not a dialog. The rows are
	//the console's controls and each carries two lights - what the pad sends and
	//what the port receives. The pad's own button is the pad side (the tester's
	//list, the per-backend order slice 1 reads); the port's bound code being held
	//is the port side. A row lit on the pad side and dark on the port side is a
	//wrong binding made visible - here a binding whose device index moved.
	[AvaloniaFact]
	public void The_remap_rows_are_the_consoles_controls_each_with_the_pads_two_lights()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;
		//The pad's own codes, as its backend names them (macOS: "Pad1 A" is device
		//0's button 0), built from the tester's own per-backend table so the name a
		//code gets is the name the sheet's button lookup reads back.
		string[] backendNames = ControllerLivePad.NameList(GamepadBackend.GameController);
		sheet.KeyName = key => "Pad" + (((key - ControllerDevices.BaseGamepadIndex) >> 8) + 1) + " " + backendNames[key & 0xFF];

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			//Device 0's A is bound to the console's A: a correct binding.
			ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(0, 0);

			GamepadTestItem pad = new(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController };
			sheet.Tester.Gamepads.Add(pad);
			sheet.SelectedPadIndex = 0;
			//The pad's own A is down, and the port's bound code is the one held -
			//the two real sources, both lit.
			pad.Buttons[0].IsPressed = true;
			sheet.PressedKeys = () => new ushort[] { PadButton(0, 0) };
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			Assert.True(sheet.ShowRemap);
			Assert.True(window.FindNamed<StackPanel>("ControllerSheetRemap").IsOnScreen());
			Assert.Equal(8, sheet.RemapRows.Count);
			Assert.Equal("A", sheet.RemapRows[0].Label);
			Assert.True(sheet.RemapRows[0].HasBinding);
			Assert.True(sheet.RemapRows[0].PadLit);
			Assert.True(sheet.RemapRows[0].PortLit);
			//An unbound control lights nothing.
			Assert.False(sheet.RemapRows[1].HasBinding);
			Assert.False(sheet.RemapRows[1].PadLit);
			Assert.False(sheet.RemapRows[1].PortLit);

			//The lights are the drawing, not only the model: the lit class the style
			//reads is on the row's own two dots.
			Assert.Contains("lit", LightOf(window, "ControllerSheetRemapPadLight", "A").Classes);
			Assert.Contains("lit", LightOf(window, "ControllerSheetRemapPortLight", "A").Classes);
			Assert.DoesNotContain("lit", LightOf(window, "ControllerSheetRemapPadLight", "B").Classes);

			//The binding's device index moves (the pad reconnected as device 1):
			//the stale code is never held, so the port goes dark while the pad's own
			//A still lights the pad side. The wrong binding is visible.
			ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(1, 0);
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();
			Assert.True(sheet.RemapRows[0].PadLit);
			Assert.False(sheet.RemapRows[0].PortLit);
			Assert.Contains("lit", LightOf(window, "ControllerSheetRemapPadLight", "A").Classes);
			Assert.DoesNotContain("lit", LightOf(window, "ControllerSheetRemapPortLight", "A").Classes);

			Assert.Equal(8, window.FindNamed<ItemsControl>("ControllerSheetRemapRows").ItemCount);
			Assert.Equal(8, window.FindAll<Button>().Count(b => b.Name == "ControllerSheetRemapRow"));
			Assert.Equal(8, window.FindAll<Border>().Count(b => b.Name == "ControllerSheetRemapPortLight"));
			Assert.Equal(8, window.FindAll<Border>().Count(b => b.Name == "ControllerSheetRemapPadLight"));
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//Picking a row arms the capture; the next pad button that goes down (after the
	//one that picked the row is released) is bound, through the same ConfigManager
	//path and ApplyConfig() the classic Input page uses.
	[AvaloniaFact]
	public void Picking_a_remap_row_arms_it_and_the_next_pad_button_is_bound()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			//Tap the B row (the second one).
			Click(window.FindAll<Button>().First(b => b.Name == "ControllerSheetRemapRow" && b.DataContext is ControllerSheetRemapRow { Label: "B" }));

			Assert.True(sheet.IsCapturing);
			Assert.True(model.IsControllerCapturing);
			Assert.Contains("B", sheet.RemapNote);
			Assert.Contains("Esc", sheet.RemapNote);

			//The release that arms it, then the new button.
			ushort x = PadButton(0, 2);
			sheet.PressedKeys = () => new ushort[] { x };
			sheet.RefreshRemap();
			Assert.Equal(x, ConfigManager.Config.Nes.Port1.Mapping1.B);
			Assert.False(sheet.IsCapturing);
			Assert.False(model.IsControllerCapturing);
			//The write is the port's own, and nothing else moved.
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.A);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//ADR-0256 Decision 4: the pad's navigation controls may not be bound here, and
	//the refusal is shown rather than swallowed. The pad's A is the preset's
	//Confirm, so a capture that takes it refuses and stays armed.
	[AvaloniaFact]
	public void A_navigation_control_is_refused_visibly_and_nothing_is_written()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;
		//The backend's own names, so PadNavControls resolves the pad's preset for
		//it: the pad's A is Confirm and its B is Back (the Xbox family).
		sheet.KeyName = key => key == PadButton(0, 0) ? "Pad1 A" : key == PadButton(0, 1) ? "Pad1 B" : "Pad1 " + (key & 0xFF);
		sheet.KeyCode = name => name switch {
			"Pad1 A" => PadButton(0, 0),
			"Pad1 B" => PadButton(0, 1),
			"Pad1 Up" => PadButton(0, 8),
			"Pad1 Down" => PadButton(0, 9),
			"Pad1 Left" => PadButton(0, 10),
			"Pad1 Right" => PadButton(0, 11),
			_ => 0
		};

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();

			sheet.ArmRemap(SetupButton.A);
			string armNote = sheet.RemapNote;
			//Released, then the pad's own Confirm is pressed - the control the
			//player opens menus with, and the one they would be stuck behind if it
			//were bound here.
			ushort confirm = PadButton(0, 0);
			sheet.PressedKeys = () => new ushort[] { confirm };
			sheet.RefreshRemap();

			Assert.True(sheet.IsCapturing);
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.A);
			//The refusal *replaced* the arm note, and is not a bind. "Something was
			//said" would not do: the note the arm put there ("Press the controller
			//button for A - Esc cancels.") is non-empty and carries no "is now", so
			//that assertion held whether the refusal was shown or swallowed - which
			//is the one thing this case exists to tell apart. The refusal's own text
			//is not readable from this project (ResourceHelper is internal), so the
			//note it replaced is what stands in for it.
			Assert.NotEqual(armNote, sheet.RemapNote);
			Assert.DoesNotContain("is now", sheet.RemapNote);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//ADR-0255 slice 3, trap 2: Esc cancels the capture through the one Esc router
	//(PlayEsc's CancelCapture state), and the sheet stays up - a second Esc is what
	//closes it. No second key handler races the chain.
	[AvaloniaFact]
	public void Esc_cancels_the_capture_through_the_esc_router_and_leaves_the_sheet_up()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();
			sheet.ArmRemap(SetupButton.A);
			Assert.True(sheet.IsCapturing);

			model.TogglePlayerOverlay();

			Assert.False(sheet.IsCapturing);
			Assert.False(model.IsControllerCapturing);
			//Still the Controller sheet: the capture ended, the sheet did not.
			Assert.True(sheet.IsVisible);
			Assert.Equal("", sheet.RemapNote);

			//The next Esc is the ordinary one again.
			model.TogglePlayerOverlay();
			Assert.False(sheet.IsVisible);
			Assert.True(model.IsPlayerOverlayVisible);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//ADR-0255 slice 3, trap 1: while the sheet captures, the pad press is the
	//capture's, so ADR-0256's bridge must not also turn it into a focus move or a
	//Confirm. The one authority predicate answers it, so `IsControllerCapturing`
	//is what the bridge reads - and a bridge tick with the pad's Confirm down moves
	//nothing while a capture is armed.
	[AvaloniaFact]
	public void The_pad_bridge_leaves_the_pad_to_the_capture()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			//The pad, as the bridge and the capture both resolve it: the Xbox
			//preset's own button names, so the D-pad is a navigation control.
			Func<string, ushort> keyCode = name => name switch {
				"Pad1 A" => PadButton(0, 0),
				"Pad1 B" => PadButton(0, 1),
				"Pad1 Up" => PadButton(0, 8),
				"Pad1 Down" => PadButton(0, 9),
				"Pad1 Left" => PadButton(0, 10),
				"Pad1 Right" => PadButton(0, 11),
				_ => 0
			};
			Func<ushort, string> keyName = key => key == PadButton(0, 0) ? "Pad1 A" : key == PadButton(0, 1) ? "Pad1 B" : "Pad1 X";
			sheet.KeyName = keyName;
			sheet.KeyCode = keyCode;
			ushort confirm = PadButton(0, 0);

			//Capturing: the pad's Confirm is the capture's. The bridge must not also
			//take it as a Confirm - which, on the sheet, would activate the focused
			//control (Done) and close the sheet - and must not move the focus with a
			//direction either. The one authority predicate answers it.
			sheet.ArmRemap(SetupButton.A);
			Assert.True(model.IsControllerCapturing);
			Control? armed = window.FocusManager?.GetFocusedElement() as Control;
			PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), keyName, keyCode);
			PlayPadNavigationWiring.TickForTest(window, new ushort[] { confirm }, TimeSpan.FromMilliseconds(50), keyName, keyCode);
			PlayPadNavigationWiring.TickForTest(window, new ushort[] { PadButton(0, 8) }, TimeSpan.FromMilliseconds(50), keyName, keyCode);

			Assert.Same(armed, window.FocusManager?.GetFocusedElement() as Control);
			Assert.True(model.ControllerSheet.IsVisible);
			Assert.False(model.IsPlayerOverlayVisible);
			//The press bound nothing: it is the capture's, and the capture has not
			//run a tick of its own here.
			Assert.True(sheet.IsCapturing);
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.A);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//A capture is a mode of the sheet, and the sheet closing takes it with it. The
	//poll that ends a capture the other way - the pad going away - stops with the
	//sheet, so a capture left armed by the close would keep answering true to the
	//one predicate ADR-0256's bridge asks (IsControllerCapturing), and the pad would
	//stay out of the entire Play door: no focus moves, no Confirm, nothing, for the
	//rest of the session. Reached by arm-then-Done-with-the-pointer, which is one
	//click away from the ordinary path.
	[AvaloniaFact]
	public void Closing_the_sheet_ends_the_capture_it_was_in()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();

			sheet.ArmRemap(SetupButton.A);
			Assert.True(sheet.IsCapturing);
			Assert.True(model.IsControllerCapturing);

			//Done: back to W-P4, which is where this sheet came from.
			model.CloseControllerSheetToOverlay();

			Assert.False(sheet.IsCapturing);
			//The one predicate the pad bridge asks (PlayPadNavigationWiring.
			//HasAuthority): true here is a pad that moves no focus and confirms
			//nothing anywhere in the Play door, for the rest of the session. What the
			//bridge does with it while it *is* true is the sibling case above.
			Assert.False(model.IsControllerCapturing);
			Assert.Equal("", sheet.RemapNote);
			//Back where the sheet came from, and the door is the player's again.
			Assert.False(sheet.IsVisible);
			Assert.True(model.IsPlayerOverlayVisible);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//The REMAP rows are the loaded console's controls, and the console can change
	//under one sheet: the view-model is built once per window
	//(MainWindowViewModel.ControllerSheet) and only toggled, so opening the sheet on
	//one console, closing it and loading another in the same session takes the
	//rebuild path. The rows have to be handed over as a *fresh* list - re-assigning
	//the one already bound notifies nobody (the generated setter skips an equal
	//reference, and List<T> raises no collection change) - or the ItemsControl keeps
	//the previous console's containers, frozen, because ApplyRemapRows then only
	//touches the new objects. Asserted on the containers the player sees, which is
	//where the difference is: RemapRows itself holds the new rows either way.
	[AvaloniaFact]
	public void The_remap_rows_follow_the_console_the_sheet_is_over()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();

		NesConfig savedNes = SavedNes();
		SmsConfig savedSms = ConfigManager.Config.Sms.Clone();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			ConfigManager.Config.Sms.Port1 = new SmsControllerConfig();
			ConfigManager.Config.Sms.Port2 = new SmsControllerConfig();
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.CurrentConsole = () => ConsoleType.Nes;
			sheet.ApplyPad();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			//The NES pad's eight controls, on screen.
			Assert.Equal(8, RemapRows(window).Length);

			//The game changes under the same sheet.
			sheet.CurrentConsole = () => ConsoleType.Sms;
			sheet.ApplyPad();
			window.UpdateLayout();
			Dispatcher.UIThread.RunJobs();

			//The Master System pad's six, and none of the NES ones left behind.
			Button[] rows = RemapRows(window);
			Assert.Equal(6, rows.Length);
			Assert.Equal(new[] { "1", "2", "Down", "Left", "Right", "Up" },
				rows.Select(r => ((ControllerSheetRemapRow)r.DataContext!).Label).OrderBy(l => l, StringComparer.Ordinal).ToArray());
		} finally {
			ConfigManager.Config.Nes = savedNes;
			ConfigManager.Config.Sms = savedSms;
		}
	}

	//REMAP edits the port that *holds* the selected pad's keys, in any of its slots.
	//A port whose first named slot holds one pad and a later slot another is a state
	//the classic Input page can make, and reading only the first slot (PortDevice)
	//matched no port and fell back to the first one - writing the rebind over the
	//other player's bindings (found in review).
	[AvaloniaFact]
	public void A_rebind_lands_on_the_port_that_holds_the_pads_keys()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			//Port2's first slot holds pad 0, its second the pad the sheet shows.
			ConfigManager.Config.Nes.Port2.Mapping1.A = PadButton(0, 0);
			ConfigManager.Config.Nes.Port2.Mapping2.A = PadButton(1, 0);

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			//The slot is the pad's own key block, and the ctor does not set it.
			sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad One", Backend = GamepadBackend.GameController, Slot = 1 });
			sheet.SelectedPadIndex = 1;
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();

			sheet.ArmRemap(SetupButton.B);
			ushort x = PadButton(1, 2);
			sheet.PressedKeys = () => new ushort[] { x };
			sheet.RefreshRemap();

			//The write went to Port2, where pad 1's keys live, and left Port1 - the
			//other player's port - alone.
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.A);
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.B);
			Assert.Contains(Enumerable.Range(0, 4),
				slot => ControllerSheetSlotWrite.Slot(ConfigManager.Config.Nes.Port2, slot).B == x);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//A capture is a mode of the sheet's *reads*, so it goes with them - the same
	//rule the pad going away and the sheet closing already follow. A pad shortcut
	//resumes the game under the sheet (the Esc router does not route it), the reads
	//stop, and the capture's baseline freezes at the pressed set from before the
	//resume: the first tick after the player pauses again then reads whatever they
	//are holding by then - a button pressed only to play - as a new press and binds
	//it (found in review).
	[AvaloniaFact]
	public void A_game_that_resumes_under_the_sheet_ends_the_capture()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();

			sheet.ArmRemap(SetupButton.A);
			Assert.True(sheet.IsCapturing);

			//The game resumes under the sheet, the player plays, and pauses again.
			sheet.IsPaused = () => false;
			Tick(sheet);
			sheet.PressedKeys = () => new ushort[] { PadButton(0, 2) };
			sheet.IsPaused = () => true;
			sheet.ApplyPad();

			Assert.False(sheet.IsCapturing);
			//Nothing was bound: that button went down while the reads were stopped,
			//so it is not a press of this capture's.
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.A);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//A rebind of a control the pad has not bound yet lands in the pad's own slot,
	//not in whichever slot happens to be free: a pad split across two slots makes
	//the PLAYERS assignment move both, and it refuses with NoFreeSlot where one slot
	//would have fit. The pad here has A and the D-pad in Port1's second slot, and B
	//unbound, with the first slot free (review of #839).
	[AvaloniaFact]
	public void A_rebind_of_an_unbound_control_joins_the_pads_own_slot()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig saved = SavedNes();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port1.Mapping2.A = PadButton(0, 0);
			ConfigManager.Config.Nes.Port1.Mapping2.Up = PadButton(0, 8);
			//Mapping1 stays empty, so it is the first free slot.

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();

			sheet.ArmRemap(SetupButton.B);
			ushort x = PadButton(0, 1);
			sheet.PressedKeys = () => new ushort[] { x };
			sheet.RefreshRemap();

			//The pad's keys stay in one slot: B joins them, and the free first slot
			//is left alone.
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.A);
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.B);
			Assert.Equal(x, ConfigManager.Config.Nes.Port1.Mapping2.B);
		} finally {
			ConfigManager.Config.Nes = saved;
		}
	}

	//ADR-0255 slice 4: EXTRA BUTTONS, the second binding surface of the same
	//sheet - the pad's spare buttons carrying the emulator's own actions, the
	//user's own fourth requirement ("se o jostick tiver mais botoes que o nitendo
	//quero poder atribuir outros controles como retroceder, avancar, compartilhar,
	//home"). A row's binding *is* the shortcut's own spare slot
	//(ShortcutKeyInfo.PadBinding), so what the classic Input page shows for that
	//action and what the row shows are the same field - which is what this pins,
	//along with the keyboard combination the slot must leave alone.
	[AvaloniaFact]
	public void The_extra_rows_are_the_shortcut_lists_own_pad_slot()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;
		sheet.KeyName = key => "Pad1 " + (key & 0xFF);

		NesConfig savedNes = SavedNes();
		ShortcutKeyInfo rewind = ShortcutOf(EmulatorShortcut.Rewind);
		PadShortcutBinding? savedBinding = rewind.PadBinding;
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			//The shortcut's spare slot is empty, as a fresh config's is.
			rewind.PadBinding = null;

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();
			Relayout(window);

			//The section is up, one row per listed action, and every row is drawn.
			Assert.True(sheet.ShowExtra);
			Assert.True(window.FindNamed<StackPanel>("ControllerSheetExtra").IsOnScreen());
			Assert.Equal(ControllerSheetExtra.Actions.Count, sheet.ExtraRows.Count);
			Assert.Equal(ControllerSheetExtra.Actions.Count, window.FindAll<Button>().Count(b => b.Name == "ControllerSheetExtraRow"));
			Assert.All(sheet.ExtraRows, row => Assert.False(string.IsNullOrWhiteSpace(row.Label)));

			ControllerSheetExtraRow rewindRow = sheet.ExtraRows.First(r => r.Action == EmulatorShortcut.Rewind);
			Assert.False(rewindRow.HasBinding);

			//The classic Input page's own field: writing the shortcut's PadBinding
			//is what makes the row show it, with no second store in between.
			ushort spare = PadButton(0, 5);
			rewind.PadBinding = new PadShortcutBinding() { KeyCode = spare };
			sheet.ApplyPad();
			Relayout(window);

			rewindRow = sheet.ExtraRows.First(r => r.Action == EmulatorShortcut.Rewind);
			Assert.True(rewindRow.HasBinding);
			Assert.Equal("Pad1 5", rewindRow.BoundName);
			//...and clearing that field from the other surface empties the row again.
			rewind.PadBinding = null;
			sheet.ApplyPad();
			Assert.False(sheet.ExtraRows.First(r => r.Action == EmulatorShortcut.Rewind).HasBinding);
		} finally {
			rewind.PadBinding = savedBinding;
			ConfigManager.Config.Nes = savedNes;
		}
	}

	//The pad slot is a *third* binding beside the two key combinations, never a
	//replacement for them: a shortcut that already answers to a keyboard
	//combination keeps it, and clearing the spare button gives back only that.
	[AvaloniaFact]
	public void An_extra_binding_leaves_the_shortcuts_keyboard_combination_alone()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig savedNes = SavedNes();
		ShortcutKeyInfo rewind = ShortcutOf(EmulatorShortcut.Rewind);
		ushort savedKey1 = rewind.KeyCombination.Key1;
		PadShortcutBinding? savedBinding = rewind.PadBinding;
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			//A shortcut with a key and no spare button: the ADR's "the two coexist".
			rewind.KeyCombination.Key1 = 200;
			rewind.PadBinding = null;

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();

			sheet.ArmExtra(EmulatorShortcut.Rewind);
			//The row is picked with the pad at rest, so this tick spends the
			//release-first step (ControllerSheetCapture) and the press after it binds.
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.RefreshRemap();
			sheet.PressedKeys = () => new ushort[] { PadButton(0, 2) };
			sheet.RefreshRemap();

			Assert.Equal(PadButton(0, 2), rewind.PadBinding!.KeyCode);
			//The keyboard combination is exactly where it was.
			Assert.Equal(200, rewind.KeyCombination.Key1);

			sheet.ClearExtra(EmulatorShortcut.Rewind);
			Assert.Null(rewind.PadBinding);
			//Clearing the spare button did not touch the key it sits beside.
			Assert.Equal(200, rewind.KeyCombination.Key1);
		} finally {
			rewind.KeyCombination.Key1 = savedKey1;
			rewind.PadBinding = savedBinding;
			ConfigManager.Config.Nes = savedNes;
		}
	}

	//Picking a row arms slice 3's own capture - the same machine, one target
	//apart - so the release-first rule, the pad bridge's single IsControllerCapturing
	//flag and the write path are all the ones REMAP already had. And the arm is the
	//EXTRA section's: a REMAP row must not light up for a press aimed at an action.
	[AvaloniaFact]
	public void Picking_an_extra_row_arms_the_capture_and_the_next_pad_button_binds_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;
		sheet.KeyName = key => "Pad1 " + (key & 0xFF);

		NesConfig savedNes = SavedNes();
		ShortcutKeyInfo rewind = ShortcutOf(EmulatorShortcut.Rewind);
		PadShortcutBinding? savedBinding = rewind.PadBinding;
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			rewind.PadBinding = null;

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();
			Relayout(window);

			Click(window.FindAll<Button>().First(b => b.Name == "ControllerSheetExtraRow" && b.DataContext is ControllerSheetExtraRow { Action: EmulatorShortcut.Rewind }));

			Assert.True(sheet.IsCapturing);
			//One flag, read by the pad bridge: while this is true the pad press is
			//the capture's and not a focus move (ADR-0256's bridge).
			Assert.True(model.IsControllerCapturing);
			Assert.Contains("Esc", sheet.ExtraNote);
			//The armed row is the EXTRA one, and no REMAP row claims the press.
			Assert.True(sheet.ExtraRows.First(r => r.Action == EmulatorShortcut.Rewind).Armed);
			Assert.DoesNotContain(sheet.RemapRows, r => r.Armed);

			//The release that arms it, then the spare button the player picked.
			ushort spare = PadButton(0, 2);
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.RefreshRemap();
			sheet.PressedKeys = () => new ushort[] { spare };
			sheet.RefreshRemap();

			Assert.Equal(spare, rewind.PadBinding!.KeyCode);
			Assert.False(sheet.IsCapturing);
			Assert.False(model.IsControllerCapturing);
			//A plain button carries no threshold - a field that would lie about
			//being in use (PadShortcutBinding).
			Assert.Null(rewind.PadBinding.ThresholdPercent);
			//The row now shows the host's own name for it, and nothing else moved.
			Assert.True(sheet.ExtraRows.First(r => r.Action == EmulatorShortcut.Rewind).HasBinding);
			//No console control was rebound by it: this section never touches a port.
			Assert.Equal(0, ConfigManager.Config.Nes.Port1.Mapping1.A);
		} finally {
			rewind.PadBinding = savedBinding;
			ConfigManager.Config.Nes = savedNes;
		}
	}

	//The row's clear control gives the spare button back. The ViewModel's guard is
	//what decides it is offered, so the case also pins that the clear is a write of
	//the same kind - it goes through ApplyConfig() and reaches the core.
	[AvaloniaFact]
	public void The_extra_rows_clear_button_gives_the_spare_button_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;
		sheet.KeyName = key => "Pad1 " + (key & 0xFF);

		NesConfig savedNes = SavedNes();
		ShortcutKeyInfo screenshot = ShortcutOf(EmulatorShortcut.TakeScreenshot);
		PadShortcutBinding? savedBinding = screenshot.PadBinding;
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			screenshot.PadBinding = new PadShortcutBinding() { KeyCode = PadButton(0, 6) };

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();
			Relayout(window);

			Assert.True(sheet.ExtraRows.First(r => r.Action == EmulatorShortcut.TakeScreenshot).HasBinding);
			Click(window.FindAll<Button>().First(b => b.Name == "ControllerSheetExtraClear" && b.DataContext is ControllerSheetExtraRow { Action: EmulatorShortcut.TakeScreenshot }));

			Assert.Null(screenshot.PadBinding);
			Assert.False(sheet.ExtraRows.First(r => r.Action == EmulatorShortcut.TakeScreenshot).HasBinding);
			Assert.False(sheet.IsCapturing);
		} finally {
			screenshot.PadBinding = savedBinding;
			ConfigManager.Config.Nes = savedNes;
		}
	}

	//ADR-0256 Decision 4 on the EXTRA section: the pad's navigation controls are
	//refused here too, visibly, and the capture stays armed. The refusal has to
	//replace the arm note - "something was said" would hold whether the refusal was
	//shown or swallowed, because the arm note is non-empty too (the same trap the
	//REMAP case was written against).
	[AvaloniaFact]
	public void A_navigation_control_is_refused_on_an_extra_row_visibly()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;
		//The backend's own names, so PadNavControls resolves the pad's preset for
		//it: the pad's A is Confirm (the Xbox family).
		sheet.KeyName = key => key == PadButton(0, 0) ? "Pad1 A" : "Pad1 " + (key & 0xFF);
		sheet.KeyCode = name => name switch {
			"Pad1 A" => PadButton(0, 0),
			"Pad1 B" => PadButton(0, 1),
			"Pad1 Up" => PadButton(0, 8),
			"Pad1 Down" => PadButton(0, 9),
			"Pad1 Left" => PadButton(0, 10),
			"Pad1 Right" => PadButton(0, 11),
			_ => 0
		};

		NesConfig savedNes = SavedNes();
		ShortcutKeyInfo rewind = ShortcutOf(EmulatorShortcut.Rewind);
		PadShortcutBinding? savedBinding = rewind.PadBinding;
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			rewind.PadBinding = null;

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();

			sheet.ArmExtra(EmulatorShortcut.Rewind);
			string armNote = sheet.ExtraNote;
			//Released first: without this tick the capture is still spending the
			//release-first step and the press below would only be recorded.
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.RefreshRemap();
			//The pad's own Confirm, the control the player opens menus with.
			sheet.PressedKeys = () => new ushort[] { PadButton(0, 0) };
			sheet.RefreshRemap();

			//Refused, still armed, nothing written, and the note says why.
			Assert.True(sheet.IsCapturing);
			Assert.Null(rewind.PadBinding);
			Assert.NotEqual(armNote, sheet.ExtraNote);
			//The refusal belongs to this section, and the other one's note is not
			//carrying it.
			Assert.Equal("", sheet.RemapNote);

			//Still listening: another control binds as usual.
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.RefreshRemap();
			sheet.PressedKeys = () => new ushort[] { PadButton(0, 4) };
			sheet.RefreshRemap();
			Assert.Equal(PadButton(0, 4), rewind.PadBinding!.KeyCode);
		} finally {
			rewind.PadBinding = savedBinding;
			ConfigManager.Config.Nes = savedNes;
		}
	}

	//The one Esc router is the capture's own exit (PlayEsc's CancelCapture state,
	//ADR-0255 slice 3) and an EXTRA arm goes through it too: one Esc ends the
	//capture, the note the arm put under the list goes with it, and the sheet
	//stays up - a second Esc is what closes it.
	[AvaloniaFact]
	public void Esc_cancels_an_extra_capture_and_leaves_the_sheet_up()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig savedNes = SavedNes();
		ShortcutKeyInfo rewind = ShortcutOf(EmulatorShortcut.Rewind);
		PadShortcutBinding? savedBinding = rewind.PadBinding;
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			rewind.PadBinding = null;

			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();

			sheet.ArmExtra(EmulatorShortcut.Rewind);
			Assert.True(sheet.IsCapturing);
			Assert.False(string.IsNullOrEmpty(sheet.ExtraNote));

			model.TogglePlayerOverlay();

			Assert.False(sheet.IsCapturing);
			Assert.False(model.IsControllerCapturing);
			//The arm's own line went with the capture, under the right section.
			Assert.Equal("", sheet.ExtraNote);
			Assert.Equal("", sheet.RemapNote);
			//Still the Controller sheet: the capture ended, the sheet did not.
			Assert.True(sheet.IsVisible);
		} finally {
			rewind.PadBinding = savedBinding;
			ConfigManager.Config.Nes = savedNes;
		}
	}

	//The section's gate is REMAP's, and for the same reason: a binding is made by
	//pressing a control on the pad, over a game whose console has a player port.
	//A pad that goes away takes its capture with it, and only its own.
	[AvaloniaFact]
	public void The_extra_section_is_dark_without_a_pad_or_a_player_port()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;

		NesConfig savedNes = SavedNes();
		ShortcutKeyInfo rewind = ShortcutOf(EmulatorShortcut.Rewind);
		PadShortcutBinding? savedBinding = rewind.PadBinding;
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			rewind.PadBinding = null;

			//No pad at all: nothing to press, so no section.
			sheet.PressedKeys = () => Array.Empty<ushort>();
			sheet.ApplyPad();
			Assert.False(sheet.ShowExtra);
			Assert.False(window.FindNamed<StackPanel>("ControllerSheetExtra").IsOnScreen());

			//A pad, but a console with no player port the sheet knows: still no
			//section, and no rows were built for it either.
			sheet.CurrentConsole = () => ConsoleType.Snes;
			sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
			sheet.ApplyPad();
			Assert.False(sheet.ShowExtra);

			//A port back, and the capture an arm opened ends when the pad does.
			sheet.CurrentConsole = () => ConsoleType.Nes;
			sheet.ApplyPad();
			Assert.True(sheet.ShowExtra);
			sheet.ArmExtra(EmulatorShortcut.Rewind);
			Assert.True(sheet.IsCapturing);

			sheet.Tester.Gamepads.Clear();
			sheet.ApplyPad();

			Assert.False(sheet.ShowExtra);
			Assert.False(sheet.IsCapturing);
			//The bridge's flag goes with it: the sheet is not waiting for anything.
			Assert.False(model.IsControllerCapturing);
			Assert.Null(rewind.PadBinding);
		} finally {
			rewind.PadBinding = savedBinding;
			ConfigManager.Config.Nes = savedNes;
		}
	}

	//The shortcut entry a row edits: the one the classic Input page's list holds,
	//found the way that page finds it.
	private static ShortcutKeyInfo ShortcutOf(EmulatorShortcut action)
	{
		return ConfigManager.Config.Preferences.ShortcutKeys.Find(sk => sk.Shortcut == action)!;
	}

	//Every console's player ports, emptied: the state the keyboard guard
	//("nothing is bound anywhere") reads.
	private static void ClearPlayerPorts()
	{
		ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
		ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
		ConfigManager.Config.Gameboy.Controller = new ControllerConfig();
		ConfigManager.Config.Gba.Controller = new ControllerConfig();
		ConfigManager.Config.Sms.Port1 = new SmsControllerConfig();
		ConfigManager.Config.Sms.Port2 = new SmsControllerConfig();
	}
}

//The sheet's coupling to the core that CI can check without a core: the pad's
//button order is *per backend*, and a drawn key has to read the bit the core
//reports for that button on the backend the pad came through. These read the
//three backends' own key tables off disk (Windows/WindowsKeyManager.cpp,
//MacOS/MacOSKeyManager.mm, Linux/LinuxKeyManager.cpp) and fail when the C# order
//mirrored in UI/Logic/ControllerSheet.cs drifts from them - the link the core
//unit test's own literals cannot close - and pin Core/Shared/GamepadButtonOrder.h
//to the same order. Not in NativeCoreCollection - a GamepadTestItem is built from
//no device read, and nothing here touches the tester's poll - so it runs on the
//CI runner instead of self-skipping.
public class ControllerSheetPadTests
{
	//The tester's chip labels are the core's own names for the pad's backend, so a
	//GamepadTestItem for the GameController backend is the macOS row
	//(MacOSKeyManager.mm): A 0, B 1, L1 4, R1 5, Start 6, Select 7, D-pad 8..11.
	private static readonly (string Label, int Bit)[] MacCoreButtons = {
		("A", 0), ("B", 1), ("L1", 4), ("R1", 5), ("Start", 6), ("Select", 7),
		("Up", 8), ("Down", 9), ("Left", 10), ("Right", 11)
	};

	[AvaloniaFact]
	public void The_macos_tester_labels_still_read_the_cores_own_bits()
	{
		GamepadTestItem pad = new(0) { Backend = GamepadBackend.GameController };
		foreach((string label, int bit) in MacCoreButtons) {
			Assert.Equal(label, pad.Buttons[bit].Label);
		}
		//Ten keys, ten buttons: the drawn pad is a console pad, and every one of its
		//keys reads a button the tester really reports.
		Assert.Equal(10, ControllerLivePad.Keys.Length);
		Assert.All(ControllerLivePad.Keys, key => Assert.NotNull(ControllerLivePad.BitOf(key, GamepadBackend.GameController)));
	}

	//#817: the value chips are the same per-backend order the drawn keys read. On
	//XInput the core's bit 12 is A and bit 0 is D-pad Up, so the A key and the chip
	//labelled A light together - before this rule the chip list was the macOS order
	//for every backend, and the pad's A lit a chip labelled LT.
	[AvaloniaFact]
	public void The_value_chips_are_labelled_in_the_pads_own_button_order()
	{
		//Every bit the chip list carries is the core's own name for that bit, for
		//every backend the sheet can place.
		foreach(GamepadBackend backend in new[] { GamepadBackend.GameController, GamepadBackend.XInput, GamepadBackend.Evdev }) {
			GamepadTestItem pad = new(0) { Backend = backend };
			for(int bit = 0; bit < pad.Buttons.Count; bit++) {
				Assert.Equal(ControllerLivePad.NameOfBit(backend, bit), pad.Buttons[bit].Label);
			}
		}

		//The concrete defect.
		GamepadTestItem xinput = new(0) { Backend = GamepadBackend.XInput };
		Assert.Equal("A", xinput.Buttons[12].Label);
		Assert.Equal("Up", xinput.Buttons[0].Label);
		Assert.Equal("L1", xinput.Buttons[8].Label);
	}

	//The defect that reached main with the Controller sheet (#817): the sheet's table
	//was the macOS one for
	//every backend, so on Windows bit 0 - the XInput D-pad Up - lit the sheet's A
	//key, and no test could see it. The cores' Windows order is xinput j -> bit
	//j-1 (Windows/WindowsKeyManager.cpp): 0..3 the D-pad, 4 Start, 5 Back, 8/9 the
	//shoulders, 12/13 A/B.
	[AvaloniaFact]
	public void An_xinput_pad_lights_the_core_buttons_an_xinput_pad_reports()
	{
		GamepadTestItem pad = new(0) { Backend = GamepadBackend.XInput };
		pad.Buttons[0].IsPressed = true; //D-pad Up on XInput
		Assert.False(Lit(pad, SetupButton.A));
		Assert.True(Lit(pad, SetupButton.Up));

		pad.Buttons[0].IsPressed = false;
		pad.Buttons[12].IsPressed = true; //A on XInput
		Assert.True(Lit(pad, SetupButton.A));
		Assert.False(Lit(pad, SetupButton.Up));
	}

	//Linux/evdev (Linux/LinuxGameController.cpp): 0 A, 1 B, 6/7 TL/TR, 10/11
	//SELECT/START; its D-pad is reported as axes at bits 26-29, outside the 24
	//GamepadState carries, so the drawn D-pad has no bit there and stays dark.
	[AvaloniaFact]
	public void An_evdev_pad_lights_the_core_buttons_an_evdev_pad_reports()
	{
		GamepadTestItem pad = new(0) { Backend = GamepadBackend.Evdev };
		pad.Buttons[0].IsPressed = true; //BTN_A
		pad.Buttons[1].IsPressed = true; //BTN_B
		Assert.True(Lit(pad, SetupButton.A));
		Assert.True(Lit(pad, SetupButton.B));

		foreach((int bit, SetupButton key) in new[] { (6, SetupButton.L), (7, SetupButton.R), (10, SetupButton.Select), (11, SetupButton.Start) }) {
			Assert.False(Lit(pad, key));
			pad.Buttons[bit].IsPressed = true;
			Assert.True(Lit(pad, key));
			pad.Buttons[bit].IsPressed = false;
		}

		foreach(SetupButton key in new[] { SetupButton.Up, SetupButton.Down, SetupButton.Left, SetupButton.Right }) {
			Assert.Null(ControllerLivePad.BitOf(key, GamepadBackend.Evdev));
		}
	}

	//Core/Shared/GamepadButtonOrder.h carries the ten console keys of the per-backend
	//order; this pins it to the C# order (already read off the backends by
	//Every_backends_key_table_matches_ControllerLivePad below), so a reorder on either
	//side fails here - and transitively the header is pinned to the backends too.
	[AvaloniaFact]
	public void Every_backend_table_matches_the_cores_button_order_header()
	{
		Dictionary<(GamepadBackend Backend, SetupButton Key), int> core = ReadCoreButtonOrder();
		Assert.NotEmpty(core);

		//The header's side: each row lights exactly the key it names, from a pad
		//that reports only that bit.
		foreach(var row in core) {
			GamepadTestItem pad = new(0) { Backend = row.Key.Backend };
			pad.Buttons[row.Value].IsPressed = true;
			foreach(SetupButton key in ControllerLivePad.Keys) {
				Assert.Equal(key == row.Key.Key, Lit(pad, key));
			}
		}

		//The sheet's side: nothing in its table the header does not carry, and
		//nothing the header carries that its table drops.
		foreach(GamepadBackend backend in Enum.GetValues<GamepadBackend>()) {
			foreach(SetupButton key in ControllerLivePad.Keys) {
				int? bit = ControllerLivePad.BitOf(key, backend);
				bool inHeader = core.TryGetValue((backend, key), out int headerBit);
				Assert.True(bit is int value ? (inHeader && headerBit == value) : !inHeader,
					$"{backend}.{key}: sheet says {(bit is int b ? b.ToString() : "none")}, GamepadButtonOrder.h says {(inHeader ? headerBit.ToString() : "none")}");
			}
		}
	}

	//The link the core unit test's own literals cannot close: the three backends' key
	//tables are on disk, so the C# mirror can be read back against them directly. For
	//every backend the sheet places, bit i is the name the backend reports at bit i -
	//WindowsKeyManager.cpp's table is 1-based (its key code is ...+ j + 1, and
	//XInputManager.cpp shifts by j), macOS/evdev's are 0-based, so all three land on
	//bit == index. This is what makes a value transcribed wrongly into the mirror
	//(or into GamepadButtonOrder.h, which the test above pins to the mirror) visible.
	[AvaloniaFact]
	public void Every_backends_key_table_matches_ControllerLivePad()
	{
		Dictionary<GamepadBackend, string[]> backend = new() {
			[GamepadBackend.XInput] = ReadButtonNames("Windows", "WindowsKeyManager.cpp"),
			[GamepadBackend.GameController] = ReadButtonNames("MacOS", "MacOSKeyManager.mm"),
			[GamepadBackend.Evdev] = ReadButtonNames("Linux", "LinuxKeyManager.cpp")
		};

		foreach((GamepadBackend kind, string[] names) in backend) {
			//Only the bits GamepadState carries are mirrored; a table may name more.
			for(int bit = 0; bit < 24; bit++) {
				Assert.Equal(names[bit], ControllerLivePad.NameOfBit(kind, bit));
			}
		}

		//What is not mirrored is deliberate and checked: evdev's D-pad lives past the
		//24 bits - the hat is reported as axes there - so the four keys stay dark
		//rather than lighting from a guess.
		foreach(string dpad in new[] { "Up", "Down", "Left", "Right" }) {
			Assert.True(Array.IndexOf(backend[GamepadBackend.Evdev], dpad) >= 24,
				$"evdev's {dpad} moved under the 24-bit window - it now has a drawn key");
			Assert.Null(ControllerLivePad.BitOf(Enum.Parse<SetupButton>(dpad), GamepadBackend.Evdev));
		}

		//DirectInput is raw joystick buttons (diButtonNames in the same file), with no
		//console button to name one by, so the sheet lists none of its keys.
		Assert.DoesNotContain(GamepadBackend.DirectInput, backend.Keys);
		Assert.Null(ControllerLivePad.NameOfBit(GamepadBackend.DirectInput, 0));
		Assert.Null(ControllerLivePad.BitOf(SetupButton.A, GamepadBackend.DirectInput));
	}

	//The backend's own name table, in order: `vector<string> buttonNames = { ... }`.
	private static string[] ReadButtonNames(string folder, string file)
	{
		string path = Path.Combine(FindRepoRoot(), folder, file);
		Assert.True(File.Exists(path), path + " does not exist - the backend's key table is the sheet's source of truth");
		Match table = Regex.Match(File.ReadAllText(path), @"vector<string>\s+buttonNames\s*=\s*\{(.*?)\};", RegexOptions.Singleline);
		Assert.True(table.Success, $"no buttonNames table found in {path}");
		string[] names = Regex.Matches(table.Groups[1].Value, "\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();
		Assert.True(names.Length >= 24, $"{path} names only {names.Length} buttons; the sheet mirrors 24");
		return names;
	}

	[AvaloniaFact]
	public void A_key_lights_only_from_its_own_button()
	{
		GamepadTestItem pad = new(0) { Backend = GamepadBackend.GameController };
		pad.Buttons[ControllerLivePad.BitOf(SetupButton.B, GamepadBackend.GameController)!.Value].IsPressed = true;

		ControllerPadLight[] keys = ControllerLivePad.Keys.Select(b => new ControllerPadLight(b)).ToArray();
		foreach(ControllerPadLight key in keys) {
			key.Follow(pad);
		}

		Assert.Equal(new[] { SetupButton.B }, keys.Where(k => k.IsLit).Select(k => k.Button).ToArray());

		//No pad at all, a pad whose backend the sheet cannot place, and a pad that
		//reports fewer buttons than the core's list, all leave every key dark rather
		//than throwing.
		foreach(ControllerPadLight key in keys) {
			key.Follow(null);
		}
		Assert.All(keys, k => Assert.False(k.IsLit));

		pad.Buttons.Clear();
		foreach(ControllerPadLight key in keys) {
			key.Follow(pad);
		}
		Assert.All(keys, k => Assert.False(k.IsLit));

		foreach(ControllerPadLight key in keys) {
			key.Follow(new GamepadTestItem(0));
		}
		Assert.All(keys, k => Assert.False(k.IsLit));
	}

	private static bool Lit(GamepadTestItem pad, SetupButton key)
	{
		ControllerPadLight light = new(key);
		light.Follow(pad);
		return light.IsLit;
	}

	//One row per (backend, button, bit) in Core/Shared/GamepadButtonOrder.h:
	//{ GamepadBackend::XInput, PadButton::A, 12 }. The regex is deliberately this
	//strict, so a row that changes shape fails loudly instead of parsing as none.
	private static readonly Regex HeaderRow = new(
		@"\{\s*GamepadBackend::(\w+)\s*,\s*PadButton::(\w+)\s*,\s*(\d+)\s*\}", RegexOptions.Compiled);

	private static Dictionary<(GamepadBackend, SetupButton), int> ReadCoreButtonOrder()
	{
		string path = Path.Combine(FindRepoRoot(), "Core", "Shared", "GamepadButtonOrder.h");
		Assert.True(File.Exists(path), path + " does not exist - the Core's per-backend button order is the sheet's source of truth");
		Dictionary<(GamepadBackend, SetupButton), int> rows = new();
		foreach(Match m in HeaderRow.Matches(File.ReadAllText(path))) {
			GamepadBackend backend = Enum.Parse<GamepadBackend>(m.Groups[1].Value);
			SetupButton button = Enum.Parse<SetupButton>(m.Groups[2].Value);
			Assert.True(rows.TryAdd((backend, button), int.Parse(m.Groups[3].Value)),
				$"{backend}.{button} is listed twice in GamepadButtonOrder.h");
		}
		return rows;
	}

	private static string FindRepoRoot()
	{
		DirectoryInfo? dir = new(AppContext.BaseDirectory);
		while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
			dir = dir.Parent;
		}
		if(dir == null) {
			throw new InvalidOperationException("Could not locate repo root (Mesen.sln) from " + AppContext.BaseDirectory);
		}
		return dir.FullName;
	}
}
