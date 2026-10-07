using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//#1032 (ADR-0264): which of the sheet's two surfaces is up. The LIBRARY is
	//the sheet the Play home opens - every openable ROM under the library
	//folders, at once (Decision 1). The folder browser survives behind
	//*Browse a file…* (Decision 11), unchanged, for the one ROM outside the
	//library.
	public enum RomPickerMode
	{
		Library,
		BrowseFile
	}

	//One tile of the library grid: the path that opens it, the title it carries,
	//the console it is for (Decision 7) and the cover it draws.
	//
	//The generic cover is "a console-coloured cover carrying the title"
	//(Decision 6 case 4), so the colour is part of the answer the library gives
	//and not a styling decision the template is free to make - which is why it
	//is chosen here and only here. The palette is the wireframe's own
	//(scripts/render_gui_wireframes.py, CONSOLE_TINT), so a render of this sheet
	//and W-P19 agree on what a Game Boy game looks like.
	public class PlayerLibraryTile
	{
		public PlayerLibraryTile(LibraryEntry entry, string consoleName)
		{
			Path = entry.Path;
			Title = entry.Title;
			Console = entry.Console;
			ConsoleName = consoleName;
			Cover = ConsoleCover(entry.Console);
		}

		public string Path { get; }
		public string Title { get; }
		public RomConsole Console { get; }
		//Read by the player, so it comes from the locale files like every other
		//string on this sheet (the caller resolves it, the way a root's label is
		//already resolved).
		public string ConsoleName { get; }
		public IBrush Cover { get; }

		private static readonly Dictionary<RomConsole, Color> _tint = new() {
			[RomConsole.Nes] = Color.FromRgb(84, 88, 96),
			[RomConsole.GameBoy] = Color.FromRgb(104, 116, 84),
			[RomConsole.GameBoyColor] = Color.FromRgb(128, 104, 44),
			[RomConsole.GameBoyAdvance] = Color.FromRgb(74, 62, 132),
			[RomConsole.MasterSystem] = Color.FromRgb(44, 92, 122),
			[RomConsole.Sg1000] = Color.FromRgb(96, 74, 52),
			[RomConsole.GameGear] = Color.FromRgb(52, 70, 112)
		};

		//A console with no tint of its own is grey, never a colour borrowed from
		//another machine: the tile says "a game whose console we could not name"
		//rather than claiming one.
		private static readonly Color _unknownTint = Color.FromRgb(142, 142, 147);

		private static IBrush ConsoleCover(RomConsole console)
		{
			return new SolidColorBrush(_tint.TryGetValue(console, out Color color) ? color : _unknownTint);
		}
	}

	//One row of the picker's list: a folder to descend into, a game to open, or
	//the action that names the folder as the games folder. The rules that
	//produced it (roots, rows, ranking) are PlayRomPicker's, host-free; this only
	//carries it to the template.
	public class PlayerRomPickerRow
	{
		public PlayerRomPickerRow(RomPickerRow row)
		{
			Label = row.Label;
			Path = row.Path;
			Kind = row.Kind;
		}

		public string Label { get; }
		public string Path { get; }
		public RomPickerRowKind Kind { get; }
		//The template branches on three kinds now; IsFolder survives as the same
		//computed answer it always was.
		public bool IsFolder => Kind == RomPickerRowKind.Folder;
		public bool IsGame => Kind == RomPickerRowKind.Game;
		public bool IsAction => Kind == RomPickerRowKind.Action;
	}

	//#1032 review finding 1: both scans of this sheet run off the UI thread and
	//are answered later, so the sheet has to know WHICH surface a late answer
	//belongs to. This is that bookkeeping and nothing else: a counter every scan
	//is tagged with. It is bumped when a scan is scheduled and again when the
	//surface a scan reads is rebuilt, so an answer whose tag is no longer the
	//current generation was read from a folder the sheet has already left - and
	//applying it would put the old folder's tiles back on the grid, or its
	//suggestions back under the rows, which is the bug this guards.
	//
	//A type of its own, free of Avalonia and of the core, because the rule is
	//about which answer wins and not about the sheet it is drawn on.
	internal sealed class ScanGeneration
	{
		private int _current;

		//The generation the next unit of scan work carries. Called both when a
		//scan starts and when the surface it reads is reset: either way every
		//scan already in flight is left behind by it.
		public int Next() => ++_current;

		public bool IsCurrent(int generation) => generation == _current;
	}

	//#845 (ADR-0256 Decision 9): the in-app ROM picker, which is what the Play
	//home's *Open a ROM…* opens. It used to reach Avalonia's StorageProvider - a
	//native dialog that owns the screen once it opens, so the focus engine (and
	//with it the pad) cannot drive the choice, and a cabinet with a pad and
	//nothing else could not load a game.
	//
	//This is the host half: it reads the filesystem and the mounted volumes and
	//hands the names to PlayRomPicker, which owns every rule. It walks folders
	//from the roots - the configured game folder, the app's own ROM folder, the
	//mounted volumes, the whole computer - and hands the chosen path to the
	//owner, which loads it through the same call the native dialog's own result
	//took. Only a file whose extension is a ROM's is a pick, and only the Play
	//home opens it: every other file choice in the app, and Advanced's own Open,
	//stay native (ADR-0256 Decision 9's non-goals).
	//
	//Decision 9's amendment adds two things: a bounded background scan that
	//suggests the standard places a library hides in, and one action row inside a
	//folder that names that folder the games folder. Both are described where
	//they are implemented below.
	public partial class PlayerRomPickerViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool IsVisible { get; private set; }
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(SheetHeading))]
		public partial string Title { get; private set; } = "";
		//What the path line reads; empty on the roots list, where there is no
		//folder to name. It is also what tells the focus arbiter the list was
		//rebuilt by a step (see MainWindowViewModel's claim): every descend and
		//every ascend changes it, and the roots list and any folder read
		//differently. A save re-roots in place and changes it too, which is what
		//re-claims the ring without a dismiss.
		[ObservableProperty] public partial string PathText { get; private set; } = "";
		[ObservableProperty] public partial string EmptyText { get; private set; } = "";
		//The line the roots list shows while the scan runs, and the line a save
		//leaves behind. Neither is a row, so the pad ignores both.
		[ObservableProperty] public partial string SearchingText { get; private set; } = "";
		[ObservableProperty] public partial string NoticeText { get; private set; } = "";
		//Bumped whenever the suggestion rows below the roots are rebuilt. It is
		//never shown: it is the property the focus arbiter watches, so a rewrite
		//of the tail - the deep pass replacing the shallow answer - re-claims the
		//ring the removed row's container took with it, instead of leaving the pad
		//with nothing to press Confirm on.
		[ObservableProperty] public partial int SuggestionRevision { get; private set; }

		//#1032 (ADR-0264): which surface is up, and the two flags the template
		//reads. The mode is the state; the flags exist because a XAML binding
		//cannot test an enum for equality without a converter, and two booleans
		//are cheaper to read than a converter is to register.
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(SheetHeading))]
		public partial RomPickerMode Mode { get; private set; }
		[ObservableProperty] public partial bool IsLibraryMode { get; private set; } = true;
		[ObservableProperty] public partial bool IsBrowseMode { get; private set; }

		//The library's two header lines: "Your library" and "<N> games in <M>
		//folders" (Decision 8). The count is what the scan actually found, so a
		//capped scan and a complete one read differently.
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(SheetHeading))]
		public partial string HeaderText { get; private set; } = "";
		[ObservableProperty] public partial string CountText { get; private set; } = "";
		//The one thing the header has to say about a capped scan: it stopped
		//collecting (Decision 9). Never shown otherwise.
		[ObservableProperty] public partial string TruncatedText { get; private set; } = "";
		//Bumped whenever the grid is rebuilt, for the same reason
		//SuggestionRevision exists: the rows are new containers, and whatever the
		//focus arbiter had the ring on went with the old ones.
		[ObservableProperty] public partial int TilesRevision { get; private set; }

		//The one heading the sheet has. It is the library's own when the library
		//is up and the sheet's title when the browser is, because the heading is a
		//single control the player reads either way - two TextBlocks swapping
		//places would be two headings that happen to be exclusive, and the one the
		//sheet's own case reads would be off screen half the time.
		public string SheetHeading => Mode == RomPickerMode.Library ? HeaderText : Title;

		//The grid itself: one instance for the life of the view-model, mutated in
		//place so a scan that lands while the sheet is up does not rebuild the
		//list under the player - and so the focus ring survives it.
		public ObservableCollection<PlayerLibraryTile> Tiles { get; } = new();

		//The list itself: one instance for the life of the view-model, mutated in
		//place. Suggestions arrive asynchronously, and appending to the same
		//collection keeps the rows already on screen - and the focus ring with
		//them - instead of rebuilding the list under the player.
		public ObservableCollection<PlayerRomPickerRow> Rows { get; } = new();

		//The mounted volumes, which only the host can enumerate; a test replaces
		//it, and so does a platform the default does not know.
		public Func<IReadOnlyList<string>> VolumeSource { get; set; } = MountedVolumes.List;
		//One folder's entries. A seam for the same reason, and the one the rules
		//are pinned through in UI.Tests without touching a disk. The default is
		//the app's own lister, shared with the library scan and the background
		//walk (DiskFolderLister), so the three cannot read a folder differently.
		public Func<string, (IReadOnlyList<string> Folders, IReadOnlyList<string> Files)> FolderSource { get; set; } = DiskFolderLister.List;
		//The scan, behind a seam: a test injects fake hits and drives the sheet
		//deterministically, and the shell stays off the thread pool. It answers per
		//PASS, because one scan is two of them - a shallow answer published as soon
		//as it lands, then the deep one that replaces it.
		public Func<RomScanPass, IReadOnlyList<RomPickerHit>> SuggestionSource { get; set; } = RomFolderScanSource.Scan;
		//The call that hands a folder to the core's known-game-folder list. A seam
		//only because the interop has no getter to read it back: the default is the
		//real call, and the point of it is that the folder the player designated
		//works without a restart (see MakeGamesFolder).
		public Action<string> KnownGameFolderSink { get; set; } = EmuApi.AddKnownGameFolder;
		//The whole-computer root the host offers, if any. Null where the platform
		//has no single root to offer (Windows lists its drives instead), which is
		//also what keeps this file from hardcoding `/`.
		public string? WholeComputerFolder { get; set; } = OperatingSystem.IsWindows() ? null : "/";
		//A test runs the scan in the Open() turn instead of on the thread pool.
		public bool RunScanInline { get; set; }

		//#1032 (ADR-0264 Decision 8): the folders the library reads. The app has
		//ONE games folder today, and the list is seeded from it; the multi-folder
		//list *Library folders…* edits is its own slice, and it arrives by
		//replacing this seam rather than by changing the scan.
		public Func<IReadOnlyList<string>> LibraryFolderSource { get; set; } = ConfiguredLibraryFolders;
		//The library scan, behind a seam for the same reason the walk is: a test
		//drives the grid deterministically without depending on the disk this
		//suite happens to run on. The default is the real module, and it is the
		//module's own Caps that bound it, never this file.
		public Func<IReadOnlyList<string>, FolderLister, LibraryScanResult> LibraryScanSource { get; set; } = GameLibrary.Scan;
		//A test runs the library scan in the Open() turn too.
		public bool RunLibraryScanInline { get; set; }

		//Which surface Open() lands on. The Play home's sheet opens on the
		//LIBRARY - that is the whole of ADR-0264 Decision 1 - but this view-model
		//is also the Remaster workspace's *right game* chooser, where the player
		//is picking one file for a project rather than browsing a shelf, and there
		//the folder walk is the right surface. One default, one override, so the
		//two callers differ by a property instead of by a copy of this sheet.
		public RomPickerMode OpenMode { get; set; } = RomPickerMode.Library;

		//The chosen game. The owner loads it; the picker never does.
		public event Action<string>? RomChosen;

		//The suggestions currently offered, for a test to read. The list they
		//become is Rows.
		public IReadOnlyList<RomPickerSuggestion> Suggestions => _suggestions;

		private IReadOnlyList<RomPickerRoot> _roots = Array.Empty<RomPickerRoot>();
		//The library folders of the open that is up, so the background scan reads
		//the same list the sheet was built from even if the settings change under
		//it.
		private IReadOnlyList<string> _folders = Array.Empty<string>();
		//The folder being shown, or null for the roots list.
		private string? _folder;
		private IReadOnlyList<RomPickerSuggestion> _suggestions = Array.Empty<RomPickerSuggestion>();
		//The rows standing for `_suggestions` in Rows, so a later pass can take
		//back exactly what an earlier one put there.
		private readonly List<PlayerRomPickerRow> _suggestionRows = new();
		private bool _scanStarted;
		private bool _scanDone;
		//The surface every LIBRARY scan in flight was read for. A result carries
		//the generation it was scheduled under and is dropped when that is no
		//longer the one on the sheet (see ScanGeneration).
		private readonly ScanGeneration _scanGeneration = new();
		//The suggestion walk gets a counter of its own (review finding 2 on
		//#1032). It is not an answer about the library folders - it looks in the
		//standard places a library hides in, and its hits are the roots list's own
		//tail - so a library rebuild must not invalidate it: with one shared
		//counter, a player who stepped into *Browse a file…* and came straight
		//back before the deep pass landed (measured, 0.86 s) dropped the walk's
		//only answer AND left _scanStarted set, so the roots showed no suggestion
		//for the rest of the session.
		private readonly ScanGeneration _suggestionGeneration = new();

		//Opens on the roots. Called by the Play home's own action, and only when
		//there is nothing to pause: the picker is what a machine with no game
		//loaded uses, so it never has an overlay under it.
		//
		//The roots are computed and shown before the scan is kicked, so the sheet
		//is up instantly and never waits: the scan fills the roots list in when
		//(and if) it returns.
		public void Open()
		{
			_roots = BuildRoots(GamesFolder);
			_folder = null;
			Title = ResourceHelper.GetMessage("RomPickerTitle");
			IsVisible = true;
			//#1032 (ADR-0264 Decision 1): the Play sheet opens on the LIBRARY,
			//not on a list of folders to descend. The folder browser is one press
			//away, behind *Browse a file…* (Decision 11) - and a caller whose job
			//is a single file (the Remaster workspace's right game) says so
			//through OpenMode and lands on the browser directly.
			if(OpenMode == RomPickerMode.BrowseFile) {
				IsLibraryMode = false;
				IsBrowseMode = true;
				Mode = RomPickerMode.BrowseFile;
				ShowRoots();
				StartScan();
				return;
			}
			ShowLibrary();
		}

		//#1032 (ADR-0264 Decision 1/9): the library, which is the sheet's own
		//surface. It shows the empty state instantly when there is no library
		//folder to read - a named state with a next step, never an empty grid -
		//and otherwise kicks the bounded scan and fills the grid as it lands.
		private void ShowLibrary()
		{
			//The surface is rebuilt here from the folders as they are NOW, so
			//every scan still in flight was read for the surface this replaces -
			//and a folder list that no longer answers anything (the early return
			//below) is as much a rebuild as one that does. The bump is what makes
			//the rebuild, not the new scan, the thing that decides which answer
			//may land: a scan scheduled before it can only fill a grid the player
			//has already left.
			_scanGeneration.Next();
			Mode = RomPickerMode.Library;
			IsLibraryMode = true;
			IsBrowseMode = false;
			HeaderText = ResourceHelper.GetMessage("RomPickerLibraryTitle");
			PathText = "";
			NoticeText = "";
			CountText = "";
			TruncatedText = "";
			EmptyText = "";
			Tiles.Clear();
			TilesRevision++;
			//Nothing to browse, so the searching line belongs to no state: it is
			//set below, by the scan that is actually about to run.
			SearchingText = "";

			_folders = LibraryFolderSource();
			//#1060: nothing scanned yet, so the only state known here is the folder
			//list's own; a library with folders waits for its scan to say anything.
			EmptyText = LibraryEmptyText(PlayRomPicker.LibraryEmptyMessageId(_folders.Count, 0));
			if(_folders.Count == 0) {
				return;
			}
			StartLibraryScan();
		}

		//#1032 (ADR-0264 Decision 11): *Browse a file…*. The folder browser is
		//exactly what it always was - its roots, its walk, its *Make this my
		//games folder* row and the first-row focus guard that protects it - and
		//it is reached from inside the library rather than instead of it.
		public void BrowseFile()
		{
			if(!IsVisible) {
				return;
			}
			Mode = RomPickerMode.BrowseFile;
			IsLibraryMode = false;
			IsBrowseMode = true;
			_folder = null;
			EmptyText = "";
			ShowRoots();
			StartScan();
		}

		//A tile's press: the pick, exactly as a game row's was. Nothing else on
		//this sheet opens a game, so the tile IS the choice.
		public void Play(PlayerLibraryTile tile)
		{
			if(!IsVisible) {
				return;
			}
			IsVisible = false;
			RomChosen?.Invoke(tile.Path);
		}

		//Esc, and the sheet's own Back button - the same step, because they are
		//the same decision: up one folder, and out of a root back to the roots
		//list, and on the roots there is nowhere up, so it is the dismiss. Nothing
		//opens under it: the picker sits over the home, and there is no game to
		//come back to (unlike every other Play sheet, which returns to W-P4).
		public void Back()
		{
			if(!IsVisible) {
				return;
			}
			//On the library there is nowhere back to: it is the sheet, so the
			//step is the dismiss (ADR-0264 Decision 3, ADR-0256's stop rule).
			if(Mode == RomPickerMode.Library) {
				IsVisible = false;
				return;
			}
			//The browser is INSIDE the sheet (Decision 11), so walking out of
			//its roots lands back on the library rather than closing the sheet:
			//the escape hatch leads back, not away.
			//
			//Unless the browser IS the sheet, which is the case for the one
			//caller that says so through OpenMode: the Remaster workspace's right
			//game chooser picks a single file for a project and has no library to
			//lead back to - rebuilding the Play grid under it would turn that
			//sheet into a surface the artist never opened (review finding 1 on
			//#1032). There, out of the roots is the dismiss, the way it was before
			//the library landed.
			if(_folder is null) {
				if(OpenMode == RomPickerMode.BrowseFile) {
					IsVisible = false;
					return;
				}
				ShowLibrary();
				return;
			}
			string? parent = PlayRomPicker.Ascend(_folder, _roots);
			if(parent is null) {
				_folder = null;
				ShowRoots();
			} else {
				_folder = parent;
				ShowFolder(parent);
			}
		}

		//A row's own action: a folder descends, a game is the pick, and the
		//action row names this folder the games folder.
		public void Choose(PlayerRomPickerRow row)
		{
			if(!IsVisible) {
				return;
			}
			switch(row.Kind) {
				case RomPickerRowKind.Action:
					MakeGamesFolder(row.Path);
					return;
				case RomPickerRowKind.Folder:
					_folder = row.Path;
					ShowFolder(row.Path);
					return;
				default:
					IsVisible = false;
					RomChosen?.Invoke(row.Path);
					return;
			}
		}

		//The game changed under it (another ROM opened, the device unplugged):
		//the sheet goes without loading anything.
		public void Hide() => IsVisible = false;

		//The action row's press: this folder becomes the games folder. It is the
		//same two properties the classic Advanced Options row writes, saved the
		//same way, then the picker re-roots in place and STAYS: the sheet does not
		//dismiss.
		//
		//There are two outcomes (#887), and the difference is whether the folder
		//answers anything. A folder with entries in it IS the games folder now: the
		//action row is gone, the path line reads "Your games", and it leads the roots
		//list. A folder that answers nothing is SAVED but not used - it is not
		//registered with the core, not made a root, and not led with - so the player
		//keeps the action row and is told why by the notice.
		//
		//The first outcome re-claims the ring through PathText, which changes. The
		//second does not, which is why it bumps SuggestionRevision by hand.
		private void MakeGamesFolder(string folder)
		{
			if(_folder is null) {
				return;
			}
			ConfigManager.Config.Preferences.GameFolder = folder;
			ConfigManager.Config.Preferences.OverrideGameFolder = true;
			ConfigManager.Config.Save();

			//#887: the setting is saved either way - a folder that answers nothing
			//today is used the moment it holds anything, and this action is the
			//player's, not the app's to refuse - but a folder the app cannot open on
			//is not registered with the core and does not become a root. Otherwise
			//the press that designates an empty folder is also the press that leaves
			//them leading on it, which is the state #887 is about.
			string? usable = GamesFolderChoice.Usable(folder);

			//The core keeps its own list of folders a game can be found in, and
			//reads the configured folder only at startup (MainWindow's own
			//AddKnownGameFolder call). Without this one the folder the player
			//designated here is invisible to the core - to RomFinder, which
			//resolves a ROM by name and CRC - until the next launch. The native
			//dialog's initial folder reads the config live and was never affected;
			//this is the other half of the same write.
			if(usable != null) {
				KnownGameFolderSink(usable);
			}

			_roots = BuildRoots(usable);
			ShowFolder(folder);
			//The rebuilt rows are new containers, so whatever the arbiter had the ring
			//on - the action row this press came from - went with the old one.
			//
			//The claim above cannot lean on PathText here the way the class comment
			//says it can: a folder that answers nothing is deliberately not made a
			//root as *the games folder* (#887), so the path line reads the same text
			//before and after - the shortened path, or another root's own label when
			//the folder is also that root, since Roots dedupes by path - and the
			//arbiter sees no change in any of the three properties it watches. Without this the pad is left with nothing focused, the
			//direction keys and Confirm return immediately because there is no
			//control to act on, and only Back still works - escaped through the
			//window rather than through the sheet. Found by the second review of
			//#894, which traced the ring rather than the rule.
			SuggestionRevision++;
			//The notice says which of the two happened, because "Saved" alone would
			//contradict what the player then sees: the action row is still there
			//offering to make this the games folder, which is only confusing if
			//nothing says why.
			NoticeText = ResourceHelper.GetMessage(usable is null ? "RomPickerGamesFolderEmpty" : "RomPickerGamesFolderSaved");
		}

		//#1032 (ADR-0264 Decision 9): the library's one bounded scan. It runs off
		//the UI thread and posts its answer back, so the sheet is usable while it
		//works - and the searching line is the visible wait, because every
		//visible wait has an animation (or, here, a sentence that says what is
		//happening rather than a frozen grid).
		private void StartLibraryScan()
		{
			SearchingText = ResourceHelper.GetMessage("RomPickerSearching");
			//The generation is taken BEFORE the work is handed out, so the answer
			//carries the surface it was read for - and the check that drops a
			//stale one happens where the answer lands (ApplyLibraryScan), never
			//here: the posting thread cannot know what the UI thread did while
			//the scan ran.
			int generation = _scanGeneration.Next();
			if(RunLibraryScanInline || RunScanInline) {
				ApplyLibraryScan(ScanLibrary(_folders), generation);
				return;
			}
			IReadOnlyList<string> folders = _folders;
			Task.Run(() => {
				LibraryScanResult result = ScanLibrary(folders);
				Dispatcher.UIThread.Post(() => ApplyLibraryScan(result, generation));
			});
		}

		//A scan that threw answers nothing to say: an unreadable disk is not a
		//reason to leave the sheet waiting on a line that will never go.
		private LibraryScanResult ScanLibrary(IReadOnlyList<string> folders)
		{
			try {
				return LibraryScanSource(folders, new FolderLister(FolderSource));
			} catch {
				return new LibraryScanResult(Array.Empty<LibraryEntry>(), 0, false);
			}
		}

		//One scan's answer into the grid. The entries are the module's, in the
		//module's order (by title, Decision 1); this only carries them across and
		//says what the header reads.
		private void ApplyLibraryScan(LibraryScanResult result, int generation)
		{
			//First, before anything is written - not even the searching line. A
			//scan the player has already left behind answers about a folder the
			//sheet no longer shows, however slowly it got there: the grid, the
			//counts and the waiting line all belong to the scan that is current
			//NOW, and letting the older one through would hand them all to the
			//folder the player walked away from.
			if(!_scanGeneration.IsCurrent(generation)) {
				return;
			}
			//A scan that landed after the player left the library - a B press, a
			//step into *Browse a file…* - belongs to no surface: the browser's
			//own rows must not be replaced by a grid nobody is looking at, and
			//its waiting line is not this scan's to clear (review finding 5 on
			//#1032: the browser says "Looking for your games…" while its own walk
			//runs, and a library answer landing in the meantime used to wipe it).
			if(!IsVisible || Mode != RomPickerMode.Library) {
				return;
			}
			SearchingText = "";
			//#1060: a scan that answered no game is a named state that names the next
			//step, not a blank grid. The rule is PlayRomPicker's; this is the lookup.
			EmptyText = LibraryEmptyText(PlayRomPicker.LibraryEmptyMessageId(_folders.Count, result.Entries.Count));
			Tiles.Clear();
			foreach(LibraryEntry entry in result.Entries) {
				Tiles.Add(new PlayerLibraryTile(entry, ConsoleName(entry.Console)));
			}
			CountText = ResourceHelper.GetMessage("RomPickerLibraryCount",
				CountLabel(result.Entries.Count, "RomPickerGameOne", "RomPickerGameMany"),
				CountLabel(result.FolderCount, "RomPickerFolderOne", "RomPickerFolderMany"));
			TruncatedText = result.Truncated
				? ResourceHelper.GetMessage("RomPickerLibraryTruncated", GameLibrary.MaxEntries)
				: "";
			//The rebuilt tiles are new containers, so whatever the arbiter had
			//the ring on went with the old ones.
			TilesRevision++;
		}

		//#1060: the id PlayRomPicker answered, in the player's own words. Nothing to
		//say is an empty line, which is the length the sentence's own visibility
		//binding reads.
		private static string LibraryEmptyText(string? messageId)
		{
			return messageId is null ? "" : ResourceHelper.GetMessage(messageId);
		}

		//A counted noun: "1 game" and "11 games" are different words in English,
		//and the header shows both counts on every visit.
		private static string CountLabel(int count, string oneId, string manyId)
		{
			return ResourceHelper.GetMessage(count == 1 ? oneId : manyId, count);
		}

		private void ShowRoots()
		{
			PathText = "";
			NoticeText = "";
			ReplaceRows(PlayRomPicker.RootRows(_roots));
			ShowSuggestionRows();
			SearchingText = _scanStarted && !_scanDone ? ResourceHelper.GetMessage("RomPickerSearching") : "";
		}

		private void ShowFolder(string folder)
		{
			(IReadOnlyList<string> folders, IReadOnlyList<string> files) = FolderSource(folder);
			//The list first, the path line second: PathText is the property the
			//focus arbiter watches (see the claim in PlayPadNavigationWiring), and
			//the step is only complete once the new rows exist. The arbiter is
			//posted, so it would survive either order today - this is so that it
			//still does if it is ever made to answer in the same turn.
			ReplaceRows(PlayRomPicker.FolderRows(
				folder, GamesFolder, ResourceHelper.GetMessage("RomPickerMakeGamesFolder"), folders, files));
			//Both lines belong to the state they were set in: the searching line
			//belongs to the roots list, and a save notice belongs to the folder it
			//was made in. A step clears them; a save sets the notice after this.
			SearchingText = "";
			NoticeText = "";
			PathText = PlayRomPicker.PathText(folder, _roots, ConfigManager.HomeFolder);
		}

		private void ReplaceRows(IReadOnlyList<RomPickerRow> rows)
		{
			//A folder with nothing to open is not an error: it is a folder the
			//player picked and the sentence says where they are, with Back as the
			//next step. ContentCount, not Count: the action row is not content.
			EmptyText = PlayRomPicker.ContentCount(rows) == 0 ? ResourceHelper.GetMessage("RomPickerEmpty") : "";
			Rows.Clear();
			//The suggestion rows went with the Clear; ShowRoots puts back the ones
			//this list is meant to have.
			_suggestionRows.Clear();
			foreach(RomPickerRow row in rows) {
				Rows.Add(new PlayerRomPickerRow(row));
			}
		}

		//Kicks the one scan of this session, lazily, on the first Open. The sheet
		//is already up: this runs on the thread pool and posts its answers back,
		//and the player reads the list while it works.
		//
		//Two passes, in this order, and never the other: the shallow one lands in
		//a fraction of the deep one's budget, so its rows are on screen while the
		//deep walk is still going. A cold cache or a slow disk can spend the whole
		//deep budget above any library - measured, the deep pass alone is 0.86 s on
		//the requesting machine - and without the shallow answer the player would
		//be looking at the plain roots with no suggestion and no way to designate
		//anything.
		private void StartScan()
		{
			if(_scanStarted) {
				return;
			}
			_scanStarted = true;
			SearchingText = ResourceHelper.GetMessage("RomPickerSearching");
			//This scan's tag, taken before any of its work is handed out: the
			//surface it reads is the one the sheet is on now (see
			//ScanGeneration), and every answer below is dropped if that surface
			//is rebuilt before the answer lands. Its own counter, because the
			//walk answers about the places it looked in and not about the
			//library folders a rebuild re-reads - which is why a walk in flight
			//survives one (review finding 2 on #1032).
			int generation = _suggestionGeneration.Next();

			if(RunScanInline) {
				PublishInline(RomScanPass.Shallow, generation);
				PublishInline(RomScanPass.Deep, generation);
				FinishScan(generation);
				return;
			}
			Task.Run(() => {
				Publish(RomScanPass.Shallow, generation);
				Publish(RomScanPass.Deep, generation);
				Dispatcher.UIThread.Post(() => FinishScan(generation));
			});
		}

		//One pass off the UI thread. A pass that throws answers nothing, which
		//leaves whatever the other one found in place: the scan runs on a machine
		//whose disks are not ours, and an exception there is not a reason to erase
		//the rows the player is reading.
		private void Publish(RomScanPass pass, int generation)
		{
			try {
				IReadOnlyList<RomPickerHit> hits = SuggestionSource(pass);
				Dispatcher.UIThread.Post(() => ApplySuggestions(pass, hits, generation));
			} catch {
				//The shallow pass's rows, or the roots alone, stay.
			}
		}

		private void PublishInline(RomScanPass pass, int generation)
		{
			try {
				ApplySuggestions(pass, SuggestionSource(pass), generation);
			} catch {
				//As above.
			}
		}

		//The line belongs to the scan, not to a pass: it goes when the deep pass
		//is done, once, and it goes whatever that pass answered.
		private void FinishScan(int generation)
		{
			//The scan is over, and that is a fact about the scan, not about the
			//sheet: it is recorded even when the surface was rebuilt under it, so
			//the roots list does not go on claiming a wait that has ended (the
			//scan runs once, and a lost flag would leave "Searching…" there
			//forever). Only the LINE is refused to a scan that no longer owns it:
			//by then it belongs to the scan the rebuild started.
			_scanDone = true;
			//As with the rows below: the line is the walk's own, so it goes only
			//where the walk's surface is the one up. While the library is, the
			//line belongs to the library scan (review finding 5's rule).
			if(_suggestionGeneration.IsCurrent(generation) && Mode == RomPickerMode.BrowseFile) {
				SearchingText = "";
			}
		}

		//One pass's answer. It is always cached, so later Opens include the
		//suggestions synchronously and instantly; it is only shown when the picker
		//is still on the roots - a player who has already walked into a folder is
		//never pulled back to the list under their ring.
		//
		//The deep pass REPLACES the shallow one's rows, with one exception that is
		//the point of the whole thing: a deep pass that offers FEWER rows than the
		//shallow one already did is discarded. "The scan finished" must never read
		//as "your libraries are gone".
		private void ApplySuggestions(RomScanPass pass, IReadOnlyList<RomPickerHit> hits, int generation)
		{
			//A pass superseded by a LATER walk of the same roots is dropped whole
			//(the walk runs once per session, so today this only guards a scan
			//the sheet started twice). The library's own rebuild does not
			//supersede it: this answer is about the places the walk looked in,
			//and _suggestions is the cache every later visit to the roots reads
			//- dropping it there is what left the roots bare for the session
			//(review finding 2 on #1032).
			if(!_suggestionGeneration.IsCurrent(generation)) {
				return;
			}
			IReadOnlyList<RomPickerSuggestion> offered = PlayRomPicker.Suggestions(hits, _roots);
			if(pass == RomScanPass.Deep && offered.Count < _suggestions.Count) {
				return;
			}
			if(Same(offered, _suggestions)) {
				//Nothing to rebuild, so nothing for the focus ring to lose: the
				//second open of the sheet, and a deep pass that agrees with the
				//shallow one, both land here.
				return;
			}
			_suggestions = offered;
			//The rows are the BROWSER's, so a pass that lands while the library
			//is up only fills the cache above: rewriting the list under a grid
			//the player is reading would move the ring for a surface that is not
			//on screen.
			if(IsVisible && Mode == RomPickerMode.BrowseFile && _folder is null) {
				ShowSuggestionRows();
				//The rebuilt rows are new containers, so whatever the arbiter put
				//the ring on is gone with the old one.
				SuggestionRevision++;
			}
		}

		//The suggestions sit BELOW the roots, never above them: the known roots
		//come first because they are what the app guarantees, and the discovered
		//ones follow. The rebuild takes back exactly the rows the previous pass put
		//there, so the roots above them keep their containers and their place.
		private void ShowSuggestionRows()
		{
			foreach(PlayerRomPickerRow row in _suggestionRows) {
				Rows.Remove(row);
			}
			_suggestionRows.Clear();
			foreach(RomPickerRow row in PlayRomPicker.SuggestionRows(_suggestions, ConfigManager.HomeFolder, ConsoleName)) {
				PlayerRomPickerRow vm = new(row);
				_suggestionRows.Add(vm);
				Rows.Add(vm);
			}
		}

		//A console's name is read by the player, so it comes from the locale files
		//like every other string in this sheet. PlayRomPicker owns the rules and
		//takes the words, the same split the roots' own labels already use.
		private static string ConsoleName(RomConsole console)
		{
			return console == RomConsole.Unknown ? "" : ResourceHelper.GetMessage("RomConsole" + console);
		}

		private static bool Same(IReadOnlyList<RomPickerSuggestion> left, IReadOnlyList<RomPickerSuggestion> right)
		{
			if(left.Count != right.Count) {
				return false;
			}
			for(int i = 0; i < left.Count; i++) {
				//The whole record, not just the folder: the console and the count
				//are both rendered into the row's text now, so two answers that
				//agree on the folder but not on those would leave the label the
				//previous pass wrote. (Today the walk reads both off the same
				//listing, so they cannot actually differ for one folder - which is
				//exactly why a comparison that silently stopped covering them would
				//not be noticed.)
				if(!string.Equals(left[i].Folder, right[i].Folder, StringComparison.OrdinalIgnoreCase)
					|| left[i].RomCount != right[i].RomCount
					|| left[i].Console != right[i].Console) {
					return false;
				}
			}
			return true;
		}

		private IReadOnlyList<RomPickerRoot> BuildRoots(string? gameFolder)
		{
			return PlayRomPicker.Roots(gameFolder, AppRomFolder, VolumeSource(), WholeComputerRoot);
		}

		//The whole-computer root, folder and label together in ONE place. The
		//folder is `/` on every platform that offers one, so a label naming a
		//particular machine ("This Mac") would be wrong on Linux, where the sheet
		//offers the same root; building the pair here rather than at each call
		//site is what keeps the two from drifting apart.
		private RomPickerRoot? WholeComputerRoot => WholeComputerFolder is null
			? null
			: new RomPickerRoot(ResourceHelper.GetMessage("RomPickerThisComputer"), WholeComputerFolder);

		//The configured games folder, or null when the player never set one or the
		//folder they set answers nothing (#887). Both the roots and the action row's
		//"already the games folder" test read it, so the rule is applied in one
		//place: the list must not lead with a root that opens on nothing, and the
		//action row keeps offering to designate one.
		private static string? GamesFolder => GamesFolderChoice.Usable(
			ConfigManager.Config.Preferences.OverrideGameFolder ? ConfigManager.Config.Preferences.GameFolder : null);

		//#1032 (ADR-0264 Decision 8): the folders the library reads, seeded from
		//the one games folder the app already has, so no player loses the folder
		//they had set. A player who set none gets an EMPTY list, and an empty
		//list is what the empty state is: the sheet tells them to add a library
		//folder rather than showing a grid with nothing in it.
		private static IReadOnlyList<string> ConfiguredLibraryFolders()
		{
			string? games = GamesFolder;
			return games is null ? Array.Empty<string>() : new[] { games };
		}

		//The app's own ROM folder, beside its settings. Created on demand: a
		//fresh install has no Roms folder, and a root that does not answer would
		//be a row that does nothing.
		private static string AppRomFolder
		{
			get {
				string folder = Path.Combine(ConfigManager.HomeFolder, "Roms");
				try {
					Directory.CreateDirectory(folder);
				} catch {
					//An unwritable home folder is the storage tab's problem to
					//report; here the row simply has nothing in it.
				}
				return folder;
			}
		}

	}
}
