using System;

namespace Mesen.Logic
{
	//#1106 (spec #1102), the host-free rule: a haptic tick goes to ONE pad, and only
	//while the user's switch is on and the core says that pad is aimable. The
	//aimable answer is the core's alone (IKeyManager::IsAimable); it is taken as a
	//delegate so the GUI passes InputApi.IsGamepadAimable and tests pass a stub.
	public static class HapticTickRule
	{
		public static bool ShouldTick(bool enabled, uint padIndex, Func<uint, bool> isAimable)
		{
			return enabled && isAimable(padIndex);
		}
	}
}
