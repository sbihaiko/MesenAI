using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Mesen.Services
{
	//G.3 (PRD Part B §13.4, §13.5.3 W-R0b/W-R3): the stateful half of the
	//Remaster jobs - real child processes behind the host-free
	//IJobProcessLauncher / IPythonProbe (UI/Logic/RemasterJob.cs,
	//RemasterFeasibility.cs). Arguments go through ArgumentList, never a joined
	//command line, so a project path with spaces or quotes stays one argument.
	public sealed class JobProcessLauncher : IJobProcessLauncher
	{
		private sealed class RunningJob : IJobProcess
		{
			private readonly Process _process;

			public RunningJob(Process process)
			{
				_process = process;
			}

			public void Kill()
			{
				try {
					//The kit is a tree (mep_project.py runs each generator as its
					//own child), so the whole tree goes.
					_process.Kill(entireProcessTree: true);
				} catch(InvalidOperationException) {
					//Already exited
				}
			}
		}

		public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
		{
			ProcessStartInfo info = new() {
				FileName = argv[0],
				WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : "",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			foreach(string arg in argv.Skip(1)) {
				info.ArgumentList.Add(arg);
			}
			//Line-buffered progress: the generators print as they go.
			info.Environment["PYTHONUNBUFFERED"] = "1";
			info.Environment["PYTHONIOENCODING"] = "utf-8";

			Process process = new() { StartInfo = info, EnableRaisingEvents = true };
			process.OutputDataReceived += (_, e) => { if(e.Data != null) onLine(e.Data, false); };
			process.ErrorDataReceived += (_, e) => { if(e.Data != null) onLine(e.Data, true); };
			process.Exited += (_, _) => {
				//WaitForExit() (no timeout) drains the redirected streams first,
				//so the exit code is reported after the last line.
				process.WaitForExit();
				int code = process.ExitCode;
				process.Dispose();
				onExit(code);
			};
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			return new RunningJob(process);
		}
	}

	public sealed class PythonProbe : IPythonProbe
	{
		public string? Version(PythonCandidate candidate)
		{
			try {
				ProcessStartInfo info = new() {
					FileName = candidate.Executable,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				};
				foreach(string arg in candidate.PrefixArgs) {
					info.ArgumentList.Add(arg);
				}
				info.ArgumentList.Add("-c");
				info.ArgumentList.Add("import sys; print('%d.%d' % sys.version_info[:2])");
				using Process? process = Process.Start(info);
				if(process == null) {
					return null;
				}
				if(!process.WaitForExit(5000)) {
					try {
						process.Kill(entireProcessTree: true);
					} catch(InvalidOperationException) {
					}
					return null;
				}
				string output = process.StandardOutput.ReadToEnd().Trim();
				return process.ExitCode == 0 && output.Length > 0 ? output : null;
			} catch(Exception ex) when(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is IOException) {
				return null;
			}
		}
	}

	public static class RemasterFeasibilityProbe
	{
		//Measured once per session (and again after Locate…): spawning a few
		//`python -c` children is cheap, but not something to do per repaint.
		public static RemasterFeasibility Measure(string configuredPython, string configuredTools)
		{
			IReadOnlyList<PythonCandidate> candidates = PythonLocator.Candidates(configuredPython, Environment.GetEnvironmentVariable("PATH") ?? "",
				OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), File.Exists);
			(PythonGate gate, PythonCandidate? found, string version) = PythonLocator.Locate(candidates, new PythonProbe());
			string tools = RemasterToolsLocator.Locate(configuredTools, AppContext.BaseDirectory, File.Exists,
				dir => Directory.Exists(dir) ? Directory.EnumerateDirectories(dir) : Array.Empty<string>());
			return new RemasterFeasibility(gate, found?.Executable ?? "", found?.PrefixArgs ?? Array.Empty<string>(), version,
				tools.Length > 0 ? ToolsGate.Found : ToolsGate.Missing, tools);
		}

		//"Show Project Folder": the OS file manager on the folder itself.
		public static void ShowFolder(string folder)
		{
			if(!Directory.Exists(folder)) {
				return;
			}
			ProcessStartInfo info = new() { UseShellExecute = OperatingSystem.IsWindows(), CreateNoWindow = true };
			if(OperatingSystem.IsWindows()) {
				info.FileName = folder;
			} else {
				info.FileName = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
				info.ArgumentList.Add(folder);
			}
			try {
				using Process? _ = Process.Start(info);
			} catch(Exception ex) when(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) {
			}
		}
	}
}
