using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mesen.Logic;

//G.6 (PRD Part B §13.5.3 W-R4, rule 3): a failed build's lines, rewritten
//against the surface the artist painted. The build runs in a staging copy of
//the project, so mep_build / mep_lint / mep_figure name a file of that copy
//(`usr003.png`, `textures/sheets/map-000.png`); the kit index maps the file
//name back to the kit file the artist saves over and to its caption. A line
//this reader cannot translate is only counted - the raw text stays under
//Show Log, never on the surface. The sentences are the VM's (one resource
//per kind); this type says which kind, about what, and the numbers.
public enum RemasterProblemKind
{
	//A painted PNG is no longer the size it was handed out at.
	CanvasResized,
	//A picture and its `.orig.png` reference twin no longer match in size.
	ReferenceChanged,
	//Sheets painted at different upscales (Detail: "3×|2×").
	ScaleMismatch,
	//The `.ora`'s guides/palettes magenta left visible on export (ADR-0220 §4).
	GuideMarker,
	//The file is not a PNG the build can read.
	NotAPng,
	//mep/ holds a pack the build did not write (an installed pack, ADR-0147).
	ForeignPack,
	//The project has no recording with textures.
	NothingRecorded
}

public sealed record RemasterBuildProblem(RemasterProblemKind Kind, string Caption, string FilePath, string Detail);

public sealed record RemasterBuildProblems(IReadOnlyList<RemasterBuildProblem> Problems, int Untranslated)
{
	public int Count => Problems.Count + Untranslated;
}

//File name -> (caption, absolute kit path), from the kit.json files of the
//recording the build used and of the pattern pages. Caption: the entry's
//`caption`, else its title up to " — " (the generator's id), else the file
//stem (ADR-0183 §5: names come from the data or from a human). G.7's tile
//browser owns the full caption rule (names.json, ADR-0209 Q1).
public sealed class RemasterKitIndex
{
	private readonly Dictionary<string, (string Caption, string Path)> _byName = new(StringComparer.OrdinalIgnoreCase);

	public void Add(string fileName, string caption, string path)
	{
		_byName.TryAdd(fileName, (caption, path));
	}

	public (string Caption, string Path)? Find(string fileName)
	{
		return _byName.TryGetValue(fileName ?? "", out var hit) ? hit : null;
	}

	public static RemasterKitIndex Load(string projectFolder, string recordingId)
	{
		RemasterKitIndex index = new();
		if(string.IsNullOrEmpty(projectFolder)) {
			return index;
		}
		if(!string.IsNullOrEmpty(recordingId)) {
			index.AddKit(Path.Combine(projectFolder, "kit", recordingId));
		}
		index.AddKit(Path.Combine(projectFolder, "kit", "pages"));
		return index;
	}

	private void AddKit(string kitFolder)
	{
		string manifest = Path.Combine(kitFolder, "kit.json");
		if(!File.Exists(manifest)) {
			return;
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifest));
			if(doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("parts", out JsonElement parts) || parts.ValueKind != JsonValueKind.Array) {
				return;
			}
			foreach(JsonElement part in parts.EnumerateArray()) {
				if(part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array) {
					continue;
				}
				foreach(JsonElement file in files.EnumerateArray()) {
					string rel = Str(file, "path");
					if(rel.Length == 0) {
						continue;
					}
					string caption = CaptionOf(file, rel);
					Add(Path.GetFileName(rel), caption, Path.Combine(kitFolder, rel.Replace('/', Path.DirectorySeparatorChar)));
					string figure = Str(file, "figure");
					if(figure.Length > 0) {
						Add(Path.GetFileName(figure), caption, Path.Combine(kitFolder, figure.Replace('/', Path.DirectorySeparatorChar)));
					}
				}
			}
		} catch(Exception ex) when(ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) {
			//No index: the problems keep their file names.
		}
	}

	private static string CaptionOf(JsonElement file, string rel)
	{
		string caption = Str(file, "caption");
		if(caption.Length > 0) {
			return caption;
		}
		string title = Str(file, "title");
		int dash = title.IndexOf(" — ", StringComparison.Ordinal);
		string head = (dash >= 0 ? title.Substring(0, dash) : title).Trim();
		return head.Length > 0 ? head : RemasterBuildProblemReader.Stem(Path.GetFileName(rel));
	}

	private static string Str(JsonElement e, string key)
	{
		return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
	}
}

public static class RemasterBuildProblemReader
{
	private const string Png = @"(?<f>[^\s:]+\.png)";
	private static readonly Regex FigureResized = new(@"^error: " + Png + @": (?<w>\d+)x(?<h>\d+) is not a whole multiple of the (?<tw>\d+)x(?<th>\d+) twin", RegexOptions.CultureInvariant);
	private static readonly Regex SheetResized = new(@"^error: " + Png + @": \d+x\d+ is not (an integer multiple of the \d+x\d+ sheet|a multiple of \d+)", RegexOptions.CultureInvariant);
	private static readonly Regex LegacyColumns = new(@"^error: " + Png + @": \d+px wide = \d+ columns", RegexOptions.CultureInvariant);
	private static readonly Regex TwinChanged = new(@"^error: " + Png + @": reference twin \S+ is ", RegexOptions.CultureInvariant);
	private static readonly Regex Scale = new(@"^error: " + Png + @": painted at (?<n>\d+)x while \S+ is at (?<m>\d+)x", RegexOptions.CultureInvariant);
	private static readonly Regex NotPng = new(@"^error: " + Png + @": not a valid PNG", RegexOptions.CultureInvariant);
	private static readonly Regex Sentinel = new(@"^error\s+(?<f>\S+\.png)\s+cell index \d+ at .*guide sentinel", RegexOptions.CultureInvariant);
	private static readonly Regex Foreign = new(@"^error: (?<p>.+?) holds (an installed pack|a pack this build did not write)", RegexOptions.CultureInvariant);
	private static readonly Regex NoRecording = new(@"^error: .+ has no recording with textures", RegexOptions.CultureInvariant);
	//Summaries of errors already reported on their own line.
	private static readonly Regex Summary = new(@"^error: lint failed \(", RegexOptions.CultureInvariant);

	public static string Stem(string fileName)
	{
		string name = Path.GetFileName(fileName ?? "");
		return name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
	}

	public static RemasterBuildProblems Read(IEnumerable<string> log, RemasterKitIndex kit)
	{
		List<RemasterBuildProblem> problems = new();
		HashSet<RemasterBuildProblem> seen = new();
		int untranslated = 0;
		foreach(string raw in log) {
			string line = raw?.Trim() ?? "";
			if(!line.StartsWith("error", StringComparison.Ordinal) || Summary.IsMatch(line)) {
				continue;
			}
			RemasterBuildProblem? problem = Translate(line, kit);
			if(problem == null) {
				untranslated++;
			} else if(seen.Add(problem)) {
				problems.Add(problem);
			}
		}
		return new RemasterBuildProblems(problems, untranslated);
	}

	private static RemasterBuildProblem? Translate(string line, RemasterKitIndex kit)
	{
		Match m;
		if((m = FigureResized.Match(line)).Success) {
			return About(RemasterProblemKind.CanvasResized, m.Groups["f"].Value, kit, HandedOutSize(m));
		}
		if((m = SheetResized.Match(line)).Success || (m = LegacyColumns.Match(line)).Success) {
			return About(RemasterProblemKind.CanvasResized, m.Groups["f"].Value, kit, "");
		}
		if((m = TwinChanged.Match(line)).Success) {
			return About(RemasterProblemKind.ReferenceChanged, m.Groups["f"].Value, kit, "");
		}
		if((m = Scale.Match(line)).Success) {
			return About(RemasterProblemKind.ScaleMismatch, m.Groups["f"].Value, kit, m.Groups["n"].Value + "×|" + m.Groups["m"].Value + "×");
		}
		if((m = NotPng.Match(line)).Success) {
			return About(RemasterProblemKind.NotAPng, m.Groups["f"].Value, kit, "");
		}
		if((m = Sentinel.Match(line)).Success) {
			return About(RemasterProblemKind.GuideMarker, m.Groups["f"].Value, kit, "");
		}
		if((m = Foreign.Match(line)).Success) {
			return new RemasterBuildProblem(RemasterProblemKind.ForeignPack, "", m.Groups["p"].Value.Trim(), "");
		}
		if(NoRecording.IsMatch(line)) {
			return new RemasterBuildProblem(RemasterProblemKind.NothingRecorded, "", "", "");
		}
		return null;
	}

	private static RemasterBuildProblem About(RemasterProblemKind kind, string file, RemasterKitIndex kit, string detail)
	{
		string name = Path.GetFileName(file.Replace('\\', '/').Split('/')[^1]);
		var hit = kit.Find(name);
		return new RemasterBuildProblem(kind, hit?.Caption ?? Stem(name), hit?.Path ?? "", detail);
	}

	//The figure was handed out at a whole multiple of its 1x twin: the
	//dimension that still divides gives the factor ("was 640×128").
	private static string HandedOutSize(Match m)
	{
		int w = Int(m, "w"), h = Int(m, "h"), tw = Int(m, "tw"), th = Int(m, "th");
		if(tw <= 0 || th <= 0) {
			return "";
		}
		int scale = h % th == 0 && h / th > 0 ? h / th : w % tw == 0 && w / tw > 0 ? w / tw : 0;
		return scale > 0 ? (tw * scale).ToString(CultureInfo.InvariantCulture) + "×" + (th * scale).ToString(CultureInfo.InvariantCulture) : "";
	}

	private static int Int(Match m, string group)
	{
		return int.TryParse(m.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : 0;
	}
}
