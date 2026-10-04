using Mesen.Interop;
using Mesen.Logic;
using System;
using System.Text.Json.Serialization;

namespace Mesen.Config.Shortcuts
{
	//ADR-0255 slice 4 ("Extender o ShortcutKeyInfo"): the slot a shortcut's
	//spare pad control lives in, so a shortcut may carry a *button and no key* -
	//the case the ADR names. The two KeyCombination slots stay what they are (a
	//keyboard combination, with a legacy default or two holding a pad button);
	//this is where a control that is not a key goes, and the two coexist: a
	//shortcut can hold a key and a spare button at once.
	//
	//One code, not a KeyCombination: a spare control is a single press, and the
	//pad's code already carries both the device and the button
	//(IKeyManager::BaseGamepadIndex + device * 0x100 + button), which is what
	//the core's ShortcutKeyRules reads to answer the press on whichever pad is
	//in the player's hands (ADR-0256 Decision 5).
	public class PadShortcutBinding
	{
		public UInt16 KeyCode { get; set; }

		//Null is "the player never moved it": PadAxisAction's default (40%, what
		//the backends already do at the default deadzone) applies, and the key is
		//dropped when writing, so a plain button is stored as {"KeyCode": N} -
		//a threshold with nothing to threshold is a field that lies about being
		//in use.
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? ThresholdPercent { get; set; }

		public bool IsEmpty => KeyCode == 0;

		//The threshold that governs, whether or not the player ever set one.
		public int EffectiveThresholdPercent => PadAxisAction.ClampPercent(ThresholdPercent ?? PadAxisAction.DefaultThresholdPercent);

		//Only an axis direction ("Pad1 X+") is analog, and only there does the
		//threshold have anything to say.
		public bool IsAxis => !IsEmpty && PadAxisAction.IsAxisDirectionName(InputApi.GetKeyName(KeyCode));

		//The same binding in the shape the rest of the shortcut code speaks, so
		//the slot can be pushed, compared and displayed without a second path.
		public KeyCombination ToKeyCombination()
		{
			return new KeyCombination() { Key1 = KeyCode };
		}
	}
}
