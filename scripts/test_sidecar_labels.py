#!/usr/bin/env python3
"""Headless suite for the reader side of ADR-0209 Q1 (b): the Core-inferred
sidecar `label` and how every reader captions from it.

The Core side (the scheme itself, its determinism, the empty label of an
entry with no grouping data, the provenance beside every label) is pinned in
`scripts/core_unit_tests.cpp`. This file asserts what the Python readers owe
ADR-0183 §5:

  * `compose_engine.caption` precedence is names.json > sidecar `label` > id,
    and it reports which one won;
  * `poses.json` poses and runs, and a sprNNN/objNNN sheet, carry their label
    and `labelSource` into `Pose`, `PoseRun` and `Sheet`; a pack recorded
    before the field existed reads exactly as it did;
  * `artist_kit.grid_title` uses the label in place of the counts it used to
    spell out, keeps the id beside it, and still yields to a human name;
  * `mep_figure.py export` writes `label`/`labelSource` for a group, a pose and
    a human-named figure, and an artist's rename in the sidecar is reported as
    theirs;
  * a labelled pack round-trips through `mep_build.py build` with the same
    `(tileData, palette)` key set (ADR-0183 §4) and the labels untouched.

Run:  python3 scripts/test_sidecar_labels.py
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
import test_compose_engine as T  # noqa: E402 — the synthetic pack fixtures
import test_mep_figure as TF  # noqa: E402 — the figure pack (sprites + spr000 + obj001)

_FAILURES = []
GROUP_LABEL = "sprite group 4x2, 5 cells, 1 pose, x100"
POSE_LABEL = "figure 2x3, 5 tiles, 412 frames"
CYCLE_LABEL = "loop of 2 phases, 2x3, x7"


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def _relabel(path: Path, top=None, per_entry=None):
    """Add label fields to an existing sidecar the way the Core writes them."""
    doc = json.loads(path.read_text(encoding="utf-8"))
    if top:
        doc["label"] = top
        doc["labelSource"] = E.LABEL_SOURCE_INFERRED
    for key, labels in (per_entry or {}).items():
        for entry in doc.get(key, []):
            if entry.get("id") in labels:
                entry["label"] = labels[entry["id"]]
                entry["labelSource"] = E.LABEL_SOURCE_INFERRED
    path.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")


def _labelled_pack(root: Path) -> Path:
    pack = TF.make_pack(root, with_poses=True)
    sheets = pack / "textures" / "sheets"
    # A second pose so a cycle can be written: the same tiles shifted.
    doc = json.loads((sheets / E.POSES_FILE).read_text(encoding="utf-8"))
    doc["poses"].append({"id": "pose001", "frames": 7, "size": [2, 3],
                         "tiles": doc["poses"][0]["tiles"]})
    doc["cycles"] = [{"id": "cycle000", "period": 2, "repeats": 7,
                      "poses": ["pose000", "pose001"], "hold": [3, 0]}]
    (sheets / E.POSES_FILE).write_text(json.dumps(doc), encoding="utf-8")
    _relabel(sheets / "spr000.json", top=GROUP_LABEL)
    _relabel(sheets / E.POSES_FILE, per_entry={"poses": {"pose000": POSE_LABEL},
                                               "cycles": {"cycle000": CYCLE_LABEL}})
    return pack


def test_caption_precedence_is_names_then_label_then_id():
    check(E.caption("pose003", "figure 2x2, 4 tiles, 9 frames", "inferred", "the runner")
          == ("the runner", "names"),
          "a human name wins over the recorder's label and says so")
    check(E.caption("pose003", "figure 2x2, 4 tiles, 9 frames", "inferred", "  ")
          == ("figure 2x2, 4 tiles, 9 frames", "inferred"),
          "a blank human name is no name: the label wins and carries its own source")
    check(E.caption("pose003", "renamed by hand", "human") == ("renamed by hand", "human"),
          "an artist's rename in the sidecar is reported under the source they set")
    check(E.caption("pose003", "", None) == ("pose003", "id"),
          "with nothing else the id is the caption, and the source says so")
    check(E.caption("pose003", "x", None) == ("x", "sidecar"),
          "a label with no source is still a label, sourced to the sidecar")
    check(E.read_label({"label": "  ", "labelSource": "inferred"}) == ("", None),
          "a whitespace label reads as no label")
    check(E.read_label({"label": "scene #4 x12", "labelSource": "inferred"}) == ("scene #4 x12", "inferred"),
          "a label reads with its source")
    check(E.read_label("not a dict") == ("", None), "a non-entry reads as no label")


def test_readers_carry_labels_and_old_packs_read_as_before():
    with tempfile.TemporaryDirectory() as td:
        pack = E.Pack(_labelled_pack(Path(td)))
        pose = pack.poses.by_id("pose000")
        check(pose.label == POSE_LABEL and pose.label_source == "inferred",
              "a pose carries its label and source", f"{pose.label!r} {pose.label_source!r}")
        other = pack.poses.by_id("pose001")
        check(other.label == "" and other.label_source is None,
              "a pose without a label reads as empty, not as a guess")
        cycle = pack.poses.cycles[0]
        check(cycle.label == CYCLE_LABEL and cycle.label_source == "inferred",
              "a cycle carries its label and source", f"{cycle.label!r}")
        group = next(s for s in pack.sheets if s.json_path.stem == "spr000")
        check(group.label == GROUP_LABEL and group.label_source == "inferred",
              "a group sheet carries its top-level label and source", f"{group.label!r}")
        vocab = next(s for s in pack.sheets if s.json_path.stem == "sprites")
        check(vocab.label == "" and vocab.label_source is None,
              "a vocabulary sheet has no label")
    with tempfile.TemporaryDirectory() as td:
        pack = E.Pack(TF.make_pack(Path(td), with_poses=True))
        pose = pack.poses.by_id("pose000")
        group = next(s for s in pack.sheets if s.json_path.stem == "spr000")
        check(pose.label == "" and group.label == "" and pose.label_source is None,
              "a pack recorded before the field existed reads with empty labels everywhere")


def _grid_with_run(run_label, run_source, pose_label=""):
    pose = E.Pose("pose000", 412, (2, 3), {0: (0, 0)}, label=pose_label,
                  label_source="inferred" if pose_label else None)
    run = E.PoseRun("cycle000", ["pose000", "pose001"], [3, 0], 7, period=2,
                    label=run_label, label_source=run_source)
    grid = K.Grid("cycle", run.id, run=run)
    grid.rows.append([K.Cell(pose, 0, 0, run=run, phase=1, phases=2)])
    return grid, pose


def test_kit_captions_prefer_names_then_label_then_the_measured_tail():
    grid, _ = _grid_with_run(CYCLE_LABEL, "inferred")
    check(K.grid_title(grid, K.Names()) == f"cycle000 — {CYCLE_LABEL}",
          "with a label and no names the caption is the id beside the recorder's label",
          K.grid_title(grid, K.Names()))
    named = K.Names({"cycles": {"cycle000": "the player's run"}})
    check(K.grid_title(grid, named) == "the player's run",
          "a names.json entry still wins over the label", K.grid_title(grid, named))
    subject = K.Names({"poses": {"pose000": {"subject": "green-soldier"}}})
    check(K.grid_title(grid, subject) == "green soldier — a 2-phase loop, seen 7 time(s)",
          "a human subject wins over the label and keeps the measured tail",
          K.grid_title(grid, subject))
    bare, _ = _grid_with_run("", None)
    check(K.grid_title(bare, K.Names()) == "cycle000 — a 2-phase loop, seen 7 time(s)",
          "with no label at all the caption is exactly what it was before", K.grid_title(bare, K.Names()))
    single_pose = E.Pose("pose000", 412, (2, 3), {0: (0, 0)}, label=POSE_LABEL, label_source="inferred")
    rest = K.Grid("rest", "rest")
    rest.rows.append([K.Cell(single_pose, 0, 0)])
    check(K.grid_title(rest, K.Names()) == f"pose000 — {POSE_LABEL}",
          "a lone pose is captioned by its id beside its label", K.grid_title(rest, K.Names()))
    unlabelled = K.Grid("rest", "rest")
    unlabelled.rows.append([K.Cell(E.Pose("pose000", 412, (2, 3), {0: (0, 0)}), 0, 0)])
    check(K.grid_title(unlabelled, K.Names()) == "pose000 — seen in 412 frame(s)",
          "a lone unlabelled pose reads exactly as before")


def test_figure_export_writes_the_caption_and_its_source():
    with tempfile.TemporaryDirectory() as td:
        root = _labelled_pack(Path(td) / "pack")
        pack = E.Pack(root)
        out = Path(td) / "figures"
        doc = F.export_figure(pack, "spr000", out)
        check(doc["label"] == GROUP_LABEL and doc["labelSource"] == "inferred",
              "a group export carries the sheet's inferred label", f"{doc['label']!r} {doc['labelSource']!r}")
        on_disk = json.loads((out / "spr000-figure.json").read_text(encoding="utf-8"))
        check(on_disk.get("label") == GROUP_LABEL and on_disk.get("labelSource") == "inferred",
              "and writes both fields into the figure sidecar")
        doc = F.export_figure(pack, "pose000", out)
        check(doc["label"] == POSE_LABEL and doc["labelSource"] == "inferred",
              "a pose export carries the pose's inferred label", f"{doc['label']!r}")
        names = {"figures": {"spr000": "the soldier"}, "poses": {"pose000": {"name": "standing"}}}
        doc = F.export_figure(pack, "spr000", out, names)
        check((doc["label"], doc["labelSource"]) == ("the soldier", "names"),
              "a human name for the group wins over the inferred label", f"{doc['label']!r}")
        doc = F.export_figure(pack, "pose000", out, names)
        check((doc["label"], doc["labelSource"]) == ("standing", "names"),
              "a human name for the pose wins over the inferred label", f"{doc['label']!r}")
        doc = F.export_figure(pack, "obj001", out)
        check((doc["label"], doc["labelSource"]) == ("obj001", "id"),
              "a group with no label is captioned by its id, and says so", f"{doc['label']!r}")
        # The artist's rename: edit the sidecar's label, change its source.
        sidecar = root / "textures" / "sheets" / "spr000.json"
        d = json.loads(sidecar.read_text(encoding="utf-8"))
        d["label"], d["labelSource"] = "bill, running", "human"
        sidecar.write_text(json.dumps(d, indent=2) + "\n", encoding="utf-8")
        doc = F.export_figure(E.Pack(root), "spr000", out)
        check((doc["label"], doc["labelSource"]) == ("bill, running", "human"),
              "an artist's rename in the sidecar is used and attributed to them", f"{doc['label']!r}")
        check(F.load_names(Path(td) / "missing.json") == {}, "a missing names file is an empty one")


def test_a_labelled_pack_round_trips_through_build_unchanged():
    with tempfile.TemporaryDirectory() as td:
        root = _labelled_pack(Path(td) / "pack")
        sheets = root / "textures" / "sheets"
        before_labels = {p.name: p.read_bytes() for p in sheets.glob("*.json")}
        keys_before = F.hires_keys(root / "textures" / "hires.txt")
        rc = mep_build.main(["build", str(root), "--quiet"])
        keys_after = F.hires_keys(root / "textures" / "hires.txt")
        check(rc == 0, "mep_build.py build exits 0 on a labelled pack", str(rc))
        check(keys_before == keys_after, "with the same (tileData, palette) key set (ADR-0183 §4)",
              f"{len(keys_before)} -> {len(keys_after)}")
        after_labels = {p.name: p.read_bytes() for p in sheets.glob("*.json")}
        check(before_labels == after_labels, "and the sidecars, labels included, are byte-identical")


def main() -> int:
    test_caption_precedence_is_names_then_label_then_id()
    test_readers_carry_labels_and_old_packs_read_as_before()
    test_kit_captions_prefer_names_then_label_then_the_measured_tail()
    test_figure_export_writes_the_caption_and_its_source()
    test_a_labelled_pack_round_trips_through_build_unchanged()
    if _FAILURES:
        print(f"\n{len(_FAILURES)} check(s) failed")
        return 1
    print("\nall ok")
    return 0


if __name__ == "__main__":
    sys.exit(main())
