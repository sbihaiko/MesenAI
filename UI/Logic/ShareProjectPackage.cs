using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Mesen.Logic
{
	//Why W-H3's *Build Pack .zip* is disabled (rule 4).
	public enum ShareBuildReason
	{
		None,
		NeedsPython,
		NeedsTools,
		//mep/ holds no built layer yet (no textures/hires.txt, audio/hires.txt
		//or synth/preset.cfg): mep_build.py pack would say "nothing to pack".
		NothingBuilt,
		//No pack.json target and the project's game is not the one running:
		//mep_build.py pack needs --rom (or a declared target) for the hash.
		NeedsGame,
		JobRunning,
		//#647: a Remaster job (kit, build) runs on this project's folder.
		RemasterJobRunning
	}

	//What W-H3 shows about a project: its name for the title, and what step 3
	//sends as Game/Console. Console is "" when nothing on disk names it.
	public sealed record ShareProjectIdentity(string Folder, string Name, string Game, string ConsoleOption, bool HasBuiltLayer, bool HasTargets)
	{
		public string PackFolder => Path.Combine(Folder, ShareProjectPackage.PackFolderName);
		public string ZipPath => Path.Combine(Folder, ShareProjectPackage.ZipFileName(Name));
	}

	//G.8 (PRD Part B §13.5.4 W-H3, ADR-0243): sharing your own project. The
	//pack is the project's human layer, `<project>/mep/` (ADR-0243 Decision 1,
	//ADR-0147); step 1 is `mep_build.py pack` as a job, which writes pack.json,
	//zips the folder deterministically and lints the zip (a non-zero exit is a
	//lint problem). The zip goes to the project root, next to mep/, so a later
	//pack never embeds it. MesenAI hosts and uploads nothing (Part A §1,
	//ADR-0154): step 2 is the user's own upload. Host-free.
	public static class ShareProjectPackage
	{
		public const string PackFolderName = "mep";
		public const string BuildScript = "mep_build.py";
		//W-H3 step 2: the most common accepted host, opened in the browser.
		public const string GoogleDriveUrl = "https://drive.google.com/drive/my-drive";

		//"Contra (USA)" -> "contra-usa-mep.zip" (W-H3).
		public static string ZipFileName(string projectName)
		{
			StringBuilder slug = new StringBuilder();
			foreach(char c in (projectName ?? "").ToLowerInvariant()) {
				if(char.IsLetterOrDigit(c)) {
					slug.Append(c);
				} else if(slug.Length > 0 && slug[slug.Length - 1] != '-') {
					slug.Append('-');
				}
			}
			string stem = slug.ToString().Trim('-');
			return (stem.Length == 0 ? "project" : stem) + "-mep.zip";
		}

		public static ShareProjectIdentity Read(string projectFolder)
		{
			RemasterProjectInfo info = RemasterProjectReader.Read(projectFolder);
			string mep = Path.Combine(projectFolder, PackFolderName);
			bool built = File.Exists(Path.Combine(mep, "textures", "hires.txt")) ||
				File.Exists(Path.Combine(mep, "audio", "hires.txt")) ||
				File.Exists(Path.Combine(mep, "synth", "preset.cfg"));
			//#659: a first build stopped mid-sync left a half-written mep/.
			built = built && !RemasterBuildFreshness.IsClaimOnly(Path.Combine(mep, RemasterBuildFreshness.StampFile));
			string system = FirstTargetSystem(Path.Combine(mep, "pack.json"), out bool hasTargets);
			string game = StampValue(Path.Combine(projectFolder, RemasterProjectReader.StampFile), "rom");
			return new ShareProjectIdentity(projectFolder, info.Name, game.Length > 0 ? game : info.Name,
				PackShare.ConsoleOptionForSystem(system), built, hasTargets);
		}

		//remasterJobOnProject: Remaster's runner works on this project (#647);
		//mep_build.py pack would zip mep/ while the build rewrites it.
		public static ShareBuildReason BuildReason(ShareProjectIdentity project, bool isRunningGamesProject, RemasterFeasibility feasibility, bool jobRunning, bool remasterJobOnProject = false)
		{
			if(jobRunning) {
				return ShareBuildReason.JobRunning;
			}
			if(remasterJobOnProject) {
				return ShareBuildReason.RemasterJobRunning;
			}
			if(!project.HasBuiltLayer) {
				return ShareBuildReason.NothingBuilt;
			}
			if(!project.HasTargets && !isRunningGamesProject) {
				return ShareBuildReason.NeedsGame;
			}
			if(feasibility.Python != PythonGate.Found) {
				return ShareBuildReason.NeedsPython;
			}
			if(feasibility.Tools != ToolsGate.Found) {
				return ShareBuildReason.NeedsTools;
			}
			return ShareBuildReason.None;
		}

		//`mep_build.py pack <project>/mep --out <project>/<slug>-mep.zip [--rom ROM] --quiet`.
		//--rom only when the running game is the project's: its No-Intro hash
		//becomes the target, and an existing pack.json target is never replaced
		//by another game's.
		public static RemasterJobSpec PackJob(PythonCandidate python, string toolsFolder, ShareProjectIdentity project, string romPath)
		{
			List<string> argv = new() { python.Executable };
			argv.AddRange(python.PrefixArgs);
			argv.Add(Path.Combine(toolsFolder, BuildScript));
			argv.Add("pack");
			argv.Add(project.PackFolder);
			argv.Add("--out");
			argv.Add(project.ZipPath);
			if(!string.IsNullOrEmpty(romPath)) {
				argv.Add("--rom");
				argv.Add(romPath);
			}
			argv.Add("--quiet");
			return new RemasterJobSpec(RemasterJobKind.Pack, argv, toolsFolder, 1, project.Folder, project.Name);
		}

		//*Package a Project…* lists the projects Remaster knows (W-H2): each
		//existing project folder once, in the order given.
		public static IReadOnlyList<string> KnownProjects(IEnumerable<string> candidates)
		{
			List<string> list = new();
			foreach(string folder in candidates) {
				if(!RemasterProjectReader.IsProjectFolder(folder)) {
					continue;
				}
				if(!list.Exists(f => RemasterProjectLocator.SameFolder(f, folder))) {
					list.Add(folder);
				}
			}
			return list;
		}

		//"38 MB" (W-H3's result line); KB under a megabyte.
		public static string FormatSize(long bytes)
		{
			if(bytes < 1024 * 1024) {
				return Math.Max(1, (bytes + 1023) / 1024).ToString(CultureInfo.InvariantCulture) + " KB";
			}
			return Math.Round(bytes / (1024.0 * 1024.0)).ToString(CultureInfo.InvariantCulture) + " MB";
		}

		//`.bootstrap` is `key=value` lines (MepPackManager::StartBootstrapIfNeeded).
		private static string StampValue(string stampPath, string key)
		{
			try {
				if(!File.Exists(stampPath)) {
					return "";
				}
				foreach(string line in File.ReadAllLines(stampPath)) {
					if(line.StartsWith(key + "=", StringComparison.Ordinal)) {
						return line.Substring(key.Length + 1).Trim();
					}
				}
			} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
			}
			return "";
		}

		private static string FirstTargetSystem(string packJson, out bool hasTargets)
		{
			hasTargets = false;
			try {
				if(!File.Exists(packJson)) {
					return "";
				}
				using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(packJson));
				if(doc.RootElement.ValueKind == JsonValueKind.Object &&
					doc.RootElement.TryGetProperty("targets", out JsonElement targets) &&
					targets.ValueKind == JsonValueKind.Array && targets.GetArrayLength() > 0) {
					hasTargets = true;
					JsonElement first = targets[0];
					if(first.ValueKind == JsonValueKind.Object && first.TryGetProperty("system", out JsonElement s) && s.ValueKind == JsonValueKind.String) {
						return s.GetString() ?? "";
					}
				}
			} catch(Exception ex) when(ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) {
				//An unreadable pack.json declares no target; mep_build reports it.
			}
			return "";
		}
	}
}
