using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Config.Shortcuts;
using Mesen.Interop;

namespace Mesen.Logic;

//ADR-0255 slice 3 (PRD Part B §13.5.2 W-P17): remapping as a *mode* of the
//Controller sheet rather than a dialog. Pick a row (a console control), press a
//control, Esc cancels - and each row carries two lights, what the pad sends and
//what the port receives, because testing and mapping are one line and not two
//screens. This file is the host-free half: which controls a console binds, where
//a rebind lands, the pad button a bound code names, and - the part that carries
//the slice's decision - the two lights, each a different reading of the same
//binding.
//
//The rows are the console's own controls, the same list W-P15's setup walks
//(ControllerSetupSteps.For), so a control the setup sheet can bind is a control
//this sheet can rebind and nothing else: the sheet draws W-P15's pad, and the
//nav controls (ADR-0256 Decision 4) are not in that list at all.
public static class ControllerSheetRemap
{
	//The console controls a player's port binds, in the order the sheet lists
	//them. `Controls` is empty for a console with no player port (or none this
	//sheet knows), which is what leaves the REMAP section dark instead of
	//offering a row nothing can write.
	public static IReadOnlyList<SetupButton> Controls(ConsoleType console)
	{
		return console switch {
			ConsoleType.Nes => ControllerSetupSteps.For(SetupConsole.Nes),
			ConsoleType.Gameboy => ControllerSetupSteps.For(SetupConsole.GameBoy),
			ConsoleType.Sms => ControllerSetupSteps.For(SetupConsole.MasterSystem),
			ConsoleType.Gba => ControllerSetupSteps.For(SetupConsole.Gba),
			_ => Array.Empty<SetupButton>()
		};
	}

	//The console's own name for a control, as a row shows it. Almost every control
	//is its own name; the Master System pad's two buttons are the console's 1 and
	//2, and which field carries which is the *core's* answer, not a preference:
	//GetKeyNames() is "UDLR12P" (SmsController.h), so button 1 is the field the
	//core reads as B and button 2 the one it reads as A, and the classic page's
	//own view draws Mapping.B as "1" and Mapping.A as "2" (SmsControllerView.axaml).
	//A row labelled "1" has to write the field the console calls button 1, or the
	//player binds a button they did not pick - which is what both surfaces did
	//before this rule was shared (found in review).
	public static string ControlLabel(ConsoleType console, SetupButton button)
	{
		if(console == ConsoleType.Sms && button is SetupButton.A or SetupButton.B) {
			return button == SetupButton.A ? "2" : "1";
		}
		return button.ToString();
	}

	//Where a rebind lands among the port's four slots, in order: the slot that
	//already binds this control, so a rebind replaces its own binding and cannot
	//leave the old key behind in another slot; else the slot the selected pad's
	//other keys are already in (`padSlot`), so the pad keeps one slot instead of
	//being split across two - a split pad makes the PLAYERS assignment move two
	//slots and refuse with NoFreeSlot where one would have fit (review of #839);
	//else the first slot that binds nothing at all; else null - every slot is
	//taken, and the sheet says so rather than overwriting a binding the player
	//(or a preset) made. `controlPerSlot` is this control's field per slot
	//(0 = unbound), `slotTaken` is the port's own free-slot rule (a slot binding
	//anything, custom keys included), and `padSlot` is null for a pad nothing in
	//the port holds - the setup case, where the first free slot is the answer.
	public static int? TargetSlot(IReadOnlyList<ushort> controlPerSlot, IReadOnlyList<bool> slotTaken, int? padSlot)
	{
		for(int i = 0; i < controlPerSlot.Count; i++) {
			if(controlPerSlot[i] != 0) {
				return i;
			}
		}
		if(padSlot is int own && own >= 0 && own < slotTaken.Count) {
			return own;
		}
		for(int i = 0; i < slotTaken.Count; i++) {
			if(!slotTaken[i]) {
				return i;
			}
		}
		return null;
	}

	//The other half of a rebind (#941): the pad button just bound to `control`
	//comes off every other control of the same port that had it, in any of the
	//port's four slots - they are alternatives for one player, so the same button
	//on two controls is one press firing two controls. The control being bound is
	//never in the answer (where its own binding lands is TargetSlot's call), a
	//code nothing else holds - a keyboard key, another pad button - is untouched,
	//and an unbound code displaces nothing. `controls` is the console's own list
	//(Controls), `field(slot, control)` reads the port's mapping, so the answer
	//is read off the one store the port already keeps and is not a second record
	//of which control owns a button. In slot order, then the console's order.
	public static IReadOnlyList<RemapDisplaced> Displaced(IReadOnlyList<SetupButton> controls, Func<int, SetupButton, ushort> field, int slotCount, SetupButton control, ushort code)
	{
		List<RemapDisplaced> displaced = new();
		if(code == 0) {
			return displaced;
		}
		for(int slot = 0; slot < slotCount; slot++) {
			foreach(SetupButton other in controls) {
				if(other != control && field(slot, other) == code) {
					displaced.Add(new RemapDisplaced(slot, other));
				}
			}
		}
		return displaced;
	}

	//The pad's own button a bound key code names, by the core's name for that
	//button - the per-backend order slice 1 reads through ControllerLivePad. The
	//device *number* is ignored on purpose (see Lights): a binding whose device
	//index moved still names a button the pad in hand can press. Null when the
	//code's name is no pad button this backend's table carries - a keyboard key,
	//or a family whose buttons this backend does not spell (DirectInput's
	//numbered joystick buttons).
	//
	//The name has to be a pad's before any of that: the prefix is the only thing
	//that says so, and "Page Up" ends in a pad button's own name too (KeyDefinitions
	//names keyboard codes 19/20 that way, and Page Up ships bound in a default NES
	//mapping), so reading the tail alone lit a row's pad side for a keyboard binding
	//the pad sends nothing for. PadNaming.Parse is the same "is this name a pad's"
	//test the rest of the Play door reads a device by.
	public static int? ButtonBitOfCodeName(string keyName, GamepadBackend backend)
	{
		if(PadNaming.Parse(keyName) is not PadId) {
			return null;
		}
		int space = keyName.IndexOf(' ');
		if(space <= 0 || space + 1 >= keyName.Length) {
			return null;
		}
		int bit = Array.IndexOf(ControllerLivePad.NameList(backend), keyName.Substring(space + 1));
		return bit >= 0 ? bit : null;
	}

	//The two lights of a row, each a different reading of the same binding
	//(ADR-0255 slice 3). The user kept both on 2026-10-04 over one or none:
	//a row lit on the pad side and dark on the port side is a wrong binding made
	//visible, and nothing else in the app shows one.
	//
	//Pad side - what the pad sends - is the pad's own GamepadState.Buttons: the
	//button the row is bound to is being pressed on the pad in the player's
	//hands. The device index the binding carries is deliberately not consulted:
	//the question is what the pad in hand is sending, and a binding that names
	//another device index (a pad reconnected at a different slot, ADR-0255
	//slice 5) still names a button this pad has.
	//
	//Port side - what the port receives - is whether the console is being handed
	//the control: the port's mapping for the row binds exactly one host code, and
	//the console's key manager reports a code held. A stale binding's code is
	//never held, so the two answers part company exactly there.
	public static RemapLights Lights(ushort boundCode, int? boundButtonBit, bool padButtonHeld, bool portCodeHeld)
	{
		return new RemapLights(
			boundCode != 0 && boundButtonBit is not null && padButtonHeld,
			boundCode != 0 && portCodeHeld);
	}
}

//One control a rebind took its pad button off (#941): the slot it was bound in
//and the control, so the write can clear that field and the note can name it.
public readonly record struct RemapDisplaced(int Slot, SetupButton Control);

//The two lights of one row, as the ViewModel applies them.
public sealed record RemapLights(bool PadLit, bool PortLit);

//What a capture is waiting to write. One machine serves both of the sheet's
//binding surfaces (ADR-0255 slices 3 and 4): REMAP binds a console control, and
//EXTRA BUTTONS binds one of the emulator's own actions. Only the target differs,
//so only the target is here - the arming rule, the release-first rule, the
//refusal and the "one press, one bind" rule are the same decisions on both, and
//a second machine would be a second copy of them.
public readonly record struct CaptureTarget(bool IsExtra, SetupButton Button, EmulatorShortcut Shortcut)
{
	public static CaptureTarget Remap(SetupButton button) => new(false, button, default);
	public static CaptureTarget Extra(EmulatorShortcut shortcut) => new(true, default, shortcut);
}

public enum CaptureOutcome
{
	//Not capturing (or a tick that changed nothing): the caller does nothing.
	None,
	//Capturing, but no pad button has gone down since the last tick.
	Waiting,
	//A pad button went down and it is a navigation control (ADR-0256 Decision 4):
	//refused, and the capture stays armed so the player can press another one.
	Refused,
	//A pad button went down and can be bound: BoundCode carries the code.
	Bound
}

//ADR-0255 slice 3, the capture: "press a control" as a state of the sheet.
//
//The rules are the ones W-P15's setup session already learned, and the two the
//sheet adds are its own: the press that opened the capture must be released
//first (the Confirm that tapped the row would otherwise bind itself), and the
//pad's own navigation controls are refused (ADR-0256 Decision 4 - a player who
//binds Confirm or Back to a control their pad does not have is stuck, with no
//keyboard behind it, and the refusal is shown rather than swallowed).
//
//The keys are filtered to the pad the sheet is showing, by the key-code block
//ControllerDevices owns, so a second pad cannot bind while this one is being
//worked on - the same rule the PLAYERS rows match a device by.
public sealed class ControllerSheetCapture
{
	private bool _armed;
	private HashSet<ushort> _previous = new();

	public bool IsCapturing { get; private set; }
	//What the capture will write when it binds, so the sheet can light the row it
	//is waiting on and the write can land on the right one.
	public CaptureTarget Target { get; private set; }
	//The console control REMAP is capturing (CaptureTarget.Button). Reading it is
	//how the REMAP rows light the armed one; EXTRA BUTTONS reads Target.Shortcut.
	public SetupButton Button => Target.Button;
	public ushort BoundCode { get; private set; }

	public void Arm(CaptureTarget target)
	{
		IsCapturing = true;
		Target = target;
		BoundCode = 0;
		_armed = false;
		_previous.Clear();
	}

	//The REMAP arm, kept as the shape slice 3 shipped so its callers and tests
	//read the same as they did.
	public void Arm(SetupButton button) => Arm(CaptureTarget.Remap(button));

	public void Cancel()
	{
		IsCapturing = false;
	}

	//One poll tick. `pressed` is every key down now (the host's own set, the one
	//the bridge reads); `deviceBlock` is the key-code block of the pad the sheet
	//shows (ControllerDevices.PadBlock); `nonRebindable` answers ADR-0256
	//Decision 4 for a code (PadNavControls.NonRebindable, resolved for the pad
	//the code came from - the caller owns the family and device, this file owns
	//no name table).
	public CaptureOutcome OnTick(IReadOnlyCollection<ushort> pressed, int deviceBlock, Func<ushort, bool> nonRebindable)
	{
		if(!IsCapturing) {
			return CaptureOutcome.None;
		}

		List<ushort> keys = pressed.Where(k => ControllerDevices.KeyBlock(k) == deviceBlock).ToList();
		if(!_armed) {
			//The sheet arms on a press (a pad Confirm, or a click on a row whose
			//button is still held on the pad): the pad has to be let go of before
			//the first step listens, or the button that opened the capture would
			//bind itself. Still recorded every tick, so a held button is not read
			//as new on the tick the arming completes.
			_armed = keys.Count == 0;
			_previous = new HashSet<ushort>(pressed);
			return CaptureOutcome.Waiting;
		}

		ushort? newest = null;
		foreach(ushort key in keys) {
			//Two buttons in one tick: the lowest code wins, so the answer never
			//depends on the order the host enumerates a set in.
			if(!_previous.Contains(key) && (newest is null || key < newest)) {
				newest = key;
			}
		}
		_previous = new HashSet<ushort>(pressed);

		if(newest is not ushort code) {
			return CaptureOutcome.Waiting;
		}
		if(nonRebindable(code)) {
			return CaptureOutcome.Refused;
		}
		BoundCode = code;
		IsCapturing = false;
		return CaptureOutcome.Bound;
	}
}
