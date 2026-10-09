using System;
using System.Globalization;
using Mesen.Controls;
using Xunit;

namespace Mesen.HeadlessTests;

//#1145 review, item 4 (ADR-0269 Decision 6): the settings sheet caps both of
//its sides with RoomLeftConverter - the room its host gives it (the host is
//inside the Interface size transform, so the room is already transformed)
//less the margin. The first measure pass reports a room of 0: the host has no
//Bounds until it is arranged. +Infinity there is "no cap", so the sheet laid
//out at its own 480 for that pass and flashed at 720 under the 1.5 transform
//before the second pass capped it. The cap has to stay finite.
public class PlayerSheetRoomConverterTests
{
	private static double Cap(double? room) =>
		Assert.IsType<double>(RoomLeftConverter.Instance.Convert(room, typeof(double), "32", CultureInfo.InvariantCulture));

	//A host that has not been arranged yet (0), and one narrower than the
	//margin itself: neither leaves room past the margin, so the cap is the
	//room, never "no cap at all".
	[Theory]
	[InlineData(0, 0)]
	[InlineData(20, 20)]
	public void A_host_with_no_room_past_the_margin_caps_at_the_host(double room, double expected)
	{
		Assert.Equal(expected, Cap(room), 3);
	}

	//The room the ADR's 512x505 window leaves the sheet: 342 in the transformed
	//space, 310 after the 32 px margin.
	[Fact]
	public void A_room_past_the_margin_keeps_the_room_less_the_margin()
	{
		Assert.Equal(310, Cap(342), 3);
	}
}
