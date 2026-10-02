#!/usr/bin/env python3
"""P.13 (ADR-0246 section 3) check of the core signal behind Settings > Look.

Settings > Look disables Pixels with "Off while a pack draws the art" when the
core's IsDrawingPackArt() export says the console filter is the HD filter.
UI.Tests pins the rule host-free; what it cannot reach is the export and the
three console implementations, so this drives the built library through
ctypes on synthetic ROMs (no third-party bytes) and minimal packs:

  - NES (NROM), Game Boy (DMG) and SMS, each loaded twice:
      * with a loose HdPacks/<rom>/hires.txt that has video content
        -> IsDrawingPackArt() is true;
      * negative control: the same ROM with the pack folder under another
        name, so nothing loads -> false;
  - with no game loaded -> false;
  - Hold to Compare's two exports (SetLookCompare, RedrawPausedFrame) run on
    a paused game, with and without a pack, without failing.

The user's own home folder is never touched: the core is pointed at a scratch
home folder that is removed afterwards.

Usage: python3 scripts/check_look_pack_art.py [path/to/MesenCore.dylib]
"""
import ctypes
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import time
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
DLL = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "bin", "osx-arm64", "Release", "MesenCore.dylib")

failures = []


def check(condition, label, detail=""):
    print(("PASS  " if condition else "FAIL  ") + label + ((" - " + detail) if detail and not condition else ""))
    if not condition:
        failures.append(label)


def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as file:
        file.write(data if isinstance(data, bytes) else data.encode())


def png_8x8():
    """A plain 8x8 RGBA PNG, the smallest sheet a GB/SMS <tile> can slice."""
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)
    raw = b"".join(b"\x00" + b"\xff\x00\x00\xff" * 8 for _ in range(8))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 8, 8, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


def nrom():
    """iNES NROM-128: the PRG is one infinite loop at $C000, CHR is blank."""
    prg = bytearray(0x4000)
    prg[0:3] = b"\x4c\x00\xc0"  # JMP $C000
    prg[0x3FFA:0x4000] = b"\x00\xc0" * 3  # NMI / RESET / IRQ -> $C000
    return b"NES\x1a\x01\x01\x00\x00" + b"\x00" * 8 + bytes(prg) + bytes(0x2000)


def tile_pack(system):
    return ("<ver>200\n<system>" + system + "\n<scale>1\n<img>t.png\n"
            "<tile>0," + "00" * 16 + ",00,0,0,1,N\n")


# The NES loader counts an <overscan> block as video content (HdData::HasVideoContent).
NES_PACK = "<ver>106\n<scale>1\n<overscan>8,0,8,0\n"

home = tempfile.mkdtemp(prefix="p13-look-home-")
roms = os.path.join(home, "roms")
os.makedirs(roms)
subprocess.run([sys.executable, os.path.join(HERE, "gen_hdpack_test_roms.py"), roms], check=True, stdout=subprocess.DEVNULL)
write(os.path.join(roms, "test_nes.nes"), nrom())

cases = [
    ("NES", "test_nes.nes", {"hires.txt": NES_PACK}),
    ("Game Boy", "test_dmg.gb", {"hires.txt": tile_pack("gb"), "t.png": png_8x8()}),
    ("SMS", "test_sms.sms", {"hires.txt": tile_pack("sms"), "t.png": png_8x8()}),
]

lib = ctypes.CDLL(os.path.abspath(DLL))
lib.InitDll.argtypes = []
lib.InitializeEmu.argtypes = [ctypes.c_char_p] + [ctypes.c_void_p] * 2 + [ctypes.c_bool] * 4
lib.LoadRom.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
lib.LoadRom.restype = ctypes.c_bool
lib.IsDrawingPackArt.argtypes = []
lib.IsDrawingPackArt.restype = ctypes.c_bool
lib.SetLookCompare.argtypes = [ctypes.c_bool]
lib.SetLookCompare.restype = None
lib.RedrawPausedFrame.argtypes = []
lib.RedrawPausedFrame.restype = None
for name in ("Pause", "Stop", "Release"):
    getattr(lib, name).argtypes = []
    getattr(lib, name).restype = None

lib.InitDll()
lib.InitializeEmu(home.encode(), None, None, True, True, True, True)
check(not lib.IsDrawingPackArt(), "no game loaded: no pack art")


def load_and_read(rom):
    lib.Pause()
    loaded = lib.LoadRom(os.path.join(roms, rom).encode(), b"")
    lib.Pause()
    # The answer is fixed at load; poll briefly in case a load-time builder
    # (F5 bootstrap) is still running when no pack loaded.
    value = lib.IsDrawingPackArt()
    for _ in range(10):
        if value:
            break
        time.sleep(0.05)
        value = lib.IsDrawingPackArt()
    lib.SetLookCompare(True)
    lib.RedrawPausedFrame()
    lib.SetLookCompare(False)
    # Emulator::Run clears _stopFlag when the new emulation thread starts, so a
    # Stop issued before that thread is up is lost and Stop joins forever. Let
    # the thread start first.
    time.sleep(0.5)
    lib.Stop()
    return loaded, value


for console, rom, files in cases:
    stem = os.path.splitext(rom)[0]
    pack = os.path.join(home, "HdPacks", stem)
    for name, data in files.items():
        write(os.path.join(pack, name), data)
    loaded, value = load_and_read(rom)
    check(loaded, console + ": the synthetic ROM loads")
    check(value, console + ": a pack with video content -> IsDrawingPackArt() is true")

    os.rename(pack, pack + "-elsewhere")
    loaded, value = load_and_read(rom)
    check(loaded, console + " (control): the ROM loads without its pack")
    check(not value, console + " (control): no pack -> IsDrawingPackArt() is false")

lib.Release()
shutil.rmtree(home, ignore_errors=True)
check(not os.path.exists(home), "the scratch home folder is gone")

print(f"\n{len(failures)} failure(s)")
sys.exit(1 if failures else 0)
