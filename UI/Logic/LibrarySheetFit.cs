using System;

namespace Mesen.Logic;

//#1078 (ADR-0264 Decision 12): the *Open a game* sheet is W-P19's own 1100 px
//wide when the window has the room, and no wider than the window when it does
//not. The width used to be a fixed 1000 with no ceiling, so a non-maximized
//window narrower than the sheet drew a sheet wider than the window itself -
//centred, so the window cut BOTH of its edges off: the header lost its start,
//the console filter lost its first segments, the first grid column was cut at
//the left and *Browse a file…* at the right, and the focus ring on the first
//tile hung outside the viewport.
//
//Stateful partner: `UI/Views/PlayerRomPickerView.axaml.cs`, whose
//`OnBackdropSized` owns the layout pass that measures the sheet. Everything
//that is arithmetic lives here, so the fit can be asserted in `UI.Tests`
//without a window, a display or a native core (ADR-0123) - host-free: no
//Avalonia types, only doubles, like RendererViewportFit.
public static class LibrarySheetFit
{
	//The wireframes' sheet (W-P19 / W-P19b: `c.sheet(1100, 620)` in
	//`scripts/render_gui_wireframes.py`), and the width every window with room
	//for it still gets.
	public const double PreferredWidth = 1100;

	//`Border.sheet`'s horizontal padding in the Player theme.
	public const double SidePadding = 24;

	//The width the sheet takes in a window this wide. Below PreferredWidth the
	//sheet gives way instead of overflowing: the grid's tiles wrap, the console
	//filter keeps its own scroll, and every fixed-width header control stays
	//where a mouse can reach it.
	//
	//The cap is the window itself, because the window is the whole of the
	//sheet's horizontal standoff: the sheet's `Margin="0 24"` is vertical only
	//and the backdrop carries no horizontal padding, so there is no gutter to
	//subtract. (There used to be a phantom 24 px one per side here, which cost
	//every window narrower than the sheet 48 px of width it was already
	//drawing in.)
	public static double SheetWidth(double windowWidth)
	{
		return Math.Min(PreferredWidth, Math.Max(0, windowWidth));
	}

	//What the sheet's content may occupy once the theme's padding is off. The
	//header's controls are laid out against this, which is why it is named
	//rather than left to the layout pass to rediscover.
	public static double ContentWidth(double windowWidth)
	{
		return Math.Max(0, SheetWidth(windowWidth) - (2 * SidePadding));
	}
}
