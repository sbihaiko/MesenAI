namespace Mesen.Logic;

//#657: the load a community-pack install or Restore was started for, captured
//before the artifact download (up to 300 MB) begins. The install writes into
//SiblingFolder's mep/ (ADR-0147), extracts under RomName and records the
//registry under RomSha1 - all from this capture, never from whatever game is
//loaded once the download returns. RomFileSha1 is the whole-file SHA-1 the
//ADR-0211 supportedRom check compares against.
public sealed record CommunityPackLoadTarget(string RomSha1, string RomFileSha1, string SiblingFolder, string RomName, int OpenGeneration)
{
	public bool HasRom => !string.IsNullOrWhiteSpace(RomSha1);

	//The W-P16 rule (PlayPackDepPrompt.BelongsToCurrentLoad): no open has
	//started since the capture and the loaded ROM is still the captured one.
	//False means the player opened another game (or reopened or quit this one)
	//meanwhile - the install drops before its first destructive step.
	public bool IsStillLoaded(CommunityPackLoadTarget current)
	{
		return PlayPackDepPrompt.BelongsToCurrentLoad(OpenGeneration, current.OpenGeneration, RomSha1, current.RomSha1);
	}
}
