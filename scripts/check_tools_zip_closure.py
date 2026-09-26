#!/usr/bin/env python3
"""Guard for the release tools zip (Phase 11 C.4, issue #538).

`scripts/release_macos.sh` builds `mesenai-tools-<version>.zip` from the Python
modules in `scripts/tools-zip-manifest.txt` plus the shell tools in its own
SHELL_TOOLS list. Both are the transitive local import closure of the tools
`docs/remastering-a-game.md` and `docs/hd-pack-authoring.md` tell a pack author
to run.

A hand-kept list rots the first time one of those tools grows an import. The
symptom is the worst kind: the zip builds green, the download looks complete,
and the author hits `ModuleNotFoundError` halfway through stage 3 with no way
to tell whether they installed it wrong.

A hand-kept list of *which* tools the guides name rots the same way, one step
earlier and quieter: the guard goes on proving that the tools someone
remembered to list are complete, while the guide prints a command for a file
the zip does not carry. That is issue #538 — `mep_add_cell.py` is printed on a
dozen lines of remastering-a-game.md and was in no list, and
`record_library.sh` is step 1 of the same guide and was copied by an ad-hoc
`cp` line rather than by the list. So the required set is now **derived** from
the guides: every `scripts/<name>.py|.sh` path, or bare `<name>.py|.sh` token,
that names a file that exists in `scripts/` and that a guide tells the reader
to run. Test suites are not tools a reader runs; the one guide-named file that
is deliberately not in a pack author's download is in NOT_A_TOOL_FOR_READERS
below, with its reason.

What it checks:

  * every guide-named `.py` is in the import closure, and the manifest is
    exactly that closure (no missing module, no surplus entry);
  * every guide-named `.sh` is staged by `release_macos.sh`, from its
    SHELL_TOOLS list rather than an ad-hoc `cp`;
  * a staged `.sh` carries what it invokes at runtime: a `python3
    "$here/<tool>.py"` is in the closure (and so in the manifest), a
    `"$here/<tool>.sh"` is staged too;
  * the non-stdlib imports, because those are what `requirements.txt` inside
    the zip has to pin: today Pillow and numpy, nothing else.

Exit code 0 when the guides, the manifest and the release script agree; 1
otherwise, naming every disagreement.
"""

from __future__ import annotations

import ast
import re
import sys
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
REPO_ROOT = SCRIPTS.parent
MANIFEST = SCRIPTS / "tools-zip-manifest.txt"
RELEASE = SCRIPTS / "release_macos.sh"

# The guides whose printed commands are a download contract: everything they
# tell the reader to run has to be in the zip, on a machine with no checkout.
GUIDES = ("remastering-a-game.md", "hd-pack-authoring.md")

# A guide names a tool either by path (`scripts/mep_build.py`) or bare
# (`mep_build.py`); every other word in the prose is just prose.
TOOL_TOKEN = re.compile(r"(?:scripts/)?([A-Za-z0-9_][A-Za-z0-9_.-]*\.(?:py|sh))")

# Guide-named files that are deliberately not in a pack author's download, each
# with the reason. An explicit list rather than a name-pattern guess, so that
# excluding one is a decision someone makes and can be asked about. A guide may
# name a tool to say what it does without telling the reader to run it.
NOT_A_TOOL_FOR_READERS = {
    "validate_pack_local.sh": (
        "maintainer-side triage of a community submission: it needs `gh`, the issue "
        "form and the `claude` CLI, and hd-pack-authoring.md names it only to say "
        "what the `pack:split` label means. An author hands a pack over, they do not "
        "triage it."
    ),
}

# Tools the zip ships that no guide names by file name and no walk can reach,
# because nothing in the closure imports them. Explicit on purpose: an entry
# dropped from here is a tool dropped from the release.
EXTRA_ENTRY_POINTS = {
    "mep_compare": (
        "compares the automatic layer against an artist pack; the guides point at "
        "the comparison in prose only, so it needs to be asked for by name"
    ),
}

# The only third-party packages the closure is allowed to reach. Anything else
# would have to be added to the zip's requirements.txt, so make that a decision
# rather than a surprise.
ALLOWED_THIRD_PARTY = {"PIL", "numpy"}

SHELL_TOOLS_DECL = re.compile(r'^SHELL_TOOLS="([^"]*)"', re.MULTILINE)
HERE_REF = re.compile(r"\$here/([A-Za-z0-9_][A-Za-z0-9_.-]*\.(?:py|sh))")


def local_modules() -> dict[str, Path]:
    return {p.stem: p for p in SCRIPTS.glob("*.py")}


def named_tools() -> set[str]:
    """Every tool the two guides tell the reader to run, by file name.

    A token counts when it names a file that exists in scripts/ - a guide that
    has drifted into naming a file that never existed is verify_artist_docs.py's
    business, not this guard's - and when it is not a test suite. The exclusion
    list is subtracted last, so a tool cannot be both required and excluded.
    """
    present = {
        p.name for p in SCRIPTS.iterdir() if p.is_file() and p.suffix in (".py", ".sh")
    }
    named: set[str] = set()
    for guide in GUIDES:
        text = (REPO_ROOT / "docs" / guide).read_text(encoding="utf-8")
        for token in TOOL_TOKEN.findall(text):
            if token in present and not token.startswith("test_"):
                named.add(token)
    return named - set(NOT_A_TOOL_FOR_READERS)


def shell_tools() -> set[str]:
    """The shell tools release_macos.sh stages, from its one SHELL_TOOLS list."""
    match = SHELL_TOOLS_DECL.search(RELEASE.read_text(encoding="utf-8"))
    if match is None:
        return set()
    return set(match.group(1).split())


def shell_references(tool: str) -> set[str]:
    """Tools a staged shell tool invokes beside itself, as `$here/<tool>`."""
    body = (SCRIPTS / tool).read_text(encoding="utf-8")
    return set(HERE_REF.findall(body))


def closure(local: dict[str, Path], roots) -> tuple[set[str], set[str]]:
    """Return (local modules reached, top-level non-local imports seen)."""
    seen: set[str] = set()
    external: set[str] = set()
    stack = list(roots)
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


def audit(
    guide_py,
    guide_sh,
    reached,
    listed,
    shell_tools,
    shell_py_refs,
    shell_sh_refs,
) -> list[str]:
    """Every way the shipped tools can disagree with the guides, one entry each.

    Pure: it takes sets, not files, so scripts/test_check_tools_zip_closure.py
    can hand it a tree that is missing exactly one thing and assert it is named.
    """
    errors: list[str] = []

    missing = sorted(reached - listed)
    if missing:
        errors.append(
            "scripts/tools-zip-manifest.txt is missing module(s) the tools import, "
            "so the release zip would ship a broken tool:\n"
            + "\n".join(f"  + {name}" for name in missing)
        )

    surplus = sorted(listed - reached)
    if surplus:
        errors.append(
            "scripts/tools-zip-manifest.txt lists module(s) nothing in the closure "
            "imports; drop them or add an entry point that needs them:\n"
            + "\n".join(f"  - {name}" for name in surplus)
        )

    unlisted = sorted(name for name in guide_py if name not in reached)
    if unlisted:
        errors.append(
            "the guides tell the reader to run tool(s) the zip does not carry, so the "
            "download ends in a \u201cNo such file\u201d:\n"
            + "\n".join(f"  + {name}" for name in unlisted)
        )

    uncopied = sorted(name for name in guide_sh if name not in shell_tools)
    if uncopied:
        errors.append(
            "the guides tell the reader to run shell tool(s) release_macos.sh does not "
            "stage into the zip:\n"
            + "\n".join(f"  + {name}" for name in uncopied)
        )

    for tool in sorted(shell_py_refs):
        for reference in sorted(shell_py_refs[tool]):
            if reference not in listed:
                errors.append(
                    "a staged shell tool runs a Python tool the manifest does not list, "
                    f"so the download fails at runtime: {tool} -> {reference}"
                )

    for tool in sorted(shell_sh_refs):
        for reference in sorted(shell_sh_refs[tool]):
            if reference not in shell_tools:
                errors.append(
                    "a staged shell tool runs a shell tool release_macos.sh does not "
                    f"stage: {tool} -> {reference}"
                )

    return errors


def main() -> int:
    local = local_modules()
    named = named_tools()
    staged = shell_tools()

    status = 0

    # An extra entry point or a staged shell tool that no longer exists is a
    # stale list entry, not a missing file: name it before the closure walk,
    # which would otherwise skip it in silence.
    absent_extra = sorted(name for name in EXTRA_ENTRY_POINTS if name not in local)
    if absent_extra:
        print(
            "error: EXTRA_ENTRY_POINTS names module(s) scripts/ no longer has: "
            + ", ".join(absent_extra),
            file=sys.stderr,
        )
        status = 1

    absent_staged = sorted(name for name in staged if not (SCRIPTS / name).is_file())
    if absent_staged:
        print(
            "error: release_macos.sh stages shell tool(s) scripts/ does not have: "
            + ", ".join(absent_staged),
            file=sys.stderr,
        )
        status = 1

    # The closure is rooted at everything a reader is told to run: the tools the
    # guides name, the ones the guides do not, and what a staged shell tool
    # invokes - `record_library.sh` is a guide line, and `library_job.py` is the
    # driver it runs, so the Python side of the zip is rooted there too.
    staged_py = {
        reference
        for tool in staged
        for reference in shell_references(tool)
        if reference.endswith(".py")
    }
    roots = [name[:-3] for name in named if name.endswith(".py")]
    roots += list(EXTRA_ENTRY_POINTS)
    roots += sorted(reference[:-3] for reference in staged_py)
    reached, external = closure(local, roots)

    errors = audit(
        guide_py=[name for name in named if name.endswith(".py")],
        guide_sh=[name for name in named if name.endswith(".sh")],
        reached={f"{name}.py" for name in reached},
        listed=read_manifest(),
        shell_tools=staged,
        shell_py_refs={
            tool: {r for r in shell_references(tool) if r.endswith(".py")} for tool in staged
        },
        shell_sh_refs={
            tool: {r for r in shell_references(tool) if r.endswith(".sh")} for tool in staged
        },
    )
    for message in errors:
        print(f"error: {message}", file=sys.stderr)
        status = 1

    third_party = {name for name in external if name not in sys.stdlib_module_names}
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
            f"({len(reached)} modules; {len(named)} guide-named tools, "
            f"{len(staged)} shell tools; third-party: "
            f"{', '.join(sorted(third_party)) or 'none'})"
        )
    return status


if __name__ == "__main__":
    sys.exit(main())
