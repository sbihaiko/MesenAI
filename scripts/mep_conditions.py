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
`frameRange`, `tileAtPosition` and `tileNearby`. Since F12.6b (ADR-0197 §3,
option (b)) every retained frame also carries the 2 KB of internal RAM,
`$0000`–`$07FF`, as an `M` line, which is what `memoryCheckConstant` and
`memoryCheck` (F12.14 — both operands come off the same line) read. Since
F12.14 (ADR-0222, option A) the OAM stream written under
`MESEN_OAM_STREAM_DUMP` is self-describing in the same way — `K`/`P` intern
lines and `<shape>,<x>,<y>,<pal>` per sprite — and is read from the sibling
`oam.txt` of a route's `grid.txt`, which is what `spriteNearby`,
`spriteAtPosition`, `positionCheckX/Y` and `originPositionCheckX/Y` need.

Limits, stated rather than hidden (ADR-0197 §2):

* `ppuMemoryCheck` / `ppuMemoryCheckConstant` — PPU memory is outside the
  retained window; a `memoryCheck` naming an address at or above `$0800` is
  refused for the same reason;
* a route with no `oam.txt` beside its grid dump, or one written before
  F12.14 (no `K` lines, so a sprite is a bare index and cannot be resolved to
  tile data), reports every sprite and position condition `not evaluable`;
* a sprite condition attached to a **background** key needs the sprites of the
  same emulated frame, and the two streams are retained independently. They
  are joined on the played-frame timeline (both are closed in the same
  `OnFrameEnd`, so the sum of repeats indexes the same frames) only when they
  retain the same number of played frames; otherwise the background instances
  are `not evaluable` and the report says why. A sprite condition attached to
  a **sprite** key needs no join;
* the recorder bakes a sprite's OAM flips into the shape it interns
  (ADR-0178) and the dump does not say which shapes were flipped, so
  `spriteNearby`'s mirrored offset sign is not modelled (the unmirrored sign
  is used) and a condition naming a flipped sprite by its run-time key does
  not match the recorded shape;
* `positionCheckX/Y` are per-pixel in the loader; here an instance holds only
  when every pixel of its 8x8 tile does, so a tile the condition would split
  counts as failed;
* `spriteNearby` is evaluated at the drawn tile's origin pixel shifted by the
  offset, and unintended hits are not counted for sprite conditions.

Each refusal reports `not evaluable` with the reason, and **`not evaluable` is
never a pass**.

The phase problem, stated once. `frameRange` is `FrameNumber % A >= B` against
the **emulator's global frame counter**, and a recording knows only its own
retained-frame index — that is exactly ADR-0189 §4's "the recording knows a
period, not a phase". So `frameRange` is reported with the phase offset that
would make it hold, per route, rather than as a bare pass or fail.
"""

import bisect
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
EVALUABLE = ("frameRange", "tileAtPosition", "tileNearby",
             "memoryCheckConstant", "memoryCheck",
             "spriteNearby", "spriteAtPosition",
             "positionCheckX", "positionCheckY",
             "originPositionCheckX", "originPositionCheckY")
# F12.14 (ADR-0222): the types that read the OAM stream. The position checks
# read only a position, but a sprite key's position is in the OAM stream, so
# they need it as much as the two sprite predicates do.
SPRITE_TYPES = ("spriteNearby", "spriteAtPosition",
                "positionCheckX", "positionCheckY",
                "originPositionCheckX", "originPositionCheckY")
MEMORY_TYPES = ("memoryCheckConstant", "memoryCheck")
# ADR-0197 §3 option (b): the recorder retains exactly this window per frame.
# It is the range ADR-0184 already bounds for RAM cheats.
RAM_WINDOW = 0x800
NO_MEMORY_STREAM = (
    "the recording retains no memory stream — it was made before F12.6b "
    "(ADR-0197 §3); re-record it to validate a memory condition")
NO_OAM_STREAM = (
    "the recording has no OAM stream beside its grid dump — point "
    "MESEN_OAM_STREAM_DUMP at `oam.txt` next to `grid.txt` and re-record "
    "(ADR-0222)")
OAM_NO_TILE_DATA = (
    "OAM stream carries no tile data — it was written before F12.14 "
    "(ADR-0222), so a sprite is a bare index and cannot be matched against a "
    "32-hex key; re-record it")
NOT_EVALUABLE = {
    "ppuMemoryCheck":
        "PPU memory is outside the retained $0000-$07FF window (ADR-0197 §3)",
    "ppuMemoryCheckConstant":
        "PPU memory is outside the retained $0000-$07FF window (ADR-0197 §3)",
}
KNOWN_TYPES = tuple(EVALUABLE) + tuple(NOT_EVALUABLE)


class ConditionError(ValueError):
    """A `<condition>` line a sheet cannot carry."""


# HdPackLoader::ParseConditionOperator, in the loader's own spellings.
_COMPARE = {
    "==": lambda a, b: a == b,
    "!=": lambda a, b: a != b,
    ">": lambda a, b: a > b,
    "<": lambda a, b: a < b,
    "<=": lambda a, b: a <= b,
    ">=": lambda a, b: a >= b,
}


class Condition:
    """One parsed `<condition>` line, in the loader's own field order."""

    __slots__ = ("name", "type", "line", "x", "y", "tile", "palette",
                 "ignore_palette", "operand_a", "operand_b", "operator", "mask")

    def __init__(self, name, ctype, line):
        self.name = name
        self.type = ctype
        self.line = line
        self.x = self.y = 0
        self.tile = ""
        self.palette = ""
        self.ignore_palette = False
        self.operand_a = self.operand_b = 0
        # memoryCheckConstant only: the comparison and the AND mask the C++
        # applies to the *watched* byte alone (`a & Mask`, `b = OperandB`).
        self.operator = "=="
        self.mask = 0xFF

    @property
    def evaluable(self):
        return self.not_evaluable_reason is None

    @property
    def not_evaluable_reason(self):
        """Why this condition cannot be decided from *any* recording, or None.

        Route-dependent reasons (an old dump with no memory stream) are
        `not_evaluable_on` — a condition that this build can evaluate should
        not read as a type the toolchain refuses.
        """
        if self.type in MEMORY_TYPES:
            watched = [self.operand_a]
            if self.type == "memoryCheck":
                watched.append(self.operand_b)
            for addr in watched:
                if addr >= RAM_WINDOW:
                    return (f"address ${addr:04X} is outside the retained "
                            f"$0000-$07FF window (ADR-0197 §3); WRAM, PRG and "
                            "mapper registers are not recorded")
        return NOT_EVALUABLE.get(self.type)

    def not_evaluable_on(self, route):
        """Why this condition cannot be decided from *this* route, or None."""
        reason = self.not_evaluable_reason
        if reason is not None:
            return reason
        if self.type in MEMORY_TYPES and not route.has_ram:
            return NO_MEMORY_STREAM
        if self.type in SPRITE_TYPES:
            if route.oam is None:
                return NO_OAM_STREAM
            if not route.oam.has_tiles:
                return OAM_NO_TILE_DATA
        return None

    def __repr__(self):
        return f"<Condition {self.name} {self.type}>"

    # --- evaluation, mirroring HdPackConditions.h ---------------------------

    def holds(self, frame, cell_col=None, cell_row=None, oam=None):
        """True when this condition would hold on `frame`.

        `cell_col`/`cell_row` are the grid position of the tile being drawn,
        which `tileNearby` and the sprite-side types read — they are relative
        to the tile, and the C++ computes `PixelOffset + (y * 256) + x` from
        the drawn tile's own pixel coordinates. `oam` is the `OamFrame` joined
        to this grid frame, for the sprite-side types (F12.14).
        """
        if self.type == "frameRange":
            # HdPackFrameRangeCondition: FrameNumber % OperandA >= OperandB.
            if self.operand_a == 0:
                return False
            return frame.index % self.operand_a >= self.operand_b
        if self.type == "tileAtPosition":
            # PixelOffset = (y * 256) + x, then ScreenTiles[PixelOffset].Tile.
            return self._tile_matches(frame, self.x, self.y)
        if self.type == "memoryCheckConstant":
            # HdPackMemoryCheckConstantCondition::InternalCheckCondition:
            #   uint8_t a = WatchedAddressValues[OperandA] & Mask;
            #   uint8_t b = OperandB;
            # The mask is applied to the read byte only, never to the constant.
            if frame.ram is None:
                return False
            return _COMPARE[self.operator](frame.ram[self.operand_a] & self.mask,
                                           self.operand_b)
        if self.type == "memoryCheck":
            # HdPackMemoryCheckCondition::InternalCheckCondition:
            #   uint8_t a = WatchedAddressValues[OperandA] & Mask;
            #   uint8_t b = WatchedAddressValues[OperandB] & Mask;
            # Both operands are watched bytes of the same frame, both masked.
            if frame.ram is None:
                return False
            return _COMPARE[self.operator](frame.ram[self.operand_a] & self.mask,
                                           frame.ram[self.operand_b] & self.mask)
        if self.type == "tileNearby":
            if cell_col is None or cell_row is None:
                return False
            px = cell_col * CELL + frame.fine + self.x
            py = cell_row * CELL + self.y
            return self._tile_matches(frame, px, py)
        if self.type in SPRITE_TYPES:
            # A background instance: the tile's origin is its cell's pixel
            # origin, and the sprites of its frame are the joined OAM frame
            # (None when the frame has none — then nothing is near).
            if cell_col is None or cell_row is None:
                return False
            return self.holds_at(cell_col * CELL + frame.fine, cell_row * CELL, oam)
        raise ConditionError(
            f"{self.name}: {self.type} cannot be evaluated from a grid stream")

    def holds_at(self, x0, y0, oam):
        """The sprite-side predicates for a tile whose origin pixel is
        `(x0, y0)` and whose frame's sprites are `oam` (an `OamFrame`, or None
        when the frame has none).

        Mirrors `HdPackConditions.h`, with the limits the module docstring
        states: `positionCheck*` must hold on every pixel of the 8x8 tile, the
        origin checks read the tile's own origin, `spriteNearby` is tested at
        the origin pixel shifted by the offset and with the unmirrored sign.
        """
        if self.type in ("positionCheckX", "positionCheckY"):
            # HdPackBasePositionCheckCondition: val = x (or y) of the pixel
            # being drawn, compared with Operand by Operator.
            base = x0 if self.type == "positionCheckX" else y0
            cmp = _COMPARE[self.operator]
            return all(cmp(base + i, self.operand_a) for i in range(CELL))
        if self.type in ("originPositionCheckX", "originPositionCheckY"):
            # val = x - OffsetX (or the mirrored twin), i.e. the tile's origin.
            val = x0 if self.type == "originPositionCheckX" else y0
            return _COMPARE[self.operator](val, self.operand_a)
        if oam is None:
            return False
        if self.type == "spriteAtPosition":
            # PixelOffset = (y * 256) + x; any sprite covering it may match.
            return self._sprite_matches(oam, self.x, self.y)
        if self.type == "spriteNearby":
            # pixelIndex = ((y + TileY) * 256) + x + TileX from the drawn
            # pixel; the C++ flips each sign when the drawn tile is mirrored,
            # which the dump does not record (module docstring).
            return self._sprite_matches(oam, x0 + self.x, y0 + self.y)
        return False

    def _sprite_matches(self, oam, px, py):
        """Some sprite of `oam` covering screen pixel (px, py) has the key.

        The C++ bounds-checks the pixel index and returns false outside the
        screen. A sprite covers the 8x8 block at its origin; an 8x16 sprite is
        in the stream as two 8x8 halves, so one rule serves both.
        """
        if not (0 <= px < SCREEN_W and 0 <= py < SCREEN_H):
            return False
        for data, pal in oam.keys_covering(px, py):
            if data == self.tile and (self.ignore_palette or pal == self.palette):
                return True
        return False

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
            # HdPackLoader::ParseBooleanValue: Y/YES/TRUE/1 are true, N/NO/
            # FALSE/0 and an empty field are false. `Y` is the spelling
            # HdPackBaseTileCondition::ToString itself writes.
            cond.ignore_palette = tokens[6].strip().upper() in ("Y", "YES", "TRUE", "1")
        if ctype == "tileNearby" and (cond.x % CELL or cond.y % CELL):
            # HdPackLoader logs this one as an error on load; a sheet must not
            # be able to author a condition the loader will reject.
            raise ConditionError(
                f"{name}: tileNearby offsets must be multiples of 8 "
                f"(got {cond.x},{cond.y})")
    elif ctype in MEMORY_TYPES:
        # HdPackLoader: `<condition>name,memoryCheckConstant,<addr>,<op>,<val>
        # [,<mask>]` and `<condition>name,memoryCheck,<addrA>,<op>,<addrB>
        # [,<mask>]`, every numeric field hex (the loader reads them all with
        # HexUtilities::FromHex, so `30` is $30 and not 30).
        if len(tokens) < 5:
            raise ConditionError(
                f"{name}: {ctype} needs at least 5 fields (name, type, "
                "address, operator, "
                + ("value)" if ctype == "memoryCheckConstant" else "address)"))
        if len(tokens) > 6:
            raise ConditionError(f"{name}: {ctype} takes at most 6 fields")
        try:
            cond.operand_a = int(tokens[2], 16)
            cond.operand_b = int(tokens[4], 16)
            cond.mask = int(tokens[5], 16) if len(tokens) > 5 else 0xFF
        except ValueError:
            raise ConditionError(
                f"{name}: {ctype} address, value and mask are hex") from None
        if tokens[3] not in _COMPARE:
            raise ConditionError(
                f"{name}: {tokens[3]!r} is not an operator; the loader knows "
                + ", ".join(sorted(_COMPARE)))
        cond.operator = tokens[3]
        if not 0 <= cond.operand_a <= 0xFFFF:
            raise ConditionError(f"{name}: address out of range")
        if ctype == "memoryCheckConstant" and not 0 <= cond.operand_b <= 0xFF:
            # HdPackLoader: "Out of range memoryCheckConstant operand".
            raise ConditionError(
                f"{name}: memoryCheckConstant compares one byte, so the value "
                f"must be 00-FF (got {tokens[4]!r})")
        if ctype == "memoryCheck" and not 0 <= cond.operand_b <= 0xFFFF:
            # HdPackLoader: "Out of range memoryCheck operand".
            raise ConditionError(f"{name}: second address out of range")
        if not 0 <= cond.mask <= 0xFF:
            raise ConditionError(f"{name}: mask must be 00-FF")
    elif ctype in ("positionCheckX", "positionCheckY",
                   "originPositionCheckX", "originPositionCheckY"):
        # HdPackLoader: `<condition>name,positionCheckX,<op>,<operand>`, exactly
        # 4 fields; the operand is read with std::stoi, so it is *decimal* —
        # unlike the memory conditions, and unlike the hex ToString writes.
        if len(tokens) != 4:
            raise ConditionError(
                f"{name}: {ctype} takes exactly 4 fields (name, type, "
                "operator, operand)")
        if tokens[2] not in _COMPARE:
            raise ConditionError(
                f"{name}: {tokens[2]!r} is not an operator; the loader knows "
                + ", ".join(sorted(_COMPARE)))
        cond.operator = tokens[2]
        try:
            cond.operand_a = int(tokens[3])
        except ValueError:
            raise ConditionError(
                f"{name}: {ctype} operand is a decimal integer (the loader "
                "reads it with std::stoi)") from None
        if not 0 <= cond.operand_a <= 0xFFFF:
            # HdPackLoader: "Out of range positionCheck operand".
            raise ConditionError(f"{name}: {ctype} operand out of range")
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

    __slots__ = ("index", "repeat", "fine", "rows", "pals", "ram", "_shapes")

    def __init__(self, index, shapes):
        self.index = index
        self.repeat = 1
        self.fine = 0
        # F12.6b: the `M` line's 2 KB, or None on a dump written before it.
        # Held as `bytes`, so `frame.ram[addr]` is the byte at that address.
        self.ram = None
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

    def __init__(self, name, frames, shapes, path=None, oam=None):
        self.name = name
        self.frames = frames
        self.shapes = shapes
        self.path = path
        # F12.6b: whether this recording carries the memory plane at all. A
        # dump is written by one build, so one frame settles it.
        self.has_ram = any(f.ram is not None for f in frames)
        # F12.14 (ADR-0222): the OAM stream recorded beside the grid, or None
        # when the route has none. `oam_skipped` says why a file that was
        # there could not be read, so the report can name it.
        self.oam = oam
        self.oam_skipped = None

    @property
    def oam_aligned(self):
        """Whether the grid and OAM streams can be joined frame for frame.

        Both are closed in the same `HdPackBuilder::OnFrameEnd`, so the sum of
        repeats up to a frame indexes the same emulated frame in both — unless
        one stream dropped a frame the other kept (a frame with nothing drawn
        is absent from the grid, one with no sprite from the OAM). Equal
        played counts are the check this reader can make; a disagreement means
        a background key's sprite condition is not evaluable on this route.
        """
        return self.oam is not None and self.oam.played == self.played

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

    Five line kinds (`HdPackBuilder::WriteGridDump`): `F <n>` opens a frame and
    repeats once per collapsed duplicate, `K <id> <32 hex tile data> <8 hex
    palette>` interns a shape the first time it is drawn, `P <id> <8 hex
    palette>` interns a palette word, `M <4096 hex>` carries that frame's
    internal RAM (F12.6b — written once, on the frame's first repeat), and
    `<x> <y> <shape> [<palette id>]` places a cell. `x` is `col * 8 + fineX`, so `x & 7` recovers the frame's
    fine scroll and `(x - fineX) // 8` its column.

    The fourth cell field and the `P` lines are F9.24's palette plane. A dump
    written by an older build has neither; it parses unchanged and every cell
    falls back to the shape's own first-seen palette. The same holds for `M`:
    a pre-F12.6b dump has none, every frame's `ram` stays None, and a memory
    condition reports `not evaluable` rather than a verdict.
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
            elif head == "M":
                # F12.6b: `M <4096 hex>` — the retained $0000-$07FF of the
                # frame that is open, written once on its first repeat. A
                # short or malformed line is dropped rather than half-read:
                # a partial window would answer a memory condition with a
                # byte from the wrong address.
                if cur is not None:
                    body = line[2:].strip()
                    if len(body) == RAM_WINDOW * 2:
                        try:
                            cur.ram = bytes.fromhex(body)
                        except ValueError:
                            cur.ram = None
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


# --- the recorded OAM stream (F12.14, ADR-0222 option A) --------------------


class OamFrame:
    """One retained frame of the recorder's sprite stream."""

    __slots__ = ("index", "repeat", "ports", "entries", "_shapes")

    def __init__(self, index, repeat, ports, shapes):
        self.index = index
        self.repeat = repeat
        # ADR-0181: the packed button bytes of ports 1 and 2.
        self.ports = ports
        # (shape, x, y, palette id[, visible pixels, behind bg, hidden
        # pixels]) per sprite, in OAM order; an 8x16 sprite is two 8x8 entries.
        # ADR-0234 appends the last three fields, so an entry may be 4 long (a
        # dump written before it), 6 (before the third field settled) or 7.
        self.entries = []
        self._shapes = shapes

    def key_of(self, entry):
        """(tileData, palette) of one sprite entry, or None when the dump
        never interned its shape — the ADR-0159 rule: the per-entry palette id
        wins, the shape's own first-seen palette is the fallback."""
        shape, pal = entry[0], entry[3]
        rec = self._shapes.get(shape)
        if rec is None:
            return None
        data, shape_pal = rec
        if pal == UNKNOWN_PALETTE:
            return data, shape_pal
        return data, self._shapes.palette(pal, shape_pal)

    def visible_pixels(self, entry):
        """ADR-0234: how many pixels of this entry's own reached the screen,
        or None when the dump predates the field. 0 on a behind-background
        entry is the mask evidence: SMB3's piranha-plant pipe mask covers
        ~207 000 pixels and puts none of them on screen."""
        return entry[4] if len(entry) > 4 else None

    def behind_bg(self, entry):
        """ADR-0234: the entry's OAM attribute bit 5, or None when the dump
        predates the field. It is the fact the mask verdict is checked against,
        not the verdict itself."""
        return bool(entry[5]) if len(entry) > 5 else None

    def hidden_pixels(self, entry):
        """ADR-0234: how many of the entry's pixels lost to an opaque
        background, or None when the dump predates the field. A mask contended
        for pixels and lost every one; an entry the PPU never drew (the
        8-per-line limit) shows 0 here too and is a figure, not a mask."""
        return entry[6] if len(entry) > 6 else None

    def keys_covering(self, px, py):
        """The keys of every sprite whose 8x8 block covers pixel (px, py) —
        `ScreenTiles[pixel].Sprite[i]` of the C++, without its 8-per-scanline
        cap (the recorder reads OAM, not the PPU's sprite evaluation)."""
        for entry in self.entries:
            if entry[1] <= px < entry[1] + CELL and entry[2] <= py < entry[2] + CELL:
                key = self.key_of(entry)
                if key is not None:
                    yield key


class OamStream:
    """A recorded OAM stream: its frames, shapes, and the played-frame index
    that joins it to the grid stream of the same recording."""

    def __init__(self, frames, shapes, path=None, has_visibility=False):
        self.frames = frames
        self.shapes = shapes
        self.path = path
        # A dump written before F12.14 has no `K` line: its entries are bare
        # indexes and no sprite can be resolved to tile data.
        self.has_tiles = bool(shapes)
        # ADR-0234: a dump written before it carries neither the priority bit
        # nor the visible-pixel count, so no entry can be told a mask from a
        # sprite that simply never had a pixel to show.
        self.has_visibility = has_visibility
        self._starts = []
        total = 0
        for f in frames:
            self._starts.append(total)
            total += f.repeat
        self.played = total

    @property
    def retained(self):
        return len(self.frames)

    def frames_between(self, start, count):
        """The frames whose played span overlaps [start, start + count)."""
        if count <= 0 or not self.frames:
            return []
        first = bisect.bisect_right(self._starts, start) - 1
        first = max(first, 0)
        out = []
        for i in range(first, len(self.frames)):
            if self._starts[i] >= start + count:
                break
            if self._starts[i] + self.frames[i].repeat > start:
                out.append(self.frames[i])
        return out


def parse_oam_dump(path):
    """Read a `MESEN_OAM_STREAM_DUMP` file into frames and shapes.

    Three line kinds (`MesenSheets::WriteOamStreamDump`, ADR-0222): `K <id>
    <32 hex tile data> <8 hex palette>` interns a shape on first sight, `P <id>
    <8 hex palette>` interns a palette word, and `<frame> <repeat> <port1>
    <port2> [<shape>,<x>,<y>,<pal>]...` is one retained frame. The shape ids
    are the grid stream's — one `ShapeIdFor` table serves both — but each dump
    interns what it names, so a reader needs only this file.

    A dump written before F12.14 has frame lines of `<node>,<x>,<y>` and no
    `K`/`P` lines; it parses (entries get `UNKNOWN_PALETTE`) and `has_tiles`
    says the sprites cannot be resolved.

    ADR-0234 appends `<visible>,<bg>,<hidden>` to the entry token. All three
    are optional on read: a dump written before it parses unchanged, and
    `OamFrame.visible_pixels` / `behind_bg` / `hidden_pixels` return None for
    such an entry rather than inventing a zero, which would read as mask
    evidence.
    """
    path = Path(path)
    frames = []
    shapes = _Shapes()
    has_visibility = False
    with path.open("r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if not line.strip():
                continue
            head = line[0]
            if head == "K":
                parts = line.split()
                shapes[int(parts[1])] = (parts[2].upper(), parts[3].upper())
            elif head == "P":
                parts = line.split()
                shapes.palettes[int(parts[1])] = parts[2].upper()
            else:
                parts = line.split()
                if len(parts) < 4:
                    continue
                frame = OamFrame(int(parts[0]), int(parts[1]),
                                 (int(parts[2]), int(parts[3])), shapes)
                for token in parts[4:]:
                    fields = token.split(",")
                    if len(fields) < 3:
                        continue
                    pal = int(fields[3]) if len(fields) > 3 else UNKNOWN_PALETTE
                    entry = (int(fields[0]), int(fields[1]), int(fields[2]), pal)
                    if len(fields) > 5:
                        entry = entry + (int(fields[4]), int(fields[5]))
                        has_visibility = True
                    if len(fields) > 6:
                        entry = entry + (int(fields[6]),)
                    frame.entries.append(entry)
                frames.append(frame)
    return frames, shapes, has_visibility


def load_oam_stream(path):
    """An `OamStream` from an OAM dump file, or raise `ConditionError`."""
    path = Path(path)
    frames, shapes, has_visibility = parse_oam_dump(path)
    if not frames:
        raise ConditionError(
            f"{path}: no OAM frames — was MESEN_OAM_STREAM_DUMP set on a run "
            "that drew a sprite?")
    return OamStream(frames, shapes, path, has_visibility)


def oam_path_for(grid_path):
    """Where a route's OAM dump is looked for: `oam.txt` beside a `grid.txt`
    (the convention `MESEN_SHEET_GRID_DUMP=<run>/grid.txt` already sets), or
    `<stem>.oam.txt` beside a grid dump with any other name."""
    grid_path = Path(grid_path)
    if grid_path.stem in ("grid", "dump"):
        return grid_path.with_name("oam.txt")
    return grid_path.with_name(grid_path.stem + ".oam.txt")


def is_oam_dump_path(path):
    """Whether a file in a routes folder is an OAM dump rather than a route."""
    name = Path(path).name
    return name == "oam.txt" or name.endswith(".oam.txt")


def load_route(path, name=None):
    """A `Route` from a grid dump file, or raise `ConditionError`.

    The OAM stream is read from `oam_path_for(path)` when it exists; a route
    without one is still a route, and every sprite condition then reports
    `not evaluable` with `NO_OAM_STREAM`.
    """
    path = Path(path)
    frames, shapes = parse_grid_dump(path)
    if not frames:
        raise ConditionError(
            f"{path}: no grid frames — was MESEN_SHEET_GRID_DUMP set on a run "
            "that reached gameplay?")
    route = Route(name or route_name(path), frames, shapes, path)
    oam_path = oam_path_for(path)
    if oam_path.is_file():
        try:
            route.oam = load_oam_stream(oam_path)
        except (ConditionError, ValueError, IndexError) as exc:
            route.oam_skipped = str(exc)
    return route


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
                                if q.is_file() and q.suffix in (".txt", ".grid")
                                and not is_oam_dump_path(q))
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
        # F12.14: the first failing *sprite* instance, (OAM frame index, x, y)
        # or None — a sprite key has a screen position, not a grid cell.
        self.first_failure_sprite = None
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
    reason = condition.not_evaluable_on(route)
    if reason is not None:
        v.evaluable = False
        v.reason = reason
        return v
    if condition.type in SPRITE_TYPES:
        return _evaluate_sprite(condition, keys, route, v)

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


def _evaluate_sprite(condition, keys, route, v):
    """F12.14 (ADR-0222): a sprite-side condition over one route.

    An instance is a drawn tile carrying one of the conditioned keys, and it
    can be either a **sprite** (an OAM entry — evaluated within its own OAM
    frame, no join needed) or a **background cell** (evaluated against the
    OAM frames that overlap its grid frame on the played-frame timeline, one
    instance per overlapping OAM frame). The position checks read no sprite
    other than the instance itself, so a background cell's position check
    needs no join either. When a join is needed and the two streams do not
    align (`Route.oam_aligned`), the verdict is `not evaluable` and says so —
    the wrong frame's sprites must never become a pass or a fail.

    Unintended hits are not counted: every sprite on screen would be a
    candidate, and the report says n/a rather than a number that means less.
    """
    oam = route.oam
    for of in oam.frames:
        for entry in of.entries:
            key = of.key_of(entry)
            if key is None or key not in keys:
                continue
            v.instances += 1
            if condition.holds_at(entry[1], entry[2], of):
                v.held += 1
            else:
                v.failed += 1
                if v.first_failure_sprite is None:
                    v.first_failure_sprite = (of.index, entry[1], entry[2])

    needs_join = condition.type in ("spriteNearby", "spriteAtPosition")
    unjoined = 0
    start = 0
    for frame in route.frames:
        cells = [(row, col) for row, col in frame.drawn_cells()
                 if frame.key_at(row, col) in keys]
        if cells:
            if not needs_join:
                joined = [None]
            elif route.oam_aligned:
                joined = oam.frames_between(start, frame.repeat) or [None]
            else:
                joined = []
                unjoined += len(cells)
            for of in joined:
                for row, col in cells:
                    v.instances += 1
                    if condition.holds(frame, col, row, of):
                        v.held += 1
                    else:
                        v.failed += 1
                        if v.first_failure is None:
                            v.first_failure = (frame.index, row, col)
        start += frame.repeat

    if unjoined:
        v.evaluable = False
        v.reason = (
            f"{unjoined} background instance(s) of the key need the sprites of "
            f"their own frame, but the grid and OAM streams retain different "
            f"played-frame counts ({route.played} vs {oam.played}) and cannot "
            "be joined (ADR-0222)")
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
    same order `inherited_variants` produces for an inherited condition.
    """
    rest = list(rest or ["1", "N"])
    return [(f"[{name}]", list(rest)), ("", list(rest))]


def inherited_variants(raw_variants):
    """(cond, rest) rows for one (tile, palette), with the unconditional
    fallback twin ADR-0189 §3 / #256 requires. `raw_variants` is the list
    collected from the key source, or None when the key is unknown.

    Moved here from `mep_build` with F12.7, so that the three ways a crop can
    decide which rules it carries — inherit, authored, exact — read as one
    model in one module.
    """
    if not raw_variants:
        return [("", ["1", "N"])]
    by_cond = {}
    for cond, rest in raw_variants:
        by_cond.setdefault(cond, list(rest))
    if any(c for c in by_cond) and "" not in by_cond:
        # Recorder always writes the bare twin after each [condition] rule;
        # synthesise it from the first conditional's trailing fields when the
        # key source lost it (or a hand-edited manifest omitted it).
        by_cond[""] = list(next(v for c, v in by_cond.items() if c))
    # Conditionals first, bare twin last — matches HdPackBuilder's order and
    # GetMatchingTile's "first passing entry" walk.
    return sorted(by_cond.items(), key=lambda kv: (0 if kv[0] else 1, kv[0]))


def cell_condition(cell):
    """How a sheet cell says which rules its crop carries, or None.

    - `None` — the cell says nothing, so the key source decides. Every
      recorded pack, and every sheet written before ADR-0197.
    - `(name, False)` — ADR-0197 §1: a human attached a condition to the cell,
      and the crop emits `[name]` plus the ADR-0189 §3 bare twin (#256).
    - `(name, True)` — ADR-0198 §1 (F12.7): the crop carries *exactly* one
      rule, `[name]`, or the bare unconditional rule when `name` is empty. No
      inherited sibling, no synthesised twin. A legacy manifest keys one
      pattern at several crops, one per condition, and any extra rule emitted
      from this crop would draw the other crops' art.
    """
    if not isinstance(cell, dict):
        return None
    name = str(cell.get("condition") or "")
    exact = cell.get("exactCondition") is True
    return (name, exact) if (name or exact) else None


def rest_for(raw_variants, name):
    """The trailing `<tile>` fields (brightness, defaultTile, ...) a crop keeps.

    The key source carries them per rule, so a crop that names a condition
    reuses the fields of the rule that had that condition; failing that, the
    key's unconditional rule; failing that, the loader's own defaults. Contra80s
    gives 576 of its 592 multi-crop patterns a different brightness per
    condition, so taking them from the key source is the difference between a
    faithful import and a plausible one.
    """
    want = f"[{name}]" if name else ""
    for cond, rest in raw_variants or []:
        if cond == want:
            return list(rest)
    for cond, rest in raw_variants or []:
        if not cond:
            return list(rest)
    return ["1", "N"]


def variants_for(authored, raw_variants):
    """The (cond, rest) rows one crop emits, over the three modes above."""
    if authored is None:
        return inherited_variants(raw_variants)
    name, exact = authored
    rest = rest_for(raw_variants, name)
    if exact:
        return [(f"[{name}]" if name else "", rest)]
    return authored_variants(name, rest)


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
