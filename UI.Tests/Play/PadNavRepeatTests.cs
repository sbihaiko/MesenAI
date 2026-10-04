using Mesen.Logic;
using System;
using Xunit;

namespace Mesen.Tests.Play;

//ADR-0256 (accepted 2026-10-04) Decision 7: a held D-pad repeats. The core
//re-emits a key only when the pressed set changes (KeyManager::SetKeyState), so
//a button held down is one edge and a menu would take one step per press - the
//repeat is the host's. PadNavRepeat is the rule for it: the edge is still
//PlayPadNavigation.Next's answer, and the repeat only adds the steps a held
//direction owes after it. The clock is passed in, so these are exact.
public class PadNavRepeatTests
{
	private static readonly PadNavMapping Nav = new(Up: 0x1010, Down: 0x1011, Left: 0x1012, Right: 0x1013, Confirm: 0x1014, Back: 0x1015);

	private static ushort[] Pressed(params ushort[] keys) => keys;

	//Every delta below is written out in milliseconds rather than read off
	//PadNavRepeat's constants, so these tests pin the *numbers* - 400 ms before
	//the first repeat, one step every 100 ms after it - and not just the rule's
	//agreement with itself. The keys are the code's, the clock is the caller's.
	private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

	//One tick while Right is held.
	private static PadNavAction HeldTick(PadNavRepeat repeat)
	{
		return repeat.Next(Pressed(Nav.Right), Pressed(Nav.Right), Nav, hasAuthority: true, Tick);
	}

	//The press steps at once - a player tapping the D-pad never waits - and then
	//the hold owes nothing for 400 ms and one step every 100 ms after that.
	[Fact]
	public void A_held_direction_steps_once_then_repeats()
	{
		PadNavRepeat repeat = new();

		Assert.Equal(PadNavAction.Right, repeat.Next(Pressed(Nav.Right), Pressed(), Nav, hasAuthority: true, Tick));

		//300 ms in: still the delay.
		for(int i = 0; i < 3; i++) {
			Assert.Equal(PadNavAction.None, HeldTick(repeat));
		}
		//400 ms: the first repeat, and one per tick from there.
		for(int i = 0; i < 3; i++) {
			Assert.Equal(PadNavAction.Right, HeldTick(repeat));
		}
	}

	//A released direction stops: the player who let go gets exactly the one step
	//they asked for, however long the tick after it takes.
	[Fact]
	public void A_released_direction_does_not_repeat()
	{
		PadNavRepeat repeat = new();
		Assert.Equal(PadNavAction.Right, repeat.Next(Pressed(Nav.Right), Pressed(), Nav, hasAuthority: true, Tick));

		for(int i = 0; i < 10; i++) {
			Assert.Equal(PadNavAction.None, repeat.Next(Pressed(), Pressed(Nav.Right), Nav, hasAuthority: true, Tick));
		}
	}

	//Confirm and Back are one-shot by intent: a repeat of Confirm activates
	//whatever the repeat itself just scrolled onto, and on the cabinet this
	//decision exists for there is no pointer to take that back with.
	[Theory]
	[InlineData(PadNavAction.Confirm)]
	[InlineData(PadNavAction.Back)]
	public void Confirm_and_back_never_repeat(PadNavAction action)
	{
		PadNavRepeat repeat = new();
		ushort code = action == PadNavAction.Confirm ? Nav.Confirm : Nav.Back;

		Assert.Equal(action, repeat.Next(Pressed(code), Pressed(), Nav, hasAuthority: true, Tick));
		for(int i = 0; i < 20; i++) {
			Assert.Equal(PadNavAction.None, repeat.Next(Pressed(code), Pressed(code), Nav, hasAuthority: true, Tick));
		}
	}

	//Losing authority stops it the same way a release does, and a direction that
	//is still down when authority comes back does not fire on the spot: the pad
	//was the console's, and the press that was held through that was the game's,
	//not the menu's.
	[Fact]
	public void Losing_authority_stops_the_repeat()
	{
		PadNavRepeat repeat = new();
		Assert.Equal(PadNavAction.Right, repeat.Next(Pressed(Nav.Right), Pressed(), Nav, hasAuthority: true, Tick));
		for(int i = 0; i < 3; i++) {
			Assert.Equal(PadNavAction.None, HeldTick(repeat));
		}
		Assert.Equal(PadNavAction.Right, HeldTick(repeat));

		//Authority gone: the overlay closed and the game is running again.
		for(int i = 0; i < 10; i++) {
			Assert.Equal(PadNavAction.None, repeat.Next(Pressed(Nav.Right), Pressed(Nav.Right), Nav, hasAuthority: false, Tick));
		}

		//Back, with the same button still down: no step on the tick authority
		//returns (no edge - `previous` still holds it), then the delay again.
		Assert.Equal(PadNavAction.None, HeldTick(repeat));
		for(int i = 0; i < 3; i++) {
			Assert.Equal(PadNavAction.None, HeldTick(repeat));
		}
		Assert.Equal(PadNavAction.Right, HeldTick(repeat));
	}

	//Two directions down at once (a worn D-pad, a diagonal) repeat in
	//PadNavControls.Navigation's order - the same order PlayPadNavigation.Next
	//breaks two presses in, so the pad cannot step one way and repeat another.
	[Fact]
	public void Two_directions_held_repeat_the_first_in_the_navigation_order()
	{
		PadNavRepeat repeat = new();
		ushort[] both = Pressed(Nav.Right, Nav.Up);

		Assert.Equal(PadNavAction.Up, repeat.Next(both, Pressed(), Nav, hasAuthority: true, Tick));
		for(int i = 0; i < 3; i++) {
			Assert.Equal(PadNavAction.None, repeat.Next(both, both, Nav, hasAuthority: true, Tick));
		}
		Assert.Equal(PadNavAction.Up, repeat.Next(both, both, Nav, hasAuthority: true, Tick));
	}

	//One step per tick, never a burst: a tick that arrives late owes the single
	//move the player is waiting for, and the next tick is a normal one - the
	//repeat's cadence is not a debt that gets paid off at once.
	[Fact]
	public void A_late_tick_owes_one_step_and_no_more()
	{
		PadNavRepeat repeat = new();
		Assert.Equal(PadNavAction.Right, repeat.Next(Pressed(Nav.Right), Pressed(), Nav, hasAuthority: true, Tick));

		Assert.Equal(PadNavAction.Right, repeat.Next(Pressed(Nav.Right), Pressed(Nav.Right), Nav, hasAuthority: true, TimeSpan.FromSeconds(2)));
		Assert.Equal(PadNavAction.None, repeat.Next(Pressed(Nav.Right), Pressed(Nav.Right), Nav, hasAuthority: true, TimeSpan.FromMilliseconds(10)));
		Assert.Equal(PadNavAction.Right, HeldTick(repeat));
	}

	//A pad whose family the app cannot tell (PadNavControls.Resolve answered
	//null) asks nothing, and nothing here has to know why.
	[Fact]
	public void An_unresolved_mapping_asks_nothing()
	{
		PadNavRepeat repeat = new();
		Assert.Equal(PadNavAction.None, repeat.Next(Pressed(0x1013), Pressed(), null, hasAuthority: true, Tick));
	}

	//The caller records `previous` on every tick, so a key that was already down
	//when the tick sequence started is never an edge - the case that matters is a
	//button held across the moment the overlay opens.
	[Fact]
	public void A_hold_that_predates_the_first_tick_is_not_an_edge()
	{
		PadNavRepeat repeat = new();
		Assert.Equal(PadNavAction.None, repeat.Next(Pressed(Nav.Right), Pressed(Nav.Right), Nav, hasAuthority: true, Tick));
	}
}
