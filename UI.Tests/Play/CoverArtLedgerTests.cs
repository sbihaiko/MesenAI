using System;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1035: the covers a grid decoded are handed back when the grid is rebuilt.
	//A cover drawn from the Recent list is a decoded picture - in the app an
	//Avalonia Bitmap, which is an allocation outside the managed heap - and the
	//library rebuilds its whole grid on every visit, so a session of opening and
	//closing the sheet must not leave one picture per played game behind each
	//time.
	//
	//The ledger never sees a picture, only the allocation behind it, so the rule
	//is pinned without a renderer (ADR-0123). What is asserted is what a player
	//would notice: nothing is released while the grid is still drawing it, and
	//everything is released when the grid goes.
	public class CoverArtLedgerTests
	{
		//One decoded picture, as far as the ledger can tell: something that can be
		//handed back, and a count of how many times it was.
		private sealed class Picture : IDisposable
		{
			public int HandedBack { get; private set; }

			public void Dispose()
			{
				HandedBack++;
			}
		}

		[Fact]
		public void A_cover_the_grid_is_drawing_is_not_handed_back()
		{
			CoverArtLedger ledger = new();
			Picture cover = new();

			ledger.Track(cover);

			Assert.Equal(0, cover.HandedBack);
		}

		[Fact]
		public void Rebuilding_the_grid_hands_back_every_cover_it_was_drawing()
		{
			CoverArtLedger ledger = new();
			Picture contra = new();
			Picture metroid = new();
			ledger.Track(contra);
			ledger.Track(metroid);

			ledger.Clear();

			Assert.Equal(1, contra.HandedBack);
			Assert.Equal(1, metroid.HandedBack);
		}

		//The new grid's covers are tracked after the call that hands the old ones
		//back, so clearing must not reach them: a picture the grid is drawing
		//right now is not an old one.
		[Fact]
		public void A_cover_tracked_after_the_rebuild_is_kept()
		{
			CoverArtLedger ledger = new();
			ledger.Track(new Picture());
			ledger.Clear();

			Picture rebuilt = new();
			ledger.Track(rebuilt);
			ledger.Clear();

			Assert.Equal(1, rebuilt.HandedBack);
		}

		//A grid is rebuilt more often than it holds anything: a library with no
		//played game yet clears an empty ledger, and that is not a case to guard.
		[Fact]
		public void Clearing_an_empty_ledger_hands_back_nothing_twice()
		{
			CoverArtLedger ledger = new();
			Picture cover = new();
			ledger.Track(cover);
			ledger.Clear();

			ledger.Clear();

			Assert.Equal(1, cover.HandedBack);
		}

		[Fact]
		public void A_tile_with_no_picture_registers_nothing()
		{
			CoverArtLedger ledger = new();

			ledger.Track(null);

			ledger.Clear();
		}

		//#1065: a query narrowing the grid takes a tile off the sheet while the
		//rest stay exactly where they are - so the tile that goes has to hand its
		//picture back on its own, and the tiles that stayed must be untouched.
		//This is "covers of tiles dropped by a query are released", which is the
		//ledger's half of the acceptance criterion: the sheet drops the tile, and
		//the ledger is what the picture behind it belongs to.
		[Fact]
		public void The_picture_of_one_dropped_tile_is_handed_back_and_the_others_are_kept()
		{
			CoverArtLedger ledger = new();
			Picture dropped = new();
			Picture kept = new();
			ledger.Track(dropped);
			ledger.Track(kept);

			ledger.Release(dropped);

			Assert.Equal(1, dropped.HandedBack);
			Assert.Equal(0, kept.HandedBack);
		}

		//Releasing is not a second Clear: the picture it handed back is no longer
		//the ledger's, so the rebuild that finally replaces the grid must not hand
		//the same picture back a second time.
		[Fact]
		public void A_released_picture_is_not_handed_back_again_by_the_next_rebuild()
		{
			CoverArtLedger ledger = new();
			Picture dropped = new();
			ledger.Track(dropped);

			ledger.Release(dropped);
			ledger.Clear();

			Assert.Equal(1, dropped.HandedBack);
		}

		//A tile drawing the console colour has no picture of its own, which is the
		//same "not a case the caller has to guard" as Track.
		[Fact]
		public void Releasing_a_cover_that_was_never_drawn_hands_back_nothing()
		{
			CoverArtLedger ledger = new();

			ledger.Release(null);
		}

		//The sheet is closed for good: whatever is still being drawn goes back
		//with it rather than waiting for a rebuild that will not come.
		[Fact]
		public void Closing_the_sheet_hands_back_the_covers_still_drawn()
		{
			CoverArtLedger ledger = new();
			Picture cover = new();
			ledger.Track(cover);

			ledger.Dispose();

			Assert.Equal(1, cover.HandedBack);
		}
	}
}
