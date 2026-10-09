using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Mesen.Logic;
using System;

namespace Mesen.Controls
{
	//The Look footer (W-P10, #1149). Children, in order: Hold to Compare, the
	//compare note, and - third, optional - the line the note is kept at, measured
	//with the note and never drawn (the view gives it Opacity 0). The Look tab
	//can always carry one of two lines, the hold-to-compare hint while Pixels or
	//Screen has something to compare and the reason while it does not, and
	//toggling either swaps one for the other: keeping the note's line at the
	//taller of the two is what stops the footer - and so the page's scroller
	//above it - from moving under the user's hands (review of #1163, finding 3).
	//
	//The shape is LookFooter's rule, host-free and pinned by UI.Tests: the note
	//shares the row beside the button wherever the row has room for it, drawn as
	//the wireframe draws it - one line, ellipsized - and takes the page's own
	//line above the row where it has none, wrapped, so a reason is drawn whole.
	//Which of the two it is decides whether it wraps, so this panel shapes its
	//note. Done is the sheet's own, at the row's right: the row keeps
	//LookFooter.DoneReserve clear of it, the note's own line does not.
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

			bool ownLine = OnItsOwnLine(note, availableSize.Width, button.DesiredSize.Width);
			ShapeNote(note, ownLine);
			note.Measure(ownLine
				? new Size(availableSize.Width, availableSize.Height)
				: new Size(Math.Max(0, LookFooter.NoteRoom(availableSize.Width, button.DesiredSize.Width)), availableSize.Height));

			//On its own line the note is kept at the taller of the lines it can
			//carry; on the row the button is the taller of the two anyway.
			double noteHeight = ownLine ? Math.Max(note.DesiredSize.Height, MeasureReserve(availableSize)) : note.DesiredSize.Height;
			double height = ownLine
				? noteHeight + LookFooter.StackGap + button.DesiredSize.Height
				: Math.Max(button.DesiredSize.Height, noteHeight);
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
			if(OnItsOwnLine(note, finalSize.Width, button.DesiredSize.Width)) {
				//The note at the top, the reserve over it (never drawn), and the
				//button's row at the foot, so the slack a taller reserve leaves
				//falls between the two rather than under the button.
				Rect line = new(0, 0, finalSize.Width, note.DesiredSize.Height);
				note.Arrange(line);
				ArrangeReserve(line, finalSize);
				button.Arrange(new Rect(0, finalSize.Height - button.DesiredSize.Height, button.DesiredSize.Width, button.DesiredSize.Height));
			} else {
				button.Arrange(new Rect(0, 0, button.DesiredSize.Width, finalSize.Height));
				note.Arrange(new Rect(button.DesiredSize.Width + LookFooter.NoteGap, 0, Math.Max(0, LookFooter.NoteRoom(finalSize.Width, button.DesiredSize.Width)), finalSize.Height));
				ArrangeReserve(new Rect(0, 0, 0, 0), finalSize);
			}
			return finalSize;
		}

		//The rule, with the note's own visibility folded in: a note nobody draws
		//takes no line, so none is reserved for it either.
		private static bool OnItsOwnLine(Control note, double pageWidth, double buttonWidth)
		{
			return note.IsVisible && !LookFooter.NoteKeepsTheRow(pageWidth, buttonWidth);
		}

		private static void ShapeNote(Control note, bool ownLine)
		{
			if(note is TextBlock text) {
				text.TextWrapping = ownLine ? TextWrapping.Wrap : TextWrapping.NoWrap;
				text.TextTrimming = ownLine ? TextTrimming.None : TextTrimming.CharacterEllipsis;
			}
		}

		private double MeasureReserve(Size availableSize)
		{
			if(Children.Count < 3) {
				return 0;
			}
			Children[2].Measure(new Size(availableSize.Width, availableSize.Height));
			return Children[2].DesiredSize.Height;
		}

		private void ArrangeReserve(Rect line, Size finalSize)
		{
			if(Children.Count >= 3) {
				Children[2].Arrange(line);
			}
		}
	}
}
