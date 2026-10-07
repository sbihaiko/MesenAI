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
		//#1037: the walk answers through the same stream the real one does, so the
		//held folder hands its games over late rather than all at once - which is
		//what the generation has to drop, now that a batch lands the moment it is
		//found instead of only when the last folder answers.
		picker.LibraryScanStreamSource = (scanned, _, onEntries) => {
			if(scanned.Contains("A")) {
				//The folder the player is about to leave: the scan holds its
				//answer until the case says the new folder has already answered.
				oldScanStarted.Set();
				oldScanMayFinish.Wait(TimeSpan.FromSeconds(30));
				LibraryScanResult stale = Scan("Contra", "Metroid");
				onEntries(stale.Entries);
				oldScanAnswered.Set();
				return stale;
			}
			LibraryScanResult fresh = Scan("Tetris");
			onEntries(fresh.Entries);
			return fresh;
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

	//#1032 (ADR-0264 Decision 11) review finding 2: the suggestion walk is
	//kicked on the first visit to the browser and only ever once, while the
	//library rebuild bumps the ONE generation both scans share. So a player who
	//steps into *Browse a file…* and comes straight back before the deep pass -
	//measured at 0.86 s on the requesting machine - lands leaves the sheet with
	//no suggestions at all: the pass is dropped with the surface it was read
	//for, and nothing runs again, because the walk already counts as started.
	//
	//The rebuild is an answer about the LIBRARY, not about the places the walk
	//is looking in, so the walk's own answer has to survive it and be there the
	//next time the roots are up.
	[AvaloniaFact]
	public void A_deep_pass_that_lands_after_the_library_rebuild_is_still_the_roots_answer()
	{
		string library = R("/mesen-1032-review/library");
		ManualResetEventSlim deepStarted = new(false);
		ManualResetEventSlim deepMayFinish = new(false);

		PlayerRomPickerViewModel picker = new() {
			LibraryFolderSource = () => new[] { "A" },
			//Every root the walk could offer belongs to the machine this suite
			//runs on; the case must not depend on its disks.
			VolumeSource = () => Array.Empty<string>(),
			WholeComputerFolder = null,
			LibraryScanStreamSource = (_, _, onEntries) => {
				LibraryScanResult result = Scan("Contra");
				onEntries(result.Entries);
				return result;
			}
		};
		picker.SuggestionSource = pass => {
			if(pass == RomScanPass.Shallow) {
				return Array.Empty<RomPickerHit>();
			}
			deepStarted.Set();
			deepMayFinish.Wait(TimeSpan.FromSeconds(30));
			//A folder outside every root this machine could offer, so the hit is
			//a suggestion wherever the suite runs.
			return new[] { new RomPickerHit(library, 3, RomConsole.Nes) };
		};

		picker.Open();
		picker.BrowseFile();
		Assert.True(deepStarted.Wait(TimeSpan.FromSeconds(30)),
			"the browser's deep pass never reached the scan source, so this case would prove nothing");

		//B out of the browser's roots rebuilds the library under the pass that is
		//still walking.
		picker.Back();
		Assert.Equal(RomPickerMode.Library, picker.Mode);

		deepMayFinish.Set();
		Settle();

		//Only now does the walk answer. Its answer is the roots list's own, so
		//the next visit to the browser has to offer it.
		picker.BrowseFile();
		Assert.Contains(picker.Suggestions, s => s.Folder == library);
		Assert.Contains(picker.Rows, r => r.Kind == RomPickerRowKind.Folder && r.Path == library);
	}

	//#1032 review finding 5: a library answer that lands while the browser is up
	//is an answer about no surface at all - the grid is not on screen, and the
	//waiting line the player is reading belongs to the walk the browser started.
	//It used to be cleared with it.
	[AvaloniaFact]
	public void A_library_answer_that_lands_while_the_browser_is_up_leaves_its_line_alone()
	{
		ManualResetEventSlim libraryScanStarted = new(false);
		ManualResetEventSlim libraryScanMayAnswer = new(false);
		ManualResetEventSlim walkStarted = new(false);
		ManualResetEventSlim walkMayAnswer = new(false);

		PlayerRomPickerViewModel picker = new() {
			LibraryFolderSource = () => new[] { "A" },
			VolumeSource = () => Array.Empty<string>(),
			WholeComputerFolder = null
		};
		picker.LibraryScanStreamSource = (_, _, onEntries) => {
			libraryScanStarted.Set();
			libraryScanMayAnswer.Wait(TimeSpan.FromSeconds(30));
			LibraryScanResult result = Scan("Contra");
			onEntries(result.Entries);
			return result;
		};
		picker.SuggestionSource = pass => {
			if(pass == RomScanPass.Deep) {
				walkStarted.Set();
				walkMayAnswer.Wait(TimeSpan.FromSeconds(30));
			}
			return Array.Empty<RomPickerHit>();
		};

		picker.Open();
		Assert.True(libraryScanStarted.Wait(TimeSpan.FromSeconds(30)),
			"the library scan never reached the scan source, so this case would prove nothing");

		//The player steps into the browser while the library scan is still
		//running: the browser says it is looking, and that is the line under test.
		picker.BrowseFile();
		Assert.True(walkStarted.Wait(TimeSpan.FromSeconds(30)), "the browser's walk never started");
		string line = picker.SearchingText;
		Assert.False(string.IsNullOrEmpty(line), "the browser is not showing its waiting line, so this case would prove nothing");

		//Only now does the library answer, with the browser up.
		libraryScanMayAnswer.Set();
		Settle();

		Assert.Equal(line, picker.SearchingText);

		walkMayAnswer.Set();
		Settle();
	}

	private static string R(string path) => System.IO.Path.GetFullPath(path);

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
