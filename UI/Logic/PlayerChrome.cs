using System;

namespace Mesen.Logic;

//PRD Part B §6 (P.4/P.7): the single host-free rule for main-menu-bar
//visibility. Two sites used to compute it independently - the initializer in
//MainWindowViewModel (UiMode != Player && !AutoHideMenu) and
//MouseManager.UpdateMainMenuVisibility(), which recomputes it on every mouse
//move. Both now call in here so the two cannot drift.
//
//ADR-0250: the classic bar belongs to the Classic door, the original Mesen
//GUI. The three task doors have no menu bar; their Tools ⋯ holds a short menu
//(UI/Logic/WorkspaceMenu.cs).
public static class PlayerChrome
{
	//A task door hides the menu bar entirely (there is no menu bar to re-show,
	//so the AutoHideMenu hover rule is ignored). Classic keeps the classic rule:
	//exclusive fullscreen always hides; with auto-hide on, the bar shows while
	//the menu is open or the cursor sits in the top hover band; otherwise the
	//bar is always shown.
	//
	//The MainWindowViewModel initializer has no cursor/window state yet and
	//passes isExclusiveFullscreen: false, menuOpen: false, cursorInBand: false,
	//which reduces to "Classic door && !AutoHideMenu".
	public static bool IsMenuVisible(bool classicDoor, bool isExclusiveFullscreen, bool autoHide, bool menuOpen, bool cursorInBand)
	{
		if(!classicDoor) {
			return false;
		}

		if(isExclusiveFullscreen) {
			return false;
		}

		if(autoHide) {
			return menuOpen || cursorInBand;
		}

		return true;
	}

	//The auto-hide hover band: a strip at the top of the window, 15 px above
	//it and at least 35 scaled px tall, spanning the window's full width. All
	//coordinates are screen-space pixels; menuHeight/windowWidth are the
	//unscaled Avalonia bounds and scale is the layout scale, matching the
	//original inline computation in MouseManager.
	public static bool IsCursorInMenuBand(double cursorX, double cursorY, double windowLeft, double windowTop, double windowWidth, double menuHeight, double scale)
	{
		return
			cursorY >= windowTop - 15 && cursorY <= windowTop + Math.Max(menuHeight * scale + 10, 35 * scale) &&
			cursorX >= windowLeft && cursorX <= windowLeft + windowWidth * scale;
	}
}
