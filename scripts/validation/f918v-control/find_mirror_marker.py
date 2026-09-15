"""Find the four-quadrant mirror marker in control screenshots and read its
orientation. Marker cell: TL red(200,0,0) | TR green(0,200,0) /
BL blue(0,0,200) | BR white(220,220,220). H flip puts green left of red,
V flip puts blue above red. Connected components over all four colours;
per component the per-colour centroids decide the flip.
"""
import sys
from collections import deque

from PIL import Image

COLS = {(200, 0, 0): "R", (0, 200, 0): "G", (0, 0, 200): "B", (220, 220, 220): "W"}

for path in sys.argv[1:]:
    im = Image.open(path).convert("RGB")
    w, h = im.size
    px = im.load()
    seen = set()
    comps = []
    for y in range(h):
        for x in range(w):
            if px[x, y] not in COLS or (x, y) in seen:
                continue
            q, comp = deque([(x, y)]), {}
            seen.add((x, y))
            while q:
                cx, cy = q.popleft()
                comp.setdefault(COLS[px[cx, cy]], []).append((cx, cy))
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < w and 0 <= ny < h and (nx, ny) not in seen and px[nx, ny] in COLS:
                        seen.add((nx, ny))
                        q.append((nx, ny))
            comps.append(comp)
    if not comps:
        continue
    print(path.split("/")[-1])
    for comp in comps:
        cen = {k: (sum(p[0] for p in v) / len(v), sum(p[1] for p in v) / len(v)) for k, v in comp.items()}
        size = sum(len(v) for v in comp.values())
        x0 = min(p[0] for v in comp.values() for p in v)
        y0 = min(p[1] for v in comp.values() for p in v)
        hflip = vflip = "?"
        if "R" in cen and "G" in cen:
            hflip = "H" if cen["G"][0] < cen["R"][0] else "-"
        elif "B" in cen and "W" in cen:
            hflip = "H" if cen["W"][0] < cen["B"][0] else "-"
        if "R" in cen and "B" in cen:
            vflip = "V" if cen["B"][1] < cen["R"][1] else "-"
        elif "G" in cen and "W" in cen:
            vflip = "V" if cen["W"][1] < cen["G"][1] else "-"
        print(f"  at ({x0},{y0}) px={size} colours={''.join(sorted(comp))} flip={hflip}{vflip}")
