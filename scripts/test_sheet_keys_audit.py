#!/usr/bin/env python3
"""sheet_keys_audit.py on synthetic packs: CHR ROM keys by index, CHR RAM keys
by the unflipped `source`, condition-prefixed <tile> records, alias tiles, and
the #183 shape (a cell under the boot palette FF013403 that hires.txt never
carries)."""
import json
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sheet_keys_audit  # noqa: E402

ZERO = "0" * 32
DATA = "E7FFE7FFE7FFE7FF7E007E007E007E00"
FLIPPED = "E7FFE7FFE7FFE7FF7E007E007E007E01"


def write_pack(root, hires, sheets):
    textures = os.path.join(root, "textures")
    os.makedirs(os.path.join(textures, "sheets"))
    with open(os.path.join(textures, "hires.txt"), "w") as fh:
        fh.write("<ver>106\n" + "".join(hires))
    for name, cells in sheets.items():
        with open(os.path.join(textures, "sheets", name), "w") as fh:
            json.dump({"cells": cells}, fh)
    return root


def check(cond, msg):
    if not cond:
        print("FAIL:", msg)
        sys.exit(1)


with tempfile.TemporaryDirectory() as tmp:
    # CHR ROM: index 0x100 exists under two palettes; the boot palette is absent.
    # A key carried only by a condition-prefixed record ([cond]<tile>) counts;
    # an alias's tiles are audited like the cell's own (ADR-0153 §3).
    rom = write_pack(os.path.join(tmp, "rom"), [
        "<tile>1,100,FF0F3015,0,0,1,N\n",
        "[spriteNearby0]<tile>1,100,FF0F3919,32,0,1,N\n",
        "<tile>0,00,0F001030,0,0,1,Y\n",
        "<tile>1,101,FF0F3015,64,0,1,N\n",
    ], {
        "sprites.json": [
            {"index": 0, "tiles": [{"tile": DATA, "palette": "FF0F3015", "index": 256}],
             "aliases": [{"metatile": 7, "tiles": [{"tile": DATA, "palette": "FF0F3015", "index": 257}]},
                         {"metatile": 8, "tiles": [{"tile": DATA, "palette": "FF0F3015", "index": 258}]}]},
            {"index": 1, "tiles": [{"tile": DATA, "palette": "FF013403", "index": 256}]},
        ],
        "spr000.json": [
            {"index": 0, "tiles": [{"tile": DATA, "palette": "ff0f3919", "index": 256}, None]},
        ],
    })
    entries, left = sheet_keys_audit.audit(rom)
    check(entries == 5, f"CHR ROM entries {entries}")
    check(left == [("sprites.json", "0 alias 8", "102", "FF0F3015", False),
                   ("sprites.json", "1", "100", "FF013403", False)], f"CHR ROM leftovers {left}")

    # CHR RAM: the flipped `tile` is not a key, `source` is (ADR-0178); a
    # blank cell under the boot palette is the #183 residue.
    ram = write_pack(os.path.join(tmp, "ram"), [
        f"<tile>0,{DATA},FF36160F,0,0,1,N,0,0\n",
    ], {
        "sprites.json": [
            {"index": 0, "tiles": [{"tile": FLIPPED, "source": DATA, "mirror": 1, "palette": "FF36160F"}]},
            {"index": 2, "tiles": [{"tile": ZERO, "palette": "FF013403"}]},
        ],
    })
    entries, left = sheet_keys_audit.audit(ram)
    check(entries == 2, f"CHR RAM entries {entries}")
    check(left == [("sprites.json", "2", ZERO, "FF013403", True)], f"CHR RAM leftovers {left}")

    # A clean pack exits 0, a pack with a leftover exits 1.
    check(sheet_keys_audit.main([rom]) == 1, "exit code with leftovers")
    clean = write_pack(os.path.join(tmp, "clean"), [f"<tile>0,{DATA},FF36160F,0,0,1,N,0,0\n"], {
        "sprites.json": [{"index": 0, "tiles": [{"tile": DATA, "palette": "FF36160F"}]}],
    })
    check(sheet_keys_audit.main([clean]) == 0, "exit code when clean")

print("PASS: test_sheet_keys_audit")
