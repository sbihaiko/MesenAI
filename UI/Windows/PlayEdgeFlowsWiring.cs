using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Interop;
using Mesen.Utilities;
using Mesen.ViewModels;
using System;
using System.Linq;

namespace Mesen.Windows
{
	//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P13, W-P15, W-P16): the window-side
	//glue of the Play edge flows - focus on open (rule 9: the pad reaches the
	//sheet's first button), the pack-file sheet's reload and close, and the
	//controller poll. Kept out of MainWindow.axaml.cs, which only calls Attach.
	internal static class PlayEdgeFlowsWiring
	{
		//The same 50 ms cadence the Advanced key-binding grid polls at.
		private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

		public static DispatcherTimer Attach(Window window, MainWindowViewModel model)
		{
			FocusOnOpen(window, model.BiosSheet, nameof(PlayBiosSheetViewModel.IsVisible), () => model.BiosSheet.IsVisible, "BiosSheetChooseFile");
			FocusOnOpen(window, model.PackDepSheet, nameof(PlayPackDepSheetViewModel.IsVisible), () => model.PackDepSheet.IsVisible, "PackDepSheetChooseFile");
			FocusOnOpen(window, model.ControllerSetup, nameof(PlayControllerSetupViewModel.IsVisible), () => model.ControllerSetup.IsVisible, "ControllerSetupSkip");

			//Play Without It: back to the pause overlay (rule 8).
			model.PackDepSheet.Closed += model.OpenPauseOverlay;
			//The file is in the drop folder: a ROM reload (not a power cycle,
			//#156) re-resolves the pack's dependencies.
			model.PackDepSheet.FileAdded += () => {
				model.OnPackDepFileAdded();
				model.IsPlayerOverlayVisible = false;
				EmuApi.Resume();
				LoadRomHelper.ReloadRom();
			};
			model.ControllerSetup.Finished += result => {
				if(!string.IsNullOrEmpty(result)) {
					DisplayMessageHelper.DisplayMessage("Input", result);
				}
			};

			DispatcherTimer timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (s, e) => Poll(model));
			timer.Start();
			return timer;
		}

		//W-P15 listens only while a game runs in Player mode's Play workspace,
		//and keeps listening while its own sheet is open (the game is paused).
		private static void Poll(MainWindowViewModel model)
		{
			bool listening = model.ControllerSetup.IsVisible
				|| (model.IsPlayerMode && model.IsPlayWorkspace && EmuApi.IsRunning() && !EmuApi.IsPaused());
			if(listening) {
				model.ControllerSetup.Tick(InputApi.GetPressedKeys());
			}
		}

		private static void FocusOnOpen(Window window, System.ComponentModel.INotifyPropertyChanged source, string property, Func<bool> isOpen, string controlName)
		{
			source.PropertyChanged += (s, e) => {
				if(e.PropertyName == property && isOpen()) {
					Dispatcher.UIThread.Post(() => window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == controlName)?.Focus());
				}
			};
		}
	}
}
