"""mep_recorded — an untouched sheet cell keeps the recorded rule (ADR-0231, #447).

A recorded pack's `textures/hires.txt` points every key at the recorder's own
pattern pages (`chr/Chr_*.png`), which went through the pack's scale filter
(xBRZ by default, `HdPackBuilder::GenerateHdTile`). A sheet crop is the raw
tile upscaled nearest-neighbour (`SheetRender::RenderTile`). Pointing an
untouched cell's key at its crop therefore changed what an unpainted rebuild
rendered: 901 of 912 Castlevania rules drew other pixels than the recording.

`mep_build` hands this module every rule it is about to emit. A rule whose cell
was not painted (it equals its `*.orig.png` twin) and whose key the recording
has is replaced by the recording's own line, byte for byte except the `<img>`
index, pointing at the recorded page. A painted cell keeps the sheet crop.

Where the recording is read from, first hit wins:

1. `textures/hires.recorded.txt` — the snapshot this module writes;
2. `auto/textures/hires.txt` — the layered project (`mep_import`, the
   ADR-0183 §4 round trip), which no build ever rewrites;
3. the build's own key source, when a build did not write it. The first build
   overwrites that file, so its bytes are copied to (1) first.

A recorded rule is used only where it still means what it meant: the page it
names exists under `textures/`, its crop lies inside that page, and the
recording's `<scale>` is the build's. Everything else falls back to the sheet
crop, and the build says how many did.
"""

from pathlib import Path

SNAPSHOT = "hires.recorded.txt"
# The comment line above the re-emitted rules. `check-coverage` reads the
# `<img>` lines after it as build output (#218), and a manifest carrying it is
# never mistaken for a recording.
MARK = "# mep_build: rules kept as recorded for untouched cells"


def _is_sheet(rel: str) -> bool:
    return rel.replace("\\", "/").lower().startswith("sheets/")


def looks_recorded(lines) -> bool:
    """True for a manifest `mep_build` did not write: it has `<tile>` rules,
    names no `sheets/` image and carries no MARK. A pages-only manifest
    (ADR-0219) is not a recording either."""
    if lines and lines[0].strip() == "# mep-pages-only 1":
        return False
    tiles = False
    for line in lines:
        s = line.strip()
        if s.startswith(MARK) or (s.startswith("<img>") and _is_sheet(s[5:].strip())):
            return False
        tiles = tiles or "<tile>" in s and not s.startswith("#")
    return tiles


def section_imgs(lines) -> set:
    """The `<img>` paths a build declared under MARK."""
    out, inside = set(), False
    for line in lines:
        s = line.strip()
        if s.startswith(MARK):
            inside = True
        elif inside and s.startswith("<img>"):
            out.add(s[5:].strip())
    return out


class Recorded:
    """The recorded rules of one build, and what the build did with them."""

    def __init__(self, label: str = "", reason: str = ""):
        self.label = label
        self.reason = reason
        self.imgs = []
        self.rules = {}  # (cond, DATA, PAL) -> (order, img index, cond, fields)
        self.used = []
        self.fallback = 0

    def take(self, live: list) -> list:
        """Remove from `live` ((pos, entry) pairs of one sheet slot) every
        untouched entry the recording has a rule for; keep the rest."""
        kept = []
        for pos, entry in live:
            key, edited = entry[0], entry[3]
            if edited:
                kept.append((pos, entry))
            elif key in self.rules:
                self.used.append(self.rules[key])
            else:
                self.fallback += 1
                kept.append((pos, entry))
        return kept

    def emit(self, out_lines: list, img_index: int):
        """Append MARK, the recorded pages the kept rules name and the rules,
        in the recording's own order. Returns (rules, next img index, keys)."""
        if not self.used:
            return 0, img_index, set()
        used = sorted(set((r[0], r[1]) for r in self.used))
        pages = sorted({img for _o, img in used})
        where = {img: img_index + i for i, img in enumerate(pages)}
        out_lines.append(f"{MARK}, from {self.label} (ADR-0231, #447)")
        out_lines.extend(f"<img>{self.imgs[img]}" for img in pages)
        rows = sorted({r[0]: r for r in self.used}.values())
        for _order, img, cond, fields in rows:
            out_lines.append(f"{cond}<tile>{','.join([str(where[img])] + fields[1:])}")
        keys = {(f[1].upper(), f[2].upper()) for _o, _i, _c, f in rows}
        return len(rows), img_index + len(pages), keys

    def report(self) -> None:
        kept = len({r[0] for r in self.used})
        if kept:
            print(f"info: {kept} untouched cell rule(s) kept as recorded, from {self.label}: they "
                  f"render the recorded art, not the sheet crop (ADR-0231)")
        if not self.fallback:
            return
        why = self.reason or (f"{self.label} has no usable rule for them (the key was never "
                              f"recorded, or its page is missing from textures/)")
        print(f"info: {self.fallback} untouched cell rule(s) point at the sheet crop, which is "
              f"nearest-neighbour and so not the recorded art — {why} (#447)")


def _read_rules(rec: Recorded, lines, textures: Path, png_size, scale: int, tile_re) -> None:
    sizes, span = {}, 8 * scale
    for order, line in enumerate(lines):
        s = line.strip()
        if s.startswith("<img>"):
            rec.imgs.append(s[5:].strip())
            continue
        m = tile_re.match(s)
        if not m:
            continue
        f = [x.strip() for x in m.group(2).split(",")]
        try:
            img, x, y = int(f[0]), int(f[3]), int(f[4])
        except (IndexError, ValueError):
            continue
        if not 0 <= img < len(rec.imgs) or len(f) < 6:
            continue
        rel = rec.imgs[img]
        if rel not in sizes:
            sizes[rel] = png_size(textures / rel)
        size = sizes[rel]
        if size is None or x < 0 or y < 0 or x + span > size[0] or y + span > size[1]:
            continue
        rec.rules.setdefault((m.group(1) or "", f[1].upper(), f[2].upper()), (order, img, m.group(1) or "", f))


def load(folder: Path, source: Path, source_lines, scale: int, png_size, tile_re) -> Recorded:
    """The recording this build keeps untouched rules from (module docstring)."""
    textures = folder / "textures"
    snap, auto = textures / SNAPSHOT, folder / "auto" / "textures" / "hires.txt"
    path, lines = None, None
    for cand in (snap, auto):
        if cand.is_file():
            text = cand.read_text(encoding="utf-8", errors="replace").splitlines()
            if cand == snap or looks_recorded(text):
                path, lines = cand, text
                break
    if path is None and looks_recorded(source_lines):
        path, lines = source, source_lines
    if path is None:
        return Recorded(reason=f"there is no recording to keep them from (no textures/{SNAPSHOT}, "
                               f"no auto/textures/hires.txt, and the key source was written by a build)")
    rec = Recorded()
    rec_scale = next((h.strip()[7:].strip() for h in lines if h.strip().startswith("<scale>")), "1")
    _read_rules(rec, lines, textures, png_size, int(rec_scale) if rec_scale.isdigit() else 1, tile_re)
    if path == source and path != snap and rec.rules:
        # The label names the snapshot, so a second build writes the same bytes.
        snap.write_bytes(source.read_bytes())
        path = snap
        print(f"info: kept the recording as textures/{SNAPSHOT} before overwriting it, so every "
              f"later build can still re-emit its rules (ADR-0231)")
    try:
        rec.label = path.relative_to(folder).as_posix()
    except ValueError:
        rec.label = str(path)
    label = rec.label
    if str(scale) != rec_scale:
        rec.rules = {}
        rec.reason = (f"{label} was recorded at <scale>{rec_scale} and the sheets pin scale {scale}, "
                      f"so its crops do not fit this pack")
    return rec
