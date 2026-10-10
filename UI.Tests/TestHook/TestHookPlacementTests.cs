using Mesen.Logic.TestHook;
using Xunit;

namespace Mesen.Tests.TestHook
{
	//#1255: a GUI test run's windows belong on the primary (built-in) display, not
	//on whichever monitor the OS picks. The rule is host-free geometry - a display
	//area and a window rect, both as [x, y, width, height] - so it is pinned here;
	//that the real window is reported with its own position and that a dialog
	//opening off the primary display fails its step is pinned in UI.HeadlessTests
	//(GuiTestHookTests) and in the adapter's own suite
	//(scripts/test_gui_test_mesen_adapter.py).
	public class TestHookPlacementTests
	{
		//A 1512x982 built-in display at the origin, its working area 25px shorter
		//because of the menu bar - the shape a macOS laptop reports.
		private static readonly int[] Primary = { 0, 0, 1512, 982 };
		private static readonly int[] WorkingArea = { 0, 25, 1512, 945 };
		private static readonly int[] MainWindow = { 40, 60, 1100, 700 };

		[Fact]
		public void A_window_inside_the_area_is_inside()
		{
			Assert.True(TestHookPlacement.Contains(Primary, MainWindow));
		}

		[Fact]
		public void A_window_touching_the_far_corner_is_still_inside()
		{
			Assert.True(TestHookPlacement.Contains(Primary, new[] { 412, 282, 1100, 700 }));
		}

		//An external monitor to the left of the built-in one: a window the OS
		//placed there has a negative x, which is not the built-in display.
		[Fact]
		public void A_window_on_a_display_to_the_left_is_outside()
		{
			Assert.False(TestHookPlacement.Contains(Primary, new[] { -1200, 60, 1100, 700 }));
		}

		[Fact]
		public void A_window_on_a_display_above_is_outside()
		{
			Assert.False(TestHookPlacement.Contains(Primary, new[] { 40, -1080, 1100, 700 }));
		}

		[Fact]
		public void A_window_half_off_the_right_edge_is_outside()
		{
			Assert.False(TestHookPlacement.Contains(Primary, new[] { 900, 60, 1100, 700 }));
		}

		[Fact]
		public void A_window_reaching_below_the_bottom_edge_is_outside()
		{
			Assert.False(TestHookPlacement.Contains(Primary, new[] { 40, 400, 1100, 700 }));
		}

		//A display whose working area does not start at the origin (a screen the
		//hook reports with a shifted origin) holds a window placed inside it.
		[Fact]
		public void An_area_with_a_shifted_origin_holds_its_own_windows()
		{
			Assert.True(TestHookPlacement.Contains(new[] { 1512, 0, 1280, 1024 }, new[] { 1600, 100, 1100, 700 }));
		}

		[Fact]
		public void Clamping_leaves_a_window_inside_the_area_alone()
		{
			Assert.Equal(MainWindow, TestHookPlacement.Clamp(WorkingArea, MainWindow));
		}

		[Fact]
		public void Clamping_moves_a_window_on_another_display_into_the_area()
		{
			Assert.Equal(new[] { 0, 60, 1100, 700 }, TestHookPlacement.Clamp(WorkingArea, new[] { -1200, 60, 1100, 700 }));
		}

		[Fact]
		public void Clamping_moves_a_window_hanging_over_the_bottom_edge_up()
		{
			//The working area ends at y=970 (25 + 945), so a 700-tall window stops at 270.
			Assert.Equal(new[] { 40, 270, 1100, 700 }, TestHookPlacement.Clamp(WorkingArea, new[] { 40, 400, 1100, 700 }));
		}

		[Fact]
		public void Clamping_pins_a_window_wider_than_the_area_to_its_origin()
		{
			//Nothing bigger than the area fits inside it, so both axes stop at the area's origin.
			Assert.Equal(new[] { 0, 25, 2000, 700 }, TestHookPlacement.Clamp(WorkingArea, new[] { -30, -10, 2000, 700 }));
		}

		//The escape hatch is the exact value "any" - nothing else turns placement off.
		[Fact]
		public void Only_the_exact_value_any_turns_placement_off()
		{
			Assert.True(TestHookPlacement.IsAny("any"));
			Assert.False(TestHookPlacement.IsAny(null));
			Assert.False(TestHookPlacement.IsAny(""));
			Assert.False(TestHookPlacement.IsAny("ANY"));
			Assert.False(TestHookPlacement.IsAny("primary"));
			Assert.False(TestHookPlacement.IsAny("any "));
		}

		[Fact]
		public void The_switch_is_the_documented_variable()
		{
			Assert.Equal("MESEN_GUI_WINDOW", TestHookPlacement.EnvironmentVariable);
		}

		//The process with --test-hook opens its windows without activating. An
		//activation policy is an AppKit notion, so it is asked for only by a real
		//macOS desktop application: a headless run has no window server and no
		//window of its own, and retargeting the test host's own activation policy
		//is a change nobody asked for. The decision is the platform plus the kind
		//of application this process is - never a platform-handle descriptor, which
		//is a backend-private string that may differ (or be null) before a window is
		//shown, so keying off it silently skips the policy.
		[Fact]
		public void The_activation_policy_is_only_applied_by_a_desktop_app_on_macos()
		{
			Assert.True(TestHookPlacement.NeedsActivationPolicy(true, true));
			Assert.False(TestHookPlacement.NeedsActivationPolicy(true, false));
			Assert.False(TestHookPlacement.NeedsActivationPolicy(false, true));
			Assert.False(TestHookPlacement.NeedsActivationPolicy(false, false));
		}

		[Fact]
		public void The_accessory_policy_is_the_one_that_never_activates()
		{
			//NSApplicationActivationPolicyAccessory = 1: the app has windows, has no
			//Dock icon and never becomes the frontmost application (the default,
			//Regular = 0, activates and takes the keyboard).
			Assert.Equal(1, TestHookPlacement.AccessoryPolicy);
		}
	}
}
