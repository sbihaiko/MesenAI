using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.Logic;

//G.7 (PRD Part B §13.5.3 W-R5, the honesty surface): what a tile's popover
//says about where its pixels came from. Every line is a fact a file carries -
//the kit fragment's `seen`/`fill`/`empty` (ADR-0183 §3, ADR-0219), the
//recording the kit was projected from (ADR-0194), the twin comparison
//(RemasterPaintProbe) - or the plain "cannot tell" state when no file says.
//The owning ViewModel maps each kind to its sentence.
public enum RemasterProvenanceKind
{
	//✔ every pixel was recorded in play (`seen: true`).
	Seen,
	//✔ A of B cells seen in play (a pattern page).
	CellsSeen,
	//⚠ A of B cells not seen: filled from the game's own data.
	CellsFilled,
	//A of B cells empty: neither seen nor in the game's data.
	CellsEmpty,
	//⚠ nothing seen: projected over the ROM alone (ADR-0219, a static kit).
	NotSeen,
	//The fragment carries no `seen` at all.
	SeenUnknown,
	//W-R6: cut from the pack the user imported, not from a recording.
	FromImportedPack,
	Painted,
	NotPainted,
	PaintUnknown
}

public sealed record RemasterProvenanceLine(RemasterProvenanceKind Kind, int Count, int Of, RemasterPaintUnknown Why);

public static class RemasterProvenance
{
	public static IReadOnlyList<RemasterProvenanceLine> Lines(RemasterKitTile tile, RemasterPaintResult paint)
	{
		List<RemasterProvenanceLine> lines = new();
		if(tile.Category == RemasterKitCategory.Imported) {
			lines.Add(new(RemasterProvenanceKind.FromImportedPack, 0, 0, RemasterPaintUnknown.None));
		} else if(tile.Category == RemasterKitCategory.PatternPages && tile.SeenCells >= 0) {
			if(tile.SeenCells > 0) {
				lines.Add(new(RemasterProvenanceKind.CellsSeen, tile.SeenCells, tile.Cells, RemasterPaintUnknown.None));
			}
			if(tile.Fill > 0) {
				lines.Add(new(RemasterProvenanceKind.CellsFilled, tile.Fill, tile.Cells, RemasterPaintUnknown.None));
			}
			if(tile.Empty > 0) {
				lines.Add(new(RemasterProvenanceKind.CellsEmpty, tile.Empty, tile.Cells, RemasterPaintUnknown.None));
			}
		} else if(tile.Seen == true) {
			lines.Add(new(RemasterProvenanceKind.Seen, 0, 0, RemasterPaintUnknown.None));
		} else if(tile.Seen == false) {
			lines.Add(new(RemasterProvenanceKind.NotSeen, 0, 0, RemasterPaintUnknown.None));
		} else {
			lines.Add(new(RemasterProvenanceKind.SeenUnknown, 0, 0, RemasterPaintUnknown.None));
		}

		lines.Add(paint.State switch {
			RemasterPaintState.Painted => new(RemasterProvenanceKind.Painted, 0, 0, RemasterPaintUnknown.None),
			RemasterPaintState.Untouched => new(RemasterProvenanceKind.NotPainted, 0, 0, RemasterPaintUnknown.None),
			_ => new(RemasterProvenanceKind.PaintUnknown, 0, 0, paint.Why),
		});
		return lines;
	}

	//The tile's painted state: its picture against its twin, and for a figure
	//grid also its composed view (ADR-0225: either surface may be the one the
	//artist painted). Surfaces whose twin is no pre-paint copy say so.
	public static RemasterPaintResult Paint(RemasterKitTile tile, Func<string, string, RemasterPaintResult> compare)
	{
		if(!tile.HasPrePaintTwin) {
			return RemasterPaintResult.Unknown(RemasterPaintUnknown.NoPrePaintCopy);
		}
		RemasterPaintResult sheet = tile.ReferencePath.Length > 0 ? compare(tile.ImagePath, tile.ReferencePath) : RemasterPaintResult.Unknown(RemasterPaintUnknown.NoTwin);
		if(sheet.State == RemasterPaintState.Painted || tile.FigurePath.Length == 0) {
			return sheet;
		}
		RemasterPaintResult view = tile.FigureReferencePath.Length > 0 ? compare(tile.FigurePath, tile.FigureReferencePath) : RemasterPaintResult.Unknown(RemasterPaintUnknown.NoTwin);
		if(view.State == RemasterPaintState.Painted) {
			return view;
		}
		return sheet.State == RemasterPaintState.Untouched && view.State == RemasterPaintState.Untouched ? RemasterPaintResult.Untouched
			: sheet.State == RemasterPaintState.Unknown ? sheet : view;
	}

	//W-R5's banner (ADR-0198 §3): a project imported against a patched ROM
	//carries the pack's <patch> lines in its manifests; its key namespace is
	//the patched ROM's, so a recording of the stock ROM never lands in it.
	public static bool PaintsPatchedGame(string projectFolder)
	{
		if(string.IsNullOrEmpty(projectFolder)) {
			return false;
		}
		string[] manifests = {
			Path.Combine(projectFolder, "auto", "textures", "hires.txt"),
			Path.Combine(projectFolder, "textures", "hires.txt"),
			Path.Combine(projectFolder, "mep", "textures", "hires.txt"),
		};
		foreach(string m in manifests) {
			if(HasPatchLine(m)) {
				return true;
			}
		}
		//A recording into the patched game keeps its <patch> lines too.
		string auto = Path.Combine(projectFolder, RemasterProjectReader.AutoFolder);
		if(Directory.Exists(auto)) {
			foreach(string rec in Directory.GetDirectories(auto)) {
				if(RemasterProjectReader.RecordingNumber(Path.GetFileName(rec)) > 0 && HasPatchLine(Path.Combine(rec, "textures", "hires.txt"))) {
					return true;
				}
			}
		}
		return false;
	}

	//A `<patch>` line, at column 0 or after a `[condition]` prefix (the loader
	//strips one) - mep_patch.patch_lines' reading.
	public static bool HasPatchLine(string hiresPath)
	{
		if(!File.Exists(hiresPath)) {
			return false;
		}
		try {
			foreach(string raw in File.ReadLines(hiresPath)) {
				string line = raw.TrimStart();
				if(line.StartsWith('[')) {
					int close = line.IndexOf(']');
					line = close > 0 ? line.Substring(close + 1) : line;
				}
				if(line.StartsWith("<patch>", StringComparison.Ordinal)) {
					return true;
				}
			}
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
		}
		return false;
	}
}
