using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//ADR-0256 Decision 4: confirm, back and the four directions follow the pad's
//own preset - DefaultKeyMappingType.Xbox or DefaultKeyMappingType.Ps4, the
//flags the first run applies (PlayFirstRun.Mappings) and KeyPresets writes into
//the port's slots - and no surface may unbind them. The reason is the one the
//decision exists for: the target is a cabinet with a pad and nothing else, so a
//player who binds "confirm" to a control their pad does not have is stuck, with
//no keyboard and no pointer to recover with. ADR-0255's extra-buttons section
//is the pad's spare controls for the same reason, and its setup sheet binds the
//console's buttons, never these.
//
//The two presets do not share a button table, so a resolution is only
//meaningful inside the family it was asked for: ApplyXboxLayout writes XInput
//names ("Pad1 A", "Pad1 Up"), ApplyPs4Layout DirectInput ones ("Joy1 But2",
//"Joy1 DPad Up"). PadFamily is the host-free mirror of that flag - the config
//enum lives in UI/Config, which UI/Logic cannot see; FirstRunMappings is the
//same mirror - and every name below is the one KeyPresets itself resolves,
//copied from it rather than guessed, because a name that drifted would leave
//the pad navigating with codes nothing in the config binds.
public enum PadFamily
{
	Xbox,
	Ps4
}

//The six codes the pad navigates with, already resolved for one device.
public readonly record struct PadNavMapping(ushort Up, ushort Down, ushort Left, ushort Right, ushort Confirm, ushort Back);

//ADR-0264 Decision 3: the library sheet's own controls, which are NOT the six
//the pad navigates with. **Y** opens the sheet's search box, and W-P19's footer
//names it ("Y Search"), so the button is the decision rather than a detail of
//the bridge that reads it. LB / RB cycle the console filter and join this table
//with the ticket that builds it (#1034).
//
//A SECOND table rather than two more members of PadNavMapping, and the reason is
//ADR-0255: that ADR's extra-buttons section binds the pad's spare controls, and
//a control in the navigation set is one no rebinding surface may offer
//(NonRebindable). A player who binds Y to a console's own button keeps it - Y is
//the sheet's only while the sheet has the focus, which is what "the sheet's own
//control" means.
public enum PadSheetControl
{
	Search
}

public static class PadNavControls
{
	//The six, in the order a surface lists them and the order two presses in one
	//tick are broken in (PlayPadNavigation.Next).
	public static readonly IReadOnlyList<PadNavAction> Navigation = new[] {
		PadNavAction.Up, PadNavAction.Down, PadNavAction.Left, PadNavAction.Right,
		PadNavAction.Confirm, PadNavAction.Back
	};

	//A control no rebinding surface may ever offer, by the code that surface is
	//holding. ADR-0256 Decision 4 and ADR-0255 slice 4 ("the section is the
	//pad's spare controls, not its navigation"): a prompt that accepted one of
	//these would take the player's only way back, and on a cabinet there is no
	//keyboard behind it to recover with. The caller passes the mapping resolved
	//for the pad the press came from - a code from another device is not one of
	//these codes, and a family that resolved to nothing protects nothing.
	public static bool NonRebindable(ushort keyCode, PadNavMapping? mapping)
	{
		if(mapping is not PadNavMapping nav) {
			return false;
		}
		return keyCode == nav.Up || keyCode == nav.Down || keyCode == nav.Left || keyCode == nav.Right
			|| keyCode == nav.Confirm || keyCode == nav.Back;
	}

	//keyCode is the host's own name-to-code lookup (InputApi.GetKeyCode), which
	//answers 0 for a name the platform's key manager does not define.
	//
	//Null is a real answer here, not a failure, and it is the one this returns
	//instead of guessing: a family the app cannot tell, a device index the
	//caller could not resolve, or a name this backend does not have (the PS4
	//preset's "Joy" names exist only where DirectInput does - macOS and Linux
	//name every pad "Pad"). A guess would either watch the wrong button or hand
	//the player a confirm they cannot press.
	public static PadNavMapping? Resolve(PadFamily? family, int device, Func<string, ushort> keyCode)
	{
		if(family is not PadFamily known || device < 0) {
			return null;
		}

		ushort? up = Code(known, device, PadNavAction.Up, keyCode);
		ushort? down = Code(known, device, PadNavAction.Down, keyCode);
		ushort? left = Code(known, device, PadNavAction.Left, keyCode);
		ushort? right = Code(known, device, PadNavAction.Right, keyCode);
		ushort? confirm = Code(known, device, PadNavAction.Confirm, keyCode);
		ushort? back = Code(known, device, PadNavAction.Back, keyCode);

		if(up is not ushort u || down is not ushort d || left is not ushort l
			|| right is not ushort r || confirm is not ushort c || back is not ushort b) {
			return null;
		}
		return new PadNavMapping(u, d, l, r, c, b);
	}

	//ADR-0264 Decision 3: the code one of the sheet's own controls is bound to on
	//this pad, or null. Null carries exactly the meaning Resolve's does - a
	//family the app cannot tell, a device index nobody resolved, a name this
	//backend does not define - because a caller that acted on a guess would be
	//watching a button the player does not have.
	public static ushort? SheetCode(PadFamily? family, int device, PadSheetControl control, Func<string, ushort> keyCode)
	{
		if(family is not PadFamily known || device < 0) {
			return null;
		}
		return Code(SheetNamesOf(known, device, control), keyCode);
	}

	private static ushort? Code(PadFamily family, int device, PadNavAction action, Func<string, ushort> keyCode)
	{
		return Code(NamesOf(family, device, action), keyCode);
	}

	private static ushort? Code(IReadOnlyList<string> names, Func<string, ushort> keyCode)
	{
		foreach(string name in names) {
			ushort code = keyCode(name);
			if(code != 0) {
				return code;
			}
		}
		return null;
	}

	//The control's own name on the pad, per family. Confirm and back are the
	//pad's affirmative and cancel buttons as the player reads them off the
	//plastic - the Xbox pad's A and B, the DualShock's cross and circle - which
	//is why back is "A" on one desk and a DirectInput button number on another.
	//
	//Read off KeyPresets, not mirrored from it: that file puts the *console's* A
	//on the pad's right-hand button (the Xbox B, DirectInput But3), which is the
	//opposite of what a menu wants. A console's jump is not a menu's yes, and a
	//preset is the wrong place to read one off the other.
	//
	//One table, not a switch, because the two names of the same control have to
	//stay side by side: the host may only define one of them, and the fallback in
	//NamesOf is what pairs them.
	public static readonly (PadNavAction Action, string Xbox, string Ps4)[] Controls = {
		(PadNavAction.Up, "Up", "DPad Up"),
		(PadNavAction.Down, "Down", "DPad Down"),
		(PadNavAction.Left, "Left", "DPad Left"),
		(PadNavAction.Right, "Right", "DPad Right"),
		(PadNavAction.Confirm, "A", "But2"),
		(PadNavAction.Back, "B", "But3")
	};

	//The names to ask the host for one control, the family's own spelling first
	//and the other family's second, because which spelling a host defines is the
	//*backend's* business and not the pad's: macOS and Linux name every pad "Pad"
	//(GameController and evdev devices), DirectInput names joysticks "Joy", and
	//Windows defines both. Asking only the family's own spelling meant a
	//DualShock read as Ps4 - the preset the first run applies - resolved to
	//nothing on a Mac and the menu simply did not move. Asking for the other
	//spelling afterwards is not the cross-family guess Core's pad rule refuses:
	//there is no button byte being reinterpreted here, only a second name for the
	//same control, and the host answers 0 for a name it does not define.
	public static IReadOnlyList<string> NamesOf(PadFamily family, int device, PadNavAction action)
	{
		string pad = "Pad" + (device + 1).ToString() + " ";
		string joy = "Joy" + (device + 1).ToString() + " ";
		foreach((PadNavAction owned, string xbox, string ps4) in Controls) {
			if(owned != action) {
				continue;
			}
			return family == PadFamily.Xbox
				? new[] { pad + xbox, joy + ps4 }
				: new[] { joy + ps4, pad + xbox };
		}
		return Array.Empty<string>();
	}

	//The sheet's own controls, by the name the pad carries on its plastic. Read
	//off KeyPresets the same way Controls is, and off the same lines: the Xbox
	//layout puts the pad's top face button on Y, and the PS4 layout puts the
	//triangle on But4. A player reading "Y Search" on the sheet presses the
	//button the pad prints Y on, whichever preset the first run applied.
	public static readonly (PadSheetControl Control, string Xbox, string Ps4)[] SheetControls = {
		(PadSheetControl.Search, "Y", "But4")
	};

	//The names to ask the host for one sheet control, the family's own spelling
	//first - the same rule, and the same reason, as NamesOf above.
	public static IReadOnlyList<string> SheetNamesOf(PadFamily family, int device, PadSheetControl control)
	{
		string pad = "Pad" + (device + 1).ToString() + " ";
		string joy = "Joy" + (device + 1).ToString() + " ";
		foreach((PadSheetControl owned, string xbox, string ps4) in SheetControls) {
			if(owned != control) {
				continue;
			}
			return family == PadFamily.Xbox
				? new[] { pad + xbox, joy + ps4 }
				: new[] { joy + ps4, pad + xbox };
		}
		return Array.Empty<string>();
	}
}
