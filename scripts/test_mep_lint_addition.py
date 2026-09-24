#!/usr/bin/env python3
"""Framework-free checks for mep_lint's `<addition>` rules (ADR-0196 §4,
Slice F12.5).

Writes a tiny NES texture pack in a temp dir — one hires.txt, one PNG and one
ADR-0153 sheet sidecar — runs `mep_lint.lint_nes_hires()` on it and asserts on
the exact messages ADR-0196 §4 asks for: an anchor no `<tile>` rule keys, a
target no `<tile>` rule keys, a target the sidecars do not mark synthetic, a
target that fails §3's check for the pack's key kind, `ignorePalette`, a
condition prefix the loader ignores, and a manifest too old for the tag.

Usage: python3 scripts/test_mep_lint_addition.py
"""
from __future__ import annotations

import json
import struct
import sys
import tempfile
import zlib
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import mep_addition  # noqa: E402
import mep_lint  # noqa: E402

FAILURES = []

ANCHOR = ("007EFFFFE3E70000007E817E9D18FFFF", "FF36160F")
TARGET = (mep_addition.chr_ram_target(1), mep_addition.RESERVED_PALETTE)


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"PASS: {msg}")


def tiny_png(width=64, height=64):
    """Minimal valid RGBA PNG (IHDR + one zlib IDAT + IEND)."""
    def chunk(tag, body):
        return (struct.pack(">I", len(body)) + tag + body
                + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF))

    raw = b"".join(b"\x00" + b"\x00\x00\x00\xff" * width for _ in range(height))
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


def pack(lines, sidecar=None, version=109):
    """A folder holding textures/hires.txt with `lines` appended to a minimal
    header, one image, and `sidecar` written as textures/sheets/usr000.json
    when given. Returns the lint report's items."""
    root = Path(tempfile.mkdtemp())
    tex = root / "textures"
    (tex / "sheets").mkdir(parents=True)
    (tex / "usr000.png").write_bytes(tiny_png())
    head = [f"<ver>{version}", "<scale>1", "<supportedRom>" + "0" * 40,
            "<img>usr000.png"]
    (tex / "hires.txt").write_text("\n".join(head + lines) + "\n", encoding="utf-8")
    if sidecar is not None:
        (tex / "sheets" / "usr000.json").write_text(json.dumps(sidecar), encoding="utf-8")
    rep = mep_lint.Report()
    mep_lint.lint_nes_hires(mep_lint.Source(root), "textures/hires.txt", rep)
    return rep.items


def messages(items, level=None):
    return [m for lv, _where, m in items if level is None or lv == level]


def has(items, level, needle):
    return any(needle in m for m in messages(items, level))


def tile(key, x=0, y=0, default="N", prefix=""):
    return f"{prefix}<tile>0,{key[0]},{key[1]},{x},{y},1,{default}"


def addition(anchor=ANCHOR, offset=(16, -24), target=TARGET, extra="", prefix=""):
    return (f"{prefix}<addition>{anchor[0]},{anchor[1]},{offset[0]},{offset[1]},"
            f"{target[0]},{target[1]}{extra}")


def sidecar(key=TARGET, synthetic=True):
    return {"cells": [{"index": 0, "synthetic": synthetic,
                       "tiles": [{"tile": key[0], "palette": key[1]}]}]}


def check(cond, msg):
    ok(msg) if cond else fail(msg)


# -- the happy path -----------------------------------------------------------

def check_valid_addition():
    items = pack([tile(ANCHOR), tile(TARGET, 8, 0), addition()], sidecar())
    check(not messages(items, "error"),
          f"a well-formed CHR RAM addition lints clean: {messages(items, 'error')}")
    check(has(items, "info", "1 additions"),
          "the info line counts the pack's additions")


# -- ADR-0196 section 4's three refusals ---------------------------------------

def check_anchor_not_keyed():
    items = pack([tile(TARGET, 8, 0), addition()], sidecar())
    check(has(items, "error", "keyed by no <tile> rule in this manifest"),
          "an anchor no <tile> rule keys is an error — the tag can never fire")


def check_target_not_keyed():
    items = pack([tile(ANCHOR), addition()], sidecar())
    check(has(items, "error", "the overflow has no art to draw"),
          "a target no <tile> rule keys is an error")


def check_target_not_marked_synthetic():
    items = pack([tile(ANCHOR), tile(TARGET, 8, 0), addition()],
                 sidecar(synthetic=False))
    check(has(items, "error", "not marked synthetic in any sheet sidecar"),
          "a target the sidecars do not mark synthetic is an error")


def check_no_sidecar_is_a_warning():
    items = pack([tile(ANCHOR), tile(TARGET, 8, 0), addition()])
    check(has(items, "warning", "ships no sheet sidecars"),
          "a pack with no sidecar at all gets a warning, not an error")
    check(not has(items, "error", "not marked synthetic"),
          "and not the marked-synthetic error it cannot check")


# -- ADR-0196 section 3, as the linter can check it ----------------------------

def check_unreserved_palette():
    bad = (TARGET[0], "FF36160F")
    items = pack([tile(ANCHOR), tile(bad, 8, 0), addition(target=bad)], sidecar(bad))
    check(has(items, "error", f"is not the reserved {mep_addition.RESERVED_PALETTE}"),
          "a CHR RAM target whose palette is a recorded one is refused")


def check_unreserved_pattern():
    bad = ("0" * 32, mep_addition.RESERVED_PALETTE)
    items = pack([tile(ANCHOR), tile(bad, 8, 0), addition(target=bad)], sidecar(bad))
    check(has(items, "error", "is not the reserved pattern"),
          "a CHR RAM target that is sixteen zero bytes is refused")


def check_chr_rom_target_inside_chr():
    """On an index-keyed pack the pack-local form of §3 is "past every index
    the manifest itself names"."""
    anchor = ("0412", "FF0F3919")
    good = ("2000", mep_addition.RESERVED_PALETTE)
    inside = ("0100", mep_addition.RESERVED_PALETTE)
    items = pack([tile(anchor), tile(("1FFF", "FF0F3919"), 8, 0), tile(good, 16, 0),
                  addition(anchor=anchor, target=good)], sidecar(good))
    check(not messages(items, "error"),
          f"an index past the pack's own largest keyed index passes: {messages(items, 'error')}")
    items = pack([tile(anchor), tile(("1FFF", "FF0F3919"), 8, 0), tile(inside, 16, 0),
                  addition(anchor=anchor, target=inside)], sidecar(inside))
    check(has(items, "error", "not past the pack's own CHR"),
          "an index the pack itself keys is not provably unmatched")


# -- #382: keys compare by value, the loader's way -----------------------------

REAL = ("00", "FF161927")
SYNTH = ("0217", "FF000464")
ROM_SIDECAR = {"cells": [{"index": 0, "synthetic": True,
                          "tiles": [{"tile": "0" * 32, "palette": SYNTH[1],
                                     "index": 0x217}]}]}


def check_padded_index_tokens_are_one_key():
    """`000`/`00` and `217`/`0217` are one CHR index at <ver>103+ —
    HdPackLoader::ReadTileData FromHex's both — so an <addition> spelt the
    legacy way is keyed by a <tile> rule mep_build re-spelt (#382)."""
    items = pack([tile(REAL), tile(SYNTH, 8, 0),
                  addition(anchor=("000", REAL[1]), target=("217", SYNTH[1]))], ROM_SIDECAR)
    check(not messages(items, "error"),
          f"a legacy-padded anchor/target is keyed by the build's tokens: {messages(items, 'error')}")
    items = pack([tile(("000", REAL[1])), tile(("217", SYNTH[1]), 8, 0),
                  addition(anchor=REAL, target=("0217", SYNTH[1]))], ROM_SIDECAR)
    check(not messages(items, "error"),
          f"and the other way round — the build's tokens cite legacy-padded rules: {messages(items, 'error')}")


def check_padded_palette_is_one_key():
    """The palette half is FromHex'd too, so a 7-digit spelling names the
    8-digit key."""
    items = pack([tile(("00", "0F161927")), tile(SYNTH, 8, 0),
                  addition(anchor=("00", "F161927"), target=SYNTH)], ROM_SIDECAR)
    check(not messages(items, "error"),
          f"a 7-digit palette token is padded to the <tile> rule's 8: {messages(items, 'error')}")


def check_chr_ram_pattern_is_never_an_index():
    """32 hex digits are pattern data (ReadTileData: `size() >= 32`), so a
    pattern of zeros ending in 01 does not key CHR index `01`, and the pattern
    itself is untouched by the index re-spelling."""
    pattern = ("0" * 30 + "01", "FF161927")
    items = pack([tile(("01", "FF161927")), tile(TARGET, 8, 0), addition(anchor=pattern)],
                 sidecar())
    check(has(items, "error", "anchor " + pattern[0] + "/" + pattern[1] + " is keyed by no <tile> rule"),
          "a 32-hex pattern is not conflated with the CHR index its digits spell")
    items = pack([tile(ANCHOR), tile(TARGET, 8, 0),
                  addition(anchor=(ANCHOR[0].lower(), ANCHOR[1]))], sidecar())
    check(not messages(items, "error"),
          f"a CHR RAM pattern compares case-insensitively and otherwise verbatim: {messages(items, 'error')}")


def check_decimal_index_below_ver103():
    """Below <ver>103 the loader reads a short tileData field with std::stoi —
    decimal — so `10` is index 10 (`0A`), not 16. The tag itself needs 107+,
    so the version rule is exercised on the shared helper and on the lint's
    duplicate-<tile> pass, which keys by the same canonical form."""
    check(mep_addition.canonical_key(("10", "FF161927"), 102) == ("0A", "FF161927"),
          "at <ver>102 a short token is decimal: 10 -> index 0A")
    check(mep_addition.canonical_key(("10", "FF161927"), 103) == ("10", "FF161927"),
          "at <ver>103 the same token is hex: 10 -> index 10")
    items = pack([tile(("9", "FF161927")), tile(("09", "FF161927"), 8, 0)], version=102)
    check(has(items, "warning", "1 duplicate <tile>"),
          "a <ver>102 pack's `9` and `09` are one decimal key — reported as a duplicate")
    check(not any("Traceback" in m for m in messages(items)),
          "a <ver>102 manifest lints without a crash")


def check_unkeyed_index_anchor_still_reported():
    """The by-value compare does not make every index keyed: an anchor whose
    index no <tile> rule names is still ADR-0196 §4's first refusal."""
    items = pack([tile(REAL), tile(SYNTH, 8, 0),
                  addition(anchor=("01", REAL[1]), target=("217", SYNTH[1]))], ROM_SIDECAR)
    check(has(items, "error", "anchor 01/" + REAL[1] + " is keyed by no <tile> rule in this manifest"),
          "an anchor index no <tile> rule keys is still an error, in the author's own spelling")
    items = pack([tile(REAL), tile(SYNTH, 8, 0),
                  addition(anchor=("000", REAL[1]), target=("218", SYNTH[1]))], ROM_SIDECAR)
    check(has(items, "error", "target 218/" + SYNTH[1] + " is keyed by no <tile> rule"),
          "a target index no <tile> rule keys is still an error")


# -- #386: a defaultTile=Y rule keys every palette of its index ---------------

HIGH_SYNTH = ("2000", SYNTH[1])     # past Metroid's own 1066/1072 anchors
HIGH_SIDECAR = {"cells": [{"index": 0, "synthetic": True,
                           "tiles": [{"tile": "0" * 32, "palette": SYNTH[1],
                                      "index": 0x2000}]}]}

def check_default_tile_keys_every_palette():
    """HdPackLoader::InitializeHdPack also files a defaultTile=Y rule under
    its tileData with palette FFFFFFFF, and HdNesPack::GetMatchingTile falls
    back to that key — so the rule draws its index under any palette, and an
    anchor keyed only that way can fire (#386, Metroid's `1066/0F162700`)."""
    items = pack([tile(("1066", "0F162000"), default="Y"), tile(HIGH_SYNTH, 8, 0),
                  addition(anchor=("1066", "0F162700"), target=HIGH_SYNTH)], HIGH_SIDECAR)
    check(not messages(items, "error"),
          f"an anchor keyed only by a defaultTile=Y rule under another palette lints clean: {messages(items, 'error')}")
    items = pack([tile(("1066", "0F162000"), default="yes",
                       prefix="<condition>c1,frameRange,0,1\n[c1]"),
                  tile(HIGH_SYNTH, 8, 0),
                  addition(anchor=("1066", "0F162700"), target=HIGH_SYNTH)], HIGH_SIDECAR)
    check(not messages(items, "error"),
          f"any loader spelling of Y counts, and a condition prefix does not hide the key: {messages(items, 'error')}")


def check_non_default_tile_keeps_palette():
    """A defaultTile=N rule is filed under its exact key only, so the palette
    half of the anchor check still bites."""
    items = pack([tile(("1066", "0F162000")), tile(HIGH_SYNTH, 8, 0),
                  addition(anchor=("1066", "0F162700"), target=HIGH_SYNTH)], HIGH_SIDECAR)
    check(has(items, "error", "anchor 1066/0F162700 is keyed by no <tile> rule in this manifest"),
          "an anchor keyed by a defaultTile=N rule under another palette is still an error")
    items = pack([tile(("1067", "0F162000"), default="Y"), tile(HIGH_SYNTH, 8, 0),
                  addition(anchor=("1066", "0F162700"), target=HIGH_SYNTH)], HIGH_SIDECAR)
    check(has(items, "error", "anchor 1066/0F162700 is keyed by no <tile> rule"),
          "a defaultTile=Y rule on another index keys nothing for this one")


def check_default_tile_with_padded_index():
    """The wildcard is filed under #382's canonical index, so `012E` on the
    rule and `12E` on the anchor (Metroid's own spelling) are one key."""
    items = pack([tile(("012E", "FF161920"), default="Y"), tile(SYNTH, 8, 0),
                  addition(anchor=("12E", "FF161926"), target=SYNTH)], ROM_SIDECAR)
    check(not messages(items, "error"),
          f"a padded defaultTile=Y rule keys the unpadded anchor under any palette: {messages(items, 'error')}")
    items = pack([tile(("12E", "FF161920"), default="Y"), tile(SYNTH, 8, 0),
                  addition(anchor=("0012E", "FF161926"), target=SYNTH)], ROM_SIDECAR)
    check(not messages(items, "error"),
          f"and the other way round: {messages(items, 'error')}")


def check_default_tile_keys_target():
    """The overflow sprite is drawn through GetMatchingTile too, so a target
    whose index only a defaultTile=Y rule keys has art — but a synthetic
    target's default key never widens the §3 "past the pack's own CHR" floor."""
    items = pack([tile(REAL), tile((SYNTH[0], "FF000000"), 8, 0, default="Y"),
                  addition(anchor=REAL, target=SYNTH)], ROM_SIDECAR)
    check(not has(items, "error", "the overflow has no art to draw"),
          f"a target keyed only by a defaultTile=Y rule has art: {messages(items, 'error')}")
    items = pack([tile(REAL), tile(SYNTH, 8, 0, default="Y"),
                  addition(anchor=REAL, target=SYNTH)], ROM_SIDECAR)
    check(not messages(items, "error"),
          f"a defaultTile=Y synthetic target is not counted as real CHR: {messages(items, 'error')}")


# -- the tag's own limits ------------------------------------------------------

def check_ignore_palette_refused():
    items = pack([tile(ANCHOR), tile(TARGET, 8, 0), addition(extra=",Y")], sidecar())
    check(has(items, "error", "keeps the palette half of a synthetic key load-bearing"),
          "ignorePalette is refused outright — it is what lets the key collide")


def check_version_floor():
    items = pack([tile(ANCHOR), tile(TARGET, 8, 0), addition()], sidecar(), version=106)
    check(has(items, "error", f"requires <ver>{mep_addition.MIN_VERSION}+"),
          "a manifest older than 107 cannot carry the tag at all")
    items = pack([tile(ANCHOR), tile(TARGET, 8, 0), addition(extra=",N")], sidecar(),
                 version=107)
    check(has(items, "error", f"requires <ver>{mep_addition.IGNORE_PALETTE_VERSION}+"),
          "the seventh field needs 108, even when it is off")


def check_condition_prefix_warns():
    items = pack(["<condition>c1,spriteNearby,0,0,3",
                  tile(ANCHOR), tile(TARGET, 8, 0), addition(prefix="[c1]")], sidecar())
    check(has(items, "warning", "ProcessAdditionTag ignores it"),
          "a condition prefix on <addition> is a warning — the loader drops it")


def check_offscreen_offset_warns():
    items = pack([tile(ANCHOR), tile(TARGET, 8, 0), addition(offset=(400, 0))], sidecar())
    check(has(items, "warning", "is larger than the screen"),
          "an offset that can never land on screen is a warning")


def check_malformed_line():
    items = pack([tile(ANCHOR), tile(TARGET, 8, 0), "<addition>a,b,c"], sidecar())
    check(has(items, "error", "fields"),
          "a line with the wrong field count is one parse error, not a crash")


def main():
    check_valid_addition()
    check_anchor_not_keyed()
    check_target_not_keyed()
    check_target_not_marked_synthetic()
    check_no_sidecar_is_a_warning()
    check_unreserved_palette()
    check_unreserved_pattern()
    check_chr_rom_target_inside_chr()
    check_padded_index_tokens_are_one_key()
    check_padded_palette_is_one_key()
    check_chr_ram_pattern_is_never_an_index()
    check_decimal_index_below_ver103()
    check_unkeyed_index_anchor_still_reported()
    check_default_tile_keys_every_palette()
    check_non_default_tile_keeps_palette()
    check_default_tile_with_padded_index()
    check_default_tile_keys_target()
    check_ignore_palette_refused()
    check_version_floor()
    check_condition_prefix_warns()
    check_offscreen_offset_warns()
    check_malformed_line()
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        return 1
    print("\nall addition lint checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
