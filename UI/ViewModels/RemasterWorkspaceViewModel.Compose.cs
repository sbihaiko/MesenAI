using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.ViewModels
{
	//G.7 (PRD Part B §13.5.3 W-R7, ADR-0165): project menu › Compose a Scene…
	//The composer is compose_editor.py in its own window, never embedded: this
	//sheet says whether the project has the layout data it needs and starts it
	//as a child process on the newest recording that holds textures.
	public partial class RemasterWorkspaceViewModel
	{
		private IJobProcess? _composer;
		private string _composerLastError = "";

		[ObservableProperty] public partial bool IsComposeSheetVisible { get; private set; }
		[ObservableProperty] public partial bool HasLayoutData { get; private set; }
		[ObservableProperty] public partial string ComposeHint { get; private set; } = "";
		[ObservableProperty] public partial RemasterControlViewModel OpenComposer { get; private set; } = RemasterControlViewModel.Hidden;

		public void ShowCompose()
		{
			IsComposeSheetVisible = true;
			RefreshCompose();
		}

		public void CancelCompose() => IsComposeSheetVisible = false;

		private void RefreshCompose()
		{
			if(!IsComposeSheetVisible) {
				return;
			}
			RemasterHandOffControl c = _composer != null ? RemasterHandOffControl.Off(RemasterHandOffReason.ComposerOpen)
				: RemasterHandOff.ComposeControl(_project, _feasibility ?? PendingFeasibility, File.Exists, IsRecording);
			RemasterRecording? rec = RemasterHandOff.ComposeRecording(_project);
			HasLayoutData = rec != null && File.Exists(RemasterHandOff.AdjacencyFile(rec));
			ComposeHint = ResourceHelper.GetMessage(HasLayoutData ? "RemasterComposeHasLayout" : "RemasterComposeNoLayout");
			OpenComposer = new RemasterControlViewModel(c.Enabled, c.Enabled || c.Reason == RemasterHandOffReason.NoLayoutData ? "" : HandOffReason(c.Reason));
		}

		//[ Open Composer ↗ ]
		public bool LaunchComposer()
		{
			RefreshCompose();
			RemasterRecording? rec = RemasterHandOff.ComposeRecording(_project);
			if(!OpenComposer.IsEnabled || rec == null || _feasibility == null) {
				return false;
			}
			IReadOnlyList<string> argv = RemasterHandOff.ComposeArgv(_feasibility, rec, ComposeRom());
			_composerLastError = "";
			try {
				_composer = _launcher.Start(argv, _feasibility.ToolsFolder, (line, isError) => {
					if(isError && line.Trim().Length > 0) {
						_composerLastError = line.Trim();
					}
				}, code => Dispatcher.UIThread.Post(() => OnComposerExited(code)));
			} catch(Exception ex) when(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is IOException) {
				NoticeText = ResourceHelper.GetMessage("RemasterComposerFailed", ex.Message);
				return false;
			}
			IsComposeSheetVisible = false;
			NoticeText = "";
			return true;
		}

		//--rom only for the project's own game (an overflow layer on a CHR ROM
		//game reads it); another running game is not passed.
		private string ComposeRom() => Inputs().ProjectIsGames ? _romPath : "";

		private void OnComposerExited(int code)
		{
			_composer = null;
			if(code != 0) {
				NoticeText = ResourceHelper.GetMessage("RemasterComposerFailed", _composerLastError.Length > 0 ? _composerLastError : code.ToString());
			}
			RefreshCompose();
		}
	}
}
