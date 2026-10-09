using Mesen.Interop;

namespace Mesen.Logic
{
	//#1106 (spec #1102), the host-free rule: a haptic tick targets ONE pad, so it
	//only goes to a pad whose backend addresses a single device and that reports
	//haptics. Mirrors IKeyManager::IsAimable in Core; DirectInput and a pad with
	//no backend are never aimable.
	public static class HapticTickRule
	{
		public static bool IsAimable(GamepadBackend backend, bool hasRumble)
		{
			switch(backend) {
				case GamepadBackend.XInput:
				case GamepadBackend.Evdev:
				case GamepadBackend.GameController:
					return hasRumble;
				default:
					return false;
			}
		}

		public static bool ShouldTick(bool enabled, bool aimable)
		{
			return enabled && aimable;
		}
	}
}
