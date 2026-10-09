using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Input
{
	//#1106: the host-free copy of the core's IKeyManager::IsAimable rule plus the
	//user's enablement switch - a tick is sent only to a pad the host can address
	//on its own, and only while the feature is on.
	public class HapticTickRuleTests
	{
		[Theory]
		[InlineData(GamepadBackend.XInput, true, true)]
		[InlineData(GamepadBackend.Evdev, true, true)]
		[InlineData(GamepadBackend.GameController, true, true)]
		[InlineData(GamepadBackend.XInput, false, false)] //no haptics reported
		[InlineData(GamepadBackend.DirectInput, true, false)] //never addressable
		[InlineData(GamepadBackend.None, true, false)]
		public void IsAimable_NeedsAnAddressableBackendThatReportsHaptics(GamepadBackend backend, bool hasRumble, bool expected)
		{
			Assert.Equal(expected, HapticTickRule.IsAimable(backend, hasRumble));
		}

		[Theory]
		[InlineData(true, true, true)]
		[InlineData(false, true, false)] //switched off
		[InlineData(true, false, false)] //not aimable
		[InlineData(false, false, false)]
		public void ShouldTick_NeedsTheSwitchAndAnAimablePad(bool enabled, bool aimable, bool expected)
		{
			Assert.Equal(expected, HapticTickRule.ShouldTick(enabled, aimable));
		}
	}
}
