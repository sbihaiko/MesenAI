#!/usr/bin/env python3
"""The library tile never draws a label wider than the tile (W-P19, ADR-0264).

`library_tile()` (scripts/render_gui_wireframes.py) truncates a title that does
not fit its tile and appends '…'. The bug (#1058): the truncation loop measured
the bare title and then drew `title + '…'`, so every truncated label overflowed
the tile by exactly the ellipsis advance.

The test drives the real `library_tile()` through a stub canvas whose text
metric is monospace — an independent source of truth that does not care how the
function truncates — and asserts on the string the function actually hands to
`text()`: that string must fit the tile width. It fails on the old measurement
(the drawn label is one glyph wider than the width the loop measured) and passes
on the corrected one. Running the function, not a re-implementation of the loop,
is what keeps it honest: a hand-copied expected label would pass by construction.

The stub keeps the test hermetic: no macOS system font, so it also runs in the
`python-tests` CI job on ubuntu.

Usage:
  python3 scripts/test_render_gui_wireframes.py
"""
from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import render_gui_wireframes as R  # noqa: E402  (needs the sys.path line above)

# Stub advance per glyph, in logical px. Monospace so the test owns the metric.
CHAR = 7.0

# The grid tile width library_sheet() computes for the W-P19 sheet: 8 columns,
# 14 px gaps inside the sheet's 24 px margins (scripts/render_gui_wireframes.py,
# library_sheet). 954 / 8.
TILE_W = 119.25
TILE_H = TILE_W * 4 / 3


class _NoopDraw:
    """ImageDraw stand-in: `library_tile` reaches `c.d.rounded_rectangle`."""

    def __getattr__(self, name):
        return lambda *args, **kwargs: None


class StubCanvas:
    """The smallest slice of Canvas `library_tile` draws through.

    `tw` is the only metric, and it is monospace; `text` records every string
    the function draws so the test can assert on what came out.
    """

    def __init__(self):
        self.drawn = []  # (string, size, weight), in draw order
        self.d = _NoopDraw()
        self.shadow = self.scene = self.rrect = self._noop

    @staticmethod
    def _noop(*args, **kwargs):
        return None

    def tw(self, s, size=13, weight=400):
        return len(s) * CHAR

    def text(self, x, y, s, size=13, weight=400, color=None, anchor="la"):
        self.drawn.append((s, size, weight))
        return self.tw(s, size, weight)


def draw_tile(title, kind="art", w=TILE_W):
    """Draw one tile and return (stub canvas, the label string it drew)."""
    c = StubCanvas()
    R.library_tile(c, 24, 100, w, TILE_H, title, "NES", 1, kind)
    return c, c.drawn[0][0]


def check(failures, ok, label):
    if ok:
        print(f"ok   [{label}]")
    else:
        print(f"FAIL [{label}]")
    return failures + (0 if ok else 1)


def main():
    failures = 0

    # A title far too long for the tile: the drawn label must still fit, and it
    # must say it was cut. Under the old measurement this label was one glyph
    # wider than TILE_W.
    title = "The Legend of Zelda: Oracle of Ages"
    c, label = draw_tile(title)
    width = c.tw(label)
    failures = check(failures, width <= TILE_W,
                     f"truncated label fits the tile ({width:.1f} <= {TILE_W})")
    failures = check(failures, label.endswith("…"),
                     f"truncated label carries the ellipsis ({label!r})")
    failures = check(failures, title.startswith(label[:-1]),
                     f"truncated label is a prefix of the title ({label!r})")

    # Every string the tile drew fits: the label and the console tag below it.
    over = [(s, c.tw(s, size, weight)) for s, size, weight in c.drawn
            if c.tw(s, size, weight) > TILE_W]
    failures = check(failures, not over, f"no drawn string overflows the tile ({over})")

    # A title that fits is drawn whole and stays bare: no ellipsis appears just
    # because the loop ran.
    short = "Metroid"
    c, label = draw_tile(short)
    failures = check(failures, label == short, f"a fitting title is drawn unchanged ({label!r})")

    # The same long title in a tile too narrow even for the ellipsis: the tile
    # still never draws past its width while it can shorten the label.
    c, label = draw_tile(title, w=60)
    failures = check(failures, c.tw(label) <= 60,
                     f"a narrow tile keeps its label inside ({c.tw(label):.1f} <= 60)")

    print()
    if failures:
        print(f"FAIL: {failures} of 6 case(s) did not hold")
        return 1
    print("PASS: 6 case(s) - the library tile draws no label wider than the tile")
    return 0


if __name__ == "__main__":
    sys.exit(main())
