#!/usr/bin/env python3
"""Does a recording add kit keys no other pack has? (ADR-0238 section 5, clause 2)

    scripts/kit_new_keys.py --candidate LABEL=PATH [...] --baseline LABEL=PATH [...] [--json OUT]

ADR-0242 Q3 adopts the AI recorder only when an AI-driven recording "adds keys
that no existing pack or committed route has". F14.15 measured that with a
scratch script that was never versioned (`runs/f1415/cells.py`); this is the
same measurement, versioned, so F14.20 and any later pass read one definition.

The unit is the `hires.txt` key `(tileData, palette)` of a `<tile>` rule - the
bootstrap writes one rule per key it saw, and an artist repaints per key. A
condition prefix (`[cond]<tile>...`) does not change the key. `tileData` is
either the 16 bytes of a CHR RAM pattern (32 hex digits) or a CHR ROM tile
index; the emulator tells them apart by width (`HdPackLoader::ReadTileData`).
An index is decimal below `<ver>103` and hex from it on, so indices are
normalized to an integer before they are compared - reading them all as hex
invents keys that are not there.

PATH is a `hires.txt`, a `textures/` folder, a pack folder holding
`textures/hires.txt`, or a Remaster project / `auto/` folder, whose every
recording (`auto/rec-NNN/textures`, or the bare `auto/textures` that reads as
rec-001, ADR-0243) is unioned.

Prints, per pack, its rule and key count, its key namespace and the keys no
other pack listed has; per candidate, the keys absent from the union of every
baseline (the clause-2 number), and the keys absent from the other candidates
too.
"""
import argparse
import json
import re
import sys
from pathlib import Path

TILE = re.compile(r"^(?:\[[^\]]*\])?<tile>\d+,([0-9A-Fa-f]+),([0-9A-Fa-f]+),")
VERSION = re.compile(r"^<ver>(\d+)")
PATTERN_WIDTH = 32


def normalise_tile(tile_data: str, version: int) -> str:
    """A canonical spelling of a tileData field: patterns uppercased, indices as
    `#<decimal>` so a decimal and a hex spelling of one index compare equal."""
    if len(tile_data) >= PATTERN_WIDTH:
        return tile_data.upper()
    base = 16 if version >= 103 else 10
    try:
        return f"#{int(tile_data, base)}"
    except ValueError:
        #A pre-103 index with hex letters is not an index the loader reads as
        #one; keep it apart rather than guess
        return f"?{tile_data.upper()}"


def hires_keys(path: Path):
    """`(rules, {(tileData, palette)})` for one hires.txt."""
    version = 0
    rules = 0
    keys = set()
    with open(path, encoding="utf-8", errors="replace") as handle:
        for raw in handle:
            line = raw.strip()
            match = VERSION.match(line)
            if match:
                version = int(match.group(1))
                continue
            match = TILE.match(line)
            if match:
                rules += 1
                keys.add((normalise_tile(match.group(1), version), match.group(2).upper()))
    return rules, keys


def hires_files(path: Path) -> list:
    """Every hires.txt PATH names (see the module docstring), sorted."""
    path = Path(path)
    if path.is_file():
        return [path]
    if (path / "hires.txt").is_file():
        return [path / "hires.txt"]
    if (path / "textures" / "hires.txt").is_file():
        return [path / "textures" / "hires.txt"]
    auto = path / "auto" if (path / "auto").is_dir() else path
    found = sorted(auto.glob("rec-*/textures/hires.txt"))
    if (auto / "textures" / "hires.txt").is_file():
        found.insert(0, auto / "textures" / "hires.txt")
    return found


def namespace(keys) -> str:
    kinds = {"pattern" if not tile.startswith(("#", "?")) else "index" for tile, _ in keys}
    return "+".join(sorted(kinds)) if kinds else "empty"


def load(label: str, path: str) -> dict:
    files = hires_files(Path(path))
    if not files:
        raise SystemExit(f"error: {label}: no hires.txt under {path}")
    rules, keys = 0, set()
    for file in files:
        count, found = hires_keys(file)
        rules += count
        keys |= found
    return {"label": label, "path": str(path), "files": [str(f) for f in files],
            "rules": rules, "keys": keys}


def measure(candidates: list, baselines: list) -> dict:
    packs = candidates + baselines
    for pack in packs:
        others = set().union(*(p["keys"] for p in packs if p is not pack))
        pack["unique"] = len(pack["keys"] - others)
    union = set().union(*(p["keys"] for p in baselines)) if baselines else set()
    report = {"baseline_union_keys": len(union), "packs": [], "candidates": []}
    for pack in packs:
        report["packs"].append({
            "label": pack["label"], "role": "candidate" if pack in candidates else "baseline",
            "rules": pack["rules"], "keys": len(pack["keys"]),
            "namespace": namespace(pack["keys"]), "unique": pack["unique"],
            "files": pack["files"]})
    for pack in candidates:
        new = pack["keys"] - union
        other_candidates = set().union(*(p["keys"] for p in candidates if p is not pack)) \
            if len(candidates) > 1 else set()
        report["candidates"].append({
            "label": pack["label"], "keys": len(pack["keys"]),
            "new_vs_baselines": len(new),
            "new_vs_everything": len(new - other_candidates),
            "sample_new": sorted(f"{tile},{palette}" for tile, palette in new)[:8]})
    return report


def markdown(report: dict) -> str:
    lines = ["| pack | role | rules | keys | namespace | keys no other pack has |",
             "|---|---|---|---|---|---|"]
    for pack in report["packs"]:
        lines.append(f"| `{pack['label']}` | {pack['role']} | {pack['rules']} | {pack['keys']} "
                     f"| {pack['namespace']} | {pack['unique']} |")
    lines += ["", f"Union of the baselines: {report['baseline_union_keys']} keys.", "",
              "| candidate | keys | new vs the baselines' union | new vs every other pack |",
              "|---|---|---|---|"]
    for cand in report["candidates"]:
        lines.append(f"| `{cand['label']}` | {cand['keys']} | {cand['new_vs_baselines']} "
                     f"| {cand['new_vs_everything']} |")
    return "\n".join(lines) + "\n"


def parse_pair(text: str):
    label, sep, path = text.partition("=")
    if not sep or not label or not path:
        raise argparse.ArgumentTypeError(f"expected LABEL=PATH, got {text!r}")
    return label, path


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--candidate", type=parse_pair, action="append", default=[], required=True)
    parser.add_argument("--baseline", type=parse_pair, action="append", default=[])
    parser.add_argument("--json", help="also write the report as JSON")
    args = parser.parse_args(argv)
    candidates = [load(label, path) for label, path in args.candidate]
    baselines = [load(label, path) for label, path in args.baseline]
    report = measure(candidates, baselines)
    sys.stdout.write(markdown(report))
    if args.json:
        Path(args.json).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
