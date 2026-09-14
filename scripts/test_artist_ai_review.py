"""Headless suite for the AI kit review protocol (`artist_ai_review.py`).

What is tested here is the contract the protocol exists to hold, not its
plumbing: the reviewer is only ever shown a rendered surface and a rectangle
inside it, an answer that cannot be checked in seconds is rejected rather than
recorded, an abstention is never scored as a mistake, a confidently wrong answer
is counted on its own, and nothing reaches the generators' `--names` file that a
human did not tick first.

Run:  python3 scripts/test_artist_ai_review.py
"""

import json
import struct
import sys
import tempfile
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import artist_ai_review as R  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


# ---- fixtures --------------------------------------------------------------

def _png(path: Path, width, height):
    """A real, minimal PNG - the tool reads the IHDR to size a surface."""
    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    raw = b"".join(b"\x00" + b"\xff\xff\xff\xff" * width for _ in range(height))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw))
        + chunk(b"IEND", b""))


def _cell(index, col, row, tiles=(), unit=8, gutter=1):
    pitch = unit + gutter
    return {"index": index, "x": col * pitch + gutter, "y": row * pitch + gutter,
            "tiles": [dict(t) for t in tiles]}


def _sprite_kit(root: Path, scale=4):
    """A kit with one sheet: two figure boxes, two cells each, one gap column.

    Box width 2, so the columns are 0-1 and 3-4 - exactly the layout
    `artist_kit.py` exports and the only thing the segmenter is allowed to
    assume."""
    root.mkdir(parents=True, exist_ok=True)
    unit, gutter = 8, 1
    columns = 5
    _png(root / "sheets/usr000.png",
         (columns * (unit + gutter) + gutter) * scale, (1 * (unit + gutter) + gutter) * scale)
    _png(root / "sheets/usr000.orig.png", 1, 1)
    (root / "sheets/usr000.json").write_text(json.dumps({
        "version": 1, "kind": "sprite", "gridUnit": unit, "gutter": gutter,
        "sheet": "usr000.png", "reference": "usr000.orig.png",
        "poses": ["pose000", "pose001"],
        "cells": [
            _cell(0, 0, 0, [{"tile": "AA", "palette": "P0"}]),
            _cell(1, 1, 0, [{"tile": "BB", "palette": "P0"}]),
            _cell(2, 3, 0, [{"tile": "CC", "palette": "P1"}]),
            _cell(3, 4, 0, [{"tile": "DD", "palette": "P1"}]),
        ],
    }))
    (root / "kit-part-sprites.json").write_text(json.dumps({
        "part": "sprites", "generator": "scripts/artist_kit.py", "pack": "packs/demo",
        "files": [{"path": "sheets/usr000.png", "title": "cycle000", "unit": "grid",
                   "rows": 1, "columns": 2, "cells": 2,
                   "ids": ["pose000", "pose001"], "seen": True}],
        "dropped": [], "notes": [], "verify": {"ran": True, "errors": 0},
    }))
    return root


# ---- what the reviewer is shown --------------------------------------------

def test_every_ask_names_a_rendered_surface_and_a_rect_inside_it():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        check(len(packet["asks"]) == 2, "one ask per figure box",
              str(len(packet["asks"])))
        for ask in packet["asks"]:
            surface = Path(td) / ask["surface"]
            width, height = R._png_size(surface)
            x, y, w, h = ask["rect"]
            check(surface.suffix == ".png", "the reviewer is shown a PNG", ask["surface"])
            check(w > 0 and h > 0 and x + w <= width and y + h <= height,
                  "the rect lies inside the surface it names", str(ask["rect"]))
            check("tiles" not in json.dumps(ask) and "palette" not in json.dumps(ask),
                  "no tile data is handed to the reviewer", json.dumps(ask))


def test_the_rect_is_in_the_png_own_pixels_not_in_cells():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td), scale=4))
        first = packet["asks"][0]
        check(first["rect"] == [4, 4, 68, 32],
              "a 2x1 cell box at 4x reads back as its own pixels", str(first["rect"]))
        check(first["cellRect"] == [0, 0, 2, 1],
              "the cell rect is carried too, for the slicer", str(first.get("cellRect")))


def test_the_boxes_of_a_row_are_recovered_in_reading_order():
    check(R.split_boxes([0, 1, 3, 4]) == [0, 0, 1, 1],
          "one empty column between two boxes of width 2")
    check(R.split_boxes([0, 1, 2, 4, 5, 6]) == [0, 0, 0, 1, 1, 1],
          "boxes of width 3")
    check(R.split_boxes([]) is None, "an empty row segments into nothing")


def test_a_kit_that_cannot_be_segmented_is_reported_not_guessed_at():
    with tempfile.TemporaryDirectory() as td:
        root = _sprite_kit(Path(td))
        doc = json.loads((root / "sheets/usr000.json").read_text())
        doc["poses"] = ["pose000", "pose001", "pose002"]   # one more than the layout holds
        (root / "sheets/usr000.json").write_text(json.dumps(doc))
        try:
            R.build_packet(root)
            asks = [a for a in R.build_packet(root)["asks"]]
            check(not asks, "a kit whose layout disagrees with its poses yields no asks",
                  str(asks))
        except R.ReviewError:
            check(True, "a kit whose layout disagrees with its poses is refused")
        packet = R.build_packet(root)
        check(packet["skipped"], "and the surface it could not read is listed as skipped")


# ---- what an answer has to contain -----------------------------------------

def _answer(packet, index=0, **over):
    ask = packet["asks"][index]
    entry = {"ask": ask["ask"], "saw": ask["surface"], "rect": list(ask["rect"]),
             "subject": "player", "name": "the player, running",
             "confidence": "medium", "why": "blue trousers, bare chest"}
    entry.update(over)
    return entry


def _doc(packet, *entries, subjects=None):
    return {"version": R.SCHEMA_VERSION, "packet": packet["sha256"],
            "subjects": subjects if subjects is not None else {"player": "the soldier you control"},
            "proposals": list(entries)}


def test_a_clean_answer_is_accepted():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        report = R.check_proposals(packet, _doc(packet, _answer(packet)))
        check(report["accepted"] == [packet["asks"][0]["ask"]], "the answer is ingested",
              json.dumps(report["defects"]))


def test_an_answer_that_cannot_be_checked_is_a_defect():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        cases = {
            "an ask the packet never made": _answer(packet, ask="usr000#pose999"),
            "a file it was not shown": _answer(packet, saw="sheets/somewhere-else.png"),
            "a rect outside the box": _answer(packet, rect=[0, 0, 10000, 10000]),
            "a name with no subject": _answer(packet, subject=""),
            "a subject it never declared": _answer(packet, subject="ghost"),
            "a confidence off the scale": _answer(packet, confidence="certain"),
            "no reason at all": _answer(packet, why="   "),
            "a name that only echoes the id": _answer(packet, name="pose000"),
        }
        for why, entry in cases.items():
            report = R.check_proposals(packet, _doc(packet, entry))
            check(len(report["defects"]) == 1 and not report["accepted"],
                  f"rejected: {why}", json.dumps(report))


def test_the_same_ask_answered_twice_is_a_defect():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        report = R.check_proposals(packet, _doc(packet, _answer(packet), _answer(packet)))
        check(len(report["defects"]) == 1, "one answer per ask", json.dumps(report))


def test_an_answer_to_a_stale_packet_is_refused():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        doc = _doc(packet, _answer(packet))
        doc["packet"] = "0" * 64
        report = R.check_proposals(packet, doc)
        check(any("different packet" in d["why"] for d in report["defects"]),
              "a review of an older kit is not silently accepted", json.dumps(report))


def test_an_abstention_is_accepted_but_still_has_to_say_why():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        good = _answer(packet, abstain=True, why="two shapes overlap, I cannot split them")
        for key in ("subject", "name", "confidence"):
            good.pop(key)
        report = R.check_proposals(packet, _doc(packet, good))
        check(report["abstained"] and not report["defects"],
              "\"I do not know\" is a valid answer", json.dumps(report))
        bare = dict(good, why="")
        report = R.check_proposals(packet, _doc(packet, bare))
        check(report["defects"], "an abstention with no reason is still a defect")


def test_unanswered_asks_are_named():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        report = R.check_proposals(packet, _doc(packet, _answer(packet)))
        check(report["unanswered"] == [packet["asks"][1]["ask"]],
              "a skipped ask is reported, not treated as an abstention",
              json.dumps(report["unanswered"]))


# ---- ground truth ----------------------------------------------------------

def _hires(path: Path, rows):
    images, lines = [], []
    for image, tile, palette in rows:
        if image not in images:
            images.append(image)
    lines += [f"<img>{n}" for n in images]
    lines += [f"<tile>{images.index(i)},{t},{p},0,0,1,N" for i, t, p in rows]
    lines.append("#<tile>0,ZZ,P0,0,0,1,N")      # commented out: never evidence
    path.write_text("\n".join(lines) + "\n")


def test_truth_comes_from_the_reference_pack_own_names():
    with tempfile.TemporaryDirectory() as td:
        root = _sprite_kit(Path(td))
        hires = Path(td) / "hires.txt"
        _hires(hires, [("Hero.png", "AA", "P0"), ("Hero.png", "BB", "P0"),
                       ("Foe.png", "CC", "P1"), ("Foe.png", "DD", "P1")])
        truth = R.build_truth(root, hires, {"player": ["Hero.png"], "enemy": ["Foe.png"]})
        check(truth["labels"]["usr000#pose000"]["subject"] == "player",
              "a box whose tiles are only in the player's file is the player")
        check(truth["labels"]["usr000#pose001"]["subject"] == "enemy",
              "and the other box is the enemy")


def test_a_tile_matching_on_pattern_alone_is_not_evidence():
    with tempfile.TemporaryDirectory() as td:
        root = _sprite_kit(Path(td))
        hires = Path(td) / "hires.txt"
        # The same patterns, under palettes the kit never recorded: a mountain
        # and a shoulder can share a shape, so a palette-blind match is a guess.
        _hires(hires, [("Hero.png", "AA", "OTHER"), ("Hero.png", "BB", "OTHER")])
        truth = R.build_truth(root, hires, {"player": ["Hero.png"]})
        check(truth["labels"]["usr000#pose000"]["subject"] is None,
              "no exact (pattern, palette) match means no label, not a guessed one")


def test_a_box_holding_two_subjects_is_labelled_mixed():
    with tempfile.TemporaryDirectory() as td:
        root = _sprite_kit(Path(td))
        hires = Path(td) / "hires.txt"
        _hires(hires, [("Hero.png", "AA", "P0"), ("Foe.png", "BB", "P0"),
                       ("Foe.png", "CC", "P1"), ("Foe.png", "DD", "P1")])
        truth = R.build_truth(root, hires, {"player": ["Hero.png"], "enemy": ["Foe.png"]})
        check(truth["labels"]["usr000#pose000"]["subject"] == "mixed",
              "half the tiles from each file is two figures in one box, not one")


# ---- scoring ---------------------------------------------------------------

def _truth(**labels):
    return {"version": 1, "labels": {k: {"kind": "figure", "subject": v}
                                     for k, v in labels.items()}}


def test_the_four_counts_are_reported_separately():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        a, b = (ask["ask"] for ask in packet["asks"])
        doc = _doc(packet,
                   _answer(packet, 0, subject="player", confidence="high"),
                   _answer(packet, 1, subject="player", confidence="high"))
        result = R.score(doc, _truth(**{a: "player", b: "enemy"}))
        counts = result["counts"]
        check(counts == {"correct": 1, "wrong": 1, "abstained": 0,
                         "confidently_wrong": 1, "unscored": 0, "scorable_asks": 2},
              "correct, wrong, abstained and confidently wrong stand on their own",
              json.dumps(counts))


def test_an_abstention_is_never_counted_as_wrong():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        a, b = (ask["ask"] for ask in packet["asks"])
        entry = _answer(packet, 0, abstain=True, why="I cannot tell")
        result = R.score(_doc(packet, entry), _truth(**{a: "enemy", b: "enemy"}))
        check(result["counts"]["wrong"] == 0 and result["counts"]["abstained"] == 1,
              "not knowing costs nothing", json.dumps(result["counts"]))


def test_a_wrong_answer_is_only_confidently_wrong_when_it_was_confident():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        a = packet["asks"][0]["ask"]
        for confidence, expected in (("high", 1), ("medium", 0), ("low", 0)):
            result = R.score(_doc(packet, _answer(packet, 0, confidence=confidence)),
                             _truth(**{a: "enemy"}))
            check(result["counts"]["confidently_wrong"] == expected,
                  f"{confidence} confidence, wrong answer -> {expected} confidently wrong",
                  json.dumps(result["counts"]))


def test_a_mixed_box_is_right_only_when_the_reviewer_saw_two_figures():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        a = packet["asks"][0]["ask"]
        said_one = R.score(_doc(packet, _answer(packet, 0)), _truth(**{a: "mixed"}))
        said_two = R.score(_doc(packet, _answer(packet, 0, multiple=True)),
                           _truth(**{a: "mixed"}))
        check(said_one["counts"]["wrong"] == 1, "picking one of two figures is wrong")
        check(said_two["counts"]["correct"] == 1, "saying there are two is right")


def test_an_ask_with_no_ground_truth_is_unscored_not_wrong():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        a = packet["asks"][0]["ask"]
        result = R.score(_doc(packet, _answer(packet, 0)),
                         {"version": 1, "labels": {a: {"kind": "figure", "subject": None}}})
        check(result["counts"] == {"correct": 0, "wrong": 0, "abstained": 0,
                                   "confidently_wrong": 0, "unscored": 1, "scorable_asks": 0},
              "nothing to measure against is its own bucket", json.dumps(result["counts"]))


def test_the_reviewer_own_vocabulary_is_mapped_rather_than_required():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        a = packet["asks"][0]["ask"]
        doc = _doc(packet, _answer(packet, 0, subject="red-soldier"),
                   subjects={"red-soldier": "the red-uniformed rifleman"})
        result = R.score(doc, _truth(**{a: "enemy"}), alias={"red-soldier": "enemy"})
        check(result["counts"]["correct"] == 1,
              "a reviewer may invent its own subject keys", json.dumps(result["counts"]))


# ---- promotion -------------------------------------------------------------

def test_only_ticked_proposals_reach_the_names_file():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        a, b = (ask["ask"] for ask in packet["asks"])
        doc = _doc(packet, _answer(packet, 0, name="the player, running"),
                   _answer(packet, 1, name="the enemy, running"))
        names = R.promote(packet, doc, [a])
        check(list(names["poses"]) == ["pose000"],
              "an untouched proposal promotes to nothing", json.dumps(names["poses"]))
        check(names["poses"]["pose000"] == {"name": "the player, running",
                                            "subject": "player"},
              "the promoted caption is shaped like the generators' --names file",
              json.dumps(names["poses"]))
        check(names["subjects"] == {"player": "the soldier you control"},
              "and the subject line comes along", json.dumps(names["subjects"]))


def test_an_abstention_promotes_to_nothing_even_when_ticked():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        a = packet["asks"][0]["ask"]
        entry = _answer(packet, 0, abstain=True, why="cannot tell")
        names = R.promote(packet, _doc(packet, entry), [a])
        check(names["poses"] == {}, "there is nothing to promote in \"I do not know\"")


def test_promoting_an_ask_the_packet_never_made_is_refused():
    with tempfile.TemporaryDirectory() as td:
        packet = R.build_packet(_sprite_kit(Path(td)))
        try:
            R.promote(packet, _doc(packet, _answer(packet, 0)), ["usr000#pose999"])
            check(False, "an accepted id with no ask behind it is refused")
        except R.ReviewError:
            check(True, "an accepted id with no ask behind it is refused")


def main():
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            fn()
    print()
    if _FAILURES:
        print(f"{len(_FAILURES)} failure(s): {', '.join(_FAILURES)}")
        return 1
    print("all checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
