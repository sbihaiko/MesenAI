using System;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config
{
	//ADR-0246 (P.13): the Settings window's TabControl index is not the
	//ConfigWindowTab value - removed consoles left holes in the enum and the
	//separator rows are tabs too. ConfigWindowTabOrder is the one mapping, and
	//it must match ConfigWindow.axaml's markup order (UI.HeadlessTests checks
	//the realized window against it).
	public class ConfigWindowTabOrderTests
	{
		[Fact]
		public void Look_sits_right_after_Video()
		{
			Assert.Equal(ConfigWindowTabOrder.IndexOf(ConfigWindowTab.Video) + 1, ConfigWindowTabOrder.IndexOf(ConfigWindowTab.Look));
		}

		//G.4 (W-P8): Display is Play's own tab, in the Player strip only
		//(PlayerSettingsEssentials.Tabs); ADR-0256 Decision 8 added a second one,
		//System. Every other id is an Advanced tab.
		[Fact]
		public void Every_defined_tab_has_exactly_one_index_and_maps_back()
		{
			foreach(ConfigWindowTab tab in Enum.GetValues<ConfigWindowTab>()) {
				if(tab is ConfigWindowTab.Display or ConfigWindowTab.System) {
					Assert.Equal(tab, PlayerSettingsEssentials.TabAt(PlayerSettingsEssentials.IndexOf(tab)));
					continue;
				}
				int index = ConfigWindowTabOrder.IndexOf(tab);
				Assert.True(index >= 0, $"{tab} has no tab");
				Assert.Equal(tab, ConfigWindowTabOrder.TabAt(index));
			}
			Assert.Equal(Enum.GetValues<ConfigWindowTab>().Length - 2, ConfigWindowTabOrder.Tabs.Count(t => t != null));
		}

		[Fact]
		public void An_id_is_not_its_position()
		{
			//The pre-P.13 binding used the enum value as the index: Sms (10)
			//selected a separator row and Preferences (14) no tab at all.
			Assert.Equal(ConfigWindowTab.Sms, ConfigWindowTabOrder.TabAt(9));
			Assert.Equal(ConfigWindowTab.Preferences, ConfigWindowTabOrder.TabAt(11));
			Assert.Equal(12, ConfigWindowTabOrder.Tabs.Length);
		}

		[Fact]
		public void A_separator_or_out_of_range_index_maps_to_no_tab()
		{
			Assert.Null(ConfigWindowTabOrder.TabAt(ConfigWindowTabOrder.IndexOf(ConfigWindowTab.Look) + 1));
			Assert.Null(ConfigWindowTabOrder.TabAt(-1));
			Assert.Null(ConfigWindowTabOrder.TabAt(ConfigWindowTabOrder.Tabs.Length));
		}
	}
}
