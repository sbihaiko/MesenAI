using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//#1037 (ADR-0264 Decision 9): the library's scan, streamed. The walk itself
	//is GameLibrary's, bounded by the two caps the ADR names; this is the half
	//that makes it BOUNDED, BACKGROUNDED and VISIBLE to a player:
	//
	//- backgrounded: the walk runs on the thread pool and never in a press, so
	//  the sheet opens at once and answers the pad while the disks are read;
	//- streaming: each folder's finds are posted to the grid as they are handed
	//  over, so the grid FILLS while the scan runs and the player can start
	//  playing before it ends;
	//- visible: an indeterminate indicator runs for as long as the scan does
	//  (#734: every wait the player can see shows a moving one) and the count
	//  line appears when it stops;
	//- and it says so when it stopped at the count cap, rather than truncating
	//  in silence.
	//
	//The focused game is remembered here too, because "the entry the player
	//focused last time is focused again when the sheet reopens" (Decision 1) is
	//a fact about the sheet's life, and this is the object that has one.
	//
	//Split out of PlayerRomPickerViewModel.cs so the tracer's file keeps its own
	//subject (which surface is up, which row does what): everything here is one
	//scan's lifetime.
	public partial class PlayerRomPickerViewModel
	{
		//The streaming scan, behind a seam for the reason every seam on this
		//sheet exists: a test drives the grid with its own batches and its own
		//timing - including a scan that is slow on purpose - without a disk and
		//without depending on how fast the machine running the suite lists
		//folders. The default is the real module, and it is the module's own caps
		//that bound it, never this file.
		public Func<IReadOnlyList<string>, FolderLister, Action<IReadOnlyList<LibraryEntry>>, LibraryScanResult> LibraryScanStreamSource { get; set; }
			= GameLibrary.ScanStreaming;

		//The scan is running, which is what puts the indicator on screen. It goes
		//when the walk returns, whatever it returned - a scan that threw is not a
		//reason to leave the player waiting on a moving bar that never stops.
		[ObservableProperty] public partial bool IsScanning { get; private set; }

		//The game the player focused last, by path. The sheet reopens on it
		//(Decision 1), and it is kept as a path rather than as a tile because a
		//tile is a container a rebuilt grid throws away.
		[ObservableProperty] public partial string LastFocusedTilePath { get; private set; } = "";

		//The entries behind Tiles, in the module's own order and in the same
		//positions: a batch is merged here as it arrives, so the grid reads in
		//title order at every instant rather than only once the scan ends.
		private readonly List<LibraryEntry> _ordered = new();
		//What the walk answered. Written on the scan's thread and read on the UI
		//thread at the finish, which the dispatcher post orders.
		//
		//Which scan a batch belongs to is the sheet's own ScanGeneration (see
		//PlayerRomPickerViewModel.cs): a batch from a scan the player has already
		//left - a B press, a step into *Browse a file…* and back - must not land
		//in the grid of the scan that replaced it.
		private LibraryScanResult _scanResult = NoEntries;

		private static readonly LibraryScanResult NoEntries = new(Array.Empty<LibraryEntry>(), 0, false);

		//The grid emptied, in one place: the tiles, the entries behind them and
		//the revision the focus arbiter watches all move together, so the two
		//lists can never disagree about which index a game belongs at.
		//
		//The tiles go through ClearTiles and not Tiles.Clear, because a tile is
		//not only a row: the cover the previous visit decoded is released with it
		//(#1035). A rebuild that took the rows and left the pictures behind would
		//leak one decoded image per visit.
		private void ResetLibraryGrid()
		{
			_ordered.Clear();
			ClearTiles();
			TilesRevision++;
		}

		//The game the player is on. Called by the view when a tile takes the ring,
		//including when the arbiter puts it there - so this is where the ring was,
		//not only where the player moved it by hand.
		public void RememberFocus(PlayerLibraryTile tile)
		{
			LastFocusedTilePath = tile.Path;
		}

		//The scan of one open. The sheet is already up: this shows the wait, hands
		//the walk to the thread pool and returns, and the grid fills from whatever
		//the walk reports from then on.
		private void StartLibraryStream()
		{
			IReadOnlyList<string> folders = _folders;
			//The generation is taken BEFORE the work is handed out, so a batch and
			//the finish carry the surface they were read for - and the check that
			//drops a stale one happens where it lands, never here: the posting
			//thread cannot know what the UI thread did while the scan ran.
			int generation = _scanGeneration.Next();
			//The Recent folder is read here, on the UI thread, and carried into the
			//scan: ConfigManager is not a background thread's to read, and the
			//`*.rgd` files it names are (#1035).
			string? recentGamesFolder = ConfigManager.RecentGamesFolder;
			ResetLibraryGrid();
			_scanResult = NoEntries;
			IsScanning = true;
			SearchingText = ResourceHelper.GetMessage("RomPickerSearching");

			//A test, and nothing else, runs the whole scan inside the Open() turn:
			//the grid is then complete before the call returns, which is what
			//makes a case about the grid's contents readable.
			if(RunLibraryScanInline || RunScanInline) {
				RunLibraryStream(generation, folders, recentGamesFolder, null);
				FinishLibraryStream(generation);
				return;
			}

			Task.Run(() => {
				RunLibraryStream(generation, folders, recentGamesFolder, action => Dispatcher.UIThread.Post(action));
				Dispatcher.UIThread.Post(() => FinishLibraryStream(generation));
			});
		}

		//The walk, on whatever thread called this. `post` is how one folder's
		//finds reach the grid: null means this IS the UI thread (the inline open),
		//and the dispatcher otherwise.
		private void RunLibraryStream(int generation, IReadOnlyList<string> folders, string? recentGamesFolder, Action<Action>? post)
		{
			try {
				//The Recent index is built here, once per scan and on this thread:
				//it reads and unzips every record the folder names, which is the
				//disk work #1035 keeps off the thread that draws - and one index
				//for the whole walk is what keeps a library of two hundred folders
				//from unzipping each record once per folder.
				RecentCoverIndex index = RecentCoverIndex.Open(recentGamesFolder);
				_scanResult = LibraryScanStreamSource(folders, new FolderLister(FolderSource),
					batch => Deliver(generation, batch, CoversFor(batch, index), post));
			} catch {
				//A walk that threw answers nothing to show - an unreadable disk is
				//not a reason to keep a wait on screen - and leaves whatever the
				//batches before it already put in the grid.
				_scanResult = NoEntries;
			}
		}

		private void Deliver(int generation, IReadOnlyList<LibraryEntry> batch, IReadOnlyList<LibraryCoverPick> covers, Action<Action>? post)
		{
			if(post is null) {
				ApplyLibraryBatch(generation, batch, covers);
			} else {
				post(() => ApplyLibraryBatch(generation, batch, covers));
			}
		}

		//One folder's finds, merged into the grid. Each entry goes in at its
		//ordered position rather than at the end, so the grid is in title order
		//while it fills (Decision 1) and a tile that is already on screen keeps
		//its container - which is how the ring stays where the player put it
		//while the rest of the library arrives.
		private void ApplyLibraryBatch(int generation, IReadOnlyList<LibraryEntry> batch, IReadOnlyList<LibraryCoverPick> covers)
		{
			//A batch that landed after the player left the library - a B press, a
			//step into *Browse a file…* - belongs to no surface, and a batch from
			//the scan before this one belongs to a grid that no longer exists.
			if(!_scanGeneration.IsCurrent(generation) || !IsVisible || Mode != RomPickerMode.Library) {
				return;
			}
			bool wasEmpty = _ordered.Count == 0;
			for(int i = 0; i < batch.Count; i++) {
				LibraryEntry entry = batch[i];
				int at = InsertIndex(entry);
				_ordered.Insert(at, entry);
				//The cover travels with its entry, in the same order (#1035): a
				//tile inserted mid-grid draws the picture the scan resolved for
				//THAT game and never its neighbour's.
				Tiles.Insert(at, TileFor(entry, covers[i]));
			}
			//The first games to arrive are the ones the ring has been waiting for:
			//the sheet opened with nothing to play, so it is holding Back, and this
			//is the revision that moves it onto a game. Later batches do not bump
			//it - the player may already be walking the grid, and a claim per
			//folder would pull the ring back out of their hands.
			if(wasEmpty && _ordered.Count > 0) {
				TilesRevision++;
			}
		}

		//The position an entry's tile takes. The order is GameLibrary's own (title,
		//then path) - asked rather than restated, so a grid filled this way ends in
		//exactly the order the module's collected list has.
		private int InsertIndex(LibraryEntry entry)
		{
			int low = 0;
			int high = _ordered.Count;
			while(low < high) {
				int middle = low + (high - low) / 2;
				if(GameLibrary.Compare(_ordered[middle], entry) <= 0) {
					low = middle + 1;
				} else {
					high = middle;
				}
			}
			return low;
		}

		//The scan is over: the wait goes, and the header says what it found -
		//including that it stopped at the count cap, which the player is owed
		//(Decision 9: the header says so rather than the grid silently ending) and
		//the empty sentence when it found nothing at all (#1060).
		private void FinishLibraryStream(int generation)
		{
			if(!_scanGeneration.IsCurrent(generation)) {
				return;
			}
			//The wait is this object's and goes whatever happens next, even if the
			//scan threw: a moving bar that never stops is worse than no bar.
			IsScanning = false;
			//A scan that landed after the player left the library - a B press, a
			//step into *Browse a file…* - belongs to no surface: the browser's own
			//rows must not be replaced by a grid nobody is looking at, and its
			//waiting line is not this scan's to clear (review finding 5 on #1032).
			if(!IsVisible || Mode != RomPickerMode.Library) {
				return;
			}
			SearchingText = "";
			//The final shape of the grid. It does not move the ring: the arbiter's
			//target is the tile the player focused last, which on a scan that just
			//ended is the one under the ring already.
			TilesRevision++;
			//#1060: a scan that answered no game is a named state that names the next
			//step, not a blank grid. The rule is PlayRomPicker's; this is the lookup -
			//and it is taken here, at the finish, because until the walk returns "no
			//games" is a claim the scan has not made.
			EmptyText = LibraryEmptyText(PlayRomPicker.LibraryEmptyMessageId(_folders.Count, _scanResult.Entries.Count));
			CountText = ResourceHelper.GetMessage("RomPickerLibraryCount",
				CountLabel(_scanResult.Entries.Count, "RomPickerGameOne", "RomPickerGameMany"),
				CountLabel(_scanResult.FolderCount, "RomPickerFolderOne", "RomPickerFolderMany"));
			TruncatedText = _scanResult.Truncated
				? ResourceHelper.GetMessage("RomPickerLibraryTruncated", GameLibrary.MaxEntries)
				: "";
		}
	}
}
