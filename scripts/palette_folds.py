"""Palette folding: which recorded palettes of one pattern are the same picture.

Moved out of `artist_chr_kit.py` by F14.9 (ADR-0230) so the pattern pages and
the recorder's sheets share one definition of a fold. The recorder's port is
`MesenSheets::ClassifyPaletteRelation` in `Core/NES/HdPacks/SheetColourways.h`;
`docs/specs/golden/sheets/palette-relation-cases.txt` is checked by both
`scripts/core_unit_tests` and `scripts/test_palette_folds.py`, so the two
cannot drift.

Python 3, standard library only.
"""

from __future__ import annotations

import collections
import json
import math
from pathlib import Path

# The 2C02 table of Core/NES/NesDefaultVideoFilter.cpp, ARGB. Only ever used for
# a palette index that no cell of the pack exhibits; everything else is read off
# the recorded pages, which keeps a custom palette working for free.
DEFAULT_PALETTE_ARGB = [
    0xFF666666, 0xFF002A88, 0xFF1412A7, 0xFF3B00A4, 0xFF5C007E, 0xFF6E0040,
    0xFF6C0600, 0xFF561D00, 0xFF333500, 0xFF0B4800, 0xFF005200, 0xFF004F08,
    0xFF00404D, 0xFF000000, 0xFF000000, 0xFF000000, 0xFFADADAD, 0xFF155FD9,
    0xFF4240FF, 0xFF7527FE, 0xFFA01ACC, 0xFFB71E7B, 0xFFB53120, 0xFF994E00,
    0xFF6B6D00, 0xFF388700, 0xFF0C9300, 0xFF008F32, 0xFF007C8D, 0xFF000000,
    0xFF000000, 0xFF000000, 0xFFFFFEFF, 0xFF64B0FF, 0xFF9290FF, 0xFFC676FF,
    0xFFF36AFF, 0xFFFE6ECC, 0xFFFE8170, 0xFFEA9E22, 0xFFBCBE00, 0xFF88D800,
    0xFF5CE430, 0xFF45E082, 0xFF48CDDE, 0xFF4F4F4F, 0xFF000000, 0xFF000000,
    0xFFFFFEFF, 0xFFC0DFFF, 0xFFD3D2FF, 0xFFE8C8FF, 0xFFFBC2FF, 0xFFFEC4EA,
    0xFFFECCC5, 0xFFF7D8A5, 0xFFE4E594, 0xFFCFEF96, 0xFFBDF4AB, 0xFFB3F3CC,
    0xFFB5EBF2, 0xFFB8B8B8, 0xFF000000, 0xFF000000,
]



# --- palette folding (F9.27 follow-up) --------------------------------------

# A recorded pack keys a cell by (pattern, palette), so one drawing comes back
# once per palette the run saw it wearing. On the TAS Zelda recording that is
# 4712 cells for 1642 distinct patterns, because a screen fade gives every tile
# on screen a key per step of the fade.
#
# `HdPackLoader` has carried a per-`<tile>` **Brightness** since version 105
# (`HdPackLoader.cpp:513`, applied by `HdNesPack::AdjustBrightness`), and the
# builder has never written anything but 255 (`HdPackBuilder.cpp:337/391/464`).
# So the renderer can already reconstruct a fade step from one painted cell,
# and this module decides which cells that is true of.
#
# THE RULE, read off the NES palette and never off the picture:
#
#   NES colour byte c: row = c >> 4, hue = c & 0x0F. The four rows of one hue
#   column ARE the console's brightness ramp for that colour
#   ($2C -> $1C -> $0C -> $0F). Ten indices render pure black in the 2C02 table
#   ($0D-$0F, $1D-$1F, $2E, $2F, $3E, $3F); a black entry carries no hue and is
#   a wildcard.
#
#   Only the palette indices the tile ACTUALLY PAINTS are compared - a pattern
#   that paints colours 0 and 3 does not care what 1 and 2 hold.
#
#   INERT  - the two palettes render the pattern to identical RGB. No judgement
#            at all; the cells are the same picture.
#   FADE   - every painted entry keeps its hue and none gets brighter, i.e. the
#            game moved those colours down their own ramps.
#   Neither - a painted entry changed hue. A different picture. Collapsing a
#            green enemy onto a red one would destroy evidence (ADR-0183 3),
#            so it is never folded, however close the two happen to render.
#
# The rule is a precondition, not the decision. `AdjustBrightness` is a single
# RGB multiplier, and the one thing a multiplier can never do is change hue, so
# each candidate is also measured: the least-squares multiplier that takes the
# base to the variant, and the largest hue angle between them. Anything past
# HUE_DRIFT_GATE_DEG is left as its own cell. Both numbers are written into the
# cell's sidecar entry, and the per-page distribution into `fold.driftHistogram`,
# so the gate is retunable from the data a kit already carries rather than from
# another recording run.
#
# Measured on the corpus (2026-09-14), fraction of the rows-over-patterns gap
# this removes: Contra 77 %, Mega Man 3 58 %, Gauntlet 57 %, Ninja Gaiden 53 %,
# Zelda TAS 44 %, Castlevania 17 %. Zelda TAS: 4712 cells -> 3372, and the
# deepest CHR slot goes from 35 variants to 16.
#
# What this does NOT do is collapse a pattern to one cell. 1642 is Zelda's
# *pattern* count, not its picture count: its most-repeated patterns carry ~7
# hue families (grey, olive $x8, green $xB, cyan $xC, red $x6, blue $x2, brown
# $x7), each a 3-5 step ramp. 30 keys become 7 cells, not 1.

HUE_DRIFT_GATE_DEG = 25.0


def _nes_rgb(c):
    v = DEFAULT_PALETTE_ARGB[c & 0x3F]
    return ((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF)


def _is_black(c):
    return _nes_rgb(c) == (0, 0, 0)


def _hue(c):
    return c & 0x0F


def _level(c):
    """0 for black, else the palette row + 1 - the rung of the hue's ramp."""
    return 0 if _is_black(c) else (c >> 4) + 1


def _luma(c):
    r, g, b = _nes_rgb(c)
    return 0.299 * r + 0.587 * g + 0.114 * b


def palette_entries(palette: str):
    """`"0F0B1B2B"` -> the four NES colour indices, colour 0 first."""
    v = int(palette, 16)
    return [(v >> ((3 - k) * 8)) & 0x3F for k in range(4)]


def painted_indices(tile_data: str):
    """Which of the four palette indices the 8x8 pattern actually paints.

    `tile_data` is the 32 hex characters a CHR RAM `<tile>` row carries. A CHR
    ROM row carries only an index, and then the caller has to assume all four -
    which is the conservative direction: more entries have to agree on hue, so
    fewer cells fold."""
    d = [int(tile_data[i * 2:i * 2 + 2], 16) for i in range(16)]
    used = set()
    for i in range(8):
        lo, hi = d[i], d[i + 8]
        for j in range(8):
            used.add(((lo >> (7 - j)) & 1) | (((hi >> (7 - j)) & 1) << 1))
    return sorted(used)


def fade_related(base, other, used) -> bool:
    """`other` is `base` further down the same ramps: no painted entry changed
    hue, none got brighter, and at least one moved."""
    moved = False
    for k in used:
        a, b = base[k], other[k]
        if _is_black(a) and _is_black(b):
            continue
        if _is_black(a) or _is_black(b):
            moved = True
            continue
        if _hue(a) != _hue(b):
            return False
        if _level(b) > _level(a):
            return False
        if _level(b) < _level(a):
            moved = True
    return moved


def fold_measure(base, other, used):
    """(brightness, max hue drift in degrees) for folding `other` onto `base`.

    `brightness` is the least-squares single multiplier, in the 0..1 units the
    `<tile>` column uses (1 = the loader's 255). The drift is the largest angle
    between a painted entry's two RGB vectors - the part of the residual a
    multiplier can never remove. Black entries carry no direction and are
    skipped."""
    num = den = 0.0
    for k in used:
        a, b = _nes_rgb(base[k]), _nes_rgb(other[k])
        for i in range(3):
            num += a[i] * b[i]
            den += a[i] * a[i]
    brightness = 0.0 if den == 0 else num / den
    drift = 0.0
    for k in used:
        a, b = _nes_rgb(base[k]), _nes_rgb(other[k])
        na = math.sqrt(sum(x * x for x in a))
        nb = math.sqrt(sum(x * x for x in b))
        if na == 0 or nb == 0:
            continue
        cos = sum(x * y for x, y in zip(a, b)) / (na * nb)
        drift = max(drift, math.degrees(math.acos(max(-1.0, min(1.0, cos)))))
    return max(0.0, min(4.0, brightness)), drift


def palette_relation(tile_data: str, cell_palette: str, other_palette: str,
                     gate: float = HUE_DRIFT_GATE_DEG):
    """How `other_palette` relates to the palette a sheet cell carries, for
    this pattern: `(relation, brightness, drift)`.

    `relation` is "inert" (same RGB on every painted entry), "fade" (the cell's
    palette further down the same ramps), "brighter" (the cell's palette is a
    fade of `other`, so rebuilding `other` needs a Brightness above 1) or
    "colourway" (a painted entry changes hue, or the residual fails the drift
    gate). The first three are folds; a colourway is another picture (ADR-0230
    Decision item 1, ADR-0183 3). `brightness` is always measured from the
    cell's palette to `other`. The F14.4 measurement's pairwise test, and what
    `MesenSheets::ClassifyPaletteRelation` ports."""
    used = painted_indices(tile_data) or [0]
    base, other = palette_entries(cell_palette), palette_entries(other_palette)
    if all(_nes_rgb(base[k]) == _nes_rgb(other[k]) for k in used):
        return "inert", 1.0, 0.0
    brightness, drift = fold_measure(base, other, used)
    if fade_related(base, other, used):
        return ("fade" if drift <= gate else "colourway"), brightness, drift
    if fade_related(other, base, used):
        return ("brighter" if drift <= gate else "colourway"), brightness, drift
    return "colourway", brightness, drift


FOLD_BRIGHTNESS_MAX = 4.0  # fold_measure's clamp; the loader takes values above 1


def fold_brightness_text(brightness) -> str:
    """The Brightness column as a fold rule carries it: four decimals at most,
    trailing zeroes dropped - `MesenSheets::FoldBrightnessText`, so the text
    the recorder proved exact is the text mep_build.py writes."""
    text = f"{float(brightness):.4f}".rstrip("0").rstrip(".")
    return text or "0"


def entry_folds(entry):
    """ADR-0230 Decision item 2: a sidecar tile entry's `folds`, as
    `(folds, problems)`. `folds` is `[(PALETTE, brightness text)]` for every
    well-formed item; `problems` names each item mep_lint must reject. An
    entry without the field - every sidecar before F14.9 - has neither.

    A fold is another palette the recording drew this shape in that the cell
    reproduces exactly at one Brightness (the #448 refinement), so it names a
    palette other than the entry's own, at most once, with a Brightness in
    [0, 4]."""
    raw = entry.get("folds") if isinstance(entry, dict) else None
    if raw is None:
        return [], []
    if not isinstance(raw, list):
        return [], ["`folds` is not a list"]
    own = str(entry.get("palette") or "").strip().upper()
    folds, problems, seen = [], [], set()
    for i, item in enumerate(raw):
        pal = str(item.get("palette") or "").strip().upper() if isinstance(item, dict) else ""
        bright = item.get("brightness") if isinstance(item, dict) else None
        if len(pal) != 8 or any(c not in "0123456789ABCDEF" for c in pal):
            problems.append(f"folds[{i}] has no 8-hex-digit palette")
        elif isinstance(bright, bool) or not isinstance(bright, (int, float)) or not 0 <= bright <= FOLD_BRIGHTNESS_MAX:
            problems.append(f"folds[{i}] brightness must be a number in [0, {FOLD_BRIGHTNESS_MAX:g}]")
        elif pal == own:
            problems.append(f"folds[{i}] repeats the entry's own palette {pal}")
        elif pal in seen:
            problems.append(f"folds[{i}] lists palette {pal} twice")
        else:
            seen.add(pal)
            folds.append((pal, fold_brightness_text(bright)))
    return folds, problems


def sheet_fold_anchors(sheets_dir: Path) -> dict:
    """`{tile data: [palettes]}` - the palettes the recorder's sheet cells
    carry for each pattern (ADR-0230 Decision item 3). A sheet entry is keyed
    by its `source` when it has one (ADR-0178), which is the data a pattern
    page's row carries. Empty for a pack without sheets, and for CHR ROM
    entries, whose pages key a row by index."""
    anchors = collections.defaultdict(list)
    for path in sorted(Path(sheets_dir).glob("*.json")) if Path(sheets_dir).is_dir() else []:
        try:
            doc = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        cells = doc.get("cells") if isinstance(doc, dict) else None
        for cell in cells if isinstance(cells, list) else []:
            for entry in (cell.get("tiles") or []) if isinstance(cell, dict) else []:
                if not isinstance(entry, dict):
                    continue
                data = str(entry.get("source") or entry.get("tile") or "").upper()
                pal = str(entry.get("palette") or "").upper()
                if len(data) == 32 and len(pal) == 8 and pal not in anchors[data]:
                    anchors[data].append(pal)
    return dict(anchors)


def compute_folds(pages, gate: float = HUE_DRIFT_GATE_DEG, anchors=None):
    """Fold every page's palette variants of one pattern onto one painted cell.

    ADR-0230 Decision item 3 (F14.9): when the recorder's sheets carry a cell
    for the pattern (`anchors`, from `sheet_fold_anchors`), that cell's palette
    is the base, and a step is measured against it - in either direction, with
    a Brightness above 1 for a brighter step - so the pattern pages and the
    sheets agree on what a fold is. Measuring against the brightest step
    instead refused 83 of Zelda's fade steps (F14.4). A pattern no sheet
    carries keeps the brightest step as its base.

    Pack-wide, not per bank: a pack whose CHR bank hashes are all zero has its
    pages regrouped structurally (`_regroup_without_hashes`), so the variants of
    one pattern routinely sit in different Bank objects. The key this folds is
    (pattern, palette), which is global.

    Returns `(folded, bases, stats)`:
      folded[(page name, slot)] = {"kind", "brightness", "drift", "base*"}
      bases[(page name, slot)]  = [that cell's folded entries]
    """
    groups = collections.defaultdict(list)
    for page in pages:
        for slot, row in page.rows.items():
            # The bootstrap's ROM export (a `defaultTile=Y` row, #449) is not a
            # picture the run drew, so it neither folds nor is folded onto.
            if getattr(row, "exported", False):
                continue
            ident = row.tile_data or f"#{page.index_of_slot(slot)}"
            groups[ident].append((page, slot, row))

    folded, bases = {}, collections.defaultdict(list)
    stats = {"inert": 0, "fade": 0, "kept": 0, "refused": 0,
             "driftHistogram": collections.Counter()}

    for ident, members in groups.items():
        data = members[0][2].tile_data
        used = painted_indices(data) if data else [0, 1, 2, 3]
        if not used:
            used = [0]
        # One member per palette, brightest first; a member of the fullest page
        # wins a tie, so the cell an artist opens first tends to be the base.
        by_palette = {}
        for page, slot, row in members:
            by_palette.setdefault(row.palette, (page, slot, row))
        order = sorted(
            by_palette.values(),
            key=lambda m: (-sum(_luma(palette_entries(m[2].palette)[k]) for k in used),
                           -len(m[0].rows), m[0].name, m[1]))
        cells = [p for p in (anchors or {}).get(ident, []) if p in by_palette]
        order = [by_palette[p] for p in cells] + [m for m in order if m[2].palette not in cells]
        if len(order) < 2:
            stats["kept"] += 1
            continue

        # INERT first: palettes that render this pattern to identical RGB are
        # the same picture, and folding them is not a judgement.
        reps, inert_of = [], collections.defaultdict(list)
        seen = {}
        for m in order:
            sig = tuple(_nes_rgb(palette_entries(m[2].palette)[k]) for k in used)
            at = seen.get(sig)
            if at is None:
                seen[sig] = len(reps)
                reps.append(m)
            else:
                inert_of[at].append(m)

        # FADE: greedy from the sheet cells' palettes, then the brightest.
        taken = [False] * len(reps)
        for i, base in enumerate(reps):
            if taken[i]:
                continue
            taken[i] = True
            stats["kept"] += 1
            bkey = (base[0].name, base[1])
            base_pal = palette_entries(base[2].palette)

            def _fold(member, kind, brightness, drift):
                entry = {"kind": kind, "palette": member[2].palette,
                         "brightness": round(brightness, 4), "drift": round(drift, 1),
                         "basePage": base[0].name, "baseSlot": base[1],
                         "basePalette": base[2].palette}
                folded[(member[0].name, member[1])] = entry
                bases[bkey].append(entry)
                stats[kind] += 1
                stats["driftHistogram"][int(drift) // 5 * 5] += 1

            for m in inert_of[i]:
                _fold(m, "inert", 1.0, 0.0)
            for j in range(i + 1, len(reps)):
                if taken[j]:
                    continue
                other = reps[j]
                other_pal = palette_entries(other[2].palette)
                if not (fade_related(base_pal, other_pal, used) or
                        (base[2].palette in cells and fade_related(other_pal, base_pal, used))):
                    continue
                brightness, drift = fold_measure(base_pal, other_pal, used)
                if drift > gate:
                    # Same ramps, but too much of the residual is hue for a
                    # multiplier to carry. Left as its own cell, and counted.
                    stats["refused"] += 1
                    continue
                taken[j] = True
                _fold(other, "fade", brightness, drift)
                for m in inert_of[j]:
                    _fold(m, "inert", brightness, drift)
    return folded, bases, stats
