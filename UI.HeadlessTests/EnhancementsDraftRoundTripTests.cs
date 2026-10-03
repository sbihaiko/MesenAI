using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//W-P7's draft across a detour (PRD Part B §13.5.2 W-P7, ADR-0244 Decision 3):
//the four switches edit a draft that only the one button applies, so leaving
//by the Pack row - a look at the pack, not a decision about the switches - and
//coming back must show the switches the player already flipped, not the saved
//state. The rule is pinned host-free in UI.Tests/Play/EnhancementsDraftVisitTests;
//this checks the realized panel survives the round trip through W-P6 - and
//that the detour is spent by the pause it belongs to.
[Collection(NativeCoreCollection.Name)]
public class EnhancementsDraftRoundTripTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowPlayWithGame()
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		MainWindow window = new();
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
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

	[AvaloniaFact]
	public void The_draft_survives_the_round_trip_through_the_pack_row()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();

		//A switch flipped, not applied: the button is the only thing that applies it.
		CheckBox widescreen = window.FindNamed<CheckBox>("EnhancementsWidescreenCheckBox");
		widescreen.IsChecked = !widescreen.IsChecked;
		bool flipped = widescreen.IsChecked == true;
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("Apply", window.FindNamed<Button>("EnhancementsApplyButton").Content);

		//The Pack row: the pack sheet opens over the panel.
		Click(window.FindNamed<Button>("EnhancementsPackButton"));
		Assert.False(window.FindNamed<Border>("PlayerEnhancementsPanel").IsOnScreen());
		Assert.True(model.IsPackDetailVisible || model.IsPlayerPackPickerVisible, "the Pack row opened no pack sheet");

		//Back: W-P6 closes to W-P4, and W-P7 opens from there.
		model.ClosePackDetail();
		Dispatcher.UIThread.RunJobs();
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<Border>("PlayerEnhancementsPanel").IsOnScreen());
		Assert.Equal(flipped, window.FindNamed<CheckBox>("EnhancementsWidescreenCheckBox").IsChecked);
		Assert.Equal("Apply", window.FindNamed<Button>("EnhancementsApplyButton").Content);

		//The one button still applies the draft, and then the sheet is Done.
		Click(window.FindNamed<Button>("EnhancementsApplyButton"));
		Dispatcher.UIThread.RunJobs();
		Assert.False(window.FindNamed<Border>("PlayerEnhancementsPanel").IsOnScreen());
	}

	//Only the Pack row's detour carries the draft: Esc is leaving the panel, so
	//the next open reads the switches from what is applied (nothing was applied
	//by the flip).
	[AvaloniaFact]
	public void Esc_drops_the_unapplied_draft()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();

		CheckBox widescreen = window.FindNamed<CheckBox>("EnhancementsWidescreenCheckBox");
		bool applied = widescreen.IsChecked == true;
		widescreen.IsChecked = !applied;
		Dispatcher.UIThread.RunJobs();

		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(applied, window.FindNamed<CheckBox>("EnhancementsWidescreenCheckBox").IsChecked);
		Assert.Equal("Done", window.FindNamed<Button>("EnhancementsApplyButton").Content);
	}

	//The detour is the way back to the panel, not a draft that survives the
	//pause: resuming the game ends it, and the pause opened after that reads
	//the switches from what is applied.
	[AvaloniaFact]
	public void Resuming_the_game_ends_the_detour_and_the_draft_with_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();

		CheckBox widescreen = window.FindNamed<CheckBox>("EnhancementsWidescreenCheckBox");
		bool applied = widescreen.IsChecked == true;
		widescreen.IsChecked = !applied;
		Dispatcher.UIThread.RunJobs();

		//The Pack row, and back to W-P4 with the draft still held.
		Click(window.FindNamed<Button>("EnhancementsPackButton"));
		model.ClosePackDetail();
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.IsPlayerOverlayVisible);

		//Esc resumes the game - the player left the pause.
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.IsPlayerOverlayVisible);

		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(applied, window.FindNamed<CheckBox>("EnhancementsWidescreenCheckBox").IsChecked);
		Assert.Equal("Done", window.FindNamed<Button>("EnhancementsApplyButton").Content);
	}
}
