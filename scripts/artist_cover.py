#!/usr/bin/env python3
"""Which of an artist's HD-pack tiles did the recorded states put on screen?

    scripts/artist_cover.py <artist hires.txt> <recorded pack dir>...

A recorded pack dir is the `auto/` folder the bootstrap builder writes
(`.../<rom name>/auto`, holding `textures/hires.txt` and
`textures/sheets/spr*.json`). The unit is `tileData`: the same tile under a
stage's other palette is a different `hires.txt` key, so keys undercount
figures the artist repaints per stage. An artist image counts as *sprite*
when at least half of its seen tiles sit on one of the recorded packs'
sprite sheets, *background* otherwise, *unseen* when no tile of it was on
screen. Optional `[cond]` prefixes on the artist's rules are read only for
`memoryCheckConstant` on `$30` (Contra's stage byte), reported as the
image's stage gate - a gate marks a stage-specific repaint, not a
stage-exclusive tile.

Prints three markdown tables: totals, per artist image, per recorded
state (with the tiles only that state exhibited). This is the measurement
ADR-0182 §3 asks for before a further stage is played; the 2026-09-13 run
over Contra80s 1.1 and the fifteen Contra packs is summarised in that ADR.
"""
import collections, glob, json, os, re, sys

TILE = re.compile(r"(?:\[([^\]]*)\])?<tile>(\d+),([0-9A-Fa-f]+),([0-9A-Fa-f]+),")
COND = re.compile(r"<condition>([^,]+),memoryCheckConstant,([0-9A-Fa-f]+),==,([0-9A-Fa-f]+)")


def parse(path):
    """(image name, tileData, palette, {stage gate values}) per <tile> rule."""
    img, conds, out = {}, {}, []
    for line in open(path, encoding="utf-8", errors="replace"):
        line = line.strip()
        if line.startswith("<img>"):
            img[len(img)] = line[5:]
            continue
        m = COND.match(line)
        if m:
            conds[m.group(1)] = (m.group(2).upper(), int(m.group(3), 16))
            continue
        m = TILE.match(line)
        if m:
            gate = set()
            for c in (m.group(1) or "").split("&"):
                c = c.strip().lstrip("!")
                if c in conds and conds[c][0] == "30":
                    gate.add(conds[c][1])
            out.append((img.get(int(m.group(2)), m.group(2)), m.group(3).upper(), m.group(4).upper(), gate))
    return out


def main(argv):
    if len(argv) < 3:
        print(__doc__.strip().splitlines()[2].strip(), file=sys.stderr)
        return 2
    artist = parse(argv[1])
    a_keys, a_data, a_gate = collections.defaultdict(set), collections.defaultdict(set), collections.defaultdict(set)
    for name, data, pal, gate in artist:
        a_keys[name].add((data, pal))
        a_data[name].add(data)
        a_gate[name] |= gate
    all_keys = set().union(*a_keys.values())
    all_data = set().union(*a_data.values())

    per, seen, sprite = {}, set(), set()
    for root in argv[2:]:
        name = os.path.basename(os.path.dirname(os.path.dirname(root.rstrip("/")))) or root
        rules = parse(os.path.join(root, "textures", "hires.txt"))
        keys = {(d, p) for _, d, p, _ in rules}
        per[name] = keys
        seen |= keys
        for sheet in glob.glob(os.path.join(root, "textures", "sheets", "spr*.json")):
            for cell in json.load(open(sheet)).get("cells", []):
                for t in cell.get("tiles", []):
                    sprite.add(t["tile"].upper())
    seen_data = {d for d, _ in seen}

    rows = []
    for name in a_keys:
        data = a_data[name]
        seen_here = data & seen_data
        spr = sum(1 for d in seen_here if d in sprite)
        kind = "unseen" if not seen_here else ("sprite" if spr / len(seen_here) >= 0.5 else "background")
        rows.append((name, kind, sorted(a_gate[name]), len(a_keys[name]), len(data), len(seen_here), len(a_keys[name] & seen)))

    print(f"artist: {len(a_keys)} images, {len(artist)} tile rules, {len(all_keys)} keys (tileData+palette), {len(all_data)} distinct tileData")
    print(f"recorded: {len(per)} packs, {len(seen)} keys, {len(seen_data)} tileData, {len(sprite)} sprite tileData on sheets")
    print(f"artist tileData on screen in some state: {len(all_data & seen_data)}/{len(all_data)}; exact keys {len(all_keys & seen)}/{len(all_keys)}")
    for kind in ("sprite", "background", "unseen"):
        r = [x for x in rows if x[1] == kind]
        print(f"  {kind:10s} images {len(r):3d}  tileData {sum(x[4] for x in r):5d}  seen {sum(x[5] for x in r):5d}")

    print("\n| artist image | kind | $30 gate | tile rules | distinct tileData | seen | exact keys seen |")
    print("|---|---|---|---|---|---|---|")
    order = {"sprite": 0, "unseen": 1, "background": 2}
    for name, kind, gate, nkeys, ndata, nseen, nexact in sorted(rows, key=lambda r: (order[r[1]], -(r[4] - r[5]))):
        print(f"| {name} | {kind} | {','.join(map(str, gate)) or '-'} | {nkeys} | {ndata} | {nseen} | {nexact} |")

    print("\n| recorded state | tileData | artist tileData exhibited | only this state |")
    print("|---|---|---|---|")
    per_data = {n: {d for d, _ in k} for n, k in per.items()}
    for n, k in sorted(per_data.items(), key=lambda x: -len(x[1] & all_data)):
        others = set().union(*(v for m, v in per_data.items() if m != n)) if len(per_data) > 1 else set()
        print(f"| {n} | {len(k)} | {len(k & all_data)} | {len((k & all_data) - others)} |")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
