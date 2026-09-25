#!/usr/bin/env python3
"""Unit tests for scripts/measure_capture_overdraw.py (ADR-0221).

No emulator, no ROM, no pack: every frame here is a synthetic PNG built to
isolate one property of the metric. What needs a real core -- that a recorded
pack actually erases the cells the tool says it does -- is the acceptance test
in ADR-0221, and is exercised by running the tool on a sweep.

Usage: python3 scripts/test_measure_capture_overdraw.py
"""
import json
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


# ---------------------------------------------------------------- F12.15 split (ADR-0224 §4)

FLAT_TILE = "00" * 16                    # every pixel colour index 0
DETAIL_TILE = "80" + "00" * 15           # one pixel of colour index 1, rest 0
PAL_WORD = "0F300F0F"                    # index 0 black, index 1 white


def grid_dump(path: Path, detail_cells=(), ram_sprites=None, oam_entries=None):
    """One retained frame: flat everywhere but `detail_cells`; an `M` line
    with `ram_sprites` [(x, y, attr)] in the shadow OAM at $0200 when given;
    an ADR-0222 `oam.txt` beside it with `oam_entries` [(x, y)] when given."""
    lines = ["F 0"]
    if ram_sprites is not None:
        ram = bytearray(0x800)
        for i, (x, y, attr) in enumerate(ram_sprites):
            ram[0x200 + i * 4:0x200 + i * 4 + 4] = bytes((y - 1, 0, attr, x))
        for i in range(len(ram_sprites), 64):
            ram[0x200 + i * 4] = 0xF8    # off screen
        lines.append("M " + ram.hex().upper())
    lines += [f"K 0 {FLAT_TILE} {PAL_WORD}", f"K 1 {DETAIL_TILE} {PAL_WORD}", f"P 0 {PAL_WORD}"]
    for cy in range(m.ROWS):
        for cx in range(m.COLS):
            lines.append(f"{cx * 8} {cy * 8} {1 if (cx, cy) in detail_cells else 0} 0")
    path.write_text("\n".join(lines) + "\n")
    if oam_entries is not None:
        entries = " ".join(f"7,{x},{y},0" for x, y in oam_entries)
        (path.parent / "oam.txt").write_text(
            f"K 7 {DETAIL_TILE} {PAL_WORD}\nP 0 {PAL_WORD}\n0 1 0 0 {entries}\n")
    return path


def baseline_from(rec: m.Recording, path: Path, sprite_cells=()):
    """The unpacked frame: the recording's background plus a sprite diagonal."""
    img = Image.fromarray(rec._rgb[0].copy())
    for cx, cy in sprite_cells:
        for i in range(8):
            img.putpixel((cx * 8 + i, cy * 8 + i), (200, 50, 50))
    img.save(path)
    return path


def flat_render(path: Path):
    Image.new("RGB", (m.NATIVE[0] * 4, m.NATIVE[1] * 4), (0, 0, 0)).save(path)
    return path


def test_split_background_only(tmp: Path):
    print("a flat render over ROM background detail is background loss")
    (tmp / "g1").mkdir()
    rec = m.Recording(grid_dump(tmp / "g1" / "grid.txt", detail_cells=[(3, 4)], ram_sprites=[]))
    out = m.measure(baseline_from(rec, tmp / "g1-base.png"), flat_render(tmp / "g1-rend.png"), rec)
    check(out["erased_cells"] == 1, "one erased cell")
    check(out["erased_background_cells"] == 1 and out["erased_sprite_cells"] == 0, "split: 1 background, 0 sprite")
    check(out["sprite_source"] == "ram-line", "the `M` line is the sprite source")
    check(out["retained_frame"] == 0 and out["frame_match_residual_cells"] == 0, "the frame is located exactly")


def test_split_sprite_only_front(tmp: Path):
    print("a flat render under a front sprite on flat background is sprite loss, not behind-bg")
    (tmp / "g2").mkdir()
    rec = m.Recording(grid_dump(tmp / "g2" / "grid.txt", ram_sprites=[(80, 88, 0x02)]))
    out = m.measure(baseline_from(rec, tmp / "g2-base.png", sprite_cells=[(10, 11)]),
                    flat_render(tmp / "g2-rend.png"), rec)
    check(out["erased_background_cells"] == 0 and out["erased_sprite_cells"] == 1, "split: 0 background, 1 sprite")
    check(out["erased_sprite_behind_bg_cells"] == 0, "attr 0x02 is a front sprite")
    check(out["erased_sprite_unattributed_cells"] == 0, "the OAM box covers the cell")
    check(out["frame_match_residual_cells"] == 1, "the sprite-covered cell is the match residual")


def test_split_sprite_behind_bg(tmp: Path):
    print("OAM attribute bit 5 marks the erased sprite cell as behind-background")
    (tmp / "g3").mkdir()
    rec = m.Recording(grid_dump(tmp / "g3" / "grid.txt", ram_sprites=[(80, 88, 0x21)]))
    out = m.measure(baseline_from(rec, tmp / "g3-base.png", sprite_cells=[(10, 11)]),
                    flat_render(tmp / "g3-rend.png"), rec)
    check(out["erased_sprite_cells"] == 1 and out["erased_sprite_behind_bg_cells"] == 1, "1 sprite, 1 behind-bg")


def test_split_both(tmp: Path):
    print("background and sprite loss in one frame land in their own columns")
    (tmp / "g4").mkdir()
    rec = m.Recording(grid_dump(tmp / "g4" / "grid.txt", detail_cells=[(3, 4), (5, 5)],
                                ram_sprites=[(80, 88, 0x20)]))
    out = m.measure(baseline_from(rec, tmp / "g4-base.png", sprite_cells=[(10, 11)]),
                    flat_render(tmp / "g4-rend.png"), rec)
    check(out["erased_cells"] == 3, "total stays 3 for continuity")
    check(out["erased_background_cells"] == 2 and out["erased_sprite_cells"] == 1, "split: 2 background, 1 sprite")
    check(out["erased_sprite_behind_bg_cells"] == 1, "the one sprite cell is behind-bg")


def test_split_unattributed_and_sprite_height(tmp: Path):
    print("a sprite cell no OAM box covers is `unattributed`; 8x16 boxes reach the lower cell")
    (tmp / "g5").mkdir()
    grid = grid_dump(tmp / "g5" / "grid.txt", ram_sprites=[(80, 80, 0x20)])
    rec8 = m.Recording(grid, sprite_height=8)
    base = baseline_from(rec8, tmp / "g5-base.png", sprite_cells=[(10, 11)])
    out8 = m.measure(base, flat_render(tmp / "g5-rend.png"), rec8)
    check(out8["erased_sprite_cells"] == 1 and out8["erased_sprite_unattributed_cells"] == 1,
          "an 8x8 box at y=80 does not reach row 11")
    out16 = m.measure(base, tmp / "g5-rend.png", m.Recording(grid, sprite_height=16))
    check(out16["erased_sprite_unattributed_cells"] == 0 and out16["erased_sprite_behind_bg_cells"] == 1,
          "an 8x16 box does, and carries the behind-bg bit")


def test_split_oam_dump_source_has_no_priority(tmp: Path):
    print("without an `M` line the ADR-0222 OAM dump gives presence but no behind-bg bit")
    (tmp / "g6").mkdir()
    rec = m.Recording(grid_dump(tmp / "g6" / "grid.txt", oam_entries=[(80, 88)]))
    check(rec.sprite_source == "oam-dump", "oam.txt beside grid.txt is found")
    out = m.measure(baseline_from(rec, tmp / "g6-base.png", sprite_cells=[(10, 11)]),
                    flat_render(tmp / "g6-rend.png"), rec)
    check(out["erased_sprite_cells"] == 1 and out["erased_sprite_unattributed_cells"] == 0, "the entry covers the cell")
    check(out["erased_sprite_behind_bg_cells"] is None, "no attribute byte in the format: behind-bg is n/a")


def test_split_absent_without_grid_and_verdict_column(tmp: Path):
    print("old callers see the old keys; --verdict background ignores sprite loss")
    (tmp / "g7").mkdir()
    grid = grid_dump(tmp / "g7" / "grid.txt", ram_sprites=[(80, 88, 0x20)])
    rec = m.Recording(grid)
    base = baseline_from(rec, tmp / "g7-base.png", sprite_cells=[(10, 11)])
    rend = flat_render(tmp / "g7-rend.png")
    plain = m.measure(base, rend)
    check("erased_background_cells" not in plain and plain["erased_cells"] == 1, "no --grid: total only")
    check(m.main([str(base), str(rend)]) == 1, "total verdict: sprite loss fails")
    check(m.main([str(base), str(rend), "--grid", str(grid), "--verdict", "background"]) == 0,
          "background verdict: sprite loss alone passes (ADR-0224 §4)")


def test_split_sweep_json(tmp: Path):
    print("sweep rows and --json carry the split columns")
    (tmp / "g8").mkdir()
    grid = grid_dump(tmp / "g8" / "grid.txt", detail_cells=[(3, 4)], ram_sprites=[])
    rec = m.Recording(grid)
    root = tmp / "sweep-split"
    shots = root / "none-18" / "mesen-home" / "Screenshots"
    shots.mkdir(parents=True)
    baseline_from(rec, shots / "rom_000.png")
    shots = root / "post-18" / "mesen-home" / "Screenshots"
    shots.mkdir(parents=True)
    flat_render(shots / "rom_000.png")
    out_json = tmp / "split.json"
    rc = m.main(["--sweep", str(root), "--grid", str(grid), "--json", str(out_json)])
    rows = json.loads(out_json.read_text())
    check(rc == 1 and rows[0]["erased_background_cells"] == 1 and rows[0]["erased_sprite_cells"] == 0,
          "the JSON row has both columns")


def main():
    with tempfile.TemporaryDirectory() as tmpdir:
        tmp = Path(tmpdir)
        test_split_background_only(tmp)
        test_split_sprite_only_front(tmp)
        test_split_sprite_behind_bg(tmp)
        test_split_both(tmp)
        test_split_unattributed_and_sprite_height(tmp)
        test_split_oam_dump_source_has_no_priority(tmp)
        test_split_absent_without_grid_and_verdict_column(tmp)
        test_split_sweep_json(tmp)
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
