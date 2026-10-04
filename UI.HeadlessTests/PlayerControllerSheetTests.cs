using System;
using System.Linq;
using System.Reflection;
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
			InfoText = "SDL · Pad1 · VID:054C · PID:0CE6",
			LeftStickReadout = "X: 128  Y: -64  mag 45%",
			RightX = -32,
			RightY = 200
		};
		pad.Buttons[ControllerLivePad.BitOf(SetupButton.A)!.Value].IsPressed = true;
		sheet.Tester.Gamepads.Add(pad);
		sheet.ApplyPad();
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();

		Assert.True(sheet.HasPad);
		Assert.Equal("Wireless Controller", window.FindNamed<TextBlock>("ControllerSheetPadName").Text);
		Assert.Equal("SDL · Pad1 · VID:054C · PID:0CE6", window.FindNamed<TextBlock>("ControllerSheetPadInfo").Text);
		Assert.False(window.FindNamed<TextBlock>("ControllerSheetNoPad").IsOnScreen());
		Assert.False(window.FindNamed<TextBlock>("ControllerSheetMorePads").IsOnScreen());

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

	[AvaloniaFact]
	public void Showing_a_second_pad_says_which_one_this_is()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		OpenFromSettings(window, model);
		ControllerSheetViewModel sheet = model.ControllerSheet;
		Poll(sheet)!.Stop();

		//Slice 2 (a pad per player) is not here yet, so the sheet shows the first
		//pad and says how many it is not showing, rather than pretending there is
		//only one.
		sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad One" });
		sheet.Tester.Gamepads.Add(new GamepadTestItem(1) { Name = "Pad Two" });
		sheet.ApplyPad();
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal("Pad One", window.FindNamed<TextBlock>("ControllerSheetPadName").Text);
		Assert.Equal("Showing the first of 2 connected controllers.", window.FindNamed<TextBlock>("ControllerSheetMorePads").Text);
		Assert.True(window.FindNamed<TextBlock>("ControllerSheetMorePads").IsOnScreen());
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
}

//The one part of the sheet's coupling to the core that CI can check without a
//core: the tester's button list is what the sheet's keys read by index, and that
//list is the core's GamepadState.Buttons bit order. Not in NativeCoreCollection -
//a GamepadTestItem is built from no device read, and nothing here touches the
//tester's poll - so it runs there instead of self-skipping.
public class ControllerSheetPadTests
{
	//The core's own names, at the bits the sheet's keys read (E1.0 key order):
	//what a reordered core list would break.
	private static readonly (string Label, int Bit)[] CoreButtons = {
		("A", 0), ("B", 1), ("LB", 4), ("RB", 5), ("Menu", 6), ("Options", 7),
		("DUp", 8), ("DDown", 9), ("DLeft", 10), ("DRight", 11)
	};

	[AvaloniaFact]
	public void The_keys_read_the_core_button_the_core_reports_at_that_bit()
	{
		GamepadTestItem pad = new(0);
		foreach((string label, int bit) in CoreButtons) {
			Assert.Equal(label, pad.Buttons[bit].Label);
		}
		//Ten keys, ten buttons: the drawn pad is a console pad, and every one of its
		//keys reads a button the tester really reports.
		Assert.Equal(10, ControllerLivePad.Keys.Length);
		Assert.All(ControllerLivePad.Keys, key => Assert.NotNull(ControllerLivePad.BitOf(key)));
	}

	[AvaloniaFact]
	public void A_key_lights_only_from_its_own_button()
	{
		GamepadTestItem pad = new(0);
		pad.Buttons[ControllerLivePad.BitOf(SetupButton.B)!.Value].IsPressed = true;

		ControllerPadLight[] keys = ControllerLivePad.Keys.Select(b => new ControllerPadLight(b)).ToArray();
		foreach(ControllerPadLight key in keys) {
			key.Follow(pad);
		}

		Assert.Equal(new[] { SetupButton.B }, keys.Where(k => k.IsLit).Select(k => k.Button).ToArray());

		//No pad at all, and a pad that reports fewer buttons than the core's list,
		//leave every key dark rather than throwing.
		foreach(ControllerPadLight key in keys) {
			key.Follow(null);
		}
		Assert.All(keys, k => Assert.False(k.IsLit));

		pad.Buttons.Clear();
		foreach(ControllerPadLight key in keys) {
			key.Follow(pad);
		}
		Assert.All(keys, k => Assert.False(k.IsLit));
	}
}
