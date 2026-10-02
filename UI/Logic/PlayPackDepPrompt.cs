using System;
using System.IO;

namespace Mesen.Logic;

//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P16): a pack waits for a file only
//the user can add. Replaces the OSD line of
//CommunityPackInstallService.NotifyPendingDeps; the data is the same
//CommunityPackDepPrompt. MEP-v1 §6 and MEI-v1 §2.3 are unchanged: the app
//never fetches a user_supplied dep by itself, and a file is matched by its
//content hash, never by its name (CommunityPackDepResolver).
public enum PackDepFileCheck
{
	Accepted,
	//"That is not the file this pack was made with" - refused inline.
	WrongFile
}

public enum PackDepDropTarget
{
	Copy,
	//The same file is already in the drop folder.
	AlreadyThere,
	//A different file has that name: refused inline, never overwritten.
	NameTaken
}

public enum PackDepPrimaryAction
{
	//Today: only a ROM reload re-resolves deps (OnGameLoaded returns early on
	//a power cycle, #156), so adding the file reloads the game.
	AddAndRestart,
	//With ADR-0244 (P.9) the pack change applies in place.
	Add
}

public static class PlayPackDepPrompt
{
	//Rule 2: drop zone, Show Folder, Play Without It, Choose File… = 4.
	public const int ControlCount = 4;

	//P.9 (ADR-0244) is accepted but not implemented: no in-place apply yet.
	public const bool AppliesInPlace = false;

	public static PackDepFileCheck Check(string actualSha256, string expectedSha256)
	{
		if(string.IsNullOrEmpty(expectedSha256) || string.IsNullOrEmpty(actualSha256)) {
			return PackDepFileCheck.WrongFile;
		}
		return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase) ? PackDepFileCheck.Accepted : PackDepFileCheck.WrongFile;
	}

	public static PackDepPrimaryAction PrimaryAction(bool appliesInPlace)
	{
		return appliesInPlace ? PackDepPrimaryAction.Add : PackDepPrimaryAction.AddAndRestart;
	}

	//The file is copied into the pack's download folder under its own name; the
	//resolver finds it there by hash on the next resolve.
	public static string TargetPath(string dropFolder, string sourcePath)
	{
		return Path.Combine(dropFolder, Path.GetFileName(sourcePath));
	}

	//The drop folder is shared by every pack: a file already there under the
	//same name is kept when it is this one (nothing to copy) and never replaced
	//when it is another (it may be another pack's dependency).
	public static PackDepDropTarget CheckTarget(bool targetExists, string targetSha256, string expectedSha256)
	{
		if(!targetExists) {
			return PackDepDropTarget.Copy;
		}
		return Check(targetSha256, expectedSha256) == PackDepFileCheck.Accepted ? PackDepDropTarget.AlreadyThere : PackDepDropTarget.NameTaken;
	}

	//The install posts its pending files to the UI thread; by then the user
	//may have opened another game. The post applies only while no open has
	//started since the install began and the loaded ROM is still the one the
	//install was for.
	public static bool BelongsToCurrentLoad(int installOpenGeneration, int currentOpenGeneration, string installRomSha1, string currentRomSha1)
	{
		return installOpenGeneration == currentOpenGeneration
			&& !string.IsNullOrEmpty(installRomSha1)
			&& string.Equals(installRomSha1, currentRomSha1, StringComparison.OrdinalIgnoreCase);
	}
}

//When the sheet shows. The game is never interrupted: the install pill (or
//the OSD line until W-P9 lands) names the file and Esc; the next pause overlay
//opens the sheet on top of it, once per notice. Play Without It closes it back
//to the overlay and the status line keeps "waiting for one file".
public sealed class PackDepNoticeState
{
	private bool _shown;

	public bool HasPending { get; private set; }
	public string PackName { get; private set; } = "";
	public int FileCount { get; private set; }

	public void Pending(string packName, int fileCount)
	{
		PackName = packName ?? "";
		FileCount = fileCount;
		HasPending = fileCount > 0;
		_shown = false;
	}

	public bool ShouldOpenWithOverlay() => HasPending && !_shown;

	public void MarkShown() => _shown = true;

	//Another game loaded, or the file was added.
	public void Clear()
	{
		HasPending = false;
		PackName = "";
		FileCount = 0;
		_shown = false;
	}
}
