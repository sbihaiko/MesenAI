using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

//G.4 (PRD Part B §8, ADR-0241, §13.5.2 W-P5–W-P9): the XAML side of the Play
//sheets. Which sheet the Pack row opens, what a row/chip/notice says, the
//Apply label and the pill's states are pinned host-free in UI.Tests/Play
//(PlayPackSheets, EnhancementsSheet, PackInstallPill); this checks the
//realized sheets bind them: W-P4's Pack row opens W-P5 for two packs and W-P6
//for one, W-P6's disabled control shows its reason and its Restore confirms in
//place, Esc closes a sheet back to W-P4, W-P7's one button renames itself as
//the draft changes, and W-P9's sentence reaches the status line.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class PlaySheetsViewTests : IDisposable
{
	//Container, Name, Version, Author, License, Sections, Enabled, Origin(0=folder),
	//PackId, ContentId - see MepPackListParser.
	private const string OnePack = "aaa\tAaa Pack\t1.2\tTastic\tCC BY-NC 4.0\ttextures,audio\t1\t0\tissue-1\tc1\n";
	private const string TwoPacks =
		"aaa\tAaa Pack\t1.0\t\t\ttextures\t1\t0\tissue-1\tc1\n" +
		"bbb\tBbb Pack\t1.0\t\t\ttextures\t1\t0\tissue-2\tc2\n";
	private const string Sha1 = "0000000000000000000000000000000000000000";

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

	[AvaloniaFact]
	public void Pack_row_with_one_pack_opens_its_detail_and_esc_returns_to_the_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		Assert.False(model.OpenPackFromOverlay(OnePack, Sha1, "/packs", "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();

		Border detail = window.FindNamed<Border>("PlayerPackDetailSheet");
		Assert.True(detail.IsOnScreen());
		Assert.False(window.IsPauseCardActive());
		Assert.Equal("Aaa Pack", window.FindNamed<TextBlock>("PackDetailTitle").Text);
		Assert.Equal("by Tastic · version 1.2 · CC BY-NC 4.0", window.FindNamed<TextBlock>("PackDetailByline").Text);
		Assert.True(window.FindNamed<StackPanel>("PackDetailLayers").IsOnScreen());

		//Rule 4: nothing to change to - disabled, with its reason.
		Assert.False(window.FindNamed<Button>("PackDetailChangeButton").IsEnabled);
		Assert.Equal("There is no other pack for this game.", window.FindNamed<TextBlock>("PackDetailChangeReason").Text);
		Assert.True(window.FindNamed<TextBlock>("PackDetailChangeReason").IsOnScreen());
		//A local pack has nothing to restore from: absent, not disabled.
		Assert.False(window.FindNamed<Button>("PackDetailRestoreButton").IsOnScreen());
		//Ids only behind Details ▸.
		Assert.False(window.FindNamed<SelectableTextBlock>("PackDetailIds").IsOnScreen());
		window.FindNamed<ToggleButton>("PackDetailIdsToggle").IsChecked = true;
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("pack id: issue-1 · content id: c1 · container: aaa", window.FindNamed<SelectableTextBlock>("PackDetailIds").Text);
		//Focus lands on the first control that can act (Done here).
		Assert.True(window.FindNamed<Button>("PackDetailDoneButton").IsFocused);

		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.False(detail.IsOnScreen());
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	[AvaloniaFact]
	public void Restore_confirms_once_in_place_and_keep_cancels()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		model.OpenPackFromOverlay(OnePack, Sha1, "/packs", "", installedSourceSha256: "abc123");
		Dispatcher.UIThread.RunJobs();

		Button restore = window.FindNamed<Button>("PackDetailRestoreButton");
		Assert.True(restore.IsOnScreen());
		Assert.False(window.FindNamed<Border>("PackDetailRestoreConfirm").IsOnScreen());

		Click(restore);
		Assert.True(window.FindNamed<Border>("PackDetailRestoreConfirm").IsOnScreen());
		Assert.False(restore.IsOnScreen());

		Click(window.FindNamed<Button>("PackDetailRestoreKeepButton"));
		Assert.False(window.FindNamed<Border>("PackDetailRestoreConfirm").IsOnScreen());
		Assert.True(restore.IsOnScreen());

		Click(window.FindNamed<Button>("PackDetailDoneButton"));
		Assert.False(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	//The user's rule (2026-10-03): from the confirm until the restore ends,
	//the sheet shows a moving wait (the catalog fetch and match come before
	//the install pill does).
	[AvaloniaFact]
	public void A_running_restore_shows_a_moving_wait_until_it_ends()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		model.OpenPackFromOverlay(OnePack, Sha1, "/packs", "", installedSourceSha256: "abc123");
		Dispatcher.UIThread.RunJobs();
		Control wait = window.FindNamed<Control>("PackDetailRestoreWait");
		Assert.False(wait.IsOnScreen());

		Assert.False(model.PressRestore());
		Assert.True(model.PressRestore());
		Dispatcher.UIThread.RunJobs();
		Assert.True(wait.IsOnScreen());
		Assert.True(wait.FindAll<ProgressBar>().Single().IsIndeterminate);
		Assert.Equal("Restoring the pack…", window.FindNamed<TextBlock>("PackDetailRestoreWaitText").Text);

		model.RestoreFinished();
		Dispatcher.UIThread.RunJobs();
		Assert.False(wait.IsOnScreen());
	}

	[AvaloniaFact]
	public void Pack_row_with_two_packs_opens_the_picker_with_one_radio_selected()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		Assert.True(model.OpenPackFromOverlay(TwoPacks, Sha1, "/packs", "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<Border>("PlayerPackPicker").IsOnScreen());
		Assert.False(model.IsPackDetailVisible);
		Assert.StartsWith("Choose a pack for ", window.FindNamed<TextBlock>("PackPickerTitle").Text);

		RadioButton[] radios = window.FindNamed<ItemsControl>("PackPickerList").FindAll<RadioButton>().ToArray();
		//Two packs and W-P5's "No pack" row, last.
		Assert.Equal(3, radios.Length);
		Assert.True(radios[0].IsChecked);
		Assert.False(radios[1].IsChecked);
		Assert.False(radios[2].IsChecked);
		Assert.True(radios[0].IsFocused);
		Assert.True(window.FindNamed<Button>("PackPickerUseButton").IsEnabled);
		//"author unknown", not the catalog's "?" (W-P5).
		Assert.Contains(radios[0].FindAll<TextBlock>(), t => t.Text == "author unknown · 1.0 · textures");

		radios[1].IsChecked = true;
		Click(radios[1]);
		Assert.False(radios[0].IsChecked);
		Assert.True(model.PlayerPackChoices[1].IsSelected);
		Assert.False(model.PlayerPackChoices[0].IsSelected);

		//Cancel/Esc from W-P4's Pack row closes back to W-P4.
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.False(window.FindNamed<Border>("PlayerPackPicker").IsOnScreen());
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	//#691: Use This Pack from W-P4's Pack row swaps the pack in place (NES,
	//P.9), so it closes back to W-P4 - not to a paused game with no overlay.
	[AvaloniaFact]
	public void Use_this_pack_from_the_overlay_returns_to_the_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		try {
			Assert.True(model.OpenPackFromOverlay(TwoPacks, Sha1, "/packs", "", installedSourceSha256: null));
			Dispatcher.UIThread.RunJobs();
			Assert.True(model.LayerChangeKeepsPlace);

			Click(window.FindNamed<Button>("PackPickerUseButton"));

			Assert.False(window.FindNamed<Border>("PlayerPackPicker").IsOnScreen());
			Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
		} finally {
			//Use This Pack saved the choice to the test home's settings.json;
			//save the reset too, or the next run starts with it stored.
			ConfigManager.Config.EnhancementPacks.SetRomPackPreference(Sha1, "");
			ConfigManager.Config.Save();
		}
	}

	//#693: a disabled pack is neither offered nor counted - with one enabled
	//pack left, W-P4's Pack row inspects it instead of opening the picker.
	[AvaloniaFact]
	public void Pack_row_ignores_a_disabled_pack()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		const string oneDisabled =
			"aaa\tAaa Pack\t1.0\t\t\ttextures\t1\t0\tissue-1\tc1\n" +
			"bbb\tBbb Pack\t1.0\t\t\ttextures\t0\t0\tissue-2\tc2\n";

		Assert.False(model.OpenPackFromOverlay(oneDisabled, Sha1, "/packs", "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();

		Assert.False(model.IsPlayerPackPickerVisible);
		Assert.True(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
		Assert.Equal("Aaa Pack", window.FindNamed<TextBlock>("PackDetailTitle").Text);
		Assert.Equal("There is no other pack for this game.", window.FindNamed<TextBlock>("PackDetailChangeReason").Text);
		Assert.DoesNotContain(model.PlayerPackChoices, c => c.Container == "bbb");
	}

	//#703: the current pack is the one the core renders - the human sibling,
	//first in the core's order (ADR-0049) - not the first row of the
	//👍-then-name order the picker displays.
	[AvaloniaFact]
	public void Current_pack_is_the_sibling_the_core_renders()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		const string siblingThenFolder =
			"Contra\tZzz Sibling\t1.0\t\t\ttextures\t1\t2\t\t\t0\n" +
			"aaa\tAaa Pack\t1.0\t\t\ttextures\t1\t0\tissue-1\tc1\t0\n";

		Assert.False(model.OpenPackFromOverlay(siblingThenFolder, Sha1, "/packs", "/roms/Contra", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();

		Assert.Equal("Zzz Sibling", window.FindNamed<TextBlock>("PackDetailTitle").Text);
		Assert.Equal("Zzz Sibling", model.CurrentPackName);
	}

	[AvaloniaFact]
	public void Enhancements_button_names_the_restart_the_draft_causes()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();
		Button apply = window.FindNamed<Button>("EnhancementsApplyButton");
		Assert.Equal("Done", apply.Content);
		Assert.True(window.FindNamed<CheckBox>("EnhancementsTexturesCheckBox").IsFocused);

		CheckBox widescreen = window.FindNamed<CheckBox>("EnhancementsWidescreenCheckBox");
		widescreen.IsChecked = !widescreen.IsChecked;
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("Apply", apply.Content);

		CheckBox border = window.FindNamed<CheckBox>("EnhancementsBorderCheckBox");
		border.IsChecked = !border.IsChecked;
		Dispatcher.UIThread.RunJobs();
		//P.9: a layer change on NES swaps the pack in place, so it costs no reload.
		Assert.Equal("Apply", apply.Content);

		if(model.IsOverclockSupported) {
			CheckBox overclock = window.FindNamed<CheckBox>("EnhancementsOverclockCheckBox");
			overclock.IsChecked = !overclock.IsChecked;
			Dispatcher.UIThread.RunJobs();
			Assert.Equal("Apply & Restart", apply.Content);
			overclock.IsChecked = !overclock.IsChecked;
		}

		//Back to what is applied: Done again, and it only closes to W-P4.
		widescreen.IsChecked = !widescreen.IsChecked;
		border.IsChecked = !border.IsChecked;
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("Done", apply.Content);
		Click(apply);
		Assert.False(window.FindNamed<Border>("PlayerEnhancementsPanel").IsOnScreen());
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	[AvaloniaFact]
	public void Install_pill_sentence_reaches_the_status_line_and_clears()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		model.OnPackInstallStarted("Contra 80s");
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("Installing Contra 80s…", model.PackInstallPillText);
		Assert.Equal("Installing Contra 80s…", window.FindNamed<TextBlock>("ShellStatusText").Text);

		model.OnPackInstallFinished(installed: false, silent: false);
		Dispatcher.UIThread.RunJobs();
		//A core HUD/status line text: no ⚠ glyph, the words only.
		Assert.Equal("The pack could not be downloaded. Playing without it.", model.PackInstallPillText);
		Assert.Equal("The pack could not be downloaded. Playing without it.", window.FindNamed<TextBlock>("ShellStatusText").Text);

		model.OnPackInstallStarted("Contra 80s");
		model.OnPackInstallFinished(installed: true, silent: false);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("", model.PackInstallPillText);
		Assert.NotEqual("Installing Contra 80s…", window.FindNamed<TextBlock>("ShellStatusText").Text);
	}
}
