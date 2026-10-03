#!/usr/bin/env python3
"""assets:textures|audio|external labels for one validation pass (bug #677).

The labels mirror what the LATEST pass found, both ways: the workflow adds the
ones printed here and removes the rest of ALL_LABELS, like the patch:* loop
(#557). textures/audio come from classify's `assets` array; external comes
only from the deterministic recipe assembly (a recipe with at least one
`sources.deps` entry, ADR-0138 §6/§13), never from classify, whose `assets`
enum has no "external" member.

Usage: pack_asset_labels.py <mep_classify_clean.json> [--external]
       (labels on one line, space-separated, sorted)
"""
import json
import sys

ALL_LABELS = ("assets:textures", "assets:audio", "assets:external")
_FROM_CLASSIFY = {"textures": "assets:textures", "audio": "assets:audio"}


def asset_labels(classify, has_external_deps):
    assets = classify.get("assets") if isinstance(classify, dict) else None
    assets = assets if isinstance(assets, list) else []
    labels = {_FROM_CLASSIFY[a] for a in assets if isinstance(a, str) and a in _FROM_CLASSIFY}
    if has_external_deps:
        labels.add("assets:external")
    return sorted(labels)


def main(argv):
    args = argv[1:]
    external = "--external" in args
    args = [a for a in args if a != "--external"]
    if len(args) != 1:
        print(__doc__, file=sys.stderr)
        return 2
    with open(args[0], encoding="utf-8") as f:
        print(" ".join(asset_labels(json.load(f), external)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
