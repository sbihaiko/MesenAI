using System;
using Mesen.Interop;

namespace Mesen.Logic;

//G.3 (PRD Part B §13.5.3 W-R0-W-R3, §13.3 rules 2, 4, 6, 10): which Remaster
//screen shows and why each control is enabled or not. The owning ViewModel
//maps every reason to its one-line sentence; a disabled control is never
//hidden (rule 4), it shows its reason.
public enum RemasterView
{
	//W-R0: no project for the running game (or no game) and none chosen.
	NoProject,
	//W-R1: the one project screen.
	Project,
	//W-R2: recording - the game fills the content area, one pill and Stop.
	Recording
}

public enum RemasterReason
{
	None,
	NoGame,
	ConsoleNotSupported,
	NotThisProjectsGame,
	JobRunning,
	RecordingRunning,
	NesOnly,
	NeedsPython,
	NeedsTools,
	NothingRecorded,
	TasNotInThisBuild,
	TasLater,
	AiNotReady,
	BuildLater
}

public sealed record RemasterControl(bool Enabled, RemasterReason Reason)
{
	public static RemasterControl On { get; } = new(true, RemasterReason.None);
	public static RemasterControl Off(RemasterReason reason) => new(false, reason);
}

public sealed record RemasterInputs(
	bool GameLoaded,
	ConsoleType Console,
	bool Recording,
	bool JobRunning,
	//The project on screen ("" = none) and whether it is the running game's own.
	string ProjectFolder,
	bool ProjectIsGames,
	int TexturedRecordings,
	RemasterFeasibility Feasibility,
	//headless_record ships in the macOS arm64 zip only (ADR-0243 Decision 6).
	bool HasHeadlessRecorder
);

public sealed record RemasterScreenState(
	RemasterView View,
	//W-R0's primary card: Start Recording, or "Open a ROM to Start" (rule 10).
	bool PrimaryOpensRom,
	RemasterControl Record,
	RemasterControl RecordFromTas,
	RemasterControl LetTheAiPlay,
	RemasterControl PrepareFigures,
	RemasterControl BuildAndShow,
	//W-R0b: shown inside W-R0/W-R1 until resolved.
	bool ShowFeasibilityBanner
);

public static class RemasterScreen
{
	//ADR-0243 Decision 5: NES, Game Boy (Color) and SMS record tiles; GBA has
	//no HD path. Sheets (figures, scenery, pattern pages) are NES only.
	public static bool CanRecord(ConsoleType console)
	{
		return console == ConsoleType.Nes || console == ConsoleType.Gameboy || console == ConsoleType.Sms;
	}

	public static RemasterScreenState Evaluate(RemasterInputs i)
	{
		RemasterView view = i.Recording ? RemasterView.Recording
			: string.IsNullOrEmpty(i.ProjectFolder) ? RemasterView.NoProject
			: RemasterView.Project;

		RemasterControl record = RecordControl(i);
		RemasterControl prepare = PrepareControl(i);
		bool canStartAny = !i.JobRunning && !i.Recording;

		//W-R1 notes: TAS is headless_record bootstrap movie=… run as a job; the
		//button is drawn (§13.8 Q2: stays visible) but the GUI path is not built
		//yet. Off the macOS arm64 zip the reason is the build, not the slice.
		RemasterControl tas = Off(i.HasHeadlessRecorder ? RemasterReason.TasLater : RemasterReason.TasNotInThisBuild);
		//ADR-0242 Q3: the AI recorder (F14.20) stays disabled until its adoption
		//clauses pass; it is not hidden.
		RemasterControl ai = Off(RemasterReason.AiNotReady);
		//W-R1 zone ③: the build/reload loop (mep_build, mep_lint,
		//RequestMepImageReload) is a later slice; this one runs the kit only.
		RemasterControl build = Off(RemasterReason.BuildLater);

		return new RemasterScreenState(
			view,
			PrimaryOpensRom: !i.GameLoaded,
			Record: record,
			RecordFromTas: tas,
			LetTheAiPlay: ai,
			PrepareFigures: canStartAny || !prepare.Enabled ? prepare : Off(i.JobRunning ? RemasterReason.JobRunning : RemasterReason.RecordingRunning),
			BuildAndShow: build,
			ShowFeasibilityBanner: !i.Feasibility.CanRunJobs && view != RemasterView.Recording
		);
	}

	private static RemasterControl Off(RemasterReason reason) => RemasterControl.Off(reason);

	private static RemasterControl RecordControl(RemasterInputs i)
	{
		if(i.Recording) {
			return Off(RemasterReason.RecordingRunning);
		}
		if(!i.GameLoaded) {
			return Off(RemasterReason.NoGame);
		}
		if(!CanRecord(i.Console)) {
			return Off(RemasterReason.ConsoleNotSupported);
		}
		if(!string.IsNullOrEmpty(i.ProjectFolder) && !i.ProjectIsGames) {
			return Off(RemasterReason.NotThisProjectsGame);
		}
		//W-R3: the three record buttons are disabled while a job runs.
		if(i.JobRunning) {
			return Off(RemasterReason.JobRunning);
		}
		return RemasterControl.On;
	}

	private static RemasterControl PrepareControl(RemasterInputs i)
	{
		if(string.IsNullOrEmpty(i.ProjectFolder) || i.TexturedRecordings <= 0) {
			return Off(RemasterReason.NothingRecorded);
		}
		//The kit needs the ROM for the pattern pages, and the project's ROM is
		//the running one only when the project is the game's own.
		if(!i.GameLoaded || !i.ProjectIsGames) {
			return Off(i.GameLoaded ? RemasterReason.NotThisProjectsGame : RemasterReason.NoGame);
		}
		if(i.Console != ConsoleType.Nes) {
			return Off(RemasterReason.NesOnly);
		}
		if(i.Feasibility.Python != PythonGate.Found) {
			return Off(RemasterReason.NeedsPython);
		}
		if(i.Feasibility.Tools != ToolsGate.Found) {
			return Off(RemasterReason.NeedsTools);
		}
		return RemasterControl.On;
	}

	//After Stop, the kit runs by itself (W-R2: "Esc or Stop ends the
	//recording, returns to W-R1 and runs the kit generators as a job") when it
	//can; otherwise W-R1 shows why zone ② is waiting.
	public static bool RunKitAfterRecording(RemasterInputs afterStop)
	{
		return PrepareControl(afterStop).Enabled && !afterStop.JobRunning;
	}

	//W-R2's pill: "Recording 01:42"; hours appear past 59:59.
	public static string FormatElapsed(TimeSpan elapsed)
	{
		if(elapsed < TimeSpan.Zero) {
			elapsed = TimeSpan.Zero;
		}
		return elapsed.TotalHours >= 1
			? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
			: $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
	}
}

//§13.6 / W-R2: switching profile never stops a recording or a job; while
//another profile is shown, the profile button carries a dot (red =
//recording, tint = job) and the status line names the work (W-X3).
public enum RemasterActivity
{
	None,
	Recording,
	Job
}

public static class RemasterActivityIndicator
{
	public static RemasterActivity Of(bool recording, bool jobRunning)
	{
		return recording ? RemasterActivity.Recording : jobRunning ? RemasterActivity.Job : RemasterActivity.None;
	}

	public static bool ShowsDot(Workspace active, RemasterActivity activity)
	{
		return active != Workspace.Remaster && activity != RemasterActivity.None;
	}
}
