namespace Mesen.ViewModels
{
	//ADR-0255 slice 1 (PRD Part B §13.5.2 W-P17): the Controller sheet - what
	//Play › Settings › Controls › More in Options… opens over the paused game.
	//Built on first use, like the Cheats and Replays sheets, and hidden rather
	//than closed by the Esc router (#641: the router opens W-P4 itself, once).
	public partial class MainWindowViewModel
	{
		private ControllerSheetViewModel? _controllerSheet;

		public ControllerSheetViewModel ControllerSheet {
			get {
				_controllerSheet ??= new ControllerSheetViewModel();
				return _controllerSheet;
			}
		}

		//From the Settings sheet's Controls row; false when the sheet cannot be
		//operated, so the caller keeps the classic Input page as the landing.
		//
		//No game: the sheet reads a paused game, and both its Done and its Esc go
		//to W-P4 - there is nothing to return to without one.
		//
		//Not in the Play door (#816): the sheet's host lives inside PlayWorkspace,
		//and the Settings sheet it is reached from is a sibling, on screen from
		//every door. Two doors had to be refused, and they are not the same one:
		//
		//  - Remaster/Share: IsPlayWorkspace hides PlayWorkspace, so the sheet drew
		//    under a hidden ancestor and froze the session (Esc's ToggleOverlay there
		//    routes to Remaster.LeaveGameView, and CurrentPlaySheet kept answering
		//    Controller). Before the Controller sheet landed this path opened the
		//    classic Input page, which is visible from every door - so a task door
		//    keeps it.
		//  - Classic: IsPlayWorkspace is the *game screen* gate, not the Play door,
		//    and Classic shows the game screen too - so a guard on it admitted
		//    Classic, whose Esc is Advanced's (ShortcutHandler routes ToggleOverlay
		//    only in Player mode) and could not close the sheet. The door is the
		//    rule, and it is the same Shell.IsPlay CloseControllerSheetToOverlay
		//    already uses.
		public bool OpenControllerSheet()
		{
			if(!IsGameLoaded || !Shell.IsPlay) {
				return false;
			}
			//The Settings sheet it came from closes first: only one Play sheet is
			//current at a time (CurrentPlaySheet checks Settings before this one),
			//and two up at once would send Esc to the wrong one.
			ClosePlayerSettings();
			IsPlayerOverlayVisible = false;
			ControllerSheet.Open();
			return true;
		}

		//Done: back to W-P4, which is where this sheet came from. Same guard as
		//ClosePlayerSettingsToOverlay - a game can go away under it.
		public void CloseControllerSheetToOverlay()
		{
			CloseControllerSheet();
			if(IsGameLoaded && Shell.IsPlay) {
				OpenPauseOverlay();
			}
		}

		//Hides the sheet without re-showing anything: the Esc router and the
		//game going both use this (#642).
		public void CloseControllerSheet()
		{
			_controllerSheet?.Close();
		}
	}
}
