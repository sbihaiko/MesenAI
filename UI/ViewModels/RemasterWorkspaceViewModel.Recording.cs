using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//W-R0 › Start Recording / W-R1 › Record While I Play and W-R2 › Stop. The
	//core's builder blocks while it starts (it exports every ROM tile: 365-978 ms
	//measured) and while it stops (243-529 ms), so both calls run off the UI
	//thread behind a moving wait (the user's rule, 2026-10-03: every wait
	//moves). A click or Esc during either does nothing (RecordingTransitions).
	public partial class RemasterWorkspaceViewModel
	{
		private Task<bool>? _starting;
		private Task? _stopping;

		[ObservableProperty] public partial RecordingTransition Transition { get; private set; }
		//W-R0/W-R1: "Starting the recording…" with an indeterminate bar.
		[ObservableProperty] public partial bool IsRecordWaitVisible { get; private set; }
		[ObservableProperty] public partial string RecordWaitText { get; private set; } = "";
		//W-R2's strip: "Stopping the recording…" in place of Stop.
		[ObservableProperty] public partial bool IsStopWaitVisible { get; private set; }
		[ObservableProperty] public partial string StopWaitText { get; private set; } = "";

		//The start or stop in flight, or a completed task. The core must not be
		//stopped or reloaded under a start or stop: the shell's interruptions
		//wait for this before they ask (MainWindowViewModel.Interruptions).
		public Task Settled => Transition == RecordingTransition.Starting && _starting != null ? _starting
			: Transition == RecordingTransition.Stopping && _stopping != null ? _stopping
			: Task.CompletedTask;

		//The Python/tools probe is in flight and has not answered yet.
		private bool IsFeasibilityPending => _feasibility == null && _measuring != null;

		//The bootstrap recorder into the next auto/rec-NNN/ (ADR-0243 Decision 2,
		//source play). Called on the UI thread; completes there once the core answered.
		public Task<bool> StartRecording()
		{
			if(!RecordingTransitions.AcceptsClick(Transition)) {
				return _starting ?? Task.FromResult(false);
			}
			RemasterScreenState state = Evaluate();
			if(!state.Record.Enabled) {
				return Task.FromResult(false);
			}
			//ADR-0184 §1: a cheat that is not a RAM code refuses the run, named.
			string refusal = CheatRefusal();
			if(refusal.Length > 0) {
				NoticeText = refusal;
				Refresh();
				return Task.FromResult(false);
			}
			//Held back from here on, so nothing turned on later reaches the core.
			BeginRecordingArtCheats();
			NoticeText = "";
			SetTransition(RecordingTransition.Starting);
			_starting = FinishStart(Task.Run(StartInCore));
			return _starting;
		}

		private static string? StartInCore()
		{
			if(EmuApi.IsMepBootstrapping()) {
				//#690: the legacy "Record while I play" setting (ADR-0243 Q3)
				//already records this load. Record takes over: that recording is
				//closed and kept, and this one starts as the next rec-NNN.
				EmuApi.StopMepRecording();
			}
			return EmuApi.StartMepRecording("play", "") ? EmuApi.GetMepRecordingFolder() : null;
		}

		private async Task<bool> FinishStart(Task<string?> core)
		{
			string? folder;
			try {
				folder = await core;
			} catch(Exception) {
				folder = null;
			}
			SetTransition(RecordingTransition.None);
			if(folder == null) {
				EndRecordingArtCheats();
				NoticeText = ResourceHelper.GetMessage("RemasterRecordFailed");
				Refresh();
				return false;
			}
			//#663: the builder stops the project's own pack art, if it was drawing.
			PackArtSwitch.Raise();
			_chosenProject = RemasterProjectLocator.FromRecordingFolder(folder);
			_chosenByUser = false;
			IsRecording = true;
			_recordingClock.Restart();
			_recordingTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => OnRecordingTick());
			_recordingTimer.Start();
			Refresh();
			return true;
		}

		//W-R2 › Stop (or Esc): the recording is closed and kept, W-R1 shows it,
		//and the kit runs as a job when it can (W-R2 text). G.6 (W-X3): quitting
		//or opening another game stops it without the kit run. Completes on the
		//UI thread once the core answered; a stop already in flight is returned.
		public Task StopRecording(bool prepareFigures = true)
		{
			if(Transition == RecordingTransition.Stopping && _stopping != null) {
				return _stopping;
			}
			if(!IsRecording || !RecordingTransitions.AcceptsClick(Transition)) {
				return Task.CompletedTask;
			}
			SetTransition(RecordingTransition.Stopping);
			_stopping = FinishStop(Task.Run(() => {
				string folder = EmuApi.GetMepRecordingFolder();
				EmuApi.StopMepRecording();
				return folder;
			}), prepareFigures);
			return _stopping;
		}

		private async Task FinishStop(Task<string> core, bool prepareFigures)
		{
			string folder;
			try {
				folder = await core;
			} catch(Exception) {
				folder = "";
			}
			SetTransition(RecordingTransition.None);
			PackArtSwitch.Raise();
			string project = RemasterProjectLocator.FromRecordingFolder(folder);
			if(project.Length > 0) {
				_chosenProject = project;
			}
			EndRecordingView();
			Refresh();
			if(prepareFigures && RemasterScreen.RunKitAfterRecording(Inputs())) {
				StartKit();
			}
		}

		private void EndRecordingView()
		{
			EndRecordingArtCheats();
			IsRecording = false;
			RecordingCounters = "";
			_recordingClock.Reset();
			_recordingTimer?.Stop();
		}

		private void SetTransition(RecordingTransition transition)
		{
			Transition = transition;
			Refresh();
		}

		private void RefreshRecordingWait()
		{
			IsRecordWaitVisible = Transition == RecordingTransition.Starting;
			RecordWaitText = IsRecordWaitVisible ? ResourceHelper.GetMessage("RemasterRecordingStarting") : "";
			IsStopWaitVisible = Transition == RecordingTransition.Stopping;
			StopWaitText = IsStopWaitVisible ? ResourceHelper.GetMessage("RemasterRecordingStopping") : "";
		}

		//#969 (W-X2, rule 10): W-R1's "⚠ This is not the game the project was
		//recorded from." with Open the Right Game…, which loads the project's ROM
		//(RemasterScreen.RightGameStep) or opens the ROM picker on the games folder.
		[ObservableProperty] public partial bool IsWrongGame { get; private set; }
		[ObservableProperty] public partial string WrongGameText { get; private set; } = "";
		//Remaster's own picker: the shell's sits on Play's load layer.
		public PlayerRomPickerViewModel RightGamePicker => _rightGamePicker ??= NewRightGamePicker();
		private PlayerRomPickerViewModel? _rightGamePicker;

		//Seams: the game load, the recent games and the games folder's files.
		public Action<string> LoadRom { get; set; } = LoadRomHelper.LoadFile;
		public Func<IEnumerable<string>> RecentRoms { get; set; } = ConfiguredRecentRoms;
		public Func<string?> GamesFolder { get; set; } = ConfiguredGamesFolder;

		private void RefreshWrongGame(RemasterScreenState s)
		{
			IsWrongGame = s.View == RemasterView.Project && s.Record.Reason == RemasterReason.NotThisProjectsGame;
			WrongGameText = IsWrongGame ? Reason(RemasterReason.NotThisProjectsGame) : "";
		}

		//Returns the step it took (null when the game is already the project's).
		public RemasterRightGame? OpenRightGame()
		{
			string? games = GamesFolder();
			RemasterRightGame? step = RemasterScreen.RightGameStep(Evaluate().Record.Reason, _project?.Folder ?? "",
				RecentRoms().Where(File.Exists), FilesIn(games));
			if(step == null) {
				return null;
			}
			if(!step.OpensPicker) {
				LoadRom(step.RomPath);
				return step;
			}
			RightGamePicker.Open();
			//Rooted at the games folder: descend into it when it is a root.
			PlayerRomPickerRow? root = games == null ? null : RightGamePicker.Rows.FirstOrDefault(r => r.Kind == RomPickerRowKind.Folder && RemasterProjectLocator.SameFolder(r.Path, games));
			if(root != null) {
				RightGamePicker.Choose(root);
			}
			return step;
		}

		private PlayerRomPickerViewModel NewRightGamePicker()
		{
			PlayerRomPickerViewModel picker = new();
			picker.RomChosen += path => LoadRom(path);
			return picker;
		}

		private static IEnumerable<string> FilesIn(string? folder)
		{
			try {
				return string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? Array.Empty<string>() : Directory.GetFiles(folder);
			} catch(Exception) {
				return Array.Empty<string>();
			}
		}

		private static IEnumerable<string> ConfiguredRecentRoms() => ConfigManager.Config.RecentFiles.Items.Select(i => i.RomFile.Path);

		private static string? ConfiguredGamesFolder() => GamesFolderChoice.Usable(
			ConfigManager.Config.Preferences.OverrideGameFolder ? ConfigManager.Config.Preferences.GameFolder : null);
	}
}
