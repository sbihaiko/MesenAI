using System;
using Mesen.Logic.TestHook;
using Xunit;

namespace Mesen.Tests.TestHook
{
	//#1255: a GUI test run's windows belong on the primary (built-in) display, not
	//on whichever monitor the OS picks. The rule is host-free geometry - a display
	//area and a window rect, both as [x, y, width, height] - so it is pinned here.
	//That the rule is wired to a real window (a window opened off the primary
	//display is pulled onto it, and its own frame is what gets reported) is pinned
	//in UI.HeadlessTests/GuiTestWindowPlacementTests, and the adapter's half of the
	//same rule in scripts/test_gui_test_mesen_adapter.py.
	public class TestHookPlacementTests
	{
		//A 1512x982 built-in display at the origin, its working area 25px shorter
		//because of the menu bar - the shape a macOS laptop reports.
		private static readonly int[] Primary = { 0, 0, 1512, 982 };
		private static readonly int[] WorkingArea = { 0, 25, 1512, 945 };
		private static readonly int[] MainWindow = { 40, 60, 1100, 700 };

		//The 2x Retina laptop the real-binary gate failed on: a 2560x1600 panel run
		//at 1440x900 points, so the display and its working area read in points while
		//RenderScaling is 2.0 - the machine that refused every launch (#1255).
		private static readonly int[] RetinaDisplay = { 0, 0, 1440, 900 };
		private static readonly int[] RetinaWorkingArea = { 0, 30, 1440, 870 };

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

		//The working area is the display without the menu bar, so the same window
		//can be inside one and outside the other: the check is only as strict as the
		//area it is given.
		[Fact]
		public void A_window_under_the_menu_bar_is_outside_the_working_area()
		{
			Assert.True(TestHookPlacement.Contains(Primary, new[] { 40, 5, 1100, 700 }));
			Assert.False(TestHookPlacement.Contains(WorkingArea, new[] { 40, 5, 1100, 700 }));
		}

		//A display whose working area does not start at the origin (a screen the
		//hook reports with a shifted origin) holds a window placed inside it.
		[Fact]
		public void An_area_with_a_shifted_origin_holds_its_own_windows()
		{
			Assert.True(TestHookPlacement.Contains(new[] { 1512, 0, 1280, 1024 }, new[] { 1600, 100, 1100, 700 }));
		}

		//#1255 review: the window manager's rectangle is the FRAME, not the client
		//area - a title bar hanging over the edge is a window half off the display
		//even when every pixel of the client area fits. The origin is the window's
		//own (Position is the frame's origin, the same reading WindowExtensions uses
		//to center a child), and the size is the frame's.
		//
		//#1255 gate: the rect is in the SAME unit as the display it is checked
		//against - the display's own, which is the unit Window.Position and
		//Screen.Bounds/WorkingArea are read in. The frame size the platform reports
		//is already in that unit, so the render scaling has no part in the rect: on
		//the 2x Retina laptop every launch was refused because it WAS folded in - a
		//1100x700 window reported its frame as 2200x1400, wider than the 1440x900
		//display it was on, so no placement could ever contain it.
		[Theory]
		[InlineData(1.0)]   // a display that reports 1:1, where the old fold was invisible
		[InlineData(2.0)]   // the Retina laptop the gate failed on
		public void The_reported_rect_is_the_displays_unit_whatever_the_render_scaling_is(double renderScaling)
		{
			//A 700-tall client area with a 28-tall title bar at 40,60: the frame the
			//platform reports, read as it reports it, and the only rect a run may
			//report for it.
			int[] frame = TestHookPlacement.WindowRect(40, 60, 1100, 728);
			Assert.Equal(new[] { 40, 60, 1100, 728 }, frame);
			Assert.True(TestHookPlacement.Contains(RetinaWorkingArea, frame));
			//What folding the render scaling into the same reading gives: the same
			//rect at 1x, and at 2x a rect wider than the display it is on - refused
			//instead of measured, which is what the gate saw.
			int[] folded = { frame[0], frame[1], (int)Math.Ceiling(frame[2] * renderScaling), (int)Math.Ceiling(frame[3] * renderScaling) };
			Assert.Equal(TestHookPlacement.Contains(RetinaWorkingArea, folded), renderScaling == 1.0);
			//A frame a fraction of a unit wide is still a unit: a zero-sized rect
			//would read as "inside" any display at all.
			Assert.Equal(new[] { 40, 60, 1, 1 }, TestHookPlacement.WindowRect(40, 60, 0.2, 0.2));
		}

		//The state the gate refused, host-free: the run's window - pinned at 1100x700
		//at 40,60 (scripts/gui_test/mesen_gui_adapter.py) and pulled to the working
		//area's origin by the placement - is inside the display it was refused
		//against, and the reading that was refused (the frame folded through the 2x
		//render scaling, 2200x1400) is not.
		[Fact]
		public void The_window_the_gate_refused_is_inside_the_display_that_refused_it()
		{
			int[] reported = TestHookPlacement.WindowRect(RetinaWorkingArea[0], RetinaWorkingArea[1], 1100, 700);
			Assert.Equal(new[] { 0, 30, 1100, 700 }, reported);
			Assert.True(TestHookPlacement.Contains(RetinaWorkingArea, reported));
			Assert.False(TestHookPlacement.Contains(RetinaWorkingArea, new[] { 0, 30, 2200, 1400 }));
			//The two rectangles the same state reports are nested, never one display
			//and one window in a unit of their own.
			Assert.True(TestHookPlacement.Contains(RetinaDisplay, RetinaWorkingArea));
		}

		//The frame is what gets checked, so a window whose client area fits but whose
		//frame does not is outside - the case the client-area check used to pass.
		[Fact]
		public void A_frame_hanging_over_the_bottom_edge_is_outside_while_the_client_area_is_inside()
		{
			int[] client = TestHookPlacement.WindowRect(40, 260, 1100, 700);
			int[] frame = TestHookPlacement.WindowRect(40, 260, 1100, 740);
			Assert.True(TestHookPlacement.Contains(WorkingArea, client));
			Assert.False(TestHookPlacement.Contains(WorkingArea, frame));
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

		//#1255 review: the adapter refuses a value that is neither mode, and a run
		//launched without the adapter has to be refused too - a typo like "off"
		//reading as primary placement is the silent fallback both halves exist to
		//prevent. Read is the strict reader the hook validates with at startup
		//(TestHookWiring.Start); IsAny above is the per-window test that never
		//throws, because a window opening is no place to discover a typo.
		[Fact]
		public void The_switch_reads_primary_or_any_and_refuses_anything_else()
		{
			Assert.Equal(TestHookPlacement.Primary, TestHookPlacement.Read(null));
			Assert.Equal(TestHookPlacement.Primary, TestHookPlacement.Read(""));
			Assert.Equal(TestHookPlacement.Primary, TestHookPlacement.Read(TestHookPlacement.Primary));
			Assert.Equal(TestHookPlacement.Any, TestHookPlacement.Read(TestHookPlacement.Any));
			Assert.Throws<ArgumentException>(() => TestHookPlacement.Read("off"));
			Assert.Throws<ArgumentException>(() => TestHookPlacement.Read("ANY"));
			Assert.Throws<ArgumentException>(() => TestHookPlacement.Read("any "));
		}

		[Fact]
		public void The_switch_is_the_documented_variable()
		{
			Assert.Equal("MESEN_GUI_WINDOW", TestHookPlacement.EnvironmentVariable);
		}

		//The process with --test-hook opens its windows without activating. An
		//activation policy is an AppKit notion, so it is asked for only by a macOS
		//process that really has a window behind it: a headless run has no window
		//server, and retargeting the test host's own activation policy is a change
		//nobody asked for. Both halves are what the caller observes, never an
		//assumption.
		[Fact]
		public void The_activation_policy_is_only_applied_by_a_macos_process_with_a_window()
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
