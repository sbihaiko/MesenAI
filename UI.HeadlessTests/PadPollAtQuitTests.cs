using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1484: the application can exit -11 (SIGSEGV) after a batch quits with the
//Settings sheet open. The .ips reports name the frame: MesenCore
//Emulator::IsPaused() reading the emulator through a null `_emu`
//(KERN_INVALID_ADDRESS at 0x40), on the main thread - a core call made after
//EmuApi.Release emptied the process-global emulator.
//
//The caller is the window's own pad poll: PlayEdgeFlowsWiring.Attach starts a
//50 ms DispatcherTimer, and every tick runs MainWindowViewModel.TickPadLoss,
//which reads EmuApi.IsPaused() before it looks at anything else. The lamps
//timer beside it stops with the window (#840); the 50 ms bridge did not, so it
//kept ticking past the exit path's EmuApi.Release - a race the process
//sometimes loses, which is why the same batch reports -11 on one run and 0 on
//the next.
//
//The rule the exit path has to keep: by the time the core is released, the pad
//poll is already stopped. It is asserted at the release itself rather than by
//ticking afterwards - a tick into a released core is the crash, and the
//process-global core is not something a test may release for real.
[Collection(NativeCoreCollection.Name)]
public class PadPollAtQuitTests
{
	[AvaloniaFact]
	public void QuitWithThePadPollRunning_StopsThePollBeforeTheCoreIsReleased()
	{
		if(!NativeCore.IsAvailable) {
			return;
		}
		MainWindow window = new();
		window.ShowStarted();

		DispatcherTimer? poll = window.PadPollTimer;
		Assert.NotNull(poll);
		Assert.True(poll!.IsEnabled, "the window's pad poll was never started, so this case would pass without covering anything.");

		bool pollingAtRelease = false;
		window.ReleaseCore = () => { pollingAtRelease = poll.IsEnabled; };
		window.SkipCloseConfirmation = true;
		window.Close();
		Dispatcher.UIThread.RunJobs();

		Assert.False(pollingAtRelease, "the 50 ms pad poll was still running when the core was released: its next tick calls EmuApi.IsPaused() through a null emulator (#1484).");
		Assert.False(poll.IsEnabled, "the pad poll outlived the window that owns it (#840).");
	}
}
