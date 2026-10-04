#!/usr/bin/env python3
"""Framework-free checks for mep_lint's widescreen-section rules (MEP-v1 §5.5,
ADR-0253 slice W.3).

Builds a tiny MEP pack in a temp dir (pack.json + widescreen/widescreen.json
and the PNGs it names, written with zlib/struct, no fixture files under docs/),
runs `mep_lint.main()` on it and asserts on the exact error/warning messages
the lint emits for `widescreen/widescreen.json`.

Usage: python3 scripts/test_mep_lint_widescreen.py
"""
from __future__ import annotations

import contextlib
import io
import json
import re
import struct
import sys
import tempfile
import zlib
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import mep_lint  # noqa: E402

LINE_RE = re.compile(r"^(error|warning|info)\s+(\S+)  (.*)$")

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"PASS: {msg}")


def tiny_png(width=64, height=240):
    """Minimal valid RGBA PNG (IHDR + one zlib IDAT + IEND). The default is the
    NES Reveal's side art: 64 extra columns per side, frame height 240."""
    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + b"\x00\x00\x00\xff" * width for _ in range(height))
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


PACK_JSON = {
    "mep": "1.8.0",
    "name": "Widescreen test",
    "version": "1.0.0",
    "id": "widescreen-test",
    "license": "CC-BY-4.0",
    "targets": [{"system": "nes", "sha1": "0" * 40}],
    "sections": {"widescreen": {"path": "widescreen/"}},
}

GOOD_WIDESCREEN_JSON = {
    "version": 1,
    "left": "left.png",
    "right": "right.png",
    "screens": [{"id": 12, "left": "screens/12-left.png"}],
}


def run_lint(manifest="__default__", images=("left.png", "right.png", "screens/12-left.png")):
    """Writes the pack and returns (exit_code, [(level, where, msg), ...]).

    `manifest=None` omits widescreen.json entirely; a dict is serialized, a
    string is written verbatim. `images` are the PNG paths to create under
    widescreen/."""
    with tempfile.TemporaryDirectory(prefix="mep_lint_widescreen_") as tmp:
        root = Path(tmp) / "pack"
        (root / "widescreen").mkdir(parents=True)
        (root / "pack.json").write_text(json.dumps(PACK_JSON), encoding="utf-8")
        for rel in images:
            path = root / "widescreen" / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(tiny_png())
        if manifest == "__default__":
            manifest = GOOD_WIDESCREEN_JSON
        if manifest is not None:
            text = manifest if isinstance(manifest, str) else json.dumps(manifest)
            (root / "widescreen" / "widescreen.json").write_text(text, encoding="utf-8")
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = mep_lint.main(["mep_lint.py", str(root)])
        items = []
        for line in out.getvalue().splitlines():
            m = LINE_RE.match(line)
            if m:
                items.append((m.group(1), m.group(2), m.group(3)))
        return code, items


def errors(items):
    return [msg for level, _, msg in items if level == "error"]


def warnings(items):
    return [msg for level, _, msg in items if level == "warning"]


def infos(items):
    return [msg for level, _, msg in items if level == "info"]


def expect_error(name, msg, **kwargs):
    code, items = run_lint(**kwargs)
    errs = errors(items)
    if code == 1 and msg in errs:
        ok(name)
    else:
        fail(f"{name}: expected exit 1 with error {msg!r}, got exit {code}, errors {errs!r}")


def check_valid_pack():
    code, items = run_lint()
    if code == 0 and not errors(items) and not warnings(items):
        ok("valid widescreen pack (default pair + one screen override) lints clean")
    else:
        fail(f"valid widescreen pack: exit {code}, items {items!r}")
    if "widescreen manifest: 2 side image(s), 1 screen override(s)" in infos(items):
        ok("the manifest's own counts are reported as info")
    else:
        fail(f"expected the manifest info line, got {infos(items)!r}")


def check_right_only_and_screens_only():
    code, items = run_lint(manifest={"version": 1, "right": "right.png"}, images=("right.png",))
    if code == 0 and not errors(items):
        ok("a right-only manifest is valid")
    else:
        fail(f"right-only: exit {code}, items {items!r}")
    code, items = run_lint(manifest={"version": 1, "screens": [{"id": 0, "left": "screens/12-left.png"}]},
                           images=("screens/12-left.png",))
    if code == 0 and not errors(items):
        ok("a screens-only manifest with no default pair is valid")
    else:
        fail(f"screens-only: exit {code}, items {items!r}")


def check_missing_manifest():
    expect_error("missing widescreen.json is an error",
                 "section 'widescreen': 'widescreen/widescreen.json' does not exist", manifest=None)


def check_schema():
    code, items = run_lint(manifest="{nope")
    if code == 1 and any(e.startswith("invalid JSON: ") for e in errors(items)):
        ok("malformed widescreen.json is an error")
    else:
        fail(f"malformed widescreen.json: exit {code}, errors {errors(items)!r}")
    expect_error("root not an object", "root must be an object", manifest=[1, 2])
    expect_error("version missing", "'version' is required", manifest={"left": "left.png"})
    expect_error("version 2", "'version' must be 1", manifest={**GOOD_WIDESCREEN_JSON, "version": 2})
    expect_error("version string", "'version' must be 1", manifest={**GOOD_WIDESCREEN_JSON, "version": "1"})
    expect_error("left not a string", "'left' must be a string", manifest={**GOOD_WIDESCREEN_JSON, "left": 4})
    expect_error("left not a png", "'left' must be a .png path", manifest={**GOOD_WIDESCREEN_JSON, "left": "left.bmp"})
    expect_error("right unsafe", "'right' is unsafe: ../right.png", manifest={**GOOD_WIDESCREEN_JSON, "right": "../right.png"})
    expect_error("left does not resolve", "'left' does not exist: left.png",
                 manifest={**GOOD_WIDESCREEN_JSON}, images=("right.png", "screens/12-left.png"))
    expect_error("no image at all", "widescreen.json names no image", manifest={"version": 1})


def check_screens():
    base = {"version": 1, "left": "left.png"}
    expect_error("screens not an array", "'screens' must be an array", manifest={**base, "screens": {}})
    expect_error("screen not an object", "screens[0] must be an object", manifest={**base, "screens": ["x"]})
    expect_error("screen id missing", "screens[0].id is required", manifest={**base, "screens": [{"left": "left.png"}]})
    expect_error("screen id negative", "screens[0].id must be an integer >= 0",
                 manifest={**base, "screens": [{"id": -1, "left": "left.png"}]})
    expect_error("screen id bool", "screens[0].id must be an integer >= 0",
                 manifest={**base, "screens": [{"id": True, "left": "left.png"}]})
    expect_error("duplicate screen id", "screens[1].id 3 is duplicated",
                 manifest={**base, "screens": [{"id": 3, "left": "left.png"}, {"id": 3, "right": "right.png"}]})
    expect_error("screen names no image", "screens[0] names no image", manifest={**base, "screens": [{"id": 1}]})
    expect_error("screen path does not resolve", "screens[0].left does not exist: screens/99-left.png",
                 manifest={**base, "screens": [{"id": 1, "left": "screens/99-left.png"}]})


def check_case_insensitive_extension():
    code, items = run_lint(manifest={"version": 1, "left": "left.PNG"}, images=("left.PNG",))
    if code == 0 and not errors(items):
        ok("the .png extension is matched case-insensitively")
    else:
        fail(f"uppercase extension: exit {code}, items {items!r}")


def check_multiple_errors_reported_together():
    code, items = run_lint(manifest={"version": 2, "left": "a.bmp", "screens": ["x"]})
    errs = set(errors(items))
    want = {
        "'version' must be 1",
        "'left' must be a .png path",
        "screens[0] must be an object",
    }
    if code == 1 and want <= errs:
        ok("every schema violation is reported in one pass")
    else:
        fail(f"combined violations: exit {code}, missing {want - errs!r}")


def check_lint_widescreen_json_unit():
    # Direct unit call keeps the rule usable outside a pack (e.g. by other tools).
    rep = mep_lint.Report()
    mep_lint.lint_widescreen_json(GOOD_WIDESCREEN_JSON, "widescreen", _AlwaysThere(), "widescreen/widescreen.json", rep)
    if rep.errors == 0 and rep.warnings == 0:
        ok("lint_widescreen_json accepts the §5.5 example shape")
    else:
        fail(f"lint_widescreen_json on good input: {rep.items!r}")
    rep = mep_lint.Report()
    mep_lint.lint_widescreen_json("string", "widescreen", _AlwaysThere(), "widescreen/widescreen.json", rep)
    if rep.errors == 1 and rep.items[0][2] == "root must be an object":
        ok("lint_widescreen_json rejects a non-object root with a single error")
    else:
        fail(f"lint_widescreen_json on string: {rep.items!r}")


class _AlwaysThere:
    """A Source stand-in: every referenced path resolves."""

    def exists(self, _path):
        return True


def main():
    check_valid_pack()
    check_right_only_and_screens_only()
    check_missing_manifest()
    check_schema()
    check_screens()
    check_case_insensitive_extension()
    check_multiple_errors_reported_together()
    check_lint_widescreen_json_unit()
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        return 1
    print("\nall widescreen lint checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
