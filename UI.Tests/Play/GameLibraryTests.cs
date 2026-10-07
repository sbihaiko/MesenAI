using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1032 (ADR-0264 Decision 7/9): the flat library's rules, pinned against a
	//fake tree. The scan reads nothing itself - the folder listing comes in as
	//the FolderLister seam RomFolderScan already uses - so nesting, archives,
	//the depth cap and the count cap are all decided here without a disk, a pad
	//or a window. Only the rules a person would notice are asserted: which
	//entries come out, what they are called and how many folders answered.
	public class GameLibraryTests
	{
		private static string R(string path) => Path.GetFullPath(path);

		//One fake tree, listed the way a host lists a real one: a folder's
		//subfolders and its files, and nothing for a folder that does not exist
		//(a volume pulled out between listing and descending).
		private static FolderLister Tree(params string[] paths)
		{
			Dictionary<string, List<string>> folders = new(StringComparer.Ordinal);
			Dictionary<string, List<string>> files = new(StringComparer.Ordinal);
			foreach(string path in paths) {
				string full = R(path);
				files.TryAdd(full, new List<string>());
				folders.TryAdd(full, new List<string>());
				//Every ancestor is a real folder too - a chain that skipped the
				//folders between two named paths would not be a tree, and the
				//depth cap could never be reached.
				string? child = full;
				for(string? parent = Path.GetDirectoryName(full); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent)) {
					if(!folders.TryGetValue(parent, out List<string>? children)) {
						children = folders[parent] = new List<string>();
					}
					if(!children.Contains(child!)) {
						children.Add(child!);
					}
					files.TryAdd(parent, new List<string>());
					child = parent;
				}
			}
			return folder => (
				folders.TryGetValue(folder, out List<string>? sub) ? sub : new List<string>(),
				files.TryGetValue(folder, out List<string>? own) ? own : new List<string>());
		}

		private static string[] Paths(IReadOnlyList<LibraryEntry> entries) => entries.Select(e => e.Path).ToArray();

		private static string[] Titles(IReadOnlyList<LibraryEntry> entries) => entries.Select(e => e.Title).ToArray();

		//A library folder's own files are games, however deeply the rest is
		//nested: a folder row is never a row (ADR-0264 Decision 1), so the trees
		//a player actually has - `NES/`, `NES/Action/`, `Hacks/` - all arrive in
		//the same list.
		[Fact]
		public void A_nested_rom_is_an_entry_like_a_shallow_one()
		{
			FolderLister tree = Tree("/lib/NES", "/lib/NES/Action", "/lib/Hacks/2024");
			Dictionary<string, string[]> content = new(StringComparer.Ordinal) {
				[R("/lib/NES")] = new[] { R("/lib/NES/Contra.nes") },
				[R("/lib/NES/Action")] = new[] { R("/lib/NES/Action/Mega Man 2 (U).nes") },
				[R("/lib/Hacks/2024")] = new[] { R("/lib/Hacks/2024/Zelda Redux.gb") }
			};
			FolderLister withFiles = folder => {
				(IReadOnlyList<string> sub, IReadOnlyList<string> own) = tree(folder);
				return (sub, content.TryGetValue(folder, out string[]? f) ? f : own);
			};

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib" }, withFiles);

			Assert.Equal(
				new[] { R("/lib/Hacks/2024/Zelda Redux.gb"), R("/lib/NES/Action/Mega Man 2 (U).nes"), R("/lib/NES/Contra.nes") },
				Paths(result.Entries).OrderBy(p => p, StringComparer.Ordinal).ToArray());
		}

		//The grid is ordered by title (ADR-0264 Decision 1), so the order the
		//host happened to list a folder in cannot leak into what the player sees.
		[Fact]
		public void The_entries_are_ordered_by_title()
		{
			FolderLister tree = Tree("/lib/roms");
			FolderLister withFiles = folder => (
				tree(folder).Folders,
				folder == R("/lib/roms")
					? new[] { R("/lib/roms/Metroid.nes"), R("/lib/roms/Balloon Fight.nes"), R("/lib/roms/Castlevania.nes") }
					: Array.Empty<string>());

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib" }, withFiles);

			Assert.Equal(new[] { "Balloon Fight", "Castlevania", "Metroid" }, Titles(result.Entries));
		}

		//Everything downstream accepts an archive (RomFileKinds says so), and a
		//library on a cabinet is usually zipped - so a `.zip`/`.7z` is an entry,
		//and the archive path is the entry's path (ADR-0264 Decision 9).
		[Fact]
		public void An_archive_holding_a_rom_is_an_entry()
		{
			FolderLister tree = Tree("/lib/roms");
			FolderLister withFiles = folder => (
				tree(folder).Folders,
				folder == R("/lib/roms")
					? new[] { R("/lib/roms/Contra (U).nes.zip"), R("/lib/roms/collection.7z") }
					: Array.Empty<string>());

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib" }, withFiles);

			Assert.Equal(
				new[] { R("/lib/roms/Contra (U).nes.zip"), R("/lib/roms/collection.7z") },
				Paths(result.Entries).OrderBy(p => p, StringComparer.Ordinal).ToArray());
			//The archive's own extension goes too, so a zipped game reads as the
			//game and not as "Contra (U).nes.zip".
			Assert.Equal(new[] { "Contra", "collection" }, Titles(result.Entries).OrderBy(t => t, StringComparer.Ordinal).ToArray());
		}

		//An archive's console is the one its NAME names - `Contra (U).nes.zip` is
		//a NES game and is offered as one. A `collection.7z` names no console, so
		//it is Unknown rather than a guess: the picker must never claim a console
		//for a file whose machine it cannot tell (RomConsoleKinds' own rule).
		[Fact]
		public void An_archives_console_is_the_one_its_name_names()
		{
			FolderLister tree = Tree("/lib/roms");
			FolderLister withFiles = folder => (
				tree(folder).Folders,
				folder == R("/lib/roms")
					? new[] { R("/lib/roms/Contra (U).nes.zip"), R("/lib/roms/collection.7z"), R("/lib/roms/Kirby.gb") }
					: Array.Empty<string>());

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib" }, withFiles);

			Assert.Equal(RomConsole.Nes, result.Entries.Single(e => e.Path == R("/lib/roms/Contra (U).nes.zip")).Console);
			Assert.Equal(RomConsole.Unknown, result.Entries.Single(e => e.Path == R("/lib/roms/collection.7z")).Console);
			Assert.Equal(RomConsole.GameBoy, result.Entries.Single(e => e.Path == R("/lib/roms/Kirby.gb")).Console);
		}

		//A library folder is full of things that are not games - save files, a
		//readme, a box art the player dropped in, a `.DS_Store`. None of them is
		//a tile (RomFileKinds.IsOpenable is the one rule, reused rather than
		//restated), and neither is anything whose name starts with a dot.
		[Fact]
		public void A_file_that_is_not_openable_is_not_an_entry()
		{
			FolderLister tree = Tree("/lib/roms", "/lib/.git");
			FolderLister withFiles = folder => (
				tree(folder).Folders,
				folder == R("/lib/roms")
					? new[] {
						R("/lib/roms/Contra.nes"), R("/lib/roms/Contra.sav"), R("/lib/roms/readme.txt"),
						R("/lib/roms/box.png"), R("/lib/roms/.DS_Store")
					}
					: folder == R("/lib/.git") ? new[] { R("/lib/.git/config.nes") } : Array.Empty<string>());

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib" }, withFiles);

			Assert.Equal(new[] { R("/lib/roms/Contra.nes") }, Paths(result.Entries));
		}

		//The depth cap is the whole protection against a library folder pointed
		//at a whole disk (ADR-0264 Decision 9: LibraryScan.MaxDepth). A game at
		//the cap is found; one below it is not.
		[Fact]
		public void A_game_below_the_depth_cap_is_not_found()
		{
			string deep = "/lib/1/2/3/4/5/6";
			string tooDeep = "/lib/1/2/3/4/5/6/7";
			FolderLister tree = Tree("/lib", deep, tooDeep);
			FolderLister withFiles = folder => (
				tree(folder).Folders,
				folder == R(deep) ? new[] { R(deep + "/At The Cap.nes") }
					: folder == R(tooDeep) ? new[] { R(tooDeep + "/Past The Cap.nes") }
					: Array.Empty<string>());

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib" }, withFiles);

			Assert.Equal(new[] { R(deep + "/At The Cap.nes") }, Paths(result.Entries));
			Assert.Equal(6, GameLibrary.MaxDepth);
		}

		//A folder that lists itself - a symlinked home, a mount that points back
		//up - is a walk that never ends, so the walk visits each folder once.
		[Fact]
		public void A_folder_that_lists_itself_is_walked_once()
		{
			FolderLister withFiles = folder => (
				folder == R("/lib") ? new[] { R("/lib/lib") } : Array.Empty<string>(),
				new[] { R("/lib/Contra.nes") });

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib" }, withFiles);

			Assert.Single(result.Entries);
		}

		//The count cap: a library folder pointed at a whole disk lands on
		//LibraryScan.MaxEntries and says so in the header rather than silently
		//truncating (ADR-0264 Decision 9).
		[Fact]
		public void The_scan_stops_at_the_count_cap_and_says_so()
		{
			Assert.Equal(20000, GameLibrary.MaxEntries);
			string[] files = Enumerable.Range(0, GameLibrary.MaxEntries + 5).Select(i => R($"/lib/roms/Game {i:00000}.nes")).ToArray();
			FolderLister tree = Tree("/lib/roms");
			FolderLister withFiles = folder => (tree(folder).Folders, folder == R("/lib/roms") ? files : Array.Empty<string>());

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib" }, withFiles);

			Assert.Equal(GameLibrary.MaxEntries, result.Entries.Count);
			Assert.True(result.Truncated, "the cap was reached and the scan did not say so");
		}

		//A scan that stays under the cap is not truncated: the header must not
		//report a truncation that did not happen.
		[Fact]
		public void A_scan_under_the_cap_is_not_truncated()
		{
			FolderLister tree = Tree("/lib/roms");
			FolderLister withFiles = folder => (
				tree(folder).Folders,
				folder == R("/lib/roms") ? new[] { R("/lib/roms/Contra.nes") } : Array.Empty<string>());

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib" }, withFiles);

			Assert.False(result.Truncated);
		}

		//Review finding 6 on #1032: every case above lists a FAKE tree through
		//the FolderLister seam, so the disk half of the scan - the lister the app
		//itself runs - was covered only by the headless cases, which are skipped
		//where the native core is absent (CI runs that project with no core at
		//all). This one writes a real tree under Path.GetTempPath() and reads it
		//through DiskFolderLister, the production lister both the library scan
		//and the background walk go through.
		[Fact]
		public void The_scan_reads_a_real_tree_on_disk_through_the_production_lister()
		{
			string root = Path.Combine(Path.GetTempPath(), "mesen-1032-scan-" + Guid.NewGuid().ToString("N"));
			string nes = Path.Combine(root, "Console", "NES");
			string notes = Path.Combine(root, "notes");
			try {
				Directory.CreateDirectory(nes);
				Directory.CreateDirectory(notes);
				File.WriteAllBytes(Path.Combine(nes, "Contra (U) [!].nes"), new byte[16]);
				File.WriteAllBytes(Path.Combine(nes, "Metroid (USA).nes"), new byte[16]);
				//A file that is not a ROM, and a folder with nothing openable in
				//it: what the real disk offers that a fake tree only claims.
				File.WriteAllText(Path.Combine(nes, "readme.txt"), "not a rom");
				File.WriteAllText(Path.Combine(notes, "todo.txt"), "notes");

				LibraryScanResult result = GameLibrary.Scan(new[] { root }, DiskFolderLister.List);

				Assert.Equal(new[] { "Contra", "Metroid" }, result.Entries.Select(entry => entry.Title).ToArray());
				//One folder answered: the one the two ROMs are directly in.
				Assert.Equal(1, result.FolderCount);
				Assert.False(result.Truncated);
			} finally {
				try {
					Directory.Delete(root, true);
				} catch {
					//A case that failed before it built its tree leaves nothing to remove.
				}
			}
		}

		//The header counts the folders the library reads (ADR-0264 Decision 8):
		//a folder that answered nothing is not one of them, so the count is what
		//the scan actually found rather than what it was pointed at.
		[Fact]
		public void The_header_counts_the_folders_that_answered()
		{
			FolderLister tree = Tree("/lib-a/roms", "/lib-b/empty");
			FolderLister withFiles = folder => (
				tree(folder).Folders,
				folder == R("/lib-a/roms") ? new[] { R("/lib-a/roms/Contra.nes") } : Array.Empty<string>());

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib-a", "/lib-b" }, withFiles);

			Assert.Equal(1, result.FolderCount);
			Assert.Single(result.Entries);
		}

		//A library folder that is a subfolder of another library folder is not
		//two folders of games: the same ROM is one tile.
		[Fact]
		public void The_same_rom_under_two_library_folders_is_one_entry()
		{
			FolderLister tree = Tree("/lib/roms");
			FolderLister withFiles = folder => (
				tree(folder).Folders,
				folder == R("/lib/roms") ? new[] { R("/lib/roms/Contra.nes") } : Array.Empty<string>());

			LibraryScanResult result = GameLibrary.Scan(new[] { "/lib", "/lib/roms" }, withFiles);

			Assert.Single(result.Entries);
			Assert.Equal(1, result.FolderCount);
		}

		//A library folder that does not exist answers nothing rather than
		//throwing: a drive that is unplugged is not a crash.
		[Fact]
		public void A_library_folder_that_lists_nothing_yields_nothing()
		{
			LibraryScanResult result = GameLibrary.Scan(new[] { "/gone" }, _ => (Array.Empty<string>(), Array.Empty<string>()));

			Assert.Empty(result.Entries);
			Assert.Equal(0, result.FolderCount);
			Assert.False(result.Truncated);
		}

		//The clean title is what the tile carries (ADR-0264 Decision 7): the
		//extension and the region / revision / dump tags go, and what is left is
		//the name a person would say out loud.
		[Theory]
		[InlineData("Castlevania (U) [!].nes", "Castlevania")]
		[InlineData("Castlevania (USA).nes", "Castlevania")]
		[InlineData("Mega Man 2 (U) (Rev 1).nes", "Mega Man 2")]
		[InlineData("Super Mario Bros. 3 (Europe) (Rev A).nes", "Super Mario Bros. 3")]
		[InlineData("Zelda II - The Adventure of Link (U) [!].nes", "Zelda II - The Adventure of Link")]
		[InlineData("Pokemon - Red Version (USA, Europe) (SGB Enhanced).gb", "Pokemon - Red Version")]
		[InlineData("Kirby's Dream Land (World) (Rev A) [b].gb", "Kirby's Dream Land")]
		[InlineData("Sonic The Hedgehog (Europe, Brazil).sms", "Sonic The Hedgehog")]
		[InlineData("Contra (U) [!].nes.zip", "Contra")]
		[InlineData("Metroid.nes", "Metroid")]
		public void A_file_name_becomes_a_clean_title(string fileName, string expected)
		{
			Assert.Equal(expected, GameLibrary.CleanTitle(fileName));
		}

		//A name that is nothing but tags has no title left, so the tile falls
		//back to the file name without its extension - never to an empty tile.
		[Fact]
		public void A_name_that_is_only_tags_keeps_its_own_name()
		{
			Assert.Equal("(U) [!]", GameLibrary.CleanTitle("(U) [!].nes"));
		}

		//Sort order moves a leading article to the end (ADR-0264 Decision 7:
		//moved for sort order, not for display), so "The Legend of Zelda" files
		//under L where a player looks for it.
		[Theory]
		[InlineData("The Legend of Zelda", "Legend of Zelda, The")]
		[InlineData("A Boy and His Blob", "Boy and His Blob, A")]
		[InlineData("An American Tail", "American Tail, An")]
		[InlineData("Metroid", "Metroid")]
		[InlineData("Thexder", "Thexder")]
		public void Sort_order_moves_a_leading_article(string title, string expected)
		{
			Assert.Equal(expected, GameLibrary.SortTitle(title));
		}
	}
}
