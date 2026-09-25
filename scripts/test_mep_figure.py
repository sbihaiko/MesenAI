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

import contextlib
import io
import json
import shutil
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


def _paint_node(out: Path, stem: str, doc: dict, node: int, color):
    target = next(c for c in doc["cells"] if c["node"] == node)
    fig = sheet_repaint.read_png(out / f"{stem}.png")
    fig.paste(T._solid(8 * SCALE, color), target["x"] * SCALE, target["y"] * SCALE)
    sheet_repaint.write_png(out / f"{stem}.png", fig)
    return target


def _cell_px(sheets: Path, sheet: str, node: int):
    doc = json.loads((sheets / f"{sheet}.json").read_text(encoding="utf-8"))
    cell = next(c for c in doc["cells"] if c.get("metatile") == node)
    return int(cell["x"]) * SCALE, int(cell["y"]) * SCALE


def test_a_figure_cut_from_the_vocabulary_lands_on_the_sheet_that_owns_the_key():
    """#413: a pose figure maps every cell to `sprites.json`, but in the built
    pack the untouched `spr000` group (rank 4) owns those keys. The paint must
    land on the owner's crop, so the rebuild changes no rule and the in-place
    reload (ADR-0212) can show it; writing `sprites.png` instead made it the
    painted crop, which re-pointed the key (painted beats untouched)."""
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=True)
        sheets = pack_dir / "textures" / "sheets"
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the pack builds before the import")
        manifest_before = (pack_dir / "textures" / "hires.txt").read_bytes()
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "pose000", out)
        check(all(c["sheet"] == "sprites.json" for c in doc["cells"]),
              "the pose figure's cells come from the sprite vocabulary", str(doc["cells"]))
        before = _snapshot(sheets)
        magenta = (255, 0, 255, 255)
        _paint_node(out, "pose000-figure", doc, 1, magenta)

        rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        check(rep["painted"] == 1 and rep["written"] == 1 and rep["sheets"] == ["spr000.png"],
              "the painted cell is written into spr000.png, the sheet that owns its key", json.dumps(rep))
        check(rep["rerouted"] == 1 and rep["sourceLeft"] == 1 and rep["moves"] == 0,
              "and reported as routed, with the vocabulary cell left alone", json.dumps(rep))
        after = _snapshot(sheets)
        check(after["sprites.png"] == before["sprites.png"], "sprites.png is untouched")
        sx, sy = _cell_px(sheets, "spr000", 1)
        (Path(td) / "spr000.before.png").write_bytes(before["spr000.png"])
        b_img = sheet_repaint.read_png(Path(td) / "spr000.before.png")
        a_img = sheet_repaint.read_png(sheets / "spr000.png")
        check(_differs_only_in(b_img, a_img, sx, sy, 8 * SCALE, 8 * SCALE) and a_img.get(sx, sy) == magenta,
              "spr000.png differs only inside node 1's cell, which carries the paint", str(a_img.get(sx, sy)))

        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the repainted pack builds")
        check((pack_dir / "textures" / "hires.txt").read_bytes() == manifest_before,
              "hires.txt is byte-identical to the build before the import (no rule moved)")

        rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        check(rep["written"] == 0 and rep["alreadyApplied"] == 1, "importing it again is a no-op",
              json.dumps(rep))
        doc2 = F.export_figure(E.Pack(pack_dir), "pose000", Path(td) / "figures2")
        fig2 = sheet_repaint.read_png(Path(td) / "figures2" / "pose000-figure.png")
        t = next(c for c in doc2["cells"] if c["node"] == 1)
        check(fig2.get(t["x"] * SCALE, t["y"] * SCALE) == magenta,
              "a re-export shows the paint that was routed to spr000")

        v = F.verify(pack_dir, restore={"spr000.png": before["spr000.png"]})
        check(v["ok"] and v["manifest_unchanged"],
              "verify: same key set, and the manifest is unchanged by the import", json.dumps(v))


def test_an_owner_with_other_art_is_skipped_and_the_move_is_reported():
    """Never paint over a different drawing: when the owning crop's twin is
    not the source cell's, the source is written (paint is never dropped) and
    the import says the next build re-points the key."""
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=True)
        sheets = pack_dir / "textures" / "sheets"
        # spr000's node-1 cell gets other original art (twin and sheet alike).
        sx, sy = _cell_px(sheets, "spr000", 1)
        for name, n in (("spr000.orig.png", 1), ("spr000.png", SCALE)):
            img = sheet_repaint.read_png(sheets / name)
            img.paste(T._solid(8 * n, (1, 2, 3, 255)), sx // SCALE * n, sy // SCALE * n)
            sheet_repaint.write_png(sheets / name, img)
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the pack builds before the import")
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "pose000", out)
        before = _snapshot(sheets)
        _paint_node(out, "pose000-figure", doc, 1, (255, 0, 255, 255))
        rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        check(rep["sheets"] == ["sprites.png"] and rep["moves"] == 1 and rep["rerouted"] == 0,
              "the source sheet is written and the move is reported", json.dumps(rep))
        v = F.verify(pack_dir, restore={"sprites.png": before["sprites.png"]})
        check(not v["manifest_unchanged"], "verify reports the manifest change", json.dumps(v))


def _k16(n):
    return {"tile": f"{0xA0 + n:032X}", "palette": "0F0F0F0F"}


def _make_unit16_pack(root: Path, owner_tiles) -> Path:
    """`obj010` is the figure: node 10 (four sub-tile keys K0..K3) beside node
    11. `obj011` holds node 10's art once more with `owner_tiles`; same rank
    (object), later name, so the build hands it every key the two share."""
    root = make_pack(root, with_poses=False)
    sheets = root / "textures" / "sheets"
    fig = [T._bg_cell(0, 10, 50), T._bg_cell(1, 11, 50)]
    fig[0]["tiles"] = [_k16(i) for i in range(4)]
    fig[1]["tiles"] = [_k16(i) for i in range(10, 14)]
    T._write_sheet(sheets, "obj010", "object", fig, 16, 4, scale=SCALE,
                   extra={"evidence": [{"a": 10, "b": 11, "dir": "E", "dx": 1, "dy": 0, "count": 50}]})
    own = [T._bg_cell(0, 10, 50)]
    own[0]["tiles"] = owner_tiles
    T._write_sheet(sheets, "obj011", "object", own, 16, 4, scale=SCALE)
    return root


def _painted_crops(pack_dir: Path) -> dict:
    """`{key: [(sheet, x, y)]}`: every 8x8 crop, across all sheets, that a
    cell emits for `key` and whose pixels differ from its twin's upscale."""
    pack = E.Pack(pack_dir)
    out = {}
    for sheet in pack.sheets:
        if not sheet.png_path.is_file():
            continue
        img = sheet_repaint.read_png(sheet.png_path)
        for cell in sheet.cells:
            if not isinstance(cell, dict) or "x" not in cell:
                continue
            for key, lx, ly in F._cell_keys(sheet, cell, False, 109):
                x, y = int(cell["x"]) + lx, int(cell["y"]) + ly
                twin = F._twin_crop(sheet, x, y)
                crop = img.crop(x * SCALE, y * SCALE, 8 * SCALE, 8 * SCALE)
                if twin is not None and crop.px != twin.upscale(SCALE).px:
                    out.setdefault(key, []).append((sheet.name, x, y))
    return out


def _rule_image(pack_dir: Path, tile: str, pal: str) -> str:
    hires = (pack_dir / "textures" / "hires.txt").read_text(encoding="utf-8").splitlines()
    images = [ln.strip()[5:].strip() for ln in hires if ln.startswith("<img>")]
    rule = next((ln for ln in hires if ln.startswith("<tile>") and f",{tile},{pal}," in ln), "")
    return images[int(rule[6:].split(",")[0])] if rule else ""


def _paint_unit16_node10(td: Path, pack_dir: Path, color):
    out = td / "figures"
    doc = F.export_figure(E.Pack(pack_dir), "obj010", out)
    target = next(c for c in doc["cells"] if c["node"] == 10)
    fig = sheet_repaint.read_png(out / "obj010-figure.png")
    fig.paste(T._solid(16 * SCALE, color), target["x"] * SCALE, target["y"] * SCALE)
    sheet_repaint.write_png(out / "obj010-figure.png", fig)
    return out / "obj010-figure.png", doc


def test_a_unit16_cell_with_mixed_ownership_is_written_once_and_reports_the_move():
    """#413 review: node 10 owns K0/K2/K3 itself while `obj011` cleanly owns
    K1. The source must be written (paint never dropped) and K1 must NOT be
    routed as well — two painted crops for one key is the #253 ambiguity."""
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack_dir = _make_unit16_pack(td / "pack", [_k16(20), _k16(1)])
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "unit-16 mixed: the pack builds first")
        check(_rule_image(pack_dir, _k16(1)["tile"], "0F0F0F0F").endswith("sheets/obj011.png")
              and _rule_image(pack_dir, _k16(0)["tile"], "0F0F0F0F").endswith("sheets/obj010.png"),
              "unit-16 mixed: K1 is drawn from obj011, K0 from obj010 before the import")
        magenta = (255, 0, 255, 255)
        png, _doc = _paint_unit16_node10(td, pack_dir, magenta)
        rep = F.import_figure(E.Pack(pack_dir), png)
        check(rep["painted"] == 1 and rep["written"] == 1 and rep["sheets"] == ["obj010.png"],
              "unit-16 mixed: only the source sheet is written", json.dumps(rep))
        check(rep["rerouted"] == 0 and rep["moves"] == 1,
              "unit-16 mixed: nothing is routed and the import says a reopen is needed", json.dumps(rep))
        painted = _painted_crops(pack_dir)
        doubled = {k: v for k, v in painted.items() if len(v) > 1}
        check(not doubled, "unit-16 mixed: no key has two painted crops", str(doubled))
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "unit-16 mixed: the repainted pack builds")
        homes = [_rule_image(pack_dir, _k16(i)["tile"], "0F0F0F0F") for i in range(4)]
        check(all(h.endswith("sheets/obj010.png") for h in homes),
              "unit-16 mixed: every key of the cell is drawn from obj010, the one crop that carries the paint", str(homes))
        sx, sy = _cell_px(pack_dir / "textures" / "sheets", "obj010", 10)
        img = sheet_repaint.read_png(pack_dir / "textures" / "sheets" / "obj010.png")
        check(img.get(sx, sy) == magenta and img.get(sx + 15 * SCALE, sy + 15 * SCALE) == magenta,
              "unit-16 mixed: obj010.png carries the paint")


def test_a_unit16_cell_owned_elsewhere_is_routed_and_the_manifest_holds():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack_dir = _make_unit16_pack(td / "pack", [_k16(i) for i in range(4)])
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "unit-16 routed: the pack builds first")
        manifest_before = (pack_dir / "textures" / "hires.txt").read_bytes()
        before = _snapshot(pack_dir / "textures" / "sheets")
        magenta = (255, 0, 255, 255)
        png, _doc = _paint_unit16_node10(td, pack_dir, magenta)
        rep = F.import_figure(E.Pack(pack_dir), png)
        check(rep["sheets"] == ["obj011.png"] and rep["rerouted"] == 1 and rep["sourceLeft"] == 1
              and rep["moves"] == 0, "unit-16 routed: all four keys land on obj011", json.dumps(rep))
        after = _snapshot(pack_dir / "textures" / "sheets")
        check(after["obj010.png"] == before["obj010.png"], "unit-16 routed: the source sheet is untouched")
        painted = _painted_crops(pack_dir)
        check(len(painted) == 4 and all(len(v) == 1 and v[0][0] == "obj011.png" for v in painted.values()),
              "unit-16 routed: each key has exactly one painted crop, on obj011", str(painted))
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "unit-16 routed: the repainted pack builds")
        check((pack_dir / "textures" / "hires.txt").read_bytes() == manifest_before,
              "unit-16 routed: hires.txt is byte-identical (no rule moved)")


def _two_halves(unit, left, right):
    """An 8x8-grid-aligned cell whose left and right halves differ, so an H
    mirror of it is a different picture (a solid cell is its own mirror)."""
    img = T._solid(unit, left)
    for y in range(0, unit, unit // 2):
        img.paste(T._solid(unit // 2, right), unit // 2, y)
    return img


def _bake_mirror_into_group(pack_dir: Path, node: int):
    """Make `spr000`'s `node` cell what the recorder writes for a sprite drawn
    H-flipped (ADR-0178): the pixels (sheet and twin) carry the flip baked in,
    the sidecar keys the flipped data with `source` + `mirror: H`. The
    vocabulary cell of the same node holds the unflipped art. Only the first
    `mep_build.py build` un-bakes the group cell (#255); until then its twin is
    the mirror of the vocabulary's."""
    sheets = pack_dir / "textures" / "sheets"
    a, b = (10, 20, 30, 255), (200, 100, 50, 255)
    tile, pal = _key(node)
    flipped = bytes(int(f"{x:08b}"[::-1], 2) for x in bytes.fromhex(tile)).hex().upper()  # H mirror
    for stem, left, right in (("sprites", a, b), ("spr000", b, a)):
        doc = json.loads((sheets / f"{stem}.json").read_text(encoding="utf-8"))
        cell = next(c for c in doc["cells"] if c.get("metatile") == node)
        if stem == "spr000":
            cell["tiles"] = [{"tile": flipped, "source": tile, "mirror": "H", "palette": pal}]
            (sheets / f"{stem}.json").write_text(json.dumps(doc), encoding="utf-8")
        art = _two_halves(8, left, right)
        for name, n in ((f"{stem}.orig.png", 1), (f"{stem}.png", SCALE)):
            img = sheet_repaint.read_png(sheets / name)
            img.paste(art if n == 1 else art.upscale(n), int(cell["x"]) * n, int(cell["y"]) * n)
            sheet_repaint.write_png(sheets / name, img)


def test_an_import_before_the_first_build_is_refused_and_the_recipe_holds_the_manifest():
    """#435: the kit's "When you are done" recipe copied the kit sheets into a
    copy of the recording and imported the figures before any build. The
    build un-bakes flip-baked crops in place (ADR-0178, #255), so the twins
    import compared were not the ones the build would slice: the owner's crop
    looked like other art, the paint went to the vocabulary, and the build
    re-pointed the key - *Reload Repainted Images* (ADR-0212) could not show
    it. An import whose plan would be made against sheets the next build
    rewrites is refused, untouched, with the build named; after one build
    the same import routes the paint and the manifest holds."""
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=True)
        _bake_mirror_into_group(pack_dir, 1)
        sheets = pack_dir / "textures" / "sheets"
        control = Path(td) / "control"
        shutil.copytree(pack_dir, control)
        check(mep_build.main(["build", str(control), "--quiet"]) == 0, "the unpainted control builds")
        manifest_control = (control / "textures" / "hires.txt").read_bytes()

        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "pose000", out)
        _paint_node(out, "pose000-figure", doc, 1, (255, 0, 255, 255))
        before = _snapshot(sheets)
        hires_before = (pack_dir / "textures" / "hires.txt").read_bytes()
        try:
            rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
            check(False, "an import before the first build is refused", json.dumps(rep))
        except F.FigureError as e:
            check("mep_build.py build" in str(e) and "spr000" in str(e),
                  "an import before the first build is refused, naming the build and the sheet", str(e))
        check(_snapshot(sheets) == before and (pack_dir / "textures" / "hires.txt").read_bytes() == hires_before,
              "and the refused import wrote nothing")
        check(F.main(["import", str(pack_dir), str(out / "pose000-figure.png")]) == 2,
              "the CLI exits 2, so the recipe's fail-fast loop holds the build back")

        # The recipe as it now reads: copy, build, import, build.
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the copy builds before the import")
        rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        check(rep["written"] == 1 and rep["rerouted"] == 1 and rep["moves"] == 0 and rep["sheets"] == ["spr000.png"],
              "after one build the paint is routed to the owner and no rule moves", json.dumps(rep))
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the repainted pack builds")
        check((pack_dir / "textures" / "hires.txt").read_bytes() == manifest_control,
              "hires.txt is byte-identical to the unpainted control")
        sx, sy = _cell_px(sheets, "spr000", 1)
        check(sheet_repaint.read_png(sheets / "spr000.png").get(sx, sy) == (255, 0, 255, 255),
              "and the paint is on the crop the manifest draws")


def _bake_recorded_mirror(pack_dir: Path, node: int, true_art, stems=("sprites", "spr000")):
    """What the recorder writes for a sprite the game always draws H-flipped
    (ADR-0178), on every sheet that shows it: the sidecar keys the flipped
    data with `source` + `mirror: H`, and the pixels (sheet and twin) are the
    H mirror of `true_art`, the unflipped tile. The kit's figures are cut from
    these crops, so they show the baked view; the first build un-bakes them
    back to `true_art` (#255) and drops the two fields."""
    sheets = pack_dir / "textures" / "sheets"
    tile, pal = _key(node)
    flipped = bytes(int(f"{x:08b}"[::-1], 2) for x in bytes.fromhex(tile)).hex().upper()
    baked = sheet_repaint.Image(true_art.width, true_art.height)
    for y in range(true_art.height):
        for x in range(true_art.width):
            baked.set(true_art.width - 1 - x, y, true_art.get(x, y))
    for stem in stems:
        doc = json.loads((sheets / f"{stem}.json").read_text(encoding="utf-8"))
        cell = next(c for c in doc["cells"] if c.get("metatile") == node)
        cell["tiles"] = [{"tile": flipped, "source": tile, "mirror": "H", "palette": pal}]
        (sheets / f"{stem}.json").write_text(json.dumps(doc), encoding="utf-8")
        for name, n in ((f"{stem}.orig.png", 1), (f"{stem}.png", SCALE)):
            img = sheet_repaint.read_png(sheets / name)
            img.paste(baked if n == 1 else baked.upscale(n), int(cell["x"]) * n, int(cell["y"]) * n)
            sheet_repaint.write_png(sheets / name, img)


def _drawn_crop(pack_dir: Path, node: int):
    """The `8*SCALE` square crop the built `hires.txt` draws `node`'s key from."""
    tile, pal = _key(node)
    hires = (pack_dir / "textures" / "hires.txt").read_text(encoding="utf-8").splitlines()
    images = [ln.strip()[5:].strip() for ln in hires if ln.startswith("<img>")]
    rule = next(ln for ln in hires if ln.startswith("<tile>") and f",{tile},{pal}," in ln)
    f = rule[6:].split(",")
    img = sheet_repaint.read_png(pack_dir / "textures" / images[int(f[0])])
    return img.crop(int(f[3]), int(f[4]), 8 * SCALE, 8 * SCALE)


def test_paint_on_a_mirrored_cell_lands_unmirrored_after_the_first_build():
    """#463: the kit exports its figures from the recording, where the
    recorder baked each sprite's OAM flip into the crop (ADR-0178). The
    #435 recipe builds once before the import, and that build un-bakes the
    crops (#255), so the figure became the mirror of the cells it imports
    into and import pasted the paint as it stood: painted art landed
    mirrored in game. The figure records each cell's flip and import
    un-bakes the paint the same way the build un-baked the crop."""
    magenta, green = (255, 0, 255, 255), (0, 255, 0, 255)
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=True)
        true_art = _two_halves(8, (10, 20, 30, 255), (200, 100, 50, 255))
        _bake_recorded_mirror(pack_dir, 1, true_art)
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "pose000", out)  # the kit: before any build
        cells = {c["node"]: c for c in doc["cells"]}
        check(cells[1].get("mirror") == ["H"] and "mirror" not in cells[0],
              "the export records the flip of the mirrored cell only",
              json.dumps({n: c.get("mirror") for n, c in cells.items()}))
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the recipe's first build")

        # Paint an asymmetric picture on a mirrored cell and on a plain one:
        # magenta on the left half, green on the right, as the figure shows them.
        fig = sheet_repaint.read_png(out / "pose000-figure.png")
        for node in (0, 1):
            fig.paste(_two_halves(8 * SCALE, magenta, green), cells[node]["x"] * SCALE, cells[node]["y"] * SCALE)
        sheet_repaint.write_png(out / "pose000-figure.png", fig)
        rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        check(rep["written"] == 2, "both painted cells are written", json.dumps(rep))
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the repainted pack builds")

        half = 4 * SCALE
        mirrored = _drawn_crop(pack_dir, 1)
        check(mirrored.get(0, 0) == green and mirrored.get(half, 0) == magenta,
              "the mirrored cell's crop stores the paint un-baked, so the run time's H flip draws "
              "magenta|green as painted", f"left {mirrored.get(0, 0)} right {mirrored.get(half, 0)}")
        plain = _drawn_crop(pack_dir, 0)
        check(plain.get(0, 0) == magenta and plain.get(half, 0) == green,
              "a cell that was never flipped takes the paint as it is",
              f"left {plain.get(0, 0)} right {plain.get(half, 0)}")


def test_pixel_ownership_reads_a_mirrored_cell_in_the_figure_orientation():
    """#463 (the #452 mechanism): ADR-0225 §3 gives a shared pixel to the
    frontmost cell whose recorded art is opaque there. It read that art from
    the pack after the first build, un-baked, so a mirrored front cell won
    the pixels where the figure shows it transparent and its paint landed
    over the transparent half of the tile."""
    magenta = (255, 0, 255, 255)
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=False)
        sheets = pack_dir / "textures" / "sheets"
        # Node 1 in front, node 0 behind and 4 px to its right. Node 1's tile
        # is opaque on its right half only (true art), so the game, drawing
        # it H-flipped, shows it opaque on the left and see-through where it
        # overlaps node 0: those 4 columns are node 0's.
        true_art = _two_halves(8, (0, 0, 0, 0), (200, 100, 50, 255))
        _bake_recorded_mirror(pack_dir, 1, true_art)
        T._write_poses(sheets, {"version": 1, "unit": 8, "frames": 10, "poses": [
            {"id": "pose000", "frames": 10, "size": [2, 1], "tiles": [
                {"node": 1, "dx": 0, "dy": 0, "px": 0, "py": 0, "z": 0},
                {"node": 0, "dx": 1, "dy": 0, "px": 4, "py": 0, "z": 1}]}]})
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "pose000", out)
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the recipe's first build")
        fig = sheet_repaint.read_png(out / "pose000-figure.png")
        for py in range(fig.height):
            for px in range(fig.width):
                if fig.get(px, py)[3]:
                    fig.set(px, py, magenta)
        sheet_repaint.write_png(out / "pose000-figure.png", fig)
        rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        check(rep["written"] >= 1, "the painted figure is written", json.dumps(rep))
        mirrored = _drawn_crop(pack_dir, 1)
        left, right = mirrored.get(0, 0), mirrored.get(4 * SCALE, 0)
        check(left == (0, 0, 0, 0) and right == magenta,
              "node 1 takes paint only where its tile is opaque; its transparent half stays a hole",
              f"left {left} right {right}; cells {json.dumps(doc['cells'])}")


def test_the_pending_flip_is_the_recorded_one_the_crop_has_since_lost():
    """#463: import flips a cell only by a flip the figure recorded AND the
    crop no longer carries - a crop still baked (never built, or a build that
    does not un-bake it) already matches the figure. Per 8x8 sub-tile, and
    each flip undoes itself."""
    baked = {"tiles": [{"tile": "1" * 32, "source": "2" * 32, "mirror": "H", "palette": "0F0F0F0F"}]}
    built = {"tiles": [{"tile": "2" * 32, "palette": "0F0F0F0F"}]}
    check(F.pending_flips({"mirror": ["H"]}, built, 8) == ["H"], "recorded, since un-baked: flip")
    check(F.pending_flips({"mirror": ["H"]}, baked, 8) == [""], "recorded, still baked: no flip")
    check(F.pending_flips({}, built, 8) == [""], "nothing recorded: no flip")
    quad = {"tiles": [{"tile": "1" * 32, "palette": "0F0F0F0F"}] * 4}
    check(F.pending_flips({"mirror": ["", "V", "", "HV"]}, quad, 16) == ["", "V", "", "HV"],
          "a unit-16 cell flips each sub-tile by its own record")
    img = sheet_repaint.Image(16, 16)
    for y in range(16):
        for x in range(16):
            img.set(x, y, (x, y, 7, 255))
    flips = ["H", "V", "", "HV"]
    once = F.mirror_image(img, flips, 8)
    check(once.get(0, 0) == (7, 0, 7, 255) and once.get(8, 0) == (8, 7, 7, 255)
          and once.get(0, 8) == (0, 8, 7, 255) and once.get(8, 8) == (15, 15, 7, 255),
          "each sub-tile is flipped in place, on its own axis", str([once.get(0, 0), once.get(8, 0)]))
    check(F.mirror_image(once, flips, 8).px == img.px, "and flipping twice restores the cell")


def test_an_import_into_a_pack_that_does_not_build_is_refused():
    """#443 review: when the throwaway build fails, the probe can say neither
    which crop owns a key nor whether the next build rewrites the sheets, so
    a painted import would be planned blind - exactly the re-pointing #435
    guards against. It is refused, untouched, like an unbuilt pack."""
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=True)
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the pack builds once")
        sheets = pack_dir / "textures" / "sheets"
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "pose000", out)
        _paint_node(out, "pose000-figure", doc, 1, (255, 0, 255, 255))
        before = _snapshot(sheets)
        real_build = F._quiet_build
        F._quiet_build = lambda folder: 1
        try:
            try:
                rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
                check(False, "an import into a pack that does not build is refused", json.dumps(rep))
            except F.FigureError as e:
                check("does not build" in str(e), "an import into a pack that does not build is refused", str(e))
            check(F.main(["import", str(pack_dir), str(out / "pose000-figure.png")]) == 2,
                  "and the CLI exits 2")
        finally:
            F._quiet_build = real_build
        check(_snapshot(sheets) == before, "and the refused import wrote nothing")

        # #444 review: a pack with no hires.txt yet is probed too, not waved through.
        (pack_dir / "textures" / "hires.txt").unlink()
        F._quiet_build = lambda folder: 1
        try:
            try:
                rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
                check(False, "a pack with no manifest that does not build is refused", json.dumps(rep))
            except F.FigureError as e:
                check("does not build" in str(e), "a pack with no manifest that does not build is refused", str(e))
        finally:
            F._quiet_build = real_build
        check(_snapshot(sheets) == before, "and that refusal wrote nothing either")


def _blank_node(pack_dir: Path, node: int):
    """Make `node`'s cell fully transparent (all colour 0) on every sprite
    sheet, twin and painted sheet alike: the blank sprite tile a game parks
    in a metasprite (Castlevania's `0000...` in Simon's walk, #452)."""
    sheets = pack_dir / "textures" / "sheets"
    for stem in ("sprites", "spr000"):
        doc = json.loads((sheets / f"{stem}.json").read_text(encoding="utf-8"))
        cell = next(c for c in doc["cells"] if c.get("metatile") == node)
        cell["tiles"] = [{"tile": "0" * 32, "palette": "0F0F0F0F"}]
        (sheets / f"{stem}.json").write_text(json.dumps(doc), encoding="utf-8")
        for name, n in ((f"{stem}.orig.png", 1), (f"{stem}.png", SCALE)):
            img = sheet_repaint.read_png(sheets / name)
            img.paste(T._solid(8 * n, (0, 0, 0, 0)), int(cell["x"]) * n, int(cell["y"]) * n)
            sheet_repaint.write_png(sheets / name, img)
    hires = pack_dir / "textures" / "hires.txt"
    tile, pal = _key(node)
    hires.write_text(hires.read_text(encoding="utf-8").replace(f",{tile},{pal},", f",{'0' * 32},0F0F0F0F,"),
                     encoding="utf-8")


def _cell_rect(sheets: Path, png: str, node: int):
    x, y = _cell_px(sheets, png[:-len(".png")], node)
    return sheet_repaint.read_png(sheets / png).crop(x, y, 8 * SCALE, 8 * SCALE).px


def test_a_fully_transparent_tile_is_never_painted_and_the_skip_is_reported():
    """#452: painting over a whole figure put paint on the blank member tile
    (its rect overlaps the body tiles at pixel precision, ADR-0225), import
    wrote it into two sheets as two different pictures and the build failed
    with #253's "painted tile ... lost to ...". The NES draws nothing for an
    all-transparent tile, so import skips it and says so."""
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=True)
        _blank_node(pack_dir, 2)
        sheets = pack_dir / "textures" / "sheets"
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the pack builds before the import")
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "pose000", out)
        check(any(c["node"] == 2 for c in doc["cells"]), "the blank member is part of the exported figure")
        fig = sheet_repaint.read_png(out / "pose000-figure.png")
        magenta = (255, 0, 255, 255)
        for py in range(fig.height):
            for px in range(fig.width):
                fig.set(px, py, magenta)  # paint over the whole figure, holes included
        sheet_repaint.write_png(out / "pose000-figure.png", fig)
        before = {n: _cell_rect(sheets, n, 2) for n in ("sprites.png", "spr000.png")}

        rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        skipped = rep.get("blank") or []
        check([b.get("node") for b in skipped] == [2],
              "the all-transparent tile is skipped and named in the report", json.dumps(rep))
        check(rep["painted"] == len(doc["cells"]) - 1 and rep["written"] == len(doc["cells"]) - 1,
              "every other painted cell is still written", json.dumps(rep))
        after = {n: _cell_rect(sheets, n, 2) for n in ("sprites.png", "spr000.png")}
        check(after == before, "no sheet carries paint on the blank tile's cell",
              str([n for n in after if after[n] != before[n]]))
        check(sheet_repaint.read_png(sheets / "spr000.png").get(*_cell_px(sheets, "spr000", 1)) == magenta,
              "while a body tile carries the paint")
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0,
              "the repainted pack builds (no #253 conflict on the blank key)")

        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            rc = F.main(["import", str(pack_dir), str(out / "pose000-figure.png")])
        text = buf.getvalue()
        check(rc == 0 and "1 fully transparent tile(s) skipped" in text and "0" * 32 in text,
              "the CLI reports the skipped tile with its key", text)


def test_import_decodes_each_reference_twin_once():
    """#466 review: the blank check re-read and decoded a sheet's whole
    `*.orig.png` once per painted cell, so a 138-cell figure decoded the same
    twin 138 times. Import decodes each twin once, whatever the cell count."""
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=True)
        check(mep_build.main(["build", str(pack_dir), "--quiet"]) == 0, "the pack builds before the import")
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "pose000", out)
        fig = sheet_repaint.read_png(out / "pose000-figure.png")
        for py in range(fig.height):
            for px in range(fig.width):
                fig.set(px, py, (255, 0, 255, 255))
        sheet_repaint.write_png(out / "pose000-figure.png", fig)
        sheets = (pack_dir / "textures" / "sheets").resolve()
        reads = {}
        real_read = sheet_repaint.read_png

        def spy(path, *a, **kw):
            path = Path(path)
            if path.name.endswith(".orig.png") and path.resolve().parent == sheets:
                reads[path.name] = reads.get(path.name, 0) + 1
            return real_read(path, *a, **kw)
        sheet_repaint.read_png = spy
        try:
            rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        finally:
            sheet_repaint.read_png = real_read
        check(rep["painted"] == len(doc["cells"]) >= 2, "several cells of one sheet are painted", json.dumps(rep))
        check(reads and max(reads.values()) <= 2,
              "each pack twin is decoded at most twice (the scale probe and the import)", json.dumps(reads))


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


def test_the_figure_palette_band_follows_first_use_in_reading_order_and_is_labelled():
    """The `palettes` band of a figure follows the same contract as a sheet's
    (`artist_kit_assemble.py` says so to the artist): first use in reading
    order, each group labelled with the cell index it belongs to."""
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_poses=False)
        # Node 0 (the first placed cell, at (0, 0)) wears a palette that sorts
        # *after* the shared one, so hex order and reading order disagree.
        T._patch_sp_tiles(pack_dir / "textures" / "sheets", 0,
                          [{"tile": "F" * 32, "palette": "FF36160F"}])
        group = json.loads((pack_dir / "textures" / "sheets" / "spr000.json").read_text(encoding="utf-8"))
        for cell in group["cells"]:
            if cell.get("metatile") == 0:
                cell["tiles"] = [{"tile": "F" * 32, "palette": "FF36160F"}]
        (pack_dir / "textures" / "sheets" / "spr000.json").write_text(json.dumps(group), encoding="utf-8")
        seen = {}
        real_write = F.ora_writer.write_surface

        def spy(*a, **kw):
            seen.update(kw)
            return real_write(*a, **kw)
        F.ora_writer.write_surface = spy
        try:
            doc = F.export_figure(E.Pack(pack_dir), "spr000", Path(td) / "figures")
        finally:
            F.ora_writer.write_surface = real_write
        first = doc["cells"][0]
        check(first["node"] == 0 and (first["x"], first["y"]) == (0, 0), "node 0 is the first cell in reading order")
        labels = seen.get("swatch_labels")
        check(labels and labels[0] == str(first["index"]),
              "the leftmost group is labelled with the first cell's index", str(labels))
        check(len(labels or ()) == 2 and labels[1] != labels[0],
              "the shared palette is listed once, under the first cell that wears it", str(labels))
        check(seen.get("swatches") == F.ora_writer.nes_swatches(["FF36160F", "0F0F0F0F"]),
              "swatches follow first use in reading order, not hex order")


if __name__ == "__main__":
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            print(f"-- {name}")
            fn()
    if _FAILURES:
        print(f"\n{len(_FAILURES)} failure(s): {_FAILURES}")
        sys.exit(1)
    print("\nall ok")
