"""Headless suite for hand-authored conditions and their evaluation over a
recorded route (`mep_conditions.py`). F12.6a / ADR-0197.

Two things are tested, and the second is the one that matters: that the
evaluator agrees with `Core/NES/HdPacks/HdPackConditions.h`. A report that
disagrees with the emulator is worse than no report, so every evaluation case
here is written from the C++ expression rather than from the Python.

Synthetic grid streams in a temp dir; no emulator, no ROM, no recording.

Run:  python3 scripts/test_mep_conditions.py
"""

import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mep_conditions as C  # noqa: E402

_FAILURES = []

T = "AA" * 16          # one shape's 16 pattern bytes
U = "BB" * 16          # another
PAL = "0F001020"
PAL2 = "0F112233"


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def _route(td, lines, name="route.txt"):
    p = Path(td) / name
    p.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return C.load_route(p)


def _two_frame_route(td):
    """Frame 0: T at (0,0) and (1,0); U at (0,1). Frame 1: the two swapped."""
    return _route(td, [
        "F 0", f"K 1 {T} {PAL}", f"K 2 {U} {PAL}",
        "0 0 1", "8 0 2", "0 8 1",
        "F 1", "0 0 2", "8 0 1",
    ])


# --- parsing ---------------------------------------------------------------


def test_the_loaders_own_syntax_is_what_a_sheet_may_carry():
    c = C.parse_condition_line(f"<condition>onBridge,tileAtPosition,120,80,{T},{PAL}")
    check(c.name == "onBridge" and c.type == "tileAtPosition", "name and type parse")
    check((c.x, c.y) == (120, 80), "x and y parse", f"{c.x},{c.y}")
    check(c.tile == T.upper() and c.palette == PAL, "the key parses and is upper-cased")
    check(c.evaluable, "tileAtPosition is evaluable from a grid stream")


def test_a_line_that_is_not_a_condition_is_refused():
    for bad, needle in [
        ("<tile>0,AA,0F001020,0,0,1,N", "not a <condition> line"),
        ("<condition>x,tileAtPosition", "at least 4"),
        ("<condition>,frameRange,2,1", "may not be empty"),
        ("<condition>a!b,frameRange,2,1", "may not contain"),
        ("<condition>x,noSuchType,1,2", "unknown condition type"),
    ]:
        try:
            C.parse_condition_line(bad)
            check(False, f"refused: {bad[:34]}", "it parsed")
        except C.ConditionError as exc:
            check(needle in str(exc), f"refused: {bad[:34]}", str(exc))


def test_a_chr_index_key_is_refused_with_the_reason():
    # Legal for the loader, useless against a recording: a grid stream interns
    # shapes by their 16 pattern bytes and has no index to compare against.
    try:
        C.parse_condition_line("<condition>x,tileAtPosition,8,8,1F,0F001020")
        check(False, "a CHR index key is refused", "it parsed")
    except C.ConditionError as exc:
        check("CHR index" in str(exc) and "keys shapes by data" in str(exc),
              "a CHR index key is refused, saying why a recording cannot use it",
              str(exc))


def test_tile_nearby_offsets_must_be_multiples_of_eight():
    # HdPackLoader logs this as an error on load; a sheet must not be able to
    # author a condition the emulator will reject.
    try:
        C.parse_condition_line(f"<condition>x,tileNearby,4,0,{T},{PAL}")
        check(False, "an unaligned tileNearby is refused", "it parsed")
    except C.ConditionError as exc:
        check("multiples of 8" in str(exc), "an unaligned tileNearby is refused",
              str(exc))
    C.parse_condition_line(f"<condition>x,tileNearby,-8,16,{T},{PAL}")
    check(True, "an aligned negative offset is accepted")


def test_a_frame_range_period_of_zero_is_refused():
    # The emulator computes `FrameNumber % OperandA`; zero would divide by zero
    # there, so it can never be written into a pack.
    try:
        C.parse_condition_line("<condition>x,frameRange,0,1")
        check(False, "a zero period is refused", "it parsed")
    except C.ConditionError as exc:
        check("period must be positive" in str(exc), "a zero period is refused",
              str(exc))


def test_the_types_a_recording_cannot_answer_are_named_not_guessed():
    for ctype, needle in [
        ("<condition>q,ppuMemoryCheckConstant,2000,==,01", "PPU memory"),
        ("<condition>q,ppuMemoryCheck,2000,==,2001", "PPU memory"),
        ("<condition>m,memoryCheck,0050,==,0800", "outside the retained"),
    ]:
        c = C.parse_condition_line(ctype)
        check(not c.evaluable, f"{c.type} is not evaluable")
        check(needle in (c.not_evaluable_reason or ""),
              f"{c.type} says why", c.not_evaluable_reason)
    # F12.14 (ADR-0222): the five sprite-side types and memoryCheck are
    # evaluable types now; whether a *route* can answer them is per route.
    for line in [f"<condition>s,spriteNearby,8,0,{T},{PAL}",
                 f"<condition>s,spriteAtPosition,8,0,{T},{PAL}",
                 "<condition>m,memoryCheck,0050,==,0060",
                 "<condition>p,positionCheckX,>,80",
                 "<condition>p,originPositionCheckY,==,80"]:
        c = C.parse_condition_line(line)
        check(c.evaluable, f"{c.type} is an evaluable type since F12.14",
              c.not_evaluable_reason)


# --- the recorded route ----------------------------------------------------


def test_a_grid_dump_parses_into_frames_shapes_and_palettes():
    with tempfile.TemporaryDirectory() as td:
        r = _route(td, [
            "F 0", f"K 1 {T} {PAL}", f"P 3 {PAL2}", "0 0 1", "8 8 1 3",
            "F 1", "F 1", "0 0 1",
        ])
        check(r.retained == 2, "two distinct frames", str(r.retained))
        check(r.played == 3, "the collapsed duplicate is counted as played",
              str(r.played))
        check(r.frames[0].key_at(0, 0) == (T, PAL),
              "a cell with no palette id falls back to the shape's own")
        check(r.frames[0].key_at(1, 1) == (T, PAL2),
              "a cell with a palette id uses the interned word (ADR-0159)")
        check(r.frames[0].key_at(5, 5) is None, "an undrawn cell has no key")


def test_fine_scroll_is_recovered_from_the_cell_x():
    with tempfile.TemporaryDirectory() as td:
        r = _route(td, ["F 0", f"K 1 {T} {PAL}", "11 0 1"])
        f = r.frames[0]
        check(f.fine == 3, "x & 7 is the frame's fine scroll", str(f.fine))
        check(f.rows[0][1] == 1, "(x - fine) // 8 is the column",
              str(f.rows[0][:3]))


def test_a_dump_with_no_frames_is_refused_rather_than_read_as_empty():
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "empty.txt"
        p.write_text("", encoding="utf-8")
        try:
            C.load_route(p)
            check(False, "an empty dump is refused", "it loaded")
        except C.ConditionError as exc:
            check("MESEN_SHEET_GRID_DUMP" in str(exc),
                  "an empty dump is refused, naming what produces one", str(exc))


def test_routes_are_loaded_one_at_a_time_not_all_at_once():
    # Not a style point: six Contra recordings are ~1 GB of text and ~80 000
    # retained frames, so a caller that materialises them runs out of memory on
    # the slice's own bounded input.
    import inspect
    check(inspect.isgeneratorfunction(C.iter_routes),
          "iter_routes is a generator, so a caller can drop each route")


def test_a_route_called_grid_is_named_by_its_folder():
    # Six recordings are six `<run>/grid.txt`; reported by stem they would be
    # six rows all called "grid", and the reader could not tell which run the
    # failure came from.
    with tempfile.TemporaryDirectory() as td:
        run = Path(td) / "nav-stage2"
        run.mkdir()
        r = _route(run, ["F 0", f"K 1 {T} {PAL}", "0 0 1"], "grid.txt")
        check(r.name == "nav-stage2", "a generic stem is named by its folder", r.name)
        r2 = _route(run, ["F 0", f"K 1 {T} {PAL}", "0 0 1"], "stage1-boss.txt")
        check(r2.name == "stage1-boss", "a named route keeps its own name", r2.name)


def test_discover_skips_what_is_not_a_route_and_says_so():
    with tempfile.TemporaryDirectory() as td:
        _route(td, ["F 0", f"K 1 {T} {PAL}", "0 0 1"], "good.txt")
        (Path(td) / "notes.txt").write_text("just a note\n", encoding="utf-8")
        skipped = []
        routes = list(C.iter_routes([td], on_skip=lambda p, w: skipped.append((p, w))))
        check([r.name for r in routes] == ["good"],
              "only the real route is loaded", str([r.name for r in routes]))
        check(len(skipped) == 1 and "notes.txt" in skipped[0][0],
              "the other file is skipped with its reason", str(skipped))


# --- evaluation, against HdPackConditions.h --------------------------------


def test_tile_at_position_reads_the_absolute_screen_pixel():
    # C++: PixelOffset = (y * 256) + x; ScreenTiles[PixelOffset].Tile compared
    # against PaletteColors and TileData together.
    with tempfile.TemporaryDirectory() as td:
        r = _two_frame_route(td)
        c = C.parse_condition_line(f"<condition>x,tileAtPosition,0,0,{T},{PAL}")
        v = C.evaluate(c, {(T, PAL)}, r)
        # Counted per *drawn instance* of the key, not per frame: the emulator
        # asks the condition once for every tile it draws through the rule, so
        # frame 0's two T cells both hold and frame 1's single T cell fails.
        check((v.held, v.failed) == (2, 1),
              "every drawn instance of the key asks the condition",
              f"{v.held}/{v.failed}")
        check(v.first_failure == (1, 0, 1),
              "the first failure names the frame and the cell", str(v.first_failure))
        check(v.state == "mixed", "a condition that both held and failed is mixed")


def test_the_palette_must_match_unless_ignore_palette_is_set():
    with tempfile.TemporaryDirectory() as td:
        r = _route(td, ["F 0", f"K 1 {T} {PAL}", f"P 3 {PAL2}", "0 0 1 3"])
        strict = C.parse_condition_line(f"<condition>x,tileAtPosition,0,0,{T},{PAL}")
        check(not strict.holds(r.frames[0]),
              "a different palette fails: the C++ memcmp covers palette and data")
        loose = C.parse_condition_line(
            f"<condition>x,tileAtPosition,0,0,{T},{PAL},true")
        check(loose.holds(r.frames[0]),
              "ignorePalette compares the tile data alone")


def test_a_position_off_the_screen_never_holds_rather_than_erroring():
    # C++ bounds-checks the pixel index and returns false.
    with tempfile.TemporaryDirectory() as td:
        r = _two_frame_route(td)
        c = C.parse_condition_line(f"<condition>x,tileAtPosition,300,0,{T},{PAL}")
        check(not c.holds(r.frames[0]), "an x past 255 never holds")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.state == "never held" and v.held == 0,
              "lint reports it as a run of failures, not as a crash", v.state)


def test_tile_nearby_is_relative_to_the_tile_being_drawn():
    # C++: pixelIndex = PixelOffset + (y * 256) + x, with x/y the drawn tile's.
    with tempfile.TemporaryDirectory() as td:
        r = _two_frame_route(td)
        c = C.parse_condition_line(f"<condition>x,tileNearby,8,0,{U},{PAL}")
        v = C.evaluate(c, {(T, PAL)}, r)
        # Frame 0: T at (0,0), U one cell to its right -> holds. T also at row 1
        # col 0, with nothing to its right -> fails. Frame 1: T at col 1, empty
        # to its right -> fails.
        check((v.held, v.failed) == (1, 2),
              "held only where U really is one cell to the right",
              f"{v.held}/{v.failed}")
        check(v.instances == 3, "every drawn instance of the key is an instance",
              str(v.instances))


def test_an_unintended_hit_is_a_pattern_around_a_key_the_author_did_not_mean():
    with tempfile.TemporaryDirectory() as td:
        # T at (0,0) with U to its right, and U at (0,2) with U to *its* right:
        # the pattern the author described also occurs around a key they never
        # attached the condition to.
        r = _route(td, [
            "F 0", f"K 1 {T} {PAL}", f"K 2 {U} {PAL}",
            "0 0 1", "8 0 2", "16 0 2", "24 0 2",
        ])
        c = C.parse_condition_line(f"<condition>x,tileNearby,8,0,{U},{PAL}")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.held == 1 and v.failed == 0, "it held on the key it was attached to",
              f"{v.held}/{v.failed}")
        check(v.unintended == 2,
              "and on two cells of a key it was not attached to", str(v.unintended))
        check(v.first_unintended == (0, 0, 1),
              "the first unintended hit names the frame and the cell",
              str(v.first_unintended))


def test_an_absolute_condition_reports_unintended_as_not_applicable():
    with tempfile.TemporaryDirectory() as td:
        r = _two_frame_route(td)
        c = C.parse_condition_line(f"<condition>x,tileAtPosition,0,0,{T},{PAL}")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.unintended == 0,
              "an absolute predicate does not vary by cell, so it reports none "
              "rather than one per drawn cell on screen")


def test_frame_range_is_reported_with_the_phase_the_recording_cannot_know():
    # C++: FrameNumber % OperandA >= OperandB, against the emulator's global
    # counter. A recording knows only its own retained index (ADR-0189 §4).
    with tempfile.TemporaryDirectory() as td:
        r = _two_frame_route(td)
        c = C.parse_condition_line("<condition>blink,frameRange,4,2")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.held == 0, "indexes 0 and 1 do not satisfy n % 4 >= 2")
        check(v.phase == [2],
              "offset 2 would make it hold on every retained frame", str(v.phase))
        never = C.parse_condition_line("<condition>never,frameRange,2,2")
        v2 = C.evaluate(never, {(T, PAL)}, r)
        check(v2.phase == [],
              "a threshold the period can never reach reports no offset at all",
              str(v2.phase))


def test_a_key_that_was_never_drawn_is_never_drawn_not_a_pass():
    with tempfile.TemporaryDirectory() as td:
        r = _two_frame_route(td)
        c = C.parse_condition_line(f"<condition>x,tileAtPosition,0,0,{T},{PAL}")
        v = C.evaluate(c, {("CC" * 16, PAL)}, r)
        check(v.state == "never drawn" and v.instances == 0,
              "a condition on a key the routes never drew says so", v.state)


def test_a_type_the_recording_cannot_answer_is_not_evaluable_never_a_pass():
    with tempfile.TemporaryDirectory() as td:
        r = _two_frame_route(td)
        c = C.parse_condition_line(f"<condition>s,spriteNearby,8,0,{T},{PAL}")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.state == "not evaluable" and not v.evaluable,
              "a route with no OAM stream cannot answer a sprite condition", v.state)
        check("MESEN_OAM_STREAM_DUMP" in v.reason, "and it names the missing stream", v.reason)
        check(v.held == 0 and v.failed == 0,
              "and it counts nothing, so it can never read as a pass")


# --- F12.14 (ADR-0222 option A): the OAM stream -----------------------------

S = "CC" * 16          # a sprite shape
PALS = "0F162736"      # the sprite palette word


def _oam(td, lines, name="oam.txt"):
    (Path(td) / name).write_text("\n".join(lines) + "\n", encoding="utf-8")


def _grid_with_t_at_cell(td, col, row, repeat=1, name="grid.txt"):
    """A grid dump drawing T at one cell on one retained frame, repeated."""
    lines = ["F 0", f"K 1 {T} {PAL}", f"{col * 8} {row * 8} 1"]
    for _ in range(repeat - 1):
        lines += ["F 0", f"{col * 8} {row * 8} 1"]
    return _route(td, lines, name)


def test_an_oam_dump_parses_into_frames_shapes_and_palettes():
    with tempfile.TemporaryDirectory() as td:
        _oam(td, [f"K 5 {S} {PAL}", "P 2 " + PALS,
                  "0 3 129 0 5,10,20,2 5,40,40,255",
                  "1 1 0 0 5,12,20,2"])
        stream = C.load_oam_stream(Path(td) / "oam.txt")
        check(stream.retained == 2 and stream.played == 4,
              "two retained OAM frames standing for four played",
              f"{stream.retained}/{stream.played}")
        check(stream.has_tiles, "a dump with K lines resolves its sprites")
        f0 = stream.frames[0]
        check(f0.ports == (129, 0), "the two port bytes are kept (ADR-0181)", str(f0.ports))
        check(f0.key_of(f0.entries[0]) == (S, PALS),
              "a per-entry palette id resolves through the P line (ADR-0159)")
        check(f0.key_of(f0.entries[1]) == (S, PAL),
              "kUnknownPalette falls back to the shape's own first-seen palette")
        check(list(f0.keys_covering(17, 27)) == [(S, PALS)],
              "a sprite covers the 8x8 block at its origin")
        check(list(f0.keys_covering(18, 20)) == [], "and nothing outside it")
        check(stream.frames_between(3, 1) == [stream.frames[1]],
              "the played-frame index finds the frame a span falls in")
        check(stream.frames_between(0, 4) == stream.frames,
              "a span over both frames returns both")


def test_a_pre_f12_14_oam_dump_reports_no_tile_data():
    with tempfile.TemporaryDirectory() as td:
        _grid_with_t_at_cell(td, 2, 3)
        _oam(td, ["0 1 0 0 3,10,20 4,18,20"])
        r = C.load_route(Path(td) / "grid.txt")
        check(r.oam is not None and not r.oam.has_tiles,
              "a dump of bare node indexes parses but has no tile data")
        c = C.parse_condition_line(f"<condition>s,spriteNearby,8,0,{S},{PALS}")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(not v.evaluable and "OAM stream carries no tile data" in v.reason,
              "and a sprite condition says so rather than guessing", v.reason)
        check("vocabulary" not in (c.not_evaluable_reason or ""),
              "the old 'vocabulary indexes' reason is retired")


def test_oam_dumps_beside_a_route_are_read_with_it_not_as_routes():
    with tempfile.TemporaryDirectory() as td:
        _grid_with_t_at_cell(td, 2, 3)
        _oam(td, [f"K 5 {S} {PALS}", "0 1 0 0 5,24,24,255"])
        skipped = []
        routes = list(C.iter_routes([td], on_skip=lambda p, w: skipped.append(p)))
        check(len(routes) == 1 and not skipped,
              "oam.txt is the route's OAM stream, not a second route",
              f"routes={len(routes)} skipped={skipped}")
        check(routes[0].oam is not None and routes[0].oam_aligned,
              "the stream is attached and aligned with the grid")


def test_sprite_nearby_on_a_background_key_reads_the_joined_oam_frame():
    with tempfile.TemporaryDirectory() as td:
        # T at cell (2,3) = pixel (16,24); the sprite S sits 8 px to its right.
        _grid_with_t_at_cell(td, 2, 3)
        _oam(td, [f"K 5 {S} {PAL}", "P 1 " + PALS, "0 1 0 0 5,24,24,1"])
        r = C.load_route(Path(td) / "grid.txt")
        c = C.parse_condition_line(f"<condition>n,spriteNearby,8,0,{S},{PALS}")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.state == "always held" and v.instances == 1,
              "the sprite 8 px right of the tile satisfies spriteNearby 8,0",
              f"{v.state} held={v.held} failed={v.failed}")
        # HdPackSpriteNearbyCondition: PaletteColors must match too...
        c = C.parse_condition_line(f"<condition>n,spriteNearby,8,0,{S},{PAL}")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.state == "never held", "a palette mismatch fails", v.state)
        check(v.first_failure == (0, 3, 2), "and the failing cell is named", str(v.first_failure))
        # ...unless ignorePalette (field 7) is set.
        c = C.parse_condition_line(f"<condition>n,spriteNearby,8,0,{S},{PAL},Y")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.state == "always held", "ignorePalette skips the palette", v.state)
        c = C.parse_condition_line(f"<condition>n,spriteNearby,-8,0,{S},{PALS}")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.state == "never held", "the offset is signed; nothing is on the left", v.state)


def test_sprite_nearby_on_a_sprite_key_needs_no_join():
    with tempfile.TemporaryDirectory() as td:
        # The grid stands for five played frames, the OAM stream for one: the
        # streams do not align, but a sprite key is evaluated in its own frame.
        _grid_with_t_at_cell(td, 2, 3, repeat=5)
        _oam(td, [f"K 5 {S} {PALS}", f"K 6 {U} {PAL}",
                  "0 1 0 0 5,40,40,255 6,48,40,255"])
        r = C.load_route(Path(td) / "grid.txt")
        check(not r.oam_aligned, "the fixture's streams disagree on played frames")
        c = C.parse_condition_line(f"<condition>n,spriteNearby,8,0,{U},{PAL}")
        v = C.evaluate(c, {(S, PALS)}, r)
        check(v.evaluable and v.state == "always held" and v.instances == 1,
              "a sprite key's neighbour is read off the same OAM frame",
              f"{v.state} {v.reason}")
        c = C.parse_condition_line(f"<condition>n,spriteNearby,8,0,{S},{PALS}")
        v = C.evaluate(c, {(U, PAL)}, r)
        check(v.state == "never held" and v.first_failure_sprite == (0, 48, 40),
              "a failing sprite instance is named by its OAM frame and origin",
              f"{v.state} {v.first_failure_sprite}")


def test_a_background_key_is_not_evaluable_when_the_streams_do_not_align():
    with tempfile.TemporaryDirectory() as td:
        _grid_with_t_at_cell(td, 2, 3, repeat=5)
        _oam(td, [f"K 5 {S} {PALS}", "0 1 0 0 5,24,24,255"])
        r = C.load_route(Path(td) / "grid.txt")
        c = C.parse_condition_line(f"<condition>n,spriteNearby,8,0,{S},{PALS}")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(not v.evaluable and "played-frame counts (5 vs 1)" in v.reason,
              "a background key's sprite condition refuses a join it cannot make",
              v.reason)
        check(v.held == 0 and v.failed == 0, "and counts nothing")


def test_sprite_at_position_reads_the_absolute_pixel():
    with tempfile.TemporaryDirectory() as td:
        _grid_with_t_at_cell(td, 2, 3)
        _oam(td, [f"K 5 {S} {PALS}", "0 1 0 0 5,100,120,255"])
        r = C.load_route(Path(td) / "grid.txt")
        c = C.parse_condition_line(f"<condition>a,spriteAtPosition,107,127,{S},{PALS}")
        check(C.evaluate(c, {(T, PAL)}, r).state == "always held",
              "the last pixel of the sprite's block is covered")
        c = C.parse_condition_line(f"<condition>a,spriteAtPosition,108,120,{S},{PALS}")
        check(C.evaluate(c, {(T, PAL)}, r).state == "never held",
              "one pixel past it is not")


def test_position_checks_use_the_loaders_decimal_operand_and_every_pixel():
    c = C.parse_condition_line("<condition>p,positionCheckX,>=,32")
    check(c.operand_a == 32 and c.operator == ">=",
          "positionCheck reads its operand with std::stoi, i.e. decimal")
    with tempfile.TemporaryDirectory() as td:
        _grid_with_t_at_cell(td, 4, 5)   # pixel (32,40)
        _oam(td, [f"K 5 {S} {PALS}", "0 1 0 0 5,32,40,255 5,30,40,255"])
        r = C.load_route(Path(td) / "grid.txt")
        v = C.evaluate(c, {(S, PALS)}, r)
        check(v.instances == 2 and v.held == 1 and v.failed == 1,
              "the sprite at x=32 holds on all 8 pixels; the one at x=30 is split, so it fails",
              f"held={v.held} failed={v.failed}")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.state == "always held", "a background cell at x=32 holds too", v.state)
        c = C.parse_condition_line("<condition>o,originPositionCheckY,==,40")
        v = C.evaluate(c, {(S, PALS)}, r)
        check(v.state == "always held" and v.instances == 2,
              "originPositionCheck reads the tile origin, mirrored or not", v.state)
        c = C.parse_condition_line("<condition>o,positionCheckY,<,40")
        v = C.evaluate(c, {(S, PALS)}, r)
        check(v.state == "never held", "y < 40 fails for a sprite whose origin is 40", v.state)
    for bad in ["<condition>p,positionCheckX,>=,0x20",
                "<condition>p,positionCheckX,>=,32,1",
                "<condition>p,positionCheckX,=>,32"]:
        try:
            C.parse_condition_line(bad)
            check(False, f"{bad!r} is refused")
        except C.ConditionError:
            check(True, f"{bad!r} is refused")


def test_memory_check_compares_two_watched_bytes_masked():
    c = C.parse_condition_line("<condition>m,memoryCheck,0030,==,0031")
    check(c.operand_a == 0x30 and c.operand_b == 0x31 and c.evaluable,
          "memoryCheck names two addresses, both hex")
    with tempfile.TemporaryDirectory() as td:
        r = _memory_route(td, [{0x30: 0x12, 0x31: 0x12},
                               {0x30: 0x12, 0x31: 0x13},
                               {0x30: 0xF1, 0x31: 0x01}])
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.held == 1 and v.failed == 2 and v.state == "mixed",
              "equal on one frame of three", f"held={v.held} failed={v.failed}")
        c = C.parse_condition_line("<condition>m,memoryCheck,0030,==,0031,0F")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.held == 2 and v.failed == 1,
              "the mask is applied to both watched bytes (F1 & 0F == 01 & 0F)",
              f"held={v.held} failed={v.failed}")
        c = C.parse_condition_line("<condition>m,memoryCheck,0031,>,0030")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.held == 1, "the operator is the loader's, operand order kept", str(v.held))
    with tempfile.TemporaryDirectory() as td:
        r = _two_frame_route(td)
        v = C.evaluate(c, {(T, PAL)}, r)
        check(not v.evaluable and v.reason == C.NO_MEMORY_STREAM,
              "a route without an M plane cannot answer memoryCheck either")
    try:
        C.parse_condition_line("<condition>m,memoryCheck,0030,==,10000")
        check(False, "a second address past FFFF is refused")
    except C.ConditionError:
        check(True, "a second address past FFFF is refused")


# --- the sheet side --------------------------------------------------------


def test_a_sheet_condition_must_be_marked_authored():
    line = f"<condition>onBridge,tileAtPosition,0,0,{T},{PAL}"
    ok = C.load_sheet_conditions({"conditions": [
        {"name": "onBridge", "authored": True, "line": line}]}, "s.json")
    check(list(ok) == ["onBridge"], "a marked condition loads")
    try:
        C.load_sheet_conditions({"conditions": [{"name": "x", "line": line}]}, "s.json")
        check(False, "an unmarked condition is refused", "it loaded")
    except C.ConditionError as exc:
        check("authored: true" in str(exc),
              "an unmarked condition is refused: the toolchain never generates "
              "one, so an unmarked one is a mistake", str(exc))


def test_a_sheet_condition_whose_name_disagrees_with_its_line_is_refused():
    line = f"<condition>onBridge,tileAtPosition,0,0,{T},{PAL}"
    try:
        C.load_sheet_conditions({"conditions": [
            {"name": "elsewhere", "authored": True, "line": line}]}, "s.json")
        check(False, "a disagreeing name is refused", "it loaded")
    except C.ConditionError as exc:
        check("but the line names" in str(exc), "a disagreeing name is refused",
              str(exc))


def test_an_authored_condition_always_emits_the_bare_twin_after_it():
    # ADR-0189 §3 / #256: without the unconditional twin, a frame where the
    # condition does not hold falls through to the ROM's own art.
    rows = C.authored_variants("onBridge", ["1", "N"])
    check([r[0] for r in rows] == ["[onBridge]", ""],
          "the conditional rule comes first and the bare twin last, which is "
          "the order GetMatchingTile walks", str([r[0] for r in rows]))
    check(rows[0][1] == rows[1][1] and rows[0][1] is not rows[1][1],
          "the twin carries the same trailing fields, as its own list")


def test_two_sheets_may_share_a_condition_but_not_two_definitions_of_it():
    line = f"<condition>onBridge,tileAtPosition,0,0,{T},{PAL}"
    other = f"<condition>onBridge,tileAtPosition,8,0,{T},{PAL}"
    same = {"conditions": [{"name": "onBridge", "authored": True, "line": line}]}
    check(C.definition_lines([same, dict(same)]) == [line],
          "the same definition in two sheets is emitted once")
    try:
        C.definition_lines([same, {"conditions": [
            {"name": "onBridge", "authored": True, "line": other}]}])
        check(False, "two different definitions under one name are refused",
              "it returned")
    except C.ConditionError as exc:
        check("two different definitions" in str(exc),
              "two different definitions under one name are refused", str(exc))


# --- F12.6b: the memory plane (ADR-0197 §3) --------------------------------


def _ram_line(pairs):
    """An `M` line body with `pairs` = {address: value}, zero elsewhere."""
    ram = bytearray(C.RAM_WINDOW)
    for addr, value in pairs.items():
        ram[addr] = value
    return ram.hex().upper()


def _memory_route(td, per_frame, name="route.txt"):
    """One retained frame per entry of `per_frame` (a dict of address->value),
    each drawing shape T at cell (0,0)."""
    out = ["F 0", f"K 1 {T} {PAL}", f"M {_ram_line(per_frame[0])}", "0 0 1"]
    for i, pairs in enumerate(per_frame[1:], start=1):
        out += [f"F {i}", f"M {_ram_line(pairs)}", "0 0 1"]
    return _route(td, out, name)


def test_a_memory_check_constant_is_parsed_in_the_loaders_hex():
    c = C.parse_condition_line("<condition>stage5,memoryCheckConstant,30,==,04")
    check(c.operand_a == 0x30, "the address is hex, so `30` is $0030", hex(c.operand_a))
    check(c.operand_b == 0x04 and c.operator == "==", "value and operator parse")
    check(c.mask == 0xFF, "the mask defaults to FF", hex(c.mask))
    check(c.evaluable, "an in-window memoryCheckConstant is evaluable")
    d = C.parse_condition_line("<condition>half,memoryCheckConstant,0040,>=,0F,0F")
    check(d.mask == 0x0F and d.operator == ">=", "an explicit mask parses")


def test_a_memory_check_constant_out_of_range_is_refused_like_the_loader():
    for line, needle in [
        ("<condition>x,memoryCheckConstant,30,==,0100", "00-FF"),
        ("<condition>x,memoryCheckConstant,30,~,01", "not an operator"),
        ("<condition>x,memoryCheckConstant,30,==", "at least 5 fields"),
    ]:
        try:
            C.parse_condition_line(line)
            check(False, f"refused: {line}", "it was accepted")
        except C.ConditionError as exc:
            check(needle in str(exc), f"refused: {line}", str(exc))


def test_an_address_outside_the_window_is_not_evaluable_never_a_pass():
    # ADR-0197 §3 fixes the window at $0000-$07FF; WRAM, PRG and the mapper
    # registers are outside it and the report must say so rather than guess.
    c = C.parse_condition_line("<condition>wram,memoryCheckConstant,6000,==,01")
    check(not c.evaluable, "an address above $07FF is not evaluable")
    check("outside the retained" in (c.not_evaluable_reason or ""),
          "it says the window is the reason", c.not_evaluable_reason)


def test_the_m_line_is_read_into_the_frame_it_opens():
    with tempfile.TemporaryDirectory() as td:
        r = _memory_route(td, [{0x30: 0x00, 0x64: 0x00}, {0x30: 0x04, 0x64: 0x21}])
        check(r.has_ram, "the route carries a memory plane")
        check(r.frames[0].ram[0x30] == 0x00 and r.frames[1].ram[0x30] == 0x04,
              "each frame keeps its own window",
              f"{r.frames[0].ram[0x30]},{r.frames[1].ram[0x30]}")
        check(len(r.frames[0].ram) == C.RAM_WINDOW,
              "the window is 2 KB wide", str(len(r.frames[0].ram)))


def test_a_recording_made_before_f12_6b_is_not_evaluable_never_a_pass():
    with tempfile.TemporaryDirectory() as td:
        r = _two_frame_route(td)   # no M lines: a pre-F12.6b dump
        check(not r.has_ram, "an old dump carries no memory plane")
        c = C.parse_condition_line("<condition>stage5,memoryCheckConstant,30,==,04")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(not v.evaluable and v.state == "not evaluable",
              "the verdict is not evaluable", v.state)
        check(v.held == 0 and v.failed == 0,
              "not evaluable is never counted as a pass or a failure")
        check("before F12.6b" in v.reason, "it says why", v.reason)


def test_the_comparison_mirrors_the_cpp_operator_by_operator():
    # HdPackMemoryCheckConstantCondition::InternalCheckCondition.
    with tempfile.TemporaryDirectory() as td:
        r = _memory_route(td, [{0x30: 0x10}])
        frame = r.frames[0]
        for op, expected in [("==", False), ("!=", True), (">", False),
                             ("<", True), ("<=", True), (">=", False)]:
            c = C.parse_condition_line(f"<condition>o,memoryCheckConstant,30,{op},20")
            check(c.holds(frame) is expected,
                  f"$10 {op} $20 is {expected}", str(c.holds(frame)))


def test_the_mask_is_applied_to_the_read_byte_and_not_to_the_constant():
    # The C++ masks `a` only: `uint8_t a = ...& Mask; uint8_t b = OperandB;`.
    with tempfile.TemporaryDirectory() as td:
        r = _memory_route(td, [{0x40: 0xF3}])
        frame = r.frames[0]
        c = C.parse_condition_line("<condition>m,memoryCheckConstant,40,==,03,0F")
        check(c.holds(frame), "$F3 & $0F == $03 holds")
        d = C.parse_condition_line("<condition>m,memoryCheckConstant,40,==,F3,0F")
        check(not d.holds(frame),
              "the constant is not masked, so $F3 & $0F == $F3 fails")


def test_a_memory_condition_is_counted_per_drawn_instance_of_its_key():
    with tempfile.TemporaryDirectory() as td:
        r = _memory_route(td, [{0x30: 0x04}, {0x30: 0x00}])
        c = C.parse_condition_line("<condition>stage5,memoryCheckConstant,30,==,04")
        v = C.evaluate(c, {(T, PAL)}, r)
        check(v.state == "mixed", "one frame holds and one does not", v.state)
        check(v.held == 1 and v.failed == 1, "one each",
              f"{v.held}/{v.failed}")
        check(v.first_failure == (1, 0, 0),
              "the failure names its frame and cell", str(v.first_failure))


def test_a_short_or_broken_m_line_is_dropped_not_half_read():
    with tempfile.TemporaryDirectory() as td:
        r = _route(td, ["F 0", f"K 1 {T} {PAL}", "M DEADBEEF", "0 0 1"])
        check(r.frames[0].ram is None,
              "a short window is dropped rather than indexed into")
        check(not r.has_ram, "and the route reports no memory plane")


def main():
    tests = [
        test_the_loaders_own_syntax_is_what_a_sheet_may_carry,
        test_a_line_that_is_not_a_condition_is_refused,
        test_a_chr_index_key_is_refused_with_the_reason,
        test_tile_nearby_offsets_must_be_multiples_of_eight,
        test_a_frame_range_period_of_zero_is_refused,
        test_the_types_a_recording_cannot_answer_are_named_not_guessed,
        test_a_grid_dump_parses_into_frames_shapes_and_palettes,
        test_fine_scroll_is_recovered_from_the_cell_x,
        test_a_dump_with_no_frames_is_refused_rather_than_read_as_empty,
        test_routes_are_loaded_one_at_a_time_not_all_at_once,
        test_a_route_called_grid_is_named_by_its_folder,
        test_discover_skips_what_is_not_a_route_and_says_so,
        test_tile_at_position_reads_the_absolute_screen_pixel,
        test_the_palette_must_match_unless_ignore_palette_is_set,
        test_a_position_off_the_screen_never_holds_rather_than_erroring,
        test_tile_nearby_is_relative_to_the_tile_being_drawn,
        test_an_unintended_hit_is_a_pattern_around_a_key_the_author_did_not_mean,
        test_an_absolute_condition_reports_unintended_as_not_applicable,
        test_frame_range_is_reported_with_the_phase_the_recording_cannot_know,
        test_a_key_that_was_never_drawn_is_never_drawn_not_a_pass,
        test_a_type_the_recording_cannot_answer_is_not_evaluable_never_a_pass,
        test_a_sheet_condition_must_be_marked_authored,
        test_a_sheet_condition_whose_name_disagrees_with_its_line_is_refused,
        test_an_authored_condition_always_emits_the_bare_twin_after_it,
        test_two_sheets_may_share_a_condition_but_not_two_definitions_of_it,
        test_a_memory_check_constant_is_parsed_in_the_loaders_hex,
        test_a_memory_check_constant_out_of_range_is_refused_like_the_loader,
        test_an_address_outside_the_window_is_not_evaluable_never_a_pass,
        test_the_m_line_is_read_into_the_frame_it_opens,
        test_a_recording_made_before_f12_6b_is_not_evaluable_never_a_pass,
        test_the_comparison_mirrors_the_cpp_operator_by_operator,
        test_the_mask_is_applied_to_the_read_byte_and_not_to_the_constant,
        test_a_memory_condition_is_counted_per_drawn_instance_of_its_key,
        test_a_short_or_broken_m_line_is_dropped_not_half_read,
        test_an_oam_dump_parses_into_frames_shapes_and_palettes,
        test_a_pre_f12_14_oam_dump_reports_no_tile_data,
        test_oam_dumps_beside_a_route_are_read_with_it_not_as_routes,
        test_sprite_nearby_on_a_background_key_reads_the_joined_oam_frame,
        test_sprite_nearby_on_a_sprite_key_needs_no_join,
        test_a_background_key_is_not_evaluable_when_the_streams_do_not_align,
        test_sprite_at_position_reads_the_absolute_pixel,
        test_position_checks_use_the_loaders_decimal_operand_and_every_pixel,
        test_memory_check_compares_two_watched_bytes_masked,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
