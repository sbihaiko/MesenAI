using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.HeadlessTests;

//#1106: the InputApi delegates reach the core's IsGamepadAimable/TickGamepad.
//Index 0xFFFFFF is past any connected pad, so the core answers false for both
//without touching hardware; the host-free rule (HapticTickRule) is pinned in
//UI.Tests/Input/HapticTickRuleTests. Needs the native core, so it self-skips
//on the core-less runner.
[Collection(NativeCoreCollection.Name)]
public class HapticTickInputApiTests
{
	private const uint NoSuchPad = 0xFFFFFF;

	[Fact]
	public void IsGamepadAimable_IsFalseForAPadThatIsNotConnected()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Assert.False(InputApi.IsGamepadAimable(NoSuchPad));
	}

	[Fact]
	public void TickGamepad_IsFalseForAPadThatIsNotConnected()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Assert.False(InputApi.TickGamepad(NoSuchPad));
	}

	[Fact]
	public void ShouldTick_FollowsTheCoreAimableAnswer()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Assert.False(HapticTickRule.ShouldTick(true, InputApi.IsGamepadAimable(NoSuchPad)));
	}
}
