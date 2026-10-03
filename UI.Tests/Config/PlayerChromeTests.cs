using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config
{
	//P.7 (PRD Part B §6): the menu-bar visibility rule shared by the
	//MainWindowViewModel initializer and MouseManager.UpdateMainMenuVisibility().
	//G.1 (ADR-0241, §13.2): the classic bar is keyed on ShowClassicMenuBar,
	//not on UiMode - Tools ⋯ carries the same menus when the bar is off.
	public class PlayerChromeTests
	{
		[Theory]
		//Bar off ignores every other input - the menus live under Tools ⋯.
		[InlineData(false, false, false, false)]
		[InlineData(true, true, true, true)]
		[InlineData(false, true, false, true)]
		[InlineData(true, false, true, false)]
		public void ClassicBarOff_AlwaysHidesMenu(bool exclusiveFullscreen, bool autoHide, bool menuOpen, bool cursorInBand)
		{
			Assert.False(PlayerChrome.IsMenuVisible(false, exclusiveFullscreen, autoHide, menuOpen, cursorInBand));
		}

		[Fact]
		public void ClassicBarOn_ExclusiveFullscreen_HidesMenuEvenWhenOpenOrHovered()
		{
			Assert.False(PlayerChrome.IsMenuVisible(true, true, false, false, false));
			Assert.False(PlayerChrome.IsMenuVisible(true, true, true, true, true));
			Assert.False(PlayerChrome.IsMenuVisible(true, true, false, true, true));
		}

		[Fact]
		public void ClassicBarOn_WithoutAutoHide_AlwaysShowsMenu()
		{
			Assert.True(PlayerChrome.IsMenuVisible(true, false, false, false, false));
			Assert.True(PlayerChrome.IsMenuVisible(true, false, false, true, false));
			Assert.True(PlayerChrome.IsMenuVisible(true, false, false, false, true));
		}

		[Theory]
		//autoHide on: the bar shows only while the menu is open or the cursor
		//is inside the top hover band.
		[InlineData(false, false, false)]
		[InlineData(true, false, true)]
		[InlineData(false, true, true)]
		[InlineData(true, true, true)]
		public void ClassicBarOn_WithAutoHide_FollowsMenuOpenOrHover(bool menuOpen, bool cursorInBand, bool expected)
		{
			Assert.Equal(expected, PlayerChrome.IsMenuVisible(true, false, true, menuOpen, cursorInBand));
		}

		[Fact]
		public void InitializerInputs_MatchHistoricalRule()
		{
			//MainWindowViewModel's initializer passes false for the window/cursor
			//state, which must reduce to "ShowClassicMenuBar && !AutoHideMenu".
			foreach(bool showBar in new[] { true, false }) {
				foreach(bool autoHide in new[] { false, true }) {
					bool expected = showBar && !autoHide;
					Assert.Equal(expected, PlayerChrome.IsMenuVisible(showBar, false, autoHide, false, false));
				}
			}
		}

		[Fact]
		public void CursorBand_CoversTopStripOfWindow()
		{
			//Window at (100, 200), 640 wide, 20px menu, scale 1: the band runs
			//from y = 185 (15px above the window) to y = 235 (max(20+10, 35)).
			Assert.True(PlayerChrome.IsCursorInMenuBand(400, 200, 100, 200, 640, 20, 1));
			Assert.True(PlayerChrome.IsCursorInMenuBand(100, 185, 100, 200, 640, 20, 1));
			Assert.True(PlayerChrome.IsCursorInMenuBand(740, 230, 100, 200, 640, 20, 1));

			//Above the band, below the band, left of it, right of it.
			Assert.False(PlayerChrome.IsCursorInMenuBand(400, 184, 100, 200, 640, 20, 1));
			Assert.False(PlayerChrome.IsCursorInMenuBand(400, 236, 100, 200, 640, 20, 1));
			Assert.False(PlayerChrome.IsCursorInMenuBand(99, 200, 100, 200, 640, 20, 1));
			Assert.False(PlayerChrome.IsCursorInMenuBand(741, 200, 100, 200, 640, 20, 1));
		}

		[Fact]
		public void CursorBand_ScalesWidthAndHeight()
		{
			//scale 2 doubles the menu height (2*20+10 = 50 > 35*2 = 70 -> 70) and
			//the window width used for the horizontal extent.
			Assert.True(PlayerChrome.IsCursorInMenuBand(1379, 270, 100, 200, 640, 20, 2));
			Assert.False(PlayerChrome.IsCursorInMenuBand(1381, 270, 100, 200, 640, 20, 2));
			Assert.False(PlayerChrome.IsCursorInMenuBand(400, 271, 100, 200, 640, 20, 2));
		}
	}
}
