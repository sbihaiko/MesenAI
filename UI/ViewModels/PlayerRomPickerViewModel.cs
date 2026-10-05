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
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
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
		[ObservableProperty] public partial string Title { get; private set; } = "";
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

		//The list itself: one instance for the life of the view-model, mutated in
		//place. Suggestions arrive asynchronously, and appending to the same
		//collection keeps the rows already on screen - and the focus ring with
		//them - instead of rebuilding the list under the player.
		public ObservableCollection<PlayerRomPickerRow> Rows { get; } = new();

		//The mounted volumes, which only the host can enumerate; a test replaces
		//it, and so does a platform the default does not know.
		public Func<IReadOnlyList<string>> VolumeSource { get; set; } = MountedVolumes.List;
		//One folder's entries. A seam for the same reason, and the one the rules
		//are pinned through in UI.Tests without touching a disk.
		public Func<string, (IReadOnlyList<string> Folders, IReadOnlyList<string> Files)> FolderSource { get; set; } = ReadFolder;
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

		//The chosen game. The owner loads it; the picker never does.
		public event Action<string>? RomChosen;

		//The suggestions currently offered, for a test to read. The list they
		//become is Rows.
		public IReadOnlyList<RomPickerSuggestion> Suggestions => _suggestions;

		private IReadOnlyList<RomPickerRoot> _roots = Array.Empty<RomPickerRoot>();
		//The folder being shown, or null for the roots list.
		private string? _folder;
		private IReadOnlyList<RomPickerSuggestion> _suggestions = Array.Empty<RomPickerSuggestion>();
		//The rows standing for `_suggestions` in Rows, so a later pass can take
		//back exactly what an earlier one put there.
		private readonly List<PlayerRomPickerRow> _suggestionRows = new();
		private bool _scanStarted;
		private bool _scanDone;

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
			ShowRoots();
			IsVisible = true;
			StartScan();
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
			if(_folder is null) {
				IsVisible = false;
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
			//says it can: a folder that answers nothing is deliberately NOT made a
			//root (#887), so the path line reads the same shortened path before and
			//after and the arbiter sees no change in any of the three properties it
			//watches. Without this the pad is left with nothing focused, the
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

			if(RunScanInline) {
				PublishInline(RomScanPass.Shallow);
				PublishInline(RomScanPass.Deep);
				FinishScan();
				return;
			}
			Task.Run(() => {
				Publish(RomScanPass.Shallow);
				Publish(RomScanPass.Deep);
				Dispatcher.UIThread.Post(FinishScan);
			});
		}

		//One pass off the UI thread. A pass that throws answers nothing, which
		//leaves whatever the other one found in place: the scan runs on a machine
		//whose disks are not ours, and an exception there is not a reason to erase
		//the rows the player is reading.
		private void Publish(RomScanPass pass)
		{
			try {
				IReadOnlyList<RomPickerHit> hits = SuggestionSource(pass);
				Dispatcher.UIThread.Post(() => ApplySuggestions(pass, hits));
			} catch {
				//The shallow pass's rows, or the roots alone, stay.
			}
		}

		private void PublishInline(RomScanPass pass)
		{
			try {
				ApplySuggestions(pass, SuggestionSource(pass));
			} catch {
				//As above.
			}
		}

		//The line belongs to the scan, not to a pass: it goes when the deep pass
		//is done, once, and it goes whatever that pass answered.
		private void FinishScan()
		{
			_scanDone = true;
			SearchingText = "";
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
		private void ApplySuggestions(RomScanPass pass, IReadOnlyList<RomPickerHit> hits)
		{
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
			if(IsVisible && _folder is null) {
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

		//A folder's entries, split. A folder that cannot be read (a volume pulled
		//out between listing and descending, a permission) reads as empty rather
		//than throwing out of a pad press.
		private static (IReadOnlyList<string> Folders, IReadOnlyList<string> Files) ReadFolder(string folder)
		{
			try {
				string[] folders = Directory.GetDirectories(folder);
				string[] files = Directory.GetFiles(folder);
				return (folders, files);
			} catch {
				return (Array.Empty<string>(), Array.Empty<string>());
			}
		}
	}
}
