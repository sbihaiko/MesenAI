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
state (with the tiles only that state exhibited; a state is named for the
folder two levels above `auto/`, extended leftwards when two runs of the
same state would otherwise share a row). This is the measurement
ADR-0182 §3 asks for before a further stage is played; the 2026-09-13 run
over Contra80s 1.1 and the fifteen Contra packs is summarised in that ADR.

The measurement is a set intersection, so it is only meaningful when both
sides key their tiles the same way. A `hires.txt` names a tile either by
its CHR ROM bank index or by the 16 bytes of its CHR RAM pattern, and the
emulator tells the two apart by width alone (`HdPackLoader::ReadTileData`:
`tileData.size() >= 32` is a CHR RAM tile, anything shorter is an index).
A reference pack built for a ROM whose mapper was changed by a `<patch>`
therefore keys tiles in a namespace our recording of the stock ROM cannot
contain: the intersection is empty by construction, not by under-recording.
We refuse that comparison instead of printing 0% (#225).

A `<patch>` whose keys happen to share the recording's *shape* passes that
check, yet the pack is still built for a patched ROM. That is undecidable
from the pack alone — an audio-only patch leaves the tiles valid, a mapper
patch does not — so we do not refuse it. Instead a caveat is printed above
the tables (on stderr) naming the patch and its target sha1 and quoting the
header bytes the patch writes, and the summary line carries the same caveat,
so the figures never read as plain coverage (#231).
"""
import collections, glob, json, os, re, sys

TILE = re.compile(r"(?:\[([^\]]*)\])?<tile>(\d+),([0-9A-Fa-f]+),([0-9A-Fa-f]+),")
COND = re.compile(r"<condition>([^,]+),memoryCheckConstant,([0-9A-Fa-f]+),==,([0-9A-Fa-f]+)")
PATCH = re.compile(r"<patch>([^,]+),([0-9A-Fa-f]+)")

# iNES header fields an IPS record can land on, by offset. Byte 5 is the one
# that decides the namespace: 0 means the board has CHR RAM (patterns), any
# other value is that many 8 KB banks of CHR ROM (indices).
INES_FIELD = {
    4: "PRG ROM size, 16 KB units",
    5: "CHR ROM size, 8 KB units (0 = CHR RAM)",
    6: "flags 6 (mapper low nibble, mirroring)",
    7: "flags 7 (mapper high nibble)",
}


def shape(data):
    """Which namespace a `tileData` field is in, by the emulator's own rule."""
    return "chr-ram-pattern" if len(data) >= 32 else "chr-rom-index"


SHAPE_TEXT = {
    "chr-ram-pattern": "CHR RAM pattern (32 hex chars, the tile's 16 bytes)",
    "chr-rom-index": "CHR ROM bank index (short hex, the tile's number)",
}


def patches(path):
    """`<patch>` directives of a manifest, as (file name, target sha1)."""
    out = []
    for line in open(path, encoding="utf-8", errors="replace"):
        m = PATCH.match(line.strip())
        if m:
            out.append((m.group(1), m.group(2).lower()))
    return out


def ips_header_writes(path):
    """Bytes an IPS patch writes into the first 16 bytes of its target.

    Returns {offset: byte}, or None when the file is not an IPS we can read.
    Only the iNES header window is collected; every other record is skipped
    over, not skipped past — see the ordering note below.
    """
    try:
        blob = open(path, "rb").read()
    except OSError:
        return None
    if not blob.startswith(b"PATCH"):
        return None
    out, i = {}, 5
    while i + 3 <= len(blob):
        if blob[i:i + 3] == b"EOF":
            break
        off = int.from_bytes(blob[i:i + 3], "big")
        i += 3
        if i + 2 > len(blob):
            return None
        size = int.from_bytes(blob[i:i + 2], "big")
        i += 2
        if size:
            chunk, i = blob[i:i + size], i + size
        else:  # RLE record: run length, then the repeated byte
            if i + 3 > len(blob):
                return None
            run = int.from_bytes(blob[i:i + 2], "big")
            chunk, i = bytes([blob[i + 2]]) * run, i + 3
        # Records are NOT required to be ordered by offset: the emulator's own
        # patcher reads them all and applies them in stream order
        # (IpsPatcher::PatchBuffer, Utilities/Patches/IpsPatcher.cpp), so a
        # header record can follow a body record. Skipping the rest of the file
        # at the first offset past the window would then miss a header write
        # the emulator does perform.
        for n, b in enumerate(chunk):
            if off + n < 16:
                out[off + n] = b
    return out


def patch_evidence(artist_path):
    """Classify every `<patch>` a manifest declares, by what it writes.

    Returns `(found, silent, unreadable, shas)`: the bytes each readable patch
    writes into the first 16 bytes of its target, keyed by file name; the file
    names that read fine but leave the header alone; the ones we could not open
    at all; and every declared target sha1 keyed by file name (one `<patch>`
    line is usually declared once per supported ROM sha1).
    """
    found, silent, unreadable = {}, [], []
    shas = collections.defaultdict(list)
    for name, sha1 in patches(artist_path):
        shas[name].append(sha1)
        if name in found or name in silent or name in unreadable:
            continue
        ips = os.path.join(os.path.dirname(os.path.abspath(artist_path)), name)
        writes = ips_header_writes(ips)
        if writes is None:
            unreadable.append(name)
        elif writes:
            found[name] = writes
        else:
            silent.append(name)
    return found, silent, unreadable, shas


def namespace_report(artist_path, a_data, seen_data, pack_paths):
    """None when the two sides can intersect, else the refusal message."""
    a_shapes = {shape(d) for d in a_data}
    s_shapes = {shape(d) for d in seen_data}
    if not a_data:
        return (f"error: {artist_path} declares no <tile> rules — nothing to measure.\n"
                f"       Point this at the reference pack's textures/hires.txt (or the pack's "
                f"hires.txt for a plain HD Mesen pack).")
    if not seen_data:
        return ("error: the recorded pack(s) declare no <tile> rules — nothing to measure "
                "against:\n" + "".join(f"         {p}\n" for p in pack_paths) +
                "       Each argument must be a recorder `auto/` folder, the one holding "
                "textures/hires.txt.")
    if a_shapes & s_shapes:
        return None

    lines = [
        "error: the reference pack and the recording key their tiles in different namespaces, "
        "so this measurement cannot be made.",
        f"       reference {artist_path}",
        f"         {len(a_data)} distinct tileData, all {', '.join(SHAPE_TEXT[s] for s in sorted(a_shapes))}",
        f"       recording {', '.join(pack_paths)}",
        f"         {len(seen_data)} distinct tileData, all {', '.join(SHAPE_TEXT[s] for s in sorted(s_shapes))}",
        "       The emulator itself splits these by width alone (HdPackLoader::ReadTileData: a "
        "tileData of 32+ hex chars is a CHR RAM pattern, anything shorter is a CHR ROM bank "
        "index), so no key of one kind can ever equal a key of the other. The intersection is "
        "empty by construction — it is not a coverage of 0%.",
    ]

    # One <patch> file is usually declared once per supported ROM sha1, so
    # group by file: the header bytes it writes are the same every time.
    # Three outcomes matter and they are not the same claim: a patch that
    # rewrites the header (evidence we can quote), one that reads fine and
    # leaves the header alone (its edits are in the PRG/CHR body), and one we
    # could not open at all. Only the first is evidence for the key-shape
    # split; the other two are both "built for a different build".
    found, silent, unreadable, shas = patch_evidence(artist_path)
    if found:
        lines.append("       The reference pack ships a <patch>, and it rewrites the iNES header:")
        for name, writes in found.items():
            targets = shas[name]
            lines.append(f"         {name} (targets {len(targets)} ROM sha1(s), first "
                         f"{targets[0]}) writes into the header:")
            for off in sorted(writes):
                field = INES_FIELD.get(off, f"byte {off}")
                lines.append(f"           offset {off} = 0x{writes[off]:02X}  ({field})")
        chr_rom = [w[5] for w in found.values() if w.get(5)]
        if chr_rom:
            lines.append(
                f"       Header byte 5 becomes 0x{chr_rom[0]:02X}, i.e. {chr_rom[0] * 8} KB of CHR "
                "ROM: the patched build is a CHR ROM game, so its tiles have bank indices. The "
                "stock ROM we recorded has CHR RAM (byte 5 = 0), whose tiles have no index at "
                "all and are keyed by pattern.")
    elif unreadable:
        lines.append("       The reference pack declares a <patch> we could not read next to "
                     f"{artist_path} — it is built for a patched ROM, not the stock one.")

    if silent:
        lines.append(f"       The reference pack ships a <patch> ({', '.join(sorted(set(silent)))}) "
                     "that does not rewrite the iNES header, so what it changes is in the PRG/CHR "
                     "body. Either way the pack is built for a different build than the one you "
                     "recorded.")

    lines += [
        "       What to do: this reference pack is not measurable against a recording of the "
        "stock ROM. Either record the patched ROM the pack targets (apply its <patch>, then "
        "`headless_record` that build) and measure against that, or pick a reference pack that "
        "targets the same ROM you recorded.",
    ]
    return "\n".join(lines)


def patch_caveat(artist_path):
    """The caveat to print when the reference declares a `<patch>`, else None.

    A `<patch>` line means the reference pack was built for a patched ROM, so
    the intersection below compares two builds. Whether that invalidates the
    tile keys cannot be decided from the pack alone (#231): a patch that leaves
    PRG/CHR where it is (audio, say) leaves the keys valid, a mapper patch does
    not. Refusing would delete the only measurement half the installed
    reference packs can give, so we keep measuring and say out loud what the
    pack declares — the silence is the one answer that is always wrong.
    """
    declared = patches(artist_path)
    if not declared:
        return None
    found, silent, unreadable, shas = patch_evidence(artist_path)
    lines = [
        f"warning: {artist_path} declares a <patch> — the reference pack is built for a "
        "patched ROM, so every figure below compares two builds. This is not automatically "
        "fatal (a patch that leaves PRG/CHR alone, e.g. audio-only, leaves the tile keys "
        "valid) but it is never visible any other way.",
        "         patches declared:",
    ]
    for name, sha1s in shas.items():
        lines.append(f"           {name}, target sha1 {', '.join(sha1s)}")
    for name, writes in found.items():
        lines.append(f"         {name} rewrites the iNES header:")
        for off in sorted(writes):
            field = INES_FIELD.get(off, f"byte {off}")
            lines.append(f"           offset {off} = 0x{writes[off]:02X}  ({field})")
        if 5 in writes:
            byte5 = writes[5]
            lines.append(
                f"         byte 5 becomes 0x{byte5:02X} = {byte5 * 8} KB of CHR ROM. Byte 5 is "
                "the field that decides the tile namespace (0 = CHR RAM, tiles keyed by 16-byte "
                "pattern; any other value = that many 8 KB banks of CHR ROM, tiles keyed by bank "
                "index), so a patch that rewrites it is the case where the two builds provably do "
                "not address CHR the same way — read the numbers below as indicative, not "
                "coverage.")
        else:
            lines.append("         it leaves byte 5 — the field that decides the tile namespace "
                         "(CHR RAM vs CHR ROM) — at its original value.")
    if silent:
        lines.append(f"         {', '.join(sorted(set(silent)))} reads fine and does not rewrite "
                     "the iNES header, so its edits are in the PRG/CHR body: they may or may not "
                     "move the tiles, and byte 5 is unchanged.")
    if unreadable:
        lines.append(f"         {', '.join(sorted(set(unreadable)))} could not be read, so how it "
                     "affects the tiles is unknown.")
    return "\n".join(lines)


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


def state_labels(roots):
    """A distinct per-state label for each recorded pack dir, shortest first.

    The state name is the folder two levels above `auto/` (`.../<state>/<rom
    name>/auto`), which is short and is what the per-state table wants. Two
    runs of the same state under different run folders share it, though, and
    keying the table on it silently collapsed them into one row and made "only
    this state" wrong. Extend leftwards until the labels are distinct.
    """
    parts = [os.path.abspath(r.rstrip("/")).split(os.sep) for r in roots]
    depth = 3  # <state>/<rom name>/auto
    while depth <= max(len(p) for p in parts):
        out = [os.sep.join(p[-depth:-2]) or p[-1] for p in parts]
        if len(set(out)) == len(out):
            return out
        depth += 1
    return [os.sep.join(p) for p in parts]


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
    for root, name in zip(argv[2:], state_labels(argv[2:])):
        rules = parse(os.path.join(root, "textures", "hires.txt"))
        keys = {(d, p) for _, d, p, _ in rules}
        per[name] = keys
        seen |= keys
        for sheet in glob.glob(os.path.join(root, "textures", "sheets", "spr*.json")):
            for cell in json.load(open(sheet)).get("cells", []):
                for t in cell.get("tiles", []):
                    sprite.add(t["tile"].upper())
    seen_data = {d for d, _ in seen}

    # Refuse before printing anything: a namespace mismatch makes every number
    # below a confident zero, which reads as "record more" and is not (#225).
    refusal = namespace_report(argv[1], all_data, seen_data, list(argv[2:]))
    if refusal:
        print(refusal, file=sys.stderr)
        return 1
    # A <patch> that survives that check is not a refusal (its keys share the
    # recording's shape), but the pack is still built for a patched ROM. Say so
    # above the tables rather than letting the figures read as plain coverage.
    caveat = patch_caveat(argv[1])
    if caveat:
        print(caveat, file=sys.stderr)
    # Partial mismatch still measures, but say how much of the reference is
    # unreachable for the same reason rather than letting it read as unseen.
    s_shapes = {shape(d) for d in seen_data}
    foreign = {d for d in all_data if shape(d) not in s_shapes}
    if foreign:
        kinds = ", ".join(SHAPE_TEXT[s] for s in sorted({shape(d) for d in foreign}))
        print(f"warning: {len(foreign)}/{len(all_data)} reference tileData are {kinds}, which the "
              f"recording never produces — they can only count as unseen", file=sys.stderr)

    rows = []
    for name in a_keys:
        data = a_data[name]
        seen_here = data & seen_data
        spr = sum(1 for d in seen_here if d in sprite)
        kind = "unseen" if not seen_here else ("sprite" if spr / len(seen_here) >= 0.5 else "background")
        rows.append((name, kind, sorted(a_gate[name]), len(a_keys[name]), len(data), len(seen_here), len(a_keys[name] & seen)))

    print(f"artist: {len(a_keys)} images, {len(artist)} tile rules, {len(all_keys)} keys (tileData+palette), {len(all_data)} distinct tileData")
    print(f"recorded: {len(per)} packs, {len(seen)} keys, {len(seen_data)} tileData, {len(sprite)} sprite tileData on sheets")
    coverage = (f"artist tileData on screen in some state: {len(all_data & seen_data)}/{len(all_data)}; "
                f"exact keys {len(all_keys & seen)}/{len(all_keys)}")
    if caveat:
        # A reader who sees only this line must not take it for coverage.
        names = ", ".join(sorted({n for n, _ in patches(argv[1])}))
        coverage += (f"  [caveat: the reference declares <patch> ({names}) and is built for a "
                     "patched ROM, so this compares two builds — not coverage]")
    print(coverage)
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
