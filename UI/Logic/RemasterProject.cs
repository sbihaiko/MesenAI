using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Mesen.Logic;

//G.3 (PRD Part B §13.5.3 W-R1, ADR-0243): the Remaster project as the screen
//reads it - the ROM's enhancement folder, its recordings and the kit beside
//them. The layout rule mirrors scripts/mep_project.py's `list` (and the core's
//RemasterProject.h) so the recordings list needs no Python: W-R0b says "You
//can still record" without it, and a list that disappeared with Python would
//contradict that. Ids are `rec-` plus digits; a bare auto/textures or
//auto/audio (a folder recorded before ADR-0243) is rec-001 at auto/ itself;
//project.json adds metadata to recordings that exist and never lists one that
//does not (Q2). Host-free: BCL file system and System.Text.Json's DOM only.
public sealed record RemasterRecording(
	string Id,
	string Path,
	bool HasTextures,
	bool HasAudio,
	string? Source,
	DateTime? RecordedAtUtc,
	double? DurationSeconds,
	string Note
)
{
	public int Number => RemasterProjectReader.RecordingNumber(Id);
}

public sealed record RemasterProjectInfo(
	string Folder,
	string Name,
	IReadOnlyList<RemasterRecording> Recordings,
	bool HasKit,
	string Problem
)
{
	public int TexturedRecordingCount
	{
		get
		{
			int count = 0;
			foreach(RemasterRecording r in Recordings) {
				if(r.HasTextures) {
					count++;
				}
			}
			return count;
		}
	}
}

public static class RemasterProjectReader
{
	public const string AutoFolder = "auto";
	public const string ManifestFile = "project.json";
	public const string KitFolder = "kit";
	public const string StampFile = ".bootstrap";

	private static readonly string[] Sources = { "play", "tas", "ai", "script" };

	//`rec-012` -> 12; anything else (or rec-000, or more than six digits) -> 0,
	//as mep_project.recording_number and the core parse it.
	public static int RecordingNumber(string name)
	{
		if(name == null || !name.StartsWith("rec-", StringComparison.Ordinal)) {
			return 0;
		}
		string digits = name.Substring(4);
		if(digits.Length < 1 || digits.Length > 6) {
			return 0;
		}
		foreach(char c in digits) {
			if(c < '0' || c > '9') {
				return 0;
			}
		}
		return int.Parse(digits, CultureInfo.InvariantCulture);
	}

	public static string FormatId(int number) => "rec-" + number.ToString("000", CultureInfo.InvariantCulture);

	//The scaler the automatic upscale was made with, from the stamp the
	//recorder writes ("filter=xBRZ", "scale=4"): "xBRZ 4×". W-P6 names it in
	//the automatic layer's byline; without the stamp or either key it says
	//nothing about the scaler (PackDetail.AutoByline).
	public static string BootstrapScaler(string projectFolder)
	{
		string filter = StampValue(projectFolder, "filter");
		string scale = StampValue(projectFolder, "scale");
		return filter.Length == 0 || scale.Length == 0 ? "" : filter + " " + scale + "×";
	}

	//`.bootstrap` is `key=value` lines (MepPackManager::StartBootstrapIfNeeded).
	public static string StampValue(string projectFolder, string key)
	{
		try {
			string stampPath = System.IO.Path.Combine(projectFolder ?? "", StampFile);
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

	//A folder is a project once it holds auto/ or the .bootstrap stamp
	//(ADR-0243 Decision 1); W-R0's "Open a project folder…" accepts exactly that.
	public static bool IsProjectFolder(string folder)
	{
		return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) &&
			(Directory.Exists(System.IO.Path.Combine(folder, AutoFolder)) || File.Exists(System.IO.Path.Combine(folder, StampFile)));
	}

	private static bool HasSection(string recordingFolder, bool textures)
	{
		//mep_project.PROBES: audio may hold fingerprints.json instead of hires.txt
		if(textures) {
			return File.Exists(System.IO.Path.Combine(recordingFolder, "textures", "hires.txt"));
		}
		return File.Exists(System.IO.Path.Combine(recordingFolder, "audio", "hires.txt")) ||
			File.Exists(System.IO.Path.Combine(recordingFolder, "audio", "fingerprints.json"));
	}

	public static RemasterProjectInfo Read(string folder)
	{
		string name = System.IO.Path.GetFileName(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
		string problem = "";
		Dictionary<string, JsonElement> listed = new();
		JsonDocument? doc = null;
		string manifestPath = System.IO.Path.Combine(folder, ManifestFile);
		if(File.Exists(manifestPath)) {
			try {
				doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
				if(doc.RootElement.ValueKind != JsonValueKind.Object) {
					problem = ManifestFile + " is not a JSON object";
				} else {
					if(doc.RootElement.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String && n.GetString() is string s && s.Length > 0) {
						name = s;
					}
					if(doc.RootElement.TryGetProperty("recordings", out JsonElement list) && list.ValueKind == JsonValueKind.Array) {
						foreach(JsonElement item in list.EnumerateArray()) {
							if(item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String) {
								listed[id.GetString()!] = item;
							}
						}
					}
				}
			} catch(Exception ex) when(ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) {
				//The folders still list; the metadata is what is missing.
				problem = ManifestFile + " could not be read (" + ex.Message + ")";
			}
		}

		SortedDictionary<int, string> found = new();
		string auto = System.IO.Path.Combine(folder, AutoFolder);
		if(Directory.Exists(auto)) {
			foreach(string child in Directory.GetDirectories(auto)) {
				int number = RecordingNumber(System.IO.Path.GetFileName(child));
				if(number > 0) {
					found[number] = child;
				}
			}
			if(HasSection(auto, true) || HasSection(auto, false)) {
				if(found.ContainsKey(1)) {
					problem = "both a bare auto/textures and auto/rec-001 claim recording 1 - move one aside";
				} else {
					found[1] = auto;
				}
			}
		}

		List<RemasterRecording> recordings = new();
		foreach(KeyValuePair<int, string> pair in found) {
			string id = FormatId(pair.Key);
			listed.TryGetValue(id, out JsonElement meta);
			recordings.Add(new RemasterRecording(
				id, pair.Value, HasSection(pair.Value, true), HasSection(pair.Value, false),
				ReadSource(meta), ReadTimestamp(meta), ReadDuration(meta), ReadString(meta, "note")
			));
		}
		doc?.Dispose();

		bool hasKit = Directory.Exists(System.IO.Path.Combine(folder, KitFolder)) &&
			Directory.GetFileSystemEntries(System.IO.Path.Combine(folder, KitFolder)).Length > 0;
		return new RemasterProjectInfo(folder, name, recordings, hasKit, problem);
	}

	private static string ReadString(JsonElement meta, string key)
	{
		return meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
	}

	private static string? ReadSource(JsonElement meta)
	{
		string source = ReadString(meta, "source");
		return Array.IndexOf(Sources, source) >= 0 ? source : null;
	}

	private static DateTime? ReadTimestamp(JsonElement meta)
	{
		string text = ReadString(meta, "recordedAt");
		return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t) ? t : null;
	}

	private static double? ReadDuration(JsonElement meta)
	{
		return meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("durationSeconds", out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
	}
}

//Which folder is the running game's project (ADR-0243 Decision 1): the
//ADR-0049 sibling `<dir>/<Game>/`, or `EnhancementPacks/<Game>/` when the ROM
//folder was read-only and an earlier recording went there. The core decides
//where a new recording goes; this only answers "is there one already".
public static class RemasterProjectLocator
{
	public static string FallbackFolder(string siblingFolder, string enhancementPacksFolder)
	{
		if(string.IsNullOrEmpty(siblingFolder) || string.IsNullOrEmpty(enhancementPacksFolder)) {
			return "";
		}
		string game = Path.GetFileName(siblingFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		return Path.Combine(enhancementPacksFolder, game);
	}

	//The existing project of the running game, or "" (W-R0: no project yet).
	public static string ForGame(string siblingFolder, string enhancementPacksFolder)
	{
		if(RemasterProjectReader.IsProjectFolder(siblingFolder)) {
			return siblingFolder;
		}
		string fallback = FallbackFolder(siblingFolder, enhancementPacksFolder);
		return RemasterProjectReader.IsProjectFolder(fallback) ? fallback : "";
	}

	//The project a recording writes into: `<project>/auto/rec-NNN` -> `<project>`.
	public static string FromRecordingFolder(string recordingFolder)
	{
		if(string.IsNullOrEmpty(recordingFolder)) {
			return "";
		}
		DirectoryInfo rec = new DirectoryInfo(recordingFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		return rec.Parent?.Name == RemasterProjectReader.AutoFolder && rec.Parent.Parent != null ? rec.Parent.Parent.FullName : "";
	}

	public static bool SameFolder(string a, string b)
	{
		if(string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) {
			return false;
		}
		string na = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string nb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(na, nb, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
	}
}
