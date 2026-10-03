using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//#734: the Play load card - from an open, or a reload of the game on
	//screen, to the game's first picture (rule in Logic/PlayLoadWait).
	//MainWindow.LoadWait feeds it the Core's notifications; this partial holds
	//what the card binds to. The card is a surface over the game
	//(PlayGameLayer), so the native picture never covers it.
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

		//LoadRomHelper's reloads (power cycle, Reload, the in-place pack change),
		//on the UI thread, before the Core is asked. Returns the wait's ticket,
		//0 when no card shows (Advanced mode, or no game on screen).
		public int BeginReloadWait()
		{
			bool gameOnScreen = IsGameViewVisible && !RecentGames.Visible && RomInfo.Format != RomFormat.Unknown && EmuApi.IsRunning();
			if(!PlayLoadWait.ShowsReloadFor(Config.Preferences.UiMode == UiMode.Player, gameOnScreen)) {
				return 0;
			}
			int ticket = LoadWait.BeginReload(RomInfo.GetRomName(), OpenGeneration);
			RefreshLoadWait();
			//The safety net: a reload that never draws still gives the picture back.
			DispatcherTimer.RunOnce(() => {
				if(LoadWait.Expire(ticket)) {
					RefreshLoadWait();
				}
			}, PlayLoadWait.PictureTimeout);
			return ticket;
		}

		//Any thread: a blocking reload call returned (refused, or the game is
		//paused and draws nothing) - see PlayLoadWait.OnReloadReturned.
		public void OnReloadReturned(int ticket)
		{
			if(ticket != 0 && LoadWait.OnReloadReturned(ticket, EmuApi.IsPaused())) {
				Dispatcher.UIThread.Post(RefreshLoadWait);
			}
		}

		//UI thread: mirror the wait onto the card.
		public void RefreshLoadWait()
		{
			IsLoadWaitActive = LoadWait.IsActive;
			if(IsLoadWaitActive) {
				(string id, string? arg) = PlayLoadWait.Text(LoadWait.GameName, LoadWait.Kind);
				LoadWaitText = arg == null ? ResourceHelper.GetMessage(id) : ResourceHelper.GetMessage(id, arg);
			}
		}
	}
}
