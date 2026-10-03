using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#658, #660, #661: the edge flows' close paths. The BIOS sheet's request is
//raised through MainWindow's own MissingFirmware handler, on a thread of its
//own the way the Core's load thread raises it: that thread blocks until the
//UI answers, and in the app it holds the Core's load locks meanwhile.
public partial class PlayEdgeFlowsTests
{
	//The Core's MissingFirmware notification for disksys.rom, raised through
	//MainWindow.OnNotification on a separate thread.
	private sealed class CoreBiosRequest : IDisposable
	{
		private readonly IntPtr _name = Marshal.StringToCoTaskMemUTF8("disksys.rom");
		private readonly IntPtr _message = Marshal.AllocHGlobal(Marshal.SizeOf<MissingFirmwareMessage>());
		private readonly Thread _thread;

		public CoreBiosRequest(MainWindow window)
		{
			Marshal.StructureToPtr(new MissingFirmwareMessage() { Filename = _name, Firmware = FirmwareType.FDS, Size = 8192 }, _message, false);
			NotificationEventArgs e = new() { NotificationType = ConsoleNotificationType.MissingFirmware, Parameter = _message };
			MethodInfo handler = typeof(MainWindow).GetMethod("OnNotification", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new XunitException("MainWindow.OnNotification not found");
			_thread = new Thread(() => handler.Invoke(window, new object[] { e })) { IsBackground = true, Name = "fake core load thread" };
			_thread.Start();
		}

		public bool Answered => !_thread.IsAlive;

		//Pumps the dispatcher meanwhile (the sheet's answer runs on the UI
		//thread) unless pump is false: in the app, the UI thread is then blocked
		//in EmuApi.Stop, so the answer cannot need it.
		public bool WaitAnswered(TimeSpan timeout, bool pump = true)
		{
			if(!pump) {
				return _thread.Join(timeout);
			}
			Stopwatch clock = Stopwatch.StartNew();
			while(!Answered && clock.Elapsed < timeout) {
				Dispatcher.UIThread.RunJobs();
				Thread.Sleep(10);
			}
			Dispatcher.UIThread.RunJobs();
			return Answered;
		}

		public void Dispose()
		{
			//A request never answered still reads the message: leak it.
			if(Answered) {
				Marshal.FreeHGlobal(_message);
				Marshal.FreeCoTaskMem(_name);
			}
		}
	}

	//#658: EmuApi.Stop on the UI thread waits for the load locks the Core's
	//thread holds while it waits for the sheet, so closing answers it first,
	//and a request raised after that is answered at once, with no sheet.
	[AvaloniaFact]
	public void Closing_the_window_while_the_bios_sheet_waits_answers_the_core_first()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		int released = 0;
		window.ReleaseCore = () => released++;

		using CoreBiosRequest request = new(window);
		WaitFor(() => model.BiosSheet.IsVisible, "the Core's request never showed the BIOS sheet");

		window.Close();
		Assert.Equal(1, released);
		Assert.True(request.WaitAnswered(TimeSpan.FromSeconds(10), pump: false), "closing left the Core's BIOS request unanswered: EmuApi.Stop would wait on its load locks forever (#658)");
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.BiosSheet.IsVisible);
		Assert.DoesNotContain("needs the FDS BIOS", model.Shell.StatusText);

		using CoreBiosRequest late = new(window);
		Assert.True(late.WaitAnswered(TimeSpan.FromSeconds(10)), "a request raised while closing was not answered at once");
		Assert.False(model.BiosSheet.IsVisible);
	}

	//#658: another open (a recent card, Open a ROM…, a drop) drops the request:
	//the old load ends and the new one is not queued behind the sheet. No
	//"needs the BIOS" sentence for a game the player moved away from.
	[AvaloniaFact]
	public void Opening_another_game_drops_the_bios_request()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		using CoreBiosRequest request = new(window);
		WaitFor(() => model.BiosSheet.IsVisible, "the Core's request never showed the BIOS sheet");

		model.OnOpenStarted();
		Assert.True(request.WaitAnswered(TimeSpan.FromSeconds(10)), "another open left the Core's BIOS request waiting on the sheet (#658)");
		Assert.False(model.BiosSheet.IsVisible);
		Assert.DoesNotContain("needs the FDS BIOS", model.Shell.StatusText);
	}

	//#658: a power off (the hotkey; Quit game on W-P4) would wait behind the
	//sheet for the load locks, then power off whatever the answer loaded.
	[AvaloniaFact]
	public void Powering_off_drops_the_bios_request()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		using CoreBiosRequest request = new(window);
		WaitFor(() => model.BiosSheet.IsVisible, "the Core's request never showed the BIOS sheet");
		try {
			LoadRomHelper.PowerOff();
			Assert.True(request.WaitAnswered(TimeSpan.FromSeconds(10)), "a power off left the Core's BIOS request waiting on the sheet (#658)");
			Assert.False(model.BiosSheet.IsVisible);
			Assert.DoesNotContain("needs the FDS BIOS", model.Shell.StatusText);
		} finally {
			//The power off runs on the thread pool; its notifications post back.
			Thread.Sleep(200);
			Dispatcher.UIThread.RunJobs();
			LoadRomHelper.ResetReloadCounter();
		}
	}

	//#660: the pill's 8 s advance only on the controller poll's ticks, which
	//stop with the game; pausing within the 8 s must not leave it on screen.
	[AvaloniaFact]
	public void The_unknown_controller_pill_goes_away_when_the_game_pauses()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		PlayControllerSetupViewModel setup = model.ControllerSetup;
		const int device = 7;
		ushort key = (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + 1);
		setup.KeyName = k => "Pad8 But" + (k & 0xFF);
		setup.CurrentConsole = () => ConsoleType.Nes;
		try {
			RunSyntheticGame(model);
			setup.Tick(new[] { key });
			Assert.True(setup.IsPillVisible);
			Dispatcher.UIThread.RunJobs();
			Assert.True(window.FindNamed<Border>("ControllerSetupPill").IsOnScreen());

			EmuApi.Pause();
			WaitFor(() => !setup.IsPillVisible, "the unknown-controller pill stayed after the game paused (#660)");
			Assert.False(window.FindNamed<Border>("ControllerSetupPill").IsOnScreen());
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//#661: quitting the app or shutting the OS down while the first-run sheet
	//is up closes it even when the folder cannot be written; nothing is
	//written, so the sheet shows again next launch.
	[AvaloniaTheory]
	[InlineData(WindowCloseReason.ApplicationShutdown)]
	[InlineData(WindowCloseReason.OSShutdown)]
	public void The_first_run_sheet_never_blocks_quitting_the_app_or_shutting_the_OS_down(WindowCloseReason reason)
	{
		RecordingFirstRun model = new() { Succeeds = false };
		SetupWizardWindow window = new(model);
		window.Show();
		Dispatcher.UIThread.RunJobs();
		try {
			MethodInfo handleClosing = typeof(Window).GetMethod("HandleClosing", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				?? throw new XunitException("Window.HandleClosing not found");
			bool cancelled = (bool)handleClosing.Invoke(window, new object[] { reason })!;
			Assert.False(cancelled, $"the first-run sheet cancelled a {reason} close (#661)");
			Assert.Equal(0, model.Confirms);
		} finally {
			model.Succeeds = true;
			window.Close();
		}
	}
}
