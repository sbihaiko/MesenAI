using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic.TestHook;

//#1233 (ADR-0272 item 3): which surface the hook's `screen` names.
//
//`state` carries every named control the window has; `screen` is the one surface
//that is up, and it is what a script's `ui.screen == ...` check reads. The answer
//cannot be read off the ids alone - `play.home` and `play.library` look alike, and
//`play.library.tile` is a control on one of them - so the surfaces are listed
//here, once, and a surface that gains an automation id without gaining a line
//here stays invisible to `ui.screen`. That was #1233: A on the home's
//*Open a ROM…* did open the library sheet, and the step still failed, because
//`play.home` was the only id the state ever named as a screen.
public static class TestHookScreens
{
	public const string Home = "play.home";
	public const string Library = "play.library";

	//The Play door's surfaces, topmost first, so the topmost one that is visible
	//is the screen. The library sheet is drawn over the home - it is the one Play
	//sheet the home itself opens - so while it is up both ids are visible and the
	//sheet, not the floor under it, is what `ui.screen` answers.
	private static readonly string[] _topmostFirst = { Library, Home };

	//The topmost known surface among the ids the window reports as visible, or
	//null when none of them is up (a window that is not on a Play surface).
	public static string? Resolve(IEnumerable<string> visibleIds)
	{
		foreach(string screen in _topmostFirst) {
			if(visibleIds.Contains(screen)) {
				return screen;
			}
		}
		return null;
	}
}
