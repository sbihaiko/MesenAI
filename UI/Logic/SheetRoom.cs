using System;

namespace Mesen.Logic;

//#1111, ADR-0269 Decision 6 / #1123: how much room a host leaves a sheet.
//A Play sheet caps both sides of its box the same way - the room its host gives
//it less the margin the cap reserves - so the sheet shrinks with the window
//instead of hanging off it at a larger Interface size. The rule is arithmetic
//and host-free, so it lives here and UI.Tests pins it; the XAML face of it is
//Mesen.Controls.RoomLeftConverter (UI/Controls/PlayerTheme.cs), which reads the
//bound `$parent[UserControl].Bounds` and calls this.
public static class SheetRoom
{
	//The Player settings sheet's designed width (PlayerSettingsSheetView's
	//`Width`, ADR-0249) and the height of its two taller tabs (PlayerSettings
	//Essentials.SheetHeight): the largest a sheet ever asks to be. It is also
	//the answer when there is no room to read at all - see Cap.
	public const double DesignSize = 480;

	//The room is already in the transformed space: the host sits inside the
	//Interface size transform, so its Bounds are drawn pixels.
	public static double Cap(double? room, double margin)
	{
		//A room the host measured - 0 included, which is the first measure
		//pass, before the host has Bounds. The cap is then the room itself and
		//never "no cap": +Infinity there laid the sheet out at its own 480 and
		//flashed at 720 drawn for a frame under the 1.5 transform.
		if(room is double measured && !double.IsNaN(measured)) {
			return measured > margin ? measured - margin : Math.Max(0, measured);
		}
		//No room at all: the binding handed over AvaloniaProperty.UnsetValue or
		//a BindingNotification, because `$parent[UserControl]` does not resolve
		//while the sheet is detached or re-templated. That is not the host
		//saying "0" - capping there collapses the sheet to 0x0, Done included,
		//for as long as the binding stays unresolved - so the fallback keeps it
		//at its own designed size, which caps nothing it was not already.
		return DesignSize;
	}
}
