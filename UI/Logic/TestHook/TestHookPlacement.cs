using System;

namespace Mesen.Logic.TestHook
{
	//#1255: a GUI test run's window belongs on the PRIMARY display - the built-in
	//one on a laptop - and never on an external monitor, so a person running tests
	//sees the run on the screen they are looking at. This is the host-free half of
	//that rule: rectangles as [x, y, width, height], which is what the hook reports
	//to the adapter and what the adapter checks a run against. The window-bound
	//half (reading a window's rect and its display, moving it) is in
	//UI/Windows/TestHookWiring.cs, because it needs Avalonia.
	//
	//The one escape hatch is the environment variable MESEN_GUI_WINDOW: the exact
	//value "any" means "do not enforce placement", for a CI, Linux or headless
	//runner where no primary display applies. Unset (or anything but "any") means
	//primary-display placement. The adapter reads the same variable and refuses a
	//launch whose window is outside the primary display's bounds.
	public static class TestHookPlacement
	{
		public const string EnvironmentVariable = "MESEN_GUI_WINDOW";
		public const string Any = "any";

		//NSApplicationActivationPolicyAccessory: the process may show windows but is
		//not in the Dock and never becomes the frontmost application, which is what
		//keeps a run from stealing the keyboard of whoever is working.
		public const int AccessoryPolicy = 1;

		public static bool IsAny(string? value) => value == Any;

		//AppKit's activation policy only means something to a process that really
		//has an NSWindow behind it: a headless (or non-macOS) process must not have
		//its own activation policy retargeted.
		public static bool NeedsActivationPolicy(bool isMacOS, string? platformDescriptor)
			=> isMacOS && platformDescriptor == "NSWindow";

		public static int[] Rect(int x, int y, int width, int height) => new[] { x, y, width, height };

		//True when rect sits entirely inside area: a window half on a second
		//display is not "inside the primary display".
		public static bool Contains(int[] area, int[] rect)
			=> rect[0] >= area[0] && rect[1] >= area[1]
				&& rect[0] + rect[2] <= area[0] + area[2]
				&& rect[1] + rect[3] <= area[1] + area[3];

		//The nearest position inside area, keeping the window's size. A window
		//larger than the area is pinned to the area's origin instead of throwing.
		public static int[] Clamp(int[] area, int[] rect)
		{
			int x = Math.Clamp(rect[0], area[0], Math.Max(area[0], area[0] + area[2] - rect[2]));
			int y = Math.Clamp(rect[1], area[1], Math.Max(area[1], area[1] + area[3] - rect[3]));
			return Rect(x, y, rect[2], rect[3]);
		}
	}
}
