#!/usr/bin/env python3
"""Sheet cells whose (key, palette) the pack's own hires.txt never emits.

A sprite-sheet tile entry offers the artist a key the rebuilt pack can emit;
repainting a cell whose (key, palette) pair has no <tile> line is inert and
nothing in the pack says so (#181 for flipped keys, #183 for OAM captured
while sprites were disabled). This audit is the repro of both: for every
`sheets/spr*.json` / `sheets/sprites.json` tile entry it looks up the run-time
key - `index` on a CHR ROM game, `source` (the unflipped data, ADR-0178) or
`tile` on a CHR RAM game - together with the palette, in `textures/hires.txt`.

Usage: sheet_keys_audit.py <pack-dir>...   (a pack dir holds textures/)
Exit 1 when any pack has a leftover cell, so it can gate a recording batch.
"""
import glob
import json
import os
import sys


def hires_keys(path):
    """(key, palette) pairs of the <tile> lines; CHR ROM keys as upper hex
    without leading zeros, CHR RAM keys as the 32-hex data."""
    keys = set()
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if not line.startswith("<tile>"):
                continue
            parts = line[6:].strip().split(",")
            if len(parts) < 3:
                continue
            key = parts[1].upper()
            if len(key) < 32:
                key = format(int(key, 16), "X")
            keys.add((key, parts[2].upper()))
    return keys


def entry_key(tile):
    if "index" in tile:
        return format(int(tile["index"]), "X")
    return str(tile.get("source", tile["tile"])).upper()


def audit(pack):
    """Return (entries, leftovers); a leftover is
    (sheet file, cell index, key, palette, tile is all zero)."""
    textures = os.path.join(pack, "textures")
    keys = hires_keys(os.path.join(textures, "hires.txt"))
    sheets = sorted(glob.glob(os.path.join(textures, "sheets", "spr[0-9]*.json")))
    sheets += glob.glob(os.path.join(textures, "sheets", "sprites.json"))
    entries = 0
    leftovers = []
    for sheet in sheets:
        with open(sheet, encoding="utf-8") as fh:
            doc = json.load(fh)
        for cell in doc.get("cells", []):
            for tile in cell.get("tiles", []):
                entries += 1
                key = entry_key(tile)
                palette = str(tile["palette"]).upper()
                if (key, palette) not in keys:
                    blank = str(tile.get("tile", "")).strip("0") == ""
                    leftovers.append((os.path.basename(sheet), cell.get("index"), key, palette, blank))
    return entries, leftovers


def main(argv):
    if not argv:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    failed = False
    for pack in argv:
        entries, leftovers = audit(pack)
        print(f"{pack}: {entries} sprite tile entries, {len(leftovers)} leftover")
        for sheet, cell, key, palette, blank in leftovers:
            print(f"  {sheet} cell {cell}: {key} pal {palette}{' (blank)' if blank else ''}")
        failed |= bool(leftovers)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
