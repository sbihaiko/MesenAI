using Avalonia.Threading;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.Windows;
using System;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//G.6 (PRD Part B §13.5.5 W-X3): quitting or opening another game while
	//work runs asks once, inline (InterruptionBar above every profile). Only
	//lost work asks; switching profile never does (§13.6).
	public partial class MainWindowViewModel
	{
		public InterruptionViewModel Interruption { get; } = new();

		//ADR-0249 (W-X1): W-P4's Quit game asks on the overlay card itself (the
		//ConfirmExitResetPower preference), never in a message box.
		public InterruptionViewModel QuitGameConfirm { get; } = new();

		//W-P4's Quit game. True = power off now. False = the stop banner is up on
		//the overlay; quit runs after Quit Game.
		public bool ConfirmQuitGame(bool confirm, Action quit)
		{
			if(DeferWhileRecordingSettles(() => ConfirmQuitGame(confirm, quit), quit)) {
				return false;
			}
			InterruptionKind kind = Interruptions.ForQuitGame(confirm);
			if(kind == InterruptionKind.None) {
				return true;
			}
			QuitGameConfirm.Ask(kind, IsGameLoaded ? RomInfo.GetRomName() : "", 0, false, quit);
			return false;
		}

		//Closing the window in Player mode (MainWindow.ValidateExit), after
		//ConfirmQuit found no work to lose. True = quit now. False = the stop
		//banner is up above the profile; quit runs after Quit.
		public bool ConfirmQuitApp(bool confirm, Action quit)
		{
			InterruptionKind kind = Interruptions.ForQuitApp(confirm);
			if(kind == InterruptionKind.None) {
				return true;
			}
			Interruption.Ask(kind, IsGameLoaded ? RomInfo.GetRomName() : "", 0, false, quit);
			return false;
		}

		//The overlay's question goes with the overlay (Resume, Esc, a sheet).
		partial void OnIsPlayerOverlayVisibleChanged(bool value)
		{
			if(!value) {
				QuitGameConfirm.Keep();
			}
		}

		//MainWindow.OnClosing. True = nothing to lose, quit now. False = the
		//question is up; quit runs after Stop and Quit / Quit.
		public bool ConfirmQuit(Action quit)
		{
			if(DeferWhileRecordingSettles(() => ConfirmQuit(quit), quit)) {
				return false;
			}
			InterruptionKind kind = Interruptions.ForQuit(Remaster.IsRecording, Remaster.Job.IsRunning, Share.Job.IsRunning);
			if(kind == InterruptionKind.None) {
				return true;
			}
			Interruption.Ask(kind, "", Remaster.CurrentRecordingNumber(), Remaster.Job.Kind == RemasterJobKind.Build, async () => {
				//The recording is closed cleanly and kept (ADR-0243 Q1); a job's
				//partial output is discarded - it re-runs from the project. The
				//core closes it off the UI thread; quit waits for it.
				if(Remaster.IsRecording) {
					await Remaster.StopRecording(prepareFigures: false);
				}
				Remaster.StopJob();
				//#650: Share's mep_build.py pack would outlive the app.
				Share.StopBuild();
				quit();
			});
			return false;
		}

		//LoadRomHelper, before any game opens. True = open now. False = the
		//question is up; open runs after Stop and Open. A job is a separate
		//process and keeps running (W-X3).
		public bool ConfirmOpen(string romPath, Action open)
		{
			if(DeferWhileRecordingSettles(() => ConfirmOpen(romPath, open), open)) {
				return false;
			}
			HdPackBuilderViewModel? classic = ApplicationHelper.GetExistingWindow<HdPackBuilderWindow>()?.DataContext as HdPackBuilderViewModel;
			InterruptionKind kind = Interruptions.ForOpen(Remaster.IsRecording, classic?.IsRecording == true);
			if(kind == InterruptionKind.None) {
				return true;
			}
			Interruption.Ask(kind, Interruptions.GameName(romPath), Remaster.CurrentRecordingNumber(), false, async () => {
				if(Remaster.IsRecording) {
					//Kept, and its figures prepared, as after Stop; the new game
					//opens in Play (W-X3) once the core closed the recording.
					await Remaster.StopRecording();
					SelectWorkspace(Workspace.Play);
				}
				if(classic?.IsRecording == true) {
					classic.StopRecording();
				}
				open();
			});
			return false;
		}

		//#698: LoadRomHelper's reloads of the running game - Reload ROM, Power
		//Cycle, a pack switch or pick (ADR-0244's in-place reload included) -
		//before they run. The core ends a recording with the ROM it recorded,
		//so they ask like opening another game. True = reload now. False = the
		//question is up; reload runs after Stop and Reload, which keeps the
		//recording and prepares its figures, as Stop does.
		public bool ConfirmReload(Action reload)
		{
			if(DeferWhileRecordingSettles(() => ConfirmReload(reload), reload)) {
				return false;
			}
			InterruptionKind kind = Interruptions.ForReload(Remaster.IsRecording);
			if(kind == InterruptionKind.None) {
				return true;
			}
			Interruption.Ask(kind, RomInfo.GetRomName(), Remaster.CurrentRecordingNumber(), false, async () => {
				if(Remaster.IsRecording) {
					await Remaster.StopRecording();
				}
				reload();
			});
			return false;
		}

		//Remaster starts and stops a recording off the UI thread (every wait
		//moves); the core must not be stopped or reloaded under it. While one
		//is in flight the question is asked again once it settled - a started
		//recording then asks as any recording does. True = deferred.
		private bool DeferWhileRecordingSettles(Func<bool> confirm, Action go)
		{
			Task settled = Remaster.Settled;
			if(settled.IsCompleted) {
				return false;
			}
			settled.ContinueWith(_ => Dispatcher.UIThread.Post(() => {
				if(confirm()) {
					go();
				}
			}), TaskScheduler.Default);
			return true;
		}
	}
}
