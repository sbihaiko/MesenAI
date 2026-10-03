using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//G.3 (PRD Part B §8, ADR-0241, ADR-0243; §13.5.3 W-R0-W-R3): the Remaster
	//workspace - no project yet (W-R0), the Python/tools feasibility banner
	//(W-R0b), the project screen (W-R1), the recording view (W-R2) and the job
	//card (W-R3). The rules (which screen, which control, why disabled, the job
	//state) are host-free in UI/Logic/Remaster*.cs; this type maps them to
	//strings and calls the core. Phase 3 VM rule (UI/AGENTS.md): the
	//constructor does no EmuApi/ConfigManager I/O - config, the process
	//launcher and the feasibility measurement are injected.
	public partial class RemasterWorkspaceViewModel : ViewModelBase
	{
		private readonly RemasterConfig _config;
		private readonly Func<RemasterConfig, RemasterFeasibility> _measure;
		private readonly RemasterJobRunner _jobs;
		private readonly IJobProcessLauncher _launcher;
		private readonly bool _hasHeadlessRecorder;
		private readonly Stopwatch _recordingClock = new();
		private readonly RemasterShapeCache _shapeCache = new();
		private DispatcherTimer? _recordingTimer;
		private DispatcherTimer? _jobResultTimer;

		private bool _gameLoaded;
		private ConsoleType _console = ConsoleType.Nes;
		private string _gameName = "";
		private string _romPath = "";
		private string _gameProjectSibling = "";
		private string _packsFolder = "";
		//A folder picked with W-R0's Choose Folder… (or a recording's project);
		//"" = follow the running game's project.
		private string _chosenProject = "";
		//True when the user picked it; a project adopted from a recording follows
		//the game, so loading another game shows that game's project (or W-R0).
		private bool _chosenByUser;
		private RemasterProjectInfo? _project;
		private RemasterFeasibility? _feasibility;
		private Task? _measuring;

		[ObservableProperty] public partial RemasterView View { get; private set; } = RemasterView.NoProject;
		[ObservableProperty] public partial bool IsNoProject { get; private set; } = true;
		[ObservableProperty] public partial bool IsProject { get; private set; }
		[ObservableProperty] public partial bool IsRecording { get; private set; }
		[ObservableProperty] public partial RemasterActivity Activity { get; private set; }

		//W-R0
		[ObservableProperty] public partial string StartCardGame { get; private set; } = "";
		[ObservableProperty] public partial string StartButtonText { get; private set; } = "";
		[ObservableProperty] public partial bool IsStartEnabled { get; private set; }
		[ObservableProperty] public partial bool StartOpensRom { get; private set; }
		[ObservableProperty] public partial string StartReason { get; private set; } = "";
		[ObservableProperty] public partial string NoticeText { get; private set; } = "";

		//W-R0b
		[ObservableProperty] public partial bool IsBannerVisible { get; private set; }
		[ObservableProperty] public partial string BannerText { get; private set; } = "";
		//The banner's second, regular-weight line (the render: what still works).
		[ObservableProperty] public partial string BannerDetail { get; private set; } = "";
		//Advanced's one-line banner: both sentences, as before the W-R0b split.
		[ObservableProperty] public partial string BannerLine { get; private set; } = "";
		[ObservableProperty] public partial bool IsPythonMissing { get; private set; }
		[ObservableProperty] public partial bool IsToolsMissing { get; private set; }
		//The Python/tools probe is running (a moving line until it answers).
		[ObservableProperty] public partial bool IsFeasibilityChecking { get; private set; }
		//A project scan (recordings, shapes, painted cells) is in flight: a sentence over a moving bar.
		[ObservableProperty] public partial bool IsScanWaitVisible { get; private set; }
		[ObservableProperty] public partial string ScanWaitText { get; private set; } = "";
		[ObservableProperty] public partial string PendingBrowserUrl { get; private set; } = "";
		[ObservableProperty] public partial string BrowserConfirmText { get; private set; } = "";

		//W-R1
		[ObservableProperty] public partial string ProjectName { get; private set; } = "";
		[ObservableProperty] public partial string ProjectFolder { get; private set; } = "";
		[ObservableProperty] public partial string RecordSummary { get; private set; } = "";
		[ObservableProperty] public partial List<RemasterRecordingRow> Recordings { get; private set; } = new();
		//W-R1's second line under the summary: the newest recording (Player mode
		//shows it instead of the list).
		[ObservableProperty] public partial string RecordDetail { get; private set; } = "";
		//ADR-0252 §1: "1 240 shapes seen while you played" ("" = unknown).
		[ObservableProperty] public partial string ShapesSeenText { get; private set; } = "";
		[ObservableProperty] public partial RemasterControlViewModel Record { get; private set; } = RemasterControlViewModel.Hidden;
		[ObservableProperty] public partial RemasterControlViewModel RecordFromTas { get; private set; } = RemasterControlViewModel.Hidden;
		[ObservableProperty] public partial RemasterControlViewModel LetTheAiPlay { get; private set; } = RemasterControlViewModel.Hidden;
		[ObservableProperty] public partial RemasterControlViewModel PrepareFigures { get; private set; } = RemasterControlViewModel.Hidden;
		[ObservableProperty] public partial RemasterControlViewModel BuildAndShow { get; private set; } = RemasterControlViewModel.Hidden;
		[ObservableProperty] public partial string PaintText { get; private set; } = "";

		//W-R2
		[ObservableProperty] public partial string RecordingPill { get; private set; } = "";
		//W-R2's counters: the core's live coverage of this recording ("" until it reports any).
		[ObservableProperty] public partial string RecordingCounters { get; private set; } = "";

		//W-R3
		[ObservableProperty] public partial bool IsJobCardVisible { get; private set; }
		[ObservableProperty] public partial bool IsJobRunning { get; private set; }
		[ObservableProperty] public partial string JobTitle { get; private set; } = "";
		[ObservableProperty] public partial string JobDetail { get; private set; } = "";
		[ObservableProperty] public partial int JobPercent { get; private set; }
		//A step reports nothing until it ends: the bar moves meanwhile.
		[ObservableProperty] public partial bool IsJobBarIndeterminate { get; private set; }

		//Raised when Activity or the job's progress changes, for the shell's
		//dot and status line (§13.6, W-X3).
		public event Action? ActivityChanged;

		//G.8 (§13.6, rule 11): W-R1's "Share this project — opens Share" link
		//calls RequestShareProject; the shell switches to Share's W-H3.
		public event Action<string>? ShareProjectRequested;

		public void RequestShareProject()
		{
			if(_project != null) {
				ShareProjectRequested?.Invoke(_project.Folder);
			}
		}

		public RemasterJobSnapshot Job => _jobs.Snapshot;

		//#647: Share's job, read by the job buttons' gate (WorkspaceJobs.Link).
		public Func<RemasterJobSnapshot> OtherWorkspaceJob { get; set; } = () => RemasterJobSnapshot.Idle;

		//Raised after every change of this workspace's job, on the UI thread.
		public event Action? JobChanged;
		public RemasterFeasibility? Feasibility => _feasibility;
		public RemasterProjectInfo? Project => _project;
		public string GameName => _gameName;
		//What the jobs get as --rom (#689: an archive's inner ROM, written out).
		public string RomPath => _romPath;

		[Obsolete("For designer only")]
		public RemasterWorkspaceViewModel() : this(new RemasterConfig(), _ => new RemasterFeasibility(PythonGate.Found, "", Array.Empty<string>(), "", ToolsGate.Found, ""), new NullLauncher(), false) { }

		public RemasterWorkspaceViewModel(RemasterConfig config, Func<RemasterConfig, RemasterFeasibility> measure, IJobProcessLauncher launcher, bool hasHeadlessRecorder)
		{
			_config = config;
			CountShapes = _shapeCache.Count;
			_measure = measure;
			_hasHeadlessRecorder = hasHeadlessRecorder;
			_launcher = launcher;
			_jobs = new RemasterJobRunner(launcher);
			_jobs.Changed += _ => Dispatcher.UIThread.Post(OnJobChanged);
			Refresh();
		}

		//#619: the measurement in flight (it posts its result from the thread
		//pool), without starting one. Headless tests wait on it before they end.
		public Task Measuring => _measuring ?? Task.CompletedTask;

		//The first time Remaster is shown: measure the gate once (off the UI
		//thread - it spawns a few `python -c` children). Returns the measurement.
		public Task EnsureFeasibilityMeasured()
		{
			if(_measuring == null) {
				RemasterConfig snapshot = new() { PythonPath = _config.PythonPath, ToolsFolder = _config.ToolsFolder };
				_measuring = Task.Run(() => _measure(snapshot)).ContinueWith(t => {
					Dispatcher.UIThread.Post(() => {
						_feasibility = t.IsCompletedSuccessfully ? t.Result : new RemasterFeasibility(PythonGate.Missing, "", Array.Empty<string>(), "", ToolsGate.Missing, "");
						Refresh();
					});
				}, TaskScheduler.Default);
				//The jobs wait with their reason, and the probe shows, until it answers.
				Refresh();
			}
			return _measuring;
		}

		//Locate Python… / Locate Tools…: store the pick and measure again.
		public Task Relocate(string? pythonPath, string? toolsFolder)
		{
			if(pythonPath != null) {
				_config.PythonPath = pythonPath;
			}
			if(toolsFolder != null) {
				_config.ToolsFolder = toolsFolder;
			}
			_measuring = null;
			return EnsureFeasibilityMeasured();
		}

		//Fed by MainWindowViewModel on every RomInfo change. The sibling and the
		//packs folder locate the game's project (ADR-0243 Decision 1). A game
		//change never closes this screen (rule 5); a recording the core ended
		//with its ROM (MepPackManager::Clear) returns to W-R1.
		public void UpdateGame(bool gameLoaded, ConsoleType console, string gameName, string romPath, string siblingFolder, string packsFolder)
		{
			bool changedGame = gameName != _gameName || romPath != _romPath;
			_gameLoaded = gameLoaded;
			_console = console;
			_gameName = gameName ?? "";
			_romPath = romPath ?? "";
			_gameProjectSibling = siblingFolder ?? "";
			_packsFolder = packsFolder ?? "";
			if(changedGame && IsRecording) {
				EndRecordingView();
			}
			if(changedGame) {
				//G.6: the build view showed the project's game, not this one.
				IsShowingBuild = false;
			}
			if(changedGame && !_chosenByUser) {
				_chosenProject = "";
			}
			Refresh();
		}

		private string GameProjectFolder()
		{
			return _gameLoaded ? RemasterProjectLocator.ForGame(_gameProjectSibling, _packsFolder) : "";
		}

		private string ShownProjectFolder()
		{
			return _chosenProject.Length > 0 ? _chosenProject : GameProjectFolder();
		}

		//W-R0 › Choose Folder… (and the project menu's Switch Project…).
		public bool OpenProjectFolder(string folder)
		{
			if(!RemasterProjectReader.IsProjectFolder(folder)) {
				if(RemasterHandOff.Classify(folder) == RemasterFolderKind.FinishedPack) {
					return BeginImport(folder);
				}
				NoticeText = ResourceHelper.GetMessage("RemasterNotAProject");
				return false;
			}
			_chosenProject = folder;
			_chosenByUser = true;
			NoticeText = "";
			Refresh();
			return true;
		}

		private void OnRecordingTick()
		{
			//A stop in flight ends the view itself when the core answers.
			if(!IsRecording || Transition != RecordingTransition.None) {
				return;
			}
			//The core ends a recording by itself when its ROM goes away.
			if(!EmuApi.IsMepBootstrapping()) {
				EndRecordingView();
				Refresh();
				return;
			}
			RecordingPill = ResourceHelper.GetMessage("RemasterRecordingPill", RemasterScreen.FormatElapsed(_recordingClock.Elapsed));
			//F5.4d's coverage report: zero-filled off NES or before the builder saw anything.
			InteropHdPackCoverageReport coverage = EmuApi.GetHdPackCoverageReport();
			UpdateRecordingCounters(coverage.TilesSeen, coverage.ScreensSeen);
		}

		private void UpdateRecordingCounters(uint tilesSeen, uint screensSeen)
		{
			//ADR-0252 §4: shapes this recording has drawn; screens up to the cap the core writes.
			uint screens = RemasterScreen.ScreensCaptured(screensSeen);
			RecordingCounters = !RemasterScreen.ShowsRecordingCounters(tilesSeen, screensSeen) ? ""
				: ResourceHelper.GetMessage(tilesSeen == 1 ? "RemasterRecordingShapesOne" : "RemasterRecordingShapesMany", tilesSeen)
					+ " · " + ResourceHelper.GetMessage(screens == 1 ? "RemasterRecordingScreensOne" : "RemasterRecordingScreensMany", screens);
		}

		//Zone ② Prepare Figures, and the run after Stop: mep_project.py kit.
		public bool StartKit()
		{
			if(!Evaluate().PrepareFigures.Enabled || _project == null || _feasibility == null) {
				return false;
			}
			PythonCandidate python = new(_feasibility.PythonExecutable, _feasibility.PythonPrefixArgs);
			RemasterJobSpec spec = RemasterJobs.Kit(python, _feasibility.ToolsFolder, _project.Folder, _romPath, _project.TexturedRecordingCount, _gameName);
			_jobResultTimer?.Stop();
			bool started = _jobs.Start(spec);
			OnJobChanged();
			return started;
		}

		public void StopJob() => _jobs.Stop();

		public void DismissJobResult()
		{
			_jobResultTimer?.Stop();
			_jobs.Clear();
		}

		//W-R0b › How to Install / How to Get Them: opening the browser is an
		//external action, so it asks once, in place (rule 7).
		public void AskToOpenBrowser(string url)
		{
			PendingBrowserUrl = url;
			BrowserConfirmText = ResourceHelper.GetMessage("RemasterOpenBrowserConfirm", new Uri(url).Host);
		}

		public string ConfirmBrowser()
		{
			string url = PendingBrowserUrl;
			PendingBrowserUrl = "";
			return url;
		}

		public void CancelBrowser() => PendingBrowserUrl = "";

		private void OnJobChanged()
		{
			RemasterJobSnapshot job = _jobs.Snapshot;
			IsJobRunning = job.IsRunning;
			JobPercent = job.Percent;
			IsJobBarIndeterminate = job.BarIsIndeterminate;
			switch(job.Status) {
				case RemasterJobStatus.Running:
					JobTitle = job.Kind == RemasterJobKind.Build ? BuildJobTitle(job) : ResourceHelper.GetMessage(JobMessage(job.Kind, "RemasterJobKitTitle"));
					JobDetail = job.Kind == RemasterJobKind.Import ? "" : ResourceHelper.GetMessage("RemasterJobStep", Math.Min(job.StepsDone + 1, Math.Max(job.TotalSteps, 1)), Math.Max(job.TotalSteps, 1),
						ResourceHelper.GetMessage("RemasterStep" + RemasterJobs.StepOf(job.CurrentStep)));
					break;
				case RemasterJobStatus.Succeeded:
					JobTitle = job.Kind == RemasterJobKind.Build ? BuildJobTitle(job) : ResourceHelper.GetMessage(JobMessage(job.Kind, "RemasterJobDone"));
					JobDetail = "";
					//W-R3: success collapses to one line for 5 s.
					_jobResultTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => DismissJobResult());
					_jobResultTimer.Stop();
					_jobResultTimer.Start();
					break;
				case RemasterJobStatus.Failed:
					//The kit's failure is a plain line; a build's is W-R4 (G.6).
					JobTitle = job.Kind == RemasterJobKind.Build ? BuildJobTitle(job) : ResourceHelper.GetMessage(JobMessage(job.Kind, "RemasterJobFailed"));
					JobDetail = job.Kind == RemasterJobKind.Build ? "" : job.FailureLine;
					break;
				case RemasterJobStatus.Stopped:
					JobTitle = job.Kind == RemasterJobKind.Build ? BuildJobTitle(job) : ResourceHelper.GetMessage("RemasterJobStopped");
					JobDetail = "";
					break;
				default:
					JobTitle = "";
					JobDetail = "";
					break;
			}
			if(!job.IsRunning) {
				ReadProject();
			}
			Refresh();
			OnImportJobChanged(job);
			JobChanged?.Invoke();
		}

		private void ReadProject()
		{
			string folder = ShownProjectFolder();
			_project = folder.Length > 0 && RemasterProjectReader.IsProjectFolder(folder) ? RemasterProjectReader.Read(folder) : null;
		}

		private RemasterInputs Inputs()
		{
			string shown = _project?.Folder ?? "";
			return new RemasterInputs(_gameLoaded, _console, IsRecording, _jobs.Snapshot.IsRunning, shown, IsGamesProject(shown) && _gameLoaded,
				_project?.TexturedRecordingCount ?? 0, _feasibility ?? PendingFeasibility, _hasHeadlessRecorder, _project?.HasKit ?? false,
				RemasterJobs.RunsOn(OtherWorkspaceJob(), shown), Transition, IsFeasibilityPending);
		}

		//Before the first recording the game's project does not exist yet; a
		//chosen folder is the game's own when it is where that game records.
		private bool IsGamesProject(string folder)
		{
			return folder.Length > 0 && (RemasterProjectLocator.SameFolder(folder, GameProjectFolder()) ||
				RemasterProjectLocator.SameFolder(folder, _gameProjectSibling) ||
				RemasterProjectLocator.SameFolder(folder, RemasterProjectLocator.FallbackFolder(_gameProjectSibling, _packsFolder)));
		}

		//Until measured, the gate reads as passed so the banner does not flash.
		private static readonly RemasterFeasibility PendingFeasibility = new(PythonGate.Found, "", Array.Empty<string>(), "", ToolsGate.Found, "");

		private RemasterScreenState Evaluate() => RemasterScreen.Evaluate(Inputs());

		public void Refresh()
		{
			if(!IsJobRunning) {
				ReadProject();
			}
			RemasterScreenState s = Evaluate();
			View = s.View;
			IsNoProject = s.View == RemasterView.NoProject;
			IsProject = s.View == RemasterView.Project;
			RefreshRecentProjects();

			StartCardGame = _gameLoaded ? _gameName : ResourceHelper.GetMessage("RemasterNoGameRunning");
			StartButtonText = ResourceHelper.GetMessage(s.PrimaryOpensRom ? "RemasterOpenRomToStart" : "RemasterStartRecording");
			StartOpensRom = s.PrimaryOpensRom;
			IsStartEnabled = s.PrimaryOpensRom || s.Record.Enabled;
			StartReason = IsStartEnabled ? "" : Reason(s.Record.Reason);

			RemasterFeasibility f = _feasibility ?? PendingFeasibility;
			IsBannerVisible = s.ShowFeasibilityBanner;
			IsFeasibilityChecking = s.ShowFeasibilityChecking;
			RefreshRecordingWait();
			IsPythonMissing = f.Python != PythonGate.Found;
			IsToolsMissing = !IsPythonMissing && f.Tools != ToolsGate.Found;
			string banner = f.Python == PythonGate.TooOld ? "RemasterPythonTooOld"
				: f.Python == PythonGate.Missing ? "RemasterNeedsPython"
				: "RemasterNeedsTools";
			BannerText = ResourceHelper.GetMessage(banner, f.PythonVersion);
			BannerDetail = ResourceHelper.GetMessage(banner + "Detail");
			BannerLine = BannerText + " " + BannerDetail;

			ProjectName = _project?.Name ?? "";
			ProjectFolder = _project?.Folder ?? "";
			int count = _project?.Recordings.Count ?? 0;
			RecordSummary = count == 0 ? ResourceHelper.GetMessage("RemasterRecordingsNone")
				: count == 1 ? ResourceHelper.GetMessage("RemasterRecordingsOne")
				: ResourceHelper.GetMessage("RemasterRecordingsMany", count);
			if(_project != null && _project.Problem.Length > 0) {
				RecordSummary += " · " + _project.Problem;
			}
			Recordings = (_project?.Recordings ?? Array.Empty<RemasterRecording>()).Reverse().Select(RemasterRecordingRow.From).ToList();
			RefreshShapesSeen();

			Record = Control(s.Record);
			RecordFromTas = Control(s.RecordFromTas);
			LetTheAiPlay = Control(s.LetTheAiPlay);
			PrepareFigures = Control(s.PrepareFigures);
			BuildAndShow = Control(s.BuildAndShow);
			PaintText = ResourceHelper.GetMessage(_project?.HasKit == true ? "RemasterKitReady" : "RemasterKitNotYet");

			RefreshTiles();
			RefreshImportControl();
			RefreshCompose();

			if(IsRecording) {
				RecordingPill = ResourceHelper.GetMessage("RemasterRecordingPill", RemasterScreen.FormatElapsed(_recordingClock.Elapsed));
			}
			RefreshBuild();

			RemasterActivity activity = RemasterActivityIndicator.Of(IsRecording, _jobs.Snapshot.IsRunning);
			Activity = activity;
			ActivityChanged?.Invoke();
		}

		//ADR-0252 §1: the shapes the recordings drew. The count parses every
		//recording's hires.txt, so it runs off the UI thread; a result for an
		//older refresh is dropped. The newest recording stands in while the
		//count is unknown or pending.
		//The recordings' shapes count; a test swaps it to hold the read open.
		public Func<IReadOnlyList<RemasterRecording>, int?> CountShapes { get; set; }

		private readonly RemasterScanWait _scans = new();
		private int _shapesGeneration;
		private string _shapesFolder = "";

		//Completes when the shapes count the last refresh asked for is in.
		public Task ShapesSettled { get; private set; } = Task.CompletedTask;

		private void RefreshShapesSeen()
		{
			int generation = ++_shapesGeneration;
			var project = _project;
			string folder = project?.Folder ?? "";
			if(folder != _shapesFolder) {
				_shapesFolder = folder;
				ShapesSeenText = "";
			}
			UpdateRecordDetail();
			if(project == null) {
				_scans.Cancel(RemasterScanKind.Shapes);
				UpdateScanWait();
				ShapesSettled = Task.CompletedTask;
				return;
			}
			List<RemasterRecording> snapshot = project.Recordings.ToList();
			int scan = _scans.Begin(RemasterScanKind.Shapes);
			UpdateScanWait();
			TaskCompletionSource settled = new();
			ShapesSettled = settled.Task;
			Task.Run(() => CountShapes(snapshot)).ContinueWith(t => Dispatcher.UIThread.Post(() => {
				_scans.End(RemasterScanKind.Shapes, scan);
				UpdateScanWait();
				if(generation == _shapesGeneration) {
					int? shapes = t.IsCompletedSuccessfully ? t.Result : null;
					ShapesSeenText = shapes is int n ? ResourceHelper.GetMessage(n == 0 ? "RemasterShapesSeenNone" : n == 1 ? "RemasterShapesSeenOne" : "RemasterShapesSeenMany", n) : "";
					UpdateRecordDetail();
				}
				settled.TrySetResult();
			}), TaskScheduler.Default);
		}

		private void UpdateScanWait()
		{
			IsScanWaitVisible = _scans.IsWaiting;
			ScanWaitText = !IsScanWaitVisible ? ""
				: ResourceHelper.GetMessage(_scans.IsWaitingForProject ? "RemasterScanningProject" : "RemasterScanningProjects");
		}

		private void UpdateRecordDetail()
		{
			RemasterRecordingRow? latest = Recordings.FirstOrDefault();
			RecordDetail = ShapesSeenText.Length > 0 ? ShapesSeenText
				: latest == null ? "" : ResourceHelper.GetMessage("RemasterRecordingsLatest", latest.Detail.Length > 0 ? latest.Title + " · " + latest.Detail : latest.Title);
		}

		private static RemasterControlViewModel Control(RemasterControl c) => new(c.Enabled, c.Enabled ? "" : Reason(c.Reason));

		private static string Reason(RemasterReason reason) => reason == RemasterReason.None ? "" : ResourceHelper.GetMessage("RemasterReason" + reason);

		//The other profiles' status line while Remaster works (W-X3).
		public string ActivityStatus()
		{
			return Activity switch {
				RemasterActivity.Recording => ResourceHelper.GetMessage("ShellStatusRemasterRecording", _gameName),
				RemasterActivity.Job => ResourceHelper.GetMessage(_jobs.Snapshot.Kind == RemasterJobKind.Build ? "ShellStatusRemasterBuild" : JobMessage(_jobs.Snapshot.Kind, "ShellStatusRemasterJob"), _jobs.Snapshot.GameName, _jobs.Snapshot.Percent),
				_ => "",
			};
		}

		private sealed class NullLauncher : IJobProcessLauncher
		{
			public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
			{
				throw new InvalidOperationException("designer");
			}
		}
	}

	public sealed record RemasterControlViewModel(bool IsEnabled, string Reason)
	{
		public bool HasReason => Reason.Length > 0;
		public static RemasterControlViewModel Hidden { get; } = new(false, "");
	}

	//One row of W-R1's recordings list, newest first.
	public sealed record RemasterRecordingRow(string Id, string Title, string Detail)
	{
		public static RemasterRecordingRow From(RemasterRecording r)
		{
			List<string> parts = new();
			if(r.Source != null) {
				parts.Add(ResourceHelper.GetMessage("RemasterSource" + char.ToUpperInvariant(r.Source[0]) + r.Source.Substring(1)));
			}
			if(r.RecordedAtUtc is DateTime t) {
				parts.Add(t.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
			}
			if(r.DurationSeconds is double d && d > 0) {
				parts.Add(RemasterScreen.FormatElapsed(TimeSpan.FromSeconds(d)));
			}
			if(!r.HasTextures) {
				parts.Add(ResourceHelper.GetMessage("RemasterRecordingNoTiles"));
			}
			if(r.Note.Length > 0) {
				parts.Add(r.Note);
			}
			return new RemasterRecordingRow(r.Id, ResourceHelper.GetMessage("RemasterRecordingTitle", r.Number), string.Join(" · ", parts));
		}
	}
}
