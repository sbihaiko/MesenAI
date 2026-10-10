using System;
using System.Collections.Generic;

namespace Mesen.Logic.TestHook;

//#1236 (ADR-0272 item 3): `screen` is the ACTIVE screen, and on the Play door
//the surfaces stack - the library sheet is drawn over the home, and the home
//keeps painting under it, so both are effectively visible at once. A hook that
//answers with "any visible control whose id is a screen id" therefore answers
//`play.home` for as long as the home exists, whatever opens over it, and the
//pilot's `home.open-library` step - which waits for `ui.screen == play.library`
//- can never be met however well the sheet opened (#1231/#1233/#1236/#1237).
//
//So the stack is written down: bottom first, and the last id in it that has a
//visible control is the active screen. A screen the application does not carry
//never wins, which is what keeps this list honest as surfaces are added - a
//surface that is not named here is `null` (no screen), never a wrong one.
//
//Host-free on purpose (BCL only, no Avalonia): which id wins is a rule about
//the order, and it is pinned in UI.Tests without a window.
public static class TestHookScreens
{
	//The Play door's surfaces, bottom of the stack first. `play.library` is the
	//library sheet the home's *Open a ROM…* opens (ADR-0264 Decision 1).
	private static readonly string[] Stack = { "play.home", "play.library" };

	public static string? Active(IEnumerable<string> visibleIds)
	{
		string? active = null;
		int rank = -1;
		foreach(string id in visibleIds) {
			int at = Array.IndexOf(Stack, id);
			if(at > rank) {
				active = id;
				rank = at;
			}
		}
		return active;
	}
}
