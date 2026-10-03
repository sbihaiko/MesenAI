"""MepPackManager / HD pack loader state, driven through the real MesenCore.

Three bugs whose effect only shows on the whole load path - none of the code
involved links into scripts/core_unit_tests (it needs the Emulator):

- #694: Emulator::InternalLoadRom resolved the packs of the new ROM (and
  applied its patches) before TryLoadRom, and a failed load restored neither.
  With game A running, opening a ROM that does not load left the manager
  describing that ROM - its sha1, its sibling folder, its packs - and Clear()
  had already ended A's recording, so Record wrote A's tiles into the other
  ROM's project. The case loads a synthetic NROM, starts a recording, opens a
  file that is not a ROM, and expects every reported value unchanged and the
  recording still in progress.
- #695: a Remaster project in the EnhancementPacks/<Game>/ fallback folder
  (the ROM folder is read-only) keeps its human layer under mep/, but the
  fallback folder was probed with an empty human prefix, so mep/textures/ was
  never seen. The case expects the folder listed with its textures section.
- #705: <bgm>/<sfx> file names were checked case-insensitively but stored as
  declared, so a case-sensitive file system (or a zipped pack) found nothing
  at play time. The HD pack builder writes the stored names back to
  hires.txt, which makes them observable here: the case declares
  "Track.ogg"/"Boom.ogg" over files named "track.ogg"/"boom.ogg" and expects
  the on-disk spelling written back. It needs a case-sensitive folder (on a
  case-insensitive one the declared spelling opens as it is and there is no
  bug to see): the temporary folder when it is one, else on macOS a throwaway
  case-sensitive APFS image (hdiutil); with neither, the case says so and is
  skipped.

Each case runs in a child process (InitDll + InitializeEmu with a temporary
home, no window), like scripts/test_core_stop_race.py. The library is a build
product (`make core`); when absent the file says so and exits 0.
MESEN_CORE_LIB=<path> picks a specific build.
"""

import contextlib
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
from gen_synthetic_nrom import build_rom  # noqa: E402

NAMES = ("MesenCore.dylib", "MesenCore.so")
CHILD_TIMEOUT = 120
ROM_NAME = "Synthetic Game"

CHILD = r"""
import ctypes, os, sys
lib = ctypes.CDLL(sys.argv[1])
home, rom, mode = sys.argv[2], sys.argv[3].encode(), sys.argv[4]
lib.InitDll()
lib.InitializeEmu(home.encode(), None, None, True, True, True, True)
lib.LoadRom.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
lib.LoadRom.restype = ctypes.c_bool
lib.IsMepBootstrapping.restype = ctypes.c_bool
lib.StartMepRecording.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
lib.StartMepRecording.restype = ctypes.c_bool
lib.StopMepRecording.restype = ctypes.c_bool

def text(name):
    buffer = ctypes.create_string_buffer(1 << 16)
    getattr(lib, name)(buffer, len(buffer))
    return buffer.value.decode()

def snapshot():
    return {name: text(name) for name in ("GetMepRomSha1", "GetMepRomFileSha1", "GetMepSiblingFolder", "GetMepPackList")}

def finish(lines):
    lib.Stop()
    lib.Release()
    print("\n".join(lines), flush=True)
    os._exit(0)

if not lib.LoadRom(rom, b""):
    finish(["error: the synthetic ROM did not load"])
lib.Pause()
out = []
if mode == "failed-load":
    before = snapshot()
    out.append("recording-started=%d" % lib.StartMepRecording(b"script", b""))
    out.append("broken-load=%d" % lib.LoadRom(sys.argv[5].encode(), b""))
    after = snapshot()
    for name in before:
        out.append("%s-kept=%d" % (name, before[name] == after[name]))
    out.append("sha1-not-empty=%d" % (before["GetMepRomSha1"] != ""))
    out.append("packs-not-empty=%d" % (before["GetMepPackList"] != ""))
    out.append("still-recording=%d" % lib.IsMepBootstrapping())
    out.append("stop-recording=%d" % lib.StopMepRecording())
elif mode == "pack-list":
    out.append(text("GetMepPackList"))
elif mode == "hd-builder":
    class Options(ctypes.Structure):
        _fields_ = [("SaveFolder", ctypes.c_char_p), ("FilterType", ctypes.c_int), ("Scale", ctypes.c_uint32),
                    ("ChrRamBankSize", ctypes.c_uint32), ("UseLargeSprites", ctypes.c_bool),
                    ("SortByUsageFrequency", ctypes.c_bool), ("GroupBlankTiles", ctypes.c_bool),
                    ("IgnoreOverscan", ctypes.c_bool)]
    class Params(ctypes.Structure):
        _fields_ = [("Shortcut", ctypes.c_int), ("Param", ctypes.c_uint32), ("ParamPtr", ctypes.c_void_p)]
    start, stop, folder = int(sys.argv[5]), int(sys.argv[6]), sys.argv[7].encode()
    options = Options(folder, 0, 1, 0x1000, False, True, True, False)
    lib.ExecuteShortcut.argtypes = [Params]
    lib.ExecuteShortcut(Params(start, 0, ctypes.cast(ctypes.pointer(options), ctypes.c_void_p)))
    lib.ExecuteShortcut(Params(stop, 0, None))
    out.append("builder-done")
finish(out)
"""

_FAILURES = []
_CHECKS = []


def check(cond, name, detail=""):
    _CHECKS.append(name)
    print(("ok   " if cond else "FAIL ") + name + ("" if cond else f"\n     {detail}"))
    if not cond:
        _FAILURES.append(name)


def find_library():
    from_env = os.environ.get("MESEN_CORE_LIB", "")
    if from_env:
        return Path(from_env) if Path(from_env).is_file() else None
    interop = ROOT / "InteropDLL"
    if not interop.is_dir():
        return None
    for folder in sorted(interop.glob("obj.*")):
        for name in NAMES:
            if (folder / name).is_file():
                return folder / name
    return None


def shortcut_ordinal(name):
    """The EmulatorShortcut value of `name`, counted from SettingTypes.h."""
    source = (ROOT / "Core" / "Shared" / "SettingTypes.h").read_text(encoding="utf-8")
    body = re.search(r"enum class EmulatorShortcut\s*\{(.*?)\};", source, re.S).group(1)
    entries = []
    for line in body.splitlines():
        line = line.split("//")[0].strip().rstrip(",")
        if line:
            entries.append(line)
    return entries.index(name)


def run_child(library, home, rom, mode, *extra):
    with tempfile.TemporaryDirectory() as cwd:
        try:
            proc = subprocess.run(
                [sys.executable, "-c", CHILD, str(library), str(home), str(rom), mode, *map(str, extra)],
                cwd=cwd, capture_output=True, text=True, timeout=CHILD_TIMEOUT,
            )
        except subprocess.TimeoutExpired:
            return None, f"child did not finish within {CHILD_TIMEOUT} s"
    return proc.returncode, proc.stdout.strip() + ("\n" + proc.stderr.strip() if proc.returncode else "")


def make_case(folder):
    home = folder / "home"
    roms = folder / "roms"
    home.mkdir()
    roms.mkdir()
    rom = roms / f"{ROM_NAME}.nes"
    rom.write_bytes(build_rom())
    return home, rom


def test_failed_load_keeps_the_running_game(library):
    with tempfile.TemporaryDirectory() as tmp:
        home, rom = make_case(Path(tmp))
        sibling = rom.parent / ROM_NAME / "textures"
        sibling.mkdir(parents=True)
        (sibling / "hires.txt").write_text("<ver>106\n<scale>1\n")
        broken = rom.parent / "Broken.nes"
        broken.write_bytes(b"\x00" * 100)
        code, out = run_child(library, home, rom, "failed-load", broken)
    lines = dict(line.split("=", 1) for line in out.splitlines() if "=" in line)
    check(code == 0 and lines.get("recording-started") == "1" and lines.get("broken-load") == "0",
          "#694 setup: A loads and records, the broken file does not load", f"exit {code}: {out}")
    out = " ".join(out.splitlines())
    for name in ("GetMepRomSha1", "GetMepRomFileSha1", "GetMepSiblingFolder", "GetMepPackList"):
        check(lines.get(f"{name}-kept") == "1", f"#694: {name} still describes the running game after a failed load", out)
    check(lines.get("sha1-not-empty") == "1" and lines.get("packs-not-empty") == "1",
          "#694: the running game has a sha1 and a pack to keep", out)
    check(lines.get("still-recording") == "1", "#694: a failed load does not end the running game's recording", out)
    check(lines.get("stop-recording") == "1", "#694: Stop still finds the recording in progress", out)


def test_fallback_folder_plays_its_mep_layer(library):
    with tempfile.TemporaryDirectory() as tmp:
        home, rom = make_case(Path(tmp))
        textures = home / "EnhancementPacks" / ROM_NAME / "mep" / "textures"
        textures.mkdir(parents=True)
        (textures / "hires.txt").write_text("<ver>106\n<scale>1\n")
        code, out = run_child(library, home, rom, "pack-list")
    rows = [line.split("\t") for line in out.splitlines() if line.startswith(ROM_NAME + "\t")]
    check(code == 0 and len(rows) == 1 and "textures" in rows[0][5].split(","),
          "#695: EnhancementPacks/<Game>/mep/textures is listed as the project's textures",
          f"exit {code}: {out!r}")


def is_case_sensitive(folder):
    probe = Path(folder) / "CaseProbe"
    probe.write_bytes(b"")
    try:
        return not (Path(folder) / "caseprobe").exists()
    finally:
        probe.unlink()


@contextlib.contextmanager
def case_sensitive_folder():
    """A temporary case-sensitive folder, or None when none can be made."""
    with tempfile.TemporaryDirectory() as tmp:
        if is_case_sensitive(tmp):
            yield Path(tmp)
            return
        if sys.platform != "darwin" or shutil.which("hdiutil") is None:
            yield None
            return
        image, mount = Path(tmp) / "case", Path(tmp) / "mnt"
        mount.mkdir()
        made = subprocess.run(["hdiutil", "create", "-size", "20m", "-type", "SPARSE", "-fs", "Case-sensitive APFS",
                               "-volname", "mesentest", str(image)], capture_output=True).returncode == 0
        attached = made and subprocess.run(["hdiutil", "attach", "-nobrowse", "-noverify", "-mountpoint", str(mount),
                                            str(image) + ".sparseimage"], capture_output=True).returncode == 0
        try:
            yield mount if attached else None
        finally:
            if attached:
                subprocess.run(["hdiutil", "detach", "-force", str(mount)], capture_output=True)


def test_builder_writes_the_resolved_audio_names(library):
    with case_sensitive_folder() as folder:
        if folder is None:
            print("SKIP #705: no case-sensitive folder available (needs one to tell the spellings apart)")
            return
        home, rom = make_case(folder)
        pack = folder / "pack"
        pack.mkdir()
        (pack / "hires.txt").write_text("<ver>106\n<scale>1\n<bgm>0,0,Track.ogg\n<sfx>0,1,Boom.ogg\n")
        (pack / "track.ogg").write_bytes(b"OggS")
        (pack / "boom.ogg").write_bytes(b"OggS")
        code, out = run_child(library, home, rom, "hd-builder", shortcut_ordinal("StartRecordHdPack"),
                              shortcut_ordinal("StopRecordHdPack"), pack)
        written = (pack / "hires.txt").read_text()
    check(code == 0 and out.endswith("builder-done"), "#705 setup: the HD pack builder ran", f"exit {code}: {out}")
    check("<bgm>0,0,track.ogg" in written, "#705: <bgm> stores the file name found on disk", written)
    check("<sfx>0,1,boom.ogg" in written, "#705: <sfx> stores the file name found on disk", written)


def main():
    library = find_library()
    if library is None:
        print("SKIP: MesenCore is not built (make core) and MESEN_CORE_LIB names no file")
        return 0
    test_failed_load_keeps_the_running_game(library)
    test_fallback_folder_plays_its_mep_layer(library)
    test_builder_writes_the_resolved_audio_names(library)
    print(f"\n{len(_CHECKS) - len(_FAILURES)}/{len(_CHECKS)} checks passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
