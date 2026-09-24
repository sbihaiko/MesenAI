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
re-imported explicitly, no watcher. A cell is written only where the figure
differs from the twin — an unpainted figure changes nothing, and a cell
already carrying those pixels is left alone. It is written where the pack's
built `hires.txt` already draws its key from (#413): the source cell when
that cell owns the key, else the owning crop of another sheet (a kit
project's `usrNNN` row) and not the source. So `mep_build.py build` changes
no rule, and *HD Packs > Reload Repainted Images* (F12.3, ADR-0212) shows the
paint without reopening the ROM; an import that must re-point a key says so.
A later `export` shows paint routed that way.

Where the layout comes from is stated in the sidecar (`source`): `poses` when
`sheets/poses.json` records the silhouette (ADR-0170 §4), `walk` when the pack
predates it and the ADR-0168 §2 evidence walk had to guess — a heuristic, and
the members it could not place are listed under `unplaced`, never drawn at a
guessed position (ADR-0168 §3). `pose` when the figure was named by pose id.

Stdlib only (ADR-0165). Reads the pack through `compose_engine`, the pixels
through `sheet_repaint`.
"""

import argparse
import contextlib
import io
import json
import re
import shutil
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import asset_names as N  # noqa: E402 — F12.4 painting-surface name contract
import compose_engine as E  # noqa: E402
import mep_addition  # noqa: E402 — the one key spelling build and lint share
import mep_build  # noqa: E402
import ora_writer  # noqa: E402 — ADR-0220: the layered .ora beside the figure
import sheet_repaint  # noqa: E402

# ADR-0225 §2: version 2 places cells at native-pixel offsets (`x`/`y` are 1x
# pixels from the figure's top-left, `z` present when cells overlap). Import
# still reads version 1 — its cells are on the 8 px grid, a special case.
FIGURE_VERSION = 2
FIGURE_VERSIONS_READ = (1, 2)
FIGURE_SUFFIX = "-figure"
_POSE_ID = re.compile(r"^pose\d{3,}$")


class FigureError(Exception):
    pass


# ---- selection: which cells, laid out how ----------------------------------

class Figure:
    """A resolved figure: its layout in cell units plus where it came from."""

    __slots__ = ("id", "kind", "layout", "source", "pose_id", "group", "unplaced", "pixels", "z")

    def __init__(self, figure_id, kind, layout, source, pose_id=None, group=None, unplaced=(),
                 pixels=None, z=None):
        self.id = figure_id
        self.kind = kind            # "sprite" or "object"
        self.layout = layout        # {node: (dx, dy)} in cell units
        self.source = source        # "poses" | "walk" | "pose"
        self.pose_id = pose_id
        self.group = group          # the sprNNN/objNNN Sheet, or None for a bare pose
        self.unplaced = tuple(unplaced)
        # ADR-0225: {node: (px, py)} native pixels when a pose supplied the
        # layout (poses.json px/py, or dx*unit on an older sidecar); None on
        # the walk path, which only knows cells. {node: z} when tiles overlap.
        self.pixels = pixels
        self.z = dict(z or {})


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
        return Figure(figure_id, "sprite", pose.layout(), "pose", pose_id=pose.id,
                      pixels=pose.pixels, z=pose.z)

    sheet = _group_sheet(pack, figure_id)
    if sheet is None:
        raise FigureError(f"{figure_id}: no {figure_id}.json under {pack.sheets_dir}")
    if sheet.kind not in ("sprite", "object"):
        raise FigureError(f"{figure_id}: kind {sheet.kind!r} is not a sprNNN/objNNN group sheet")
    nodes = pack.group_nodes(figure_id)
    if not nodes:
        raise FigureError(f"{figure_id}: the group sheet has no cells")
    pose = None
    if sheet.kind == "sprite":
        layout, source, pose = pack.figure_layout_detail(figure_id)
        pose_id = pose.id if pose is not None else None
    else:
        # Poses are an OAM record; a background group only has its walk.
        layout, source, pose_id = E.walk_layout(nodes, sheet.doc.doc.get("evidence")), "walk", None
    unplaced = [n for n in nodes if n not in layout]
    if not layout:
        raise FigureError(f"{figure_id}: nothing could be laid out")
    return Figure(figure_id, sheet.kind, layout, source, pose_id, sheet, unplaced,
                  pixels=pose.pixels if pose is not None else None,
                  z=pose.z if pose is not None else None)


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


# ---- caption (ADR-0209 Q1 (b), ADR-0183 §5) ----------------------------------

def load_names(path):
    """The optional human caption file (`--names`, the schema of
    `artist_kit.py`'s names file): `poses{id: name | {name}}`, `cycles{}`,
    and `figures{sprNNN|objNNN: name}` for a group a human named directly.
    Missing or unreadable is an empty file, never an error — a caption is
    never evidence and must not cost an export."""
    if not path:
        return {}
    try:
        doc = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return doc if isinstance(doc, dict) else {}


def _human_name(names: dict, section: str, key):
    table = names.get(section) if isinstance(names, dict) else None
    if not isinstance(table, dict) or key is None:
        return None
    entry = table.get(key)
    if isinstance(entry, dict):
        entry = entry.get("name")
    return entry if isinstance(entry, str) and entry.strip() else None


def figure_caption(pack: E.Pack, figure: Figure, names=None):
    """`(text, source)` for a figure: a human's name from `names` (the figure's
    own id first, then its pose) > the sidecar's `label` (the group sheet's
    for sprNNN/objNNN, the pose entry's for poseNNN) > the id. `source` says
    which won — `names`, the label's own `labelSource` (`inferred` when the
    Core wrote it), or `id`."""
    names = names or {}
    human = _human_name(names, "figures", figure.id) or _human_name(names, "poses", figure.pose_id)
    if figure.group is not None:
        label, label_source = figure.group.label, figure.group.label_source
    else:
        pose = pack.poses.by_id(figure.pose_id) if pack.poses is not None and figure.pose_id else None
        label, label_source = (pose.label, pose.label_source) if pose is not None else ("", None)
    return E.caption(figure.id, label, label_source, human)


# ---- export ------------------------------------------------------------------

def figure_palettes(pack: E.Pack, figure: Figure, cells):
    """`(swatches, labels)` for the figure's `palettes` band: first-use order
    in the figure's **reading order** (row `dy`, then column `dx` - the order
    `export_figure` places cells), each group labelled the way the `guides`
    layer labels the cell: its home-sheet `index`, else its position in the
    figure. Same contract as `compose_engine.Pack.export` and
    `artist_map.panorama_palettes` (2026-09-23 follow-up, defect (b))."""
    labelled = []
    for i, c in enumerate(cells):
        home = home_cell(pack, figure, c["node"])
        tiles = (home[1].get("tiles") or []) if home else []
        labelled.append((c.get("index", i), [t.get("palette") for t in tiles
                                             if isinstance(t, dict) and t.get("palette")]))
    return ora_writer.first_use_palettes(labelled)


def figure_cells(pack: E.Pack, figure: Figure, origin=(0, 0), pose_id=None):
    """`(cells, unresolved, unit)`: one sidecar entry per placed node of
    `figure`, `x`/`y` in 1x native pixels from `origin` (ADR-0225 §2). A
    pose-backed figure places each cell at its `px`/`py`; the walk path only
    knows cells, so it places at `dx * unit`. `z` is copied when the pose
    carries it (its tiles overlap)."""
    unit = None
    cells, unresolved, homes = [], [], []
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
            raise FigureError(f"{figure.id}: node {node} lives on {sheet.name} at unit "
                              f"{sheet.unit}, the figure is at unit {unit}")
        homes.append((node, dx, dy, sheet, cell))
    if unit is None:
        return [], unresolved, None
    pixels = figure.pixels or {n: (dx * unit, dy * unit) for n, (dx, dy) in figure.layout.items()}
    x0 = min(pixels[n][0] for n in figure.layout)
    y0 = min(pixels[n][1] for n in figure.layout)
    for node, dx, dy, sheet, cell in homes:
        entry = {
            "node": node, "dx": dx, "dy": dy,
            "x": origin[0] + pixels[node][0] - x0, "y": origin[1] + pixels[node][1] - y0,
            "sheet": sheet.json_path.name,
            "sheetX": int(cell["x"]), "sheetY": int(cell["y"]),
        }
        if isinstance(cell.get("index"), int):
            entry["index"] = cell["index"]
        if node in figure.z:
            entry["z"] = figure.z[node]
        if pose_id is not None:
            entry["pose"] = pose_id
        cells.append(entry)
    return cells, unresolved, unit


def _overlaps(cells, unit):
    """Per cell index, the other cells whose `unit` square shares a pixel."""
    out = [[] for _ in cells]
    for i, a in enumerate(cells):
        for j in range(i + 1, len(cells)):
            b = cells[j]
            if abs(a["x"] - b["x"]) < unit and abs(a["y"] - b["y"]) < unit:
                out[i].append(j)
                out[j].append(i)
    return out


def _front_key(cells, i):
    """Front-to-back order (ADR-0225 §1): lowest `z` first, then list order."""
    return (cells[i].get("z", 0), i)


def _paste_over(dst, src, x, y):
    """Paste `src` skipping fully transparent pixels — how a sprite drawn in
    front of another shows the one behind through its holes."""
    for row in range(src.height):
        for col in range(src.width):
            o = src.offset(col, row)
            if src.px[o + 3]:
                d = dst.offset(x + col, y + row)
                dst.px[d:d + 4] = src.px[o:o + 4]


def compose_cells(pack: E.Pack, cells, unit, scale, home_of, manifest=None):
    """`(canvas_1x, canvas)` for placed cells: sized to the cells' pixel
    extent, drawn back to front so the frontmost opaque pixel wins where
    cells overlap. No gutter inside a figure (ADR-0225 §2). `home_of(entry)`
    returns the `(Sheet, cell)` an entry's art comes from. With `manifest`
    (`manifest_owners`), paint an earlier import routed to the sheet that owns
    a cell's key is shown too (#413)."""
    width = max(c["x"] for c in cells) + unit
    height = max(c["y"] for c in cells) + unit
    canvas_1x = sheet_repaint.Image(width, height)
    canvas = sheet_repaint.Image(width * scale, height * scale)
    overlaps = _overlaps(cells, unit)
    for i in sorted(range(len(cells)), key=lambda i: _front_key(cells, i), reverse=True):
        entry = cells[i]
        sheet, cell = home_of(entry)
        art = sheet.cell_image(cell)
        painted = sheet.cell_image_painted(cell, scale) if scale > 1 else None
        big = painted if painted is not None else art.upscale(scale)
        if manifest is not None:
            big = routed_paint(pack, sheet, cell, scale, manifest, big)
        if overlaps[i]:
            _paste_over(canvas_1x, art, entry["x"], entry["y"])
            _paste_over(canvas, big, entry["x"] * scale, entry["y"] * scale)
        else:
            canvas_1x.paste(art, entry["x"], entry["y"])
            canvas.paste(big, entry["x"] * scale, entry["y"] * scale)
    return canvas_1x, canvas


def export_figure(pack: E.Pack, figure_id: str, out_dir: Path, names=None) -> dict:
    """Write `<stem>.png`, `<stem>.orig.png`, `<stem>.json` into `out_dir`
    and return the sidecar document."""
    figure = resolve_figure(pack, figure_id)
    scale = pack.scale
    stem = figure_stem(figure_id)
    label, label_source = figure_caption(pack, figure, names)
    name = f"{stem}.png"
    N.require_asset_name(name, where=f"figure {figure_id}")

    xs = [p[0] for p in figure.layout.values()]
    ys = [p[1] for p in figure.layout.values()]
    cols, rows = max(xs) - min(xs) + 1, max(ys) - min(ys) + 1

    cells, unresolved, unit = figure_cells(pack, figure)
    if not cells:
        raise FigureError(f"{figure_id}: no sheet shows any of its cells")
    canvas_1x, canvas = compose_cells(pack, cells, unit, scale,
                                      lambda e: home_cell(pack, figure, e["node"]),
                                      manifest_owners(pack))

    out_dir.mkdir(parents=True, exist_ok=True)
    # ADR-0220 §1: the layered .ora from this same canvas, beside the pair.
    # Four layers — no `context`: a figure has no stage position (§3).
    swatches, swatch_labels = figure_palettes(pack, figure, cells)
    ora_writer.write_surface(
        out_dir, name, canvas, canvas_1x,
        [{"index": c.get("index", i), "x": c["x"] * scale, "y": c["y"] * scale,
          "w": unit * scale, "h": unit * scale} for i, c in enumerate(cells)],
        captions=[(0, 0, f"{figure_id} {figure.pose_id or ''}".strip())],
        swatches=swatches, swatch_labels=swatch_labels)
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
        # ADR-0225 §2: the 1x canvas is the figure's pixel extent; cells[]
        # x/y are 1x native pixels (multiply by `scale` on the painted PNG).
        "pixelSize": [canvas_1x.width, canvas_1x.height],
        "sheet": name,
        "reference": f"{stem}.orig.png",
        "assetName": N.asset_name_for(name),
        # ADR-0209 Q1 (b) / ADR-0183 §5: the caption and where it came from
        # (names > sidecar label > id). "inferred" means the Core wrote it.
        "label": label,
        "labelSource": label_source,
        "cells": cells,
        "unplaced": list(figure.unplaced),
        "unresolved": unresolved,
    }
    (out_dir / f"{stem}.json").write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    return doc


def export_pose_rows(pack: E.Pack, rows, out_dir: Path, stem: str, caption: str = "") -> dict:
    """The kit's Figures surface as one composed view (ADR-0225 §2):
    `rows` is `[[(pose, ox, oy), ...], ...]` — each pose drawn at pixel
    precision with its top-left at `(ox, oy)` 1x pixels. Same three files and
    sidecar as `export_figure`, so `import` returns paint to the sprite
    vocabulary cells the poses came from. Returns the sidecar, or None when
    no sheet draws any tile of any pose."""
    scale = pack.scale
    name = f"{stem}.png"
    N.require_asset_name(name, where=f"figure rows {stem}")
    cells, unresolved, unit = [], [], None
    homes = {}
    for row in rows:
        for pose, ox, oy in row:
            figure = Figure(pose.id, "sprite", pose.layout(), "pose", pose_id=pose.id,
                            pixels=pose.pixels, z=pose.z)
            placed, missing, u = figure_cells(pack, figure, (ox, oy), pose_id=pose.id)
            unresolved += [n for n in missing if n not in unresolved]
            if not placed:
                continue
            if unit is not None and u != unit:
                raise FigureError(f"{stem}: {pose.id} is at unit {u}, the surface at unit {unit}")
            unit = u
            for entry in placed:
                homes[id(entry)] = home_cell(pack, figure, entry["node"])
            cells += placed
    if not cells:
        return None
    canvas_1x, canvas = compose_cells(pack, cells, unit, scale, lambda e: homes[id(e)])
    out_dir.mkdir(parents=True, exist_ok=True)
    ora_writer.write_surface(
        out_dir, name, canvas, canvas_1x,
        [{"index": c.get("index", i), "x": c["x"] * scale, "y": c["y"] * scale,
          "w": unit * scale, "h": unit * scale} for i, c in enumerate(cells)],
        captions=[(0, 0, caption or stem)])
    doc = {
        "version": FIGURE_VERSION,
        "kind": "figure",
        "figure": stem,
        "figureKind": "sprite",
        "source": "poses",
        "pose": None,
        "poses": [p.id for row in rows for p, _x, _y in row],
        "unit": unit,
        "scale": scale,
        "pixelSize": [canvas_1x.width, canvas_1x.height],
        "sheet": name,
        "reference": f"{stem}.orig.png",
        "assetName": N.asset_name_for(name),
        "label": caption or stem,
        "labelSource": E.LABEL_SOURCE_ID if not caption else "names",
        "cells": cells,
        "unplaced": [],
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
    if (not isinstance(doc, dict) or doc.get("version") not in FIGURE_VERSIONS_READ
            or doc.get("kind") != "figure"):
        raise FigureError(f"{json_path.name}: not a figure sidecar of a known version "
                          f"({', '.join(str(v) for v in FIGURE_VERSIONS_READ)})")
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


def _owned_pixels(pack: E.Pack, entries, i, others, unit):
    """ADR-0225 §3: the 1x pixels of cell `i` that are its own. A pixel no
    other cell covers is its own; a shared pixel goes to the frontmost cell
    (lowest `z`) whose original art is opaque there, else to the frontmost.
    Returns a `unit`x`unit` list of booleans, row-major."""
    arts = {}

    def art(j):
        if j not in arts:
            e = entries[j]
            sheet = _group_sheet(pack, Path(str(e["sheet"])).stem)
            cell = _find_cell(sheet, e) if sheet is not None else None
            arts[j] = sheet.cell_image(cell) if cell is not None else None
        return arts[j]

    me = entries[i]
    owned = []
    for ly in range(unit):
        for lx in range(unit):
            gx, gy = me["x"] + lx, me["y"] + ly
            covering = [i] + [j for j in others
                              if 0 <= gx - entries[j]["x"] < unit and 0 <= gy - entries[j]["y"] < unit]
            if len(covering) == 1:
                owned.append(True)
                continue
            covering.sort(key=lambda j: _front_key(entries, j))
            owner = covering[0]
            for j in covering:
                a = art(j)
                if a is not None and a.px[a.offset(gx - entries[j]["x"], gy - entries[j]["y"]) + 3]:
                    owner = j
                    break
            owned.append(owner == i)
    return owned


def _differs(fig_cell, ref_cell, owned, unit, scale) -> bool:
    """Was any pixel this cell owns painted? `owned` None means all of them."""
    if owned is None:
        return fig_cell.px != ref_cell.px
    for k, mine in enumerate(owned):
        if not mine:
            continue
        lx, ly = k % unit, k // unit
        for dy in range(scale):
            o = fig_cell.offset(lx * scale, ly * scale + dy)
            if fig_cell.px[o:o + 4 * scale] != ref_cell.px[o:o + 4 * scale]:
                return True
    return False


def _merge_owned(current, fig_cell, owned, unit, scale):
    """The sheet cell after import: the figure's pixels where this cell owns
    them, the sheet's own pixels where another cell covered it — the artist
    could not have painted what they could not see (ADR-0225 §3)."""
    out = current.clone()
    for k, mine in enumerate(owned):
        if not mine:
            continue
        lx, ly = k % unit, k // unit
        for dy in range(scale):
            o = fig_cell.offset(lx * scale, ly * scale + dy)
            out.px[o:o + 4 * scale] = fig_cell.px[o:o + 4 * scale]
    return out


# ---- return target: the crop the built manifest draws (#413) -----------------
#
# A figure's sidecar names the sheet each cell was *cut from* (for the kit's
# own figures, always the `sprites` vocabulary). That is not necessarily the
# sheet whose crop `hires.txt` points the key at: in a kit project the
# untouched `usrNNN` row outranks the vocabulary (`_SHEET_RANK`), so it owns
# the key. Writing the paint into the vocabulary cell made that cell "painted",
# painted beats untouched (ADR-0153 §4), and the next build re-pointed the
# key — a manifest change, which Reload Repainted Images (ADR-0212) cannot
# apply. So import writes the paint where the build already draws the key
# from, and leaves the source cell alone when another sheet owns every key it
# emits: painting both would make the source a painted sprite crop that loses
# its key, which the build refuses (#253).

def _quiet_build(folder: Path) -> int:
    out = io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
        return mep_build.main(["build", str(folder), "--quiet"])


def _parse_owners(lines):
    """`(owners, index_keyed, version)` from a built `hires.txt`: `owners` maps
    each canonical `(tileData, palette)` to the set of `(sheet png name, x, y)`
    crops (sheet pixels at `<scale>`) its `<tile>` rules point at, over every
    condition variant."""
    version, images, owners = 0, [], {}
    rules = []
    for line in lines:
        s = line.strip()
        if s.startswith("<ver>"):
            try:
                version = int(s[5:].strip())
            except ValueError:
                pass
        elif s.startswith("<img>"):
            images.append(s[5:].strip())
        else:
            m = mep_build._TILE_RE.match(s)
            if m:
                rules.append([f.strip() for f in m.group(2).split(",")])
    index_keyed = False
    for f in rules:
        if len(f) < 5:
            continue
        try:
            img, x, y = int(f[0]), int(f[3]), int(f[4])
        except ValueError:
            continue
        key = mep_addition.canonical_key((f[1], f[2]), version)
        index_keyed = index_keyed or mep_addition.is_index_key(key[0])
        rel = images[img] if 0 <= img < len(images) else ""
        if rel.startswith("sheets/"):
            owners.setdefault(key, set()).add((rel[len("sheets/"):], x, y))
    return owners, index_keyed, version


def manifest_owners(pack: E.Pack, scratch=None):
    """`_parse_owners` of the manifest `mep_build.py build` makes of the pack
    as it is right now, before this import — the manifest the running game
    loaded when the artist followed build -> play. Built in a throwaway copy
    (build rewrites sheets in place, ADR-0178). `({}, False, 0)` when the pack
    has no manifest or does not build: then there is no owner to protect."""
    textures = pack.sheets_dir.parent
    if pack.sheets_dir.name != "sheets" or not (textures / "hires.txt").is_file():
        return {}, False, 0
    with tempfile.TemporaryDirectory(dir=scratch) as td:
        work = Path(td) / "control"
        shutil.copytree(textures.parent, work)
        if _quiet_build(work) != 0:
            return {}, False, 0
        lines = (work / "textures" / "hires.txt").read_text(encoding="utf-8", errors="replace").splitlines()
    return _parse_owners(lines)


def _cell_keys(sheet: E.Sheet, cell: dict, index_keyed: bool, version: int):
    """`[(key, lx, ly)]`: every key the build emits from `cell` (its own
    `tiles[]` and every alias's, ADR-0153 §3), keyed the way the build keys
    it (CHR index on an index-keyed game, ADR-0172; the unflipped `source`,
    ADR-0178), with the 8x8 sub-tile's 1x offset inside the cell."""
    per = 4 if sheet.unit >= 16 else 1
    lists = [cell.get("tiles")] + [a.get("tiles") for a in cell.get("aliases") or [] if isinstance(a, dict)]
    out = []
    for tiles in lists:
        if not isinstance(tiles, list):
            continue
        for i, t in enumerate(tiles[:per]):
            if not isinstance(t, dict):
                continue
            data = str(t.get("tile") or "").strip().upper()
            if index_keyed:
                idx = t.get("index")
                if not isinstance(idx, int) or idx < 0:
                    continue
                data = mep_addition.index_token(idx)
            else:
                src = str(t.get("source") or "").strip().upper()
                data = src if mep_build._HEX_TILE_RE.match(src) else data
            key = mep_addition.canonical_key((data, str(t.get("palette") or "")), version)
            out.append((key, (i % 2) * 8, (i // 2) * 8))
    return out


def _twin_crop(sheet: E.Sheet, x: int, y: int, cache=None):
    """The 8x8 1x crop of `sheet`'s `*.orig.png` at `(x, y)`, or None."""
    if not sheet.orig_path or not sheet.orig_path.is_file():
        return None
    cache = {} if cache is None else cache
    if sheet.orig_path not in cache:
        cache[sheet.orig_path] = sheet_repaint.read_png(sheet.orig_path)
    img = cache[sheet.orig_path]
    if x < 0 or y < 0 or x + 8 > img.width or y + 8 > img.height:
        return None
    return img.crop(x, y, 8, 8)


def _painted_over_twin(sheet: E.Sheet, img, px: int, py: int, pixels, scale: int, cache=None) -> bool:
    """True when `img` (the sheet at `scale`) already carries paint at the
    `pixels`-sized crop `(px, py)` — `_EditedProbe`'s test, against the
    nearest upscale of the twin — and that paint is not `pixels`."""
    current = img.crop(px, py, pixels.width, pixels.height)
    if current.px == pixels.px or px % scale or py % scale:
        return False
    subs = []
    for dy in range(0, pixels.height // scale, 8):
        for dx in range(0, pixels.width // scale, 8):
            subs.append((dx, dy, _twin_crop(sheet, px // scale + dx, py // scale + dy, cache)))
    if any(t is None for _x, _y, t in subs):
        return False
    return any(current.crop(dx * scale, dy * scale, 8 * scale, 8 * scale).px != t.upscale(scale).px
               for dx, dy, t in subs)


def routed_paint(pack: E.Pack, sheet: E.Sheet, cell: dict, scale: int, manifest, big):
    """`big` (the cell at `scale`) with each 8x8 sub-tile whose key another
    sheet owns replaced by that owner's pixels, where the owner carries paint
    — what `import` routed there (#413) — so a re-export shows it. Unpainted
    owners leave `big` untouched, so an unpainted pack exports as before."""
    write_source, routes, _moves = plan_targets(pack, sheet, cell, scale, manifest)
    if write_source or not routes:
        return big
    out, cache = None, {}
    for other, ox, oy, lx, ly in routes:
        if other.scale != scale or not other.png_path.is_file():
            continue
        img = sheet_repaint.read_png(other.png_path)
        if ox + 8 * scale > img.width or oy + 8 * scale > img.height:
            continue
        crop = img.crop(ox, oy, 8 * scale, 8 * scale)
        twin = _twin_crop(other, ox // scale, oy // scale, cache)
        if twin is None or crop.px == twin.upscale(scale).px:
            continue
        if out is None:
            out = big.clone()
        out.paste(crop, lx * scale, ly * scale)
    return out if out is not None else big


def plan_targets(pack: E.Pack, sheet: E.Sheet, cell: dict, scale: int, manifest):
    """Where one painted cell goes: `(write_source, routes, moves)`.

    `routes` is `[(owner Sheet, x, y, lx, ly)]` — each 8x8 crop another sheet
    owns for a key this cell emits, with the sub-tile offset `(lx, ly)` of the
    paint to copy there. An owner whose twin art is not the source's is
    skipped (never paint over a different drawing). `write_source` says the
    source cell is written: when it owns a key itself, when a key has no
    owner at all, or when an owner had to be skipped — paint is never
    dropped, even at the cost of a reopen. `moves` is True when this import
    will re-point or add a rule at the next build (the reload then cannot
    show it, ADR-0212)."""
    owners, index_keyed, version = manifest
    if not owners:
        return True, [], False
    by_name = {s.name: s for s in pack.sheets}
    cx, cy = int(cell["x"]), int(cell["y"])
    routes, self_owned, orphan, skipped, twins = [], False, False, False, {}
    for key, lx, ly in _cell_keys(sheet, cell, index_keyed, version):
        mine = (sheet.name, (cx + lx) * scale, (cy + ly) * scale)
        found = owners.get(key) or set()
        if not found:
            orphan = True  # no rule draws this key today; painting it adds one
        for owner in sorted(found):
            if owner == mine:
                self_owned = True
                continue
            other = by_name.get(owner[0])
            ox, oy = owner[1], owner[2]
            here = _twin_crop(sheet, cx + lx, cy + ly, twins)
            there = (_twin_crop(other, ox // scale, oy // scale, twins)
                     if other is not None and not ox % scale and not oy % scale else None)
            if here is None or there is None or here.px != there.px:
                skipped = True
                continue
            routes.append((other, ox, oy, lx, ly))
    write_source = self_owned or orphan or skipped or not routes
    return write_source, routes, orphan or (write_source and (bool(routes) or skipped))


def import_figure(pack: E.Pack, png_path: Path, scratch=None) -> dict:
    """Write the painted cells of a figure back into the pack. Only a cell
    that differs from the `*.orig.png` twin is written; a cell the sheet
    already holds is left alone. The paint lands on the crop the pack's
    built manifest already draws each key from (#413, `plan_targets`), so the
    rebuild changes no rule and the in-place reload shows it. Returns a
    report."""
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
              "written": 0, "alreadyApplied": 0, "sheets": [], "overlapped": 0,
              "rerouted": 0, "sourceLeft": 0, "moves": 0, "overwrote": 0}
    entries = [e for e in (doc.get("cells") or []) if isinstance(e, dict)]
    for e in entries:
        e["x"], e["y"] = int(e["x"]), int(e["y"])
    overlaps = _overlaps(entries, unit)
    canvases = {}   # sheet name -> [Sheet, Image, dirty]
    manifest = None  # built on the first painted cell only: an unpainted import builds nothing
    twins = {}

    def canvas(sheet):
        if sheet.name not in canvases:
            if sheet.scale != scale:
                raise FigureError(
                    f"{sheet.name}: painted at {sheet.scale}x while the figure is at {scale}x — "
                    "all sheets of a pack share one <scale>")
            canvases[sheet.name] = [sheet, sheet_repaint.read_png(sheet.png_path), False]
        return canvases[sheet.name]

    def put(sheet, pixels, px, py):
        """Paste `pixels` at sheet pixel `(px, py)`; True when anything changed."""
        slot = canvas(sheet)
        img = slot[1]
        if px + pixels.width > img.width or py + pixels.height > img.height:
            raise FigureError(f"{sheet.name}: crop ({px},{py}) falls outside the sheet")
        if img.crop(px, py, pixels.width, pixels.height).px == pixels.px:
            return False
        if _painted_over_twin(sheet, img, px, py, pixels, scale, twins):
            report["overwrote"] += 1  # paint already there, not this figure's
        img.paste(pixels, px, py)
        slot[2] = True
        return True

    for i, entry in enumerate(entries):
        report["cells"] += 1
        x, y = entry["x"], entry["y"]
        if x < 0 or y < 0 or x + unit > twin.width or y + unit > twin.height:
            raise FigureError(f"{png_path.name}: cell for node {entry.get('node')} falls outside the twin")
        fig_cell = figure.crop(x * scale, y * scale, unit * scale, unit * scale)
        ref_cell = twin.crop(x, y, unit, unit).upscale(scale)
        # ADR-0225 §3: where cells overlap, a pixel belongs to one of them.
        owned = _owned_pixels(pack, entries, i, overlaps[i], unit) if overlaps[i] else None
        if owned is not None:
            report["overlapped"] += 1
        if not _differs(fig_cell, ref_cell, owned, unit, scale):
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
        img = canvas(sheet)[1]
        sx, sy = int(cell["x"]) * scale, int(cell["y"]) * scale
        if sx + unit * scale > img.width or sy + unit * scale > img.height:
            raise FigureError(f"{sheet.name}: cell ({cell['x']},{cell['y']}) falls outside the sheet")
        current = img.crop(sx, sy, unit * scale, unit * scale)
        new_cell = fig_cell if owned is None else _merge_owned(current, fig_cell, owned, unit, scale)
        if manifest is None:
            manifest = manifest_owners(pack, scratch)
        write_source, routes, moves = plan_targets(pack, sheet, cell, scale, manifest)
        changed = put(sheet, new_cell, sx, sy) if write_source else False
        for other, ox, oy, lx, ly in routes:
            sub = new_cell.crop(lx * scale, ly * scale, 8 * scale, 8 * scale)
            changed = put(other, sub, ox, oy) or changed
        if not changed:
            report["alreadyApplied"] += 1
            continue
        report["written"] += 1
        report["rerouted"] += 1 if routes else 0
        report["sourceLeft"] += 0 if write_source else 1
        report["moves"] += 1 if moves else 0
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
    """`{sheet png name: bytes}` for every sheet the import may write, as they
    are on disk right now — the control `verify` restores. That is every
    sheet of the pack, not only the ones the figure's sidecar names: the
    paint lands on whichever sheet owns the key (#413)."""
    _load_figure_doc(Path(png_path))  # refuse a non-figure before snapshotting
    return {s.name: s.png_path.read_bytes() for s in pack.sheets if s.png_path.is_file()}


def _read_bytes(path: Path):
    return path.read_bytes() if path.is_file() else None


def verify(pack_dir: Path, restore=None, scratch=None) -> dict:
    """Rebuild two throwaway copies of the pack — a control with the sheets in
    `restore` put back to their pre-import bytes, and the pack as it is — and
    compare the key sets of the two rebuilt `hires.txt`: nothing lost, nothing
    invented (ADR-0183 §4).

    The control is what makes this honest on a recorded pack: a bootstrap
    `hires.txt` carries every CHR tile (ADR-0043) while a rebuild carries only
    what the sheets hold, so "keys on disk vs rebuilt" would fail on every
    first build for a reason that has nothing to do with the figure. That
    drift is reported separately as `drift_from_recording`. Whether the two
    rebuilt `hires.txt` are byte-identical is reported as
    `manifest_unchanged` (#413): the in-place reload needs it, the key-set
    round trip does not, so it informs rather than fails."""
    recorded = hires_keys(pack_dir / "textures" / "hires.txt")
    with tempfile.TemporaryDirectory(dir=scratch) as td:
        control = Path(td) / "control"
        shutil.copytree(pack_dir, control)
        for name, data in (restore or {}).items():
            (control / "textures" / "sheets" / name).write_bytes(data)
        rc_control = _quiet_build(control)
        before = hires_keys(control / "textures" / "hires.txt")
        manifest_before = _read_bytes(control / "textures" / "hires.txt")
        work = Path(td) / "pack"
        shutil.copytree(pack_dir, work)
        rc = _quiet_build(work)
        after = hires_keys(work / "textures" / "hires.txt")
        manifest_after = _read_bytes(work / "textures" / "hires.txt")
    report = {"errors": rc, "control_errors": rc_control,
              "keys_before": len(before), "keys_after": len(after),
              "lost": len(before - after), "added": len(after - before),
              "drift_from_recording": len(recorded ^ before),
              # #413: an unchanged manifest is what lets Reload Repainted
              # Images (ADR-0212) show the paint without reopening the ROM.
              "manifest_unchanged": manifest_before is not None and manifest_before == manifest_after}
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
    doc = export_figure(pack, args.figure, out_dir, load_names(args.names))
    print(f"{doc['sheet']}: {len(doc['cells'])} cells, {doc['size'][0]}x{doc['size'][1]} "
          f"at unit {doc['unit']}, scale {doc['scale']}x, layout from {doc['source']}"
          + (f" ({doc['pose']})" if doc["pose"] else ""))
    print(f"  caption   {doc['label']}  ({doc['labelSource']})")
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
    report = import_figure(pack, Path(args.figure_png), args.scratch)
    print(f"{report['figure']}: {report['cells']} cells, {report['painted']} painted, "
          f"{report['written']} written, {report['alreadyApplied']} already on the sheet")
    if report["rerouted"]:
        print(f"  {report['rerouted']} painted cell(s) written where the built hires.txt draws their "
              f"key from; {report['sourceLeft']} of their source cell(s) left as they were, so no rule moves")
    for name in report["sheets"]:
        print(f"  wrote {pack.sheets_dir / name}")
    if report["overwrote"]:
        print(f"  note: {report['overwrote']} crop(s) already carried other paint — an earlier import "
              "of this figure, or a paint on the sheet itself — and now carry this figure's; a figure "
              "and its sheet row are the same tiles, paint one, not both")
    if report["moves"]:
        print(f"  note: {report['moves']} painted cell(s) will re-point a key in hires.txt at the next "
              "build — reopen the ROM to see them; Reload Repainted Images cannot (ADR-0212)")
    elif report["written"]:
        print("  next: python3 scripts/mep_build.py build <pack>, then HD Packs > Reload Repainted Images")
    if args.verify:
        v = verify(Path(args.pack), restore, args.scratch)
        print(f"verify: build errors {v['errors']}, keys {v['keys_before']} -> {v['keys_after']}, "
              f"{v['lost']} lost, {v['added']} added: {'PASS' if v['ok'] else 'FAIL'}")
        print("  hires.txt " + ("unchanged by this import: Reload Repainted Images shows it"
                                if v["manifest_unchanged"] else
                                "changed by this import: reopen the ROM to see it (ADR-0212)"))
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
    ex.add_argument("--names", help="optional human caption file (poses/cycles/figures); a name in it "
                                    "beats the recorder's inferred label (ADR-0183 §5)")
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
