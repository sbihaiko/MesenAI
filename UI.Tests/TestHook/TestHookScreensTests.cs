using Mesen.Logic.TestHook;
using Xunit;

namespace Mesen.Tests.TestHook
{
	//#1228 (ADR-0272 item 3): `ui.screen` names the surface that is up. The rule
	//is host-free - a list of surfaces and the ids a window reports as visible -
	//so it is pinned here; that the real window's library sheet carries the id,
	//and that A on the home opens it, is pinned in UI.HeadlessTests
	//(PlayPadOnlyHomeOpenLibraryTests), which is the GUI test suite's step
	//`home.open-library` of the play-pad-only script.
	public class TestHookScreensTests
	{
		//The failure #1228 reports: the library sheet is drawn over the home, so
		//both ids are visible and the sheet is the screen - never the floor under
		//it. A step waiting on `ui.screen == play.library` times out otherwise,
		//even though the press that opened the sheet did its job.
		[Fact]
		public void The_library_sheet_is_the_screen_while_it_is_drawn_over_the_home()
		{
			Assert.Equal(TestHookScreens.Library, TestHookScreens.Resolve(new[] {
				"play.home", "play.home.open-rom", "play.home.recent",
				"play.library", "play.library.tile", "play.library.search"
			}));
		}

		[Fact]
		public void The_home_is_the_screen_when_nothing_is_over_it()
		{
			Assert.Equal(TestHookScreens.Home, TestHookScreens.Resolve(new[] {
				"play.home", "play.home.open-rom", "play.home.continue"
			}));
		}

		//`play.library` gone from the visible set is the sheet closed: Back lands
		//on the home (the script's `home.back-to-home`), and that step's own check
		//has to see the home again.
		[Fact]
		public void Closing_the_sheet_hands_the_screen_back_to_the_home()
		{
			Assert.Equal(TestHookScreens.Home, TestHookScreens.Resolve(new[] {
				"play.home", "play.home.open-rom"
			}));
		}

		//Only the surfaces this list names are screens: an id that is a control
		//on one of them never names one on its own.
		[Fact]
		public void A_control_id_alone_never_names_a_screen()
		{
			Assert.Null(TestHookScreens.Resolve(new[] { "play.home.open-rom", "play.library.tile", "menu.settings" }));
		}

		//A window that is not on a Play surface at all names none, rather than
		//inventing one.
		[Fact]
		public void No_visible_surface_is_no_screen()
		{
			Assert.Null(TestHookScreens.Resolve(System.Array.Empty<string>()));
		}
	}
}
