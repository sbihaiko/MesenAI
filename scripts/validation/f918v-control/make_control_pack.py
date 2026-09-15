"""Build a negative-control MEP pack that makes HdNesPack's rule choice visible.

    python3 make_control_pack.py <built-pack> <out-pack> [<kit-sheets-dir>]

Validator artifact, not the artist path: PRD gate 6.3 forbids `hires.txt`
diagnosis on the *user's* success path, so this edits a copy of the built
pack for measurement only. Background: the builder emits a gated line and
its bare twin pointing at the same PNG cell (F9.28 dual emission), so a
condition hit and a miss draw identical pixels and no screenshot sweep can
tell them apart. `HdNesPack::GetMatchingTile` returns the first entry in
file order whose conditions pass, so re-pointing each *line class* at an
unmistakable cell turns that decision into a colour:

  `[cond]<tile>` (gated)              -> Y(8) | CYAN(16)   | B(8) cell
  bare line with NO gated sibling
  (same tileData+palette)             -> Y(8) | ORANGE(16) | B(8) cell
  bare twin of a gated line           -> keeps its sheet cell; every
      sprite-kit sheet (usr*, spr*, sprites) is repainted per cell over
      its opaque pixels  Y(8) | MAGENTA(16) | B(8)
  keys the kit sidecars flag "mirror" -> one SOLID four-quadrant cell
      (TL red, TR green, BL blue, BR white), every line class alike, so
      the flip is read off the quadrant order with no transparency involved

At run time: cyan = the gated rule matched live; magenta = every gate for
that key missed and the bare twin rendered; orange = key outside both
checks; quadrant order = OAM flip of a mirror-flagged key. Y|mid|B bands
are painted over opaque pixels only, so sprite-over-background regions can
fake a reversed band - use the solid marker for orientation claims.

The third argument is the kit's sheet-sidecar folder (`kit/sheets/`, the
`usr*.json` written by artist_kit.py); its entries carrying "mirror" give
the unflipped `source` tile data that is the hires.txt key. Without it no
key is marker-painted. Read with mep_lint.py afterwards; the run that used
this is docs/validation/f918v-current-binary-painting-2026-09-15.md §4.
"""
import json
import re
import shutil
import sys
from pathlib import Path

from PIL import Image

Y, M, B = (255, 255, 0, 255), (255, 0, 255, 255), (0, 0, 255, 255)
CYAN, ORANGE = (0, 255, 255, 255), (255, 128, 0, 255)
Q = ((200, 0, 0, 255), (0, 200, 0, 255), (0, 0, 200, 255), (220, 220, 220, 255))


def band_marker(mid, path):
    im = Image.new("RGBA", (32, 32))
    px = im.load()
    for y in range(32):
        for x in range(32):
            px[x, y] = Y if x < 8 else (mid if x < 24 else B)
    im.save(path)


def quadrant_marker(path):
    im = Image.new("RGBA", (32, 32))
    px = im.load()
    for y in range(32):
        for x in range(32):
            px[x, y] = Q[(0 if x < 16 else 1) + (0 if y < 16 else 2)]
    im.save(path)


def mirror_sources(sheets_dir):
    """Unflipped tile data of every sidecar entry that carries a mirror flag."""
    found = set()

    def walk(o):
        if isinstance(o, dict):
            if o.get("mirror") and o.get("source"):
                found.add(o["source"].upper())
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)

    for f in sorted(Path(sheets_dir).glob("*.json")):
        walk(json.loads(f.read_text()))
    return found


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    src, dst = Path(argv[1]), Path(argv[2])
    mirror_keys = mirror_sources(argv[3]) if len(argv) > 3 else set()
    if dst.exists():
        shutil.rmtree(dst)
    shutil.copytree(src, dst)
    tex = dst / "textures"
    band_marker(CYAN, tex / "control-cyan.png")
    band_marker(ORANGE, tex / "control-orange.png")
    quadrant_marker(tex / "control-mirror.png")

    lines = (tex / "hires.txt").read_text().splitlines()
    # <tile>img,tileData,palette,x,y,brightness,default[,bank,index]
    tile_re = re.compile(r"^(\[[^\]]+\])?<tile>(\d+),([0-9A-Fa-f]+),([0-9A-Fa-f]+),(.*)$")
    gated_keys = {(m.group(3), m.group(4)) for m in map(tile_re.match, lines) if m and m.group(1)}

    out, n, inserted = [], {"gated": 0, "twin": 0, "bare_only": 0, "mirror_marked": 0}, False
    for ln in lines:
        if ln.startswith("<img>") and not inserted:
            # The loader checks the bitmap index against the images read so
            # far, so the markers go first and every existing index shifts +3.
            out += ["<img>control-cyan.png", "<img>control-orange.png", "<img>control-mirror.png"]
            inserted = True
        m = tile_re.match(ln)
        if not m:
            out.append(ln)
            continue
        cond, idx, data, pal, rest = m.groups()
        f = rest.split(",")
        if data.upper() in mirror_keys:
            f[0], f[1] = "0", "0"
            out.append(f"{cond or ''}<tile>2,{data},{pal},{','.join(f)}")
            n["mirror_marked"] += 1
        elif cond:
            f[0], f[1] = "0", "0"
            out.append(f"{cond}<tile>0,{data},{pal},{','.join(f)}")
            n["gated"] += 1
        elif (data, pal) in gated_keys:
            out.append(f"<tile>{int(idx) + 3},{data},{pal},{rest}")
            n["twin"] += 1
        else:
            f[0], f[1] = "0", "0"
            out.append(f"<tile>1,{data},{pal},{','.join(f)}")
            n["bare_only"] += 1
    (tex / "hires.txt").write_text("\n".join(out) + "\n")
    print(n, f"mirror keys from sidecars: {len(mirror_keys)}")

    sheets = sorted((tex / "sheets").glob("usr*.png")) + sorted((tex / "sheets").glob("spr*.png"))
    sheets += [p for p in [tex / "sheets" / "sprites.png"] if p.exists()]
    for png in sheets:
        if ".orig." in png.name:
            continue
        im = Image.open(png).convert("RGBA")
        px = im.load()
        cnt = 0
        for y in range(im.height):
            for x in range(im.width):
                if px[x, y][3] == 0 or x < 4:
                    continue
                cx = (x - 4) % 36  # ADR-0153 sheets: 32 px cells at x = 4 + 36k
                if cx >= 32:
                    continue
                px[x, y] = Y if cx < 8 else (M if cx < 24 else B)
                cnt += 1
        im.save(png)
        print(f"{png.name}: {cnt} px repainted")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
