"""Headless suite for the artist kit's sprite half (`artist_kit.py`, F9.18).

The promise this tool makes is a layout: a row is one animation, its columns
are that animation's phases in order, a variant sits beside the figure it
varies, a fusion is not laid out at all, and every figure in a grid shares one
baseline so a row can be painted straight across. Each of those is asserted
here on the same synthetic pack the rest of the compose suite uses — no ROM,
no emulator, no recorded library — together with the one thing that makes the
surface worth anything: what it writes is legal `mep_build` input.

Run:  python3 scripts/test_artist_kit.py
"""

import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import artist_kit as K  # noqa: E402
import compose_engine as E  # noqa: E402
import sheet_repaint  # noqa: E402 — to read the exported PNG back
from test_compose_engine import (make_pack, mep_build_load,  # noqa: E402
                                 _write_spr_group, _write_poses, _poses_doc)

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def _pose(pid, frames, tiles, **extra):
    doc = {"id": pid, "frames": frames,
           "tiles": [{"node": n, "dx": d[0], "dy": d[1]} for n, d in sorted(tiles.items())]}
    doc.update(extra)
    return doc


def _kit_pack(root: Path, doc):
    """A pack with the sprite vocabulary of `make_pack` and the given sidecar."""
    sheets = root / "textures" / "sheets"
    make_pack(root)
    _write_spr_group(sheets)
    _write_poses(sheets, doc)
    return E.Pack(root)


# The fixture's sprite nodes are 0..3 (node 4 has no sheet art, so it is the
# hole every case here must tolerate). Three silhouettes of one figure, one
# variant of the first and one fusion of two of them.
_PHASE_A = {0: (0, 0), 1: (0, 1)}
_PHASE_B = {1: (0, 0), 2: (0, 1), 3: (1, 1)}
_PHASE_C = {2: (0, 0), 3: (0, 1)}


def _doc_with_cycle():
    poses = [
        _pose("pose001", 300, _PHASE_B),
        _pose("pose002", 200, _PHASE_C),
        _pose("pose003", 150, _PHASE_A, variantOf="pose000"),
        _pose("pose004", 90, {0: (0, 0), 1: (0, 1), 2: (1, 1)}, fusionOf=["pose000", "pose002"]),
    ]
    doc = _poses_doc(tiles=_PHASE_A, extra_poses=poses)   # pose000 = _PHASE_A
    doc["cycles"] = [{"id": "cycle000", "period": 3, "repeats": 7,
                      "poses": ["pose002", "pose000", "pose001"], "hold": [4, 4, 4]}]
    return doc


def test_a_cycle_becomes_one_row_in_phase_order():
    with tempfile.TemporaryDirectory() as td:
        pack = _kit_pack(Path(td), _doc_with_cycle())
        grids = K.KitBuilder(pack).build()
        cycles = [g for g in grids if g.kind == "cycle"]
        check(len(cycles) == 1, "one grid per cycle", str([g.kind for g in grids]))
        grid = cycles[0]
        check(len(grid.rows) == 1, "the cycle is one row", str(len(grid.rows)))
        ids = [c.pose.id for c in grid.cells]
        # pose000 is phase 2 and drags its variant pose003 in behind it.
        check(ids[:2] == ["pose002", "pose000"] and ids[-1] == "pose001",
              "the row follows the cycle's phase order", str(ids))
        phases = [c.phase for c in grid.cells if c.phase is not None]
        check(phases == sorted(phases), "phases are numbered in order", str(phases))


def test_a_variant_sits_next_to_its_base():
    with tempfile.TemporaryDirectory() as td:
        pack = _kit_pack(Path(td), _doc_with_cycle())
        grid = [g for g in K.KitBuilder(pack).build() if g.kind == "cycle"][0]
        ids = [c.pose.id for c in grid.cells]
        check(ids.index("pose003") == ids.index("pose000") + 1,
              "the variant is the column immediately after its base", str(ids))
        cells = grid.cells
        base = cells[ids.index("pose000")]
        variant = cells[ids.index("pose003")]
        check(base.row == variant.row, "and it is in the same row as its base",
              f"{base.row} vs {variant.row}")


def test_a_fusion_is_never_laid_out():
    """ADR-0177: a fused entry is two figures that touched, and both halves are
    entries of this same file — laying it out hands the artist a bystander."""
    with tempfile.TemporaryDirectory() as td:
        pack = _kit_pack(Path(td), _doc_with_cycle())
        builder = K.KitBuilder(pack)
        grids = builder.build()
        ids = {c.pose.id for g in grids for c in g.cells}
        check("pose004" not in ids, "the fused pose is in no grid", str(sorted(ids)))
        check(builder.excluded_fusions == ["pose004"], "and it is reported as excluded",
              str(builder.excluded_fusions))
        dropped = {d["path"] for d in K._dropped(builder)}
        check("pose004" in dropped, "the fragment's dropped[] says why", str(dropped))


def test_every_figure_shares_the_rows_baseline():
    """The alignment the tool exists for: pad to one box, centre across, and
    put every figure's bottom row on the same line."""
    with tempfile.TemporaryDirectory() as td:
        pack = _kit_pack(Path(td), _doc_with_cycle())
        builder = K.KitBuilder(pack)
        grid = [g for g in builder.build() if g.kind == "cycle"][0]
        cells, boxes = builder.placements(grid)
        check(boxes == [(2, 2)], "the row's box is that row's largest figure", str(boxes))
        box = boxes[0]
        by_col = {}
        for node, cx, cy in cells:
            by_col.setdefault(cx // (box[0] + K.SLOT_GAP), []).append(cy)
        bottoms = {slot: max(ys) for slot, ys in by_col.items()}
        check(len(set(bottoms.values())) == 1,
              "every figure of the row ends on one baseline", str(bottoms))
        check(all(cy < box[1] + K.SLOT_GAP for _n, _cx, cy in cells),
              "a one-row grid stays inside one box height", str(cells))


def test_the_rest_grid_bins_by_box_so_a_row_is_uniform():
    """A 6x9 boss must not set the cell size for a 1x2 pickup: the rest grid
    bins by figure box, and each row is padded to its own box only."""
    with tempfile.TemporaryDirectory() as td:
        doc = _poses_doc(tiles={0: (0, 0)}, extra_poses=[          # pose000: 1x1
            _pose("pose001", 300, {1: (0, 0), 2: (0, 1), 3: (1, 1)}),   # 2x2
            _pose("pose002", 200, {2: (0, 0)}),                         # 1x1
        ])
        pack = _kit_pack(Path(td), doc)
        builder = K.KitBuilder(pack, columns=4)
        grid = builder.build()[0]
        _cells, boxes = builder.placements(grid)
        check(len(grid.rows) == 2, "each box gets its own row", str([len(r) for r in grid.rows]))
        check(sorted(boxes) == [(1, 1), (2, 2)],
              "and each row carries only its own box", str(boxes))
        # pose000 is the file's most-seen entry (412 frames), so its 1x1 bin
        # leads and keeps the file's order inside itself.
        first = [c.pose.id for c in grid.rows[0]]
        check(first == ["pose000", "pose002"],
              "the bin holding the most-seen figure comes first, in file order", str(first))


def test_the_exported_sheet_is_a_legal_composed_sheet():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td) / "pack"
        pack = _kit_pack(root, _doc_with_cycle())
        out = Path(td) / "kit" / "sheets"
        out.mkdir(parents=True)
        builder = K.KitBuilder(pack)
        grids = builder.build()
        names = [K.export_grid(pack, builder, g, out) for g in grids]
        check(all(n is not None for n in names), "every grid exports a sheet", str(names))
        docs, _ = mep_build_load(out)
        check(len(docs) == len(grids) and {d.kind for d in docs} == {"sprite"},
              "mep_build loads them all as sprite sheets", str([d.kind for d in docs]))
        doc = json.loads((out / f"{names[0]}.json").read_text(encoding="utf-8"))
        check(doc["composed"] is True and doc["gutter"] == E.GUTTER and doc["gridUnit"] == 8,
              "the sidecar keeps the composed sheet's slicing convention", str(doc["gutter"]))
        check(doc["poses"] == [c.pose.id for c in grids[0].cells],
              "ADR-0174: the sidecar names the poses its cells belong to", str(doc.get("poses")))
        img = sheet_repaint.read_png(out / f"{names[0]}.png")
        twin = sheet_repaint.read_png(out / f"{names[0]}.orig.png")
        check(img.width == twin.width and img.height == twin.height,
              "the canvas and its untouched reference twin agree in size",
              f"{img.width}x{img.height} vs {twin.width}x{twin.height}")
        cell = doc["cells"][0]
        check(0 <= cell["x"] and cell["x"] + doc["gridUnit"] <= twin.width,
              "the first cell falls inside the sheet", str(cell))


def test_a_multi_row_grid_keeps_the_old_single_row_sidecar_shape():
    """The rest grid is the only multi-row case; it must still be the sidecar
    the editor writes, with nothing new but ADR-0174's `poses[]`."""
    with tempfile.TemporaryDirectory() as td:
        root = Path(td) / "pack"
        doc = _poses_doc(tiles=_PHASE_A, extra_poses=[
            _pose("pose001", 300, _PHASE_B), _pose("pose002", 200, _PHASE_C)])
        pack = _kit_pack(root, doc)
        out = Path(td) / "kit"
        out.mkdir()
        builder = K.KitBuilder(pack, columns=2)
        grids = builder.build()
        check(len(grids) == 1 and grids[0].kind == "rest",
              "no run means one rest grid", str([g.kind for g in grids]))
        K.export_grid(pack, builder, grids[0], out)
        sidecar = json.loads((out / f"{grids[0].name}.json").read_text(encoding="utf-8"))
        check(len(grids[0].rows) == 2, "the rest grid wraps at --columns",
              str([len(r) for r in grids[0].rows]))
        check(sidecar["columns"] > 0 and len(sidecar["cells"]) > 0,
              "a multi-row grid is expressed in the same cells[] the editor writes")
        expected = set(K._file_record(grids[0], K.Names()).keys())
        check(expected == {"path", "title", "unit", "rows", "columns", "cells", "ids", "seen"},
              "the fragment's file record is the contract's shape", str(sorted(expected)))


def test_a_hud_only_pose_is_kept_out_of_the_figure_grids():
    """ADR-0173: the fixture's node 5 is screen-pinned. A pose made only of
    pinned nodes is the life bar, not a figure — and a pack that never
    classified HUD must not have one guessed for it."""
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        doc = _poses_doc(tiles=_PHASE_A, extra_poses=[
            _pose("pose001", 800, {3: (0, 0)}),             # the HUD bar alone
            _pose("pose002", 200, {0: (0, 0), 3: (1, 0)}),  # a figure overlapping it
        ])
        pack = _kit_pack(root, doc)
        # Pin node 3 as well: the fixture's own pinned node (5) has no sheet
        # art, and a pose with no pixels is excluded for that reason first.
        adj_path = root / "textures" / "sheets" / "adjacency.json"
        adj = json.loads(adj_path.read_text(encoding="utf-8"))
        for node in adj["sprites"]["nodes"]:
            if node["cell"] == 3:
                node["screenFixed"] = True
        adj_path.write_text(json.dumps(adj), encoding="utf-8")
        pack = E.Pack(root)
        builder = K.KitBuilder(pack)
        check(builder.hud_classified, "the fixture's adjacency classifies HUD")
        laid = {c.pose.id for g in builder.build() for c in g.cells}
        check("pose001" not in laid, "a pose made only of pinned nodes is left out", str(laid))
        check("pose002" in laid, "a figure that merely touches the HUD is kept", str(laid))
        check(builder.excluded_hud == ["pose001"], "and the exclusion is reported",
              str(builder.excluded_hud))
        whys = {d["path"]: d["why"] for d in K._dropped(builder)}
        check("ADR-0173" in whys.get("pose001", ""), "dropped[] cites the ADR", str(whys))


def test_a_pack_that_never_classified_hud_says_so_instead_of_guessing():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        pack = _kit_pack(root, _poses_doc(tiles=_PHASE_A, extra_poses=[
            _pose("pose001", 800, {5: (0, 0)})]))
        adj_path = root / "textures" / "sheets" / "adjacency.json"
        adj = json.loads(adj_path.read_text(encoding="utf-8"))
        for node in adj["sprites"]["nodes"]:
            node.pop("screenFixed", None)
        adj_path.write_text(json.dumps(adj), encoding="utf-8")
        builder = K.KitBuilder(E.Pack(root))
        check(not builder.hud_classified, "a pre-ADR-0173 pack reads as unclassified")
        check(builder.excluded_hud == [], "so nothing is excluded as HUD",
              str(builder.excluded_hud))
        notes = " ".join(K._notes(builder.pack, builder, builder.build(), K.Names(), "p"))
        check("screenFixed" in notes and "does not guess" in notes,
              "and the kit says the limitation out loud", notes[-200:])


def test_a_name_is_claimed_by_creating_it_so_two_writers_cannot_collide():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td) / "pack"
        pack = _kit_pack(root, _poses_doc())
        out = Path(td) / "sheets"
        first = K.claim_name(pack, out)
        check((out / f"{first}.json").is_file(), "claiming creates the sidecar", first)
        second = K.claim_name(pack, out)
        check(second != first, "a second claim cannot be handed the same name",
              f"{first} vs {second}")
        check(pack.next_free_name(out) not in (first, second),
              "and the free-name scan agrees both are taken")


def test_names_caption_a_file_and_absence_falls_back_to_the_id():
    with tempfile.TemporaryDirectory() as td:
        pack = _kit_pack(Path(td), _doc_with_cycle())
        grids = K.KitBuilder(pack).build()
        grid = [g for g in grids if g.kind == "cycle"][0]
        bare = K.grid_title(grid, K.Names())
        check("cycle000" in bare and "7 time(s)" in bare,
              "an unnamed grid falls back to its id and what was measured", bare)
        named = K.Names({"cycles": {"cycle000": "the player's run"}})
        check(K.grid_title(grid, named) == "the player's run",
              "a name from --names becomes the title", K.grid_title(grid, named))


def test_a_sheet_is_captioned_by_the_subject_it_holds():
    """The caption says what is on the sheet, and only ever from the names
    file: one subject named, several named in cell order, none left as the id."""
    with tempfile.TemporaryDirectory() as td:
        pack = _kit_pack(Path(td), _doc_with_cycle())
        grid = [g for g in K.KitBuilder(pack).build() if g.kind == "cycle"][0]
        ids = [c.pose.id for c in grid.cells]

        bare = K.grid_title(grid, K.Names())
        check(bare.startswith("cycle000 — a 3-phase loop"),
              "with nothing named the caption stays the id plus the counts", bare)

        one = K.Names({"subjects": {"green-soldier": "the green-uniformed enemy"},
                       "poses": {pid: {"subject": "green-soldier"} for pid in ids}})
        check(K.grid_title(grid, one) == "green soldier — a 3-phase loop, seen 7 time(s)",
              "one subject captions the sheet", K.grid_title(grid, one))

        # pose002 is alone under "player"; the rest are "green".
        mixed = K.Names({"subjects": {"green-soldier": "the enemy", "player": "the hero"},
                         "poses": dict({pid: {"subject": "green-soldier"} for pid in ids},
                                       **{ids[0]: {"subject": "player"}})})
        check(K.grid_title(grid, mixed).startswith("green soldier, player —"),
              "several subjects are named most-cells-first", K.grid_title(grid, mixed))
        check("the enemy" in " ".join(K._notes(pack, K.KitBuilder(pack), [grid], mixed, "p")),
              "the subjects' descriptions reach the artist once, in the notes")

        # A cycle with a name of its own still wins over the subjects.
        both = K.Names({"cycles": {"cycle000": "the player's run"},
                        "subjects": {"green-soldier": "the green-uniformed enemy"},
                        "poses": {pid: {"subject": "green-soldier"} for pid in ids}})
        check(K.grid_title(grid, both) == "the player's run",
              "a named run outranks its subjects", K.grid_title(grid, both))

        # A subject named for no pose on this sheet never reaches the caption.
        elsewhere = K.Names({"subjects": {"somebody-else": "not on this sheet"},
                             "poses": {"pose999": {"subject": "somebody-else"}}})
        check(K.grid_title(grid, elsewhere) == bare,
              "a subject belonging to no pose here changes nothing",
              K.grid_title(grid, elsewhere))


def test_a_pack_without_a_pose_sidecar_is_refused_with_the_reason():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        make_pack(root)
        _write_spr_group(root / "textures" / "sheets")
        pack = E.Pack(root)
        try:
            K.KitBuilder(pack)
            check(False, "a pack with no poses.json is refused")
        except K.KitError as e:
            check("poses.json" in str(e) and "ADR-0170" in str(e),
                  "a pack with no poses.json is refused, with the fix in the message", str(e))


def main():
    tests = [
        test_a_cycle_becomes_one_row_in_phase_order,
        test_a_variant_sits_next_to_its_base,
        test_a_fusion_is_never_laid_out,
        test_every_figure_shares_the_rows_baseline,
        test_the_rest_grid_bins_by_box_so_a_row_is_uniform,
        test_the_exported_sheet_is_a_legal_composed_sheet,
        test_a_multi_row_grid_keeps_the_old_single_row_sidecar_shape,
        test_a_hud_only_pose_is_kept_out_of_the_figure_grids,
        test_a_pack_that_never_classified_hud_says_so_instead_of_guessing,
        test_a_name_is_claimed_by_creating_it_so_two_writers_cannot_collide,
        test_names_caption_a_file_and_absence_falls_back_to_the_id,
        test_a_sheet_is_captioned_by_the_subject_it_holds,
        test_a_pack_without_a_pose_sidecar_is_refused_with_the_reason,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
