using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mesen.Logic;

//ADR-0252: the three numbers the Remaster renders show, each defined by the
//file it is read from. Nothing here estimates: a number no file carries is
//null ("unknown"), and the ViewModel leaves its words out rather than show 0.
//Host-free: BCL file system and System.Text.Json's DOM only.

//§1 "N shapes seen while you played" (W-R1 zone ①): the distinct tile shapes
//the recordings drew. A shape is a `<tile>` row's tileData with its palette
//folded (the core's HdTileKey::GetKey(true), the same identity the W-R2 live
//counter uses); only `N` rows count - `Y` is the ROM export the bootstrap
//recorder seeds (artist_chr_kit's TileRow.exported, #449).
public static class RemasterShapesSeen
{
	public static HashSet<string> KeysOf(IEnumerable<string> hiresLines)
	{
		HashSet<string> keys = new(StringComparer.Ordinal);
		foreach(string raw in hiresLines) {
			string line = raw.TrimStart();
			if(line.StartsWith('[')) {
				int close = line.IndexOf(']');
				line = close > 0 ? line.Substring(close + 1) : line;
			}
			if(!line.StartsWith("<tile>", StringComparison.Ordinal)) {
				continue;
			}
			string[] fields = line.Substring(6).Split(',');
			if(fields.Length < 7 || !fields[6].Trim().Equals("N", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			string data = fields[1].Trim().ToUpperInvariant();
			if(data.Length > 0) {
				keys.Add(data);
			}
		}
		return keys;
	}

	//The union over the project's recordings; null when none of them has a
	//readable textures/hires.txt (an audio-only project, or no recording).
	public static int? Count(IEnumerable<IReadOnlySet<string>?> perRecording)
	{
		HashSet<string>? union = null;
		foreach(IReadOnlySet<string>? keys in perRecording) {
			if(keys == null) {
				continue;
			}
			union ??= new HashSet<string>(StringComparer.Ordinal);
			union.UnionWith(keys);
		}
		return union?.Count;
	}

	public static string HiresOf(RemasterRecording recording) => Path.Combine(recording.Path, "textures", "hires.txt");
}

//§1's reader: a recording's hires.txt is read again only when it changes.
public sealed class RemasterShapeCache
{
	private readonly Dictionary<string, (string Stamp, IReadOnlySet<string>? Keys)> _keys = new(StringComparer.Ordinal);
	private readonly object _lock = new();

	public int? Count(IReadOnlyList<RemasterRecording> recordings)
	{
		List<IReadOnlySet<string>?> sets = new();
		foreach(RemasterRecording r in recordings) {
			sets.Add(r.HasTextures ? KeysOf(RemasterShapesSeen.HiresOf(r)) : null);
		}
		return RemasterShapesSeen.Count(sets);
	}

	private IReadOnlySet<string>? KeysOf(string path)
	{
		string stamp = RemasterFileStamp.Of(path);
		lock(_lock) {
			if(_keys.TryGetValue(path, out var hit) && hit.Stamp == stamp) {
				return hit.Keys;
			}
		}
		IReadOnlySet<string>? keys;
		try {
			keys = RemasterShapesSeen.KeysOf(File.ReadLines(path));
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
			keys = null;
		}
		lock(_lock) {
			_keys[path] = (stamp, keys);
		}
		return keys;
	}
}

public static class RemasterFileStamp
{
	public static string Of(string path)
	{
		if(string.IsNullOrEmpty(path)) {
			return "";
		}
		try {
			FileInfo f = new(path);
			return f.Exists ? f.Length + ":" + f.LastWriteTimeUtc.Ticks : "-";
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) {
			return "-";
		}
	}
}

//One slicing cell: `Key` names the build's cell (`<sidecar path>#<index>`),
//(X, Y) its top-left in 1x pixels of the picture it is measured on, `Size`
//its side, `Pose` the pose it draws ("" when the file does not say).
public sealed record RemasterCellRect(string Key, int X, int Y, int Size, string Pose);

//A tile's measurement: the build cells painted on it, and the poses those
//cells draw. Null = cannot tell (no pre-paint twin, no sidecar, a picture
//this reader cannot compare); Poses is also null without a figure sidecar.
public sealed record RemasterTileCells(IReadOnlySet<string>? Cells, IReadOnlySet<string>? Poses)
{
	public static RemasterTileCells Unknown { get; } = new(null, null);
}

//§2 "N cells painted" and §3 "Painted: N of M phases".
public static class RemasterCellPaint
{
	public const string SidecarExtension = ".json";

	//The cells whose square differs from the twin upscaled by the whole-number
	//ratio of the widths - mep_build's `_EditedProbe.edited`, cell by cell. A
	//cell reaching past the picture counts as painted (as _EditedProbe); a
	//picture that is no whole multiple of its twin is null.
	public static IReadOnlyList<RemasterCellRect>? Painted(RemasterPixels picture, RemasterPixels twin, IEnumerable<RemasterCellRect> rects)
	{
		if(twin.Width == 0 || picture.Width % twin.Width != 0 || picture.Channels != twin.Channels) {
			return null;
		}
		int n = picture.Width / twin.Width;
		if(n < 1 || twin.Height * n != picture.Height) {
			return null;
		}
		List<RemasterCellRect> painted = new();
		foreach(RemasterCellRect r in rects) {
			if(Differs(picture, twin, n, r)) {
				painted.Add(r);
			}
		}
		return painted;
	}

	private static bool Differs(RemasterPixels picture, RemasterPixels twin, int n, RemasterCellRect r)
	{
		int ch = picture.Channels;
		if(r.X < 0 || r.Y < 0 || r.Size <= 0 || (r.X + r.Size) * n > picture.Width || (r.Y + r.Size) * n > picture.Height) {
			return true;
		}
		for(int py = r.Y * n; py < (r.Y + r.Size) * n; py++) {
			int srow = py * picture.Stride;
			int orow = (py / n) * twin.Stride;
			for(int px = r.X * n; px < (r.X + r.Size) * n; px++) {
				int s = srow + px * ch;
				int o = orow + (px / n) * ch;
				for(int c = 0; c < ch; c++) {
					if(picture.Data[s + c] != twin.Data[o + c]) {
						return true;
					}
				}
			}
		}
		return false;
	}

	//An ADR-0153 sheet sidecar's `cells[]` at its `gridUnit` (default 8, as
	//mep_build). Null when the file is no sidecar with cells (a map slices
	//through placements and has no cell ordinal to count).
	public static List<RemasterCellRect>? SheetCells(string sidecarJson, string sidecarPath)
	{
		try {
			using JsonDocument doc = JsonDocument.Parse(sidecarJson);
			JsonElement root = doc.RootElement;
			if(root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("cells", out JsonElement cells) || cells.ValueKind != JsonValueKind.Array) {
				return null;
			}
			int unit = Int(root, "gridUnit", 8);
			unit = unit > 0 ? unit : 8;
			List<RemasterCellRect> rects = new();
			foreach(JsonElement c in cells.EnumerateArray()) {
				if(TryInt(c, "index", out int index) && TryInt(c, "x", out int x) && TryInt(c, "y", out int y)) {
					rects.Add(new RemasterCellRect(sidecarPath + "#" + index, x, y, unit, ""));
				}
			}
			return rects;
		} catch(JsonException) {
			return null;
		}
	}

	//An ADR-0225 §2 figure sidecar (version 2): every cell at its 1x pixel
	//offset on the composed view, keyed by its home cell on the kit sheet
	//(`sheet` + `index`, #498) so paint on either surface is one build cell.
	//A cell naming no home index is left out (no build cell to count).
	public static List<RemasterCellRect>? FigureCells(string sidecarJson, string sheetsFolder)
	{
		try {
			using JsonDocument doc = JsonDocument.Parse(sidecarJson);
			JsonElement root = doc.RootElement;
			if(root.ValueKind != JsonValueKind.Object || Int(root, "version", 0) != 2 || Str(root, "kind") != "figure"
				|| !root.TryGetProperty("cells", out JsonElement cells) || cells.ValueKind != JsonValueKind.Array) {
				return null;
			}
			int unit = Int(root, "unit", 8);
			unit = unit > 0 ? unit : 8;
			List<RemasterCellRect> rects = new();
			foreach(JsonElement c in cells.EnumerateArray()) {
				string sheet = Str(c, "sheet");
				if(sheet.Length == 0 || Path.IsPathRooted(sheet) || sheet.Contains("..", StringComparison.Ordinal)) {
					continue;
				}
				if(TryInt(c, "index", out int index) && TryInt(c, "x", out int x) && TryInt(c, "y", out int y)) {
					rects.Add(new RemasterCellRect(Path.Combine(sheetsFolder, sheet) + "#" + index, x, y, unit, Str(c, "pose")));
				}
			}
			return rects;
		} catch(JsonException) {
			return null;
		}
	}

	//`a/sheets/usr000.png` -> `a/sheets/usr000.json`
	public static string SidecarOf(string png)
	{
		return png.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? png.Substring(0, png.Length - 4) + SidecarExtension : "";
	}

	public static RemasterTileCells Measure(RemasterKitTile tile)
	{
		if(!tile.HasPrePaintTwin || tile.ReferencePath.Length == 0) {
			return RemasterTileCells.Unknown;
		}
		string sidecar = SidecarOf(tile.ImagePath);
		List<RemasterCellRect>? sheetCells = ReadRects(sidecar, json => SheetCells(json, sidecar));
		RemasterPixels? sheet = sheetCells == null ? null : RemasterPng.Read(tile.ImagePath);
		RemasterPixels? twin = sheetCells == null ? null : RemasterPng.Read(tile.ReferencePath);
		IReadOnlyList<RemasterCellRect>? sheetPainted = sheet == null || twin == null ? null : Painted(sheet, twin, sheetCells!);
		if(sheetPainted == null) {
			return RemasterTileCells.Unknown;
		}
		HashSet<string> cells = new(StringComparer.Ordinal);
		foreach(RemasterCellRect r in sheetPainted) {
			cells.Add(r.Key);
		}

		//The composed view (ADR-0225): its cells name their pose and home cell.
		HashSet<string>? poses = null;
		if(tile.FigurePath.Length > 0 && tile.FigureReferencePath.Length > 0) {
			string figureSidecar = SidecarOf(tile.FigurePath);
			string sheetsFolder = Path.GetDirectoryName(sidecar) ?? "";
			List<RemasterCellRect>? figureCells = ReadRects(figureSidecar, json => FigureCells(json, sheetsFolder));
			RemasterPixels? view = figureCells == null ? null : RemasterPng.Read(tile.FigurePath);
			RemasterPixels? viewTwin = figureCells == null ? null : RemasterPng.Read(tile.FigureReferencePath);
			IReadOnlyList<RemasterCellRect>? viewPainted = view == null || viewTwin == null ? null : Painted(view, viewTwin, figureCells!);
			if(viewPainted != null) {
				foreach(RemasterCellRect r in viewPainted) {
					cells.Add(r.Key);
				}
				poses = new HashSet<string>(StringComparer.Ordinal);
				foreach(RemasterCellRect r in figureCells!) {
					if(r.Pose.Length > 0 && cells.Contains(r.Key)) {
						poses.Add(r.Pose);
					}
				}
			}
		}
		return new RemasterTileCells(cells, poses);
	}

	private static List<RemasterCellRect>? ReadRects(string path, Func<string, List<RemasterCellRect>?> parse)
	{
		if(path.Length == 0 || !File.Exists(path)) {
			return null;
		}
		try {
			return parse(File.ReadAllText(path));
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
			return null;
		}
	}

	//§2: the distinct build cells painted over every tile that can tell; null
	//when none can.
	public static int? Total(IEnumerable<RemasterTileCells> tiles)
	{
		HashSet<string>? union = null;
		foreach(RemasterTileCells t in tiles) {
			if(t.Cells == null) {
				continue;
			}
			union ??= new HashSet<string>(StringComparer.Ordinal);
			union.UnionWith(t.Cells);
		}
		return union?.Count;
	}

	//§3: of the tile's phases (its `playsColumns`, the "6 phases" under the
	//caption), those whose column draws a painted pose. A column that plays
	//twice is two phases; a phase this sheet does not draw (null) is never
	//painted here. Null without a play order or without the figure sidecar.
	public static (int Painted, int Of)? Phases(RemasterKitTile tile, RemasterTileCells cells)
	{
		IReadOnlyList<int?> order = tile.PlayOrder;
		if(order.Count == 0 || cells.Poses == null) {
			return null;
		}
		IReadOnlyList<string> ids = tile.PoseIds;
		int painted = 0;
		foreach(int? column in order) {
			if(column is int c && c >= 1 && c <= ids.Count && cells.Poses.Contains(ids[c - 1])) {
				painted++;
			}
		}
		return (painted, order.Count);
	}

	private static string Str(JsonElement e, string key)
	{
		return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
	}

	private static int Int(JsonElement e, string key, int fallback) => TryInt(e, key, out int n) ? n : fallback;

	private static bool TryInt(JsonElement e, string key, out int n)
	{
		n = 0;
		return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out n);
	}
}

//§2/§3 off the UI thread: a tile is measured again only when one of the six
//files it reads changes.
public sealed class RemasterCellPaintCache
{
	private readonly Dictionary<string, (string Stamp, RemasterTileCells Cells)> _results = new(StringComparer.Ordinal);
	private readonly object _lock = new();

	public static string Stamp(RemasterKitTile tile)
	{
		return string.Join("|", RemasterFileStamp.Of(tile.ImagePath), RemasterFileStamp.Of(tile.ReferencePath),
			RemasterFileStamp.Of(RemasterCellPaint.SidecarOf(tile.ImagePath)), RemasterFileStamp.Of(tile.FigurePath),
			RemasterFileStamp.Of(tile.FigureReferencePath), RemasterFileStamp.Of(RemasterCellPaint.SidecarOf(tile.FigurePath)));
	}

	public bool TryGet(RemasterKitTile tile, out RemasterTileCells cells)
	{
		string stamp = Stamp(tile);
		lock(_lock) {
			if(_results.TryGetValue(tile.ImagePath, out var hit) && hit.Stamp == stamp) {
				cells = hit.Cells;
				return true;
			}
		}
		cells = RemasterTileCells.Unknown;
		return false;
	}

	public RemasterTileCells Get(RemasterKitTile tile)
	{
		if(TryGet(tile, out RemasterTileCells cached)) {
			return cached;
		}
		string stamp = Stamp(tile);
		RemasterTileCells cells = RemasterCellPaint.Measure(tile);
		lock(_lock) {
			_results[tile.ImagePath] = (stamp, cells);
		}
		return cells;
	}

	//§2 for a whole kit.
	public int? Total(RemasterKit kit)
	{
		List<RemasterTileCells> all = new();
		foreach(RemasterKitTile t in kit.Tiles) {
			all.Add(Get(t));
		}
		return RemasterCellPaint.Total(all);
	}
}
