using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//P.4 / PRD Part B §6, plan subsection 2B: "the Player Settings reduced tab set
//rendering"; since G.4, PRD Part B §13.5.2 W-P8. `PlayerSettingsEssentials`
//(which tab is in the Player strip, and in what order) is covered host-free in
//UI.Tests; what is covered here is the step after it - that Player mode's
//Settings sheet (PlayerSettingsSheetView, ADR-0249) shows its own strip
//(Display | Look | Audio | Controls) with the hint and Done and no Advanced tab
//list, and that Advanced's ConfigWindow still shows every tab and no strip.
[NativeCoreFree("Opens the Settings sheet (with injected device and pad sources) and ConfigWindow on the Input tab; only the classic Audio/Video/Display/Look tab view-models reach ConfigApi/EmuApi on construction.")]
public class PlayerSettingsTabsTests
{
	//Input is the tab a host-free run opens: its classic view-model does not
	//reach the native core on construction (Audio enumerates devices through
	//ConfigApi, Display and Look read the core). The tab bars under test are
	//the same either way.
	private static ConfigWindow ShowAdvancedSettings()
	{
		ConfigWindow window = new(ConfigWindowTab.Input);
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return window;
	}

	//ADR-0249: Player mode's Settings is a sheet MainWindow hosts in its Player
	//scope; here the same view in a bare Player-scoped window, core-free.
	private static Window ShowPlayerSheet()
	{
		PlayerSettingsSheetView sheet = new() { DataContext = new ConfigViewModel(ConfigWindowTab.Input, playerMode: true, audioDevices: () => new[] { "Speakers" }, connectedPads: () => 0) };
		Window window = new() { Width = 1100, Height = 740, Content = new Panel { Classes = { "player" }, Children = { sheet } } };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return window;
	}

	[AvaloniaFact]
	public void Player_settings_shows_the_display_look_audio_controls_strip()
	{
		Window window = ShowPlayerSheet();

		TabControl strip = window.FindNamed<TabControl>("PlayerSettingsTabs");
		Assert.True(strip.IsOnScreen());
		List<TabItem> tabs = strip.Items.Cast<TabItem>().ToList();
		Assert.Equal(PlayerSettingsEssentials.Tabs.Length, tabs.Count);
		Assert.Equal(new[] { "Display", "Look", "Audio", "Controls" }, tabs.Select(t => t.Header as string).ToArray());
		Assert.All(tabs, tab => Assert.True(tab.IsOnScreen()));
		Assert.Equal(PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Input), tabs.FindIndex(t => t.IsSelected));

		//The Advanced tab list is not there; neither are OK/Cancel - the hint
		//and Done take their place (rule 10, W-P8's five elements).
		Assert.Empty(window.FindAll<TabControl>().Where(t => t.Name == "AdvancedSettingsTabs"));
		Assert.True(window.FindNamed<Button>("btnPlayerSettingsDone").IsOnScreen());
		Assert.Equal("Everything else: Tools ⋯ › Options", window.FindNamed<TextBlock>("lblPlayerSettingsEverythingElse").Text);
		Assert.DoesNotContain(window.FindAll<Button>().Where(b => b.IsOnScreen()), b => b.Content as string is "OK" or "Cancel");
	}

	[AvaloniaFact]
	public void Advanced_settings_still_shows_every_tab()
	{
		//Guards against the reduction being unconditional rather than bound to
		//PlayerMode - the failure mode a grep of the markup cannot tell apart.
		ConfigWindow window = ShowAdvancedSettings();

		List<TabItem> tabs = window.FindNamed<TabControl>("AdvancedSettingsTabs").Items.Cast<TabItem>().ToList();
		Assert.Equal(ConfigWindowTabOrder.Tabs.Length, tabs.Count);
		Assert.All(tabs, tab => Assert.True(tab.IsOnScreen()));
		Assert.Empty(window.FindAll<TabControl>().Where(t => t.Name == "PlayerSettingsTabs"));
	}

	//W-P8's Scale popup was blank for a window under 1× (#audit): the nearest
	//offered scale shows instead, and showing it does not resize the window.
	[AvaloniaFact]
	public void Scale_shows_the_nearest_offered_value_below_one()
	{
		double? resized = null;
		PlayerDisplaySettingsViewModel display = new(new VideoConfig(), false, 0.5, () => { }, s => resized = s);
		Assert.NotNull(display.SelectedScale);
		Assert.Equal(1, display.SelectedScale!.Value);
		Assert.Null(resized);
		display.Dispose();
	}
}
