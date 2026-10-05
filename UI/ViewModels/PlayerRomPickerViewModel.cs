using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mesen.ViewModels
{
	//One row of the picker's list: a folder to descend into, or a game to open.
	//The rules that produced it (roots, ordering, what counts as a game) are
	//PlayRomPicker's, host-free; this only carries it to the template.
	public class PlayerRomPickerRow
	{
		public PlayerRomPickerRow(RomPickerRow row)
		{
			Label = row.Label;
			Path = row.Path;
			IsFolder = row.IsFolder;
		}

		public string Label { get; }
		public string Path { get; }
		public bool IsFolder { get; }
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
	//mounted volumes - and hands the chosen path to the owner, which loads it
	//through the same call the native dialog's own result took. Only a file whose
	//extension is a ROM's is a pick, and only the Play home opens it: every other
	//file choice in the app, and Advanced's own Open, stay native (ADR-0256
	//Decision 9's non-goals).
	public partial class PlayerRomPickerViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool IsVisible { get; private set; }
		[ObservableProperty] public partial string Title { get; private set; } = "";
		//What the path line reads; empty on the roots list, where there is no
		//folder to name. It is also what tells the focus arbiter the list was
		//rebuilt by a step (see MainWindowViewModel's claim): every descend and
		//every ascend changes it, and the roots list and any folder read
		//differently.
		[ObservableProperty] public partial string PathText { get; private set; } = "";
		[ObservableProperty] public partial List<PlayerRomPickerRow> Rows { get; private set; } = new();
		[ObservableProperty] public partial string EmptyText { get; private set; } = "";

		//The mounted volumes, which only the host can enumerate; a test replaces
		//it, and so does a platform the default does not know.
		public Func<IReadOnlyList<string>> VolumeSource { get; set; } = MountedVolumes.List;
		//One folder's entries. A seam for the same reason, and the one the rules
		//are pinned through in UI.Tests without touching a disk.
		public Func<string, (IReadOnlyList<string> Folders, IReadOnlyList<string> Files)> FolderSource { get; set; } = ReadFolder;

		//The chosen game. The owner loads it; the picker never does.
		public event Action<string>? RomChosen;

		private IReadOnlyList<RomPickerRoot> _roots = Array.Empty<RomPickerRoot>();
		//The folder being shown, or null for the roots list.
		private string? _folder;

		//Opens on the roots. Called by the Play home's own action, and only when
		//there is nothing to pause: the picker is what a machine with no game
		//loaded uses, so it never has an overlay under it.
		public void Open()
		{
			string? gameFolder = ConfigManager.Config.Preferences.OverrideGameFolder
				? ConfigManager.Config.Preferences.GameFolder : null;
			_roots = PlayRomPicker.Roots(gameFolder, AppRomFolder, VolumeSource());
			_folder = null;
			Title = ResourceHelper.GetMessage("RomPickerTitle");
			ShowRoots();
			IsVisible = true;
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

		//A row's own action: a folder descends, a game is the pick.
		public void Choose(PlayerRomPickerRow row)
		{
			if(!IsVisible) {
				return;
			}
			if(row.IsFolder) {
				_folder = row.Path;
				ShowFolder(row.Path);
				return;
			}
			IsVisible = false;
			RomChosen?.Invoke(row.Path);
		}

		//The game changed under it (another ROM opened, the device unplugged):
		//the sheet goes without loading anything.
		public void Hide() => IsVisible = false;

		private void ShowRoots()
		{
			PathText = "";
			SetRows(PlayRomPicker.RootRows(_roots));
		}

		private void ShowFolder(string folder)
		{
			(IReadOnlyList<string> folders, IReadOnlyList<string> files) = FolderSource(folder);
			//The list first, the path line second: PathText is the property the
			//focus arbiter watches (see the claim in PlayPadNavigationWiring), and
			//the step is only complete once the new rows exist. The arbiter is
			//posted, so it would survive either order today - this is so that it
			//still does if it is ever made to answer in the same turn.
			SetRows(PlayRomPicker.Rows(folders, files));
			PathText = PlayRomPicker.PathText(folder, _roots, ConfigManager.HomeFolder);
		}

		private void SetRows(IReadOnlyList<RomPickerRow> rows)
		{
			Rows = rows.Select(r => new PlayerRomPickerRow(r)).ToList();
			//A folder with nothing to open is not an error: it is a folder the
			//player picked and the sentence says where they are, with Back (or
			//the sheet's own Back) as the next step.
			EmptyText = Rows.Count == 0 ? ResourceHelper.GetMessage("RomPickerEmpty") : "";
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
