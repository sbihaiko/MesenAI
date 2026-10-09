namespace Mesen.Logic;

//Why W-P4 is up when the player did not ask for it, beyond the plain pause.
public enum PadPauseReason
{
	None,
	ControllerDisconnected,
	ControllerReconnected
}

//#1109 (spec #1102), ADR-0254's second voice: a pad that disappears while a game
//runs unpaused in Play pauses into W-P4 with the reason written on it, as a lost
//window focus does. Reconnecting rewrites the line and never resumes - ADR-0254's
//answer for focus regain, stay paused. Host-free (ADR-0123;
//UI.Tests/Play/PadLossPauseTests); MainWindowViewModel.TickPadLoss is the only
//caller and owns the poll and the pause call.
public static class PadLossPause
{
	//Any pad counts: the trigger is the connected count going down, not which
	//pad it was. A game already paused is left as it is (the player has their own
	//surface), Classic has no W-P4, and with no game there is nothing to pause.
	public static bool ShouldPause(uint previousCount, uint currentCount, bool isPlayDoor, bool gameLoaded, bool paused)
		=> currentCount < previousCount && isPlayDoor && gameLoaded && !paused;

	//A pad coming back turns "disconnected" into "reconnected"; anything else
	//keeps the line it has. Nothing here ever resumes the game.
	public static PadPauseReason AfterCountChange(PadPauseReason reason, uint previousCount, uint currentCount)
		=> reason == PadPauseReason.ControllerDisconnected && currentCount > previousCount
			? PadPauseReason.ControllerReconnected
			: reason;
}
