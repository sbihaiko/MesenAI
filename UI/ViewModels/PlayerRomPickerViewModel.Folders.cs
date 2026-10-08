using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//One row of *Library folders…*: a folder the library scans, and the press that
	//takes it out of the list. The path IS the row - a library folder is named by
	//where it is and not by a title someone invented - and the label is what a
	//player reads to tell two folders of the same name apart.
	public class PlayerLibraryFolderRow
	{
		public PlayerLibraryFolderRow(string path)
		{
			Path = path;
		}

		public string Path { get; }
		public string Label => Path;
	}

	//#1036 (ADR-0264 Decision 8): *Library folders…*, the sheet's own
	//pad-reachable list of the folders "Your library" scans. This is the host half
	//of the slice: the list rules (which folder is one folder, nesting, the union,
	//the header's literal) are host-free in UI/Logic/LibraryFolders.cs and pinned
	//in UI.Tests/Play/LibraryFoldersTests.cs. What lives here is the part that has
	//a disk and a screen: the stored preference, the two doors an add comes
	//through, and the header the sheet reads.
	//
	//The two doors are the point of Decision 8. **A mouse adds through the native
	//folder dialog** - the one a player at a desk expects, and the mouse-
	//reachability clause ADR-0256 Decision 6 leans on. **A pad adds through the
	//sheet's own folder browser**, because a native dialog owns the screen once it
	//is up: the focus engine cannot draw a ring in it, so a cabinet with a pad and
	//nothing else could not add a folder at all. The bridge answers Confirm on
	//*Add a folder…* with the pad's door (PlayPadNavigationWiring), which is the
	//only reason one control can have two.
	//
	//Removing is a list edit and nothing else. Nothing on this path opens a file:
	//LibraryFolders.Remove has no file API to call, and the only write here is the
	//preference. A folder the player takes out of their library is still their
	//folder, with every game in it.
	public partial class PlayerRomPickerViewModel
	{
		//The folders sheet, which is a surface of this sheet rather than a second
		//one: it comes up over the library, and B closes it back to the library.
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(IsLibrarySurfaceVisible), nameof(SheetHeading))]
		public partial bool IsFoldersSheetVisible { get; private set; }

		//The library surface proper: the grid, its count and *Browse a file…*. It
		//is down while the folders sheet is up, and down while the browser is -
		//the sheet shows one surface at a time, and two visible ones over a single
		//Panel is one of them drawn through the other.
		public bool IsLibrarySurfaceVisible => IsLibraryMode && !IsFoldersSheetVisible;

		//Bumped whenever the folders sheet opens, closes or rebuilds its rows, for
		//the same reason TilesRevision exists: the rows are new containers, so
		//whatever the focus arbiter had the ring on went with the old ones. It is
		//what re-claims the ring on a remove, and what hands it back to the library
		//on a close.
		[ObservableProperty] public partial int FoldersRevision { get; private set; }

		//What the last add or remove did, said where the player is looking.
		[ObservableProperty] public partial string FoldersNoticeText { get; private set; } = "";

		//The list the sheet shows: one instance for the life of the view-model,
		//mutated in place, so a rebuilt list keeps the rows already on screen - and
		//the focus ring with them.
		public ObservableCollection<PlayerLibraryFolderRow> LibraryFolderRows { get; } = new();

		//True while the folder browser is up to PICK a folder to add rather than to
		//open a game. The walk is the same one *Browse a file…* opens; what changes
		//is what a folder's action row says and what it does.
		public bool IsPickingLibraryFolder { get; private set; }

		//The native folder dialog, behind a seam for the same reason every other
		//host call on this sheet is: a headless case drives the mouse path without
		//a dialog owning the screen.
		public Func<Task<string?>> FolderPickerSource { get; set; } = () => FileDialogHelper.OpenFolder(null);

		//The list the library reads, and the one this sheet edits: the stored
		//preference, seeded from the single games folder the app already had so no
		//player loses the folder they had set (Decision 8).
		//
		//"First run" is the preference being absent, never empty - that distinction
		//is LibraryFolders.Seed's, and it is why an emptied library stays emptied
		//instead of having a folder put back on every start. `GamesFolder` is the
		//app's own read of the pair (a folder the player never designated is not
		//their library) and `Seed` does the rest.
		//
		//The seed is persisted ONCE, by the first Open() that finds the preference
		//absent (SeedLibraryFolders), so the list is the same list from then on and
		//*Make this my games folder* cannot change the library before an edit and
		//stop changing it after one. Only a seed that holds a folder is written: a
		//start with no folder set must not turn into an emptied library nobody
		//emptied, and the absent preference stays absent until there is something
		//to seed.
		private static IReadOnlyList<string> StoredLibraryFolders()
		{
			PreferencesConfig prefs = ConfigManager.Config.Preferences;
			if(prefs.LibraryFolders is not null) {
				return prefs.LibraryFolders;
			}
			string? games = GamesFolder;
			return LibraryFolders.Seed(null, games is not null, games);
		}

		private static void SeedLibraryFolders()
		{
			PreferencesConfig prefs = ConfigManager.Config.Preferences;
			if(prefs.LibraryFolders is not null) {
				return;
			}
			IReadOnlyList<string> seed = StoredLibraryFolders();
			if(seed.Count > 0) {
				prefs.LibraryFolders = new List<string>(seed);
				ConfigManager.Config.Save();
			}
		}

		//*Library folders…*: the list comes up over the library, and it is the
		//surface from then on. Called by the header button, which is the one control
		//that leads here.
		public void OpenFoldersSheet()
		{
			if(!IsVisible || Mode != RomPickerMode.Library || IsFoldersSheetVisible) {
				return;
			}
			FoldersNoticeText = "";
			FillLibraryFolderRows();
			IsFoldersSheetVisible = true;
			FoldersRevision++;
		}

		//The folders sheet's close: B, and the same step the sheet's own Back button
		//takes. The library is under it and comes back as it was - the list it shows
		//is the one this sheet just edited, because every edit re-scanned it.
		public void CloseFoldersSheet()
		{
			if(!IsFoldersSheetVisible) {
				return;
			}
			IsFoldersSheetVisible = false;
			FoldersNoticeText = "";
			FoldersRevision++;
		}

		//The pad's own add (Decision 8): the folder browser, walking the same roots
		//and the same folders *Browse a file…* walks, with the folder a player
		//descends into offered as the row that adds it. The sheet stays up behind
		//it and comes back when the pick ends - cancelled or taken.
		public void AddFolderFromPad()
		{
			if(!IsFoldersSheetVisible) {
				return;
			}
			IsPickingLibraryFolder = true;
			//The browser is the surface while it is up; the folders sheet comes back
			//when the pick ends (ReturnFromFolderPick).
			IsFoldersSheetVisible = false;
			FoldersRevision++;
			BrowseFile();
		}

		//The mouse's own add: the native folder dialog. The pad never reaches this
		//- the bridge answers Confirm on the same button with AddFolderFromPad,
		//because a native dialog owns the screen and a pad-only player cannot
		//answer one.
		public async Task AddFolderFromMouse()
		{
			if(!IsFoldersSheetVisible) {
				return;
			}
			string? picked = await FolderPickerSource();
			if(picked is null) {
				//A cancelled dialog is not an answer: nothing changed, so nothing is
				//said about it.
				return;
			}
			if(!IsVisible || !IsFoldersSheetVisible) {
				//The sheet went while the dialog was up. The pick is still the
				//player's, so the list is saved; the notice and the re-scan are for a
				//sheet nobody is looking at.
				CommitLibraryFolders(LibraryFolders.Add(_folders, picked).Folders);
				return;
			}
			AddLibraryFolder(picked);
		}

		//One add, whichever door it came through. The rule is host-free
		//(LibraryFolders.Add) and answers what happened to the list; this only
		//carries it out and says so, because "already in your library" and "every
		//game in it is already in your library" are different answers that both
		//leave the list alone.
		public void AddLibraryFolder(string? folder)
		{
			LibraryFolderEdit edit = LibraryFolders.Add(_folders, folder);
			CommitLibraryFolders(edit.Folders);
			FoldersNoticeText = ResourceHelper.GetMessage(AddNoticeId(edit.Change), folder ?? "");
			ReturnFromFolderPick();
		}

		//The row's own Remove. It takes the row out of the list and stops there:
		//no file call happens on this path, so nothing on the player's disk moves.
		public void RemoveLibraryFolder(PlayerLibraryFolderRow? row)
		{
			if(row is null || !IsFoldersSheetVisible) {
				return;
			}
			CommitLibraryFolders(LibraryFolders.Remove(_folders, row.Path));
			FoldersNoticeText = ResourceHelper.GetMessage("RomPickerFolderRemoved", row.Label);
			FillLibraryFolderRows();
			FoldersRevision++;
			//The games under that folder leave the grid with it, so the grid is
			//rebuilt rather than left showing a library the list no longer names.
			RescanLibrary();
		}

		//The stored list and the list this sheet is showing are one value, so an
		//edit is visible to the scan that follows it and to the next start alike.
		private void CommitLibraryFolders(IReadOnlyList<string> folders)
		{
			ConfigManager.Config.Preferences.LibraryFolders = new List<string>(folders);
			ConfigManager.Config.Save();
			//LibraryFolderSource is a SEAM - a caller or a test puts its own list
			//there - so an edit never writes it back; reassigning it here silently
			//destroyed whatever a caller injected, and the next open read the config
			//instead of the list it was given.
			//
			//What the edit refreshes is the list the sheet is showing, and it comes
			//from the preference that was just written rather than from a copy, so
			//the edit and the scan that follows it are the same value.
			_folders = ConfigManager.Config.Preferences.LibraryFolders;
		}

		private void FillLibraryFolderRows()
		{
			LibraryFolderRows.Clear();
			foreach(string folder in _folders) {
				LibraryFolderRows.Add(new PlayerLibraryFolderRow(folder));
			}
		}

		//The pick is over - taken or cancelled - and the folders sheet comes back
		//with the answer on it. The library is re-scanned either way: an add changes
		//what the grid holds, and a cancel costs one walk the player asked for by
		//opening the sheet.
		private void ReturnFromFolderPick()
		{
			if(IsPickingLibraryFolder) {
				IsPickingLibraryFolder = false;
				Mode = RomPickerMode.Library;
				IsLibraryMode = true;
				IsBrowseMode = false;
				_folder = null;
				//The browser's own lines go with it: ShowLibrary's reset, without
				//the scan it kicks (the rescan below owns that).
				HeaderText = ResourceHelper.GetMessage("RomPickerLibraryTitle");
				PathText = "";
				NoticeText = "";
				IsFoldersSheetVisible = true;
			}
			FillLibraryFolderRows();
			FoldersRevision++;
			RescanLibrary();
		}

		//B while the browser is up to pick: the cancel of the pick, back to the
		//folders sheet. The list is as it was, and the notice that would have been
		//written is not written - nothing happened to answer for.
		internal void CancelFolderPick()
		{
			FoldersNoticeText = "";
			ReturnFromFolderPick();
		}

		private void RescanLibrary()
		{
			//Only the library surface scans: the browser has no grid to fill, and a
			//scan published while it is up is dropped by ApplyLibraryScan anyway.
			if(Mode != RomPickerMode.Library) {
				return;
			}
			if(_folders.Count == 0) {
				//The last folder just left the list. The library is the named empty
				//state again, never a grid still showing games no folder on the list
				//reaches any more. The visit is rebuilt the way ShowLibrary rebuilds
				//it: a scan still in flight is stale, and the search state of the
				//scan before the edit goes, or the next keystroke would draw it.
				_scanGeneration.Next();
				BeginLibraryVisit();
				ResetConsoleFilter();
				ClearTiles();
				HeaderText = ResourceHelper.GetMessage("RomPickerLibraryTitle");
				SearchingText = "";
				TruncatedText = "";
				EmptyText = ResourceHelper.GetMessage("RomPickerLibraryNoFolders");
				TilesRevision++;
				return;
			}
			EmptyText = "";
			//The games of the scan before the edit are not the library any more:
			//a query or filter while this rescan runs must not draw them.
			BeginLibraryVisit();
			ResetConsoleFilter();
			StartLibraryStream();
		}

		//One message per answer the host-free rule can give about an add. `Added`
		//changed the list; the other two did not, and each of them says why rather
		//than leaving the player pressing a button that appears to do nothing. A
		//folder nested in a listed one is not an answer of its own: every root is
		//kept, so it is a row like any other.
		private static string AddNoticeId(LibraryFolderChange change)
		{
			return change switch {
				LibraryFolderChange.Added => "RomPickerFolderAdded",
				LibraryFolderChange.AlreadyListed => "RomPickerFolderAlreadyListed",
				_ => "RomPickerFolderInvalid"
			};
		}
	}
}
