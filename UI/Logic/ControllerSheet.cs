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

//ADR-0255 slice 2 (W-P17 PLAYERS): a player port as the sheet sees it. The
//port is a ControllerConfig (Port1, Port2, Controller) - never a mapping slot:
//Mapping1..4 are alternatives within one port, and reading them as four ports
//is the mistake the ADR records under "The answers, against the code". Slots
//are the port's four KeyMapping slots, each the key codes it binds (empty =
//free); the sheet reads a pad's device off the key codes, which is where the
//device a mapping speaks for lives (ControllerDevices.BaseGamepadIndex).
//
//A slot answers two different questions, so it carries two answers. `Slots` is
//every key the slot binds - the fixed fields plus the port type's own custom
//keys - which is what "is this slot taken?" (the free-slot rule, W-P15's HasKeys)
//and "which pad's keys are here?" read. `KeyboardSlots` is the fixed fields
//alone: the keyboard line asks what the *keyboard* plays, and a port type's
//custom keys (a Zapper's mouse clicks, a Family Basic Keyboard's rows) are that
//port device's buttons, not the keyboard's - so a Zapper-typed port must not
//report its mouse buttons as the keyboard's bindings.
public sealed record SheetPort(string Key, string Label, int ColorIndex, IReadOnlyList<ushort[]> Slots, IReadOnlyList<ushort[]> KeyboardSlots);

public enum PortMoveOutcome
{
	Moved,
	//The device's keys are already under the target port.
	AlreadyThere,
	//The target has fewer free slots than the device's keys need: refused rather
	//than overwriting a slot the user (or a preset) already bound.
	NoFreeSlot,
	//No mapping holds this device's keys: there is nothing to move.
	NotBound
}

//One slot of the device's keys moving, from a slot of a source port to a free
//slot of the target.
public sealed record SlotMove(int SourcePort, int SourceSlot, int TargetSlot);

//The plan for "put this device on that port". A move takes *every* slot that
//holds the device's keys - a device bound in two slots of a port must not leave
//one behind and end up playing as two players - so the plan is a list, one entry
//per slot. Empty unless Outcome is Moved.
public sealed record PortMove(PortMoveOutcome Outcome, IReadOnlyList<SlotMove> Slots)
{
	public bool Moves => Outcome == PortMoveOutcome.Moved;
}

public static class ControllerSheetPorts
{
	//The player ports of the loaded console, in the players' order. The console
	//decides - a fixed list would be a second table of "who is P1" (ADR-0250).
	public static IReadOnlyList<(string Key, int Player)> For(ConsoleType type)
	{
		return type switch {
			ConsoleType.Nes => new[] { ("Port1", 1), ("Port2", 2) },
			ConsoleType.Sms => new[] { ("Port1", 1), ("Port2", 2) },
			ConsoleType.Gameboy => new[] { ("Controller", 1) },
			ConsoleType.Gba => new[] { ("Controller", 1) },
			_ => System.Array.Empty<(string, int)>()
		};
	}

	//The one gamepad block a slot's keys belong to, or null when the slot is empty,
	//binds keyboard keys only, or names two pads (which is not an assignment this
	//sheet made, so it reads as no device rather than a guess). The block is the
	//key code with its button byte cleared, the same value
	//ControllerDevices.PadBlock builds from a pad's (Backend, Slot) - so a row
	//matches a pad however the host enumerated it, and a DirectInput pad is not the
	//XInput pad of the same ordinal (#813).
	public static int? SlotDevice(ushort[] slot)
	{
		int? block = null;
		foreach(ushort key in slot) {
			if(ControllerDevices.KeyBlock(key) is not int found) {
				continue;
			}
			if(block is int existing && existing != found) {
				return null;
			}
			block = found;
		}
		return block;
	}

	//The device whose keys live under a port: the device of its first slot that
	//names one.
	public static int? PortDevice(SheetPort port)
	{
		foreach(ushort[] slot in port.Slots) {
			if(SlotDevice(slot) is int device) {
				return device;
			}
		}
		return null;
	}

	//Whether the port holds this device in ANY of its slots. Distinct from
	//PortDevice, which answers the *first* named slot's device: a pad whose keys
	//sit in a later slot of the target (with an earlier slot naming another
	//device) is bound to that player, and "already there" must say so.
	private static bool TargetHolds(SheetPort port, int device)
	{
		foreach(ushort[] slot in port.Slots) {
			if(SlotDevice(slot) == device) {
				return true;
			}
		}
		return false;
	}

	//The first slot that binds nothing, or null when all four are taken.
	public static int? FreeSlot(SheetPort port)
	{
		for(int i = 0; i < port.Slots.Count; i++) {
			if(port.Slots[i].Length == 0) {
				return i;
			}
		}
		return null;
	}

	//"Put this device on that port": which free slots of the target receive the
	//keys and which slots of which ports they are moved out of, one plan for the
	//whole device so it cannot be split across two ports. The move refuses rather
	//than overwriting a slot the user (or a preset) already bound, refuses when the
	//target has too few free slots for every slot the device holds, and refuses a
	//pad nothing has bound yet - there would be no keys to move.
	public static PortMove PlanMove(IReadOnlyList<SheetPort> ports, int device, int targetPort)
	{
		if(targetPort < 0 || targetPort >= ports.Count) {
			return new(PortMoveOutcome.NotBound, System.Array.Empty<SlotMove>());
		}
		SheetPort target = ports[targetPort];

		//Every slot of every *other* port that holds the device's keys. The target's
		//own slots are left out: they are where the keys are going, not where they
		//come from.
		List<(int Port, int Slot)> sources = new();
		for(int port = 0; port < ports.Count; port++) {
			if(port == targetPort) {
				continue;
			}
			for(int slot = 0; slot < ports[port].Slots.Count; slot++) {
				if(SlotDevice(ports[port].Slots[slot]) == device) {
					sources.Add((port, slot));
				}
			}
		}

		if(sources.Count == 0) {
			return TargetHolds(target, device)
				? new(PortMoveOutcome.AlreadyThere, System.Array.Empty<SlotMove>())
				: new(PortMoveOutcome.NotBound, System.Array.Empty<SlotMove>());
		}

		List<int> free = new();
		for(int i = 0; i < target.Slots.Count; i++) {
			if(target.Slots[i].Length == 0) {
				free.Add(i);
			}
		}
		if(free.Count < sources.Count) {
			return new(PortMoveOutcome.NoFreeSlot, System.Array.Empty<SlotMove>());
		}

		List<SlotMove> moves = new();
		for(int i = 0; i < sources.Count; i++) {
			moves.Add(new SlotMove(sources[i].Port, sources[i].Slot, free[i]));
		}
		return new(PortMoveOutcome.Moved, moves);
	}
}

//ADR-0255 slice 2, the keyboard case's read side: with no pad connected the
//sheet says what the keyboard does. The "may the preset be written back" question
//is NOT here: it belongs to Configuration.CanRestoreKeyboardPreset, the guard the
//load path itself uses, and the sheet asks that one rather than a copy (ADR-0255:
//"the guard belongs where the presets are resolved, not in one caller").
public static class ControllerSheetKeyboard
{
	//The keyboard keys the player's port binds - every key that is not a
	//gamepad's - in slot then field order, distinct. The keyboard plays as
	//player 1, so this is the first port that binds any; a keyboard bound to a
	//player no pad is on is still the player's. The ViewModel names the codes
	//(InputApi.GetKeyName); the rule here is which codes count.
	//
	//It reads KeyboardSlots, not Slots: the fixed KeyMapping fields only. A port
	//type's custom keys are that port device's buttons, not the keyboard's, so a
	//Zapper's mouse clicks must not surface as the keyboard's bindings (finding 1).
	public static IReadOnlyList<ushort> Keys(IReadOnlyList<SheetPort> ports)
	{
		foreach(SheetPort port in ports) {
			List<ushort> keys = new();
			foreach(ushort[] slot in port.KeyboardSlots) {
				foreach(ushort key in slot) {
					if(key != 0 && ControllerDevices.KeyBlock(key) == null && !keys.Contains(key)) {
						keys.Add(key);
					}
				}
			}
			if(keys.Count > 0) {
				return keys;
			}
		}
		return System.Array.Empty<ushort>();
	}
}
