using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mesen.ViewModels
{
	//G.6 (PRD Part B §13.5.3 W-R1 zone ③, W-R3, W-R4): Build & show in game,
	//its problems, and the game view that shows the result. The rules are
	//host-free in UI/Logic/RemasterBuild*.cs; this half maps them to strings
	//and calls the core. Kept in its own file so the workspace VM stays the
	//G.3 shape (G.7 edits it in parallel).
	public partial class RemasterWorkspaceViewModel
	{
		//W-R4
		[ObservableProperty] public partial bool IsBuildProblemsVisible { get; private set; }
		[ObservableProperty] public partial string BuildProblemsTitle { get; private set; } = "";
		[ObservableProperty] public partial List<RemasterProblemRow> BuildProblems { get; private set; } = new();
		[ObservableProperty] public partial string BuildProblemsNote { get; private set; } = "";
		[ObservableProperty] public partial bool IsBuildLogVisible { get; private set; }
		[ObservableProperty] public partial string BuildLogText { get; private set; } = "";

		//W-R1 zone ③ at rest: "2 files changed since the last build."
		[ObservableProperty] public partial string BuildFreshness { get; private set; } = "";
		//The Build button's row: neither the job card nor W-R4 is showing.
		[ObservableProperty] public partial bool IsBuildAtRest { get; private set; } = true;

		//The game view after a build (W-R1 notes: Remaster's game view, the
		//strip on top, Esc back to the project). ShowsGame covers W-R2 too.
		[ObservableProperty] public partial bool IsShowingBuild { get; private set; }
		[ObservableProperty] public partial bool ShowsGame { get; private set; }
		[ObservableProperty] public partial string ShowingPill { get; private set; } = "";

		//What "show in game" does to the running game. Replaced by tests, which
		//run without the core.
		public Func<RemasterShowAction, bool> ShowInGame { get; set; } = DefaultShowInGame;

		//#649: ADR-0244's plan for a pack change right now (a movie, a shared
		//replay or netplay make it a restart). Replaced by tests.
		public Func<PackChangePlan> PlanPackChange { get; set; } = () => LoadRomHelper.PlanPackChange(ConsoleType.Nes);

		private string _buildProject = "";
		private int _buildSerial;
		private int _handledBuildSerial;

		//Zone ③ › Build & Show in Game, and W-R4 › Try Again.
		public bool StartBuild()
		{
			if(!Evaluate().BuildAndShow.Enabled || _project == null || _feasibility == null) {
				return false;
			}
			PythonCandidate python = new(_feasibility.PythonExecutable, _feasibility.PythonPrefixArgs);
			RemasterJobSpec spec = RemasterBuilds.Spec(python, _feasibility.ToolsFolder, _project.Folder, _romPath, _gameName);
			_buildProject = _project.Folder;
			_buildSerial++;
			HideBuildProblems();
			_jobResultTimer?.Stop();
			bool started = _jobs.Start(spec);
			OnJobChanged();
			return started;
		}

		public void ToggleBuildLog()
		{
			IsBuildLogVisible = !IsBuildLogVisible;
			BuildLogText = IsBuildLogVisible ? string.Join(Environment.NewLine, _jobs.Log) : "";
		}

		//Esc or Back to Project in the game view: W-R2 stops its recording
		//(running the kit); the build view just returns to W-R1.
		public void LeaveGameView()
		{
			if(IsRecording) {
				StopRecording();
				return;
			}
			if(IsShowingBuild) {
				IsShowingBuild = false;
				Refresh();
			}
		}

		private void HideBuildProblems()
		{
			IsBuildProblemsVisible = false;
			IsBuildLogVisible = false;
			BuildLogText = "";
			BuildProblems = new();
			BuildProblemsNote = "";
		}

		//OnJobChanged's Build half: the running card's title, then - once per
		//build - the result shown in the game or as W-R4.
		private string BuildJobTitle(RemasterJobSnapshot job)
		{
			switch(job.Status) {
				case RemasterJobStatus.Running:
					return ResourceHelper.GetMessage("RemasterJobBuildTitle");
				case RemasterJobStatus.Succeeded:
					return _handledBuildSerial == _buildSerial ? JobTitle : ShowBuild();
				case RemasterJobStatus.Failed:
					if(_handledBuildSerial != _buildSerial) {
						_handledBuildSerial = _buildSerial;
						ShowBuildProblems(job);
					}
					return ResourceHelper.GetMessage("RemasterJobBuildFailed");
				case RemasterJobStatus.Stopped:
					return ResourceHelper.GetMessage("RemasterJobBuildStopped");
				default:
					return "";
			}
		}

		private string ShowBuild()
		{
			_handledBuildSerial = _buildSerial;
			RemasterBuildOutcome outcome = RemasterBuildOutcome.Parse(_jobs.Log);
			RemasterShowAction action = RemasterShow.Decide(outcome, _gameLoaded && IsGamesProject(_buildProject), _console == ConsoleType.Nes);
			if(action == RemasterShowAction.ReloadPack) {
				//#649: never a restart the player did not ask for (ADR-0244 §2).
				action = RemasterShow.PackReload(PlanPackChange());
			}
			bool shown = action != RemasterShowAction.None && ShowInGame(action);
			if(!shown && action == RemasterShowAction.ReloadImages) {
				//The image reload had no NES console to talk to: reload the pack.
				action = RemasterShow.PackReload(PlanPackChange());
				shown = action != RemasterShowAction.None && ShowInGame(action);
			}
			if(!shown) {
				return ResourceHelper.GetMessage("RemasterJobBuiltNotShown", _jobs.Snapshot.GameName);
			}
			IsShowingBuild = true;
			ShowingPill = ResourceHelper.GetMessage("RemasterJobBuiltShowing");
			return ShowingPill;
		}

		private void ShowBuildProblems(RemasterJobSnapshot job)
		{
			RemasterBuildOutcome outcome = RemasterBuildOutcome.Parse(_jobs.Log);
			RemasterBuildProblems read = RemasterBuildProblemReader.Read(_jobs.Log, RemasterKitIndex.Load(_buildProject, outcome.RecordingId));
			BuildProblems = read.Problems.Select(RemasterProblemRow.From).ToList();
			BuildProblemsTitle = read.Count == 0 ? ResourceHelper.GetMessage("RemasterProblemsNone")
				: read.Count == 1 ? ResourceHelper.GetMessage("RemasterProblemsOne")
				: ResourceHelper.GetMessage("RemasterProblemsMany", read.Count);
			BuildProblemsNote = read.Untranslated == 0 ? ""
				: read.Problems.Count == 0 ? ResourceHelper.GetMessage("RemasterProblemsOnlyInLog")
				: ResourceHelper.GetMessage("RemasterProblemsMoreInLog", read.Untranslated);
			IsBuildLogVisible = false;
			BuildLogText = "";
			IsBuildProblemsVisible = true;
		}

		//Refresh's Build half.
		private void RefreshBuild()
		{
			//W-R4 lasts while the failed build is the runner's last job: OK on
			//it, Try Again, or the kit run after a recording replaces it.
			RemasterJobSnapshot job = _jobs.Snapshot;
			if(IsBuildProblemsVisible && !(job.Kind == RemasterJobKind.Build && job.Status == RemasterJobStatus.Failed)) {
				HideBuildProblems();
			}
			if(IsBuildProblemsVisible) {
				IsJobCardVisible = false;
			}
			IsBuildAtRest = !IsJobCardVisible && !IsBuildProblemsVisible;
			ShowsGame = IsRecording || IsShowingBuild;
			int? changed = _project == null ? null : RemasterBuildFreshness.ChangedSinceLastBuild(_project.Folder, LatestTexturedRecording());
			BuildFreshness = _project == null || !_project.HasKit ? ""
				: changed == null ? ResourceHelper.GetMessage("RemasterBuildNever")
				: changed == 0 ? ResourceHelper.GetMessage("RemasterBuildUpToDate")
				: changed == 1 ? ResourceHelper.GetMessage("RemasterBuildChangedOne")
				: ResourceHelper.GetMessage("RemasterBuildChangedMany", changed.Value);
		}

		private string LatestTexturedRecording()
		{
			return _project?.Recordings.LastOrDefault(r => r.HasTextures)?.Id ?? "";
		}

		//The recording number the core is writing, for W-X3's "kept as recording 3".
		public int CurrentRecordingNumber()
		{
			return IsRecording ? RemasterProjectReader.RecordingNumber(Path.GetFileName(EmuApi.GetMepRecordingFolder().TrimEnd('/', '\\'))) : 0;
		}

		private static bool DefaultShowInGame(RemasterShowAction action)
		{
			switch(action) {
				case RemasterShowAction.ReloadImages:
					//ADR-0212: re-decode the images whose size or mtime changed, in place.
					return EmuApi.RequestMepImageReload();
				case RemasterShowAction.ReloadPack:
					//The manifest changed: ADR-0244's pack change keeps the
					//player's place (ShowBuild asked only when the plan is in
					//place). A movie or netplay that started since makes the core
					//refuse; a build never restarts the game itself (#649), so
					//then it plays next time.
					LoadRomHelper.ApplyPackChange(ConsoleType.Nes, () => { });
					return true;
				default:
					return false;
			}
		}
	}

	//One W-R4 row: the sentence and the kit file Open File opens ("" = none).
	public sealed record RemasterProblemRow(string Text, string FilePath)
	{
		public bool CanOpen => FilePath.Length > 0;

		public static RemasterProblemRow From(RemasterBuildProblem p)
		{
			string text = p.Kind switch {
				RemasterProblemKind.CanvasResized when p.Detail.Length > 0 => ResourceHelper.GetMessage("RemasterProblemCanvasResizedWas", p.Caption, p.Detail),
				RemasterProblemKind.ScaleMismatch => ResourceHelper.GetMessage("RemasterProblemScaleMismatch", p.Caption, Part(p.Detail, 0), Part(p.Detail, 1)),
				_ => ResourceHelper.GetMessage("RemasterProblem" + p.Kind, p.Caption),
			};
			return new RemasterProblemRow(text, p.FilePath);
		}

		private static string Part(string detail, int index)
		{
			string[] parts = detail.Split('|');
			return index < parts.Length ? parts[index] : "";
		}
	}
}
