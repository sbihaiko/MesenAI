using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P14): a file that does not open.
	//One inline alert (W-X2 shape) on the Play home, which is not replaced
	//(rule 5); it stays until the next open or a click on its ✕. Player mode
	//only - Advanced keeps today's on-screen message.
	public partial class RecentGamesViewModel
	{
		private readonly PlayHomeAlert _loadAlert = new();

		[ObservableProperty] public partial bool IsLoadAlertVisible { get; private set; }
		[ObservableProperty] public partial string LoadAlertTitle { get; private set; } = "";
		[ObservableProperty] public partial string LoadAlertBody { get; private set; } = "";

		public LoadFailureCause LoadAlertCause => _loadAlert.Cause;

		public void ShowLoadFailure(LoadFailureCause cause, string fileName)
		{
			_loadAlert.Show(cause, fileName);
			LoadAlertTitle = ResourceHelper.GetMessage("LoadFailedTitle" + cause, fileName);
			LoadAlertBody = ResourceHelper.GetMessage("LoadFailedBody" + cause);
			IsLoadAlertVisible = true;
		}

		//Any open (Open Another…, a drop, a recent game) clears the alert.
		public void OnOpenStarted()
		{
			_loadAlert.OnOpenStarted();
			IsLoadAlertVisible = false;
		}
	}
}
