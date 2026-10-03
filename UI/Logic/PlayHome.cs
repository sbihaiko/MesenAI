using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Mesen.Logic;

//G.2 (PRD Part B §8, ADR-0241, §13.5.2 W-P1/W-P2): the Play home's host-free
//rules. The home is the Player-mode recent-games screen; Advanced keeps its
//classic game-selection grid (GameSelectionScreenMode) unchanged.
public enum PlayHomeKind
{
	//W-P1: no recent game - one primary action, Open a ROM… (replaces the
	//§6.2 Welcome card: recents are necessarily empty on a true first run).
	FirstRun,
	//W-P2: Continue playing (the most recent game) + Open a ROM… + the grid
	//of the other recent games.
	WithRecents
}

public enum PlayHomeOrientation
{
	AudioAndPacks,
	AudioOnly,
	PacksOnly,
	None
}

public enum LastPlayedKind
{
	Today,
	Yesterday,
	DaysAgo,
	OnDate
}

public static class PlayHome
{
	//W-P2's pack badge on a tile: the game has an HD pack where the Core looks
	//for it, HdPacks/<ROM file name>/hires.txt (HdPackLoader). The recent-game
	//file is named after the ROM, so its name is the folder's. Packs found by
	//ROM hash (MEP, community packs) go through RecentPackBadge.
	public static bool HasHdPack(string hdPackFolder, string romName)
	{
		if(string.IsNullOrEmpty(hdPackFolder) || string.IsNullOrEmpty(romName)) {
			return false;
		}
		return File.Exists(Path.Combine(hdPackFolder, romName, "hires.txt"));
	}

	//W-P2's Continue picture: the Screenshot.png the Core keeps inside a
	//recent-game file (a zip). Null when the file, the entry or the zip is
	//missing or unreadable - the card keeps its placeholder.
	//A Core screenshot is a few hundred KB at most; anything past this is not one.
	public const int MaxScreenshotBytes = 8 * 1024 * 1024;

	public static byte[]? ReadScreenshot(string recentGameFile)
	{
		try {
			if(!File.Exists(recentGameFile)) {
				return null;
			}
			using FileStream fs = new(recentGameFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using ZipArchive zip = new(fs, ZipArchiveMode.Read);
			ZipArchiveEntry? entry = zip.GetEntry("Screenshot.png");
			if(entry == null) {
				return null;
			}
			//A user-placed file can claim any size: read at most MaxScreenshotBytes
			//decompressed, whatever the entry declares, and keep the placeholder past it.
			using Stream stream = entry.Open();
			using MemoryStream copy = new();
			byte[] buffer = new byte[81920];
			int read;
			while((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
				if(copy.Length + read > MaxScreenshotBytes) {
					return null;
				}
				copy.Write(buffer, 0, read);
			}
			return copy.ToArray();
		} catch(IOException) {
			return null;
		} catch(InvalidDataException) {
			return null;
		} catch(UnauthorizedAccessException) {
			return null;
		}
	}

	//Beyond this many days the subtitle names the date instead of a count.
	public const int MaxDaysAgo = 6;

	public static PlayHomeKind Classify(int recentCount)
	{
		return recentCount > 0 ? PlayHomeKind.WithRecents : PlayHomeKind.FirstRun;
	}

	//W-P2: the Continue card already is the most recent game, so the Recent
	//grid lists the others (the wireframe's tiles never repeat the card).
	public static List<T> RecentGrid<T>(IReadOnlyList<T> recents)
	{
		return recents.Skip(1).ToList();
	}

	//W-P2's "last played today". Calendar days in local time, not 24 h spans,
	//so a game played at 23:50 reads "yesterday" ten minutes after midnight.
	public static (LastPlayedKind Kind, int Days) LastPlayed(DateTime played, DateTime now)
	{
		int days = (now.Date - played.Date).Days;
		if(days <= 0) {
			return (LastPlayedKind.Today, 0);
		}
		if(days == 1) {
			return (LastPlayedKind.Yesterday, 1);
		}
		return days <= MaxDaysAgo ? (LastPlayedKind.DaysAgo, days) : (LastPlayedKind.OnDate, days);
	}

	//W-P2's Continue subtitle: "Last played today · Contra 80s 1.2". The pack
	//clause appears only when the lookup knows the pack's name; the version only
	//when real (ShellStatusLine.PackLabel drops "" and 0.0.0).
	public static string ContinueSubtitle(string lastPlayed, string? packName, string? packVersion)
	{
		string name = (packName ?? "").Trim();
		if(name.Length == 0) {
			return lastPlayed;
		}
		string pack = ShellStatusLine.PackLabel(name, packVersion ?? "");
		return lastPlayed.Length == 0 ? pack : lastPlayed + " · " + pack;
	}

	//W-P1's orientation sentence states what will happen, so it only says what
	//the settings make true (rule 10): "Enhanced audio is on" only while the
	//pack audio layer is on, the automatic community-pack clause only while
	//AutoInstallCommunityPacks is on (ADR-0146's master switch).
	public static PlayHomeOrientation Orientation(bool enhancedAudioOn, bool autoInstallCommunityPacks)
	{
		if(enhancedAudioOn) {
			return autoInstallCommunityPacks ? PlayHomeOrientation.AudioAndPacks : PlayHomeOrientation.AudioOnly;
		}
		return autoInstallCommunityPacks ? PlayHomeOrientation.PacksOnly : PlayHomeOrientation.None;
	}
}
