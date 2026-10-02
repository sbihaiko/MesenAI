using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//G.3 (PRD Part B §13.5.3 W-R3, rule 6): a long Remaster step is a job - a
//child process with a progress card and a Stop button, in the workspace that
//started it, never a window or a terminal. The runner is host-free: it owns
//the state (running, step i of n, the result) and reads the child's output;
//spawning is behind IJobProcessLauncher (UI/Services/JobProcessLauncher.cs
//over System.Diagnostics.Process, a fake in UI.Tests).
public interface IJobProcess
{
	void Kill();
}

public interface IJobProcessLauncher
{
	//Starts argv[0] with argv[1..]. Each stdout/stderr line goes to onLine
	//(isError for stderr) and the exit code to onExit, once, after the last
	//line, on any thread. Throws when the program cannot be started.
	IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit);
}

public enum RemasterJobKind
{
	Kit
}

public enum RemasterJobStatus
{
	Idle,
	Running,
	Succeeded,
	Failed,
	Stopped
}

public sealed record RemasterJobSpec(RemasterJobKind Kind, IReadOnlyList<string> Argv, string WorkingDirectory, int TotalSteps, string ProjectFolder, string GameName);

public sealed record RemasterJobSnapshot(RemasterJobStatus Status, RemasterJobKind Kind, int StepsDone, int TotalSteps, string CurrentStep, string FailureLine, string GameName)
{
	public bool IsRunning => Status == RemasterJobStatus.Running;

	//0-100, for the card's bar and the other profiles' status line.
	public int Percent => TotalSteps <= 0 ? 0 : Math.Clamp(StepsDone * 100 / TotalSteps, 0, 100);

	public static RemasterJobSnapshot Idle { get; } = new(RemasterJobStatus.Idle, RemasterJobKind.Kit, 0, 0, "", "", "");
}

public enum RemasterJobStep
{
	None,
	Figures,
	Scenery,
	PatternPages,
	Assemble,
	Other
}

public static class RemasterJobs
{
	//The card names a step in plain words (rule 3), not by its script.
	public static RemasterJobStep StepOf(string tool)
	{
		return tool switch {
			"" => RemasterJobStep.None,
			"artist_kit.py" => RemasterJobStep.Figures,
			"artist_bg_kit.py" => RemasterJobStep.Scenery,
			"artist_chr_kit.py" => RemasterJobStep.PatternPages,
			"artist_kit_assemble.py" => RemasterJobStep.Assemble,
			_ => RemasterJobStep.Other,
		};
	}

	//`scripts/mep_project.py kit <project> --rom ROM` (ADR-0243 Q1, ADR-0183).
	//kit_plan runs artist_kit + artist_bg_kit per textured recording, one
	//artist_chr_kit for the union pattern pages, then one assemble per kit
	//folder (each recording's and pages/): 2n + 1 + (n + 1) = 3n + 2 steps.
	public static int KitSteps(int texturedRecordings) => texturedRecordings <= 0 ? 0 : 3 * texturedRecordings + 2;

	public static RemasterJobSpec Kit(PythonCandidate python, string toolsFolder, string projectFolder, string romPath, int texturedRecordings, string gameName)
	{
		List<string> argv = new() { python.Executable };
		argv.AddRange(python.PrefixArgs);
		argv.Add(System.IO.Path.Combine(toolsFolder, RemasterToolsLocator.EntryScript));
		argv.Add("kit");
		argv.Add(projectFolder);
		argv.Add("--rom");
		argv.Add(romPath);
		return new RemasterJobSpec(RemasterJobKind.Kit, argv, toolsFolder, KitSteps(texturedRecordings), projectFolder, gameName);
	}
}

public sealed class RemasterJobRunner
{
	private readonly IJobProcessLauncher _launcher;
	private readonly object _lock = new();
	private IJobProcess? _process;
	private RemasterJobSpec? _spec;
	private RemasterJobSnapshot _snapshot = RemasterJobSnapshot.Idle;
	private bool _stopRequested;
	private string _lastError = "";
	private int _generation;

	//Raised on every state change, on whatever thread the change happened.
	public event Action<RemasterJobSnapshot>? Changed;

	public RemasterJobRunner(IJobProcessLauncher launcher)
	{
		_launcher = launcher;
	}

	public RemasterJobSnapshot Snapshot
	{
		get { lock(_lock) { return _snapshot; } }
	}

	//False (and nothing started) while a job already runs: one job at a time
	//per workspace.
	public bool Start(RemasterJobSpec spec)
	{
		int generation;
		lock(_lock) {
			if(_snapshot.IsRunning) {
				return false;
			}
			_spec = spec;
			_stopRequested = false;
			_lastError = "";
			generation = ++_generation;
			_snapshot = new RemasterJobSnapshot(RemasterJobStatus.Running, spec.Kind, 0, spec.TotalSteps, "", "", spec.GameName);
		}
		Raise();
		try {
			IJobProcess process = _launcher.Start(spec.Argv, spec.WorkingDirectory,
				(line, isError) => OnLine(generation, line, isError),
				code => OnExit(generation, code));
			lock(_lock) {
				if(generation == _generation && _snapshot.IsRunning) {
					_process = process;
				}
			}
		} catch(Exception ex) {
			Finish(generation, RemasterJobStatus.Failed, ex.Message);
		}
		return true;
	}

	//Stop on the card: the child is killed and its partial output is not a
	//result (§13.5.5: a job re-runs from the project).
	public void Stop()
	{
		IJobProcess? process;
		lock(_lock) {
			if(!_snapshot.IsRunning) {
				return;
			}
			_stopRequested = true;
			process = _process;
		}
		process?.Kill();
	}

	//The card's result line is dismissed (or the success line timed out).
	public void Clear()
	{
		lock(_lock) {
			if(_snapshot.IsRunning) {
				return;
			}
			_snapshot = RemasterJobSnapshot.Idle;
		}
		Raise();
	}

	private void OnLine(int generation, string line, bool isError)
	{
		lock(_lock) {
			if(generation != _generation || !_snapshot.IsRunning || line == null) {
				return;
			}
			RemasterJobSnapshot s = _snapshot;
			if(isError) {
				if(line.Trim().Length > 0) {
					_lastError = line.Trim();
				}
				return;
			}
			//mep_project.cmd_kit prints "== <tool> -> <kit>" before a step and
			//"ok   <tool> -> <kit>" / "FAIL <tool> -> <kit>" after it.
			if(line.StartsWith("== ", StringComparison.Ordinal)) {
				_snapshot = s with { CurrentStep = StepName(line.Substring(3)) };
			} else if(line.StartsWith("ok ", StringComparison.Ordinal) || line.StartsWith("FAIL ", StringComparison.Ordinal)) {
				bool failed = line.StartsWith("FAIL ", StringComparison.Ordinal);
				_snapshot = s with {
					StepsDone = s.StepsDone + 1,
					FailureLine = failed && s.FailureLine.Length == 0 ? line.Trim() : s.FailureLine
				};
			} else {
				return;
			}
		}
		Raise();
	}

	private void OnExit(int generation, int code)
	{
		RemasterJobStatus status;
		string failure;
		lock(_lock) {
			if(generation != _generation || !_snapshot.IsRunning) {
				return;
			}
			status = _stopRequested ? RemasterJobStatus.Stopped : code == 0 ? RemasterJobStatus.Succeeded : RemasterJobStatus.Failed;
			failure = _snapshot.FailureLine.Length > 0 ? _snapshot.FailureLine : _lastError;
			if(status == RemasterJobStatus.Failed && failure.Length == 0) {
				failure = "exit code " + code;
			}
		}
		Finish(generation, status, failure);
	}

	private void Finish(int generation, RemasterJobStatus status, string failure)
	{
		lock(_lock) {
			if(generation != _generation || !_snapshot.IsRunning) {
				return;
			}
			_process = null;
			RemasterJobSnapshot s = _snapshot;
			_snapshot = s with {
				Status = status,
				StepsDone = status == RemasterJobStatus.Succeeded ? s.TotalSteps : s.StepsDone,
				FailureLine = status == RemasterJobStatus.Failed ? failure : ""
			};
		}
		Raise();
	}

	//"artist_kit.py -> /x/kit/rec-001" -> "artist_kit.py"
	private static string StepName(string rest)
	{
		int arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
		return (arrow >= 0 ? rest.Substring(0, arrow) : rest).Trim();
	}

	private void Raise()
	{
		Changed?.Invoke(Snapshot);
	}
}
