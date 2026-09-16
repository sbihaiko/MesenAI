#!/usr/bin/env python3
"""Guard for the release tools zip (Phase 11 C.4).

`scripts/release_macos.sh` builds `mesenai-tools-<version>.zip` from the file
list in `scripts/tools-zip-manifest.txt`. That list is the transitive local
import closure of the tools `docs/remastering-a-game.md` and
`docs/hd-pack-authoring.md` tell a pack author to run.

A hand-kept list rots the first time one of those tools grows an import. The
symptom is the worst kind: the zip builds green, the download looks complete,
and the author hits `ModuleNotFoundError` halfway through stage 3 with no way
to tell whether they installed it wrong. So recompute the closure here, from
the sources, and fail when it and the manifest disagree.

Also reports the non-stdlib imports, because those are what `requirements.txt`
inside the zip has to pin: today Pillow and numpy, nothing else.

Exit code 0 when the manifest is exactly the closure; 1 otherwise, naming the
missing and the surplus entries.
"""

from __future__ import annotations

import ast
import sys
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
MANIFEST = SCRIPTS / "tools-zip-manifest.txt"

# The tools a reader of the two guides actually types. Kept in sync with the
# "Entry points" comment inside the manifest.
ENTRY_POINTS = [
    "artist_ai_review",
    "artist_bg_kit",
    "artist_chr_kit",
    "artist_cover",
    "artist_kit",
    "artist_kit_assemble",
    "artist_map",
    "audio_cleanup_suggest",
    "cdl_tool",
    "fm2_to_bk2",
    "mep_build",
    "mep_compare",
    "mep_lint",
    "mep_render_audio",
    "sheet_repaint",
]

# The only third-party packages the closure is allowed to reach. Anything else
# would have to be added to the zip's requirements.txt, so make that a decision
# rather than a surprise.
ALLOWED_THIRD_PARTY = {"PIL", "numpy"}


def local_modules() -> dict[str, Path]:
    return {p.stem: p for p in SCRIPTS.glob("*.py")}


def closure(local: dict[str, Path]) -> tuple[set[str], set[str]]:
    """Return (local modules reached, top-level non-local imports seen)."""
    seen: set[str] = set()
    external: set[str] = set()
    stack = list(ENTRY_POINTS)
    while stack:
        name = stack.pop()
        if name in seen or name not in local:
            continue
        seen.add(name)
        tree = ast.parse(local[name].read_text(encoding="utf-8"))
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                tops = [alias.name.split(".")[0] for alias in node.names]
            elif isinstance(node, ast.ImportFrom):
                if node.level or not node.module:
                    continue  # relative import: not a thing in this flat folder
                tops = [node.module.split(".")[0]]
            else:
                continue
            for top in tops:
                if top in local:
                    stack.append(top)
                else:
                    external.add(top)
    return seen, external


def read_manifest() -> set[str]:
    entries: set[str] = set()
    for raw in MANIFEST.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        entries.add(line)
    return entries


def main() -> int:
    local = local_modules()

    missing_entry_points = [e for e in ENTRY_POINTS if e not in local]
    if missing_entry_points:
        print(
            "error: entry point(s) named by this guard no longer exist in scripts/: "
            + ", ".join(sorted(missing_entry_points)),
            file=sys.stderr,
        )
        return 1

    reached, external = closure(local)
    expected = {f"{name}.py" for name in reached}
    listed = read_manifest()

    status = 0

    missing = sorted(expected - listed)
    if missing:
        print(
            "error: scripts/tools-zip-manifest.txt is missing module(s) the tools "
            "import, so the release zip would ship a broken tool:",
            file=sys.stderr,
        )
        for name in missing:
            print(f"  + {name}", file=sys.stderr)
        status = 1

    surplus = sorted(listed - expected)
    if surplus:
        print(
            "error: scripts/tools-zip-manifest.txt lists module(s) nothing in the "
            "closure imports; drop them or add an entry point that needs them:",
            file=sys.stderr,
        )
        for name in surplus:
            print(f"  - {name}", file=sys.stderr)
        status = 1

    third_party = {
        name for name in external if name not in sys.stdlib_module_names
    }
    unexpected = sorted(third_party - ALLOWED_THIRD_PARTY)
    if unexpected:
        print(
            "error: the tools now import third-party package(s) the zip's "
            "requirements.txt does not pin: " + ", ".join(unexpected),
            file=sys.stderr,
        )
        print(
            "       add the pin in scripts/release_macos.sh and to "
            "ALLOWED_THIRD_PARTY here, or drop the import.",
            file=sys.stderr,
        )
        status = 1

    if status == 0:
        print(
            f"ok: tools zip manifest matches the import closure "
            f"({len(expected)} modules; third-party: "
            f"{', '.join(sorted(third_party)) or 'none'})"
        )
    return status


if __name__ == "__main__":
    sys.exit(main())
