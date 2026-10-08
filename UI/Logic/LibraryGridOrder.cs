using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//#1065 (ADR-0264 Decisions 1 and 7): the order the library grid is drawn in.
//
//The scan has an order of its own - GameLibrary.Compare - and it sorts the grid
//by the title the SCAN knows, which is the cleaned file name. #1038 gives a tile
//the database's own title once its hash answers, and #1065 makes the grid follow
//while the sheet is open: the rule below is that same rule, asked over the title
//the player READS rather than the one the file is named.
//
//It lives here, host-free (ADR-0123), because two callers need ONE answer: the
//scan, which holds a LibraryEntry, and the sheet, which holds a tile. A second
//copy of "title, then path" on the view-model's side is how the order the grid
//fills in and the order it re-sorts into would drift apart - and the drift would
//be a tile that jumps when a title lands for no reason the player can see.
public static class LibraryGridOrder
{
	//Title first, the path as the tie-break: two games with one canonical title
	//(an NES and a Game Boy one, the same ROM under two folders) keep one stable
	//order rather than whatever the sort happened to do with them.
	//
	//`SortTitle` and the case fold are GameLibrary's own rules, asked rather than
	//restated. The path fold is the caller's, the way it is for GameLibrary's own
	//compare: it is the "is this the same file?" question, and only the caller
	//knows which platform it is answering for.
	public static int Compare(string leftTitle, string leftPath, string rightTitle, string rightPath, StringComparer pathComparer)
	{
		int byTitle = string.Compare(GameLibrary.SortTitle(leftTitle), GameLibrary.SortTitle(rightTitle), StringComparison.OrdinalIgnoreCase);
		return byTitle != 0 ? byTitle : pathComparer.Compare(leftPath, rightPath);
	}

	//The items in the order the grid draws them: a copy, sorted by the rule
	//above, over the title each item is shown by.
	//
	//A copy and never a sort in place: the caller's list is the library the scan
	//answered, and its order is the scan's to own. The result is what the grid
	//takes, and the caller walks it to move, add and drop tiles.
	public static List<T> Sorted<T>(IEnumerable<T> items, Func<T, string> titleOf, Func<T, string> pathOf, StringComparer pathComparer)
	{
		List<T> ordered = new(items);
		ordered.Sort((left, right) => Compare(titleOf(left), pathOf(left), titleOf(right), pathOf(right), pathComparer));
		return ordered;
	}
}
