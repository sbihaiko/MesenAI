"""mep_capture_scan — which live captured screens draw a painted tile key (#422).

`mep_build.py build` warns when a painted key is also drawn by a
`<background>` capture (#338): on the frames that capture was frozen for it
wins over every `<tile>` (ADR-0050/ADR-0156), so the paint never shows there.
The recorder's own provenance for that, `screens[]` in `adjacency.json`, is
written only for a *screen-resident* node — one no sheet shows (ADR-0156 §1,
ADR-0166). A key that is on a sheet **and** on a capture carries none, and
that is exactly the key a reader pastes with `mep_add_cell.py`: the F14.2
cold read found the warning silent on 10 of 27 runs, and naming only
`screens[0]` of the first resident node named a capture other than the one
covering the frame on Ice Climber and Pac-Man.

So the capture itself is read. `HdPackBuilder::CaptureScreen` writes
`backgrounds/screenNNN.orig.png` as the frame's background plane, nearest-
upscaled by the pack scale and drawn with the 2C02 palette; a key's tile
bitmap under its palette is looked up among that plane's 8x8 cells. Their
columns start at the capture's fine x scroll, which its `tileAtPosition`
probes carry (`x = fineX + 8 * col`; a capture with no probe in the key
source is read at x phase 0); the row offset is searched, all eight of it.

Standard library only, like the rest of the tree's pack tools. The PNG
decoder is `mep_build`'s, passed in, so the two never read a file differently.
"""

import re
from pathlib import Path

# `Core/NES/NesDefaultVideoFilter.cpp`, the 2C02 row: what the recorder draws
# a capture with (`scripts/headless_record` seeds the same table).
_RGB_2C02 = tuple(int(v, 16) for v in (
    "666666 002A88 1412A7 3B00A4 5C007E 6E0040 6C0600 561D00 333500 0B4800 005200 004F08 00404D 000000 000000 000000 "
    "ADADAD 155FD9 4240FF 7527FE A01ACC B71E7B B53120 994E00 6B6D00 388700 0C9300 008F32 007C8D 000000 000000 000000 "
    "FFFEFF 64B0FF 9290FF C676FF F36AFF FE6ECC FE8170 EA9E22 BCBE00 88D800 5CE430 45E082 48CDDE 4F4F4F 000000 000000 "
    "FFFEFF C0DFFF D3D2FF E8C8FF FBC2FF FEC4EA FECCC5 F7D8A5 E4E594 CFEF96 BDF4AB B3F3CC B5EBF2 B8B8B8 000000 000000"
).split())

_PROBE_RE = re.compile(r"^<condition>(screen\d+)_\w+,tileAtPosition,(\d+),(\d+),", re.IGNORECASE)


def tile_rgb(bitmap: str, palette: str):
    """The 64 row-major 0xRRGGBB pixels a 2bpp tile draws under a palette, or
    None for a key that is not a 32-hex bitmap and an 8-hex palette."""
    try:
        data = bytes.fromhex(bitmap)
        pal = [_RGB_2C02[int(palette[i:i + 2], 16) & 0x3F] for i in range(0, 8, 2)]
    except (TypeError, ValueError):
        return None
    if len(data) != 16 or len(palette) != 8:
        return None
    return tuple(pal[((data[r] >> (7 - c)) & 1) | (((data[r + 8] >> (7 - c)) & 1) << 1)]
                 for r in range(8) for c in range(8))


def _phases(source_lines):
    """`{capture: x phase}` from each capture's first probe."""
    out = {}
    for line in source_lines:
        m = _PROBE_RE.match(line.strip())
        if m:
            out.setdefault(m.group(1).lower(), int(m.group(2)) % 8)
    return out


def _cells(bmp, phase):
    """Every whole 8x8 cell of a capture's 256x240 plane whose column starts
    at x phase `phase`, at any of the 8 row offsets, as the same 64-pixel
    tuples `tile_rgb` builds. Empty for a PNG of another shape. The row offset
    is searched rather than read: a probe's y names the scanline it samples,
    not where its tile starts (Ice Climber draws its rows at y = 8k + 1)."""
    if bmp is None or bmp.width % 256 or bmp.width // 256 < 1 or bmp.channels < 3:
        return set()
    n = bmp.width // 256
    if bmp.height != 240 * n:
        return set()
    step = n * bmp.channels
    rows = []
    for y in range(240):
        line = bmp.band(y * n, 0, bmp.width)
        rows.append([int.from_bytes(line[i:i + 3], "big") for i in range(0, len(line), step)])
    return {tuple(v for r in range(8) for v in rows[y + r][x:x + 8])
            for py in range(8) for y in range(py, 240 - 7, 8) for x in range(phase, 256 - 7, 8)}


def shadowing(textures: Path, painted, resident, source_lines, decode):
    """`{(key data, palette): {capture stem, ...}}` for each painted key a live
    capture draws. `painted` maps a key to its 32-hex tile bitmap (None when
    unknown); `resident` is the recorder's `{key data: capture}` provenance
    (`screen_resident_keys`). A capture counts only while its
    `backgrounds/screenNNN.png` exists — a retired one (#344) warns about
    nothing — and is read through its `.orig.png` twin, the ROM's own pixels,
    never the painted PNG."""
    backgrounds = Path(textures) / "backgrounds"
    live = sorted(p.stem for p in backgrounds.glob("screen*.png") if not p.stem.endswith(".orig"))
    out = {}
    for key in painted:
        if resident.get(key[0]) in live:
            out.setdefault(key, set()).add(resident[key[0]])
    wanted = {}
    for key, bitmap in painted.items():
        rgb = tile_rgb(bitmap, key[1]) if bitmap else None
        if rgb is not None:
            wanted.setdefault(rgb, []).append(key)
    phases = _phases(source_lines) if wanted else {}
    for stem in live if wanted else ():
        seen = _cells(decode(backgrounds / f"{stem}.orig.png"), phases.get(stem.lower(), 0))
        for rgb in seen.intersection(wanted):
            for key in wanted[rgb]:
                out.setdefault(key, set()).add(stem)
    return out
