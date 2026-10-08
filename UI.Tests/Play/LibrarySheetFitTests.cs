using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1078 (ADR-0264 Decision 12): the *Open a game* sheet is W-P19's own 1100 px
	//wide when the window has room for it, and the window's own width when it
	//does not. Before this, the width was a fixed 1000 - narrower than the
	//wireframe - and the rule also subtracted two 24 px horizontal margins the
	//sheet does not have (`Margin="0 24"` is vertical only), so a 1400 px window
	//got a 1000 px sheet and a narrower one lost 48 px it could have used: at
	//1000 px of window the sheet was 952, and its contents were laid out against
	//the width it was not given. The arithmetic lives here (host-free, ADR-0123)
	//so the rule can be asserted without a window; the view only applies it.
	public class LibrarySheetFitTests
	{
		[Theory]
		[InlineData(1600, 1100)]  //room to spare: the wireframe's own width
		[InlineData(1100, 1100)]  //exactly the sheet
		[InlineData(1000, 1000)]  //the window #1078 was reported at: the whole window
		[InlineData(900, 900)]
		[InlineData(760, 760)]
		public void The_sheet_is_the_wireframes_width_or_the_window_it_is_drawn_in(double windowWidth, double expected)
		{
			Assert.Equal(expected, LibrarySheetFit.SheetWidth(windowWidth));
		}

		[Fact]
		public void The_content_width_is_what_the_themes_sheet_padding_leaves()
		{
			//`.player Border.sheet` pads 24 px on each side (PlayerTheme), so the
			//1100 px sheet leaves 1052 px of content and a 1000 px window's sheet
			//leaves 952.
			Assert.Equal(952, LibrarySheetFit.ContentWidth(1000));
			Assert.Equal(1052, LibrarySheetFit.ContentWidth(1600));
		}

		[Fact]
		public void A_window_narrower_than_the_sheet_gets_the_window_and_no_negative_width()
		{
			Assert.Equal(30, LibrarySheetFit.SheetWidth(30));
			Assert.Equal(0, LibrarySheetFit.SheetWidth(0));
		}
	}
}
