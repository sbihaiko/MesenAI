using System;

namespace Mesen.Logic.TestHook
{
	//#1255: a GUI test run's window belongs on the PRIMARY display - the built-in
	//one on a laptop - and never on an external monitor, so a person running tests
	//sees the run on the screen they are looking at instead of having a window pop
	//up on the monitor beside them. This is the host-free half of that rule:
	//rectangles as [x, y, width, height], the shape the hook reports to the adapter
	//and the adapter checks a run against. The window-bound half (reading a window's
	//rect, the machine's primary display, setting ShowActivated, asking AppKit) is
	//in UI/Windows, because it needs Avalonia.
	//
	//The one escape hatch is the environment variable MESEN_GUI_WINDOW: the exact
	//value "any" means "do not place me", for a CI, Linux or headless runner where
	//there is no primary display to speak of. Unset (or "primary") means
	//primary-display placement. The adapter reads the same variable and refuses a
	//launch whose window is outside the primary display's bounds.
	public static class TestHookPlacement
	{
		public const string EnvironmentVariable = "MESEN_GUI_WINDOW";
		public const string Any = "any";

		//NSApplicationActivationPolicyAccessory: the process may show windows but is
		//not in the Dock and never becomes the frontmost application, which is what
		//keeps a run from taking the keyboard away from whoever is working.
		public const int AccessoryPolicy = 1;

		public static bool IsAny(string? value) => value == Any;

		//AppKit's activation policy only means something to a process that really
		//has a window behind it: a headless or non-macOS process must not have its
		//own policy retargeted (that is what would drop a person's emulator out of
		//the Dock). Both halves are what the caller observes, never an assumption.
		public static bool NeedsActivationPolicy(bool isMacOS, bool hasPlatformWindow) => isMacOS && hasPlatformWindow;

		public static int[] Rect(int x, int y, int width, int height) => new[] { x, y, width, height };

		//True when rect sits entirely inside area: a window half on a second display
		//is not "inside the primary display".
		public static bool Contains(int[] area, int[] rect)
			=> rect[0] >= area[0] && rect[1] >= area[1]
				&& rect[0] + rect[2] <= area[0] + area[2]
				&& rect[1] + rect[3] <= area[1] + area[3];

		//The nearest position inside area, keeping the window's size. A window larger
		//than the area is pinned to the area's origin rather than thrown at.
		public static int[] Clamp(int[] area, int[] rect)
			=> Rect(
				Math.Clamp(rect[0], area[0], Math.Max(area[0], area[0] + area[2] - rect[2])),
				Math.Clamp(rect[1], area[1], Math.Max(area[1], area[1] + area[3] - rect[3])),
				rect[2], rect[3]);
	}
}
