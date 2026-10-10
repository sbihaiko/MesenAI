using Avalonia.Controls;
using Mesen.Logic.TestHook;
using System;
using System.Runtime.InteropServices;

namespace Mesen.Windows
{
	//#1255: a GUI test run must not take the keyboard focus away from whoever is
	//using the machine. Driving input goes through the hook, never through the
	//operating system's focus, so the process started with --test-hook tells AppKit
	//it is an accessory: it may show windows, it is not in the Dock, and it never
	//becomes the frontmost application. On top of that, every window it opens is
	//shown without activating (Window.ShowActivated), because a window shown
	//activated would pull the focus even out of an accessory process.
	//
	//Inert without the flag: Confine is only reached from TestHookWiring, which
	//does nothing at all when the process was not started with --test-hook.
	public static class TestHookActivation
	{
		//Whether the window really has a platform window behind it (the probe the
		//decision at TestHookPlacement.NeedsActivationPolicy reads). A property, so
		//the platform boundary is one named place; nothing else here is stubbed.
		public static Func<Window, bool> HasPlatformWindow { get; set; }
			= window => window.TryGetPlatformHandle() is not null;

		//The AppKit call, named so a test can watch what was asked for without
		//making the real one.
		public static Action<int> ApplyPolicy { get; set; } = SetAccessoryPolicy;

		//Called before the window is shown: ShowActivated is read when the platform
		//window comes up, so setting it afterwards would be too late.
		public static void Confine(Window window)
		{
			window.ShowActivated = false;
			//Asked for once per window, and only by a macOS process that has a real
			//one: the policy is a property of the process, so repeating it is free,
			//while asking for it in a headless or non-macOS run is not.
			if(TestHookPlacement.NeedsActivationPolicy(OperatingSystem.IsMacOS(), HasPlatformWindow(window))) {
				ApplyPolicy(TestHookPlacement.AccessoryPolicy);
			}
		}

		//NSApplicationActivationPolicyAccessory on the shared application.
		private static void SetAccessoryPolicy(int policy)
		{
			if(!OperatingSystem.IsMacOS()) {
				return;
			}
			IntPtr application = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
			if(application == IntPtr.Zero) {
				return;
			}
			objc_msgSend_setActivationPolicy(application, sel_registerName("setActivationPolicy:"), policy);
		}

		[DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
		private static extern IntPtr objc_getClass(string name);

		[DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
		private static extern IntPtr sel_registerName(string name);

		[DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
		private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

		[DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
		private static extern void objc_msgSend_setActivationPolicy(IntPtr receiver, IntPtr selector, long policy);
	}
}
