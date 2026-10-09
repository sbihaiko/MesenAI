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
	//The door the whole ADR is for: Player UI mode in a game-screen workspace -
	//the switcher's Play door, or Classic under Player mode. ADR-0256 is the Play
	//GUI's, and the classic (Advanced) GUI keeps its own input behavior, so every
	//rule below is asked *inside* this gate, never instead of it.
	//
	//It is a rule of its own, and not a private test in the window that wires the
	//pad, because two places ask the same door: the bridge's authority and Back
	//edge (PlayPadNavigationWiring) and the slot grid's own key loop
	//(StateGrid.TimerInput_Tick). The grid is one control serving both doors - it
	//is W-P2's row of tiles and the Save/Load screens in Play, and Advanced's
	//game-selection and Save/Load screens in the classic GUI - so a branch of it
	//that reads the pad's preset has to ask the same door the bridge does, or the
	//classic grid loses the console mapping it had before ADR-0256 and its pad
	//keys are consumed by a branch nothing else in Advanced can act on.
	public static bool InPlayDoor(bool isPlayerMode, bool isPlayWorkspace)
	{
		return isPlayerMode && isPlayWorkspace;
	}

	//Decisions 1 and 2, stated as the predicate they actually are: the pad drives
	//the GUI while the console is not in the player's hands. Three things say it
	//is not, and each is a named surface - never the coarse "something is drawn
	//over the game".
	//
	//  * A surface that took the console away is up: W-P4 and every sheet opened
	//    from it sits over a pause the overlay itself took, and that pause is what
	//    makes the surface the pad's (`playSurfaceUp && gamePaused`). Neither half
	//    alone is the test - a surface that never pauses (the barcode tool sheet,
	//    Settings reached from a task door, the archive's ROM list) can sit over a
	//    running game and must NOT hand the pad to the GUI (Decision 1: while a
	//    game runs unpaused the pad is the console's and nothing else), and a game
	//    paused by the classic menus or by an auto-pause with no Play surface has
	//    nothing to drive either.
	//  * No game is loaded and the load card is not the thing on screen: the home,
	//    or a sheet over it such as the W-P13 BIOS sheet shown inside a load.
	//  * The on-load pack picker is up: it is opened by itself over an un-enhanced
	//    game that is NOT paused (EvaluatePlayerPackPicker never pauses), and the
	//    pack has to be chosen before the game is played - the one unpaused surface
	//    a first-run cabinet must be able to act on.
	//
	//`loadCardUp` is the exception that names itself. The #734 load card pauses
	//nothing and carries no focusable control of its own, so the pad has nothing
	//to aim at and authority would only let a Confirm reach the home underneath
	//and launch a game through the card. It is NOT the W-P13 BIOS sheet, which
	//shares the same load (`IsLoadWaitActive` is true under both): the sheet has a
	//focusable control, and that is the whole reason the two are named apart
	//instead of both being refused by one coarse `loading` flag.
	//
	//The consequence, stated rather than worked around: while a game runs unpaused,
	//a surface that does not pause is not drivable by pad at all. That is the price
	//of Decision 1 - taking the console's buttons means giving up the pad's - and
	//the fix is for such a surface to pause the game (as W-P4 does), never for this
	//rule to widen back to "something is drawn over the game".
	public static bool HasAuthority(bool playSurfaceUp, bool gameLoaded, bool gamePaused, bool loadCardUp, bool firstRunPickerUp)
	{
		//The on-load picker outranks the load card: it is posted as the game loads,
		//before the first picture clears the card, and it has to be answered.
		if(firstRunPickerUp) {
			return true;
		}
		if(loadCardUp) {
			return false;
		}
		return !gameLoaded || (playSurfaceUp && gamePaused);
	}

	//ADR-0255 slice 3 adds one clause to the same predicate: while the Controller
	//sheet is capturing "press a control", the pad is the capture's, so it is
	//refused and the capture consumes the press. That is not a second rule - the
	//capture is a state of "the pad is not the GUI's", which is what HasAuthority
	//answers - and it is here, above both callers, because since #1127 TWO input
	//paths have to give the same answer: the pad's bridge
	//(PlayPadNavigationWiring.HasAuthority) and the window's own keyboard arms.
	//A keyboard move or Back that sounds where this answers false - a game paused
	//with no Play surface up, the #734 load card, a capture in progress - is the
	//divergence the review of #1157 sent back: the two paths must agree, not
	//merely reach the same sink.
	public static bool AuthorityInPlayDoor(bool inPlayDoor, bool controllerCapturing, bool playSurfaceUp, bool gameLoaded, bool gamePaused, bool loadCardUp, bool firstRunPickerUp)
	{
		return inPlayDoor && !controllerCapturing
			&& HasAuthority(playSurfaceUp, gameLoaded, gamePaused, loadCardUp, firstRunPickerUp);
	}

	//The pad's Back code going down, whether or not the pad has menu authority.
	//The slot grid's own way out is the one press no authority rule may gate: a
	//grid opened by the Load/Save-state shortcuts sits over a game the pad does
	//not otherwise drive (CurrentPlaySheet() is None, so HasAuthority is false),
	//and the grid's 50 ms loop moves SelectedIndex but has no exit of its own. A
	//player who cannot leave the slot grid is the failure ADR-0256 exists to
	//prevent, so the caller asks this before it asks HasAuthority.
	public static bool IsBackEdge(IReadOnlyCollection<ushort> pressed, IReadOnlyCollection<ushort> previous, PadNavMapping? mapping)
	{
		return mapping is PadNavMapping nav && pressed.Contains(nav.Back) && !previous.Contains(nav.Back);
	}

	//ADR-0264 Decision 3: the sheet's own control going down. Written the same
	//way IsBackEdge is, and for the same reason - it is read out of the caller's
	//own pressed sets rather than out of Next's answer, because the sheet's
	//controls are not members of PadNavAction and Next answers one action.
	//
	//A code of null or 0 is "this pad has no such control" and answers false: on
	//a backend that defines the name it is some other button, and acting on it
	//would be watching a control the player does not have.
	public static bool IsSheetEdge(ushort? code, IReadOnlyCollection<ushort> pressed, IReadOnlyCollection<ushort> previous)
	{
		return code is ushort value && value != 0 && pressed.Contains(value) && !previous.Contains(value);
	}

	//Decision 4 for the slot grid: what one pad code means while the grid holds the
	//focus, off the pad's own preset and never off the console mapping the port
	//carries. The player rebinding or clearing their console D-pad must not change
	//or lose grid navigation, and a second pad must be able to drive it.
	//
	//Back is deliberately not here. Leaving the grid is the bridge's (IsBackEdge),
	//and that split is exactly what resolves the B ambiguity: the pad's B is the
	//preset's Back, while the console mapping puts the console's A on that same
	//button. Reading the grid's load off the preset instead puts Confirm on the
	//pad's A and leaves B to the bridge, instead of the old loop reading console A
	//as "load this slot" and colliding with Back.
	public static PadNavAction GridAction(ushort keyCode, PadNavMapping mapping)
	{
		foreach(PadNavAction action in PadNavControls.Navigation) {
			if(action == PadNavAction.Back) {
				continue;
			}
			if(keyCode == CodeOf(mapping, action)) {
				return action;
			}
		}
		return PadNavAction.None;
	}

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

	//The code one of the six actions is bound to on this mapping. Public
	//because the repeat rule (PadNavRepeat) reads the directions back out of a
	//pressed set and must ask the same table Next does, never its own copy.
	public static ushort CodeOf(PadNavMapping mapping, PadNavAction action)
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
