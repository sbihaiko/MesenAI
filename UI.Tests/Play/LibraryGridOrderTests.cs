using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1065 (ADR-0264 Decisions 1 and 7): the order the library grid is drawn in,
	//as the rule and not as the wiring. The wiring - that a title landing while
	//the sheet is open moves its tile and nothing else - is
	//UI.HeadlessTests/PlayerLibraryLiveResortTests; what is pinned here is which
	//order the sheet asks for, and that it is the scan's own rule asked over the
	//title the tile shows.
	//
	//Every expected order below is written out by hand from the titles, never
	//recomputed the way the code computes it: a table of strings and the order a
	//reader would put them in.
	public class LibraryGridOrderTests
	{
		private static int Compare(string leftTitle, string leftPath, string rightTitle, string rightPath)
		{
			return LibraryGridOrder.Compare(leftTitle, leftPath, rightTitle, rightPath, StringComparer.Ordinal);
		}

		//The point of the module: the title that ORDERS the grid is the one the
		//player reads (the canonical one once the pass resolved it), not the
		//cleaned file name the scan happened to start from.
		[Fact]
		public void A_canonical_title_orders_a_game_where_that_title_says()
		{
			//"Zelda II - The Adventure of Link" against "Metroid": by the file name
			//the first ROM was called "Contra" and led the grid; by its canonical
			//title it follows Metroid.
			Assert.True(Compare("Zelda II - The Adventure of Link", "/g/contra.nes", "Metroid", "/g/metroid.nes") > 0,
				"a canonical title did not order its game where that title says");
		}

		//GameLibrary's own rules, asked rather than restated: a leading article is
		//not what the player looks the game up by, and the case fold is the same
		//one the scan uses.
		[Fact]
		public void The_order_is_the_article_adjusted_one_the_scan_uses()
		{
			//Under L, where a player looks for it - and NOT under T, which is
			//where the title as written would put it.
			Assert.True(Compare("The Legend of Zelda", "/g/a.nes", "Metroid", "/g/b.nes") < 0,
				"\"The Legend of Zelda\" did not sort under L");
			Assert.Equal(0, Compare("metroid", "/g/a.nes", "Metroid", "/g/a.nes"));
		}

		//Two games with one title keep one order rather than whatever the sort
		//did with them, and the fold is the caller's - the same "is this the same
		//file?" question the scan asks.
		[Fact]
		public void Two_games_with_one_title_are_ordered_by_path()
		{
			Assert.True(Compare("Metroid", "/g/a/metroid.nes", "Metroid", "/g/b/metroid.nes") < 0);
		}

		//The scan's comparator IS this rule: a caller that merges a streamed entry
		//into the collected list and a caller that re-sorts the grid cannot
		//disagree about where a game belongs.
		[Fact]
		public void The_scans_own_comparator_answers_the_same_way()
		{
			LibraryEntry zelda = new("/g/contra.nes", RomConsole.Nes, "Zelda II - The Adventure of Link");
			LibraryEntry metroid = new("/g/metroid.nes", RomConsole.Nes, "Metroid");

			Assert.True(GameLibrary.Compare(zelda, metroid, StringComparer.Ordinal) > 0,
				"the scan and the grid's order disagree about where a game belongs");
		}

		//The order the grid takes, over the title each item is SHOWN by - which is
		//the whole of the sheet's side: it holds tiles, and a tile carries the
		//title the player reads.
		[Fact]
		public void The_items_come_back_in_the_order_the_grid_draws_them()
		{
			Dictionary<string, string> shown = new() {
				["/g/metroid.nes"] = "Metroid",
				["/g/contra.nes"] = "Contra",
				["/g/zelda.nes"] = "Zelda II - The Adventure of Link"
			};

			List<string> ordered = LibraryGridOrder.Sorted(
				new[] { "/g/metroid.nes", "/g/contra.nes", "/g/zelda.nes" },
				path => shown[path], path => path, StringComparer.Ordinal);

			Assert.Equal(new[] { "/g/contra.nes", "/g/metroid.nes", "/g/zelda.nes" }, ordered.ToArray());
		}

		//The caller's list is the library the scan answered, and its order is the
		//scan's to own: sorting the grid must not sort the scan.
		[Fact]
		public void Sorting_the_grid_leaves_the_callers_list_alone()
		{
			List<string> library = new() { "/g/metroid.nes", "/g/contra.nes" };

			LibraryGridOrder.Sorted(library, path => path, path => path, StringComparer.Ordinal);

			Assert.Equal(new[] { "/g/metroid.nes", "/g/contra.nes" }, library.ToArray());
		}
	}
}
