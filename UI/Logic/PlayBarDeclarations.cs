using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//#1104: what each surface already on the shared action bar performs. The
//surface's claim (PlayFocusOnOpen) returns one of these, so the line can only
//name what the surface does - the rule PlayActionBar then orders and names.
//Each list is a statement about code that exists: Home's A starts Continue (or
//opens the library on a first run); the library's Y, LB/RB and B are the bridge's
//(ADR-0264 Decision 3); W-P4's A presses a row and its B resumes. Home has no
//Back - the pad's B does nothing there, as Esc does.
public static class PlayBarDeclarations
{
	public static readonly IReadOnlyList<PlayBarEntry> None = Array.Empty<PlayBarEntry>();

	public static readonly IReadOnlyList<PlayBarEntry> HomeFirstRun = new[] {
		new PlayBarEntry(PlayAction.Confirm, "BarOpenGame"),
	};

	public static readonly IReadOnlyList<PlayBarEntry> Home = new[] {
		new PlayBarEntry(PlayAction.Confirm, "BarPlay"),
	};

	public static readonly IReadOnlyList<PlayBarEntry> Library = new[] {
		new PlayBarEntry(PlayAction.Confirm, "BarPlay"),
		new PlayBarEntry(PlayAction.Search, "BarSearch"),
		new PlayBarEntry(PlayAction.ConsoleFilter, "BarConsole"),
		new PlayBarEntry(PlayAction.Back, "BarBack"),
	};

	//The library's header actions: A opens the action the ring is on, so the bar
	//names it instead of the tile's Play (PlayPadNavigationWiring picks by focus).
	public static readonly IReadOnlyList<PlayBarEntry> LibraryFolders = new[] {
		new PlayBarEntry(PlayAction.Confirm, "BarLibraryFolders"),
		new PlayBarEntry(PlayAction.Search, "BarSearch"),
		new PlayBarEntry(PlayAction.ConsoleFilter, "BarConsole"),
		new PlayBarEntry(PlayAction.Back, "BarBack"),
	};

	public static readonly IReadOnlyList<PlayBarEntry> BrowseFile = new[] {
		new PlayBarEntry(PlayAction.Confirm, "BarBrowseFile"),
		new PlayBarEntry(PlayAction.Search, "BarSearch"),
		new PlayBarEntry(PlayAction.ConsoleFilter, "BarConsole"),
		new PlayBarEntry(PlayAction.Back, "BarBack"),
	};

	//A on the search box enters it, so Search is the Confirm and Y is not repeated.
	public static readonly IReadOnlyList<PlayBarEntry> SearchField = new[] {
		new PlayBarEntry(PlayAction.Confirm, "BarSearch"),
		new PlayBarEntry(PlayAction.ConsoleFilter, "BarConsole"),
		new PlayBarEntry(PlayAction.Back, "BarBack"),
	};

	//A on the header's Back leaves the library, and A on the search's clear button
	//empties the query: neither plays, so the bar names what A does.
	public static readonly IReadOnlyList<PlayBarEntry> BackButton = new[] {
		new PlayBarEntry(PlayAction.Confirm, "BarBack"),
		new PlayBarEntry(PlayAction.Search, "BarSearch"),
		new PlayBarEntry(PlayAction.ConsoleFilter, "BarConsole"),
	};

	public static readonly IReadOnlyList<PlayBarEntry> SearchClear = new[] {
		new PlayBarEntry(PlayAction.Confirm, "BarClearSearch"),
		new PlayBarEntry(PlayAction.Search, "BarSearch"),
		new PlayBarEntry(PlayAction.ConsoleFilter, "BarConsole"),
		new PlayBarEntry(PlayAction.Back, "BarBack"),
	};

	//The folder browser and Library folders… inside the same sheet: no search,
	//no console row to cycle.
	public static readonly IReadOnlyList<PlayBarEntry> Browser = new[] {
		new PlayBarEntry(PlayAction.Confirm, "BarSelect"),
		new PlayBarEntry(PlayAction.Back, "BarBack"),
	};

	public static readonly IReadOnlyList<PlayBarEntry> PauseOverlay = new[] {
		new PlayBarEntry(PlayAction.Confirm, "BarSelect"),
		new PlayBarEntry(PlayAction.Back, "BarResume"),
	};

	//#1110 (ADR-0268 Decision 1): X is a surface control that acts only where a
	//cover has the focus, so a surface's declaration gains it only while one does,
	//reading Favorite on an unfavorited cover and Unfavorite on a favorited one.
	//The surface's own list is never edited: an entry it already carries for X is
	//replaced, so the bar names X once.
	public static IReadOnlyList<PlayBarEntry> WithFavorite(IReadOnlyList<PlayBarEntry> declared, bool favorited)
	{
		return declared.Where(e => e.Action != PlayAction.Favorite)
			.Append(new PlayBarEntry(PlayAction.Favorite, favorited ? "BarUnfavorite" : "BarFavorite"))
			.ToList();
	}
}
