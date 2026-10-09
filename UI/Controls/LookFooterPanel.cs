using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Mesen.Logic;
using System;

namespace Mesen.Controls
{
	//The Look footer (W-P10, #1149). Children, in order: Hold to Compare, the
	//compare note, and - third, optional - the line the note is kept at, measured
	//with the note and never drawn (the view hides it: no opacity, no hit, no
	//automation tree). The Look tab can always carry one of two lines, the
	//hold-to-compare hint while Pixels or Screen has something to compare and the
	//reason while it does not, and toggling either swaps one for the other:
	//keeping the note's line at the taller of the two is what stops the footer -
	//and so the page's scroller above it - from moving under the user's hands
	//(review of #1163, finding 3).
	//
	//The shape is LookFooter's rule, host-free and pinned by UI.Tests: the note
	//shares the row beside the button wherever the row has room for it, as the
	//wireframe draws it, and takes the page's own line above the row where it has
	//none. Both are drawn whole - the note wraps in either - because the note is
	//the tab's reason for refusing a compare and half of one is no reason at all
	//(review of #1163, finding 1): the row's room is what wraps it there, and the
	//floor in LookFooter is where that room stops being worth wrapping in. Which
	//room the note is drawn in is what this panel shapes and arranges it by. Done
	//is the sheet's own, at the row's right: the row keeps LookFooter.DoneReserve
	//clear of it, the note's own line does not.
	public class LookFooterPanel : Panel
	{
		protected override Size MeasureOverride(Size availableSize)
		{
			if(Children.Count < 2) {
				return base.MeasureOverride(availableSize);
			}

			Control button = Children[0];
			Control note = Children[1];
			button.Measure(availableSize);

			double room = RoomFor(note, availableSize.Width, button.DesiredSize.Width);
			ShapeNote(note);
			note.Measure(new Size(room, availableSize.Height));

			//The room the note is kept at is the taller of the two lines the tab
			//can carry, and it is the reserve's own measure that says which: it is
			//measured in the note's room, whichever line that is.
			double line = Math.Max(note.DesiredSize.Height, MeasureReserve(room, availableSize.Height));
			double height = OwnLine(note, availableSize.Width, button.DesiredSize.Width)
				? line + LookFooter.StackGap + button.DesiredSize.Height
				: Math.Max(button.DesiredSize.Height, line);
			double width = double.IsInfinity(availableSize.Width)
				? button.DesiredSize.Width + LookFooter.NoteGap + note.DesiredSize.Width
				: availableSize.Width;
			return new Size(width, height);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			if(Children.Count < 2) {
				return base.ArrangeOverride(finalSize);
			}

			Control button = Children[0];
			Control note = Children[1];
			double room = RoomFor(note, finalSize.Width, button.DesiredSize.Width);
			double left = OwnLine(note, finalSize.Width, button.DesiredSize.Width) ? 0 : button.DesiredSize.Width + LookFooter.NoteGap;
			//The note is drawn at the height its own text needs in that room and
			//never stretched to the line's: its box is then the text's, which is
			//what "drawn whole" is read off (UI.HeadlessTests).
			double top = OwnLine(note, finalSize.Width, button.DesiredSize.Width) ? 0 : Math.Max(0, (finalSize.Height - note.DesiredSize.Height) / 2);
			note.Arrange(new Rect(left, top, room, note.DesiredSize.Height));
			ArrangeReserve(new Rect(left, top, room, note.DesiredSize.Height));

			if(OwnLine(note, finalSize.Width, button.DesiredSize.Width)) {
				//The note at the top of its own line and the button's row at the
				//foot, so the slack a taller reserve leaves falls between the two
				//rather than under the button.
				button.Arrange(new Rect(0, finalSize.Height - button.DesiredSize.Height, button.DesiredSize.Width, button.DesiredSize.Height));
			} else {
				//On the row the button carries the line's whole height, which is
				//what puts the note and Hold to Compare on one line.
				button.Arrange(new Rect(0, 0, button.DesiredSize.Width, finalSize.Height));
			}
			return finalSize;
		}

		//The room the note is drawn in: the page's own width where it has its own
		//line, and what is left of the row beside the button, clear of Done, while
		//it keeps that one.
		private static double RoomFor(Control note, double pageWidth, double buttonWidth)
		{
			return OwnLine(note, pageWidth, buttonWidth) ? pageWidth : Math.Max(0, LookFooter.NoteRoom(pageWidth, buttonWidth));
		}

		//The rule, with the note's own visibility folded in: a note nobody draws
		//takes no line, so none is reserved for it either.
		private static bool OwnLine(Control note, double pageWidth, double buttonWidth)
		{
			return note.IsVisible && !LookFooter.NoteKeepsTheRow(pageWidth, buttonWidth);
		}

		//Wrapped, never trimmed, in either branch: the note is a reason, and half
		//a reason reads as a different one - the room it is given decides how many
		//lines it takes, not how much of it is drawn.
		private static void ShapeNote(Control note)
		{
			if(note is TextBlock text) {
				text.TextWrapping = TextWrapping.Wrap;
				text.TextTrimming = TextTrimming.None;
			}
		}

		private double MeasureReserve(double room, double height)
		{
			if(Children.Count < 3) {
				return 0;
			}
			Children[2].Measure(new Size(room, height));
			return Children[2].DesiredSize.Height;
		}

		private void ArrangeReserve(Rect noteLine)
		{
			if(Children.Count >= 3) {
				Children[2].Arrange(noteLine);
			}
		}
	}
}
