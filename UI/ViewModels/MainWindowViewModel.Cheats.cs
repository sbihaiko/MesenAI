using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//P.10 (ADR-0245 §1, PRD Part B §13 W-P4/W-P11): the pause overlay's Cheats
	//row and the sheet it opens. Kept in its own partial file so the overlay
	//glue stays small in MainWindowViewModel.cs. The sheet replaces the overlay
	//(same replace-not-stack shape as the Enhancements panel), and closing it -
	//Done or Esc - brings the overlay back (rule 8).
	public partial class MainWindowViewModel
	{
		private PlayerCheatsSheetViewModel? _cheatsSheet;

		public PlayerCheatsSheetViewModel CheatsSheet
		{
			get {
				if(_cheatsSheet == null) {
					_cheatsSheet = new PlayerCheatsSheetViewModel();
					//G.2: back to W-P4 with its row values refreshed.
					_cheatsSheet.Closed += OpenPauseOverlay;
				}
				return _cheatsSheet;
			}
		}

		//The row's value: "none", "N on", or "off" (CheatSheet.OverlaySummary).
		[ObservableProperty] public partial string CheatsSummary { get; private set; } = "none";

		public void RefreshCheatsSummary()
		{
			CheatsSummary = CheatSheet.OverlaySummary(CheatSheet.CountOn(PlayerCheatsStore.LoadStored()), Config.Cheats.DisableAllCheats);
		}

		//Play is unrestricted (ADR-0245 §3), so recordingArt is false here; the
		//Remaster game view is the context that will pass true.
		public void OpenCheatsSheet()
		{
			IsPlayerOverlayVisible = false;
			ConsoleType console = RomInfo.ConsoleType;
			//The core dereferences the running console for the hash, so with no
			//game loaded there is no copy to match (the sheet says "not in the list").
			string cheatSha1 = EmuApi.IsRunning() ? EmuApi.GetRomHash(HashType.Sha1Cheat) : "";
			CheatsSheet.Open(
				console,
				cheatSha1,
				PlayerCheatsStore.LoadDatabase(console),
				PlayerCheatsStore.LoadStored(),
				recordingArt: false,
				Config.Cheats.DisableAllCheats,
				PlayerCheatsStore.SaveAndApply
			);
		}

		//Esc while the sheet is up closes it back to the overlay. Returns true
		//when it handled the press.
		private bool CloseCheatsSheetOnEsc()
		{
			if(_cheatsSheet?.IsVisible != true) {
				return false;
			}
			_cheatsSheet.Close();
			return true;
		}
	}
}
