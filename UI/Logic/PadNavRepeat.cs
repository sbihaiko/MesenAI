using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//ADR-0256 Decision 7 (accepted 2026-10-04): a held D-pad repeats. It is not
//optional: nothing in the stack below makes it happen.
//KeyManager::SetKeyState re-emits a key
//only when the pressed set changes, so a button held down produces exactly one
//edge and a menu would take one step per press. The repeat is the host's, and
//this is the rule for it, host-free so it can be tested without a pad or a
//clock.
//
//It wraps PlayPadNavigation.Next rather than reimplementing it: the edge stays
//the authority rule's answer (one action, in PadNavControls.Navigation's order,
//never while the pad is the console's) and the repeat only adds the steps a
//held direction owes after that edge.
public sealed class PadNavRepeat
{
	//The two numbers, in the one place they are decided: the first repeat comes
	//RepeatDelay after the press and one more every RepeatInterval while the
	//direction stays down. The press's own step is PlayPadNavigation.Next's, not
	//this delay's - a player tapping the D-pad never waits.
	//
	//400/100 is the shape menus have used for decades (the OS's own key repeat
	//is the same order): long enough that a deliberate single step is not read
	//as a hold, short enough that crossing a full page of packs or save slots
	//does not feel stuck.
	public static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
	public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(100);

	private PadNavAction _held = PadNavAction.None;
	private TimeSpan _heldFor;
	private TimeSpan _sinceStep;

	//The action to apply this tick, or None.
	//
	//Same arguments as PlayPadNavigation.Next - pressed is what the host reports
	//now and previous is what the caller passed last tick, recorded on every
	//tick, authority or not - plus how long passed since the last call. The
	//caller owns the clock: a DispatcherTimer's cadence is not this rule's
	//business, and a test passes the interval it wants to reason about.
	public PadNavAction Next(IReadOnlyCollection<ushort> pressed, IReadOnlyCollection<ushort> previous, PadNavMapping? mapping, bool hasAuthority, TimeSpan delta)
	{
		PadNavAction edge = PlayPadNavigation.Next(pressed, previous, mapping, hasAuthority);
		PadNavAction held = hasAuthority ? Held(pressed, mapping) : PadNavAction.None;

		if(held != _held) {
			//A direction that just went down starts its delay here, and this tick
			//owes nothing but the edge itself: the press's own step (a direction
			//that went down in this tick) or nothing at all (a hold that arrives
			//with the delay not yet run - the tick authority came back, the tick
			//a pad was picked up). One that came up stops repeating, and losing
			//authority (the game resumed, the overlay closed) stops it the same
			//way: the pad is the console's again, so a button still down must not
			//keep scrolling a menu that is no longer there.
			_held = held;
			_heldFor = TimeSpan.Zero;
			_sinceStep = TimeSpan.Zero;
			return edge;
		}

		if(edge != PadNavAction.None) {
			//The press's own step, on a tick where the hold did not change (a
			//second direction pressed while the first stays down): still the only
			//step this tick takes, so a repeat can never double it.
			return edge;
		}
		if(held == PadNavAction.None) {
			return PadNavAction.None;
		}

		_heldFor += delta;
		_sinceStep += delta;
		if(_heldFor < RepeatDelay || _sinceStep < RepeatInterval) {
			return PadNavAction.None;
		}

		//One step per tick, never a burst. A tick that arrives late owes at most
		//the single move the player is waiting for; draining the backlog would
		//scroll several rows at once, and the menu's answer to a long stall is
		//not to run the player's presses through it at once.
		_sinceStep = TimeSpan.Zero;
		return held;
	}

	//The direction still down, or None. Confirm and Back are deliberately not
	//here: a repeat of Confirm activates whatever the repeat itself just
	//scrolled onto, and on the cabinet this decision exists for there is no
	//pointer and no keyboard behind it to take that back with.
	//
	//Two directions down at once (a worn D-pad, a diagonal) resolve in
	//PadNavControls.Navigation's order - the same order PlayPadNavigation.Next
	//breaks two presses in, so the pad never moves one way and repeats another.
	private static PadNavAction Held(IReadOnlyCollection<ushort> pressed, PadNavMapping? mapping)
	{
		if(mapping is not PadNavMapping nav) {
			return PadNavAction.None;
		}
		foreach(PadNavAction action in PadNavControls.Navigation) {
			if(action is PadNavAction.Confirm or PadNavAction.Back) {
				continue;
			}
			if(pressed.Contains(PlayPadNavigation.CodeOf(nav, action))) {
				return action;
			}
		}
		return PadNavAction.None;
	}
}
