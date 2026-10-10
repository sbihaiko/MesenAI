using System;
using System.Collections.Generic;

namespace Mesen.Logic.TestHook;

//ADR-0272 item 3: `screen` is the surface the player is on, named by the id the
//control carries. Host-free, because which of two ids is the screen is a reading
//of the tree the window half already walked - the window half only reports what
//it found visible, in draw order.
//
//A surface is a control the player is ON (the home, the library sheet); a control
//inside one is not, and carries a deeper id (play.home.open-rom, play.library.tile)
//that the walk reports as a control. So the ids that name a surface are declared
//here, one line per surface, and a surface added later adds its line with the step
//that reads it.
//
//Last visible wins, and "last" is the walk's own order: the visual tree is walked
//in draw order, so of two surfaces on screen at once - the home stays behind the
//sheet opened from it (ADR-0256 Decision 3) - the one drawn over the other is the
//one the player is on.
public static class TestHookScreens
{
	private static readonly string[] Declared = {
		"play.home",
		"play.library"
	};

	//True for an id that names a surface rather than a control on one.
	public static bool IsScreen(string id) => Array.IndexOf(Declared, id) >= 0;

	//The active screen: the last declared id the walk found visible, null when no
	//surface is on screen at all (Advanced, or a Play surface that is not one of
	//the declared ones yet).
	public static string? Active(IEnumerable<string> visibleIdsInDrawOrder)
	{
		string? active = null;
		foreach(string id in visibleIdsInDrawOrder) {
			if(IsScreen(id)) {
				active = id;
			}
		}
		return active;
	}
}
