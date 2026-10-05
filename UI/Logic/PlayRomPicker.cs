using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mesen.Logic;

//Where the picker can start from. `Folder` is what opening it means; `Label` is
//what the player reads (a volume's name, "Your games").
public sealed record RomPickerRoot(string Label, string Folder);

//What a row does when the pad confirms it. A folder descends, a game is the
//pick, and the action row names the folder it sits in as the games folder. The
//enum is the repo's idiom for a discriminated row (PlayerSettingsRowKind).
public enum RomPickerRowKind
{
	Folder,
	Game,
	Action
}

//One row. A folder row descends; a file row is a game to open. `Path` is the
//identity the picker walks by, and it is what the row's own action receives.
//
//`IsFolder` stays a computed property: the row gained a third kind, and every
//reader that asked the old bool - the template, the view-model mirror and the
//tests that pinned #845 - must keep reading the same answer.
public sealed record RomPickerRow(string Label, string Path, RomPickerRowKind Kind)
{
	public bool IsFolder => Kind == RomPickerRowKind.Folder;
}

//A folder the scan found, and how many openable files it holds directly in it.
public sealed record RomPickerHit(string Folder, int RomCount);

//A hit the picker is willing to offer: the same place and count, after ranking,
//dedupe and the cap. It is a separate type so "found" and "offered" cannot be
//confused at a call site.
public sealed record RomPickerSuggestion(string Folder, int RomCount);

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
	//How many discovered libraries the roots list may add below the roots.
	public const int MaxSuggestions = 5;

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
	//
	//`wholeComputer` is the user's own pick - a root at `/` so a library the
	//scan does not reach is still reachable by hand. It is the LAST of the fixed
	//roots and is supplied by the host like the volumes are (both its folder and
	//its label), so this file stays host-free and testable on any platform.
	public static IReadOnlyList<RomPickerRoot> Roots(string? gameFolder, string appRomFolder, IEnumerable<string> volumes, RomPickerRoot? wholeComputer = null)
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
		if(wholeComputer is not null) {
			Add(wholeComputer.Label, wholeComputer.Folder);
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
			rows.Add(new RomPickerRow(Path.GetFileName(folder), folder, RomPickerRowKind.Folder));
		}
		foreach(string file in Ordered(filePaths).Where(RomFileKinds.IsOpenable)) {
			rows.Add(new RomPickerRow(Path.GetFileName(file), file, RomPickerRowKind.Game));
		}
		return rows;
	}

	//The roots as rows, for the list the picker opens on. A root is a place, so
	//every one of them is a folder row: Confirm descends into it.
	public static IReadOnlyList<RomPickerRow> RootRows(IReadOnlyList<RomPickerRoot> roots)
	{
		return roots.Select(r => new RomPickerRow(r.Label, r.Folder, RomPickerRowKind.Folder)).ToList();
	}

	//The action row: the one row that is not a place and not a game, but the
	//press that names the folder it sits in as the games folder. Its subject is
	//always the folder currently shown, which is why `folder` is its path.
	public static RomPickerRow ActionRow(string label, string folder)
	{
		return new RomPickerRow(label, folder, RomPickerRowKind.Action);
	}

	//One folder's rows: the action row first when the folder is not already the
	//games folder, then the folders and the games. `makeGamesLabel` is injected
	//by the caller so this file stays string-free (the rule, not the wording).
	public static IReadOnlyList<RomPickerRow> FolderRows(string folder, string? gamesFolder, string makeGamesLabel, IEnumerable<string> folderPaths, IEnumerable<string> filePaths)
	{
		List<RomPickerRow> rows = new();
		//The folder that is already the games folder has nothing to make, so the
		//row is absent - not a dead control.
		if(gamesFolder is null || !SameFolder(folder, gamesFolder)) {
			rows.Add(ActionRow(makeGamesLabel, folder));
		}
		rows.AddRange(Rows(folderPaths, filePaths));
		return rows;
	}

	//How many rows a confirm can act on: everything but the action rows, which
	//are a press of their own. EmptyText is "" while this is not zero, so an
	//always-present action row can no longer make an empty folder look full.
	public static int ContentCount(IReadOnlyList<RomPickerRow> rows)
	{
		return rows.Count(r => r.Kind != RomPickerRowKind.Action);
	}

	//The hits the picker is willing to offer, in order. Ranked by how many games
	//the folder holds, then by path; a hit is dropped when a kept one is its
	//ancestor or its descendant (a folder that contains a better library is not
	//a second choice), when it is a specific root or under one, and past the cap.
	//
	//A root at the whole computer is deliberately NOT part of the exclusion set:
	//every folder is under it, so excluding its subtree would discard every
	//suggestion there is.
	public static IReadOnlyList<RomPickerSuggestion> Suggestions(IEnumerable<RomPickerHit> hits, IReadOnlyList<RomPickerRoot> roots)
	{
		//One spelling per folder, the biggest count winning.
		List<RomPickerHit> ranked = hits
			.Select(h => new RomPickerHit(Normalize(h.Folder) ?? "", h.RomCount))
			.Where(h => h.Folder.Length > 0)
			.GroupBy(h => h.Folder, StringComparer.OrdinalIgnoreCase)
			.Select(g => new RomPickerHit(g.Key, g.Max(h => h.RomCount)))
			.OrderByDescending(h => h.RomCount)
			.ThenBy(h => h.Folder, StringComparer.OrdinalIgnoreCase)
			.ToList();

		//The specific roots only. A root at the whole computer is deliberately
		//left out: every folder is under it, so excluding its subtree would
		//discard every suggestion there is.
		string[] excluded = roots
			.Select(r => Normalize(r.Folder))
			.Where(r => r is not null && !IsFilesystemRoot(r))
			.Select(r => r!)
			.ToArray();

		List<RomPickerSuggestion> kept = new();
		foreach(RomPickerHit hit in ranked) {
			//Already reachable through a root: not offered again.
			if(excluded.Any(root => IsSelfOrDescendant(hit.Folder, root))) {
				continue;
			}
			//A folder related to one already kept is not a second choice - and
			//because the walk is count-first, the bigger library is the one kept.
			if(kept.Any(k => IsRelated(k.Folder, hit.Folder))) {
				continue;
			}
			kept.Add(new RomPickerSuggestion(hit.Folder, hit.RomCount));
			if(kept.Count >= MaxSuggestions) {
				break;
			}
		}
		return kept;
	}

	//What separates a suggestion's own name from the path it sits in. Two spaces
	//around a middle dot: a separator a reader's eye skips, not a word.
	private const string SuggestionSeparator = "  ·  ";

	//The suggestions as rows: a suggestion is a place, so Confirm descends into
	//it exactly like a root. Its label leads with the library's OWN NAME and only
	//then gives the path it sits in, because the sheet is 480 px and trims the
	//label: a label that led with the path lost exactly the segment that says
	//which library the row is (measured on the requesting machine, its first
	//suggestion rendered as `~/VSCodeProjects/EMULADORES/2. Switch/G3 -
	//Nitendinho/ro…`).
	public static IReadOnlyList<RomPickerRow> SuggestionRows(IReadOnlyList<RomPickerSuggestion> suggestions, string homeFolder)
	{
		return suggestions
			.Select(s => new RomPickerRow(SuggestionLabel(s.Folder, homeFolder), s.Folder, RomPickerRowKind.Folder))
			.ToList();
	}

	//`<name>  ·  <shortened parent>`, e.g.
	//`roms  ·  ~/VSCodeProjects/EMULADORES/2. Switch/G3 - Nitendinho`. A folder
	//with no parent to name (a filesystem root, which the ranking never keeps)
	//falls back to the shortened path alone.
	private static string SuggestionLabel(string folder, string homeFolder)
	{
		string full = Normalize(folder) ?? folder;
		string name = Path.GetFileName(full);
		string? parent = Path.GetDirectoryName(full);
		if(name.Length == 0) {
			return Shorten(full, homeFolder);
		}
		return string.IsNullOrEmpty(parent) ? name : name + SuggestionSeparator + Shorten(parent, homeFolder);
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
		return Shorten(folder, homeFolder);
	}

	//The home folder shortened to `~` so a long path stays one line in the sheet;
	//anything else is the whole path, there being nothing to shorten it to. The
	//home folder itself is `~` too, so a library sitting directly in it reads as
	//`roms  ·  ~` rather than as that library's absolute path.
	private static string Shorten(string folder, string homeFolder)
	{
		string full = Normalize(folder) ?? folder;
		string home = string.IsNullOrEmpty(homeFolder) ? "" : Normalize(homeFolder) ?? "";
		if(home.Length == 0) {
			return full;
		}
		if(string.Equals(full, home, StringComparison.OrdinalIgnoreCase)) {
			return "~";
		}
		if(full.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
			return "~" + full[home.Length..];
		}
		return full;
	}

	//True when `folder` is `ancestor` itself or sits under it.
	private static bool IsSelfOrDescendant(string folder, string ancestor)
	{
		return SameFolder(folder, ancestor)
			|| folder.StartsWith(ancestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	//True when the two folders are related - equal, or one holding the other.
	private static bool IsRelated(string left, string right)
	{
		return IsSelfOrDescendant(left, right) || IsSelfOrDescendant(right, left);
	}

	//A root that covers the whole computer: `/` on macOS and Linux, a drive root
	//("C:\") on Windows. It navigates like any root, but the scan never excludes
	//its subtree - every folder is under it.
	private static bool IsFilesystemRoot(string folder)
	{
		string? full = Normalize(folder);
		string? root = full is null ? null : Path.GetPathRoot(full);
		return root is not null && SameFolder(root, full);
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
