using System;

namespace Mesen.Logic
{
	//ADR-0255, third answer ("Sim, com um limiar"): a stick direction may carry
	//a digital action, and the point where it counts as pressed is the player's
	//to pick. This file is the rule; the picked number travels with the binding
	//(Config/Shortcuts/PadShortcutBinding.ThresholdPercent).
	//
	//Why a rule is needed when every backend already makes a direction digital:
	//they all turn one into a key of its own - macOS compares against
	//GetControllerDeadzoneRatio() * 0.4, Linux against the same 0.400 * ratio
	//(CheckAxis), and Windows' DirectInput names the result "Joy1 Y2-" - so
	//"Pad1 X+" is bindable today. What none of them reads is the player's
	//setting: their threshold *is* the emulator's deadzone size, so a direction
	//bound to Rewind fires at 20% of travel with deadzone 0 and at 60% with
	//deadzone 4, and nothing can say "this direction, at 80%".
	//
	//Host-free (BCL only) so UI.Tests can assert it - see
	//UI.Tests/Input/PadAxisActionTests.
	public static class PadAxisAction
	{
		//40% of full travel is what every backend already uses at the default
		//ControllerDeadzoneSize (2 -> ratio 1), so a binding that never names a
		//threshold keeps behaving exactly as it does today.
		public const int DefaultThresholdPercent = 40;

		//1, not 0: a zero threshold is not "a short press", it is a direction that
		//reads as held while the stick rests - a stuck button with no way to let
		//go, and the player has no keyboard-free way back from it.
		public const int MinThresholdPercent = 1;
		public const int MaxThresholdPercent = 100;

		public static int ClampPercent(int thresholdPercent)
		{
			if(thresholdPercent < MinThresholdPercent) {
				return MinThresholdPercent;
			}
			if(thresholdPercent > MaxThresholdPercent) {
				return MaxThresholdPercent;
			}
			return thresholdPercent;
		}

		//The threshold in the units the host reports an axis in: every backend
		//scales a full deflection to +/-short.MaxValue (macOS: INT16_MAX * value;
		//Linux: (ratio - 0.5) * 2 * INT16_MAX), so 100% is full travel and the
		//default 40% is 13107.
		public static int ThresholdUnits(int thresholdPercent)
		{
			return (int)Math.Round(ClampPercent(thresholdPercent) / 100.0 * short.MaxValue, MidpointRounding.AwayFromZero);
		}

		//Which way the stick was pushed is deliberately not asked here: it is
		//already in the code the player bound ("Pad1 X+" and "Pad1 X-" are
		//different keys) and the backend applies it when deriving the press. The
		//value compared is the raw axis reading, whose sign convention belongs to
		//the backend (Linux flips it per axis, for the tilt sensors), so a
		//`positive` flag here would be a rule invented on top of a convention
		//this layer cannot see.
		public static bool IsTriggered(short axisValue, int thresholdPercent)
		{
			int magnitude = axisValue < 0 ? -(int)axisValue : axisValue;
			return magnitude >= ThresholdUnits(thresholdPercent);
		}

		//Whether a threshold means anything for a bound control: only an axis
		//*direction* is analog. Read off the host's name, the one cross-platform
		//signal there is - "Pad1 X+", "Pad1 X-" (Linux, macOS), "Joy1 Y2-"
		//(Windows' DirectInput) - and the pad prefix is required so the
		//keyboard's own "-" key is not mistaken for one.
		//
		//An analog axis name *without* a direction ("Pad1 X", "Joy1 Y2") is not
		//one: the host exposes it for GetAxisPosition only and never reports it as
		//a pressed key, so it cannot carry an action either way.
		public static bool IsAxisDirectionName(string? keyName)
		{
			if(string.IsNullOrEmpty(keyName) || !PlayMenuHint.IsControllerKey(keyName)) {
				return false;
			}
			char last = keyName[keyName.Length - 1];
			return last == '+' || last == '-';
		}
	}
}
