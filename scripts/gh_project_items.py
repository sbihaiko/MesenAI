#!/usr/bin/env python3
"""Complete listings of the community-pack board (bug #670).

`gh project item-list` returns 30 items unless `--limit` says otherwise, so
every reader of Project 3 (the catalog generator, the identity check, the
drift-check workflow) goes through this module: it asks for
PROJECT_ITEM_LIMIT items and refuses a listing that may still be truncated —
one that reaches the limit, or whose `totalCount` exceeds what came back —
rather than act on a partial board (as the cheat/replay catalog generators do
with FETCH_LIMIT). A dropped accepted item would silently leave the catalog
and the client's auto-install set.

Usage:
  gh_project_items.py check <item-list.json>   exit 1 when the listing may be truncated
  gh_project_items.py limit                    print PROJECT_ITEM_LIMIT
"""
from __future__ import annotations

import json
import sys

PROJECT_ITEM_LIMIT = 5000


def item_list_argv(project_number, owner, limit=PROJECT_ITEM_LIMIT):
    return ["project", "item-list", str(project_number), "--owner", owner, "--limit", str(limit), "--format", "json"]


def check_complete(listing, limit=PROJECT_ITEM_LIMIT):
    """The listing's items; SystemExit (non-zero) when it may be truncated."""
    items = listing.get("items") or [] if isinstance(listing, dict) else []
    total = listing.get("totalCount") if isinstance(listing, dict) else None
    if len(items) >= limit:
        raise SystemExit(f"error: gh project item-list returned {len(items)} items (the --limit); "
                         "refusing to act on a possibly truncated board")
    if isinstance(total, int) and total > len(items):
        raise SystemExit(f"error: gh project item-list returned {len(items)} of {total} items; "
                         "refusing to act on a truncated board")
    return items


def list_items(run_gh, project_number, owner, limit=PROJECT_ITEM_LIMIT):
    """Every item on the board, via `run_gh(argv) -> stdout`."""
    return check_complete(json.loads(run_gh(item_list_argv(project_number, owner, limit))), limit)


def main(argv):
    if len(argv) == 2 and argv[1] == "limit":
        print(PROJECT_ITEM_LIMIT)
        return 0
    if len(argv) != 3 or argv[1] != "check":
        print(__doc__, file=sys.stderr)
        return 2
    with open(argv[2], encoding="utf-8") as f:
        items = check_complete(json.load(f))
    print(f"{len(items)} board item(s), listing complete")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
