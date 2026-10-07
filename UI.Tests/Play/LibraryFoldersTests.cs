using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
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

		//The player-facing strings, read from the file the app reads rather than
		//restated here: the header's sentence is the resource's, so a test that typed
		//its own copy would pass while the shipped one said something else. This
		//project has no `<ProjectReference>` to UI.csproj (UI.Tests/AGENTS.md), so the
		//XML is the only way to the same text - the same route
		//`GamesFolderNoticeTests`, `PlayResumeHintTests` and `MenuPathHintTests` take.
		private static Dictionary<string, string> ResourceTexts()
		{
			XDocument doc = XDocument.Load(Path.Combine(FindRepoRoot(), "UI", "Localization", "resources.en.xml"));
			Dictionary<string, string> texts = new();
			foreach(XElement node in doc.Descendants().Where(n => n.Name.LocalName == "Message")) {
				string? id = node.Attribute("ID")?.Value;
				if(id != null) {
					texts.TryAdd(id, node.Value);
				}
			}
			return texts;
		}

		private static string FindRepoRoot()
		{
			DirectoryInfo? dir = new(AppContext.BaseDirectory);
			while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
				dir = dir.Parent;
			}
			return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Mesen.sln) from " + AppContext.BaseDirectory);
		}

		//How many folder levels below `parent` the folder `child` sits, read off the
		//paths alone. ADR-0264 Decision 9 bounds the scan at six levels below a
		//library folder, so this is the number that decides whether a row's games
		//are inside the budget.
		private static int LevelsBelow(string parent, string child)
		{
			string relative = Path.GetRelativePath(Path.GetFullPath(parent), Path.GetFullPath(child));
			return relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;
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

		//The enum names every answer an add gives, and nothing it cannot. A value no
		//caller can ever receive is a branch #1032 would have to write in the view for
		//an outcome that cannot happen, and the answer to "did the list change?" would
		//then be spread over values that never arrive.
		[Fact]
		public void Add_only_answers_the_answers_it_can_give()
		{
			Assert.Equal(
				new[] { LibraryFolderChange.Added, LibraryFolderChange.AlreadyListed, LibraryFolderChange.Invalid },
				Enum.GetValues<LibraryFolderChange>());
		}

		//A folder name may END in a space, and `/roms/NES ` is then a different
		//folder from `/roms/NES` - a real one on disk, with different games in it.
		//Rewriting the typed path into the trimmed one would silently point the
		//library at a folder the player never named, so the path is stored as the
		//caller gave it. Only a path that is BLANK is refused; whitespace around a
		//name is part of the name.
		//
		//Where the platform has already decided the question, this module does not
		//second-guess it: Windows' own path rules strip a trailing space, so there
		//`NES ` IS `NES` and the second add is the same row. The module stores
		//`Path.GetFullPath`'s spelling on both platforms - what differs is what the
		//platform's `GetFullPath` answers, so each platform asserts its own rule
		//rather than one of them asserting the other's.
		[Fact]
		public void A_folder_name_that_ends_in_a_space_follows_the_platforms_path_rule()
		{
			string games = NewTempDir();
			string spaced = Path.Combine(games, "NES ");
			Directory.CreateDirectory(spaced);
			try {
				LibraryFolderEdit edit = LibraryFolders.Add(new List<string>(), spaced);

				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Single(edit.Folders);
				Assert.Equal(Path.GetFullPath(spaced), edit.Folders[0]);

				if(OperatingSystem.IsWindows()) {
					//The space is already gone: `GetFullPath` trimmed it, which is
					//Windows' rule for the name and not this module rewriting it.
					Assert.EndsWith("NES", edit.Folders[0]);
					Assert.DoesNotContain(" ", edit.Folders[0]);

					//So the trimmed spelling names the same folder, and it is one row.
					edit = LibraryFolders.Add(edit.Folders, spaced.TrimEnd());
					Assert.Equal(LibraryFolderChange.AlreadyListed, edit.Change);
					Assert.Single(edit.Folders);
				} else {
					//The space is part of the name, and the path is stored as given.
					Assert.EndsWith("NES ", edit.Folders[0]);

					//And the trimmed spelling is a DIFFERENT folder, not the same one
					//under another spelling: adding it is a second row.
					edit = LibraryFolders.Add(edit.Folders, spaced.TrimEnd());
					Assert.Equal(LibraryFolderChange.Added, edit.Change);
					Assert.Equal(2, edit.Folders.Count);
				}
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

				//`nes/sub` IS below `NES` here, and it is still a row of its own: the
				//scan under `NES` is depth-bounded, so a subfolder is not covered by
				//being reached (ADR-0264 Decision 9).
				edit = LibraryFolders.Add(new[] { upper }, Path.Combine(lower, "sub"), StringComparison.OrdinalIgnoreCase);
				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Equal(2, edit.Folders.Count);

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

		//Decision 8's nested case: a folder inside one that is already listed is a
		//row of its OWN, because "the list already reaches it" is not the same as
		//"the scan already reaches it". ADR-0264 Decision 9 bounds the scan at six
		//levels below a library folder, so a nested root reached through its parent
		//is one level further down than the same root reached directly - the parent
		//can be past the budget on a folder the nested row still reaches. The two
		//rows answer the same games once, through `Union`, which is where the grid's
		//de-duplication lives.
		[Fact]
		public void A_folder_inside_a_listed_one_is_listed_on_its_own_row()
		{
			string games = NewTempDir();
			string nested = Path.Combine(games, "nes");
			Directory.CreateDirectory(nested);
			try {
				LibraryFolderEdit edit = LibraryFolders.Add(new[] { games }, nested);
				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Equal(new[] { Path.GetFullPath(games), Path.GetFullPath(nested) }, edit.Folders);
			} finally {
				Directory.Delete(games, true);
			}
		}

		//The same rule read the other way: a folder that CONTAINS listed ones is
		//added BESIDE them, never in their place. Replacing them would re-root their
		//subtrees one level higher, and the scan is bounded at six levels (ADR-0264
		//Decision 9) - so a merge silently drops the games that sat at the boundary.
		[Fact]
		public void Adding_a_folder_that_contains_listed_ones_keeps_them_as_rows()
		{
			string games = NewTempDir();
			string nes = Path.Combine(games, "nes");
			string gb = Path.Combine(games, "gb");
			Directory.CreateDirectory(nes);
			Directory.CreateDirectory(gb);
			try {
				LibraryFolderEdit edit = LibraryFolders.Add(new[] { nes, gb }, games);
				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Equal(new[] { Path.GetFullPath(nes), Path.GetFullPath(gb), Path.GetFullPath(games) }, edit.Folders);
			} finally {
				Directory.Delete(games, true);
			}
		}

		//The boundary the two rules above exist for, stated as the case that was
		//lost: a ROM whose folder sits exactly six levels below a nested root. It is
		//inside the scan's budget below `NES` and one level PAST it below `roms`, so
		//absorbing `NES` into `roms` is what makes the game disappear from the
		//library. Both rows stay, the union answers the game once, and neither row
		//has to claim it alone.
		[Fact]
		public void A_nested_root_survives_its_parent_so_the_depth_budget_still_reaches_its_games()
		{
			string roms = NewTempDir();
			string nes = Path.Combine(roms, "NES");
			string sixDeep = Path.Combine(nes, "1", "2", "3", "4", "5", "6");
			Directory.CreateDirectory(sixDeep);
			try {
				string rom = Path.Combine(sixDeep, "contra.nes");

				//The whole regression, as arithmetic: within the six-level budget
				//below the nested root, one level beyond it below the parent.
				Assert.Equal(6, LevelsBelow(nes, sixDeep));
				Assert.Equal(7, LevelsBelow(roms, sixDeep));

				LibraryFolderEdit edit = LibraryFolders.Add(new List<string>(), nes);
				Assert.Equal(LibraryFolderChange.Added, edit.Change);

				//Adding the parent keeps the nested row, so the game is still reached
				//by a root that is inside the budget for it.
				edit = LibraryFolders.Add(edit.Folders, roms);
				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Equal(new[] { Path.GetFullPath(nes), Path.GetFullPath(roms) }, edit.Folders);

				//And the two rows are one grid: the game both scans answer is one
				//entry, not two.
				IReadOnlyList<string> union = LibraryFolders.Union(new[] {
					new[] { rom },
					new[] { rom }
				});
				Assert.Equal(new[] { Path.GetFullPath(rom) }, union);
			} finally {
				Directory.Delete(roms, true);
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
		//
		//The sentence is the RESOURCE's, not this module's: it is player-facing text,
		//so it is read out of `resources.en.xml` the way the view reads it, and the
		//expected strings below are the ADR's literal typed out independently. That is
		//what keeps the resource and the ADR from drifting apart unnoticed, and it is
		//why the module answers with an id rather than the sentence - the module is
		//host-free (ADR-0123) and cannot reach a resource file.
		[Theory]
		[InlineData(0, 0, "LibraryHeaderGamesInFolders", "Your library · 0 games in 0 folders")]
		[InlineData(1, 1, "LibraryHeaderOneGameInOneFolder", "Your library · 1 game in 1 folder")]
		[InlineData(2, 1, "LibraryHeaderGamesInOneFolder", "Your library · 2 games in 1 folder")]
		[InlineData(2, 3, "LibraryHeaderGamesInFolders", "Your library · 2 games in 3 folders")]
		[InlineData(1150, 4, "LibraryHeaderGamesInFolders", "Your library · 1150 games in 4 folders")]
		public void The_header_is_a_localized_sentence_the_module_only_selects(int games, int folders, string id, string expected)
		{
			Assert.Equal(id, LibraryFolders.HeaderResourceId(games, folders));

			//Read exactly as the view resolves it, with the same two arguments.
			Dictionary<string, string> texts = ResourceTexts();
			Assert.True(texts.TryGetValue(id, out string? text), "resources.en.xml has no <Message ID=\"" + id + "\">");
			Assert.Equal(expected, string.Format(CultureInfo.InvariantCulture, text, games, folders));
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

				Dictionary<string, string> texts = ResourceTexts();
				string id = LibraryFolders.HeaderResourceId(grid.Count, list.Count);
				Assert.Equal("Your library · 1 game in 2 folders", string.Format(CultureInfo.InvariantCulture, texts[id], grid.Count, list.Count));
			} finally {
				Directory.Delete(withGames, true);
				Directory.Delete(withoutGames, true);
			}
		}

		//The list and the union compare SPELLINGS, not files. `Normalize` is lexical
		//(`Path.GetFullPath` does not resolve a link), so a folder reached through a
		//symlink and the same folder reached directly are two ROWS, and a game under
		//them is two ENTRIES in the grid. Resolving a link is a disk read and belongs
		//to the scan (#1032), which is where the grid's de-duplication happens; this
		//module must not claim it does it.
		//
		//The link is real, so the name is earned rather than asserted: the test makes
		//one on disk and adds BOTH the target and the link, as folders, which is what
		//`Add` takes. Where the platform refuses a link to a test host - Windows needs
		//Developer Mode or an elevated process - the test says so out loud and falls
		//back to two plain folders, because the rule under test reads the same either
		//way. On a platform that allows a link, failing to make one is a test-host
		//failure and must not pass as "the fallback was tested".
		[Fact]
		public void A_game_reached_through_a_symlink_is_a_second_row()
		{
			string games = NewTempDir();
			string link = Path.Combine(Path.GetTempPath(), "mesence-library-link-" + Guid.NewGuid().ToString("N"));
			try {
				string rom = Path.Combine(games, "contra.nes");
				File.WriteAllText(rom, "");

				bool linked = TryCreateLink(link, games);
				if(!linked) {
					Assert.True(OperatingSystem.IsWindows(), "a directory link could not be created on a platform that allows one");
					Directory.CreateDirectory(link);
				}

				//Two rows, one per spelling - the link is not folded into its target.
				LibraryFolderEdit edit = LibraryFolders.Add(
					LibraryFolders.Add(new List<string>(), games).Folders, link);
				Assert.Equal(LibraryFolderChange.Added, edit.Change);
				Assert.Equal(new[] { Path.GetFullPath(games), Path.GetFullPath(link) }, edit.Folders);

				//And the same game under both spellings is two entries, not one.
				IReadOnlyList<string> union = LibraryFolders.Union(new[] {
					new[] { rom },
					new[] { Path.Combine(link, "contra.nes") }
				});
				Assert.Equal(2, union.Count);
				Assert.Equal(Path.GetFullPath(rom), union[0]);
			} finally {
				//`Directory.Delete` removes the LINK, never the folder it points at.
				if(Directory.Exists(link)) {
					Directory.Delete(link);
				}
				Directory.Delete(games, true);
			}
		}

		//A directory link, where the platform allows one. The answer is reported
		//rather than assumed: Windows test hosts need a privilege this suite does not
		//require, and the caller decides what to do about that.
		private static bool TryCreateLink(string link, string target)
		{
			try {
				Directory.CreateSymbolicLink(link, target);
				return true;
			} catch(Exception) {
				return false;
			}
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
