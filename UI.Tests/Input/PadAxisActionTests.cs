using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Input
{
	//ADR-0255, third answer: an axis direction carries a digital action past a
	//threshold the player sets. The rule is host-free (UI/Logic/PadAxisAction);
	//what it has to get right is the *default* - 40% is what macOS, Linux and
	//Windows' DirectInput already do at the default deadzone size, so a binding
	//that never names a threshold must not change how the pad behaves today.
	public class PadAxisActionTests
	{
		[Fact]
		public void DefaultThreshold_IsTheBackendsOwnFortyPercent()
		{
			//At ControllerDeadzoneSize 2 (the default) the ratio is 1, and every
			//backend compares a normalized -1..1 axis against 0.4 - the same point
			//as this rule's 40% of int16 full travel.
			Assert.Equal(40, PadAxisAction.DefaultThresholdPercent);
			Assert.Equal(13107, PadAxisAction.ThresholdUnits(PadAxisAction.DefaultThresholdPercent));
		}

		[Fact]
		public void ThresholdUnits_ScalesToInt16FullTravel()
		{
			//macOS: INT16_MAX * value; Linux: (ratio - 0.5) * 2 * INT16_MAX. Full
			//travel is short.MaxValue on both, so 100% is that value.
			Assert.Equal(32767, PadAxisAction.ThresholdUnits(100));
			Assert.Equal(24575, PadAxisAction.ThresholdUnits(75));
			Assert.Equal(13107, PadAxisAction.ThresholdUnits(40));
			Assert.Equal(328, PadAxisAction.ThresholdUnits(1));
		}

		[Fact]
		public void ThresholdUnits_ClampsOutOfRangePercentages()
		{
			//A malformed config entry cannot produce a threshold off the axis: 0
			//would read as held at rest, and past 100% could never be reached.
			Assert.Equal(328, PadAxisAction.ThresholdUnits(0));
			Assert.Equal(328, PadAxisAction.ThresholdUnits(-50));
			Assert.Equal(32767, PadAxisAction.ThresholdUnits(101));
			Assert.Equal(32767, PadAxisAction.ThresholdUnits(int.MaxValue));
		}

		[Fact]
		public void ClampPercent_Boundaries()
		{
			Assert.Equal(1, PadAxisAction.ClampPercent(1));
			Assert.Equal(40, PadAxisAction.ClampPercent(40));
			Assert.Equal(100, PadAxisAction.ClampPercent(100));
			Assert.Equal(1, PadAxisAction.ClampPercent(0));
			Assert.Equal(100, PadAxisAction.ClampPercent(101));
		}

		[Fact]
		public void IsTriggered_AtTheThresholdOrPastIt()
		{
			//The backends compare strictly (`value > 0.4` on a normalized axis),
			//which after rounding to int16 is exactly `>= 13107` - so the default
			//threshold reproduces the platform's own behaviour, not an
			//approximation of it.
			Assert.True(PadAxisAction.IsTriggered(13107, 40));
			Assert.True(PadAxisAction.IsTriggered(13108, 40));
			Assert.False(PadAxisAction.IsTriggered(13106, 40));
		}

		[Fact]
		public void IsTriggered_IsMagnitudeOnly_BothDirections()
		{
			//The direction lives in the bound code ("Pad1 X+" vs "Pad1 X-"), which
			//the backend applies when it derives the press, so the rule reads the
			//magnitude and nothing else.
			Assert.True(PadAxisAction.IsTriggered(-13107, 40));
			Assert.True(PadAxisAction.IsTriggered(short.MinValue, 100));
			Assert.Equal(PadAxisAction.IsTriggered(20000, 60), PadAxisAction.IsTriggered(-20000, 60));
		}

		[Fact]
		public void IsTriggered_RestingStickIsNeverPressed()
		{
			Assert.False(PadAxisAction.IsTriggered(0, 1));
			Assert.False(PadAxisAction.IsTriggered(1, 1));
			Assert.False(PadAxisAction.IsTriggered(-1, 1));
		}

		[Fact]
		public void IsTriggered_HigherThresholdNeedsMoreTravel()
		{
			//The player's setting is the whole point: 50% of travel clears the
			//default and not a threshold set to 80%.
			Assert.True(PadAxisAction.IsTriggered(16384, 40));
			Assert.False(PadAxisAction.IsTriggered(16384, 80));
			Assert.True(PadAxisAction.IsTriggered(26214, 80));
		}

		[Fact]
		public void IsAxisDirectionName_RecognizesEveryBackendsDirectionKeys()
		{
			//Linux and macOS: "PadN" + X/X2/Y/Y2/Z/Z2 with a +/- suffix.
			Assert.True(PadAxisAction.IsAxisDirectionName("Pad1 X+"));
			Assert.True(PadAxisAction.IsAxisDirectionName("Pad1 X-"));
			Assert.True(PadAxisAction.IsAxisDirectionName("Pad4 Y2-"));
			//Windows' DirectInput joysticks name their own from "JoyN".
			Assert.True(PadAxisAction.IsAxisDirectionName("Joy1 Y2-"));
			Assert.True(PadAxisAction.IsAxisDirectionName("Joy16 Z+"));
		}

		[Fact]
		public void IsAxisDirectionName_RejectsButtonsAndAnalogAxisNames()
		{
			Assert.False(PadAxisAction.IsAxisDirectionName("Pad1 A"));
			Assert.False(PadAxisAction.IsAxisDirectionName("Pad1 But3"));
			Assert.False(PadAxisAction.IsAxisDirectionName("Pad1 Select"));
			//An analog axis name has no direction suffix: the host exposes it for
			//GetAxisPosition only and never reports it as a pressed key.
			Assert.False(PadAxisAction.IsAxisDirectionName("Pad1 X"));
			Assert.False(PadAxisAction.IsAxisDirectionName("Joy1 Y2"));
		}

		[Fact]
		public void IsAxisDirectionName_RejectsKeyboardKeys()
		{
			//The keyboard has a literal "-" key, and the minus/plus keys must not
			//be read as stick directions.
			Assert.False(PadAxisAction.IsAxisDirectionName("-"));
			Assert.False(PadAxisAction.IsAxisDirectionName("+"));
			Assert.False(PadAxisAction.IsAxisDirectionName("NumPad -"));
			Assert.False(PadAxisAction.IsAxisDirectionName("PadX X+"));
			Assert.False(PadAxisAction.IsAxisDirectionName(""));
			Assert.False(PadAxisAction.IsAxisDirectionName(null));
		}
	}
}
