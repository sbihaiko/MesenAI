using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Interop;
using Mesen.Logic.TestHook;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Mesen.Windows
{
	//The window-bound half of the GUI test hook (#1182, the GUI test hook ADR on PR
	//#1202; the host-free half is UI/Logic/TestHook). Started only by a process
	//given --test-hook: without the flag nothing here is ever constructed, no
	//state is read and no input path changes.
	public static class TestHookWiring
	{
		private static TestHookKeys? _keys;
		private static TestHookServer? _server;
		private static readonly List<Window> _windows = new();

		//The flag a process started with --test-hook carries. RunningForTest is the
		//same flag for a test that only needs the window wiring - no socket, no pad
		//bridge: nothing in the application ever sets it.
		public static bool RunningForTest { get; set; }

		private static bool HookUp => _keys is not null || RunningForTest;

		public static bool Running => HookUp;

		//The machine's displays, as the hook reports them: the primary one's full
		//bounds and its working area (the same display without the menu bar), in the
		//[x, y, width, height] shape of TestHookPlacement. A property, so "which
		//displays does this machine have" is one named place a headless test can
		//stand in for; nothing else about placement is stubbed.
		public static Func<Window, (int[]? Bounds, int[]? WorkingArea)> PrimaryDisplayForTest { get; set; } = PrimaryDisplay;

		//#1255: every window the application creates while a hook runs - the main
		//window first, then each dialog - is shown without activating. Called from
		//MesenWindow.OnInitialized, which runs while the window is built and before
		//the platform shows it, the only moment ShowActivated is still read.
		public static void WindowCreated(Window window)
		{
			if(HookUp) {
				TestHookActivation.Confine(window);
			}
		}

		//#1255: a window that has just opened is kept inside the primary display's
		//working area and becomes part of what the run reports, so no window of the
		//application can sit on an external monitor - a dialog opened during a run
		//follows the main window instead of landing wherever the window manager
		//puts it. Returns the position it settled on, or null when it did not move.
		public static int[]? WindowOpened(Window window)
		{
			if(!HookUp) {
				return null;
			}
			if(!_windows.Contains(window)) {
				_windows.Add(window);
				window.Closed += (_, _) => _windows.Remove(window);
			}
			return KeepOnPrimaryDisplay(window);
		}

		//The window's client area in the display's own unit - the same unit the
		//display's bounds and working area come back in, and the fallback for a
		//platform that reports no frame. Not the capture's pixels: WindowTarget.Capture
		//sizes its bitmap itself, from Bounds and the render scaling.
		public static int[] WindowRect(Window window)
			=> TestHookPlacement.WindowRect(window.Position.X, window.Position.Y,
				window.Bounds.Width, window.Bounds.Height);

		//The window's frame in the display's own unit: Position plus FrameSize, the
		//title bar and the borders included. This is the rectangle the window manager
		//puts on the display, so it is the one that has to fit on it - a client rect
		//that fits while the title bar hangs over the edge is still a window half off
		//the display. Position is the frame's origin, the same reading
		//WindowExtensions uses to center a child window (UI/Utilities/WindowExtensions.cs),
		//and FrameSize is the size the platform reports in the same unit - so this is
		//Position plus FrameSize, with no render scaling anywhere (#1255: folding the
		//scaling in made a 1100x700 window report 2200x1400 on a 1440x900 display, and
		//every launch was refused). Null when the platform reports no frame (a
		//headless window), which is why the adapter falls back to position and size.
		public static int[]? WindowFrameRect(Window window)
		{
			Size? frame = window.FrameSize;
			return frame is null
				? null
				: TestHookPlacement.WindowRect(window.Position.X, window.Position.Y,
					frame.Value.Width, frame.Value.Height);
		}

		//Moves a window onto the primary display's working area when the OS put it
		//somewhere else. MESEN_GUI_WINDOW=any skips it, and so does a platform that
		//has no screen to ask. Returns the position applied, null when none was.
		public static int[]? KeepOnPrimaryDisplay(Window window)
		{
			if(TestHookPlacement.IsAny(Environment.GetEnvironmentVariable(TestHookPlacement.EnvironmentVariable))) {
				return null;
			}
			int[]? area = PrimaryDisplayForTest(window).WorkingArea;
			if(area is null) {
				return null;
			}
			int[] rect = WindowFrameRect(window) ?? WindowRect(window);
			int[] placed = TestHookPlacement.Clamp(area, rect);
			if(placed[0] == rect[0] && placed[1] == rect[1]) {
				return null;
			}
			window.Position = new PixelPoint(placed[0], placed[1]);
			return placed;
		}

		private static (int[]? Bounds, int[]? WorkingArea) PrimaryDisplay(Window window)
		{
			Screen? screen = window.Screens.Primary ?? window.Screens.All.FirstOrDefault();
			return screen is null
				? (null, null)
				: (Area(screen.Bounds), Area(screen.WorkingArea));
		}

		private static int[] Area(PixelRect rect) => TestHookPlacement.Rect(rect.X, rect.Y, rect.Width, rect.Height);

		//The tick the pad bridge counts. Null keys = no hook = nothing happens.
		public static void Advance() => _keys?.Advance();

		//The emulated frame counter while the emulated clock advances (a game is
		//loaded and not paused); null in every other state, where only ticks pass.
		public static long? RunningFrames()
		{
			if(!EmuApi.IsRunning() || EmuApi.IsPaused()) {
				return null;
			}
			return EmuApi.GetTimingInfo(EmuApi.GetRomInfo().ConsoleType.GetMainCpuType()).FrameCount;
		}

		//Null (and nothing created) without the flag. With it, a path the process
		//cannot create throws, and the caller exits non-zero.
		//keyCode is the backend's key-name lookup; the headless suite, which has no key
		//manager, passes its own the way PlayPadNavigationWiring.TickForTest does.
		public static IDisposable? Start(string[] args, MainWindow window, Func<string, ushort>? keyCode = null)
		{
			TestHookOptions? options = TestHookOptions.Parse(args);
			if(options is null) {
				return null;
			}
			//#1255: the window switch is read once, here, where a wrong value can
			//still fail the process (App.axaml.cs prints it and exits 2) instead of
			//being read as primary placement a window at a time. Inert without the
			//flag: a process that is not a test run never reads the variable at all.
			TestHookPlacement.Read(Environment.GetEnvironmentVariable(TestHookPlacement.EnvironmentVariable));
			//#1255: this window is built but not shown yet, and Start runs before the
			//application shows it: the process stops activating and this window is
			//shown without activation, so a person typing elsewhere keeps typing.
			TestHookActivation.Confine(window);
			WindowTarget target = new(window);
			TestHookKeys keys = new(InputApi.SetInjectedKey, keyCode ?? InputApi.GetKeyCode, RunningFrames, target.RaiseKey);
			TestHookProtocol protocol = new(options.Token, target, keys);
			_server = TestHookServer.Start(options.Endpoint, line => Dispatcher.UIThread.InvokeAsync(() => protocol.Handle(line)).GetAwaiter().GetResult());
			_keys = keys;
			TestHookServer server = _server;
			//One line on stderr, so a hook-driven run can never be mistaken for a person's.
			Console.Error.WriteLine("test hook listening on " + options.Endpoint);
			return new Stopper(() => {
				keys.ReleaseAll();
				server.Dispose();
				_keys = null;
				_server = null;
			});
		}

		private sealed class Stopper : IDisposable
		{
			private readonly Action _stop;

			public Stopper(Action stop)
			{
				_stop = stop;
			}

			public void Dispose() => _stop();
		}

		//The window the hook describes: named controls, and a bitmap of this window
		//and nothing else.
		public sealed class WindowTarget : ITestHookTarget
		{
			private readonly Window _window;
			private readonly Func<bool> _gameLoaded;

			//gameLoaded: whether an emulated picture is on screen; the headless suite
			//passes its own, the way Start takes its own keyCode.
			public WindowTarget(Window window, Func<bool>? gameLoaded = null)
			{
				_window = window;
				_gameLoaded = gameLoaded ?? EmuApi.IsRunning;
			}

			public JsonObject State()
			{
				JsonArray controls = new();
				JsonArray visible = new();
				JsonArray options = new();
				List<string> visibleIds = new();
				foreach(Control control in _window.GetVisualDescendants().OfType<Control>()) {
					string? id = AutomationProperties.GetAutomationId(control);
					if(id is null || id.Length == 0) {
						continue;
					}
					controls.Add(new JsonObject {
						["id"] = id,
						["enabled"] = control.IsEffectivelyEnabled,
						["visible"] = control.IsEffectivelyVisible,
						["focused"] = control.IsFocused
					});
					if(control.IsEffectivelyVisible) {
						visible.Add((JsonNode?)JsonValue.Create(id));
						visibleIds.Add(id);
					}
				}
				//Menu entries and list choices are the options a pad or key can pick
				//(ADR-0271), each with its visible text (ADR-0272 §3). Submenu items
				//live in the logical tree until their menu opens, and count as visible
				//only while every menu above them is open.
				foreach(Control control in _window.GetLogicalDescendants().OfType<Control>()) {
					string? id = AutomationProperties.GetAutomationId(control);
					if(id is { Length: > 0 } && control is MenuItem or ComboBoxItem or ListBoxItem) {
						bool menuOpen = control.GetLogicalAncestors().OfType<MenuItem>().All(m => m.IsSubMenuOpen);
						string? text = control switch {
							MenuItem item => item.Header as string,
							ContentControl item => item.Content as string,
							_ => null
						};
						options.Add(new JsonObject {
							["id"] = id,
							["text"] = text,
							["enabled"] = control.IsEffectivelyEnabled,
							["visible"] = control.IsEffectivelyVisible && menuOpen
						});
					}
				}
				return new JsonObject {
					//#1228: the topmost surface that is up, off the ids the loop
					//above collected - never a single hardcoded id, which is how
					//the library sheet came to read as the home behind it.
					["screen"] = TestHookScreens.Resolve(visibleIds),
					["dialogs"] = new JsonArray(),
					["focus"] = FocusedId(),
					["controls"] = controls,
					["visible"] = visible,
					["options"] = options,
					//#1255: where the run's windows are, and the display they have to
					//stay inside. The adapter refuses a launch - or the step that
					//opened one - whose window is not fully inside primaryBounds, so
					//the rectangles and the display they were compared against travel
					//together, in ONE unit: the display's own, which is what
					//Position and the screen rectangles are read in.
					["window"] = new JsonObject {
						["mode"] = _window.WindowState == WindowState.FullScreen ? "fullscreen" : "windowed",
						["size"] = new JsonArray(MainRect[2], MainRect[3]),
						["position"] = new JsonArray(MainRect[0], MainRect[1]),
						//#1255: the frame, which is what has to fit on the display,
						//and the display's WORKING area, which is what the window was
						//placed inside - the adapter refuses a window outside it, and a
						//check looser than the placement could never fail for one.
						["frame"] = AreaOf(WindowFrameRect(_window)),
						["maximized"] = _window.WindowState == WindowState.Maximized,
						["primaryBounds"] = AreaOf(PrimaryDisplayForTest(_window).Bounds),
						["primaryWorkingArea"] = AreaOf(PrimaryDisplayForTest(_window).WorkingArea)
					},
					["windows"] = new JsonArray(ReportedWindows().Select(w => (JsonNode)WindowEntry(w.Window, w.Id)).ToArray())
				};
			}

			private int[] MainRect => WindowRect(_window);

			//The windows the run has up, the one the hook was started for first: a
			//dialog opened during a run is reported where it really is, so the
			//adapter can refuse a step that put it on another monitor.
			private IEnumerable<(Window Window, string Id)> ReportedWindows()
			{
				yield return (_window, "main");
				foreach(Window window in _windows) {
					if(!ReferenceEquals(window, _window) && window.IsVisible) {
						//Named by its title, which is what tells a step's dialog apart
						//from the main window; the type name is the fallback for a
						//window that never got one.
						yield return (window, window.Title is { Length: > 0 } title ? title : window.GetType().Name);
					}
				}
			}

			private static JsonObject WindowEntry(Window window, string id)
			{
				int[] rect = WindowRect(window);
				return new JsonObject {
					["id"] = id,
					["position"] = new JsonArray(rect[0], rect[1]),
					["size"] = new JsonArray(rect[2], rect[3]),
					//#1255: the same frame the `window` object carries, so a dialog is
					//measured the way the main window is - and null, not absent, when
					//the platform reports no frame.
					["frame"] = AreaOf(WindowFrameRect(window))
				};
			}

			private static JsonNode? AreaOf(int[]? rect)
				=> rect is null ? null : new JsonArray(rect[0], rect[1], rect[2], rect[3]);

			//The focused control's own id, or its nearest ancestor's that has one.
			private string? FocusedId()
			{
				Visual? at = _window.FocusManager?.GetFocusedElement() as Visual;
				while(at is not null) {
					if(at is Control control && AutomationProperties.GetAutomationId(control) is { Length: > 0 } id) {
						return id;
					}
					at = at.GetVisualParent();
				}
				return null;
			}

			//Rendered from the window's own visual tree, so the desktop and every
			//other application stay out of it by construction. The game picture is the
			//native renderer's, not a visual in this tree (ADR-0167, ADR-0157 section
			//6), so a render of the window with a game loaded would be a black game
			//area passed off as a screenshot: it is refused instead (ADR-0272 item 2).
			public CaptureResult Capture(string path)
			{
				if(_gameLoaded()) {
					throw new InvalidOperationException("capture is not available while a game is loaded: the emulated picture is drawn by the native renderer and is not in the window's own render");
				}
				int width = Math.Max(1, (int)Math.Ceiling(_window.Bounds.Width * _window.RenderScaling));
				int height = Math.Max(1, (int)Math.Ceiling(_window.Bounds.Height * _window.RenderScaling));
				using RenderTargetBitmap bitmap = new(new PixelSize(width, height), new Vector(96 * _window.RenderScaling, 96 * _window.RenderScaling));
				bitmap.Render(_window);
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
				bitmap.Save(path, PngBitmapEncoderOptions.Default);
				return new CaptureResult(path, width, height, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
			}

			//KeyDown / KeyUp of a literal key on the focused element (the window when
			//nothing has focus), so the GUI keyboard - OnPreviewKeyDown, the
			//keyboard navigation - sees the press as it sees a person's. The backend's
			//keyboard codes ARE Avalonia Key values (KeyDefinitions.h: Enter = 6,
			//Esc = 13, Up Arrow = 24); pad, joystick and mouse codes (0x1FF and up)
			//reach the pressed set only.
			public void RaiseKey(ushort code, bool down)
			{
				Key key = (Key)code;
				if(code == 0 || code >= 0x1FF || !Enum.IsDefined(key)) {
					return;
				}
				InputElement target = _window.FocusManager?.GetFocusedElement() as InputElement ?? _window;
				target.RaiseEvent(new KeyEventArgs {
					RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent,
					Key = key,
					Source = target
				});
			}

			public void Quit() => _window.Close();
		}
	}
}
