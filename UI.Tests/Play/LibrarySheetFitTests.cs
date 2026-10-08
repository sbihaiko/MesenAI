using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1078: the *Open a game* sheet is the wireframes' 1000 px wide when the
	//window has room for it, and the window's own width - minus its two 24 px
	//margins - when it does not. Before this, the width was a fixed 1000: a
	//non-maximized window narrower than 1048 px got a sheet wider than itself,
	//centred, so both of its edges - the header's start, the console filter's
	//first segments, the first grid column and *Browse a file…* - were cut off
	//by the window. The arithmetic lives here (host-free, ADR-0123) so the rule
	//can be asserted without a window; the view only applies it.
	public class LibrarySheetFitTests
	{
		[Theory]
		[InlineData(1600, 1000)]  //room to spare: the wireframe's own width
		[InlineData(1048, 1000)]  //exactly the sheet plus its two margins
		[InlineData(1000, 952)]   //the window #1078 was reported at
		[InlineData(900, 852)]
		[InlineData(760, 712)]
		public void The_sheet_is_the_wireframes_width_or_the_window_minus_its_margins(double windowWidth, double expected)
		{
			Assert.Equal(expected, LibrarySheetFit.SheetWidth(windowWidth));
		}

		[Fact]
		public void The_content_width_is_what_the_themes_sheet_padding_leaves()
		{
			//`.player Border.sheet` pads 24 px on each side (PlayerTheme), so a
			//952 px sheet leaves 904 px of content.
			Assert.Equal(904, LibrarySheetFit.ContentWidth(1000));
			Assert.Equal(952, LibrarySheetFit.ContentWidth(1600));
		}

		[Fact]
		public void A_window_narrower_than_its_own_margins_asks_for_no_width_rather_than_a_negative_one()
		{
			Assert.Equal(0, LibrarySheetFit.SheetWidth(30));
		}
	}
}
