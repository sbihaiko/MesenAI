#!/usr/bin/env python3
"""Merge the artist kit's manifest fragments into one kit (`kit.json`) and one
human page (`ARTIST.md`).

Each generator of the kit writes its own painting surfaces plus a fragment
`kit-part-<part>.json` describing them (the contract lives in
`runs/golden-20260913-f922/artist-kit-contract.md`): the sprite figure grids,
the background object sheets, the stage panoramas and the completed pattern
pages. This script owns nothing of that content - it orders it, counts it, and
writes the page an artist opens first, which the Phase 9 cold read found to be
the thing missing most: `sprites.png` read as noise only because nobody said it
is the raw OAM vocabulary and not a painting surface (ADR-0153 §3).

Usage:
    scripts/artist_kit_assemble.py <kit dir> [--title "Contra, the second base"]
"""

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import asset_names as N  # noqa: E402 — the F12.4 painting-surface name contract

# The order an artist should open the kit in, most recognisable first. A part
# missing from the kit is simply skipped - the four generators run separately
# and a kit assembled from one of them is still a kit.
PART_ORDER = ("sprites", "background", "map", "chr")

PART_TITLE = {
    "sprites": "Figures",
    "background": "Scenery",
    "map": "Stage maps",
    "chr": "Pattern pages",
}

PART_BLURB = {
    "sprites": (
        "Whole figures, never fragments. Where the recording caught an "
        "animation repeating, a row is that animation and a column one of its "
        "phases, so a row can be repainted straight across; where it caught "
        "none, the figures are simply grouped. The sheet's own notes below say "
        "which case this pack is."
    ),
    "background": (
        "One file per scenery element the recording could group into something "
        "nameable. Elements with no art in them were dropped - they are listed "
        "at the end so you can see what was left out and why."
    ),
    "map": (
        "The stage as one image, stitched from the recorded playthrough. Paint "
        "it as a picture; the slicer cuts it back into the tiles the pack needs."
    ),
    "chr": (
        "The ROM's own pattern pages, 16x16 tiles each, in the layout the game "
        "stores them in. Cells marked as fill were never seen in play and were "
        "recovered from the ROM - treat their colours as a guess, not evidence."
    ),
}


class KitError(Exception):
    pass


def load_fragments(kit_dir: Path) -> list:
    """The fragments present in `kit_dir`, in reading order."""
    found = {}
    for path in sorted(kit_dir.glob("kit-part-*.json")):
        try:
            data = json.loads(path.read_text())
        except json.JSONDecodeError as exc:
            raise KitError(f"{path.name}: not valid JSON ({exc})") from exc
        part = data.get("part") or path.stem[len("kit-part-"):]
        if part in found:
            raise KitError(f"two fragments claim part '{part}'")
        data["part"] = part
        found[part] = data
    ordered = [found.pop(p) for p in PART_ORDER if p in found]
    # A part nobody planned for still gets shown, after the known ones.
    ordered.extend(found[k] for k in sorted(found))
    return ordered


def _count(fragment: dict) -> dict:
    files = fragment.get("files") or []
    cells = sum(int(f.get("cells") or 0) for f in files)
    inferred = sum(1 for f in files if f.get("seen") is False)
    return {
        "files": len(files),
        "cells": cells,
        "inferred_files": inferred,
        "dropped": len(fragment.get("dropped") or []),
    }


def _verify_line(fragment: dict) -> str:
    v = fragment.get("verify") or {}
    if not v.get("ran"):
        return "not verified"
    before, after = v.get("keys_before"), v.get("keys_after")
    lost, added = v.get("lost", 0), v.get("added", 0)
    errors = v.get("errors", 0)
    # A key the rebuild gained is only harmless when it is a key the pack's own
    # manifest already held and the surface merely routed to art for the first
    # time — which is what a stage panorama does, and it says so with
    # `addedAreFromSource`. A gained key from anywhere else is a key the pack
    # did not have, and calling that "passed" would hand the artist a green
    # line over invented art.
    from_source = bool(v.get("addedAreFromSource"))
    ok = errors == 0 and not lost and (not added or from_source)
    state = "passed" if ok else "FAILED"
    tail = ""
    if added and from_source:
        tail = " (each already in the pack's own manifest, newly routed to art)"
    return (f"{state}: rebuild reported {errors} error(s), "
            f"{before} tile keys before, {after} after, {lost} lost, {added} added{tail}")


def _name_surfaces(fragment: dict) -> None:
    """Stamp each surface with the name the artist's paint program exports to.

    F12.4 / ADR-0213. `path` is relative to the kit (`sheets/usr000.png`) but a
    Photoshop layer name reads `/` as a subfolder under its own `-assets`
    folder, so what an artist pastes is the base name alone. The generators
    already refuse an unusable name at write time; this is the last gate before
    the kit claims one, and it also catches the rule that only exists *between*
    names: two surfaces in one folder that differ by case are one file on the
    artist's machine.
    """
    by_folder = {}
    for entry in fragment.get("files") or []:
        path = str(entry.get("path") or "")
        if not path:
            continue
        name = N.asset_name_for(path)
        reasons = N.check_asset_name(name)
        if reasons:
            raise KitError(
                f"{fragment.get('part', '?')}: `{path}` cannot be painted - "
                + "; ".join(reasons))
        entry["assetName"] = name
        by_folder.setdefault(path[: -len(name)], []).append(name)
    for folder, names in sorted(by_folder.items()):
        clashes = N.check_asset_set(names)
        if clashes:
            raise KitError(
                f"{fragment.get('part', '?')}: {folder or './'} - "
                + "; ".join(clashes))


def build_kit(kit_dir: Path, title: str = "") -> dict:
    fragments = load_fragments(kit_dir)
    if not fragments:
        raise KitError(f"no kit-part-*.json fragment in {kit_dir}")
    packs = {f.get("pack") for f in fragments if f.get("pack")}
    # A static kit's `pack` is an output location that may not even exist, so
    # the ROM is what names it (ADR-0219).
    static_rom = next((f.get("rom") for f in fragments
                       if f.get("static") and f.get("rom")), "")
    kit = {
        "version": 1,
        "title": title or static_rom or (sorted(packs)[0] if packs else kit_dir.name),
        "packs": sorted(p for p in packs if p),
        "parts": [],
        "totals": {"files": 0, "cells": 0, "inferred_files": 0, "dropped": 0},
    }
    # A kit is static only when *every* part of it is: one recorded part means a
    # recording exists, and the first line must not deny it.
    if fragments and all(f.get("static") for f in fragments):
        kit["static"] = True
        kit["rom"] = next((f.get("rom") for f in fragments if f.get("rom")), "")
    for fragment in fragments:
        _name_surfaces(fragment)
        counts = _count(fragment)
        for key, value in counts.items():
            kit["totals"][key] += value
        kit["parts"].append({
            "part": fragment["part"],
            # ADR-0219: a part projected over the ROM alone, with no recording.
            # Carried up so ARTIST.md's first line can say so before anything
            # else, and absent on every recorded part.
            **({"static": True} if fragment.get("static") else {}),
            "generator": fragment.get("generator", ""),
            "counts": counts,
            "verify": fragment.get("verify", {}),
            "files": fragment.get("files", []),
            "dropped": fragment.get("dropped", []),
            "notes": fragment.get("notes", []),
        })
    return kit


def render_markdown(kit: dict) -> str:
    out = [f"# Artist kit - {kit['title']}", ""]
    if kit.get("static"):
        # ADR-0219 / PRD F12.9: the first line, before anything else, because
        # every later sentence of this page is about what a recording gives an
        # artist and this kit had none.
        rom = kit.get("rom") or "the ROM"
        out.append(
            f"**Nothing here was seen in play.** Every page was read straight out of "
            f"{rom}'s own pattern tables, with no recording at all: the shapes are "
            "exact, the colours are a placeholder, and each cell says `seen: false` "
            "in its sidecar. There is no figure sheet, no scenery sheet and no stage "
            "map in this kit - those come from what a run observed, and nothing was "
            "observed. Record the game and generate the kit again to get them; a "
            "recorded cell always wins over one of these."
        )
    else:
        out.append(
            "Everything here was generated from a recording of the game being played. "
            "Nothing was drawn by hand, and nothing was invented: a caption comes from "
            "the recording's own data (a default name the recorder inferred is marked "
            "`inferred` in its sidecar, and a name you put in names.json wins over it), "
            "and anything inferred rather than seen is marked as such."
        )
    out.append("")
    out.append("## Before you paint")
    out.append("")
    out.extend([
        "- Every painting surface has an untouched `.orig.png` beside it. That file is "
        "the reference the pack is rebuilt against - never paint it.",
        "- Keep each image's size and the position of everything inside it. A cell that "
        "moves stops matching what the game draws.",
        "- Transparency is transparency. What is transparent in the reference must stay "
        "transparent in your version.",
        "- When you are done, rebuild the pack: `python3 scripts/mep_build.py build "
        "<pack folder>`. It reports 0 errors when the pack is still legal.",
        "- Do not paint `sheets/sprites.png` if you meet it: it is the raw sprite "
        "vocabulary the recorder dumps, not a surface (ADR-0153 §3).",
    ])
    out.append("")
    out.append("## Open, paint, save")
    out.append("")
    out.extend([
        "Open the surface in the program you already use, paint on it, and save "
        "back over the same file - then ask the running game for it with **HD Packs "
        "> Reload Repainted Images**. You do not reopen the ROM and you do not lose "
        "where you are standing.",
        "",
        "Each surface's file name is also the name to export to, so the save is one "
        "shortcut after the first time:",
        "",
        "| program | the one step |",
        "|---|---|",
        "| GIMP | *File > Overwrite `<name>.png`* |",
        "| Aseprite | *File > Export* once, then *Repeat last export* |",
        "| Krita | *File > Export* once, then *File > Export* again over the same path |",
        "| Photoshop | *File > Generate > Image Assets*, with your layer named exactly "
        "`<name>.png` (the `assetName` in `kit.json`) |",
        "",
        "Photoshop is the one exception and it is worth knowing before you start: its "
        "generator always writes into a `<document>-assets` folder beside the `.psd` "
        "and that location cannot be changed. The file it writes has the right name, "
        "so copying it over the kit's copy is the whole difference. The other three "
        "overwrite the kit file directly.",
        "",
        "### The layered file (GIMP, Krita, MyPaint)",
        "",
        "Beside every surface there is also a `<name>.ora` - the same picture as "
        "layers, for a program that opens OpenRaster: `orig` (the untouched reference, "
        "locked), `paint` (empty - the one you paint on; it is the topmost visible "
        "layer when the file opens), `guides` (the cell grid, the captions and a hatch "
        "over every cell nothing was seen in play, hidden) and `palettes` (the colours "
        "the recording saw on this sheet, hidden). A stage panorama also carries "
        "`context` - the stage at 1x, at half strength, below the grid, a reference "
        "you may move - so it has five layers; a figure, scenery or pattern page has "
        "four, because nothing recorded says where on the stage its cells were seen.",
        "",
        "The `.ora` is a **starting point, not the deliverable**. Paint on `paint`, "
        "then export a flat PNG over `<name>.png` - the name in the table above - "
        "exactly as you would without it. Nothing reads the `.ora` back: not the "
        "rebuild, not the reload, not the lint. Keep `guides` and `palettes` hidden "
        "when you export; both are drawn in one magenta (`#FF00FD`) no NES palette "
        "reaches, and a cell that carries that colour is refused by "
        "`python3 scripts/mep_lint.py`, which names the cell. Photoshop and Aseprite "
        "do not open `.ora`; they stay on the per-surface names above, and there is no "
        "`.psd`, `.aseprite` or `.kra` in the kit.",
    ])
    out.append("")
    out.append("## When you are done")
    out.append("")
    out.extend([
        "The kit is a folder, not the pack itself. There is no recording to copy here, "
        "so the pack is the pages and nothing else:",
        "",
        "```",
        "mkdir -p <game>/painted/textures",
        "cp -R <kit>/chr <game>/painted/textures/",
        "python3 scripts/mep_build.py build <game>/painted   # 0 errors means it is legal",
        "```",
        "",
        "`chr/fill-rules.hires.txt` travels with the pages: it is the manifest, one "
        "`<tile>` row per tile of the ROM, and the build regenerates "
        "`textures/hires.txt` from it. Leave every `.orig.png` in the kit - it is the "
        "untouched reference, and painting it is how your work becomes invisible.",
    ] if kit.get("static") else [
        "The kit is a folder beside the recording, not the pack itself. To turn painted "
        "work into a pack, copy the recording, drop your files into the copy and build it:",
        "",
        "```",
        "cp -R <game>/auto <game>/painted",
        "cp <kit>/sheets/*.png <kit>/sheets/*.json <game>/painted/textures/sheets/",
        "cp <kit>/chr/*.png    <kit>/chr/*.json    <game>/painted/textures/chr/",
        "python3 scripts/mep_build.py build <game>/painted   # 0 errors means it is legal",
        "```",
        "",
        "Copy each sheet together with its `.json`: the JSON is the slicing contract that "
        "says which cell is which tile. Leave every `.orig.png` in the kit - it is the "
        "untouched reference, and painting it is how your work becomes invisible.",
        "",
        "Build the whole copy, not a folder holding only your sheets. A pack manifest also "
        "points at the recording's screen images, and a part-folder build fails on those "
        "references - measured, not guessed. Installing the finished pack as the human "
        "layer (`mep/`, which wins over `auto/` entry by entry, ADR-0147) is the "
        "installer's job, not something to assemble by hand.",
        "",
        "A stage map is not copied: it is sliced back into tiles, because a pack stores "
        "tiles and the map is a picture of them. Run the slicer named in the map section.",
    ])
    out.extend([
        "",
        "If the manifest carries a bare `<bgPreservesBehindBgSprites>` line, leave it: "
        "the recorder writes it so a recorded screen does not hide a behind-background "
        "sprite over empty (colour-0) canvas (ADR-0224). It is not noise, and other "
        "emulators simply ignore it.",
    ])
    out.append("")
    out.append("## What is in here")
    out.append("")
    out.append("| open | what it is | files | cells | verified |")
    out.append("|---|---|---|---|---|")
    for i, part in enumerate(kit["parts"], start=1):
        name = PART_TITLE.get(part["part"], part["part"])
        c = part["counts"]
        out.append(f"| {i}. {name} | {PART_BLURB.get(part['part'], '')} | "
                   f"{c['files']} | {c['cells']} | {_verify_line(part)} |")
    out.append("")
    for i, part in enumerate(kit["parts"], start=1):
        name = PART_TITLE.get(part["part"], part["part"])
        out.append(f"## {i}. {name}")
        out.append("")
        files = part["files"]
        if files:
            out.append("| file | shows | size | from |")
            out.append("|---|---|---|---|")
            for f in files:
                shape = ""
                if f.get("rows") and f.get("columns"):
                    shape = f"{f['rows']} x {f['columns']} cells"
                elif f.get("cells"):
                    shape = f"{f['cells']} cells"
                origin = "recorded" if f.get("seen", True) else "inferred - check it"
                out.append(f"| `{f.get('path','')}` | {f.get('title','')} | {shape} | {origin} |")
            out.append("")
        for note in part["notes"]:
            out.append(f"- {note}")
        if part["notes"]:
            out.append("")
        if part["dropped"]:
            out.append("Left out of the kit:")
            out.append("")
            for d in part["dropped"]:
                out.append(f"- `{d.get('path','')}` - {d.get('why','')}")
            out.append("")
    t = kit["totals"]
    out.append("## Totals")
    out.append("")
    out.append(f"{t['files']} files, {t['cells']} cells, {t['inferred_files']} files carrying "
               f"inferred art, {t['dropped']} surfaces left out.")
    out.append("")
    return "\n".join(out)


def main(argv=None):
    ap = argparse.ArgumentParser(description="Assemble an artist kit from its manifest fragments")
    ap.add_argument("kit", type=Path, help="the kit folder holding kit-part-*.json")
    ap.add_argument("--title", default="", help="human title for the kit page")
    args = ap.parse_args(argv)
    try:
        kit = build_kit(args.kit, args.title)
    except KitError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    (args.kit / "kit.json").write_text(json.dumps(kit, indent=2) + "\n")
    (args.kit / "ARTIST.md").write_text(render_markdown(kit))
    t = kit["totals"]
    print(f"{args.kit}: {len(kit['parts'])} part(s), {t['files']} file(s), "
          f"{t['cells']} cell(s); wrote kit.json and ARTIST.md")
    for part in kit["parts"]:
        print(f"  {part['part']:<11} {_verify_line(part)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
