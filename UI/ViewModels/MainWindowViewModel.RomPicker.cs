using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//#845 (ADR-0256 Decision 9): the in-app ROM picker the Play home opens.
	//Built on first use, like the Cheats, Replays and Controller sheets, and
	//hidden rather than closed by the Esc router (#641: the router opens the
	//overlay itself, once - and here there is no overlay to open).
	//
	//Only the Play home reaches it, and only with no game loaded: W-P1 and W-P2
	//are what a cabinet with a pad and nothing else boots onto, which is the
	//machine this exists for. Advanced's Open and every other file choice in the
	//app stay on the native dialog (the same ADR's non-goals).
	public partial class MainWindowViewModel
	{
		private PlayerRomPickerViewModel? _romPicker;

		public PlayerRomPickerViewModel RomPicker {
			get {
				if(_romPicker is null) {
					_romPicker = new PlayerRomPickerViewModel();
					//The chosen path goes to the call the native dialog's own
					//result took, so nothing downstream knows the difference:
					//an archive still asks which game it holds, a pack still
					//resolves, and the load card still shows.
					_romPicker.RomChosen += path => LoadRomHelper.LoadFile(path);
				}
				return _romPicker;
			}
		}

		//W-P1/W-P2's *Open a ROM…*, and the load alert's *Open Another…*: they
		//share it, as they shared the dialog.
		public void OpenRomPicker()
		{
			if(!IsPlayWorkspace) {
				return;
			}
			RomPicker.Open();
		}
	}
}
