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
				IReadOnlyList<string> seeded = LibraryFolders.Seed(new List<string>(), true, games);
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
				Assert.Empty(LibraryFolders.Seed(new List<string>(), false, games));
			} finally {
				Directory.Delete(games, true);
			}
		}

		[Fact]
		public void A_blank_games_folder_seeds_nothing()
		{
			Assert.Empty(LibraryFolders.Seed(new List<string>(), true, "   "));
			Assert.Empty(LibraryFolders.Seed(new List<string>(), true, ""));
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
		//M counts the folders that answered with games - the ones the grid is
		//actually drawn from - so the header never claims a folder that gave
		//nothing (ADR-0260 is the same rule for the older games folder).
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

		//Overlapping folders are two scans over the same files, and the player
		//sees one grid: a ROM found under both appears once.
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
