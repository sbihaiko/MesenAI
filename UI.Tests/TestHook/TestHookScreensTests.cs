using System.Collections.Generic;
using Mesen.Logic.TestHook;
using Xunit;

namespace Mesen.Tests.TestHook
{
	//#1236 (ADR-0272 item 3): which of the ids on screen is the ACTIVE screen. The
	//window half of the same rule - a real visual tree, and the shipped picker's
	//own id - is pinned in UI.HeadlessTests.
	public class TestHookScreensTests
	{
		//The order in the list is the answer, never the order the ids arrive in: a
		//test that passed the same ids twice in the other order would pass on a
		//"first one wins" implementation just as well.
		[Fact]
		public void The_sheet_drawn_over_the_home_is_the_active_screen_in_either_order()
		{
			Assert.Equal("play.library", TestHookScreens.Active(new[] { "play.home", "play.library" }));
			Assert.Equal("play.library", TestHookScreens.Active(new[] { "play.library", "play.home" }));
		}

		[Fact]
		public void The_active_screen_falls_back_to_the_bottom_of_the_stack()
		{
			Assert.Equal("play.home", TestHookScreens.Active(new[] { "play.home" }));
			Assert.Equal("play.home", TestHookScreens.Active(new[] { "play.home", "play.home.open-rom", "play.home.continue" }));
		}

		//A control under the sheet is still visible and still reported - the screen
		//is which surface is ACTIVE, not which one is drawn (tested through the
		//`visible` list in UI.HeadlessTests).
		[Fact]
		public void The_library_is_active_even_while_a_control_of_the_home_is_focused()
		{
			Assert.Equal("play.library", TestHookScreens.Active(new[] { "play.home", "play.home.open-rom", "play.library" }));
		}

		//No screen id on screen is no screen, never a guessed one: a surface that is
		//not in the stack cannot become `play.home` by being the only thing visible.
		[Fact]
		public void No_visible_screen_id_is_no_screen()
		{
			Assert.Null(TestHookScreens.Active(new List<string>()));
			Assert.Null(TestHookScreens.Active(new[] { "play.library.tile", "play.home.open-rom" }));
		}
	}
}
