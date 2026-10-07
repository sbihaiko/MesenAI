using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//#964 (ADR-0256 stop rule): the focused control's own value semantics come
//before focus movement. PlayPadValueRules answers what a pad press means to a
//slider, a drop-down and a hold button; None hands the press back to the
//focus engine. Every expected value is written out, never derived the way the
//rule derives it.
public class PlayPadValueRulesTests
{
	private static readonly PadNavMapping Nav = new(Up: 0x1010, Down: 0x1011, Left: 0x1012, Right: 0x1013, Confirm: 0x1014, Back: 0x1015);

	[Theory]
	[InlineData(PadNavAction.Left, PadValueVerb.Step, -1)]
	[InlineData(PadNavAction.Right, PadValueVerb.Step, 1)]
	[InlineData(PadNavAction.Up, PadValueVerb.None, 0)]
	[InlineData(PadNavAction.Down, PadValueVerb.None, 0)]
	[InlineData(PadNavAction.Back, PadValueVerb.None, 0)]
	public void A_slider_steps_on_left_and_right_and_leaves_the_rest_to_focus(PadNavAction action, PadValueVerb verb, int delta)
	{
		Assert.Equal(new PadValueAnswer(verb, delta), PlayPadValueRules.Next(PadValueKind.Slider, popupOpen: false, action));
	}

	[Theory]
	[InlineData(PadNavAction.Confirm, PadValueVerb.Open, 0)]
	[InlineData(PadNavAction.Left, PadValueVerb.None, 0)]
	[InlineData(PadNavAction.Down, PadValueVerb.None, 0)]
	[InlineData(PadNavAction.Back, PadValueVerb.None, 0)]
	public void A_closed_popup_opens_on_confirm_only(PadNavAction action, PadValueVerb verb, int delta)
	{
		Assert.Equal(new PadValueAnswer(verb, delta), PlayPadValueRules.Next(PadValueKind.Popup, popupOpen: false, action));
	}

	[Theory]
	[InlineData(PadNavAction.Up, PadValueVerb.Walk, -1)]
	[InlineData(PadNavAction.Down, PadValueVerb.Walk, 1)]
	[InlineData(PadNavAction.Confirm, PadValueVerb.Commit, 0)]
	[InlineData(PadNavAction.Back, PadValueVerb.Cancel, 0)]
	[InlineData(PadNavAction.Left, PadValueVerb.Consume, 0)]
	[InlineData(PadNavAction.Right, PadValueVerb.Consume, 0)]
	public void An_open_popup_owns_every_press(PadNavAction action, PadValueVerb verb, int delta)
	{
		Assert.Equal(new PadValueAnswer(verb, delta), PlayPadValueRules.Next(PadValueKind.Popup, popupOpen: true, action));
	}

	[Theory]
	[InlineData(PadNavAction.Confirm, PadValueVerb.HoldStart)]
	[InlineData(PadNavAction.Left, PadValueVerb.None)]
	[InlineData(PadNavAction.Back, PadValueVerb.None)]
	public void A_hold_control_starts_on_confirm(PadNavAction action, PadValueVerb verb)
	{
		Assert.Equal(new PadValueAnswer(verb), PlayPadValueRules.Next(PadValueKind.Hold, popupOpen: false, action));
	}

	[Theory]
	[InlineData(PadNavAction.Left)]
	[InlineData(PadNavAction.Right)]
	[InlineData(PadNavAction.Up)]
	[InlineData(PadNavAction.Confirm)]
	public void A_non_value_control_leaves_every_press_to_focus_and_activation(PadNavAction action)
	{
		Assert.Equal(new PadValueAnswer(PadValueVerb.None), PlayPadValueRules.Next(PadValueKind.None, popupOpen: false, action));
	}

	[Fact]
	public void A_hold_ends_when_confirm_is_released()
	{
		Assert.False(PlayPadValueRules.EndsHold(holding: true, new ushort[] { Nav.Confirm }, Nav));
		Assert.True(PlayPadValueRules.EndsHold(holding: true, new ushort[] { Nav.Left }, Nav));
		Assert.True(PlayPadValueRules.EndsHold(holding: true, new ushort[0], Nav));
		//A pad that is no longer resolvable cannot keep a hold alive.
		Assert.True(PlayPadValueRules.EndsHold(holding: true, new ushort[] { Nav.Confirm }, null));
		Assert.False(PlayPadValueRules.EndsHold(holding: false, new ushort[0], Nav));
	}

	[Theory]
	[InlineData(37, 1, 0, 100, 1, 38)]
	[InlineData(38, 1, 0, 100, -1, 37)]
	[InlineData(100, 1, 0, 100, 1, 100)]
	[InlineData(0, 1, 0, 100, -1, 0)]
	[InlineData(3.5, 1, 0, 4, 1, 4)]
	public void A_step_is_one_small_change_clamped_to_the_range(double value, double smallChange, double min, double max, int delta, double expected)
	{
		Assert.Equal(expected, PlayPadValueRules.Step(value, smallChange, min, max, delta));
	}

	[Theory]
	[InlineData(1, 5, 1, 2)]
	[InlineData(1, 5, -1, 0)]
	[InlineData(0, 5, -1, 0)]
	[InlineData(4, 5, 1, 4)]
	[InlineData(-1, 5, 1, 0)]
	[InlineData(-1, 5, -1, 0)]
	[InlineData(0, 0, 1, -1)]
	public void A_walk_moves_one_row_and_stops_at_the_ends(int index, int count, int delta, int expected)
	{
		Assert.Equal(expected, PlayPadValueRules.Walk(index, count, delta));
	}

	//#983: the walk lands only on a row the drop-down could show (realized
	//after scrolling it into view); otherwise it stays where it was, so the
	//commit never writes a row the player did not see.
	[Theory]
	[InlineData(1, 2, true, 2)]
	[InlineData(1, 2, false, 1)]
	[InlineData(-1, 0, false, -1)]
	[InlineData(-1, 0, true, 0)]
	public void A_walk_lands_only_on_a_row_that_is_shown(int from, int to, bool shown, int expected)
	{
		Assert.Equal(expected, PlayPadValueRules.Land(from, to, shown));
	}
}
