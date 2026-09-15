#!/usr/bin/env python3
"""Framework-free checks for nested-zip game discovery (repo-link spike,
ADR-0143). Given a container shaped like a GitHub repo archive — a wrapper
folder holding subfolders that each contain a game zip rather than an
extracted pack — discover_game_roots must enumerate the nested game zips as
one root per game, so a single repo link can split into N packs + N sibling
issues. Also verifies the fail-closed edges: unrelated zips (docs, bonus,
HTML) and non-pack zips do not count, a single nested zip does not split,
and a container that already resolves by subfolder keeps its behavior
(with an image beside each manifest, per #161/ADR-0121).

Usage: python3 scripts/test_mep_nested_zip.py
"""
from __future__ import annotations

import contextlib
import io
import sys
import tempfile
import zipfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import mep_lint  # noqa: E402

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"PASS: {msg}")


def make_zip(entries: dict) -> bytes:
    """A synthetic zip from {inner_path: bytes}."""
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        for path, data in entries.items():
            z.writestr(path, data)
    return buf.getvalue()


HIRES_AUDIO = b"<ver>105\n<bgm>0,1,Stage 1.ogg\n"
HIRES_TEX = b"<ver>105\n<img>0,0,Chr_00_0.png\n"
# Not a decodable image; discovery only looks at the file extension.
PNG = b"\x89PNG\r\n\x1a\n"


def check_repo_like():
    """AC-1 (the spike): a repo-like container enumerates its nested game zips
    as one root per game, ignoring the wrapper and unrelated content. A
    subfolder holding several valid game zips (Duck_Hunt's Audio/NEA/HDV1.1
    variants) still yields ONE root — one game, one slot, one issue."""
    game_zips = {
        "HDnes-main/1942/1942audio.zip": make_zip({"hires.txt": HIRES_AUDIO}),
        "HDnes-main/Dr_Mario/NEA-DrMario_v2.zip": make_zip({"hires.txt": HIRES_AUDIO}),
        # Duck_Hunt carries three valid zips; all must collapse to one root.
        "HDnes-main/Duck_Hunt/DuckHunt-Audio.zip": make_zip({"hires.txt": HIRES_AUDIO}),
        "HDnes-main/Duck_Hunt/DuckHunt-NEA.zip": make_zip({"hires.txt": HIRES_AUDIO}),
        "HDnes-main/Duck_Hunt/DuckHuntHDV1.1.zip": make_zip({"hires.txt": HIRES_TEX}),
        "HDnes-main/Ice_Climber/HDpack-IceClimber(USA,Europe).zip": make_zip({"hires.txt": HIRES_TEX}),
        "HDnes-main/Super_Mario_Bros-2/NEA-smb2.zip": make_zip({"hires.txt": HIRES_AUDIO}),
        "HDnes-main/Yie_Ar_Kung_Fu/NEA-Yie.Ar.Kung-Fu.zip": make_zip({"hires.txt": HIRES_AUDIO}),
    }
    entries = dict(game_zips)
    entries["HDnes-main/HTML/FILES/HDNes-Graphics-Pac-master.zip"] = make_zip({"README.md": b"hi"})
    entries["HDnes-main/README.md"] = b"# HDnes\n"
    entries["HDnes-main/index.html"] = b"<html></html>\n"
    src = mep_lint.Source.from_zip_bytes(make_zip(entries), label="repo.zip")
    roots = mep_lint.discover_game_roots(src, "UNKNOWN")
    expected = [
        # The prefix of a nested-zip root is the exact zip path (the first by
        # sorted name when the subfolder holds several), so the per-game zip
        # the pipeline builds contains ONLY that zip — a subfolder with
        # DuckHunt-Audio/NEA/HDV1.1 must not leak all three into one game.
        ("HDnes-main/1942/1942audio.zip", "1942"),
        ("HDnes-main/Dr_Mario/NEA-DrMario_v2.zip", "Dr_Mario"),
        ("HDnes-main/Duck_Hunt/DuckHunt-Audio.zip", "Duck_Hunt"),
        ("HDnes-main/Ice_Climber/HDpack-IceClimber(USA,Europe).zip", "Ice_Climber"),
        ("HDnes-main/Super_Mario_Bros-2/NEA-smb2.zip", "Super_Mario_Bros-2"),
        ("HDnes-main/Yie_Ar_Kung_Fu/NEA-Yie.Ar.Kung-Fu.zip", "Yie_Ar_Kung_Fu"),
    ]
    # Exact list: six roots, no duplicates (Duck_Hunt's 3 zips collapse to 1,
    # and that one is the sorted-first zip path, not the bare subfolder).
    if len(roots) != len(expected):
        fail(f"repo-like roots: got {len(roots)} entries {sorted(roots)!r}, expected {len(expected)}")
        return
    for i, (exp_prefix, exp_game) in enumerate(expected):
        prefix, game = roots[i]
        if prefix != exp_prefix:
            fail(f"repo-like roots[{i}]: prefix {prefix!r}, expected {exp_prefix!r}")
            return
        if game != exp_game:
            fail(f"repo-like roots[{i}] ({prefix}): game {game!r}, expected {exp_game!r}")
            return
    ok("repo-like container enumerates 6 nested game zips, one root per game (multi-zip subfolder collapses)")


def check_unrelated_zips_excluded():
    """AC-2: a nested zip that is not a pack root (no hires.txt/pack.json at
    its own root) is not a game candidate — a docs/bonus zip must not create
    a spurious slot."""
    entries = {
        "HDnes-main/1942/1942audio.zip": make_zip({"hires.txt": HIRES_AUDIO}),
        "HDnes-main/docs/bonus.zip": make_zip({"README.md": b"not a pack"}),
    }
    src = mep_lint.Source.from_zip_bytes(make_zip(entries), label="mix.zip")
    roots = mep_lint.discover_game_roots(src, "UNKNOWN")
    if dict(roots) != {"HDnes-main/1942/1942audio.zip": "1942"}:
        fail(f"unrelated nested zip should not count: {roots!r}")
        return
    ok("a nested zip without an internal pack root is not a game candidate")


def check_pack_json_nested():
    """AC-3: a nested zip whose pack.json names a target is detected, and the
    game name comes from targets[0].name (ADR-0143), not the zip basename."""
    pack_json = b'{"version":"1.0.0","targets":[{"name":"Dr. Mario","system":"nes"}]}'
    entries = {
        "HDnes-main/Dr_Mario/NEA-DrMario_v2.zip": make_zip({"pack.json": pack_json}),
    }
    src = mep_lint.Source.from_zip_bytes(make_zip(entries), label="pm.zip")
    roots = mep_lint.discover_game_roots(src, "UNKNOWN")
    if dict(roots) != {"HDnes-main/Dr_Mario/NEA-DrMario_v2.zip": "Dr. Mario"}:
        fail(f"pack.json nested root: got {roots!r}")
        return
    ok("a nested zip with pack.json is detected, named from targets[0].name")


def check_single_nested_zip_not_split():
    """AC-4: a single nested game zip is a single root (the existing one-issue
    flow) — the pipeline splits only N>1."""
    entries = {
        "HDnes-main/1942/1942audio.zip": make_zip({"hires.txt": HIRES_AUDIO}),
    }
    src = mep_lint.Source.from_zip_bytes(make_zip(entries), label="single.zip")
    roots = mep_lint.discover_game_roots(src, "UNKNOWN")
    if len(roots) != 1 or dict(roots) != {"HDnes-main/1942/1942audio.zip": "1942"}:
        fail(f"single nested zip should be one root, got {roots!r}")
        return
    ok("a single nested game zip stays a single root (no split)")


def check_named_nested_game_lint():
    """A split sibling revalidation must select only its named inner game zip.

    The source URL remains the complete multi-game repository archive, so the
    normal lint invocation receives the outer zip plus the sibling's Issue Form
    game. The requested spelling matches the actual #132 revalidation command,
    while the archive also contains the distinct VS sibling.
    """
    entries = {
        "HDnes-main/Ice_Climber/IceClimber.zip": make_zip({"hires.txt": b"<ver>105\n"}),
        # The real HDnes archive also carries this distinct sibling. Its
        # parenthetical suffix must not be discarded before matching #132.
        "HDnes-main/Ice_Climber_(VS)/IceClimberVS.zip": make_zip({"hires.txt": b"<ver>105\n"}),
    }
    with tempfile.TemporaryDirectory() as tmp:
        outer = Path(tmp) / "HDnes.zip"
        outer.write_bytes(make_zip(entries))
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            rc = mep_lint.main(["mep_lint.py", str(outer), "Ice_Climber"])
        content_output = io.StringIO()
        with contextlib.redirect_stdout(content_output):
            content_rc = mep_lint.main(["mep_lint.py", "--content-id", str(outer), "Ice_Climber"])
    text = output.getvalue()
    content_id = content_output.getvalue().strip()
    if rc != 0:
        fail(f"named nested game lint returned {rc}: {text!r}")
        return
    if content_rc != 0 or len(content_id) != 64:
        fail(f"named nested game content_id did not resolve the sibling: rc={content_rc}, output={content_id!r}")
        return
    if "Ice_Climber/IceClimber.zip" not in text:
        fail(f"named nested game lint did not select Ice Climber: {text!r}")
        return
    if "Ice_Climber_(VS)" in text:
        fail(f"named nested game lint included the distinct VS sibling: {text!r}")
        return
    if "no section found" in text:
        fail(f"named nested game lint searched the container instead of the sibling: {text!r}")
        return
    ok("named nested game lint selects only the Ice Climber sibling pack")


def check_subfolder_behavior_unchanged():
    """AC-5: a container that already resolves by direct subfolder candidates
    (extracted packs) keeps enumerating those — nested-zip scanning is only a
    last resort after the direct paths found nothing.

    Each folder carries a PNG beside its manifest: since #161 (ADR-0121, "a
    bare hires.txt is a pack root only with a sibling image") the bare-basename
    shape is the one candidate shape with no structural evidence of its own, so
    a manifest with nothing to draw beside it is a variant manifest rather than
    a root. That rule is asserted on its own below."""
    entries = {
        "1942/hires.txt": HIRES_AUDIO,
        "1942/Chr_00_0.png": PNG,
        "Dr_Mario/hires.txt": HIRES_AUDIO,
        "Dr_Mario/Chr_00_0.png": PNG,
    }
    src = mep_lint.Source.from_zip_bytes(make_zip(entries), label="extracted.zip")
    roots = mep_lint.discover_game_roots(src, "UNKNOWN")
    if dict(roots) != {"1942": "1942", "Dr_Mario": "Dr_Mario"}:
        fail(f"direct subfolder discovery must be unchanged: {roots!r}")
        return
    ok("direct subfolder candidates still win (nested scanning is last-resort)")


def check_bare_manifest_folder_is_not_a_root():
    """#161 / ADR-0121: a subfolder holding only a `hires.txt` (a variant
    manifest next to a patch, issue #138's shape) is not a fallback candidate
    root — only a folder that directly holds an image is. Without this the
    four-variant pack presented five candidates and failed closed."""
    entries = {
        "Customization/Patch - Music A/hires.txt": HIRES_TEX,
        "Customization/Patch - Music A/patch.ips": b"PATCH",
        "Customization/Patch - Music B/hires.txt": HIRES_TEX,
        "Customization/Patch - Music B/patch.ips": b"PATCH",
    }
    candidates = mep_lint.find_fallback_subfolder_candidates(list(entries))
    if candidates:
        fail(f"bare-manifest folders must not be candidate roots: {candidates!r}")
        return
    ok("a subfolder with a bare hires.txt and no image is not a root (#161)")


def main():
    check_repo_like()
    check_unrelated_zips_excluded()
    check_pack_json_nested()
    check_single_nested_zip_not_split()
    check_named_nested_game_lint()
    check_subfolder_behavior_unchanged()
    check_bare_manifest_folder_is_not_a_root()

    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
