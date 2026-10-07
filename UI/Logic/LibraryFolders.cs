using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.Logic;

//#1036 (host-free half), ADR-0264 Decision 8: the LIST of folders the Play
//"Open a game" sheet scans, and the header that counts what they hold. The sheet
//itself is #1032's; this file is only the list, so the rules about which folders
//are in it are pinned here in UI.Tests without a window.
//
//It is a pure list over paths. It reads no disk (ADR-0123: BCL only, no Avalonia,
//no EmuApi), so "removing a folder never deletes a file" is not a promise the
//code has to keep carefully - there is no file API here to call by mistake. The
//only thing it owns is strings, which is also why the scan (#1032) can consume it
//later without learning a type from this file: entry paths stay strings.

//What an add did, so the caller can say something honest about it rather than
//guess from a list it has to diff. `Added` and `MergedWithListed` changed the
//list; the other three left it exactly as it was.
public enum LibraryFolderChange
{
	//The folder was not there and is now the list's last row.
	Added,

	//The same folder is already listed, under another spelling of the same path.
	AlreadyListed,

	//The folder is inside one that is already listed, so every game under it is
	//already reached by the list; it is absorbed rather than listed twice.
	CoveredByListed,

	//The folder CONTAINS folders that are listed; those rows go away and this
	//ancestor takes their place, because it already reaches their games.
	MergedWithListed,

	//Nothing to add: a blank path, or one the platform's path rules refuse.
	Invalid
}

//The list an add leaves behind, and what happened to it. The list is always a
//fresh one, so a caller that keeps the old list keeps the old list.
public sealed record LibraryFolderEdit(IReadOnlyList<string> Folders, LibraryFolderChange Change);

//The library folders preference: what "Your library" scans.
public static class LibraryFolders
{
	//Is this the same folder? Two folders spelled differently are one folder only
	//where the file system says so: Windows and macOS fold case, Linux does not,
	//and folding it there would silently drop one of two folders that really are
	//two. The comparison follows the OS rather than picking a side, and every
	//comparison in this file goes through it.
	private static readonly StringComparison _comparison =
		OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

	private static readonly StringComparer _comparer = StringComparer.FromComparison(_comparison);

	//The one spelling of a folder this list stores. The rule, and its limits:
	//
	//- trailing separators go away, and `.` / `..` / doubled separators are
	//  resolved (`Path.GetFullPath`, then `Path.TrimEndingDirectorySeparator` -
	//  `GetFullPath` alone keeps a trailing separator, which is exactly the
	//  spelling a native picker hands back), so the picker's `/games/` and the
	//  sheet's `/games/.` are one row and the nested test below is about folders
	//  rather than about how they were typed. A root survives: `/` stays `/`;
	//- a relative path is resolved against the process's current directory, which
	//  is what the platform's own rule does - a library folder a caller passes in
	//  is a folder the player chose in a picker, so it is absolute anyway;
	//- **symlinks are NOT resolved.** `Path.GetFullPath` is lexical, and resolving
	//  a link is a disk read - the thing this file does not do. So a folder reached
	//  through a symlink and the same folder reached directly are two rows if the
	//  two paths differ. That is accepted: the game under them is one game, and
	//  `Union` below is what keeps it from being drawn twice.
	//
	//Null when the path cannot be one: blank, or a string the platform refuses.
	public static string? Normalize(string? folder)
	{
		if(string.IsNullOrWhiteSpace(folder)) {
			return null;
		}
		try {
			return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));
		} catch(Exception) {
			//Invalid characters, a path too long, a path the platform does not
			//support. None of those is a folder, so there is nothing to list.
			return null;
		}
	}

	//Is `child` strictly below `parent`? The separator after the prefix is what
	//keeps `/games2` from reading as inside `/games`.
	private static bool IsInside(string parent, string child)
	{
		if(_comparer.Equals(parent, child)) {
			return false;
		}
		string prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
		return child.StartsWith(prefix, _comparison);
	}

	private static IReadOnlyList<string> Copy(IReadOnlyList<string> folders)
	{
		return new List<string>(folders);
	}

	//Decision 8's seeding, and it is a first-run act only: the single
	//`Preferences.GameFolder` the app already had becomes the list's first folder,
	//so no player loses the folder they had set - and a list that already names
	//folders is never touched again. `OverrideGameFolder` is read the way every
	//other call site reads it (`OverrideGameFolder ? GameFolder : null`): a folder
	//the player never designated is not their library, so it does not seed one.
	public static IReadOnlyList<string> Seed(IReadOnlyList<string> folders, bool overrideGameFolder, string? gameFolder)
	{
		if(folders.Count > 0 || !overrideGameFolder) {
			return Copy(folders);
		}
		string? seeded = Normalize(gameFolder);
		return seeded != null ? new List<string> { seeded } : Copy(folders);
	}

	//The nested case, decided: **absorb, do not reject.** A folder inside one that
	//is already listed adds no game the list does not already reach - the scan
	//walks a folder's whole subtree - so listing it too would show the player two
	//rows that are one library and make the header's folder count claim a folder
	//that contributes nothing. The same rule read the other way is why adding a
	//folder that CONTAINS listed ones takes their place instead of standing beside
	//them. Neither is an error: `CoveredByListed` and `MergedWithListed` are
	//answers, not refusals, and in both the list is already right.
	public static LibraryFolderEdit Add(IReadOnlyList<string> folders, string? folder)
	{
		string? added = Normalize(folder);
		if(added == null) {
			return new LibraryFolderEdit(Copy(folders), LibraryFolderChange.Invalid);
		}

		foreach(string listed in folders) {
			string? normalized = Normalize(listed);
			if(normalized == null) {
				continue;
			}
			if(_comparer.Equals(normalized, added)) {
				return new LibraryFolderEdit(Copy(folders), LibraryFolderChange.AlreadyListed);
			}
			if(IsInside(normalized, added)) {
				return new LibraryFolderEdit(Copy(folders), LibraryFolderChange.CoveredByListed);
			}
		}

		List<string> merged = new();
		bool replacedAny = false;
		foreach(string listed in folders) {
			string? normalized = Normalize(listed);
			if(normalized != null && IsInside(added, normalized)) {
				replacedAny = true;
				continue;
			}
			merged.Add(listed);
		}
		merged.Add(added);
		return new LibraryFolderEdit(merged, replacedAny ? LibraryFolderChange.MergedWithListed : LibraryFolderChange.Added);
	}

	//Removing takes the row out of the list and does nothing else - there is no
	//file call in this file, and a folder the player takes out of their library is
	//still their folder. A path that is not listed is not an error either: the
	//list comes back as it was.
	public static IReadOnlyList<string> Remove(IReadOnlyList<string> folders, string? folder)
	{
		string? removed = Normalize(folder);
		if(removed == null) {
			return Copy(folders);
		}
		List<string> kept = new();
		foreach(string listed in folders) {
			string? normalized = Normalize(listed);
			if(normalized != null && _comparer.Equals(normalized, removed)) {
				continue;
			}
			kept.Add(listed);
		}
		return kept;
	}

	//The union of what several folders answered, each path once: two library
	//folders that overlap - or a symlink and its target - are one game in the grid,
	//not two. The order is first-seen, so the caller's folder order is what decides
	//the scan's order.
	public static IReadOnlyList<string> Union(IEnumerable<IEnumerable<string>> perFolder)
	{
		List<string> union = new();
		HashSet<string> seen = new(_comparer);
		foreach(IEnumerable<string> entries in perFolder) {
			foreach(string entry in entries) {
				string? normalized = Normalize(entry);
				if(normalized != null && seen.Add(normalized)) {
					union.Add(normalized);
				}
			}
		}
		return union;
	}

	//Decision 8's header, and the exact literal is the ADR's: "Your library · N
	//games in M folders", with the one-count reading `1 game` / `1 folder` rather
	//than a plural that says the player has two of something.
	//
	//M counts the folders that ANSWERED with games, not every row in the list: the
	//header is the line that tells the player the scan found their collection, and
	//a folder holding no ROM would make it claim one that gave the grid nothing
	//(ADR-0260's rule for the older single games folder, read the same way).
	public static string Header(int games, int folders)
	{
		return "Your library · " + games + (games == 1 ? " game" : " games")
			+ " in " + folders + (folders == 1 ? " folder" : " folders");
	}
}
