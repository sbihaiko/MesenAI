using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Mesen.Logic;

//G.7 (PRD Part B §13.5.3 W-R1 zone ②, ADR-0183 §2, ADR-0194): the tiles of
//the PAINT zone, read from what `scripts/mep_project.py kit` already wrote -
//`kit/rec-NNN/kit.json` (figures and scenery, per recording) and
//`kit/pages/kit.json` (the cross-recording pattern pages), each assembled by
//artist_kit_assemble.py from the generators' `kit-part-*.json` fragments.
//Nothing here infers what a file does not carry: captions are the
//generators' titles (names.json > inferred label > id, ADR-0209 Q1), and
//seen/fill counts are the fragments' own (ADR-0183 §3, ADR-0219). An AI's
//`kit-proposals.json` is never read (ADR-0188 §2: a proposal is not evidence).
public enum RemasterKitCategory
{
	Figures,
	Scenery,
	StageMaps,
	PatternPages,
	//W-R6: the sheets mep_import.py cut from a finished pack (IMPORT.md marks
	//the project as an import). They come from that pack, not from a recording.
	Imported
}

public sealed record RemasterKitTile(
	RemasterKitCategory Category,
	//The kit generator's `unit`: grid, object, element, scene, panorama, page.
	string Unit,
	string Caption,
	string Title,
	string ImagePath,
	//The `*.orig.png` twin ("" = none declared and none beside the picture).
	string ReferencePath,
	//ADR-0225: a figure grid's composed view, and its twin ("" = none).
	string FigurePath,
	string FigureReferencePath,
	//rec-NNN for a per-recording kit, "" for the union pages and imports.
	string RecordingId,
	int Cells,
	int Rows,
	int Columns,
	//Phases of an animation (a figure run's playback order, or an element's
	//blink phases); 0 when the kit says nothing about phases.
	int Phases,
	//The fragment's `seen` (null when it carries none).
	bool? Seen,
	//Pattern pages only (-1 = not given): ROM fill and empty cells.
	int Fill,
	int Empty,
	//ADR-0252 §2: a figure grid's `playsColumns` (1-based figure columns of
	//row 0 in phase order, null = a phase this sheet does not draw) and its
	//`ids` (the poses on the sheet in reading order). Empty when not given.
	IReadOnlyList<int?>? PlaysColumns = null,
	IReadOnlyList<string>? Ids = null
)
{
	//W-R5: a page's cells seen in play = every cell that is neither a ROM fill
	//nor empty (recorded here, donated by another recording, or folded onto
	//one of those - artist_chr_kit's own totals add up that way).
	public int SeenCells => Fill < 0 || Empty < 0 ? -1 : Math.Max(0, Cells - Fill - Empty);

	//Only these surfaces keep their twin as a pixel copy of the untouched
	//picture, so only for them is "differs from the twin" the same as
	//"painted" (mep_build's `_EditedProbe` reads the same pairs).
	public bool HasPrePaintTwin => Unit is "grid" or "object" or "element" or "panorama";

	public IReadOnlyList<int?> PlayOrder => PlaysColumns ?? Array.Empty<int?>();
	public IReadOnlyList<string> PoseIds => Ids ?? Array.Empty<string>();
}

public sealed record RemasterKit(IReadOnlyList<RemasterKitTile> Tiles, IReadOnlyList<string> Problems)
{
	public static RemasterKit Empty { get; } = new(Array.Empty<RemasterKitTile>(), Array.Empty<string>());

	public int Count(RemasterKitCategory category)
	{
		int n = 0;
		foreach(RemasterKitTile t in Tiles) {
			if(t.Category == category) {
				n++;
			}
		}
		return n;
	}
}

public static class RemasterKitReader
{
	public const string KitManifest = "kit.json";
	public const string ImportNote = "IMPORT.md";
	public const string PagesFolder = "pages";

	public static RemasterKitCategory? CategoryOf(string part)
	{
		return part switch {
			"sprites" => RemasterKitCategory.Figures,
			"background" => RemasterKitCategory.Scenery,
			"map" => RemasterKitCategory.StageMaps,
			"chr" => RemasterKitCategory.PatternPages,
			_ => null,
		};
	}

	//"cycle000 — a 4-phase loop, seen 8 time(s)" -> "cycle000": the tile shows
	//what the generator put first; the popover keeps the whole title.
	public static string CaptionOf(string title)
	{
		int dash = title.IndexOf(" — ", StringComparison.Ordinal);
		return (dash > 0 ? title.Substring(0, dash) : title).Trim();
	}

	public static RemasterKit Read(string projectFolder)
	{
		List<RemasterKitTile> tiles = new();
		List<string> problems = new();
		if(string.IsNullOrEmpty(projectFolder) || !Directory.Exists(projectFolder)) {
			return RemasterKit.Empty;
		}
		string kitRoot = Path.Combine(projectFolder, RemasterProjectReader.KitFolder);
		if(Directory.Exists(kitRoot)) {
			//A kit assembled straight into kit/ (by hand) reads too, before the
			//per-recording ones.
			ReadKit(kitRoot, "", tiles, problems);
			List<string> subs = new(Directory.GetDirectories(kitRoot));
			subs.Sort(StringComparer.Ordinal);
			foreach(string sub in subs) {
				string name = Path.GetFileName(sub);
				bool isRecording = RemasterProjectReader.RecordingNumber(name) > 0;
				if(isRecording || name == PagesFolder) {
					ReadKit(sub, isRecording ? name : "", tiles, problems);
				}
			}
		}
		if(File.Exists(Path.Combine(projectFolder, ImportNote))) {
			ReadImported(projectFolder, tiles);
		}
		//Reading order (ADR-0183 §2): the category, then the kit's own order.
		List<RemasterKitTile> ordered = new(tiles.Count);
		foreach(RemasterKitCategory c in Enum.GetValues<RemasterKitCategory>()) {
			foreach(RemasterKitTile t in tiles) {
				if(t.Category == c) {
					ordered.Add(t);
				}
			}
		}
		return new RemasterKit(ordered, problems);
	}

	private static void ReadKit(string kitDir, string recordingId, List<RemasterKitTile> tiles, List<string> problems)
	{
		string manifest = Path.Combine(kitDir, KitManifest);
		if(!File.Exists(manifest)) {
			return;
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifest));
			if(doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("parts", out JsonElement parts) || parts.ValueKind != JsonValueKind.Array) {
				problems.Add(Relative(kitDir, manifest) + " has no parts");
				return;
			}
			foreach(JsonElement part in parts.EnumerateArray()) {
				RemasterKitCategory? category = CategoryOf(Str(part, "part"));
				if(category == null || !part.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array) {
					continue;
				}
				foreach(JsonElement f in files.EnumerateArray()) {
					RemasterKitTile? tile = TileOf(kitDir, recordingId, category.Value, f);
					if(tile != null) {
						tiles.Add(tile);
					}
				}
			}
		} catch(Exception ex) when(ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) {
			problems.Add(Relative(kitDir, manifest) + " could not be read (" + ex.Message + ")");
		}
	}

	private static RemasterKitTile? TileOf(string kitDir, string recordingId, RemasterKitCategory category, JsonElement f)
	{
		if(f.ValueKind != JsonValueKind.Object) {
			return null;
		}
		string rel = Str(f, "path");
		if(rel.Length == 0 || Path.IsPathRooted(rel) || rel.Contains("..", StringComparison.Ordinal)) {
			return null;
		}
		string image = Path.Combine(kitDir, rel);
		string title = Str(f, "title");
		if(title.Length == 0) {
			title = Path.GetFileNameWithoutExtension(rel);
		}
		string reference = Str(f, "reference");
		string referencePath = reference.Length > 0 && !Path.IsPathRooted(reference) ? Path.Combine(kitDir, reference) : TwinOf(image);
		string figure = Str(f, "figure");
		string figurePath = figure.Length > 0 && !Path.IsPathRooted(figure) && !figure.Contains("..", StringComparison.Ordinal) ? Path.Combine(kitDir, figure) : "";
		int phases = 0;
		List<int?> order = new();
		if(f.TryGetProperty("playsColumns", out JsonElement plays) && plays.ValueKind == JsonValueKind.Array) {
			phases = plays.GetArrayLength();
			foreach(JsonElement c in plays.EnumerateArray()) {
				order.Add(c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int col) ? col : null);
			}
		} else {
			phases = Int(f, "phases", 0);
		}
		List<string> ids = new();
		if(f.TryGetProperty("ids", out JsonElement idList) && idList.ValueKind == JsonValueKind.Array) {
			foreach(JsonElement id in idList.EnumerateArray()) {
				ids.Add(id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : "");
			}
		}
		bool? seen = f.TryGetProperty("seen", out JsonElement s) && (s.ValueKind == JsonValueKind.True || s.ValueKind == JsonValueKind.False) ? s.GetBoolean() : null;
		bool page = category == RemasterKitCategory.PatternPages;
		return new RemasterKitTile(
			category, Str(f, "unit"), CaptionOf(title), title, image,
			File.Exists(referencePath) ? referencePath : "",
			File.Exists(figurePath) ? figurePath : "",
			File.Exists(figurePath) && File.Exists(TwinOf(figurePath)) ? TwinOf(figurePath) : "",
			recordingId, Int(f, "cells", 0), Int(f, "rows", 0), Int(f, "columns", 0), phases, seen,
			page ? Int(f, "fill", -1) : -1, page ? Int(f, "empty", -1) : -1,
			order, ids
		);
	}

	private static void ReadImported(string projectFolder, List<RemasterKitTile> tiles)
	{
		string sheets = Path.Combine(projectFolder, "textures", "sheets");
		if(!Directory.Exists(sheets)) {
			return;
		}
		List<string> pngs = new(Directory.GetFiles(sheets, "*.png"));
		pngs.Sort(StringComparer.Ordinal);
		foreach(string png in pngs) {
			if(png.EndsWith(".orig.png", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			string stem = Path.GetFileNameWithoutExtension(png);
			string twin = TwinOf(png);
			tiles.Add(new RemasterKitTile(RemasterKitCategory.Imported, "imported", stem, stem, png,
				File.Exists(twin) ? twin : "", "", "", "", 0, 0, 0, 0, null, -1, -1));
		}
	}

	//`a/b/usr000.png` -> `a/b/usr000.orig.png`
	public static string TwinOf(string png)
	{
		if(string.IsNullOrEmpty(png) || !png.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) {
			return "";
		}
		return png.Substring(0, png.Length - 4) + ".orig.png";
	}

	private static string Relative(string kitDir, string path)
	{
		string parent = Path.GetFileName(Path.GetDirectoryName(kitDir) ?? "");
		return parent + "/" + Path.GetFileName(kitDir) + "/" + Path.GetFileName(path);
	}

	private static string Str(JsonElement e, string key)
	{
		return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
	}

	private static int Int(JsonElement e, string key, int fallback)
	{
		return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : fallback;
	}

	//"rec-002" -> "2" for the popover's "from recording 2".
	public static string RecordingLabel(string recordingId)
	{
		int n = RemasterProjectReader.RecordingNumber(recordingId);
		return n > 0 ? n.ToString(CultureInfo.InvariantCulture) : "";
	}
}
