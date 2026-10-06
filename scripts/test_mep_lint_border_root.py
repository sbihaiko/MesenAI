#!/usr/bin/env python3
"""Framework-free checks for mep_lint's bare root `border.png` probe
(MEP-v1 §5.4, PRD Part A §4 Phase 8 F8.4).

MEP-v1 §5.4 says the border section's `path` is a directory (`"path": ""` is
the container root) and, for the folder convention, that "Hosts MAY
additionally accept a bare `border.png` directly at the (human) root as a
`border` section with `path` `""` — the reference implementation does
(`MepPack::DetectConventionLayout`), mirroring the bare-`hires.txt` rule of
§2.1 rule 9". The same section states the lint side of the gap: the
validators "enforce the schema above at submission time (`scripts/mep_lint.py`
errors)" while `scripts/mep_lint.py` "currently recognizes only the `border/`
and `auto/border/` probes.

These checks pin the fix: a bare root `border.png` is discovered as the human
layer of 'border' with path `""`, so its PNG is decoded and its `border.json`
is schema-checked like any other border, a conforming bare-root border pack
stays valid, and neither a declared `border/` layout nor the ADR-0120
fallback discovery changes verdict.

Usage: python3 scripts/test_mep_lint_border_root.py
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


def tiny_png(width=16, height=9):
    """Minimal valid RGBA PNG (IHDR + one zlib IDAT + IEND)."""
    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + b"\x00\x00\x00\xff" * width for _ in range(height))
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


GOOD_BORDER_JSON = {
    "version": 1,
    "width": 16,
    "height": 9,
    "viewport": {"x": 2, "y": 0, "width": 12, "height": 9},
    "scale_mode": "fit",
    "underlay": False,
}

PACK_JSON = {
    "mep": "1.5.0",
    "name": "Border root test",
    "version": "1.0.0",
    "id": "border-root-test",
    "license": "CC-BY-4.0",
    "targets": [{"system": "nes", "sha1": "0" * 40}],
    "sections": {"border": {"path": "border/"}},
}


def run_lint(populate):
    """Builds a temp container, lets `populate(root)` write it, lints it.

    Returns (exit_code, [(level, where, msg), ...]). No pack.json is written
    unless `populate` writes one, so the folder convention is what is under
    test."""
    with tempfile.TemporaryDirectory(prefix="mep_lint_border_root_") as tmp:
        root = Path(tmp) / "pack"
        root.mkdir(parents=True)
        populate(root)
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
    return [(where, msg) for level, where, msg in items if level == "error"]


def infos(items):
    return [(where, msg) for level, where, msg in items if level == "info"]


def check_border_only_pack():
    """A container whose only content is a bare root border.png is a border
    section (MEP-v1 §5.4: "A pack MAY consist of a `border` section alone"),
    not a pack with no section at all."""
    code, items = run_lint(lambda root: (root / "border.png").write_bytes(tiny_png()))
    errs = errors(items)
    if code == 0 and not errs:
        ok("bare root border.png alone is discovered as the 'border' section")
    else:
        fail(f"border-only pack: exit {code}, errors {errs!r}")
    if ("border.png", "border frame PNG 16x9") in infos(items):
        ok("the bare root border.png is decoded and reported")
    else:
        fail(f"expected 'border frame PNG 16x9' for border.png, got infos {infos(items)!r}")


def check_bare_root_png_is_decoded():
    """The bare root file is linted, not silently accepted: a corrupt
    border.png at the root is an error naming that file."""
    code, items = run_lint(lambda root: (root / "border.png").write_bytes(b"not a png at all, just bytes..."))
    errs = errors(items)
    if code == 1 and ("border.png", "invalid or corrupt PNG file") in errs:
        ok("a corrupt bare root border.png is an error")
    else:
        fail(f"corrupt bare root border.png: exit {code}, errors {errs!r}")


def check_bare_root_border_json_schema():
    """MEP-v1 §5.4: the validators enforce the border.json schema at
    submission time — including when the border lives at the bare root."""
    def populate(root):
        (root / "border.png").write_bytes(tiny_png())
        (root / "border.json").write_text(
            json.dumps({k: v for k, v in GOOD_BORDER_JSON.items() if k != "width"}), encoding="utf-8")

    code, items = run_lint(populate)
    errs = errors(items)
    if code == 1 and ("border.json", "'width' is required") in errs:
        ok("a bare root border.json missing 'width' is an error")
    else:
        fail(f"bare root border.json schema: exit {code}, errors {errs!r}")


def check_bare_root_valid_pack_stays_valid():
    """A conforming bare-root border pack (PNG + schema-valid border.json)
    lints clean."""
    def populate(root):
        (root / "border.png").write_bytes(tiny_png())
        (root / "border.json").write_text(json.dumps(GOOD_BORDER_JSON), encoding="utf-8")

    code, items = run_lint(populate)
    errs = errors(items)
    if code == 0 and not errs:
        ok("a valid bare root border.png + border.json lints clean")
    else:
        fail(f"valid bare root border pack: exit {code}, errors {errs!r}")


def check_declared_layout_wins():
    """A declared/convention `border/` section still wins over a stray bare
    root border.png — the section is resolved once, not per entry."""
    def populate(root):
        (root / "pack.json").write_text(json.dumps(PACK_JSON), encoding="utf-8")
        (root / "border").mkdir()
        (root / "border" / "border.png").write_bytes(tiny_png())
        (root / "border.png").write_bytes(b"not a png at all, just bytes...")

    code, items = run_lint(populate)
    errs = errors(items)
    if code == 0 and not errs:
        ok("border/border.png wins over a stray bare root border.png (still exit 0)")
    else:
        fail(f"declared border/ layout: exit {code}, errors {errs!r}")
    if ("border/border.png", "border frame PNG 16x9") in infos(items):
        ok("the resolved section is the border/ probe")
    else:
        fail(f"expected the border/ probe to be the resolved section, got infos {infos(items)!r}")


def check_fallback_still_wins():
    """A container that wraps the real pack in a subfolder keeps discovering
    that subfolder: a stray bare root border.png must not turn it into a
    border-only pack (ADR-0120 fallback precedence is unchanged)."""
    def populate(root):
        (root / "border.png").write_bytes(tiny_png())
        (root / "Rel-v1").mkdir()
        (root / "Rel-v1" / "pack.json").write_text(json.dumps({"sections": {}}), encoding="utf-8")

    code, items = run_lint(populate)
    if not [e for e in errors(items) if e[0] == "border.png"]:
        ok("the ADR-0120 fallback still wins over a stray bare root border.png")
    else:
        fail(f"fallback precedence broken: errors {errors(items)!r}")


def main():
    check_border_only_pack()
    check_bare_root_png_is_decoded()
    check_bare_root_border_json_schema()
    check_bare_root_valid_pack_stays_valid()
    check_declared_layout_wins()
    check_fallback_still_wins()
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        return 1
    print("\nall bare root border.png lint checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
