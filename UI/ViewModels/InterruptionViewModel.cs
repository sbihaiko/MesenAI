using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;
using System;

namespace Mesen.ViewModels
{
	//G.6 (PRD Part B §13.5.5 W-X3 in the W-X1 shape): the one inline question
	//asked before work is lost - a sentence and two buttons, the safe one
	//first. Which question, and when, is UI/Logic/RemasterInterruptions.cs;
	//MainWindow asks it and runs the continuation on the second button.
	public partial class InterruptionViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool IsVisible { get; private set; }
		[ObservableProperty] public partial InterruptionKind Kind { get; private set; }
		[ObservableProperty] public partial string Text { get; private set; } = "";
		[ObservableProperty] public partial string KeepText { get; private set; } = "";
		[ObservableProperty] public partial string GoText { get; private set; } = "";

		private Action? _go;

		//game: the game being opened; recording: the recording number kept;
		//buildJob: the running job is a build (else the kit).
		public void Ask(InterruptionKind kind, string game, int recording, bool buildJob, Action go)
		{
			if(kind == InterruptionKind.None) {
				go();
				return;
			}
			Kind = kind;
			_go = go;
			(string text, string keep, string goText) = kind switch {
				InterruptionKind.QuitWhileRecording => (ResourceHelper.GetMessage("InterruptQuitWhileRecording", recording), "InterruptKeepRecording", "InterruptStopAndQuit"),
				InterruptionKind.QuitWhileJob => (ResourceHelper.GetMessage(buildJob ? "InterruptQuitWhileBuild" : "InterruptQuitWhileKit"), "InterruptKeepRunning", "InterruptQuit"),
				InterruptionKind.OpenWhileRecording => (ResourceHelper.GetMessage("InterruptOpenWhileRecording", game, recording), "InterruptCancel", "InterruptStopAndOpen"),
				_ => (ResourceHelper.GetMessage("InterruptOpenWhileClassicBuilder", game), "InterruptCancel", "InterruptStopAndOpen"),
			};
			Text = text;
			KeepText = ResourceHelper.GetMessage(keep);
			GoText = ResourceHelper.GetMessage(goText);
			IsVisible = true;
		}

		public void Keep()
		{
			_go = null;
			IsVisible = false;
			Kind = InterruptionKind.None;
		}

		public void Go()
		{
			Action? go = _go;
			Keep();
			go?.Invoke();
		}
	}
}
