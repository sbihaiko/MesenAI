#!/usr/bin/env python3
"""measure_capture_draw_rate -- how often each captured screen's gate fires.

F12.13 / ADR-0221 asks the library re-record to publish, per pack, the capture
count *and* the per-capture draw rate, so the cost option C would have hidden
(a capture that is refused nothing but draws on fewer frames) is visible.
This tool reads that rate off the recording itself, with no re-render:

* the pack's `hires.txt` -- every `<background>` line and the
  `<condition>...,tileAtPosition,x,y,tile,palette` definitions its `[a&b&c]`
  prefix names (the recorder writes exactly that form, ADR-0050 / ADR-0159);
* the recorder's grid dump (`MESEN_SHEET_GRID_DUMP`, see Core/AGENTS.md) --
  one `F` line per *played* frame (repeats re-emitted), `K id <16 CHR bytes>
  <palette>` interning a shape, `P id <palette>` interning a palette word, and
  `x y shape palette` per drawn cell.

A gate fires on a frame when every probe finds, at its (x, y), a cell whose
shape has the probe's CHR bytes and whose palette word is the probe's. A CHR
ROM pack keys the probe by tile *index*; the bytes come from the ROM's own
CHR block (`--rom`), which is why the ROM is an argument. A CHR RAM pack
carries the 16 bytes inline and needs no ROM.

    draw rate = frames the gate fires on / frames played

The recording it reads must be the one the pack was written from -- the same
route on the same binary -- otherwise the number says nothing. Two gates that
fire on the same frame are both counted; co-gating is a different question
(`docs/validation/adr0217-0218-anchor-gate-collisions-2026-09-20.md`).

Usage:
  python3 scripts/measure_capture_draw_rate.py <hires.txt> <grid.txt> [--rom <rom.nes>] [--json <out>]

Exit code 2 on a pack whose gates cannot be decoded (a CHR ROM pack without
--rom, a condition kind this tool does not score); 0 otherwise. A capture
whose gate fires on no frame at all is reported, not fatal -- it is exactly
the kind of number this tool exists to surface.
"""
import argparse
import json
import re
import sys
from pathlib import Path

_BG = re.compile(r"^\[([^\]]*)\]<background>([^,]+)")
_COND = re.compile(r"^<condition>([^,]+),tileAtPosition,(-?\d+),(-?\d+),([0-9A-Fa-f]+),([0-9A-Fa-f]+)(,Y)?\s*$")
_OTHER_COND = re.compile(r"^<condition>([^,]+),")


def read_chr(rom: Path) -> bytes:
    data = rom.read_bytes()
    if data[:4] != b"NES\x1a":
        raise ValueError(f"{rom}: not an iNES file")
    prg = data[4] * 16384
    chr_size = data[5] * 8192
    offset = 16 + (512 if data[6] & 0x04 else 0) + prg
    return data[offset:offset + chr_size]


def parse_pack(hires: Path, chr_rom: bytes | None) -> tuple[list[dict], list[str]]:
    """Returns ([{name, png, probes:[(x, y, tile_bytes, palette)]}], problems)."""
    conditions: dict[str, tuple] = {}
    unscored: dict[str, str] = {}
    backgrounds = []
    problems = []
    for line in hires.read_text(encoding="utf-8", errors="replace").splitlines():
        m = _COND.match(line)
        if m:
            name, x, y, tile, pal, ignore_pal = m.groups()
            if len(tile) == 32:
                tile_bytes = bytes.fromhex(tile)
            elif chr_rom is None:
                problems.append(f"{name}: keyed by CHR index {tile} but no --rom given")
                tile_bytes = None
            else:
                index = int(tile, 16)
                tile_bytes = chr_rom[index * 16:index * 16 + 16]
                if len(tile_bytes) != 16:
                    problems.append(f"{name}: CHR index {tile} is outside the ROM's CHR")
                    tile_bytes = None
            conditions[name] = (int(x), int(y), tile_bytes, None if ignore_pal else int(pal, 16))
            continue
        m = _OTHER_COND.match(line)
        if m:
            unscored[m.group(1)] = line.split(",")[1] if "," in line else "?"
            continue
        m = _BG.match(line)
        if m:
            names = [n for n in m.group(1).split("&") if n]
            probes = []
            for n in names:
                if n in conditions:
                    probes.append(conditions[n])
                elif n in unscored:
                    problems.append(f"{m.group(2)}: condition {n} is {unscored[n]}, not scored")
                else:
                    problems.append(f"{m.group(2)}: condition {n} undefined")
            backgrounds.append({"name": Path(m.group(2)).stem, "png": m.group(2), "probes": probes})
    return backgrounds, problems


def score(backgrounds: list[dict], grid: Path) -> dict:
    wanted = set()
    for bg in backgrounds:
        for x, y, _, _ in bg["probes"]:
            wanted.add((x, y))
    shapes: dict[int, tuple[bytes, int]] = {}
    palettes: dict[int, int] = {}
    fired = [0] * len(backgrounds)
    frames = 0
    cells: dict[tuple[int, int], tuple[int, int]] = {}

    def close_frame():
        nonlocal frames
        if frames == 0:
            return
        for i, bg in enumerate(backgrounds):
            if not bg["probes"]:
                continue
            ok = True
            for x, y, tile_bytes, pal in bg["probes"]:
                cell = cells.get((x, y))
                if cell is None or tile_bytes is None:
                    ok = False
                    break
                shape, cell_pal = cell
                art = shapes.get(shape)
                if art is None or art[0] != tile_bytes:
                    ok = False
                    break
                if pal is not None:
                    have = palettes.get(cell_pal, art[1]) if cell_pal != 255 else art[1]
                    if have != pal:
                        ok = False
                        break
            fired[i] += 1 if ok else 0

    with grid.open("r", encoding="ascii", errors="replace") as fh:
        for line in fh:
            c = line[0]
            if c == "F":
                close_frame()
                frames += 1
                cells = {}
            elif c == "K":
                _, sid, tile, pal = line.split()
                shapes[int(sid)] = (bytes.fromhex(tile), int(pal, 16))
            elif c == "P":
                _, pid, pal = line.split()
                palettes[int(pid)] = int(pal, 16)
            elif c == "M":
                continue
            else:
                parts = line.split()
                if len(parts) < 3:
                    continue
                x, y = int(parts[0]), int(parts[1])
                if (x, y) in wanted:
                    cells[(x, y)] = (int(parts[2]), int(parts[3]) if len(parts) > 3 else 255)
        close_frame()

    rows = []
    for bg, n in zip(backgrounds, fired):
        rows.append({
            "capture": bg["name"],
            "probes": len(bg["probes"]),
            "frames_fired": n,
            "rate": (n / frames) if frames else 0.0,
        })
    scored = [r for r in rows if r["probes"]]
    return {
        "frames_played": frames,
        "captures": len(rows),
        "captures_scored": len(scored),
        "captures_never_firing": sum(1 for r in scored if r["frames_fired"] == 0),
        "mean_rate": (sum(r["rate"] for r in scored) / len(scored)) if scored else 0.0,
        "rows": rows,
    }


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("hires", type=Path)
    ap.add_argument("grid", type=Path)
    ap.add_argument("--rom", type=Path)
    ap.add_argument("--json", type=Path)
    args = ap.parse_args(argv)

    chr_rom = read_chr(args.rom) if args.rom else None
    backgrounds, problems = parse_pack(args.hires, chr_rom)
    for p in problems:
        print(f"warning: {p}", file=sys.stderr)
    result = score(backgrounds, args.grid)
    result["problems"] = problems
    for row in result["rows"]:
        print(f"{row['capture']:>10}  probes={row['probes']}  fired={row['frames_fired']:5d}/{result['frames_played']}  rate={row['rate']:.4f}")
    print(f"captures={result['captures']} scored={result['captures_scored']} "
          f"never_firing={result['captures_never_firing']} mean_rate={result['mean_rate']:.4f} "
          f"frames={result['frames_played']}")
    if args.json:
        args.json.write_text(json.dumps(result, indent=2) + "\n")
    return 2 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
