#!/usr/bin/env python3
"""#560: the guide must never copy the kit's stitched map into the pack.

`verify_artist_docs.copies_kit_map` flags any `cp` that names a `map` path
component, however the source is written (a glob, the bare directory, a
`--target-directory` form), and leaves unrelated copies alone.
"""
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "scripts" / "checks"))
import verify_artist_docs  # noqa: E402

FAILURES = []


def check(name, got, want):
    if got == want:
        print(f"PASS {name}")
    else:
        FAILURES.append(name)
        print(f"FAIL {name}: got {got!r}, want {want!r}")


def main():
    f = verify_artist_docs.copies_kit_map
    check("glob of map pngs", f("cp out/kit/map/*.png out/painted/textures/sheets/"), True)
    check("bare map directory (cp -R)", f("cp -R out/kit/map out/painted/textures/sheets/"), True)
    check("map directory with trailing slash", f("cp -R out/kit/map/ out/painted/textures/sheets/"), True)
    check("--target-directory form", f("cp --target-directory=out/painted/textures/sheets out/kit/map/stage1-000.png"), True)
    check("-t form", f("cp -t out/painted/textures/sheets out/kit/map/stage1-000.json"), True)
    check("sheets copy is fine", f("cp out/kit/sheets/*.png out/kit/sheets/*.json out/painted/textures/sheets/"), False)
    check("a path merely containing 'map' is fine", f("cp out/kit/roadmap.png out/painted/textures/sheets/"), False)
    check("not a cp", f("scripts/artist_map.py --slice out/kit/map/stage1-000.painted.png --out out/kit"), False)
    if FAILURES:
        print(f"{len(FAILURES)} FAILED")
        return 1
    print("all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
