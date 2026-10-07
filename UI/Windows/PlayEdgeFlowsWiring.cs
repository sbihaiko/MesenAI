using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Interop;
using Mesen.Services;
using Mesen.Utilities;
using Mesen.ViewModels;
using System;
using System.Linq;

namespace Mesen.Windows
{
	//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P13, W-P15, W-P16): the window-side
	//glue of the Play edge flows - the pack-file sheet's reload and close, and
	//the controller poll. Kept out of MainWindow.axaml.cs, which only calls
	//Attach. The sheets' focus-on-open (rule 9: the pad reaches the sheet's first
	//button) is PlayPadNavigationWiring's now, one path for every Play surface.
	internal static class PlayEdgeFlowsWiring
	{
		//The same 50 ms cadence the Advanced key-binding grid polls at.
		private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

		//ADR-0249 (W-S1)/ADR-0255: the port lamps are a cabinet indicator, not a
		//frame-rate read - a pad appears or goes in human time - so they get their
		//own 1 s cadence rather than the flows' 50 ms.
		private static readonly TimeSpan PadPortLampInterval = TimeSpan.FromSeconds(1);

		public static DispatcherTimer Attach(Window window, MainWindowViewModel model)
		{
			//The six sheets' focus-on-open used to be registered here, each one
			//posting its own Focus(). ADR-0256 Decision 3 moved them to
			//PlayPadNavigationWiring, where every Play surface's claim is
			//arbitrated in one place; this file keeps the flows and the poll.

			//Play Without It: back to the pause overlay (rule 8).
			model.PackDepSheet.Closed += model.OpenPauseOverlay;
			//The file is in the drop folder. #938 (ADR-0244): where the pack
			//change policy allows it the install completes the pack and applies
			//it in place; otherwise a ROM reload (not a power cycle, #156)
			//re-resolves the pack's dependencies and the game restarts.
			model.PackDepSheet.FileAdded += () => {
				bool inPlace = model.PackDepAppliesInPlace;
				model.OnPackDepFileAdded();
				model.IsPlayerOverlayVisible = false;
				EmuApi.Resume();
				if(inPlace) {
					CommunityPackInstallService.InstallWithAddedFile();
				} else {
					LoadRomHelper.ReloadRom();
				}
			};
			model.ControllerSetup.Finished += result => {
				if(!string.IsNullOrEmpty(result)) {
					DisplayMessageHelper.DisplayMessage("Input", result);
				}
			};

			bool listening = false;
			DispatcherTimer timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (s, e) => listening = Poll(model, listening));
			timer.Start();

			//The port lamps: four LEDs for the connected pads. Their own cadence and
			//their own timer, stopped with the window - the 50 ms bridge's own reason
			//(#840: a tick that outlived its window would run against a closed one).
			//The first read is immediate so a cabinet with pads shows them lit from
			//the instant the window opens, not a second later.
			DispatcherTimer lamps = new DispatcherTimer(PadPortLampInterval, DispatcherPriority.Background, (s, e) => model.RefreshPadLamps());
			window.Closed += (_, _) => lamps.Stop();
			model.RefreshPadLamps();
			lamps.Start();
			return timer;
		}

		//W-P15 listens only while a game runs in Player mode's Play workspace,
		//and keeps listening while its own sheet is open (the game is paused).
		//#660: when listening stops, the pill goes with it - its 8 s only
		//advance on these ticks. A pill raised between two ticks (no tick saw
		//the game running) goes too. Returns whether it is listening now.
		private static bool Poll(MainWindowViewModel model, bool wasListening)
		{
			//ADR-0255 slice 5: before the setup detector decides a pad is unknown,
			//give a pad that reconnected at another device index the chance to keep
			//its bindings. Gated on the whole Play door (IsPlayWorkspace), not just
			//the game screen: the keys must be right by the time a game reads them,
			//so the home screen - where the player is about to launch one - is the
			//better moment, not a later one. The cost is one pad enumeration per
			//50 ms while the door is active, and on the backends that report a
			//VID:PID it can actually move something. On macOS and Windows XInput
			//every pad is unidentified, so the repair cannot fire there at all.
			if(model.IsPlayerMode && model.IsPlayWorkspace) {
				model.ControllerReconnect.Check();
			}

			bool listening = model.ControllerSetup.IsVisible
				|| (model.IsPlayerMode && model.IsPlayWorkspace && EmuApi.IsRunning() && !EmuApi.IsPaused());
			if(listening) {
				model.ControllerSetup.Tick(InputApi.GetPressedKeys());
				//ADR-0253 §4 (W.5): the per-game widescreen measurement closes
				//after the first gameplay seconds; the record is written as it
				//closes, whether or not the player ever opens Enhancements. It
				//needs frames, so the tick only runs while the game runs.
				model.TickWidescreenSupport();
			} else if(wasListening || model.ControllerSetup.IsPillVisible) {
				model.ControllerSetup.StopListening();
			}
			return listening;
		}
	}
}
