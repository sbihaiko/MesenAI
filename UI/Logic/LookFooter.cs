namespace Mesen.Logic;

//W-P10 (ADR-0249) draws the Look footer as Hold to Compare and its note on one
//row, with Done on the sheet's right, and that is where the note belongs. #1149:
//at the guaranteed 512x505 at 1.5 the sheet's page is about 271 px, the row
//keeps 110 px clear of Done, and the 137 px button left the note about 14 px -
//no room at all, so the reason was drawn ellipsized to nothing ("bounded, not
//whole", #1123). The note therefore keeps the row wherever the row has room for
//it - unchanged at the ~1024x640 the wireframe is drawn at, where the row
//leaves it about 184 px - and takes the line above the row only where it does
//not. This is the rule; Controls/LookFooterPanel arranges by it. Pure, so it is
//pinned host-free in UI.Tests/Play/LookFooterTests and rendered in
//UI.HeadlessTests (InterfaceSizeLayoutTests).
public static class LookFooter
{
	//Done is the sheet's own, drawn at the row's right (W-P10), so the row keeps
	//this much of its width clear of it - the note's own line, above the row,
	//does not.
	public const double DoneReserve = 110;
	//Between the button and the note while they share the row.
	public const double NoteGap = 10;
	//Between the note's own line and the row below it.
	public const double StackGap = 8;
	//The least the row can leave the note and still be said to have room for it:
	//about sixteen characters at the footnote size, which draws the start of the
	//longest reason the Look tab has ("Nothing to compare: Pixels and Screen are
	//off"). The row leaves the note about 184 px at the drawn size and about 14
	//at the guaranteed one, so the threshold sits well inside both.
	public const double MinNoteWidth = 96;

	//What the row leaves the note beside the button, Done's own reserve taken out.
	public static double NoteRoom(double pageWidth, double buttonWidth)
	{
		return pageWidth - DoneReserve - buttonWidth - NoteGap;
	}

	public static bool NoteKeepsTheRow(double pageWidth, double buttonWidth)
	{
		//A measure pass can hand over Infinity (a column with no width of its
		//own), and a NaN page is no room at all to divide: neither reflows the
		//note out of the row it was drawn in.
		if(double.IsNaN(pageWidth)) {
			return true;
		}
		return NoteRoom(pageWidth, buttonWidth) >= MinNoteWidth;
	}
}
