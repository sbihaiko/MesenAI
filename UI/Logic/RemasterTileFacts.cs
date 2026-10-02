using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.Logic;

//G.7 (PRD Part B §13.5.3 W-R1 zone ②, W-R5): the host-free facts one tile
//shows - which category the strip opens on, the count under the caption,
//where the pixels came from, which file a click opens - and the cache that
//keeps the twin comparison off every repaint. The ViewModel only maps these
//to strings.
public enum RemasterTileCountKind
{
	None,
	Phases,
	Cells
}

public enum RemasterTileSource
{
	//A per-recording kit (ADR-0194): "from recording N".
	Recording,
	//The union pattern pages (`artist_chr_kit.py --also`): every recording.
	EveryRecording,
	//W-R6: the pack the project was imported from.
	ImportedPack
}

public static class RemasterTileFacts
{
	//The strip keeps the category the user picked while it still has tiles,
	//else the first one in reading order that has any (null = no tiles).
	public static RemasterKitCategory? Pick(RemasterKit kit, RemasterKitCategory? current)
	{
		if(current is RemasterKitCategory c && kit.Count(c) > 0) {
			return c;
		}
		foreach(RemasterKitCategory candidate in Enum.GetValues<RemasterKitCategory>()) {
			if(kit.Count(candidate) > 0) {
				return candidate;
			}
		}
		return null;
	}

	//The chips the strip shows: a category with no tiles is left out rather
	//than shown as a disabled chip (a kit never writes stage maps today).
	public static IReadOnlyList<RemasterKitCategory> Shown(RemasterKit kit)
	{
		List<RemasterKitCategory> shown = new();
		foreach(RemasterKitCategory c in Enum.GetValues<RemasterKitCategory>()) {
			if(kit.Count(c) > 0) {
				shown.Add(c);
			}
		}
		return shown;
	}

	//"6 phases" for an animation the kit says has phases, else its cells;
	//nothing when the kit carries neither (an imported sheet).
	public static (RemasterTileCountKind Kind, int Count) CountOf(RemasterKitTile tile)
	{
		if(tile.Phases > 0) {
			return (RemasterTileCountKind.Phases, tile.Phases);
		}
		return tile.Cells > 0 ? (RemasterTileCountKind.Cells, tile.Cells) : (RemasterTileCountKind.None, 0);
	}

	public static RemasterTileSource SourceOf(RemasterKitTile tile)
	{
		if(tile.Category == RemasterKitCategory.Imported) {
			return RemasterTileSource.ImportedPack;
		}
		return tile.RecordingId.Length > 0 ? RemasterTileSource.Recording : RemasterTileSource.EveryRecording;
	}

	//The ⚠ on the tile: some of it was not seen in play.
	public static bool Warns(IReadOnlyList<RemasterProvenanceLine> lines)
	{
		foreach(RemasterProvenanceLine l in lines) {
			if(l.Kind == RemasterProvenanceKind.CellsFilled || l.Kind == RemasterProvenanceKind.NotSeen) {
				return true;
			}
		}
		return false;
	}

	//What a click opens with the OS default: a figure grid's composed view
	//(ADR-0225, `mep_figure.py export`'s surface, already written by the kit)
	//when there is one, else the picture itself. Never the `.ora` (ADR-0220
	//stop condition 2; PRD W-R1 notes).
	public static string OpenPath(RemasterKitTile tile) => tile.FigurePath.Length > 0 ? tile.FigurePath : tile.ImagePath;

	//The picture the tile shows, the same surface the click opens.
	public static string ThumbnailPath(RemasterKitTile tile) => OpenPath(tile);
}

//The twin comparison decodes two PNGs per surface (~6 ms a tile on a real
//Contra kit), so a result is kept until any of the files it read changes.
public sealed class RemasterPaintCache
{
	private readonly Dictionary<string, (string Stamp, RemasterPaintResult Result)> _results = new(StringComparer.Ordinal);
	private readonly object _lock = new();

	public static string Stamp(RemasterKitTile tile)
	{
		return string.Join("|", StampOf(tile.ImagePath), StampOf(tile.ReferencePath), StampOf(tile.FigurePath), StampOf(tile.FigureReferencePath));
	}

	private static string StampOf(string path)
	{
		if(path.Length == 0) {
			return "";
		}
		try {
			FileInfo f = new(path);
			return f.Exists ? f.Length + ":" + f.LastWriteTimeUtc.Ticks : "-";
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) {
			return "-";
		}
	}

	public bool TryGet(RemasterKitTile tile, out RemasterPaintResult result)
	{
		string stamp = Stamp(tile);
		lock(_lock) {
			if(_results.TryGetValue(tile.ImagePath, out var hit) && hit.Stamp == stamp) {
				result = hit.Result;
				return true;
			}
		}
		result = RemasterPaintResult.Unknown(RemasterPaintUnknown.None);
		return false;
	}

	public RemasterPaintResult Get(RemasterKitTile tile, Func<string, string, RemasterPaintResult> compare)
	{
		if(TryGet(tile, out RemasterPaintResult cached)) {
			return cached;
		}
		string stamp = Stamp(tile);
		RemasterPaintResult result = RemasterProvenance.Paint(tile, compare);
		lock(_lock) {
			_results[tile.ImagePath] = (stamp, result);
		}
		return result;
	}
}
