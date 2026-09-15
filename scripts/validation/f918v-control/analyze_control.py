"""Classify a control-pack screenshot: colour census + Y|mid|B orientation scan.

Every human-layer cell renders as  Y(8) | mid(16) | B(8)  where mid is
  C (cyan)    = gated rule matched live
  M (magenta) = gate missed, bare twin rendered
  O (orange)  = key with no gate at all
Per row, collapse pixels into runs; Y -> mid -> B (mid >= 8 px, ends >= 3)
is an un-mirrored cell, B -> mid -> Y an H-mirrored one. Adjacent
un-mirrored cells only ever produce B->Y at the seam, never B->mid->Y, so
the two patterns are exclusive.
"""
import sys
from collections import Counter

from PIL import Image

COL = {(255, 255, 0): "Y", (0, 0, 255): "B", (0, 255, 255): "C", (255, 0, 255): "M", (255, 128, 0): "O"}

for path in sys.argv[1:]:
    im = Image.open(path).convert("RGB")
    w, h = im.size
    px = im.load()
    census = Counter(COL.get(px[x, y], ".") for y in range(h) for x in range(w))
    normal, mirrored = Counter(), Counter()
    where = {}
    for y in range(h):
        runs = []
        for x in range(w):
            c = COL.get(px[x, y], ".")
            if runs and runs[-1][0] == c:
                runs[-1][2] += 1
            else:
                runs.append([c, x, 1])
        seq = [r for r in runs if r[0] != "."]
        for a, b, c in zip(seq, seq[1:], seq[2:]):
            if b[0] not in "CMO" or b[2] < 8 or a[2] < 3 or c[2] < 3:
                continue
            if a[0] == "Y" and c[0] == "B":
                normal[b[0]] += 1
            elif a[0] == "B" and c[0] == "Y":
                mirrored[b[0]] += 1
                where.setdefault(b[0], []).append((a[1], y))
    cen = " ".join(f"{k}={census[k]}" for k in "CMOYB")
    print(f"{path.split('/')[-1]}: {cen}")
    print(f"  un-mirrored rows C/M/O={normal['C']}/{normal['M']}/{normal['O']}  MIRRORED rows C/M/O={mirrored['C']}/{mirrored['M']}/{mirrored['O']}")
    for k, v in where.items():
        print(f"  mirrored {k} at {v[0]} .. {v[-1]} ({len(v)} rows)")
