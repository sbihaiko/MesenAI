#!/usr/bin/env python3
"""mep_project_kit — rerunning a generator over a kit folder the artist paints in (#644).

`mep_project.py kit` runs after every Stop recording (and *Prepare Figures*),
and `kit/` is where a Remaster project's artist paints (ADR-0243). A kit is a
projection (ADR-0183 §1), but once an artist has painted one of its surfaces
that file is the artist's work, and regenerating it would erase it. So a
generator never writes straight into a kit that already exists: it writes a
fresh copy beside it, and `merge_kit` carries that copy in under one rule.

**A painted surface keeps its whole family.** A surface is a painting file - a
`*.png` that is not an `.orig.png` twin or a `.legend.png`, or a `*.ora`. Its
family is every file sharing its stem: `<stem>.png`, `.orig.png`, `.ora`,
`.json` (the sidecar) and `.legend.png`. The family is kept as it is on disk,
none of it replaced, because a painting is only meaningful against the twin
and sidecar it was painted over (mep_build diffs it against that twin).

**"Painted" is decided from the kit's own record.** Every file a merge writes
is listed with its sha256 in `<kit>/.kit-written.json`. A surface is painted
when its bytes differ from what the record says was written. A surface the
record does not list (a kit written before this rule, or a file the artist
added) is painted unless its bytes equal the fresh generation's - the
conservative reading, since nothing tells an untouched old page from a
painted one. The `.orig.png` twin is not the test: a pattern page's twin is
not a pixel copy of the untouched page, so "differs from its twin" would
call every page painted.

Everything else the fresh copy holds replaces what the kit had; a kit file the
fresh copy no longer has is removed only when the record says the kit wrote
it and it is unchanged. A fragment entry (`kit-part-*.json`) describing a kept
surface keeps the entry it was written with, so the kit's counts match the
page on disk.
"""

import hashlib
import json
import os
import shutil
from pathlib import Path

LEDGER = ".kit-written.json"
# Longest first: "Chr_0.orig.png" is the twin of "Chr_0", not a surface "Chr_0.orig".
FAMILY_SUFFIXES = (".orig.png", ".legend.png", ".png", ".ora", ".json")


def _sha(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def family_of(rel: Path):
    """`chr/Chr_0.orig.png` -> `chr/Chr_0`; None for a file outside any family."""
    for suffix in FAMILY_SUFFIXES:
        if rel.name.endswith(suffix) and len(rel.name) > len(suffix):
            return (rel.parent / rel.name[:-len(suffix)]).as_posix()
    return None


def is_surface(rel: Path) -> bool:
    name = rel.name
    if name.endswith(".ora"):
        return True
    return name.endswith(".png") and not name.endswith((".orig.png", ".legend.png"))


def _files(root: Path) -> set:
    if not root.is_dir():
        return set()
    return {p.relative_to(root) for p in root.rglob("*") if p.is_file() and p.name != LEDGER}


def read_ledger(kit: Path) -> dict:
    try:
        doc = json.loads((kit / LEDGER).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    files = doc.get("files") if isinstance(doc, dict) else None
    return {k: v for k, v in files.items() if isinstance(v, str)} if isinstance(files, dict) else {}


def painted_families(fresh: Path, kit: Path, ledger: dict) -> dict:
    """{family: the surface that shows it painted} for every painted surface in `kit`."""
    found = {}
    for rel in sorted(_files(kit)):
        if not is_surface(rel):
            continue
        family = family_of(rel)
        key = rel.as_posix()
        if key in ledger:
            painted = _sha(kit / rel) != ledger[key]
        else:
            new = fresh / rel
            painted = not (new.is_file() and _sha(new) == _sha(kit / rel))
        if painted and family not in found:
            found[family] = key
    return found


def _restore_kept_entries(fresh_frag: Path, old_frag: Path, kept: set) -> None:
    """In a fresh `kit-part-*.json`, put back the old entry of every kept
    surface (matched by its `path`), so the fragment describes the page on disk."""
    try:
        new = json.loads(fresh_frag.read_text(encoding="utf-8"))
        old = json.loads(old_frag.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return
    if not isinstance(new, dict) or not isinstance(old, dict):
        return
    changed = False
    for key, items in new.items():
        old_items = old.get(key)
        if not isinstance(items, list) or not isinstance(old_items, list):
            continue
        before = {i["path"]: i for i in old_items if isinstance(i, dict) and isinstance(i.get("path"), str)}
        for n, item in enumerate(items):
            path = item.get("path") if isinstance(item, dict) else None
            if isinstance(path, str) and family_of(Path(path)) in kept and path in before:
                items[n] = before[path]
                changed = True
    if changed:
        fresh_frag.write_text(json.dumps(new, indent=1) + "\n", encoding="utf-8")


def merge_kit(fresh: Path, kit: Path) -> dict:
    """Carry the generator's `fresh` output into `kit` under the module's rule.
    Returns {"kept": {family: surface}, "written": n, "removed": n}."""
    fresh, kit = Path(fresh), Path(kit)
    ledger = read_ledger(kit)
    kept = painted_families(fresh, kit, ledger)
    for frag in sorted(fresh.glob("kit-part-*.json")):
        if (kit / frag.name).is_file() and kept:
            _restore_kept_entries(frag, kit / frag.name, set(kept))
    new_ledger, written, removed = {}, 0, 0
    fresh_files = _files(fresh)
    for rel in sorted(fresh_files):
        key = rel.as_posix()
        if family_of(rel) in kept:
            if key in ledger:
                new_ledger[key] = ledger[key]
            continue
        src, dest = fresh / rel, kit / rel
        digest = _sha(src)
        new_ledger[key] = digest
        if dest.is_file() and _sha(dest) == digest:
            continue
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dest)
        written += 1
    for rel in sorted(_files(kit) - fresh_files):
        key = rel.as_posix()
        if family_of(rel) in kept:
            if key in ledger:
                new_ledger[key] = ledger[key]
            continue
        if key in ledger and _sha(kit / rel) == ledger[key]:
            (kit / rel).unlink()
            removed += 1
    kit.mkdir(parents=True, exist_ok=True)
    tmp = kit / (LEDGER + ".tmp")
    tmp.write_text(json.dumps({"version": 1, "generator": "scripts/mep_project.py kit",
                               "files": dict(sorted(new_ledger.items()))}, indent=1) + "\n", encoding="utf-8")
    os.replace(tmp, kit / LEDGER)
    return {"kept": kept, "written": written, "removed": removed}
