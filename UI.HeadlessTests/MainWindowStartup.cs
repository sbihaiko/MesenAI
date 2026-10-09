using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Mesen.Controls;
using Mesen.HeadlessTests;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.v3;

[assembly: SettleMainWindows]

namespace Mesen.HeadlessTests;

//Issue #619. MainWindow.OnOpened runs its startup (InitializeEmu, the view
//model's Init, the post-init Dispatcher.UIThread.Post) on a thread-pool thread
//and returns at once; the window's recent-game previews and the Remaster/Share
//gate measurement post back from the thread pool too. Avalonia's per-test
//isolation resets Dispatcher.UIThread to null between tests, and Avalonia.Base
//creates Dispatcher.UIThread lazily on whichever thread reads it first. So a
//post that landed after its test ended claimed the UI dispatcher for a
//thread-pool thread, and the NEXT test's application setup failed in
//DefaultRenderLoop.Add with "The calling thread cannot access this object
//because a different thread owns it".
//
//Every MainWindow a test opens goes through ShowStarted, which waits for the
//startup, pumping the dispatcher so its posts run on the test's own UI thread.
//SettleMainWindows then waits, after each test, for the background work those
//windows started during it.
internal static class MainWindowStartup
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
	private static readonly List<MainWindow> Shown = new();

	public static void ShowStarted(this MainWindow window)
	{
		Shown.Add(window);
		window.Show();
		WaitFor(window.Startup, "MainWindow startup");
	}

	//#681: for a test that observes the startup itself. The test waits for
	//the startup; SettleShownWindows waits for it too, in case the test failed first.
	public static void ShowUnstarted(this MainWindow window)
	{
		Shown.Add(window);
		window.Show();
	}

	//Runs on the test's UI thread, before Avalonia tears its application down.
	internal static void SettleShownWindows()
	{
		//A plain [Fact] opens no window: it must not touch Dispatcher.UIThread
		//from its thread-pool thread, which would claim the dispatcher itself.
		if(Shown.Count == 0) {
			return;
		}
		try {
			WaitUntil(() => !StateGridEntry.ThumbnailsInFlight, "The recent-game previews");
			//#909: W-P4's Save states grid loads its slot previews the same way.
			WaitUntil(() => !SaveStateSlotViewModel.PreviewsInFlight, "The Save states slot previews");
			foreach(MainWindow window in Shown) {
				WaitFor(window.Startup, "MainWindow startup");
				if(window.DataContext is MainWindowViewModel model) {
					WaitFor(model.Remaster.Measuring, "The Remaster gate measurement");
					WaitFor(model.ShareGateRefresh, "The Share refresh after the gate measurement");
					WaitFor(model.PackPickApplied, "The pack swap after Use This Pack");
					WaitFor(model.PackLayerApplied, "The pack swap after a W-P6 layer switch");
				}
			}
		} finally {
			//#840: the windows go with the test that showed them, and this is the
			//other half of the #619 fix above. Waiting for the work a window started
			//is not enough while the window itself stays open: an open window keeps a
			//50 ms DispatcherTimer of its own (PlayPadNavigationWiring.Attach, one per
			//MainWindow) and whatever else it wired, so a tick can land after this
			//test ended and the next test's session setup fails in
			//HeadlessUnitTestSession.EnsureIsolatedApplication with
			//"The calling thread cannot access this object because a different thread
			//owns it" - reported at 1 ms, as a *cleanup* failure of the case that
			//happens to run next, which is what #840 sees.
			//
			//Closing runs MainWindow's exit path, which releases the process-global
			//core (EmuApi.Release cannot be undone in one process), so ReleaseCore is
			//set first: MainWindow.axaml.cs names the hook for exactly this, and the
			//tests that follow still need the core. EmuApi.Stop still runs, so a game
			//a test left loaded is stopped here rather than by the next test's
			//constructor (#790).
			//
			//Two passes, and the reason is the first pass's own: MainWindow.CloseEmu
			//closes *every other open window* before it stops anything
			//(MainWindow.axaml.cs), so closing the first window here can close the
			//second from inside it. A sibling closed that way would still be holding
			//its own ReleaseCore and would release the core on the way out, which is
			//the one thing this hook exists to prevent. Every hook is therefore
			//neutered before any window is closed.
			List<MainWindow> closing = new(Shown);
			foreach(MainWindow window in closing) {
				window.ReleaseCore = () => { };
				//And the close itself has to happen: ConfirmQuit (a recording, a
				//Remaster job) and ValidateExit (ConfirmExitResetPower, Player
				//mode's stop banner) each cancel OnClosing and leave the window
				//open, which is the leak this hook exists to stop.
				window.SkipCloseConfirmation = true;
			}

			//MainWindow.OnClosing can refuse: a recording or a Remaster job asks
			//first, and a refused close leaves the window open. Such a window stays
			//in Shown - the next settle retries it, and a window that never closes is
			//a leak the suite fails on rather than one this hook quietly forgets.
			//A throw from one Close must not strand the windows behind it either.
			foreach(MainWindow window in closing) {
				try {
					window.Close();
				} catch {
					//Left in Shown below, exactly like a refused close.
				}
			}
			Shown.RemoveAll(window => !window.IsVisible);
		}
	}

	private static void WaitFor(Task task, string what)
	{
		WaitUntil(() => task.IsCompleted, what);
		//Surfaces an exception instead of leaving it unobserved.
		task.GetAwaiter().GetResult();
	}

	//Pumps the dispatcher meanwhile, so the posts run on the test's UI thread.
	private static void WaitUntil(Func<bool> done, string what)
	{
		Stopwatch elapsed = Stopwatch.StartNew();
		while(!done()) {
			Dispatcher.UIThread.RunJobs();
			if(elapsed.Elapsed > Timeout) {
				throw new TimeoutException($"{what} did not finish within {Timeout.TotalSeconds} s.");
			}
			Thread.Sleep(1);
		}
		Dispatcher.UIThread.RunJobs();
	}
}

[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class SettleMainWindowsAttribute : BeforeAfterTestAttribute
{
	public override void After(MethodInfo methodUnderTest, IXunitTest test) => MainWindowStartup.SettleShownWindows();
}
