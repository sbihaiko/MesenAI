using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0249 final audit: the slot grid opened from W-P4 › Save States uses
	//Player copy in Player mode; classic/Advanced keep Mesen's strings.
	public class PlaySlotGridTests
	{
		[Fact]
		public void Player_mode_uses_the_player_keys()
		{
			Assert.Equal("PlayerLoadStateTitle", PlaySlotGrid.TitleKey(load: true, player: true));
			Assert.Equal("PlayerSaveStateTitle", PlaySlotGrid.TitleKey(load: false, player: true));
			Assert.Equal("PlayerSlotNumber", PlaySlotGrid.SlotKey(player: true));
			Assert.Equal("PlayerEmptySlot", PlaySlotGrid.EmptyKey(player: true));
		}

		[Fact]
		public void Classic_keeps_mesens_keys()
		{
			Assert.Equal("LoadStateDialog", PlaySlotGrid.TitleKey(load: true, player: false));
			Assert.Equal("SaveStateDialog", PlaySlotGrid.TitleKey(load: false, player: false));
			Assert.Equal("SlotNumber", PlaySlotGrid.SlotKey(player: false));
			Assert.Equal("EmptyState", PlaySlotGrid.EmptyKey(player: false));
		}
	}
}
