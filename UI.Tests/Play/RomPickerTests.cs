using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#845 (ADR-0256 Decision 9): the ROM picker's rules, pinned against a fake
	//tree. Every path here is a name the view-model would have read off a disk;
	//nothing in PlayRomPicker touches one, which is what lets the rules be
	//tested without a filesystem, a pad or a window.
	public class RomPickerTests
	{
		private static readonly RomPickerRoot[] Roots = {
			new("Your games", R("/games")),
			new("MesenAI's games folder", R("/home/roms")),
			new("My Book", R("/Volumes/My Book"))
		};

		private static string R(string path) => Path.GetFullPath(path);

		private static string[] Labels(IReadOnlyList<RomPickerRoot> roots) => roots.Select(r => r.Label).ToArray();

		[Fact]
		public void The_roots_lead_with_the_configured_folder_then_the_apps_own_then_the_volumes()
		{
			Assert.Equal(
				new[] { "Your games", "MesenAI's games folder", "My Book" },
				Labels(PlayRomPicker.Roots("/games", "/home/roms", new[] { "/Volumes/My Book" })));
		}

		//A machine set up on purpose lands on its own folder; the app's own folder
		//is always a root, so a fresh install still has one.
		[Fact]
		public void Without_a_configured_folder_the_apps_own_is_the_first_root()
		{
			Assert.Equal(
				new[] { "MesenAI's games folder", "My Book" },
				Labels(PlayRomPicker.Roots(null, "/home/roms", new[] { "/Volumes/My Book" })));
			Assert.Equal(
				new[] { "MesenAI's games folder" },
				Labels(PlayRomPicker.Roots("  ", "/home/roms", new[] { "" })));
		}

		//A volume that is also the configured folder is one row, not two: the
		//player would otherwise see the same place twice and have to guess.
		[Fact]
		public void A_folder_named_twice_is_one_root()
		{
			Assert.Equal(
				new[] { "Your games", "MesenAI's games folder" },
				Labels(PlayRomPicker.Roots("/games", "/home/roms", new[] { "/games", "/GAMES/" })));
		}

		//Folders first - they are the way through - then games, each alphabetical
		//and case-insensitive. A file that is not a game is not a row, and neither
		//is anything whose name starts with a dot, at any level.
		[Fact]
		public void Folders_come_first_then_games_and_nothing_else_is_a_row()
		{
			IReadOnlyList<RomPickerRow> rows = PlayRomPicker.Rows(
				new[] { R("/games/zeta"), R("/games/Alpha"), R("/games/.git") },
				new[] { R("/games/contra.nes"), R("/games/README.txt"), R("/games/beta.zip"), R("/games/.DS_Store"), R("/games/Down.7z") });

			Assert.Equal(new[] { "Alpha", "zeta", "beta.zip", "contra.nes", "Down.7z" }, rows.Select(r => r.Label).ToArray());
			//A folder row descends; a game row is the pick.
			Assert.Equal(new[] { true, true, false, false, false }, rows.Select(r => r.IsFolder).ToArray());
			//And the path each row carries is the one the view-model read.
			Assert.Equal(R("/games/Alpha"), rows[0].Path);
			Assert.Equal(R("/games/contra.nes"), rows[3].Path);
		}

		//The ROM extensions FolderHelper has always carried, plus the two archive
		//kinds everything downstream already unpacks.
		[Theory]
		[InlineData("Contra.nes", true)]
		[InlineData("Tetris.GB", true)]
		[InlineData("Sonic.sms", true)]
		[InlineData("game.gba", true)]
		[InlineData("pack.zip", true)]
		[InlineData("pack.7z", true)]
		[InlineData("notes.txt", false)]
		[InlineData("movie.mmo", false)]
		[InlineData("state.mst", false)]
		[InlineData("noextension", false)]
		public void Only_a_game_or_an_archive_is_openable(string name, bool openable)
		{
			Assert.Equal(openable, RomFileKinds.IsOpenable(name));
		}

		[Fact]
		public void A_root_is_where_up_stops_and_the_picker_goes_back_to_its_first_list()
		{
			Assert.Null(PlayRomPicker.Ascend("/games", Roots));
			Assert.Null(PlayRomPicker.Ascend("/Volumes/My Book", Roots));
			//Inside one, up is one step - and it never leaves the tree on its own.
			Assert.Equal(R("/games/nes"), PlayRomPicker.Ascend("/games/nes/usa", Roots));
			Assert.Equal(R("/games"), PlayRomPicker.Ascend("/games/nes", Roots));
		}

		//The path line names a root by its label, shortens the home folder to `~`,
		//and is empty on the roots list, where there is no folder to name.
		[Fact]
		public void The_path_line_names_the_place()
		{
			Assert.Equal("", PlayRomPicker.PathText(null, Roots, "/home"));
			Assert.Equal("Your games", PlayRomPicker.PathText("/games", Roots, "/home"));
			Assert.Equal("My Book", PlayRomPicker.PathText("/Volumes/My Book", Roots, "/home"));
			Assert.Equal("~/roms/x", PlayRomPicker.PathText("/home/roms/x", Roots, "/home"));
			//Outside the home folder it is the whole path: there is nothing to
			//shorten it to.
			Assert.Equal("/opt/games/nes", PlayRomPicker.PathText("/opt/games/nes", Roots, "/home"));
		}

		//The first list is the roots themselves, and every one of them descends.
		[Fact]
		public void The_first_list_is_the_roots()
		{
			IReadOnlyList<RomPickerRow> rows = PlayRomPicker.RootRows(Roots);
			Assert.Equal(new[] { "Your games", "MesenAI's games folder", "My Book" }, rows.Select(r => r.Label).ToArray());
			Assert.All(rows, r => Assert.True(r.IsFolder, r.Label + " is a place, so it descends"));
			Assert.Equal(new[] { R("/games"), R("/home/roms"), R("/Volumes/My Book") }, rows.Select(r => r.Path).ToArray());
		}

		//An empty folder is no rows, which is what the view-model turns into the
		//sentence on the sheet rather than a list with nothing in it.
		[Fact]
		public void A_folder_with_nothing_to_open_has_no_rows()
		{
			Assert.Empty(PlayRomPicker.Rows(new[] { R("/games/.hidden") }, new[] { R("/games/a.txt"), R("/games/.b.nes") }));
		}

		//The review of #845 (Grok 4.6, 2026-10-05): the configured game folder is a
		//string out of settings.json, which a person can edit, and Path.GetFullPath
		//throws on one the platform cannot spell. That throw would leave the press
		//that opened the picker - the pad's Confirm on the home - and the sheet
		//would never appear on the machine this exists for. A path that cannot be
		//spelled is not a root: it is left out, like a blank.
		[Fact]
		public void A_root_the_platform_cannot_spell_is_left_out_instead_of_throwing()
		{
			//The unspellable folder is dropped and the two that can be spelled are
			//not: this is a root left out, not a picker that refuses to open.
			Assert.Equal(new[] { R("/roms"), R("/Volumes/My Book") },
				PlayRomPicker.Roots("bad\0path", "/roms", new[] { "/Volumes/My Book" }).Select(r => r.Folder).ToArray());
			//And a volume the platform cannot spell is not a row either.
			Assert.Equal(new[] { R("/roms") }, PlayRomPicker.Roots(null, "/roms", new[] { "bad\0volume" }).Select(r => r.Folder).ToArray());
		}
	}
}
