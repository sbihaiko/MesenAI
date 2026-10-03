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
images inverted, pack B the same recording with its colour channels rotated (so
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
slice recorded. Bootstrap is off in every run: it writes beside the ROM, which
is the user's library, so a swap's effect on the bootstrap is not measured here.
"""
import argparse
import json
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
PROFILES = {
    "nes": dict(rom=LIBRARY / "G3 - Nitendinho" / "roms" / "Ninja Gaiden (1989) (Tecmo).nes",
                mint=STAGES / "ninjagaiden" / "mint-stage1.txt", mint_seconds=18,
                route=STAGES / "ninjagaiden" / "stage1-run.txt", record_seconds=12,
                video="ppu.", ram="memoryManager.internalRam", read_ram=True, audio=True, extra=True),
    "sms": dict(rom=LIBRARY / "G3 - MasterSystem" / "roms" / "Sonic the Hedgehog (1991) (Sega).sms",
                mint=None, mint_seconds=40, route=None, record_seconds=12,
                video="vdp.", ram="memoryManager.workRam", read_ram=False, audio=False, extra=False),
    "gb": dict(rom=None, synthetic="test_dmg.gb",
               mint=None, mint_seconds=5, route=None, record_seconds=12,
               video="ppu.", ram="memoryManager.workRam", read_ram=False, audio=False, extra=False),
    "gbc": dict(rom=None, synthetic="test_cgb.gbc",
                mint=None, mint_seconds=5, route=None, record_seconds=12,
                video="ppu.", ram="memoryManager.workRam", read_ram=False, audio=False, extra=False),
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


def prepare_rom(profile, rom_arg, work):
    """The ROM the run uses, always a copy inside `work`. A copy, never the
    library file: a pack beside the ROM (its sibling folder, MEP-v1 sec. 2.1)
    outranks every installed pack, so in the library it would be the one
    rendering and the swaps under test would change nothing on screen. Returns
    None when the ROM is not on this machine."""
    folder = work / "rom"
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
    rom = prepare_rom(profile, a.rom, work)
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
    print_table(a.console, rom, a.n, a.m, work, results)
    if a.json:
        a.json.write_text(json.dumps({"console": a.console, "rom": rom.name, "n": a.n, "m": a.m,
                                      "results": results}, indent=2))
    return 0 if all(r["pass"] == r["expected_pass"] for r in results) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
