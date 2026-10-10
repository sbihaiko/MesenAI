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
		private static readonly List<Window> _open = new();

		public static bool Running => _keys is not null;

		//#1255: every window the application creates while a hook runs - the main
		//window first, then each dialog - is shown without activating (called from
		//MesenWindow.OnInitialized, before the platform window comes up).
		public static void WindowCreated(Window window)
		{
			if(Running) {
				TestHookActivation.Confine(window);
			}
		}

		//#1255: a window that has just opened is kept inside the primary display's
		//working area and becomes part of what the run reports, so no window of the
		//application can sit on an external monitor - a dialog follows the main
		//window instead of landing wherever the window manager puts it.
		public static void WindowOpened(Window window)
		{
			if(!Running) {
				return;
			}
			if(!_open.Contains(window)) {
				_open.Add(window);
				window.Closed += (_, _) => _open.Remove(window);
			}
			KeepOnPrimaryDisplay(window);
		}

		//The windows a state read reports, the main one first. Dialogs opened before
		//this process had a hook (nothing is constructed without the flag) never appear.
		private static List<Window> ReportedWindows(Window main)
		{
			List<Window> windows = new() { main };
			foreach(Window window in _open) {
				if(!ReferenceEquals(window, main) && window.IsVisible) {
					windows.Add(window);
				}
			}
			return windows;
		}

		//The window's rect in physical pixels, the same way Capture measures it.
		private static int[] WindowRect(Window window)
		{
			double scale = window.RenderScaling;
			return TestHookPlacement.Rect(window.Position.X, window.Position.Y,
				Math.Max(1, (int)Math.Ceiling(window.Bounds.Width * scale)),
				Math.Max(1, (int)Math.Ceiling(window.Bounds.Height * scale)));
		}

		private static Screen? PrimaryScreen(Window window)
			=> window.Screens.Primary ?? window.Screens.All.FirstOrDefault();

		private static int[]? AreaOf(PixelRect area) => TestHookPlacement.Rect(area.X, area.Y, area.Width, area.Height);

		//The primary display's full bounds and its working area (the same display
		//without the menu bar), or nulls when the platform has no screen to ask.
		private static (int[]? Bounds, int[]? WorkingArea) PrimaryDisplay(Window window)
		{
			Screen? screen = PrimaryScreen(window);
			return screen is null ? (null, null) : (AreaOf(screen.Bounds), AreaOf(screen.WorkingArea));
		}

		public static void KeepOnPrimaryDisplay(Window window)
		{
			if(TestHookPlacement.IsAny(Environment.GetEnvironmentVariable(TestHookPlacement.EnvironmentVariable))) {
				return;  // MESEN_GUI_WINDOW=any: a runner with no primary-display notion
			}
			int[]? area = PrimaryDisplay(window).WorkingArea;
			if(area is null) {
				return;
			}
			int[] rect = WindowRect(window);
			int[] placed = TestHookPlacement.Clamp(area, rect);
			if(placed[0] != rect[0] || placed[1] != rect[1]) {
				window.Position = new PixelPoint(placed[0], placed[1]);
			}
		}

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
			//#1255: before this window is shown (Start runs with the window built but
			//not yet up): the process never activates and the window is shown without
			//activating, so a person typing elsewhere keeps typing.
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
				//#1255: where the run's windows are. The adapter refuses a launch (or
				//a step) whose window sits outside the primary display's bounds, so
				//the position and the display it is compared against travel together.
				int[] mainRect = WindowRect(_window);
				(int[]? bounds, int[]? workingArea) = PrimaryDisplay(_window);
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
					["window"] = new JsonObject {
						["mode"] = _window.WindowState == WindowState.FullScreen ? "fullscreen" : "windowed",
						["size"] = new JsonArray(mainRect[2], mainRect[3]),
						["position"] = new JsonArray(mainRect[0], mainRect[1]),
						["maximized"] = _window.WindowState == WindowState.Maximized,
						["primaryBounds"] = Area(bounds),
						["primaryWorkingArea"] = Area(workingArea)
					},
					["windows"] = new JsonArray(ReportedWindows(_window).Select(w => (JsonNode)WindowEntry(w)).ToArray())
				};
			}

			private static JsonNode? Area(int[]? rect)
				=> rect is null ? null : new JsonArray(rect[0], rect[1], rect[2], rect[3]);

			private static JsonObject WindowEntry(Window window)
			{
				int[] rect = WindowRect(window);
				return new JsonObject {
					["id"] = window is MainWindow ? "main" : (window.Title ?? window.GetType().Name),
					["position"] = new JsonArray(rect[0], rect[1]),
					["size"] = new JsonArray(rect[2], rect[3])
				};
			}

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
