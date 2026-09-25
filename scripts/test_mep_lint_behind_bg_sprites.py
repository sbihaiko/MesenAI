#!/usr/bin/env python3
"""mep_lint accepts the ADR-0224 opt-in tag `<bgPreservesBehindBgSprites>`
(F12.15): no "unknown tag" warning for the bare line, nothing said about its
absence, a warning (never an error) when it carries a stray argument, and the
GB/SMS branch is untouched (the tag is NES-only, so <ver>2xx still flags it).

Usage: python3 scripts/test_mep_lint_behind_bg_sprites.py
"""
from __future__ import annotations

import sys
import tempfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import mep_lint  # noqa: E402

FAILURES: list[str] = []
PNG_1x1 = bytes.fromhex(
    "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489"
    "0000000d49444154789c63f8ffff3f0005fe02fea72d3e4a0000000049454e44ae426082"
)
TAG = "bgPreservesBehindBgSprites"


def check(cond, msg, extra=""):
    print(("PASS: " if cond else "FAIL: ") + msg + (f" -- {extra}" if extra and not cond else ""))
    if not cond:
        FAILURES.append(msg)


def _lint(hires: str) -> mep_lint.Report:
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "textures").mkdir()
        (root / "textures" / "hires.txt").write_text(hires)
        (root / "textures" / "tiles.png").write_bytes(PNG_1x1)
        rep = mep_lint.Report()
        mep_lint.lint_nes_hires(mep_lint.Source(root), "textures/hires.txt", rep)
        return rep


def _msgs(rep, level=None):
    return [m for lv, _, m in rep.items if level is None or lv == level]


def main() -> int:
    base = "<ver>106\n<scale>1\n<img>tiles.png\n"

    rep = _lint(base + f"<{TAG}>\n")
    msgs = _msgs(rep)
    check(not any("unknown tag" in m for m in msgs), "the bare tag is not an unknown tag", str(msgs))
    check(not rep.errors, "the bare tag is not an error", str(msgs))

    rep = _lint(base)
    msgs = _msgs(rep)
    check(not any(TAG in m for m in msgs), "its absence is never reported", str(msgs))

    rep = _lint(base + f"<{TAG}>yes\n")
    msgs = _msgs(rep)
    check(any(TAG in m and "no arguments" in m for m in _msgs(rep, "warning")),
          "a stray argument is a warning", str(msgs))
    check(not rep.errors, "a stray argument is not an error", str(msgs))

    check(TAG in mep_lint.NES_TAGS and TAG not in mep_lint.GBSMS_TAGS,
          "the tag is NES-only: in NES_TAGS, not in GBSMS_TAGS")

    print("ALL PASS" if not FAILURES else f"{len(FAILURES)} FAILURE(S)")
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
