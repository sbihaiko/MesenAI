using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mesen.ViewModels
{
	//One segment of the library's console filter: `All`, or one console the
	//library actually holds, with the word the row draws for it.
	//
	//A record rather than a bare `RomConsole?` because the row has to show a
	//word and the view-model must not make the view resolve one: the console
	//names come from the locale files (RomConsole*, the same messages the tiles'
	//own console tags read), and resolving them here is what keeps a segment and
	//a tile tag from ever disagreeing about what "Game Boy" is called.
	//
	//Value equality is load-bearing, not a convenience: a rescan replaces the
	//instances, and the segmented control matches its selection by equality - so
	//a rebuilt row that holds the same consoles keeps the player's segment
	//rather than dropping back to the first one under their hands.
	public sealed record PlayerConsoleFilterOption(RomConsole? Console, string Label);

	//#1034 (ADR-0264 Decisions 3 and 5): the console filter of the flat library
	//sheet - the segmented row that lists `All` and the consoles the library
	//holds, cycled with LB/RB.
	//
	//The rules are host-free (UI/Logic/LibraryConsoleFilter, tested in
	//UI.Tests/Play/LibraryConsoleFilterTests): which options exist, what the ring
	//does at either end, what a selection the rescan invalidated means, and how
	//the filter composes with the search. This file is the other half - the state
	//the sheet binds to and the one place the two narrowings are applied to the
	//grid, so the row, the grid and the pad cannot disagree about which console
	//is up.
	//
	//It is a partial of the sheet's own view-model rather than a second one
	//because there is ONE grid: Decision 5's `mario` under NES is a single
	//result set produced by two narrowings, and splitting them across two
	//view-models would be splitting the one view they both draw.
	public partial class PlayerRomPickerViewModel
	{
		//Set while the option row is being rebuilt, so re-selecting a segment the
		//row already holds does not re-filter the grid a second time in the same
		//turn (the assignment below notifies, and the notification is the
		//re-filter's trigger).
		private bool _rebuildingConsoleOptions;

		//The segments, in the order the row draws them: All, then the consoles
		//present, in the product's order (Decision 5). Rebuilt in place on every
		//scan so the row the player is looking at keeps its container - and the
		//focus ring with it, the same reason the grid is mutated in place.
		public ObservableCollection<PlayerConsoleFilterOption> ConsoleOptions { get; } = new();

		//The segment that is up, and the whole of the filter's state: the pad
		//writes it (CycleConsole), the pointer writes it (the row's own
		//selection), and both end in the same re-filter below. Null is `All` -
		//the absence of a narrowing, never a member of the enum (RomConsole
		//already answers `Unknown` for "this file names no console").
		[ObservableProperty] public partial PlayerConsoleFilterOption? SelectedConsoleOption { get; set; }

		//What the row's selection means, for a caller that wants the console
		//rather than the segment. Null is `All`.
		public RomConsole? SelectedConsole => SelectedConsoleOption?.Console;

		//A segment change re-filters, whoever made it. This is the one trigger:
		//a pad cycle, a click on a segment and a rescan that had to move the
		//selection all land here, so no path exists where the row moved and the
		//grid did not.
		partial void OnSelectedConsoleOptionChanged(PlayerConsoleFilterOption? value)
		{
			if(_rebuildingConsoleOptions) {
				return;
			}
			//The row's two-way selection reads null when the pointer clears it,
			//and null means All: the grid would show everything while no segment
			//is lit. The first segment comes back instead, so the row and the
			//grid keep agreeing (Decision 5).
			if(value is null && ConsoleOptions.Count > 0) {
				SelectedConsoleOption = ConsoleOptions[0];
				return;
			}
			RebuildLibraryTiles();
			//The rebuild destroys the focused tile's container, and the arbiter
			//only re-claims focus on a TilesRevision bump, so a selection change
			//from the grid would leave the ring on nothing. The bump is safe for
			//the box: RomPickerFocusTarget answers with the search box while it
			//holds the ring or the pad keyboard. Only a selection change bumps -
			//the scan and reset paths rebuild the row with their own bump.
			_claims = _claims.AfterFilterRebuild();
			TilesRevision++;
		}

		//#1034 (ADR-0264 Decision 3): LB/RB, one option per press, wrapping at
		//both ends. The ring itself is the module's (Next/Previous), so the
		//sheet is not a second place that knows the last console wraps back to
		//All.
		//
		//A stale selection is not a special case here either: both rules resolve
		//through the same option set built from the entries on screen right now,
		//so a console the last rescan dropped steps off All rather than off a
		//position the row cannot show.
		public void CycleConsole(int step)
		{
			if(!IsVisible || Mode != RomPickerMode.Library || ConsoleOptions.Count == 0) {
				return;
			}
			IReadOnlyList<RomConsole?> options = ConsoleOptions.Select(option => option.Console).ToList();
			RomConsole? selected = step < 0
				? LibraryConsoleFilter.Previous(options, SelectedConsole)
				: LibraryConsoleFilter.Next(options, SelectedConsole);
			SelectedConsoleOption = ConsoleOptions.FirstOrDefault(option => option.Console == selected) ?? ConsoleOptions.FirstOrDefault();
		}

		//The empty state, and the way back into the library: nothing to filter,
		//so the row is `All` alone and the grid is empty. Called from the base
		//file's ShowLibrary beside its own Tiles.Clear, so a sheet that comes
		//back to the library does not come back carrying the last scan's
		//selection.
		private void ResetConsoleFilter()
		{
			_libraryGames.Clear();
			RebuildConsoleOptions();
		}

		//The row, rebuilt in place. `Options` is asked of the entries themselves
		//and never of the enum, which is Decision 5's "only the consoles actually
		//present" - a player cannot cycle onto a filter that holds nothing. All
		//is always first and the list is never empty, so the ring has somewhere
		//to be even in a library with no games at all.
		private void RebuildConsoleOptions()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(_libraryGames.Select(game => game.Entry.Console));
			//The selection the player already had, when the scan still holds it.
			//Anything else - a console the rescan dropped, a folder that went
			//away - reads as All (the module's Resolve), which is what keeps a
			//rescan from leaving the grid empty under a filter the row cannot
			//show.
			RomConsole? keep = LibraryConsoleFilter.Resolve(options, SelectedConsole);
			List<PlayerConsoleFilterOption> desired = options
				.Select(console => new PlayerConsoleFilterOption(console, ConsoleFilterLabel(console)))
				.ToList();

			//Everything the row itself does is under the guard: clearing the
			//collection resets the segmented control's own selection, and that
			//reset travels back out through the two-way binding as a selection of
			//null. It is not the player's press, so it must not re-filter the grid
			//- the assignment below is the only selection this method means.
			_rebuildingConsoleOptions = true;
			try {
				//Rewritten only when it actually changed. A rescan that found the
				//same consoles should not make the player's segment blink, and an
				//unchanged row has nothing to re-resolve.
				if(!ConsoleOptions.SequenceEqual(desired)) {
					ConsoleOptions.Clear();
					foreach(PlayerConsoleFilterOption option in desired) {
						ConsoleOptions.Add(option);
					}
				}
				PlayerConsoleFilterOption? selected = desired.FirstOrDefault(option => option.Console == keep);
				if(SelectedConsoleOption != selected) {
					SelectedConsoleOption = selected;
				}
			} finally {
				_rebuildingConsoleOptions = false;
			}
			RebuildLibraryTiles();
		}

		//The grid, as the two narrowings leave it. Both are applied to the same
		//list by the module in one call, so "the console filter and the search
		//narrow the same view at once" (Decision 5) is a property of the code
		//path rather than of two calls that happen to follow each other.
		private void RebuildLibraryTiles()
		{
			//A scan that landed after the player left the library belongs to no
			//surface: the browser's own rows must not be replaced by a grid
			//nobody is looking at (the base file's ApplyLibraryScan guards the
			//same way, and this is reachable from a pad press too).
			if(!IsVisible || Mode != RomPickerMode.Library) {
				return;
			}
			FillTiles();
		}

		//The word a segment draws. `All` is the sheet's own string (it is not a
		//console, so no console name is right for it); every other segment
		//borrows the console's name, which is the one the tiles already tag their
		//games with.
		private static string ConsoleFilterLabel(RomConsole? console)
		{
			return console is RomConsole known ? ConsoleName(known) : ResourceHelper.GetMessage("RomPickerConsoleAll");
		}
	}
}
