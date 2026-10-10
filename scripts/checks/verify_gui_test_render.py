#!/usr/bin/env python3
"""Fail when a committed GUI test script's Markdown view differs from its render.

The GUI test hook ADR (PR #1202), items 6-7: the script is JSON
(`docs/**/*.gui-test.json`), its readable view is the Markdown next to it
(`*.gui-test.md`), and the renderer is the agent-squad fork's
`squad/gui_test_render.py`, vendored read-only under
`scripts/vendor/agent_squad/`. In CI this check FAILS, it never skips, when:
the vendored renderer is missing, its sha256 differs from the header, no script
exists, a script's `format` is unknown to the renderer, a script has no view, or
a view differs from its render.

The header's sha256 covers the bytes after the `end vendored header` line, which
are the fork file at `source-commit` verbatim.

Usage:
  python3 scripts/checks/verify_gui_test_render.py
  python3 scripts/checks/verify_gui_test_render.py --repo <root>

Exit 0 when every view equals its render; exit 1 with one error line per drift.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path

VENDORED = Path("scripts/vendor/agent_squad/gui_test_render.py")
END_MARK = "# ---- end vendored header ----\n"


def load_renderer(path: Path, errors: list[str]):
    if not path.is_file():
        errors.append(f"{VENDORED}: vendored renderer is missing")
        return None
    text = path.read_text(encoding="utf-8")
    if END_MARK not in text:
        errors.append(f"{VENDORED}: header end marker is missing")
        return None
    header, body = text.split(END_MARK, 1)
    fields = dict(l[2:].split(": ", 1) for l in header.splitlines() if l.startswith("# ") and ": " in l)
    actual = hashlib.sha256(body.encode("utf-8")).hexdigest()
    if fields.get("sha256") != actual:
        errors.append(f"{VENDORED}: sha256 {actual} differs from header {fields.get('sha256')}")
        return None
    spec = importlib.util.spec_from_file_location("gui_test_render_vendored", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def check(repo: Path) -> list[str]:
    errors: list[str] = []
    renderer = load_renderer(repo / VENDORED, errors)
    scripts = sorted((repo / "docs").rglob("*.gui-test.json"))
    if not scripts:
        errors.append("no *.gui-test.json under docs/: nothing to verify, and a check that checks nothing is a failure")
    if renderer is None:
        return errors
    for js in scripts:
        rel = js.relative_to(repo)
        view = js.with_suffix(".md")
        try:
            doc = json.loads(js.read_text(encoding="utf-8"))
            rendered = renderer.render(doc)
        except (ValueError, KeyError) as e:
            errors.append(f"{rel}: unknown format or unrenderable script ({e})")
            continue
        if not view.is_file():
            errors.append(f"{rel}: no committed view {view.relative_to(repo)}")
        elif view.read_text(encoding="utf-8") != rendered:
            errors.append(f"{view.relative_to(repo)}: differs from its render of {rel.name}; "
                          f"regenerate with `python3 {VENDORED} {rel} > {view.relative_to(repo)}`")
    return errors


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    errors = check(ap.parse_args().repo)
    for e in errors:
        print(f"verify_gui_test_render: {e}", file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
