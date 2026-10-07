using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1032 review finding 3 (ADR-0264 Decision 9): the flat library dedups the
	//paths it walks so one file is one tile. Folding two spellings together is a
	//FILE SYSTEM rule and not a string rule - Windows and macOS spell a path
	//case-insensitively, Linux does not - so the scan takes the comparer it folds
	//with, and the default is the platform's own answer.
	public class GameLibraryPathCaseTests
	{
		private static string R(string path) => Path.GetFullPath(path);

		//A folder holding exactly the files it is handed: the smallest tree the
		//dedup can be observed on.
		private static FolderLister Listing(params string[] files)
		{
			string[] full = files.Select(R).ToArray();
			return _ => (Array.Empty<string>(), full);
		}

		private static string[] Paths(LibraryScanResult result) => result.Entries.Select(e => e.Path).ToArray();

		//Two spellings that are one file on Windows and macOS are one tile: the
		//grid must not show the same game twice.
		[Fact]
		public void The_case_insensitive_comparer_folds_two_spellings_of_one_file()
		{
			LibraryScanResult result = GameLibrary.Scan(
				new[] { "/lib/roms" },
				Listing("/lib/roms/Contra.nes", "/lib/roms/contra.nes"),
				StringComparer.OrdinalIgnoreCase);

			Assert.Single(result.Entries);
			//The spelling that survives is the one the host listed first, so the
			//tile opens a path that exists as it is spelled.
			Assert.Equal(R("/lib/roms/Contra.nes"), Paths(result)[0]);
		}

		//On Linux those two spellings are two files on disk, so folding them would
		//hide a game the player has: the case-sensitive comparer keeps both.
		[Fact]
		public void The_case_sensitive_comparer_keeps_two_spellings_apart()
		{
			LibraryScanResult result = GameLibrary.Scan(
				new[] { "/lib/roms" },
				Listing("/lib/roms/Contra.nes", "/lib/roms/contra.nes"),
				StringComparer.Ordinal);

			Assert.Equal(
				new[] { R("/lib/roms/Contra.nes"), R("/lib/roms/contra.nes") },
				Paths(result).OrderBy(p => p, StringComparer.Ordinal).ToArray());
		}

		//A scan nobody hands a comparer to folds the way the platform under it
		//folds. This is the assertion that fails on a Linux run if the default
		//ever goes back to "one spelling everywhere".
		[Fact]
		public void The_default_comparer_follows_the_platform_the_scan_runs_on()
		{
			StringComparer expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
				? StringComparer.OrdinalIgnoreCase
				: StringComparer.Ordinal;

			Assert.Same(expected, GameLibrary.PathComparer);
		}

		//The rule itself, with the platform NAMED rather than interrogated, so both
		//sides of it are pinned whatever OS the test happens to run on.
		[Theory]
		[InlineData(true, true)]
		[InlineData(false, false)]
		public void The_comparer_rule_folds_case_exactly_where_paths_do(bool caseInsensitiveFileSystem, bool foldsCase)
		{
			StringComparer comparer = GameLibrary.PathComparerFor(caseInsensitiveFileSystem);

			Assert.Equal(foldsCase, comparer.Equals("Contra.nes", "contra.nes"));
		}

		//Two spellings of one title tie on the title and are broken by the path, so
		//the grid's order follows the same comparer instead of whatever the host
		//answered first - the order has to be the same on the next scan.
		[Fact]
		public void The_path_tiebreak_orders_the_way_the_comparer_does()
		{
			//Listed lower-case first: the ordinal comparison puts `Contra.nes` in
			//front of `contra.nes` whatever order the folder came in.
			LibraryScanResult result = GameLibrary.Scan(
				new[] { "/lib/roms" },
				Listing("/lib/roms/contra.nes", "/lib/roms/Contra.nes"),
				StringComparer.Ordinal);

			Assert.Equal(
				new[] { R("/lib/roms/Contra.nes"), R("/lib/roms/contra.nes") },
				Paths(result));
		}

		//The comparer is the one the FOLDER walk folds with too, not just the file
		//one: `Games/` and `games/` beside each other are two folders on Linux and
		//one on macOS, and the walk's answer has to follow the same rule.
		[Fact]
		public void The_comparer_is_the_one_the_walk_folds_folders_with()
		{
			FolderLister listing = folder => (
				folder == R("/lib/roms") ? new[] { R("/lib/roms/Games"), R("/lib/roms/games") } : Array.Empty<string>(),
				folder == R("/lib/roms/Games") ? new[] { R("/lib/roms/Games/A.nes") }
					: folder == R("/lib/roms/games") ? new[] { R("/lib/roms/games/B.nes") }
					: Array.Empty<string>());

			LibraryScanResult caseSensitive = GameLibrary.Scan(new[] { "/lib/roms" }, listing, StringComparer.Ordinal);
			LibraryScanResult caseInsensitive = GameLibrary.Scan(new[] { "/lib/roms" }, listing, StringComparer.OrdinalIgnoreCase);

			Assert.Equal(
				new[] { R("/lib/roms/Games/A.nes"), R("/lib/roms/games/B.nes") },
				Paths(caseSensitive).OrderBy(p => p, StringComparer.Ordinal).ToArray());
			Assert.Single(caseInsensitive.Entries);
		}
	}
}
