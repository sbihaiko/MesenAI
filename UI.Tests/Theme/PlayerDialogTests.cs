using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Theme
{
	//ADR-0249 (W-X1, W-X2): a dialog the Player GUI still raises - a message
	//box, the archive's game list, a shader's parameters - takes the Player
	//look when its owner shows the Player theme, and keeps the classic one
	//under a classic window, the debugger or Advanced mode.
	public class PlayerDialogTests
	{
		[Theory]
		[InlineData(true, true, true)]
		[InlineData(true, false, false)]
		[InlineData(false, true, false)]
		[InlineData(false, false, false)]
		public void The_player_look_needs_player_mode_and_a_player_owner(bool playerMode, bool ownerShowsPlayerTheme, bool expected)
		{
			Assert.Equal(expected, PlayerDialog.UsesPlayerLook(playerMode, ownerShowsPlayerTheme));
		}

		//An error is the stop banner, a warning or a question the warning one,
		//information the pale blue one.
		[Theory]
		[InlineData(DialogTone.Error, BannerKind.Stop)]
		[InlineData(DialogTone.Warning, BannerKind.Warning)]
		[InlineData(DialogTone.Question, BannerKind.Warning)]
		[InlineData(DialogTone.Info, BannerKind.Info)]
		public void The_tone_picks_the_banner(DialogTone tone, BannerKind banner)
		{
			Assert.Equal(banner, PlayerDialog.BannerOf(tone));
		}

		//Classic order is OK/Yes first; the renders put the safe button first
		//and the one that goes on last, on the right.
		[Theory]
		[InlineData(1, new[] { 0 })]
		[InlineData(2, new[] { 1, 0 })]
		[InlineData(3, new[] { 2, 1, 0 })]
		public void The_button_that_goes_on_is_last(int count, int[] order)
		{
			Assert.Equal(order, PlayerDialog.ButtonOrder(count));
		}

		[Fact]
		public void The_first_classic_button_is_the_primary_one()
		{
			Assert.True(PlayerDialog.IsPrimary(0));
			Assert.False(PlayerDialog.IsPrimary(1));
			Assert.False(PlayerDialog.IsPrimary(2));
		}
	}
}
