using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//#1033 (ADR-0264 Decision 4): the library's search box.
	//
	//The sheet opens on the whole library, and the box is how a title in a few
	//hundred of them is reached without walking. The query narrows the grid AS IT
	//IS TYPED - on a keyboard straight into the box, on a pad through the shared
	//on-screen keyboard ADR-0262 owns, which opens on Confirm over the focused
	//field (and which Y puts the ring on, see PlayPadNavigationWiring) - and an
	//empty result is a NAMED state rather than an empty grid.
	//
	//The match rule itself is NOT here: it is LibrarySearch's, and this half only
	//asks it. `zel` finding *The Legend of Zelda*, an accent-less keyboard finding
	//*Pokémon*, and a dump tag matching nothing are that module's decisions, pinned
	//by UI.Tests/Play/LibrarySearchTests, and a second copy of any of them here is
	//how the two would drift apart.
	//
	//This is a partial of PlayerRomPickerViewModel on purpose: the sheet has one
	//view-model, and the search is a property of it rather than a surface of its
	//own. The scan and the box are two sources for ONE grid, so the tiles are
	//filled on one path (FillTiles) whatever changed.
	public partial class PlayerRomPickerViewModel
	{
		//The query, exactly as the box holds it. Read-only to everything but the
		//box and ClearSearch: the grid follows it, and nothing else writes it.
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasQuery))]
		[NotifyPropertyChangedFor(nameof(ShowSearchClear))]
		public partial string SearchQuery { get; set; } = "";

		//Whether the box holds anything, which is what shows the Clear action: a
		//search the player cannot see is a search they cannot undo.
		public bool HasQuery => SearchQuery.Length > 0;

		//Whether the Clear action belongs on screen at all (#1033). The library is
		//where the box is, so the browser *Browse a file…* steps into is where it is
		//not: Clear there would sit beside a search box that is hidden, undo a query
		//nobody can see, and refill a grid that is not the surface on screen.
		//IsLibraryMode is part of the answer AND of the change notification, so the
		//step into the browser takes the button away with the box it belongs to.
		public bool ShowSearchClear => IsLibraryMode && HasQuery;

		//The whole of "the grid narrows live". The generator answers this hook on
		//every change, including the property's own initial value, so there is no
		//second place where a query could reach the tiles.
		partial void OnSearchQueryChanged(string value) => FillTiles();

		//The Clear action, which is also the way out of the empty result: the box
		//empties and the whole library comes back. Nothing is rescanned - the
		//entries the scan answered are still in hand.
		public void ClearSearch() => SearchQuery = "";

		//A fresh visit to the library (#1032's ShowLibrary). The box empties - the
		//sheet opens on the whole library, so a filter left over from the previous
		//visit is not one the player asked for now - and the entries go with it:
		//they belong to the scan of a library this visit has not read yet, and
		//narrowing a grid over them would show games from a folder the player may
		//since have changed.
		private void BeginLibraryVisit()
		{
			_libraryGames.Clear();
			_titles = new LibraryTitleBook();
			_hasLibrary = false;
			_scanEmptyText = "";
			SearchQuery = "";
		}

		//One scanned game and the cover the same scan chose for it (#1052). They
		//travel as one value because the query narrows the grid by dropping games,
		//and a dropped game must take its cover with it - a parallel list indexed
		//by position would hand the survivor of a filter the cover of the game
		//that was filtered out.
		private sealed record LibraryGame(LibraryEntry Entry, LibraryCoverPick Cover);

		//What the scan has answered so far, kept whole and in the module's order.
		//The streamed batches are merged in here as they arrive (Scan), and the query
		//narrows the grid OVER this list rather than over the tiles, so clearing the
		//box and typing the next query both cost nothing but a walk of the list.
		private readonly List<LibraryGame> _libraryGames = new();
		//The title each of them is shown and searched by (see LibraryTitleBook):
		//the canonical-title pass writes here, and FillTiles reads from here.
		private LibraryTitleBook _titles = new();
		//Whether those entries come from a scan of THIS library. False while the
		//sheet has no library folder at all, where the empty state belongs to
		//ShowLibrary's own sentence and not to this box.
		private bool _hasLibrary;

		//The library's own state, re-derived from the entries in hand after the
		//query changes or a scan lands. The header's COUNT is deliberately not
		//here: it reads the library, not the grid (Decision 8, and W-P19b draws it
		//unchanged with the query `zel` on screen) - a search narrows which games
		//are shown, never how many the player owns.
		private void FillTiles()
		{
			if(Mode != RomPickerMode.Library || !_hasLibrary) {
				//No library behind the grid: the tiles that are there belong to a
				//surface this visit no longer has, and the pictures they drew go
				//with them.
				ClearTiles();
				return;
			}
			//The library in the order the grid DRAWS it, with the query asked once
			//per entry through the one rule that owns the question. A blank query
			//keeps everything, which is what "no search yet" means (the rule's own
			//Decision 4 case).
			//The console filter narrows first (#1034, Decision 5), so the query
			//searches the games of the segment that is up.
			//Searched by the title the tile shows (ADR-0264 Decisions 4 and 7): the
			//canonical one once the canonical-title pass has resolved it - and
			//ORDERED by it too, which is #1065: the scan sorted the grid by the file
			//name, so a title that arrives while the sheet is open has to move its
			//tile to the seat the title gives it.
			IReadOnlyList<LibraryGame> inConsole = LibraryConsoleFilter.Apply(_libraryGames, SelectedConsole, game => game.Entry.Console);
			IReadOnlyList<LibraryGame> shown = _titles.Search(inConsole, g => g.Entry, SearchQuery);
			RestoreTiles(LibraryGridOrder.Sorted(shown,
				game => _titles.TitleOf(game.Entry), game => game.Entry.Path, GameLibrary.PathComparer));
			UpdateEmptyResult();
		}

		//The grid the two narrowings and the titles leave, reached by MOVING, adding
		//and dropping the tiles that are already there rather than by rebuilding it
		//(#1065).
		//
		//A rebuild is what this replaces, and the difference is the whole of the
		//review that produced this issue (the five rounds on PR #1055): a rebuild
		//hands the grid a new list, so every tile becomes a new container, the cover
		//of every tile is released and decoded again, and the ring - which is a fact
		//about CONTAINERS - is left on one the sheet has just thrown away. A move
		//keeps the container, and the ring with it, which is what makes re-sorting
		//under a player who is already walking the grid something the sheet may do at
		//all (ADR-0264 Decision 1).
		private void RestoreTiles(IReadOnlyList<LibraryGame> shown)
		{
			//The ring, read BEFORE anything moves. Whether the tile it is on survives
			//is the one thing that decides whether the arbiter has to place the ring
			//again - see the end of this method.
			PlayerLibraryTile? ring = FocusTile;

			//What the grid is to hold, so a tile the query dropped is recognized
			//without a walk of the grid per game.
			HashSet<string> keep = new(StringComparer.Ordinal);
			foreach(LibraryGame game in shown) {
				keep.Add(game.Entry.Path);
			}

			//The tiles the query (or the console segment) no longer keeps. Their
			//containers go, and so does the picture behind each of them: a decoded
			//screenshot nobody draws is an allocation outside the managed heap, and
			//the ledger is what owns it (#1035). This is the release the review asked
			//for by name - the dropped tile's cover goes with the tile.
			for(int index = Tiles.Count - 1; index >= 0; index--) {
				PlayerLibraryTile tile = Tiles[index];
				if(keep.Contains(tile.Path)) {
					continue;
				}
				Tiles.RemoveAt(index);
				_tileByPath.Remove(tile.Path);
				_coverArt.Release(tile.CoverArt);
				//The downloaded cover too, and the download the tile had in flight
				//(#1039): a picture nobody draws is an allocation the grid must not
				//keep, whichever source it came from.
				ForgetCover(tile);
			}

			//And now the order. The target sequence is walked once, each game's tile
			//MOVED to the seat it belongs in and a game the grid does not hold yet
			//inserted there. Every tile still in the grid sits at or after the seat
			//being filled, so a move is always backwards and no seat is visited
			//twice.
			int at = 0;
			foreach(LibraryGame game in shown) {
				if(_tileByPath.TryGetValue(game.Entry.Path, out PlayerLibraryTile? tile)) {
					int from = Tiles.IndexOf(tile);
					if(from != at) {
						Tiles.Move(from, at);
					}
				} else {
					Tiles.Insert(at, TileFor(game.Entry, game.Cover));
				}
				at++;
			}

			//TilesRevision is deliberately NOT bumped for a re-order, and the
			//difference from the scan path is the whole point: a scan replaces the
			//tiles under a ring that has nowhere else to go, while a MOVE carries the
			//container - and the ring on it - to the tile's new seat. Nothing the
			//player can feel happened, so nothing is claimed.
			//
			//The one tile that cannot keep its container is the one that left the
			//grid, and the ring it was carrying has nowhere to be. That IS a claim -
			//and it is made only when the ring was actually in the grid. A ring the
			//player walked to *Browse a file...*, to Back or into the search box is
			//not a tile, FocusTile is null, and the re-sort does not touch it (#1065
			//acceptance criteria 1 and 4).
			if(ring is not null && !(_tileByPath.TryGetValue(ring.Path, out PlayerLibraryTile? held) && ReferenceEquals(held, ring))) {
				FocusTile = null;
				TilesRevision++;
			}
		}

		//The tile the ring is on, or null when the ring is anywhere else - Back,
		//*Browse a file...*, the search box, the console row, or nothing at all.
		//
		//It is state the VIEW reports and never a lookup: the sheet asks the window
		//who has the focus every time it decides where to put it
		//(PlayPadNavigationWiring), and this is the view-model's own half of the same
		//fact - which surface's claim the ring is inside. A stale value here is a
		//re-sort that takes the ring off *Browse a file...* for a grid the player is
		//not in, which is exactly the defect the #1055 review found in its own
		//FocusTile, so it is cleared the moment the ring leaves the grid
		//(PlayerRomPickerView.OnTileBlurred).
		public PlayerLibraryTile? FocusTile { get; private set; }

		//The ring left the grid. A move from one tile to the next is not "leaving"
		//and the view answers for it; what arrives here is the case where no tile of
		//this sheet holds the ring any more.
		public void ForgetTileFocus() => FocusTile = null;

		//The named empty RESULT (Decision 4): a query that kept nothing says so,
		//with the query shown, and the Clear action beside the box undoes it. Never
		//an empty grid.
		//
		//It writes EmptyText only when the library itself answered: a sheet with no
		//library folder has its own sentence there (Decision 8), and this box must
		//not overwrite the next step the player is being told to take.
		//
		//The two empty states are different facts and both are kept: with a query on,
		//a grid the query emptied says so with the query in it; with the box empty,
		//the sentence is the LIBRARY's own - the scan that answered no game (#1060) -
		//which is why it is remembered rather than written straight to EmptyText. A
		//player who searches an empty library and clears the box gets the library's
		//sentence back, not a blank grid.
		private void UpdateEmptyResult()
		{
			//While the scan runs the library has not answered: "no match" over a
			//grid still filling would read as a verdict, and clearing the box must
			//not erase what the wait set. FinishLibraryStream calls this again.
			if(!_hasLibrary || IsScanning) {
				return;
			}
			EmptyText = HasQuery
				? (Tiles.Count == 0 ? ResourceHelper.GetMessage("RomPickerLibraryNoMatch", SearchQuery) : "")
				: _scanEmptyText;
		}

		//The library's own empty state as the last scan answered it, kept for the
		//reason above: the query's path is the one that owns EmptyText once the
		//library has answered, so the scan's sentence has to be here to be put back.
		private string _scanEmptyText = "";
	}
}
