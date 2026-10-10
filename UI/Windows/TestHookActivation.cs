using Avalonia.Controls;
using Mesen.Logic.TestHook;
using System;
using System.Runtime.InteropServices;

namespace Mesen.Windows
{
	//#1255: a GUI test run must not take the keyboard focus away from whoever is
	//using the machine. Driving input goes through the hook, never through the
	//operating system's focus, so the process started with --test-hook tells AppKit
	//it is an accessory: it has windows, it is not in the Dock, and it never
	//becomes the frontmost application. Every window it opens is shown without
	//activating on top of that (Window.ShowActivated), because a window that is
	//shown activated would pull the focus even from an accessory process.
	//
	//The whole thing is inert without the flag: Confine is only reached from
	//TestHookWiring, which exits on its first line when there is no --test-hook.
	public static class TestHookActivation
	{
		//The platform seam: what the process is, and what descriptor the window's
		//real platform window carries. A test replaces both, because neither a
		//headless run nor a non-macOS one must have its activation policy touched.
		public static Func<Window, (bool IsMacOS, string? Descriptor)> DescribePlatformForTest { get; set; } = DefaultPlatform;
		public static Action<int> ApplyPolicyForTest { get; set; } = SetAccessoryPolicy;

		private static (bool IsMacOS, string? Descriptor) DefaultPlatform(Window window)
			=> (OperatingSystem.IsMacOS(), window.TryGetPlatformHandle()?.HandleDescriptor);

		//Called before the window is shown: ShowActivated is read when the platform
		//window comes up, so setting it later would be too late.
		public static void Confine(Window window)
		{
			//Read by the platform when the window comes up, so it is set before the
			//first Show of every window this process opens - main window and dialogs.
			window.ShowActivated = false;
			(bool isMacOS, string? descriptor) = DescribePlatformForTest(window);
			if(TestHookPlacement.NeedsActivationPolicy(isMacOS, descriptor)) {
				ApplyPolicyForTest(TestHookPlacement.AccessoryPolicy);
			}
		}

		//NSApplicationActivationPolicyAccessory on the shared application. Called on
		//the UI thread from OnFrameworkInitializationCompleted, before the first
		//window is shown.
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
