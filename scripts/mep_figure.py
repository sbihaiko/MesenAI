#!/usr/bin/env python3
"""Mark a figure, hand it to the artist's own editor, and bring it back.

ADR-0209 Q2 (e) and Q3 (i), implemented 2026-09-22. The figure is the unit
ADR-0168 names: a `sprNNN` group (or an `objNNN` group) reassembled through
the offsets the pack already records into ONE PNG an artist sees as a
character, never as the fragments the sheet cuts it into. Nothing here paints;
this owns **selection** (which cells make up the figure, laid out as the game
drew them) and **return** (the painted file coming back onto the cells it came
from). The brush stays in GIMP, Aseprite, Krita or Photoshop.

    python3 scripts/mep_figure.py export <pack> spr000 [--out DIR]
    python3 scripts/mep_figure.py import <pack> <DIR>/spr000-figure.png [--verify]

`export` writes three files beside each other, on the same contract every
ADR-0153 sheet follows:

  * `<figure>-figure.png` — the painting surface, at the pack's `<scale>`, cut
    from the sheets that already carry the cells (their painted pixels, so an
    earlier repaint is visible in the figure);
  * `<figure>-figure.orig.png` — the 1x reference twin, never painted, the
    file the return path diffs against (ADR-0153 §3/§4);
  * `<figure>-figure.json` — the sidecar mapping every cell rect of the figure
    back to `(sheet, cell index)`, which is what lets a flat PNG re-import
    cell by cell.

The name is the F12.4 contract (ADR-0213): `require_asset_name` checks the
string an artist pastes as a Photoshop layer name, so the file the paint
program exports is the file `import` reads.

`import` is ADR-0209 Q3 (i) resolved by ADR-0213 §4 into (h): the same path,
re-imported explicitly, no watcher. A cell is written into its source sheet
only where the figure differs from the twin — an unpainted figure changes
nothing, and a cell already carrying those pixels is left alone. The sheet
then reaches the game the way every painted sheet does: `mep_build.py build`
rewrites `hires.txt` to point the key at the painted cell, and *HD Packs >
Reload Repainted Images* (F12.3, ADR-0212) shows it without reopening the ROM.

Where the layout comes from is stated in the sidecar (`source`): `poses` when
`sheets/poses.json` records the silhouette (ADR-0170 §4), `walk` when the pack
predates it and the ADR-0168 §2 evidence walk had to guess — a heuristic, and
the members it could not place are listed under `unplaced`, never drawn at a
guessed position (ADR-0168 §3). `pose` when the figure was named by pose id.

Stdlib only (ADR-0165). Reads the pack through `compose_engine`, the pixels
through `sheet_repaint`.
"""

import argparse
import json
import re
import shutil
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import asset_names as N  # noqa: E402 — F12.4 painting-surface name contract
import compose_engine as E  # noqa: E402
import mep_build  # noqa: E402
import sheet_repaint  # noqa: E402

FIGURE_VERSION = 1
FIGURE_SUFFIX = "-figure"
_POSE_ID = re.compile(r"^pose\d{3,}$")


class FigureError(Exception):
    pass


# ---- selection: which cells, laid out how ----------------------------------

class Figure:
    """A resolved figure: its layout in cell units plus where it came from."""

    __slots__ = ("id", "kind", "layout", "source", "pose_id", "group", "unplaced")

    def __init__(self, figure_id, kind, layout, source, pose_id=None, group=None, unplaced=()):
        self.id = figure_id
        self.kind = kind            # "sprite" or "object"
        self.layout = layout        # {node: (dx, dy)} in cell units
        self.source = source        # "poses" | "walk" | "pose"
        self.pose_id = pose_id
        self.group = group          # the sprNNN/objNNN Sheet, or None for a bare pose
        self.unplaced = tuple(unplaced)


def _group_sheet(pack: E.Pack, stem: str):
    for sheet in pack.sheets:
        if sheet.json_path.stem == stem:
            return sheet
    return None


def resolve_figure(pack: E.Pack, figure_id: str) -> Figure:
    """`sprNNN`/`objNNN` (a group sheet) or `poseNNN` (a `poses.json` entry)."""
    if _POSE_ID.match(figure_id):
        if pack.poses is None:
            raise FigureError(f"{figure_id}: this pack has no usable sheets/poses.json")
        pose = pack.poses.by_id(figure_id)
        if pose is None:
            raise FigureError(f"{figure_id}: no such pose in sheets/poses.json")
        return Figure(figure_id, "sprite", pose.layout(), "pose", pose_id=pose.id)

    sheet = _group_sheet(pack, figure_id)
    if sheet is None:
        raise FigureError(f"{figure_id}: no {figure_id}.json under {pack.sheets_dir}")
    if sheet.kind not in ("sprite", "object"):
        raise FigureError(f"{figure_id}: kind {sheet.kind!r} is not a sprNNN/objNNN group sheet")
    nodes = pack.group_nodes(figure_id)
    if not nodes:
        raise FigureError(f"{figure_id}: the group sheet has no cells")
    if sheet.kind == "sprite":
        layout, source, pose = pack.figure_layout_detail(figure_id)
        pose_id = pose.id if pose is not None else None
    else:
        # Poses are an OAM record; a background group only has its walk.
        layout, source, pose_id = E.walk_layout(nodes, sheet.doc.doc.get("evidence")), "walk", None
    unplaced = [n for n in nodes if n not in layout]
    if not layout:
        raise FigureError(f"{figure_id}: nothing could be laid out")
    return Figure(figure_id, sheet.kind, layout, source, pose_id, sheet, unplaced)


def home_cell(pack: E.Pack, figure: Figure, node: int):
    """`(Sheet, cell)` the node's paint lands on: the group sheet's own cell
    first (that is the unit ADR-0209 (e) exports, and `_SHEET_RANK` lets a
    painted group cell win over the vocabulary sheet), else the vocabulary
    sheet that shows the node. None when no sheet shows it."""
    if figure.group is not None:
        for cell in figure.group.cells:
            if isinstance(cell, dict) and cell.get("metatile") == node:
                return (figure.group, cell)
    if figure.kind == "sprite":
        return pack.sprite_home(node)
    return pack.background_home(node)


def figure_stem(figure_id: str) -> str:
    return f"{figure_id}{FIGURE_SUFFIX}"


# ---- export ------------------------------------------------------------------

def export_figure(pack: E.Pack, figure_id: str, out_dir: Path) -> dict:
    """Write `<stem>.png`, `<stem>.orig.png`, `<stem>.json` into `out_dir`
    and return the sidecar document."""
    figure = resolve_figure(pack, figure_id)
    scale = pack.scale
    stem = figure_stem(figure_id)
    name = f"{stem}.png"
    N.require_asset_name(name, where=f"figure {figure_id}")

    xs = [p[0] for p in figure.layout.values()]
    ys = [p[1] for p in figure.layout.values()]
    min_x, min_y = min(xs), min(ys)
    cols, rows = max(xs) - min_x + 1, max(ys) - min_y + 1

    unit = None
    cells = []
    unresolved = []
    placed = sorted(figure.layout.items(), key=lambda kv: (kv[1][1], kv[1][0], kv[0]))
    for node, (dx, dy) in placed:
        home = home_cell(pack, figure, node)
        if home is None:
            unresolved.append(node)
            continue
        sheet, cell = home
        if unit is None:
            unit = sheet.unit
        elif sheet.unit != unit:
            raise FigureError(f"{figure_id}: node {node} lives on {sheet.name} at unit "
                              f"{sheet.unit}, the figure is at unit {unit}")
        entry = {
            "node": node, "dx": dx, "dy": dy,
            "x": (dx - min_x) * unit, "y": (dy - min_y) * unit,
            "sheet": sheet.json_path.name,
            "sheetX": int(cell["x"]), "sheetY": int(cell["y"]),
        }
        if isinstance(cell.get("index"), int):
            entry["index"] = cell["index"]
        cells.append(entry)
    if not cells:
        raise FigureError(f"{figure_id}: no sheet shows any of its cells")

    canvas_1x = sheet_repaint.Image(cols * unit, rows * unit)
    canvas = sheet_repaint.Image(cols * unit * scale, rows * unit * scale)
    for entry in cells:
        sheet, cell = home_cell(pack, figure, entry["node"])
        art = sheet.cell_image(cell)
        canvas_1x.paste(art, entry["x"], entry["y"])
        painted = sheet.cell_image_painted(cell, scale) if scale > 1 else None
        canvas.paste(painted if painted is not None else art.upscale(scale),
                     entry["x"] * scale, entry["y"] * scale)

    out_dir.mkdir(parents=True, exist_ok=True)
    sheet_repaint.write_png(out_dir / name, canvas)
    sheet_repaint.write_png(out_dir / f"{stem}.orig.png", canvas_1x)
    doc = {
        "version": FIGURE_VERSION,
        "kind": "figure",
        "figure": figure_id,
        "figureKind": figure.kind,
        "source": figure.source,
        "pose": figure.pose_id,
        "unit": unit,
        "scale": scale,
        "size": [cols, rows],
        "sheet": name,
        "reference": f"{stem}.orig.png",
        "assetName": N.asset_name_for(name),
        "cells": cells,
        "unplaced": list(figure.unplaced),
        "unresolved": unresolved,
    }
    (out_dir / f"{stem}.json").write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    return doc


# ---- import ------------------------------------------------------------------

def _load_figure_doc(png_path: Path):
    json_path = png_path.with_name(png_path.name[:-len(".png")] + ".json")
    if not json_path.is_file():
        raise FigureError(f"{png_path.name}: no sidecar {json_path.name} beside it")
    try:
        doc = json.loads(json_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        raise FigureError(f"{json_path.name}: not readable as JSON ({e})")
    if not isinstance(doc, dict) or doc.get("version") != FIGURE_VERSION or doc.get("kind") != "figure":
        raise FigureError(f"{json_path.name}: not a version {FIGURE_VERSION} figure sidecar")
    return doc


def _find_cell(sheet: E.Sheet, entry: dict):
    idx = entry.get("index")
    for cell in sheet.cells:
        if not isinstance(cell, dict):
            continue
        if idx is not None and cell.get("index") == idx:
            return cell
        if idx is None and int(cell.get("x", -1)) == entry["sheetX"] and int(cell.get("y", -1)) == entry["sheetY"]:
            return cell
    return None


def import_figure(pack: E.Pack, png_path: Path) -> dict:
    """Write the painted cells of a figure back into the sheets they came
    from. Only a cell that differs from the `*.orig.png` twin is written; a
    cell the sheet already holds is left alone. Returns a report."""
    png_path = Path(png_path)
    if not png_path.is_file():
        raise FigureError(f"{png_path}: not a file")
    doc = _load_figure_doc(png_path)
    twin_path = png_path.with_name(str(doc.get("reference") or ""))
    if not twin_path.is_file():
        raise FigureError(f"{png_path.name}: reference twin {twin_path.name} is missing")
    figure = sheet_repaint.read_png(png_path)
    twin = sheet_repaint.read_png(twin_path)
    if (twin.width <= 0 or twin.height <= 0 or figure.width % twin.width
            or figure.height % twin.height or figure.width // twin.width != figure.height // twin.height):
        raise FigureError(
            f"{png_path.name}: {figure.width}x{figure.height} is not a whole multiple of the "
            f"{twin.width}x{twin.height} twin — the canvas was resized; repaint at the size you were given")
    scale = figure.width // twin.width
    unit = int(doc.get("unit") or 8)

    report = {"figure": doc.get("figure"), "scale": scale, "cells": 0, "painted": 0,
              "written": 0, "alreadyApplied": 0, "sheets": []}
    canvases = {}   # sheet name -> (Sheet, Image, dirty)
    for entry in doc.get("cells") or []:
        report["cells"] += 1
        x, y = int(entry["x"]), int(entry["y"])
        if x + unit > twin.width or y + unit > twin.height:
            raise FigureError(f"{png_path.name}: cell for node {entry.get('node')} falls outside the twin")
        fig_cell = figure.crop(x * scale, y * scale, unit * scale, unit * scale)
        ref_cell = twin.crop(x, y, unit, unit).upscale(scale)
        if fig_cell.px == ref_cell.px:
            continue  # not painted: ADR-0153 §3, the twin decides
        report["painted"] += 1
        sheet = _group_sheet(pack, Path(str(entry["sheet"])).stem)
        if sheet is None:
            raise FigureError(f"{entry['sheet']}: no such sheet under {pack.sheets_dir}")
        cell = _find_cell(sheet, entry)
        if cell is None:
            raise FigureError(f"{entry['sheet']}: cell for node {entry.get('node')} is gone")
        if sheet.unit != unit:
            raise FigureError(f"{sheet.name}: unit {sheet.unit} does not match the figure's {unit}")
        if sheet.name not in canvases:
            if sheet.scale != scale:
                raise FigureError(
                    f"{sheet.name}: painted at {sheet.scale}x while the figure is at {scale}x — "
                    "all sheets of a pack share one <scale>")
            canvases[sheet.name] = [sheet, sheet_repaint.read_png(sheet.png_path), False]
        _, img, _ = canvases[sheet.name]
        sx, sy = int(cell["x"]) * scale, int(cell["y"]) * scale
        if sx + unit * scale > img.width or sy + unit * scale > img.height:
            raise FigureError(f"{sheet.name}: cell ({cell['x']},{cell['y']}) falls outside the sheet")
        if img.crop(sx, sy, unit * scale, unit * scale).px == fig_cell.px:
            report["alreadyApplied"] += 1
            continue
        img.paste(fig_cell, sx, sy)
        canvases[sheet.name][2] = True
        report["written"] += 1
    for sheet, img, dirty in canvases.values():
        if dirty:
            sheet_repaint.write_png(sheet.png_path, img)
            report["sheets"].append(sheet.name)
    return report


# ---- verify (ADR-0183 §4) ----------------------------------------------------

def hires_keys(path: Path) -> set:
    """The `(tileData, palette)` pairs a `hires.txt` keys, as a set."""
    keys = set()
    if not path.is_file():
        return keys
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        m = mep_build._TILE_RE.match(line.strip())
        if not m:
            continue
        fields = [f.strip() for f in m.group(2).split(",")]
        if len(fields) >= 3:
            keys.add((fields[1].upper(), fields[2].upper()))
    return keys


def figure_sheet_bytes(pack: E.Pack, png_path: Path) -> dict:
    """`{sheet png name: bytes}` for every sheet a figure's sidecar names, as
    they are on disk right now — the control `verify` restores."""
    doc = _load_figure_doc(Path(png_path))
    out = {}
    for entry in doc.get("cells") or []:
        sheet = _group_sheet(pack, Path(str(entry.get("sheet") or "")).stem)
        if sheet is not None and sheet.png_path.is_file() and sheet.name not in out:
            out[sheet.name] = sheet.png_path.read_bytes()
    return out


def verify(pack_dir: Path, restore=None, scratch=None) -> dict:
    """Rebuild two throwaway copies of the pack — a control with the sheets in
    `restore` put back to their pre-import bytes, and the pack as it is — and
    compare the key sets of the two rebuilt `hires.txt`: nothing lost, nothing
    invented (ADR-0183 §4).

    The control is what makes this honest on a recorded pack: a bootstrap
    `hires.txt` carries every CHR tile (ADR-0043) while a rebuild carries only
    what the sheets hold, so "keys on disk vs rebuilt" would fail on every
    first build for a reason that has nothing to do with the figure. That
    drift is reported separately as `drift_from_recording`."""
    recorded = hires_keys(pack_dir / "textures" / "hires.txt")
    with tempfile.TemporaryDirectory(dir=scratch) as td:
        control = Path(td) / "control"
        shutil.copytree(pack_dir, control)
        for name, data in (restore or {}).items():
            (control / "textures" / "sheets" / name).write_bytes(data)
        rc_control = mep_build.main(["build", str(control), "--quiet"])
        before = hires_keys(control / "textures" / "hires.txt")
        work = Path(td) / "pack"
        shutil.copytree(pack_dir, work)
        rc = mep_build.main(["build", str(work), "--quiet"])
        after = hires_keys(work / "textures" / "hires.txt")
    report = {"errors": rc, "control_errors": rc_control,
              "keys_before": len(before), "keys_after": len(after),
              "lost": len(before - after), "added": len(after - before),
              "drift_from_recording": len(recorded ^ before)}
    report["ok"] = rc == 0 and report["lost"] == 0 and report["added"] == 0
    return report


# ---- CLI ---------------------------------------------------------------------

def _open_pack(folder: str) -> E.Pack:
    try:
        return E.Pack(Path(folder))
    except E.ComposeError as e:
        raise FigureError(str(e))


def cmd_export(args) -> int:
    pack = _open_pack(args.pack)
    out_dir = Path(args.out) if args.out else Path(args.pack).resolve().parent / "kit" / "figures"
    doc = export_figure(pack, args.figure, out_dir)
    print(f"{doc['sheet']}: {len(doc['cells'])} cells, {doc['size'][0]}x{doc['size'][1]} "
          f"at unit {doc['unit']}, scale {doc['scale']}x, layout from {doc['source']}"
          + (f" ({doc['pose']})" if doc["pose"] else ""))
    print(f"  surface   {out_dir / doc['sheet']}")
    print(f"  reference {out_dir / doc['reference']}  (never paint this one)")
    print(f"  layer name to paste in Photoshop: {doc['assetName']}")
    if doc["unplaced"]:
        print(f"  {len(doc['unplaced'])} group member(s) the walk could not place, not drawn: "
              + ", ".join(str(n) for n in doc["unplaced"]))
    if doc["unresolved"]:
        print(f"  {len(doc['unresolved'])} member(s) no sheet shows, left out: "
              + ", ".join(str(n) for n in doc["unresolved"]))
    return 0


def cmd_import(args) -> int:
    pack = _open_pack(args.pack)
    restore = figure_sheet_bytes(pack, Path(args.figure_png)) if args.verify else None
    report = import_figure(pack, Path(args.figure_png))
    print(f"{report['figure']}: {report['cells']} cells, {report['painted']} painted, "
          f"{report['written']} written, {report['alreadyApplied']} already on the sheet")
    for name in report["sheets"]:
        print(f"  wrote {pack.sheets_dir / name}")
    if report["written"]:
        print("  next: python3 scripts/mep_build.py build <pack>, then HD Packs > Reload Repainted Images")
    if args.verify:
        v = verify(Path(args.pack), restore, args.scratch)
        print(f"verify: build errors {v['errors']}, keys {v['keys_before']} -> {v['keys_after']}, "
              f"{v['lost']} lost, {v['added']} added: {'PASS' if v['ok'] else 'FAIL'}")
        if v["drift_from_recording"]:
            print(f"  ({v['drift_from_recording']} key(s) differ between the recorded hires.txt and a "
                  "rebuild without this figure — the bootstrap-vs-sheets drift ADR-0172 accepted, "
                  "not this import's doing)")
        return 0 if v["ok"] else 1
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(
        prog="mep_figure.py",
        description="Export a sprNNN/objNNN figure as one PNG for the artist's own paint program, "
                    "and bring the painted file back onto the sheet cells it came from (ADR-0209 Q2/Q3).")
    sub = ap.add_subparsers(dest="cmd")
    ex = sub.add_parser("export", help="write <figure>-figure.png/.orig.png/.json")
    ex.add_argument("pack", help="the recorded pack folder (the one holding textures/)")
    ex.add_argument("figure", help="sprNNN, objNNN or poseNNN")
    ex.add_argument("--out", help="folder for the three files (default: kit/figures beside the pack)")
    ex.set_defaults(func=cmd_export)
    im = sub.add_parser("import", help="write the painted cells of a figure back into its sheets")
    im.add_argument("pack", help="the same pack folder the figure was exported from")
    im.add_argument("figure_png", help="the painted <figure>-figure.png (its .json and .orig.png beside it)")
    im.add_argument("--verify", action="store_true",
                    help="rebuild a throwaway copy and assert the (tileData, palette) key set is unchanged")
    im.add_argument("--scratch", help="directory for --verify's temporary copy")
    im.set_defaults(func=cmd_import)
    args = ap.parse_args(argv)
    if not hasattr(args, "func"):
        ap.print_usage()
        return 2
    try:
        return args.func(args)
    except (FigureError, E.ComposeError, N.AssetNameError, sheet_repaint.RepaintError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
