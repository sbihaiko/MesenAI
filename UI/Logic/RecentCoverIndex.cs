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
//every entry once, and answers one question: full ROM path -> PNG bytes, or
//null.
//
//Host-free (ADR-0123): no Avalonia, no core, no ConfigManager, no window. It
//reads files and returns bytes, so the library's cover choice is unit-tested
//without a host. A corrupt or incomplete entry is ignored - never a throw,
//never a partial image: the tile keeps its generic cover.
public sealed class RecentCoverIndex
{
	private readonly Dictionary<string, string> _recentFileByRomPath;
	private readonly Dictionary<string, byte[]> _coverByRecentFile = new(StringComparer.Ordinal);

	//The case rule a path comparison follows on this machine, as ADR-0264 says:
	//Windows and macOS compare paths without case, the others do not. The index
	//takes the rule as a parameter so both behaviours are testable; this is only
	//the default the app opens with.
	public static StringComparison PlatformPathComparison =>
		OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	//The app's entry point: the Recent folder, compared the platform's way.
	public static RecentCoverIndex Open(string? recentGamesFolder)
	{
		return new RecentCoverIndex(recentGamesFolder, PlatformPathComparison);
	}

	public RecentCoverIndex(string? recentGamesFolder, StringComparison pathComparison)
	{
		_recentFileByRomPath = new Dictionary<string, string>(StringComparer.FromComparison(pathComparison));
		foreach(string recentFile in RecentFiles(recentGamesFolder)) {
			string? romPath = FullPath(ReadRomPath(recentFile));
			if(romPath != null) {
				//One file per ROM name, so a duplicate is the same ROM played
				//again: the later name wins, as the Recent list itself would.
				_recentFileByRomPath[romPath] = recentFile;
			}
		}
	}

	//The one lookup the library sheet makes: the screenshot of the game at this
	//full path, or null when the player never played it, the entry is corrupt, or
	//the folder is not there. Never throws.
	public byte[]? FindCover(string? romPath)
	{
		string? full = FullPath(romPath);
		if(full == null || !_recentFileByRomPath.TryGetValue(full, out string? recentFile)) {
			return null;
		}
		if(_coverByRecentFile.TryGetValue(recentFile, out byte[]? cached)) {
			return cached;
		}
		byte[]? cover = PlayHome.ReadScreenshot(recentFile);
		if(cover != null) {
			//A tile is drawn many times; the zip is opened once. A miss is not
			//cached, so a game played later in the session is still picked up.
			_coverByRecentFile[recentFile] = cover;
		}
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
			ZipArchiveEntry? entry = zip.GetEntry("RomInfo.txt");
			if(entry == null) {
				return null;
			}
			using Stream stream = entry.Open();
			using StreamReader reader = new(stream);
			return PlayRecentGameFailure.ParseRomInfo(reader.ReadToEnd())?.Path;
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

	//The comparison is on full paths, so a relative one, a `.` segment or a
	//trailing separator still matches the same file. A path the OS refuses is no
	//candidate at all.
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
