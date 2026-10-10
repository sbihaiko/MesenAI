using Mesen.Logic.TestHook;
using Xunit;

namespace Mesen.Tests.TestHook
{
	//#1233: which surface the hook's `screen` names (ADR-0272 item 3). This is the
	//host-free rule behind the failing step `home.open-library` of the
	//play-pad-only script: while the library sheet is up, `ui.screen` has to answer
	//`play.library`, not the home the sheet is drawn over. The wiring - the pad
	//press that opens the sheet and the state the runner reads - is pinned in
	//UI.HeadlessTests.
	public class TestHookScreensTests
	{
		//The library sheet is opened from the home, so both ids are visible while
		//it is up: the surface on top is what a script's `ui.screen` check means.
		[Fact]
		public void The_topmost_visible_surface_is_the_screen()
		{
			Assert.Equal(TestHookScreens.Library, TestHookScreens.Resolve(new[] { TestHookScreens.Home, TestHookScreens.Library }));
		}

		//The order the ids arrive in is the visual tree's, not the answer's.
		[Fact]
		public void The_order_of_the_ids_does_not_decide()
		{
			Assert.Equal(TestHookScreens.Library, TestHookScreens.Resolve(new[] { TestHookScreens.Library, TestHookScreens.Home }));
		}

		[Fact]
		public void The_home_is_the_screen_when_the_library_is_not_up()
		{
			Assert.Equal(TestHookScreens.Home, TestHookScreens.Resolve(new[] { TestHookScreens.Home, "play.home.open-rom" }));
		}

		//A control on a surface is not the surface itself: `play.library.tile` is a
		//tile of the library, and naming it as the screen would answer a question
		//no script asks.
		[Fact]
		public void A_control_on_a_surface_is_not_a_screen()
		{
			Assert.Null(TestHookScreens.Resolve(new[] { "play.library.tile", "play.home.open-rom" }));
		}

		//A window that is not on a Play surface names no screen at all.
		[Fact]
		public void No_known_surface_up_names_no_screen()
		{
			Assert.Null(TestHookScreens.Resolve(new[] { "menu.options", "play.library.tile" }));
		}
	}
}
