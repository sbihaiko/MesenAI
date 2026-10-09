using System;

namespace Mesen.Logic;

//#1035 (ADR-0264 Decision 6): where a tile's picture comes from, decided by the
//library module rather than by the sheet that draws it. Decision 6 fixes the
//order - art the scan already resolved (downloaded box art, a title screen)
//first, the player's own screenshot next, the console-coloured generic cover
//last - and that order is a rule about the library, so it belongs here beside
//the scan that produces the entries.
//
//The sheet's half is decoding and drawing: it hands the resolver a lookup (its
//RecentCoverIndex) and draws whatever brush the answer turns into. It never
//decides the priority, which is why a second cover source - the box-art
//downloader, L.8 - lands in this file and not in the view-model.
//
//Host-free (ADR-0123): the lookup comes in as a function from a ROM path to
//bytes, so the rule is unit-tested without a host, a disk or a bitmap.

//What one tile draws and, when the answer is the player's own screenshot, the
//bytes the sheet still has to decode. `Screenshot` is null whenever the cover is
//the entry's own: the sheet holds no copy of art it is not going to draw.
public readonly record struct LibraryCoverPick(LibraryCover Cover, byte[]? Screenshot);

public static class GameLibraryCover
{
	//The cover of one entry, from the entry and the player's Recent list.
	//
	//`recentLookup` answers the screenshot bytes of the game at a full ROM path,
	//or null when the player never ran it; it is asked only for an entry that has
	//no art of its own, so the `.rgd` read is skipped for every game the scan
	//already covered.
	public static LibraryCoverPick Resolve(LibraryEntry entry, Func<string, byte[]?> recentLookup)
	{
		//Decision 6's cases 1 and 2: art the scan resolved is already on the
		//entry, and the player's own screenshot fills the gap below it rather
		//than replacing it.
		if(entry.Cover != LibraryCover.Generic) {
			return new LibraryCoverPick(entry.Cover, null);
		}
		//Case 3, and last: the game the player has run. A lookup that answers
		//nothing leaves the entry where it was - the generic cover - never an
		//empty tile.
		byte[]? screenshot = recentLookup(entry.Path);
		return screenshot is null
			? new LibraryCoverPick(LibraryCover.Generic, null)
			: new LibraryCoverPick(LibraryCover.RecentScreenshot, screenshot);
	}
}
