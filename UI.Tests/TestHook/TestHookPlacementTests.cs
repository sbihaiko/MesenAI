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
		//#1255 review: the rect is in the SAME unit as the display it is checked
		//against - the display's own, which is the unit Window.Position and
		//Screen.Bounds/WorkingArea are read in - and that unit is the platform's,
		//not a constant. Avalonia reads Position and the screen rectangles as a
		//PixelPoint/PixelRect whose VALUE is points on macOS and physical pixels on
		//Windows and X11, while Bounds and FrameSize are device-independent pixels
		//everywhere: on Windows and X11 the size has to be multiplied by the render
		//scaling, on macOS it must not be. The 2x Retina laptop the gate failed on is
		//the macOS case - scaling there read a 1100x700 window as 2200x1400 against
		//the 1440x900 display it was on, so no placement could contain it and every
		//launch was refused - and Windows/X11 is the other way round, where NOT
		//scaling under-measures the window and lets one hanging off the edge pass.
		[Theory]
		[InlineData(2.0, true, 1100, 700)]   // the 2x Retina laptop: screen rects in points, so the DIP size IS the display's unit
		[InlineData(1.5, false, 1650, 1050)] // a Windows/X11 display at 150%: physical pixels, so 1100 DIPs really cover 1650
		[InlineData(1.0, false, 1100, 700)]  // a display that reports 1:1, where the two answers agree
		public void The_reported_rect_is_the_displays_own_unit(double renderScaling, bool screenRectsInPoints, int width, int height)
		{
			Assert.Equal(new[] { 40, 60, width, height },
				TestHookPlacement.WindowRect(40, 60, 1100, 700, renderScaling, screenRectsInPoints));
		}

		//The frame the platform reports is the size that goes in: a 700-tall client
		//area with a 28-tall title bar reports 728, and that is the rect a run may
		//report for it.
		[Fact]
		public void The_reported_rect_is_the_frame_the_platform_reports()
		{
			int[] frame = TestHookPlacement.WindowRect(40, 60, 1100, 728, 2.0, screenRectsInPoints: true);
			Assert.Equal(new[] { 40, 60, 1100, 728 }, frame);
			Assert.True(TestHookPlacement.Contains(RetinaWorkingArea, frame));
		}

		//#1255 review: the false pass the un-scaled reading gives on a scaled display,
		//the other half of the unit rule. A 1100x700-DIP window at x=1000 on a 150%
		//Windows/X11 display really covers 1650x1050 physical pixels and reaches
		//x=2650 against a 2560-wide working area, so it hangs off the right edge -
		//while the reading that skipped the scaling stops at 2100 and reports it as
		//inside.
		[Fact]
		public void A_window_hanging_off_a_scaled_display_is_outside()
		{
			int[] area = { 0, 0, 2560, 1400 };
			int[] frame = TestHookPlacement.WindowRect(1000, 300, 1100, 700, 1.5, screenRectsInPoints: false);
			Assert.Equal(new[] { 1000, 300, 1650, 1050 }, frame);
			Assert.False(TestHookPlacement.Contains(area, frame));
			Assert.True(TestHookPlacement.Contains(area, new[] { 1000, 300, 1100, 700 }),
				"the un-scaled reading is the false pass the containment check must not give");
		}

		//A frame a fraction of a unit wide is still a unit: a zero-sized rect would
		//read as "inside" any display at all.
		[Fact]
		public void A_rect_is_never_zero_wide()
		{
			Assert.Equal(new[] { 40, 60, 1, 1 }, TestHookPlacement.WindowRect(40, 60, 0.2, 0.2, 2.0, screenRectsInPoints: true));
			Assert.Equal(new[] { 40, 60, 1, 1 }, TestHookPlacement.WindowRect(40, 60, 0.2, 0.2, 1.5, screenRectsInPoints: false));
		}

		//The state the gate refused, host-free: the run's window - pinned at 1100x700
		//at 40,60 (scripts/gui_test/mesen_gui_adapter.py) and pulled to the working
		//area's origin by the placement - is inside the display it was refused
		//against, and the reading that was refused (the frame folded through the 2x
		//render scaling, 2200x1400) is not.
		[Fact]
		public void The_window_the_gate_refused_is_inside_the_display_that_refused_it()
		{
			int[] reported = TestHookPlacement.WindowRect(RetinaWorkingArea[0], RetinaWorkingArea[1], 1100, 700, 2.0, screenRectsInPoints: true);
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
			int[] client = TestHookPlacement.WindowRect(40, 260, 1100, 700, 2.0, screenRectsInPoints: true);
			int[] frame = TestHookPlacement.WindowRect(40, 260, 1100, 740, 2.0, screenRectsInPoints: true);
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
