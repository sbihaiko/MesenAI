"""Headless suite for artist_map.py — the stage panorama generator and its slicer.

Everything here runs on a *synthetic* recording: a hand-written world of 8x8
cells, a camera walked over it, and the grid dump the recorder would have
written for that walk. That makes every answer checkable by hand — the world is
known, so the panorama is known — which a real Contra dump can never be.

What the suite pins down:

  * the dump parser (fine x recovery, shape interning, collapsed repeats);
  * the shift search and the scroll accumulation that stand in for the camera
    position the dump does not carry;
  * de-duplication by world position, not by frame: a stretch walked twice is
    written once, and a position that was re-seen *differently* is counted,
    never silently overwritten;
  * the cut rule: a jump to an unrelated place starts a new region;
  * the HUD band detector, on a world with a screen-fixed top row;
  * the round trip — panorama -> sidecar -> slicer -> a textures/sheets/
    drop-in — including the first-occurrence-wins rule when one tile key is
    painted differently at two panorama positions.

Standard library only, no pytest, no emulator.

Run:  python3 scripts/test_artist_map.py
"""

import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import artist_map as M  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


# --- fixtures ---------------------------------------------------------------


def tile_hex(n: int) -> str:
    """16 bytes of 2bpp data that differ per shape, so a mis-slice cannot pass
    by accident."""
    return f"{n & 0xFFFFFFFF:08X}" * 4


def palette_hex(n: int) -> str:
    return f"0F{(n * 5 + 1) & 0x3F:02X}{(n * 7 + 2) & 0x3F:02X}{(n * 11 + 3) & 0x3F:02X}"


def palette_id(n: int) -> int:
    """The palette plane's id for a shape. The fixture gives each shape its own
    palette word, which is the identity case; the recolour case builds its own
    ids by hand."""
    return n % M.UNKNOWN_PALETTE


def world_shape(wx: int, wy: int) -> int:
    """The synthetic stage: one shape per (column mod 23, row), which makes
    every 8x8 cell of the panorama predictable from its world position."""
    return 1 + (wx % 23) * M.ROWS + wy


def write_dump(path: Path, camera, *, hud_row=None, world=world_shape, repeats=1):
    """The dump the recorder would have written while the camera visited
    `camera` (a list of (world col, world row) origins).

    Cell lines carry `col * 8 + fineX` and the cell's palette id; the walk uses
    fine x 0 throughout, which is what a camera that moves whole cells does."""
    lines = []
    emitted = set()
    pal_emitted = set()
    for frame, (ox, oy) in enumerate(camera):
        for _ in range(repeats):
            lines.append(f"F {frame}")
        for row in range(M.ROWS):
            for col in range(M.COLS):
                if hud_row is not None and row == hud_row:
                    # A HUD sits at a fixed screen position: its shape depends
                    # on the column on screen, never on where the camera is.
                    sid = 9000 + col
                else:
                    sid = world(ox + col, oy + row)
                if sid not in emitted:
                    emitted.add(sid)
                    lines.append(f"K {sid} {tile_hex(sid)} {palette_hex(sid)}")
                pid = palette_id(sid)
                if pid not in pal_emitted:
                    pal_emitted.add(pid)
                    lines.append(f"P {pid} {palette_hex(sid)}")
                lines.append(f"{col * 8} {row * 8} {sid} {pid}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


class FakePack:
    """Stands in for a recorded pack: it knows every synthetic key and renders
    its art from the key, which is exactly what artist_map falls back to when a
    real pack has no art for a tile."""

    scale = 1

    def __init__(self, keys):
        self.tiles = {k: (0, 0, 0) for k in keys}
        self.index_keyed = False

    @property
    def keys(self):
        return set(self.tiles)

    def crop_1x(self, key):
        return M.render_tile(*key) if key in self.tiles else None


def all_keys(camera, hud_row=None, world=world_shape):
    keys = set()
    for ox, oy in camera:
        for row in range(M.ROWS):
            for col in range(M.COLS):
                sid = 9000 + col if (hud_row is not None and row == hud_row) else world(ox + col, oy + row)
                keys.add((tile_hex(sid), palette_hex(sid)))
    return keys


def walk(dx, dy, steps, start=(0, 0)):
    return [(start[0] + dx * i, start[1] + dy * i) for i in range(steps)]


# --- cases ------------------------------------------------------------------


def test_dump_parser_recovers_frames_shapes_and_collapsed_repeats():
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        write_dump(p, walk(1, 0, 4), repeats=3)
        frames, shapes, palettes = M.parse_grid_dump(p)
        check(len(frames) == 4, "one GridFrame per distinct F block", str(len(frames)))
        check(all(f.repeat == 3 for f in frames), "a repeated F line is a repeat count, not a new frame",
              str([f.repeat for f in frames]))
        check(frames[0].rows[0][0] == world_shape(0, 0), "a cell line lands at its column and row",
              str(frames[0].rows[0][0]))
        check(shapes[world_shape(0, 0)] == (tile_hex(world_shape(0, 0)), palette_hex(world_shape(0, 0))),
              "a K line interns the (tileData, palette) key of its shape")
        sid = world_shape(0, 0)
        check(palettes[palette_id(sid)] == palette_hex(sid),
              "a P line interns a palette word", str(palettes.get(palette_id(sid))))
        check(frames[0].pals[0][0] == palette_id(sid),
              "and the cell line's fourth field is the cell's palette id",
              str(frames[0].pals[0][0]))


def test_fine_scroll_is_recovered_from_the_cell_x():
    """The dump never names fine x; `x & 7` is it, and the column is what is
    left. A frame at fine 5 must produce the same columns as one at fine 0."""
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        lines = ["F 0"]
        for col in range(M.COLS):
            sid = 1 + col
            lines.append(f"K {sid} {tile_hex(sid)} {palette_hex(sid)}")
            lines.append(f"{col * 8 + 5} 0 {sid}")
        p.write_text("\n".join(lines) + "\n", encoding="utf-8")
        frames, _shapes, _palettes = M.parse_grid_dump(p)
        check(frames[0].fine == 5, "fine x is the low three bits of the cell x", str(frames[0].fine))
        check(frames[0].rows[0] == [1 + c for c in range(M.COLS)],
              "and the columns are unshifted by it", str(frames[0].rows[0][:4]))


def test_best_shift_finds_the_camera_step_in_both_axes():
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        write_dump(p, [(0, 0), (3, 0), (3, 2)])
        frames, _shapes, _palettes = M.parse_grid_dump(p)
        rows = tuple(range(M.ROWS))
        dx, dy, s = M.best_shift(frames[0], frames[1], rows)
        check((dx, dy) == (3, 0) and s >= M.GOOD_ENOUGH, "a horizontal step is read off the overlap",
              f"{dx},{dy},{s}")
        dx, dy, s = M.best_shift(frames[1], frames[2], rows, 3, 0)
        check((dx, dy) == (0, 2) and s >= M.GOOD_ENOUGH, "so is a vertical one", f"{dx},{dy},{s}")


def test_a_horizontal_walk_becomes_one_panorama_of_the_expected_size():
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        camera = walk(1, 0, 40)
        write_dump(p, camera)
        frames, shapes, palettes = M.parse_grid_dump(p)
        regions = M.stitch(frames, 0, 0)
        check(len(regions) == 1, "an uninterrupted walk is one region", str(len(regions)))
        y0, x0, y1, x1 = regions[0].bounds
        check((x1 - x0 + 1) == M.COLS + 39, "the panorama is as wide as the walk plus one screen",
              str(x1 - x0 + 1))
        check((y1 - y0 + 1) == M.ROWS, "and one screen tall", str(y1 - y0 + 1))
        check(M.orientation_of(regions[0]) == "horizontal", "a side-scrolling stage reads as horizontal")
        pack = FakePack(all_keys(camera))
        _img, orig, cells, stats = M.build_panorama(regions[0], shapes, palettes, pack, 1)
        check(orig.width == (M.COLS + 39) * 8 and orig.height == M.ROWS * 8,
              "the image is 8 px per cell", f"{orig.width}x{orig.height}")
        check(stats["cells"] == len(cells) == (M.COLS + 39) * M.ROWS,
              "every world cell of the walk is on it", str(stats["cells"]))


def test_a_vertical_walk_reads_as_vertical():
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        write_dump(p, walk(0, 1, 40))
        frames, _shapes, _palettes = M.parse_grid_dump(p)
        regions = M.stitch(frames, 0, 0)
        check(len(regions) == 1 and M.orientation_of(regions[0]) == "vertical",
              "a stage that scrolls down stitches downwards", str([M.orientation_of(r) for r in regions]))
        y0, _x0, y1, _x1 = regions[0].bounds
        check((y1 - y0 + 1) == M.ROWS + 39, "and is as tall as the climb plus one screen", str(y1 - y0 + 1))


def test_a_stretch_walked_twice_is_written_once():
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        camera = walk(1, 0, 20) + walk(-1, 0, 20, start=(19, 0)) + walk(1, 0, 20)
        write_dump(p, camera)
        frames, _shapes, _palettes = M.parse_grid_dump(p)
        regions = M.stitch(frames, 0, 0)
        check(len(regions) == 1, "walking back and forth is still one region", str(len(regions)))
        r = regions[0]
        check(len(r.cells) == (M.COLS + 19) * M.ROWS,
              "de-duplication is by world position, not by frame", str(len(r.cells)))
        check(r.conflicts == 0, "and a world that did not change produces no disagreement", str(r.conflicts))
        check(r.agreements > len(r.cells), "every re-sighting that agreed is counted", str(r.agreements))


def test_a_position_re_seen_differently_keeps_what_it_showed_most():
    """A door that opens, a wall that is destroyed, water that animates: the
    same world cell, two drawings. The panorama keeps the one the recording
    showed most often - measured on Contra's waterfall, the *first* variant a
    position is seen with holds for a median 0.02 of its sightings, because the
    frame that first covers a world position is a transitional one. Every
    sighting that disagreed is still counted."""
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        camera = (walk(1, 0, 12) + walk(-1, 0, 12, start=(11, 0))
                  + walk(1, 0, 12) + walk(-1, 0, 12, start=(11, 0)))

        def world(wx, wy, frame):
            # Half way through the first pass, the cell at world (5, 5) starts
            # reading differently - a wall the player destroyed on the way out -
            # and stays that way for the rest of the recording.
            return 7777 if (wx, wy) == (5, 5) and frame > 12 else world_shape(wx, wy)

        lines = []
        emitted = set()
        for frame, (ox, oy) in enumerate(camera):
            lines.append(f"F {frame}")
            for row in range(M.ROWS):
                for col in range(M.COLS):
                    sid = world(ox + col, oy + row, frame)
                    if sid not in emitted:
                        emitted.add(sid)
                        lines.append(f"K {sid} {tile_hex(sid)} {palette_hex(sid)}")
                        lines.append(f"P {palette_id(sid)} {palette_hex(sid)}")
                    lines.append(f"{col * 8} {row * 8} {sid} {palette_id(sid)}")
        p.write_text("\n".join(lines) + "\n", encoding="utf-8")

        frames, _shapes, _palettes = M.parse_grid_dump(p)
        regions = M.stitch(frames, 0, 0)
        check(len(regions) == 1, "the change does not cut the region", str(len(regions)))
        r = regions[0]
        tally = r.cells[(5, 5)]
        check(len(tally) == 2, "both drawings of the position are remembered", str(tally))
        check(r.winner((5, 5))[0] == 7777, "the panorama keeps the one seen most often",
              str(tally))
        check(r.conflicts > 0, "and every sighting that disagreed is counted", str(r.conflicts))
        check((5, 5) in r.conflicting, "the position is reported as one that ever disagreed",
              str(r.conflicting))


def test_a_tie_between_two_drawings_goes_to_the_first_sighting():
    """Nothing in the evidence separates them, so the rule has to be stated:
    the earlier sighting wins."""
    r = M.Region()
    r.see((0, 0), (11, 1))
    r.see((0, 0), (22, 2))
    r.see((0, 0), (22, 2))
    r.see((0, 0), (11, 1))
    check(r.winner((0, 0)) == (11, 1), "a tie goes to the first sighting", str(r.cells[(0, 0)]))
    check(r.conflicts == 2, "the losing sightings are counted", str(r.conflicts))
    check(r.agreements == 1, "and so are the ones that confirmed the winner", str(r.agreements))


def test_a_jump_to_an_unrelated_place_cuts_the_region():
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"

        def other_world(wx, wy):
            return 5000 + (wx % 17) * M.ROWS + wy

        lines = []
        emitted = set()
        frame = 0
        for world, camera in ((world_shape, walk(1, 0, 40)), (other_world, walk(1, 0, 40))):
            for ox, oy in camera:
                lines.append(f"F {frame}")
                frame += 1
                for row in range(M.ROWS):
                    for col in range(M.COLS):
                        sid = world(ox + col, oy + row)
                        if sid not in emitted:
                            emitted.add(sid)
                            lines.append(f"K {sid} {tile_hex(sid)} {palette_hex(sid)}")
                            lines.append(f"P {palette_id(sid)} {palette_hex(sid)}")
                        lines.append(f"{col * 8} {row * 8} {sid} {palette_id(sid)}")
        p.write_text("\n".join(lines) + "\n", encoding="utf-8")
        frames, _shapes, _palettes = M.parse_grid_dump(p)
        regions = M.stitch(frames, 0, 0)
        check(len(regions) == 2, "two unrelated places are two panoramas, never one collage",
              str(len(regions)))
        check(all(len(r.cells) == (M.COLS + 39) * M.ROWS for r in regions),
              "and each keeps its own full extent", str([len(r.cells) for r in regions]))


def test_the_hud_band_is_found_and_kept_out_of_the_panorama():
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        camera = walk(1, 0, 30)
        write_dump(p, camera, hud_row=0)
        frames, shapes, palettes = M.parse_grid_dump(p)
        top, bottom = M.hud_bands(frames, tuple(range(M.ROWS)))
        check((top, bottom) == (1, 0), "a screen-fixed top row is detected as a HUD band",
              f"{top},{bottom}")
        regions = M.stitch(frames, top, bottom)
        pack = FakePack(all_keys(camera, hud_row=0))
        _img, orig, cells, _stats = M.build_panorama(regions[0], shapes, palettes, pack, 1)
        check(orig.height == (M.ROWS - 1) * 8, "and it is not painted into the panorama",
              str(orig.height))
        hud_keys = {(tile_hex(9000 + c), palette_hex(9000 + c)) for c in range(M.COLS)}
        on_map = {(c["tiles"][0]["tile"], c["tiles"][0]["palette"]) for c in cells}
        check(not (hud_keys & on_map), "no HUD tile key reaches the sidecar",
              str(len(hud_keys & on_map)))


BLANK = 8000


def write_blank_margin_dump(path: Path, camera, *, blank_rows, hud_rows, drop_last_col_on_odd):
    """A dump shaped like Castlevania's (issue #221): a margin of the blank tile
    on top, the status bar right below it, the stage under both.

    The blank tile is a shape like any other - `BLANK` here, shape id 0 in the
    real dump - never the EMPTY sentinel, which only ever means "no cell was
    recorded here". `drop_last_col_on_odd` reproduces the recorded asymmetry the
    issue measured: when one side of a comparison is missing its rightmost
    column, a fully blank row scores 31/32 unshifted against a perfect 1.0
    shifted, and used to vote "moving" off that single cell."""
    lines = []
    emitted = set()
    for frame, (ox, oy) in enumerate(camera):
        lines.append(f"F {frame}")
        cols = M.COLS - 1 if (drop_last_col_on_odd and frame % 2) else M.COLS
        for row in range(M.ROWS):
            for col in range(cols):
                if row in blank_rows:
                    sid = BLANK
                elif row in hud_rows:
                    sid = 9000 + col
                else:
                    sid = world_shape(ox + col, oy + row)
                if sid not in emitted:
                    emitted.add(sid)
                    lines.append(f"K {sid} {tile_hex(sid)} {palette_hex(sid)}")
                    lines.append(f"P {palette_id(sid)} {palette_hex(sid)}")
                lines.append(f"{col * 8} {row * 8} {sid} {palette_id(sid)}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def test_a_blank_row_abstains_and_a_hud_below_it_is_still_found():
    """Issue #221, on Castlevania: two rows of the blank tile sit above a
    three-row status bar. A uniformly blank row says nothing about whether it
    scrolled - it matches itself under every shift - so the only thing left to
    separate the unshifted from the shifted comparison is a one-cell edge
    artifact. That artifact voted "moving", and since only a band contiguous
    from row 0 is honoured, the real HUD underneath was never reached and got
    smeared across the panorama."""
    # The root cause, in the two calls hud_bands makes: `a` has all 32 columns
    # of a blank row, `b` is missing the rightmost one.
    a, b = M.GridFrame(0), M.GridFrame(1)
    a.rows[0] = [BLANK] * M.COLS
    b.rows[0] = [BLANK] * (M.COLS - 1) + [M.EMPTY]
    s0, t0 = M.score_shift(a, b, 0, 0, (0,))
    sd, td = M.score_shift(a, b, 1, 0, (0,))
    check((s0, t0) == (31 / 32, 32) and (sd, td) == (1.0, 31),
          "a blank row's shifted comparison beats its unshifted one by one edge cell",
          f"s0={s0} t0={t0} sd={sd} td={td}")

    camera = walk(1, 0, 30)
    for drop in (False, True):
        with tempfile.TemporaryDirectory() as td_:
            p = Path(td_) / "grid.txt"
            write_blank_margin_dump(p, camera, blank_rows={0, 1}, hud_rows={2, 3, 4},
                                    drop_last_col_on_odd=drop)
            frames, shapes, palettes = M.parse_grid_dump(p)
            top, bottom = M.hud_bands(frames, tuple(range(M.ROWS)))
            check((top, bottom) == (5, 0),
                  f"a HUD under a blank margin is found, whole (edge artifact: {drop})",
                  f"{top},{bottom}")
            if (top, bottom) != (5, 0):
                continue
            regions = M.stitch(frames, top, bottom)
            keys = {(tile_hex(9000 + c), palette_hex(9000 + c)) for c in range(M.COLS)}
            keys |= {(tile_hex(BLANK), palette_hex(BLANK))}
            for ox, oy in camera:
                for row in range(5, M.ROWS):
                    for col in range(M.COLS):
                        sid = world_shape(ox + col, oy + row)
                        keys.add((tile_hex(sid), palette_hex(sid)))
            _img, orig, cells, _stats = M.build_panorama(regions[0], shapes, palettes,
                                                         FakePack(keys), 1)
            check(orig.height == (M.ROWS - 5) * 8,
                  "and neither the margin nor the bar is painted into the panorama",
                  str(orig.height))
            on_map = {(c["tiles"][0]["tile"], c["tiles"][0]["palette"]) for c in cells}
            check(not (on_map & {(tile_hex(9000 + c), palette_hex(9000 + c)) for c in range(M.COLS)}),
                  "no status-bar tile key reaches the sidecar")


def test_a_blank_margin_with_nothing_fixed_under_it_stays_in_the_panorama():
    """The other half of the abstention rule: a row with no evidence must not
    invent a band. Blank rows above a stage that has no HUD at all are world,
    and an artist needs them on the strip."""
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        write_blank_margin_dump(p, walk(1, 0, 30), blank_rows={0, 1}, hud_rows=set(),
                                drop_last_col_on_odd=True)
        frames, _shapes, _palettes = M.parse_grid_dump(p)
        top, bottom = M.hud_bands(frames, tuple(range(M.ROWS)))
        check((top, bottom) == (0, 0), "a blank margin over open stage is not a HUD band",
              f"{top},{bottom}")


def test_one_shape_under_two_palettes_keeps_both_colours():
    """The reason the palette plane exists (F9.24). The grid stream interns a
    shape palette-agnostically, so without the plane a tile a bank switch
    recoloured lands on the panorama under whichever colours it happened to be
    seen with first - and an artist repaints a stretch of stage in a palette
    the game never used there."""
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        left, right = palette_hex(1), palette_hex(2)
        lines = [f"P 0 {left}", f"P 1 {right}"]
        emitted = set()
        # The world's drawing repeats every 23 columns, so the same shape is on
        # both sides of the recolour boundary at world column 40.
        for frame, ox in enumerate(range(0, 50)):
            lines.append(f"F {frame}")
            for row in range(M.ROWS):
                for col in range(M.COLS):
                    sid = world_shape(ox + col, row)
                    if sid not in emitted:
                        emitted.add(sid)
                        lines.append(f"K {sid} {tile_hex(sid)} {left}")
                    lines.append(f"{col * 8} {row * 8} {sid} {0 if ox + col < 40 else 1}")
        p.write_text("\n".join(lines) + "\n", encoding="utf-8")
        frames, shapes, palettes = M.parse_grid_dump(p)
        check(palettes == {0: left, 1: right}, "both palette words are interned", str(palettes))
        regions = M.stitch(frames, 0, 0)
        check(len(regions) == 1, "the recolour does not cut the region", str(len(regions)))
        keys = set()
        for sid in shapes:
            keys.add((tile_hex(sid), left))
            keys.add((tile_hex(sid), right))
        pack = FakePack(keys)
        _img, _orig, cells, stats = M.build_panorama(regions[0], shapes, palettes, pack, 1)
        by_col = {}
        for c in cells:
            by_col.setdefault(c["x"] // 8, set()).add(c["tiles"][0]["palette"])
        check(by_col[0] == {left}, "a cell keeps the palette it was recorded with", str(by_col[0]))
        check(by_col[max(by_col)] == {right}, "and a recoloured stretch keeps its own",
              str(by_col[max(by_col)]))
        repeated = {t for t in by_col.values() if len(t) > 1}
        check(not repeated, "no cell carries two palettes at one position", str(repeated))
        # The decisive check: one shape, two palettes, both on the panorama.
        shape = world_shape(0, 0)
        pal_of_shape = {c["tiles"][0]["palette"] for c in cells
                        if c["tiles"][0]["tile"] == tile_hex(shape)}
        check(pal_of_shape == {left, right},
              "one shape recoloured mid-stage appears under both its palettes", str(pal_of_shape))
        check(stats["ambiguous"] == 0, "nothing is attributed when the plane answered",
              str(stats["ambiguous"]))


def test_a_dump_without_the_palette_plane_still_parses_and_says_so():
    """A dump written before F9.24 has three fields per cell. It must parse
    unchanged, fall back to the shape's own first-seen palette, and mark every
    such cell as an attribution rather than a record."""
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "grid.txt"
        lines = []
        emitted = set()
        # Two neighbouring shape ids share one tile data under two palettes -
        # exactly the case the plane exists to resolve - and the dump has no
        # plane to resolve it with.
        def data_of(sid):
            return tile_hex(sid // 2)

        for frame, ox in enumerate(range(0, 40)):
            lines.append(f"F {frame}")
            for row in range(M.ROWS):
                for col in range(M.COLS):
                    sid = world_shape(ox + col, row)
                    if sid not in emitted:
                        emitted.add(sid)
                        lines.append(f"K {sid} {data_of(sid)} {palette_hex(sid)}")
                    lines.append(f"{col * 8} {row * 8} {sid}")
        p.write_text("\n".join(lines) + "\n", encoding="utf-8")
        frames, shapes, palettes = M.parse_grid_dump(p)
        check(palettes == {}, "no P lines, no palette words", str(palettes))
        check(frames[0].pals[0][0] == M.UNKNOWN_PALETTE,
              "and every cell reads as 'no evidence'", str(frames[0].pals[0][0]))
        regions = M.stitch(frames, 0, 0)
        check(len(regions) == 1, "an old dump still stitches", str(len(regions)))
        pack = FakePack({(data_of(sid), palette_hex(sid)) for sid in shapes})
        _img, _orig, cells, stats = M.build_panorama(regions[0], shapes, palettes, pack, 1)
        marked = [c for c in cells if c.get("paletteAttributed")]
        check(marked, "a fallen-back cell whose data has rival palettes is marked paletteAttributed",
              f"{len(marked)} of {len(cells)}")
        check(stats["ambiguous"] == len(marked), "and the total agrees with the marks",
              f"{stats['ambiguous']} vs {len(marked)}")


def test_the_sidecar_addresses_every_cell_and_the_slicer_round_trips_it():
    """The acceptance shape, in miniature: generate, slice the *unpainted*
    panorama back, and get one sheet cell per distinct key with the art the
    panorama had."""
    with tempfile.TemporaryDirectory() as td:
        out = Path(td) / "kit"
        p = Path(td) / "grid.txt"
        camera = walk(1, 0, 25)
        write_dump(p, camera)
        frames, shapes, palettes = M.parse_grid_dump(p)
        regions = M.stitch(frames, 0, 0)
        pack = FakePack(all_keys(camera))
        img, orig, cells, stats = M.build_panorama(regions[0], shapes, palettes, pack, 1)
        map_dir = out / "map"
        map_dir.mkdir(parents=True)
        M.write_png(map_dir / "s-000.png", img)
        M.write_png(map_dir / "s-000.orig.png", orig)
        doc = M.sidecar("s-000", cells, orig.width // 8, 1, "horizontal", stats, regions[0])
        (map_dir / "s-000.json").write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")

        # Every cell's art is the tile its key names, read straight off the image.
        wrong = 0
        for c in cells:
            key = (c["tiles"][0]["tile"], c["tiles"][0]["palette"])
            if orig.crop(c["x"], c["y"], 8, 8).px != M.render_tile(*key).px:
                wrong += 1
        check(wrong == 0, "every sidecar cell points at the art actually on the panorama", str(wrong))

        res = M.cut_painted(map_dir / "s-000.json", map_dir / "s-000.png", out, quiet=True)
        distinct = len({(c["tiles"][0]["tile"], c["tiles"][0]["palette"]) for c in cells})
        check(res["keys"] == distinct, "the slicer emits one cell per distinct key",
              f"{res['keys']} vs {distinct}")
        check(res["conflicts"] == [], "an unpainted panorama has nothing to disagree about",
              str(len(res["conflicts"])))
        check(res["unpainted"] == len(cells), "and every cell still matches its reference twin",
              f"{res['unpainted']} vs {len(cells)}")
        sheet = out / "sheets" / "pano-s-000.json"
        check(sheet.is_file(), "a textures/sheets/ drop-in sidecar is written")
        sd = json.loads(sheet.read_text(encoding="utf-8"))
        check(sd["version"] == 1 and sd["gridUnit"] == 8 and sd["gutter"] == 0,
              "in the ADR-0153 v1 shape mep_build.py slices", json.dumps(sd)[:120])
        art = M.read_png(out / "sheets" / "pano-s-000.png")
        for cell in sd["cells"]:
            key = (cell["tiles"][0]["tile"], cell["tiles"][0]["palette"])
            if art.crop(cell["x"], cell["y"], 8, 8).px != M.render_tile(*key).px:
                wrong += 1
        check(wrong == 0, "and the sliced art is the panorama's art, tile for tile", str(wrong))


def test_one_key_painted_two_ways_resolves_first_occurrence_and_reports_the_rest():
    with tempfile.TemporaryDirectory() as td:
        out = Path(td) / "kit"
        map_dir = out / "map"
        map_dir.mkdir(parents=True)
        # Two cells, same key, second one painted red.
        key = (tile_hex(3), palette_hex(3))
        orig = M.Image(16, 8)
        orig.paste(M.render_tile(*key), 0, 0)
        orig.paste(M.render_tile(*key), 8, 0)
        painted = orig.clone()
        for y in range(8):
            for x in range(8, 16):
                painted.set(x, y, (255, 0, 0, 255))
        M.write_png(map_dir / "s-000.orig.png", orig)
        M.write_png(map_dir / "s-000.png", painted)
        (map_dir / "s-000.json").write_text(json.dumps({
            "version": 1, "kind": "misc", "gridUnit": 8, "gutter": 0, "columns": 2,
            "sheet": "s-000.png", "reference": "s-000.orig.png",
            "cells": [{"index": 0, "x": 0, "y": 0, "tiles": [{"tile": key[0], "palette": key[1]}]},
                      {"index": 1, "x": 8, "y": 0, "tiles": [{"tile": key[0], "palette": key[1]}]}],
        }, indent=1) + "\n", encoding="utf-8")
        res = M.cut_painted(map_dir / "s-000.json", map_dir / "s-000.png", out, quiet=True)
        check(res["keys"] == 1, "one key is one sheet cell however many positions it has", str(res["keys"]))
        check(len(res["conflicts"]) == 1, "the disagreeing position is reported, not hidden",
              str(res["conflicts"]))
        c = res["conflicts"][0]
        check((c["winner"], c["disagrees"]) == ({"x": 0, "y": 0}, {"x": 8, "y": 0}),
              "first occurrence in (y, x) order wins", json.dumps(c))
        art = M.read_png(out / "sheets" / "pano-s-000.png")
        check(art.crop(0, 0, 8, 8).px == M.render_tile(*key).px,
              "and the winner's art is what the sheet carries")


def test_a_painted_panorama_may_be_any_whole_upscale():
    with tempfile.TemporaryDirectory() as td:
        out = Path(td) / "kit"
        map_dir = out / "map"
        map_dir.mkdir(parents=True)
        key = (tile_hex(4), palette_hex(4))
        orig = M.Image(8, 8)
        orig.paste(M.render_tile(*key), 0, 0)
        M.write_png(map_dir / "s-000.orig.png", orig)
        M.write_png(map_dir / "s-000.png", orig.upscale(3))
        (map_dir / "s-000.json").write_text(json.dumps({
            "version": 1, "kind": "misc", "gridUnit": 8, "gutter": 0, "columns": 1,
            "sheet": "s-000.png", "reference": "s-000.orig.png",
            "cells": [{"index": 0, "x": 0, "y": 0, "tiles": [{"tile": key[0], "palette": key[1]}]}],
        }, indent=1) + "\n", encoding="utf-8")
        res = M.cut_painted(map_dir / "s-000.json", map_dir / "s-000.png", out, quiet=True)
        art = M.read_png(out / "sheets" / "pano-s-000.png")
        check(art.width == 16 * 24 and art.height == 24, "a 3x painting slices into 3x cells",
              f"{art.width}x{art.height}")
        check(res["unpainted"] == 1, "a nearest upscale of the reference still counts as unpainted",
              str(res["unpainted"]))
        # A non-integer ratio is refused rather than mis-sliced.
        M.write_png(map_dir / "bad.png", M.Image(12, 8))
        try:
            M.cut_painted(map_dir / "s-000.json", map_dir / "bad.png", out, quiet=True)
            check(False, "a panorama at a fractional scale is refused")
        except M.MapError as e:
            check("whole-factor" in str(e), "a panorama at a fractional scale is refused", str(e))


def test_a_sliced_sheet_comes_out_at_the_pack_scale_not_the_painted_one():
    # A panorama is written at 1x on purpose - it is a picture of a stage and
    # an artist wants it at its own size - but every sheet of a pack shares one
    # <scale> (MEP-v1 2.1). Slicing a 1x painting into a 4x pack used to emit a
    # 1x sheet and fail the build with "painted at 1x while metatiles.png is at
    # 4x". The sidecar carries the pack's scale and the slicer honours it.
    with tempfile.TemporaryDirectory() as td:
        out = Path(td) / "kit"
        map_dir = out / "map"
        map_dir.mkdir(parents=True)
        key = (tile_hex(5), palette_hex(5))
        orig = M.Image(8, 8)
        orig.paste(M.render_tile(*key), 0, 0)
        M.write_png(map_dir / "s-000.orig.png", orig)
        M.write_png(map_dir / "s-000.png", orig)
        doc = {
            "version": 1, "kind": "misc", "gridUnit": 8, "gutter": 0, "columns": 1,
            "sheet": "s-000.png", "reference": "s-000.orig.png",
            "cells": [{"index": 0, "x": 0, "y": 0, "tiles": [{"tile": key[0], "palette": key[1]}]}],
            "panorama": {"scale": 1, "packScale": 4},
        }
        (map_dir / "s-000.json").write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
        M.cut_painted(map_dir / "s-000.json", map_dir / "s-000.png", out, quiet=True)
        art = M.read_png(out / "sheets" / "pano-s-000.png")
        check(art.width == 16 * 32 and art.height == 32,
              "a 1x painting is written at the pack's 4x", f"{art.width}x{art.height}")

        # A painted scale that does not divide the pack's is refused by name,
        # not silently resampled.
        M.write_png(map_dir / "three.png", orig.upscale(3))
        try:
            M.cut_painted(map_dir / "s-000.json", map_dir / "three.png", out, quiet=True)
            check(False, "a painted scale that does not divide the pack's is refused")
        except M.MapError as e:
            check("does not divide" in str(e),
                  "a painted scale that does not divide the pack's is refused", str(e))

        # No packScale (a sidecar written before this) keeps the old behaviour.
        del doc["panorama"]["packScale"]
        (map_dir / "s-000.json").write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
        M.cut_painted(map_dir / "s-000.json", map_dir / "s-000.png", out, quiet=True)
        art = M.read_png(out / "sheets" / "pano-s-000.png")
        check(art.width == 16 * 8 and art.height == 8,
              "a sidecar with no packScale still slices at the painted scale",
              f"{art.width}x{art.height}")


def main():
    tests = [
        test_dump_parser_recovers_frames_shapes_and_collapsed_repeats,
        test_fine_scroll_is_recovered_from_the_cell_x,
        test_best_shift_finds_the_camera_step_in_both_axes,
        test_a_horizontal_walk_becomes_one_panorama_of_the_expected_size,
        test_a_vertical_walk_reads_as_vertical,
        test_a_stretch_walked_twice_is_written_once,
        test_a_position_re_seen_differently_keeps_what_it_showed_most,
        test_a_tie_between_two_drawings_goes_to_the_first_sighting,
        test_a_jump_to_an_unrelated_place_cuts_the_region,
        test_the_hud_band_is_found_and_kept_out_of_the_panorama,
        test_a_blank_row_abstains_and_a_hud_below_it_is_still_found,
        test_a_blank_margin_with_nothing_fixed_under_it_stays_in_the_panorama,
        test_one_shape_under_two_palettes_keeps_both_colours,
        test_a_dump_without_the_palette_plane_still_parses_and_says_so,
        test_the_sidecar_addresses_every_cell_and_the_slicer_round_trips_it,
        test_one_key_painted_two_ways_resolves_first_occurrence_and_reports_the_rest,
        test_a_painted_panorama_may_be_any_whole_upscale,
        test_a_sliced_sheet_comes_out_at_the_pack_scale_not_the_painted_one,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} case group(s) passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
