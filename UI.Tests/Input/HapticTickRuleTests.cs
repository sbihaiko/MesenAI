using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Input
{
	//#1106: a tick is sent only while the user's switch is on and the core answers
	//that pad is aimable. The core's answer is stubbed both ways, so the rule can
	//fail if it stops following it.
	public class HapticTickRuleTests
	{
		[Theory]
		[InlineData(true, true, true)]
		[InlineData(true, false, false)] //the core says not aimable
		[InlineData(false, true, false)] //switched off
		[InlineData(false, false, false)]
		public void ShouldTick_NeedsTheSwitchAndTheCoresAimableAnswer(bool enabled, bool coreAnswer, bool expected)
		{
			Assert.Equal(expected, HapticTickRule.ShouldTick(enabled, 3, index => index == 3 && coreAnswer));
		}

		[Fact]
		public void ShouldTick_AsksTheCoreAboutTheSamePad()
		{
			uint asked = 0;
			HapticTickRule.ShouldTick(true, 7, index => { asked = index; return true; });
			Assert.Equal(7u, asked);
		}

		[Fact]
		public void ShouldTick_DoesNotAskTheCoreWhileSwitchedOff()
		{
			bool asked = false;
			Assert.False(HapticTickRule.ShouldTick(false, 0, _ => { asked = true; return true; }));
			Assert.False(asked);
		}

		//#1112: the focus-move tick adds three conditions to the switch and the
		//core's answer - Rumble above 0, and no game running unpaused (the pad is
		//the console's then, and a cartridge's own rumble owns the motor).
		[Theory]
		[InlineData(true, 5u, false, true, true)]
		[InlineData(false, 5u, false, true, false)] //the switch is off
		[InlineData(true, 0u, false, true, false)] //Rumble is 0
		[InlineData(true, 5u, true, true, false)] //a game runs unpaused
		[InlineData(true, 5u, false, false, false)] //the core says not aimable
		public void ShouldTickOnMove_NeedsSwitchRumbleNoRunningGameAndAnAimablePad(bool enabled, uint rumble, bool gameRunningUnpaused, bool coreAnswer, bool expected)
		{
			Assert.Equal(expected, HapticTickRule.ShouldTickOnMove(enabled, rumble, gameRunningUnpaused, 2, index => index == 2 && coreAnswer));
		}

		[Fact]
		public void ShouldTickOnMove_DoesNotAskTheCoreWhenAnEarlierConditionFails()
		{
			bool asked = false;
			HapticTickRule.ShouldTickOnMove(true, 5, true, 0, _ => { asked = true; return true; });
			Assert.False(asked);
		}
	}
}
