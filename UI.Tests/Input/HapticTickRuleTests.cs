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
	}
}
