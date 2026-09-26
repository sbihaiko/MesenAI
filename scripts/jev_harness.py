#!/usr/bin/env python3
"""The stall helper: a local search plays, and Jev is asked only where it stops.

F14.14, ADR-0238 sections 3 and 4, including every amendment of 2026-09-26
(rewind ladder, situation tips, web research, loop guard, cheats).

The shape of a run:

* A **step-mode session** (F14.12, `scripts/step_emu.py`) loads the ROM and a
  minted `.mss` once. Everything after that is requests to it.
* A **base search** plays, greedily, the macros of a fixed table - each macro a
  name, a window of `(buttons, frames)` parts and a frame count fixed here in
  code. It is scored by the
  game's own progress byte, through `route_search.rank` when that module is
  importable and by this file's own ranking otherwise. The eight windows the
  scratch search used never left Act 1-1's x 987 pin, which is why Jev exists.
* When the search makes **no progress for `--stall-seconds` emulated seconds**,
  the stall helper takes over: one Jev `Choice` over the macros, with a
  RAM-derived JSON state (`scripts/stages/<game>/ram-map.json`), the situation
  tips whose RAM trigger holds (`scripts/stages/<game>/jev-tips.json`), and the
  macros that already failed from that checkpoint withdrawn from the options.
* A **rewind ladder** of 1, 2, 4, 8 and 16 emulated seconds back, never past the
  start of the current screen or the last real progress, up to three questions
  per rung. When the ladder is exhausted the stall is written out as a report
  and **one research pass** (`scripts/jev_stall_research.py`, a `claude -p`
  worker with web search) proposes tips and a macro; the retry starts from the
  checkpoint with the proposals kept under `runs/`.
* A **loop guard** watches three things per decision - a state fingerprint seen
  three times in one stall, a watermark that has not risen for 60 emulated
  seconds, a period-2..4 cycle repeated three times in the last 12 choices. The
  first loop bans the cycle's macros and climbs a rung, the second goes to
  research, the third ends the stall as `loop`.
* What survives is a plain `<n>f <buttons>` script, the same format
  `scripts/stages/<game>/*.txt` already uses, with one `1f -` after each macro
  (`render_script`, the same input-neutral boundary `scripts/route_search.py`
  writes *and plays*, `step_emu.play_window`). The chain plays that frame too
  (`_play_macro`), so the path the search walked and the flat script it ships
  are the same play. Replay never calls Jev: same ROM, same state, same inputs,
  same frames (ADR-0185 - Jev's output is input, never evidence).
* What the run *claims* is never taken from the search: the script is replayed
  flat from the minted state (`measure_script`) and it is that replay's RAM that
  decides `goal_reached`, becomes the checkpoints `--verify` replays through a
  one-shot run, and is reported in the summary. A path only the search's chain
  of `play` calls reaches is reported as `chain-not-reproduced`, never shipped.
  Measured 2026-09-26 on Ninja Gaiden Act 1-1: without the boundary frame the
  flat script's 1 155 frames reach abs x 978 where the chain reaches 991.
* A **cheat** (`--cheat`, ADR-0184 section 1) reaches the session's own
  `cheat=` and the run checks the tool's report of applying it; the script that
  comes out is a coverage pass and ships with its codes beside it.
* A **stage** (`--stage`, or `<game>/stage-set.json`) is a run-level key a tip
  may be gated on. No RAM address carries one: mm3's `stage_id` is recorded as
  unconfirmed, so a run declares its stage rather than reading a byte.

    python3 scripts/jev_harness.py --rom "$NG_ROM" --game ninjagaiden \
        --state runs/ng/stage1-run.mss --work runs/jev-ng \
        --goal abs_x:990 --budget 0.05 --verify

Exit codes: 0 the goal was reached, 1 the run ended without it, 2 a refusal (no
key, a cheat the session will not apply, a config this harness will not guess
at), 5 the budget was reached.
"""
from __future__ import annotations

import argparse
import ast
import hashlib
import json
import re
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import NamedTuple

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
import jev_client  # noqa: E402
import step_emu  # noqa: E402

FPS = step_emu.FPS_NTSC
STAGES = HERE / "stages"
DEFAULT_WORK = ROOT / "runs" / "jev-harness"


class HarnessError(RuntimeError):
    """A config or a request this harness refuses to guess about."""


class CheatError(HarnessError):
    """A cheat code ADR-0184 section 1 refuses."""


class RamMapError(HarnessError):
    """A ram-map.json this harness cannot read a state out of."""


class TipsError(HarnessError):
    """A jev-tips.json whose trigger names a field the game does not have."""


# --------------------------------------------------------------------------
# The run-level keys, and the one of them there is (ADR-0238 section 3).
# --------------------------------------------------------------------------

def stage_from_set(path):
    """The stage a `scripts/stages/<game>/stage-set.json` names, or None.

    The set is where a game's routes are described, so a set that *is* one route
    names its stage under `stage`, and a set carrying a list names it under
    `stages` - where only a single-entry list is unambiguous, and a list of
    several is left to `--stage` rather than guessed at by taking the first.

    A stage is also a thing a RAM byte could carry, and the honest answer here
    is that no byte does yet: `scripts/stages/mm3/ram-map.json` records its
    `stage_id` as `open`, unconfirmed, and this harness reads a stage from a
    file or from the command line and never from an unverified address.
    """
    try:
        document = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None, []
    if not isinstance(document, dict):
        return None, []
    stages = [name.strip() for name in (document.get("stages") or [])
              if isinstance(name, str) and name.strip()]
    named = document.get("stage")
    if isinstance(named, str) and named.strip():
        named = named.strip()
        return named, list(dict.fromkeys([*stages, named]))
    if len(stages) == 1:
        return stages[0], stages
    return None, stages


def resolve_stage(path, explicit=None):
    """`(the run's stage, the stage names known here)`.

    `--stage` wins when it is given, because a run knows what it is playing
    better than a file does; the set supplies the default. The names come back
    so a caller can refuse a `--stage` the set does not carry - a typo would
    otherwise gate every tip off in silence, which reads exactly like a game
    whose tips do not apply.
    """
    named, known = stage_from_set(path)
    return ((explicit.strip() if explicit else None) or named), known


def tips_gated_on_stage(tips, fields=()) -> list:
    """The tips whose trigger names the run-level `stage` - the ones a run with
    no stage to declare cannot fire, and says so about.

    A map that verifies a byte called `stage` of its own is not one of these:
    Ninja Gaiden's `$006D` is a RAM field, so `birds` and `barbarian-boss` read
    the console and fire whether or not the run declared anything. Only the
    game whose map has no such byte (Mega Man 3, whose `stage_id` is `open`)
    has tips this note is about.
    """
    fields = set(fields)
    return [tip.id for tip in tips.all
            if any(condition.field == "stage" and "stage" not in fields
                   for condition in tip.conditions)]


# --------------------------------------------------------------------------
# Macros: a name, a window of `(buttons, frames)` parts and a description. The
# window is one value for the whole table (--macro-frames), fixed here, never
# chosen by the model.
# --------------------------------------------------------------------------

# ADR-0238 section 3's seven. The key is the ADR's name with its count stripped.
BASE_MACROS = {
    "RIGHT": ("R", "hold Right and walk forward"),
    "RIGHT_RUN": ("RB", "hold Right and B and run forward"),
    "JUMP_RIGHT": ("RA", "jump forward: hold Right and A"),
    "JUMP": ("A", "jump straight up: hold A"),
    "ATTACK": ("B", "slash where Ryu is facing: hold B"),
    "LEFT": ("L", "hold Left and walk back"),
    "WAIT": ("-", "press nothing"),
}
# Macros a tip may name (ADR-0238 section 3: "macros a tip calls for are added
# to the fixed set"). They are in this table, so their buttons and their
# duration are still this file's and not the tip's.
TIP_MACROS = {
    "JUMP_LEFT": ("LA", "jump backward-left: hold Left and A"),
    "ATTACK_LEFT": ("LB", "turn and slash to the left: hold Left and B"),
    "JUMP_RIGHT_RUN": ("RAB", "jump forward running: hold Right, A and B"),
    "DUCK": ("D", "duck: hold Down"),
    #Mega Man 3 (F14.15): B is the buster and the slide is Down with A. SHOOT
    #and SHOOT_BURST are the same press under the two names the tips use for it
    #- one shot, and fire held through the window - because the duration is the
    #window either way and only the tip's own words say which it wants.
    "SHOOT": ("B", "fire the buster: hold B"),
    "SHOOT_BURST": ("B", "fire a burst: hold B through the window"),
    "SLIDE": ("DA", "slide: hold Down and A"),
    "SLIDE_RIGHT": ("DRA", "slide forward: hold Down, Right and A"),
    "SLIDE_LEFT": ("DLA", "slide back: hold Down, Left and A"),
    "JUMP_SHOOT": ("AB", "jump and fire: hold A and B"),
}
LIBRARY = {**BASE_MACROS, **TIP_MACROS}


def macro_parts(spec):
    """A macro's window as parts: `"RA"` is one hold to the end of the window,
    `(("LA", 4), ("RA", None))` is the search's own multi-part shape."""
    if isinstance(spec, str):
        return ((spec, None),)
    return tuple((str(buttons), None if count is None else int(count))
                 for buttons, count in spec)


def macro_lines(parts, frames) -> list:
    """A macro's window as the script lines it writes: one per part.

    The last part may hold to the end of the window (no frame count), and a run
    of parts that does not fill it is padded with the idle frame, so every macro
    is exactly `frames` long and a chain of them has a known frame count. That
    is the convention `scripts/route_search.py` writes its own candidates with
    (a Ninja Gaiden jump is edge-triggered and a wall hop is two moves - off the
    wall, then back at it - so a candidate is a run of parts, never one hold).
    """
    parts = list(parts)
    if not parts:
        raise HarnessError("a macro needs at least one part")
    if any(count is None for _buttons, count in parts[:-1]):
        raise HarnessError(
            f"only the last part of {parts!r} may hold to the end of the window")
    lines, used = [], 0
    for buttons, count in parts:
        count = frames - used if count is None else int(count)
        if count <= 0:
            raise HarnessError(f"a macro part needs a positive frame count, got {count!r}")
        if used + count > frames:
            raise HarnessError(
                f"the parts of {parts!r} run past the {frames}-frame window")
        lines.append(step_emu.macro_line(count, buttons))
        used += count
    if used < frames:
        lines.append(step_emu.macro_line(frames - used, "-"))
    return lines


@dataclass(frozen=True)
class Macro:
    """One entry of the fixed table: a window of parts, with the run's frame
    count baked in."""

    name: str
    parts: tuple
    frames: int
    text: str

    @property
    def buttons(self) -> str:
        """Every button the macro presses, in order."""
        return "".join(buttons for buttons, _count in self.parts)

    def lines(self) -> tuple:
        return tuple(macro_lines(self.parts, self.frames))

    def line(self) -> str:
        """The macro's script text: one `<n>f <buttons>` line per part."""
        return "\n".join(self.lines())

    def describe(self) -> str:
        return f"{self.text} ({self.frames} frames)."


def macro_table(frames: int, extra=None) -> dict:
    """`{name: Macro}` for the fixed set plus any extra `{name: (buttons, text)}`.

    A macro whose fixed parts cannot fit the run's window (a 4-frame wall hop
    under `--macro-frames 3`) is left out with a note, the way
    `route_search.playable_candidates` drops one, rather than crashing the run
    the first time a question reaches for it.
    """
    table = {}
    for name, (spec, text) in {**LIBRARY, **(extra or {})}.items():
        macro = Macro(f"{name}_{frames}", macro_parts(spec), frames, text)
        try:
            macro.lines()
        except (HarnessError, ValueError) as error:
            print(f"note: the macro {name} is not offered: {error}", file=sys.stderr)
            continue
        table[macro.name] = macro
    return table


def route_macros() -> dict:
    """F14.13's window labels, imported from `route_search` when it is there.

    The whole candidate comes across, parts and all: the search's wall hop is
    two moves with fixed durations of its own, and flattening it to its first
    buttons would offer a question a move the search never made. The durations
    stay the search's (they are the ones it measured); only the window's total
    is this run's `--macro-frames`.

    The default table is ADR-0238's own seven and does *not* include these, so
    the x 987 pin this harness is measured on still stands after F14.13 lands.
    `--route-macros` is how a run asks for them.
    """
    try:
        import route_search  # noqa: PLC0415
    except Exception:  # noqa: BLE001 - a missing or broken module is not fatal
        return {}
    return {str(label): (macro_parts(parts), f"the search's own {label} window")
            for label, parts in getattr(route_search, "CANDIDATES", [])}


# --------------------------------------------------------------------------
# Cheats: ADR-0184 section 1, checked in Python before the emulator is touched.
# --------------------------------------------------------------------------

_CHEAT_RE = re.compile(r"^([0-9A-Fa-f]{1,4}):([0-9A-Fa-f]{2})(?::([0-9A-Fa-f]{2}))?$")
_GG_RE = re.compile(r"^[A-Za-z]{6}$|^[A-Za-z]{8}$")


def parse_cheat(code: str) -> str:
    """`AAAA:VV[:CC]` with `AAAA < 0x0800`, or a `CheatError` naming the code.

    Three refusals, in the order ADR-0184 section 1 lists them: the type (a Game
    Genie letter code is `NesGameGenie`; a Pro Action Rocky code is decoded into
    PRG space), the address space (`AAAA < 0x0800` - above it is a mirror, a
    register or the cartridge, and a byte substituted there can reach tile
    data), and the shape.
    """
    text = (code or "").strip()
    if not text:
        raise CheatError("an empty cheat code")
    if _GG_RE.match(text):
        raise CheatError(
            f"refused cheat {text!r}: a Game Genie letter code is a NesGameGenie "
            "code, and ADR-0184 section 1 accepts NesCustom RAM-address codes only")
    if text.count(":") > 2:
        raise CheatError(
            f"refused cheat {text!r}: not the AAAA:VV[:CC] form (a Pro Action "
            "Rocky code decodes into PRG space and is refused outright)")
    match = _CHEAT_RE.match(text)
    if not match:
        raise CheatError(
            f"refused cheat {text!r}: not the AAAA:VV[:CC] form with hex digits")
    address = int(match.group(1), 16)
    if address >= 0x800:
        raise CheatError(
            f"refused cheat {text!r}: address ${address:04X} is outside the NES's "
            "internal RAM ($0000-$07FF) - a cheat above it can change bytes the "
            "game unpacks into CHR data (ADR-0184 section 1)")
    parts = [match.group(1).upper().rjust(4, "0"), match.group(2).upper()]
    if match.group(3):
        parts.append(match.group(3).upper())
    return ":".join(parts)


def parse_cheats(codes) -> list:
    """The whole `--cheat` list, or the first refusal with its code named."""
    return [parse_cheat(code) for code in (codes or [])]


def cheats_unconfirmed(init_output, cheats) -> list:
    """The cheats a launch did *not* report applying.

    `headless_record` prints `cheat applied: <code> (RAM address, ADR-0184)`
    after `SetCheats`, and a session prints it before its `ready` - the two
    paths share one `applyCheats` lambda, so what this reads is the tool's own
    report of what it applied and not a promise that it would. The code is the
    first token after the colon; the parenthetical is the tool's, and reading
    the whole tail would make every real line look like an unconfirmed code.
    """
    applied = set()
    for line in init_output or []:
        if not line.startswith("cheat applied:"):
            continue
        rest = line.split(":", 1)[1].strip().split()
        if rest:
            applied.add(rest[0].upper().lstrip("0"))
    return [code for code in cheats if code.upper().lstrip("0") not in applied]


# --------------------------------------------------------------------------
# The RAM map: named fields, and the derived numbers a state JSON carries.
# --------------------------------------------------------------------------

_BIN_OPS = (ast.Add, ast.Sub, ast.Mult, ast.Div, ast.FloorDiv, ast.Mod)
_UNARY_OPS = (ast.UAdd, ast.USub)


def eval_field_expr(expr: str, values: dict):
    """A ram-map `expr`, evaluated over the fields computed before it.

    Deliberately an expression language and not a bare `eval`: names, numbers
    and arithmetic only, so a `ram-map.json` cannot call anything. It is here
    because the numbers a question is asked over are derived - Ryu's absolute x
    is the camera plus a screen-relative byte - and a map that could only name
    addresses would push that arithmetic into this file, per game.
    """
    try:
        tree = ast.parse(expr, mode="eval")
    except SyntaxError as error:
        raise RamMapError(f"{expr!r} is not an expression: {error}") from error
    for node in ast.walk(tree):
        #`ast.walk` reaches the operator nodes as well as the expressions
        #(a BinOp carries its `op` as a child), so the arithmetic itself is a
        #leaf this language allows.
        if isinstance(node, (ast.Expression, ast.Load, ast.BinOp, ast.UnaryOp,
                             *_BIN_OPS, *_UNARY_OPS)):
            continue
        if isinstance(node, ast.UnaryOp) and isinstance(node.op, _UNARY_OPS):
            continue
        if isinstance(node, ast.Constant) and isinstance(node.value, int | float):
            continue
        if isinstance(node, ast.Name):
            if node.id not in values:
                raise RamMapError(
                    f"{expr!r} names {node.id!r}, which is not a field of this map "
                    "computed before it")
            continue
        raise RamMapError(
            f"{expr!r} uses {type(node).__name__}, which a ram map may not")
    return eval(  # noqa: S307 - the tree above is the whitelist
        compile(tree, "<ram-map>", "eval"), {"__builtins__": {}}, dict(values))


def _as_int(value) -> int:
    """An address or a range bound: hex, whether or not it carries the `0x`.

    Both spellings are in the stage sets - Ninja Gaiden's map writes `0x0076`
    and Mega Man 3's writes the four-digit `0027` - and `int(s, 0)` refuses the
    second outright (Python reads the leading zero as an octal prefix and then
    errors on the digits). A string here is always a hex address, never a
    decimal, which is what a ram map is written in.
    """
    if isinstance(value, str):
        try:
            return int(value.strip(), 16)
        except ValueError as error:
            raise RamMapError(f"{value!r} is not a hex address") from error
    return int(value)


@dataclass
class Field:
    """One named number: an address, a range count, or an expression."""

    name: str
    address: int | None = None
    address2: int | None = None
    signed_high: bool = False
    range_start: int | None = None
    range_end: int | None = None
    reduce: str | None = None
    expr: str | None = None
    note: str = ""


def _field_from_spec(name, spec):
    """One field, or `None` for a spec that says the field is not RAM.

    `address: null` is how a map records a number it read from somewhere that is
    not an address - Mega Man 3's `state_frame` is the save state's
    `ppu.frameCount` - so there is nothing to read at question time. The entry
    is a note for a human, and `extract_fields` names it in `RamMap.not_ram`
    instead of refusing the whole map over it.
    """
    if isinstance(spec, str | int):
        return Field(name=name, address=_as_int(spec))
    if not isinstance(spec, dict):
        raise RamMapError(f"field {name!r} is {type(spec).__name__}, not a mapping")
    if "expr" in spec:
        return Field(name=name, expr=str(spec["expr"]), note=str(spec.get("note", "")))
    if "range" in spec:
        start, end = spec["range"]
        return Field(name=name, range_start=_as_int(start), range_end=_as_int(end),
                     reduce=spec.get("reduce", "count_nonzero"),
                     note=str(spec.get("note", "")))
    address = spec.get("address")
    if isinstance(address, list | tuple):
        if len(address) != 2:
            raise RamMapError(f"field {name!r} needs a 2-byte address or one address")
        return Field(name=name, address=_as_int(address[0]), address2=_as_int(address[1]),
                     signed_high=bool(spec.get("signed_high")),
                     note=str(spec.get("note", "")))
    if address is None:
        return None
    return Field(name=name, address=_as_int(address),
                 signed_high=bool(spec.get("signed_high")),
                 note=str(spec.get("note", "")))


def extract_fields(document) -> tuple:
    """The field map out of a `ram-map.json`, in the shapes a game writes it.

    Three accepted places, in order: a `fields` (or `state`/`ram`) mapping; a
    top-level mapping of specs; and a `verified` (or `ram_map`) section - which
    is the shape `scripts/stages/mm3/ram-map.json` grew, so a game's research
    notes and its field map can live in one file. A spec that carries
    `address: null` is skipped and its name returned in the second half of the
    pair: the map records a number that is not RAM, and a reader that refused
    the whole file over it would have no way to say so.

    Returns `(fields, not_ram)`, in declaration order.
    """
    if not isinstance(document, dict):
        raise RamMapError("a ram map is a JSON object")
    specs = None
    for key in ("fields", "state", "ram"):
        if isinstance(document.get(key), dict):
            specs = document[key]
            break
    if specs is None:
        specs = {name: spec for name, spec in document.items()
                 if isinstance(spec, dict) and ({"address", "expr", "range"} & set(spec))}
    if not specs:
        for section in ("verified", "ram_map"):
            entries = document.get(section)
            if isinstance(entries, dict):
                specs = {name: spec for name, spec in entries.items()
                         if isinstance(spec, dict)
                         and ({"address", "expr", "range"} & set(spec))}
                if specs:
                    break
    if not specs:
        raise RamMapError(
            "no field map: a ram map needs a `fields` mapping of name -> "
            "{address|expr|range}, or field specs at the top level")
    fields, not_ram = {}, []
    for name, spec in specs.items():
        field_ = _field_from_spec(name, spec)
        if field_ is None:
            not_ram.append(name)
        else:
            fields[name] = field_
    if not fields:
        raise RamMapError(
            "a field map whose every entry is `address: null`: this map names no "
            "number the emulator can read")
    return fields, not_ram


class RamMap:
    """A game's named RAM fields, read in one session round trip."""

    def __init__(self, fields: dict, *, progress=None, screen=None,
                 screen_width=256, room="room", hp="hp", path=None, game=None,
                 not_ram=(), run_keys=None):
        self.fields = dict(fields)
        #Run-level names a tip's `when` may carry that are not RAM addresses -
        #`stage` is the one there is (ADR-0238 section 3). They ride on every
        #`read()`, so a tip's trigger sees them exactly where it sees the
        #bytes, and they are kept out of `fields` so nothing that counts the
        #game's RAM (`verify_script`) can mistake one for a byte.
        self.run_keys = {str(name): value for name, value in (run_keys or {}).items()
                         if value is not None}
        self.progress = progress
        self.screen = screen
        self.screen_width = int(screen_width)
        self.room_field = room if room in self.fields else None
        self.hp_field = hp if hp in self.fields else None
        self.path = Path(path) if path else None
        self.game = game
        #Fields the map records as not being RAM (`address: null`): named, so a
        #reader can say what it did not read, and never asked for.
        self.not_ram = list(not_ram)
        if not self.fields:
            raise RamMapError("a ram map with no fields")
        for name in (progress, screen):
            if name and name not in self.fields:
                raise RamMapError(
                    f"the ram map names {name!r}, which is not one of its fields "
                    f"({', '.join(sorted(self.fields))})")
        if self.progress is None:
            self.progress = next(
                (name for name in ("abs_x", "camera_x", "progress") if name in self.fields),
                next(iter(self.fields)))
        self.sha256 = (hashlib.sha256(self.path.read_bytes()).hexdigest()
                       if self.path and self.path.exists() else "")

    @classmethod
    def load(cls, path, *, progress=None, screen=None, run_keys=None):
        path = Path(path)
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise RamMapError(f"could not read {path}: {error}") from error
        document = document if isinstance(document, dict) else {"fields": document}
        fields, not_ram = extract_fields(document)
        #A run key may not shadow a field: `stage` from one source and `stage`
        #from the other would leave a tip's trigger reading whichever won.
        clash = sorted(set(run_keys or {}) & set(fields))
        if clash:
            raise RamMapError(
                f"{path}: {', '.join(clash)} is both a RAM field and a run-level "
                "key, so a tip naming it would read one of them and say nothing "
                "about which")
        return cls(fields,
                   progress=progress or document.get("progress"),
                   screen=screen if screen is not None else document.get("screen"),
                   screen_width=document.get("screen_width", 256),
                   path=path, game=document.get("game"), not_ram=not_ram,
                   run_keys=run_keys)

    def read(self, emu) -> dict:
        """Every field as a number, in one `ram` request.

        The plain addresses and the ranges are asked for together, so a state
        costs one round trip however many fields a game names.
        """
        items, owners = [], []
        for field_ in self.fields.values():
            if field_.expr:
                continue
            if field_.range_start is not None:
                items.append((field_.range_start, field_.range_end))
            elif field_.address2 is not None:
                items.append((field_.address, field_.address2))
            else:
                items.append(field_.address)
            owners.append(field_.name)
        replies = emu.read_ram(*items) if items else []
        raw = dict(zip(owners, replies, strict=True))
        values = {}
        for name, field_ in self.fields.items():
            if field_.expr:
                values[name] = eval_field_expr(field_.expr, values)
            elif field_.range_start is not None:
                values[name] = count_reduced(raw[name], field_.reduce)
            elif field_.address2 is not None:
                values[name] = word_of(raw[name], field_.signed_high)
            else:
                values[name] = raw[name]
        values.update(self.run_keys)
        return {name: round(value, 2) if isinstance(value, float) else value
                for name, value in values.items()}


def count_reduced(data, reduce) -> int:
    if reduce == "count_nonzero":
        return sum(1 for byte in data if byte)
    if reduce in (None, "count"):
        return len(data)
    raise RamMapError(f"unknown range reduction {reduce!r}")


def word_of(pair, signed_high: bool) -> int:
    """A little-endian 16-bit word; `$0052` is signed, which read unsigned turns
    a clamped camera into the far right of the level."""
    lo, hi = pair[0], pair[1]
    if signed_high and hi >= 0x80:
        hi -= 256
    return hi * 256 + lo


# --------------------------------------------------------------------------
# Situation tips (ADR-0238 section 3, amended 2026-09-26).
# --------------------------------------------------------------------------

@dataclass(frozen=True)
class Condition:
    field: str
    minimum: float | None = None
    maximum: float | None = None
    equals: float | str | None = None

    def holds(self, state) -> bool:
        if self.field not in state:
            return False
        value = state[self.field]
        if self.equals is not None:
            return value == self.equals
        if self.minimum is not None and value < self.minimum:
            return False
        return not (self.maximum is not None and value > self.maximum)


@dataclass(frozen=True)
class Tip:
    id: str
    text: str
    macro: str | None
    conditions: tuple
    sources: tuple = ()
    note: str = ""

    def holds(self, state) -> bool:
        return all(condition.holds(state) for condition in self.conditions)


def _condition_from(item, path) -> Condition:
    """One trigger clause. `value` is the file's name for pinning one value and
    is read as `equals`; the value itself may be a string (a run-level `stage`
    like `"snake-man"`) or a number (`stage >= 2` written as a bound)."""
    if not isinstance(item, dict):
        raise TipsError(f"{path}: a trigger entry is {type(item).__name__}, not a mapping")
    if "field" in item:
        return Condition(str(item["field"]),
                         item.get("min"), item.get("max"),
                         item.get("equals", item.get("value")))
    # `{"abs_x": {"min": 950, "max": 1035}}` - the same thing written compactly.
    if len(item) != 1:
        raise TipsError(f"{path}: a trigger entry needs one field or a `field` key")
    (name, spec), = item.items()
    if isinstance(spec, dict):
        return Condition(str(name), spec.get("min"), spec.get("max"),
                         spec.get("equals", spec.get("value")))
    if isinstance(spec, list | tuple) and len(spec) == 2:
        return Condition(str(name), spec[0], spec[1])
    raise TipsError(f"{path}: trigger {name!r} is {type(spec).__name__}, not a range")


def _conditions_from(trigger, path) -> tuple:
    if trigger is None:
        return ()
    if isinstance(trigger, dict):
        if "any" in trigger:
            raise TipsError(
                f"{path}: an `any` trigger is refused - a tip that fires on one of "
                "two conditions is a tip nobody can predict, so write it as two tips")
        if "all" in trigger:
            return tuple(_condition_from(item, path) for item in trigger["all"])
        return (_condition_from(trigger, path),)
    if isinstance(trigger, list | tuple):
        return tuple(_condition_from(item, path) for item in trigger)
    raise TipsError(f"{path}: the trigger is {type(trigger).__name__}, not a mapping")


class Tips:
    """A game's tips, with the file hash the decision log records."""

    def __init__(self, tips, *, path=None, sha256="", extra=None):
        self.tips = list(tips)
        self.extra = list(extra or [])   # proposed by a research pass, under runs/
        self.path = Path(path) if path else None
        self.sha256 = sha256

    @property
    def all(self) -> list:
        return self.tips + self.extra

    @classmethod
    def load(cls, path, *, fields=None):
        path = Path(path)
        try:
            raw = path.read_bytes()
            document = json.loads(raw.decode("utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise TipsError(f"could not read {path}: {error}") from error
        if isinstance(document, list):
            entries, declared = document, ()
        elif isinstance(document, dict):
            entries = (document.get("tips") or document.get("situations")
                       or document.get("advice") or [])
            #A file may declare the run-level names its triggers use (`stage`,
            #which is the run's own stage-set entry and not a RAM address). A
            #key of that map is allowed - and only those: an `_comment` is a
            #comment, and a name neither the map nor `runKeys` carries is still
            #refused rather than silently skipped over.
            declared = tuple(name for name in (document.get("runKeys") or {})
                             if not str(name).startswith("_"))
        else:
            raise TipsError(f"{path}: a tips file is a list or an object with `tips`")
        allowed = None if fields is None else set(fields) | set(declared)
        tips = []
        for index, entry in enumerate(entries):
            where = f"{path} tip #{index + 1}"
            if not isinstance(entry, dict):
                raise TipsError(f"{where} is not a mapping")
            text = entry.get("tip") or entry.get("text") or entry.get("advice")
            if not isinstance(text, str) or not text.strip():
                raise TipsError(f"{where} carries no tip text")
            conditions = _conditions_from(
                entry.get("when", entry.get("trigger", entry.get("where"))), where)
            for condition in conditions:
                if allowed is not None and condition.field not in allowed:
                    raise TipsError(
                        f"{where} triggers on {condition.field!r}, which is neither a "
                        f"field of this game's ram map ({', '.join(sorted(fields))}) "
                        f"nor one of the run keys the file declares "
                        f"({', '.join(sorted(declared)) or 'none'})")
            tips.append(Tip(
                id=str(entry.get("id") or f"tip{index + 1}"),
                text=text.strip(),
                macro=entry.get("macro") or entry.get("macro_name"),
                conditions=conditions,
                sources=tuple(entry.get("sources") or ()),
                note=str(entry.get("note", "")),
            ))
        return cls(tips, path=path, sha256=hashlib.sha256(raw).hexdigest())

    def matching(self, state) -> list:
        return [tip for tip in self.all if tip.holds(state)]

    def macro_names(self) -> list:
        """The macro names the tips ask for, so a question can offer them."""
        return [tip.macro for tip in self.all if tip.macro]


def load_tips(path, *, fields=None, missing_ok=True) -> Tips:
    path = Path(path)
    if missing_ok and not path.exists():
        return Tips([], path=path)
    return Tips.load(path, fields=fields)


# --------------------------------------------------------------------------
# The loop guard's detectors, as pure functions (ADR-0238 section 3).
# --------------------------------------------------------------------------

def fingerprint(state, ram_map, *, round_to=8) -> tuple:
    """Position rounded to 8 px, camera, room, HP - the state's coarse name."""
    position = float(state.get(ram_map.progress, 0))
    rounded = int(position // round_to) * round_to
    screen = state.get(ram_map.screen, 0) if ram_map.screen else 0
    room = state.get(ram_map.room_field, 0) if ram_map.room_field else 0
    hp = state.get(ram_map.hp_field, 0) if ram_map.hp_field else 0
    return (rounded, screen, room, hp)


#The ADR's window for the cycle detector: "a period-2-to-4 cycle repeated 3
#times in the last 12 choices". Twelve is also the longest run the detector is
#allowed to look at, so a cycle that ended longer ago than that is not one.
CYCLE_WINDOW = 12


def cycle_in(choices, *, max_period=4, repeats=3, window=CYCLE_WINDOW):
    """The macros of a period-2..4 cycle repeated `repeats` times at the tail of
    the last `window` choices.

    `choices[i] == choices[i + period]` for `period * repeats` steps back means
    the tail is that cycle repeated. Returns `(period, macros)` or `None`. A
    constant run is not a period-2 cycle - that is the watermark detector's
    finding, and reporting it here as a cycle would ban one macro for a reason
    that has nothing to do with it.
    """
    recent = list(choices)[-window:]
    for period in range(2, max_period + 1):
        span = period * repeats
        if len(recent) < span:
            continue
        tail = recent[-span:]
        if len(set(tail)) < 2:
            continue
        if all(tail[i] == tail[i + period] for i in range(span - period)):
            return period, sorted(set(tail))
    return None


# --------------------------------------------------------------------------
# The run itself.
# --------------------------------------------------------------------------

class Head(NamedTuple):
    """Where the committed path stands: the live state, and what it took."""

    frame: int
    handle: int
    state: dict
    lines: tuple
    screen: int

    @property
    def t(self) -> float:
        return self.frame / FPS


@dataclass
class Checkpoint:
    """One emulated second of the committed path, kept for the rewind ladder."""

    t: float
    frame: int
    handle: int
    state: dict
    progress: float
    lines: int
    screen: int


@dataclass
class StallState:
    """Everything one stall knows, across the rungs and the questions."""

    start_t: float
    watermark: float
    frames: float = 0.0     # emulated frames played during this stall
    rung: int = 0
    questions: int = 0
    decisions: int = 0
    loops: int = 0
    reasons: list = field(default_factory=list)
    fingerprints: list = field(default_factory=list)
    choices: list = field(default_factory=list)
    tried: dict = field(default_factory=dict)     # checkpoint t -> [(macro, progress, death)]
    banned: dict = field(default_factory=dict)    # checkpoint t -> {macro: reason}
    research: object = None

    def tried_at(self, t) -> list:
        return self.tried.setdefault(round(t, 3), [])

    def banned_at(self, t) -> dict:
        return self.banned.setdefault(round(t, 3), {})


LADDER = (1, 2, 4, 8, 16)


@dataclass
class Summary:
    reason: str
    script: list
    frames: int
    decisions: int
    loops: int
    stalls: int
    spend_usd: float
    wall_s: float
    emulated_s: float
    goal_reached: bool
    played_frames: int = 0
    checkpoints: list = field(default_factory=list)
    chain_goal_reached: bool = False
    chain_frames: int = 0
    out: object = None
    verified: object = None

    def as_json(self) -> dict:
        return {
            "reason": self.reason,
            "out": self.out,
            "script_lines": len(self.script),
            "script_frames": self.frames,
            "played_frames": self.played_frames,
            "emulated_seconds": round(self.emulated_s, 2),
            "wall_seconds": round(self.wall_s, 2),
            "speed_vs_real_time": (round(self.emulated_s / self.wall_s, 2)
                                   if self.wall_s else None),
            "decisions": self.decisions,
            "loops": self.loops,
            "stalls": self.stalls,
            "spend_usd": round(self.spend_usd, 6),
            "goal_reached": self.goal_reached,
            #The search plays a *chain* of play calls; the artifact is one flat
            #script, and only the flat replay of it is measured (measure_script).
            #When the two disagree the run says so instead of shipping the claim.
            "chain_goal_reached": self.chain_goal_reached,
            "chain_frames": self.chain_frames,
            "checkpoints": self.checkpoints,
            "verified": self.verified,
        }


class Harness:
    """One run: a session, a base search, a stall helper and a loop guard."""

    def __init__(self, emu, ram_map, *, start_state=None, start_handle=None,
                 client=None, tips=None, macros=None, frames=15, work=None,
                 stall_seconds=5.0, settle_seconds=1.5, ring_seconds=24.0,
                 max_questions_per_rung=3, max_decisions_per_stall=18,
                 max_stalls=8, max_emulated_seconds=600.0, budget_usd=1.0,
                 goal=None, log_path=None, dashboard=None, research=None,
                 research_timeout=None, clock=time.monotonic):
        self.emu = emu
        self.ram_map = ram_map
        self.frames = int(frames)
        self.macros = dict(macros if macros is not None else macro_table(self.frames))
        self.base_names = [name for name in self.macros
                           if name.rsplit("_", 1)[0] in BASE_MACROS]
        self.tips = tips if tips is not None else Tips([])
        self.client = client
        self.work = Path(work) if work else DEFAULT_WORK
        self.settle_seconds = float(settle_seconds)
        self.ring_seconds = float(ring_seconds)
        self.stall_seconds = float(stall_seconds)
        self.max_questions_per_rung = int(max_questions_per_rung)
        self.max_decisions_per_stall = int(max_decisions_per_stall)
        self.max_stalls = int(max_stalls)
        self.max_emulated_seconds = float(max_emulated_seconds)
        self.budget_usd = float(budget_usd)
        self.goal = goal
        self.log_path = Path(log_path) if log_path else None
        self.dashboard = dashboard if dashboard is not None else sys.stderr
        self.research = research or self._research_subprocess
        self.research_timeout = research_timeout
        self.clock = clock
        self.start_state = start_state
        self.start_handle = start_handle
        self.ring = []
        self.seen_fingerprints = set()
        self.recent_novel = []
        #Macros a research pass proposed: they joined the fixed set, so a later
        #question offers them without a tip having to name them.
        self.researched_macros = []
        self.stalls = 0
        self.loop_count = 0
        self.head = None
        self._lines = []
        self._watermark = 0.0
        self._last_progress_t = 0.0
        self._last_progress_ckpt = None
        self._screen = None
        self._screen_ckpt = None
        #Two clocks, and they measure different things. `_played_frames` is what
        #the emulator actually emulated - every candidate of every base step -
        #and it is the honest cost, so the emulated-seconds cap and the speed
        #ratio use it. `_stuck_frames` is the other one: frames of the *run*
        #that bought no progress, which is what "stuck for N emulated seconds"
        #means to anyone reading the script, and what the rewind ladder is
        #measured in. They are kept apart because the probes of one base step
        #are seven eighths of the work and none of the run, and a stall clock
        #fed by the probes would open a ladder whose every rung is behind the
        #last real progress and would therefore never rewind at all.
        self._played_frames = 0
        self._stuck_frames = 0
        self._active_stall = None

    # ---- handles ---------------------------------------------------------

    def _held_by_ring(self, handle) -> bool:
        return any(c.handle == handle for c in self.ring) or handle == self.start_handle

    def _maybe_drop(self, handle) -> None:
        """Forget a state nobody owns any more. A ring entry owns its own.

        The head is an owner too, and that is not a formality: the callers that
        move the head before committing (`_settle`, the stall helper) hand this
        the very handle they just made the head, and a version without this
        guard dropped the state the run was standing on.
        """
        if (handle is None or handle == self.start_handle
                or self._held_by_ring(handle)
                or (self.head is not None and handle == self.head.handle)):
            return
        try:
            self.emu.drop(handle)
        except Exception:  # noqa: BLE001 - a session that forgot a handle is no failure
            pass

    def _keep_checkpoint(self, head: Head) -> None:
        if self.ring and head.t - self.ring[-1].t < 1.0:
            return
        self.ring.append(Checkpoint(t=head.t, frame=head.frame, handle=head.handle,
                                    state=dict(head.state),
                                    progress=self._progress_of(head.state),
                                    lines=len(head.lines), screen=head.screen))
        while self.ring and head.t - self.ring[0].t > self.ring_seconds:
            self._maybe_drop(self.ring.pop(0).handle)

    # ---- the committed path ----------------------------------------------

    def _progress_of(self, state) -> float:
        return float(state[self.ram_map.progress])

    def _screen_of(self, state) -> int:
        """Which screen of the level a state is standing on (0 when unmapped)."""
        if not self.ram_map.screen:
            return 0
        return int(float(state.get(self.ram_map.screen, 0)) // self.ram_map.screen_width)

    def _played(self, frames) -> None:
        """Count emulated frames the emulator actually ran, stall and all."""
        self._played_frames += float(frames)

    def _play_macro(self, macro) -> int:
        """Play a macro's whole window, part by part, plus the boundary frame.

        The parts are the search's own move (a wall hop is off the wall and then
        back at it) and the last one holds to the end of the window, so the
        window is always exactly `macro.frames` frames of input whatever the
        parts are - which is what the frame accounting and the ladder's seconds
        are measured in.

        The boundary frame is the one a one-shot run adds past its script and
        `render_script` writes after every macro, and `play_window` puts it in
        the window, so the whole chain is the same play as the flat script the
        run ships - and the flat replay is the one that decides `goal_reached`
        (see `render_script` and `measure_script`). It used to be played by
        accident: before issue #543 the `ram` read after every window advanced
        the emulated frame by one, and with the read inert the chain is one
        frame per macro short of its own artifact unless it asks for it.

        Returns the frames the session parked on; the frames it *ran* are
        `window_frames(macro)`, which is what the frame-counting callers want.
        """
        #One window, one request: the parts as one script, run through
        #play_window so the boundary frame is part of the window and the
        #window is the flat script's own play of it.
        played = self.emu.play_window(list(macro.lines()), macro.frames)
        self._played(macro.frames + step_emu.IDLE_FRAMES)
        return played

    def _read_head(self, handle, frame) -> Head:
        state = self.ram_map.read(self.emu)
        return Head(frame=frame, handle=handle, state=state, lines=tuple(self._lines),
                    screen=self._screen_of(state))

    def _bootstrap(self) -> Head:
        if self.start_handle is None:
            self.start_handle = self.emu.load_file(self.start_state)
        self._lines = []
        head = self._read_head(self.start_handle, 0)
        self._watermark = self._progress_of(head.state)
        self._screen = head.screen
        self.head = head
        self._keep_checkpoint(head)
        return head

    def _commit(self, head: Head) -> Head:
        """Move the committed path onto a state one macro further along.

        The state is the caller's, never a re-read: `_base_step` leaves the
        session parked back on the state it probed from, so RAM read here would
        be that state's and the commit would carry the frame of one state and
        the numbers of another - which is exactly the bug this signature fixed.
        """
        previous = self.head
        progress = self._progress_of(head.state)
        if int(progress) > int(self._watermark):
            self._watermark = progress
            self._last_progress_t = head.t
            self._last_progress_ckpt = self.ring[-1] if self.ring else None
            self._stuck_frames = 0
        else:
            #A frame of the run that bought nothing. The base search commits its
            #best legal move whether or not it advanced, which is what the run
            #would look like played back - and those are the frames the stall
            #clock counts.
            self._stuck_frames += head.frame - (previous.frame if previous else 0)
        if head.screen != self._screen:
            self._screen = head.screen
            self._screen_ckpt = self.ring[-1] if self.ring else None
        self.head = head
        self._keep_checkpoint(head)
        if previous is not None and previous.handle != head.handle:
            self._maybe_drop(previous.handle)
        return head

    def _truncate_to(self, checkpoint: Checkpoint) -> None:
        """Drop the checkpoints the rewind made untrue, and re-derive the marks."""
        while self.ring and self.ring[-1].t > checkpoint.t + 1e-6:
            self._maybe_drop(self.ring.pop().handle)
        self._watermark = max([c.progress for c in self.ring] + [checkpoint.progress])
        self._last_progress_ckpt = max(self.ring,
                                       key=lambda c: c.progress) if self.ring else None
        self._screen = checkpoint.screen
        self._screen_ckpt = next((c for c in reversed(self.ring)
                                  if c.screen != checkpoint.screen), None)

    # ---- the base search -------------------------------------------------

    def _rank(self, state) -> tuple:
        """`route_search.rank` over the same names when it is importable."""
        try:
            import route_search  # noqa: PLC0415
            keys = ("abs_x", "hp", "score_hi", "score_lo", "lives")
            if all(key in state for key in keys) and hasattr(route_search, "rank"):
                return route_search.rank(state)
        except Exception:  # noqa: BLE001 - a broken module falls back to ours
            pass
        return (int(self._progress_of(state)) // 8, state.get("hp", 0),
                state.get("score", 0), state.get("lives", 0))

    def _base_step(self, head: Head, names) -> tuple | None:
        """Try every macro from `head`; return the best `(macro, handle, state)`.

        A candidate that costs a life is dropped, the way the ported search
        drops one: a death is not progress, and a search that walks into one
        twice has learned nothing.
        """
        best = None
        for name in names:
            macro = self.macros[name]
            self.emu.restore(head.handle)
            self._play_macro(macro)
            state = self.ram_map.read(self.emu)
            lives = state.get("lives", 1)
            if lives == 0 or lives < head.state.get("lives", lives):
                continue
            candidate = (self._rank(state), name, self.emu.save(), state)
            if best is None or candidate[0] > best[0]:
                if best is not None:
                    self._maybe_drop(best[2])
                best = candidate
            else:
                self._maybe_drop(candidate[2])
        self.emu.restore(head.handle)
        if best is None:
            return None
        _rank, name, handle, state = best
        return self.macros[name], handle, state

    # ---- the loop guard --------------------------------------------------

    def _note_state(self, state) -> bool:
        """Record a fingerprint; True when it is one never seen before."""
        fp = fingerprint(state, self.ram_map)
        novel = fp not in self.seen_fingerprints
        self.seen_fingerprints.add(fp)
        self.recent_novel.append(1 if novel else 0)
        del self.recent_novel[:-20]
        return novel

    @property
    def novelty(self) -> float:
        if not self.recent_novel:
            return 1.0
        return sum(self.recent_novel) / len(self.recent_novel)

    def _loop_flags(self, stall: StallState, state, *, choice=None):
        """The three detectors, evaluated once per decision."""
        flags = []
        fp = fingerprint(state, self.ram_map)
        stall.fingerprints.append(fp)
        seen = stall.fingerprints.count(fp)
        if seen >= 3:
            flags.append(f"fingerprint {fp} seen {seen}x")
        if stall.frames >= 60.0 * FPS:
            flags.append(f"watermark has not risen in {stall.frames / FPS:.0f} "
                         "emulated seconds")
        if choice is not None:
            stall.choices.append(choice)
        cycle = cycle_in(stall.choices)
        if cycle:
            flags.append(f"period-{cycle[0]} cycle {cycle[1]} x3 in the last 12")
        return flags, cycle

    # ---- the stall helper ------------------------------------------------

    def _checkpoint_at(self, target_t: float) -> Checkpoint | None:
        """The newest checkpoint at or before `target_t`, never past the floor.

        The floor is the start of the current screen or the last real progress,
        whichever is later. A rung that would land before it is *clamped* to it
        rather than dropped, and that is a reading of ADR-0238 section 3 that the
        measurements forced: a stall is entered after `--stall-seconds` of
        emulated play, so the last real progress is only a fraction of a second
        of path behind the head, and a ladder that refused every rung inside that
        margin would never ask a question at all. Clamping keeps both clauses
        true - the run never rewinds past either mark, and a rung is always a
        checkpoint - at the price of the deeper rungs landing on the same one.
        """
        floor = max([c.t for c in (self._screen_ckpt, self._last_progress_ckpt)
                     if c is not None] or [0.0])
        target_t = max(target_t, floor)
        candidates = [c for c in self.ring if c.t <= target_t + 1e-6]
        return candidates[-1] if candidates else None

    def macro_named(self, name):
        """The table's entry for a tip's or a worker's macro name.

        A tip names the *move* ("JUMP_LEFT"), never a duration; the count is this
        run's fixed one, so the name a question offers is `<move>_<frames>`.
        """
        if name in self.macros:
            return self.macros[name]
        return self.macros.get(f"{name}_{self.frames}")

    def _question(self, checkpoint: Checkpoint, stall: StallState):
        """The macros this checkpoint has not already refused, and their text."""
        banned = dict(stall.banned_at(checkpoint.t))
        for macro, progress, death in stall.tried_at(checkpoint.t):
            banned[macro] = (f"tried here: reached {progress:.0f}"
                             + (", and died" if death else ""))
        state = dict(checkpoint.state)
        state[self.ram_map.progress] = checkpoint.progress
        tips = self.tips.matching(state)
        #ADR-0238 section 3 gives a tip's macro a place in the fixed table, and
        #this is where it reaches a question - only while that tip's trigger
        #holds, so a boss move is not offered in a corridor. The base search
        #keeps to its own seven either way (that is what leaves the wall pin for
        #Jev to find, which is the whole point of F14.14).
        #
        #A tip writes the *move* ("JUMP_LEFT") and a question offers the table's
        #name for it ("JUMP_LEFT_15"), so the two are matched through the table
        #and never by string equality: comparing them directly matches nothing,
        #which folds every tip into every option and makes all seven choices read
        #the same.
        named = {tip.id: self.macro_named(tip.macro) for tip in tips if tip.macro}
        tip_macros = [macro.name for macro in named.values() if macro is not None]
        names = [name for name in dict.fromkeys(
            list(self.base_names) + tip_macros + self.researched_macros)
            if name not in banned]
        if len(names) < 2:
            return None
        criteria = {}
        for name in names:
            text = self.macros[name].describe()
            folded = [tip for tip in tips
                      if named.get(tip.id) is not None and named[tip.id].name == name]
            if folded:
                text += " " + " ".join(f"Tip: {tip.text}" for tip in folded)
            criteria[name] = text
        #A tip that names no macro, or names one this question cannot offer (it
        #was withdrawn here, or banned by the loop guard), is advice about
        #whatever is chosen, so it goes on every option.
        unfolded = [tip for tip in tips
                    if not tip.macro or named.get(tip.id) is None
                    or named[tip.id].name not in names]
        if unfolded:
            extra = " ".join(f"Tip: {tip.text}" for tip in unfolded)
            criteria = {name: f"{text} {extra}" for name, text in criteria.items()}
        return names, criteria, tips

    def _ask(self, checkpoint: Checkpoint, stall: StallState, *, rung: int):
        question = self._question(checkpoint, stall)
        if question is None:
            return None
        _names, criteria, tips = question
        tried = [{"macro": macro, "reached": round(progress, 1), "died": bool(death)}
                 for macro, progress, death in stall.tried_at(checkpoint.t)]
        state = dict(checkpoint.state)
        state[self.ram_map.progress] = checkpoint.progress
        state.update({
            "game": self.ram_map.game or "",
            "checkpoint_at_s": round(checkpoint.t, 1),
            "rewound_seconds": round(stall.start_t - checkpoint.t, 1),
            "stuck_seconds": round(stall.frames / FPS, 1),
            "watermark": round(stall.watermark, 1),
            "attempts_here": len(tried),
            "tried_here": tried,
            "tips": [tip.id for tip in tips],
        })
        instructions = (
            "A local search has made no progress for several emulated seconds, so "
            "the run has been rewound to the state in `state` and one of the "
            "offered macros will be played from it, each for a fixed number of "
            "frames. Choose the single macro most likely to get past this spot. "
            "Macros already tried from this same state are listed in `tried_here` "
            f"and are not offered: {len(tried)} of them."
        )
        started = self.clock()
        decision = self.client.ask("next_action", state=state,
                                  instructions=instructions, criteria=criteria)
        stall.decisions += 1
        self._dashboard({
            "t": round(self.head.t, 1),
            "watermark": round(stall.watermark, 1),
            "watermark_age_s": round(self.head.t - self._last_progress_t, 1),
            "novelty": round(self.novelty, 2),
            "loops": stall.loops,
            "decisions": stall.decisions,
            "decisions_cap": self.max_decisions_per_stall,
            "spend_usd": round(getattr(self.client, "spent_usd", 0.0), 6),
            "rung_s": rung,
            "question": stall.questions + 1,
            "checkpoint_t": round(checkpoint.t, 1),
            "choice": decision.choice,
            "tips_sha256": self.tips.sha256[:16],
            "tips_ids": [tip.id for tip in tips],
            "latency_s": round(self.clock() - started, 3),
        })
        return decision, criteria

    def _dashboard(self, record) -> None:
        line = (f"t={record['t']:.1f}s wm={record['watermark']:.0f} "
                f"(age={record['watermark_age_s']:.1f}s) "
                f"novelty={record['novelty']:.2f} loops={record['loops']} "
                f"dec={record['decisions']}/{record['decisions_cap']} "
                f"rung={record['rung_s']}s q={record['question']} "
                f"choice={record['choice']} spend=${record['spend_usd']:.6f}")
        if self.dashboard is not None:
            print(line, file=self.dashboard, flush=True)
        self._log_jsonl(record)

    def _log_jsonl(self, record) -> None:
        if not self.log_path:
            return
        self.log_path.parent.mkdir(parents=True, exist_ok=True)
        with self.log_path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(record, sort_keys=True, ensure_ascii=False) + "\n")

    def _research_subprocess(self, report) -> dict:
        """One `claude -p` web-search worker, through the helper module."""
        import jev_stall_research  # noqa: PLC0415
        options = {"out_dir": self.work / "research"}
        if self.research_timeout:
            options["timeout"] = float(self.research_timeout)
        return jev_stall_research.research(report, **options)

    def _report(self, stall: StallState, checkpoint: Checkpoint) -> dict:
        return {
            "game": self.ram_map.game or "",
            "progress_field": self.ram_map.progress,
            "spot": {
                "position": round(checkpoint.progress, 1),
                "position_now": round(self._progress_of(self.head.state), 1),
                "state": checkpoint.state,
            },
            "macros_available": {name: self.macros[name].describe()
                                 for name in self.base_names},
            "tried": {str(t): [{"macro": m, "reached": round(p, 1), "died": bool(d)}
                               for m, p, d in entries]
                      for t, entries in stall.tried.items()},
            "loops": stall.loops,
            "tips_ids": [tip.id for tip in self.tips.all],
            "stall_seconds": round(self.head.t - stall.start_t, 1),
        }

    def _merge_research(self, proposals) -> int:
        """Fold a research pass's proposals in, keeping the durations ours."""
        if not isinstance(proposals, dict):
            return 0
        added = 0
        for macro in proposals.get("macros") or []:
            if not isinstance(macro, dict):
                continue
            name = str(macro.get("name") or "").strip()
            buttons = str(macro.get("buttons") or "").strip()
            if not name or not buttons:
                continue
            try:
                step_emu.button_spec(buttons)
            except ValueError:
                continue
            self.researched_macros.append(f"{name}_{self.frames}")
            #The duration is this file's, whatever the worker proposed: ADR-0238
            #section 3 fixes it in code, never in the model's answer. Its buttons
            #are one hold to the end of that window - a proposal is not the place
            #for a multi-part move.
            self.macros[f"{name}_{self.frames}"] = Macro(
                f"{name}_{self.frames}", macro_parts(buttons), self.frames,
                str(macro.get("description") or f"hold {buttons}"))
            added += 1
        for tip in proposals.get("tips") or []:
            if not isinstance(tip, dict):
                continue
            text = str(tip.get("tip") or tip.get("text") or "").strip()
            if not text:
                continue
            try:
                conditions = _conditions_from(
                    tip.get("when", tip.get("trigger")), "research")
            except TipsError:
                continue
            self.tips.extra.append(Tip(
                id=str(tip.get("id") or f"research{len(self.tips.extra) + 1}"),
                text=text, macro=tip.get("macro"), conditions=conditions,
                sources=tuple(tip.get("sources") or ()),
                note=str(tip.get("note", ""))))
            added += 1
        if added:
            self._log_jsonl({"event": "research", "added": added,
                             "macros": list(proposals.get("macros") or []),
                             "tips": [t.get("id") for t in proposals.get("tips") or []]})
        return added

    def _stall(self, head: Head):
        """Run one stall to its outcome. Returns `(head | None, reason)`."""
        self.stalls += 1
        self._last_progress_t = max(self._last_progress_t, head.t)
        stall = StallState(start_t=head.t, watermark=self._watermark)
        self._active_stall = stall
        try:
            outcome, why = self._run_stall(head, stall)
        finally:
            self._active_stall = None
        self.loop_count += stall.loops
        self._log_jsonl({"event": "stall", "reason": why, "stalls": self.stalls,
                         "loops": stall.loops, "decisions": stall.decisions,
                         "flags": stall.reasons})
        return outcome, why

    def _run_stall(self, head: Head, stall: StallState):
        while True:
            if stall.decisions >= self.max_decisions_per_stall:
                return None, "decisions-cap"
            if getattr(self.client, "spent_usd", 0.0) >= self.budget_usd:
                return None, "budget"
            if stall.rung >= len(LADDER):
                if stall.research is not None:
                    #One research pass per stall, and the ladder is only spent
                    #once it has been walked again with the proposals in hand.
                    return None, "exhausted"
                checkpoint = self._checkpoint_at(head.t - LADDER[0]) or (
                    self.ring[0] if self.ring else None)
                if checkpoint is None:
                    return None, "exhausted"
                stall.research = self.research(self._report(stall, checkpoint)) or {}
                self._merge_research(stall.research)
                stall.rung = 0
                stall.questions = 0
                continue
            rung = LADDER[stall.rung]
            checkpoint = self._checkpoint_at(head.t - rung)
            if checkpoint is None:
                stall.rung += 1
                stall.questions = 0
                continue
            asked = self._ask(checkpoint, stall, rung=rung)
            if asked is None:
                stall.rung += 1
                stall.questions = 0
                continue
            decision, _criteria = asked
            macro = self.macros.get(decision.choice)
            if macro is None:
                raise HarnessError(
                    f"Jev chose {decision.choice!r}, which this table does not carry")
            played, reached, died = self._attempt(checkpoint, macro, stall)
            flags, cycle = self._loop_flags(stall, played.state, choice=macro.name)
            if flags:
                stall.loops += 1
                stall.reasons.extend(flags)
                for name in (cycle[1] if cycle else []):
                    stall.banned_at(checkpoint.t)[name] = "loop guard"
                self._log_jsonl({"event": "loop", "loops": stall.loops, "flags": flags,
                                 "t": round(played.t, 1)})
                if stall.loops >= 3:
                    self._maybe_drop(played.handle)
                    return None, "loop"
                if stall.loops == 2 and stall.research is None:
                    stall.research = self.research(self._report(stall, checkpoint)) or {}
                    self._merge_research(stall.research)
                self._maybe_drop(played.handle)
                self._rewind_to(checkpoint)
                stall.rung += 1
                stall.questions = 0
                continue
            if int(self._progress_of(played.state)) > int(stall.watermark):
                return played, "passed"
            #Failed here: the macro is withdrawn from this checkpoint's questions.
            stall.tried_at(checkpoint.t).append((macro.name, reached, died))
            stall.questions += 1
            self._rewind_to(checkpoint)
            if stall.questions >= self.max_questions_per_rung:
                stall.rung += 1
                stall.questions = 0

    def _rewind_to(self, checkpoint: Checkpoint) -> None:
        """Put the committed path and the session back on a checkpoint."""
        self.emu.restore(checkpoint.handle)
        self._truncate_to(checkpoint)
        self._lines = list(self._lines[:checkpoint.lines])
        self.head = Head(frame=checkpoint.frame, handle=checkpoint.handle,
                         state=dict(checkpoint.state), lines=tuple(self._lines),
                         screen=checkpoint.screen)

    def _attempt(self, checkpoint: Checkpoint, macro: Macro, stall: StallState):
        """Play Jev's macro from a checkpoint, and judge it with a settle window.

        A wall hop leaves the spot on its own frames and the distance it buys
        only shows up afterwards, so a macro is never judged on the frame it
        ends on: the base search runs on from there for `--settle-seconds`, and
        the attempt counts as progress if the watermark rises anywhere in that
        window. The lines the window commits are part of the winning path, and
        `_rewind_to` throws them away when the attempt failed.

        Returns `(head, reached, died)` - the state the attempt ended on (already
        committed when it worked), the furthest progress it reached, and whether
        it cost a life.
        """
        self._rewind_to(checkpoint)
        target = int(stall.watermark)
        self._play_macro(macro)
        stall.frames += window_frames(macro)
        self._lines = list(self._lines) + [macro.line()]
        state = self.ram_map.read(self.emu)
        head = Head(frame=checkpoint.frame + window_frames(macro),
                    handle=self.emu.save(),
                    state=state, lines=tuple(self._lines), screen=checkpoint.screen)
        self.head = head
        self._note_state(state)
        reached = self._progress_of(state)
        if int(reached) > target:
            return self._commit(head), reached, False
        deadline = head.frame + int(self.settle_seconds * FPS)
        while head.frame < deadline:
            step = self._base_step(head, self.base_names)
            if step is None:
                break
            follow, handle, follow_state = step
            self._lines = list(head.lines) + [follow.line()]
            head = self._commit(
                Head(frame=head.frame + window_frames(follow), handle=handle,
                     state=follow_state,
                     lines=tuple(self._lines), screen=self._screen_of(follow_state)))
            reached = max(reached, self._progress_of(follow_state))
            if int(reached) > target:
                return head, reached, False
        died = head.state.get("lives", 1) < checkpoint.state.get("lives", 1)
        return head, reached, died

    # ---- the run ---------------------------------------------------------

    def run(self) -> Summary:
        started = self.clock()
        head = self._bootstrap()
        reason, goal = "caps", False
        while True:
            if self._played_frames >= self.max_emulated_seconds * FPS:
                reason = "emulated-seconds-cap"
                break
            if self.goal and self._goal_reached(head.state):
                reason, goal = "goal", True
                break
            if getattr(self.client, "spent_usd", 0.0) >= self.budget_usd:
                reason = "budget"
                break
            #A search with nothing legal left to play is stalled in every sense,
            #whether or not the clock has run out yet - so both doors lead to
            #the same helper.
            stalled_now = self._stuck_frames >= self.stall_seconds * FPS
            step = None if stalled_now else self._base_step(head, self.base_names)
            if stalled_now or step is None:
                if self.stalls >= self.max_stalls:
                    reason = "stall-cap"
                    break
                outcome, why = self._stall(head)
                if outcome is None:
                    reason = why
                    break
                head = outcome
                continue
            macro, handle, state = step
            self._lines = list(head.lines) + [macro.line()]
            self._note_state(state)
            head = self._commit(
                Head(frame=head.frame + window_frames(macro), handle=handle, state=state,
                     lines=tuple(self._lines), screen=self._screen_of(state)))
        wall = self.clock() - started
        self.head = head
        #Four points, not three: one of them is frame 0, which --verify skips
        #(a one-shot's zero-frame run is its own boot slack), so the run still
        #replays three frames of the artifact through a run with no AI.
        measured = self.measure_script(list(head.lines), kind=4)
        flat_goal = bool(self.goal) and self._goal_reached(measured["final"])
        if reason == "goal" and not flat_goal:
            #The chain of play calls got there; the flat script of the same
            #inputs does not, so there is no artifact to ship - say it.
            reason = "chain-not-reproduced"
        return Summary(reason=reason, script=list(head.lines),
                       frames=measured["frames"], played_frames=self._played_frames,
                       decisions=getattr(self.client, "calls", 0),
                       loops=self.loop_count, stalls=self.stalls,
                       spend_usd=getattr(self.client, "spent_usd", 0.0), wall_s=wall,
                       emulated_s=self._played_frames / FPS,
                       goal_reached=flat_goal if self.goal else goal,
                       chain_goal_reached=goal, chain_frames=head.frame,
                       checkpoints=[{"frame": frame, "state": dict(state)}
                                    for frame, state in measured["checkpoints"]])

    def _goal_reached(self, state) -> bool:
        field_, _, threshold = str(self.goal).partition(":")
        if field_ not in state:
            raise HarnessError(f"--goal names {field_!r}, which is not a field of this map")
        return float(state[field_]) >= float(threshold)

    def measure_script(self, lines, *, kind=3) -> dict:
        """Replay the emitted script flat, and report what it actually does.

        This is the artifact's own measurement, and it is not a formality. A path
        the search *played* is a chain of `play` calls; the artifact is one flat
        script, and the two only agree when the script carries the input-neutral
        boundary frame `render_script` writes (the same convention
        `scripts/route_search.py` uses between candidates). Only a replay of the
        whole script as one text, from the minted state, says what a reader of
        `scripts/stages/<game>/*.txt` would see - which is the only thing that
        counts (ADR-0238 section 4: replay never calls Jev, and the script is
        the artifact).

        Returns `{"frames", "final", "checkpoints": [(frame, state)]}` with the
        states read at up to `kind` points spread over the script, the first of
        them the minted state itself.
        """
        text = render_script(lines)
        total = sum(int(line.split("f")[0]) for line in text.splitlines() if line)
        wanted = sorted({0, *[round(total * index / (kind - 1)) for index in range(kind)]}
                        if kind > 1 else {0, total})
        self.emu.restore(self.start_handle)
        loaded = self.emu.load_script(text)
        if loaded != total:
            raise HarnessError(
                f"the script measures {loaded} frames, its lines say {total}")
        walked, checkpoints = 0, []
        for frame in wanted:
            if frame > walked:
                #run_exact, not run: a `run` from the just-loaded start state
                #covers one frame more than it asks for (the state's own), so
                #the first checkpoint of every replay would be read a frame
                #late - and the chain this is compared against (`play_window`)
                #does not have that bias, which is how `chain_goal_reached` and
                #`goal_reached` could disagree over one idle frame.
                self.emu.run_exact(frame - walked)
                walked = frame
            checkpoints.append((frame, self.ram_map.read(self.emu)))
        return {"frames": total, "final": dict(checkpoints[-1][1]), "checkpoints": checkpoints}

# --------------------------------------------------------------------------
# Output, replay and the CLI.
# --------------------------------------------------------------------------

def _parse_macro_extra(text) -> dict:
    """`NAME=buttons[:text]` extra macros, for a game whose tips need one."""
    extra = {}
    for item in text or []:
        name, sep, rest = item.partition("=")
        if not sep:
            raise HarnessError(f"--macro expects NAME=BUTTONS[:TEXT], got {item!r}")
        buttons, _, description = rest.partition(":")
        extra[name.strip()] = (buttons.strip(),
                              description.strip() or f"hold {buttons.strip()}")
    return extra


#The boundary frame, shared with the search and the session module so there is
#one spelling of it: `step_emu.IDLE_LINE` is `1f -`, `IDLE_FRAMES` is the one
#frame it costs.
IDLE_FRAMES = step_emu.IDLE_FRAMES
IDLE_LINE = step_emu.IDLE_LINE


def window_frames(macro) -> int:
    """The frames one macro costs the session: its own window plus the boundary
    frame `_play_macro` plays. Every frame-counting caller uses this and not
    `macro.frames`, or the committed path's frame would drift one behind the
    session it describes, one macro at a time."""
    return macro.frames + IDLE_FRAMES


def render_script(lines, *, idle=True) -> str:
    """The macro lines as one input script, with an input-neutral boundary.

    This is the artifact, so the convention matters. The search plays a *chain*
    of play calls - one macro per call, each with its own `load_script` - and
    the shipped script is one flat text; the two are only the same play when
    every macro is followed by an input-neutral boundary frame. One `1f -` after
    each macro is the same boundary `scripts/route_search.py` writes between its
    candidates (and plays, through `step_emu.play_window`), and the chain plays
    it too (`_play_macro`), so the flat replay reproduces the chain exactly: 77
    macros, 1232 frames, abs x 991 on the Ninja Gaiden Act 1-1 run
    (`runs/f1414/e2e4/route.txt`), which is what `--verify` then replays through
    a one-shot run.

    Measured 2026-09-26 (Ninja Gaiden Act 1-1, `runs/f1414/e2e2/route.txt`):
    plain 1155 frames -> abs x 978; with the boundary 1232 frames -> abs x 991.
    The 77-frame difference is exactly one frame per macro, and it is the script
    that knows where Ryu is - which is why the flat replay, not the chain,
    decides `goal_reached`. The chain reached 991 without playing the boundary
    frame then; since issue #543's fix it plays that frame itself, so the chain
    and the script are the same play and `chain-not-reproduced` is a real
    disagreement rather than a bookkeeping difference.
    """
    if not lines:
        return ""
    if idle:
        return "".join(f"{line}\n{IDLE_LINE}\n" for line in lines)
    return "".join(f"{line}\n" for line in lines)


def write_script(path, lines, *, idle=True) -> Path:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(render_script(lines, idle=idle), encoding="utf-8")
    return path


def read_ram_fields(ram_map, ram) -> dict:
    """Every named field out of a 2 KB RAM dump, exprs in declaration order.

    The same arithmetic `RamMap.read` does over a live session, so a replay is
    compared against the run field for field and not only byte for byte.
    """
    values = {}
    for name, field_ in ram_map.fields.items():
        if field_.expr:
            values[name] = round(eval_field_expr(field_.expr, values), 2)
        elif field_.address2 is not None:
            values[name] = word_of((ram[field_.address], ram[field_.address2]),
                                   field_.signed_high)
        elif field_.range_start is not None:
            values[name] = count_reduced(
                ram[field_.range_start:field_.range_end + 1], field_.reduce)
        else:
            values[name] = ram[field_.address]
    return values


def run_one_shot(rom, prefix, frame, *, state=None, script=None, save_state=None,
                 binary=None, one_shot=None):
    """One plain `headless_record` run of `frame` frames: the replay, no Jev."""
    if one_shot is not None:
        return one_shot(rom=rom, prefix=prefix, frame=frame, state=state,
                        script=script, save_state=save_state)
    binary = Path(binary or step_emu.DEFAULT_BINARY)
    if not binary.exists():
        raise HarnessError(f"{binary} is not built; run `make capture-tool`")
    args = [str(binary), str(rom), f"{frame / FPS:.6f}", str(prefix),
            "mep-off", "hdpack-off"]
    if state:
        args.append(f"state={state}")
    if script:
        args.append(f"input={script}")
    if save_state:
        args.append(f"save-state={save_state}")
    done = subprocess.run(args, capture_output=True, text=True, check=False)
    if done.returncode != 0:
        raise HarnessError(f"headless_record failed: {done.stdout}{done.stderr}")
    return done.stdout


def verify_script(rom, state, script, checkpoints, ram_map, *, binary=None, work=None,
                  one_shot=None, ram_of=None):
    """Replay the script with no AI at all and compare the RAM checkpoints.

    One `headless_record` per checkpoint, from the same minted state, with the
    script cut by the run's own frame budget, over the fields this game's map
    names.

    The budget is the checkpoint's frame minus one, and that is the tool's
    arithmetic rather than a fudge: a one-shot run's `<seconds>` names the frame
    the run *ends* on, so a budget of `n` frames plays the script's first `n+1`
    frames (the state's own frame is the script's frame 0). A checkpoint at
    script frame `f` is therefore the state a one-shot budget of `f-1` ends on -
    the same instant `measure_script` reads it at from the session.

    A checkpoint at frame 0 is not verifiable this way and is skipped, named in
    the result: a one-shot run asked for zero frames still loads the state and
    its boot slack runs a frame or two before it reads (measured: abs x 35 where
    the session reads the state's own 32). The starting position is the minted
    state's, and the mint is what establishes it.
    """
    if ram_of is None:
        import mss_ram  # noqa: PLC0415
        ram_of = mss_ram.ram
    work = Path(work or DEFAULT_WORK / "verify")
    work.mkdir(parents=True, exist_ok=True)
    names = [ram_map.progress]
    names += [name for name in ("lives", "hp") if name in ram_map.fields]
    names = list(dict.fromkeys(names))
    results, skipped = [], []
    for index, (frame, expected) in enumerate(checkpoints):
        if frame == 0:
            skipped.append(frame)
            continue
        out = work / f"oneshot-{index}.mss"
        run_one_shot(rom, work / f"run{index}", frame - 1, state=state, script=script,
                     save_state=out, binary=binary, one_shot=one_shot)
        actual_all = read_ram_fields(ram_map, ram_of(out))
        actual = {name: actual_all[name] for name in names}
        wanted = {name: expected.get(name) for name in names}
        results.append({"frame": frame,
                        "ok": all(actual[name] == wanted[name] for name in names),
                        "expected": wanted, "actual": actual})
    return {"ok": bool(results) and all(entry["ok"] for entry in results),
            "checkpoints": results, "skipped_frames": skipped}


def write_cheat_sidecar(out, cheats) -> Path:
    """Mark a cheated script as a coverage pass, beside the script itself.

    ADR-0184 section 1: a RAM cheat is legitimate for reaching a spot, and the
    artifact it produces is not the same claim as an uncheated one - it replays
    only with the same cheat list. So the script ships with a sidecar naming the
    codes, and the pass is labelled `coverage`, never `route`.
    """
    sidecar = Path(f"{out}.cheats.json")
    sidecar.write_text(json.dumps(
        {"pass": "coverage", "cheats": list(cheats),
         "note": "ADR-0184: a cheated script is a coverage-pass artifact, and it "
                 "replays only with this cheat list"},
        indent=2) + "\n", encoding="utf-8")
    return sidecar


def promote_tips(path, tips) -> int:
    """Write a run's tips back to the versioned file, keeping the ids unique."""
    path = Path(path)
    document = (json.loads(path.read_text(encoding="utf-8")) if path.exists()
                else {"tips": []})
    known = {entry.get("id") for entry in document.get("tips", [])}
    added = 0
    #Only the tips this run's research proposed, never a rewrite of the ones the
    #file already carries: ADR-0238 section 3 promotes a tip after the stall it
    #was written for passed, and a run has no business rewriting an old one.
    for tip in tips.extra:
        if tip.id in known:
            continue
        document.setdefault("tips", []).append({
            "id": tip.id, "tip": tip.text, "macro": tip.macro,
            #`equals` only when the tip pins a value: a range keeps the two-key
            #form the other files use, and a pinned one keeps its pin (a tip that
            #lost it would fire everywhere the field exists).
            "when": [dict({"field": c.field, "min": c.minimum, "max": c.maximum},
                          **({"equals": c.equals} if c.equals is not None else {}))
                     for c in tip.conditions],
            "sources": list(tip.sources), "note": tip.note})
        added += 1
    path.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n",
                    encoding="utf-8")
    return added


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--rom", required=True)
    parser.add_argument("--game", required=True, help="a folder under scripts/stages/")
    parser.add_argument("--state", required=True, help="the minted .mss to start from")
    parser.add_argument("--work", default=None, help="scratch folder for the session")
    parser.add_argument("--out", default=None, help="the script to write")
    parser.add_argument("--ram-map", default=None)
    parser.add_argument("--tips", default=None)
    parser.add_argument("--cheat", action="append", default=None,
                        help="AAAA:VV[:CC], RAM addresses only (ADR-0184 section 1)")
    parser.add_argument("--promote-tips", action="store_true",
                        help="write this run's tips back to the versioned file")
    parser.add_argument("--macro-frames", type=int, default=15,
                        help="the one duration every macro of the table gets")
    parser.add_argument("--route-macros", action="store_true",
                        help="also offer F14.13's route_search windows (off by "
                             "default: with them the x 987 pin is not a stall)")
    parser.add_argument("--macro", action="append", default=None,
                        metavar="NAME=BUTTONS[:TEXT]")
    parser.add_argument("--goal", default=None, metavar="FIELD:MIN",
                        help="stop as soon as FIELD reaches MIN")
    parser.add_argument("--stall-seconds", type=float, default=5.0)
    parser.add_argument("--settle-seconds", type=float, default=1.5)
    parser.add_argument("--ring-seconds", type=float, default=24.0)
    parser.add_argument("--max-questions", type=int, default=3)
    parser.add_argument("--max-decisions-per-stall", type=int, default=18)
    parser.add_argument("--max-stalls", type=int, default=8)
    parser.add_argument("--max-emulated-seconds", type=float, default=600.0)
    parser.add_argument("--budget", type=float, default=jev_client.DEFAULT_BUDGET_USD)
    parser.add_argument("--log", default=None, help="the per-decision JSONL")
    parser.add_argument("--summary", default=None, help="the run summary JSON")
    parser.add_argument("--verify", action="store_true")
    parser.add_argument("--research-timeout", type=float, default=None,
                        help="wall-clock cap on one web-research pass")
    parser.add_argument("--progress-field", default=None)
    parser.add_argument("--screen-field", default=None)
    parser.add_argument("--stage", default=None, metavar="NAME",
                        help="the run's own stage, which is what a tip gated on "
                             "the run-level `stage` fires on (ADR-0238 section 3). "
                             "Defaults to the one <game>/stage-set.json names")
    args = parser.parse_args(argv)

    game_dir = STAGES / args.game
    ram_map_path = Path(args.ram_map) if args.ram_map else game_dir / "ram-map.json"
    tips_path = Path(args.tips) if args.tips else game_dir / "jev-tips.json"
    work = Path(args.work) if args.work else DEFAULT_WORK / args.game
    out = Path(args.out) if args.out else DEFAULT_WORK / f"{args.game}-run.txt"
    log_path = Path(args.log) if args.log else work / "decisions.jsonl"
    summary_path = Path(args.summary) if args.summary else work / "summary.json"

    #The run's stage, before the map is loaded: it rides on every state the map
    #reads, and a tip gated on it (`mm3/jev-tips.json`: snake-man and the other
    #five) fires on nothing until one is declared.
    stage, known_stages = resolve_stage(game_dir / "stage-set.json", args.stage)
    if args.stage and known_stages and args.stage not in known_stages:
        print(f"error: --stage {args.stage!r} is not one of this stage set's "
              f"({', '.join(known_stages)})", file=sys.stderr)
        return 2

    try:
        cheats = parse_cheats(args.cheat)
        ram_map = RamMap.load(ram_map_path, progress=args.progress_field,
                              screen=args.screen_field,
                              run_keys={"stage": stage} if stage else None)
        tips = load_tips(tips_path, fields=ram_map.fields)
        macros = macro_table(args.macro_frames)
        if args.route_macros:
            macros.update(macro_table(args.macro_frames,
                                      route_macros()))
        macros.update(macro_table(args.macro_frames, _parse_macro_extra(args.macro)))
    except HarnessError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    if stage is None:
        #Fails closed, and says so: a tip whose trigger names `stage` cannot
        #hold in a run that never declared one, and a silent no-match reads
        #exactly like a tip that was considered and rejected.
        gated = tips_gated_on_stage(tips, ram_map.fields)
        if gated:
            print(f"note: no stage declared, so the {len(gated)} tip(s) gated on "
                  f"the run-level `stage` cannot fire this run ({', '.join(gated)}). "
                  f"Pass --stage, or name one in {game_dir / 'stage-set.json'} "
                  f"(ram-map.json's stage_id is not a verified address).",
                  file=sys.stderr)

    try:
        client = jev_client.JevClient(budget_usd=args.budget, log_path=log_path)
    except jev_client.MissingKeyError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    work.mkdir(parents=True, exist_ok=True)
    emu = step_emu.StepEmu(args.rom, state=str(args.state), work=work, cheats=cheats)
    try:
        if cheats:
            missing = cheats_unconfirmed(emu.init_output, cheats)
            if missing:
                print(
                    "error: refused the run: the session did not apply "
                    f"{', '.join(missing)} - a cheated script that replays uncheated "
                    "is one nobody can tell apart from the real thing (ADR-0184 "
                    "section 1). The session applies the codes it is given and "
                    "reports each one before its `ready` (`applyCheats` in "
                    "scripts/headless_record.cpp), so a code missing from that "
                    "report was dropped, not deferred.", file=sys.stderr)
                return 2
        harness = Harness(
            emu, ram_map, start_state=args.state, client=client, tips=tips,
            macros=macros, frames=args.macro_frames, work=work,
            stall_seconds=args.stall_seconds, settle_seconds=args.settle_seconds,
            ring_seconds=args.ring_seconds, max_questions_per_rung=args.max_questions,
            max_stalls=args.max_stalls,
            max_decisions_per_stall=args.max_decisions_per_stall,
            max_emulated_seconds=args.max_emulated_seconds, budget_usd=args.budget,
            goal=args.goal, log_path=log_path, research_timeout=args.research_timeout)
        summary = harness.run()
        if summary.script:
            summary.out = str(write_script(out, summary.script))
            if cheats:
                #The session applied them (checked above, before the emulator
                #was driven), so the script is a coverage pass and ships with
                #the codes it was found under: ADR-0184 section 1 - it replays
                #only with this list, and a reader has to be able to see that.
                sidecar = write_cheat_sidecar(out, cheats)
                print(f"cheated run: the cheat list ships beside the script at "
                      f"{sidecar} (coverage pass)", file=sys.stderr)
            if args.verify and summary.checkpoints:
                summary.verified = verify_script(
                    args.rom, args.state, summary.out,
                    [(entry["frame"], entry["state"]) for entry in summary.checkpoints],
                    ram_map, work=work / "verify")
        if args.promote_tips:
            if summary.goal_reached:
                print(f"promoted {promote_tips(tips_path, tips)} tip(s) into {tips_path}")
            else:
                print("--promote-tips: the run did not reach its goal, so nothing "
                      "was promoted", file=sys.stderr)
    finally:
        emu.close()

    summary_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.write_text(json.dumps(summary.as_json(), indent=2) + "\n",
                            encoding="utf-8")
    print(json.dumps(summary.as_json(), indent=2))
    if summary.reason == "budget":
        return 5
    return 0 if summary.goal_reached else 1


if __name__ == "__main__":
    sys.exit(main())
