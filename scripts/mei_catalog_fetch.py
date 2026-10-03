#!/usr/bin/env python3
"""mei_catalog_fetch — the gh-backed read half of the community-pack catalog
generator (ADR-0138 §35 split: the generator orchestrates, this module
fetches, `mei_catalog_entry` assembles, `community_pack_markdown` renders).

Holds every live `gh` call the generator makes (board item-list, per-issue
view, mep-meta comments) plus the Issue Form field extraction — so the
generator stays a pure-ish orchestrator over fetched data. `fetch_accepted_
items` documents the CONFIRMED-vs-COVERAGE-GAP facts (which `gh project`
field ids were verified live against the real API vs. which per-item JSON
key names remain an unconfirmed gap because the Project held zero items at
write time); the item accessors use defensive `dict.get` lookups, never
direct indexing on an assumed key path.

stdlib only (plus the stdlib-only leaves `mep_meta_parser`,
`community_pack_markdown`, `mei_catalog_entry`, `pack_board_fields`).
"""
from __future__ import annotations

import json
import re
import subprocess

import community_pack_markdown as markdown
import gh_project_items
# The board-item field lookup is shared with mep_identity_check and the
# drift-check workflow (bug #685); re-exported for the generator.
from pack_board_fields import (  # noqa: F401
    item_issue_number,
    item_pack_hash,
    item_pack_url,
    item_rom_sha1,
    item_status,
)
from mep_meta_parser import MARKER as MEP_META_MARKER, parse_mep_meta

REPO = "sbihaiko/MesenAI"
OWNER = "sbihaiko"
PROJECT_NUMBER = 3


def run_gh(args):
    result = subprocess.run(["gh", *args], capture_output=True, text=True, check=True)
    return result.stdout


def fetch_accepted_items(accepted_statuses):
    """Lists the Project 3 items whose Status is one of the accepted states.

    CONFIRMED live (gh 2.83.1) against the real GitHub API: the Status field
    id (PVTSSF_lAHOB1MsbM4BhjpNzhge86c) and the Pack Hash one
    (PVTF_lAHOB1MsbM4BhjpNzhge9Is), via `gh project field-list`. Per-item key
    names were an open COVERAGE GAP at write time (`gh project item-list`
    returned zero items) and are now CONFIRMED (2026-08-29, board holds 11
    accepted items): `gh project item-list` lowercases the first letter of
    each Project field name, so "Pack URL"/"Pack Hash" surface as
    "pack URL"/"pack Hash" item keys. The accessors live in
    `pack_board_fields` (one lookup for every board reader, bug #685): gh's
    key first, then the older spellings; parsing is defensive and must
    not crash on an unexpected key.
    Any negative conclusion (e.g. "no MEI entry") is qualified by this gap:
    an absent key may mean the datastore never held the value, not that the
    field is genuinely unset.
    """
    # Bug #670: an explicit --limit, and a listing that may be truncated is
    # refused (SystemExit) instead of silently dropping rows past the 30th.
    items = gh_project_items.list_items(run_gh, PROJECT_NUMBER, OWNER)
    return [it for it in items if item_status(it) in accepted_statuses]


def fetch_issue_details(issue_number):
    raw = run_gh(["issue", "view", str(issue_number), "--repo", REPO,
                  "--json", "author,createdAt,title,labels,url,reactionGroups,body"])
    return json.loads(raw)


def fetch_mep_meta_comment_body(issue_number):
    raw = run_gh(["api", f"repos/{REPO}/issues/{issue_number}/comments", "--paginate"])
    try:
        comments = json.loads(raw)
    except json.JSONDecodeError:
        comments = None
    comments = comments if isinstance(comments, list) else []
    for comment in comments:
        if not isinstance(comment, dict):
            continue
        login = (comment.get("user") or {}).get("login")
        body = comment.get("body") or ""
        if login == OWNER and MEP_META_MARKER in body:
            return body
    return None


def fetch_mep_meta(issue_number):
    """Parses this issue's bot-owned mep-meta comment, or None if absent."""
    body = fetch_mep_meta_comment_body(issue_number)
    return parse_mep_meta(body) if body else None


def parse_form_field(body, heading):
    """The answer under a '### <heading>' Issue Form section; None when absent."""
    if not body:
        return None
    pattern = re.compile(
        r"^###\s+" + re.escape(heading) + r"\s*\n+(.*?)(?=\n###\s|\Z)",
        re.MULTILINE | re.DOTALL,
    )
    match = pattern.search(body)
    if not match:
        return None
    value = match.group(1).strip()
    return value or None


def issue_form_fields(details):
    """Form fields the row/MEI entry share ("credits" = declared pack author)."""
    body = details.get("body") or ""
    game = parse_form_field(body, "Target game/ROM and region") or details.get("title") or "(no title)"
    console = parse_form_field(body, "Console") or markdown.console_from_labels(details.get("labels"))
    license_ = parse_form_field(body, "External assets license (optional)") or "unknown"
    credits = parse_form_field(body, "Author/credits")
    return {"game": game, "console": console, "license": license_, "credits": credits}
