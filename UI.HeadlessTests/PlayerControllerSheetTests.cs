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
using Mesen.Config;
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

	//A gamepad key as the key manager numbers it (0x1000 + device*0x100 + button):
	//what "which pad" is read from, since a port is only a ControllerConfig.
	private static ushort PadButton(int device, int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);

	private static Button[] PlayerRows(Visual root) => root.FindAll<Button>().Where(b => b.Name == "ControllerSheetPlayerRow").ToArray();

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
			BackendKind = GamepadBackend.GameController,
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
			//device the port's keys carry.
			sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad One" });
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
			sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad One" });
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
		GamepadTestItem pad = new(0) { BackendKind = GamepadBackend.GameController };
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
			GamepadTestItem pad = new(0) { BackendKind = backend };
			for(int bit = 0; bit < pad.Buttons.Count; bit++) {
				Assert.Equal(ControllerLivePad.NameOfBit(backend, bit), pad.Buttons[bit].Label);
			}
		}

		//The concrete defect.
		GamepadTestItem xinput = new(0) { BackendKind = GamepadBackend.XInput };
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
		GamepadTestItem pad = new(0) { BackendKind = GamepadBackend.XInput };
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
		GamepadTestItem pad = new(0) { BackendKind = GamepadBackend.Evdev };
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
			GamepadTestItem pad = new(0) { BackendKind = row.Key.Backend };
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
		GamepadTestItem pad = new(0) { BackendKind = GamepadBackend.GameController };
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
