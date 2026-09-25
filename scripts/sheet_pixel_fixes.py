"""The pixel corrections `mep_build.py build` makes to an ADR-0153 sheet before
the rebuilt `hires.txt` points at it. The sheet *is* the image the pack ships,
so a crop whose pixels the run time would read differently from the recording
has to be corrected in the sheet itself, and in its `*.orig.png` twin in
lockstep (#329) so the correction never reads as the artist's paint.

Two corrections live here:

* **Un-baking a mirror** (ADR-0178, #255, #457). The recorder bakes a sprite's
  OAM flips into the shape it records; the run time mirrors the replacement
  art itself, so the crop has to hold the unflipped pixels. `flip_region`
  un-bakes one crop, `sidecar_drop_mirrors` clears the sidecar afterwards so
  a second build does not flip again.
* **Keeping colour 0 transparent** (#456). `SheetRender` draws a background
  cell with colour 0 opaque (a transparent colour 0 would punch holes in a
  tree the artist paints). The recording itself keeps colour 0 transparent on
  every background key a behind-background sprite was drawn over
  (`HdPackBuilder` `TransparencyRequired`, `HdPackTileInfo::ToRgb`), and that
  is what lets the sprite show through, as it does on hardware. A rebuilt
  crop that kept the opaque backdrop hid the sprite (Punch-Out!!: Glass Joe).
  `KeySourceAlpha` reads which keys the key source draws with an alpha-0
  pixel at a colour-0 position (the recorder's signature, never a translucent
  brush); `punch_backdrop` clears colour 0 in those crops wherever the sheet
  still holds the twin's backdrop colour, so the artist's own paint stays.
  An RGB sheet gains an alpha channel first (`with_alpha`). The contract -
  inputs, side effects, exclusions, verification - is in `scripts/AGENTS.md`.

Stdlib only; the PNG decoder is `mep_build._png_pixels`, passed in.
"""

import json
import re
from pathlib import Path

import palette_folds

_HEX_TILE_RE = re.compile(r"^[0-9A-F]{32}$")
_TILE_RE = re.compile(r"^(\[[^\]]*\])?<tile>(.*)$")
_CLEAR = b"\x00\x00\x00\x00"


def flip_region(bmp, x: int, y: int, w: int, h: int, mirror: str) -> None:
    """Un-bake an OAM mirror from a crop in place (#255 / ADR-0178). The run
    time keys by the unflipped tile and mirrors the replacement art itself, so
    a sheet cell that carried `source` + `mirror` must store unflipped pixels
    under the source key, not the baked-flipped bitmap the kit showed."""
    ch = bmp.channels
    if "H" in mirror:
        for row in range(y, y + h):
            off = row * bmp.stride
            for i in range(w // 2):
                a = off + (x + i) * ch
                b = off + (x + w - 1 - i) * ch
                bmp.raw[a:a + ch], bmp.raw[b:b + ch] = (
                    bytes(bmp.raw[b:b + ch]), bytes(bmp.raw[a:a + ch]))
    if "V" in mirror:
        for col in range(x, x + w):
            for i in range(h // 2):
                a = (y + i) * bmp.stride + col * ch
                b = (y + h - 1 - i) * bmp.stride + col * ch
                bmp.raw[a:a + ch], bmp.raw[b:b + ch] = (
                    bytes(bmp.raw[b:b + ch]), bytes(bmp.raw[a:a + ch]))


def sidecar_drop_mirrors(json_path: Path) -> int:
    """After un-baking mirror crops into the sheet PNG, rewrite the sidecar so
    a second `build` does not flip again (#255 idempotency). Each tile entry
    that carried `source` + `mirror` becomes a plain unflipped entry: `tile`
    is replaced by `source`, and both optional fields are removed. `index`
    (ADR-0172) is left alone: a flip never changes the CHR index."""
    try:
        doc = json.loads(json_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return 0
    if not isinstance(doc, dict):
        return 0
    n = 0

    def fix_tiles(tiles):
        nonlocal n
        if not isinstance(tiles, list):
            return
        for entry in tiles:
            if not isinstance(entry, dict):
                continue
            src = str(entry.get("source") or "").strip().upper()
            mir = str(entry.get("mirror") or "").strip().upper()
            if not src or mir not in ("H", "V", "HV") or not _HEX_TILE_RE.match(src):
                continue
            entry["tile"] = src
            entry.pop("source", None)
            entry.pop("mirror", None)
            n += 1

    for cell in doc.get("cells") or []:
        if not isinstance(cell, dict):
            continue
        fix_tiles(cell.get("tiles"))
        for alias in cell.get("aliases") or []:
            if isinstance(alias, dict):
                fix_tiles(alias.get("tiles"))
    if not n:
        return 0
    json_path.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    return n


class KeySourceAlpha:
    """Which `(tile, palette)` keys the key source draws with the recorder's
    `TransparencyRequired` signature: a fully transparent (alpha 0) pixel at a
    colour-0 position of the key's tile, read off the crops its own
    `<img>`/`<tile>` lines point at, at its own `<scale>`. On a recording that
    is exactly the background keys `TransparencyRequired` marked; on a
    manifest a previous build wrote, it is the crops that build already
    corrected, so a rebuild keeps them. A translucent brush pixel (alpha
    1..254) or an alpha-0 pixel over the tile's ink is the artist's paint, not
    the signature (PR #475 review). `bitmaps` maps a key to its 32-hex tile
    bitmap - on a CHR ROM game the key is an index, which carries no pixels;
    a key missing from it that is itself 32-hex data is its own bitmap."""

    def __init__(self, source: Path, decode, bitmaps=None):
        self._decode = decode
        self._bitmaps = bitmaps or {}
        self._dir = Path(source).parent
        self._imgs, self._rules, self._images, self._known = [], {}, {}, {}
        self._scale = 1
        for line in Path(source).read_text(encoding="utf-8", errors="replace").splitlines():
            s = line.strip()
            if s.startswith("<scale>") and s[7:].strip().isdigit():
                self._scale = int(s[7:].strip())
            elif s.startswith("<img>"):
                self._imgs.append(s[5:].strip())
            else:
                m = _TILE_RE.match(s)
                f = [t.strip() for t in m.group(2).split(",")] if m else []
                if len(f) >= 5:
                    self._rules.setdefault((f[1].upper(), f[2].upper()), []).append(f[:5])

    def _image(self, i: int):
        if i not in self._images:
            rel = self._imgs[i] if 0 <= i < len(self._imgs) else None
            self._images[i] = self._decode(self._dir / rel) if rel else None
        return self._images[i]

    def transparent(self, key) -> bool:
        if key not in self._known:
            tile = self._bitmaps.get(key) or key[0]
            zeros = _colour0_positions(tile) if _HEX_TILE_RE.match(tile or "") else ()
            self._known[key] = bool(zeros) and any(self._crop_has_alpha(f, zeros) for f in self._rules.get(key, ()))
        return self._known[key]

    def _crop_has_alpha(self, fields, zeros) -> bool:
        try:
            bmp, x, y = self._image(int(fields[0])), int(fields[3]), int(fields[4])
        except ValueError:
            return False
        span = 8 * self._scale
        if bmp is None or bmp.channels != 4 or x < 0 or y < 0 or x + span > bmp.width or y + span > bmp.height:
            return False
        n = self._scale
        for r, c in zeros:
            for row in range(y + r * n, y + r * n + n):
                off = row * bmp.stride + (x + c * n) * 4 + 3
                if 0 in bmp.raw[off:off + n * 4:4]:
                    return True
        return False


def _colour0_positions(tile_hex: str) -> list:
    """The (row, column) of every colour-0 pixel of a 32-hex 2bpp NES tile."""
    data = bytes.fromhex(tile_hex)
    return [(r, c) for r in range(8) for c in range(8)
            if not ((data[r] >> (7 - c)) & 1 or (data[r + 8] >> (7 - c)) & 1)]


def with_alpha(bmp):
    """`bmp` as 8-bit RGBA: an RGB bitmap (an editor's opaque working sheet)
    gains a fully opaque alpha channel so `punch_backdrop` can clear colour 0
    in it (#456). RGBA and None come back as they are."""
    if bmp is None or bmp.channels != 3:
        return bmp
    raw = bytearray(len(bmp.raw) // 3 * 4)
    raw[0::4], raw[1::4], raw[2::4] = bmp.raw[0::3], bmp.raw[1::3], bmp.raw[2::3]
    raw[3::4] = b"\xff" * (len(raw) // 4)
    return type(bmp)(bmp.width, bmp.height, 4, raw)


def see_through_places(placed, transparent) -> set:
    """The crops `punch_backdrop` has to clear, from `placed` = every
    background `(place, key)` the sheets emit. A key the recording draws
    see-through (`transparent(key)`) clears every crop that emits it - and a
    crop two keys share (an alias, or the same art under two CHR indexes)
    then shows its other key see-through too, so that key's other crops are
    cleared as well, to a fixed point. Without the closure the next build,
    which reads this one's manifest as its key source, would find the second
    key see-through and clear more crops: not idempotent."""
    keys_at, places_of = {}, {}
    for place, key in placed:
        keys_at.setdefault(place, set()).add(key)
        places_of.setdefault(key, set()).add(place)
    todo = [k for k in places_of if transparent(k)]
    done = set(todo)
    while todo:
        for place in places_of[todo.pop()]:
            for other in keys_at[place] - done:
                done.add(other)
                todo.append(other)
    return {place for place, keys in keys_at.items() if keys & done}


def punch_backdrop(bmp, ref, x: int, y: int, scale: int, tile_hex: str, palette: str) -> int:
    """Clear colour 0 of the crop at sheet pixel `(x, y)` (#456): every pixel
    at a colour-0 position of `tile_hex` that still holds the backdrop colour
    becomes fully transparent, and so does that 1x pixel of the twin `ref`.
    The backdrop colour is the twin's own pixel there; with no twin it is
    palette entry 0 in the default NES palette. A twin pixel that is already
    transparent was cleared by an earlier build, and is left alone - that is
    what makes a second build a no-op. Returns 1 when anything changed."""
    if bmp.channels != 4 or not _HEX_TILE_RE.match(tile_hex or ""):
        return 0
    span, tx, ty = 8 * scale, x // scale, y // scale
    if x < 0 or y < 0 or x + span > bmp.width or y + span > bmp.height:
        return 0
    if ref is not None and (ref.channels != 4 or tx + 8 > ref.width or ty + 8 > ref.height):
        ref = None
    argb = palette_folds.DEFAULT_PALETTE_ARGB[palette_folds.palette_entries(palette)[0]]
    fallback = bytes(((argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF, 0xFF))
    changed = 0
    for r, c in _colour0_positions(tile_hex):
        back = fallback
        if ref is not None:
            o = (ty + r) * ref.stride + (tx + c) * 4
            back = bytes(ref.raw[o:o + 4])
            if back[3] != 0xFF:
                continue
            ref.raw[o:o + 4] = _CLEAR
            changed = 1
        for dy in range(scale):
            row = (y + r * scale + dy) * bmp.stride + (x + c * scale) * 4
            for p in range(row, row + scale * 4, 4):
                if bmp.raw[p:p + 4] == back:
                    bmp.raw[p:p + 4] = _CLEAR
                    changed = 1
    return changed
