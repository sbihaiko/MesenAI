using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//The native renderer is drawn above every Avalonia control, so W-P4 and the
	//sheets opened over the game only show when the picture is hidden.
	public class PlayGameLayerTests
	{
		[Fact]
		public void The_game_shows_when_nothing_covers_it()
		{
			Assert.True(PlayGameLayer.ShowsNativeRenderer(true, false, false, false));
		}

		[Fact]
		public void A_surface_over_the_game_hides_the_native_picture()
		{
			Assert.False(PlayGameLayer.ShowsNativeRenderer(true, false, false, true));
		}

		[Theory]
		[InlineData(false, false, false)]
		[InlineData(true, true, false)]
		[InlineData(true, false, true)]
		public void Outside_the_game_view_the_recents_or_a_software_frame_hide_it_as_before(bool gameView, bool recents, bool softwareFrame)
		{
			Assert.False(PlayGameLayer.ShowsNativeRenderer(gameView, recents, softwareFrame, false));
		}
	}
}
