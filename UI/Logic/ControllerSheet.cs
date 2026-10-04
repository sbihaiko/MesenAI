using System;
using System.Collections.Generic;
using Mesen.Interop;

namespace Mesen.Logic;

//ADR-0255 slice 1 (PRD Part B §13.5.2 W-P17): the Play Controller sheet's
//host-free rules. The picture is W-P15's (ControllerPadLayout owns every
//coordinate) and the values come from the host tester (GamepadTesterViewModel),
//so what belongs here is the one thing neither owns: which of the pad's buttons
//lights which key of the drawn pad, and what the drawn keys are called.
public static class ControllerLivePad
{
	//The core's GamepadState.Buttons numbers one bit per button, and the bit -> button
	//order is *per backend*. This is that order as the core names it: entry i is the
	//core's own name for bit i, copied from the backend's own key table
	//(Windows/WindowsKeyManager.cpp, MacOS/MacOSKeyManager.mm, Linux/LinuxKeyManager.cpp).
	//UI.HeadlessTests/ControllerSheetPadTests reads those three tables back and fails
	//when this mirror drifts, so the drawn keys, the value chips and the core read one
	//order with no unchecked hand copy left between them. Core/Shared/
	//GamepadButtonOrder.h carries the ten console keys that same order exposes, and the
	//same test pins it to this list.
	//
	//Only the first 24 bits are listed, because that is all GamepadState's consumers
	//show. The evdev D-pad is not among them: a hat is reported as axes at bits 26..29
	//(LinuxGameController.cpp), outside the 24 - which is why those four keys stay
	//dark on that backend rather than lighting from a guess.
	private static readonly Dictionary<GamepadBackend, string[]> _names = new() {
		[GamepadBackend.GameController] = new[] {
			"A", "B", "X", "Y", "L1", "R1", "Start", "Select",
			"Up", "Down", "Left", "Right", "L2", "R2", "L3", "R3",
			"X+", "X-", "Y+", "Y-", "X2+", "X2-", "Y2+", "Y2-"
		},
		[GamepadBackend.XInput] = new[] {
			"Up", "Down", "Left", "Right", "Start", "Back", "L3", "R3",
			"L1", "R1", "?", "?", "A", "B", "X", "Y",
			"L2", "R2", "RT Up", "RT Down", "RT Left", "RT Right", "LT Up", "LT Down"
		},
		[GamepadBackend.Evdev] = new[] {
			"A", "B", "C", "X", "Y", "Z", "L1", "R1",
			"L2", "R2", "Select", "Start", "L3", "R3",
			"X+", "X-", "Y+", "Y-", "Z+", "Z-", "X2+", "X2-", "Y2+", "Y2-"
		}
	};

	//The core's name for each key the sheet draws. Only Select differs by backend:
	//macOS and evdev call it Select, XInput calls it Back (WindowsKeyManager.cpp's
	//own key table).
	private static readonly Dictionary<SetupButton, string> _keyNames = new() {
		[SetupButton.Up] = "Up", [SetupButton.Down] = "Down",
		[SetupButton.Left] = "Left", [SetupButton.Right] = "Right",
		[SetupButton.Start] = "Start", [SetupButton.Select] = "Select",
		[SetupButton.B] = "B", [SetupButton.A] = "A",
		[SetupButton.L] = "L1", [SetupButton.R] = "R1"
	};

	//The keys the sheet draws, in the order the items are built. Every member of
	//SetupButton, so a key the layout gains cannot quietly miss a bit here.
	public static readonly SetupButton[] Keys = {
		SetupButton.Up, SetupButton.Down, SetupButton.Left, SetupButton.Right,
		SetupButton.Select, SetupButton.Start,
		SetupButton.B, SetupButton.A, SetupButton.L, SetupButton.R
	};

	//The core's GamepadState.Buttons bit that lights this key on `backend`, or null
	//when that backend's state carries no counterpart for it (the key then stays
	//dark): the evdev D-pad, and DirectInput - raw joystick buttons with no console
	//button to name one by, so the sheet lists none of its keys. Null - not zero -
	//for an unknown backend: a pad the sheet cannot place must not light its first
	//key from bit 0.
	public static int? BitOf(SetupButton button, GamepadBackend backend)
	{
		if(!_names.TryGetValue(backend, out string[]? names)) {
			return null;
		}
		int index = Array.IndexOf(names, CoreNameOf(button, backend));
		return index >= 0 ? index : null;
	}

	//The core's own name for `backend`'s bit `bit`, or null when this backend's
	//GamepadState carries no such bit (an unplaceable pad, DirectInput's raw joystick
	//buttons, and the evdev D-pad's bits past the 24 window). The value chips show
	//these, in the order the core reports, so a press lights one key of the picture
	//and one chip, both naming the same button on every backend.
	public static string? NameOfBit(GamepadBackend backend, int bit)
		=> _names.TryGetValue(backend, out string[]? names) && bit >= 0 && bit < names.Length ? names[bit] : null;

	//The core's own names for a backend's bits, in bit order - what a pad's chip row
	//shows. A pad the sheet cannot place (None, or DirectInput, which has no console
	//names) falls back to the console order, the one a console pad's names describe;
	//its drawn keys still stay dark, because BitOf names no key there.
	public static string[] NameList(GamepadBackend backend)
		=> _names.TryGetValue(backend, out string[]? names) ? names : _names[GamepadBackend.GameController];

	//The key's own name - the pad's button, not the console's: this sheet reads
	//the pad in your hands, and its values list names the same buttons. Only the
	//round and shoulder keys draw it (ControllerPadLayout.ShowsLabel, W-P15's
	//rule - the D-pad arms and the pills are too small), but every key needs one
	//for the accessibility tree, where a wordless arm would announce as nothing.
	public static string KeyName(SetupButton button) => button.ToString();

	private static string CoreNameOf(SetupButton button, GamepadBackend backend)
		=> button == SetupButton.Select && backend == GamepadBackend.XInput ? "Back" : _keyNames[button];
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
	//The reads: the sheet touches the host devices only while it is visible *and*
	//the game is paused, the state it makes true when it opens.
	public static bool Wanted(bool sheetVisible, bool gamePaused) => sheetVisible && gamePaused;

	//The timer's own rule: it runs while the sheet is up, even behind a game that
	//resumed under it. Nothing observes the pause state, so a tick is the only
	//thing that can notice the game pausing again - a timer stopped on resume
	//never restarted, and the drawn keys and readouts froze forever. The reads
	//keep the stricter rule above; this only decides whether a tick happens at all.
	public static bool Polls(bool sheetVisible) => sheetVisible;
}
