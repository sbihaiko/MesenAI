#!/usr/bin/env python3
"""Propose cheat codes for a game from public code lists, and offer only the
ones a headless run on the player's own copy confirms (ADR-0245 section 4, P.12).

The external half of the cheats sheet's web lookup. The client never calls a
network service (ADR-0247, PRD Part A section 1 principle 5): this script does,
and what it proposes reaches the client only as data a deterministic check has
confirmed.

How a lookup is answered:

  1. **Fetch.** A *source* is a public code list: the hosts it may be read from,
     the way one game's page is named, and the page's format. The shipped
     source is `libretro-database` (github.com/libretro/libretro-database, its
     `cht/Nintendo - Nintendo Entertainment System/` folder - RetroArch's own
     `.cht` files, one file per game, plain text, no key and no API). `--page`
     names one page directly and `--page-file` reads an offline copy of one, so
     a committed fixture can be scored with no network at all.
  2. **Parse.** Each entry becomes a candidate: a code, and the description the
     page gave it. Nothing is invented here, and nothing is rewritten - a
     candidate is text the page carried.
  3. **Decode.** Every code goes through `scripts/cheat_decoder.py`, the port of
     the Core's own decoders (held to them by
     `scripts/test_cheat_decoder_parity.py`). A candidate whose decode yields no
     NES internal-RAM address is dropped and never offered: a Game Genie or Pro
     Action Rocky code patches PRG, and ADR-0245's Decision 4 says such a code
     cannot pass, so it is not put to the check at all.
  4. **Mint.** One `.mss` is minted from the player's own ROM at the start of
     the lookup - the ROM is read by path, **never uploaded**, and the state is
     written under `runs/` (gitignored), because a `.mss` is never versioned.
     The mint script is MINT_MACROS: past the title, then some play, so that the
     addresses a code promises to pin are alive in the window being measured.
  5. **Check.** From that same state, and one session per candidate: run
     CHECK_FRAMES frames with the code off, and the same frames with it on, the
     code applied by the runner's own `cheat=` argument. A code passes only when
     its target address holds the promised value in **every** "on" frame **and**
     the "off" run differs there - a code whose address already held the value
     with the code off has been confirmed by nothing.
  6. **Offer.** Only the codes that pass are in the output, each labelled LABEL,
     and the description is still the page's own text. A passing code can still
     have side effects this check does not see; the label says it was checked,
     never that it is safe (ADR-0245 Consequences).

What leaves the machine: the page GETs, and nothing else. No ROM byte, no RAM
value, no screenshot, no key. This script uses no hosted model, so it reads no
key at all; every sink it writes (stdout, stderr, the `--log` JSONL) still goes
through `scrub`, so a value the environment holds under a known key name cannot
ride out on a page's text or a transport's error message (ADR-0242 Q1).

Output, one JSON object on stdout:

  {"schema": "mesence.cheat-web-lookup/1",
   "game": {"name": "...", "rom": "<file name>", "rom_sha256": "..."},
   "source": {"name": "libretro-database", "page": "https://..."},
   "frames": 120, "state": "runs/cheat-web-lookup/castlevania.mss",
   "counts": {"entries": 140, "undecodable": 0, "multi_part": 6,
              "no_ram_target": 120, "checked": 14, "passed": 2},
   "codes": [{"code": "0071:63", "desc": "...", "address": "0x0071",
              "value": 99, "on": "120/120", "off": "0/120",
              "label": "found online, checked on your copy"}]}

Usage:
  python3 scripts/cheat_web_lookup.py --rom "Castlevania (U) (PRG0) [!].nes"
  python3 scripts/cheat_web_lookup.py --rom game.nes --game Contra --page-file page.cht
  python3 scripts/cheat_web_lookup.py --rom game.nes --page https://.../Game%20(USA).cht

Exit codes: 0 a lookup ran (even with no code passing), 2 usage, console, ROM or
page refused, 3 the page could not be fetched, 4 the headless check failed.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import NamedTuple

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cheat_decoder  # noqa: E402
import step_emu  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = "mesence.cheat-web-lookup/1"

#The label every code in the output carries, worded by ADR-0245 Decision 4.
LABEL = "found online, checked on your copy"

#The fixed N of the check, recorded here and in the slice's measurement log
#(docs/validation/measurements/p12-cheat-web-lookup-2026-10-06.md):
#120 frames is about two seconds of NTSC play - long enough that a code which
#writes its value once and is then overwritten by the game fails, and short
#enough that a page's whole RAM list is a minute of checking.
CHECK_FRAMES = 120

#The state every check in one lookup starts from. Ten seconds of play: past the
#title screen (the Start tap), then walking and jumping, so the addresses a code
#promises to pin are being written by the game while the window runs - which is
#what the "off" side of the check needs in order to differ.
MINT_MACROS = ((180, "-"), (1, "S"), (60, "-"), (120, "R"), (120, "RA"), (120, "-"))
MINT_FRAMES = sum(frames for frames, _ in MINT_MACROS)

#The NES's internal RAM, as scripts/headless_record.cpp's NesInternalRamEnd has
#it. Above it is a mirror, a register or the cartridge (ADR-0184 section 1).
NES_INTERNAL_RAM_END = 0x0800

USER_AGENT = "MesenCE-cheat-web-lookup/1 (+https://github.com/sbihaiko/MesenAI)"
DEFAULT_TIMEOUT = 30.0
DEFAULT_STATE_DIR = ROOT / "runs" / "cheat-web-lookup"

#Environment names whose values are treated as secrets and scrubbed from every
#sink. This script reads none of them; the list exists so that a key the user's
#shell already holds cannot ride out on an error message or a page's text.
SECRET_ENV_NAMES = ("OPENROUTER_API_KEY", "GEMINI_API_KEY", "ANTHROPIC_API_KEY",
                    "MESENCE_API_KEY")
REDACTED = "<redacted>"


class LookupError(RuntimeError):
    """A request this script refuses before anything is fetched (exit 2)."""


class FetchError(RuntimeError):
    """The page could not be read (exit 3)."""


class CheckError(RuntimeError):
    """The headless check could not run (exit 4)."""


class ParseError(RuntimeError):
    """The page is not in the format it claims to be."""


class Entry(NamedTuple):
    """One code as the page spelled it, with the page's own description."""

    desc: str
    code: str


class Candidate(NamedTuple):
    """One entry a check can carry: exactly one NES internal-RAM write."""

    code: str
    desc: str
    address: int
    value: int
    compare: int


class Result(NamedTuple):
    """One candidate's verdict, with the counts the numbers came from."""

    candidate: Candidate
    passed: bool
    reason: str
    on_holding: int
    off_differing: int


class Rejected(NamedTuple):
    """One entry that was never offered, and the rule that dropped it."""

    code: str
    desc: str
    reason: str


# --- secrets ---------------------------------------------------------------


def scrub(text, env=None):
    """Replaces any value `env` (the process environment by default) holds under
    a known key name. Nothing here reads a key; this is the belt to the
    suspenders, so that the sinks ADR-0247 lists cannot carry one by accident."""
    values = env if env is not None else os.environ
    for name in SECRET_ENV_NAMES:
        value = values.get(name)
        if value and len(value) >= 8:
            text = text.replace(value, REDACTED)
    return text


# --- sources ---------------------------------------------------------------


class Source(NamedTuple):
    """One public code list: where it lives, how a game's page is named, and the
    format its pages are written in."""

    name: str
    hosts: tuple
    page_template: str
    format: str

    def urls_for(self, game: str) -> list:
        """The pages to try for `game`, most likely first. A guess, and only a
        guess: the check is what decides whether a page's codes fit this ROM."""
        base = game_base(game)
        names = []
        for region in region_hints(game):
            names.append(f"{base} ({region})")
        names.append(base)
        return [self.page_template.format(page=urllib.parse.quote(name) + ".cht")
                for name in names]


LIBRETRO = Source(
    name="libretro-database",
    hosts=("raw.githubusercontent.com",),
    page_template=("https://raw.githubusercontent.com/libretro/libretro-database/"
                   "master/cht/Nintendo%20-%20Nintendo%20Entertainment%20System/"
                   "{page}"),
    format="retroarch-cht",
)

SOURCES = {LIBRETRO.name: LIBRETRO}

#No-Intro's parenthesised region tags ("Castlevania (U) (PRG0) [!]") and
#libretro-database's spelled-out ones ("Castlevania (USA)") name the same
#release differently, so the tag is translated rather than passed through.
REGION_WORDS = {"u": "USA", "ue": "USA", "j": "Japan", "je": "Japan",
                "e": "Europe", "w": "World", "ju": "Japan", "jue": "USA"}
REGION_ORDER = ("Japan", "USA", "Europe", "World")
_TAGS = re.compile(r"\([^()]*\)|\[[^\[\]]*\]")


def game_base(name: str) -> str:
    """A game's name with its file extension and its parenthesised/bracketed
    tags removed: "Castlevania (U) (PRG0) [!].nes" -> "Castlevania"."""
    stem = Path(name.strip()).name
    for extension in (".nes", ".zip", ".unf", ".unif", ".fds", ".nsf"):
        if stem.lower().endswith(extension):
            stem = stem[: -len(extension)]
            break
    return " ".join(_TAGS.sub(" ", stem).split()).strip() or stem.strip()


def region_hints(name: str) -> list:
    """The regions to try for `name`, the one it names first, then the rest in a
    fixed order so a lookup is reproducible. An empty tail entry means the page
    with no region tag at all."""
    text = Path(name.strip()).name.lower()
    found = []
    for tag in _TAGS.findall(text):
        key = tag.strip("()[] ").lower()
        if key in REGION_WORDS:
            found.append(REGION_WORDS[key])
        elif key in ("usa", "japan", "europe", "world"):
            found.append(key.capitalize())
    hints = []
    for region in [*found, *REGION_ORDER]:
        if region not in hints:
            hints.append(region)
    return hints


def check_host(url: str, source: Source) -> None:
    """A page may only be read from a host its source lists. Refusing here keeps
    a mistyped or redirected URL from turning the lookup into an open fetcher."""
    host = (urllib.parse.urlparse(url).hostname or "").lower()
    if host not in source.hosts:
        raise LookupError(
            f"{url!r} is not on {source.name}'s host list ({', '.join(source.hosts)})")


def http_get(url, timeout=DEFAULT_TIMEOUT):
    """One HTTPS GET. `urllib` follows redirects, so the host is checked again
    on the final URL by the caller."""
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            if response.status != 200:
                raise FetchError(f"{url} answered HTTP {response.status}")
            return response.read().decode("utf-8", "replace"), response.geturl()
    except urllib.error.HTTPError as exc:
        raise FetchError(f"{url} answered HTTP {exc.code}") from None
    except (urllib.error.URLError, OSError, ValueError) as exc:
        raise FetchError(f"{url} could not be read: {type(exc).__name__}") from None


# --- page formats ----------------------------------------------------------

_CHT_LINE = re.compile(r"^\s*cheat(\d+)_(desc|code|enable)\s*=\s*(.*?)\s*$")
_PLAIN_LINE = re.compile(r"^\s*([0-9A-Fa-f]{4}:[0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2})?"
                         r"|[APZLGITYEOXUKSVNapzlgltyeoxuksvn]{6,8}"
                         r"|[0-9A-Fa-f]{3}-[0-9A-Fa-f]{3}(?:-[0-9A-Fa-f]{3})?"
                         r"|[0-9A-Fa-f]{8})\s+(.+?)\s*$")


def _looks_like_html(text: str) -> bool:
    head = text[:4096].lower()
    return "<html" in head or "<!doctype html" in head


def parse_cht(text: str) -> list:
    """RetroArch's `.cht`, which is what libretro-database stores: numbered
    `cheatN_desc` / `cheatN_code` / `cheatN_enable` blocks. Returns one Entry per
    block that carries a code, in file order. A block with a description and no
    code is a page's own placeholder, not a candidate, so it is skipped."""
    if _looks_like_html(text):
        raise ParseError("the page is HTML, not a .cht code list")
    blocks, order = {}, []
    for line in text.splitlines():
        match = _CHT_LINE.match(line)
        if match is None:
            continue
        index, field, raw = int(match.group(1)), match.group(2), match.group(3)
        if index not in blocks:
            blocks[index] = {}
            order.append(index)
        blocks[index][field] = raw.strip().strip('"').strip()
    return [Entry(blocks[index].get("desc", ""), blocks[index]["code"])
            for index in order if blocks[index].get("code")]


def parse_plain(text: str) -> list:
    """A plain list: one `<code> <description>` per line, the description being
    everything after the code. What a hand-kept list or a wiki export looks
    like; `--format plain-lines` selects it."""
    if _looks_like_html(text):
        raise ParseError("the page is HTML, not a plain code list")
    entries = []
    for line in text.splitlines():
        match = _PLAIN_LINE.match(line)
        if match is not None:
            entries.append(Entry(match.group(2), match.group(1)))
    return entries


FORMATS = {"retroarch-cht": parse_cht, "plain-lines": parse_plain}


def parse_page(text: str, page_format: str) -> list:
    parser = FORMATS.get(page_format)
    if parser is None:
        raise LookupError(
            f"unknown page format {page_format!r} ({', '.join(sorted(FORMATS))})")
    entries = parser(text)
    if not entries:
        raise LookupError("the page carried no cheat entries")
    return entries


# --- the candidates a check can carry --------------------------------------


def split_parts(code: str) -> list:
    """A page may join a code's parts with '+' (RetroArch) or ';' (the bundled
    list). Both are the separators the rest of the project reads."""
    return [part.strip() for part in re.split(r"[+;]", code or "") if part.strip()]


def ram_target(decoded):
    """The NES internal-RAM address this decoded part writes, or None when a
    check may not carry it.

    The rule is ADR-0184 section 1 as ADR-0245 section 3 restates it for cheats:
    a RAM code is a `NesCustom` code whose every address is inside $0000-$07FF.
    A Game Genie or Pro Action Rocky part decodes to a PRG address, so it is
    refused here - and the runner refuses it again at the `cheat=` argument
    (scripts/headless_record.cpp, parseRamCheat), which is the same rule written
    twice on purpose: this copy decides what is offered, that one decides what
    the emulator will accept.
    """
    if decoded is None or decoded.type != cheat_decoder.NES_CUSTOM:
        return None
    if not 0 <= decoded.address < NES_INTERNAL_RAM_END:
        return None
    return decoded.address


def candidate_for(entry: Entry, console: str = "nes"):
    """`(candidate, None)` when the entry can be checked, `(None, reason)` when
    it cannot. The decode is the Core's own (`cheat_decoder`), never a guess."""
    parts = split_parts(entry.code)
    if not parts:
        return None, "empty"
    decoded = [cheat_decoder.decode_for_console(console, part) for part in parts]
    if any(part is None for part in decoded):
        return None, "undecodable"
    if len(decoded) != 1:
        #The check names one target address, and ADR-0245 Decision 4 has the
        #"off" side differ *at that address*. A multi-part entry has several, so
        #it is not a candidate this instrument can confirm; it is reported as
        #such rather than half-checked.
        return None, "multi-part"
    address = ram_target(decoded[0])
    if address is None:
        return None, "no-ram-target"
    return Candidate(entry.code, entry.desc, address, decoded[0].value,
                     decoded[0].compare), None


def collect(entries, console: str = "nes"):
    """Every entry of a page as `(candidates, rejected)`, in page order."""
    candidates, rejected = [], []
    for entry in entries:
        candidate, reason = candidate_for(entry, console)
        if candidate is None:
            rejected.append(Rejected(entry.code, entry.desc, reason))
        else:
            candidates.append(candidate)
    return candidates, rejected


# --- the check -------------------------------------------------------------


def default_session_factory(rom, cheats, work, binary=None):
    """One `headless_record session` (scripts/step_emu.py, ADR-0238 section 1).
    The runner takes `cheat=` at launch, which is why a candidate costs a
    session: a session's cheat set is fixed when the emulator loads the ROM.
    `binary` is the runner to launch (default: scripts/headless_record)."""
    try:
        return step_emu.StepEmu(rom, cheats=list(cheats), work=work, binary=binary)
    except step_emu.StepEmuError as exc:
        raise CheckError(str(exc)) from None


def mint_state(rom, out_path, *, work=None, session_factory=None, macros=MINT_MACROS):
    """Mints the `.mss` a lookup starts from, and answers `(path, frames played)`.

    A `.mss` is never versioned, so the caller's default path is under `runs/`.
    The macros are played with no cheat on: the state is the game as it is, and
    every candidate's check starts from this one state, so the "off" and "on"
    sides of a check differ in the code and in nothing else.
    """
    session = (session_factory or default_session_factory)(str(rom), [], work)
    out = Path(out_path)
    try:
        out.parent.mkdir(parents=True, exist_ok=True)
        for frames, buttons in macros:
            session.play(frames, buttons)
        handle = session.save()
        session.save_file(handle, out)
    except (step_emu.StepEmuError, OSError, ValueError) as exc:
        raise CheckError(f"minting {out} failed: {exc}") from None
    finally:
        session.close()
    return out, MINT_FRAMES


def verdict(candidate: Candidate, off_values, on_values) -> Result:
    """ADR-0245 Decision 4's rule, and nothing more than it: the target address
    holds the promised value in every "on" frame, and the "off" run differs
    there. `off-unchanged` is the case a weaker check would have called a pass -
    the address held the value anyway, so the code confirmed nothing."""
    on_holding = sum(1 for value in on_values if value == candidate.value)
    off_differing = sum(1 for value in off_values if value != candidate.value)
    on_ok = on_holding == len(on_values)
    off_differs = off_differing > 0
    if on_ok and off_differs:
        reason = "confirmed"
    elif not on_ok:
        reason = "value-missing-on"
    else:
        reason = "off-unchanged"
    return Result(candidate, on_ok and off_differs, reason, on_holding, off_differing)


class Checker:
    """The deterministic gate between a public page and the player.

    One session per side, each of them loading the same minted `.mss`: the "off"
    window reads every candidate's address once per frame, and an "on" window
    reads its own candidate's address the same way. Nothing about a candidate
    reaches the output except through `verdict`.
    """

    def __init__(self, rom, state, *, frames=CHECK_FRAMES, work=None,
                 session_factory=None, progress=None):
        if frames < 2:
            #One frame out of a freshly loaded state is the one span a session
            #cannot name (step_emu.run_exact says so): the first run after a load
            #covers two frames. The window is therefore at least two.
            raise LookupError(f"a check window needs at least 2 frames, got {frames}")
        self.rom = str(rom)
        self.state = str(state)
        self.frames = frames
        self.work = work
        self._sessions = session_factory or default_session_factory
        self.progress = progress

    def _say(self, text):
        if self.progress is not None:
            print(text, file=self.progress)

    def samples(self, cheats, addresses) -> list:
        """Plays `self.frames` frames from the minted state, one frame at a time,
        and reads `addresses` after every one of them. Returns one row per
        frame, one value per address.

        The frames are single `run 1` requests rather than one `run N` because
        "in every 'on' frame" is the rule, and a single run would only ever show
        the last frame. The first run out of a load covers the state's own frame
        plus one (step_emu's arithmetic: a `.mss` carries the counter of the
        frame about to run), so the window spans `frames + 1` emulated frames
        with `frames` samples - and it does so identically on both sides, which
        is what makes the comparison sound.
        """
        if not addresses:
            raise LookupError("no address to check")
        session = self._sessions(self.rom, list(cheats), self.work)
        try:
            session.load_file(self.state)
            rows = []
            for _ in range(self.frames):
                session.run(1)
                rows.append(list(session.read_ram(*addresses)))
            return rows
        except (step_emu.StepEmuError, OSError, ValueError) as exc:
            raise CheckError(f"the headless check failed: {exc}") from None
        finally:
            session.close()

    def check(self, candidates) -> list:
        """Every candidate against the one minted state, in order."""
        if not candidates:
            return []
        addresses = sorted({candidate.address for candidate in candidates})
        column = {address: index for index, address in enumerate(addresses)}
        self._say(f"checking {len(candidates)} code(s), {self.frames} frames each side")
        off = self.samples([], addresses)
        results = []
        for number, candidate in enumerate(candidates, 1):
            on = [row[0] for row in self.samples([candidate.code], [candidate.address])]
            off_values = [row[column[candidate.address]] for row in off]
            result = verdict(candidate, off_values, on)
            results.append(result)
            self._say(f"[{number}/{len(candidates)}] "
                      f"{'PASS' if result.passed else 'fail'} {candidate.code} "
                      f"-> ${candidate.address:04X} = {candidate.value:02X} "
                      f"({result.reason}: on {result.on_holding}/{self.frames}, "
                      f"off {result.off_differing}/{self.frames})")
        return results


# --- the whole request -----------------------------------------------------


def read_page(*, source, game, page=None, page_file=None, transport=None,
              timeout=DEFAULT_TIMEOUT):
    """The page text and the URL it came from.

    `--page` names one page, `--page-file` reads a local copy of one (a fixture,
    for an offline run), and otherwise the source's own guess for `game` is
    tried in order until one answers. A page the source cannot name is a refusal,
    not an empty result, so the caller can say which game had no page.
    """
    get = transport or http_get
    if page_file is not None:
        with open(page_file, encoding="utf-8-sig", errors="replace") as handle:
            return handle.read(), str(page_file)
    if page is not None:
        check_host(page, source)
        text, final = get(page, timeout)
        check_host(final, source)
        return text, final
    failures = []
    for url in source.urls_for(game):
        try:
            text, final = get(url, timeout)
        except FetchError as exc:
            failures.append(str(exc))
            continue
        check_host(final, source)
        return text, final
    raise FetchError(f"no page for {game!r} in {source.name}: {'; '.join(failures)}")


def file_sha256(path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def result_json(*, game, rom, page_url, source, state_path, frames, entries,
                candidates, rejected, results) -> dict:
    """The one JSON object the client reads. Only passing codes are in it."""
    reason_counts = {}
    for item in rejected:
        reason_counts[item.reason] = reason_counts.get(item.reason, 0) + 1
    passed = [result for result in results if result.passed]
    codes = [{
        "code": result.candidate.code,
        "desc": result.candidate.desc,
        "address": f"0x{result.candidate.address:04X}",
        "value": result.candidate.value,
        "on": f"{result.on_holding}/{frames}",
        "off": f"{result.off_differing}/{frames}",
        "label": LABEL,
    } for result in passed]
    return {
        "schema": SCHEMA,
        "game": {"name": game, "rom": Path(rom).name, "rom_sha256": file_sha256(rom)},
        "source": {"name": source.name, "page": page_url},
        "frames": frames,
        "state": str(state_path),
        "counts": {
            "entries": len(entries),
            "undecodable": reason_counts.get("undecodable", 0),
            "multi_part": reason_counts.get("multi-part", 0),
            "no_ram_target": reason_counts.get("no-ram-target", 0),
            "checked": len(candidates),
            "passed": len(passed),
        },
        "codes": codes,
    }


def lookup(*, rom, game=None, console="nes", source=LIBRETRO, page=None,
           page_file=None, frames=CHECK_FRAMES, state=None, work=None,
           session_factory=None, transport=None, timeout=DEFAULT_TIMEOUT,
           progress=None) -> dict:
    """The whole request: page -> candidates -> mint -> check -> client JSON."""
    if console != "nes":
        #ADR-0245 section 5: NES gets the browser phases; GB/SMS show the sheet
        #with manual entry only. A lookup for them would have nothing to run.
        raise LookupError(f"the web lookup is NES only; {console!r} has no list")
    rom_path = Path(rom)
    if not rom_path.is_file():
        raise LookupError(f"the ROM {str(rom_path)!r} is not a file")
    name = game or game_base(rom_path.name)
    if not name:
        raise LookupError("no game name to look up")
    text, page_url = read_page(source=source, game=name, page=page,
                               page_file=page_file, transport=transport,
                               timeout=timeout)
    entries = parse_page(text, source.format)
    candidates, rejected = collect(entries, console)
    if not candidates:
        raise LookupError(
            f"the page for {name!r} carried no code a check can carry "
            f"({len(entries)} entries, none a RAM write)")
    state_path = Path(state) if state else (DEFAULT_STATE_DIR / f"{_slug(name)}.mss")
    mint_state(rom_path, state_path, work=work, session_factory=session_factory)
    if progress is not None:
        print(f"minted {state_path} ({MINT_FRAMES} frames)", file=progress)
    checker = Checker(rom_path, state_path, frames=frames, work=work,
                      session_factory=session_factory, progress=progress)
    results = checker.check(candidates)
    return result_json(game=name, rom=rom_path, page_url=page_url, source=source,
                       state_path=state_path, frames=frames, entries=entries,
                       candidates=candidates, rejected=rejected, results=results)


def _slug(name: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", game_base(name).lower()).strip("-")
    return slug or "game"


def write_log(path, payload, env=None) -> None:
    """One JSON line per event, scrubbed. `--log` is where a run says what it did
    without any of it reaching stdout; it is never a sidecar the app writes."""
    line = scrub(json.dumps(payload, ensure_ascii=False), env)
    with open(path, "a", encoding="utf-8") as handle:
        handle.write(line + "\n")


def main(argv=None, *, env=None, transport=None, session_factory=None,
         default_source=LIBRETRO) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--rom", required=True,
                        help="the player's own copy; read by path, never uploaded")
    parser.add_argument("--game", help="the name to look up (default: the ROM's file name)")
    parser.add_argument("--console", default="nes", help="only nes is supported")
    parser.add_argument("--source", default=default_source.name,
                        help=f"a public code list ({', '.join(sorted(SOURCES))})")
    parser.add_argument("--page", help="one page URL, instead of the source's guess")
    parser.add_argument("--page-file", help="a local copy of a page (offline run)")
    parser.add_argument("--format", help="the page format, overriding the source's")
    parser.add_argument("--frames", type=int, default=CHECK_FRAMES,
                        help=f"the check window, in frames (default {CHECK_FRAMES})")
    parser.add_argument("--state", help="where the .mss is minted (default under runs/)")
    parser.add_argument("--work", help="the headless session's scratch folder")
    parser.add_argument("--binary", help="the headless_record runner to launch "
                        "(default: scripts/headless_record)")
    parser.add_argument("--log", help="a JSONL log of the run (scrubbed)")
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT)
    args = parser.parse_args(argv)
    source = SOURCES.get(args.source)
    if source is None:
        print(f"cheat_web_lookup: unknown source {args.source!r}", file=sys.stderr)
        return 2
    if args.format:
        if args.format not in FORMATS:
            print(f"cheat_web_lookup: unknown format {args.format!r}", file=sys.stderr)
            return 2
        source = source._replace(format=args.format)
    if session_factory is None and args.binary:
        def session_factory(rom, cheats, work):
            return default_session_factory(rom, cheats, work, binary=args.binary)
    try:
        result = lookup(
            rom=args.rom, game=args.game, console=args.console, source=source,
            page=args.page, page_file=args.page_file, frames=args.frames,
            state=args.state, work=args.work, session_factory=session_factory,
            transport=transport, timeout=args.timeout,
        )
    except LookupError as exc:
        print(scrub(f"cheat_web_lookup: {exc}", env), file=sys.stderr)
        return 2
    except FetchError as exc:
        print(scrub(f"cheat_web_lookup: {exc}", env), file=sys.stderr)
        return 3
    except CheckError as exc:
        print(scrub(f"cheat_web_lookup: {exc}", env), file=sys.stderr)
        return 4
    except OSError as exc:
        print(scrub(f"cheat_web_lookup: {exc}", env), file=sys.stderr)
        return 2
    if args.log:
        write_log(args.log, {"event": "lookup", **result}, env)
    print(scrub(json.dumps(result, ensure_ascii=False), env))
    return 0


if __name__ == "__main__":
    sys.exit(main())
