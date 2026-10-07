using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//#1034 (ADR-0264 Decision 5): which console the Play library grid is narrowed
//to. The sheet is a flat library, and the player cycles this with LB/RB, so the
//rules that decide what the cycle contains are behaviour rather than drawing -
//they live here, host-free, and the sheet only draws the row it returns
//(ADR-0123: dual-compiled into UI.Tests, which is why nothing here reads a
//disk, a window or the core).
//
//The set is built from the consoles the entries actually carry, never from the
//enum: Decision 5's "only the consoles actually present" is what stops the
//player from cycling onto a filter that holds nothing. `Unknown` is not a
//console a player can choose - a library is not narrowed to "games the
//classifier could not name" - so it is never an option and never counts as a
//console being present.
//
//`All` is the first option and is spoken as null, not as a member of the enum:
//`RomConsole.Unknown` already means "this file names no console" (RomConsoleKinds),
//and reusing it for "no narrowing" would make the unknown-console entry and the
//unfiltered library the same value.
public static class LibraryConsoleFilter
{
	//The ordered set the row offers: All, then only the consoles present, in the
	//product's order. That order is the RomConsole enum's own order (NES, Game
	//Boy, Game Boy Color, Game Boy Advance, Master System, SG-1000, Game Gear) -
	//asked by sorting on the member rather than written out a second time, so a
	//console added to the enum lands in the product order instead of in a list
	//someone forgot to extend. It is the product order and not the order the
	//scan happened to produce: a library whose Game Gear folder is walked first
	//must still lead with NES, or the row moves between visits.
	//
	//All is always the first entry, which is also why the result is never empty:
	//a library with one console, or with no games at all, offers a one-option
	//set rather than a row the pad can fall off the end of.
	public static IReadOnlyList<RomConsole?> Options(IEnumerable<RomConsole> consoles)
	{
		List<RomConsole?> options = new() { null };
		options.AddRange(consoles
			.Where(console => console != RomConsole.Unknown)
			.Distinct()
			.OrderBy(console => (int)console)
			.Select(console => (RomConsole?)console));
		return options;
	}

	//RB: the next option, wrapping from the last console back to All - the cycle
	//is a ring, so a player who keeps pressing RB ends up where they started
	//rather than on a dead end.
	public static RomConsole? Next(IReadOnlyList<RomConsole?> options, RomConsole? selected)
	{
		return Cycle(options, selected, 1);
	}

	//LB: the same ring backwards, wrapping from All to the last console present.
	public static RomConsole? Previous(IReadOnlyList<RomConsole?> options, RomConsole? selected)
	{
		return Cycle(options, selected, -1);
	}

	//The ring itself. A selection the set no longer holds - the folder was
	//removed, the rescan found no Game Boy game where there was one - is treated
	//as All rather than as an index into nothing: the sheet came back with a
	//selection it cannot show, and All is the one option that is always true.
	private static RomConsole? Cycle(IReadOnlyList<RomConsole?> options, RomConsole? selected, int step)
	{
		int count = options.Count;
		if(count == 0) {
			return null;
		}

		int index = 0;
		for(int i = 0; i < count; i++) {
			if(options[i] == selected) {
				index = i;
				break;
			}
		}

		return options[((index + step) % count + count) % count];
	}

	//What the selection means for one entry, and the whole of Decision 5's
	//"never land on an empty filter": All is the absence of the narrowing, so it
	//keeps every entry - including one whose console the classifier could not
	//name. An archive's console is not knowable from its name (RomFileKinds
	//counts `.zip`/`.7z` as openable while RomConsoleKinds answers Unknown for
	//them), and dropping those under All would hide a game the library lists
	//behind no filter at all.
	public static bool Allows(RomConsole? selected, RomConsole console)
	{
		return selected is null || console == selected;
	}

	//Applies the two narrowings the sheet has at once, in the order it draws
	//them: Decision 5's example is `mario` under the NES filter, which finds
	//Super Mario Bros. 3 and not the Game Boy's Super Mario Land. Composing
	//rather than choosing between them is the point - a caller that let either
	//one replace the other would return every Mario, or every NES game.
	//
	//`matchesSearch` is null when the search field is empty, so the sheet has
	//one call shape for "no query" as well as for "a query".
	public static List<T> Apply<T>(
		IEnumerable<T> entries,
		RomConsole? selected,
		Func<T, RomConsole> consoleOf,
		Func<T, bool>? matchesSearch = null)
	{
		return entries
			.Where(entry => Allows(selected, consoleOf(entry)))
			.Where(entry => matchesSearch is null || matchesSearch(entry))
			.ToList();
	}
}
