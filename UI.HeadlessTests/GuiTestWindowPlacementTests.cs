using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Logic.TestHook;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1255: the window half of "a GUI test run goes to the primary (built-in) display
//and never takes the keyboard focus". The rule itself is host-free geometry and is
//pinned in UI.Tests/TestHook/TestHookPlacementTests; what only a real window can
//show is that the rule is wired to one: MesenWindow.OnInitialized asks for the
//activation policy and shows the window without activating, MesenWindow.OnOpened
//pulls a window that opened off the primary display onto its working area, and the
//hook's state carries each window's frame and the display it has to stay inside,
//which is what the adapter reads (scripts/gui_test/mesen_gui_adapter.py).
//
//No MainWindow and no native core: the wiring under test is on a plain MesenWindow,
//so every case runs on CI too. "Which displays does this machine have" is the one
//injected seam (TestHookWiring.PrimaryDisplayForTest): a unit test has no second
//monitor to open a window on, and the refusal path a real off-primary window takes
//is covered end to end by the adapter's own suite against a hook that reports one.
[Collection(NativeCoreCollection.Name)]
public class GuiTestWindowPlacementTests : IDisposable
{
	private readonly List<Window> _windows = new();
	private readonly bool _runningForTest = TestHookWiring.RunningForTest;
	private readonly Func<Window, (int[]? Bounds, int[]? WorkingArea)> _primary = TestHookWiring.PrimaryDisplayForTest;
	private readonly Func<Window, bool> _hasPlatformWindow = TestHookActivation.HasPlatformWindow;
	private readonly Action<int> _applyPolicy = TestHookActivation.ApplyPolicy;

	//A 1512x982 built-in display at the origin with the menu bar cut out of its
	//working area - the shape the machine's real one reports.
	private static readonly int[] Bounds = { 0, 0, 1512, 982 };
	private static readonly int[] WorkingArea = { 0, 25, 1512, 945 };

	public void Dispose()
	{
		TestHookWiring.RunningForTest = _runningForTest;
		TestHookWiring.PrimaryDisplayForTest = _primary;
		TestHookActivation.HasPlatformWindow = _hasPlatformWindow;
		TestHookActivation.ApplyPolicy = _applyPolicy;
		foreach(Window window in _windows) {
			window.Close();
		}
		_windows.Clear();
		Pump();
	}

	private static void Pump() => Dispatcher.UIThread.RunJobs();

	private MesenWindow Window(int width, int height)
	{
		MesenWindow window = new() { Width = width, Height = height };
		_windows.Add(window);
		return window;
	}

	private static JsonObject MainOf(MesenWindow window) =>
		new TestHookWiring.WindowTarget(window, () => false).State()["window"]!.AsObject();

	//The blocking criterion, at the one place a rule can be wired to it: every
	//window a run builds is shown without activating, which is what keeps a
	//Cmd-Tab or a click elsewhere from being pulled back to the run.
	[AvaloniaFact]
	public void A_window_created_while_the_hook_runs_is_shown_without_activating()
	{
		TestHookWiring.RunningForTest = true;
		MesenWindow window = Window(1100, 700);

		window.Show();
		Pump();

		Assert.False(window.ShowActivated, "a window of the run must not activate itself");
	}

	//...and the same window without the flag is untouched: the whole of #1255 is
	//inert in a process that was not started for a test run.
	[AvaloniaFact]
	public void Without_the_hook_a_window_still_activates()
	{
		TestHookWiring.RunningForTest = false;
		MesenWindow window = Window(1100, 700);

		window.Show();
		Pump();

		Assert.True(window.ShowActivated, "a person's emulator is not a test run");
	}

	//The policy is an AppKit call on the shared application, and the window is what
	//carries the process: Confine is called from MesenWindow.OnInitialized, before
	//the platform shows the window. Gated to macOS because that is the only place
	//the decision can be true; the plugin call itself is stubbed here (the real one
	//is exercised by scripts/test_gui_test_window_focus_e2e.py, which reads AppKit's
	//answer back).
	[AvaloniaFact]
	public void A_macOS_run_asks_AppKit_for_the_accessory_policy_before_showing_the_window()
	{
		Assert.SkipWhen(!OperatingSystem.IsMacOS(), "an activation policy is an AppKit notion: only a macOS process asks for one");
		TestHookWiring.RunningForTest = true;
		List<int> asked = new();
		TestHookActivation.HasPlatformWindow = _ => true;
		TestHookActivation.ApplyPolicy = asked.Add;
		MesenWindow window = Window(1100, 700);

		window.Show();
		Pump();

		Assert.False(window.ShowActivated);
		Assert.Equal(new[] { TestHookPlacement.AccessoryPolicy }, asked);
	}

	//The other half of the same decision: a process with no window behind it is not
	//the desktop application whose activation policy may be retargeted, so nothing
	//is asked for. Without this half a window in the wrong process would change the
	//policy of a person's running emulator.
	[AvaloniaFact]
	public void A_process_with_no_platform_window_of_its_own_is_left_out_of_the_policy()
	{
		TestHookWiring.RunningForTest = true;
		List<int> asked = new();
		TestHookActivation.HasPlatformWindow = _ => false;
		TestHookActivation.ApplyPolicy = asked.Add;
		MesenWindow window = Window(1100, 700);

		window.Show();
		Pump();

		Assert.False(window.ShowActivated);
		Assert.Empty(asked);
	}

	//A window the window manager put on the external monitor is pulled onto the
	//primary display's working area when it opens - and the frame, not the client
	//area, is what has to fit: a title bar hanging over the edge is still a window
	//half off the display.
	[AvaloniaFact]
	public void A_window_that_opens_off_the_primary_display_is_pulled_onto_it()
	{
		TestHookWiring.RunningForTest = true;
		TestHookWiring.PrimaryDisplayForTest = _ => (Bounds, WorkingArea);
		MesenWindow window = Window(1100, 700);

		window.Position = new PixelPoint(2000, 300);  // the monitor beside the laptop
		window.Show();
		Pump();

		//The frame when the platform reports one; a headless window has none, so the
		//client rect it was placed by is what is checked here.
		int[] frame = TestHookWiring.WindowFrameRect(window) ?? TestHookWiring.WindowRect(window);
		Assert.NotEqual(2000, window.Position.X);
		Assert.True(TestHookPlacement.Contains(WorkingArea, frame),
			$"the window settled at ({frame[0]},{frame[1]}) size {frame[2]}x{frame[3]}, outside the working area");
	}

	//A window that is already inside the primary display is left exactly where it
	//is: the pull is a correction, not a re-placement of every window.
	[AvaloniaFact]
	public void A_window_already_on_the_primary_display_does_not_move()
	{
		TestHookWiring.RunningForTest = true;
		TestHookWiring.PrimaryDisplayForTest = _ => (Bounds, WorkingArea);
		MesenWindow window = Window(1100, 700);
		window.Position = new PixelPoint(40, 60);

		window.Show();
		Pump();

		Assert.Equal(40, window.Position.X);
		Assert.Equal(60, window.Position.Y);
	}

	//MESEN_GUI_WINDOW=any is the documented escape hatch for a runner with no
	//primary display: the window stays where the platform put it.
	[AvaloniaFact]
	public void The_escape_hatch_leaves_the_window_where_the_platform_put_it()
	{
		TestHookWiring.RunningForTest = true;
		TestHookWiring.PrimaryDisplayForTest = _ => (Bounds, WorkingArea);
		string? before = Environment.GetEnvironmentVariable(TestHookPlacement.EnvironmentVariable);
		Environment.SetEnvironmentVariable(TestHookPlacement.EnvironmentVariable, TestHookPlacement.Any);
		try {
			MesenWindow window = Window(1100, 700);
			window.Position = new PixelPoint(2000, 300);

			window.Show();
			Pump();

			Assert.Equal(2000, window.Position.X);
		} finally {
			Environment.SetEnvironmentVariable(TestHookPlacement.EnvironmentVariable, before);
		}
	}

	//The state the adapter refuses a launch from: every window of the run with its
	//own frame, and the display that frame has to stay inside. A frame the platform
	//does not report is null and the adapter falls back to position and size, so
	//both are pinned here as reported, never as assumed.
	[AvaloniaFact]
	public void The_state_reports_each_window_frame_and_the_display_to_stay_inside()
	{
		TestHookWiring.RunningForTest = true;
		TestHookWiring.PrimaryDisplayForTest = _ => (Bounds, WorkingArea);
		MesenWindow window = Window(1100, 700);
		window.Show();
		Pump();

		JsonObject main = MainOf(window);
		Assert.Equal(4, main["primaryWorkingArea"]!.AsArray().Count);
		Assert.Equal(WorkingArea, main["primaryWorkingArea"]!.AsArray().Select(n => n!.GetValue<int>()).ToArray());
		Assert.Equal(Bounds, main["primaryBounds"]!.AsArray().Select(n => n!.GetValue<int>()).ToArray());
		//The frame, which is what the adapter measures a window by. A headless
		//window has no window manager and so no frame to report, and the field is
		//there as null rather than absent - the adapter's signal to fall back to
		//position and size (pinned in scripts/test_gui_test_mesen_adapter.py). What
		//the frame MEANS (origin + frame size, in the display's own unit - points on
		//macOS, physical pixels on Windows and X11) is host-free and pinned in
		//UI.Tests/TestHook/TestHookPlacementTests.
		Assert.True(main.ContainsKey("frame"), "the adapter reads state.window.frame");
		Assert.Equal(window.FrameSize is null, main["frame"] is null);
		Assert.Equal(window.Bounds.Width, main["size"]![0]!.GetValue<int>());

		JsonArray windows = new TestHookWiring.WindowTarget(window, () => false).State()["windows"]!.AsArray();
		JsonObject first = windows[0]!.AsObject();
		Assert.Equal("main", first["id"]!.GetValue<string>());
		Assert.True(first.ContainsKey("frame"), "every window of the run carries its frame");
	}

	//A dialog is a window of the run like any other: the adapter refuses a step
	//whose window is off the primary display, so the state names every one of them,
	//by title, with its own frame.
	[AvaloniaFact]
	public void A_second_window_opened_while_the_hook_runs_is_reported_with_its_frame()
	{
		TestHookWiring.RunningForTest = true;
		TestHookWiring.PrimaryDisplayForTest = _ => (Bounds, WorkingArea);
		MesenWindow main = Window(1100, 700);
		MesenWindow dialog = new() { Title = "play.controller-sheet", Width = 800, Height = 600 };
		_windows.Add(dialog);

		main.Show();
		dialog.Show();
		Pump();

		JsonArray windows = new TestHookWiring.WindowTarget(main, () => false).State()["windows"]!.AsArray();
		Assert.Equal(2, windows.Count);
		Assert.Equal("main", windows[0]!["id"]!.GetValue<string>());
		Assert.Equal("play.controller-sheet", windows[1]!["id"]!.GetValue<string>());
		Assert.True(windows[1]!.AsObject().ContainsKey("frame"), "a dialog is measured the way the main window is");
		Assert.False(dialog.ShowActivated, "a dialog of the run must not activate either");
	}

}
