using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Mesen.Logic;

//G.3 (PRD Part B §13.4 "Feasibility gate for Remaster jobs", W-R0b; ADR-0243
//Decision 6): the kit and the build are Python under scripts/ (ADR-0165), run
//by the user's own python3 (ADR-0242 Q4: no bundled runtime). The gate is
//measured once per session and shown as a banner, never a modal. Two things
//must be found: an interpreter new enough, and the tools themselves - the app
//bundle ships no scripts/, they come in the separate tools zip
//(scripts/tools-zip-manifest.txt), or from a checkout.
public enum PythonGate
{
	Found,
	Missing,
	TooOld
}

public enum ToolsGate
{
	Found,
	Missing
}

public sealed record PythonCandidate(string Executable, IReadOnlyList<string> PrefixArgs);

public sealed record RemasterFeasibility(PythonGate Python, string PythonExecutable, IReadOnlyList<string> PythonPrefixArgs, string PythonVersion, ToolsGate Tools, string ToolsFolder)
{
	public bool CanRunJobs => Python == PythonGate.Found && Tools == ToolsGate.Found;
}

//Asks one interpreter for its version, "3.12" style, or null when it does not
//run (or is not Python). Implemented over a child process in UI/Services; a
//fake in the tests.
public interface IPythonProbe
{
	string? Version(PythonCandidate candidate);
}

public static class PythonLocator
{
	//Measured 2026-10-02: the kit generators use PEP 604 annotations
	//(`-> int | None` in artist_chr_kit.py), which Python 3.9 (the macOS
	//Command Line Tools' python3) rejects at import with "unsupported operand
	//type(s) for |". 3.10 is the oldest that runs them.
	public const int MinMajor = 3;
	public const int MinMinor = 10;

	public static bool IsRecentEnough(string version)
	{
		string[] parts = (version ?? "").Trim().Split('.');
		if(parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
			!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)) {
			return false;
		}
		return major > MinMajor || (major == MinMajor && minor >= MinMinor);
	}

	//Candidates in the order they are tried. An app opened from Finder or
	//Explorer does not see the shell's PATH, so the usual install locations
	//come first. Two stubs are skipped on purpose: macOS's /usr/bin/python3
	//opens an "install developer tools" dialog when the tools are absent (the
	//Command Line Tools' own python3 is tried by its real path instead), and
	//Windows' WindowsApps python.exe opens the Store.
	public static IReadOnlyList<PythonCandidate> Candidates(string configured, string pathVariable, bool isWindows, bool isMacOS, Func<string, bool> fileExists)
	{
		List<PythonCandidate> list = new();
		HashSet<string> seen = new(StringComparer.Ordinal);
		void Add(string exe, params string[] prefix)
		{
			if(!string.IsNullOrWhiteSpace(exe) && seen.Add(exe + " " + string.Join(" ", prefix)) && (prefix.Length > 0 || !Path.IsPathRooted(exe) || fileExists(exe))) {
				list.Add(new PythonCandidate(exe, prefix));
			}
		}

		if(!string.IsNullOrWhiteSpace(configured)) {
			Add(configured);
		}
		if(isMacOS) {
			Add("/opt/homebrew/bin/python3");
			Add("/usr/local/bin/python3");
			Add("/Library/Frameworks/Python.framework/Versions/Current/bin/python3");
		}
		char separator = isWindows ? ';' : ':';
		string exeName = isWindows ? "python.exe" : "python3";
		foreach(string dir in (pathVariable ?? "").Split(separator, StringSplitOptions.RemoveEmptyEntries)) {
			string trimmed = dir.Trim().Trim('"');
			if(isMacOS && (trimmed == "/usr/bin" || trimmed == "/usr/bin/")) {
				continue;
			}
			if(isWindows && trimmed.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			Add(Path.Combine(trimmed, exeName));
		}
		if(isWindows) {
			//The py launcher, when installed, lives in C:\Windows and is on PATH
			Add("py", "-3");
		}
		if(isMacOS) {
			Add("/Library/Developer/CommandLineTools/usr/bin/python3");
		}
		return list;
	}

	//The first candidate recent enough; else the newest too-old one found
	//(TooOld names its version); else Missing.
	public static (PythonGate Gate, PythonCandidate? Candidate, string Version) Locate(IReadOnlyList<PythonCandidate> candidates, IPythonProbe probe)
	{
		PythonCandidate? old = null;
		string oldVersion = "";
		foreach(PythonCandidate c in candidates) {
			string? version = probe.Version(c);
			if(string.IsNullOrEmpty(version)) {
				continue;
			}
			if(IsRecentEnough(version)) {
				return (PythonGate.Found, c, version);
			}
			if(old == null) {
				old = c;
				oldVersion = version;
			}
		}
		return old != null ? (PythonGate.TooOld, old, oldVersion) : (PythonGate.Missing, null, "");
	}
}

public static class RemasterToolsLocator
{
	public const string EntryScript = "mep_project.py";

	//A folder holds the tools when mep_project.py is in it or in its scripts/
	//(the tools zip unpacks to <folder>/scripts/, a checkout has scripts/ at
	//its root). Returns the folder that holds the script, or "".
	public static string Resolve(string folder, Func<string, bool> fileExists)
	{
		if(string.IsNullOrWhiteSpace(folder)) {
			return "";
		}
		if(fileExists(Path.Combine(folder, EntryScript))) {
			return folder;
		}
		string scripts = Path.Combine(folder, "scripts");
		return fileExists(Path.Combine(scripts, EntryScript)) ? scripts : "";
	}

	//The configured folder first; then every ancestor of the app's folder
	//(a checkout's bin/ sits under the repo root; a tools zip unpacked next to
	//the app sits beside it as mesenai-tools*/).
	public static string Locate(string configured, string appFolder, Func<string, bool> fileExists, Func<string, IEnumerable<string>> childFolders)
	{
		string found = Resolve(configured, fileExists);
		if(found.Length > 0) {
			return found;
		}
		DirectoryInfo? dir = string.IsNullOrEmpty(appFolder) ? null : new DirectoryInfo(appFolder);
		for(int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent) {
			found = Resolve(dir.FullName, fileExists);
			if(found.Length > 0) {
				return found;
			}
			foreach(string child in childFolders(dir.FullName)) {
				if(Path.GetFileName(child).StartsWith("mesenai-tools", StringComparison.OrdinalIgnoreCase)) {
					found = Resolve(child, fileExists);
					if(found.Length > 0) {
						return found;
					}
				}
			}
		}
		return "";
	}
}
