using Mesen.Logic.TestHook;
using Xunit;

namespace Mesen.Tests.TestHook
{
	//#1231: which surface the hook calls the screen. Host-free - the window half
	//walks the tree and reports what is visible, and which of those ids is the
	//screen is decided here.
	public class TestHookScreensTests
	{
		[Fact]
		public void The_home_alone_is_the_screen()
		{
			Assert.Equal("play.home", TestHookScreens.Active(new[] { "play.home", "play.home.open-rom" }));
		}

		//The library sheet is drawn over the home, so both are on screen and the
		//library is the one the player is on.
		[Fact]
		public void The_library_wins_over_the_home_it_is_drawn_over()
		{
			Assert.Equal("play.library", TestHookScreens.Active(new[] { "play.home", "play.library", "play.library.tile" }));
		}

		//Draw order is the whole rule: an id's place in the declaration above never
		//decides which surface is up.
		[Fact]
		public void The_last_surface_drawn_wins_whatever_the_ids_are()
		{
			Assert.Equal("play.home", TestHookScreens.Active(new[] { "play.library", "play.home" }));
		}

		//A control's own id is never the screen (ADR-0272 item 3).
		[Fact]
		public void A_control_on_a_surface_is_not_a_screen()
		{
			Assert.False(TestHookScreens.IsScreen("play.home.continue"));
			Assert.False(TestHookScreens.IsScreen("play.status.p1"));
			Assert.Null(TestHookScreens.Active(new[] { "play.home.continue", "play.status.p1" }));
		}

		[Fact]
		public void No_surface_on_screen_is_no_screen()
		{
			Assert.Null(TestHookScreens.Active(new string[0]));
		}
	}
}
