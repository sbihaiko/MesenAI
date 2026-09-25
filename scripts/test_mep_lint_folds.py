"""Headless suite for mep_lint's ADR-0230 check (F14.9): a sheet sidecar tile
entry's `folds` and a variant cell's `variantOf`.

A fold is another palette the recording drew a shape in that the cell
reproduces exactly at one Brightness; mep_build.py emits one defaultTile=N rule
per fold, and skips an item it cannot read. So a malformed item is an error -
it would otherwise vanish and leave its key drawing the ROM - while a sidecar
without the field (every pack before F14.9) stays clean.

Run:  python3 scripts/test_mep_lint_folds.py
"""

import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mep_lint  # noqa: E402

_FAILURES = []
TILE = "00" * 16
PAL = "0F162A30"


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def lint(cells):
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "sheets").mkdir()
        (root / "hires.txt").write_text("<ver>107\n", encoding="utf-8")
        (root / "sheets" / "misc.json").write_text(json.dumps({"kind": "misc", "cells": cells}), encoding="utf-8")
        rep = mep_lint.Report()
        mep_lint.lint_sheet_folds(mep_lint.Source(root), "hires.txt", rep)
        return rep.items


def cell(index, folds=None, **extra):
    entry = {"tile": TILE, "palette": PAL}
    if folds is not None:
        entry["folds"] = folds
    return dict({"index": index, "x": 1, "y": 1, "tiles": [entry]}, **extra)


def test_a_sidecar_without_folds_is_clean():
    check(lint([cell(0), cell(1, variantOf=0)]) == [], "an old sidecar, and a variant naming a real cell, are clean")


def test_well_formed_folds_are_clean():
    items = lint([cell(0, [{"palette": "0F0F0F0F", "brightness": 0}, {"palette": "0f162a0f", "brightness": 1},
                           {"palette": "0F062A30", "brightness": 1.2}])])
    check(items == [], "exact folds with a Brightness in [0, 4] are clean", str(items))


def test_each_malformed_fold_is_an_error():
    cases = {
        "not a list": ({"palette": "0F0F0F0F", "brightness": 0}, "not a list"),
        "short palette": ([{"palette": "0F0F0F", "brightness": 0}], "8-hex-digit"),
        "no brightness": ([{"palette": "0F0F0F0F"}], "brightness must be a number"),
        "boolean brightness": ([{"palette": "0F0F0F0F", "brightness": True}], "brightness must be a number"),
        "brightness above 4": ([{"palette": "0F0F0F0F", "brightness": 4.5}], "brightness must be a number"),
        "negative brightness": ([{"palette": "0F0F0F0F", "brightness": -0.1}], "brightness must be a number"),
        "own palette": ([{"palette": PAL.lower(), "brightness": 1}], "own palette"),
        "listed twice": ([{"palette": "0F0F0F0F", "brightness": 0}, {"palette": "0f0f0f0f", "brightness": 0}], "twice"),
        "not an object": (["0F0F0F0F"], "8-hex-digit"),
    }
    for name, (folds, needle) in cases.items():
        items = lint([cell(3, folds)])
        good = len(items) == 1 and items[0][0] == "error" and needle in items[0][2] and "cell index 3" in items[0][2]
        check(good, f"a fold that is {name} is an error naming the cell", str(items))


def test_a_dangling_variant_of_is_a_warning():
    items = lint([cell(0), cell(1, variantOf=7)])
    check(len(items) == 1 and items[0][0] == "warning" and "variantOf 7" in items[0][2],
          "a variantOf naming no cell of the sheet is a warning", str(items))


def test_an_unhashable_index_or_variant_of_is_reported_not_raised():
    """#461 review: a malformed sidecar whose `index` or `variantOf` is not a
    scalar (e.g. `[]`) must reach the Report, not crash lint with TypeError."""
    for name, cells, needle in (
            ("index []", [dict(cell(0), index=[]), cell(1)], "index []"),
            ("index {}", [dict(cell(0), index={}), cell(1)], "index {}"),
            ("variantOf []", [cell(0), cell(1, variantOf=[])], "variantOf []"),
            ("variantOf {}", [cell(0), cell(1, variantOf={})], "variantOf {}")):
        try:
            items = lint(cells)
        except TypeError as exc:
            check(False, f"a sidecar with {name} is reported, not raised", f"TypeError: {exc}")
            continue
        check(len(items) == 1 and items[0][0] == "warning" and needle in items[0][2],
              f"a sidecar with {name} is reported as one warning", str(items))


def main():
    tests = [test_a_sidecar_without_folds_is_clean, test_well_formed_folds_are_clean,
             test_each_malformed_fold_is_an_error, test_a_dangling_variant_of_is_a_warning,
             test_an_unhashable_index_or_variant_of_is_reported_not_raised]
    for t in tests:
        t()
    print(f"\n{len(tests)} tests, {len(_FAILURES)} failure(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
