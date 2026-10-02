#!/usr/bin/env python3
"""mep_project_build — Remaster's *Build & show in game* (PRD Part B §13.5.3 W-R1 zone 3, slice G.6).

    scripts/mep_project.py build <project> [--rom ROM]

A Remaster project (ADR-0243) keeps the artist's pack in `mep/`, the human
layer that wins over `auto/` entry by entry (ADR-0147). This command rebuilds
that layer from two inputs only, every time:

  * the newest recording holding textures — the one the loader plays and the
    one `mep_build.py` keys from (ADR-0243);
  * that recording's kit, `kit/<rec-NNN>/`, plus the pattern pages in
    `kit/pages/` when they were made for that same recording (the union pages
    of several recordings are named after the first one, ADR-0194, and their
    page files do not line up with another recording's `textures/chr/`).

It is the recipe ARTIST.md prints under "When you are done"
(`artist_kit_assemble._recorded_done_steps`), run by the app instead of typed
by the artist, into a staging copy `<project>/.mep-build/`:

  1. copy     the recording's `textures/` (and `audio/`), the kit's sheets into
              `textures/sheets/`, its pages into `textures/chr/` and its
              screens into `textures/backgrounds/`;
  2. build    `mep_build.py build` once, before any figure returns (#435);
  3. figures  `mep_figure.py import` for every kit figure (an unpainted figure
              changes nothing, so every one is offered);
  4. check    `mep_build.py build` again — 0 errors, lint included, is the
              ADR-0183 §4 acceptance.

Only a clean build reaches `mep/`. The staging copy is synced into it file by
file: a file whose bytes did not change is not rewritten, so its mtime stays
and ADR-0212's reload re-decodes only the images that moved; a file the new
build no longer has is removed - except what Share wrote there (#645):
`pack.json` (`mep_build.py pack`: targets, the ADR-0140 id, author, license)
and every patch file its `patches[]` names, which no build ever makes. A
failed or stopped build leaves `mep/` as the last good build, and nothing
under `kit/` or `auto/` is ever written.

The stamp is written into `mep/` before the first file is synced (#646): a
build stopped mid-sync leaves a stamped, half-written `mep/` the next build
simply finishes, never a stampless one it would refuse as foreign. It is
rewritten after a clean sync, so its mtime is the last good build's.

A `mep/` this tool did not write (no `.remaster-build.json` stamp — an
installed catalog pack, ADR-0147, or a hand-made one) is never touched: the
build refuses before step 1 and names it.

Output, for the GUI's job card (W-R3) and its problem list (W-R4): `steps: 4`,
then `== <step>` before and `ok   <step>` / `FAIL <step>` after each step, the
tools' own lines in between (stdout and stderr passed through), `recording:
rec-NNN` once, and on success one of

    show: images   only images changed and `mep/` was already there - the
                   loaded pack can re-decode them in place (ADR-0212)
    show: reload   the manifest changed, or `mep/` is new: the ROM must be
                   reopened to pick it up (ADR-0209 constraint 3)

Exit codes: 0 built, 1 the build failed, 2 refused (usage, nothing to build,
a foreign `mep/`).
"""

import filecmp
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import mep_project  # noqa: E402

MEP = "mep"
STAGE = ".mep-build"
KIT = "kit"
STAMP = ".remaster-build.json"
STEPS = ("copy", "build", "figures", "check")
# The manifests a host parses once at load; any change to them needs the ROM
# reopened, because ADR-0212's reload re-decodes images and nothing else.
MANIFESTS = ("textures/hires.txt", "audio/hires.txt", "pack.json")
# Written into mep/ by Share (`mep_build.py pack`), never by a build (#645).
PACK_JSON = "pack.json"


class BuildRefused(Exception):
    """Nothing was built and nothing was written."""


def stamp_path(project: Path) -> Path:
    return Path(project) / MEP / STAMP


def check_mep_is_ours(project: Path) -> None:
    """Refuses a `mep/` this tool did not write - someone's pack lives there."""
    mep = Path(project) / MEP
    if mep.exists() and not stamp_path(project).is_file():
        what = "an installed pack" if (mep / ".mep-install.json").is_file() else "a pack this build did not write"
        raise BuildRefused(f"{mep} holds {what} - move it out of the project folder to build your own "
                           f"(the build never overwrites it, ADR-0147)")


def pick_recording(project: Path):
    rec = mep_project.latest(project, "textures")
    if rec is None:
        raise BuildRefused(f"{project} has no recording with textures - record the game first")
    return rec


def pages_are_for(pages_kit: Path, rec) -> bool:
    """True when kit/pages/ was made with `rec` as its base pack."""
    part = pages_kit / "kit-part-chr.json"
    try:
        doc = json.loads(part.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return False
    base = doc.get("pack") if isinstance(doc, dict) else None
    if not base:
        return False
    try:
        return Path(base).resolve() == Path(rec.path).resolve()
    except OSError:
        return False


def kit_figures(kit: Path) -> list:
    folder = kit / "figures"
    if not folder.is_dir():
        return []
    return sorted(p for p in folder.glob("*-figure.png") if not p.name.endswith(".orig.png"))


def _copy_matching(src: Path, dest: Path, patterns) -> int:
    if not src.is_dir():
        return 0
    dest.mkdir(parents=True, exist_ok=True)
    count = 0
    for pattern in patterns:
        for f in sorted(src.glob(pattern)):
            if f.is_file():
                shutil.copy2(f, dest / f.name)
                count += 1
    return count


def stage_copy(project: Path, rec, stage: Path) -> dict:
    """Step 1: a fresh staging copy of the recording plus the kit's surfaces."""
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True)
    for section in ("textures", "audio"):
        if (rec.path / section).is_dir():
            shutil.copytree(rec.path / section, stage / section)
    kit = Path(project) / KIT / rec.id
    counts = {
        "sheets": _copy_matching(kit / "sheets", stage / "textures" / "sheets", ("*.png", "*.json")),
        "screens": _copy_matching(kit / "scene", stage / "textures" / "backgrounds", ("*.png",)),
        "pages": 0,
    }
    pages = Path(project) / KIT / "pages"
    if (pages / "chr").is_dir():
        if pages_are_for(pages, rec):
            counts["pages"] = _copy_matching(pages / "chr", stage / "textures" / "chr", ("*.png", "*.json"))
        else:
            print(f"note: the pattern pages in {pages} were made for another recording - "
                  f"painted pages are not part of this build of {rec.id}", flush=True)
    return counts


def _run(argv) -> int:
    sys.stdout.flush()
    return subprocess.run(argv).returncode


def build_argv(python: str, stage: Path, rom) -> list:
    argv = [python, str(HERE / "mep_build.py"), "build", str(stage), "--quiet"]
    if rom:
        argv += ["--rom", str(rom)]
    return argv


def kept_files(mep: Path) -> set:
    """What a sync never removes from `mep`: the stamp, Share's `pack.json`
    and the patch files that `pack.json` declares (MEP-v1 §3.1 `patches[]`,
    ADR-0044) - relative paths. An unreadable `pack.json` keeps only itself."""
    kept = {Path(STAMP), Path(PACK_JSON)}
    try:
        doc = json.loads((mep / PACK_JSON).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return kept
    patches = doc.get("patches") if isinstance(doc, dict) else None
    for patch in patches if isinstance(patches, list) else []:
        name = patch.get("file") if isinstance(patch, dict) else None
        if isinstance(name, str) and name and not Path(name).is_absolute() and ".." not in Path(name).parts:
            kept.add(Path(name))
    return kept


def manifests_differ(old: Path, new: Path) -> bool:
    for rel in MANIFESTS:
        a, b = old / rel, new / rel
        if rel == PACK_JSON and not b.is_file():
            continue  # the build never makes one; Share's is kept as it is (#645)
        if a.is_file() != b.is_file():
            return True
        if a.is_file() and not filecmp.cmp(a, b, shallow=False):
            return True
    return False


def sync_into(stage: Path, mep: Path) -> int:
    """Mirror `stage` into `mep`, rewriting only files whose bytes changed.
    Returns how many files were written."""
    written = 0
    mep.mkdir(parents=True, exist_ok=True)
    kept = kept_files(mep)
    wanted = set()
    for root, _dirs, files in os.walk(stage):
        rel_root = Path(root).relative_to(stage)
        (mep / rel_root).mkdir(parents=True, exist_ok=True)
        for name in files:
            rel = rel_root / name
            wanted.add(rel)
            src, dest = stage / rel, mep / rel
            if dest.is_file() and filecmp.cmp(src, dest, shallow=False):
                continue
            shutil.copy2(src, dest)
            written += 1
    for root, dirs, files in os.walk(mep, topdown=False):
        rel_root = Path(root).relative_to(mep)
        for name in files:
            rel = rel_root / name
            if rel not in wanted and rel not in kept:
                (mep / rel).unlink()
        for name in dirs:
            d = Path(root) / name
            if not any(d.iterdir()):
                d.rmdir()
    return written


def write_stamp(project: Path, rec, complete: bool = True) -> None:
    """`complete: false` is the claim written before the sync (#646)."""
    doc = {"generator": "scripts/mep_project.py build", "recording": rec.id,
           "builtAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "complete": complete}
    path = stamp_path(project)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    os.replace(tmp, path)


def _step(name: str, ok: bool) -> None:
    print(f"{'ok  ' if ok else 'FAIL'} {name}", flush=True)


def run(project, rom=None, python: str = sys.executable) -> int:
    project = Path(project).resolve()
    try:
        check_mep_is_ours(project)
        rec = pick_recording(project)
    except (BuildRefused, mep_project.ProjectError) as exc:
        print(f"error: {exc}", file=sys.stderr, flush=True)
        return 2
    print(f"steps: {len(STEPS)}", flush=True)
    print(f"recording: {rec.id}", flush=True)
    stage = project / STAGE
    try:
        return _build(project, rec, rom, python, stage, project / MEP)
    finally:
        # A clean build was synced into mep/ already; a failed or stopped one
        # is discarded - the next build starts from the recording again.
        shutil.rmtree(stage, ignore_errors=True)


def _build(project: Path, rec, rom, python: str, stage: Path, mep: Path) -> int:

    print("== copy", flush=True)
    try:
        counts = stage_copy(project, rec, stage)
    except OSError as exc:
        print(f"error: could not copy the recording and the kit: {exc}", file=sys.stderr, flush=True)
        _step("copy", False)
        return 1
    print(f"copied {rec.id} with {counts['sheets']} sheet file(s), {counts['screens']} screen(s) "
          f"and {counts['pages']} page file(s) from the kit", flush=True)
    _step("copy", True)

    print("== build", flush=True)
    ok = _run(build_argv(python, stage, rom)) == 0
    _step("build", ok)
    if not ok:
        return 1

    print("== figures", flush=True)
    failed = 0
    figures = kit_figures(project / KIT / rec.id)
    for fig in figures:
        if _run([python, str(HERE / "mep_figure.py"), "import", str(stage), str(fig)]) != 0:
            failed += 1
    print(f"figures: {len(figures) - failed} of {len(figures)} returned", flush=True)
    _step("figures", failed == 0)
    if failed:
        return 1

    print("== check", flush=True)
    ok = _run(build_argv(python, stage, rom)) == 0
    _step("check", ok)
    if not ok:
        return 1

    reload = not mep.is_dir() or manifests_differ(mep, stage)
    try:
        # check_mep_is_ours passed, so mep/ is absent or already stamped: claim
        # it before the first file lands, or a stop mid-sync locks it out (#646).
        if not stamp_path(project).is_file():
            write_stamp(project, rec, complete=False)
        written = sync_into(stage, mep)
        write_stamp(project, rec)
    except OSError as exc:
        print(f"error: the build is clean but could not be written into {mep}: {exc}", file=sys.stderr, flush=True)
        return 1
    print(f"wrote {written} file(s) into {mep}", flush=True)
    print(f"show: {'reload' if reload else 'images'}", flush=True)
    return 0
