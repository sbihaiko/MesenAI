using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1038 review finding 3 (ADR-0264 Decision 1): the order the grid shows two
	//games in is ONE rule. `GameLibrary.Scan` sorts the entries by it as they come
	//out of the walk, and the canonical-title pass re-sorts the grid by it again
	//once the real titles land - so both callers call this comparer rather than
	//each keeping a copy of the rule.
	//
	//The copy that used to live in the pass was `SortTitle` with
	//`OrdinalIgnoreCase`, then the path, written out again in the view-model. It
	//could drift from the scan's order with every test still green: the only other
	//coverage was the headless re-sort case, which skips where the native core is
	//absent - CI among them. The cases below are the rule itself, host-free, so
	//either caller drifting from it is a failure here rather than a library in
	//whatever order a reader does not expect.
	public class GameLibraryOrderTests
	{
		private static string R(string path) => Path.GetFullPath(path);

		//One folder with files in it and nothing below, which is all a case about
		//the ORDER needs: what the walk returns is sorted, not the tree it read.
		private static FolderLister Tree(string root, params string[] files)
		{
			string full = R(root);
			string[] listed = files.Select(R).ToArray();
			return folder => folder == full
				? (Array.Empty<string>(), listed)
				: (Array.Empty<string>(), Array.Empty<string>());
		}

		//The article rule: "The Legend of Zelda" files under L, where a player
		//looks for it, and never under T (Decision 1). The article moves for SORT
		//ORDER only - the tile still reads its own name - which is why this is
		//asserted here on the comparer and not on the title a tile carries.
		[Fact]
		public void A_leading_article_is_ordered_as_if_it_moved_to_the_end()
		{
			Assert.True(GameLibrary.Compare("The Legend of Zelda", R("/lib/zelda.nes"), "Metroid", R("/lib/metroid.nes")) < 0,
				"a leading “The” did not file the game under L");
			Assert.True(GameLibrary.Compare("Zelda II", R("/lib/zelda2.nes"), "The Legend of Zelda", R("/lib/zelda.nes")) > 0,
				"a game whose name has no article did not sort after a filed one");
			//The article has to be a WORD: "Thexder" is not "The xder", so it keeps
			//its own place in the alphabet.
			Assert.True(GameLibrary.Compare("Thexder", R("/lib/thexder.nes"), "Tetris", R("/lib/tetris.nes")) > 0,
				"“Thexder” was read as an article and filed under X");
		}

		//Two games of one name - an NES and a Game Boy one, or the same ROM under
		//two folders - need an order that does not depend on which the disk answered
		//first, or the grid would shuffle between two runs over one library.
		[Fact]
		public void Two_games_of_one_name_are_separated_by_their_path()
		{
			Assert.True(GameLibrary.Compare("Contra", R("/lib/a/Contra.nes"), "Contra", R("/lib/b/Contra.nes")) < 0);
			Assert.True(GameLibrary.Compare("Contra", R("/lib/b/Contra.nes"), "Contra", R("/lib/a/Contra.nes")) > 0);

			//One entry compared with itself is "no difference" and not a coin toss:
			//the re-sort decides whether the grid changed with this answer.
			Assert.Equal(0, GameLibrary.Compare("Contra", R("/lib/a/Contra.nes"), "Contra", R("/lib/a/Contra.nes")));
		}

		//The title comparison folds case, so a library that spells one game two
		//ways still gets as far as the path. A case-SENSITIVE title comparison
		//would answer with the spelling ("CONTRA" before "contra") and never reach
		//the file at all.
		[Fact]
		public void The_title_comparison_ignores_case_so_the_path_still_decides()
		{
			Assert.True(GameLibrary.Compare("contra", R("/lib/z.nes"), "CONTRA", R("/lib/a.nes")) > 0,
				"the title comparison was case-sensitive: it ordered the spellings instead of the files");
			Assert.True(GameLibrary.Compare("contra", R("/lib/a.nes"), "CONTRA", R("/lib/z.nes")) < 0);
		}

		//The path tiebreak folds the way the CALLER says, because folding a path is
		//a file system rule and not a string rule (GameLibrary.PathComparer): on
		//Windows and macOS two spellings are one file, on Linux they are two. The
		//same pair therefore lands on either side depending on the fold, and the
		//rule has to be the caller's rather than one hard-coded into this comparer.
		[Fact]
		public void The_path_tiebreak_folds_the_way_the_caller_names()
		{
			string[] upper = { "Contra", R("/lib/Contra.nes"), "Contra", R("/lib/contra (alt).nes") };

			Assert.True(GameLibrary.Compare(upper[0], upper[1], upper[2], upper[3], StringComparer.OrdinalIgnoreCase) > 0,
				"the fold the caller named was ignored");
			Assert.True(GameLibrary.Compare(upper[0], upper[1], upper[2], upper[3], StringComparer.Ordinal) < 0,
				"the fold the caller named was ignored");
		}

		//And the SCAN orders by this very rule, which is what makes one comparer
		//enough: a second caller (the canonical-title pass re-sorting the grid) that
		//re-applies it to the same games cannot disagree with the order already on
		//screen, so a library does not visibly re-shuffle when the real titles land.
		//
		//The listing below is deliberately hostile - the disk answers in an order no
		//reader wants - and the expected order is written out in full, so this case
		//fails on a rule that is merely self-consistent as well as on one that is
		//simply wrong.
		[Fact]
		public void The_scans_order_is_the_rule_this_comparer_states()
		{
			FolderLister files = Tree("/lib", "Metroid.nes", "The Legend of Zelda.nes", "contra.nes");

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib" }, files);

			Assert.Equal(new[] { "contra", "The Legend of Zelda", "Metroid" }, result.Entries.Select(entry => entry.Title).ToArray());

			IEnumerable<string> byThisRule = result.Entries
				.OrderBy(entry => entry, Comparer<LibraryEntry>.Create((left, right) =>
					GameLibrary.Compare(left.Title, left.Path, right.Title, right.Path)))
				.Select(entry => entry.Path);

			Assert.Equal(result.Entries.Select(entry => entry.Path), byThisRule);
		}
	}
}
