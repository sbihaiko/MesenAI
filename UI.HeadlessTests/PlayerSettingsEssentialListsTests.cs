using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;

namespace Mesen.HeadlessTests;

//Bug (user: "essas telas estao MUITO feias e amontoadas"): Settings > Audio and
//Controls embedded the whole classic option pages - big-font sub-tabs, a
//cut-off per-console button row, horizontal and vertical scrollbars. They are
//now Display's pattern: one inset list of three rows, the "More in Options..."
//link in the hint's place, the same 340 px sheet. The rules live host-free in
//UI.Tests/Config/PlayerSettingsEssentialsTests; this checks the XAML crossing.
[NativeCoreFree("Binds the Audio and Controls essentials with injected device and pad sources; nothing here changes a value, so no config write reaches the native core.")]
public class PlayerSettingsEssentialListsTests
{
	private static readonly string[] Devices = { "Speakers", "Headset" };

	private static (Window Window, ConfigViewModel Model, PlayerSettingsSheetView Sheet) Show(ConfigWindowTab tab)
	{
		ConfigViewModel model = new(tab, playerMode: true, audioDevices: () => Devices, connectedPads: () => 2);
		PlayerSettingsSheetView sheet = new() { DataContext = model };
		Window window = new() { Width = 700, Height = 520, Content = new Panel { Classes = { "player" }, Children = { sheet } } };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
		return (window, model, sheet);
	}

	private static void AssertNoClassicChrome(Window window, PlayerSettingsSheetView sheet, string group)
	{
		Assert.Empty(window.FindAll<AudioConfigView>());
		Assert.Empty(window.FindAll<InputConfigView>());
		//The strip is the only TabControl: no General/Equalizer/Advanced or
		//General/Display/Test sub-tabs, and no per-console button row.
		Assert.Equal(new[] { "PlayerSettingsTabs" }, window.FindAll<TabControl>().Where(t => t.IsOnScreen()).Select(t => t.Name).ToArray());
		Assert.Empty(window.FindAll<ScrollViewer>().Where(s => s.IsOnScreen()));
		Assert.Empty(window.FindAll<ScrollBar>().Where(s => s.IsOnScreen()));
		Assert.Empty(window.FindAll<CheckBox>().Where(c => c.IsOnScreen()));
		Border list = sheet.FindNamed<Border>(group);
		Assert.True(list.IsOnScreen());
		Assert.True(list.Bounds.Width <= sheet.FindNamed<Border>("PlayerSettingsSheet").Bounds.Width, "no horizontal overflow");
		Assert.Equal(340, sheet.FindNamed<Border>("PlayerSettingsSheet").Bounds.Height, 0.5);
		Assert.Equal(PlayerSettingsEssentials.Rows(group == "AudioSettingsGroup" ? ConfigWindowTab.Audio : ConfigWindowTab.Input).Count,
			list.FindAll<DockPanel>().Count(d => d.Classes.Contains("setting-row")));
		Assert.True(sheet.FindNamed<Button>("btnPlayerSettingsMoreInOptions").IsOnScreen());
		Assert.False(sheet.FindNamed<TextBlock>("lblPlayerSettingsEverythingElse").IsOnScreen());
	}

	[AvaloniaFact]
	public void Audio_is_a_three_row_list_with_no_classic_page()
	{
		(Window window, ConfigViewModel model, PlayerSettingsSheetView sheet) = Show(ConfigWindowTab.Audio);
		AssertNoClassicChrome(window, sheet, "AudioSettingsGroup");
		Assert.IsType<ToggleSwitch>(sheet.FindNamed<ToggleButton>("chkAudioEnabled"));
		Assert.NotNull(sheet.FindNamed<Slider>("sldAudioVolume"));
		Assert.NotNull(sheet.FindNamed<ComboBox>("cboAudioDevice"));
		PlayerRender.Save(PlayerRender.Capture(window), "Settings-Audio");
		//The line under the group follows the tab: Display has the hint, not the link.
		sheet.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Display);
		Dispatcher.UIThread.RunJobs();
		Assert.False(sheet.FindNamed<Button>("btnPlayerSettingsMoreInOptions").IsOnScreen());
		Assert.True(sheet.FindNamed<TextBlock>("lblPlayerSettingsEverythingElse").IsOnScreen());
		model.Dispose();
	}

	[AvaloniaFact]
	public void Controls_is_a_three_row_list_with_no_classic_page()
	{
		(Window window, ConfigViewModel model, PlayerSettingsSheetView sheet) = Show(ConfigWindowTab.Input);
		AssertNoClassicChrome(window, sheet, "ControlsSettingsGroup");
		Assert.Equal("2 controllers connected", sheet.FindNamed<TextBlock>("txtControlsPads").Text);
		Assert.NotNull(sheet.FindNamed<Slider>("sldControlsRumble"));
		Assert.NotNull(sheet.FindNamed<Slider>("sldControlsDeadzone"));
		PlayerRender.Save(PlayerRender.Capture(window), "Settings-Controls");
		model.Dispose();
	}

	//Restore-not-clobber: a device and a volume set in Options show as the
	//current values and are not rewritten by opening the tab.
	[AvaloniaFact]
	public void A_value_set_in_Options_is_the_current_item_and_survives()
	{
		AudioConfig audio = ConfigManager.Config.Audio;
		(string device, uint volume) = (audio.AudioDevice, audio.MasterVolume);
		audio.AudioDevice = "USB DAC (set in Options)";
		audio.MasterVolume = 37;
		try {
			(Window window, ConfigViewModel model, PlayerSettingsSheetView sheet) = Show(ConfigWindowTab.Audio);
			Assert.Equal("USB DAC (set in Options)", sheet.FindNamed<ComboBox>("cboAudioDevice").SelectedItem);
			Assert.Equal(37, sheet.FindNamed<Slider>("sldAudioVolume").Value);
			Assert.Equal("USB DAC (set in Options)", audio.AudioDevice);
			Assert.Equal(37u, audio.MasterVolume);
			model.Dispose();
		} finally {
			audio.AudioDevice = device;
			audio.MasterVolume = volume;
		}
	}

	//"More in Options..." is the Look mechanism: PlayerMode turns off and the
	//view-model's tab is the classic page the window opens on. Audio's twin
	//needs the core (the classic Audio view-model enumerates devices), so it is
	//PlayerSettingsMoreInOptionsAudioTests.
	//
	//ADR-0255 slice 1: Controls is the one row the window reroutes (to the Play
	//Controller sheet), and this is that reroute's fallback - a view nobody wired
	//(the designer, a sheet tested alone) still reaches the page.
	[AvaloniaFact]
	public void More_in_Options_without_the_window_still_expands_to_the_classic_controls_page()
	{
		(Window window, ConfigViewModel model, PlayerSettingsSheetView sheet) = Show(ConfigWindowTab.Input);
		try {
			List<string> changed = new();
			model.PropertyChanged += (s, e) => changed.Add(e.PropertyName ?? "");
			sheet.FindNamed<Button>("btnPlayerSettingsMoreInOptions").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
			Dispatcher.UIThread.RunJobs();
			Assert.False(model.PlayerMode);
			Assert.Equal(ConfigWindowTab.Input, model.SelectedIndex);
			Assert.Contains(nameof(ConfigViewModel.PlayerMode), changed);
			//The classic page's view-model exists now; the essentials one stays for Cancel.
			Assert.NotNull(model.Input);
			Assert.Same(model.PlayerControls!.OriginalConfig, model.Input!.OriginalConfig);
		} finally {
			model.Dispose();
		}
	}

	//ADR-0255 slice 1: with the window listening, Controls' row asks it for the
	//Controller sheet instead - and never touches this view-model, because the
	//sheet's own Done/Esc are what come back (the essentials one stays intact
	//underneath for the Cancel that never happens).
	[AvaloniaFact]
	public void More_in_Options_on_controls_asks_the_window_for_the_controller_sheet()
	{
		(Window window, ConfigViewModel model, PlayerSettingsSheetView sheet) = Show(ConfigWindowTab.Input);
		try {
			int asked = 0;
			sheet.ControllerSheetRequested += (s, e) => asked++;
			List<string> changed = new();
			model.PropertyChanged += (s, e) => changed.Add(e.PropertyName ?? "");
			sheet.FindNamed<Button>("btnPlayerSettingsMoreInOptions").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
			Dispatcher.UIThread.RunJobs();

			Assert.Equal(1, asked);
			//The window owns the rest, so nothing here has moved on.
			Assert.True(model.PlayerMode);
			Assert.Equal(ConfigWindowTab.Input, model.SelectedIndex);
			Assert.DoesNotContain(nameof(ConfigViewModel.PlayerMode), changed);
			Assert.Null(model.Input);
		} finally {
			model.Dispose();
		}
	}
}

[Collection(NativeCoreCollection.Name)]
public class PlayerSettingsMoreInOptionsAudioTests
{
	[AvaloniaFact]
	public void More_in_Options_expands_to_the_classic_audio_page()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigViewModel model = new(ConfigWindowTab.Audio, playerMode: true, audioDevices: () => new[] { "Speakers" }, connectedPads: () => 0);
		PlayerSettingsSheetView sheet = new() { DataContext = model };
		Window window = new() { Width = 700, Height = 520, Content = new Panel { Classes = { "player" }, Children = { sheet } } };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		try {
			sheet.FindNamed<Button>("btnPlayerSettingsMoreInOptions").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
			Dispatcher.UIThread.RunJobs();
			Assert.False(model.PlayerMode);
			Assert.Equal(ConfigWindowTab.Audio, model.SelectedIndex);
			Assert.NotNull(model.Audio);
			Assert.Same(model.PlayerAudio!.OriginalConfig, model.Audio!.OriginalConfig);
		} finally {
			model.Dispose();
		}
	}
}
