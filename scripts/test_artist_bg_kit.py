"""Headless suite for the artist kit's background generator (F9.18).

`artist_bg_kit.py` decides what an artist is handed and what is thrown away, so
the two decisions that must never drift are the two this suite pins: a cell
that is one flat colour carries no art and is dropped *with its number said out
loud*, and cells that adjacency.json shows always sitting at the same offsets
are one scenery element even when the recorder's own `objNNN` grouping split
them across animation phases (the Contra base door sensor, the one background
unit the 2026-09-13 human panel found missing).

The fixture is a synthetic pack built here rather than `test_compose_engine`'s:
that one paints every cell a single solid colour, which is exactly the input
this tool exists to reject, so it cannot also be the input that proves the tool
keeps anything.

Run:  python3 scripts/test_artist_bg_kit.py
"""

import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import artist_bg_kit as K  # noqa: E402
import compose_engine as E  # noqa: E402
import sheet_repaint  # noqa: E402

_FAILURES = []

UNIT = 16
FLAT = (0, 0, 0, 255)
INK = (200, 40, 40, 255)

# Nodes 0 and 1 are flat fills; 2..9 carry a mark.
FLAT_NODES = {0, 1}

# The fixture's door sensor: two 2x1 halves (6-7 and 8-9), each a different
# blink phase of the same thing, both hanging off wall node 5 above them.
DOOR = {6, 7, 8, 9}


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


# ---- fixture ---------------------------------------------------------------

def _art(node):
    img = sheet_repaint.Image(UNIT, UNIT)
    for y in range(UNIT):
        for x in range(UNIT):
            img.set(x, y, FLAT)
    if node not in FLAT_NODES:
        # One drawn mark, shaped per node so two cells are never the same art.
        for x in range(2 + node % 3):
            img.set(x + 1, 1 + node % 4, INK)
    return img


def _tiles(node):
    return [{"tile": f"{node:032X}", "palette": "0F0F0F0F"}]


def _cell(slot, node, count, columns):
    stride = UNIT + E.GUTTER
    return {
        "index": slot,
        "x": E.GUTTER + (slot % columns) * stride,
        "y": E.GUTTER + (slot // columns) * stride,
        "count": count,
        "context": "scene",
        "metatile": node,
        "aliases": [],
        "label": "",
        "tiles": _tiles(node),
    }


def _write_sheet(sheets, stem, kind, nodes, counts, columns=4, extra=None):
    cells = [_cell(i, n, counts[n], columns) for i, n in enumerate(nodes)]
    rows = max(1, -(-len(cells) // columns))
    stride = UNIT + E.GUTTER
    canvas = sheet_repaint.Image(columns * stride + E.GUTTER, rows * stride + E.GUTTER)
    for cell in cells:
        canvas.paste(_art(cell["metatile"]), cell["x"], cell["y"])
    sheet_repaint.write_png(sheets / f"{stem}.png", canvas.clone())
    sheet_repaint.write_png(sheets / f"{stem}.orig.png", canvas.clone())
    doc = {
        "version": 1, "kind": kind, "gridUnit": UNIT,
        "gridPhase": {"x": 0, "y": 0}, "hasGrid": True,
        "cell": {"w": UNIT, "h": UNIT}, "gutter": E.GUTTER, "columns": columns,
        "sheet": f"{stem}.png", "reference": f"{stem}.orig.png", "cells": cells,
    }
    if extra:
        doc.update(extra)
    (sheets / f"{stem}.json").write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    return cells


COUNTS = {0: 90, 1: 80, 2: 70, 3: 60, 4: 50, 5: 40, 6: 4, 7: 4, 8: 2, 9: 2}

# Edges. The 2..3 / 3..4 pair is a wall: 3 follows 2 most of the time but not
# always, so it never reads as one thing. The door halves are deterministic
# both ways, and 5 is the only cell ever above any of them.
EDGES = [
    {"a": 2, "b": 3, "dir": "E", "count": 40},
    {"a": 2, "b": 4, "dir": "E", "count": 30},
    {"a": 3, "b": 4, "dir": "S", "count": 60},
    {"a": 5, "b": 6, "dir": "S", "count": 4},
    {"a": 5, "b": 8, "dir": "S", "count": 2},
    {"a": 6, "b": 7, "dir": "E", "count": 4},
    {"a": 8, "b": 9, "dir": "E", "count": 2},
]
# (outE, outS, inE, inS) consistent with the edges above.
DEGREES = {
    0: (0, 0, 0, 0),
    1: (0, 0, 0, 0),
    2: (70, 0, 0, 0),
    3: (0, 60, 40, 0),
    4: (0, 0, 30, 60),
    5: (0, 6, 0, 0),
    6: (4, 0, 0, 4),
    7: (0, 0, 4, 0),
    8: (2, 0, 0, 2),
    9: (0, 0, 2, 0),
}


def make_pack(root: Path, with_screen: bool = True) -> Path:
    sheets = root / "textures" / "sheets"
    sheets.mkdir(parents=True)
    _write_sheet(sheets, "metatiles", "metatiles", sorted(COUNTS), COUNTS)
    # obj000: nothing but flat fills -> dropped whole.
    _write_sheet(sheets, "obj000", "object", [0, 1], COUNTS, columns=2,
                 extra={"evidence": [{"a": 0, "b": 1, "dir": "E", "dx": 1, "dy": 0,
                                      "count": 90}]})
    # obj001: one flat cell and one drawn one -> the flat one goes, the sheet stays.
    _write_sheet(sheets, "obj001", "object", [1, 2], COUNTS, columns=2,
                 extra={"evidence": [{"a": 1, "b": 2, "dir": "S", "dx": 0, "dy": 1,
                                      "count": 70}]})
    # obj002: a wall crop, drawn, kept as is, laid out at the walk's offsets.
    _write_sheet(sheets, "obj002", "object", [2, 3, 4], COUNTS, columns=2,
                 extra={"evidence": [{"a": 2, "b": 3, "dir": "E", "dx": 1, "dy": 0,
                                      "count": 40},
                                     {"a": 3, "b": 4, "dir": "S", "dx": 0, "dy": 1,
                                      "count": 60}]})
    # obj003: one phase of the door, and only that phase — the recorder's
    # count >= 3 floor is why the other phase never became an object.
    _write_sheet(sheets, "obj003", "object", [6, 7], COUNTS, columns=2,
                 extra={"evidence": [{"a": 6, "b": 7, "dir": "E", "dx": 1, "dy": 0,
                                      "count": 4}]})
    nodes = []
    for node in sorted(COUNTS):
        out_e, out_s, in_e, in_s = DEGREES[node]
        nodes.append({"cell": node, "count": COUNTS[node], "context": "scene",
                      "outE": out_e, "outS": out_s, "inE": in_e, "inS": in_s,
                      "tiles": _tiles(node)})
    adj = {"version": 1, "kind": "adjacency",
           "background": {"vocabulary": "metatiles.json", "vocabularySize": len(COUNTS),
                          "gridUnit": UNIT, "distinctScreens": 3,
                          "nodes": nodes, "edges": EDGES}}
    (sheets / "adjacency.json").write_text(json.dumps(adj) + "\n", encoding="utf-8")
    # The key source `mep_build` rebuilds against: one <tile> rule per node,
    # so a rebuilt hires.txt has the same set of (tileData, palette) keys to
    # be compared with.
    lines = ["<ver>109", "<scale>1",
             "<supportedRom>0000000000000000000000000000000000000000",
             "<overscan>0,0,0,0", "<img>sheets/metatiles.png"]
    for i, node in enumerate(sorted(COUNTS)):
        lines.append(f"<tile>0,{node:032X},0F0F0F0F,{i * 16},0,1,N,0,{i}")
    (root / "textures" / "hires.txt").write_text("\n".join(lines) + "\n",
                                                 encoding="utf-8")
    if with_screen:
        backgrounds = root / "textures" / "backgrounds"
        backgrounds.mkdir(parents=True)
        frame = sheet_repaint.Image(256, 240)
        frame.paste(_art(3), 24, 16)
        sheet_repaint.write_png(backgrounds / "screen001.png", frame)
        sheet_repaint.write_png(backgrounds / "screen001.orig.png", frame.clone())
    return root


def _kit(td, **kwargs):
    pack = make_pack(Path(td) / "pack")
    out = Path(td) / "kit"
    return pack, out, K.build_kit(pack, out, **kwargs)


def _by_unit(manifest, unit):
    return [f for f in manifest["files"] if f["unit"] == unit]


def _ids(entry):
    return set(entry["ids"])


# ---- cases -----------------------------------------------------------------

def test_ink_ratio_separates_a_fill_from_a_drawing():
    with tempfile.TemporaryDirectory() as td:
        pack = E.Pack(make_pack(Path(td) / "pack"))
        sheet = next(s for s in pack.sheets if s.kind == "metatiles")
        by_node = {c["metatile"]: c for c in sheet.cells}
        flat = K.ink_ratio(K.cell_histogram(sheet, by_node[0]))
        drawn = K.ink_ratio(K.cell_histogram(sheet, by_node[3]))
        check(flat == 0.0, "a cell of one colour has an ink ratio of exactly 0", str(flat))
        check(drawn > 0.0, "a cell with a mark on it has ink", str(drawn))
        check(K.colour_name(K.cell_histogram(sheet, by_node[0]).most_common(1)[0][0])
              == "#000000", "the fill colour is reported, not just the fact of it")


def test_an_object_of_only_flat_cells_is_dropped_and_says_why():
    with tempfile.TemporaryDirectory() as td:
        _pack, _out, manifest = _kit(td)
        dropped = {d["path"]: d["why"] for d in manifest["dropped"]}
        check("obj000.png" in dropped, "an object with no ink anywhere is dropped",
              str(sorted(dropped)))
        why = dropped.get("obj000.png", "")
        check("2 cells" in why and "#000000" in why,
              "the drop carries the cell count and the colour behind it", why)
        check(not any("obj000" in f["path"] for f in manifest["files"]),
              "and it is not emitted as a painting surface anyway")


def test_a_partly_flat_object_keeps_only_what_was_drawn():
    with tempfile.TemporaryDirectory() as td:
        _pack, out, manifest = _kit(td)
        entry = next(f for f in _by_unit(manifest, "object") if f["source"] == "obj001.png")
        check(entry["cells"] == 1, "the flat cell is gone from the sheet", str(entry["cells"]))
        check(entry["droppedCells"] == 1, "and the count of removed cells is on the record",
              str(entry["droppedCells"]))
        check(any("obj001.png" in n and "1 of 2" in n for n in manifest["notes"]),
              "a note tells the artist which cells went and why",
              str([n for n in manifest["notes"] if "obj001" in n]))
        sidecar = json.loads((out / entry["path"]).with_suffix(".json").read_text())
        check([c["metatile"] for c in sidecar["cells"]] == [2],
              "the sidecar addresses exactly the surviving node",
              str([c["metatile"] for c in sidecar["cells"]]))


def test_a_kept_object_keeps_the_shape_the_walk_recovered():
    with tempfile.TemporaryDirectory() as td:
        _pack, out, manifest = _kit(td)
        entry = next(f for f in _by_unit(manifest, "object") if f["source"] == "obj002.png")
        check((entry["rows"], entry["columns"]) == (2, 2),
              "obj002's three cells stay an L, not a row",
              f"{entry['rows']}x{entry['columns']}")
        sidecar = json.loads((out / entry["path"]).with_suffix(".json").read_text())
        slots = {c["metatile"]: (c["x"], c["y"]) for c in sidecar["cells"]}
        check(slots[3][0] > slots[2][0] and slots[4][1] > slots[3][1],
              "and each cell sits where the evidence says it sat", str(slots))


def test_the_door_sensor_comes_out_as_one_file_with_all_its_phases():
    """The panel's finding, as an assertion: a thing the recorder split across
    animation phases — one of which became an objNNN and the rest nothing —
    is handed over whole."""
    with tempfile.TemporaryDirectory() as td:
        _pack, out, manifest = _kit(td)
        elements = _by_unit(manifest, "element")
        door = [e for e in elements if _ids(e) & {"bg006", "bg008"}]
        check(len(door) == 1, "the door sensor is exactly one file",
              str([sorted(_ids(e)) for e in elements]))
        if not door:
            return
        door = door[0]
        check(_ids(door) == {f"bg{n:03d}" for n in DOOR},
              "with every cell of every phase on it", str(sorted(_ids(door))))
        check(door["phases"] == 2, "and the phases counted", str(door["phases"]))
        check(door["absorbs"] == ["obj003.png"],
              "the objNNN that was one phase of it is folded in", str(door["absorbs"]))
        check(any(d["path"] == "obj003.png" and "phase" in d["why"]
                  for d in manifest["dropped"]),
              "and dropped[] says so rather than the sheet just vanishing",
              str(manifest["dropped"]))
        check((out / door["path"]).is_file() and
              (out / door["path"]).with_suffix(".orig.png").is_file(),
              "the element has its untouched reference twin beside it")


def test_an_element_an_object_already_shows_whole_is_not_emitted_twice():
    with tempfile.TemporaryDirectory() as td:
        _pack, _out, manifest = _kit(td)
        object_nodes = set()
        for entry in _by_unit(manifest, "object"):
            object_nodes |= _ids(entry)
        for entry in _by_unit(manifest, "element"):
            check(not _ids(entry) <= object_nodes,
                  f"{entry['path']} is not a subset of a sheet the artist already has",
                  str(sorted(_ids(entry))))
        check(len(_by_unit(manifest, "element")) == 1,
              "and the one element there is, is the one no object covers",
              str([sorted(_ids(e)) for e in _by_unit(manifest, "element")]))


def test_the_wall_never_fuses_into_one_unnameable_blob():
    """2 -E-> 3 is 40 of 70 — common, not certain. The measurement that set
    `_DETERMINISTIC_P` says a relation like that must not make an element."""
    with tempfile.TemporaryDirectory() as td:
        _pack, _out, manifest = _kit(td)
        for entry in _by_unit(manifest, "element"):
            check(not ({"bg002", "bg003"} <= _ids(entry)),
                  "a merely-frequent neighbour pair is not an element",
                  str(sorted(_ids(entry))))
        check(len(_by_unit(manifest, "element")) == 1,
              "so the only element is the door, not the wall",
              str([sorted(_ids(e)) for e in _by_unit(manifest, "element")]))


def test_the_manifest_is_the_shared_kit_fragment():
    with tempfile.TemporaryDirectory() as td:
        _pack, out, manifest = _kit(td)
        check((out / "kit-part-background.json").is_file(),
              "the fragment is named for its part")
        check(not (out / "kit.json").exists() and not (out / "ARTIST.md").exists(),
              "and the generator never writes the assembler's files")
        check(manifest["part"] == "background" and
              manifest["generator"] == "scripts/artist_bg_kit.py",
              "the fragment declares which generator made it")
        check(set(manifest) >= {"part", "generator", "pack", "files", "dropped",
                                "notes", "verify"},
              "with every key the contract names", str(sorted(manifest)))
        for entry in manifest["files"]:
            missing = {"path", "title", "unit", "rows", "columns", "cells",
                       "ids", "seen"} - set(entry)
            check(not missing, f"{entry['path']} carries every required field",
                  str(sorted(missing)))
            check(entry["seen"] is True,
                  f"{entry['path']} is marked as recorded, not inferred")
        check(bool(manifest["notes"]), "and the artist is told something in notes[]")


def test_every_emitted_surface_has_its_untouched_reference_beside_it():
    with tempfile.TemporaryDirectory() as td:
        _pack, out, manifest = _kit(td)
        for entry in manifest["files"]:
            png = out / entry["path"]
            twin = png.with_name(png.stem + ".orig.png")
            check(png.is_file(), f"{entry['path']} exists")
            check(twin.is_file(), f"{entry['path']} has its .orig.png twin")


def test_a_name_from_the_file_becomes_a_title_and_nothing_else_is_invented():
    with tempfile.TemporaryDirectory() as td:
        pack = make_pack(Path(td) / "pack")
        names = Path(td) / "names.json"
        names.write_text(json.dumps({
            "background": {"obj002": "the rock wall with vines"},
        }), encoding="utf-8")
        manifest = K.build_kit(pack, Path(td) / "kit", names_file=names)
        titled = next(f for f in _by_unit(manifest, "object") if "obj002" in f["ids"])
        check(titled["title"] == "the rock wall with vines",
              "a named id gets the human's caption", titled["title"])
        for entry in manifest["files"]:
            if entry is titled:
                continue
            check(any(i in entry["title"] for i in entry["ids"]),
                  f"{entry['path']} falls back to its own id rather than a made-up name",
                  entry["title"])


def test_the_screens_are_listed_and_copied_byte_for_byte():
    with tempfile.TemporaryDirectory() as td:
        pack, out, manifest = _kit(td)
        scene = _by_unit(manifest, "scene")
        check(len(scene) == 1, "the capture is listed as the scene surface", str(scene))
        if not scene:
            return
        source = pack / "textures" / "backgrounds" / "screen001.png"
        check((out / scene[0]["path"]).read_bytes() == source.read_bytes(),
              "and it is the recorded file, untouched")


def test_the_pack_it_reads_is_never_written_to():
    with tempfile.TemporaryDirectory() as td:
        pack = make_pack(Path(td) / "pack")
        before = {p.relative_to(pack): p.read_bytes()
                  for p in pack.rglob("*") if p.is_file()}
        K.build_kit(pack, Path(td) / "kit")
        after = {p.relative_to(pack): p.read_bytes()
                 for p in pack.rglob("*") if p.is_file()}
        check(before == after, "a recorded pack is evidence, not a workspace",
              str(sorted(set(after) ^ set(before))))


def test_verify_rebuilds_the_pack_with_nothing_lost():
    with tempfile.TemporaryDirectory() as td:
        pack, out, _manifest = _kit(td)
        result = K.verify(pack, out)
        check(result["ran"] is True, "verify reports that it ran")
        check(result["errors"] == 0, "the kit adds no build error",
              str(result["errorLines"]))
        check(result["lost"] == 0 and result["added"] == 0,
              "and the rebuilt pack addresses exactly the same (tileData, palette) keys",
              f"{result['keys_before']} -> {result['keys_after']}, "
              f"{result['lost']} lost, {result['added']} added")
        check(result["ok"] is True, "so verify passes", json.dumps(result))


def test_a_pack_without_captures_is_a_pack_not_an_error():
    with tempfile.TemporaryDirectory() as td:
        pack = make_pack(Path(td) / "pack", with_screen=False)
        manifest = K.build_kit(pack, Path(td) / "kit")
        check(_by_unit(manifest, "scene") == [], "no captures, no scene surface")
        check(bool(_by_unit(manifest, "object")), "the cell surfaces are still built")


def main():
    tests = [
        test_ink_ratio_separates_a_fill_from_a_drawing,
        test_an_object_of_only_flat_cells_is_dropped_and_says_why,
        test_a_partly_flat_object_keeps_only_what_was_drawn,
        test_a_kept_object_keeps_the_shape_the_walk_recovered,
        test_the_door_sensor_comes_out_as_one_file_with_all_its_phases,
        test_an_element_an_object_already_shows_whole_is_not_emitted_twice,
        test_the_wall_never_fuses_into_one_unnameable_blob,
        test_the_manifest_is_the_shared_kit_fragment,
        test_every_emitted_surface_has_its_untouched_reference_beside_it,
        test_a_name_from_the_file_becomes_a_title_and_nothing_else_is_invented,
        test_the_screens_are_listed_and_copied_byte_for_byte,
        test_the_pack_it_reads_is_never_written_to,
        test_verify_rebuilds_the_pack_with_nothing_lost,
        test_a_pack_without_captures_is_a_pack_not_an_error,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
