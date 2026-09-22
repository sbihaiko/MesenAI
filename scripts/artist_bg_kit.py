#!/usr/bin/env python3
"""Build the artist-facing *background* surface of a recorded pack.

The sprite half of a recording already reads as art: ADR-0170's `poses.json`
gives an artist one whole figure per entry, and the 2026-09-13 human panel
confirmed it (`runs/golden-20260913-f922/panel-human-2026-09-13.md`, section
2). The background half does not, and the same panel measured why on Contra's
base interior:

  * `obj000.png` (19 cells), `obj001.png` (17) and `obj002.png` (8) are almost
    entirely solid-black cells — an "object" nobody can name, let alone paint;
  * 16 of that pack's 28 `objNNN` sheets are *nothing but* uniform cells;
  * the one scenery element an artist would want whole — the base door sensor,
    the artist pack Contra80s 1.1 paints it as the single file
    `Stage1BaseDoor1.png` — is scattered across `metatiles.png` with no
    `objNNN` around it.

This tool turns that pack into a background kit that obeys the artist-pack
rule: one file, one recognisable thing. It never writes into the pack it
reads — the sheets land in `--out` and a manifest says what was dropped and
why, so the drop is reviewable instead of silent.

Three surfaces come out of it:

  1. **objects** — every `objNNN` that carries ink, re-emitted as a composed
     sheet with its uniform (inkless) cells removed and the rest kept at the
     relative offsets ADR-0168's `evidence[]` walk recovers, so a 19-cell
     black L becomes the four rock cells that were actually drawn on it.
  2. **elements** — clusters of vocabulary cells that always appear together
     in the same relative offsets, recovered from `adjacency.json`, so a thing
     like the door sensor arrives as one sheet even though no `objNNN` groups
     it (see `_DETERMINISTIC_P` / `_MIN_PAIR_COUNT` for the thresholds and the
     measurement behind them).
  3. **scene** — `textures/backgrounds/screenNNN.png`, listed and left exactly
     as recorded: a whole screen is already one recognisable thing.

It is one of the four generators of the shared artist kit
(`runs/golden-20260913-f922/artist-kit-contract.md`): it writes its painting
surfaces under `<out>/sheets/`, the screens it leaves alone under
`<out>/scene/`, and one manifest fragment `kit-part-background.json`. The
assembler writes `kit.json` and `ARTIST.md`; this tool never does, so anything
an artist should read about the background half goes in the fragment's
`notes[]`.

Usage:
    python3 scripts/artist_bg_kit.py <pack dir> [--out DIR] [--names F] [--verify]

`--verify` is ADR-0183 §4's acceptance test: it builds a throwaway copy of the
pack twice, once untouched and once with the kit's sheets dropped in, and
asserts that the kit adds no build error the pack did not already report and
that the two rebuilt `hires.txt` files address exactly the same set of
`(tileData, palette)` keys — the kit may re-cut the art into better files, but
it may never lose a rule or invent one.
"""

import argparse
import collections
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import asset_names as N  # noqa: E402 — the F12.4 painting-surface name contract
import compose_engine as E  # noqa: E402

PART = "background"
MANIFEST = f"kit-part-{PART}.json"
GENERATOR = "scripts/artist_bg_kit.py"

# A cell is "inkless" when every one of its pixels is the same colour: there is
# no drawing on it, only a fill. Measured over the 14 golden packs listed in
# `runs/golden-20260913-f922/background-objects.md`, 122 of 2490 background
# cells (4.9%) are inkless, and every one of the 22 objects the panel could not
# name is made only of those. Above zero the ink histogram is continuous — no
# gap to put a threshold in — so the rule is exactly "ink == 0", not a fraction
# somebody chose.
_INKLESS = 0.0

# ADR-0153 §2's conditional probability at which a neighbour relation is read
# as "these two cells are one thing". Required in *both* directions: B follows A
# every time A is seen, and A precedes B every time B is seen.
#
# 1.0, from the measurement: on the Contra base pack, clustering the background
# vocabulary at p >= 1.0 yields components of at most 6 cells — the door-sensor
# frame halves and their animation phases, which is the element the panel
# missed. Relaxing to 0.95 changes nothing there (54 qualifying edges vs 56),
# but at 0.85 the rock-and-vines wall bleeds into components of 19 and 17 cells
# — exactly the unnameable blobs this tool exists to stop emitting.
_DETERMINISTIC_P = 1.0

# ...and the pair has to have been seen twice, not once. The recorder's own
# `objNNN` grouping has a floor of 3 (no `objNNN` cell in any of the 14 golden
# packs has `count` < 3), and that floor is precisely why the door sensor was
# never grouped: its four animation phases were each seen exactly twice. Two is
# the smallest count that still means "again", so it is where this tool looks.
_MIN_PAIR_COUNT = 2

_DIR_OFFSET = {"E": (1, 0), "S": (0, 1)}


class KitError(Exception):
    pass


# ---- ink ------------------------------------------------------------------

def cell_histogram(sheet: E.Sheet, cell: dict) -> collections.Counter:
    """Colour histogram of one cell's 1x reference art."""
    img = sheet.cell_image(cell)
    hist = collections.Counter()
    for y in range(img.height):
        for x in range(img.width):
            hist[img.get(x, y)] += 1
    return hist


def ink_ratio(hist: collections.Counter) -> float:
    """Share of pixels that are not the cell's most common colour.

    The most common colour stands in for "the fill"; anything else is a mark
    somebody drew. A flat cell scores 0 whatever its colour, which is the only
    property this tool needs from the measure."""
    total = sum(hist.values())
    if not total:
        return 0.0
    return (total - hist.most_common(1)[0][1]) / total


def colour_name(rgba) -> str:
    r, g, b, a = rgba
    return "#%02X%02X%02X" % (r, g, b) if a else "transparent"


# ---- objects --------------------------------------------------------------

def _object_layout(sheet: E.Sheet, nodes):
    """Relative cell offsets of an `objNNN` group, normalised to (0, 0).

    ADR-0168's walk is the pack's own account of how the group sat on screen;
    reusing it means a kept cell lands where it was drawn instead of wrapping
    into a row. Nodes the walk cannot place are simply absent — same contract
    as `walk_layout` itself."""
    pos = E.walk_layout(list(nodes), sheet.doc.doc.get("evidence") or [])
    return pos


def read_objects(pack: E.Pack):
    """(kept, dropped) for the pack's `objNNN` sheets.

    `kept` is [(sheet, [(node, cx, cy)], report)], `dropped` the report of the
    sheets that carry no ink at all."""
    kept, dropped = [], []
    for sheet in pack.sheets:
        if sheet.kind != "object":
            continue
        cells = [c for c in sheet.cells if isinstance(c, dict)
                 and isinstance(c.get("metatile"), int)]
        if not cells:
            continue
        inked, flat = [], []
        for cell in cells:
            try:
                hist = cell_histogram(sheet, cell)
            except E.ComposeError:
                # No usable reference twin: the tool cannot tell ink from fill,
                # and guessing "inkless" would delete art. Keep the cell.
                inked.append(cell)
                continue
            if ink_ratio(hist) > _INKLESS:
                inked.append(cell)
            else:
                flat.append((cell, colour_name(hist.most_common(1)[0][0])))
        report = {
            "sheet": sheet.name,
            "cells": len(cells),
            "inklessCells": len(flat),
            "inklessColours": sorted({c for _cell, c in flat}),
            "seen": max(int(c.get("count") or 0) for c in cells),
        }
        if not inked:
            report["reason"] = ("every cell is a single flat colour — there is "
                                "nothing drawn on this object to repaint")
            dropped.append(report)
            continue
        pos = _object_layout(sheet, [c["metatile"] for c in cells])
        placed = [(c["metatile"], pos[c["metatile"]]) for c in inked
                  if c["metatile"] in pos]
        if not placed:
            placed = [(c["metatile"], (i, 0)) for i, c in enumerate(inked)]
        min_x = min(p[0] for _n, p in placed)
        min_y = min(p[1] for _n, p in placed)
        kept.append((sheet,
                     [(n, x - min_x, y - min_y) for n, (x, y) in placed],
                     report))
    return kept, dropped


# ---- elements recovered from adjacency ------------------------------------

def _deterministic_edges(adj: E.Adjacency):
    """Edges that read as "these two cells are one thing" in both directions."""
    out = []
    for e in adj.bg_edges:
        try:
            a, b, direction = int(e["a"]), int(e["b"]), str(e["dir"])
            count = int(e["count"])
        except (KeyError, TypeError, ValueError):
            continue
        if direction not in _DIR_OFFSET or count < _MIN_PAIR_COUNT or a == b:
            continue
        node_a, node_b = adj.bg.get(a), adj.bg.get(b)
        if not node_a or not node_b:
            continue
        out_deg = node_a.out_e if direction == "E" else node_a.out_s
        in_deg = node_b.in_e if direction == "E" else node_b.in_s
        if not out_deg or not in_deg:
            continue
        if count / out_deg >= _DETERMINISTIC_P and count / in_deg >= _DETERMINISTIC_P:
            out.append((a, b, direction, count))
    return out


def _components(edges):
    parent = {}

    def find(x):
        parent.setdefault(x, x)
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for a, b, _d, _n in edges:
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[ra] = rb
    groups = collections.defaultdict(set)
    for node in list(parent):
        groups[find(node)].add(node)
    return [members for members in groups.values() if len(members) > 1]


def _place(members, edges):
    """Relative offsets of a component's cells, normalised to (0, 0)."""
    pos = {min(members): (0, 0)}
    changed = True
    while changed:
        changed = False
        for a, b, direction, _n in edges:
            dx, dy = _DIR_OFFSET[direction]
            if a in pos and b not in pos:
                pos[b] = (pos[a][0] + dx, pos[a][1] + dy)
                changed = True
            elif b in pos and a not in pos:
                pos[a] = (pos[b][0] - dx, pos[b][1] - dy)
                changed = True
    inside = {n: p for n, p in pos.items() if n in members}
    min_x = min(x for x, _y in inside.values())
    min_y = min(y for _x, y in inside.values())
    return {n: (x - min_x, y - min_y) for n, (x, y) in inside.items()}


def _phase_signature(members, layout, edges):
    """What the world outside this component attaches to it, keyed by slot.

    Two components with the same signature and the same footprint are the same
    scenery element caught in different animation phases: the door sensor's orb
    blinks, so its 2x2 frame appears four times in the vocabulary, each phase a
    component of its own, all four hanging off the same wall cells above them.
    Merging on this key is what makes the sensor one file instead of four."""
    sig = set()
    for a, b, direction, _n in edges:
        if a in members and b not in members:
            sig.add((layout[a], direction, "out", b))
        elif b in members and a not in members:
            sig.add((layout[b], direction, "in", a))
    return frozenset(sig), frozenset(layout.values())


def node_has_ink(pack: E.Pack, node: int, _memo={}) -> bool:
    """Whether a background node's pixels draw anything.

    Same rule as the object surface, applied to whatever `node_art` resolves —
    an art sheet's cell, or a crop of the screen that owns it (ADR-0166). A
    cell that is one flat colour is a fill, not a picture, and an element built
    out of those is the very blob this tool exists to stop emitting. A node
    whose pixels cannot be resolved at all is not ours to judge, so it passes
    and `_export` decides."""
    key = (id(pack), node)
    if key not in _memo:
        try:
            art = pack.node_art(node, sprite=False)
        except E.ComposeError:
            art = None
        if art is None:
            _memo[key] = True
        else:
            hist = collections.Counter()
            for y in range(art.height):
                for x in range(art.width):
                    hist[art.get(x, y)] += 1
            _memo[key] = ink_ratio(hist) > _INKLESS
    return _memo[key]


def recover_elements(pack: E.Pack):
    """Scenery elements recovered from the adjacency graph.

    Components are formed over the whole background vocabulary, not only the
    cells no `objNNN` touches: an `objNNN` that happens to be one phase of an
    animated element is *part of* that element, and the caller folds it in
    rather than handing the artist the same door twice."""
    adj = pack.adjacency
    edges = _deterministic_edges(adj)
    # The *incoming* half of the signature needs edges that are deterministic
    # into the cell even when they are not deterministic out of their source
    # (a wall cell has many things below it; the door has only that wall
    # above it). Those are the edges that tie a phase to its surroundings.
    anchors = []
    for e in adj.bg_edges:
        try:
            a, b, direction = int(e["a"]), int(e["b"]), str(e["dir"])
            count = int(e["count"])
        except (KeyError, TypeError, ValueError):
            continue
        node_b = adj.bg.get(b)
        if direction not in _DIR_OFFSET or count < _MIN_PAIR_COUNT or not node_b:
            continue
        in_deg = node_b.in_e if direction == "E" else node_b.in_s
        if in_deg and count / in_deg >= _DETERMINISTIC_P:
            anchors.append((a, b, direction, count))

    merged = collections.OrderedDict()
    for members in _components(edges):
        layout = _place(members, edges)
        if len(layout) != len(members):
            continue
        key = _phase_signature(members, layout, edges + anchors)
        merged.setdefault(key, []).append((sorted(members), layout))

    elements = []
    for phases in merged.values():
        placed, seen, column = [], set(), 0
        width = max(max(x for x, _y in layout.values()) for _m, layout in phases) + 1
        for members, layout in sorted(phases):
            drawn = 0
            for node in members:
                # An inkless member is dropped from the drawing but stays in
                # the component: it is still what holds the phases together,
                # it is just not something anybody paints.
                # Only cells an art *sheet* shows. A node whose pixels live
                # only inside a captured screen (ADR-0156/ADR-0166) can be
                # cropped out, but putting it on a sheet adds a <tile> rule the
                # pack did not have — measured: doing so adds 13 keys to
                # Contra stage1-run and 3 to Excitebike. The screen is already
                # the artist's surface for that art, so it stays there.
                if node in seen or pack.background_home(node) is None:
                    continue
                if not node_has_ink(pack, node):
                    continue
                seen.add(node)
                x, y = layout[node]
                placed.append((node, x + column, y))
                drawn += 1
            # A phase that contributed nothing gets no column, or the sheet
            # opens with a blank the artist has to guess the meaning of.
            if drawn:
                column += width + 1
        if len(placed) < 2:
            continue
        elements.append({
            "nodes": placed,
            "absorbed": [],
            "phases": len(phases),
            "seen": max((adj.bg[n].count for n, _x, _y in placed
                         if n in adj.bg), default=0),
        })
    elements.sort(key=lambda el: (-el["seen"], el["nodes"][0][0]))
    return elements


# ---- names ----------------------------------------------------------------

def load_names(path):
    """The optional naming file of the kit contract, reduced to id -> caption.

    Only ids the file actually carries get a title; everything else falls back
    to its own id. A generator never invents a name — the whole point of the
    file is that a human, not this tool, decided what a thing is called."""
    if not path:
        return {}
    try:
        doc = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise KitError(f"{path}: not readable as JSON ({exc})")
    if not isinstance(doc, dict):
        raise KitError(f"{path}: not a JSON object")
    names = {}
    for section in doc.values():
        if not isinstance(section, dict):
            continue
        for key, value in section.items():
            if isinstance(value, str):
                names[key] = value
            elif isinstance(value, dict) and isinstance(value.get("name"), str):
                names[key] = value["name"]
    return names


def _title(names, ids, fallback):
    """The first caption the naming file has for any of this file's ids."""
    for i in ids:
        if i in names:
            return names[i]
    return fallback


# ---- writing the kit ------------------------------------------------------

def _claim_name(pack: E.Pack, out_dir: Path) -> str:
    """Reserve the next free `usrNNN` stem by *creating* its sidecar, empty.

    `Pack.next_free_name` only looks. The sprite and background generators are
    documented as running in parallel into one `<kit>/sheets/`, so both would
    be handed the same number between the scan and the write and one surface
    would vanish under the other. Creating the file with O_EXCL makes the claim
    the same act as the check - the same rule `artist_kit.claim_name` follows,
    and the two are compatible because they claim through the same file."""
    out_dir.mkdir(parents=True, exist_ok=True)
    for _attempt in range(1000):
        name = pack.next_free_name(out_dir)
        try:
            fd = os.open(out_dir / f"{name}.json", os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except FileExistsError:
            continue   # somebody else took it between the scan and the create
        os.close(fd)
        return name
    raise KitError(f"{out_dir}: no free usrNNN name")


def _export(pack: E.Pack, out_dir: Path, placements):
    """One composed object sheet. The `.ora` beside it (ADR-0220) captions the
    sheet with its first node id — the same id the manifest's `ids[]` opens
    with, never a name this tool invents (ADR-0183 §5). No `context` is
    passed: a stage position for a background node would come from a
    recorder `map-NNN.json`'s `placements[]`, and the packs this tool reads
    ship none, so a scenery sheet's `.ora` has four layers today."""
    nodes = [n for n, _x, _y in placements]
    return pack.export("object", nodes, seed=nodes[0], locked=nodes,
                       to_dir=out_dir, placements=placements,
                       name=_claim_name(pack, out_dir),
                       captions=[(E.GUTTER, E.GUTTER, f"bg{nodes[0]:03d}")])


def _geometry(placements):
    return (max(y for _n, _x, y in placements) + 1,
            max(x for _n, x, _y in placements) + 1)


def build_kit(pack_dir: Path, out_dir: Path, names_file=None) -> dict:
    """Write the background part of the kit and return its manifest fragment."""
    pack = E.Pack(Path(pack_dir))
    out_dir = Path(out_dir)
    sheets_dir = out_dir / "sheets"
    sheets_dir.mkdir(parents=True, exist_ok=True)
    names = load_names(names_file)

    kept_objects, inkless = read_objects(pack)
    files, dropped, notes = [], [], []

    for report in inkless:
        colours = " / ".join(report["inklessColours"]) or "one flat colour"
        dropped.append({
            "path": report["sheet"],
            "why": (f"{report['cells']} cells, all uniform {colours} — "
                    f"no art to paint (seen {report['seen']}x)"),
        })

    # Elements first: one of them may turn out to *contain* an objNNN, and the
    # artist wants the whole thing, not the whole thing plus a slice of it.
    elements = recover_elements(pack)
    element_nodes = [{n for n, _x, _y in el["nodes"]} for el in elements]

    for sheet, placements, report in kept_objects:
        nodes = {n for n, _x, _y in placements}
        inside = next((i for i, ns in enumerate(element_nodes) if nodes < ns), None)
        if inside is not None:
            elements[inside]["absorbed"].append(sheet.name)
            dropped.append({
                "path": sheet.name,
                "why": (f"its {len(nodes)} cells are one phase of a "
                        f"{len(element_nodes[inside])}-cell element that is emitted "
                        f"whole instead — see the element sheet carrying "
                        f"{len(elements[inside]['nodes'])} cells across "
                        f"{elements[inside]['phases']} phases"),
            })
            continue
        try:
            name = _export(pack, sheets_dir, placements)
        except E.ComposeError as exc:
            dropped.append({"path": sheet.name,
                            "why": f"{report['cells']} cells, not exportable: {exc}"})
            continue
        rows, columns = _geometry(placements)
        ids = [Path(sheet.name).stem]
        files.append({
            "path": f"sheets/{N.require_asset_name(name + N.SURFACE_EXT, 'artist_bg_kit.py')}",
            "title": _title(names, ids, f"{ids[0]} ({len(placements)} cells)"),
            "unit": "object",
            "rows": rows,
            "columns": columns,
            "cells": len(placements),
            "ids": ids,
            "seen": True,
            "source": sheet.name,
            "droppedCells": report["inklessCells"],
        })
        if report["inklessCells"]:
            colours = " / ".join(report["inklessColours"])
            notes.append(f"{sheet.name}: {report['inklessCells']} of "
                         f"{report['cells']} cells were uniform {colours} and were "
                         f"left out of sheets/{name}.png — the remaining "
                         f"{len(placements)} are what was actually drawn.")

    kept_object_nodes = [{n for n, _x, _y in pl} for _s, pl, _r in kept_objects
                         if not any(_s.name in el["absorbed"] for el in elements)]
    for element in elements:
        nodes = {n for n, _x, _y in element["nodes"]}
        if any(nodes <= obj for obj in kept_object_nodes):
            # An objNNN already hands the artist these cells together. Emitting
            # them again would be the same picture under a second name.
            continue
        try:
            name = _export(pack, sheets_dir, element["nodes"])
        except E.ComposeError:
            # A node whose art the pack cannot resolve is not an element the
            # artist can paint; skipping it is the honest outcome.
            continue
        rows, columns = _geometry(element["nodes"])
        ids = [f"bg{n:03d}" for n, _x, _y in element["nodes"]]
        files.append({
            "path": f"sheets/{N.require_asset_name(name + N.SURFACE_EXT, 'artist_bg_kit.py')}",
            "title": _title(names, ids,
                            f"{ids[0]} + {len(ids) - 1} cells that always follow it"),
            "unit": "element",
            "rows": rows,
            "columns": columns,
            "cells": len(element["nodes"]),
            "ids": ids,
            "seen": True,
            "phases": element["phases"],
            "absorbs": element["absorbed"],
        })
    if any(f["unit"] == "element" for f in files):
        notes.append(
            "The `element` sheets are scenery no objNNN groups: cells that "
            "adjacency.json shows always sitting at the same offsets from one "
            "another. Where one has several phases they are laid out left to "
            "right with a blank column between them — that is one thing "
            "blinking, not several things.")

    scene_dir = out_dir / "scene"
    if pack.backgrounds_dir.is_dir():
        for png in sorted(pack.backgrounds_dir.glob("screen*.png")):
            if png.name.endswith(".orig.png"):
                continue
            scene_dir.mkdir(parents=True, exist_ok=True)
            shutil.copy2(png, scene_dir / png.name)
            twin = png.with_name(png.stem + ".orig.png")
            if twin.is_file():
                shutil.copy2(twin, scene_dir / twin.name)
            stem = png.stem
            files.append({
                "path": f"scene/{png.name}",
                "title": _title(names, [stem], f"{stem} (a whole recorded screen)"),
                "unit": "scene",
                "rows": 1, "columns": 1, "cells": 1,
                "ids": [stem],
                "seen": True,
            })
    if any(f["unit"] == "scene" for f in files):
        notes.append(
            "scene/ holds the whole-screen captures exactly as recorded — no "
            "re-cutting, because a screen is already one recognisable thing. "
            "Paint over them for backdrop work; the cell sheets are for the "
            "pieces the engine actually re-uses.")

    notes.append(
        "Cells that are one flat colour were removed: nothing is drawn on "
        "them, so there is nothing to repaint. Every removal is in dropped[] "
        "or in the note for its sheet, with the count behind it.")

    manifest = {
        "part": PART,
        "generator": GENERATOR,
        "pack": str(Path(pack_dir)),
        "files": files,
        "dropped": dropped,
        "notes": notes,
        "verify": {"ran": False},
    }
    _write_manifest(out_dir, manifest)
    return manifest


def _write_manifest(out_dir: Path, manifest: dict):
    (Path(out_dir) / MANIFEST).write_text(json.dumps(manifest, indent=2) + "\n",
                                          encoding="utf-8")


# ---- verify ---------------------------------------------------------------

def tile_keys(hires: Path):
    """The `(tileData, palette)` every `<tile>` rule of a hires.txt addresses."""
    keys = set()
    if not Path(hires).is_file():
        # A pack that was never built has no rules yet; the rebuild's own set
        # is then the whole answer, and "lost" is meaningless rather than zero.
        return keys
    for line in hires.read_text(encoding="utf-8", errors="replace").splitlines():
        if not line.startswith("<tile>"):
            continue
        fields = line[len("<tile>"):].split(",")
        if len(fields) >= 3:
            keys.add((fields[1], fields[2]))
    return keys


def verify(pack_dir: Path, out_dir: Path) -> dict:
    """Rebuild a copy of the pack with the kit's sheets dropped in.

    Only `<out>/sheets/` goes in: those are drop-in replacements for the pack's
    own `textures/sheets/`. `scene/` is a copy of art the pack already ships and
    has no sidecar, so dropping it in would only earn an unclaimed-PNG warning."""
    pack_dir, out_dir = Path(pack_dir), Path(out_dir)
    original = tile_keys(pack_dir / "textures" / "hires.txt")
    with tempfile.TemporaryDirectory() as td:
        base_errors, baseline = _build(Path(td) / "base", pack_dir, None)
        errors, rebuilt = _build(Path(td) / "kit", pack_dir, out_dir / "sheets")
    # A recorded pack can arrive with errors of its own (Contra's base pack has
    # five ADR-0178 flip-baked-key errors before this tool touches it). The kit
    # is judged on what it *adds*: an error the pristine pack already reports,
    # re-reported against a sheet the kit cut out of that same art, is not a
    # regression, so errors are compared with the sheet name stripped off.
    new_errors = sorted(errors - base_errors)
    lost, added = sorted(baseline - rebuilt), sorted(rebuilt - baseline)
    # `keys_before` is the baseline *rebuild*, not the hires.txt the pack ships
    # with. Several recorded packs ship a hires.txt with far more rules than
    # `build` regenerates (Contra stage1-run: 1956 on disk, 228 rebuilt) —
    # that gap is the pack's, present with or without this tool, and measuring
    # against it would hide the only thing verification is for: whether the kit
    # itself lost a rule. `keys_on_disk` keeps the pack's own number visible.
    return {
        "ran": True,
        "errors": len(new_errors),
        "preexistingErrors": len(base_errors),
        "errorLines": new_errors[:10],
        "keys_before": len(baseline),
        "keys_after": len(rebuilt),
        "keys_on_disk": len(original),
        "lost": len(lost),
        "added": len(added),
        "ok": not new_errors and not lost and not added,
    }


def _build(copy: Path, pack_dir: Path, drop_in):
    """`mep_build.py build` on a throwaway copy: (error kinds, tile keys)."""
    shutil.copytree(pack_dir, copy, symlinks=True)
    if drop_in is not None:
        for src in sorted(Path(drop_in).glob("*")):
            if src.is_file():
                shutil.copy2(src, copy / "textures" / "sheets" / src.name)
    proc = subprocess.run(
        [sys.executable, str(Path(__file__).resolve().parent / "mep_build.py"),
         "build", str(copy)],
        capture_output=True, text=True)
    kinds = set()
    for line in (proc.stdout + proc.stderr).splitlines():
        if not line.lower().startswith("error"):
            continue
        # The sheet name and the crop count both change when the same defect
        # is reported against a sheet the kit re-cut out of the same art
        # ("obj016.png: 4 crop(s)..." becomes "usr022.png: 2 crop(s)..."), so
        # neither can be part of the identity of an error kind.
        body = re.sub(r"\S*/\S+", "<path>", line.split(": ", 2)[-1])
        body = re.sub(r"\d+", "#", body)
        kinds.add(body)
    return kinds, tile_keys(copy / "textures" / "hires.txt")


# ---- CLI ------------------------------------------------------------------

def main(argv=None) -> int:
    ap = argparse.ArgumentParser(
        description="Build the background part of the artist kit for a recorded pack.")
    ap.add_argument("pack", help="pack folder (the one holding textures/sheets/)")
    ap.add_argument("--out", default=None,
                    help="kit folder (default: kit/ beside the recorded pack). "
                         "Never inside the pack — a recording is evidence.")
    ap.add_argument("--names", default=None,
                    help="naming file (kit contract); a matching id becomes a title")
    ap.add_argument("--verify", action="store_true",
                    help="rebuild a copy of the pack with the kit dropped in "
                         "and assert nothing was lost")
    args = ap.parse_args(argv)

    pack_dir = Path(args.pack)
    out_dir = Path(args.out) if args.out else pack_dir.parent / "kit"
    try:
        manifest = build_kit(pack_dir, out_dir, args.names)
    except (E.ComposeError, KitError, OSError) as exc:
        print(f"error: {exc}")
        return 2

    by_unit = collections.Counter(f["unit"] for f in manifest["files"])
    removed = sum(f.get("droppedCells", 0) for f in manifest["files"])
    print(f"{pack_dir}")
    print(f"  objects  {by_unit['object']:3d} ({removed} uniform cells removed from them)")
    print(f"  elements {by_unit['element']:3d} (recovered from adjacency.json)")
    print(f"  scene    {by_unit['scene']:3d} (screens, untouched)")
    print(f"  dropped  {len(manifest['dropped']):3d} (no ink at all)")
    print(f"  -> {out_dir}/{MANIFEST}")

    if not args.verify:
        return 0
    result = verify(pack_dir, out_dir)
    manifest["verify"] = result
    _write_manifest(out_dir, manifest)
    print(f"  verify: {result['errors']} new errors "
          f"({result['preexistingErrors']} the pack already had); "
          f"{result['keys_before']} (tileData, palette) keys before, "
          f"{result['keys_after']} after, "
          f"{result['lost']} lost, {result['added']} added")
    for line in result["errorLines"]:
        print(f"    {line}")
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
