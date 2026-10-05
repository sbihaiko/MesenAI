#!/usr/bin/env python3
"""Every renderer that runs a librashader chain must consult IsLookCompare.

ADR-0246 §5 (Hold to Compare): while the button is held the shader chain is
bypassed rather than rebuilt, because the measured swap stutters (crt-geom
`SetShader` 67.6 ms and 17.6 ms against a 16.7 ms frame, `make
metal-presenter-tests`). macOS kept that promise from the start - its renderer
sets the presenter's bypass - and Windows and Linux did not, so §5 held on one
platform of three with nothing to notice it. Closed 2026-10-05: both renderers
now read the flag.

The set is derived, not listed. A platform directory that calls a
`*_filter_chain_frame` is a directory whose renderer presents shader output,
and it must also name `IsLookCompare` - in the same directory, because that is
where the pairing lives: on macOS the file that applies the chain
(`MetalPresenter.mm`) is not the file that reads the flag
(`MacOSMetalRenderer.mm`). A new platform, or a new renderer in an existing
one, is covered the moment it runs a chain.

Scope: Windows/, Linux/ and MacOS/ only, so the vendored `Utilities/Video`
librashader bindings (which are nothing but `*_filter_chain_frame` typedefs)
do not count as a renderer.

This is a presence guard. It cannot see whether the flag is consulted on the
right branch - only that a platform has not silently stopped consulting it.
The macOS behaviour itself is pinned by `make metal-presenter-tests`.
"""

import pathlib
import re
import sys

PLATFORM_DIRS = ("Windows", "Linux", "MacOS")
SOURCES = ("*.cpp", "*.h", "*.hpp", "*.mm", "*.m", "*.cc")

APPLIES = re.compile(r"\w+_filter_chain_frame\s*\(")
READS = re.compile(r"\bIsLookCompare\s*\(")


def sources_in(directory: pathlib.Path):
    for pattern in SOURCES:
        yield from directory.rglob(pattern)


def main() -> int:
    root = pathlib.Path(__file__).resolve().parents[2]
    failures = []
    checked = []

    for name in PLATFORM_DIRS:
        directory = root / name
        if not directory.is_dir():
            continue
        applying = []
        reads = False
        for path in sources_in(directory):
            if "obj" in path.parts or "bin" in path.parts:
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            if APPLIES.search(text):
                applying.append(path.relative_to(root).as_posix())
            if READS.search(text):
                reads = True
        if not applying:
            continue
        checked.append((name, applying, reads))
        if not reads:
            failures.append(
                f"{name}/: {', '.join(applying)} runs a librashader chain but no "
                f"file in {name}/ reads IsLookCompare(), so Hold to Compare "
                f"(ADR-0246 §5) presents the filtered frame there."
            )

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    if not checked:
        print("FAIL: no platform renderer runs a librashader chain - the search is stale", file=sys.stderr)
        return 1

    for name, applying, _ in checked:
        print(f"ok: {name}/ presents a shader chain ({', '.join(applying)}) and reads IsLookCompare()")
    return 0


if __name__ == "__main__":
    sys.exit(main())
