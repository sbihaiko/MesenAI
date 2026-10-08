using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#1066 (round-5 review of #1056, finding 3). IsFinishFallback and IsRestoreLanding
//are the sheet's own CLAIMS over a revision: "this TilesRevision bump is mine -
//the end of a scan whose restore never landed, or the remembered game arriving -
//so keep the player's ring where they put it" (PlayPadNavigationWiring answers
//them). A claim is about ONE bump, and it is only true of the visit that made it:
//the sheet taking the ring again, or the player's next focus change, must find
//both false. Until this, they were set at the end of a scan and cleared only by
//the NEXT scan's StartLibraryStream, so a claim made on one visit was still
//readable on the next bump of that same visit - the console filter's own rebuild
//(PlayerRomPickerViewModel.ConsoleFilter) - and the sheet kept a ring the player
//had already moved instead of re-claiming the grid.
//
//The view-model is driven directly here: PlayerRomPickerViewModel lives above
//UI.Tests (that project does not reference Mesen.ViewModels at all), and the two
//triggers - a tile taking the ring and the sheet losing the ring - are the
//view-model's own hooks, so what is under test is the rule rather than the pad.
[Collection(NativeCoreCollection.Name)]
public class RomPickerScanTests
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1066-" + Guid.NewGuid().ToString("N"));

	private static LibraryEntry Entry(string path, RomConsole console, string title) => new(path, console, title);

	private static LibraryScanResult Scan(params LibraryEntry[] entries)
	{
		return new LibraryScanResult(entries, 1, false);
	}

	//The sheet the Play home opens, with the scan replaced by the caller's own
	//(the real one reads the disk this suite runs on) and run inline, so the
	//whole visit is over before Open() returns and the case can read the state
	//the finish left behind.
	private PlayerRomPickerViewModel Picker(Func<IReadOnlyList<string>, FolderLister, Action<IReadOnlyList<LibraryEntry>>, LibraryScanResult> scan)
	{
		PlayerRomPickerViewModel picker = new() {
			LibraryFolderSource = () => new[] { _folder },
			LibraryScanStreamSource = scan,
			RunLibraryScanInline = true
		};
		return picker;
	}

	//A visit that ends with the sheet owing the player a game under the ring: the
	//remembered file is gone from the library, so the scan never restores it and
	//the finish is the sheet's own bump (IsFinishFallback).
	private PlayerRomPickerViewModel PickerWithAFinishFallback()
	{
		string gone = Path.Combine(_folder, "Gone (USA).nes");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string metroid = Path.Combine(_folder, "Metroid (USA).nes");
		LibraryEntry remembered = Entry(gone, RomConsole.Nes, "Gone");

		LibraryScanResult FirstVisit(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry[] games = { remembered, Entry(contra, RomConsole.Nes, "Contra") };
			onBatch(games);
			return Scan(games);
		}

		PlayerRomPickerViewModel picker = Picker(FirstVisit);
		picker.Open();
		//The view reports the ring landing on a game; this is the game the player
		//left on, and it is what the next visit tries - and fails - to restore.
		picker.RememberFocus(picker.Tiles.Single(tile => tile.Path == remembered.Path));
		Assert.Equal(remembered.Path, picker.LastFocusedTilePath);
		picker.Hide();

		//The file is gone from the library by the time the player comes back.
		picker.LibraryScanStreamSource = (folders, list, onBatch) => {
			LibraryEntry[] games = { Entry(contra, RomConsole.Nes, "Contra"), Entry(metroid, RomConsole.Nes, "Metroid") };
			onBatch(games);
			return Scan(games);
		};
		picker.Open();
		return picker;
	}

	//The claim is the sheet's, and the sheet is not up any more: nothing can read
	//it, and the visit that follows owes the player no such promise. A ring parked
	//by a scan that is over must not be held by it on the way back in.
	[AvaloniaFact]
	public void A_scan_claim_does_not_outlive_the_visit_that_made_it()
	{
		PlayerRomPickerViewModel picker = PickerWithAFinishFallback();
		Assert.True(picker.IsFinishFallback, "the visit did not end on the fallback this case is about");

		picker.Hide();

		Assert.False(picker.IsVisible, "Hide did not take the sheet down");
		Assert.False(picker.IsFinishFallback, "the fallback claim outlived the sheet that made it");
		Assert.False(picker.IsRestoreLanding, "the landing claim outlived the sheet that made it");
	}

	//The other half of the same rule: the player's next focus change ends the
	//claim. The ring is theirs again, so the next bump - the console filter's own
	//rebuild, say - must not be read as the scan finishing what it promised.
	[AvaloniaFact]
	public void The_players_next_focus_change_ends_a_scan_claim()
	{
		PlayerRomPickerViewModel picker = PickerWithAFinishFallback();
		Assert.True(picker.IsFinishFallback, "the visit did not end on the fallback this case is about");

		//The ring lands on a game: the view reports it, exactly as it reports the
		//ring the arbiter moved and the ring the player moved themselves.
		picker.RememberFocus(picker.Tiles[0]);

		Assert.False(picker.IsFinishFallback, "the fallback claim survived the player taking the ring");
		Assert.False(picker.IsRestoreLanding, "the landing claim survived the player taking the ring");
	}

	//The landing claim is the same kind of claim, made by the same visit: the
	//remembered game arriving IS the sheet finishing what it promised (Decision 1),
	//and once the player has the ring back the bump is no longer the sheet's.
	[AvaloniaFact]
	public void The_landing_claim_ends_when_the_ring_is_the_players_again()
	{
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");

		LibraryScanResult Revisit(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry[] games = { Entry(contra, RomConsole.Nes, "Contra") };
			onBatch(games);
			return Scan(games);
		}

		PlayerRomPickerViewModel picker = Picker(Revisit);
		picker.Open();
		picker.RememberFocus(picker.Tiles[0]);
		picker.Hide();

		picker.Open();
		Assert.True(picker.IsRestoreLanding, "the remembered game did not land on the way back in");

		picker.RememberFocus(picker.Tiles[0]);

		Assert.False(picker.IsRestoreLanding, "the landing claim survived the player taking the ring");
		Assert.False(picker.IsFinishFallback, "the landing claim left the fallback claim behind");
	}

	//The header-to-header case: the player walks the ring between header controls
	//(no tile takes it, so RememberFocus never runs) and then narrows the grid.
	//The filter's rebuild is the filter's own bump, so the scan's standing claim
	//is spent with it and the arbiter re-claims the grid instead of keeping the
	//ring where the scan's finish left it.
	[AvaloniaFact]
	public void The_filters_rebuild_spends_a_claim_the_ring_never_answered()
	{
		string gone = Path.Combine(_folder, "Gone (USA).nes");
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string land = Path.Combine(_folder, "Land (World).gb");

		LibraryScanResult Games(Action<IReadOnlyList<LibraryEntry>> onBatch, bool withGone)
		{
			List<LibraryEntry> games = new() { Entry(contra, RomConsole.Nes, "Contra"), Entry(land, RomConsole.GameBoy, "Land") };
			if(withGone) {
				games.Add(Entry(gone, RomConsole.Nes, "Gone"));
			}
			onBatch(games);
			return Scan(games.ToArray());
		}

		PlayerRomPickerViewModel picker = Picker((folders, list, onBatch) => Games(onBatch, true));
		picker.Open();
		picker.RememberFocus(picker.Tiles.Single(tile => tile.Path == gone));
		picker.Hide();

		picker.LibraryScanStreamSource = (folders, list, onBatch) => Games(onBatch, false);
		picker.Open();
		Assert.True(picker.IsFinishFallback, "the visit did not end on the fallback this case is about");
		int revision = picker.TilesRevision;

		picker.CycleConsole(1);

		Assert.Equal(RomConsole.Nes, picker.SelectedConsole);
		Assert.True(picker.TilesRevision > revision, "the filter's rebuild did not bump the revision");
		Assert.False(picker.IsFinishFallback, "the fallback claim answered the filter's own bump");
		Assert.False(picker.IsRestoreLanding, "the landing claim answered the filter's own bump");
	}
}
