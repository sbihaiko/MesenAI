using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mesen.Logic;

//The console a folder's own files say it belongs to. The picker talks about
//these, not about folder names: a library on disk is a directory called `roms`
//under a directory named after a console, so every one of them is called `roms`
//and the ONLY segment that tells two apart is the extension of what is inside.
//
//The list is exactly the consoles this emulator runs. A console it does not run
//has no member here, which is what makes `Unknown` an honest answer rather than
//a guess - the picker must never claim a console for a folder whose ROMs it
//cannot open.
//
//No display word lives here: a console name is shown to the player, so it comes
//from the locale files like every other string the player reads (the caller
//resolves it, the way the roots' labels already do).
//The granularity is the player's, not the core's: the core files Game Gear
//under its Sms console type (InteropEnums.ConsoleType) and has no Game Boy
//Color at all, but a folder of `.gg` files is a different machine to the person
//reading the row, and the exit message already tells them so
//(LoadFailedBodyNotAGame lists Game Boy and Game Boy Color separately).
public enum RomConsole
{
	Unknown = 0,
	Nes,
	GameBoy,
	GameBoyColor,
	GameBoyAdvance,
	MasterSystem,
	GameGear
}

//`extension -> RomConsole`, and the ONE table of what a ROM file is: RomFileKinds
//asks this rather than keeping a second list, the same reason FolderHelper
//delegates to RomFileKinds. Two tables of one thing drift apart.
//
//Host-free (ADR-0123): it reads names, never a disk, so the picker's rules stay
//dual-compiled into UI.Tests.
public static class RomConsoleKinds
{
	private static readonly Dictionary<string, RomConsole> _byExtension = new(StringComparer.OrdinalIgnoreCase) {
		[".nes"] = RomConsole.Nes,
		[".unif"] = RomConsole.Nes,
		[".unf"] = RomConsole.Nes,
		[".fds"] = RomConsole.Nes,
		[".qd"] = RomConsole.Nes,
		[".studybox"] = RomConsole.Nes,

		[".gb"] = RomConsole.GameBoy,
		[".gbx"] = RomConsole.GameBoy,
		//`.gbc` is its own member: a Game Boy Color library is a different row
		//from a Game Boy one, and merging them would name half of them wrongly.
		[".gbc"] = RomConsole.GameBoyColor,

		[".gba"] = RomConsole.GameBoyAdvance,

		[".sms"] = RomConsole.MasterSystem,
		[".sg"] = RomConsole.MasterSystem,

		[".gg"] = RomConsole.GameGear
	};

	//The console one file names, or Unknown for anything else - an archive, a
	//save file, another machine's ROM.
	public static RomConsole OfFile(string path)
	{
		return _byExtension.TryGetValue(Path.GetExtension(path), out RomConsole console)
			? console
			: RomConsole.Unknown;
	}

	//The console a folder's files name, by majority: a library that grew a few
	//stray files is still that library. A file naming no console is not a vote,
	//so a folder of archives is Unknown - an archive's console is not knowable
	//from its name, and guessing it is how a player is offered a game that
	//cannot run.
	//
	//Ordered by count then by the enum's own order, so the answer never depends
	//on the order the host happened to list the files in.
	public static RomConsole OfFiles(IEnumerable<string> paths)
	{
		return paths
			.Select(OfFile)
			.Where(console => console != RomConsole.Unknown)
			.GroupBy(console => console)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key)
			.Select(group => group.Key)
			.FirstOrDefault();
	}
}
