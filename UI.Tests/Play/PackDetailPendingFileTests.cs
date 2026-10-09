using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#939 (PRD Part B §13.5.2 W-P16): W-P6 shows a pack's pending file as its
	//orange line with Add the File…, and only while the file is still missing.
	public class PackDetailPendingFileTests
	{
		[Fact]
		public void A_pack_waiting_for_a_file_shows_the_line()
		{
			PackDepNoticeState notice = new();
			notice.Pending("Contra Arcade Music", 1);
			Assert.True(PackDetailPendingFile.Shows(notice));
		}

		[Fact]
		public void A_complete_pack_shows_no_line()
		{
			PackDepNoticeState notice = new();
			Assert.False(PackDetailPendingFile.Shows(notice));
			notice.Pending("Contra Arcade Music", 0);
			Assert.False(PackDetailPendingFile.Shows(notice));
		}

		[Fact]
		public void The_line_goes_once_the_file_is_in_place()
		{
			PackDepNoticeState notice = new();
			notice.Pending("Contra Arcade Music", 1);
			//W-P16's add clears the notice (PlayPackDepSheetViewModel.TryFile).
			notice.Clear();
			Assert.False(PackDetailPendingFile.Shows(notice));
		}

		[Fact]
		public void The_line_stays_after_the_sheet_was_shown_once()
		{
			//Play Without It leaves the pack partial: W-P6 is where the file
			//can still be added after the overlay's one showing.
			PackDepNoticeState notice = new();
			notice.Pending("Contra Arcade Music", 1);
			notice.MarkShown();
			Assert.True(PackDetailPendingFile.Shows(notice));
		}

		//Rule 9: the pad lands on the control that can act - Add the File… when
		//there is a file to add, else Change pack…, else Done.
		[Fact]
		public void Focus_opens_on_add_the_file_when_one_is_pending()
		{
			Assert.Equal("PackDetailAddFileButton", PackDetailPendingFile.FirstControl(hasPendingFile: true, canChange: true));
			Assert.Equal("PackDetailAddFileButton", PackDetailPendingFile.FirstControl(hasPendingFile: true, canChange: false));
			Assert.Equal("PackDetailChangeButton", PackDetailPendingFile.FirstControl(hasPendingFile: false, canChange: true));
			Assert.Equal("PackDetailDoneButton", PackDetailPendingFile.FirstControl(hasPendingFile: false, canChange: false));
		}
	}
}
