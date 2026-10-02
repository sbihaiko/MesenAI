using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Mesen.Logic;

//G.7 (PRD Part B §13.5.3 W-R6, W-R7): the two hand-offs to external tools -
//making a finished pack editable (`scripts/mep_import.py`, ADR-0198, run as a
//job) and opening the scene composer (`scripts/compose_editor.py`, ADR-0165,
//its own window, never embedded). Host-free: which folder is what, whether
//each button can work and why not, and the argv. Spawning is the owner's.
public enum RemasterFolderKind
{
	//ADR-0243 Decision 1: auto/ or the .bootstrap stamp.
	Project,
	//A finished pack: hires.txt at its root or under textures/ (mep_import's
	//open_pack), and not a project.
	FinishedPack,
	Neither
}

public enum RemasterHandOffReason
{
	None,
	NeedsPython,
	NeedsTools,
	//The tools folder predates this tool (an older mesenai-tools zip).
	ToolMissing,
	JobRunning,
	RecordingRunning,
	NothingRecorded,
	//W-R7: the newest recording has no adjacency.json.
	NoLayoutData,
	//ADR-0198 §3: a patched pack is imported against the stock ROM the
	//<patch> line was made for, so that game must be the one running.
	NeedsStockRom,
	//W-R7: the composer this screen opened is still open.
	ComposerOpen
}

public sealed record RemasterHandOffControl(bool Enabled, RemasterHandOffReason Reason)
{
	public static RemasterHandOffControl On { get; } = new(true, RemasterHandOffReason.None);
	public static RemasterHandOffControl Off(RemasterHandOffReason reason) => new(false, reason);
}

//One refusal of mep_import.py, in the W-R4 shape: a plain sentence, and the
//manifest line it stopped at when it cited one.
public sealed record RemasterImportRefusal(string Sentence, string File, int Line);

public static class RemasterHandOff
{
	public const string ImportScript = "mep_import.py";
	public const string ComposeScript = "compose_editor.py";

	public static RemasterFolderKind Classify(string folder)
	{
		if(RemasterProjectReader.IsProjectFolder(folder)) {
			return RemasterFolderKind.Project;
		}
		return FinishedPackManifest(folder).Length > 0 ? RemasterFolderKind.FinishedPack : RemasterFolderKind.Neither;
	}

	public static string FinishedPackManifest(string folder)
	{
		if(string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) {
			return "";
		}
		foreach(string candidate in new[] { Path.Combine(folder, "hires.txt"), Path.Combine(folder, "textures", "hires.txt") }) {
			if(File.Exists(candidate)) {
				return candidate;
			}
		}
		return "";
	}

	//The ⚠ paragraph of W-R6: only when the pack ships a <patch> line.
	public static bool IsPatchedPack(string packFolder) => RemasterProvenance.HasPatchLine(FinishedPackManifest(packFolder));

	//The project is written next to the user's copy (ADR-0198 Consequences),
	//never into it: `<pack> (editable)`, then ` 2`, ` 3`… while one exists.
	public static string ImportDestination(string packFolder, Func<string, bool> exists)
	{
		string trimmed = packFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string parent = Path.GetDirectoryName(trimmed) ?? "";
		string name = Path.GetFileName(trimmed) + " (editable)";
		string candidate = Path.Combine(parent, name);
		for(int n = 2; exists(candidate) && n < 1000; n++) {
			candidate = Path.Combine(parent, name + " " + n);
		}
		return candidate;
	}

	public static RemasterHandOffControl ImportControl(RemasterFeasibility feasibility, Func<string, bool> fileExists, bool jobRunning, bool recording, bool patched, bool gameLoaded)
	{
		RemasterHandOffControl tools = ToolsControl(feasibility, fileExists, ImportScript);
		if(!tools.Enabled) {
			return tools;
		}
		if(recording) {
			return RemasterHandOffControl.Off(RemasterHandOffReason.RecordingRunning);
		}
		if(jobRunning) {
			return RemasterHandOffControl.Off(RemasterHandOffReason.JobRunning);
		}
		if(patched && !gameLoaded) {
			return RemasterHandOffControl.Off(RemasterHandOffReason.NeedsStockRom);
		}
		return RemasterHandOffControl.On;
	}

	//`mep_import.py import <pack> --out <project> [--rom <stock dump>]`; the
	//ROM goes only to a patched pack, the one case mep_import needs it.
	public static RemasterJobSpec ImportJob(RemasterFeasibility feasibility, string packFolder, string destination, string romPath, bool patched, string gameName)
	{
		List<string> argv = new() { feasibility.PythonExecutable };
		argv.AddRange(feasibility.PythonPrefixArgs);
		argv.Add(Path.Combine(feasibility.ToolsFolder, ImportScript));
		argv.Add("import");
		argv.Add(packFolder);
		argv.Add("--out");
		argv.Add(destination);
		if(patched && !string.IsNullOrEmpty(romPath)) {
			argv.Add("--rom");
			argv.Add(romPath);
		}
		return new RemasterJobSpec(RemasterJobKind.Import, argv, feasibility.ToolsFolder, 0, destination, gameName);
	}

	private static readonly Regex CitedLine = new(@"^(?:error:\s*)?(?<file>.+?\.txt):(?<line>\d+):\s*(?<text>.+)$", RegexOptions.CultureInvariant);

	//mep_import prints one `error: <manifest>:<line>: <why>` and stops (exit 2);
	//a refusal without a line is still one sentence.
	public static RemasterImportRefusal Refusal(string failureLine)
	{
		string text = (failureLine ?? "").Trim();
		Match m = CitedLine.Match(text);
		if(m.Success && int.TryParse(m.Groups["line"].Value, out int line)) {
			return new RemasterImportRefusal(m.Groups["text"].Value.Trim(), m.Groups["file"].Value, line);
		}
		if(text.StartsWith("error:", StringComparison.Ordinal)) {
			text = text.Substring(6).Trim();
		}
		return new RemasterImportRefusal(text, "", 0);
	}

	//*Show line*: the manifest line a refusal cites, as the file has it.
	public static string CitedText(RemasterImportRefusal refusal)
	{
		if(refusal.Line <= 0 || !File.Exists(refusal.File)) {
			return "";
		}
		try {
			int n = 0;
			foreach(string line in File.ReadLines(refusal.File)) {
				if(++n == refusal.Line) {
					return line;
				}
			}
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
		}
		return "";
	}

	//W-R7: the composer opens a pack folder with textures/sheets/ and its
	//adjacency.json (compose_engine.Pack) - the newest recording holding
	//textures, the one the loader plays and the build keys from (ADR-0243).
	public static RemasterRecording? ComposeRecording(RemasterProjectInfo? project)
	{
		if(project == null) {
			return null;
		}
		for(int i = project.Recordings.Count - 1; i >= 0; i--) {
			if(project.Recordings[i].HasTextures) {
				return project.Recordings[i];
			}
		}
		return null;
	}

	public static string AdjacencyFile(RemasterRecording recording) => Path.Combine(recording.Path, "textures", "sheets", "adjacency.json");

	public static RemasterHandOffControl ComposeControl(RemasterProjectInfo? project, RemasterFeasibility feasibility, Func<string, bool> fileExists, bool recording)
	{
		RemasterRecording? rec = ComposeRecording(project);
		if(recording) {
			return RemasterHandOffControl.Off(RemasterHandOffReason.RecordingRunning);
		}
		if(rec == null) {
			return RemasterHandOffControl.Off(RemasterHandOffReason.NothingRecorded);
		}
		if(!fileExists(AdjacencyFile(rec))) {
			return RemasterHandOffControl.Off(RemasterHandOffReason.NoLayoutData);
		}
		return ToolsControl(feasibility, fileExists, ComposeScript);
	}

	//`compose_editor.py <recording> [--rom <ROM>]`; --rom only matters for an
	//overflow layer on a CHR ROM game, and only the project's own game can be it.
	public static IReadOnlyList<string> ComposeArgv(RemasterFeasibility feasibility, RemasterRecording recording, string romPath)
	{
		List<string> argv = new() { feasibility.PythonExecutable };
		argv.AddRange(feasibility.PythonPrefixArgs);
		argv.Add(Path.Combine(feasibility.ToolsFolder, ComposeScript));
		argv.Add(recording.Path);
		if(!string.IsNullOrEmpty(romPath)) {
			argv.Add("--rom");
			argv.Add(romPath);
		}
		return argv;
	}

	private static RemasterHandOffControl ToolsControl(RemasterFeasibility feasibility, Func<string, bool> fileExists, string script)
	{
		if(feasibility.Python != PythonGate.Found) {
			return RemasterHandOffControl.Off(RemasterHandOffReason.NeedsPython);
		}
		if(feasibility.Tools != ToolsGate.Found) {
			return RemasterHandOffControl.Off(RemasterHandOffReason.NeedsTools);
		}
		if(!fileExists(Path.Combine(feasibility.ToolsFolder, script))) {
			return RemasterHandOffControl.Off(RemasterHandOffReason.ToolMissing);
		}
		return RemasterHandOffControl.On;
	}
}
