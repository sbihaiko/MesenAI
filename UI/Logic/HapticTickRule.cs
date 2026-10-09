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

		//#1112: the focus-move tick. Never with Rumble at 0 (the row is disabled
		//then), and never over a game that is running: the pad belongs to the
		//console (ADR-0256 Decision 1) and a cartridge's own rumble owns the motor.
		public static bool ShouldTickOnMove(bool enabled, uint rumble, bool gameRunningUnpaused, uint padIndex, Func<uint, bool> isAimable)
		{
			return rumble > 0 && !gameRunningUnpaused && ShouldTick(enabled, padIndex, isAimable);
		}
	}
}
