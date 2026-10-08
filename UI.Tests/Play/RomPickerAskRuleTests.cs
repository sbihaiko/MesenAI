using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1067: the sheet asks about the tiles it is showing only while it is up AND
	//showing the library; the headless wiring case cannot run in CI (no native core),
	//so the rule itself is pinned here.
	public class RomPickerAskRuleTests
	{
		[Theory]
		[InlineData(true, true, true)]
		[InlineData(true, false, false)]
		[InlineData(false, true, false)]
		[InlineData(false, false, false)]
		public void The_grid_is_walked_only_for_an_open_sheet_showing_the_library(bool sheetOpen, bool showingLibrary, bool walks)
		{
			Assert.Equal(walks, RomPickerAskRule.ShouldWalkGrid(sheetOpen, showingLibrary));
		}
	}
}
