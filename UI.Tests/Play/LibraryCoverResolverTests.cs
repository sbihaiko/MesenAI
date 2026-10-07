using System;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1035 (ADR-0264 Decision 6): which picture a tile draws, decided by the
	//library module and not by the sheet. The priority is the ADR's - art the
	//scan already resolved (a downloaded cover, a title screen) outranks the
	//player's own screenshot, which fills the gap the console-coloured generic
	//cover would otherwise take.
	//
	//The lookup is the seam: a function from a ROM path to screenshot bytes.
	//Nothing here reads a disk, a `.rgd` or a bitmap, so the rule is pinned
	//without a host (ADR-0123) and the sheet is left with one job - drawing the
	//answer it was handed.
	public class LibraryCoverResolverTests
	{
		private static LibraryEntry Entry(string path, LibraryCover cover = LibraryCover.Generic)
		{
			return new LibraryEntry(path, RomConsole.Nes, "A Game", cover);
		}

		//Decision 6 case 3: the screenshot of a game the player has already run.
		//It is the bytes the lookup answered with, handed on unchanged - the sheet
		//decodes them, this decides that they are the cover.
		[Fact]
		public void A_played_game_draws_the_screenshot_the_lookup_answered()
		{
			byte[] screenshot = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
			LibraryEntry entry = Entry("/roms/Contra (U) [!].nes");

			LibraryCoverPick pick = GameLibraryCover.Resolve(entry, path => path == entry.Path ? screenshot : null);

			Assert.Equal(LibraryCover.RecentScreenshot, pick.Cover);
			Assert.Same(screenshot, pick.Screenshot);
		}

		//A game the player never ran: the lookup answers nothing, and the tile
		//keeps the generic cover it always had - never an empty picture.
		[Fact]
		public void A_game_the_player_never_ran_keeps_the_generic_cover()
		{
			LibraryEntry entry = Entry("/roms/Metroid (USA).nes");

			LibraryCoverPick pick = GameLibraryCover.Resolve(entry, _ => null);

			Assert.Equal(LibraryCover.Generic, pick.Cover);
			Assert.Null(pick.Screenshot);
		}

		//Decision 6's order: an entry the scan already gave art to is cases 1 and
		//2, and the player's own screenshot does not replace it. The lookup is not
		//even asked - the `.rgd` read is skipped for a cover nothing would use.
		[Fact]
		public void Downloaded_art_outranks_the_players_own_screenshot()
		{
			LibraryEntry entry = Entry("/roms/Contra (U) [!].nes", LibraryCover.BoxArt);
			bool asked = false;

			LibraryCoverPick pick = GameLibraryCover.Resolve(entry, _ => {
				asked = true;
				return new byte[] { 1 };
			});

			Assert.Equal(LibraryCover.BoxArt, pick.Cover);
			Assert.Null(pick.Screenshot);
			Assert.False(asked, "the Recent list was read for an entry that already carries its own art");
		}

		//A title screen is the same case as box art: the scan's answer wins, and
		//the entry keeps the cover it was given rather than the screenshot.
		[Fact]
		public void A_title_screen_outranks_the_players_own_screenshot()
		{
			LibraryEntry entry = Entry("/roms/Contra (U) [!].nes", LibraryCover.TitleScreen);

			LibraryCoverPick pick = GameLibraryCover.Resolve(entry, _ => new byte[] { 1 });

			Assert.Equal(LibraryCover.TitleScreen, pick.Cover);
			Assert.Null(pick.Screenshot);
		}

		//The lookup is keyed by the entry's own path: a lookup that answers for
		//some other game must not hand this tile that game's picture.
		[Fact]
		public void The_lookup_is_asked_about_this_entrys_path()
		{
			LibraryEntry entry = Entry("/roms/Metroid (USA).nes");
			string? asked = null;

			LibraryCoverPick pick = GameLibraryCover.Resolve(entry, path => {
				asked = path;
				return null;
			});

			Assert.Equal(entry.Path, asked);
			Assert.Equal(LibraryCover.Generic, pick.Cover);
		}
	}
}
