#!/usr/bin/env python3
"""ADR-0244 (P.9) first step: is a pack change applied in place exact?

The in-place path is `Emulator::ReloadRomKeepingState` (export
`ReloadRomKeepingState`): save the state to memory, reload the ROM with the new
pack switches, load the state back. This harness measures it per transition
against what ADR-0244 section 4 names as the truth: the same frames played from
a fresh load of the target pack, with the same state loaded the ordinary way.

For each transition it runs three `headless_record session` processes
(`scripts/step_emu.py`):

  prep       loads the minted state S0, plays N frames of the route, writes S1.
  reference  launched with the *target* switches; loads S1 from the file the
             ordinary way, plays M frames.
  swap path  launched with the *source* switches; plays the same N frames from
             S0 (its state must equal S1 byte for byte), sets the *target*
             switches (`mep ...`), swaps, and plays on to the reference's last
             frame.

Both capture every frame they play (`capture`: the screenshot pipeline's
pixels, HD art included, FNV-1a) and, on the NES, read the 2 KB of internal RAM
after each, then keep their final state. Frames are compared by the emulator's
own frame number (see run_transition). Pass, per transition, is ADR-0244's: the
swap restored the state (not a fallback restart), every reference frame is
pixel-identical on the swap path, both end on the same frame, and the final CPU
(`cpu.*`), RAM and video (`ppu.*`/`vdp.*`) fields of the two final states are
byte-identical. Every other state field that differs is listed too, so nothing
is rounded away.

The transitions are the player's own switches, the ones the Enhancements panel
and the picker drive (ADR-0149, P.5): Textures off/on, a different pack picked
(one container disabled, another enabled), and an audio-only pack's Audio
switch off/on - the case where the console gains or loses its HD audio device
(`_hdAudioDevice`, serialized only when present), plus (NES) a border-only
pack's Border switch and a swap to and from a ROM-patch pack. A control
transition changes nothing and a negative control must fail; see transitions().

Fixtures are minted here, never committed (`.mss` and ROM-derived packs are not
versioned): the state from the user's own ROM with the committed mint script,
pack A from a 1x recording of the route (`headless_record ... hdpack`) with its
images inverted, pack B the same recording with its color channels rotated (so
A, B and no pack render differently), and an audio-only pack whose `<bgm>` the
game never triggers.

    python3 scripts/pack_swap_exactness.py [--console nes|sms|gb|gbc] [--rom <file>]
                                           [--work <dir>] [--n 240] [--m 120]
                                           [--json out.json]

GB/SMS run the texture transitions only (HD audio packs are NES-only) and
compare frames and the final state but not per-frame RAM (the session's `ram`
verb reads NES RAM). Exit 0 when every transition matches its expected verdict
(the negative control is expected to FAIL), 1 otherwise; the table is printed
either way. `scripts/test_pack_swap_exactness.py` runs it and holds the verdicts the
slice recorded.

Two cases sit beside the transitions, both from ADR-0244 (P.9):

  * **The bootstrap on a swap**, where the pack the bootstrap wrote beside the
    ROM is the source or the target (the `bootstrap` profile key). Every session
    of the case runs with the bootstrap on, so the reload a swap makes calls the
    same `StartBootstrapIfNeeded` a fresh load does, and the pack in play is the
    one the bootstrap itself wrote - a sibling (MEP-v1 sec. 2.1), which outranks
    every installed pack. It gets its own ROM copy (`work/bootstrap/rom/`),
    because a sibling created next to the ROM every other transition loads would
    be the pack in play in all of them. When the ROM is missing or the bootstrap
    wrote no pack, the case is printed as NOT MEASURED with the input it lacks -
    never as a pass.
  * **The PPU-swap alternative of Decision 1**, which is reported as NOT
    MEASURED: the protocol can only be run against a swap that exists, and the
    only pack-change entry points in this tree are the session's `swap` verb and
    `Emulator::ReloadRomKeepingState` (save state, reload, restore). Both are
    read from the sources at run time, so a second entry point shows up in the
    report instead of passing quietly.
"""
import argparse
import json
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
import gen_mep_test_pack  # noqa: E402
import mss_ram  # noqa: E402
import step_emu  # noqa: E402

BINARY = HERE / "headless_record"
LIBRARY = Path("/Users/bihaiko/VSCodeProjects/EMULADORES/2. Switch")
STAGES = ROOT / "scripts" / "stages"
PACK_A, PACK_B, PACK_AUDIO = "p9-pack-a", "p9-pack-b", "p9-pack-audio"
PACK_BORDER, PACK_PATCH = "p9-pack-border", "p9-pack-patch"
#headless_record's launch flag for EnhancementPackConfig.BootstrapEnhancementFolder
#- the setting that makes a load record the ROM's own enhancement folder.
BOOT_FLAG = "bootstrap"
BOOTSTRAP_CASE = "bootstrap on a swap"
PPU_SWAP_CASE = "PPU-swap alternative (Decision 1)"

#One profile per console. `mint` is what the state S0 is minted with (an input
#script, or none: the game's own attract mode), `route` the input the run plays
#(None: idle), `video`/`ram` the state fields ADR-0244 holds to byte identity
#beside `cpu.*`, `read_ram` whether the session's `ram` verb serves this console
#(NES only), `audio` whether an audio-only pack applies (NES HD audio only),
#`extra` whether the border-only and the ROM-patch packs are measured too: both
#are console-agnostic (the border is drawn by the renderer, the patch guard is
#Emulator::ReloadRomKeepingState's), so the NES run covers them.
#`rom` None means a synthetic ROM `gen_hdpack_test_roms.py` writes into the
#work folder: there is no commercial GB ROM in the library, and the synthetic
#one draws a still screen, so its frames prove less than a game's do.
#`bootstrap` whether the bootstrap-on-a-swap case runs for this console (below).
#It is on where the run has a real game whose screen moves - the pack the
#bootstrap wrote has to dress the frames that are compared to be worth
#comparing - and off on GB/GBC, whose synthetic ROM draws a still screen the
#profile never moves and whose run has no route.
PROFILES = {
    "nes": dict(rom=LIBRARY / "G3 - Nitendinho" / "roms" / "Ninja Gaiden (1989) (Tecmo).nes",
                mint=STAGES / "ninjagaiden" / "mint-stage1.txt", mint_seconds=18,
                route=STAGES / "ninjagaiden" / "stage1-run.txt", record_seconds=12,
                video="ppu.", ram="memoryManager.internalRam", read_ram=True, audio=True, extra=True,
                bootstrap=True),
    "sms": dict(rom=LIBRARY / "G3 - MasterSystem" / "roms" / "Sonic the Hedgehog (1991) (Sega).sms",
                mint=None, mint_seconds=40, route=None, record_seconds=12,
                video="vdp.", ram="memoryManager.workRam", read_ram=False, audio=False, extra=False,
                bootstrap=True),
    "gb": dict(rom=None, synthetic="test_dmg.gb",
               mint=None, mint_seconds=5, route=None, record_seconds=12,
               video="ppu.", ram="memoryManager.workRam", read_ram=False, audio=False, extra=False,
               bootstrap=False),
    "gbc": dict(rom=None, synthetic="test_cgb.gbc",
                mint=None, mint_seconds=5, route=None, record_seconds=12,
                video="ppu.", ram="memoryManager.workRam", read_ram=False, audio=False, extra=False,
                bootstrap=False),
}
#18 s covers the 1075-frame NES mint script (scripts/test_step_emu_rom.py); 12 s
#of recording covers N + M frames from S0.
DEFAULT_ROM = PROFILES["nes"]["rom"]

def pack_names(profile):
    names = [PACK_A, PACK_B]
    if profile["audio"]:
        names.append(PACK_AUDIO)
    if profile["extra"]:
        names += [PACK_BORDER, PACK_PATCH]
    return names


def transitions(profile):
    """(name, source launch flags, target switch requests, reference launch
    flags, expected pass, expected swap outcome) per transition. Every launch
    disables the packs it does not use, so exactly one is in play. The control
    changes nothing: if it fails, the harness is wrong, not the swap. The
    negative control swaps to pack B but plays the reference with pack A: it
    must FAIL, or the frames compared are blind to which pack renders. The two
    ROM-patch transitions must come back `patch-restarted` (ADR-0244 section 2):
    nothing is compared after them, a fresh load is the expected result."""
    names = pack_names(profile)

    def only(name):
        return [f"mep-disable={other}" for other in names if other != name]
    a_to_b = [("disable", PACK_A), ("enable", PACK_B)]
    out = [
        ("control: pack A -> pack A", only(PACK_A), [], only(PACK_A), True, "restored"),
        ("none -> pack (Textures on)", only(PACK_A) + ["mep-notextures"], [("textures", "on")],
         only(PACK_A), True, "restored"),
        ("pack -> none (Textures off)", only(PACK_A), [("textures", "off")],
         only(PACK_A) + ["mep-notextures"], True, "restored"),
        ("pack A -> pack B (picker)", only(PACK_A), a_to_b, only(PACK_B), True, "restored"),
    ]
    if profile["audio"]:
        out += [
            ("audio-only pack on (Audio on)", only(PACK_AUDIO) + ["mep-noaudio"], [("audio", "on")],
             only(PACK_AUDIO), True, "restored"),
            ("audio-only pack off (Audio off)", only(PACK_AUDIO), [("audio", "off")],
             only(PACK_AUDIO) + ["mep-noaudio"], True, "restored"),
        ]
    if profile["extra"]:
        out += [
            ("border-only pack on (Border on)", only(PACK_BORDER) + ["mep-noborder"], [("border", "on")],
             only(PACK_BORDER), True, "restored"),
            ("border-only pack off (Border off)", only(PACK_BORDER), [("border", "off")],
             only(PACK_BORDER) + ["mep-noborder"], True, "restored"),
            ("pack A -> ROM-patch pack (picker)", only(PACK_A), [("disable", PACK_A), ("enable", PACK_PATCH)],
             None, True, "patch-restarted"),
            ("ROM-patch pack -> pack A (picker)", only(PACK_PATCH), [("disable", PACK_PATCH), ("enable", PACK_A)],
             None, True, "patch-restarted"),
        ]
    out.append(("negative: A -> B vs reference A", only(PACK_A), a_to_b, only(PACK_A), False, "restored"))
    return out


def bootstrap_transitions(profile):
    """The bootstrap case's transitions: the pack the bootstrap wrote beside the
    ROM is the source, then the target, of the swap.

    The bootstrap leaves `<romFolder>/<romName>/auto/rec-NNN/textures/` next to
    the ROM (ADR-0049, ADR-0243), which is a sibling pack and outranks every
    installed one, so it is the pack in play until a switch turns it off. Every
    session here is launched with the bootstrap on - the case is "the bootstrap
    on a swap", not a pack that happens to have been recorded once - so the
    reload the swap makes runs the same `StartBootstrapIfNeeded` a fresh load
    does. What it did on each load is in the session's own log
    (`<work>/prep|ref|swap/swap.log`, the `bootstrap:` lines): with the pack
    already beside the ROM it declines and records nothing.

    The probe comes first and is the only row whose verdict is evidence. It
    turns the Textures switch off on the swapped side while the reference plays
    with it on, so it must FAIL: the two frames differ unless the comparison
    cannot see which pack renders. Measured 2026-10-05 on NES, it did *not*
    fail - the frames came back identical - and the three transitions behind it
    were therefore comparing nothing: with the bootstrap on in every session,
    each load writes its own `rec-NNN` beside the ROM (rec-001 ... rec-017 in one
    run), so the reference and the swapped side are each dressed by a pack
    recorded over the very window being compared, and the switch that should
    separate them does not. The three rows are kept for the day the probe is
    honest; `main` does not run them until it is, and reports the case as not
    measured instead.
    """
    on = [BOOT_FLAG]
    off = [BOOT_FLAG, "mep-notextures"]
    return [
        ("bootstrap probe: off vs reference on (must FAIL)", on, [("textures", "off")], on,
         False, "restored"),
        ("bootstrap: control (auto -> auto)", on, [], on, True, "restored"),
        ("bootstrap: auto -> none (Textures off)", on, [("textures", "off")], off, True, "restored"),
        ("bootstrap: none -> auto (Textures on)", off, [("textures", "on")], on, True, "restored"),
    ]


# ---- input script slicing ---------------------------------------------------

def expand_script(text):
    """One button token per frame, from a script of `<count>f <buttons>` lines
    (the only form the committed routes use)."""
    frames = []
    for raw in text.splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        count, _, buttons = line.partition(" ")
        if not count.endswith("f") or not count[:-1].isdigit():
            raise ValueError(f"unsupported script line: {raw!r}")
        frames.extend([buttons.strip() or "-"] * int(count[:-1]))
    return frames


def compress_script(frames):
    lines = []
    for token in frames:
        if lines and lines[-1][1] == token:
            lines[-1][0] += 1
        else:
            lines.append([1, token])
    return "".join(f"{n}f {token}\n" for n, token in lines)


# ---- save state fields --------------------------------------------------------

def state_fields(path):
    """Every `key -> bytes` field of a .mss's Serializer blob, plus the raw PPU
    frame the header carries (`<header frame>`)."""
    data = Path(path).read_bytes()
    offset = mss_ram.video_offset(data)
    inflater = zlib.decompressobj()
    frame = inflater.decompress(data[offset:])
    blob = mss_ram.blob(path)
    fields = {"<header frame>": frame}
    i = 0
    while i < len(blob):
        end = blob.index(b"\0", i)
        key = blob[i:end].decode("ascii")
        size = struct.unpack("<I", blob[end + 1:end + 5])[0]
        fields[key] = blob[end + 5:end + 5 + size]
        i = end + 5 + size
    return fields


def field_group(key, profile):
    if key.startswith("cpu."):
        return "cpu"
    if key.startswith(profile["video"]):
        return "video"
    if key == profile["ram"]:
        return "ram"
    return "other"


def diff_fields(a, b):
    """Keys whose bytes differ, or that only one side has."""
    return sorted(k for k in set(a) | set(b) if a.get(k) != b.get(k))


# ---- fixtures -----------------------------------------------------------------

def one_shot(rom, prefix, seconds, *flags):
    Path(prefix).parent.mkdir(parents=True, exist_ok=True)
    done = subprocess.run([str(BINARY), str(rom), str(seconds), str(prefix), *flags],
                          capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError(f"headless_record {' '.join(flags)} failed:\n{done.stdout}{done.stderr}")
    return done.stdout


def recolour_pngs(folder, mode):
    from PIL import Image, ImageChops  # noqa: PLC0415 - only the fixture needs Pillow
    for png in Path(folder).rglob("*.png"):
        image = Image.open(png).convert("RGBA")
        r, g, b, a = image.split()
        if mode == "invert":
            rgb = ImageChops.invert(Image.merge("RGB", (r, g, b)))
            r, g, b = rgb.split()
        else:
            r, g, b = g, b, r
        Image.merge("RGBA", (r, g, b, a)).save(png)


def build_packs(rom, work, s0, profile):
    """Pack A, pack B and (NES) the audio-only pack, in `<work>/packs/`."""
    record_prefix = work / "record" / "rec"
    route = [f"input={profile['route']}"] if profile["route"] else []
    one_shot(rom, record_prefix, profile["record_seconds"], "hdpack", "mep-off",
             f"state={s0}", *route)
    recorded = Path(str(record_prefix) + "-hdpack")
    if not (recorded / "hires.txt").exists():
        raise RuntimeError(f"the recording wrote no hires.txt under {recorded}")
    sha1 = gen_mep_test_pack.no_intro_sha1(rom)
    system = gen_mep_test_pack.system_for(rom)
    packs = work / "packs"
    for name, mode in ((PACK_A, "invert"), (PACK_B, "rotate")):
        root = packs / name
        shutil.copytree(recorded, root / "textures",
                        ignore=shutil.ignore_patterns("sheets", "*.json", "*.txt.bak"))
        recolour_pngs(root / "textures", mode)
        meta = gen_mep_test_pack.pack_json(f"P.9 {name}", system, sha1)
        meta["sections"] = {"textures": {"path": "textures/"}}
        (root / "pack.json").write_text(json.dumps(meta, indent=2))
    if profile["extra"]:
        build_border_and_patch_packs(rom, packs, system, sha1)
    if not profile["audio"]:
        return packs
    root = packs / PACK_AUDIO
    (root / "audio").mkdir(parents=True)
    (root / "audio" / "hires.txt").write_text("<ver>106\n<bgm>0,0,track.ogg\n")
    #Never opened: the game never writes the HD audio registers. What the pack
    #exercises is the console gaining or losing its HdAudioDevice.
    (root / "audio" / "track.ogg").write_bytes(b"OggS\x00p9-unplayed")
    meta = gen_mep_test_pack.pack_json(f"P.9 {PACK_AUDIO}", "nes", sha1)
    meta["sections"] = {"audio": {"path": "audio/"}}
    (root / "pack.json").write_text(json.dumps(meta, indent=2))
    return packs


def build_border_and_patch_packs(rom, packs, system, sha1):
    """A border-only pack (the renderer draws it; nothing in the console state
    depends on it) and a ROM-patch pack: pack A's textures plus an IPS patch
    for this ROM that flips its last byte. The patch only has to apply - what is
    measured is that a swap to or from it keeps the restart."""
    from PIL import Image  # noqa: PLC0415 - only the fixture needs Pillow
    root = packs / PACK_BORDER
    (root / "border").mkdir(parents=True)
    Image.new("RGBA", (320, 240), (200, 40, 40, 255)).save(root / "border" / "border.png")
    meta = gen_mep_test_pack.pack_json(f"P.9 {PACK_BORDER}", system, sha1)
    meta["sections"] = {"border": {"path": "border/"}}
    (root / "pack.json").write_text(json.dumps(meta, indent=2))
    root = packs / PACK_PATCH
    shutil.copytree(packs / PACK_A / "textures", root / "textures")
    data = rom.read_bytes()
    offset = len(data) - 1
    (root / "flip.ips").write_bytes(b"PATCH" + offset.to_bytes(3, "big") + (1).to_bytes(2, "big")
                                    + bytes([data[-1] ^ 0xFF]) + b"EOF")
    meta = gen_mep_test_pack.pack_json(f"P.9 {PACK_PATCH}", system, sha1)
    meta["sections"] = {"textures": {"path": "textures/"}}
    meta["patches"] = [{"sha1": sha1, "file": "flip.ips"}]
    (root / "pack.json").write_text(json.dumps(meta, indent=2))


def session(rom, work, packs, flags):
    """A session whose home holds its own copy of the packs."""
    home_packs = work / "mesen-home" / "EnhancementPacks"
    shutil.copytree(packs, home_packs)
    return step_emu.StepEmu(rom, work=work, flags=flags)


# ---- the bootstrap case's fixtures --------------------------------------------

def bootstrap_fixture(profile, work):
    """The bootstrap case's ROM copy, empty pack folder and state S0.

    The bootstrap writes beside the ROM, so the case needs a ROM copy of its
    own: a sibling pack created next to the ROM every other transition loads
    would outrank the installed packs and be the pack in play in all of them.

    The pack is minted the way a player's own first run mints it - `bootstrap`
    on, the ROM's directory holding nothing yet, playing from S0 - which is
    also the evidence that a load with the bootstrap on and no pack beside the
    ROM does start a recording and write one.

    Returns `(rom, packs, s0, reason)`: `reason` is empty when the case can run
    and otherwise names what is missing, so the caller records the case as not
    measured rather than as a pass.
    """
    here = work / "bootstrap"
    rom = prepare_rom(profile, profile["rom"], here / "rom")
    if rom is None:
        return None, None, None, "the ROM is not on this machine"
    s0 = here / "s0.mss"
    mint = [f"input={profile['mint']}"] if profile["mint"] else []
    one_shot(rom, here / "mint" / "mint", profile["mint_seconds"], "mep-off", "hdpack-off",
             *mint, f"save-state={s0}")
    #The same frames the transitions play come from the same state, so the
    #recorded tiles cover the window that is compared.
    before = {p.name for p in rom.parent.iterdir()}
    route = [f"input={profile['route']}"] if profile["route"] else []
    one_shot(rom, here / "record" / "rec", profile["record_seconds"], BOOT_FLAG,
             f"state={s0}", *route)
    siblings = [p for p in rom.parent.iterdir() if p.is_dir() and p.name not in before]
    if len(siblings) != 1:
        return None, None, None, (f"the bootstrap wrote no sibling pack beside the ROM "
                                  f"({len(siblings)} new folders in {rom.parent})")
    if not list(siblings[0].rglob("hires.txt")):
        return None, None, None, f"the bootstrap's '{siblings[0].name}' holds no hires.txt"
    packs = here / "packs"
    packs.mkdir(parents=True, exist_ok=True)
    return rom, packs, s0, ""


# ---- the PPU-swap alternative of Decision 1 -----------------------------------

#What the tree is known to hold (ADR-0244 Decision 1). A candidate outside both
#sets is the alarm this row exists for. The two sets are deliberately separate
#from the scan's own filter: naming a pack and swapping one are different
#questions, and the filter that finds candidates is broad on purpose so that a
#new export gets looked at rather than silently missed.
KNOWN_SWAP_ENTRY_POINTS = {"swap", "ReloadRomKeepingState"}
CANDIDATES_THAT_SWAP_NOTHING = {
    "GetForcedPackPatch",       # reads the patch a pack forces; changes no pack
    "SuppressForcedPackPatch",  # a switch over that patch
    "HasWidescreenPackArt",     # answers a question about the loaded pack
    "RequestMepImageReload",    # re-decodes one image in the loaded pack (ADR-0212)
}


def pack_change_entry_points():
    """The tree's pack-change entry points, read from the sources: the session
    verbs a pack swap could go through (`scripts/headless_record.cpp`) and the
    core wrapper's exports whose names read as replacing or interrogating a pack
    (`InteropDLL/EmuApiWrapperMep.cpp`).

    The export filter is a *candidate* filter - `Reload`, `Swap`, `Ppu` or
    `Pack` anywhere in the name - so it also catches exports that name a pack
    without swapping one. Measured 2026-10-05: it returned five, and reading
    them as five entry points made this row report "a second pack-change entry
    point now exists", which was false and would have hidden the day a real one
    appears among the noise. The verdict is therefore taken against
    KNOWN_SWAP_ENTRY_POINTS, and every other candidate must be one this file
    already accounts for (CANDIDATES_THAT_SWAP_NOTHING)."""
    tool = (HERE / "headless_record.cpp").read_text()
    verbs = sorted({v for v in re.findall(r'verb == "([a-z0-9-]+)"', tool) if "swap" in v})
    wrapper = (ROOT / "InteropDLL" / "EmuApiWrapperMep.cpp").read_text()
    exports = re.findall(r"DllExport\s+[\w:*&<>\s]+?__stdcall\s+(\w+)", wrapper)
    candidates = sorted(e for e in exports if any(k in e for k in ("Reload", "Swap", "Ppu", "Pack")))
    unknown = [c for c in candidates
               if c not in KNOWN_SWAP_ENTRY_POINTS and c not in CANDIDATES_THAT_SWAP_NOTHING]
    return verbs, candidates, unknown


def ppu_swap_alternative_row():
    """ADR-0244 Decision 1 names an alternative to the reload - the in-place
    PPU swap `StartRecordingHdPack` already does - and has to measure both
    before the reload is kept. The protocol can only be run against a swap that
    exists, so this row reports the case as NOT MEASURED with the entry points
    it found; should a second one appear, the row names it instead of the case
    passing quietly. A row here is never a verdict on the swap."""
    verbs, candidates, unknown = pack_change_entry_points()
    swap_verbs = [v for v in verbs if v in KNOWN_SWAP_ENTRY_POINTS]
    swap_exports = [e for e in candidates if e in KNOWN_SWAP_ENTRY_POINTS]
    if unknown or not swap_verbs or not swap_exports:
        reason = ("the tree's pack-change entry points are not the ones this harness "
                  "knows, so the alternative may already exist: known "
                  f"{sorted(KNOWN_SWAP_ENTRY_POINTS)}, found verbs {verbs} and candidate "
                  f"exports {candidates}, outside both sets {unknown}")
    else:
        reason = ("nothing in this tree implements the alternative, so the protocol "
                  "has nothing to run against: the only pack-change entry points are the "
                  f"session's `{swap_verbs[0]}` verb and the core export {swap_exports[0]} "
                  "(Emulator::ReloadRomKeepingState - save state, reload, restore); the "
                  "other exports the scan finds name a pack without swapping one (" +
                  ", ".join(c for c in candidates if c in CANDIDATES_THAT_SWAP_NOTHING) + ")")
    return {"case": PPU_SWAP_CASE, "measured": False, "entry_points": verbs + candidates,
            "reason": reason}


# ---- one transition -----------------------------------------------------------

def grab(emu, read_ram):
    """The parked frame: `(frame number, checksum, 2 KB of NES RAM or b"")`."""
    number, width, height, checksum = emu.capture()
    ram = emu.read_ram((0x0000, 0x07FF))[0] if read_ram else b""
    return number, f"{width}x{height}:{checksum}", ram


def play_until(emu, last, read_ram, seen, budget):
    """run(1) and grab until the captured frame number reaches `last`, or for
    at most `budget` frames: a swap that lost its place must not be able to
    play its way from power-on to the reference's frame numbers (a game's
    attract mode replays the same frames from power-on, so the pictures alone
    would then match)."""
    number = max(seen) if seen else -1
    for _ in range(budget):
        if number >= last:
            break
        emu.run(1)
        number, picture, ram = grab(emu, read_ram)
        seen[number] = (picture, ram)
    return number


def run_transition(rom, work, packs, s0, transition, n, m, profile):
    """Three sessions. `prep` plays N frames from S0 and writes S1. `ref`, with
    the target switches, loads S1 the ordinary way and plays M frames. `swap`,
    with the source switches, plays the same N frames (its state must be S1 byte
    for byte), sets the target switches, swaps and plays on to the last frame the
    reference reached. Frames are lined up by the emulator's own frame number,
    not by index: the swap runs one frame of the restored state itself, and
    whether a `run` from a loaded state covers one frame or two depends on where
    the console counts its frames (the NES before the input poll, the GB and the
    SMS after it), so an index-wise comparison would be off by one there."""
    name, source, switches, target, expected, expected_outcome = transition
    #No route: idle, the game's attract mode plays itself.
    route = expand_script(profile["route"].read_text()) if profile["route"] else ["-"] * (n + m + 2)
    #The next frame after S1 is route frame n + 1: run(n) from a loaded state
    #covers n + 1 frames on the NES (step_emu.run_exact). Two spare frames cover
    #the consoles whose run covers one fewer.
    tail = compress_script(route[n + 1:n + 1 + m + 2])
    out = {"name": name, "expected_pass": expected, "expected_outcome": expected_outcome}
    s1 = work / "s1.mss"
    with session(rom, work / "prep", packs, source) as emu:
        emu.load_file(s0)
        emu.load_script(compress_script(route))
        emu.run(n)
        emu.save_file(emu.save(), s1)
    ref = {}
    if target is not None:
        with session(rom, work / "ref", packs, target) as emu:
            emu.load_file(s1)
            emu.load_script(tail)
            out["hd_reference"] = emu.hd_active()
            for _ in range(m):
                emu.run(1)
                number, picture, ram = grab(emu, profile["read_ram"])
                ref[number] = (picture, ram)
            emu.save_file(emu.save(), work / "ref-final.mss")
            emu.write_log(work / "ref.log")
    swap = {}
    with session(rom, work / "swap", packs, source) as emu:
        emu.load_file(s0)
        emu.load_script(compress_script(route))
        emu.run(n)
        out["hd_before"] = emu.hd_active()
        emu.save_file(emu.save(), work / "swap-s1.mss")
        out["s1_identical"] = (work / "swap-s1.mss").read_bytes() == s1.read_bytes()
        emu.load_script(tail)
        for switch, value in switches:
            emu.mep(switch, value)
        outcome, frame, ms = emu.swap()
        restored = outcome == "restored"
        out.update(outcome=outcome, restored=restored, swap_frame=frame, swap_ms=ms,
                   hd_after=emu.hd_active())
        if target is not None:
            number, picture, ram = grab(emu, profile["read_ram"])
            swap[number] = (picture, ram)
            out["swap_last"] = play_until(emu, max(ref), profile["read_ram"], swap, m + 2)
            emu.save_file(emu.save(), work / "swap-final.mss")
        emu.write_log(work / "swap.log")
    if target is None:
        #A restart is the expected result: there is no place to compare.
        out.update(frames=0, frame_mismatches=0, ram_mismatches=0, differing_fields=[],
                   differing_groups=[])
        out["pass"] = outcome == expected_outcome and out["s1_identical"]
        return out
    numbers = sorted(ref)
    frame_mismatch = [k for k in numbers if swap.get(k, (None,))[0] != ref[k][0]]
    ram_mismatch = [k for k in numbers if k in swap and swap[k][1] != ref[k][1]]
    differing = diff_fields(state_fields(work / "swap-final.mss"), state_fields(work / "ref-final.mss"))
    groups = sorted({field_group(k, profile) for k in differing})
    out.update(
        frames=len(numbers), frame_numbers=[numbers[0], numbers[-1]],
        swap_frame_numbers=[min(swap), max(swap)],
        frame_mismatches=len(frame_mismatch), first_frame_mismatch=frame_mismatch[:1],
        ram_mismatches=len(ram_mismatch), differing_fields=differing, differing_groups=groups,
        first_picture=ref[numbers[0]][0],
        swap_captures=[f"{k}:{v[0]}" for k, v in sorted(swap.items())],
        reference_captures=[f"{k}:{ref[k][0]}" for k in numbers])
    out["pass"] = bool(restored and out["s1_identical"] and out["swap_last"] == numbers[-1]
                       and not frame_mismatch and not ram_mismatch
                       and not {"cpu", "video", "ram"} & set(groups))
    return out


def prepare_rom(profile, rom_arg, folder):
    """The ROM the run uses, always a copy inside `folder`. A copy, never the
    library file: a pack beside the ROM (its sibling folder, MEP-v1 sec. 2.1)
    outranks every installed pack, so in the library it would be the one
    rendering and the swaps under test would change nothing on screen. Returns
    None when the ROM is not on this machine."""
    folder.mkdir(parents=True, exist_ok=True)
    if rom_arg is None and profile["rom"] is None:
        subprocess.run([sys.executable, str(HERE / "gen_hdpack_test_roms.py"), str(folder)],
                       check=True, capture_output=True)
        return folder / profile["synthetic"]
    source = rom_arg or profile["rom"]
    if not source.exists():
        return None
    rom = folder / source.name
    shutil.copyfile(source, rom)
    return rom


def print_not_measured(rows):
    print(f"{'case':34} verdict")
    for row in rows:
        print(f"{row['case']:34} NOT MEASURED  {row['reason']}")


def print_table(console, rom, n, m, work, results):
    print(f"{console}: rom {rom.name}, N={n}, M={m}, work {work}")
    print(f"{'transition':34} {'verdict':7} {'expect':6} {'outcome':15} {'swap ms':>8} "
          f"{'hd s->t':7} {'frames':>8} {'ram':>7} differing state fields")
    for r in results:
        hd = f"{int(r['hd_before'])}->{int(r['hd_after'])}"
        ram = f"{r['frames'] - r['ram_mismatches']}/{r['frames']}" if r["ram_checked"] else "n/a"
        print(f"{r['name']:34} {'PASS' if r['pass'] else 'FAIL':7} "
              f"{'PASS' if r['expected_pass'] else 'FAIL':6} "
              f"{r['outcome']:15} {r['swap_ms']:8.1f} {hd:7} "
              f"{r['frames'] - r['frame_mismatches']:>4}/{r['frames']:<3} {ram:>7} "
              f"{', '.join(r['differing_fields']) or '-'}")


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--console", choices=sorted(PROFILES), default="nes")
    ap.add_argument("--rom", type=Path, help="default: the console profile's ROM")
    ap.add_argument("--work", type=Path)
    ap.add_argument("--n", type=int, default=240, help="frames played before the swap")
    ap.add_argument("--m", type=int, default=120, help="frames compared after it")
    ap.add_argument("--json", type=Path)
    a = ap.parse_args(argv[1:])
    profile = PROFILES[a.console]
    if not BINARY.exists():
        print(f"skip: {BINARY} is not built (run `make capture-tool`)")
        return 0
    work = a.work or Path(tempfile.mkdtemp(prefix=f"p9-exactness-{a.console}-"))
    work.mkdir(parents=True, exist_ok=True)
    rom = prepare_rom(profile, a.rom, work / "rom")
    if rom is None:
        print(f"skip: the {a.console} ROM is not at {a.rom or profile['rom']}")
        return 0
    s0 = work / "s0.mss"
    mint = [f"input={profile['mint']}"] if profile["mint"] else []
    one_shot(rom, work / "mint" / "mint", profile["mint_seconds"], "mep-off", "hdpack-off",
             *mint, f"save-state={s0}")
    packs = build_packs(rom, work, s0, profile)
    results = []
    for index, transition in enumerate(transitions(profile)):
        result = run_transition(rom, work / f"t{index}", packs, s0, transition, a.n, a.m, profile)
        result["ram_checked"] = profile["read_ram"]
        results.append(result)
    not_measured = [ppu_swap_alternative_row()]
    bootstrap_measured = False
    if profile["bootstrap"]:
        bs_rom, bs_packs, bs_s0, why = bootstrap_fixture(profile, work)
        if why:
            not_measured.append({"case": BOOTSTRAP_CASE, "measured": False, "reason": why})
        else:
            # The probe runs first and alone: a case whose own negative control
            # cannot tell the two states apart has nothing to report but that.
            bs_transitions = bootstrap_transitions(profile)
            probe = run_transition(bs_rom, work / "bootstrap" / "t0", bs_packs, bs_s0,
                                   bs_transitions[0], a.n, a.m, profile)
            if probe["pass"]:
                not_measured.append({
                    "case": BOOTSTRAP_CASE, "measured": False,
                    "reason": ("the case's own negative control did not fail: with the "
                               "bootstrap on, every session writes its own pack beside the "
                               "ROM, so both sides of the comparison are dressed by a pack "
                               "recorded over the window being compared and the Textures "
                               "switch does not separate them - the three transitions "
                               "behind the probe would compare nothing"),
                })
            else:
                bootstrap_measured = True
                probe["ram_checked"] = profile["read_ram"]
                results.append(probe)
                for index, transition in enumerate(bs_transitions[1:], start=1):
                    result = run_transition(bs_rom, work / "bootstrap" / f"t{index}", bs_packs,
                                            bs_s0, transition, a.n, a.m, profile)
                    result["ram_checked"] = profile["read_ram"]
                    results.append(result)
    print_table(a.console, rom, a.n, a.m, work, results)
    print_not_measured(not_measured)
    if a.json:
        a.json.write_text(json.dumps({"console": a.console, "rom": rom.name, "n": a.n, "m": a.m,
                                      "bootstrap_measured": bootstrap_measured,
                                      "results": results, "not_measured": not_measured}, indent=2))
    return 0 if all(r["pass"] == r["expected_pass"] for r in results) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
