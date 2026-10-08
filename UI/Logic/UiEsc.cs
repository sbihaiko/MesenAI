namespace Mesen.Logic;

//What Esc means, read off the surfaces that are up - the decision the overlay
//shortcut's press resolves to (P.4, ADR-0249 rule 8: one meaning per context,
//and a sheet opened from W-P4 returns to W-P4).
//
//It lives here, host-free, because *two* callers need the same answer from
//different places, and the second one is #1080. The core's shortcut handler
//reaches it through EmulatorShortcut.ToggleOverlay: the emulator watches its own
//keys and notifies the UI, which is how this press has always arrived. The
//window now also answers Esc at its own keyboard, because that path is the one
//that did not (#1080: Esc in Player mode did nothing until the window was
//re-focused, which is not something a player can be asked to know). Sharing the
//decision is what keeps the two paths from disagreeing about what Esc means -
//and from both acting on one press.
public enum UiEscAction
{
	None,

	//Player mode's own: W-P4's router. It answers for the workspace, and for the
	//sheets that show in every workspace (the BIOS sheet, an archive's game list,
	//the tool sheet) - the same three the shortcut handler names.
	TogglePlayerOverlay,

	//ADR-0250: Settings… opened from Remaster's or Share's Tools ⋯ closes on Esc,
	//keeping what was changed.
	ClosePlayerSettings,

	//G.3/G.6: Remaster's recording view, or a build shown in the game.
	LeaveRemasterGameView,

	//G.8 (ShareEsc): stops a replay recording, or closes the topmost sheet.
	ShareEsc
}

public static class UiEsc
{
	public static UiEscAction For(
		bool isPlayerMode,
		bool isPlayWorkspace,
		bool biosSheetVisible,
		bool selectRomSheetVisible,
		bool toolSheetVisible,
		bool playerSettingsVisible,
		bool isRemasterGameView,
		bool isShareWorkspace)
	{
		//The Play arm is the overlay's, and it is asked first: the overlay is a
		//Play surface, so outside Play the press does nothing there (it must not
		//pause the game or open an overlay hidden behind Remaster/Share).
		if(isPlayerMode && (isPlayWorkspace || biosSheetVisible || selectRomSheetVisible || toolSheetVisible)) {
			return UiEscAction.TogglePlayerOverlay;
		}
		if(isPlayerMode && playerSettingsVisible) {
			return UiEscAction.ClosePlayerSettings;
		}
		if(isRemasterGameView) {
			return UiEscAction.LeaveRemasterGameView;
		}
		if(isShareWorkspace) {
			return UiEscAction.ShareEsc;
		}
		return UiEscAction.None;
	}

	//Which of those the window takes at the keyboard itself, before the key is
	//handed to the core at all (#1080). Only Player mode's two: Esc in Play is
	//the overlay's key by decision (UiModeShortcutPrecedence gives it that key in
	//Player mode and suppresses the Pause binding on it), and the player is told
	//so on screen ("Esc to resume"), so it cannot be left to a path that may not
	//deliver the press. The Remaster and Share arms stay the core's: they are the
	//classic GUI's own Esc meanings, they arrive on every platform today, and an
	//arm added for a bug is not a reason to move them.
	public static bool UiTakesThePress(UiEscAction action)
	{
		return action == UiEscAction.TogglePlayerOverlay || action == UiEscAction.ClosePlayerSettings;
	}
}
