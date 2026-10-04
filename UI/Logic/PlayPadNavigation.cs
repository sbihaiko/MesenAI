using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//ADR-0256 (accepted 2026-10-04) Decisions 1 and 2: the pad is also Player 1's
//controller, so every button on it is already spoken for while a game runs, and
//the question is never "how does the pad navigate" but "when is the pad the
//GUI's and not the console's". The answer is the pause state - the pad drives
//the GUI while W-P4 is up or with no game loaded, and never while a game runs
//unpaused - which is cheap precisely because W-P4 pauses the game.
//
//One exception already ships and this rule names it rather than contradicting
//it: W-P15's UnknownControllerDetector (PlayControllerSetup) reads every
//connected pad while a game runs unpaused, to notice one whose keys no mapping
//uses and offer to set it up. That is a detector, not a menu - it moves nothing
//and acts on no press the player made on purpose - so the honest form of the
//rule is that the pad has no *menu* authority during play, and anything new has
//to justify itself against both that detector and ADR-0251's chord.
//
//This file is the authority rule plus the edge detector; PadNavControls is what
//turns a pad family into the codes to look for.
public enum PadNavAction
{
	None,
	Up,
	Down,
	Left,
	Right,
	Confirm,
	Back
}

public static class PlayPadNavigation
{
	//W-P4 up, or no game at all. Note what is deliberately absent: "paused" is
	//not the test. A game paused by the classic menus, or by an auto-pause with
	//no overlay up, has no Play surface to drive, and its buttons stay the
	//console's - so a resumed game never eats a stray Confirm.
	public static bool HasAuthority(bool overlayOpen, bool gameLoaded) => overlayOpen || !gameLoaded;

	//The single action for a key that went down since `previous`, or None.
	//
	//`pressed` is the host's currently-pressed key codes and `previous` is what
	//the caller passed on the last tick; the caller records it on every tick,
	//authority or not, or a button held across the moment the overlay opens
	//would look like a new press. `mapping` is the code set resolved for the pad
	//in hand (PadNavControls.Resolve) - its codes carry the device, so a press
	//from any other pad matches nothing and needs no second device check here.
	//
	//Two keys becoming pressed in the same tick: the first in
	//PadNavControls.Navigation's order wins. That order lives in one place
	//(directions, then Confirm, then Back) instead of depending on the order the
	//host enumerates its pressed keys in, which is a set and has none - and a
	//stray move is recoverable while a stray Confirm activates whatever the
	//cursor happens to be sitting on.
	public static PadNavAction Next(IReadOnlyCollection<ushort> pressed, IReadOnlyCollection<ushort> previous, PadNavMapping? mapping, bool hasAuthority)
	{
		if(!hasAuthority || mapping is not PadNavMapping nav) {
			return PadNavAction.None;
		}

		foreach(PadNavAction action in PadNavControls.Navigation) {
			ushort code = CodeOf(nav, action);
			if(pressed.Contains(code) && !previous.Contains(code)) {
				return action;
			}
		}
		return PadNavAction.None;
	}

	private static ushort CodeOf(PadNavMapping mapping, PadNavAction action)
	{
		return action switch {
			PadNavAction.Up => mapping.Up,
			PadNavAction.Down => mapping.Down,
			PadNavAction.Left => mapping.Left,
			PadNavAction.Right => mapping.Right,
			PadNavAction.Confirm => mapping.Confirm,
			_ => mapping.Back
		};
	}
}
