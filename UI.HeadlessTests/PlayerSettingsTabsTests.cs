using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Logic;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//P.4 / PRD Part B §6, plan subsection 2B: "the Player Settings reduced tab set
//rendering"; since G.4, PRD Part B §13.5.2 W-P8. `PlayerSettingsEssentials`
//(which tab is in the Player strip, and in what order) is covered host-free in
//UI.Tests; what is covered here is the step after it - that ConfigWindow.axaml
//shows Player mode its own strip (Display | Look | Audio | Controls) with the
//hint and Done, hides the Advanced tab list, and that Advanced still shows
//every tab.
[NativeCoreFree("Opens ConfigWindow on the Input tab; only the Audio/Video/Display/Look tab view-models reach ConfigApi/EmuApi on construction.")]
public class PlayerSettingsTabsTests
{
	private static ConfigWindow ShowSettings(bool playerMode)
	{
		//Input is the one essentials tab whose view-model does not reach the
		//native core on construction (Audio enumerates devices through
		//ConfigApi, Display and Look read the core), so it is the tab a
		//host-free run can open. The tab bars under test are the same either way.
		ConfigWindow window = new(ConfigWindowTab.Input, playerMode);
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return window;
	}

	[AvaloniaFact]
	public void Player_settings_shows_the_display_look_audio_controls_strip()
	{
		ConfigWindow window = ShowSettings(playerMode: true);

		TabControl strip = window.FindNamed<TabControl>("PlayerSettingsTabs");
		Assert.True(strip.IsOnScreen());
		List<TabItem> tabs = strip.Items.Cast<TabItem>().ToList();
		Assert.Equal(PlayerSettingsEssentials.Tabs.Length, tabs.Count);
		Assert.Equal(new[] { "Display", "Look", "Audio", "Controls" }, tabs.Select(t => t.Header as string).ToArray());
		Assert.All(tabs, tab => Assert.True(tab.IsOnScreen()));
		Assert.Equal(PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Input), tabs.FindIndex(t => t.IsSelected));

		//The Advanced tab list is not there; neither are OK/Cancel - the hint
		//and Done take their place (rule 10, W-P8's five elements).
		Assert.False(window.FindNamed<TabControl>("AdvancedSettingsTabs").IsOnScreen());
		Assert.True(window.FindNamed<Button>("btnPlayerSettingsDone").IsOnScreen());
		Assert.Equal("Everything else: Tools ⋯ › Options", window.FindNamed<TextBlock>("lblPlayerSettingsEverythingElse").Text);
		Assert.DoesNotContain(window.FindAll<Button>().Where(b => b.IsOnScreen()), b => b.Content as string is "OK" or "Cancel");
	}

	[AvaloniaFact]
	public void Advanced_settings_still_shows_every_tab()
	{
		//Guards against the reduction being unconditional rather than bound to
		//PlayerMode - the failure mode a grep of the markup cannot tell apart.
		ConfigWindow window = ShowSettings(playerMode: false);

		List<TabItem> tabs = window.FindNamed<TabControl>("AdvancedSettingsTabs").Items.Cast<TabItem>().ToList();
		Assert.Equal(ConfigWindowTabOrder.Tabs.Length, tabs.Count);
		Assert.All(tabs, tab => Assert.True(tab.IsOnScreen()));
		Assert.False(window.FindNamed<TabControl>("PlayerSettingsTabs").IsOnScreen());
	}
}
