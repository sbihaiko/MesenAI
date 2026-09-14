"""Headless suite for the artist kit assembler (`artist_kit_assemble.py`).

The assembler is the one part of the kit an artist reads before anything else,
so what is tested here is exactly what they see: the parts come in reading
order, a surface carrying inferred art is marked as such, a dropped surface
still says why it was dropped, and a failed rebuild is never reported as
passed.

Run:  python3 scripts/test_artist_kit_assemble.py
"""

import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import artist_kit_assemble as A  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def _fragment(part, files=(), dropped=(), notes=(), verify=None, pack="packs/demo"):
    return {
        "part": part,
        "generator": f"scripts/artist_{part}.py",
        "pack": pack,
        "files": list(files),
        "dropped": list(dropped),
        "notes": list(notes),
        "verify": verify if verify is not None else {
            "ran": True, "errors": 0, "keys_before": 10, "keys_after": 10,
            "lost": 0, "added": 0},
    }


def _kit(td, *fragments):
    root = Path(td)
    for fragment in fragments:
        (root / f"kit-part-{fragment['part']}.json").write_text(json.dumps(fragment))
    return root


def test_parts_are_ordered_most_recognisable_first():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("chr"), _fragment("map"), _fragment("sprites"),
                    _fragment("background"))
        kit = A.build_kit(root)
        order = [p["part"] for p in kit["parts"]]
        check(order == ["sprites", "background", "map", "chr"],
              "the kit opens on the figures and ends on the pattern pages", str(order))


def test_an_unplanned_part_is_shown_rather_than_dropped():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("sprites"), _fragment("audio"))
        kit = A.build_kit(root)
        check([p["part"] for p in kit["parts"]] == ["sprites", "audio"],
              "a part the assembler does not know about still reaches the artist")


def test_totals_add_up_across_parts():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(
            td,
            _fragment("sprites", files=[{"path": "sheets/usr000.png", "title": "a run",
                                         "rows": 1, "columns": 3, "cells": 3, "seen": True}]),
            _fragment("chr", files=[{"path": "chr/Chr_0.png", "title": "page 0",
                                     "cells": 256, "seen": False}],
                      dropped=[{"path": "chr/Chr_9.png", "why": "nothing recoverable"}]),
        )
        kit = A.build_kit(root)
        t = kit["totals"]
        check(t["files"] == 2 and t["cells"] == 259, "files and cells are summed",
              json.dumps(t))
        check(t["inferred_files"] == 1, "a surface carrying inferred art is counted", json.dumps(t))
        check(t["dropped"] == 1, "a dropped surface is counted", json.dumps(t))


def test_inferred_art_is_marked_in_the_page_an_artist_reads():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("chr", files=[
            {"path": "chr/Chr_0.png", "title": "page 0", "cells": 256, "seen": False}]))
        text = A.render_markdown(A.build_kit(root))
        check("inferred - check it" in text, "a ROM fill is labelled as inferred in ARTIST.md")
        check("never paint it" in text, "the page tells the artist the .orig.png is the reference")
        check("mep_build.py build" in text and "cp -R" in text,
              "the page says to build a copy of the recording, not the recording itself")
        check("ADR-0147" in text, "the page cites the layer rule it follows")


def test_a_dropped_surface_keeps_its_reason():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("background", dropped=[
            {"path": "sheets/obj000.png", "why": "19 cells, all uniform black"}]))
        text = A.render_markdown(A.build_kit(root))
        check("19 cells, all uniform black" in text,
              "the artist is told what was left out and why")


def test_a_failed_or_missing_rebuild_is_never_reported_as_passed():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td,
                    _fragment("sprites", verify={"ran": True, "errors": 2, "keys_before": 10,
                                                 "keys_after": 9, "lost": 1, "added": 0}),
                    _fragment("map", verify={"ran": False}))
        text = A.render_markdown(A.build_kit(root))
        check("FAILED" in text, "a rebuild with errors reads as failed")
        check("not verified" in text, "a part that never ran the rebuild says so")
        check("passed" not in text.replace("not verified", ""),
              "nothing unverified is dressed up as passed")


def test_an_empty_or_broken_kit_fails_loudly():
    with tempfile.TemporaryDirectory() as td:
        try:
            A.build_kit(Path(td))
            check(False, "an empty kit folder raises")
        except A.KitError:
            check(True, "an empty kit folder raises")
        (Path(td) / "kit-part-sprites.json").write_text("{not json")
        try:
            A.build_kit(Path(td))
            check(False, "a malformed fragment raises")
        except A.KitError:
            check(True, "a malformed fragment raises")


def test_writing_the_kit_produces_both_files():
    with tempfile.TemporaryDirectory() as td:
        root = _kit(td, _fragment("sprites", files=[{"path": "sheets/usr000.png",
                                                     "title": "a run", "cells": 3}]))
        rc = A.main([str(root), "--title", "Demo"])
        check(rc == 0, "the tool exits 0 on a good kit")
        kit = json.loads((root / "kit.json").read_text())
        check(kit["title"] == "Demo", "the title reaches kit.json")
        check((root / "ARTIST.md").read_text().startswith("# Artist kit - Demo"),
              "ARTIST.md is written and titled")


def main():
    tests = [
        test_parts_are_ordered_most_recognisable_first,
        test_an_unplanned_part_is_shown_rather_than_dropped,
        test_totals_add_up_across_parts,
        test_inferred_art_is_marked_in_the_page_an_artist_reads,
        test_a_dropped_surface_keeps_its_reason,
        test_a_failed_or_missing_rebuild_is_never_reported_as_passed,
        test_an_empty_or_broken_kit_fails_loudly,
        test_writing_the_kit_produces_both_files,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
