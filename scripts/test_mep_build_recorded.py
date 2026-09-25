#!/usr/bin/env python3
"""ADR-0231 / issue #447: an untouched sheet cell keeps the recorded rule.

A recorded pack's `textures/hires.txt` points every key at the recorder's
pattern pages (`chr/Chr_*.png`), which went through the pack's scale filter
(xBRZ by default). A sheet crop is the raw tile upscaled nearest-neighbour. So
a rebuild that points an *untouched* cell's key at its sheet crop renders
different pixels from the recording, even though nothing was painted (#447).

The decision: when a cell was not painted (it equals its `*.orig.png` twin),
`mep_build` re-emits the recording's own rule for that key, byte for byte except
the `<img>` index, pointing at the recorded page. Only a painted cell points at
the sheet crop. This file pins it on a synthetic pack whose "recorded page"
holds crops that differ from the sheet's on purpose (the xBRZ stand-in):

  * an untouched cell's rule is the recorded line (same condition prefix, same
    fields after the image index) and renders the recorded pixels;
  * a painted cell's rule points at the sheet crop, key-source attributes kept;
  * the recording survives the first build, which overwrites `hires.txt`: a
    second build is byte-identical, and a cell painted and then reverted comes
    back to the recorded rule;
  * a mirrored sprite cell (ADR-0178 `source` + `mirror`) keeps the recorded
    rule of its unflipped source key;
  * the key set is the one the sheets carry, recorded rules or not;
  * a recorded rule that cannot be used here (its page is missing, or the
    sheets were painted at another scale) falls back to the sheet crop, and
    the build says how many did;
  * a pack that keeps the recording in `auto/textures/` reads it from there
    and writes no snapshot;
  * a `mep_import` project, whose recorded pages live only under
    `auto/textures/`, gets them copied up into `textures/`, so every emitted
    `<img>` resolves in the layer that names it and the pack lints;
  * an ADR-0230 fold of an untouched cell keeps its recorded rule too, and
    follows the painted crop at the fold's Brightness once the cell is painted
    (ADR-0231 §6);
  * `check-coverage` still treats an unpainted build as a sheet-derived
    baseline.

Framework-free, like test_mep_build.py, whose fixtures it reuses.
Usage: python3 scripts/test_mep_build_recorded.py
"""

import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_mep_build as T  # noqa: E402 — the ADR-0153 sheet fixtures

MEP_BUILD = T.MEP_BUILD
PY = sys.executable
SCALE = 2
SPAN = 8 * SCALE
PAGE = "chr/Chr_0.png"
SNAPSHOT = "hires.recorded.txt"
FAILED = 0
COND = "[c0]"
COND_DEF = "<condition>c0,memoryCheck,30,=,3"


def ok(msg):
    print(f"PASS: {msg}")


def fail(msg):
    global FAILED
    FAILED = 1
    print(f"FAIL: {msg}")


def run(*argv, expect=0):
    p = subprocess.run([PY, str(MEP_BUILD), *argv], capture_output=True, text=True)
    out = (p.stdout + p.stderr).strip()
    if p.returncode != expect:
        fail(f"mep_build {' '.join(argv)} -> exit {p.returncode}, expected {expect}: {out[-1500:]}")
        return None
    return out


def page_color(shape: int) -> int:
    """The recorded page's pixel for `shape`: a solid colour no sheet crop has,
    standing in for the xBRZ-filtered art the recorder writes."""
    return 0xFF000000 | ((shape * 0x2A1B0C + 0x113355) & 0xFFFFFF)


def page_xy(shape: int):
    return (shape % 16) * SPAN, (shape // 16) * SPAN


def recorded_lines(shapes, chr_rom=False):
    """The recording's manifest: one rule per shape on the recorded page, with
    the recorder's trailing fields, shape 0 at brightness 0.5 / defaultTile Y,
    and shape 1 as a conditional rule followed by its bare twin (ADR-0189 §3)."""
    lines = ["<ver>107", f"<scale>{SCALE}", "<system>nes",
             "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B", COND_DEF, f"<img>{PAGE}"]
    rules = {}
    for shape in shapes:
        key = f"{T.CHR_INDEX_BASE + shape:02X}" if chr_rom else T.tile_hex(shape)
        x, y = page_xy(shape)
        extra = "0.5,Y" if shape == 0 else "1,N"
        body = f"0,{key},{T.PAL_HEX},{x},{y},{extra},{700000 + shape},0"
        if shape == 1:
            lines.append(f"{COND}<tile>{body}")
            rules[(COND, key)] = body
        lines.append(f"<tile>{body}")
        rules[("", key)] = body
    return lines, rules


def write_page(textures: Path, shapes):
    width = 16 * SPAN
    # Four rows at least, so a crop read at twice the scale still fits the page
    # and only the <scale> guard can keep it out (other_scale_test).
    height = max((max(shapes) // 16) + 1, 4) * SPAN
    pixels = T.blank(width, height)
    for shape in shapes:
        x, y = page_xy(shape)
        for py in range(y, y + SPAN):
            for px in range(x, x + SPAN):
                pixels[py][px] = page_color(shape)
    (textures / "chr").mkdir(parents=True, exist_ok=True)
    (textures / PAGE).write_bytes(T.png_rgba(pixels))


def make_recorded_pack(root: Path, name: str, shapes=range(8), **kw):
    """`make_sheet_folder`'s ADR-0153 sheets at scale 2, with the key source
    replaced by a recording that points at a real recorded page."""
    folder, _vocab, _cells = T.make_sheet_folder(root, name, scale=SCALE, **kw)
    textures = folder / "textures"
    lines, rules = recorded_lines(shapes, chr_rom=kw.get("chr_rom", False))
    (textures / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    write_page(textures, shapes)
    return folder, rules


def parse_rules(path: Path):
    """`(imgs, [(cond, fields)])` in file order."""
    imgs, rules = [], []
    for line in path.read_text(encoding="utf-8").splitlines():
        s = line.strip()
        if s.startswith("<img>"):
            imgs.append(s[5:].strip())
            continue
        if s.startswith("#") or "<tile>" not in s:
            continue
        cond, body = s.split("<tile>", 1)
        rules.append((cond, [f.strip() for f in body.split(",")]))
    return imgs, rules


def by_key(path: Path):
    imgs, rules = parse_rules(path)
    return imgs, {(cond, f[1].upper(), f[2].upper()): (imgs[int(f[0])], f) for cond, f in rules}


def crop_at(textures: Path, rel: str, x: int, y: int, cache: dict):
    if rel not in cache:
        cache[rel] = T.png_read(textures / rel)
    return T.crop(cache[rel], x, y, SPAN)


def untouched_keeps_recorded_test(root: Path):
    folder, rules = make_recorded_pack(root, "untouched")
    recording = (folder / "textures" / "hires.txt").read_bytes()
    if run("build", str(folder)) is None:
        return
    built = folder / "textures" / "hires.txt"
    imgs, got = by_key(built)
    bad = []
    for (cond, key), body in rules.items():
        want = body.split(",")
        have = got.get((cond, key, T.PAL_HEX))
        if have is None:
            bad.append(f"{cond}{key[:8]}: no rule")
        elif have[0] != PAGE or have[1][1:] != want[1:]:
            bad.append(f"{cond}{key[:8]}: {have[0]} {','.join(have[1])}")
    if bad:
        fail(f"#447: an untouched cell's rule is not the recorded one ({len(bad)} of {len(rules)}): "
             + "; ".join(bad[:4]))
    else:
        ok("#447: every untouched cell re-emits the recorded rule, byte for byte after the <img> index")

    # Rendered pixels: what the emitted rule crops equals what the recording cropped.
    cache, differ = {}, 0
    for (cond, key), body in rules.items():
        have = got.get((cond, key, T.PAL_HEX))
        if have is None:
            differ += 1
            continue
        f = have[1]
        want = [[page_color(int(body.split(",")[3]) // SPAN + 16 * (int(body.split(",")[4]) // SPAN))]
                * SPAN] * SPAN
        if crop_at(folder / "textures", have[0], int(f[3]), int(f[4]), cache) != want:
            differ += 1
    if differ:
        fail(f"#447: {differ} of {len(rules)} untouched rules render other pixels than the recording")
    else:
        ok("#447: every untouched rule renders the recording's pixels")

    # The recorder's order: the conditional rule stays above its bare twin.
    order = [(c, f[1]) for c, f in parse_rules(built)[1] if f[1] == T.tile_hex(1)]
    if order != [(COND, T.tile_hex(1)), ("", T.tile_hex(1))]:
        fail(f"#447: the conditional rule and its bare twin lost their order: {order}")
    else:
        ok("#447: a conditional recorded rule stays above its bare twin")

    snap = folder / "textures" / SNAPSHOT
    if not snap.is_file() or snap.read_bytes() != recording:
        fail(f"#447: the first build did not keep the recording as textures/{SNAPSHOT}")
    else:
        ok(f"#447: the first build keeps the recording as textures/{SNAPSHOT} before overwriting hires.txt")

    first = built.read_bytes()
    if run("build", str(folder)) is None:
        return
    if built.read_bytes() != first:
        fail("#447: a second build of an unpainted pack is not byte-identical")
    else:
        ok("#447: a second build of an unpainted pack is byte-identical")


def painted_then_reverted_test(root: Path):
    folder, rules = make_recorded_pack(root, "painted")
    sheets = folder / "textures" / "sheets"
    if run("build", str(folder)) is None:
        return
    pristine = (sheets / "metatiles.png").read_bytes()
    # Metatile cell 0 (shapes 0..3) sits at (1, 1) at 1x.
    T.paint(folder, "metatiles.png", 1 * SCALE, 1 * SCALE, 16 * SCALE, 0xFFFF00FF)
    if run("build", str(folder)) is None:
        return
    built = folder / "textures" / "hires.txt"
    _imgs, got = by_key(built)
    painted = [got.get(("", T.tile_hex(s), T.PAL_HEX)) for s in range(4)]
    kept = [got.get(("", T.tile_hex(s), T.PAL_HEX)) for s in range(4, 8)]
    if any(p is None or p[0] != "sheets/metatiles.png" for p in painted):
        fail(f"#447: a painted cell does not point at its sheet crop: {[p and p[0] for p in painted]}")
    elif painted[0][1][5:7] != ["0.5", "Y"]:
        fail(f"#447: the painted cell lost the key source's Brightness/defaultTile: {painted[0][1]}")
    elif any(k is None or k[0] != PAGE for k in kept):
        fail(f"#447: painting one cell moved an untouched cell off its recorded rule: {[k and k[0] for k in kept]}")
    else:
        ok("#447: a painted cell points at the sheet crop, and the untouched ones keep the recorded rule")

    (sheets / "metatiles.png").write_bytes(pristine)
    if run("build", str(folder)) is None:
        return
    _imgs, got = by_key(built)
    back = [got.get(("", T.tile_hex(s), T.PAL_HEX)) for s in range(4)]
    if any(b is None or b[0] != PAGE or b[1][1:] != rules[("", T.tile_hex(s))].split(",")[1:]
           for s, b in enumerate(back)):
        fail(f"#447: a cell painted and reverted did not come back to the recorded rule: {[b and b[0] for b in back]}")
    else:
        ok("#447: a cell painted and then reverted comes back to the recorded rule (the snapshot survives builds)")


def mirrored_source_test(root: Path):
    folder, rules = make_recorded_pack(root, "mirrored", flip_baked=True, sidecar_source=True,
                                       sprite_sheet=True)
    if run("build", str(folder)) is None:
        return
    _imgs, got = by_key(folder / "textures" / "hires.txt")
    bad = [s for s in range(8) if (got.get(("", T.tile_hex(s), T.PAL_HEX)) or ("",))[0] != PAGE]
    baked = [k for k in got if k[1] == T.flip_hex(T.tile_hex(0))]
    if bad or baked:
        fail(f"#447/ADR-0178: a mirrored untouched cell does not keep its source key's recorded rule "
             f"(off the page: {bad}; baked keys emitted: {len(baked)})")
    else:
        ok("#447/ADR-0178: a mirrored untouched cell keeps the recorded rule of its unflipped source key")


def key_set_test(root: Path):
    with_page, _ = make_recorded_pack(root, "keys-with-page")
    without, _ = make_recorded_pack(root, "keys-without-page")
    (without / "textures" / PAGE).unlink()
    out = run("build", str(with_page))
    out2 = run("build", str(without))
    if out is None or out2 is None:
        return
    a = set(by_key(with_page / "textures" / "hires.txt")[1])
    b = set(by_key(without / "textures" / "hires.txt")[1])
    if a != b:
        fail(f"#447: keeping recorded rules changed the key set: +{len(a - b)} -{len(b - a)}")
    else:
        ok(f"#447: the key set is the one the sheets carry, recorded rules or not ({len(a)} keys)")
    _imgs, got = by_key(without / "textures" / "hires.txt")
    if any(v[0] == PAGE for v in got.values()):
        fail("#447: a recorded rule whose page is missing was emitted anyway")
    elif "sheet crop" not in out2 or "#447" not in out2:
        fail(f"#447: the build does not say that untouched cells fell back to the sheet crop:\n{out2[-800:]}")
    else:
        ok("#447: a recorded rule whose page is missing falls back to the sheet crop, and the build says so")


def other_scale_test(root: Path):
    folder, _ = make_recorded_pack(root, "rescaled")
    lines = (folder / "textures" / "hires.txt").read_text(encoding="utf-8")
    (folder / "textures" / "hires.txt").write_text(lines.replace(f"<scale>{SCALE}", "<scale>4"),
                                                   encoding="utf-8")
    out = run("build", str(folder))
    if out is None:
        return
    _imgs, got = by_key(folder / "textures" / "hires.txt")
    if any(v[0] == PAGE for v in got.values()):
        fail("#447: a recording at <scale>4 was re-emitted into a pack the sheets pin at scale 2")
    elif "scale" not in out or "#447" not in out:
        fail(f"#447: the build does not say why the recorded rules were not used:\n{out[-800:]}")
    else:
        ok("#447: a recording at another <scale> than the sheets is not re-emitted, and the build says why")


def auto_layout_test(root: Path):
    folder, rules = make_recorded_pack(root, "layered")
    auto = folder / "auto" / "textures"
    auto.mkdir(parents=True)
    shutil.copy2(folder / "textures" / "hires.txt", auto / "hires.txt")
    (auto / "chr").mkdir()
    shutil.copy2(folder / "textures" / PAGE, auto / PAGE)
    # A prior build already rewrote textures/hires.txt: the recording is only in auto/.
    (folder / "textures" / "hires.txt").write_text(
        (folder / "textures" / "hires.txt").read_text(encoding="utf-8").replace(f"<img>{PAGE}", "<img>old.png"),
        encoding="utf-8")
    if run("build", str(folder)) is None:
        return
    _imgs, got = by_key(folder / "textures" / "hires.txt")
    if any((got.get(("", T.tile_hex(s), T.PAL_HEX)) or ("",))[0] != PAGE for s in range(8)):
        fail("#447: a pack that keeps the recording in auto/textures/ did not re-emit its rules")
    elif (folder / "textures" / SNAPSHOT).exists():
        fail("#447: the recording was already in auto/textures/, and the build wrote a snapshot anyway")
    else:
        ok("#447: a pack that keeps the recording in auto/textures/ reads it from there, no snapshot")


def imported_layout_test(root: Path):
    """Codex #472: the layout `mep_import.py` writes keeps the recording *and*
    its pages under `auto/textures/` only; the upper `textures/` holds just the
    sheets. HdPackLoader resolves every `<img>` against the folder of the
    `hires.txt` that names it, so the emitted upper layer must carry the pages
    it points at: the build copies them up, and the pack still lints."""
    folder, rules = make_recorded_pack(root, "imported")
    textures, auto = folder / "textures", folder / "auto" / "textures"
    (auto / "chr").mkdir(parents=True)
    (textures / "hires.txt").rename(auto / "hires.txt")
    (textures / PAGE).rename(auto / PAGE)
    (textures / "chr").rmdir()
    out = run("build", str(folder))
    if out is None:
        return
    _imgs, got = by_key(textures / "hires.txt")
    bad = [f"{c}{k[:8]}" for (c, k), body in rules.items()
           if (got.get((c, k, T.PAL_HEX)) or ("", []))[0] != PAGE
           or got[(c, k, T.PAL_HEX)][1][1:] != body.split(",")[1:]]
    if bad:
        fail(f"#447: a mep_import project (recording and pages only under auto/textures/) did not "
             f"re-emit {len(bad)} of {len(rules)} recorded rules: {bad[:4]}")
        return
    missing = [rel for rel in parse_rules(textures / "hires.txt")[0] if not (textures / rel).is_file()]
    if missing:
        fail(f"#447: the emitted textures/hires.txt names <img> files absent from textures/: {missing}")
        return
    if (textures / PAGE).read_bytes() != (auto / PAGE).read_bytes():
        fail(f"#447: textures/{PAGE} is not the recorded page from auto/textures/")
        return
    cache = {}
    differ = [f"{c}{k[:8]}" for (c, k), body in rules.items()
              if crop_at(textures, PAGE, *map(int, got[(c, k, T.PAL_HEX)][1][3:5]), cache)
              != [[page_color(int(body.split(",")[3]) // SPAN
                              + 16 * (int(body.split(",")[4]) // SPAN))] * SPAN] * SPAN]
    if differ:
        fail(f"#447: {len(differ)} kept rules of a mep_import project render other pixels than the "
             f"recording: {differ[:4]}")
        return
    lint = subprocess.run([PY, str(MEP_BUILD.parent / "mep_lint.py"), str(folder)],
                          capture_output=True, text=True)
    if lint.returncode != 0:
        fail(f"#447: the built mep_import project does not lint:\n{(lint.stdout + lint.stderr)[-800:]}")
    else:
        ok("#447: a mep_import project re-emits the recorded rules, its pages are copied up into "
           "textures/, and the pack lints")


def chr_rom_test(root: Path):
    folder, rules = make_recorded_pack(root, "chr-rom", chr_rom=True)
    if run("build", str(folder)) is None:
        return
    _imgs, got = by_key(folder / "textures" / "hires.txt")
    bad = [k for (c, k) in rules if (got.get((c, k, T.PAL_HEX)) or ("",))[0] != PAGE]
    if bad:
        fail(f"#447/ADR-0172: an index-keyed untouched cell did not keep its recorded rule: {bad}")
    else:
        ok("#447/ADR-0172: an index-keyed (CHR ROM) untouched cell keeps its recorded rule")


def coverage_baseline_test(root: Path):
    # Every sheet key recorded: the unpainted build names no sheets/ image at all.
    folder, _ = make_recorded_pack(root, "coverage", shapes=range(20))
    if run("build", str(folder)) is None:
        return
    kept = root / "coverage-baseline"
    kept.mkdir()
    shutil.copy2(folder / "textures" / "hires.txt", kept / "hires.txt")
    T.paint(folder, "metatiles.png", 1 * SCALE, 1 * SCALE, 16 * SCALE, 0xFFFF00FF)
    if run("build", str(folder)) is None:
        return
    out = run("check-coverage", str(folder), "--baseline", str(kept / "hires.txt"))
    if out is None:
        return
    # All 20 keys compared, not a vacuous pass over zero sheet-derived keys.
    if "OK:" not in out or "baseline 20 resolved key(s)" not in out:
        fail(f"#447: check-coverage did not compare an unpainted-build baseline's keys:\n{out[-800:]}")
    else:
        ok("#447: check-coverage accepts an unpainted build (recorded rules) as a sheet-derived baseline")


def restored_page_test(root: Path):
    """Codex #472: a build that can use none of the recorded rules (the only
    page is missing) still overwrites `hires.txt`, so it must snapshot the
    recording first; restoring the page brings the recorded rules back."""
    folder, rules = make_recorded_pack(root, "restored-page")
    textures = folder / "textures"
    recording = (textures / "hires.txt").read_bytes()
    page = (textures / PAGE).read_bytes()
    (textures / PAGE).unlink()
    if run("build", str(folder)) is None:
        return
    snap = textures / SNAPSHOT
    if not snap.is_file() or snap.read_bytes() != recording:
        fail(f"#447: a build that could use no recorded rule overwrote hires.txt without keeping "
             f"the recording as textures/{SNAPSHOT}")
    else:
        ok(f"#447: a build that can use no recorded rule still keeps the recording as textures/{SNAPSHOT}")
    (textures / PAGE).write_bytes(page)
    if run("build", str(folder)) is None:
        return
    _imgs, got = by_key(textures / "hires.txt")
    bad = [f"{c}{k[:8]}" for (c, k), body in rules.items()
           if (got.get((c, k, T.PAL_HEX)) or ("", []))[0] != PAGE
           or got[(c, k, T.PAL_HEX)][1][1:] != body.split(",")[1:]]
    if bad:
        fail(f"#447: after the missing page was restored, {len(bad)} of {len(rules)} untouched cells "
             f"did not come back to the recorded rule: {bad[:4]}")
    else:
        ok("#447: restoring a missing page brings every untouched cell back to its recorded rule")


def external_source_test(root: Path):
    """A build keyed from a `--source` that is not a recording (here, one a
    build wrote) still overwrites a recorded `textures/hires.txt`: the
    recording is kept before that happens."""
    folder, rules = make_recorded_pack(root, "external-source")
    textures = folder / "textures"
    recording = (textures / "hires.txt").read_bytes()
    ext = root / "external-source.hires.txt"
    ext.write_bytes(recording + b"# mep_build: rules kept as recorded for untouched cells\n")
    if run("build", str(folder), "--source", str(ext)) is None:
        return
    snap = textures / SNAPSHOT
    if not snap.is_file() or snap.read_bytes() != recording:
        fail(f"#447: a --source build overwrote a recorded textures/hires.txt without keeping it "
             f"as textures/{SNAPSHOT}")
        return
    if run("build", str(folder)) is None:
        return
    _imgs, got = by_key(textures / "hires.txt")
    if any((got.get((c, k, T.PAL_HEX)) or ("",))[0] != PAGE for (c, k) in rules):
        fail("#447: a build after a --source build lost the recorded rules")
    else:
        ok("#447: a --source build keeps a recorded textures/hires.txt, and later builds re-emit its rules")


def fold_follows_recorded_test(root: Path):
    """ADR-0231 §6 with ADR-0230 item 2: a fold rule derived from an untouched
    cell keeps the recording's own line for that key; once the cell is painted,
    the fold points at the cell's crop at the sidecar's Brightness."""
    fold_pal, slot = "0F062A30", 9
    folder, _rules = make_recorded_pack(root, "fold")
    textures = folder / "textures"
    x, y = page_xy(slot)
    recorded_fold = f"0,{T.tile_hex(0)},{fold_pal},{x},{y},1,N,{700000 + slot},0"
    with (textures / "hires.txt").open("a", encoding="utf-8") as fh:
        fh.write(f"<tile>{recorded_fold}\n")
    write_page(textures, range(slot + 1))
    for path in sorted((textures / "sheets").glob("*.json")):
        doc = json.loads(path.read_text(encoding="utf-8"))
        for cell in doc.get("cells") or []:
            for entry in cell.get("tiles") or []:
                if entry and entry.get("tile") == T.tile_hex(0):
                    entry["folds"] = [{"palette": fold_pal, "brightness": 0.75}]
        path.write_text(json.dumps(doc, indent=1), encoding="utf-8")
    if run("build", str(folder)) is None:
        return
    _imgs, got = by_key(textures / "hires.txt")
    fold = got.get(("", T.tile_hex(0), fold_pal))
    if fold is None or fold[0] != PAGE or fold[1][1:] != recorded_fold.split(",")[1:]:
        fail(f"ADR-0231 §6: an untouched cell's fold does not keep the recorded rule: {fold}")
    else:
        ok("ADR-0231 §6: an untouched cell's ADR-0230 fold keeps the recorded rule")
    T.paint(folder, "metatiles.png", 1 * SCALE, 1 * SCALE, 16 * SCALE, 0xFFFF00FF)
    if run("build", str(folder)) is None:
        return
    _imgs, got = by_key(textures / "hires.txt")
    fold = got.get(("", T.tile_hex(0), fold_pal))
    if fold is None or fold[0] != "sheets/metatiles.png" or fold[1][5:7] != ["0.75", "N"]:
        fail(f"ADR-0231 §6: a painted cell's fold does not point at its crop at the fold's Brightness: {fold}")
    else:
        ok("ADR-0231 §6: a painted cell's fold points at its crop at the fold's Brightness")


def report_rows(out: str):
    """`build`'s own report: the per-cell rows and its "and N more" count (#511)."""
    rows = [l for l in out.splitlines() if l.startswith("report: sheets/")]
    m = re.search(r"^report: \.\.\. and (\d+) more$", out, re.M)
    return rows, int(m.group(1)) if m else 0


def manifest_rule(text: str, data: str, pal: str):
    """The bare `<tile>` line the built manifest carries for one key, or None."""
    for line in text.splitlines():
        s = line.strip()
        if not s.startswith("<tile>"):
            continue
        f = [x.strip() for x in s[6:].split(",")]
        if len(f) >= 6 and f[1].upper() == data and f[2].upper() == pal:
            return s
    return None


def cell_rule_report_test(root: Path):
    """Issue #511: the build reports the rule each cell the artist touched
    produced, so criterion 5 is checked without opening `hires.txt`.

    A painted cell gets one row per key it carries, naming the sheet, the cell
    index, the key, the painted crop and the `<tile>` line itself. A cell the
    placer added (`mep_add_cell.py`) is reported before it is painted too, and
    its row carries the rule the build actually wrote — the recorded one, while
    the cell is untouched (ADR-0231). The rows are capped."""
    folder, _rules = make_recorded_pack(root, "report", shapes=range(24))
    sheets = folder / "textures" / "sheets"
    built = folder / "textures" / "hires.txt"
    first = run("build", str(folder))
    if first is None:
        return
    rows, more = report_rows(first)
    if rows or more:
        fail(f"#511: a build with nothing painted reports {len(rows) + more} cell(s)")
    else:
        ok("#511: a build where no cell was painted prints no cell report")

    # Metatile cell 0 (shapes 0..3) sits at (1,1) at 1x.
    T.paint(folder, "metatiles.png", 1 * SCALE, 1 * SCALE, 16 * SCALE, 0xFFFF00FF)
    out = run("build", str(folder))
    if out is None:
        return
    text = built.read_text(encoding="utf-8")
    rows, more = report_rows(out)
    bad = []
    for shape in range(4):
        key = T.tile_hex(shape)
        row = next((r for r in rows if f"tile {key} " in r), None)
        want = manifest_rule(text, key, T.PAL_HEX)
        if row is None:
            bad.append(f"shape {shape}: no row")
        elif ("sheets/metatiles.json cell 0 painted" not in row or f"palette {T.PAL_HEX} " not in row
              or "at crop 2,2: " not in row or want is None or not row.endswith(want)):
            bad.append(f"shape {shape}: {row}")
    if len(rows) != 4 or more:
        fail(f"#511: painting one four-tile cell printed {len(rows)} row(s) and {more} more, expected 4")
    elif bad:
        fail(f"#511: a painted cell's row does not name sheet/cell/key/crop/rule: {bad[:2]}")
    else:
        ok("#511: one row per key of a painted cell — sheet, cell index, key, crop and the <tile> line itself")

    # A cell the placer added, before any paint on it. Shapes 20..23 are in the
    # recording but on no sheet, so a pasted one keeps the recorded rule.
    payload = json.dumps({"count": 1, "tiles": [{"tile": T.tile_hex(23), "palette": T.PAL_HEX}]})
    p = subprocess.run([PY, str(T.REPO / "scripts" / "mep_add_cell.py"), str(folder), "-"],
                       input=payload, capture_output=True, text=True)
    if p.returncode != 0:
        fail(f"#511: mep_add_cell refused the pasted cell: {(p.stdout + p.stderr)[-500:]}")
        return
    out = run("build", str(folder))
    if out is None:
        return
    text = built.read_text(encoding="utf-8")
    rows, _more = report_rows(out)
    key = T.tile_hex(23)
    row = next((r for r in rows if f"tile {key} " in r), None)
    want = manifest_rule(text, key, T.PAL_HEX)
    # index 6 on a 3-column, 16px, gutter-1 sheet is col 0 of row 2: (1,35) at 1x.
    if row is None:
        fail(f"#511: a cell mep_add_cell added is not reported:\n{out[-1200:]}")
    elif ("sheets/metatiles.json cell 6 added" not in row or "at crop 2,70: " not in row
          or want is None or not row.endswith(want)):
        fail(f"#511: the added cell's row does not carry the rule the build wrote: {row}")
    else:
        ok("#511: a cell mep_add_cell placed is reported before it is painted, with the rule it produced")

    # The cap: 20 shapes on metatiles, 8 on obj000.
    cap, _ = make_recorded_pack(root, "report-cap")
    T.paint(cap, "metatiles.png", 0, 0, 400, 0xFFFF00FF)
    T.paint(cap, "obj000.png", 0, 0, 400, 0xFFFF00FF)
    out = run("build", str(cap))
    if out is None:
        return
    rows, more = report_rows(out)
    if len(rows) == 20 and more == 8:
        ok('#511: the report is capped at 20 rows and counts the rest ("... and 8 more")')
    else:
        fail(f"#511: a wholesale repaint printed {len(rows)} row(s) and {more} more, expected 20 + 8")


def main() -> int:
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        untouched_keeps_recorded_test(root)
        painted_then_reverted_test(root)
        mirrored_source_test(root)
        key_set_test(root)
        other_scale_test(root)
        auto_layout_test(root)
        imported_layout_test(root)
        chr_rom_test(root)
        coverage_baseline_test(root)
        restored_page_test(root)
        external_source_test(root)
        fold_follows_recorded_test(root)
        cell_rule_report_test(root)
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
