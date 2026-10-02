using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.Logic;

//G.6 (PRD Part B §13.5.3 W-R1 zone ③, W-R3): Build & show in game. The build
//is `scripts/mep_project.py build <project> --rom ROM` (mep_project_build.py):
//it rebuilds the project's mep/ from the newest recording and its kit, and its
//last line says how the game can show the result. This file is the host-free
//half - the job spec, reading that line, deciding what the game does, and
//W-R1's "N files changed since the last build". The VM calls the core.
public static class RemasterBuilds
{
	//copy, build, figures, check (mep_project_build.STEPS).
	public const int Steps = 4;

	public static RemasterJobSpec Spec(PythonCandidate python, string toolsFolder, string projectFolder, string romPath, string gameName)
	{
		List<string> argv = new() { python.Executable };
		argv.AddRange(python.PrefixArgs);
		argv.Add(Path.Combine(toolsFolder, RemasterToolsLocator.EntryScript));
		argv.Add("build");
		argv.Add(projectFolder);
		argv.Add("--rom");
		argv.Add(romPath);
		return new RemasterJobSpec(RemasterJobKind.Build, argv, toolsFolder, Steps, projectFolder, gameName);
	}
}

public enum RemasterShowKind
{
	//No show line (an older tool, or a failed build).
	None,
	//Only images changed and mep/ was already there: ADR-0212's in-place reload.
	Images,
	//The manifest changed or mep/ is new: the ROM is reopened.
	Reload
}

public sealed record RemasterBuildOutcome(string RecordingId, RemasterShowKind Show)
{
	public static RemasterBuildOutcome Parse(IEnumerable<string> lines)
	{
		string recording = "";
		RemasterShowKind show = RemasterShowKind.None;
		foreach(string raw in lines) {
			string line = raw?.Trim() ?? "";
			if(line.StartsWith("recording: ", StringComparison.Ordinal)) {
				recording = line.Substring(11).Trim();
			} else if(line.StartsWith("show: ", StringComparison.Ordinal)) {
				show = line.Substring(6).Trim() switch {
					"images" => RemasterShowKind.Images,
					"reload" => RemasterShowKind.Reload,
					_ => RemasterShowKind.None,
				};
			}
		}
		return new RemasterBuildOutcome(recording, show);
	}
}

public enum RemasterShowAction
{
	//The build is in mep/; it plays the next time the project's game opens.
	None,
	//RequestMepImageReload (ADR-0212): re-decode the changed images in place.
	ReloadImages,
	//The manifest changed: P.9's pack change (ADR-0244) - an in-memory save
	//state, a ROM reload that reads the new mep/, the state loaded back, so
	//the player keeps their place; it falls back to a restart where ADR-0244
	//says so (a movie, netplay, a ROM patch, an unmeasured console).
	ReloadPack
}

public static class RemasterShow
{
	//gameIsProjects: the running game is still the project's own. Opening
	//another game never stops a build (W-X3), and a build's images are never
	//poured into a different game.
	public static RemasterShowAction Decide(RemasterBuildOutcome outcome, bool gameIsProjects, bool consoleIsNes)
	{
		if(!gameIsProjects || !consoleIsNes) {
			return RemasterShowAction.None;
		}
		return outcome.Show == RemasterShowKind.Images ? RemasterShowAction.ReloadImages : RemasterShowAction.ReloadPack;
	}

	//#649: a finished build reloads the pack by itself only when ADR-0244's
	//plan keeps the player's place. Its restart (a movie, a shared-replay
	//recording, netplay) would end what the player is doing for a change
	//they did not ask for at that moment: the build plays next time instead.
	public static RemasterShowAction PackReload(PackChangePlan plan)
	{
		return plan.Route == PackChangeRoute.InPlace ? RemasterShowAction.ReloadPack : RemasterShowAction.None;
	}
}

//W-R1 zone ③: "2 files changed since the last build." A file counts when the
//artist could have painted it - a PNG of the recording's kit or of the pattern
//pages, not an `.orig.png` reference twin - and it is newer than the stamp the
//last clean build wrote into mep/. Null = never built.
public static class RemasterBuildFreshness
{
	public const string StampFile = ".remaster-build.json";

	public static int? ChangedSinceLastBuild(string projectFolder, string recordingId)
	{
		if(string.IsNullOrEmpty(projectFolder)) {
			return null;
		}
		string stamp = Path.Combine(projectFolder, "mep", StampFile);
		if(!File.Exists(stamp)) {
			return null;
		}
		DateTime built = File.GetLastWriteTimeUtc(stamp);
		int changed = 0;
		List<string> roots = new() { Path.Combine(projectFolder, "kit", "pages") };
		if(!string.IsNullOrEmpty(recordingId)) {
			roots.Insert(0, Path.Combine(projectFolder, "kit", recordingId));
		}
		foreach(string root in roots) {
			if(!Directory.Exists(root)) {
				continue;
			}
			try {
				foreach(string file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories)) {
					if(file.EndsWith(".orig.png", StringComparison.OrdinalIgnoreCase)) {
						continue;
					}
					if(File.GetLastWriteTimeUtc(file) > built) {
						changed++;
					}
				}
			} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
				//A kit being rewritten under us: count what was seen.
			}
		}
		return changed;
	}
}
