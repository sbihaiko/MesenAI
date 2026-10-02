#!/usr/bin/env python3
"""mep_project — a Remaster project is the ROM's enhancement folder (ADR-0243, F12.20).

The project is the ADR-0049 sibling `<dir>/<Game>/` (or
`EnhancementPacks/<Game>/` when the ROM folder is read-only):

    auto/rec-NNN/   one complete bootstrap recording each (textures/, audio/)
    auto/textures/  a recording made before ADR-0243 - read as rec-001, never moved
    mep/            the human layer, the pack the artist edits (ADR-0147)
    kit/            the ADR-0183 projections
    project.json    machine-written list of recordings; absent = derived from folders
    .bootstrap      the stamp tying the folder to one ROM

Every tool that looks for "the recording" goes through this module, so the
rule is written once and mirrors the core's (`RemasterProject.h`): ids are
`rec-` plus digits, a bare `auto/textures/` or `auto/audio/` is rec-001, and
the newest recording holding a section is the one that plays and the one a
build keys from.

    scripts/mep_project.py list  <project>         # the manifest, as JSON
    scripts/mep_project.py packs <dir>             # every recorded pack dir under a tree
    scripts/mep_project.py next  <project>         # the id the next recording gets
    scripts/mep_project.py kit   <project> --rom ROM [--out DIR] [--title T] [--no-verify]
    scripts/mep_project.py build <project> [--rom ROM]   # mep/ from the newest recording + its kit

`kit` is the kit generators reading `auto/rec-*/`: figures (`artist_kit.py`)
and scenery (`artist_bg_kit.py`) stay per recording, one kit each under
`<out>/rec-NNN/`, and the pattern pages are the one cross-recording union
(ADR-0194 §2), `artist_chr_kit.py <first> --also <every other>` into
`<out>/pages/`. Stage maps need a per-stage grid dump (`artist_map.py
--stage/--dump`), which a recording does not keep, so `kit` does not make
them. Each kit folder is assembled (`artist_kit_assemble.py`).

`build` is Remaster's *Build & show in game* (slice G.6); it lives in
`mep_project_build.py`, whose docstring says what it writes and what it refuses.

Exit codes: 0 ok, 1 a recording or a generator failed, 2 usage error.
"""

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

HERE = Path(__file__).resolve().parent
AUTO = "auto"
MANIFEST = "project.json"
SOURCES = ("play", "tas", "ai", "script")
_REC = re.compile(r"rec-(\d{1,6})")
BARE_SECTIONS = ("textures", "audio")
# A section is present in a recording when its probe exists (the core's
# kConventionProbe; audio may hold fingerprints.json instead of a hires.txt).
PROBES = {"textures": ("textures/hires.txt",), "audio": ("audio/hires.txt", "audio/fingerprints.json")}


class ProjectError(Exception):
    """A project this module cannot read - never answered with an empty list."""


def recording_number(name: str) -> int:
    """`rec-012` -> 12; anything else (or rec-000) -> 0, as the core parses it."""
    m = _REC.fullmatch(name)
    return int(m.group(1)) if m else 0


def format_id(number: int) -> str:
    return f"rec-{number:03d}"


@dataclass
class Recording:
    id: str
    path: Path          # the recorded pack dir: holds textures/ and/or audio/
    bare: bool = False  # the pre-ADR-0243 auto/ layout, read as rec-001
    meta: dict = field(default_factory=dict)

    @property
    def number(self) -> int:
        return recording_number(self.id)

    def has(self, section: str) -> bool:
        return any((self.path / probe).is_file() for probe in PROBES[section])

    @property
    def textures(self) -> Path:
        return self.path / "textures"


def recordings(project) -> list:
    """The project's recordings in id order. A bare `auto/textures`/`auto/audio`
    is rec-001 at `auto/` itself; a folder holding both that and `auto/rec-001/`
    has two rec-001s and is refused."""
    auto = Path(project) / AUTO
    if not auto.is_dir():
        return []
    found = {}
    for child in sorted(auto.iterdir()):
        number = recording_number(child.name) if child.is_dir() else 0
        if number:
            found[number] = Recording(format_id(number), child)
    bare = Recording("rec-001", auto, bare=True)
    if bare.has("textures") or bare.has("audio"):
        if 1 in found:
            raise ProjectError(f"{auto}: both a bare auto/textures (or auto/audio) and auto/rec-001/ claim rec-001 - "
                               f"move one of them aside")
        found[1] = bare
    return [found[n] for n in sorted(found)]


def latest(project, section: str = "textures"):
    """The newest recording holding `section`, or None."""
    for rec in reversed(recordings(project)):
        if rec.has(section):
            return rec
    return None


def auto_textures(folder) -> Path:
    """The machine textures layer a build keys from: the newest recording's
    `textures/`, else the pre-ADR path `auto/textures` (which then does not
    exist, so the caller's own "not found" message names it)."""
    folder = Path(folder)
    try:
        rec = latest(folder, "textures")
    except ProjectError:
        rec = None
    return rec.textures if rec else folder / AUTO / "textures"


def next_id(project) -> str:
    recs = recordings(project)
    return format_id((recs[-1].number if recs else 0) + 1)


def _read_manifest(project: Path):
    path = project / MANIFEST
    if not path.is_file():
        return None
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise ProjectError(f"{path}: not valid JSON ({exc})") from exc
    if not isinstance(doc, dict):
        raise ProjectError(f"{path}: the root is not an object")
    return doc


def manifest(project) -> dict:
    """`project.json` joined to the folders on disk (ADR-0243 Q2): one entry per
    recording that exists, carrying the manifest's metadata when it has any. A
    recording deleted from disk is not listed; a field nobody wrote is None."""
    project = Path(project)
    doc = _read_manifest(project) or {}
    listed = {}
    for item in doc.get("recordings") or []:
        if isinstance(item, dict) and isinstance(item.get("id"), str):
            listed[item["id"]] = item
    out = []
    for rec in recordings(project):
        meta = listed.get(rec.id, {})
        source = meta.get("source")
        duration = meta.get("durationSeconds")
        out.append({
            "id": rec.id,
            "path": str(rec.path),
            "bare": rec.bare,
            "sections": [s for s in PROBES if rec.has(s)],
            "recordedAt": meta.get("recordedAt") if isinstance(meta.get("recordedAt"), str) else None,
            "source": source if source in SOURCES else None,
            "durationSeconds": duration if isinstance(duration, (int, float)) else None,
            "note": meta.get("note") if isinstance(meta.get("note"), str) else "",
        })
    name = doc.get("name") if isinstance(doc.get("name"), str) and doc.get("name") else project.name
    return {"name": name, "derived": not (project / MANIFEST).is_file(), "recordings": out}


def projects_under(root) -> list:
    """Every project folder (a directory holding `auto/`) under `root`, `root`
    itself included, in path order."""
    root = Path(root)
    found = {p.parent for p in root.rglob(AUTO) if p.is_dir() and p.parent.name != AUTO}
    if (root / AUTO).is_dir():
        found.add(root)
    return sorted(found)


def recorded_packs(root) -> list:
    """The recorded pack dir of every recording of every project under `root`."""
    return [rec.path for project in projects_under(root) for rec in recordings(project)]


def recorded_hires(root) -> list:
    """`textures/hires.txt` of every recording under `root` that has textures."""
    return [rec.textures / "hires.txt" for project in projects_under(root)
            for rec in recordings(project) if rec.has("textures")]


# ---- the kit over a project -------------------------------------------------

def kit_plan(project, rom: Path, out: Path, title: str = "", verify: bool = True) -> list:
    """The generator runs `kit` makes, in order: `{"kit": dir, "argv": [...]}`."""
    project = Path(project)
    recs = [r for r in recordings(project) if r.has("textures")]
    py = sys.executable
    flag = ["--verify"] if verify else []
    steps, kits = [], []
    for rec in recs:
        kit = Path(out) / rec.id
        kits.append((kit, f"{title or project.name}, {rec.id}"))
        steps.append({"kit": kit, "argv": [py, str(HERE / "artist_kit.py"), str(rec.path), "--out", str(kit)] + flag})
        steps.append({"kit": kit, "argv": [py, str(HERE / "artist_bg_kit.py"), str(rec.path), "--out", str(kit)] + flag})
    if recs:
        pages = Path(out) / "pages"
        also = [a for rec in recs[1:] for a in ("--also", str(rec.path))]
        kits.append((pages, f"{title or project.name}, pattern pages"))
        steps.append({"kit": pages, "argv": [py, str(HERE / "artist_chr_kit.py"), str(recs[0].path), "--rom", str(rom),
                                             "--out", str(pages)] + also + flag})
    for kit, kit_title in kits:
        steps.append({"kit": kit, "argv": [py, str(HERE / "artist_kit_assemble.py"), str(kit), "--title", kit_title]})
    return steps


def cmd_kit(args) -> int:
    project = Path(args.project)
    out = Path(args.out) if args.out else project / "kit"
    plan = kit_plan(project, Path(args.rom), out, args.title or "", verify=not args.no_verify)
    if not plan:
        print(f"error: {project} has no recording with textures - nothing to project into a kit", file=sys.stderr)
        return 1
    failed = 0
    for step in plan:
        # The generators' own output goes to this process's stdout, never into
        # the kit: a kit holds surfaces and their manifests, nothing else.
        print(f"== {Path(step['argv'][1]).name} -> {step['kit']}", flush=True)
        rc = subprocess.run(step["argv"]).returncode
        print(f"{'ok  ' if rc == 0 else 'FAIL'} {Path(step['argv'][1]).name} -> {step['kit']}", flush=True)
        failed += rc != 0
    return 1 if failed else 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Remaster project folder (ADR-0243).")
    sub = ap.add_subparsers(dest="cmd", required=True)
    p_list = sub.add_parser("list", help="the project's recordings, as JSON")
    p_list.add_argument("project")
    p_packs = sub.add_parser("packs", help="every recorded pack dir under a tree, one per line")
    p_packs.add_argument("root")
    p_next = sub.add_parser("next", help="the id the next recording gets (one past the highest)")
    p_next.add_argument("project")
    p_kit = sub.add_parser("kit", help="per-recording kits plus the union pattern pages")
    p_kit.add_argument("project")
    p_kit.add_argument("--rom", required=True)
    p_kit.add_argument("--out", help="kit folder (default: <project>/kit)")
    p_kit.add_argument("--title", help="kit title prefix (default: the project folder name)")
    p_kit.add_argument("--no-verify", action="store_true", help="skip the generators' --verify round-trip")
    p_build = sub.add_parser("build", help="rebuild mep/ from the newest recording and its kit (G.6)")
    p_build.add_argument("project")
    p_build.add_argument("--rom", help="passed to mep_build.py build (only an ADR-0196 overflow layer needs it)")
    args = ap.parse_args(argv)
    try:
        if args.cmd == "list":
            doc = manifest(args.project)
            if not doc["recordings"]:
                print(f"error: {args.project} has no recording (no auto/rec-NNN/, no bare auto/textures)", file=sys.stderr)
                return 1
            print(json.dumps(doc, indent=2))
            return 0
        if args.cmd == "packs":
            for path in recorded_packs(args.root):
                print(path)
            return 0
        if args.cmd == "next":
            print(next_id(args.project))
            return 0
        if args.cmd == "build":
            import mep_project_build
            return mep_project_build.run(args.project, args.rom)
        return cmd_kit(args)
    except ProjectError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
