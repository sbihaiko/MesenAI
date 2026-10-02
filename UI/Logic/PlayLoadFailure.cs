namespace Mesen.Logic;

//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P14): a file that does not open.
//Today the Core shows CouldNotLoadFile on the last frame and the UI stays
//where it was; in Play the home stays (rule 5) and carries one inline alert
//(W-X2 shape) that stays until the next open or a click on its ✕.
public enum LoadFailureCause
{
	//Not a game file (the Core's LoadRomResult::UnknownType).
	NotAGame,
	//A zip/7z with no game in it.
	ZipWithoutGame,
	//A known console, but the loader failed: damaged or cut short.
	Damaged
}

public static class PlayLoadFailure
{
	//knownGameExtension: the file's extension is one MesenAI opens.
	//isArchive/archiveHasGame: a zip/7z, and whether it lists a game file (a
	//several-game zip keeps today's chooser, so a failure after it is a
	//damaged game).
	public static LoadFailureCause Classify(string fileName, bool knownGameExtension, bool isArchive, bool archiveHasGame)
	{
		if(isArchive) {
			return archiveHasGame ? LoadFailureCause.Damaged : LoadFailureCause.ZipWithoutGame;
		}
		return knownGameExtension ? LoadFailureCause.Damaged : LoadFailureCause.NotAGame;
	}

	//A load that stopped because the user cancelled W-P13 is not a broken file:
	//the status line already says which BIOS the game needs.
	public static bool ShowsAlert(bool biosPromptCancelled) => !biosPromptCancelled;

	//The home stays through a load (and carries the alert) only where it is on
	//screen: Player mode's Play workspace. Under Remaster or Share, or in
	//Advanced, a failure keeps today's on-screen message and selection screen.
	public static bool KeepsHomeDuringLoad(bool playerMode, bool playWorkspace) => playerMode && playWorkspace;
}

//The W-P14 alert's lifetime on the Play home.
public sealed class PlayHomeAlert
{
	public bool IsVisible { get; private set; }
	public LoadFailureCause Cause { get; private set; }
	public string FileName { get; private set; } = "";

	public void Show(LoadFailureCause cause, string fileName)
	{
		Cause = cause;
		FileName = fileName ?? "";
		IsVisible = true;
	}

	//The next open (any path: Open Another…, a drop, a recent game) clears it.
	public void OnOpenStarted() => IsVisible = false;

	public void Dismiss() => IsVisible = false;
}
