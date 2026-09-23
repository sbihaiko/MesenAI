#!/usr/bin/env python3
"""The lines `mep_build.py build` carries over by file name — `<background>`
and the `<bgm>`/`<sfx>` seed references — and how their names are resolved.

Split out of mep_build for #381. The one rule here: **a carried name is
resolved the way HdPackLoader resolves it.** The loader rewrites every `\\`
to `/` on each manifest line before it parses a tag
(Core/NES/HdPacks/HdPackLoader.cpp, the `std::replace` above the condition
parse), so `Backdrops\\shot.png` and `Backdrops/shot.png` are the same rule to
the emulator on every OS. `mep_lint` normalizes the same way, and
`mep_import.py` copies the file under the normalized name. Before #381 the
build alone read the raw text: on POSIX `Backdrops\\shot.png` is one literal
file name, so a Windows-authored `<background>` never resolved and its line
was dropped as "retired" — 702 lines on the Mega Man (USA) community pack —
with an info-level log. The audio seed filter had the same read.

The emitted line is rewritten to the `/` spelling. That is where the file
actually lives after the import copied it, it is what every tool in this tree
can `exists()`-check, and the loader reads both spellings identically, so the
rewrite changes nothing for the run time and stops the carried text and the
disk from disagreeing. `mep_import.py verify` compares carried lines under
the same normalization.
"""

from __future__ import annotations

import re
from pathlib import Path

BG_TAG = re.compile(r"^(\[[^\]]*\])?<background>")
BGM_RE = re.compile(r"^(\[[^\]]*\])?<bgm>(.*)$")
SFX_RE = re.compile(r"^(\[[^\]]*\])?<sfx>(.*)$")


def posix_ref(name: str) -> str:
    """A carried file name as HdPackLoader sees it: `\\` is a separator."""
    return name.replace("\\", "/")


def background_name(line: str) -> str:
    """The (normalized) file a `<background>` line names, or "" for any other
    line. The tag may carry a condition prefix (`[cond]<background>...`) — the
    only form the emulator writes for captured-screen backgrounds."""
    m = BG_TAG.match(line)
    return posix_ref(line[m.end():].split(",")[0].strip()) if m else ""


def carry_backgrounds(folder: Path, textures_dir: Path, body: list) -> list:
    """The body with every `<background>` line resolved.

    A background PNG referenced by the body that is not under textures/ yet is
    copied up from auto/textures (the author keeps their assets). A capture in
    neither layer is *retired*: its lines are dropped, so deleting the PNG is
    the way out of a capture (#344). ADR-0050/ADR-0156 still rule a present
    one. The surviving line is emitted with the name it was resolved under.

    Retirement is a warning, not an info line (#381): it is a legitimate exit
    (#344 — the artist deleted the PNG on purpose, and HdPackLoader itself
    drops a dangling entry at load, so the build must not fail), but it drops
    manifest lines, and a drop the artist did not intend has to be visible in
    a log they skim. The count and the first names are in the one message.
    """
    retired: dict = {}
    live = []
    for b in body:
        name = background_name(b)
        if not name:
            live.append(b)
            continue
        m = BG_TAG.match(b)
        rest = b[m.end():].split(",", 1)
        line = f"{b[:m.end()]}{name}{',' + rest[1] if len(rest) > 1 else ''}"
        if not (textures_dir / name).exists():
            auto_cand = folder / "auto" / "textures" / name
            if not auto_cand.exists():
                retired[name] = retired.get(name, 0) + 1
                continue
            (textures_dir / name).parent.mkdir(parents=True, exist_ok=True)
            (textures_dir / name).write_bytes(auto_cand.read_bytes())
            print(f"info: copied background {name} from auto/textures into textures/")
        live.append(line)
    if retired:
        print(f"warning: retired {len(retired)} captured screen(s) missing from textures/ and "
              f"auto/textures/ — {sum(retired.values())} <background> line(s) dropped, so the "
              f"frames they owned come from the sheets again (#344): {', '.join(sorted(retired)[:3])}")
    return live


def keep_seed_refs(folder: Path, seed: list) -> list:
    """The `<bgm>`/`<sfx>` seed references whose OGG exists under audio/,
    each emitted with its file name normalized. A dangling ref would ship an
    unregistered track (lint warning) and its id must be reclaimed, not held.
    """
    keep = []
    for s in seed:
        for rx, kind in ((BGM_RE, "bgm"), (SFX_RE, "sfx")):
            m = rx.match(s)
            if not m:
                continue
            fields = [f.strip() for f in m.group(2).split(",")]
            if len(fields) >= 3 and (folder / "audio" / posix_ref(fields[2])).exists():
                if posix_ref(fields[2]) == fields[2]:
                    keep.append(s)  # verbatim: nothing to normalize
                else:
                    fields[2] = posix_ref(fields[2])
                    keep.append(f"{m.group(1) or ''}<{kind}>{','.join(fields)}")
            else:
                print(f"info: dropping {kind} ref {fields[2] if len(fields) >= 3 else s} "
                      "(no such file under audio/)")
            break
    return keep
