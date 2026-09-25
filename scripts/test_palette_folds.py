"""Headless suite for `palette_folds.py` — the fold/colourway predicate the
pattern pages and the recorder's sheets share (ADR-0230, F14.9).

Two things are pinned here:

1. `palette_relation` against the shared vectors in
   `docs/specs/golden/sheets/palette-relation-cases.txt`. `scripts/core_unit_tests`
   checks the recorder's C++ port (`MesenSheets::ClassifyPaletteRelation`)
   against the same file, so a change to either side that the other does not
   follow fails one of the two suites.
2. ADR-0230 Decision item 3: `compute_folds` measures a step against the
   palette the sheet cell carries when there is one, in either direction,
   instead of against the brightest step.

Run:  python3 scripts/test_palette_folds.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import palette_folds as P  # noqa: E402

_FAILURES = []
VECTORS = Path(__file__).resolve().parent.parent / "docs/specs/golden/sheets/palette-relation-cases.txt"
# The shape Zelda's fade rides on: it paints colours 2 and 3 only.
RAMP_TILE = "7F80808080808080" + "FFFFFFFFFFFFFFFF"


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def _vectors():
    rows = []
    for line in VECTORS.read_text().splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        f = line.split("\t")
        rows.append((f[0], f[1], f[2], f[3], float(f[4]), float(f[5]), f[6] if len(f) > 6 else ""))
    return rows


def test_the_shared_vectors_hold_for_the_python_predicate():
    rows = _vectors()
    check(len(rows) >= 40, "the shared vector file carries the cases", str(len(rows)))
    rels = {r[3] for r in rows}
    check(rels == {"inert", "fade", "brighter", "colourway"},
          "and every relation appears in it at least once", str(sorted(rels)))
    bad = []
    for tile, cell, other, rel, bright, drift, note in rows:
        got, b, d = P.palette_relation(tile, cell, other)
        if got != rel or abs(b - bright) > 5e-5 or abs(d - drift) > 5e-3:
            bad.append(f"{tile}/{cell}->{other} ({note}): {got} {b:.4f} {d:.2f}")
    check(not bad, "palette_relation reproduces every shared vector", "; ".join(bad[:5]))


def test_the_relation_is_the_kits_own_fold_test():
    # A colourway is exactly what compute_folds refuses to fold: a hue change,
    # or a residual over the gate. The vectors are real Zelda and Castlevania
    # pairs, so this ties the sheets' split to the pages' one.
    for tile, cell, other, rel, _b, _d, _n in _vectors():
        used = P.painted_indices(tile) or [0]
        a, o = P.palette_entries(cell), P.palette_entries(other)
        _br, drift = P.fold_measure(a, o, used)
        fold = (all(P._nes_rgb(a[k]) == P._nes_rgb(o[k]) for k in used) or
                ((P.fade_related(a, o, used) or P.fade_related(o, a, used)) and drift <= P.HUE_DRIFT_GATE_DEG))
        if fold == (rel == "colourway"):
            check(False, "a colourway is precisely a pair the kit would not fold", f"{cell}->{other}")
            return
    check(True, "a colourway is precisely a pair the kit would not fold")


class _Row:
    def __init__(self, data, pal):
        self.tile_data, self.palette = data, pal


class _Page:
    def __init__(self, name, rows):
        self.name, self.rows = name, dict(enumerate(rows))

    def index_of_slot(self, slot):
        return slot


def _zelda_fade_pages():
    # Zelda's 4-step screen fade on the orange ramp (F14.4): the sheet cell
    # carries the second step, 3617270F.
    steps = ["3617370F", "3617270F", "3617170F", "3617070F"]
    return [_Page(f"p{i}", [_Row(RAMP_TILE, s)]) for i, s in enumerate(steps)], steps


def test_without_a_sheet_cell_the_brightest_step_is_the_base():
    # The F14.4 behaviour: the brightest step is the base, the third step
    # drifts over the gate against it and becomes a second base, and the
    # darkest folds onto that one - two cells to paint for one fade.
    pages, steps = _zelda_fade_pages()
    folded, bases, stats = P.compute_folds(pages)
    check(("p1", 0) in folded and folded[("p1", 0)]["basePalette"] == steps[0],
          "with no anchor the second step folds onto the brightest", str(folded.get(("p1", 0))))
    check(("p2", 0) not in folded and stats["refused"] >= 1,
          "and the third step drifts over the gate against it (the F14.4 refusal)",
          f"folded={sorted(folded)} refused={stats['refused']}")
    check(len(bases) == 2, "so one fade costs two cells without an anchor", str(list(bases)))


def test_a_sheet_cell_anchors_the_fold_and_a_brighter_step_folds_above_one():
    pages, steps = _zelda_fade_pages()
    folded, bases, _stats = P.compute_folds(pages, anchors={RAMP_TILE: [steps[1]]})
    check(set(folded) == {("p0", 0), ("p2", 0), ("p3", 0)},
          "every other step folds onto the sheet cell's palette", str(sorted(folded)))
    check(all(f["basePalette"] == steps[1] for f in folded.values()),
          "and the base is the palette the sheet cell carries",
          str({f["basePalette"] for f in folded.values()}))
    check(folded[("p0", 0)]["brightness"] > 1.0,
          "the brighter step is rebuilt with a Brightness above 1",
          str(folded[("p0", 0)]["brightness"]))
    check(folded[("p3", 0)]["brightness"] < 1.0 and folded[("p3", 0)]["drift"] <= P.HUE_DRIFT_GATE_DEG,
          "the darkest step now folds inside the gate, measured against the cell",
          str(folded[("p3", 0)]))
    check(list(bases) == [("p1", 0)], "one cell to paint, the sheet's", str(list(bases)))


def test_an_anchor_never_folds_a_colourway():
    pages = [_Page("a", [_Row(RAMP_TILE, "0F0B1B2B")]), _Page("b", [_Row(RAMP_TILE, "0F171626")])]
    folded, _b, _s = P.compute_folds(pages, anchors={RAMP_TILE: ["0F0B1B2B"]})
    check(not folded, "a red and a green of one pattern stay two cells under an anchor", str(folded))


def test_an_anchor_absent_from_the_pages_changes_nothing():
    pages, _steps = _zelda_fade_pages()
    plain = P.compute_folds(pages)[0]
    anchored = P.compute_folds(pages, anchors={RAMP_TILE: ["0F303030"], "00" * 16: ["0F000000"]})[0]
    check(plain == anchored, "an anchor palette no page row carries is ignored")


def test_sheet_fold_anchors_reads_source_keys_and_skips_non_sheets():
    import json
    import tempfile
    with tempfile.TemporaryDirectory() as td:
        d = Path(td)
        (d / "sprite.json").write_text(json.dumps({"cells": [{"tiles": [
            {"tile": "11" * 16, "palette": "ff162630", "source": "22" * 16}, None,
            {"tile": "33" * 16, "palette": "0F001030"}]}]}))
        (d / "broken.json").write_text("{")
        (d / "poses.json").write_text(json.dumps({"poses": []}))
        got = P.sheet_fold_anchors(d)
        check(got == {"22" * 16: ["FF162630"], "33" * 16: ["0F001030"]},
              "anchors key by source when present and upper-case the palette", str(got))
        check(P.sheet_fold_anchors(d / "missing") == {}, "a pack without sheets has no anchors")


def test_the_brightness_text_is_the_recorders():
    # MesenSheets::FoldBrightnessText writes the text the recorder proved
    # exact; mep_build re-formats the sidecar number with this function, so
    # the two must agree digit for digit.
    got = [P.fold_brightness_text(b) for b in (0, 1, 0.75, 0.59506, 1.2059943, 4)]
    check(got == ["0", "1", "0.75", "0.5951", "1.206", "4"], "fold_brightness_text matches FoldBrightnessText", str(got))
    folds, problems = P.entry_folds({"palette": "0F162A30", "folds": [{"palette": "0f0f0f0f", "brightness": 0.5}]})
    check(folds == [("0F0F0F0F", "0.5")] and not problems, "entry_folds upper-cases the palette and formats the text")
    check(P.entry_folds({"palette": "0F162A30"}) == ([], []), "an entry without folds has none and no problem")


def main():
    tests = [
        test_the_brightness_text_is_the_recorders,
        test_the_shared_vectors_hold_for_the_python_predicate,
        test_the_relation_is_the_kits_own_fold_test,
        test_without_a_sheet_cell_the_brightest_step_is_the_base,
        test_a_sheet_cell_anchors_the_fold_and_a_brighter_step_folds_above_one,
        test_an_anchor_never_folds_a_colourway,
        test_an_anchor_absent_from_the_pages_changes_nothing,
        test_sheet_fold_anchors_reads_source_keys_and_skips_non_sheets,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
