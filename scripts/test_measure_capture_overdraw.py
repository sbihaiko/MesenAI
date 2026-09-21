#!/usr/bin/env python3
"""Unit tests for scripts/measure_capture_overdraw.py (ADR-0221).

No emulator, no ROM, no pack: every frame here is a synthetic PNG built to
isolate one property of the metric. What needs a real core -- that a recorded
pack actually erases the cells the tool says it does -- is the acceptance test
in ADR-0221, and is exercised by running the tool on a sweep.

Usage: python3 scripts/test_measure_capture_overdraw.py
"""
import os
import sys
import tempfile
from pathlib import Path

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import measure_capture_overdraw as m  # noqa: E402

FAILURES = []


def check(condition, message):
    if condition:
        print(f"  ok   {message}")
    else:
        print(f"  FAIL {message}")
        FAILURES.append(message)


def raises(fn, message):
    try:
        fn()
    except Exception:
        print(f"  ok   {message}")
        return
    print(f"  FAIL {message}")
    FAILURES.append(message)


def frame(path: Path, scale: int, detailed_cells=(), flat=(40, 40, 40)):
    """A frame that is flat everywhere but in `detailed_cells` (cx, cy)."""
    img = Image.new("RGB", (m.NATIVE[0] * scale, m.NATIVE[1] * scale), flat)
    side = m.CELL * scale
    for cx, cy in detailed_cells:
        for i in range(side):
            img.putpixel((cx * side + i, cy * side + i), (255, 255, 255))
    img.save(path)
    return path


def test_no_loss(tmp: Path):
    print("a render that carries the ROM's detail loses nothing")
    base = frame(tmp / "a-base.png", 1, [(3, 4), (10, 11)])
    rend = frame(tmp / "a-rend.png", 4, [(3, 4), (10, 11)])
    out = m.measure(base, rend)
    check(out["detail_cells"] == 2, "both detailed cells are counted as ROM detail")
    check(out["erased_cells"] == 0, "nothing is reported erased")


def test_erased(tmp: Path):
    print("a render that flattens a detailed cell reports it, with coordinates")
    base = frame(tmp / "b-base.png", 1, [(3, 4), (10, 11)])
    rend = frame(tmp / "b-rend.png", 4, [(3, 4)])
    out = m.measure(base, rend)
    check(out["erased_cells"] == 1, "exactly the flattened cell is erased")
    check(out["erased_at"] == [[10, 11]], "the erased cell is named by (x, y)")


def test_flat_rom_cell_is_never_erased(tmp: Path):
    print("a cell the ROM itself draws flat can never count as erased")
    base = frame(tmp / "c-base.png", 1, [])
    rend = frame(tmp / "c-rend.png", 4, [])
    out = m.measure(base, rend)
    check(out["detail_cells"] == 0, "a flat ROM frame has no detail cells")
    check(out["erased_cells"] == 0, "and so cannot lose any")


def test_repaint_does_not_count(tmp: Path):
    print("a repainted cell with any variation at all is not erased")
    base = frame(tmp / "d-base.png", 1, [(5, 5)])
    rend = frame(tmp / "d-rend.png", 4, [(5, 5)], flat=(9, 9, 9))
    out = m.measure(base, rend)
    check(out["erased_cells"] == 0, "the metric is conservative by construction")


def test_native_render_is_refused(tmp: Path):
    print("a render that came back at native size means no pack was loaded")
    base = frame(tmp / "e-base.png", 1, [(1, 1)])
    rend = frame(tmp / "e-rend.png", 1, [(1, 1)])
    raises(lambda: m.measure(base, rend), "1x render raises instead of reporting zero")


def test_odd_size_is_refused(tmp: Path):
    print("a frame that is not a whole multiple of 256x240 is refused")
    odd = tmp / "f-odd.png"
    Image.new("RGB", (300, 240), (0, 0, 0)).save(odd)
    base = frame(tmp / "f-base.png", 1, [(1, 1)])
    raises(lambda: m.measure(base, odd), "300x240 raises")
    raises(lambda: m.measure(odd, base), "a non-native baseline raises")


def test_sweep_pairs_by_timestamp(tmp: Path):
    print("sweep mode pairs by timestamp and ignores unpaired runs")
    root = tmp / "sweep"
    for stamp, cells in ((18, [(3, 4)]), (22, [])):
        shots = root / f"post-{stamp}" / "mesen-home" / "Screenshots"
        shots.mkdir(parents=True)
        frame(shots / "rom_000.png", 4, cells)
        shots = root / f"none-{stamp}" / "mesen-home" / "Screenshots"
        shots.mkdir(parents=True)
        frame(shots / "rom_000.png", 1, [(3, 4)])
    lonely = root / "none-99" / "mesen-home" / "Screenshots"
    lonely.mkdir(parents=True)
    frame(lonely / "rom_000.png", 1, [])

    rows = m._sweep(root, "none", "post")
    check([r["label"] for r in rows] == ["18", "22"], "the unpaired `none-99` is skipped")
    check(rows[0]["erased_cells"] == 0, "the frame the capture matches loses nothing")
    check(rows[1]["erased_cells"] == 1, "the later frame loses the cell the ROM draws")


def test_sweep_ignores_the_pack_itself(tmp: Path):
    print("a with-pack run's own PNGs are not mistaken for frames")
    root = tmp / "sweep2"
    for prefix, scale in (("none", 1), ("post", 4)):
        shots = root / f"{prefix}-10" / "mesen-home" / "Screenshots"
        shots.mkdir(parents=True)
        frame(shots / "rom_000.png", scale, [(0, 0)])
    pack = root / "post-10" / "rom" / "auto"
    pack.mkdir(parents=True)
    for n in range(3):
        Image.new("RGB", (64, 64), (1, 2, 3)).save(pack / f"{n}.png")
    rows = m._sweep(root, "none", "post")
    check(len(rows) == 1 and rows[0]["erased_cells"] == 0,
          "the three pack PNGs beside the screenshot are ignored")


def test_exit_code_is_the_verdict(tmp: Path):
    print("the exit code is the verdict, so a sweep can gate a fix")
    base = frame(tmp / "g-base.png", 1, [(2, 2)])
    clean = frame(tmp / "g-clean.png", 4, [(2, 2)])
    lossy = frame(tmp / "g-lossy.png", 4, [])
    check(m.main([str(base), str(clean)]) == 0, "no erased cell exits 0")
    check(m.main([str(base), str(lossy)]) == 1, "any erased cell exits 1")


def main():
    with tempfile.TemporaryDirectory() as tmpdir:
        tmp = Path(tmpdir)
        test_no_loss(tmp)
        test_erased(tmp)
        test_flat_rom_cell_is_never_erased(tmp)
        test_repaint_does_not_count(tmp)
        test_native_render_is_refused(tmp)
        test_odd_size_is_refused(tmp)
        test_sweep_pairs_by_timestamp(tmp)
        test_sweep_ignores_the_pack_itself(tmp)
        test_exit_code_is_the_verdict(tmp)
    print()
    if FAILURES:
        print(f"{len(FAILURES)} failure(s)")
        return 1
    print("all checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
