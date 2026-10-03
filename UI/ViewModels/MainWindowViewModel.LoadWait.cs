using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//#734: the Play load card - from an open to the game's first picture (rule
	//in Logic/PlayLoadWait). MainWindow.LoadWait feeds it the Core's
	//notifications; this partial holds what the card binds to.
	public partial class MainWindowViewModel
	{
		public PlayLoadWait LoadWait { get; } = new();

		[ObservableProperty] public partial bool IsLoadWaitActive { get; private set; }
		[ObservableProperty] public partial string LoadWaitText { get; private set; } = "";

		//LoadRomHelper.BeginLoad, on the UI thread, after OnOpenStarted. The card
		//shows only where the home stays through the open (Player mode's Play).
		public void BeginLoadWait(string gameName, bool keepsHome)
		{
			if(!keepsHome) {
				return;
			}
			LoadWait.Begin(gameName, OpenGeneration);
			RefreshLoadWait();
		}

		//UI thread: mirror the wait onto the card.
		public void RefreshLoadWait()
		{
			IsLoadWaitActive = LoadWait.IsActive;
			if(IsLoadWaitActive) {
				(string id, string? arg) = PlayLoadWait.Text(LoadWait.GameName);
				LoadWaitText = arg == null ? ResourceHelper.GetMessage(id) : ResourceHelper.GetMessage(id, arg);
			}
		}
	}
}
