#!/usr/bin/env python3
"""P.1-local (ADR-0206) end-to-end check against the built MesenCore library.

The unit tests in scripts/core_unit_tests.cpp cover the fingerprint, the cache
round-trip, the refresh and the pack_id adoption against a temporary tree. What
they cannot cover is the exported symbol and the real packs folder, so this
script drives the shipped library through ctypes:

  1. builds a throwaway home folder with three local containers that hold the
     same pack tree - a catalog-style install (with `.mep-install.json`), a
     hand-dropped copy (no stamp) and a zip whose pack root sits inside its
     extraction (ADR-0120 fallback, resolved through `.mep-source`);
  2. runs RefreshMepLocalIdentities (ADR-0206 section 3) and reads
     EnhancementPacks/.cache/content-ids.json back;
  3. asserts all three share one content_id - the PRD Part B section 5 merge key
     - that a warm pass recomputes nothing, and that a nested edit invalidates
     exactly one container and breaks its match with the stamped twin.

The user's own EnhancementPacks folder is never touched: the script points the
Core at a scratch home folder and removes it afterwards.

Usage: python3 scripts/p1_local_identity_check.py [path/to/MesenCore.dylib]
"""
import ctypes
import json
import os
import shutil
import sys
import tempfile

DLL = sys.argv[1] if len(sys.argv) > 1 else "bin/osx-arm64/Release/MesenCore.dylib"
home = tempfile.mkdtemp(prefix="p1local-home-")
packs = os.path.join(home, "EnhancementPacks")

failures = []


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as file:
        file.write(text)


def check(condition, label, detail=""):
    print(("PASS  " if condition else "FAIL  ") + label + ((" - " + detail) if detail and not condition else ""))
    if not condition:
        failures.append(label)


def read_cache():
    with open(os.path.join(packs, ".cache", "content-ids.json")) as file:
        return {os.path.basename(entry["path"]): entry for entry in json.load(file)["containers"]}


# A catalog install (stamp and all), a hand-dropped copy of the same tree, and
# the same tree inside a zip whose pack root is a subfolder of the extraction.
write(os.path.join(packs, "installed", "textures", "hires.txt"), "<ver>106\n<img>a.png\n")
write(os.path.join(packs, "installed", ".mep-install.json"),
      json.dumps({"pack_id": "tastic/contra80s:contra", "content_id": "placeholder"}))
write(os.path.join(packs, "drop", "textures", "hires.txt"), "<ver>106\n<img>a.png\n")
write(os.path.join(packs, "zipped.zip"), "the zip bytes themselves are not read here\n")
write(os.path.join(packs, ".cache", "zipped", ".mep-source"), "123:456\nHdPacks/zipped pack\n")
write(os.path.join(packs, ".cache", "zipped", "HdPacks", "zipped pack", "textures", "hires.txt"), "<ver>106\n<img>a.png\n")

lib = ctypes.CDLL(os.path.abspath(DLL))
lib.InitializeEmu.argtypes = [ctypes.c_char_p] + [ctypes.c_void_p] * 2 + [ctypes.c_bool] * 4
lib.InitializeEmu.restype = None
lib.RefreshMepLocalIdentities.argtypes = []
lib.RefreshMepLocalIdentities.restype = ctypes.c_int32

lib.InitializeEmu(home.encode(), None, None, True, True, True, True)
check(lib.RefreshMepLocalIdentities() == 3, "the cold refresh hashes every local container once")

ids = read_cache()
check(len(ids) == 3, "the cache carries one entry per container", str(sorted(ids)))
sameTree = {name: entry["content_id"] for name, entry in ids.items()}
check(len(set(sameTree.values())) == 1,
      "a catalog install, a stamp-less drop and a zip resolve to one content_id",
      json.dumps({name: value[:12] for name, value in sameTree.items()}))

check(lib.RefreshMepLocalIdentities() == 0, "a warm cache re-hashes nothing")

# The edit changes the byte count on purpose: a same-size rewrite is only
# visible through the recorded time (ADR-0206 section 2), which this check
# cannot guarantee on every filesystem.
write(os.path.join(packs, "drop", "textures", "hires.txt"), "<ver>106\n<img>b.png\n<img>c.png\n")
check(lib.RefreshMepLocalIdentities() == 1, "a nested edit re-hashes exactly one container")
after = read_cache()
check(after["drop"]["content_id"] != after["installed"]["content_id"],
      "an edited drop stops sharing the catalog pack's identity")

shutil.rmtree(home, ignore_errors=True)
check(not os.path.exists(home), "the scratch home folder is gone")

print(f"\n{len(failures)} failure(s)")
sys.exit(1 if failures else 0)
