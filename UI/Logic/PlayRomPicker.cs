using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mesen.Logic;

//Where the picker can start from. `Folder` is what opening it means; `Label` is
//what the player reads (a volume's name, "Your games").
public sealed record RomPickerRoot(string Label, string Folder);

//One row. A folder row descends; a file row is a game to open. `Path` is the
//identity the picker walks by, and it is what the row's own action receives.
public sealed record RomPickerRow(string Label, string Path, bool IsFolder);

//#845 (ADR-0256 Decision 9): the ROM picker's rules, host-free so UI.Tests can
//pin them against a fake tree (ADR-0123). The view-model reads the filesystem
//and hands the names in; nothing here touches disk, the platform or the core.
//
//The shape is one list. The picker opens on the roots - a mounted volume, the
//app's ROM folder, the configured game folder when there is one - and choosing
//a folder row descends into it. Inside a folder the rows are the folders, then
//the openable files. Back ascends one level and, on the roots, dismisses; that
//is the whole of the navigation, so there is no Up row and no second list for
//the pad's focus to cross.
public static class PlayRomPicker
{
	//The roots, in the order the picker offers them. `gameFolder` is the
	//configured game folder and is null when Preferences.OverrideGameFolder is
	//off. Duplicates and blanks are dropped - a volume that is also the game
	//folder is one row - and the order is kept otherwise, so the caller decides
	//what comes first.
	//
	//The configured folder leads because a machine set up on purpose should land
	//there; the app's own folder follows so a library dropped next to it is one
	//press away; the volumes come last because they are the ones that change
	//(a stick plugged in for one evening).
	public static IReadOnlyList<RomPickerRoot> Roots(string? gameFolder, string appRomFolder, IEnumerable<string> volumes)
	{
		List<RomPickerRoot> roots = new();
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

		void Add(string label, string? folder)
		{
			if(string.IsNullOrWhiteSpace(folder)) {
				return;
			}
			string? full = Normalize(folder);
			if(full is null || !seen.Add(full)) {
				return;
			}
			roots.Add(new RomPickerRoot(label, full));
		}

		if(!string.IsNullOrWhiteSpace(gameFolder)) {
			Add("Your games", gameFolder);
		}
		Add("MesenAI's games folder", appRomFolder);
		foreach(string volume in volumes) {
			//A platform with no volumes answers an empty list; a caller that
			//answers a blank one is saying the same thing.
			if(!string.IsNullOrWhiteSpace(volume)) {
				Add(VolumeLabel(volume), volume);
			}
		}

		return roots;
	}

	//The rows of one folder. Folders first (they are the way through), then the
	//openable files, each group alphabetical and case-insensitive; everything
	//else is not a row. A dot-name is never a row at any level - `.DS_Store`,
	//a `.git`, a hidden folder (ADR-0256 Decision 9 leaves hidden-file policy out
	//of scope, and "never show one" is the whole of it).
	public static IReadOnlyList<RomPickerRow> Rows(IEnumerable<string> folderPaths, IEnumerable<string> filePaths)
	{
		List<RomPickerRow> rows = new();
		foreach(string folder in Ordered(folderPaths)) {
			rows.Add(new RomPickerRow(Path.GetFileName(folder), folder, IsFolder: true));
		}
		foreach(string file in Ordered(filePaths).Where(RomFileKinds.IsOpenable)) {
			rows.Add(new RomPickerRow(Path.GetFileName(file), file, IsFolder: false));
		}
		return rows;
	}

	//The roots as rows, for the list the picker opens on. A root is a place, so
	//every one of them is a folder row: Confirm descends into it.
	public static IReadOnlyList<RomPickerRow> RootRows(IReadOnlyList<RomPickerRoot> roots)
	{
		return roots.Select(r => new RomPickerRow(r.Label, r.Folder, IsFolder: true)).ToList();
	}

	//One step up. Null means there is nowhere up: the folder is a root, and the
	//step back from it is the roots list, which is the picker's own state rather
	//than a folder.
	public static string? Ascend(string folder, IReadOnlyList<RomPickerRoot> roots)
	{
		string? full = Normalize(folder);
		if(full is null || roots.Any(r => SameFolder(r.Folder, full))) {
			return null;
		}
		string? parent = Path.GetDirectoryName(full);
		return string.IsNullOrEmpty(parent) ? null : parent;
	}

	//What the path line reads. A root is named by its own label ("Your games"
	//says more than the folder it points at), the app's home folder is shortened
	//to `~` so a long macOS path stays one line in the sheet, and an empty string
	//means "the roots list": the picker shows no path line there.
	public static string PathText(string? folder, IReadOnlyList<RomPickerRoot> roots, string homeFolder)
	{
		if(string.IsNullOrEmpty(folder)) {
			return "";
		}
		RomPickerRoot? root = roots.FirstOrDefault(r => SameFolder(r.Folder, folder));
		if(root != null) {
			return root.Label;
		}
		string full = Normalize(folder) ?? folder;
		string home = string.IsNullOrEmpty(homeFolder) ? "" : Normalize(homeFolder) ?? "";
		if(home.Length > 0 && full.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
			return "~" + full[home.Length..];
		}
		return full;
	}

	//A volume's row label: its last segment ("/Volumes/My Book" -> "My Book"),
	//and the volume itself when that is all there is (a Windows drive root).
	private static string VolumeLabel(string volume)
	{
		//Null here means Roots is about to drop the volume; the label it returns
		//for it is never read, so the volume's own text will do.
		string full = Normalize(volume) ?? volume;
		string name = Path.GetFileName(full);
		return string.IsNullOrEmpty(name) ? full : name;
	}

	//One spelling per folder, so two names for the same place are one root. The
	//trailing separator goes, except where it is the whole path (the filesystem
	//root) or all but a Windows drive letter's.
	//
	//Null when the platform cannot spell the folder at all - the configured game
	//folder is a string out of settings.json, which a person can edit, and
	//Path.GetFullPath throws on one with a null character in it. That throw would
	//leave the press that opened the picker and the sheet would never appear, so a
	//path that cannot be spelled is not a root (the same answer a blank gets).
	private static string? Normalize(string folder)
	{
		try {
			string full = Path.GetFullPath(folder);
			string trimmed = full.TrimEnd(Path.DirectorySeparatorChar);
			return trimmed.Length == 0 || trimmed.EndsWith(":", StringComparison.Ordinal) ? full : trimmed;
		} catch(Exception) {
			return null;
		}
	}

	private static IEnumerable<string> Ordered(IEnumerable<string> paths)
	{
		return paths.Where(p => !RomFileKinds.IsHiddenName(Path.GetFileName(p)))
			.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
	}

	private static bool SameFolder(string? left, string? right)
	{
		if(string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) {
			return false;
		}
		string? leftFull = Normalize(left);
		string? rightFull = Normalize(right);
		//Neither is a folder the platform can name, so neither is the other.
		return leftFull is not null && rightFull is not null
			&& string.Equals(leftFull, rightFull, StringComparison.OrdinalIgnoreCase);
	}
}
