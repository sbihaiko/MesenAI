using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Mesen.Logic;

//#1035 (ADR-0264 Decision 6.3): the Recent list as a cover source. A game the
//player has already run shows its own screenshot where box art is missing, and
//the library finds it by the ROM's full path in the Recent record.
//
//The Recent folder holds one `<rombasename>.rgd` per played game: a zip with
//Screenshot.png and RomInfo.txt, whose second line is the ROM path the Core
//reopens (PlayRecentGameFailure.ParseRomInfo). The index reads that path from
//every entry once, while it is being constructed, and answers one question:
//full ROM path -> PNG bytes, or null.
//
//That single read is a snapshot, not a live view of the folder: a `.rgd` written
//after the index was created is not in it, and a game played in this session
//keeps its generic cover until the index is thrown away and created again. The
//lookup never rescans.
//
//The lookup does re-read the `.rgd`, and takes the screenshot only if that same
//read still names the requested path: the file is named after the ROM's
//basename, so a namesake in another folder overwrites the very archive this
//index recorded, and a cover read apart from the identity it is checked
//against can be the other game's.
//
//Host-free (ADR-0123): no Avalonia, no core, no ConfigManager, no window. It
//reads files and returns bytes, so the library's cover choice is unit-tested
//without a host. A corrupt or incomplete entry is ignored - never a throw,
//never a partial image: the tile keeps its generic cover.
public sealed class RecentCoverIndex
{
	//One indexed `.rgd`: the file, and when it was last written. The stamp decides
	//which entry a ROM path resolves to when two entries name the same path.
	private readonly record struct RecentEntry(string File, DateTime WrittenAt);

	private readonly Dictionary<string, RecentEntry> _recentFileByRomPath;
	private readonly Dictionary<string, byte[]> _coverByRomPath = new(StringComparer.Ordinal);

	//The case rule a path comparison follows on this machine: Windows and macOS
	//compare paths without case, the others do not. The index takes the rule as a
	//parameter so both behaviours are testable; this is only the default the app
	//opens with.
	public static StringComparison PlatformPathComparison =>
		OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	//The app's entry point: the Recent folder, compared the platform's way.
	public static RecentCoverIndex Open(string? recentGamesFolder)
	{
		return new RecentCoverIndex(recentGamesFolder, PlatformPathComparison);
	}

	public RecentCoverIndex(string? recentGamesFolder, StringComparison pathComparison)
	{
		_recentFileByRomPath = new Dictionary<string, RecentEntry>(StringComparer.FromComparison(pathComparison));
		foreach(string recentFile in RecentFiles(recentGamesFolder)) {
			string? romPath = FullPath(ReadRomPath(recentFile));
			if(romPath == null) {
				continue;
			}
			//Two entries can name the same ROM path: two games opened out of one
			//collection.zip both record the archive's path. The later play is the
			//later file, so the later timestamp wins - never the file name, since
			//the order Directory.GetFiles hands files over is unspecified.
			RecentEntry candidate = new(recentFile, LastWriteTimeUtc(recentFile));
			if(!_recentFileByRomPath.TryGetValue(romPath, out RecentEntry current) || candidate.WrittenAt > current.WrittenAt) {
				_recentFileByRomPath[romPath] = candidate;
			}
		}
	}

	//The one lookup the library sheet makes: the screenshot of the game at this
	//full path, or null when the player never played it, the entry is corrupt, the
	//archive no longer records that path, or the folder is not there. Never throws.
	public byte[]? FindCover(string? romPath)
	{
		string? full = FullPath(romPath);
		if(full == null || !_recentFileByRomPath.TryGetValue(full, out RecentEntry entry)) {
			return null;
		}
		if(_coverByRomPath.TryGetValue(full, out byte[]? cached)) {
			return cached;
		}
		//The path and the screenshot come out of one read of the file, and the path
		//is checked before the bytes are handed over: an archive a namesake
		//overwrote since the index was built records another game, so its screenshot
		//is not this ROM's cover, and nothing is cached under this path.
		(string? recordedPath, byte[]? cover) = ReadEntry(entry.File);
		string? recorded = FullPath(recordedPath);
		if(cover == null || recorded == null || !_recentFileByRomPath.Comparer.Equals(full, recorded)) {
			return null;
		}
		//A tile is drawn many times; the zip is opened once. Only a hit is cached,
		//so an indexed `.rgd` that has no screenshot yet is read again on the next
		//lookup. A `.rgd` this index does not know about is not reached at all -
		//recreate the index to see it.
		_coverByRomPath[full] = cover;
		return cover;
	}

	//The `.rgd` files directly inside the Recent folder. A folder that is absent,
	//unreadable or unset is an empty index, not an error.
	private static IEnumerable<string> RecentFiles(string? recentGamesFolder)
	{
		if(string.IsNullOrEmpty(recentGamesFolder)) {
			return Array.Empty<string>();
		}
		try {
			return Directory.GetFiles(recentGamesFolder, "*.rgd", SearchOption.TopDirectoryOnly);
		} catch(IOException) {
			return Array.Empty<string>();
		} catch(UnauthorizedAccessException) {
			return Array.Empty<string>();
		} catch(ArgumentException) {
			return Array.Empty<string>();
		}
	}

	//The ROM path inside one `.rgd`, or null when the entry is not a readable zip
	//holding a RomInfo.txt with a path on its second line.
	private static string? ReadRomPath(string recentFile)
	{
		try {
			using FileStream fs = new(recentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using ZipArchive zip = new(fs, ZipArchiveMode.Read);
			return RomPathIn(zip);
		} catch(IOException) {
			return null;
		} catch(InvalidDataException) {
			return null;
		} catch(UnauthorizedAccessException) {
			return null;
		} catch(NotSupportedException) {
			return null;
		}
	}

	//The same `.rgd` opened once, read for the two things the lookup has to agree
	//on: the ROM path it records and the screenshot beside it. Two opens would let
	//an overwrite land between them and pair one game's path with another's cover.
	private static (string? RomPath, byte[]? Cover) ReadEntry(string recentFile)
	{
		try {
			using FileStream fs = new(recentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using ZipArchive zip = new(fs, ZipArchiveMode.Read);
			return (RomPathIn(zip), PlayHome.ReadScreenshot(zip));
		} catch(IOException) {
			return (null, null);
		} catch(InvalidDataException) {
			return (null, null);
		} catch(UnauthorizedAccessException) {
			return (null, null);
		} catch(NotSupportedException) {
			return (null, null);
		}
	}

	//RomInfo.txt of an open entry: the ROM path the Core reopens, or null when the
	//archive holds no such file or no path on its second line.
	private static string? RomPathIn(ZipArchive zip)
	{
		ZipArchiveEntry? entry = zip.GetEntry("RomInfo.txt");
		if(entry == null) {
			return null;
		}
		using Stream stream = entry.Open();
		using StreamReader reader = new(stream);
		return PlayRecentGameFailure.ParseRomInfo(reader.ReadToEnd())?.Path;
	}

	//When the `.rgd` was last written, or DateTime.MinValue when the filesystem
	//will not say: a stamp that cannot be read loses to any readable one instead
	//of deciding the winner.
	private static DateTime LastWriteTimeUtc(string recentFile)
	{
		try {
			return File.GetLastWriteTimeUtc(recentFile);
		} catch(IOException) {
			return DateTime.MinValue;
		} catch(UnauthorizedAccessException) {
			return DateTime.MinValue;
		} catch(ArgumentException) {
			return DateTime.MinValue;
		}
	}

	//The comparison is on full paths, so a relative one or a `.` segment still
	//matches the same file. A path the OS refuses is no candidate at all.
	private static string? FullPath(string? path)
	{
		if(string.IsNullOrEmpty(path)) {
			return null;
		}
		try {
			return Path.GetFullPath(path);
		} catch(ArgumentException) {
			return null;
		} catch(NotSupportedException) {
			return null;
		} catch(PathTooLongException) {
			return null;
		}
	}
}
