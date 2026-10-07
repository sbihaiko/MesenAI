using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1036, the host-free half: the list of library folders behind ADR-0264
	//Decision 8 (the Play "Open a game" sheet is a flat library). The list is what
	//the scan reads, so the rules that matter are the ones that decide WHICH
	//folders are in it and what the header claims about them.
	//
	//Real temp folders rather than an injected file system: every rule here is
	//about a path, and the module reads no disk, so the test can make the paths
	//the rules are about and still assert that the folder on disk was left alone.
	public class LibraryFoldersTests
	{
		private static string NewTempDir()
		{
			string dir = Path.Combine(Path.GetTempPath(), "mesen-libraryfolders-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			return dir;
		}

		//The three spellings of one folder that a file system hands back: with the
		//separator the picker returned, without it, and through `.`.
		private static (string Plain, string Trailing, string Dotted) Spellings(string dir)
		{
			return (dir, dir + Path.DirectorySeparatorChar, Path.Combine(dir, "."));
		}

		[Fact]
		public void A_first_run_seeds_the_list_from_the_old_games_folder()
		{
			string games = NewTempDir();
			try {
				IReadOnlyList<string> seeded = LibraryFolders.Seed(null, true, games);
				Assert.Equal(new[] { Path.GetFullPath(games) }, seeded);
			} finally {
				Directory.Delete(games, true);
			}
		}

		//`OverrideGameFolder` is what makes GameFolder the folder the player chose
		//(every other call site reads it as `OverrideGameFolder ? GameFolder : null`),
		//so a folder the player never designated does not seed the library.
		[Fact]
		public void A_games_folder_the_player_never_chose_does_not_seed_the_list()
		{
			string games = NewTempDir();
			try {
				Assert.Empty(LibraryFolders.Seed(null, false, games));
			} finally {
				Directory.Delete(games, true);
			}
		}

		[Fact]
		public void A_blank_games_folder_seeds_nothing()
		{
			Assert.Empty(LibraryFolders.Seed(null, true, "   "));
			Assert.Empty(LibraryFolders.Seed(null, true, ""));
		}

		//Seeding is a first-run act only: a list the player has already curated is
		//never rewritten by the folder the app used to have.
		[Fact]
		public void A_list_that_already_names_folders_is_never_reseeded()
		{
			string chosen = NewTempDir();
			string old = NewTempDir();
			try {
				IReadOnlyList<string> seeded = LibraryFolders.Seed(new[] { chosen }, true, old);
				Assert.Equal(new[] { Path.GetFullPath(chosen) }, seeded);
			} finally {
				Directory.Delete(chosen, true);
				Directory.Delete(old, true);
			}
		}

		//Seeding happens once, and "once" is the stored value being ABSENT: the
		//preference is null until the first run seeds it, and [] once the player has
		//taken every folder out. An emptied list is the player's decision, not an
		//unseeded one, so the old games folder must not come back on the next start.
		[Fact]
		public void An_emptied_list_is_never_reseeded()
		{
			string games = NewTempDir();
			try {
				IReadOnlyList<string> seeded = LibraryFolders.Seed(null, true, games);
				Assert.Equal(new[] { Path.GetFullPath(games) }, seeded);

				IReadOnlyList<string> emptied = LibraryFolders.Remove(seeded, games);
				Assert.Empty(emptied);

				Assert.Empty(LibraryFolders.Seed(emptied, true, games));
			} finally {
				Directory.Delete(games, true);
			}
		}

		[Fact]
		public void An_added_folder_is_normalised_and_kept()
		{
			string games = NewTempDir();
			try {
				(string plain, string trailing, string dotted) = Spellings(games);
				LibraryFolderEdit edit = LibraryFolders.Add(new List<string>(), trailing);
				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Equal(new[] { Path.GetFullPath(plain) }, edit.Folders);

				//Every spelling of one folder is the same folder: the list cannot
				//grow a second row for it.
				edit = LibraryFolders.Add(edit.Folders, dotted);
				Assert.Equal(LibraryFolderChange.AlreadyListed, edit.Change);
				Assert.Single(edit.Folders);
			} finally {
				Directory.Delete(games, true);
			}
		}

		[Fact]
		public void A_blank_add_is_refused()
		{
			LibraryFolderEdit edit = LibraryFolders.Add(new List<string>(), "  ");
			Assert.Equal(LibraryFolderChange.Invalid, edit.Change);
			Assert.Empty(edit.Folders);
		}

		//A folder name may END in a space, and `/roms/NES ` is then a different
		//folder from `/roms/NES` - a real one on disk, with different games in it.
		//Rewriting the typed path into the trimmed one would silently point the
		//library at a folder the player never named, so the path is stored as the
		//caller gave it. Only a path that is BLANK is refused; whitespace around a
		//name is part of the name.
		[Fact]
		public void A_folder_name_that_ends_in_a_space_is_stored_as_given()
		{
			string games = NewTempDir();
			string spaced = Path.Combine(games, "NES ");
			Directory.CreateDirectory(spaced);
			try {
				LibraryFolderEdit edit = LibraryFolders.Add(new List<string>(), spaced);

				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Single(edit.Folders);
				Assert.Equal(Path.GetFullPath(spaced), edit.Folders[0]);
				Assert.EndsWith("NES ", edit.Folders[0]);

				//And the trimmed spelling is a DIFFERENT folder, not the same one
				//under another spelling: adding it is a second row.
				edit = LibraryFolders.Add(edit.Folders, spaced.TrimEnd());
				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Equal(2, edit.Folders.Count);
			} finally {
				Directory.Delete(games, true);
			}
		}

		//Two folders that differ only in case are two folders, or one, depending on
		//what the file system under them says - and that is not the same answer on
		//every Mac: the default APFS volume folds case, a case-sensitive APFS volume
		//does not. So the caller that knows the volume (the picker, #1032's scan)
		//passes the comparison in, and this module stops guessing from the OS name.
		//
		//With a case-SENSITIVE comparison `/roms/NES` and `/roms/nes` are two folders:
		//adding the second is a second row, `/roms/nes/sub` is not inside `/roms/NES`,
		//and the two ROMs are two entries in the grid.
		[Fact]
		public void A_case_sensitive_comparison_keeps_folders_that_differ_only_in_case_apart()
		{
			string games = NewTempDir();
			string upper = Path.Combine(games, "NES");
			string lower = Path.Combine(games, "nes");
			try {
				LibraryFolderEdit edit = LibraryFolders.Add(new[] { upper }, lower, StringComparison.Ordinal);
				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Equal(new[] { Path.GetFullPath(upper), Path.GetFullPath(lower) }, edit.Folders);

				//`nes/sub` is not below `NES`, so it is not covered by it either.
				edit = LibraryFolders.Add(new[] { upper }, Path.Combine(lower, "sub"), StringComparison.Ordinal);
				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Equal(2, edit.Folders.Count);

				//And the grid keeps both ROMs: two paths, two entries.
				IReadOnlyList<string> union = LibraryFolders.Union(new[] {
					new[] { upper + "/contra.nes" },
					new[] { lower + "/contra.nes" }
				}, StringComparison.Ordinal);
				Assert.Equal(2, union.Count);
			} finally {
				Directory.Delete(games, true);
			}
		}

		//The other answer, on a volume that folds case: the same two paths are one
		//folder, and every comparison in the file - same folder, inside, the grid's
		//set, and remove - has to fold together, not just the first one.
		[Fact]
		public void A_case_insensitive_comparison_folds_folders_that_differ_only_in_case()
		{
			string games = NewTempDir();
			string upper = Path.Combine(games, "NES");
			string lower = Path.Combine(games, "nes");
			try {
				LibraryFolderEdit edit = LibraryFolders.Add(new[] { upper }, lower, StringComparison.OrdinalIgnoreCase);
				Assert.Equal(LibraryFolderChange.AlreadyListed, edit.Change);
				Assert.Single(edit.Folders);

				//`nes/sub` IS below `NES` here, so it is absorbed rather than listed.
				edit = LibraryFolders.Add(new[] { upper }, Path.Combine(lower, "sub"), StringComparison.OrdinalIgnoreCase);
				Assert.Equal(LibraryFolderChange.CoveredByListed, edit.Change);
				Assert.Single(edit.Folders);

				IReadOnlyList<string> union = LibraryFolders.Union(new[] {
					new[] { upper + "/contra.nes" },
					new[] { lower + "/contra.nes" }
				}, StringComparison.OrdinalIgnoreCase);
				Assert.Single(union);

				Assert.Empty(LibraryFolders.Remove(new[] { upper }, lower, StringComparison.OrdinalIgnoreCase));
			} finally {
				Directory.Delete(games, true);
			}
		}

		//A caller that names no comparison gets the rule this file had before the
		//comparison became injectable: Windows and macOS fold case, everything else
		//does not. Pinned behaviourally as well as by the exposed default, so the
		//default cannot drift without a test going red on the platform it changes on.
		[Fact]
		public void The_default_comparison_is_the_operating_systems_own_rule()
		{
			bool foldsCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
			StringComparison expected = foldsCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			Assert.Equal(expected, LibraryFolders.DefaultComparison);

			string games = NewTempDir();
			string upper = Path.Combine(games, "NES");
			string lower = Path.Combine(games, "nes");
			try {
				LibraryFolderEdit edit = LibraryFolders.Add(new[] { upper }, lower);
				if(foldsCase) {
					Assert.Equal(LibraryFolderChange.AlreadyListed, edit.Change);
					Assert.Single(edit.Folders);
				} else {
					Assert.Equal(LibraryFolderChange.Added, edit.Change);
					Assert.Equal(2, edit.Folders.Count);
				}
			} finally {
				Directory.Delete(games, true);
			}
		}

		//Decision 8's nested case, resolved by ADR-0264's "the folders shape the
		//scan, not the list": a folder inside one that is already listed adds no
		//game the list does not already reach, so it is absorbed rather than added
		//as a second row.
		[Fact]
		public void A_folder_inside_a_listed_one_is_absorbed()
		{
			string games = NewTempDir();
			string nested = Path.Combine(games, "nes");
			Directory.CreateDirectory(nested);
			try {
				LibraryFolderEdit edit = LibraryFolders.Add(new[] { games }, nested);
				Assert.Equal(LibraryFolderChange.CoveredByListed, edit.Change);
				Assert.Equal(new[] { Path.GetFullPath(games) }, edit.Folders);
			} finally {
				Directory.Delete(games, true);
			}
		}

		//The same rule read the other way: a folder that CONTAINS listed ones
		//merges them, because the ancestor already reaches their games.
		[Fact]
		public void Adding_a_folder_that_contains_listed_ones_merges_them()
		{
			string games = NewTempDir();
			string nes = Path.Combine(games, "nes");
			string gb = Path.Combine(games, "gb");
			Directory.CreateDirectory(nes);
			Directory.CreateDirectory(gb);
			try {
				LibraryFolderEdit edit = LibraryFolders.Add(new[] { nes, gb }, games);
				Assert.Equal(LibraryFolderChange.MergedWithListed, edit.Change);
				Assert.Equal(new[] { Path.GetFullPath(games) }, edit.Folders);
			} finally {
				Directory.Delete(games, true);
			}
		}

		[Fact]
		public void Removing_a_folder_leaves_every_file_on_disk()
		{
			string games = NewTempDir();
			string rom = Path.Combine(games, "contra.nes");
			File.WriteAllText(rom, "");
			try {
				IReadOnlyList<string> after = LibraryFolders.Remove(new[] { games }, games);
				Assert.Empty(after);
				Assert.True(File.Exists(rom));
			} finally {
				Directory.Delete(games, true);
			}
		}

		[Fact]
		public void Removing_a_folder_that_is_not_listed_changes_nothing()
		{
			string listed = NewTempDir();
			string other = NewTempDir();
			try {
				IReadOnlyList<string> after = LibraryFolders.Remove(new[] { listed }, other);
				Assert.Equal(new[] { Path.GetFullPath(listed) }, after);
			} finally {
				Directory.Delete(listed, true);
				Directory.Delete(other, true);
			}
		}

		//Decision 8's header, and the plural every count of one has to get right.
		//M is the LIST's row count - the folders the player put in their library -
		//and nothing else: the header is a statement about their library, so a row
		//that gave the grid nothing is still a row it names (ADR-0264 Decision 8).
		[Theory]
		[InlineData(0, 0, "Your library · 0 games in 0 folders")]
		[InlineData(1, 1, "Your library · 1 game in 1 folder")]
		[InlineData(2, 1, "Your library · 2 games in 1 folder")]
		[InlineData(2, 3, "Your library · 2 games in 3 folders")]
		[InlineData(1150, 4, "Your library · 1150 games in 4 folders")]
		public void The_header_names_the_games_and_the_folders(int games, int folders, string expected)
		{
			Assert.Equal(expected, LibraryFolders.Header(games, folders));
		}

		//Where the two counts come from, which is the part the formatter cannot say
		//itself: the game count is the scan's answer, the folder count is the list's
		//own row count - not the folders that answered with games. A listed folder
		//holding no ROM is still a row the player put there, so it is still named.
		[Fact]
		public void The_header_counts_the_lists_rows_not_the_folders_that_answered()
		{
			string withGames = NewTempDir();
			string withoutGames = NewTempDir();
			try {
				string rom = Path.Combine(withGames, "contra.nes");
				File.WriteAllText(rom, "");

				IReadOnlyList<string> list = LibraryFolders.Add(
					LibraryFolders.Add(new List<string>(), withGames).Folders, withoutGames).Folders;
				IReadOnlyList<string> grid = LibraryFolders.Union(new[] {
					new[] { rom },
					Array.Empty<string>()
				});

				//Two rows, one of which gave the grid a game: the library is two
				//folders, so that is what the header says.
				Assert.Equal(2, list.Count);
				Assert.Single(grid);
				Assert.Equal("Your library · 1 game in 2 folders", LibraryFolders.Header(grid.Count, list.Count));
			} finally {
				Directory.Delete(withGames, true);
				Directory.Delete(withoutGames, true);
			}
		}

		//Overlapping folders are two scans over the same files, and the player
		//sees one grid: a ROM found under both appears once.
		//What "each path once" means, pinned rather than claimed: the list and the
		//union compare SPELLINGS, not files. `Normalize` is lexical (`Path.GetFullPath`
		//does not resolve a link), so a game reached through a symlink and the same
		//game reached through its target are two rows here, not one. Resolving links
		//is a disk read and belongs to the scan (#1032), which is where the grid's
		//de-duplication will happen - this module must not claim it does it.
		//Two spellings, no link: the rule under test is lexical, so it reads the same
		//whether a link exists or not - and creating one needs a privilege Windows
		//test hosts do not have by default.
		[Fact]
		public void A_game_reached_through_a_symlink_is_a_second_row()
		{
			string games = Path.Combine(Path.GetTempPath(), "mesence-library-symlink-spelling");
			string rom = Path.Combine(games, "contra.nes");
			string link = Path.Combine(games, "contra-linked.nes");

			//The list too: the link is its own row, because the path is its own path.
			LibraryFolderEdit edit = LibraryFolders.Add(
				LibraryFolders.Add(new List<string>(), rom).Folders, link);
			Assert.Equal(LibraryFolderChange.Added, edit.Change);
			Assert.Equal(2, edit.Folders.Count);

			IReadOnlyList<string> union = LibraryFolders.Union(new[] {
				new[] { rom },
				new[] { link }
			});
			Assert.Equal(new[] { Path.GetFullPath(rom), Path.GetFullPath(link) }, union);
		}

		[Fact]
		public void Overlapping_folders_list_each_rom_once()
		{
			string games = NewTempDir();
			string nes = Path.Combine(games, "nes");
			Directory.CreateDirectory(nes);
			try {
				string top = Path.Combine(games, "contra.nes");
				string inner = Path.Combine(nes, "mario.nes");
				IReadOnlyList<string> union = LibraryFolders.Union(new[] {
					new[] { top, inner },
					new[] { inner }
				});

				Assert.Equal(new[] { Path.GetFullPath(top), Path.GetFullPath(inner) }, union);
			} finally {
				Directory.Delete(games, true);
			}
		}
	}
}
