using Mesen.Logic;
using Mesen.Utilities;
using Mesen.Windows;
using System;

namespace Mesen.ViewModels
{
	//G.6 (PRD Part B §13.5.5 W-X3): quitting or opening another game while
	//work runs asks once, inline (InterruptionBar above every profile). Only
	//lost work asks; switching profile never does (§13.6).
	public partial class MainWindowViewModel
	{
		public InterruptionViewModel Interruption { get; } = new();

		//MainWindow.OnClosing. True = nothing to lose, quit now. False = the
		//question is up; quit runs after Stop and Quit / Quit.
		public bool ConfirmQuit(Action quit)
		{
			InterruptionKind kind = Interruptions.ForQuit(Remaster.IsRecording, Remaster.Job.IsRunning);
			if(kind == InterruptionKind.None) {
				return true;
			}
			Interruption.Ask(kind, "", Remaster.CurrentRecordingNumber(), Remaster.Job.Kind == RemasterJobKind.Build, () => {
				//The recording is closed cleanly and kept (ADR-0243 Q1); a job's
				//partial output is discarded - it re-runs from the project.
				if(Remaster.IsRecording) {
					Remaster.StopRecording(prepareFigures: false);
				}
				Remaster.StopJob();
				quit();
			});
			return false;
		}

		//LoadRomHelper, before any game opens. True = open now. False = the
		//question is up; open runs after Stop and Open. A job is a separate
		//process and keeps running (W-X3).
		public bool ConfirmOpen(string romPath, Action open)
		{
			HdPackBuilderViewModel? classic = ApplicationHelper.GetExistingWindow<HdPackBuilderWindow>()?.DataContext as HdPackBuilderViewModel;
			InterruptionKind kind = Interruptions.ForOpen(Remaster.IsRecording, classic?.IsRecording == true);
			if(kind == InterruptionKind.None) {
				return true;
			}
			Interruption.Ask(kind, Interruptions.GameName(romPath), Remaster.CurrentRecordingNumber(), false, () => {
				if(Remaster.IsRecording) {
					//Kept, and its figures prepared, as after Stop; the new game
					//opens in Play (W-X3).
					Remaster.StopRecording();
					SelectWorkspace(Workspace.Play);
				}
				if(classic?.IsRecording == true) {
					classic.StopRecording();
				}
				open();
			});
			return false;
		}
	}
}
