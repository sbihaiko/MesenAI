using System;
using System.Linq;
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

//W-P5's "No pack" row (PRD Part B §13.5.2): the rules (sentinel, resolver,
//picker, auto-install gate) are pinned host-free in UI.Tests/Mep/
//NoPackPreferenceTests and the core's side in scripts/core_unit_tests.cpp;
//this checks the realized sheets: the row sits last with the render's copy,
//picking it stores the choice and leaves no current pack (W-P4's Pack row and
//the status line), the next load stays silent, W-P6 shows the choice and the
//way back, and picking a pack again restores it.
[Collection(NativeCoreCollection.Name)]
public class NoPackRowTests : IDisposable
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
	private readonly bool _enhancedAudio = ConfigManager.Config.Audio.EnableEnhancedAudio;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		ConfigManager.Config.Audio.EnableEnhancedAudio = _enhancedAudio;
		//A pick saves the choice to the test home's settings.json; save the
		//reset too, or the next run starts with it stored.
		ConfigManager.Config.EnhancementPacks.SetRomPackPreference(Sha1, "");
		ConfigManager.Config.Save();
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

	private static RadioButton[] Radios(MainWindow window) =>
		window.FindNamed<ItemsControl>("PackPickerList").FindAll<RadioButton>().ToArray();

	private static string[] Texts(Control row) =>
		row.FindAll<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToArray();

	[AvaloniaFact]
	public void The_no_pack_row_sits_last_with_the_render_copy()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.EnableEnhancedAudio = true;
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		Assert.True(model.OpenPackFromOverlay(TwoPacks, Sha1, "/packs", "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();

		RadioButton[] radios = Radios(window);
		Assert.Equal(3, radios.Length);
		Assert.Equal(new[] { "No pack", "Play with enhanced audio only" }, Texts(radios[2]));
		//No stored choice: the 👍-first pack starts selected, not "No pack".
		Assert.True(radios[0].IsChecked);
		Assert.False(radios[2].IsChecked);
	}

	[AvaloniaFact]
	public void With_enhanced_audio_off_the_row_does_not_promise_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Audio.EnableEnhancedAudio = false;
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		Assert.True(model.OpenPackFromOverlay(TwoPacks, Sha1, "/packs", "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(new[] { "No pack", "Play with the game's original art and sound" }, Texts(Radios(window)[2]));
	}

	[AvaloniaFact]
	public void Picking_no_pack_turns_the_pack_off_and_picking_a_pack_restores_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();

		Assert.True(model.OpenPackFromOverlay(TwoPacks, Sha1, "/packs", "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();
		RadioButton noPack = Radios(window)[2];
		noPack.IsChecked = true;
		Click(noPack);
		Click(window.FindNamed<Button>("PackPickerUseButton"));

		Assert.Equal(PackPreferenceResolver.NoPack, ConfigManager.Config.EnhancementPacks.GetRomPackPreference(Sha1));
		Assert.False(window.FindNamed<Border>("PlayerPackPicker").IsOnScreen());
		//W-P4's Pack row and the status line name no pack.
		Assert.Equal("", model.CurrentPackName);
		Assert.Equal("none", model.PackSummary);
		Assert.DoesNotContain("Aaa Pack", model.Shell.StatusText);

		//Next load: the choice applies silently and still names no pack.
		Assert.False(model.EvaluatePlayerPackPicker(TwoPacks, Sha1));
		Assert.False(model.IsPlayerPackPickerVisible);
		Assert.Equal("", model.CurrentPackName);

		//Changing it later: the picker reopens on "No pack", and a pack brings the art back.
		Assert.True(model.OpenPackFromOverlay(TwoPacks, Sha1, "/packs", "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();
		RadioButton[] radios = Radios(window);
		Assert.True(radios[2].IsChecked);
		radios[0].IsChecked = true;
		Click(radios[0]);
		Click(window.FindNamed<Button>("PackPickerUseButton"));

		Assert.Equal("issue-1", ConfigManager.Config.EnhancementPacks.GetRomPackPreference(Sha1));
		Assert.Equal("Aaa Pack", model.CurrentPackName);
	}

	[AvaloniaFact]
	public void Pack_detail_shows_the_no_pack_choice_and_the_way_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame();
		ConfigManager.Config.EnhancementPacks.SetRomPackPreference(Sha1, PackPreferenceResolver.NoPack);

		//One pack: W-P4's Pack row inspects it (W-P6), which shows the choice.
		Assert.False(model.OpenPackFromOverlay(OnePack, Sha1, "/packs", "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
		Assert.Equal("No pack", window.FindNamed<TextBlock>("PackDetailTitle").Text);
		Assert.Equal("You chose to play this game without a pack.", window.FindNamed<TextBlock>("PackDetailByline").Text);
		Assert.False(window.FindNamed<StackPanel>("PackDetailLayers").IsOnScreen());
		Assert.Equal("", model.CurrentPackName);
		//The way back: Change Pack… opens the picker, "No pack" selected.
		Button change = window.FindNamed<Button>("PackDetailChangeButton");
		Assert.True(change.IsEnabled);
		Assert.False(window.FindNamed<TextBlock>("PackDetailChangeReason").IsOnScreen());

		model.ChangePackFromDetail(OnePack);
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<Border>("PlayerPackPicker").IsOnScreen());
		RadioButton[] radios = Radios(window);
		Assert.Equal(2, radios.Length);
		Assert.True(radios[1].IsChecked);
		Assert.Equal("Aaa Pack", Texts(radios[0])[0]);
	}
}
