using System;

namespace Mesen.Logic;

//#1078 (ADR-0264 Decision 12): the *Open a game* sheet is the wireframes' own
//1000 px wide when the window has the room, and no wider than the window when
//it does not. The width used to be a fixed 1000 with no ceiling, so a
//non-maximized window narrower than the sheet plus its two 24 px margins drew a
//sheet wider than the window itself - centred, so the window cut BOTH of its
//edges off: the header lost its start, the console filter lost its first
//segments, the first grid column was cut at the left and *Browse a file…* at
//the right, and the focus ring on the first tile hung outside the viewport.
//
//Stateful partner: `UI/Views/PlayerRomPickerView.axaml.cs`, whose
//`OnBackdropSized` owns the layout pass that measures the sheet. Everything
//that is arithmetic lives here, so the fit can be asserted in `UI.Tests`
//without a window, a display or a native core (ADR-0123) - host-free: no
//Avalonia types, only doubles, like RendererViewportFit.
public static class LibrarySheetFit
{
	//The wireframes' sheet (W-P19 / W-P19b), and the width every window with
	//room for it still gets.
	public const double PreferredWidth = 1000;

	//The sheet's own standoff from the window's edges (`Margin` on the sheet
	//itself), so it never touches the frame it was cut by.
	public const double Margin = 24;

	//`Border.sheet`'s horizontal padding in the Player theme.
	public const double SidePadding = 24;

	//The width the sheet takes in a window this wide. Below
	//PreferredWidth + 2 * Margin the sheet gives way instead of overflowing:
	//the grid's tiles wrap, the console filter keeps its own scroll, and every
	//fixed-width header control stays where a mouse can reach it.
	public static double SheetWidth(double windowWidth)
	{
		return Math.Min(PreferredWidth, Math.Max(0, windowWidth - (2 * Margin)));
	}

	//What the sheet's content may occupy once the theme's padding is off. The
	//header's controls are laid out against this, which is why it is named
	//rather than left to the layout pass to rediscover.
	public static double ContentWidth(double windowWidth)
	{
		return Math.Max(0, SheetWidth(windowWidth) - (2 * SidePadding));
	}
}
