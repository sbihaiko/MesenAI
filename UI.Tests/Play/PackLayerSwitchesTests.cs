using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//W-P6's per-game layer switches: Textures, Audio and the ROM patch, each
	//off for one ROM only, stored beside the per-ROM pack choice and pushed to
	//the core as a comma list; the global switches still win.
	public class PackLayerSwitchesTests
	{
		private const string Rom = "D2BF7BD570430902114F1E3393F1FEB8B1C76E4D";

		[Fact]
		public void Every_layer_is_on_for_a_rom_with_no_entry()
		{
			Dictionary<string, List<string>> map = new();
			map.TryGetValue(Rom, out List<string>? off);
			Assert.True(PackLayerSwitches.IsOn(off, PackLayer.Textures));
			Assert.True(PackLayerSwitches.IsOn(off, PackLayer.Audio));
			Assert.True(PackLayerSwitches.IsOn(off, PackLayer.Patch));
		}

		[Fact]
		public void Turning_a_layer_off_stores_it_for_that_rom_only()
		{
			Dictionary<string, List<string>> map = new();
			PackLayerSwitches.Set(map, Rom, PackLayer.Audio, false);
			PackLayerSwitches.Set(map, Rom, PackLayer.Patch, false);

			Assert.Equal(new List<string> { "audio", "patch" }, map[Rom]);
			Assert.False(PackLayerSwitches.IsOn(map[Rom], PackLayer.Audio));
			Assert.True(PackLayerSwitches.IsOn(map[Rom], PackLayer.Textures));
			Assert.False(map.ContainsKey("OTHER"));
			Assert.Equal("audio,patch", PackLayerSwitches.ToCoreList(map[Rom]));
		}

		[Fact]
		public void Turning_the_last_layer_back_on_removes_the_rom_key()
		{
			Dictionary<string, List<string>> map = new();
			PackLayerSwitches.Set(map, Rom, PackLayer.Textures, false);
			PackLayerSwitches.Set(map, Rom, PackLayer.Textures, false);
			Assert.Single(map[Rom]);
			PackLayerSwitches.Set(map, Rom, PackLayer.Textures, true);
			Assert.False(map.ContainsKey(Rom));
		}

		[Fact]
		public void No_rom_stores_nothing()
		{
			Dictionary<string, List<string>> map = new();
			PackLayerSwitches.Set(map, "", PackLayer.Textures, false);
			Assert.Empty(map);
		}

		[Fact]
		public void The_core_list_uses_the_cores_words()
		{
			Assert.Equal("textures", PackLayerSwitches.Key(PackLayer.Textures));
			Assert.Equal("audio", PackLayerSwitches.Key(PackLayer.Audio));
			Assert.Equal("patch", PackLayerSwitches.Key(PackLayer.Patch));
			Assert.Equal("textures,audio", PackLayerSwitches.ToCoreList(new[] { " Textures", "audio", "TEXTURES", "" }));
			Assert.Equal("", PackLayerSwitches.ToCoreList(null));
		}

		[Fact]
		public void A_layer_the_pack_lacks_is_a_grey_switch_that_says_so()
		{
			Assert.Equal(new PackLayerSwitch(false, false, false, PackLayerNote.NotInPack), PackLayerSwitches.Row(false, true, true, false));
		}

		[Fact]
		public void The_global_switch_off_wins_and_is_named()
		{
			Assert.Equal(new PackLayerSwitch(true, false, false, PackLayerNote.OffEverywhere), PackLayerSwitches.Row(true, false, true, false));
		}

		[Fact]
		public void A_present_layer_shows_this_games_choice()
		{
			Assert.Equal(new PackLayerSwitch(true, true, true, PackLayerNote.None), PackLayerSwitches.Row(true, true, true, false));
			Assert.Equal(new PackLayerSwitch(true, false, true, PackLayerNote.None), PackLayerSwitches.Row(true, true, false, false));
		}

		[Fact]
		public void The_switches_wait_while_a_change_applies()
		{
			Assert.False(PackLayerSwitches.Row(true, true, true, true).Enabled);
			Assert.True(PackLayerSwitches.Row(true, true, true, true).On);
		}
	}
}
