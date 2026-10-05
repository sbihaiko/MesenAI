using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config
{
	//PRD Part B §6, §13.5.2 W-P8 (G.4): Player mode's Settings is one strip,
	//Display | Look | Audio | Controls | System, and a non-essentials initial
	//selection (e.g. Preferences from the Advanced GUI path) clamps to Display,
	//so the window never lands on a hidden tab. System is the last of the five:
	//ADR-0256 Decision 8 moved the retired first-run wizard's two questions
	//there, and it is a tab of this strip rather than a door of its own
	//(ADR-0250).
	public class PlayerSettingsEssentialsTests
	{
		[Fact]
		public void The_strip_is_display_look_audio_controls_system_in_that_order()
		{
			Assert.Equal(new[] { ConfigWindowTab.Display, ConfigWindowTab.Look, ConfigWindowTab.Audio, ConfigWindowTab.Input, ConfigWindowTab.System }, PlayerSettingsEssentials.Tabs);
			Assert.Equal(1, PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look));
			Assert.Equal(ConfigWindowTab.Input, PlayerSettingsEssentials.TabAt(3));
			Assert.Equal(ConfigWindowTab.System, PlayerSettingsEssentials.TabAt(4));
			Assert.Null(PlayerSettingsEssentials.TabAt(5));
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
		public void Display_and_System_are_not_advanced_tabs()
		{
			Assert.Equal(-1, ConfigWindowTabOrder.IndexOf(ConfigWindowTab.Display));
			Assert.Equal(-1, ConfigWindowTabOrder.IndexOf(ConfigWindowTab.System));
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

		//W-P8's Scale popup is never blank: a window smaller than 1× (or no
		//window yet, scale 0) is not in the list, so the nearest offered scale
		//shows. An exact or listed value is itself.
		[Theory]
		[InlineData(0.5, 1)]
		[InlineData(0, 1)]
		[InlineData(0.99, 1)]
		[InlineData(3, 3)]
		[InlineData(2.37, 2.37)]
		[InlineData(7.5, 7.5)]
		public void The_selected_scale_is_the_current_or_the_nearest_offered(double current, double expected)
		{
			double[] items = current >= 1 ? PlayDisplaySettings.ItemsWithCurrent(PlayDisplaySettings.Scales, current).ToArray() : PlayDisplaySettings.Scales;
			Assert.Equal(expected, PlayDisplaySettings.Nearest(items, current));
		}
	
		//W-P8: Display, Audio and Controls are the same 340 px sheet (three rows,
		//then the hint or the "More in Options..." link, then Done); Look is
		//W-P10's taller 480 px sheet, and ADR-0256 Decision 8's System tab needs
		//the same room - two storage choices with their folder lines, two
		//keyboard choices, and the restart line a folder change puts there.
		[Theory]
		[InlineData(ConfigWindowTab.Display, 340)]
		[InlineData(ConfigWindowTab.Look, 480)]
		[InlineData(ConfigWindowTab.Audio, 340)]
		[InlineData(ConfigWindowTab.Input, 340)]
		[InlineData(ConfigWindowTab.System, 480)]
		public void Each_tab_has_its_sheet_height(ConfigWindowTab tab, double height)
		{
			Assert.Equal(height, PlayerSettingsEssentials.SheetHeight(tab));
		}

		//The bug: Audio and Controls embedded the whole classic option pages
		//(sub-tabs, per-console button row, scrollbars). Now each essentials tab
		//is a short inset list in the Display pattern: at most 3 rows (PRD rule
		//2 leaves room for the link), and no classic page.
		[Theory]
		[InlineData(ConfigWindowTab.Display, 3)]
		[InlineData(ConfigWindowTab.Audio, 3)]
		[InlineData(ConfigWindowTab.Input, 3)]
		public void Each_list_tab_has_three_rows_and_no_classic_page(ConfigWindowTab tab, int rows)
		{
			Assert.False(PlayerSettingsEssentials.EmbedsClassicPage(tab));
			Assert.Equal(rows, PlayerSettingsEssentials.Rows(tab).Count);
			Assert.True(PlayerSettingsEssentials.Rows(tab).Count <= PlayerSettingsEssentials.MaxRows);
		}

		[Fact]
		public void Look_keeps_its_own_page_and_no_classic_one()
		{
			Assert.False(PlayerSettingsEssentials.EmbedsClassicPage(ConfigWindowTab.Look));
			Assert.Empty(PlayerSettingsEssentials.Rows(ConfigWindowTab.Look));
		}

		[Fact]
		public void Audio_rows_are_sound_volume_and_output_device()
		{
			Assert.Equal(new[] { "Sound", "Volume", "OutputDevice" }, PlayerSettingsEssentials.Rows(ConfigWindowTab.Audio).Select(r => r.Id));
			Assert.Equal(new[] { PlayerSettingsRowKind.Switch, PlayerSettingsRowKind.Slider, PlayerSettingsRowKind.Picker }, PlayerSettingsEssentials.Rows(ConfigWindowTab.Audio).Select(r => r.Kind));
		}

		[Fact]
		public void Controls_rows_are_controllers_rumble_and_deadzone()
		{
			Assert.Equal(new[] { "Controllers", "Rumble", "Deadzone" }, PlayerSettingsEssentials.Rows(ConfigWindowTab.Input).Select(r => r.Id));
		}

		//"More in Options..." expands to the classic page of the same tab (Look's
		//opens Video); Display has no link - the hint stays.
		[Theory]
		[InlineData(ConfigWindowTab.Audio, ConfigWindowTab.Audio)]
		[InlineData(ConfigWindowTab.Input, ConfigWindowTab.Input)]
		[InlineData(ConfigWindowTab.Look, ConfigWindowTab.Video)]
		public void More_in_Options_opens_the_classic_page(ConfigWindowTab tab, ConfigWindowTab expected)
		{
			Assert.Equal(expected, PlayerSettingsEssentials.OptionsTabFor(tab));
		}

		[Fact]
		public void Display_has_no_options_link()
		{
			Assert.Null(PlayerSettingsEssentials.OptionsTabFor(ConfigWindowTab.Display));
		}

		//The sliders write the config's integer scale: rounded and clamped.
		[Theory]
		[InlineData(42.4, 100, 42)]
		[InlineData(42.6, 100, 43)]
		[InlineData(-3, 100, 0)]
		[InlineData(250, 100, 100)]
		[InlineData(4.2, 4, 4)]
		public void A_slider_value_rounds_and_clamps_to_the_config_range(double value, uint max, uint expected)
		{
			Assert.Equal(expected, PlayerSliders.ToConfig(value, max));
		}

		//Restore-not-clobber: an output device chosen in Options that is not
		//enumerated now is listed as the current item, never replaced.
		[Fact]
		public void An_output_device_set_in_Options_stays_the_current_item()
		{
			Assert.Equal(new[] { "Speakers", "Headset" }, PlayerAudioSettings.Devices(new[] { "Speakers" }, "Headset"));
			Assert.Equal(new[] { "Speakers", "Headset" }, PlayerAudioSettings.Devices(new[] { "Speakers", "Headset" }, "Headset"));
			Assert.Equal(new[] { "Speakers" }, PlayerAudioSettings.Devices(new[] { "Speakers" }, ""));
		}
	}
}
