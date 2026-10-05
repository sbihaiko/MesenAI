#!/usr/bin/env python3
"""Markdown link integrity — every `](target)` in the repo resolves from the file that writes it.

A markdown link target is relative to its own file, so moving a file changes what
its links must say even when their targets never moved. The 2026-10-05 area split
of `docs/adr/` and `docs/validation/` (283 files, one directory deeper) was swept
four times, and each sweep missed a shape the next one found:

  1. repo-relative path literals (`docs/validation/x.md`) — a prefix replacement
     sees these, but not a path split across two lines;
  2. `../`-relative paths in code spans — a link rewriter never looks at them;
  3. link targets — the sweep that DID run, which is why it missed the *label* of
     a link, and a target written `adr/core/0237-….md` with no `docs/` prefix;
  4. this one.

Resolving every target is the only formulation with no shape to miss, so it is
the one worth keeping. Run against the tree it replaced, it reports two broken
links and nothing else: `docs/shader-sweep.md` (broken by the move itself) and
`docs/releases/tools-zip-README.md` (broken since cf896c2df, 2026-09-14, and
fixed with this check rather than filed, because it is the same defect).

Tolerated, because they are not references that a reader follows: schemes other
than a repo path, and pure `#anchor` links. Everything inside a fenced code block
is skipped, since those lines are samples rather than prose.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent.parent
SKIP_DIRS = {".git", "out", "obj", "bin", "node_modules", ".vs", "runs"}
SKIP_SCHEMES = ("http://", "https://", "mailto:", "ftp://", "tel:")

#] target ) — the target may be wrapped in <> and may carry a title after a space.
LINK = re.compile(r"\]\(\s*<?([^)>\s]+)>?(?:\s+\"[^\"]*\")?\s*\)")
FENCE = re.compile(r"^\s*(```|~~~)")


def markdown_files():
    for p in ROOT.rglob("*.md"):
        if any(part in SKIP_DIRS for part in p.parts):
            continue
        yield p


def scan(failures):
    for p in markdown_files():
        rel = p.relative_to(ROOT).as_posix()
        try:
            text = p.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        in_fence = False
        for lineno, line in enumerate(text.splitlines(), 1):
            if FENCE.match(line):
                in_fence = not in_fence
                continue
            if in_fence:
                continue
            for m in LINK.finditer(line):
                target = m.group(1)
                if target.startswith(SKIP_SCHEMES) or target.startswith("#"):
                    continue
                path = target.split("#")[0]
                if not path:
                    continue
                if not (p.parent / path).resolve().exists():
                    failures.append(
                        f"{rel}:{lineno}: '{target}' does not resolve from "
                        f"{p.parent.relative_to(ROOT).as_posix()}/")


def main():
    failures = []
    scan(failures)
    if failures:
        print("FAIL verify_md_links:")
        for f in failures:
            print(f"  {f}")
        return 1
    print("PASS verify_md_links: every markdown link resolves from its own file")
    return 0


if __name__ == "__main__":
    sys.exit(main())
