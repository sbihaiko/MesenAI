#!/usr/bin/env python3
"""Headless suite for `mep_figure.py` (ADR-0209 Q2 (e) / Q3 (i)).

A synthetic pack in a temp dir, built on `test_compose_engine`'s fixtures: a
sprite vocabulary at scale 2, a `spr000` group whose `evidence[]` describes two
poses stacked on one another, and optionally the `poses.json` that states the
real silhouette. What is asserted:

  * export reassembles the group into ONE PNG through the evidence offsets
    (walk) or the pose sidecar, at the pack's scale, with a 1x twin and a
    sidecar mapping every cell rect back to `(sheet, cell index)`;
  * an unpainted round-trip writes zero cells;
  * painting one cell of the figure changes exactly that sheet cell, and the
    vocabulary sheet is untouched;
  * `mep_build.py build` on the rebuilt pack exits 0 with the same
    `(tileData, palette)` key set (ADR-0183 §4), and `mep_lint.py` exits 0;
  * a resized figure is refused rather than half-applied.

Run:  python3 scripts/test_mep_figure.py
"""

import json
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import compose_engine as E  # noqa: E402
import mep_build  # noqa: E402
import mep_figure as F  # noqa: E402
import sheet_repaint  # noqa: E402
import test_compose_engine as T  # noqa: E402 — the synthetic pack fixtures

_FAILURES = []
SCALE = 2
SPRITE_NODES = (0, 1, 2, 3, 4)
GROUP = (0, 1, 2, 3, 4)


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def _key(node):
    return f"{node + 1:032X}", "0F0F0F0F"


def _cell_with_key(cell, node):
    tile, pal = _key(node)
    cell["tiles"] = [{"tile": tile, "palette": pal}]
    return cell


def make_pack(root: Path, with_poses: bool) -> Path:
    root = T.make_pack(root, with_obj_sheet=True, scale=SCALE)
    sheets = root / "textures" / "sheets"
    # Sprite vocabulary: five nodes, each with its own key and its own colour.
    voc = [_cell_with_key(T._sprite_cell(i, n, 1000 - i), n) for i, n in enumerate(SPRITE_NODES)]
    T._write_sheet(sheets, "sprites", "sprites", voc, 8, 4, scale=SCALE)
    # The group: nodes 0..4 cut out of the vocabulary; its evidence is the
    # two-pose stack of test_compose_engine (node 3 refused at (1,0), node 4
    # under the count floor).
    grp = [_cell_with_key(T._sprite_cell(i, n, 100), n) for i, n in enumerate(GROUP)]
    T._write_sheet(sheets, "spr000", "sprite", grp, 8, 4, scale=SCALE,
                   extra={"evidence": T._SPR000_EVIDENCE})
    if with_poses:
        T._write_poses(sheets, T._poses_doc())
    # An object group too: two background metatiles side by side.
    obj = [T._bg_cell(0, 2, 50), T._bg_cell(1, 3, 50)]
    T._write_sheet(sheets, "obj001", "object", obj, 16, 4, scale=SCALE,
                   extra={"evidence": [{"a": 2, "b": 3, "dir": "E", "dx": 1, "dy": 0, "count": 50}]})
    lines = ["<ver>109", f"<scale>{SCALE}",
             "<supportedRom>0000000000000000000000000000000000000000",
             "<overscan>0,0,0,0", "<img>sheets/sprites.png"]
    for i, n in enumerate(SPRITE_NODES):
        tile, pal = _key(n)
        lines.append(f"<tile>0,{tile},{pal},{i * 16},0,1,N,0,{i}")
    lines.append(f"<tile>0,{T._tiles()[0]['tile']},{T._tiles()[0]['palette']},0,32,1,N,0,99")
    (root / "textures" / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return root


def _snapshot(sheets: Path):
    return {p.name: p.read_bytes() for p in sheets.glob("*.png")}


def _rect_equal(a, b, x, y, w, h):
    return a.crop(x, y, w, h).px == b.crop(x, y, w, h).px


def _differs_only_in(before, after, x, y, w, h):
    """True when `after` differs from `before` inside the rect and nowhere else."""
    if before.width != after.width or before.height != after.height:
        return False
    if _rect_equal(before, after, x, y, w, h):
        return False
    for py in range(before.height):
        for px in range(before.width):
            inside = x <= px < x + w and y <= py < y + h
            if not inside and before.get(px, py) != after.get(px, py):
                return False
    return True


def test_export_walks_two_poses_into_one_png():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=False)
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "spr000", out)
        check(doc["source"] == "walk", "without poses.json the layout is the evidence walk", doc["source"])
        check(doc["size"] == [3, 2], "the two poses collapse into one 3x2 figure", str(doc["size"]))
        placed = {c["node"]: (c["x"], c["y"]) for c in doc["cells"]}
        check(placed == {0: (0, 0), 1: (8, 0), 2: (8, 8), 3: (16, 8)},
              "each cell lands at its walked offset in 1x figure pixels", str(placed))
        check(doc["unplaced"] == [4], "the member the walk could not place is reported, not drawn",
              str(doc["unplaced"]))
        check(all(c["sheet"] == "spr000.json" and "index" in c for c in doc["cells"]),
              "every cell maps back to the group sheet and a cell index")
        check(doc["assetName"] == "spr000-figure.png", "the asset name is the F12.4 layer name",
              doc["assetName"])
        fig = sheet_repaint.read_png(out / "spr000-figure.png")
        twin = sheet_repaint.read_png(out / "spr000-figure.orig.png")
        check((fig.width, fig.height) == (3 * 8 * SCALE, 2 * 8 * SCALE), "the surface is at the pack's scale",
              f"{fig.width}x{fig.height}")
        check((twin.width, twin.height) == (24, 16), "the twin is 1x", f"{twin.width}x{twin.height}")
        check(fig.get(8 * SCALE, 0) == T._node_color(1) and twin.get(8, 0) == T._node_color(1),
              "the pixels are node 1's art at its slot", str(fig.get(8 * SCALE, 0)))
        check(fig.get(0, 8 * SCALE) == (0, 0, 0, 0), "an empty slot stays transparent")


def test_export_from_the_pose_sidecar_reaches_the_vocabulary_sheet():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=True)
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "spr000", out)
        check(doc["source"] == "poses" and doc["pose"] == "pose000",
              "with poses.json the layout is the pose's", f"{doc['source']} {doc['pose']}")
        check(doc["size"] == [2, 3] and len(doc["cells"]) == 5, "all five members are placed",
              f"{doc['size']} {len(doc['cells'])}")
        homes = {c["node"]: c["sheet"] for c in doc["cells"]}
        check(all(homes[n] == "spr000.json" for n in GROUP),
              "a member on the group sheet maps to the group sheet cell", str(homes))
        # A bare pose id works too, and a node the group does not hold maps to
        # the vocabulary sheet.
        (pack_dir / "textures" / "sheets" / "spr000.json").unlink()
        (pack_dir / "textures" / "sheets" / "spr000.png").unlink()
        doc2 = F.export_figure(E.Pack(pack_dir), "pose000", out)
        check(doc2["source"] == "pose" and all(c["sheet"] == "sprites.json" for c in doc2["cells"]),
              "a pose named directly maps every cell onto sprites.json", str(doc2["cells"]))


def test_object_group_exports_at_unit_16():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=False)
        doc = F.export_figure(E.Pack(pack_dir), "obj001", Path(td) / "figures")
        check(doc["unit"] == 16 and doc["size"] == [2, 1] and doc["source"] == "walk",
              "an objNNN group walks its evidence at the background unit",
              f"{doc['unit']} {doc['size']} {doc['source']}")


def test_round_trip_unpainted_paints_one_cell_and_rebuilds():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=True)
        sheets = pack_dir / "textures" / "sheets"
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "spr000", out)
        before = _snapshot(sheets)

        rep = F.import_figure(E.Pack(pack_dir), out / "spr000-figure.png")
        check(rep["painted"] == 0 and rep["written"] == 0 and rep["sheets"] == [],
              "an unpainted figure writes zero cells", json.dumps(rep))
        check(_snapshot(sheets) == before, "and no sheet PNG changed at all")

        # Paint node 1's cell in the figure (magenta, at the pack's scale).
        target = next(c for c in doc["cells"] if c["node"] == 1)
        b_img = sheet_repaint.read_png(sheets / "spr000.png")
        fig = sheet_repaint.read_png(out / "spr000-figure.png")
        fig.paste(T._solid(8 * SCALE, (255, 0, 255, 255)), target["x"] * SCALE, target["y"] * SCALE)
        sheet_repaint.write_png(out / "spr000-figure.png", fig)

        rep = F.import_figure(E.Pack(pack_dir), out / "spr000-figure.png")
        check(rep["painted"] == 1 and rep["written"] == 1 and rep["sheets"] == ["spr000.png"],
              "one painted cell is written into the group sheet", json.dumps(rep))
        after = _snapshot(sheets)
        changed = sorted(n for n in after if after[n] != before[n])
        check(changed == ["spr000.png"], "exactly one sheet PNG changed", str(changed))
        a_img = sheet_repaint.read_png(sheets / "spr000.png")
        sx, sy = target["sheetX"] * SCALE, target["sheetY"] * SCALE
        check(_differs_only_in(b_img, a_img, sx, sy, 8 * SCALE, 8 * SCALE),
              "and it differs from what it was only inside that cell's rect", f"({sx},{sy})")
        check(a_img.get(sx, sy) == (255, 0, 255, 255), "with the artist's pixels")

        rep = F.import_figure(E.Pack(pack_dir), out / "spr000-figure.png")
        check(rep["written"] == 0 and rep["alreadyApplied"] == 1,
              "importing the same file again is a no-op", json.dumps(rep))

        # Re-exporting shows the paint (the surface is cut from the painted sheet).
        doc2 = F.export_figure(E.Pack(pack_dir), "spr000", Path(td) / "figures2")
        fig2 = sheet_repaint.read_png(Path(td) / "figures2" / "spr000-figure.png")
        twin2 = sheet_repaint.read_png(Path(td) / "figures2" / "spr000-figure.orig.png")
        check(fig2.get(target["x"] * SCALE, target["y"] * SCALE) == (255, 0, 255, 255)
              and twin2.get(target["x"], target["y"]) == T._node_color(1),
              "a re-export carries the paint on the surface and the original on the twin")
        check(doc2["cells"] == doc["cells"], "with the same cell mapping")

        # ADR-0183 §4: the rebuilt pack keys exactly the same pairs.
        keys_before = F.hires_keys(pack_dir / "textures" / "hires.txt")
        rc = mep_build.main(["build", str(pack_dir), "--quiet"])
        keys_after = F.hires_keys(pack_dir / "textures" / "hires.txt")
        check(rc == 0, "mep_build.py build exits 0 on the repainted pack", str(rc))
        check(keys_before == keys_after, "with the same (tileData, palette) key set",
              f"{len(keys_before)} -> {len(keys_after)}")
        hires = (pack_dir / "textures" / "hires.txt").read_text(encoding="utf-8").splitlines()
        images = [ln.strip()[5:].strip() for ln in hires if ln.startswith("<img>")]
        tile, pal = _key(1)
        rule = next((ln for ln in hires if ln.startswith("<tile>") and f",{tile},{pal}," in ln), "")
        img = images[int(rule[6:].split(",")[0])] if rule else ""
        check(img.endswith("sheets/spr000.png"), "the painted key now points at the group sheet",
              f"{rule!r} -> {img!r}")
        lint = subprocess.run([sys.executable, str(Path(__file__).resolve().parent / "mep_lint.py"),
                               str(pack_dir), "--quiet"], capture_output=True, text=True)
        check(lint.returncode == 0, "mep_lint.py exits 0", lint.stdout[-800:] + lint.stderr[-800:])
        v = F.verify(pack_dir)
        check(v["ok"], "verify passes after the import", json.dumps(v))
        # The control: with the group sheet put back to its pre-import bytes,
        # the key set is still the same — painting adds and removes nothing.
        v = F.verify(pack_dir, restore={"spr000.png": before["spr000.png"]})
        check(v["ok"] and v["keys_before"] == v["keys_after"] == 6,
              "and against a control rebuilt without the paint", json.dumps(v))


def test_a_resized_figure_is_refused():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=False)
        out = Path(td) / "figures"
        F.export_figure(E.Pack(pack_dir), "spr000", out)
        fig = sheet_repaint.read_png(out / "spr000-figure.png")
        sheet_repaint.write_png(out / "spr000-figure.png", fig.crop(0, 0, fig.width - 1, fig.height))
        try:
            F.import_figure(E.Pack(pack_dir), out / "spr000-figure.png")
            check(False, "a resized figure is refused")
        except F.FigureError as e:
            check("resized" in str(e), "a resized figure is refused, and says so", str(e))


def test_cli_round_trip():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=True)
        out = Path(td) / "figures"
        check(F.main(["export", str(pack_dir), "spr000", "--out", str(out)]) == 0, "CLI export exits 0")
        check((out / "spr000-figure.json").is_file(), "and writes the sidecar")
        check(F.main(["import", str(pack_dir), str(out / "spr000-figure.png"), "--verify"]) == 0,
              "CLI import --verify exits 0 on an unpainted figure")
        check(F.main(["export", str(pack_dir), "spr999", "--out", str(out)]) == 2,
              "an unknown figure is exit 2")


if __name__ == "__main__":
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            print(f"-- {name}")
            fn()
    if _FAILURES:
        print(f"\n{len(_FAILURES)} failure(s): {_FAILURES}")
        sys.exit(1)
    print("\nall ok")
