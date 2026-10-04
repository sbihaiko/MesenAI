using Avalonia.Controls;
using Avalonia.Threading;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.Windows
{
	//#734: the Play load card's Core side (rule in Logic/PlayLoadWait). The
	//notifications arrive on the Core's threads; every UI change is posted.
	public partial class MainWindow
	{
		private System.IDisposable? _pictureTimeout;

		//GameLoaded, on the load thread. True when the home (and with it the
		//card, above a hidden native picture) stays until the first picture.
		private bool HoldsHomeForPicture(bool loadedPaused)
		{
			//A reload keeps the Core's pause: a paused game draws nothing after it.
			bool holds = _model.LoadWait.OnGameLoaded(loadedPaused, EmuApi.IsPaused());
			int openGeneration = _model.LoadWait.OpenGeneration;
			Dispatcher.UIThread.Post(() => {
				_model.RefreshLoadWait();
				if(holds) {
					//A game that never draws still gets its picture back.
					_pictureTimeout?.Dispose();
					_pictureTimeout = DispatcherTimer.RunOnce(() => {
						if(_model.LoadWait.EndPictureWait(openGeneration)) {
							ShowGamePicture();
						}
					}, PlayLoadWait.PictureTimeout);
				}
			});
			return holds;
		}

		//What GameLoaded did at once before #734: the home goes, the picture
		//takes the keyboard.
		private void ShowGamePicture()
		{
			EndLoadWaitOnScreen();
			_model.RecentGames.Visible = false;
			if(IsKeyboardFocusWithin || IsActive || ApplicationHelper.GetActiveOrMainWindow() == this) {
				this.GetControl<Panel>("RendererPanel").Focus();
			}
		}

		//UI thread: the card follows the wait, and an ended wait leaves no timer.
		private void EndLoadWaitOnScreen()
		{
			_model.RefreshLoadWait();
			if(!_model.LoadWait.IsActive) {
				_pictureTimeout?.Dispose();
				_pictureTimeout = null;
			}
		}

		//PpuFrameDone (every frame, emulation thread), GamePaused/CodeBreak and
		//EmulationStopped. A pause shows the game under its overlay; a stop
		//leaves the home, which EmulationStopped puts back anyway.
		private void OnLoadWaitNotification(ConsoleNotificationType type)
		{
			switch(type) {
				case ConsoleNotificationType.PpuFrameDone:
					if(_model.LoadWait.OnFrameDone()) {
						Dispatcher.UIThread.Post(ShowGamePicture);
					}
					break;

				case ConsoleNotificationType.GamePaused:
				case ConsoleNotificationType.CodeBreak:
					if(_model.LoadWait.EndPictureWait()) {
						Dispatcher.UIThread.Post(ShowGamePicture);
					}
					break;

				case ConsoleNotificationType.GameLoadFailed:
					//A reload that failed (a power cycle of a file that is gone).
					if(_model.LoadWait.OnLoadFailed()) {
						Dispatcher.UIThread.Post(EndLoadWaitOnScreen);
					}
					break;

				case ConsoleNotificationType.EmulationStopped:
					if(_model.LoadWait.EndPictureWait()) {
						Dispatcher.UIThread.Post(EndLoadWaitOnScreen);
					}
					break;
			}
		}
	}
}
