using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1032, review finding 1: the library sheet's scan is background work, so its
//answer lands on the UI thread after the player may already have left the
//folder it was reading. This is the case that pins the scan's generation:
//folder A's scan is held back mid-flight, folder B's answers first, and A's
//answer - arriving last - must be dropped instead of redrawing the grid with a
//folder the player is no longer looking at.
//
//The sheet is built directly, without a window: the rule under test is
//view-model state, so the case needs neither the native core nor a rendered
//tree, and it runs in the parallel pool with every other core-free class. That
//is also why it is marked [NativeCoreFree] rather than joining the serial
//collection: the one core path the walk finds is the view-model's own
//constructor building its AddKnownGameFolder delegate, and no case here ever
//designates a games folder, so the call is never made.
[NativeCoreFree("PlayerRomPickerViewModel's constructor only builds its AddKnownGameFolder delegate; no case here designates a games folder")]
public class PlayerLibraryScanGenerationTests
{
	[AvaloniaFact]
	public void A_slow_scan_of_the_old_folder_cannot_overwrite_the_new_folder_s_tiles()
	{
		List<string> folders = new() { "A" };
		ManualResetEventSlim oldScanStarted = new(false);
		ManualResetEventSlim oldScanMayFinish = new(false);
		ManualResetEventSlim oldScanAnswered = new(false);

		PlayerRomPickerViewModel picker = new() {
			LibraryFolderSource = () => folders.ToArray(),
			//Every root the walk could offer belongs to the machine this suite
			//runs on; the case must not depend on its disks.
			VolumeSource = () => Array.Empty<string>(),
			WholeComputerFolder = null,
			//The picker's own background walk is stubbed for the same reason,
			//and so the case is about the library scan alone.
			SuggestionSource = _ => Array.Empty<RomPickerHit>()
		};
		picker.LibraryScanSource = (scanned, _) => {
			if(scanned.Contains("A")) {
				//The folder the player is about to leave: the scan holds its
				//answer until the case says the new folder has already answered.
				oldScanStarted.Set();
				oldScanMayFinish.Wait(TimeSpan.FromSeconds(30));
				LibraryScanResult stale = Scan("Contra", "Metroid");
				oldScanAnswered.Set();
				return stale;
			}
			return Scan("Tetris");
		};

		picker.Open();
		Assert.True(oldScanStarted.Wait(TimeSpan.FromSeconds(30)),
			"the first folder's scan never reached the scan source, so this case would prove nothing");

		//The player leaves the library and comes back to a DIFFERENT folder:
		//the sheet re-scans, and its answer is the one that owns the grid.
		folders.Clear();
		folders.Add("B");
		picker.BrowseFile();
		picker.Back();

		WaitFor(() => picker.Tiles.Any(tile => tile.Title == "Tetris"),
			"the second folder's scan never reached the grid");
		Assert.Equal(new[] { "Tetris" }, Titles(picker));
		string count = picker.CountText;

		//Only now does the abandoned scan answer. The wait is on the answer
		//itself, not on a stopwatch: the case must not read the grid before the
		//stale result even exists, or it would pass on a machine slow enough to
		//hide the bug. The drain after it covers the posting that follows.
		oldScanMayFinish.Set();
		WaitFor(() => oldScanAnswered.IsSet, "the first folder's scan never answered");
		Settle();

		Assert.Equal(new[] { "Tetris" }, Titles(picker));
		Assert.Equal(count, picker.CountText);
	}

	private static LibraryScanResult Scan(params string[] titles)
	{
		return new LibraryScanResult(
			titles.Select(title => new LibraryEntry("/games/" + title, RomConsole.Nes, title)).ToArray(),
			titles.Length, false);
	}

	private static string[] Titles(PlayerRomPickerViewModel picker)
	{
		return picker.Tiles.Select(tile => tile.Title).ToArray();
	}

	//The answer is posted from the scan's thread, and this body runs on the UI
	//thread: the queue is drained by hand until the posting thread has had every
	//chance to hand its result over. Bounded, so a broken build fails here
	//rather than hanging the suite.
	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Pump();
			Thread.Sleep(20);
		}
		Pump();
	}

	private static void Settle()
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(clock.ElapsedMilliseconds < 1500) {
			Pump();
			Thread.Sleep(25);
		}
		Pump();
	}

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}
}
