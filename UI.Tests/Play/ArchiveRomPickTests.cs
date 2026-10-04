using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0249 (W-P5 sheet shape): an archive's games - when the list is asked
	//for, which inner file a pick names, what the search keeps, and that Player
	//mode asks on a sheet in the main window while Advanced keeps the window.
	public class ArchiveRomPickTests
	{
		[Theory]
		[InlineData(0, ArchiveRomRoute.WholeFile)]
		[InlineData(1, ArchiveRomRoute.OnlyGame)]
		[InlineData(2, ArchiveRomRoute.Ask)]
		[InlineData(40, ArchiveRomRoute.Ask)]
		public void Only_two_or_more_games_ask(int count, ArchiveRomRoute expected)
		{
			Assert.Equal(expected, ArchiveRomPick.Route(count));
		}

		[Fact]
		public void A_utf8_name_is_found_by_name_and_any_other_by_its_one_based_position()
		{
			Assert.Equal(0, ArchiveRomPick.InnerFileIndex(isUtf8: true, position: 3));
			Assert.Equal(1, ArchiveRomPick.InnerFileIndex(isUtf8: false, position: 0));
			Assert.Equal(4, ArchiveRomPick.InnerFileIndex(isUtf8: false, position: 3));
		}

		[Theory]
		[InlineData("Contra (USA).nes", "", true)]
		[InlineData("Contra (USA).nes", "   ", true)]
		[InlineData("Contra (USA).nes", "contra", true)]
		[InlineData("Contra (USA).nes", "SUPER", false)]
		public void The_search_keeps_names_containing_it_ignoring_case(string name, string search, bool kept)
		{
			Assert.Equal(kept, ArchiveRomPick.Matches(name, search));
		}

		[Theory]
		[InlineData(true, true, true)]
		[InlineData(true, false, false)]
		[InlineData(false, true, false)]
		[InlineData(false, false, false)]
		public void Player_mode_asks_on_a_sheet_in_the_main_window(bool playerMode, bool ownerIsMainWindow, bool sheet)
		{
			Assert.Equal(sheet, ArchiveRomPick.UsesSheet(playerMode, ownerIsMainWindow));
		}
	}
}
