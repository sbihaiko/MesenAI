#!/usr/bin/env python3
"""Headless suite for ADR-0225 (F12.18): a pose keeps its pixel offsets.

On `test_mep_figure`'s synthetic pack (a sprite vocabulary of solid-colour
cells at scale 2) with a `poses.json` whose tiles carry `px`/`py`/`z` in the
shape Contra's player has (legs at y 14 overlapping the torso's bottom rows,
a torso 4 px right of the legs, a foot 3 px off the grid). What is asserted:

  * a sidecar without `px`/`py` reads as `px = dx * unit` (the fallback), and
    one with them reads them, `z` included;
  * `mep_figure.py export` composes the pose at pixel precision, back to
    front, with no gutter inside the figure — the twin equals a hand-drawn
    composite of the same tiles at the same pixels;
  * `import` of a painted figure returns each overlapped pixel to the
    frontmost cell whose original pixel is opaque there (else the frontmost),
    the hidden cell keeps its own pixel, and `mep_build.py build` keeps the
    key set (ADR-0183 §4);
  * the kit's Figures view lays a cycle's phases out at pixel precision on
    one baseline with one empty cell between boxes and none inside a figure;
  * the composition editor's `pose_art` draws at pixel precision;
  * `mep_lint.py` warns when `dx != ToCells(px)` and says nothing otherwise.

Usage: python3 scripts/test_pose_pixel_offsets.py
"""

import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import artist_kit as K  # noqa: E402
import compose_engine as E  # noqa: E402
import mep_build  # noqa: E402
import mep_figure as F  # noqa: E402
import mep_lint  # noqa: E402
import sheet_repaint  # noqa: E402
import test_compose_engine as T  # noqa: E402
import test_mep_figure as TF  # noqa: E402 — the synthetic pack

_FAILURES = []
SCALE = TF.SCALE

# node -> (dx, dy, px, py, z). Node 0 is the frontmost leg, drawn over the
# torso's bottom rows (node 2); node 3 sits 3 px off the grid (dx rounds to 1).
RUN_POSE = {
    0: (0, 2, 0, 14, 0),
    1: (1, 0, 4, 0, 1),
    2: (1, 1, 4, 8, 2),
    3: (1, 2, 11, 16, 3),
}
# The second phase: torso at +3, which ToCells rounds to 0.
RUN_POSE_B = {
    0: (0, 2, 0, 14, 0),
    1: (0, 0, 3, 0, 1),
    2: (0, 1, 3, 8, 2),
    3: (1, 2, 11, 16, 3),
}


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def _pose_entry(pose_id, layout, frames=400, with_pixels=True):
    tiles = []
    for node, (dx, dy, px, py, z) in sorted(layout.items()):
        t = {"node": node, "dx": dx, "dy": dy}
        if with_pixels:
            t.update({"px": px, "py": py, "z": z})
        tiles.append(t)
    cols = max(v[0] for v in layout.values()) + 1
    rows = max(v[1] for v in layout.values()) + 1
    return {"id": pose_id, "frames": frames, "size": [cols, rows], "tiles": tiles}


def make_pack(root: Path, with_pixels=True, cycle=False) -> Path:
    pack = TF.make_pack(root, with_poses=False)
    doc = T._poses_doc()
    doc["poses"] = [_pose_entry("pose000", RUN_POSE, 500, with_pixels),
                    _pose_entry("pose001", RUN_POSE_B, 400, with_pixels)]
    if cycle:
        doc["cycles"] = [{"id": "cycle000", "poses": ["pose000", "pose001"],
                          "hold": [8, 8], "repeats": 6}]
    T._write_poses(pack / "textures" / "sheets", doc)
    return pack


def _composite(pack, layout):
    """The figure drawn by hand the way the PPU does: every tile at its pixel
    offset, back to front, opaque pixels only."""
    w = max(v[2] for v in layout.values()) + 8
    h = max(v[3] for v in layout.values()) + 8
    img = sheet_repaint.Image(w, h)
    for node, (_dx, _dy, px, py, _z) in sorted(layout.items(), key=lambda kv: -kv[1][4]):
        art = pack.node_art(node, sprite=True)
        for y in range(8):
            for x in range(8):
                p = art.get(x, y)
                if p[3]:
                    img.set(px + x, py + y, p)
    return img


def _clear_sprite_pixels(pack_dir: Path, node, rect):
    """Make `rect` (cell-local x, y, w, h) of `node`'s vocabulary cell
    transparent, in the painted sheet and its twin alike."""
    sheets = pack_dir / "textures" / "sheets"
    doc = json.loads((sheets / "sprites.json").read_text(encoding="utf-8"))
    cell = next(c for c in doc["cells"] if c.get("metatile") == node)
    x, y, w, h = rect
    for name, scale in (("sprites.orig.png", 1), ("sprites.png", SCALE)):
        img = sheet_repaint.read_png(sheets / name)
        img.paste(sheet_repaint.Image(w * scale, h * scale),
                  (int(cell["x"]) + x) * scale, (int(cell["y"]) + y) * scale)
        sheet_repaint.write_png(sheets / name, img)


def test_a_sidecar_without_pixels_reads_through_the_fallback():
    with tempfile.TemporaryDirectory() as td:
        pack = E.Pack(make_pack(Path(td) / "pack", with_pixels=False))
        pose = pack.poses.by_id("pose000")
        check(pose.pixels == {n: (v[0] * 8, v[1] * 8) for n, v in RUN_POSE.items()},
              "without px/py a tile reads as px = dx * unit", str(pose.pixels))
        check(pose.z == {}, "and has no z", str(pose.z))
        art = pack.pose_art(pose)
        check((art.width, art.height) == (16, 24), "pose_art keeps the old cell-grid canvas",
              f"{art.width}x{art.height}")
        pack2 = E.Pack(make_pack(Path(td) / "pack2", with_pixels=True))
        pose2 = pack2.poses.by_id("pose000")
        check(pose2.pixels == {n: (v[2], v[3]) for n, v in RUN_POSE.items()},
              "with px/py the reader keeps the pixels", str(pose2.pixels))
        check(pose2.z == {n: v[4] for n, v in RUN_POSE.items()}, "and the z order", str(pose2.z))
        check(pose2.tiles == pose.tiles, "tiles (cell units) are the same either way")


def test_export_composes_the_pose_at_pixel_precision():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack")
        pack = E.Pack(pack_dir)
        out = Path(td) / "figures"
        doc = F.export_figure(pack, "pose000", out)
        placed = {c["node"]: (c["x"], c["y"]) for c in doc["cells"]}
        check(placed == {n: (v[2], v[3]) for n, v in RUN_POSE.items()},
              "every cell sits at its px/py (no rounding, no gutter)", str(placed))
        check(doc["version"] == 2 and doc["pixelSize"] == [19, 24],
              "version 2 sidecar, 1x canvas = the pose's pixel extent", f"{doc['version']} {doc['pixelSize']}")
        check({c["node"]: c.get("z") for c in doc["cells"]} == {n: v[4] for n, v in RUN_POSE.items()},
              "the sidecar records z for an overlapping pose")
        twin = sheet_repaint.read_png(out / "pose000-figure.orig.png")
        expected = _composite(pack, RUN_POSE)
        check(twin.px == expected.px, "the twin matches a back-to-front composite pixel for pixel")
        check(twin.get(4, 14) == T._node_color(0), "the front leg covers the torso where they overlap",
              str(twin.get(4, 14)))
        fig = sheet_repaint.read_png(out / "pose000-figure.png")
        check((fig.width, fig.height) == (19 * SCALE, 24 * SCALE), "the surface is at the pack's scale",
              f"{fig.width}x{fig.height}")
        # The +3 px phase keeps its 3 px, which ToCells would have rounded to 0.
        doc_b = F.export_figure(pack, "pose001", out)
        check({c["node"]: c["x"] for c in doc_b["cells"]}[1] == 3, "a +3 px torso stays at x 3")


def test_import_returns_overlapped_pixels_to_the_front_cell():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack")
        sheets = pack_dir / "textures" / "sheets"
        # The overlap rule is under test, not where the paint lands (#413):
        # without the spr000 group, sprites.png owns every key, so it is the
        # sheet the import writes.
        for p in sheets.glob("spr000*"):
            p.unlink()
        # The leg (node 0) overlaps the torso (node 2) on cell-local x 4..7,
        # y 0..1. Its row 0 there is made transparent, so node 2 shows through
        # on that row and stays hidden behind the leg on row 1.
        _clear_sprite_pixels(pack_dir, 0, (4, 0, 4, 1))
        out = Path(td) / "figures"
        F.export_figure(E.Pack(pack_dir), "pose000", out)
        before = sheet_repaint.read_png(sheets / "sprites.png")
        keys_before = F.hires_keys(pack_dir / "textures" / "hires.txt")

        rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        check(rep["painted"] == 0 and rep["written"] == 0, "an unpainted overlapping figure writes nothing",
              json.dumps(rep))

        fig = sheet_repaint.read_png(out / "pose000-figure.png")
        magenta = (255, 0, 255, 255)
        sheet_repaint.write_png(out / "pose000-figure.png", _fill(fig.width, fig.height, magenta))
        rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        check(rep["painted"] == 4 and rep["written"] == 4 and rep["overlapped"] == 2,
              "all four cells are written, two of them through the overlap rule", json.dumps(rep))
        after = sheet_repaint.read_png(sheets / "sprites.png")
        doc = json.loads((sheets / "sprites.json").read_text(encoding="utf-8"))
        home = {c["metatile"]: (int(c["x"]) * SCALE, int(c["y"]) * SCALE) for c in doc["cells"]}

        def at(img, node, lx, ly):
            x, y = home[node]
            return img.get(x + lx * SCALE, y + ly * SCALE)

        check(at(after, 2, 0, 7) == before.get(home[2][0], home[2][1] + 7 * SCALE),
              "a torso pixel hidden behind the opaque leg keeps its own art",
              f"{at(after, 2, 0, 7)}")
        check(at(after, 2, 0, 6) == magenta,
              "a torso pixel under the leg's transparent corner takes the paint")
        check(at(after, 0, 5, 0) == (0, 0, 0, 0),
              "the leg's transparent corner stays the leg's own (transparent) pixel", str(at(after, 0, 5, 0)))
        check(at(after, 0, 0, 0) == magenta and at(after, 2, 7, 0) == magenta,
              "unshared pixels take the paint on both cells")

        rc = mep_build.main(["build", str(pack_dir), "--quiet"])
        keys_after = F.hires_keys(pack_dir / "textures" / "hires.txt")
        check(rc == 0 and keys_before == keys_after,
              "mep_build.py build exits 0 with the key set unchanged (ADR-0183 §4)",
              f"rc={rc} {len(keys_before)} -> {len(keys_after)}")


def _fill(w, h, color):
    img = sheet_repaint.Image(w, h)
    for y in range(h):
        for x in range(w):
            img.set(x, y, color)
    return img


def test_a_version_1_sidecar_still_imports():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", with_pixels=False)
        out = Path(td) / "figures"
        F.export_figure(E.Pack(pack_dir), "pose000", out)
        side = out / "pose000-figure.json"
        doc = json.loads(side.read_text(encoding="utf-8"))
        doc["version"] = 1
        side.write_text(json.dumps(doc), encoding="utf-8")
        rep = F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
        check(rep["cells"] == 4 and rep["written"] == 0, "a version 1 sidecar is still read", json.dumps(rep))
        doc["version"] = 3
        side.write_text(json.dumps(doc), encoding="utf-8")
        try:
            F.import_figure(E.Pack(pack_dir), out / "pose000-figure.png")
            check(False, "an unknown sidecar version is refused")
        except F.FigureError:
            check(True, "an unknown sidecar version is refused")


def test_the_kit_figures_view_places_a_cycle_at_pixel_precision():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack", cycle=True)
        kit = Path(td) / "kit"
        pack, builder, grids = K.build_kit(pack_dir, kit / "sheets", K.DEFAULT_COLUMNS, K.DEFAULT_ROWS,
                                           str(pack_dir), None, figures_out=kit / "figures")
        cyc = next((g for g in grids if g.kind == "cycle"), None)
        check(cyc is not None and cyc.figure, "the cycle grid gets a Figures view",
              str([(g.kind, g.figure) for g in grids]))
        if cyc is None or not cyc.figure:
            return
        doc = json.loads((kit / cyc.figure).with_suffix(".json").read_text(encoding="utf-8"))
        by_pose = {}
        for c in doc["cells"]:
            by_pose.setdefault(c["pose"], {})[c["node"]] = (c["x"], c["y"])
        a, b = by_pose.get("pose000", {}), by_pose.get("pose001", {})
        check(len(a) == 4 and len(b) == 4, "both phases are on the view", str(by_pose))
        if len(a) != 4 or len(b) != 4:
            return
        oa = (a[0][0], a[0][1] - 14)
        check(all(a[n] == (oa[0] + v[2], oa[1] + v[3]) for n, v in RUN_POSE.items()),
              "phase 1's tiles sit at their px/py from its origin — no gutter inside", str(a))
        ob = (b[0][0], b[0][1] - 14)
        check(all(b[n] == (ob[0] + v[2], ob[1] + v[3]) for n, v in RUN_POSE_B.items()),
              "phase 2 too, torso at +3", str(b))
        bottom_a = max(y for _x, y in a.values()) + 8
        bottom_b = max(y for _x, y in b.values()) + 8
        check(bottom_a == bottom_b, "both phases stand on the row's baseline", f"{bottom_a} {bottom_b}")
        right_a = max(x for x, _y in a.values()) + 8
        left_b = min(x for x, _y in b.values())
        check(left_b - right_a >= 8, "one empty cell (8 px) or more separates the two boxes",
              f"{right_a} -> {left_b}")
        frag = K.write_fragment(pack, builder, grids, kit, str(pack_dir), K.Names())
        check(any(f.get("figure") == cyc.figure for f in frag["files"]),
              "the fragment points at the Figures view")


def test_mep_lint_warns_when_cells_and_pixels_disagree():
    with tempfile.TemporaryDirectory() as td:
        pack_dir = make_pack(Path(td) / "pack")

        def warnings():
            rep = mep_lint.Report()
            mep_lint.lint_pose_offsets(mep_lint.Source(pack_dir), "textures/hires.txt", rep)
            return [m for lv, _w, m in rep.items if lv == "warning"]

        check(warnings() == [], "consistent px/dx draws no warning", str(warnings()))
        check(mep_lint._to_cells(4) == 1 and mep_lint._to_cells(3) == 0 and mep_lint._to_cells(-4) == -1,
              "the lint's ToCells is SpriteGrouping's (+4 up, +3 down, halves away from zero)")
        poses = pack_dir / "textures" / "sheets" / E.POSES_FILE
        doc = json.loads(poses.read_text(encoding="utf-8"))
        doc["poses"][0]["tiles"][1]["dx"] = 0   # px 4 must round to 1
        poses.write_text(json.dumps(doc), encoding="utf-8")
        got = warnings()
        check(len(got) == 1 and "ToCells" in got[0], "dx != ToCells(px) is a warning", str(got))
        doc = json.loads(poses.read_text(encoding="utf-8"))
        for p in doc["poses"]:
            for t in p["tiles"]:
                t.pop("px", None)
                t.pop("py", None)
        poses.write_text(json.dumps(doc), encoding="utf-8")
        check(warnings() == [], "a pre-ADR sidecar without px/py draws no warning", str(warnings()))


if __name__ == "__main__":
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            print(f"-- {name}")
            fn()
    if _FAILURES:
        print(f"\n{len(_FAILURES)} failure(s): {_FAILURES}")
        sys.exit(1)
    print("\nall ok")
