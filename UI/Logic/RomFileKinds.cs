using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.Logic;

//#845 (ADR-0256 Decision 9): what the picker may offer as a game. The list is
//the one FolderHelper carried, moved here because this is the host-free
//decision layer: the picker's rules are dual-compiled into UI.Tests (ADR-0123),
//and FolderHelper is host-aware, so the list could not be reached from there.
//FolderHelper now delegates, so there is still one table.
//
//An archive counts: everything downstream accepts one (LoadRomHelper unpacks it
//and, when it holds more than one game, asks which - the same path the native
//dialog's result took), and a library on a cabinet is usually zipped.
public static class RomFileKinds
{
	//The ROM extensions are RomConsoleKinds' table, asked rather than copied:
	//"is this a ROM" and "which console is this" are the same list, and a second
	//copy of it is a second thing to forget to update.
	private static readonly HashSet<string> _archiveExtensions = new(StringComparer.OrdinalIgnoreCase) {
		".7z", ".zip"
	};

	public static bool IsRomFile(string path)
	{
		return RomConsoleKinds.OfFile(path) != RomConsole.Unknown;
	}

	public static bool IsArchiveFile(string path)
	{
		return _archiveExtensions.Contains(Path.GetExtension(path));
	}

	//The picker's own predicate: a row the pad can confirm into a load.
	public static bool IsOpenable(string path)
	{
		return IsRomFile(path) || IsArchiveFile(path);
	}

	//A dot-name: never a row, at any level (a `.DS_Store`, `.git`, a hidden
	//folder). ADR-0256 Decision 9 leaves hidden-file policy out of scope, and
	//"never show one" is the whole of it.
	public static bool IsHiddenName(string name)
	{
		return name.Length == 0 || name[0] == '.';
	}
}
