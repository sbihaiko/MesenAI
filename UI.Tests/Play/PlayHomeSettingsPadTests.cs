using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1177 (ADR-0256, W-P8): with no game loaded the pad's Y on the Play home
	//opens the Settings sheet, and the footer names it. Y keeps its library
	//meaning (ADR-0264 Decision 3), which is the library sheet's own branch.
	public class PlayHomeSettingsPadTests
	{
		[Fact]
		public void Y_opens_Settings_on_the_home_with_no_game_loaded()
		{
			Assert.True(PlayPadNavigation.OpensSettingsFromHome(inPlayDoor: true, homeVisible: true, gameLoaded: false, otherSurfaceUp: false));
		}

		[Theory]
		[InlineData(false, true, false, false)] //outside the Play door
		[InlineData(true, false, false, false)] //the home is not on screen
		[InlineData(true, true, true, false)] //a game is loaded: Y is the game's
		[InlineData(true, true, false, true)] //a sheet (library, Settings itself) is up
		public void Y_does_nothing_off_the_home(bool inPlayDoor, bool homeVisible, bool gameLoaded, bool otherSurfaceUp)
		{
			Assert.False(PlayPadNavigation.OpensSettingsFromHome(inPlayDoor, homeVisible, gameLoaded, otherSurfaceUp));
		}

		[Theory]
		[InlineData(PadFamily.Xbox, "A BarOpenGame     Y BarSettings")]
		[InlineData(PadFamily.Ps4, "Cross BarOpenGame     Triangle BarSettings")]
		public void The_first_run_home_bar_names_Y_Settings(PadFamily family, string expected)
		{
			Assert.Equal(expected, PlayActionBar.Text(PlayBarDeclarations.HomeFirstRun, PlayInputDevice.Controller, family, false, key => key));
		}

		[Fact]
		public void The_recents_home_bar_names_Y_Settings()
		{
			Assert.Equal("A BarPlay     Y BarSettings", PlayActionBar.Text(PlayBarDeclarations.Home, PlayInputDevice.Controller, PadFamily.Xbox, false, key => key));
		}

		[Fact]
		public void The_library_bar_keeps_Y_Search_and_never_names_Settings()
		{
			string text = PlayActionBar.Text(PlayBarDeclarations.Library, PlayInputDevice.Controller, PadFamily.Xbox, false, key => key);
			Assert.Contains("Y BarSearch", text);
			Assert.DoesNotContain("BarSettings", text);
		}
	}
}
