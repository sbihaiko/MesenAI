using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//W-P6's per-game layer switches (PRD Part B §13.5.2): the rules are pinned
//host-free in UI.Tests/Play/PackLayerSwitchesTests and the core's side in
//scripts/core_unit_tests.cpp; this checks the realized sheet: one switch per
//layer, a layer the pack lacks greyed with its reason, a flip stored for this
//game only and applied like any pack change, with a moving bar meanwhile.
[Collection(NativeCoreCollection.Name)]
public class PackLayerSwitchTests : IDisposable
{
	//Container, Name, Version, Author, License, Sections, Enabled, Origin(0=folder),
	//PackId, ContentId - see MepPackListParser.
	private const string OnePack = "aaa\tAaa Pack\t1.2\tTastic\tCC BY-NC 4.0\ttextures,audio\t1\t0\tissue-1\tc1\n";
	private const string Sha1 = "0000000000000000000000000000000000000000";

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _textures = ConfigManager.Config.EnhancementPacks.EnableTextures;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		ConfigManager.Config.EnhancementPacks.EnableTextures = _textures;
		//A flip saves to the test home's settings.json; save the reset too.
		ConfigManager.Config.EnhancementPacks.RomLayersOff.Remove(Sha1);
		ConfigManager.Config.Save();
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowDetail(string packList = OnePack)
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
		Assert.False(model.OpenPackFromOverlay(packList, Sha1, "/packs", "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	[AvaloniaFact]
	public void Each_layer_has_a_switch_and_a_missing_one_says_so()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowDetail();

		CheckBox textures = window.FindNamed<CheckBox>("PackDetailTexturesSwitch");
		CheckBox audio = window.FindNamed<CheckBox>("PackDetailAudioSwitch");
		CheckBox patch = window.FindNamed<CheckBox>("PackDetailPatchSwitch");
		Assert.True(textures.IsOnScreen());
		Assert.True(textures.IsChecked == true && textures.IsEnabled);
		Assert.True(audio.IsChecked == true && audio.IsEnabled);
		//The pack wires no ROM patch: a grey switch with its reason (rule 4).
		Assert.True(patch.IsOnScreen());
		Assert.False(patch.IsEnabled);
		Assert.False(patch.IsChecked == true);
		Assert.Equal("Not in this pack", window.FindNamed<TextBlock>("PackDetailPatchNote").Text);
		Assert.True(window.FindNamed<TextBlock>("PackDetailPatchNote").IsOnScreen());
		Assert.False(window.FindNamed<TextBlock>("PackDetailAudioNote").IsOnScreen());
	}

	[AvaloniaFact]
	public void A_flip_is_stored_for_this_game_and_waits_under_a_moving_bar()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowDetail();
		CheckBox audio = window.FindNamed<CheckBox>("PackDetailAudioSwitch");

		audio.IsChecked = false;

		Assert.Equal(new List<string> { "audio" }, ConfigManager.Config.EnhancementPacks.RomLayersOff[Sha1]);
		Assert.True(ConfigManager.Config.EnhancementPacks.EnableAudio, "the global switch is left alone");
		//Every visible wait moves: the switches wait under an indeterminate bar.
		Assert.True(model.PackDetailApplyingLayer);
		Assert.False(window.FindNamed<CheckBox>("PackDetailTexturesSwitch").IsEnabled);
		ProgressBar bar = window.FindNamed<ProgressBar>("PackDetailLayersWaitBar");
		Assert.True(bar.IsIndeterminate);
		Assert.True(bar.IsEffectivelyVisible);

		//NES keeps its place (ADR-0244): the sheet stays, the switches come back.
		WaitUntilApplied(model);
		Assert.True(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
		Assert.False(model.PackDetailApplyingLayer);
		Assert.False(bar.IsEffectivelyVisible);
		Assert.True(audio.IsEnabled);
		Assert.False(audio.IsChecked == true);

		//Back on: this game has no entry left.
		audio.IsChecked = true;
		WaitUntilApplied(model);
		Assert.False(ConfigManager.Config.EnhancementPacks.RomLayersOff.ContainsKey(Sha1));
	}

	//The F5 bootstrap's layer (isAutoOnly column) is shown as what it is: made
	//here, no author/version/license, and its recorded audio is not music.
	[AvaloniaFact]
	public void The_automatic_upscale_is_not_presented_as_a_pack_someone_made()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		const string auto = "Dr. Mario (1990) (Nintendo)\tDr. Mario (1990) (Nintendo)\t0.0.0\t\tunspecified\ttextures,audio\t1\t0\t\t\t1\n";
		(MainWindow window, MainWindowViewModel model) = ShowDetail(auto);

		Assert.Equal("Automatic upscale", window.FindNamed<TextBlock>("PackDetailTitle").Text);
		string byline = window.FindNamed<TextBlock>("PackDetailByline").Text ?? "";
		Assert.Equal("Dr. Mario (1990) (Nintendo)\nMade on this computer from what you played", byline);
		Assert.DoesNotContain("unknown", byline);
		Assert.DoesNotContain("unspecified", byline);
		Assert.DoesNotContain("0.0.0", byline);
		Assert.True(window.FindNamed<CheckBox>("PackDetailTexturesSwitch").IsEnabled);
		CheckBox music = window.FindNamed<CheckBox>("PackDetailAudioSwitch");
		Assert.False(music.IsEnabled);
		Assert.Equal("Not in this pack", window.FindNamed<TextBlock>("PackDetailAudioNote").Text);
	}

	//"se nao existe o pack tem que deixar claro que e um upscale automatico":
	//every surface that names the current pack says so, never the ROM name.
	[AvaloniaFact]
	public void Every_surface_names_the_automatic_upscale_not_the_rom_as_a_pack()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		const string rom = "Dr. Mario (1990) (Nintendo)";
		const string auto = rom + "\t" + rom + "\t0.0.0\tSomeone\tunspecified\ttextures,audio\t1\t0\t\t\t1\n";
		(MainWindow window, MainWindowViewModel model) = ShowDetail(auto);

		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("Automatic upscale", model.PackSummary);
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("Pack: Automatic upscale", model.EnhPackRowText);
		Assert.Equal("Automatic upscale", window.FindNamed<TextBlock>("PackDetailTitle").Text);
		string[] surfaces = { model.PackSummary, model.EnhPackRowText, window.FindNamed<TextBlock>("PackDetailTitle").Text ?? "" };
		Assert.All(surfaces, t => Assert.DoesNotContain(rom, t));
		string byline = window.FindNamed<TextBlock>("PackDetailByline").Text ?? "";
		Assert.DoesNotContain("by ", byline);
		Assert.DoesNotContain("version", byline);
		Assert.DoesNotContain("Someone", byline);
	}

	[AvaloniaFact]
	public void The_global_switch_off_wins_and_is_named()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.EnhancementPacks.EnableTextures = false;
		(MainWindow window, _) = ShowDetail();

		CheckBox textures = window.FindNamed<CheckBox>("PackDetailTexturesSwitch");
		Assert.False(textures.IsEnabled);
		Assert.False(textures.IsChecked == true);
		Assert.Equal("Off for every game — Tools ⋯ › Enhancement Packs", window.FindNamed<TextBlock>("PackDetailTexturesNote").Text);
	}

	private static void WaitUntilApplied(MainWindowViewModel model)
	{
		DateTime until = DateTime.Now.AddSeconds(30);
		while((!model.PackLayerApplied.IsCompleted || model.PackDetailApplyingLayer) && DateTime.Now < until) {
			Dispatcher.UIThread.RunJobs();
		}
		Assert.False(model.PackDetailApplyingLayer, "the layer switch never finished applying");
	}
}
