"""Hand-authored HD pack conditions: the model, and their evaluation against a
recorded route (F12.6a, ADR-0197).

ADR-0189 §4 refuses to **emit** `frameRange`, `tileAtPosition` and
`memoryCheckConstant`, each for a reason that still holds. ADR-0197 narrows
that refusal to emission and admits a human's own condition into a MEP sheet,
on one condition of its own: the recorded routes say whether the evidence
agrees. This module is both halves — the sheet-side model `mep_build.py`
serializes, and the evaluator `mep_lint.py --routes` reports with.

Everything here mirrors `Core/NES/HdPacks/HdPackConditions.h` deliberately and
literally, because a report that disagrees with the emulator is worse than no
report. Where the C++ is reproduced, the C++ line is quoted in a comment.

**What a recording can and cannot answer.** The evaluator reads the grid stream
the recorder writes under `MESEN_SHEET_GRID_DUMP` — per retained frame, the
background tile grid with each cell's shape and palette. That is enough for
`frameRange`, `tileAtPosition` and `tileNearby`. It is not enough for:

* `spriteNearby` / `spriteAtPosition` — the sprite stream is dumped separately
  (`MESEN_OAM_STREAM_DUMP`) and carries **vocabulary indexes**, not tile data,
  so a condition's 32-hex key cannot be matched against it. Resolving that
  needs the dump format to change, which ADR-0197's Consequences assign to
  F12.6b;
* `memoryCheckConstant` / `memoryCheck` — no memory stream is retained at all
  until F12.6b (ADR-0197 §3);
* `positionCheckX/Y`, `originPositionCheckX/Y` — these read the sprite's own
  screen position, which is in the OAM stream, not the grid.

Each of those reports `not evaluable` with the reason, and **`not evaluable` is
never a pass**. ADR-0197 says the limits are to be stated by lint rather than
hidden; this is where that is kept.

The phase problem, stated once. `frameRange` is `FrameNumber % A >= B` against
the **emulator's global frame counter**, and a recording knows only its own
retained-frame index — that is exactly ADR-0189 §4's "the recording knows a
period, not a phase". So `frameRange` is reported with the phase offset that
would make it hold, per route, rather than as a bare pass or fail.
"""

import re
from pathlib import Path

# The NES background grid the recorder retains, and the screen it covers.
ROWS, COLS, CELL = 30, 32, 8
SCREEN_W, SCREEN_H = COLS * CELL, ROWS * CELL
EMPTY = -1
# MesenSheets::kUnknownPalette — "the id space ran out", i.e. no evidence.
UNKNOWN_PALETTE = 0xFF

_HEX32 = re.compile(r"^[0-9A-Fa-f]{32}$")

# Types this module can decide from a grid stream, and the reason for each one
# it cannot. Anything absent from both is an unknown type and lint says so.
EVALUABLE = ("frameRange", "tileAtPosition", "tileNearby")
NOT_EVALUABLE = {
    "spriteNearby":
        "the recorded sprite stream carries vocabulary indexes, not tile data, "
        "so a 32-hex key cannot be matched against it (ADR-0197, F12.6b)",
    "spriteAtPosition":
        "the recorded sprite stream carries vocabulary indexes, not tile data, "
        "so a 32-hex key cannot be matched against it (ADR-0197, F12.6b)",
    "memoryCheck":
        "no memory stream is retained in the recording (ADR-0197 §3, F12.6b)",
    "memoryCheckConstant":
        "no memory stream is retained in the recording (ADR-0197 §3, F12.6b)",
    "ppuMemoryCheck":
        "PPU memory is outside the retained window (ADR-0197 §3)",
    "ppuMemoryCheckConstant":
        "PPU memory is outside the retained window (ADR-0197 §3)",
    "positionCheckX":
        "the sprite's own screen position is in the sprite stream, not the grid",
    "positionCheckY":
        "the sprite's own screen position is in the sprite stream, not the grid",
    "originPositionCheckX":
        "the sprite's own screen position is in the sprite stream, not the grid",
    "originPositionCheckY":
        "the sprite's own screen position is in the sprite stream, not the grid",
}
KNOWN_TYPES = tuple(EVALUABLE) + tuple(NOT_EVALUABLE)


class ConditionError(ValueError):
    """A `<condition>` line a sheet cannot carry."""


class Condition:
    """One parsed `<condition>` line, in the loader's own field order."""

    __slots__ = ("name", "type", "line", "x", "y", "tile", "palette",
                 "ignore_palette", "operand_a", "operand_b")

    def __init__(self, name, ctype, line):
        self.name = name
        self.type = ctype
        self.line = line
        self.x = self.y = 0
        self.tile = ""
        self.palette = ""
        self.ignore_palette = False
        self.operand_a = self.operand_b = 0

    @property
    def evaluable(self):
        return self.type in EVALUABLE

    @property
    def not_evaluable_reason(self):
        return NOT_EVALUABLE.get(self.type)

    def __repr__(self):
        return f"<Condition {self.name} {self.type}>"

    # --- evaluation, mirroring HdPackConditions.h ---------------------------

    def holds(self, frame, cell_col=None, cell_row=None):
        """True when this condition would hold on `frame`.

        `cell_col`/`cell_row` are the grid position of the tile being drawn,
        which only `tileNearby` reads — it is relative to the tile, and the
        C++ computes `PixelOffset + (y * 256) + x` from the drawn tile's own
        pixel coordinates.
        """
        if self.type == "frameRange":
            # HdPackFrameRangeCondition: FrameNumber % OperandA >= OperandB.
            if self.operand_a == 0:
                return False
            return frame.index % self.operand_a >= self.operand_b
        if self.type == "tileAtPosition":
            # PixelOffset = (y * 256) + x, then ScreenTiles[PixelOffset].Tile.
            return self._tile_matches(frame, self.x, self.y)
        if self.type == "tileNearby":
            if cell_col is None or cell_row is None:
                return False
            px = cell_col * CELL + frame.fine + self.x
            py = cell_row * CELL + self.y
            return self._tile_matches(frame, px, py)
        raise ConditionError(
            f"{self.name}: {self.type} cannot be evaluated from a grid stream")

    def _tile_matches(self, frame, px, py):
        """The background tile covering screen pixel (px, py) matches the key.

        The C++ bounds-checks the pixel index against the screen and returns
        false outside it, which is the behaviour reproduced here rather than an
        error: a condition that points off-screen simply never holds, and lint
        reports that as a run of failures rather than as a crash.
        """
        if not (0 <= px < SCREEN_W and 0 <= py < SCREEN_H):
            return False
        col = (px - frame.fine) // CELL
        row = py // CELL
        if not (0 <= col < COLS and 0 <= row < ROWS):
            return False
        shape = frame.rows[row][col]
        if shape == EMPTY:
            return False
        key = frame.key_at(row, col)
        if key is None:
            return False
        data, pal = key
        if data != self.tile:
            return False
        # IgnorePalette skips the palette word; otherwise the C++ memcmp covers
        # PaletteColors and TileData together, i.e. both must match.
        return self.ignore_palette or pal == self.palette


def parse_condition_line(line):
    """Parse one `<condition>` line into a `Condition`, or raise.

    The accepted syntax is the loader's (`HdPackLoader::ProcessConditionTag`),
    which is what ADR-0197 §1 admits into a sheet — not a second dialect. Only
    what this module has to understand is validated here; lint's existing
    `<condition>` checks still run over the built `hires.txt`.
    """
    raw = str(line or "").strip()
    if not raw.startswith("<condition>"):
        raise ConditionError(f"not a <condition> line: {raw[:60]!r}")
    body = raw[len("<condition>"):]
    tokens = [t.strip() for t in body.split(",")]
    if len(tokens) < 4:
        raise ConditionError(
            f"{raw[:60]!r}: a condition tag needs at least 4 comma-separated fields")
    name, ctype = tokens[0], tokens[1]
    if not name:
        raise ConditionError("condition name may not be empty")
    if "!" in name:
        # HdPackLoader refuses it: `!name` is how the loader spells the
        # inverted twin it generates itself.
        raise ConditionError(f"{name!r}: a condition name may not contain '!'")
    if ctype not in KNOWN_TYPES:
        raise ConditionError(
            f"{name}: unknown condition type {ctype!r}; the loader knows "
            + ", ".join(sorted(KNOWN_TYPES)))

    cond = Condition(name, ctype, raw)
    if ctype in ("tileAtPosition", "tileNearby", "spriteNearby", "spriteAtPosition"):
        if len(tokens) < 6:
            raise ConditionError(f"{name}: {ctype} needs at least 6 fields")
        try:
            cond.x, cond.y = int(tokens[2]), int(tokens[3])
        except ValueError:
            raise ConditionError(f"{name}: x and y must be integers") from None
        token = tokens[4]
        if not _HEX32.match(token):
            # A CHR index key is legal for the loader but cannot be resolved
            # against a grid stream, which interns shapes by their 16 pattern
            # bytes. Refusing it here is better than reporting it as failing.
            raise ConditionError(
                f"{name}: {ctype} must name the tile by its 32-hex data, not by "
                f"a CHR index ({token!r}) — a recording keys shapes by data")
        cond.tile = token.upper()
        cond.palette = tokens[5].strip().upper()
        if len(tokens) >= 7:
            cond.ignore_palette = tokens[6].strip().lower() in ("true", "1", "yes")
        if ctype == "tileNearby" and (cond.x % CELL or cond.y % CELL):
            # HdPackLoader logs this one as an error on load; a sheet must not
            # be able to author a condition the loader will reject.
            raise ConditionError(
                f"{name}: tileNearby offsets must be multiples of 8 "
                f"(got {cond.x},{cond.y})")
    elif ctype == "frameRange":
        if len(tokens) < 4:
            raise ConditionError(f"{name}: frameRange needs 4 fields")
        try:
            cond.operand_a, cond.operand_b = int(tokens[2]), int(tokens[3])
        except ValueError:
            raise ConditionError(
                f"{name}: frameRange operands must be integers") from None
        if cond.operand_a <= 0:
            raise ConditionError(
                f"{name}: frameRange period must be positive (got {cond.operand_a}); "
                "the emulator computes FrameNumber % period")
        if not 0 <= cond.operand_b <= 0xFFFF:
            raise ConditionError(f"{name}: frameRange threshold out of range")
    return cond


def load_sheet_conditions(doc, where=""):
    """The authored conditions of one sheet sidecar, by name.

    ADR-0197 §1: a sheet may carry a named condition in the loader's own
    syntax, marked `authored: true`. Nothing in the toolchain generates one, so
    an entry missing that marker is refused rather than assumed — the marker is
    how a reader tells a human's decision from a derived one (ADR-0183 §3).
    """
    out = {}
    entries = doc.get("conditions") or []
    if not isinstance(entries, list):
        raise ConditionError(f"{where}: `conditions` must be a list")
    for entry in entries:
        if not isinstance(entry, dict):
            raise ConditionError(f"{where}: each condition must be an object")
        if entry.get("authored") is not True:
            raise ConditionError(
                f"{where}: condition {entry.get('name')!r} is missing "
                "`authored: true`; the toolchain never generates a condition, "
                "so an unmarked one is a mistake rather than a default")
        cond = parse_condition_line(entry.get("line"))
        declared = entry.get("name")
        if declared is not None and declared != cond.name:
            raise ConditionError(
                f"{where}: condition `name` is {declared!r} but the line names "
                f"{cond.name!r}")
        if cond.name in out:
            raise ConditionError(f"{where}: two conditions named {cond.name!r}")
        out[cond.name] = cond
    return out


# --- the recorded route -----------------------------------------------------


class GridFrame:
    """One retained frame of the recorder's background grid."""

    __slots__ = ("index", "repeat", "fine", "rows", "pals", "_shapes")

    def __init__(self, index, shapes):
        self.index = index
        self.repeat = 1
        self.fine = 0
        self.rows = [[EMPTY] * COLS for _ in range(ROWS)]
        self.pals = [[UNKNOWN_PALETTE] * COLS for _ in range(ROWS)]
        self._shapes = shapes

    def key_at(self, row, col):
        """(tileData, palette) of the cell, or None when it was not drawn.

        The shape id wildcards the palette on purpose, so the per-cell palette
        plane wins where it has one and the shape's own first-seen palette is
        the fallback — the ADR-0159 amendment's rule, and the same one
        `artist_map.py` applies.
        """
        shape = self.rows[row][col]
        if shape == EMPTY:
            return None
        entry = self._shapes.get(shape)
        if entry is None:
            return None
        data, shape_pal = entry
        pal = self.pals[row][col]
        if pal == UNKNOWN_PALETTE:
            return data, shape_pal
        return data, self._shapes.palette(pal, shape_pal)

    def drawn_cells(self):
        for row in range(ROWS):
            line = self.rows[row]
            for col in range(COLS):
                if line[col] != EMPTY:
                    yield row, col


class _Shapes(dict):
    """Shape id -> (tileData, palette), plus the interned palette words."""

    def __init__(self):
        super().__init__()
        self.palettes = {}

    def palette(self, pal_id, fallback):
        return self.palettes.get(pal_id, fallback)


class Route:
    """A recorded route: its grid frames, and where they came from."""

    def __init__(self, name, frames, shapes, path=None):
        self.name = name
        self.frames = frames
        self.shapes = shapes
        self.path = path

    @property
    def retained(self):
        return len(self.frames)

    @property
    def played(self):
        """Emulated frames the retained ones stand for, counting the repeats
        the recorder collapsed. A route that held one screen for a second
        retains one frame and played sixty; a report that quoted only the
        former would overstate how much was looked at."""
        return sum(f.repeat for f in self.frames)


def parse_grid_dump(path):
    """Read a `MESEN_SHEET_GRID_DUMP` file into frames and shapes.

    Four line kinds (`HdPackBuilder::WriteGridDump`): `F <n>` opens a frame and
    repeats once per collapsed duplicate, `K <id> <32 hex tile data> <8 hex
    palette>` interns a shape the first time it is drawn, `P <id> <8 hex
    palette>` interns a palette word, and `<x> <y> <shape> [<palette id>]`
    places a cell. `x` is `col * 8 + fineX`, so `x & 7` recovers the frame's
    fine scroll and `(x - fineX) // 8` its column.

    The fourth cell field and the `P` lines are F9.24's palette plane. A dump
    written by an older build has neither; it parses unchanged and every cell
    falls back to the shape's own first-seen palette.
    """
    path = Path(path)
    frames = []
    shapes = _Shapes()
    cur = None
    last = None
    with path.open("r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if not line.strip():
                continue
            head = line[0]
            if head == "F":
                n = int(line[2:])
                if last is not None and n == last:
                    cur.repeat += 1
                    continue
                cur = GridFrame(n, shapes)
                last = n
                frames.append(cur)
            elif head == "K":
                parts = line.split()
                shapes[int(parts[1])] = (parts[2].upper(), parts[3].upper())
            elif head == "P":
                parts = line.split()
                shapes.palettes[int(parts[1])] = parts[2].upper()
            elif cur is not None:
                parts = line.split()
                x = int(parts[0])
                fine = x & 7
                cur.fine = fine
                col = (x - fine) // CELL
                row = int(parts[1]) // CELL
                if 0 <= row < ROWS and 0 <= col < COLS:
                    cur.rows[row][col] = int(parts[2])
                    if len(parts) > 3:
                        cur.pals[row][col] = int(parts[3])
    return frames, shapes


def load_route(path, name=None):
    """A `Route` from a grid dump file, or raise `ConditionError`."""
    path = Path(path)
    frames, shapes = parse_grid_dump(path)
    if not frames:
        raise ConditionError(
            f"{path}: no grid frames — was MESEN_SHEET_GRID_DUMP set on a run "
            "that reached gameplay?")
    return Route(name or route_name(path), frames, shapes, path)


def route_name(path):
    """The name a route is reported under.

    `MESEN_SHEET_GRID_DUMP` is conventionally pointed at `<run>/grid.txt`, so a
    set of recordings is six files all called `grid` sitting in six differently
    named folders. Naming them by stem alone would report six identical rows
    and the reader could not tell which run failed, which is the one thing the
    report exists to say — so a generic stem is named by its folder instead.
    """
    path = Path(path)
    if path.stem in ("grid", "dump") and path.parent.name:
        return path.parent.name
    return path.stem


def iter_routes(paths, on_skip=None):
    """Yield one loaded route at a time, from the given files or directories.

    A directory is searched for `*.txt` and `*.grid` one level deep; anything
    that does not parse as a grid stream is reported to `on_skip(path, reason)`
    and passed over, so a folder holding routes beside other text files works.

    Deliberately a generator, and callers must keep it that way. A real Contra
    route is ~190 MB of text standing for ~18 000 retained frames, and six of
    them at once is an ordinary `--routes` argument; decoded, each frame is a
    30x32 grid of Python ints. Materialising the whole set costs gigabytes,
    while evaluating every condition against one route and then dropping it
    costs one. The report is therefore written route-major and printed at the
    end, rather than condition-major and streamed.
    """
    for raw in paths:
        p = Path(raw)
        if p.is_dir():
            candidates = sorted(q for q in p.iterdir()
                                if q.is_file() and q.suffix in (".txt", ".grid"))
        elif p.is_file():
            candidates = [p]
        else:
            if on_skip:
                on_skip(str(p), "does not exist")
            continue
        for q in candidates:
            try:
                yield load_route(q)
            except (ConditionError, ValueError, IndexError) as exc:
                if on_skip:
                    on_skip(str(q), str(exc))


# --- the report -------------------------------------------------------------


class ConditionVerdict:
    """What one condition did on one route."""

    def __init__(self, condition, route):
        self.condition = condition
        self.route = route
        self.evaluable = True
        self.reason = ""
        self.held = 0
        self.failed = 0
        self.first_failure = None       # (frame index, row, col) or None
        self.unintended = 0
        self.first_unintended = None    # (frame index, row, col) or None
        self.instances = 0              # drawn instances of the conditioned keys
        self.phase = None               # frameRange only: offsets that hold

    @property
    def state(self):
        if not self.evaluable:
            return "not evaluable"
        if self.instances == 0:
            return "never drawn"
        if self.failed == 0:
            return "always held"
        if self.held == 0:
            return "never held"
        return "mixed"


def evaluate(condition, keys, route):
    """Run one condition over one route against the keys it is attached to.

    `keys` is the set of `(tileData, palette)` the author conditioned. It is
    what separates a hit from an **unintended** hit: a `tileNearby` pattern is
    described relative to the tile being drawn, so the same pattern can occur
    around a tile the author never meant, and that is the case ADR-0197 §2 asks
    for by name.
    """
    v = ConditionVerdict(condition, route)
    if not condition.evaluable:
        v.evaluable = False
        v.reason = condition.not_evaluable_reason or "unknown condition type"
        return v

    relative = condition.type == "tileNearby"
    for frame in route.frames:
        if relative or keys:
            for row, col in frame.drawn_cells():
                key = frame.key_at(row, col)
                if key is None:
                    continue
                mine = key in keys
                if not mine and not relative:
                    # An absolute predicate does not depend on the cell, so
                    # asking it about every other cell on screen would report
                    # the same answer 900 times. `unintended` stays n/a for
                    # these, which `state` and the report both say.
                    continue
                ok = condition.holds(frame, col, row)
                if mine:
                    v.instances += 1
                    if ok:
                        v.held += 1
                    else:
                        v.failed += 1
                        if v.first_failure is None:
                            v.first_failure = (frame.index, row, col)
                elif ok:
                    v.unintended += 1
                    if v.first_unintended is None:
                        v.first_unintended = (frame.index, row, col)
        elif condition.holds(frame):
            v.held += 1
            v.instances += 1
        else:
            v.failed += 1
            v.instances += 1
            if v.first_failure is None:
                v.first_failure = (frame.index, None, None)

    if condition.type == "frameRange":
        v.phase = frame_range_phases(condition, route)
    return v


def frame_range_phases(condition, route, limit=4):
    """The phase offsets that would make a `frameRange` hold on every retained
    frame of this route.

    `frameRange` tests the emulator's global frame counter and a recording
    knows only its own retained index, so a bare verdict would be an artefact
    of where the recording started. What is reportable is the set of offsets
    `k` for which `(index + k) % A >= B` holds throughout — empty when no
    offset works, which is the real finding.
    """
    a, b = condition.operand_a, condition.operand_b
    if a <= 0:
        return []
    out = []
    for k in range(a):
        if all((f.index + k) % a >= b for f in route.frames):
            out.append(k)
            if len(out) >= limit:
                break
    return out


def authored_variants(name, rest):
    """The `(cond, rest)` rows an authored condition produces for one key.

    ADR-0189 §3 and issue #256: the conditional rule is always followed by the
    bare unconditional twin, so a frame where the condition does not hold still
    draws the replacement art instead of falling through to the ROM. The order
    matters — `GetMatchingTile` takes the first passing entry — and it is the
    same order `_condition_variants` produces for an inherited condition.
    """
    rest = list(rest or ["1", "N"])
    return [(f"[{name}]", list(rest)), ("", list(rest))]


def definition_lines(docs):
    """The `<condition>` lines a set of sheet sidecars authored, deduplicated.

    Two sheets may legitimately gate on the same named condition; two
    *different* lines under one name cannot both be written, and the loader
    would silently keep one, so that is refused here instead.
    """
    out = {}
    for doc in docs:
        for entry in doc.get("conditions") or []:
            if not isinstance(entry, dict):
                continue
            cond = parse_condition_line(entry.get("line"))
            prev = out.get(cond.name)
            if prev is not None and prev != cond.line:
                raise ConditionError(
                    f"two different definitions for condition {cond.name!r}: "
                    f"{prev!r} and {cond.line!r}")
            out[cond.name] = cond.line
    return [out[k] for k in sorted(out)]
