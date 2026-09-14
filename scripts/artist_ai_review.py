#!/usr/bin/env python3
"""Put an AI with vision in the artist kit's loop, as a proposer that is never
believed on its own word.

The kit generators (`artist_kit.py`, `artist_bg_kit.py`, `artist_map.py`,
`artist_chr_kit.py`) can cut a recording into surfaces an artist recognises and
can prove, by rebuilding the pack, that the cut is lossless. What they cannot do
is *judge*: which of 1642 shapes is a figure and which is HUD, whether two
figures in one box are one thing or two, and what any of it is called. In Mesen
that judgement is a human pointing at the PPU viewer. This script is the
protocol for asking a vision agent instead.

Four rules it exists to enforce (the design, not an implementation detail):

1. **The reviewer looks at the rendered surface, never at raw data.** Every ask
   names a PNG a human would recognise - a figure grid, a scenery object, a
   panorama, a pattern page - and a rectangle inside it. No tile arrays, no hex
   keys, no bare 8x8 crops. Reading 8 px thumbnails is what put three green
   enemies on the player's sheet the last time a human did this by hand.
2. **What comes back is a proposal and never becomes evidence.** ADR-0183 §3:
   a generator never invents a name, and inference is never mixed into what was
   observed. Proposals land in their own `kit-proposals.json` beside the
   `kit-part-*.json` fragments, and nothing in the pack changes because one
   exists.
3. **Every proposal is falsifiable in seconds.** It must cite the file it looked
   at and the rectangle inside it, so a reviewer opens one image and knows. A
   proposal that cannot be checked is a defect and `check` rejects it.
4. **Abstention is a first-class answer.** "I do not know what this is" is a
   valid, unpenalised outcome; `score` counts it in its own bucket and never
   folds it into an accuracy number.

The reviewer is an agent with vision reading the PNGs - not an API call. There
is no key, no endpoint and no network: `packet` writes what to look at, a human
or an agent fills the answers in, `check` accepts or rejects them, `score`
measures them, and `promote` is the only door from a proposal into the
`--names` file the generators actually read.

Usage:
    scripts/artist_ai_review.py packet <kit> [--crops] [--out <dir>]
    scripts/artist_ai_review.py check  <kit> --proposals kit-proposals.json
    scripts/artist_ai_review.py truth  <kit> --reference-pack <dir>
                                       --subjects <map.json> --out truth.json
    scripts/artist_ai_review.py score  --proposals p.json --truth t.json
                                       [--alias a.json]
    scripts/artist_ai_review.py promote <kit> --proposals p.json
                                       --accepted accepted.txt --out names.json
"""

import argparse
import hashlib
import json
import re
import sys
from collections import Counter
from pathlib import Path

SCHEMA_VERSION = 1

CONFIDENCE = ("high", "medium", "low")

# An 8 px cell plus the one-cell gutter the kit's sheets are laid out on.
GUTTER = 1

# Reference-pack files that name nothing: a whole-pattern-page replacement, or
# an author's work-in-progress copy of one. They cover every tile in the game
# and so carry no subject at all.
GENERIC_REF_IMAGE = re.compile(r"^(Chr_\d+|WIP.*)\.png$", re.IGNORECASE)


class ReviewError(Exception):
    """A kit or an answer file this tool refuses - never a silent pass."""


# ---- the questions ---------------------------------------------------------
#
# One question per kind of surface. They are deliberately blunt about what an
# answer may not be: a caption that repeats the pack's own id teaches nobody
# anything, and a guess dressed as a name is the one failure mode that can
# poison a kit.

QUESTION = {
    "figure": (
        "What is the figure in this box? Name the thing it is (its subject), "
        "then the pose or phase it is in. If the box holds more than one "
        "figure overlapping, say so with \"multiple\": true instead of "
        "picking one. If you cannot tell, abstain."
    ),
    "object": (
        "What is this piece of scenery? Name the thing, not its tiles. If the "
        "sheet shows one thing in several phases, say what changes between "
        "them. If you cannot tell, abstain."
    ),
    "panorama": (
        "What place is this, and what is in it? Name the stage and the "
        "landmarks you can point at. If you cannot tell, abstain."
    ),
    "screen": (
        "What is happening on this screen? Name the place and the figures on "
        "it. If you cannot tell, abstain."
    ),
    "page": (
        "This is a pattern page, not a scene: the game's tiles in storage "
        "order, so most of it will look like fragments. Only answer where a "
        "whole recognisable thing is laid out contiguously. Abstaining on a "
        "pattern page is the expected answer."
    ),
}

ANSWER_FIELDS = ("ask", "saw", "rect", "subject", "name", "confidence", "why")


# ---- reading the kit -------------------------------------------------------

def _load_json(path: Path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except OSError as exc:
        raise ReviewError(f"{path}: unreadable ({exc})")
    except ValueError as exc:
        raise ReviewError(f"{path}: not JSON ({exc})")


def load_fragments(kit: Path) -> dict:
    """`{part: fragment}` for every `kit-part-*.json` in the kit."""
    out = {}
    for path in sorted(kit.glob("kit-part-*.json")):
        doc = _load_json(path)
        part = doc.get("part") or path.stem[len("kit-part-"):]
        out[str(part)] = doc
    if not out:
        raise ReviewError(f"{kit}: no kit-part-*.json - this is not a kit folder")
    return out


def _cell_columns(cells, gutter=GUTTER, unit=8):
    pitch = unit + gutter
    return [(c["x"] - gutter) // pitch for c in cells]


def _cell_rows(cells, gutter=GUTTER, unit=8):
    pitch = unit + gutter
    return [(c["y"] - gutter) // pitch for c in cells]


def row_bands(rows):
    """Consecutive occupied cell-rows, grouped. One band is one grid row: the
    exporter leaves a blank cell-row between two figure rows, so a gap is a row
    break and nothing else."""
    seen = sorted(set(rows))
    bands, current = [], [seen[0]]
    for r in seen[1:]:
        if r == current[-1] + 1:
            current.append(r)
        else:
            bands.append(current)
            current = [r]
    bands.append(current)
    return bands


def box_candidates(columns):
    """Every box width one grid row could have been laid out at, narrowest first.

    `artist_kit.py` pads each figure to its row's box, centres it and leaves
    exactly one empty column between boxes (`SLOT_GAP`), then emits the cells
    box by box in reading order. A width is possible when nothing sits in one of
    its gap columns, the box indexes run contiguously from zero, and the emitted
    cells never walk backwards. Several widths can survive that on their own -
    a figure narrower than its box leaves the box's edge columns empty - so the
    choice is made across the whole sheet, against the pose count, rather than
    here. Returns `[(width, [box index per column])]`."""
    out = []
    if not columns:
        return out
    for width in range(1, max(columns) + 2):
        pitch = width + 1
        if any(c % pitch == width for c in columns):
            continue                       # something sits in a gap column
        boxes = [c // pitch for c in columns]
        if boxes != sorted(boxes):
            continue                       # cells would have to walk backwards
        distinct = sorted(set(boxes))
        if distinct != list(range(len(distinct))):
            continue                       # an empty box in the middle
        out.append((width, boxes))
    return out


def split_boxes(columns):
    """The narrowest layout a single row admits, or None. `box_candidates`
    across a whole sheet is what the segmenter actually uses; this is the
    one-row question on its own."""
    candidates = box_candidates(columns)
    return candidates[0][1] if candidates else None


def _choose_widths(per_band, target):
    """Pick one candidate per band so the sheet's boxes add up to its poses.

    A row read on its own can be ambiguous; the sheet is not, because the
    sidecar says how many poses are on it. Narrowest-first order is kept, so
    among equally valid readings the one that splits into the most figures
    wins - and a sheet with no reading at all is reported, never approximated."""
    reachable = [set() for _ in range(len(per_band) + 1)]
    reachable[len(per_band)].add(0)
    for index in range(len(per_band) - 1, -1, -1):
        for _width, boxes in per_band[index]:
            count = len(set(boxes))
            for rest in reachable[index + 1]:
                reachable[index].add(count + rest)
    if target not in reachable[0]:
        return None
    chosen, remaining = [], target
    for index, candidates in enumerate(per_band):
        for _width, boxes in candidates:
            count = len(set(boxes))
            if remaining - count in reachable[index + 1]:
                chosen.append(boxes)
                remaining -= count
                break
        else:
            return None
    return chosen


def figure_boxes(sidecar: dict):
    """`[(pose id, [cell, ...], (col, row, w, h) in cells)]` for a sprite sheet.

    The sidecar records the cells and the poses on the sheet but not which cell
    belongs to which pose - the layout does, and it is deterministic once the
    pose count picks between the readings a single row admits."""
    cells = sidecar.get("cells") or []
    poses = sidecar.get("poses") or []
    if not cells or not poses:
        return []
    unit = int(sidecar.get("gridUnit") or 8)
    gutter = int(sidecar.get("gutter") or GUTTER)
    rows = _cell_rows(cells, gutter, unit)
    cols = _cell_columns(cells, gutter, unit)
    bands = [[i for i, r in enumerate(rows) if r in band] for band in row_bands(rows)]
    per_band = [box_candidates([cols[i] for i in members]) for members in bands]
    chosen = _choose_widths(per_band, len(poses))
    if chosen is None:
        raise ReviewError(
            f"{sidecar.get('sheet', '?')}: no figure-box layout puts "
            f"{len(poses)} pose(s) on this sheet")
    out, taken = [], 0
    for members, boxes in zip(bands, chosen):
        for box in sorted(set(boxes)):
            group = [members[k] for k, b in enumerate(boxes) if b == box]
            gc = [cols[i] for i in group]
            gr = [rows[i] for i in group]
            out.append((poses[taken], [cells[i] for i in group],
                        (min(gc), min(gr), max(gc) - min(gc) + 1, max(gr) - min(gr) + 1)))
            taken += 1
    return out


def _pixel_rect(cell_rect, unit, gutter, scale):
    col, row, w, h = cell_rect
    pitch = unit + gutter
    return [(col * pitch + gutter) * scale, (row * pitch + gutter) * scale,
            (w * pitch - gutter) * scale, (h * pitch - gutter) * scale]


def _scale_of(png: Path, sidecar: dict, cells) -> int:
    """The sheet's export scale, read off the PNG rather than assumed.

    Every sheet of a pack shares one scale, but a kit may be built at any of
    them; the rectangles handed to the reviewer are in the PNG's own pixels, so
    guessing here would send them to the wrong box."""
    if not cells:
        return 1
    unit = int(sidecar.get("gridUnit") or 8)
    gutter = int(sidecar.get("gutter") or GUTTER)
    width = _png_size(png)[0]
    span = (max(_cell_columns(cells, gutter, unit)) + 1) * (unit + gutter) + gutter
    for scale in (8, 6, 4, 3, 2, 1):
        if span * scale >= width > (span - unit) * scale:
            return scale
    return max(1, width // span)


def _png_size(path: Path):
    """(width, height) straight out of the IHDR - no image library needed."""
    try:
        head = path.read_bytes()[:33]
    except OSError as exc:
        raise ReviewError(f"{path}: unreadable ({exc})")
    if head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        raise ReviewError(f"{path}: not a PNG")
    return (int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big"))


# ---- the packet ------------------------------------------------------------

def _ask(kind, part, ask_id, surface, subject_id, rect, cell_rect=None,
         reference=None, note=""):
    entry = {
        "ask": ask_id,
        "kind": kind,
        "part": part,
        "surface": surface,
        "subjectId": subject_id,
        "rect": [int(v) for v in rect],
        "question": QUESTION[kind],
        "scored": kind in ("figure", "object"),
    }
    if cell_rect:
        entry["cellRect"] = [int(v) for v in cell_rect]
    if reference:
        entry["reference"] = reference
    if note:
        entry["note"] = note
    return entry


def build_packet(kit: Path) -> dict:
    """The ordered list of what to look at, and what an answer must contain.

    Order is the kit's own reading order - figures first, pattern pages last -
    because a reviewer who has already seen the player as a whole figure is the
    one who can recognise its torso on a pattern page, never the other way
    round."""
    fragments = load_fragments(kit)
    asks, skipped = [], []

    for part in ("sprites", "background", "map", "chr"):
        fragment = fragments.get(part)
        if not fragment:
            continue
        for entry in fragment.get("files") or []:
            rel = entry.get("path") or ""
            png = kit / rel
            if not png.exists():
                skipped.append({"path": rel, "why": "the kit lists it but it is not there"})
                continue
            try:
                asks.extend(_asks_for(kit, part, entry, png))
            except ReviewError as exc:
                skipped.append({"path": rel, "why": str(exc)})

    packet = {
        "version": SCHEMA_VERSION,
        "kit": str(kit),
        "reviewer": "an agent with vision reading the PNGs; no API key, no network",
        "rules": [
            "Look at the image. Never answer from a sidecar, a tile key or a file name.",
            "Cite what you looked at: every answer repeats the ask id, the file and the rect.",
            "Abstain when you cannot tell. An abstention costs nothing; a confident wrong "
            "name is the only answer that can poison a kit.",
            "A name is a caption, never evidence: nothing in the pack changes because you "
            "proposed one (ADR-0183 §3).",
        ],
        "answerSchema": {
            "file": "kit-proposals.json, beside the kit-part-*.json fragments",
            "shape": {
                "version": SCHEMA_VERSION,
                "kit": "<the kit folder this answers>",
                "packet": "<the packet's sha256, copied from this file>",
                "subjects": {"<subject key>": "<one line saying what that thing is>"},
                "proposals": [{
                    "ask": "<ask id>", "saw": "<the surface you looked at>",
                    "rect": "[x, y, w, h] inside that surface",
                    "subject": "<subject key>", "name": "<what this box shows>",
                    "confidence": "high | medium | low",
                    "why": "<what in the image made you say that>",
                    "multiple": "true when the box holds more than one figure (optional)",
                    "abstain": "true instead of subject/name/confidence (optional)",
                }],
            },
        },
        "asks": asks,
        "skipped": skipped,
    }
    packet["sha256"] = hashlib.sha256(
        json.dumps(packet["asks"], sort_keys=True).encode()).hexdigest()
    return packet


def _asks_for(kit: Path, part, entry, png: Path):
    rel = entry.get("path")
    unit_kind = entry.get("unit")
    width, height = _png_size(png)
    reference = entry.get("reference") or (
        rel.replace(".png", ".orig.png") if (kit / rel.replace(".png", ".orig.png")).exists()
        else None)

    if part == "sprites" and unit_kind == "grid":
        sidecar = kit / rel.replace(".png", ".json")
        if not sidecar.exists():
            raise ReviewError("no sidecar next to the sheet")
        doc = _load_json(sidecar)
        cells = doc.get("cells") or []
        unit = int(doc.get("gridUnit") or 8)
        gutter = int(doc.get("gutter") or GUTTER)
        scale = _scale_of(png, doc, cells)
        out = []
        for pose, _group, cell_rect in figure_boxes(doc):
            out.append(_ask(
                "figure", part, f"{png.stem}#{pose}", rel, pose,
                _pixel_rect(cell_rect, unit, gutter, scale), cell_rect, reference,
                note=entry.get("title", "")))
        return out

    if rel.startswith("scene/"):
        # A whole recorded screen: already one recognisable thing, shown whole.
        return [_ask("screen", part, png.stem, rel,
                     (entry.get("ids") or [png.stem])[0], [0, 0, width, height],
                     None, reference, note=entry.get("title", ""))]

    if part == "background":
        return [_ask("object", part, png.stem, rel,
                     (entry.get("ids") or [png.stem])[0], [0, 0, width, height],
                     None, reference, note=entry.get("title", ""))]

    if part == "map":
        return [_ask("panorama", part, png.stem, rel,
                     (entry.get("ids") or [png.stem])[0], [0, 0, width, height],
                     None, reference, note=entry.get("title", ""))]

    if part == "chr":
        return [_ask("page", part, png.stem, rel, png.stem, [0, 0, width, height],
                     None, entry.get("reference"), note=entry.get("title", ""))]

    # A part this tool was not written for: still worth a look, shown whole.
    return [_ask("object", part, png.stem, rel, (entry.get("ids") or [png.stem])[0],
                 [0, 0, width, height], None, reference, note=entry.get("title", ""))]


def render_packet_markdown(packet: dict) -> str:
    """The packet as the page a reviewing agent actually reads."""
    lines = [
        "# Review packet",
        "",
        f"Kit: `{packet['kit']}`  ",
        f"Packet sha256: `{packet['sha256']}`  ",
        f"Asks: {len(packet['asks'])}",
        "",
        "## How to answer",
        "",
    ]
    lines += [f"{i}. {rule}" for i, rule in enumerate(packet["rules"], start=1)]
    lines += [
        "",
        "Write `kit-proposals.json` in the kit folder, shaped as `answerSchema`",
        "in `kit-review.json`. Then run:",
        "",
        "```",
        "scripts/artist_ai_review.py check <kit> --proposals <kit>/kit-proposals.json",
        "```",
        "",
    ]
    by_surface = {}
    for ask in packet["asks"]:
        by_surface.setdefault((ask["part"], ask["surface"]), []).append(ask)
    current_part = None
    for (part, surface), group in by_surface.items():
        if part != current_part:
            lines += ["", f"## {part}", ""]
            current_part = part
        note = group[0].get("note", "")
        lines += [f"### `{surface}`" + (f" - {note}" if note else ""), ""]
        ref = group[0].get("reference")
        if ref:
            lines.append(f"Untouched reference: `{ref}` (open it if the sheet looks painted).")
            lines.append("")
        lines.append(group[0]["question"])
        lines.append("")
        lines.append("| ask | subject id | rect (x, y, w, h) |")
        lines.append("| --- | --- | --- |")
        for ask in group:
            lines.append(f"| `{ask['ask']}` | `{ask['subjectId']}` | "
                         f"{', '.join(str(v) for v in ask['rect'])} |")
        lines.append("")
    if packet["skipped"]:
        lines += ["## Not shown", ""]
        for entry in packet["skipped"]:
            lines.append(f"- `{entry['path']}` - {entry['why']}")
        lines.append("")
    return "\n".join(lines) + "\n"


def write_crops(packet: dict, kit: Path, out_dir: Path, min_edge=256):
    """One enlarged PNG per figure ask, for the asks a whole sheet renders small.

    This is the lever the design's first rule points at: the same figure judged
    from an 8 px thumbnail and from a 128 px render is not the same judgement.
    Nearest-neighbour, so no pixel is invented; written outside the kit, because
    a crop is a reviewing aid and not a painting surface."""
    try:
        from PIL import Image
    except ImportError:
        raise ReviewError("crops need Pillow (python3 -m pip install pillow)")
    out_dir.mkdir(parents=True, exist_ok=True)
    written = []
    for ask in packet["asks"]:
        if ask["kind"] != "figure":
            continue
        x, y, w, h = ask["rect"]
        image = Image.open(kit / ask["surface"]).convert("RGBA")
        crop = image.crop((x, y, x + w, y + h))
        factor = max(1, -(-min_edge // max(1, max(crop.size))))
        if factor > 1:
            crop = crop.resize((crop.width * factor, crop.height * factor), Image.NEAREST)
        flat = Image.new("RGBA", crop.size, (255, 255, 255, 255))
        flat.alpha_composite(crop)
        name = ask["ask"].replace("/", "_").replace("#", "-") + ".png"
        flat.convert("RGB").save(out_dir / name)
        ask["crop"] = str((out_dir / name))
        written.append(name)
    return written


# ---- checking the answers --------------------------------------------------

def check_proposals(packet: dict, doc) -> dict:
    """Validate a filled-in proposal file against the packet it answers.

    Rejects anything a reviewer could not check in seconds: an ask that does
    not exist, an answer citing a file it was not shown, a rectangle outside
    the box it was asked about, a name with no subject behind it, a subject the
    file never declares."""
    report = {"accepted": [], "abstained": [], "defects": []}

    def defect(where, why):
        report["defects"].append({"where": where, "why": why})

    if not isinstance(doc, dict):
        defect("<file>", "the proposal file is not a JSON object")
        return report
    if doc.get("version") != SCHEMA_VERSION:
        defect("version", f"expected {SCHEMA_VERSION}, got {doc.get('version')!r}")
    if doc.get("packet") and doc["packet"] != packet["sha256"]:
        defect("packet", "answers a different packet than the one in the kit - "
                         "regenerate the packet or re-review")
    subjects = doc.get("subjects")
    if not isinstance(subjects, dict):
        subjects = {}
        defect("subjects", "missing: every subject key a proposal uses must be "
                           "declared here, in one line of plain words")
    asks = {a["ask"]: a for a in packet["asks"]}
    seen = set()
    entries = doc.get("proposals")
    if not isinstance(entries, list):
        defect("proposals", "missing or not a list")
        entries = []
    for index, entry in enumerate(entries):
        where = f"proposals[{index}]"
        if not isinstance(entry, dict):
            defect(where, "not an object")
            continue
        ask_id = entry.get("ask")
        where = f"{where} ({ask_id})"
        if ask_id not in asks:
            defect(where, "no such ask in the packet")
            continue
        if ask_id in seen:
            defect(where, "answered twice")
            continue
        seen.add(ask_id)
        ask = asks[ask_id]
        if entry.get("saw") != ask["surface"]:
            defect(where, f"cites {entry.get('saw')!r}, was shown {ask['surface']!r}")
            continue
        rect = entry.get("rect")
        if not _rect_inside(rect, ask["rect"]):
            defect(where, f"rect {rect!r} is not inside the box it was asked about "
                          f"{ask['rect']!r}")
            continue
        if entry.get("abstain"):
            if not str(entry.get("why") or "").strip():
                defect(where, "an abstention still has to say what stopped you")
                continue
            report["abstained"].append(ask_id)
            continue
        name = str(entry.get("name") or "").strip()
        subject = str(entry.get("subject") or "").strip()
        if not name:
            defect(where, "no name and no abstention")
            continue
        if name == ask["subjectId"]:
            defect(where, "the name only repeats the pack's own id")
            continue
        if not subject:
            defect(where, "a name with no subject behind it")
            continue
        if subject not in subjects:
            defect(where, f"subject {subject!r} is not declared in subjects")
            continue
        if entry.get("confidence") not in CONFIDENCE:
            defect(where, f"confidence must be one of {', '.join(CONFIDENCE)}")
            continue
        if not str(entry.get("why") or "").strip():
            defect(where, "no `why`: a proposal that does not say what it saw "
                          "cannot be checked")
            continue
        report["accepted"].append(ask_id)
    report["unanswered"] = [a for a in asks if a not in seen]
    return report


def _rect_inside(rect, outer):
    if not (isinstance(rect, list) and len(rect) == 4):
        return False
    try:
        x, y, w, h = (int(v) for v in rect)
    except (TypeError, ValueError):
        return False
    ox, oy, ow, oh = outer
    return w > 0 and h > 0 and x >= ox and y >= oy and x + w <= ox + ow and y + h <= oy + oh


# ---- ground truth ----------------------------------------------------------

def read_reference_tiles(hires: Path) -> dict:
    """`{(tile, palette): {image, ...}}` from a hand-made HD pack's `hires.txt`.

    Only the manifest is read. The pack's own PNGs are never opened, copied or
    sent anywhere - what is wanted from it is the one thing a generator may not
    produce, the names a human who knew the game gave its figures."""
    images, rows = [], {}
    for line in hires.read_text(errors="replace").splitlines():
        text = line.strip()
        if text.startswith("<img>"):
            images.append(text[len("<img>"):])
            continue
        if text.startswith("#"):
            continue                            # a commented-out rule is not evidence
        match = re.match(r"^(?:\[[^\]]*\])?<tile>(.*)$", text)
        if not match:
            continue
        fields = match.group(1).split(",")
        if len(fields) < 3:
            continue
        try:
            index = int(fields[0])
        except ValueError:
            continue
        if 0 <= index < len(images):
            rows.setdefault((fields[1], fields[2]), set()).add(images[index])
    return rows


def build_truth(kit: Path, hires: Path, subject_map: dict, dominance=0.6) -> dict:
    """Label every figure and object ask from a reference pack's file names.

    A kit tile carries the pattern the recorder saw and, where it was mirrored,
    the unmirrored `source` it came from; both are looked up against the
    reference pack's `(tile, palette)` keys, exact palette first. A tile that
    lands in files of two different subjects says nothing and is dropped: what
    labels a box is the tiles that belong to one subject and no other.

    A box whose exclusive tiles do not give one subject a `dominance` majority
    is labelled `mixed` - not unknown. The kit really does put two overlapping
    figures in one box sometimes, and a reviewer who says so is right."""
    rows = read_reference_tiles(hires)
    # `_`-prefixed keys are the map's own prose, not subjects.
    of_file = {f: subject for subject, files in subject_map.items()
               if not subject.startswith("_") for f in files}

    def subjects_of(tile):
        """Exact `(pattern, palette)` keys only.

        Matching on the pattern alone would be a guess, and a guess has no
        business in ground truth: Contra's grey rock faces share patterns with
        the light tiles of the player's sheet, and a palette-blind lookup
        cheerfully labels a mountain `player`. The kit records the unmirrored
        `source` a mirrored tile came from, and the reference pack keys both
        orientations, so both are tried - with the palette attached."""
        names = set()
        for key in (tile.get("tile"), tile.get("source")):
            if key:
                names |= rows.get((key, tile.get("palette")), set())
        return {of_file[n] for n in names if n in of_file}

    labels = {}
    fragments = load_fragments(kit)
    for part in ("sprites", "background"):
        fragment = fragments.get(part)
        if not fragment:
            continue
        for entry in fragment.get("files") or []:
            rel = entry.get("path") or ""
            sidecar = kit / rel.replace(".png", ".json")
            if not sidecar.exists():
                continue
            doc = _load_json(sidecar)
            stem = Path(rel).stem
            if part == "sprites" and entry.get("unit") == "grid":
                kind = "figure"
                groups = [(f"{stem}#{pose}", cells) for pose, cells, _r in figure_boxes(doc)]
            else:
                kind = "object"
                groups = [(stem, doc.get("cells") or [])]
            for ask_id, cells in groups:
                tally, ambiguous, unknown = Counter(), 0, 0
                for cell in cells:
                    for tile in cell.get("tiles") or []:
                        found = subjects_of(tile)
                        if len(found) == 1:
                            tally[next(iter(found))] += 1
                        elif found:
                            ambiguous += 1
                        else:
                            unknown += 1
                total = sum(tally.values())
                top, count = (tally.most_common(1) or [(None, 0)])[0]
                if not total:
                    subject = None
                elif count / total >= dominance:
                    subject = top
                else:
                    subject = "mixed"
                labels[ask_id] = {
                    "kind": kind,
                    "subject": subject,
                    "exclusiveTiles": dict(tally),
                    "ambiguousTiles": ambiguous,
                    "unlabelledTiles": unknown,
                }
    return {
        "version": SCHEMA_VERSION,
        "kit": str(kit),
        "reference": str(hires),
        "dominance": dominance,
        "note": ("Labels are the reference pack's own file names, grouped by the "
                 "subject map this run was given. Nothing here is inferred from "
                 "the kit's pixels."),
        "labels": labels,
    }


# ---- scoring ---------------------------------------------------------------

def score(proposals: dict, truth: dict, alias=None, kinds=None) -> dict:
    """Measure a proposal file against ground truth, in four counts that are
    never collapsed into one.

    `correct`, `wrong`, `abstained` - and `confidently_wrong`, a subset of
    `wrong`, reported separately because it is the only bucket that can poison
    a kit. An abstention is never scored as wrong: not knowing is a valid
    answer and the point of measuring is to find out whether the reviewer knows
    when it does not know."""
    alias = alias or {}
    all_labels = truth.get("labels") or {}
    labels = {k: v for k, v in all_labels.items()
              if not kinds or v.get("kind") in kinds}
    buckets = {"correct": [], "wrong": [], "abstained": [], "unscored": []}
    confidently_wrong = []
    for entry in proposals.get("proposals") or []:
        ask_id = entry.get("ask")
        label = labels.get(ask_id)
        if label is None:
            # The truth file says nothing about this ask. Either it held a
            # label of a kind this run was told not to score, or it never had
            # one at all - a kind the reference pack cannot label (Contra's
            # backgrounds are replaced page by page, so its named files carry
            # almost no scenery evidence). Neither is a measurement, and an
            # abstention on an ask with no ground truth behind it must not
            # reach `abstained`: it would read as caution the reviewer never
            # exercised, and inflate the one count that is supposed to mean
            # "declined a hard case it could have got wrong".
            if ask_id in all_labels:
                continue                    # a kind this run was told not to score
            buckets["unscored"].append({"ask": ask_id, "said": entry.get("subject"),
                                        "reason": "no label of this kind"})
            continue
        if entry.get("abstain"):
            buckets["abstained"].append({"ask": ask_id,
                                         "truth": label.get("subject"),
                                         "why": entry.get("why", "")})
            continue
        if label.get("subject") is None:
            buckets["unscored"].append({"ask": ask_id, "said": entry.get("subject"),
                                        "reason": "no subject reached dominance"})
            continue
        expected = label["subject"]
        said = alias.get(entry.get("subject"), entry.get("subject"))
        if expected == "mixed":
            ok = bool(entry.get("multiple"))
        else:
            ok = (said == expected) and not entry.get("multiple")
        record = {"ask": ask_id, "said": said, "expected": expected,
                  "name": entry.get("name", ""), "confidence": entry.get("confidence"),
                  "why": entry.get("why", ""), "saw": entry.get("saw"),
                  "rect": entry.get("rect"), "evidence": label.get("exclusiveTiles")}
        if ok:
            buckets["correct"].append(record)
        else:
            buckets["wrong"].append(record)
            if entry.get("confidence") == "high":
                confidently_wrong.append(record)
    scorable = [a for a, label in labels.items() if label.get("subject") is not None]
    return {
        "counts": {
            "correct": len(buckets["correct"]),
            "wrong": len(buckets["wrong"]),
            "abstained": len(buckets["abstained"]),
            "confidently_wrong": len(confidently_wrong),
            "unscored": len(buckets["unscored"]),
            "scorable_asks": len(scorable),
        },
        "confidentlyWrong": confidently_wrong,
        **buckets,
    }


# ---- promotion -------------------------------------------------------------

def promote(packet: dict, proposals: dict, accepted, ) -> dict:
    """Turn the proposals a human ticked into the `--names` file the generators
    read. Only the ticked ones: there is no flag that promotes everything, and
    an abstention promotes to nothing."""
    accepted = set(accepted)
    asks = {a["ask"]: a for a in packet["asks"]}
    unknown = sorted(accepted - set(asks))
    if unknown:
        raise ReviewError("accepted ask(s) the packet does not contain: "
                          + ", ".join(unknown))
    subjects, poses = {}, {}
    for entry in proposals.get("proposals") or []:
        ask_id = entry.get("ask")
        if ask_id not in accepted or entry.get("abstain"):
            continue
        ask = asks[ask_id]
        if ask["kind"] != "figure":
            continue
        subject = entry.get("subject")
        poses[ask["subjectId"]] = {"name": entry.get("name"), "subject": subject}
        if subject and subject not in subjects:
            subjects[subject] = (proposals.get("subjects") or {}).get(subject, "")
    return {
        "_comment": ("Captions promoted from kit-proposals.json by "
                     "scripts/artist_ai_review.py, one ask at a time and only where a "
                     "human accepted the proposal. A name here is a caption for a "
                     "file, never evidence: the pack's own ids stay authoritative."),
        "subjects": subjects,
        "poses": poses,
    }


# ---- command line ----------------------------------------------------------

def _packet_for(kit: Path, out: Path):
    path = out / "kit-review.json"
    return _load_json(path) if path.exists() else build_packet(kit)


def main(argv=None):
    ap = argparse.ArgumentParser(description="AI review protocol for an artist kit")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("packet", help="write the review packet for a kit")
    p.add_argument("kit", type=Path)
    p.add_argument("--out", type=Path, default=None, help="where to write (default: the kit)")
    p.add_argument("--crops", type=Path, default=None,
                   help="also write one enlarged PNG per figure ask, into this folder")
    p.add_argument("--min-edge", type=int, default=256)

    p = sub.add_parser("check", help="validate and ingest a filled-in proposal file")
    p.add_argument("kit", type=Path)
    p.add_argument("--proposals", type=Path, required=True)
    p.add_argument("--out", type=Path, default=None)

    p = sub.add_parser("truth", help="label a kit from a hand-made reference pack")
    p.add_argument("kit", type=Path)
    p.add_argument("--reference-pack", type=Path, required=True,
                   help="folder holding the reference pack's hires.txt")
    p.add_argument("--subjects", type=Path, required=True,
                   help='JSON: {"<subject>": ["File.png", ...]}')
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--dominance", type=float, default=0.6)

    p = sub.add_parser("score", help="measure proposals against ground truth")
    p.add_argument("--proposals", type=Path, required=True)
    p.add_argument("--truth", type=Path, required=True)
    p.add_argument("--alias", type=Path, default=None,
                   help='JSON: {"<reviewer subject>": "<truth subject>"}')
    p.add_argument("--kind", action="append", default=None,
                   help="only score asks of this kind (repeatable); default: all")
    p.add_argument("--out", type=Path, default=None)

    p = sub.add_parser("promote", help="turn accepted proposals into a --names file")
    p.add_argument("kit", type=Path)
    p.add_argument("--proposals", type=Path, required=True)
    p.add_argument("--accepted", type=Path, required=True,
                   help="one accepted ask id per line; there is no promote-everything flag")
    p.add_argument("--out", type=Path, required=True)

    args = ap.parse_args(argv)
    try:
        return _run(args)
    except ReviewError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


def _run(args):
    if args.cmd == "packet":
        out = args.out or args.kit
        out.mkdir(parents=True, exist_ok=True)
        packet = build_packet(args.kit)
        if args.crops:
            names = write_crops(packet, args.kit, args.crops, args.min_edge)
            print(f"{args.crops}: {len(names)} crop(s)")
        (out / "kit-review.json").write_text(json.dumps(packet, indent=2) + "\n")
        (out / "kit-review.md").write_text(render_packet_markdown(packet))
        kinds = Counter(a["kind"] for a in packet["asks"])
        print(f"{out}/kit-review.json: {len(packet['asks'])} ask(s) "
              f"({', '.join(f'{v} {k}' for k, v in kinds.most_common())}), "
              f"{len(packet['skipped'])} skipped")
        return 0

    if args.cmd == "check":
        packet = _packet_for(args.kit, args.out or args.kit)
        report = check_proposals(packet, _load_json(args.proposals))
        print(f"{args.proposals}: {len(report['accepted'])} accepted, "
              f"{len(report['abstained'])} abstained, "
              f"{len(report['defects'])} defect(s), "
              f"{len(report['unanswered'])} unanswered")
        for defect in report["defects"]:
            print(f"  defect {defect['where']}: {defect['why']}")
        return 1 if report["defects"] else 0

    if args.cmd == "truth":
        hires = args.reference_pack / "hires.txt"
        if not hires.exists():
            raise ReviewError(f"{hires}: no hires.txt in the reference pack")
        subject_map = _load_json(args.subjects)
        truth = build_truth(args.kit, hires, subject_map, args.dominance)
        args.out.write_text(json.dumps(truth, indent=2) + "\n")
        counts = Counter(v["subject"] for v in truth["labels"].values())
        print(f"{args.out}: {len(truth['labels'])} labelled ask(s) "
              f"({', '.join(f'{k} {v}' for k, v in counts.most_common())})")
        return 0

    if args.cmd == "score":
        alias = _load_json(args.alias) if args.alias else {}
        result = score(_load_json(args.proposals), _load_json(args.truth), alias,
                       kinds=set(args.kind) if args.kind else None)
        if args.out:
            args.out.write_text(json.dumps(result, indent=2) + "\n")
        counts = result["counts"]
        print(f"correct           {counts['correct']}")
        print(f"wrong             {counts['wrong']}")
        print(f"abstained         {counts['abstained']}")
        print(f"confidently wrong {counts['confidently_wrong']}   "
              "(a subset of wrong; the only bucket that can poison a kit)")
        print(f"unscored          {counts['unscored']}   "
              "(no ground truth for that ask)")
        for record in result["confidentlyWrong"]:
            print(f"  {record['ask']}: said {record['said']!r} "
                  f"({record['name']!r}), truth {record['expected']!r}")
        return 0

    if args.cmd == "promote":
        packet = _packet_for(args.kit, args.kit)
        accepted = [line.strip() for line in args.accepted.read_text().splitlines()
                    if line.strip() and not line.startswith("#")]
        names = promote(packet, _load_json(args.proposals), accepted)
        args.out.write_text(json.dumps(names, indent=2) + "\n")
        print(f"{args.out}: {len(names['poses'])} caption(s), "
              f"{len(names['subjects'])} subject(s) from {len(accepted)} accepted ask(s)")
        return 0

    raise ReviewError(f"unknown command {args.cmd}")


if __name__ == "__main__":
    raise SystemExit(main())
