using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//#1149: W-P10 draws the Look footer as Hold to Compare and its note on one row,
//with Done at the sheet's right, and the note keeps that row wherever the row has
//room for it; it takes the page's own line above the row only where it does not.
//The rule lives in LookFooter, host-free; the rendered result is in
//UI.HeadlessTests (InterfaceSizeLayoutTests).
public class LookFooterTests
{
	//1024x640, the size W-P10 is drawn at (ADR-0269 Decision 6): the sheet's
	//~440 px page leaves the row about 184 px beside the 137 px button, so the
	//note stays where the wireframe draws it.
	[Fact]
	public void The_drawn_sheet_leaves_the_note_the_row()
	{
		Assert.Equal(184, LookFooter.NoteRoom(441, 137), 1);
		Assert.True(LookFooter.NoteKeepsTheRow(441, 137));
	}

	//512x505 at 1.5, the size the ADR guarantees: the page is about 271 px, Done
	//keeps 110 px of the row clear, and the 137 px button leaves the note about
	//14 px - which is what #1149 was filed for, and no room to draw a reason in.
	[Fact]
	public void The_guaranteed_window_has_no_room_and_the_note_takes_its_own_line()
	{
		Assert.Equal(14, LookFooter.NoteRoom(271, 137), 1);
		Assert.False(LookFooter.NoteKeepsTheRow(271, 137));
	}

	//The floor between the two: a row that leaves the note less than a short
	//phrase's worth of width is a sliver, and the note leaves it.
	[Fact]
	public void A_row_that_leaves_the_note_less_than_the_floor_reflows_it()
	{
		Assert.Equal(LookFooter.MinNoteWidth, LookFooter.NoteRoom(137 + LookFooter.MinNoteWidth + 120, 137), 0.5);
		Assert.True(LookFooter.NoteKeepsTheRow(137 + LookFooter.MinNoteWidth + 120, 137));
		Assert.False(LookFooter.NoteKeepsTheRow(137 + LookFooter.MinNoteWidth + 119, 137));
	}

	//A measure pass hands over Infinity for a column with no width of its own,
	//and a NaN page is no room to measure in: neither is "no room", and reflowing
	//on either would move the note out of the row the wireframe draws it in.
	[Fact]
	public void An_unmeasured_page_keeps_the_note_in_the_row()
	{
		Assert.True(LookFooter.NoteKeepsTheRow(double.PositiveInfinity, 137));
		Assert.True(LookFooter.NoteKeepsTheRow(double.NaN, 137));
	}
}
