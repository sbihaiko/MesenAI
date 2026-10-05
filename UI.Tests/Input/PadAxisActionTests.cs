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

		//The rule behind "zero behaviour change for anyone who has not bound an
		//axis" (ADR-0255 slice 4): the host replaces the magnitude it compares an
		//axis direction against only when the core's table has a threshold for that
		//direction, and a table entry exists only for a direction a shortcut's
		//PadBinding actually names. Without one, whatever expression the backend
		//derived from the deadzone setting comes back exactly as it went in - so a
		//config that never used this feature cannot be affected by it.
		[Theory]
		[InlineData(null, 0.4)]
		[InlineData(0, 0.4)]
		[InlineData(null, 0.75)]
		[InlineData(0, 1.5)]
		public void ThresholdRatio_WithoutABindingKeepsTheHostsOwnRatio(int? units, double hostRatio)
		{
			Assert.Equal(hostRatio, PadAxisAction.ThresholdRatio(units, hostRatio));
		}

		[Fact]
		public void ThresholdRatio_WithABindingIsTheThresholdsFractionOfTravel()
		{
			//100% is full travel (short.MaxValue), so the fraction is the same
			//number whatever the host's own ratio was - the deadzone setting stops
			//governing this one direction and governs every other one still. The
			//fraction is a division of the stored integer units, hence the
			//tolerance: 40% is 13107 of 32767, which is 0.400006 and not 0.4.
			Assert.Equal(1.0, PadAxisAction.ThresholdRatio(PadAxisAction.ThresholdUnits(100), 0.4), 1e-4);
			Assert.Equal(0.4, PadAxisAction.ThresholdRatio(PadAxisAction.ThresholdUnits(40), 1.5), 1e-4);
			Assert.Equal(0.75, PadAxisAction.ThresholdRatio(PadAxisAction.ThresholdUnits(75), 0.2), 1e-4);
		}

		[Fact]
		public void ThresholdRatio_AtTheDefaultIsTheBackendsOwnDefaultRatio()
		{
			//The one case where the two agree, and why a binding that never names a
			//number behaves as the pad always did at the default deadzone size.
			Assert.Equal(0.4, PadAxisAction.ThresholdRatio(PadAxisAction.ThresholdUnits(40), 0.4), 1e-4);
		}

		//The key the core's axis-threshold table is stored under: the direction with
		//its device cleared, so a threshold belongs to the direction and not to the
		//pad that happened to be in the player's hands when it was set (ADR-0256
		//Decision 5).
		[Fact]
		public void DirectionKey_ClearsTheDeviceAndKeepsTheButton()
		{
			//"Pad1 X+" and "Pad3 X+" are the same direction: one threshold governs
			//both, and a binding made on one pad still fires on another.
			Assert.Equal(PadAxisAction.DirectionKey(PadKey(0, 16), "Pad1 X+"), PadAxisAction.DirectionKey(PadKey(2, 16), "Pad3 X+"));
			Assert.Equal((ushort)(ControllerDevices.BaseGamepadIndex + 16), PadAxisAction.DirectionKey(PadKey(2, 16), "Pad3 X+"));
		}

		[Fact]
		public void DirectionKey_KeepsTheTwoPadFamiliesApart()
		{
			//Windows' DirectInput joysticks number their own directions from their
			//own base ("Joy1 Y-"), so the same button byte in the two families must
			//not share a threshold.
			Assert.Equal((ushort)(ControllerDevices.BaseDirectInputIndex + 0), PadAxisAction.DirectionKey((ushort)ControllerDevices.BaseDirectInputIndex, "Joy1 Y+"));
			Assert.NotEqual(
				PadAxisAction.DirectionKey((ushort)ControllerDevices.BaseDirectInputIndex, "Joy1 Y+"),
				PadAxisAction.DirectionKey((ushort)ControllerDevices.BaseGamepadIndex, "Pad1 X+"));
		}

		private static ushort PadKey(int device, int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);
	}
}
