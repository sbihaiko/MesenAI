using System.IO;

namespace Mesen.Logic;

//The game a recent-game file (.rgd) reopens: RomInfo.txt's second line, as
//SaveStateManager::SaveRecentGame writes it - "path", or "path\x1inner[\x1index]"
//for a game inside a zip (the ResourcePath string form).
public sealed record RecentGameRom(string Path, string InnerFile)
{
	//The name the W-P14 alert shows: the game inside a zip, else the file.
	public string ShownName => InnerFile.Length > 0 ? System.IO.Path.GetFileName(InnerFile) : System.IO.Path.GetFileName(Path);
}

//#676 (PRD Part B §13.5.2 W-P14): Continue or a recent card whose game did not
//open. The core's LoadRecentGame answers nothing, so the UI reads the ROM the
//.rgd names and says why: the recent file or the ROM is gone (Missing), or the
//ROM is there and fails like any open (PlayLoadFailure.Classify).
public static class PlayRecentGameFailure
{
	//RomInfo.txt: the game's name, the ROM path, the patch path. Null when no
	//ROM path is there.
	public static RecentGameRom? ParseRomInfo(string romInfoText)
	{
		string[] lines = (romInfoText ?? "").Replace("\r\n", "\n").Split('\n');
		if(lines.Length < 2 || lines[1].Trim().Length == 0) {
			return null;
		}
		string[] tokens = lines[1].Split('\x1');
		return new RecentGameRom(tokens[0], tokens.Length > 1 ? tokens[1] : "");
	}

	//recentFileExists: the .rgd itself. rom: its RomInfo.txt (null: unreadable).
	//romFileExists, knownGameExtension, isArchive: about rom.Path.
	public static (LoadFailureCause Cause, string FileName) Classify(string recentFile, bool recentFileExists, RecentGameRom? rom, bool romFileExists, bool knownGameExtension, bool isArchive)
	{
		string recentName = Path.GetFileNameWithoutExtension(recentFile ?? "");
		if(!recentFileExists) {
			return (LoadFailureCause.Missing, recentName);
		}
		if(rom == null) {
			return (LoadFailureCause.Damaged, recentName);
		}
		if(!romFileExists) {
			return (LoadFailureCause.Missing, rom.ShownName);
		}
		return (PlayLoadFailure.Classify(rom.ShownName, knownGameExtension, isArchive, rom.InnerFile.Length > 0), rom.ShownName);
	}
}
