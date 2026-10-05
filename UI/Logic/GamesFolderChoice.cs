using System;
using System.IO;
using System.Linq;

namespace Mesen.Logic;

//#887: which folder the app opens on, once the player has named a games folder.
//
//The rule is that a named games folder is used only while it still answers
//something. Asking whether the path is a directory is not enough, and that is not
//hypothetical: macOS `/home` is an autofs node - `isdir` true, `listdir` empty,
//declared as `/home auto_home -nobrowse,hidefromfinder` in /etc/auto_master - so a
//games folder left pointing at it starts Open ROM on a folder with nothing in it
//and leads the picker's roots list with the same dead end. The picker's own "make
//this my games folder" action will happily set it, because it is reachable from
//the This Mac root.
//
//Host-free (ADR-0123): BCL and a folder path, nothing else, so UI.Tests runs it
//against real temp folders (GamesFolderChoiceTests).
public static class GamesFolderChoice
{
	//True when the folder holds anything at all. A path that cannot be read is the
	//same answer as one that is empty: the app has nothing to open on there, and
	//the caller's job is to go somewhere else rather than to report why.
	//
	//Asking whether the path IS a directory is what this replaced, and it is not
	//the same question - which is the whole of #887. A folder can be there and hold
	//nothing, and `/home` is the case that was measured.
	public static bool HasEntries(string? folder)
	{
		if(string.IsNullOrWhiteSpace(folder)) {
			return false;
		}
		try {
			//Enumerated rather than counted: a folder with a million files answers on
			//the first one, and this runs on every Open ROM.
			return Directory.EnumerateFileSystemEntries(folder).Any();
		} catch(Exception) {
			//Unreadable, not a directory, gone between the setting and the press, or
			//a permission this process does not have. None of those is somewhere to
			//open on. The picker's own rows already treat an unreadable folder the
			//same way.
			return false;
		}
	}

	//The games folder, or null when it is not one the app can open on. A blank or
	//absent setting is the same answer: nothing was designated.
	public static string? Usable(string? gameFolder)
	{
		return HasEntries(gameFolder) ? gameFolder : null;
	}

	//Where the app starts: the designated folder, else the last game the player
	//opened, else nowhere in particular (the caller decides what that means).
	//
	//The fallback is the last-opened game rather than nothing because a player who
	//has a games folder set has almost certainly opened something from it, so the
	//folder holding their last game is the closest thing to it that still works.
	public static string? StartFolder(string? gameFolder, string? lastOpenedFolder)
	{
		string? usable = Usable(gameFolder);
		return usable ?? (string.IsNullOrWhiteSpace(lastOpenedFolder) ? null : lastOpenedFolder);
	}
}
