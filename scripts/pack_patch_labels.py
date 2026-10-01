#!/usr/bin/env python3
"""patch:ips|bps labels, decided from the lint (bug #557), never from classify.

mep_lint.scan_bundled_patches reports each .ips/.bps in the archive as
`bundled patch: X (present, wired ...)` or `(present, NOT wired ...)`. Only a
wired patch is applied by the host (ADR-0148), so only it earns a label. The
classify model's `assets` array can name `ips` for a patch the archive does
not contain, so it is not consulted.

Usage: pack_patch_labels.py <mep_lint_output.txt>   (labels on one line, space-separated)
"""
import re
import sys

_WIRED = re.compile(r"bundled patch: (.+?\.(?:ips|bps)) \(present, wired\b", re.IGNORECASE)


def patch_labels(lint_text):
    kinds = {m.group(1)[-3:].lower() for m in _WIRED.finditer(lint_text)}
    return [f"patch:{k}" for k in sorted(kinds)]


def main(argv):
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    with open(argv[1], encoding="utf-8", errors="replace") as f:
        print(" ".join(patch_labels(f.read())))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
