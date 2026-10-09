using System;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//#1111, ADR-0269 Decision 6 / #1123: a Play sheet caps both sides of its box
//against the room its host gives it. The rule is arithmetic and host-free, so
//it lives in UI/Logic/SheetRoom and is pinned here - the XAML face of it is
//RoomLeftConverter (UI/Controls/PlayerTheme.cs), and PlayerSettingsSheetView
//is the one sheet that binds to it. The numbers below are the ones the ADR's
//guaranteed sizes produce, written out as literals, never derived the way the
//rule derives them.
public class SheetRoomTests
{
	//The 512x505 starting window at factor 1.5: 480 * 1.5 = 720 is wider than
	//the window, so the host's 512 is the room; the cap reserves 32 px.
	[Theory]
	[InlineData(342, 32, 310)]
	[InlineData(400, 32, 368)]
	[InlineData(20, 32, 20)]
	public void The_cap_is_the_host_s_room_less_the_margin(double room, double margin, double expected)
	{
		Assert.Equal(expected, SheetRoom.Cap(room, margin), 3);
	}

	//A host that has not been arranged yet reports a room of 0 - the first
	//measure pass. That is a finite room with nothing past the margin, so the
	//cap is the room itself (0), never "no cap": +Infinity there let the sheet
	//lay out at its own 480 and flash at 720 under the 1.5 transform before
	//the next pass capped it.
	[Fact]
	public void A_room_of_zero_caps_at_zero_and_is_never_no_cap()
	{
		Assert.Equal(0, SheetRoom.Cap(0, 32), 3);
		Assert.True(double.IsFinite(SheetRoom.Cap(0, 32)));
	}

	//A room that is not a number the host measured - the binding is unset, or
	//carries a BindingNotification because `$parent[UserControl]` did not
	//resolve while the sheet is detached or re-templated - is not the same
	//statement as "the host measured 0". Capping it at 0 collapses the sheet,
	//Done included, until some later pass updates the binding; the fallback
	//keeps it at its own designed 480, which caps nothing the sheet would not
	//have been anyway.
	[Fact]
	public void A_room_nobody_reported_keeps_the_sheet_at_its_own_size()
	{
		Assert.Equal(480, SheetRoom.Cap(null, 32), 3);
		Assert.Equal(480, SheetRoom.Cap(double.NaN, 32), 3);
	}

	//The margin is the caller's: the same room leaves a different cap.
	[Fact]
	public void The_margin_is_subtracted_from_the_room()
	{
		Assert.Equal(430, SheetRoom.Cap(480, 50), 3);
	}
}
