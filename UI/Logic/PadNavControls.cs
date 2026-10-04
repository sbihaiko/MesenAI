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

	private static ushort? Code(PadFamily family, int device, PadNavAction action, Func<string, ushort> keyCode)
	{
		ushort code = keyCode(NameOf(family, device, action));
		return code == 0 ? null : code;
	}

	//The control's own name on the pad, as the preset writes it. Confirm and
	//back are the pad's affirmative and cancel buttons as the player reads them
	//off the plastic - the Xbox pad's A and B, the DualShock's cross and circle -
	//which is why back is "Pad1 B" on one desk and "Joy1 But3" on another.
	//
	//Read off KeyPresets, not mirrored from it: that file puts the *console's* A
	//on the pad's right-hand button (the Xbox B, DirectInput But3), which is the
	//opposite of what a menu wants. A console's jump is not a menu's yes, and a
	//preset is the wrong place to read one off the other.
	private static string NameOf(PadFamily family, int device, PadNavAction action)
	{
		string pad = "Pad" + (device + 1).ToString() + " ";
		string joy = "Joy" + (device + 1).ToString() + " ";
		return (family, action) switch {
			(PadFamily.Xbox, PadNavAction.Up) => pad + "Up",
			(PadFamily.Xbox, PadNavAction.Down) => pad + "Down",
			(PadFamily.Xbox, PadNavAction.Left) => pad + "Left",
			(PadFamily.Xbox, PadNavAction.Right) => pad + "Right",
			(PadFamily.Xbox, PadNavAction.Confirm) => pad + "A",
			(PadFamily.Xbox, PadNavAction.Back) => pad + "B",
			(_, PadNavAction.Up) => joy + "DPad Up",
			(_, PadNavAction.Down) => joy + "DPad Down",
			(_, PadNavAction.Left) => joy + "DPad Left",
			(_, PadNavAction.Right) => joy + "DPad Right",
			(_, PadNavAction.Confirm) => joy + "But2",
			_ => joy + "But3"
		};
	}
}
