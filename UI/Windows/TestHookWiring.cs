using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Interop;
using Mesen.Logic.TestHook;
using System;
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

		public static bool Running => _keys is not null;

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
			TestHookKeys keys = new(InputApi.SetInjectedKey, keyCode ?? InputApi.GetKeyCode, RunningFrames);
			TestHookProtocol protocol = new(options.Token, new WindowTarget(window), keys);
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

			public WindowTarget(Window window)
			{
				_window = window;
			}

			public JsonObject State()
			{
				JsonArray controls = new();
				string? screen = null;
				foreach(Control control in _window.GetVisualDescendants().OfType<Control>()) {
					string? id = AutomationProperties.GetAutomationId(control);
					if(id is null || id.Length == 0) {
						continue;
					}
					if(control.IsEffectivelyVisible && id == "play.home") {
						screen = id;
					}
					controls.Add(new JsonObject {
						["id"] = id,
						["enabled"] = control.IsEffectivelyEnabled,
						["visible"] = control.IsEffectivelyVisible,
						["focused"] = control.IsFocused
					});
				}
				return new JsonObject {
					["screen"] = screen,
					["dialogs"] = new JsonArray(),
					["focus"] = FocusedId(),
					["controls"] = controls,
					["options"] = new JsonArray(),
					["window"] = new JsonObject {
						["mode"] = _window.WindowState == WindowState.FullScreen ? "fullscreen" : "windowed",
						["size"] = new JsonArray((int)_window.Bounds.Width, (int)_window.Bounds.Height)
					}
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
			//other application stay out of it by construction.
			public CaptureResult Capture(string path)
			{
				int width = Math.Max(1, (int)Math.Ceiling(_window.Bounds.Width * _window.RenderScaling));
				int height = Math.Max(1, (int)Math.Ceiling(_window.Bounds.Height * _window.RenderScaling));
				using RenderTargetBitmap bitmap = new(new PixelSize(width, height), new Vector(96 * _window.RenderScaling, 96 * _window.RenderScaling));
				bitmap.Render(_window);
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
				bitmap.Save(path, PngBitmapEncoderOptions.Default);
				return new CaptureResult(path, width, height, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
			}

			public void Quit() => _window.Close();
		}
	}
}
