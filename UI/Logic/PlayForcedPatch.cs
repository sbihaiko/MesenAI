namespace Mesen.Logic;

//#732 (ADR-0044's ApplyPatchOnHashMismatch override): a pack's ROM patch
//forced onto a revision it was not made for can freeze the game, and the core's
//OSD line about it is not something a Player-mode user reads. After every load
//the UI asks the core which patch it forced (EmuApi.GetForcedPackPatch) and
//this decides what the in-place banner (W-X2 shape, InterruptionBar) does.
public enum ForcedPatchBanner
{
	//Nothing changes.
	Leave,
	//Ask: "Keep Playing" / "Reload Without Patch".
	Show,
	//The banner belongs to a load that is gone.
	Withdraw
}

public static class PlayForcedPatch
{
	//mode: the UI mode; forcedPatch: the core's answer for the load that just
	//finished (empty when nothing was forced); showing: what the interruption
	//banner shows now.
	public static ForcedPatchBanner AfterLoad(UiMode mode, string forcedPatch, InterruptionKind showing)
	{
		if(string.IsNullOrEmpty(forcedPatch)) {
			return showing == InterruptionKind.ForcedPatch ? ForcedPatchBanner.Withdraw : ForcedPatchBanner.Leave;
		}
		if(mode != UiMode.Player) {
			//Advanced keeps the core's OSD line; a Player banner left up from
			//before a mode switch goes.
			return showing == InterruptionKind.ForcedPatch ? ForcedPatchBanner.Withdraw : ForcedPatchBanner.Leave;
		}
		//A question about lost work (quit, open, reload while recording) is never
		//replaced by a warning; the core's log keeps what happened.
		return showing == InterruptionKind.None || showing == InterruptionKind.ForcedPatch ? ForcedPatchBanner.Show : ForcedPatchBanner.Leave;
	}

	//The patch's file name, as the banner names it.
	public static string PatchName(string forcedPatch)
	{
		if(string.IsNullOrEmpty(forcedPatch)) {
			return "";
		}
		//The core hands a path in the host's own separators, or a pack-relative
		//name; either way the file name is the last segment.
		return forcedPatch.Replace('\\', '/').Split('/')[^1];
	}

	//The game is gone (power off, Quit game): so is its banner.
	public static bool WithdrawsWithoutGame(InterruptionKind showing)
	{
		return showing == InterruptionKind.ForcedPatch;
	}
}
