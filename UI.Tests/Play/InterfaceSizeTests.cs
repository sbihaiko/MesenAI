using System.Collections.Generic;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//#1111 (spec #1102, PRD Part B §13.3): Settings › Display › Interface size
//scales Play's chrome and nothing else. The factors are written out as
//literals (1.0 / 1.25 / 1.5), never derived the way the rule derives them.
public class InterfaceSizeTests
{
	[Theory]
	[InlineData(InterfaceSize.Standard, 1.0)]
	[InlineData(InterfaceSize.Large, 1.25)]
	[InlineData(InterfaceSize.ExtraLarge, 1.5)]
	public void Each_size_has_its_chrome_factor(InterfaceSize size, double factor)
	{
		Assert.Equal(factor, PlayerInterfaceSize.Factor(size));
	}

	[Theory]
	[InlineData(InterfaceSize.Standard, 1, InterfaceSize.Large)]
	[InlineData(InterfaceSize.Large, 1, InterfaceSize.ExtraLarge)]
	[InlineData(InterfaceSize.ExtraLarge, 1, InterfaceSize.ExtraLarge)]
	[InlineData(InterfaceSize.ExtraLarge, -1, InterfaceSize.Large)]
	[InlineData(InterfaceSize.Large, -1, InterfaceSize.Standard)]
	[InlineData(InterfaceSize.Standard, -1, InterfaceSize.Standard)]
	public void Stepping_stops_at_both_ends(InterfaceSize from, int delta, InterfaceSize expected)
	{
		Assert.Equal(expected, PlayerInterfaceSize.Step(from, delta));
	}

	//§13.3 rule 2: strip + 4 rows + Done = 6 (7 with Exit full screen); the count
	//itself is rendered in UI.HeadlessTests/InterfaceSizeLayoutTests.
	[Fact]
	public void The_display_sheet_has_four_rows_in_order()
	{
		IReadOnlyList<PlayerSettingsRow> rows = PlayerSettingsEssentials.Rows(ConfigWindowTab.Display);
		Assert.Equal(new[] { "Fullscreen", "AspectRatio", "Scale", "InterfaceSize" }, rows.Select(r => r.Id).ToArray());
	}

	[Theory]
	[InlineData(PadValueKind.Stepper, PadNavAction.Left, PadValueVerb.Step, -1)]
	[InlineData(PadValueKind.Stepper, PadNavAction.Right, PadValueVerb.Step, 1)]
	[InlineData(PadValueKind.Stepper, PadNavAction.Confirm, PadValueVerb.Open, 0)]
	[InlineData(PadValueKind.Stepper, PadNavAction.Down, PadValueVerb.None, 0)]
	[InlineData(PadValueKind.Popup, PadNavAction.Left, PadValueVerb.None, 0)]
	public void A_stepper_row_steps_in_place_and_a_plain_popup_does_not(PadValueKind kind, PadNavAction action, PadValueVerb verb, int delta)
	{
		Assert.Equal(new PadValueAnswer(verb, delta), PlayPadValueRules.Next(kind, popupOpen: false, action));
	}
}
