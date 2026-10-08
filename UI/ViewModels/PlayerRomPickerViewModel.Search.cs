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
			ClearTiles();
			if(Mode == RomPickerMode.Library && _hasLibrary) {
				//The library in the order the scan answered it, with the query
				//asked once per entry through the one rule that owns the question.
				//A blank query keeps everything, which is what "no search yet"
				//means (the rule's own Decision 4 case).
				//The console filter narrows first (#1034, Decision 5), so the query
				//searches the games of the segment that is up.
				//Searched by the title the tile shows (ADR-0264 Decisions 4 and
				//7): the canonical one once the pass has resolved it.
				IReadOnlyList<LibraryGame> inConsole = LibraryConsoleFilter.Apply(_libraryGames, SelectedConsole, game => game.Entry.Console);
				foreach(LibraryGame game in _titles.Search(inConsole, g => g.Entry, SearchQuery)) {
					Tiles.Add(TileFor(game.Entry, game.Cover));
				}
				UpdateEmptyResult();
			}
			//TilesRevision is deliberately NOT bumped here, and the difference from
			//the scan path is the whole point: a scan replaces the tiles under a
			//ring that has nowhere else to go, while a query changes with the ring
			//on the BOX - the player is typing in it. Re-claiming there would take
			//the focus off the field on the first keystroke and leave the keyboard
			//(or the pad keyboard) with nowhere to put the next one. The pad
			//returns to the grid with Down, which RomPickerHeaderStep answers.
		}

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
