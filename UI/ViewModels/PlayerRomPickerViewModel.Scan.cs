using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Localization;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Threading;
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
	//a fact about the sheet's life, and this is the object that has one. So is
	//the cover of every tile (#1035) and the empty sentence one scan earns
	//(#1060): both belonged to the one-shot walk this replaced, and both travel
	//through the streamed one rather than being left behind by it.
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

		//The entries behind the grid live in _libraryGames (Search), in the module's
		//own order: a batch is merged there as it arrives, so the list reads in title
		//order at every instant rather than only once the scan ends, and the query
		//decides which of them are tiles.
		//
		//Which scan a batch belongs to is the sheet's own ScanGeneration (see
		//PlayerRomPickerViewModel.cs): a batch from a scan the player has already
		//left - a B press, a step into *Browse a file…* and back - must not land
		//in the grid of the scan that replaced it.
		//The walk of the scan that is running, so the next one can stop it: a
		//superseded scan that keeps reading the disk is exactly what Decision 9's
		//"bounded" forbids once the player has left the sheet.
		private CancellationTokenSource? _scanCancellation;
		//The game this scan is putting the ring back on, by path, while the grid
		//does not hold it yet. Non-empty means a restore is IN FLIGHT: the focus
		//arbiter must not fall back to the first tile meanwhile, because that
		//tile's GotFocus is read as "the player is on it" and would overwrite the
		//very path being restored.
		private string _restoreTargetPath = "";
		//Whether a tile took the ring at all during this scan. A scan that ends
		//without a restore ever landing may move the ring onto the grid's first
		//game - but only when the ring was never in the grid to begin with, or
		//the move is the sheet taking the ring out of the player's hands.
		private bool _tileTookRing;
		//Chunks of a big batch that were posted and have not landed yet. The scan
		//is not over while one is on its way: the finish waits for the last of them,
		//or it would stop the wait and settle the ring over a half-filled grid.
		private int _pendingChunks;
		//The end of a scan that never found its restore put the ring on the first
		//tile (see FinishLibraryStream). The focus arbiter asks this to know that
		//bump is the sheet's fallback and not a claim over a ring the player has
		//since moved to the header (PlayPadNavigationWiring).
		public bool IsFinishFallback { get; private set; }

		//The bump for the remembered game landing is the same kind of claim: the
		//sheet finishing what it promised, not a hand taken off a ring the player
		//has since walked to Back or the search box while the restore was pending
		//(PlayPadNavigationWiring answers it the way it answers the fallback).
		public bool IsRestoreLanding { get; private set; }

		//A batch is merged into the grid in chunks this large, the rest posted at
		//Background priority: one folder holding thousands of ROMs is one batch,
		//and one collection change - and one container - per entry in a single UI
		//turn stalls the sheet, which is what "stays responsive" forbids.
		private const int BatchChunkSize = 512;

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
			_libraryGames.Clear();
			ClearTiles();
			TilesRevision++;
		}

		//A scan the player has left stops reading the library: closing the sheet or
		//stepping into the browser ends it here, and nobody has to come back for the
		//walk to stop (Decision 9: bounded).
		partial void OnIsVisibleChanged(bool value)
		{
			if(!value) {
				_scanCancellation?.Cancel();
			}
		}

		partial void OnModeChanged(RomPickerMode value)
		{
			if(value != RomPickerMode.Library) {
				_scanCancellation?.Cancel();
			}
		}

		//A restore is in flight: the scan has not reached the game the player left
		//on yet, so the grid's first tile is not where the ring may land. The
		//focus arbiter asks this before it decides (PlayPadNavigationWiring).
		public bool IsRestorePending => _restoreTargetPath.Length > 0;

		//The game the player is on. Called by the view when a tile takes the ring,
		//including when the arbiter puts it there - so this is where the ring was,
		//not only where the player moved it by hand.
		//
		//A tile that takes the ring while a restore is in flight is either the
		//remembered game arriving (the bump below) or the player moving there
		//themselves - the arbiter never hands a tile the ring mid-restore. Either
		//way the restore is over: its path must not pull the ring away later.
		public void RememberFocus(PlayerLibraryTile tile)
		{
			_tileTookRing = true;
			_restoreTargetPath = "";
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

			//The scan the player just superseded - a reopen, a B press that came
			//back in - stops reading the disk here rather than running to the end
			//of a tree nobody is looking at any more (Decision 9: bounded).
			CancellationTokenSource cancellation = new();
			CancellationTokenSource? superseded = _scanCancellation;
			_scanCancellation = cancellation;
			//Not disposed: the walk it belongs to may still be asking its token
			//whether to go on, and a token whose source is gone throws on the ask.
			superseded?.Cancel();

			//The game the sheet is putting the ring back on (Decision 1). It is
			//held here, apart from LastFocusedTilePath, because the grid is about
			//to be emptied and the arbiter must not read the first tile that
			//arrives as the player's choice (see IsRestorePending).
			_restoreTargetPath = LastFocusedTilePath;
			_tileTookRing = false;
			_pendingChunks = 0;
			IsFinishFallback = false;
			IsRestoreLanding = false;

			ResetLibraryGrid();
			//The library has folders and is being read; the box owns the empty
			//result only once the scan has answered (Search.UpdateEmptyResult).
			_hasLibrary = true;
			IsScanning = true;
			SearchingText = ResourceHelper.GetMessage("RomPickerSearching");

			//A test, and nothing else, runs the whole scan inside the Open() turn:
			//the grid is then complete before the call returns, which is what
			//makes a case about the grid's contents readable.
			if(RunLibraryScanInline || RunScanInline) {
				FinishLibraryStream(generation, RunLibraryStream(generation, folders, recentGamesFolder, null, cancellation.Token));
				return;
			}

			Action<Action> post = action => Dispatcher.UIThread.Post(action);
			Task.Run(() => {
				//The answer travels back inside the same posted closure as the
				//finish: a field written on the walk's thread and read on the UI
				//thread is a race an older scan can lose into a newer one's header.
				LibraryScanResult? result = RunLibraryStream(generation, folders, recentGamesFolder, post, cancellation.Token);
				post(() => FinishLibraryStream(generation, result));
			});
		}

		//The walk, on whatever thread called this. `post` is how one folder's
		//finds reach the grid: null means this IS the UI thread (the inline open),
		//and the dispatcher otherwise.
		//
		//The answer comes back through the return value and reaches the header
		//through the caller's posted closure, never through a field: an older scan
		//that finishes late must be unable to describe a sheet it no longer owns.
		//Null means the walk produced no answer at all - it threw. A cancelled
		//walk is not guaranteed to land here: GameLibrary.List swallows the
		//lister's exception, so the walk drains and returns what it had. That
		//is harmless, since every cancel also moves the generation, visibility
		//or mode and FinishLibraryStream drops the result.
		private LibraryScanResult? RunLibraryStream(int generation, IReadOnlyList<string> folders, string? recentGamesFolder, Action<Action>? post, CancellationToken cancellation)
		{
			try {
				//The Recent index is built here, once per scan and on this thread:
				//it reads and unzips every record the folder names, which is the
				//disk work #1035 keeps off the thread that draws - and one index
				//for the whole walk is what keeps a library of two hundred folders
				//from unzipping each record once per folder.
				RecentCoverIndex index = RecentCoverIndex.Open(recentGamesFolder);
				return LibraryScanStreamSource(folders, new FolderLister(folder => {
					//Checked before every listing, which is every read the walk
					//makes: a cancelled scan stops touching the disk at the next
					//folder instead of walking a tree nobody is showing.
					cancellation.ThrowIfCancellationRequested();
					return FolderSource(folder);
				}), batch => Deliver(generation, batch, CoversFor(batch, index), post));
			} catch {
				//A walk that threw, or one the player superseded, answers nothing
				//to show: the wait goes (an unreadable disk is not a reason to
				//leave the player on a moving bar that never stops) and the grid
				//keeps whatever the batches before it already put there. The
				//header is left unsaid rather than told "0 games" over games
				//already on screen (see FinishLibraryStream).
				return null;
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
			//A batch that is too big to insert in one turn is inserted in chunks,
			//the rest behind the frame the sheet can repaint in. The grid itself
			//is filled by the same code either way.
			if(batch.Count <= BatchChunkSize) {
				InsertEntries(generation, batch, covers, 0, batch.Count);
				return;
			}
			InsertEntries(generation, batch, covers, 0, BatchChunkSize);
			for(int start = BatchChunkSize; start < batch.Count; start += BatchChunkSize) {
				int from = start;
				int count = Math.Min(BatchChunkSize, batch.Count - from);
				_pendingChunks++;
				Dispatcher.UIThread.Post(() => {
					//A chunk of a scan that was left behind counts for nothing here:
					//the counter is the current scan's.
					if(_scanGeneration.IsCurrent(generation)) {
						_pendingChunks--;
					}
					InsertEntries(generation, batch, covers, from, count);
				}, DispatcherPriority.Background);
			}
		}

		//One chunk of one folder's finds, merged into the grid. Each entry goes in
		//at its ordered position rather than at the end, so the grid is in title
		//order while it fills (Decision 1) and a tile that is already on screen
		//keeps its container - which is how the ring stays where the player put it
		//while the rest of the library arrives.
		private void InsertEntries(int generation, IReadOnlyList<LibraryEntry> batch, IReadOnlyList<LibraryCoverPick> covers, int from, int count)
		{
			//A batch that landed after the player left the library - a B press, a
			//step into *Browse a file…* - belongs to no surface, and a batch from
			//the scan before this one belongs to a grid that no longer exists.
			if(!_scanGeneration.IsCurrent(generation) || !IsVisible || Mode != RomPickerMode.Library) {
				return;
			}
			//With a query on, the grid is a filtered view of the list and a tile's
			//position is not the game's: the view is refilled instead, and the ring -
			//which is on the box while the player types - is not claimed.
			bool query = HasQuery;
			bool wasEmpty = Tiles.Count == 0;
			bool restoreLanded = false;
			for(int index = from; index < from + count; index++) {
				LibraryEntry entry = batch[index];
				int at = InsertIndex(entry);
				//The cover travels with its entry, in the same order (#1035): a
				//tile inserted mid-grid draws the picture the scan resolved for
				//THAT game and never its neighbour's.
				LibraryGame game = new(entry, covers[index]);
				_libraryGames.Insert(at, game);
				if(!query) {
					Tiles.Insert(at, TileFor(entry, game.Cover));
				}
				restoreLanded |= _restoreTargetPath.Length > 0 && entry.Path == _restoreTargetPath;
			}
			if(query) {
				FillTiles();
				return;
			}
			//The first games to arrive are the ones the ring has been waiting for:
			//the sheet opened with nothing to play, so it is holding Back, and this
			//is the revision that moves it onto a game. Later batches do not bump
			//it - the player may already be walking the grid, and a claim per
			//folder would pull the ring back out of their hands.
			//
			//The one later bump is the game the sheet is restoring, the moment it
			//lands: until then the arbiter has kept the ring off the grid
			//(IsRestorePending), so this is the sheet finishing what it promised
			//rather than a claim over the player's ring.
			if((wasEmpty && Tiles.Count > 0) || restoreLanded) {
				IsRestoreLanding = restoreLanded;
				TilesRevision++;
			}
		}

		//The position an entry's tile takes. The order is GameLibrary's own (title,
		//then path) - asked rather than restated, so a grid filled this way ends in
		//exactly the order the module's collected list has.
		private int InsertIndex(LibraryEntry entry)
		{
			int low = 0;
			int high = _libraryGames.Count;
			while(low < high) {
				int middle = low + (high - low) / 2;
				if(GameLibrary.Compare(_libraryGames[middle].Entry, entry) <= 0) {
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
		//
		//`result` is null when the walk answered nothing at all - it threw, or the
		//player superseded it. That is not "0 games in 0 folders": the grid keeps
		//the tiles the batches before it put there, so the header stays unsaid
		//rather than counting nothing over games on screen.
		//
		//No revision bump for the grid's final shape. The tiles are already in it,
		//and a bump re-arbitrates - which pulls the ring back into the grid from
		//wherever the player put it, header included. The one exception is a
		//restore that never landed: the ring has been kept off the grid for it, so
		//it has nowhere to be but the sheet's Back, and the sheet still owes the
		//player a game under the ring (Decision 3). Only when no tile took the
		//ring at all this scan is that a bump and not a hand taken off the ring.
		private void FinishLibraryStream(int generation, LibraryScanResult? result)
		{
			if(!_scanGeneration.IsCurrent(generation)) {
				return;
			}
			//The wait is this object's and goes whatever happens next, even if the
			//scan threw: a moving bar that never stops is worse than no bar.
			if(_pendingChunks > 0) {
				//Behind the chunks still queued, which are at Background priority
				//too: the last of them lands first, and the grid is whole when the
				//wait goes.
				Dispatcher.UIThread.Post(() => FinishLibraryStream(generation, result), DispatcherPriority.Background);
				return;
			}
			IsScanning = false;
			if(_restoreTargetPath.Length > 0) {
				_restoreTargetPath = "";
				if(!_tileTookRing && Tiles.Count > 0 && !HasQuery) {
					IsFinishFallback = true;
					TilesRevision++;
				}
			}
			//A scan that landed after the player left the library - a B press, a
			//step into *Browse a file…* - belongs to no surface: the browser's own
			//rows must not be replaced by a grid nobody is looking at, and its
			//waiting line is not this scan's to clear (review finding 5 on #1032).
			if(!IsVisible || Mode != RomPickerMode.Library) {
				return;
			}
			SearchingText = "";
			if(result is null) {
				//The scan answered nothing, but a query typed over the wait is owed
				//its own verdict on the grid that is there.
				UpdateEmptyResult();
				return;
			}
			//#1060: a scan that answered no game is a named state that names the next
			//step, not a blank grid. The rule is PlayRomPicker's; this is the lookup -
			//and it is taken here, at the finish, because until the walk returns "no
			//games" is a claim the scan has not made.
			_scanEmptyText = LibraryEmptyText(PlayRomPicker.LibraryEmptyMessageId(_folders.Count, result.Entries.Count));
			UpdateEmptyResult();
			CountText = ResourceHelper.GetMessage("RomPickerLibraryCount",
				CountLabel(result.Entries.Count, "RomPickerGameOne", "RomPickerGameMany"),
				CountLabel(result.FolderCount, "RomPickerFolderOne", "RomPickerFolderMany"));
			TruncatedText = result.Truncated
				? ResourceHelper.GetMessage("RomPickerLibraryTruncated", GameLibrary.MaxEntries)
				: "";
		}
	}
}
