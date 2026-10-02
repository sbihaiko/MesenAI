using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Mesen.ViewModels
{
	//G.8 (PRD Part B §8, ADR-0241, §13.4, §13.5.4 W-H1-W-H4): the Share
	//workspace - the home with two cards (W-H1), share a pack (W-H2), share my
	//project (W-H3) and record and share a replay (W-H4). Share holds no
	//credential and uploads nothing: every submission is a pre-filled Issue URL
	//opened in the user's browser, and the click plus the ↗ glyph is the
	//confirmation (rule 7). The rules are host-free in UI/Logic/PackShare.cs,
	//ShareProjectPackage.cs and ShareScreen.cs; this type maps them to strings.
	//Phase 3 VM rule (UI/AGENTS.md): the constructor does no EmuApi/ConfigManager
	//I/O - the allow-list, the job launcher, the Python/tools gate, the replay
	//recorder and the two host actions (browser, reveal) are injected.
	public partial class ShareWorkspaceViewModel : ViewModelBase
	{
		private readonly IReadOnlyList<CommunityPackHostEntry> _hosts;
		private readonly Func<RemasterFeasibility?> _feasibility;
		private readonly Func<IEnumerable<string>> _knownProjects;
		private readonly IReplayRecorder _recorder;
		private readonly Action<string> _openBrowser;
		private readonly Action<string> _reveal;
		private readonly Func<(bool MovieBusy, bool Netplay)> _sessions;
		private readonly RemasterJobRunner _jobs;
		private readonly Stopwatch _recordingClock = new();
		private DispatcherTimer? _recordingTimer;

		private bool _gameLoaded;
		private ConsoleType _console = ConsoleType.Nes;
		private string _gameName = "";
		private string _romPath = "";
		private string _gameProject = "";
		private ShareProjectIdentity? _project;
		private string _replayFile = "";
		private bool _packedThisSession;

		[ObservableProperty] public partial ShareView View { get; private set; } = ShareView.Home;
		[ObservableProperty] public partial bool IsHome { get; private set; } = true;
		[ObservableProperty] public partial bool IsPackPage { get; private set; }
		[ObservableProperty] public partial bool IsProjectPage { get; private set; }
		[ObservableProperty] public partial string NoticeText { get; private set; } = "";

		//W-H2
		[ObservableProperty] public partial string PackLink { get; set; } = "";
		[ObservableProperty] public partial string Game { get; set; } = "";
		[ObservableProperty] public partial string ConsoleChoice { get; set; } = "";
		[ObservableProperty] public partial string HostError { get; private set; } = "";
		[ObservableProperty] public partial bool IsContinueEnabled { get; private set; }
		[ObservableProperty] public partial string ContinueReason { get; private set; } = "";
		[ObservableProperty] public partial bool IsProjectListOpen { get; private set; }
		[ObservableProperty] public partial List<ShareProjectRow> ProjectChoices { get; private set; } = new();
		[ObservableProperty] public partial bool HasNoProjects { get; private set; }

		//W-H3
		[ObservableProperty] public partial string ProjectTitle { get; private set; } = "";
		[ObservableProperty] public partial bool IsBuildEnabled { get; private set; }
		[ObservableProperty] public partial string BuildReason { get; private set; } = "";
		[ObservableProperty] public partial bool IsBuildRunning { get; private set; }
		[ObservableProperty] public partial string BuildResult { get; private set; } = "";
		[ObservableProperty] public partial string BuildFailure { get; private set; } = "";
		[ObservableProperty] public partial bool IsZipReady { get; private set; }
		[ObservableProperty] public partial string ProjectLink { get; set; } = "";
		[ObservableProperty] public partial string ProjectHostError { get; private set; } = "";
		[ObservableProperty] public partial bool IsProjectContinueEnabled { get; private set; }
		[ObservableProperty] public partial string ProjectContinueReason { get; private set; } = "";

		//W-H4
		[ObservableProperty] public partial ReplaySheet Sheet { get; private set; } = ReplaySheet.None;
		[ObservableProperty] public partial bool IsStartSheetOpen { get; private set; }
		[ObservableProperty] public partial bool IsSavedSheetOpen { get; private set; }
		[ObservableProperty] public partial bool IsRecording { get; private set; }
		[ObservableProperty] public partial string StartSheetTitle { get; private set; } = "";
		[ObservableProperty] public partial bool IsStartEnabled { get; private set; }
		[ObservableProperty] public partial string StartReason { get; private set; } = "";
		[ObservableProperty] public partial string RecordingPill { get; private set; } = "";
		[ObservableProperty] public partial string SavedFileName { get; private set; } = "";

		//Raised when the replay recording starts or ends, for the game view.
		public event Action? RecordingChanged;

		public IReadOnlyList<string> ConsoleOptions => PackShare.ConsoleOptions;
		public ShareProjectIdentity? Project => _project;
		public RemasterJobSnapshot Job => _jobs.Snapshot;

		[Obsolete("For designer only")]
		public ShareWorkspaceViewModel() : this(Array.Empty<CommunityPackHostEntry>(), new NullLauncher(), () => null, () => Array.Empty<string>(), new NullRecorder(), _ => { }, _ => { }, () => (false, false)) { }

		public ShareWorkspaceViewModel(IReadOnlyList<CommunityPackHostEntry> hosts, IJobProcessLauncher launcher, Func<RemasterFeasibility?> feasibility,
			Func<IEnumerable<string>> knownProjects, IReplayRecorder recorder, Action<string> openBrowser, Action<string> reveal,
			Func<(bool MovieBusy, bool Netplay)> sessions)
		{
			_sessions = sessions;
			_hosts = hosts;
			_feasibility = feasibility;
			_knownProjects = knownProjects;
			_recorder = recorder;
			_openBrowser = openBrowser;
			_reveal = reveal;
			_jobs = new RemasterJobRunner(launcher);
			_jobs.Changed += _ => Dispatcher.UIThread.Post(OnJobChanged);
			Refresh();
		}

		//Fed by MainWindowViewModel on every RomInfo change. A game change never
		//closes a Share screen (rule 5); the fields the user typed are kept.
		public void UpdateGame(bool gameLoaded, ConsoleType console, string gameName, string romPath, string gameProject)
		{
			_gameLoaded = gameLoaded;
			_console = console;
			_gameName = gameName ?? "";
			_romPath = romPath ?? "";
			_gameProject = gameProject ?? "";
			Refresh();
		}

		//The feasibility measurement finished (it is Remaster's, shared).
		public void Refresh()
		{
			IsHome = View == ShareView.Home;
			IsPackPage = View == ShareView.Pack;
			IsProjectPage = View == ShareView.Project;
			RefreshPack();
			RefreshProject();
			RefreshReplay();
		}

		partial void OnPackLinkChanged(string value) => RefreshPack();
		partial void OnGameChanged(string value) => RefreshPack();
		partial void OnConsoleChoiceChanged(string value) => RefreshPack();
		partial void OnProjectLinkChanged(string value) => RefreshProject();

		private void Navigate(ShareView view)
		{
			View = view;
			IsProjectListOpen = false;
			NoticeText = "";
			Refresh();
		}

		public void GoHome() => Navigate(ShareView.Home);

		//W-H1 › Share a Pack. "Filled in from the game you are playing": only an
		//empty field is filled, so a name the user typed survives.
		public void OpenPackPage()
		{
			if(_gameLoaded && Game.Length == 0) {
				Game = _gameName;
				ConsoleChoice = PackShare.ConsoleOption(_console, _romPath);
			}
			Navigate(ShareView.Pack);
		}

		//W-H2 › Package a Project…: the projects Remaster knows, inside Share
		//(rule 11 - it never jumps to the Remaster profile).
		public void ToggleProjectList()
		{
			IsProjectListOpen = !IsProjectListOpen;
			if(IsProjectListOpen) {
				ProjectChoices = ShareProjectPackage.KnownProjects(_knownProjects())
					.Select(f => new ShareProjectRow(f, ShareProjectPackage.Read(f).Name)).ToList();
				HasNoProjects = ProjectChoices.Count == 0;
			}
		}

		//Package a Project… (a row, or Choose Folder…), and Remaster's "Share this
		//project — opens Share": W-H3 for that project.
		public bool OpenProject(string folder)
		{
			if(!RemasterProjectReader.IsProjectFolder(folder)) {
				NoticeText = ResourceHelper.GetMessage("RemasterNotAProject");
				return false;
			}
			bool same = _project != null && RemasterProjectLocator.SameFolder(_project.Folder, folder);
			_project = ShareProjectPackage.Read(folder);
			if(!same) {
				ProjectLink = "";
				_packedThisSession = false;
				if(!_jobs.Snapshot.IsRunning) {
					_jobs.Clear();
				}
			}
			Navigate(ShareView.Project);
			return true;
		}

		private bool IsRunningGamesProject => _project != null && _gameLoaded && RemasterProjectLocator.SameFolder(_project.Folder, _gameProject);

		//W-H3 step 3 (and W-H2): Game/Console the form receives.
		private string ProjectConsole()
		{
			if(_project == null) {
				return "";
			}
			return _project.ConsoleOption.Length > 0 ? _project.ConsoleOption
				: IsRunningGamesProject ? PackShare.ConsoleOption(_console, _romPath) : "";
		}

		//W-H2 › Continue on GitHub ↗. Returns the URL it opened ("" when disabled).
		public string ContinuePack()
		{
			RefreshPack();
			if(!IsContinueEnabled) {
				return "";
			}
			string url = PackShare.BuildIssueUrl(PackLink, Game, ConsoleChoice);
			_openBrowser(url);
			return url;
		}

		//W-H3 › Continue on GitHub ↗.
		public string ContinueProject()
		{
			RefreshProject();
			if(!IsProjectContinueEnabled || _project == null) {
				return "";
			}
			string url = PackShare.BuildIssueUrl(ProjectLink, _project.Game, ProjectConsole());
			_openBrowser(url);
			return url;
		}

		//W-H3 › Build Pack .zip: `mep_build.py pack` as a job (W-R3 card).
		public bool BuildZip()
		{
			RefreshProject();
			RemasterFeasibility? f = _feasibility();
			if(!IsBuildEnabled || _project == null || f == null) {
				return false;
			}
			RemasterJobSpec spec = ShareProjectPackage.PackJob(new PythonCandidate(f.PythonExecutable, f.PythonPrefixArgs), f.ToolsFolder, _project,
				IsRunningGamesProject ? _romPath : "");
			bool started = _jobs.Start(spec);
			OnJobChanged();
			return started;
		}

		public void StopBuild() => _jobs.Stop();

		public void ShowZip()
		{
			if(_project != null && File.Exists(_project.ZipPath)) {
				_reveal(_project.ZipPath);
			}
		}

		//W-H3 step 2: the browser, on the user's click (the ↗ is the confirmation).
		public void OpenGoogleDrive() => _openBrowser(ShareProjectPackage.GoogleDriveUrl);

		private void OnJobChanged()
		{
			RemasterJobSnapshot job = _jobs.Snapshot;
			IsBuildRunning = job.IsRunning;
			if(job.Status == RemasterJobStatus.Succeeded) {
				_packedThisSession = true;
			}
			if(!job.IsRunning && _project != null) {
				//pack.json now declares the target: the console is known.
				_project = ShareProjectPackage.Read(_project.Folder);
			}
			RefreshProject();
		}

		private void RefreshPack()
		{
			PackLinkCheck link = PackShare.CheckLink(PackLink, _hosts);
			HostError = link == PackLinkCheck.NotAccepted ? ResourceHelper.GetMessage("ShareHostNotAccepted") : "";
			PackShareReason reason = PackShare.Evaluate(PackLink, Game, ConsoleChoice, _hosts);
			IsContinueEnabled = reason == PackShareReason.None;
			ContinueReason = reason is PackShareReason.None or PackShareReason.HostNotAccepted ? "" : ResourceHelper.GetMessage("ShareReason" + reason);
		}

		private void RefreshProject()
		{
			if(_project == null) {
				ProjectTitle = "";
				IsBuildEnabled = false;
				IsProjectContinueEnabled = false;
				return;
			}
			ProjectTitle = ResourceHelper.GetMessage("ShareProjectTitle", _project.Name);
			RemasterJobSnapshot job = _jobs.Snapshot;
			RemasterFeasibility f = _feasibility() ?? PendingFeasibility;
			ShareBuildReason build = ShareProjectPackage.BuildReason(_project, IsRunningGamesProject, f, job.IsRunning);
			IsBuildEnabled = build == ShareBuildReason.None;
			BuildReason = build is ShareBuildReason.None or ShareBuildReason.JobRunning ? "" : ResourceHelper.GetMessage("ShareBuildReason" + build);

			IsZipReady = _packedThisSession && File.Exists(_project.ZipPath);
			BuildFailure = "";
			BuildResult = job.Status switch {
				RemasterJobStatus.Running => ResourceHelper.GetMessage("ShareBuildRunning"),
				RemasterJobStatus.Stopped => ResourceHelper.GetMessage("ShareBuildStopped"),
				RemasterJobStatus.Failed => ResourceHelper.GetMessage("ShareBuildFailed"),
				_ => IsZipReady ? ResourceHelper.GetMessage("ShareBuildDone", Path.GetFileName(_project.ZipPath), ShareProjectPackage.FormatSize(new FileInfo(_project.ZipPath).Length)) : "",
			};
			if(job.Status == RemasterJobStatus.Failed) {
				//W-R4 (inline build problems) is Remaster's slice: a plain line here.
				BuildFailure = job.FailureLine;
			}

			PackLinkCheck link = PackShare.CheckLink(ProjectLink, _hosts);
			ProjectHostError = link == PackLinkCheck.NotAccepted ? ResourceHelper.GetMessage("ShareHostNotAccepted") : "";
			PackShareReason reason = PackShare.Evaluate(ProjectLink, _project.Game, ProjectConsole(), _hosts, requireConsole: false);
			IsProjectContinueEnabled = reason == PackShareReason.None;
			ProjectContinueReason = reason is PackShareReason.None or PackShareReason.HostNotAccepted ? "" : ResourceHelper.GetMessage("ShareReason" + reason);
		}

		//Until measured, the gate reads as passed so the reason does not flash.
		private static readonly RemasterFeasibility PendingFeasibility = new(PythonGate.Found, "", Array.Empty<string>(), "", ToolsGate.Found, "");

		//W-H1 › Record and Share: the first W-H4 sheet.
		public void OpenReplaySheet()
		{
			NoticeText = "";
			Sheet = ReplaySheet.Start;
			RefreshReplay();
		}

		public void CloseSheet()
		{
			if(Sheet is ReplaySheet.Start or ReplaySheet.Saved) {
				Sheet = ReplaySheet.None;
				RefreshReplay();
			}
		}

		//Start Recording: ShareRecordingSession, unchanged (ADR-0205 §2, R.1).
		public bool StartReplay()
		{
			RefreshReplay();
			if(!IsStartEnabled) {
				return false;
			}
			string? file = _recorder.Start();
			if(file == null) {
				NoticeText = ResourceHelper.GetMessage("ShareReplayRefused");
				Sheet = ReplaySheet.None;
				RefreshReplay();
				return false;
			}
			_replayFile = file;
			Sheet = ReplaySheet.Recording;
			_recordingClock.Restart();
			_recordingTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => OnRecordingTick());
			_recordingTimer.Start();
			RefreshReplay();
			RecordingChanged?.Invoke();
			return true;
		}

		//Stop (or Esc): the second sheet names the file; nothing opens by itself.
		public void StopReplay()
		{
			if(Sheet != ReplaySheet.Recording) {
				return;
			}
			string? file = _recorder.StopAndKeep();
			EndRecording(file);
		}

		private void EndRecording(string? file)
		{
			_recordingTimer?.Stop();
			_recordingClock.Reset();
			if(file != null) {
				_replayFile = file;
				SavedFileName = Path.GetFileName(file);
				Sheet = ReplaySheet.Saved;
			} else {
				_replayFile = "";
				NoticeText = ResourceHelper.GetMessage("ShareReplayEnded");
				Sheet = ReplaySheet.None;
			}
			RefreshReplay();
			RecordingChanged?.Invoke();
		}

		private void OnRecordingTick()
		{
			if(Sheet != ReplaySheet.Recording) {
				return;
			}
			//The core ends a shared recording by itself (save state, ROM change),
			//or Tools ⋯ › Movies › Stop handed the file over already.
			if(!_recorder.IsSharing) {
				EndRecording(null);
				return;
			}
			RecordingPill = ResourceHelper.GetMessage("ShareRecordingPill", RemasterScreen.FormatElapsed(_recordingClock.Elapsed));
		}

		public void ShowReplayFile()
		{
			if(_replayFile.Length > 0 && File.Exists(_replayFile)) {
				_reveal(_replayFile);
			}
		}

		//Replay saved › Continue on GitHub ↗: the R.1 form, pre-filled as before.
		public string ContinueReplay()
		{
			if(Sheet != ReplaySheet.Saved) {
				return "";
			}
			string url = _recorder.IssueUrl();
			_openBrowser(url);
			return url;
		}

		//Rule 8 in Share (ShareEsc). Returns true when Esc did something.
		public bool HandleEsc()
		{
			switch(ShareEsc.Next(Sheet, IsProjectListOpen)) {
				case ShareEscAction.StopRecording: StopReplay(); return true;
				case ShareEscAction.CloseSheet: CloseSheet(); return true;
				case ShareEscAction.CloseProjectList: IsProjectListOpen = false; return true;
				default: return false;
			}
		}

		private void RefreshReplay()
		{
			IsStartSheetOpen = Sheet == ReplaySheet.Start;
			IsSavedSheetOpen = Sheet == ReplaySheet.Saved;
			IsRecording = Sheet == ReplaySheet.Recording;
			StartSheetTitle = _gameLoaded ? ResourceHelper.GetMessage("ShareReplaySheetTitle", _gameName) : ResourceHelper.GetMessage("ShareReplaySheetTitleNoGame");
			//A movie or netplay session can start from Tools ⋯ at any time: asked each refresh.
			(bool movieBusy, bool netplay) = Sheet == ReplaySheet.Start ? _sessions() : (false, false);
			ReplayStartReason reason = ShareReplay.StartReason(_gameLoaded, _console, movieBusy, netplay);
			IsStartEnabled = reason == ReplayStartReason.None && Sheet == ReplaySheet.Start;
			StartReason = reason == ReplayStartReason.None ? "" : ResourceHelper.GetMessage("ShareReplayReason" + reason);
			if(IsRecording) {
				RecordingPill = ResourceHelper.GetMessage("ShareRecordingPill", RemasterScreen.FormatElapsed(_recordingClock.Elapsed));
			}
		}

		private sealed class NullLauncher : IJobProcessLauncher
		{
			public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
			{
				throw new InvalidOperationException("designer");
			}
		}

		private sealed class NullRecorder : IReplayRecorder
		{
			public bool IsSharing => false;
			public string? Start() => null;
			public string? StopAndKeep() => null;
			public string IssueUrl() => "";
		}
	}

	//One row of *Package a Project…*'s list.
	public sealed record ShareProjectRow(string Folder, string Name);
}
