#!/usr/bin/env python3
"""The measurements a navigation sweep is read with (ADR-0239 §4, §5).

`record_navigation_sweep.py` records; this module counts. Everything here is
host-free - sets, one ROM header and one save state - so the metric the ADR
quotes can be unit tested without an emulator, and so the recording script
stays one orchestration file.

The two units are ADR-0194 §4's, and they are not the same unit:

- **distinct `tileData`** - the string that keys a tile, which is what
  `artist_cover.py` owns and what ADR-0184's amendment quotes. A `hires.txt`
  names a tile either by its 16 bytes (32 hex digits: a CHR RAM pattern) or by
  a CHR index (`HdPackLoader::ReadTileData`: decimal below `<ver>103`, hex at
  103+, the same split `mep_addition.parse_index` reads).
- **drawn keys** - `(tileData, palette)`, the full `<tile>` rule key. The same
  tile under a stage's other palette is another key, so keys overcount figures
  an artist repaints per stage and tile data undercounts them.

§5.3 compares two *packs*, and its unit is neither of those: it is the 16-byte
CHR pattern a `<tile>` rule names (`pack_named_patterns`). Two packs of one ROM
need not spell a tile the same way - a community pack writes a decimal CHR
index, a bootstrapped one hex - so the field as a string is not an identity
they share, and asking at it reads a constant (#545).

§5's second denominator needs no third-party pack, which is why it exists: a
CHR ROM game's own CHR is the set of patterns the game can draw, so the
fraction of them the union names is a floor that holds for every game. A CHR
RAM game has no fixed denominator (the pattern table is filled at run time from
PRG), so it reports the reference line only and this module says
`n/a (CHR RAM)` rather than printing a 0% that would read as a failure.
"""

import re
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent

# ADR-0239 §5.2: "A CHR RAM game has no fixed denominator and reports the
# reference line only." The words are the ADR's, so the report and the test
# agree on them.
CHR_RAM_NOTE = "n/a (CHR RAM)"

VER_RE = re.compile(r"^<ver>(\d+)")
RAM_ADDRESS_RE = re.compile(r"^[0-9A-Fa-f]{1,4}$")

# A `<tile>` rule's last field is the builder's placeholder flag:
# `HdPackBuilder.cpp:139` - "DefaultTile neutral-ramp placeholders are excluded
# - they are waiting for art, not real PaletteColors variants (the loader
# ignores their PaletteColors)". `HdPackBuilder.cpp:620/693` sets it in
# `AddRomTiles`/`AddPrgScanTiles`, which write one such rule for *every* CHR
# index so an artist has a slot to paint. `artist_cover.parse` drops the field
# (it has no use for it) and the union's units come from there; this reader
# exists only for that flag.
# A CHR RAM rule carries two more tokens after the flag (`HdData.h:610`:
# `...,Y,<ChrBankId>,<TileIndex>`; the CHR ROM form at `:612` stops at the
# flag), so the flag is matched where it is and the tail is optional.
RULE_RE = re.compile(
    r"(?:\[[^\]]*\])?<tile>\d+,([0-9A-Fa-f]+),([0-9A-Fa-f]+),\d+,\d+,"
    r"[0-9.eE+-]+,([YN])(?:,.*)?$")

# `HdData.h: Version = 0` - a pack with no `<ver>` line is read the old way,
# so its short tile fields are decimal (`HdPackLoader::ReadTileData`:
# `Version <= 102` -> `std::stoi`).
DEFAULT_PACK_VERSION = 0


# --- packs -------------------------------------------------------------------
def _cover():
    """`artist_cover`, the module that owns the coverage unit.

    Imported at call time, the way `tile_data_keys` always has: this module is
    imported by the recorder and by its test, and neither should pay for a
    parser it is not using.
    """
    sys.path.insert(0, str(SCRIPT_DIR))
    import artist_cover  # noqa: E402 - same folder, stdlib only
    return artist_cover


def _rules(hires: Path) -> list:
    """`(image, tileData, palette, stage gate)` per `<tile>` rule, or none."""
    hires = Path(hires)
    if not hires.exists():
        return []
    return _cover().parse(str(hires))


def tile_data_keys(hires) -> set:
    """The distinct tile-data strings a pack definition holds.

    `artist_cover.py` owns this metric and this parser - the unit is
    `tileData`, not `(tileData, palette)`, because "the same tile under a
    stage's other palette is a different `hires.txt` key, so keys undercount
    figures the artist repaints per stage" (`artist_cover.py:8-10`), and it is
    the unit ADR-0184's amendment quotes. Its `parse()` is imported rather than
    reimplemented so the sweep's acceptance number cannot drift from the tool
    the ADR was measured with; Contra's reference pack has 3404 distinct
    tile-data strings there, and the amendment's 64.6% - 53.8% = 367 tiles is
    10.8% of exactly that.

    Run `artist_cover.py` itself for the full report (per-image rows, the
    sprite/background split, the `$30` gate); this is the headline line only.
    """
    return {data for _, data, _, _ in _rules(Path(hires))}


def pack_tile_data(hires_paths) -> set:
    """The union of several packs' distinct tile-data strings."""
    out = set()
    for h in hires_paths:
        out |= tile_data_keys(h)
    return out


def pack_rule_keys(hires_paths) -> set:
    """The union of several packs' drawn keys - `(tileData, palette)` (§5.1).

    ADR-0239 §5 reports the union's "drawn keys **and** distinct tile data",
    and the two differ exactly by the palettes: a run that sees the same tile
    under three palettes contributes one tile-data string and three keys.

    This is the column reader: every `<tile>` rule counts, the `defaultTile`
    placeholders included, because they are what the pack on disk holds. §4's
    per-session gate uses `pack_seen_rule_keys` instead, which drops them.
    """
    out = set()
    for h in hires_paths:
        out |= {(data, pal) for _, data, pal, _ in _rules(Path(h))}
    return out


def pack_seen_rule_keys(hires_paths) -> set:
    """The drawn keys of the rules a run *wrote* - the placeholders dropped.

    ADR-0239 §4's counts are drawn keys, and its 2026-09-26 amendment excludes
    the builder's `defaultTile` rules from both of them: "a bootstrapped CHR ROM
    pack carries one for every CHR index, so every pack of one ROM shares them".
    A placeholder is written with the neutral ramp palette (`HdPackBuilder.cpp`
    `AddRomTiles`/`AddPrgScanTiles`), so on a CHR ROM game the whole placeholder
    set is identical in every pack of that ROM and belongs to no session.
    """
    out = set()
    for h in hires_paths:
        h = Path(h)
        if not h.exists():
            continue
        for line in h.read_text(encoding="utf-8", errors="replace").splitlines():
            m = RULE_RE.match(line.strip())
            if m and m.group(3) == "N":
                out.add((m.group(1).upper(), m.group(2).upper()))
    return out


def pack_seen_tile_data(hires_paths) -> set:
    """The union's tile-data strings *excluding* the `defaultTile` placeholders.

    Measured 2026-09-26 on the four CHR ROM games of the F14.16 sweep: every
    bootstrapped pack carries a placeholder for the whole CHR - 512 rules for
    Excitebike's 512 tiles, 8192 for Punch-Out's, SMB3's and Ninja Gaiden's -
    against 335, 999, 477 and 668 distinct tile-data strings the runs really
    wrote. A figure over *every* rule is therefore 100% for any pack the
    recorder wrote, and the one that can move is this.
    """
    out = set()
    for h in hires_paths:
        h = Path(h)
        if not h.exists():
            continue
        for line in h.read_text(encoding="utf-8", errors="replace").splitlines():
            m = RULE_RE.match(line.strip())
            if m and m.group(3) == "N":
                out.add(m.group(1).upper())
    return out


def pack_version(hires) -> int:
    """The pack's `<ver>`, which is what decides how its tile field is read."""
    hires = Path(hires)
    if not hires.exists():
        return DEFAULT_PACK_VERSION
    for line in hires.read_text(encoding="utf-8", errors="replace").splitlines():
        m = VER_RE.match(line.strip())
        if m:
            return int(m.group(1))
    return DEFAULT_PACK_VERSION


def find_pack_hires(session_dir: Path) -> Path:
    """The bootstrap pack a session wrote.

    `MepPackManager::GetSiblingFolder` puts it beside the ROM, at
    `<rom stem>/auto/textures/hires.txt`.
    """
    for pattern in ("*/auto/textures/hires.txt", "*/auto/hires.txt"):
        hits = sorted(session_dir.glob(pattern))
        if hits:
            return hits[0]
    hits = sorted(session_dir.rglob("hires.txt"))
    return hits[0] if hits else session_dir / "hires.txt"


def pack_hires(path) -> Path:
    """The `hires.txt` a `--baseline`/`--reference` argument names (§5).

    ADR-0239 §5 says `--baseline <pack dir>`, and what a caller has on disk is
    never one shape: the `auto/` folder a bootstrap writes (holding
    `textures/hires.txt`), a pack folder an artist keeps (holding `hires.txt`),
    the whole session folder a recording wrote (holding `*/auto/textures/...`),
    or the file itself. One resolver answers all four, so a wrong `--baseline`
    cannot quietly count as zero tiles.
    """
    p = Path(path)
    if p.is_file():
        return p
    for rel in ("hires.txt", "textures/hires.txt"):
        if (p / rel).is_file():
            return p / rel
    return find_pack_hires(p)


# --- the ROM's own CHR (ADR-0239 §5.2) ---------------------------------------
def rom_chr_bytes(rom_path) -> bytes:
    """The CHR bytes of an iNES file, or `b""` for a CHR RAM game.

    The header rule is `artist_chr_kit.Rom`'s and is imported rather than
    repeated: iNES byte 5 is the CHR size in 8 KB units, PRG starts at byte 16
    (after a 512-byte trainer when byte 6 bit 2 is set) and CHR follows it.
    """
    sys.path.insert(0, str(SCRIPT_DIR))
    import artist_chr_kit  # noqa: E402 - one iNES reader in this repo
    return artist_chr_kit.Rom(Path(rom_path)).chr


def chr_patterns(chr_bytes: bytes) -> dict:
    """`{16-byte pattern: [indices]}` for every **non-blank** pattern.

    Blank is the 16 zero bytes the builder buckets a fully transparent tile in
    (`mep_addition.is_blank_sprite`), and it is counted on neither side: a game
    with 400 blank tiles has not 400 more tiles to cover, and a pack that names
    index 0 has covered nothing.
    """
    out = {}
    for i in range(len(chr_bytes) // 16):
        pat = bytes(chr_bytes[i * 16:(i + 1) * 16])
        if not any(pat):
            continue
        out.setdefault(pat, []).append(i)
    return out


def _named_pattern(chr_bytes: bytes, patterns, data: str, version: int):
    """The pattern one `<tile>` rule names, or None when it names no tile.

    `patterns` is the set an identity has to be *in* to count -
    `chr_patterns(chr_bytes)`, §5.2's denominator - or None when the caller
    wants the pattern the rule names whether or not this ROM's CHR holds it.
    §5.3's reference comparison is the second caller: it asks what a rule
    names, and a rule that carries its 16 bytes says so without a ROM at all
    (`pack_named_patterns`).
    """
    if len(data) >= 32:
        # A CHR RAM pattern: the rule carries the 16 bytes, so it names a
        # pattern by value. The loader reads the first 16 byte pairs.
        try:
            pat = bytes.fromhex(data[:32])
        except ValueError:
            return None
        return pat if patterns is None or pat in patterns else None
    sys.path.insert(0, str(SCRIPT_DIR))
    import mep_addition  # noqa: E402 - the repo's index reader, with the ADR-0210 rule
    try:
        index = mep_addition.parse_index(data, version)
    except ValueError:
        return None
    off = index * 16
    if off + 16 > len(chr_bytes):
        return None
    pat = bytes(chr_bytes[off:off + 16])
    return pat if patterns is None or pat in patterns else None


def _chr_coverage(hires_paths, rom_path, seen_only: bool) -> dict:
    chr_bytes = rom_chr_bytes(rom_path)
    if not chr_bytes:
        return {"status": CHR_RAM_NOTE, "patterns": 0, "named": 0, "pct": None}
    patterns = chr_patterns(chr_bytes)
    named = set()
    for h in hires_paths:
        version = pack_version(h)
        data_set = (pack_seen_tile_data([h]) if seen_only else tile_data_keys(h))
        for data in data_set:
            pat = _named_pattern(chr_bytes, patterns, data, version)
            if pat is not None:
                named.add(pat)
    total = len(patterns)
    return {
        "status": "ok",
        "patterns": total,
        "named": len(named),
        "pct": round(100.0 * len(named) / total, 1) if total else 0.0,
    }


def rom_chr_coverage(hires_paths, rom_path) -> dict:
    """§5.2, read literally: the patterns *every* `<tile>` rule of the union names.

    The unit is the *pattern*, not the index: two indices holding the same
    pattern are one tile of art, so a pack that names both has covered one.

    This is the ADR's own wording, and on a pack the recorder wrote it reads
    100% for every CHR ROM game, because the builder writes a placeholder for
    every CHR index. `rom_chr_seen_coverage` is the row that moves; report
    both, and never this one alone.
    """
    return _chr_coverage(hires_paths, rom_path, seen_only=False)


def rom_chr_seen_coverage(hires_paths, rom_path) -> dict:
    """§5.2 over the rules a run really wrote - the figure that can move.

    Same denominator, and a numerator that excludes the `defaultTile`
    placeholders (`HdPackBuilder.cpp:139` excludes them for the same reason:
    they are slots waiting for art). Measured 2026-09-26 on the F14.16
    baselines: Excitebike 456 patterns named, Punch-Out 7291, SMB3 5530 and
    Ninja Gaiden 6405 - all 100% over every rule, and over the written ones
    the same packs read 335/456, 999/7291, 477/5530 and 668/6405.
    """
    return _chr_coverage(hires_paths, rom_path, seen_only=True)


# --- the identity two packs of one ROM share (ADR-0239 §5.3) ------------------
def pack_named_patterns(hires_paths, rom_path=None, seen_only=True) -> set:
    """The 16-byte CHR patterns a pack's written `<tile>` rules name.

    §5.3 compares two packs of one ROM, and the one thing those two files are
    guaranteed **not** to share is how they spell a tile. A community pack is
    `<ver>100` and writes a decimal, unpadded CHR index (`<tile>0,1,FF072235,
    8,0,1,N`); a bootstrapped pack is `<ver>109` and writes a hex, padded one
    (`<tile>0,1000,0F25300F,0,0,1,N`). Keying the comparison on the field as a
    string therefore compares two spellings - `{'1','10','100',...}` against
    `{'00','01','0100',...}` - which intersect by accident and read the same
    figure for every pack of the game (#545, measured on Ninja Gaiden: the
    baseline, all 21 sessions and the union each read 1003/7382).

    Each rule names a pattern either way, and that is the identity to ask at:
    at it, the same packs read 535/6208 (8.6%) before against 2705/6208
    (43.6%) after. `_named_pattern` resolves both readings - the 32-hex field
    that carries the bytes, and the index through the ROM's own CHR - so this
    is `rom_chr_seen_coverage`'s numerator without its denominator.

    `seen_only` drops the builder's `defaultTile` placeholders, §5.1's unit; a
    pack that names tiles by index and no ROM to resolve them through is
    refused, because an unresolvable index is not a measurement of zero
    coverage and §5.3 forbids printing one.
    """
    paths = [Path(h) for h in hires_paths]
    chr_bytes = rom_chr_bytes(rom_path) if rom_path else b""
    # None, not {}: "this ROM holds no CHR to be a member of" is a CHR RAM
    # game (or no ROM at all), where a pattern is named by value and there is
    # nothing to check it against.
    patterns = chr_patterns(chr_bytes) if chr_bytes else None
    out, unresolved = set(), 0
    for h in paths:
        version = pack_version(h)
        data_set = (pack_seen_tile_data([h]) if seen_only else tile_data_keys(h))
        for data in data_set:
            if len(data) < 32 and not chr_bytes:
                unresolved += 1
                continue
            pat = _named_pattern(chr_bytes, patterns, data, version)
            if pat is not None:
                out.add(pat)
    if unresolved:
        raise ValueError(
            f"{unresolved} rule(s) over {len(paths)} pack(s) name a tile by CHR "
            "index, and the CHR-pattern identity needs the ROM to read an index "
            "through (ADR-0239 s5.2) - without it the comparison would be "
            "scored at nothing, which §5.3 forbids printing as 0%. Pass the ROM.")
    return out


def version_note(reference, compared) -> str:
    """The `<ver>` bases of the two files, when they differ (ADR-0239 §5.3).

    A note, never a gate: `pack_named_patterns` resolves either dialect, so a
    mismatch does not spoil the figure. What it does mean is that the reader is
    looking at two files that do not agree on how a tile is written, which is
    worth saying beside the number - §5.3's own refusal is a pack keyed for a
    patched ROM (#225) and it is a different check.
    """
    ref_v = pack_version(reference)
    others = sorted({pack_version(h) for h in compared
                     if Path(h).exists() and Path(h).is_file()})
    if not others or others == [ref_v]:
        return ""
    return (f"the reference pack declares <ver>{ref_v} and the compared packs "
            f"declare <ver>{', '.join(str(v) for v in others)}: a `<tile>` field "
            "shorter than 32 hex digits is a decimal CHR index below <ver>103 "
            "and a hex one at 103+ (HdPackLoader::ReadTileData), so the two "
            "files are not written in the same dialect. A field of 32 digits is "
            "the pattern itself and reads the same at any <ver>. Both sides are "
            "scored at the identity they share - the 16-byte CHR pattern each "
            "rule names - so the figures stand.")


# --- a session must prove it went somewhere (ADR-0239 §4) --------------------
def ram_check_spec(raw: dict, where: str) -> dict:
    """Normalise a `values[].ramCheck`, refusing a bad one before anything runs.

    Same rule as a cheat and for the same reason (ADR-0184 §1): the check reads
    the emulator's internal RAM through `mss_ram.py`, so an address outside
    `$0000-$07FF` is a check that can only ever fail, and `expect` is one byte
    written the way the cheat's value is.
    """
    address = str(raw.get("address", "")).strip().upper()
    if not RAM_ADDRESS_RE.match(address):
        raise ValueError(
            f'{where}: address "{raw.get("address", "")}" is not a 1-4 digit '
            "hex RAM address (ADR-0184 s1's $0000-$07FF rule).")
    if int(address, 16) >= 0x0800:
        raise ValueError(
            f'{where}: address ${address} is outside the NES\'s internal RAM '
            "($0000-$07FF) (ADR-0184 s1).")
    expect = raw.get("expect")
    expect = [expect] if isinstance(expect, str) else list(expect or [])
    expect = [str(e).strip().upper() for e in expect]
    if not expect or any(not re.match(r"^[0-9A-F]{2}$", e) for e in expect):
        raise ValueError(
            f'{where}: expect must be one byte as "VV" or a list of them, '
            f"not {raw.get('expect')!r}.")
    return {"address": address, "expect": expect}


def ram_check(spec: dict, state_path, read_ram=None, pinned_addresses=None) -> dict:
    """Read one byte off the session's final state and compare it.

    ADR-0239 §4: "when the profile names one, a RAM check read off the
    session's final state". `read_ram` is injectable so the comparison is unit
    testable; the default is `scripts/mss_ram.py`'s own reader, the one the
    F9.22 chains are steered by, so a state this cannot read is a state no
    other tool here can read either.

    `pinned_addresses` is **every** address the session pins, as
    `{address: byte}` - the selector and each `values[].cheats` /
    `defaults.cheats` pin, which is what the run really applies. A check on any
    of them carries a `caveat` and the byte it is pinned at: the cheat
    substitutes on the CPU read bus (`CheatManager::ApplyCheat`) and never
    writes memory, so the byte in the state is the game's own value *or* a
    value the game stored after reading the pinned bus, and this one state
    cannot tell those two apart. Measured 2026-09-26: with `cheat=0030:05`
    Contra plays stage 6 while its `$0030` reads 00, and Punch-Out's fight
    loader stores the `$0002` it read back (so a pinned `$0002` reads the pin,
    while the pinned `$0001` reads the game's own 00 in the same state) - a
    pinned address can be a good verdict byte (#546). Naming a pinned address
    is therefore reported with its caveat, never refused: pinning the
    selector's own start value and asserting the byte did not move is
    ADR-0184's inertness control.
    """
    address = str(spec.get("address", "")).strip().upper()
    raw = spec.get("expect")
    expect = [raw] if isinstance(raw, str) else list(raw or [])
    expect = [str(e).strip().upper() for e in expect]
    out = {"address": address, "expect": expect, "actual": None, "ok": False, "why": ""}
    if not RAM_ADDRESS_RE.match(address):
        out["why"] = f'"{address}" is not a hex RAM address'
        return out
    pins = _pinned_map(pinned_addresses)
    if read_ram is None:
        sys.path.insert(0, str(SCRIPT_DIR))
        import mss_ram  # noqa: E402 - the parser the chains already trust
        read_ram = mss_ram.ram
    path = Path(state_path)
    if not path.exists():
        out["why"] = f"the session wrote no final state at {path}"
        return out
    try:
        ram = read_ram(str(path))
    except Exception as exc:  # noqa: BLE001 - a state we cannot read is a result, not a crash
        out["why"] = f"{path} is not a readable save state: {exc}"
        return out
    index = int(address, 16)
    if index >= len(ram):
        out["why"] = f"${address} is past the state's {len(ram)} bytes of internal RAM"
        return out
    out["actual"] = f"{ram[index]:02X}"
    out["ok"] = out["actual"] in expect
    if not out["ok"]:
        out["why"] = f"${address} reads {out['actual']}, not {'/'.join(expect)}"
    if index in pins:
        out["pinnedValue"] = pins[index]
        out["caveat"] = (
            f"${address} is pinned at {pins[index]} for this session, and a RAM "
            "pin substitutes the byte on the CPU read bus and never writes "
            f"memory: an actual other than {pins[index]} is the game's own byte, "
            f"and one equal to it is either the pin itself or a byte the game "
            "stored after reading it - this state alone cannot separate the two."
        )
    return out


def _pinned_map(pinned_addresses) -> dict:
    """The addresses a session pins, as `{int address: pinned byte}`.

    The scalar this replaced (#546) was one address - the selector - so a
    profile that pins through `values[].cheats` and checks one of those never
    matched it. A bare string is refused loudly rather than read: it is
    iterable, so `index in pinned_addresses` would silently become a substring
    test, which is exactly the failure that hides.
    """
    if pinned_addresses is None:
        return {}
    if isinstance(pinned_addresses, (str, bytes)) or not hasattr(
            pinned_addresses, "items"):
        raise ValueError(
            "pinned_addresses is the {address: byte} map of every address the "
            f"session pins, not {pinned_addresses!r}: a bare address is the "
            "single-address shape the caveat used to be read from (issue #546).")
    out = {}
    for pin_address, pin_value in pinned_addresses.items():
        text = str(pin_address).strip().upper()
        if not RAM_ADDRESS_RE.match(text):
            raise ValueError(
                f'pinned_addresses: "{pin_address}" is not a hex RAM address')
        out[int(text, 16)] = str(pin_value).strip().upper()
    return out


def score_sessions(results, baseline_hires) -> dict:
    """ADR-0239 §4 as amended 2026-09-26: every session's counts and status.

    Two counts per session, both in **drawn keys**, and they answer different
    questions:

    - `new` is the keys the **baseline packs** do not hold. It is the gate:
      `new == 0` means the session holds nothing but what the game's current
      route already has - a stage-1 or title recording under another stage's
      name - so it is `did-not-warp` and "counts in no total" (`didNotWarp`,
      not `counted`).
    - `unique` is the keys no *other session* of the sweep and no baseline
      holds. It is reported for reading, **never** for gating, because "two
      tracks that share a tileset are both legitimate warps with `unique == 0`,
      so gating on it would drop both".

    Neither count includes the builder's `defaultTile` placeholders (§4's own
    amendment): a bootstrapped CHR ROM pack carries one per CHR index, so every
    pack of one ROM shares them and no session may claim them.

    Because `new` is measured against the baseline alone, a `did-not-warp`
    session's keys are by definition already in the baseline, so §5's `after` -
    the union of that pack and every sweep session - is the same set whether the
    `did-not-warp` sessions are in it or not. `unionHires` therefore keeps
    **every** session that produced a pack, which is literally "every sweep
    session" and cannot be inflated.

    A session that ran is selected by its status being "ok" *or* already
    "did-not-warp", so scoring the same list twice returns the same answer:
    this function writes the status it reads, and a `live` test of `== "ok"`
    would drop every `did-not-warp` session on the second pass and report a
    different union from the one the table printed.

    **Which unit, and why the drawn key.** `(tileData, palette)` is the cell
    identity ADR-0194 §2 names ("a `(tileData, palette)` cell is the same cell
    in any recording of that ROM") and the finer of the two units §5.1 reports,
    and it is the one that can answer §4's question. Measured on real packs
    2026-09-26: tile data over *every* `<tile>` rule is frozen across the packs
    of one game, because the placeholders enumerate the whole CHR (a Punch-Out
    baseline and a fresh session hold the very same 8192 index strings, and the
    rules a run writes name indices that are already in that enumeration) - on
    `runs/f1416/excitebike/rehearsal` all 13 sessions read `newTiles == 0` while
    their union gains 1422 drawn keys. The tile data a run *wrote* is the unit
    ADR-0184's measurement used, but it is one cell coarser: it cannot see the
    same art repainted under another palette, which ADR-0194 §2 calls a cell of
    its own. Over those 13 sessions it reads the same 13/13 `new` but only 1/13
    `unique` (`design` 6) against 3/13 over drawn keys (`track-a2` 114, `design`
    23, `track-a1` 1; runs/f1416/code/excitebike-rescore2.txt). The tile-data
    counts are still reported - `tiles` over every rule, `seen` over the written
    ones, `newTiles` over every rule against the baseline - so nothing is hidden
    and the ADR's wording can be re-read against the numbers.
    """
    live = [r for r in results if r.get("status") in ("ok", "did-not-warp")]
    keys = {r["name"]: pack_seen_rule_keys([r["hires"]]) if r.get("hires") else set()
            for r in live}
    tile_data = {r["name"]: tile_data_keys(r["hires"]) if r.get("hires") else set()
                 for r in live}
    base_keys = pack_seen_rule_keys(baseline_hires)
    base_tiles = pack_tile_data(baseline_hires)
    for r in live:
        name = r["name"]
        others_keys = set().union(*[k for n, k in keys.items() if n != name])
        r["keys"] = len(keys[name])
        r["tiles"] = len(tile_data[name])
        r["seen"] = len(pack_seen_tile_data([r["hires"]])) if r.get("hires") else 0
        r["new"] = len(keys[name] - base_keys)
        r["unique"] = len(keys[name] - others_keys - base_keys)
        r["newTiles"] = len(tile_data[name] - base_tiles)
        # The status is derived, never latched: the same list against a
        # different baseline has to be able to move a session back to "ok".
        r["status"] = "did-not-warp" if r["new"] == 0 else "ok"
        if r["new"] == 0:
            r["why"] = ("every drawn key this session holds is one the baseline "
                        "packs already hold, so it added nothing to the union")
    return {
        "counted": [r["name"] for r in live if r["status"] == "ok"],
        "didNotWarp": [r["name"] for r in live if r["status"] == "did-not-warp"],
        # §5: "After is the union of that pack and **every** sweep session". On
        # this rule a `did-not-warp` session's keys are already in the baseline,
        # so keeping it changes nothing - and dropping it would be a filter the
        # ADR does not ask for.
        "unionHires": [Path(r["hires"]) for r in live if r.get("hires")],
    }


# --- the totals (ADR-0239 §5) ------------------------------------------------
def _hit(have: set, reference: set) -> dict:
    hit = len(have & reference)
    return {"tiles": hit,
            "pct": round(100.0 * hit / len(reference), 1) if reference else 0.0}


def totals_document(before_hires, after_hires, rom_path=None, reference=None) -> dict:
    """`{before, after}` as §5 lists them, plus `reference` when one is given.

    `before` is the game's current stage-1 route (the `--baseline` packs),
    `after` is the union of it and every session that proved it warped. Each
    carries its drawn keys, its distinct tile data and - when a ROM is named -
    its share of the ROM's own non-blank CHR patterns. `romChr` is None, not
    zero, when `--rom-chr` was not asked for: an absent measurement and a
    measured zero are different claims.

    **§5.1's row is the rules a run *wrote*, and it was not.** Measured
    2026-09-26 on Castlevania (CHR RAM): the builder writes one `defaultTile`
    placeholder per PRG-scan pattern - 2581 of them, distinct, identical in
    every pack of that ROM - so `keys` and `tileData` over every rule read
    3284/2688 before against 3925/2700 after: the placeholder set plus a
    rounding error, and the reference hit it feeds (1752 -> 1761) was mostly
    the scan, not the run. Over the written rows the same sweep reads
    588 -> 698 tile data and 588 -> 685 of the reference. The CHR ROM games
    have the same defect in another shape - the placeholders there enumerate
    the CHR indices, so `newTiles` read 0 for all 13 Excitebike sessions while
    their union gained 1422 drawn keys.

    So `keys`/`tileData` are the written unit, the one §4's 2026-09-26
    amendment already gave `new`/`unique`, and `keysAll`/`tileDataAll` carry
    the every-rule counts beside them so a pack whose placeholders dominate is
    visible rather than hidden. `reference`'s hits follow the same split.
    """
    out = {}
    for label, paths in (("before", before_hires), ("after", after_hires)):
        out[label] = {
            "keys": len(pack_seen_rule_keys(paths)),
            "keysAll": len(pack_rule_keys(paths)),
            "tileData": len(pack_seen_tile_data(paths)),
            "tileDataAll": len(pack_tile_data(paths)),
            # §5.2's literal row, and the same row over the rules a run wrote.
            # They are equal for a CHR RAM game and can never differ on the
            # denominator; on a bootstrapped pack the first is 100% by
            # construction, so only the second is a coverage figure.
            "romChr": rom_chr_coverage(paths, rom_path) if rom_path else None,
            "romChrSeen": rom_chr_seen_coverage(paths, rom_path) if rom_path else None,
        }
    if reference is not None:
        # §5.3 is asked at the CHR pattern (#545): the two packs compared here
        # write their `<tile>` field in different bases - a community pack is
        # `<ver>100` decimal, a bootstrapped one `<ver>109` hex - so the field
        # as a string is not an identity they share. `tileData` keeps its name
        # because the F14.16 table reads it, and `unit` says what it holds.
        ref_written = pack_named_patterns([reference], rom_path)
        ref_all = pack_named_patterns([reference], rom_path, seen_only=False)
        compared = [h for h in list(before_hires) + list(after_hires)
                    if Path(h).is_file()]
        out["reference"] = {
            "pack": str(reference),
            "unit": "chr-pattern",
            "tileData": len(ref_written),
            "tileDataAll": len(ref_all),
            # The drawn rows are the ones that answer §5.3: a pattern the run
            # never drew is not "held" by the union however many placeholders
            # name its bytes.
            "before": _hit(pack_named_patterns(before_hires, rom_path), ref_written),
            "after": _hit(pack_named_patterns(after_hires, rom_path), ref_written),
            "beforeAll": _hit(pack_named_patterns(before_hires, rom_path,
                                                  seen_only=False), ref_all),
            "afterAll": _hit(pack_named_patterns(after_hires, rom_path,
                                                 seen_only=False), ref_all),
            "versions": {
                "reference": pack_version(reference),
                "compared": sorted({pack_version(h) for h in compared}),
            },
            "note": version_note(reference, compared),
        }
    return out


def summary_document(game, rom, seconds, results, totals) -> dict:
    """The machine-readable report `--summary <path>` writes (§5).

    One row per session - `{name, status, tiles, new, unique, ramCheck}`, the
    two §4 counts in drawn keys, `new` gating and `unique` reported - and the
    totals beside them, so a caller compares two sweeps without parsing prose.
    A session that never produced a pack (failed, skipped, dry) reports `new`
    as null: it is unknown, not zero, and reading it as zero would call a
    crashed session `did-not-warp`.
    """
    return {
        "game": game,
        "rom": str(rom),
        "seconds": seconds,
        "sessions": [{
            "name": r["name"],
            "status": r["status"],
            "tiles": r.get("tiles", 0),
            "new": r.get("new"),
            "unique": r.get("unique"),
            "ramCheck": r.get("ramCheck"),
            # Beside §4's verdict, every count it could have been read from:
            # drawn keys (`new`, `unique`), tile data over every rule (`tiles`)
            # and over the written ones (`seen`), and the tile-data count
            # against the baseline (`newTiles`).
            "keys": r.get("keys"),
            "seen": r.get("seen"),
            "newTiles": r.get("newTiles"),
        } for r in results],
        "totals": totals,
    }
