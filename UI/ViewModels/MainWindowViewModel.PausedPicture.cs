using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//User's decisions, 2026-10-03 ("Sim, quadro congelado", "Seguir o render"):
	//what W-P4 and its sheets show behind them. The native picture steps aside
	//for them (PlayGameLayer), so a frozen copy of the last frame takes its
	//place under the scrim; and a sheet opened from W-P4 keeps the card on
	//screen, dimmed. The rules are host-free in UI/Logic/PlayPausedPicture.
	public partial class MainWindowViewModel
	{
		//The frame the game stopped on, drawn by MainWindow's PausedGameFrame
		//image in the live picture's box (SoftwareRenderer.Width/Height).
		[ObservableProperty] public partial WriteableBitmap? PausedGameFrame { get; private set; }
		[ObservableProperty] public partial bool IsPausedGameFrameVisible { get; private set; }

		//W-P4's card: on top (Active), or behind a sheet opened from it (Dimmed).
		[ObservableProperty] public partial bool IsPauseCardVisible { get; private set; }
		[ObservableProperty] public partial bool IsPauseCardDimmed { get; private set; }

		private bool _pausedFramePending;

		//Called with UpdateRendererVisibility, i.e. whenever a Play surface or
		//the game view changes.
		private void UpdatePausedPicture(bool surfaceOverGame)
		{
			PauseCardLayer card = PauseCard.Layer(IsGameLoaded, IsPlayerOverlayVisible, CurrentPlaySheet());
			IsPauseCardVisible = card != PauseCardLayer.Hidden;
			IsPauseCardDimmed = card == PauseCardLayer.Dimmed;
			IsPausedGameFrameVisible = PlayFrozenFrame.Shows(IsGameViewVisible, RecentGames.Visible, SoftwareRenderer.FrameSurface != null, surfaceOverGame, !LoadWait.PictureCutShort);

			//Moving between W-P4 and a sheet hides one before showing the other,
			//so for an instant nothing covers the game; the capture/drop decision
			//waits for the posted pass, which sees the settled state and keeps
			//the same frame across the move.
			if(!_pausedFramePending) {
				_pausedFramePending = true;
				Dispatcher.UIThread.Post(UpdatePausedFrame);
			}
		}

		private void UpdatePausedFrame()
		{
			_pausedFramePending = false;
			switch(PlayFrozenFrame.Next(PausedGameFrame != null, IsPlaySurfaceOverGame, IsGameLoaded, !LoadWait.PictureCutShort)) {
				case FrozenFrameStep.Capture:
					PausedGameFrame = FrameCaptureApi.CaptureFrame();
					break;
				case FrozenFrameStep.Drop:
					WriteableBitmap? frame = PausedGameFrame;
					PausedGameFrame = null;
					frame?.Dispose();
					break;
			}
		}
	}
}
