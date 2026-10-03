#!/usr/bin/env python3
"""The one field lookup for community-pack board items (bug #685).

`gh project item-list --format json` keys each Project field by its name with
the FIRST letter lowercased: "Pack URL" -> "pack URL", "Pack Hash" ->
"pack Hash", "Status" -> "status" (confirmed live 2026-10-02, 24 items:
category, console, content, game, id, labels, pack Hash, pack MD5, pack URL,
repository, status, title). "ROM SHA1" -> "rOM SHA1" follows the same rule
but no item had the field set to confirm it. Each reader used to keep its own
key list and they drifted: the identity check read the URL as
packUrl / 'Pack URL' / pack_url and the drift check read the hash as
'Pack Hash', so both saw nothing on every item. Every reader of a board item
(the catalog fetch, the identity check, the drift-check workflow via
`normalize`) goes through this module instead.

stdlib only, no imports from this repo.

Usage:
  pack_board_fields.py normalize <item-list.json>
    prints {"items": [...]} with one {status, issue_number, labels, pack_url,
    pack_hash, rom_sha1} object per board item (absent values are null).
"""
from __future__ import annotations

import json
import sys


def gh_item_key(field_name):
    """The JSON key gh gives a Project field on an item-list item."""
    return field_name[:1].lower() + field_name[1:]


def _field(item, field_name, *legacy):
    """A board field's value, under gh's key first; `legacy` keeps the older
    spellings readable (the field name verbatim, camelCase, snake_case)."""
    for key in (gh_item_key(field_name), field_name, *legacy):
        value = item.get(key)
        if value:
            return value
    return None


def item_status(item):
    return _field(item, "Status") or ""


def item_issue_number(item):
    content = item.get("content") or {}
    return content.get("number") or item.get("number") or item.get("issue_number")


def item_pack_url(item):
    return _field(item, "Pack URL", "packUrl", "pack_url")


def item_pack_hash(item):
    return _field(item, "Pack Hash", "packHash", "pack_hash")


def item_rom_sha1(item):
    # Returned by item-list only when populated; callers treat it as optional.
    return _field(item, "ROM SHA1", "romSha1", "rom_sha1")


def normalize(item):
    return {
        "status": item_status(item),
        "issue_number": item_issue_number(item),
        "labels": item.get("labels") or [],
        "pack_url": item_pack_url(item),
        "pack_hash": item_pack_hash(item),
        "rom_sha1": item_rom_sha1(item),
    }


def main(argv):
    if len(argv) != 3 or argv[1] != "normalize":
        print(__doc__, file=sys.stderr)
        return 2
    with open(argv[2], encoding="utf-8") as f:
        listing = json.load(f)
    print(json.dumps({"items": [normalize(it) for it in listing.get("items") or []]}))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
