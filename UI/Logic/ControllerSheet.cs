using System.Collections.Generic;

namespace Mesen.Logic;

//ADR-0255 slice 1 (PRD Part B §13.5.2 W-P17): the Play Controller sheet's
//host-free rules. The picture is W-P15's (ControllerPadLayout owns every
//coordinate) and the values come from the host tester (GamepadTesterViewModel),
//so what belongs here is the one thing neither owns: which of the pad's buttons
//lights which key of the drawn pad, and what the drawn keys are called.
public static class ControllerLivePad
{
	//GamepadTestItem's own button order, which is the core's
	//GamepadState.Buttons bit order: A, B, X, Y, LB, RB, Menu, Options, DUp,
	//DDown, DLeft, DRight, LT, RT, L3, R3, then the four stick directions. The
	//drawn pad has ten keys, so X, Y, the triggers, the stick clicks and the
	//stick directions have no key and never light: the picture shows what a
	//console pad has, the values list beside it shows everything the pad
	//reports. Two tests pin the coupling - UI.Tests asserts the table, and
	//UI.HeadlessTests/PlayerControllerSheetTests asserts the tester's real list
	//against it - so a reordered core cannot silently light the wrong key.
	private static readonly Dictionary<SetupButton, int> _bits = new() {
		[SetupButton.A] = 0,      //"A"
		[SetupButton.B] = 1,      //"B"
		[SetupButton.L] = 4,      //"LB"
		[SetupButton.R] = 5,      //"RB"
		//"Menu" is the pad's right-middle button and "Options" its left one (the
		//DualSense / Xbox view-menu pair), which is how W-P15 draws the pills.
		[SetupButton.Start] = 6,  //"Menu"
		[SetupButton.Select] = 7, //"Options"
		[SetupButton.Up] = 8,     //"DUp"
		[SetupButton.Down] = 9,   //"DDown"
		[SetupButton.Left] = 10,  //"DLeft"
		[SetupButton.Right] = 11  //"DRight"
	};

	//The keys the sheet draws, in the order the items are built. Every member of
	//SetupButton, so a key the layout gains cannot quietly miss a bit here.
	public static readonly SetupButton[] Keys = {
		SetupButton.Up, SetupButton.Down, SetupButton.Left, SetupButton.Right,
		SetupButton.Select, SetupButton.Start,
		SetupButton.B, SetupButton.A, SetupButton.L, SetupButton.R
	};

	//The tester's button that lights this key, or null when the drawn pad has no
	//counterpart for it.
	public static int? BitOf(SetupButton button) => _bits.TryGetValue(button, out int bit) ? bit : null;

	//The key's own name - the pad's button, not the console's: this sheet reads
	//the pad in your hands, and its values list names the same buttons. Only the
	//round and shoulder keys draw it (ControllerPadLayout.ShowsLabel, W-P15's
	//rule - the D-pad arms and the pills are too small), but every key needs one
	//for the accessibility tree, where a wordless arm would announce as nothing.
	public static string KeyName(SetupButton button) => button.ToString();
}

//ADR-0255 slice 1: when the sheet may read the host devices. The tester's own
//60 Hz poll is gated on its Test tab; this sheet's is gated on being the visible
//surface over a *paused* game, so opening it never reads devices behind a
//running game (ADR-0255's Consequences). Host-free and dual-compiled because one
//of the four answers - a game resuming under a visible sheet - is only reachable
//from a timer tick, which no test host can drive: the rule is tested here, and
//the headless suite covers the timer that follows it.
public static class ControllerSheetReads
{
	public static bool Wanted(bool sheetVisible, bool gamePaused) => sheetVisible && gamePaused;
}
