using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config
{
	//PRD Part B §6, §13.5.2 W-P8 (G.4): Player mode's Settings is one strip,
	//Display | Look | Audio | Controls, and a non-essentials initial selection
	//(e.g. Preferences from the Advanced GUI path) clamps to Display, so the
	//window never lands on a hidden tab.
	public class PlayerSettingsEssentialsTests
	{
		[Fact]
		public void The_strip_is_display_look_audio_controls_in_that_order()
		{
			Assert.Equal(new[] { ConfigWindowTab.Display, ConfigWindowTab.Look, ConfigWindowTab.Audio, ConfigWindowTab.Input }, PlayerSettingsEssentials.Tabs);
			Assert.Equal(1, PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look));
			Assert.Equal(ConfigWindowTab.Input, PlayerSettingsEssentials.TabAt(3));
			Assert.Null(PlayerSettingsEssentials.TabAt(4));
			Assert.Null(PlayerSettingsEssentials.TabAt(-1));
		}

		//W-P8: Video is split in two - Display (the window) and Look (the
		//pixels) - so Advanced's Video tab is not part of Play.
		[Fact]
		public void Video_is_not_an_essentials_tab_display_is()
		{
			Assert.True(PlayerSettingsEssentials.IsEssentials(ConfigWindowTab.Display));
			Assert.False(PlayerSettingsEssentials.IsEssentials(ConfigWindowTab.Video));
		}

		[Fact]
		public void Display_is_not_an_advanced_tab()
		{
			Assert.Equal(-1, ConfigWindowTabOrder.IndexOf(ConfigWindowTab.Display));
		}

		[Fact]
		public void NonEssentials_AreNotEssentials()
		{
			Assert.False(PlayerSettingsEssentials.IsEssentials(ConfigWindowTab.Emulation));
			Assert.False(PlayerSettingsEssentials.IsEssentials(ConfigWindowTab.Nes));
			Assert.False(PlayerSettingsEssentials.IsEssentials(ConfigWindowTab.Gameboy));
			Assert.False(PlayerSettingsEssentials.IsEssentials(ConfigWindowTab.Sms));
			Assert.False(PlayerSettingsEssentials.IsEssentials(ConfigWindowTab.Preferences));
		}

		[Theory]
		[InlineData(ConfigWindowTab.Audio, ConfigWindowTab.Audio)]
		[InlineData(ConfigWindowTab.Input, ConfigWindowTab.Input)]
		[InlineData(ConfigWindowTab.Look, ConfigWindowTab.Look)]
		[InlineData(ConfigWindowTab.Display, ConfigWindowTab.Display)]
		[InlineData(ConfigWindowTab.Video, ConfigWindowTab.Display)]
		[InlineData(ConfigWindowTab.Preferences, ConfigWindowTab.Display)]
		[InlineData(ConfigWindowTab.Emulation, ConfigWindowTab.Display)]
		[InlineData(ConfigWindowTab.Nes, ConfigWindowTab.Display)]
		public void ClampToEssentials_KeepsOrFallsBackToDisplay(ConfigWindowTab tab, ConfigWindowTab expected)
		{
			Assert.Equal(expected, PlayerSettingsEssentials.ClampToEssentials(tab));
		}

		[Theory]
		[InlineData(3.0, 3.0)]
		[InlineData(2.996, 3.0)]
		[InlineData(1.0, 1.0)]
		public void A_scale_close_to_a_whole_number_reads_as_it(double scale, double expected)
		{
			Assert.Equal(expected, PlayDisplaySettings.WholeScale(scale));
		}

		[Theory]
		[InlineData(2.37)]
		[InlineData(0.5)]
		public void A_fractional_scale_has_no_whole_value(double scale)
		{
			Assert.Null(PlayDisplaySettings.WholeScale(scale));
		}

		//Restore-not-clobber: a value set elsewhere is listed, never replaced.
		[Fact]
		public void The_current_value_is_listed_when_the_short_list_lacks_it()
		{
			Assert.Equal(new[] { 1, 2, 3 }, PlayDisplaySettings.ItemsWithCurrent(new[] { 1, 2, 3 }, 2));
			Assert.Equal(new[] { 1, 2, 3, 9 }, PlayDisplaySettings.ItemsWithCurrent(new[] { 1, 2, 3 }, 9));
		}
	}
}
