using System;

namespace Mesen.Logic;

//ADR-0256 (accepted 2026-10-04) Decisions 2 and 5: the pad bridge has to know
//which pad a pressed key came from - to navigate with the pad in the player's
//hand (PadInHand) and to resolve the codes that pad's preset binds
//(PadNavControls.Resolve). Nothing in the host answers that from the code
//alone, which is the trap this file exists to close: each family numbers its
//own block from its own base, so 0x2000 is "Joy1" to Windows' DirectInput and
//"Pad17" to a backend that numbers pads past four - macOS and Linux name every
//pad "Pad{N}" (MacOSKeyManager: 20 of them) and have no "Joy" name at all. A
//code read as (code - BaseGamepadIndex) >> 8 therefore answers 16 for a pad the
//player calls the first one, and the bridge would watch the wrong device's
//buttons.
//
//The answer comes from the backend, which is why PadInHand.OnPressed takes a
//probe rather than deriving it: InputApi.GetKeyName is the backend's own name
//for the code, and the name is exactly the family the presets are written in -
//"Pad1 A", "Joy1 But2". This is the same reason
//Core/Shared/ShortcutKeyRules.h has its PadFamilies supplied by the caller.
public static class PadNaming
{
	private const string PadPrefix = "Pad";
	private const string JoyPrefix = "Joy";

	//Which pad sent this code, or null for a key no pad sent. Null is a real
	//answer: the keyboard, the mouse, a code this build's backend has no name
	//for (a pad key name that is not on this platform - "Joy1 But2" does not
	//exist where DirectInput does not), or one of the shared keyboard names.
	public static PadId? Of(ushort keyCode, Func<ushort, string> keyName)
	{
		return Parse(keyName(keyCode));
	}

	//The name the platform's key manager gave a code, read back into the pad it
	//belongs to. The prefix is the family - "Pad" is the XInput-shaped button
	//table, "Joy" the DirectInput one, and PadFamily mirrors exactly that pair -
	//and the number is 1-based in every backend, so device 0 is "Pad1".
	public static PadId? Parse(string name)
	{
		PadFamily family;
		string rest;
		if(name.StartsWith(PadPrefix, StringComparison.Ordinal)) {
			family = PadFamily.Xbox;
			rest = name.Substring(PadPrefix.Length);
		} else if(name.StartsWith(JoyPrefix, StringComparison.Ordinal)) {
			family = PadFamily.Ps4;
			rest = name.Substring(JoyPrefix.Length);
		} else {
			return null;
		}

		//Digits, then the name's own separator ("Pad1 A", "Joy12 DPad Up"). A
		//keyboard name is never shaped like this - KeyDefinition's shared table
		//has no name starting with either prefix - and a name that only looks
		//like one is not a pad, so anything else is null rather than a guess.
		int digits = 0;
		while(digits < rest.Length && char.IsAsciiDigit(rest[digits])) {
			digits++;
		}
		if(digits == 0 || digits >= rest.Length || rest[digits] != ' ') {
			return null;
		}
		if(!int.TryParse(rest.AsSpan(0, digits), out int number) || number < 1) {
			return null;
		}
		return new PadId(number - 1, family);
	}
}
