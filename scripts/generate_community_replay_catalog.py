#!/usr/bin/env python3
"""generate_community_replay_catalog.py - regenerates docs/community-replays.json,
the shared-replay catalog of ADR-0205 section 7 (slice R.2).

Reads the open `replay:valid` issues (the `[Replay]` Issue Form,
.github/ISSUE_TEMPLATE/replay.yml) and writes one catalog:

  * grouped by ROM SHA-1 - the hash the `.mmo` itself carries (`SHA1` in
    GameSettings.txt, i.e. what `Emulator::GetHash(HashType::Sha1)` returns for
    the ROM as loaded, before any patch applies), which the client matches
    exactly and never by title (section 7); games in SHA-1 order so a
    regeneration diffs only where a row changed;
  * one row per issue (section 7's row key): the attachment's stable
    `github.com/user-attachments/...` URL (never the signed redirect target,
    section 6), the sha256 and size of the bytes this run downloaded (the
    client verifies them before playing), console, author, subtitle, frames,
    the `Cheat <Type> <Code>` lines as `cheats[]` (type and code verbatim) and
    the 👍 count;
  * rows most-👍-first, a tie keeping the earlier issue first.

The structural gate validates before listing (section 8): every listed
attachment is downloaded again - allow-listed hosts only, capped at the 8 MB
section 3 cap - and re-run through scripts/replay_lint.py, so a hand-set
`replay:valid`, an attachment swapped after validation, or an archive the gate
now refuses stays out. One recording is one row: a later issue whose archive
hashes identical to an earlier one is dropped (the dedupe key, section 7).

Removal (section 9): closing the issue removes the row on the next run, since
only open issues are read, and reopening restores it. `replay:removed`, the
maintainers' lever, is honoured before the state: such an issue is never
listed. An attachment the host answers 404/410 for (the author deleted it) is
dropped as a stale row (ADR-0148); any other download failure refuses to write
the catalog at all, so a network blip never de-lists live rows.

The output holds URLs, hashes and short text only, never replay bytes, and no
date: a run with nothing new writes the same bytes and commits nothing.

Usage:
  python3 scripts/generate_community_replay_catalog.py              # reads GitHub via `gh`
  python3 scripts/generate_community_replay_catalog.py --issues-file issues.json \\
      [--archives-dir DIR] [--output PATH]

`issues.json` is `gh issue list --label replay:valid --state open --json
number,state,title,body,labels,reactionGroups,author`. With --archives-dir the
attachment named `<issue>.mmo` in DIR stands in for each download (offline).
Stdlib only.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import urllib.error
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import fetch_pack  # noqa: E402
import replay_lint  # noqa: E402
import replay_submission as rs  # noqa: E402

REPO = "sbihaiko/MesenAI"
FORMAT = "mesenai-community-replays"
VERSION = 1
OUTPUT_PATH = SCRIPTS.parent / "docs" / "community-replays.json"
GH_FIELDS = "number,state,title,body,labels,reactionGroups,author"
LABEL_REMOVED = "replay:removed"


# The download and its verdict exceptions live in replay_submission, which the
# submission gate shares (#700); these names are kept for this module's API.
AttachmentGone = rs.AttachmentGone
AttachmentRefused = rs.AttachmentRefused
fetch_attachment = rs.fetch_attachment


def _warn(message):
    print(f"WARNING: {message}", file=sys.stderr)


def thumbs_up(issue):
    """The issue's 👍 count, from `gh`'s reactionGroups (0 when absent)."""
    for group in issue.get("reactionGroups") or []:
        if group.get("content") == "THUMBS_UP":
            return int((group.get("users") or {}).get("totalCount", 0) or 0)
    return 0


def _labels(issue):
    return [label.get("name", "") if isinstance(label, dict) else str(label) for label in issue.get("labels") or []]


def _login(issue):
    author = issue.get("author") or {}
    return (author.get("login") if isinstance(author, dict) else str(author)) or ""


def is_live(issue):
    """Section 9: `replay:removed` first, whatever the state; then open and
    `replay:valid`."""
    labels = _labels(issue)
    if LABEL_REMOVED in labels:
        return False
    return (issue.get("state") or "OPEN").upper() == "OPEN" and rs.LABEL_VALID in labels


def _row(issue, url, facts, catalog):
    alias = rs._plain(facts.get("author") or "") or _login(issue)
    return {
        "issue": issue.get("number", 0),
        "url": url,
        "sha256": facts["sha256"],
        "size": facts["size"],
        "console": facts["console"],
        # NoIntroSHA1 first, as the issue title is named (#697): on NES the
        # movie's SHA1 is the whole-file hash and never names a catalog row.
        "game": rs._plain(rs.resolve_game(facts["sha1"], catalog, facts.get("game_file"),
                                          facts.get("no_intro_sha1", ""))),
        "author": alias,
        "subtitle": rs._plain(rs.build_subtitle(facts.get("description"))),
        "frames": facts["frames"],
        "cheats": [{"type": kind, "code": code} for kind, code in facts.get("cheats", [])],
        "votes": thumbs_up(issue),
    }


def build_catalog(issues, fetch, packs_catalog):
    """The catalog dict from `issues` (gh-shaped). `fetch(url) -> bytes` raises
    AttachmentGone for a deleted attachment and anything else for a failure
    that must stop the run. `packs_catalog` is the pack catalog's rows, read
    for the game name as the title rule reads it (section 5)."""
    games = {}
    seen = {}
    for issue in sorted((i for i in issues if is_live(i)), key=lambda i: i.get("number", 0)):
        number = issue.get("number", 0)
        url = rs.extract_attachment_url(issue.get("body") or "")
        if url is None:
            _warn(f"issue #{number} carries {rs.LABEL_VALID} but has no attachment link; not listed.")
            continue
        try:
            data = fetch(url)
        except AttachmentGone:
            _warn(f"issue #{number}: the attachment is gone (deleted by its author?); not listed.")
            continue
        except AttachmentRefused as exc:
            _warn(f"issue #{number}: the attachment is refused ({exc}); not listed.")
            continue
        result = replay_lint.lint_bytes(data)
        if not result.ok:
            _warn(f"issue #{number} carries {rs.LABEL_VALID} but the gate refuses it now "
                  f"({', '.join(f.code for f in result.findings)}); not listed.")
            continue
        facts = result.facts
        if facts["sha256"] in seen:
            _warn(f"issue #{number} is byte-identical to #{seen[facts['sha256']]}; not listed (one recording, one row).")
            continue
        seen[facts["sha256"]] = number
        sha1 = facts["sha1"].upper()
        row = _row(issue, url, facts, packs_catalog)
        game = games.setdefault(sha1, {"sha1": sha1, "name": row["game"], "replays": []})
        game["replays"].append(row)
    for game in games.values():
        game["replays"].sort(key=lambda r: (-r["votes"], r["issue"]))
    return {"format": FORMAT, "version": VERSION, "repository": REPO,
            "games": [games[sha1] for sha1 in sorted(games)]}


def render(catalog):
    return json.dumps(catalog, indent=2, ensure_ascii=False) + "\n"


# A result that reaches the limit may be truncated: refuse it rather than drop live rows
FETCH_LIMIT = 5000


def fetch_issues():
    out = subprocess.run(["gh", "issue", "list", "--repo", REPO, "--label", rs.LABEL_VALID, "--state", "open",
                          "--limit", str(FETCH_LIMIT), "--json", GH_FIELDS], capture_output=True, text=True,
                         check=True).stdout
    issues = json.loads(out)
    if len(issues) >= FETCH_LIMIT:
        raise SystemExit(f"gh returned {len(issues)} issues (the limit); refusing to write a truncated catalog")
    return issues


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--issues-file", help="gh-shaped JSON list of issues, instead of asking GitHub")
    ap.add_argument("--archives-dir", help="read <issue>.mmo from this folder instead of downloading")
    ap.add_argument("--output", default=str(OUTPUT_PATH))
    args = ap.parse_args(argv[1:])

    issues = json.loads(Path(args.issues_file).read_text(encoding="utf-8")) if args.issues_file else fetch_issues()
    if args.archives_dir:
        by_url = {rs.extract_attachment_url(i.get("body") or ""): i.get("number", 0) for i in issues}

        def fetch(url):
            path = Path(args.archives_dir) / f"{by_url.get(url, 0)}.mmo"
            if not path.is_file():
                raise AttachmentGone(str(path))
            return path.read_bytes()
    else:
        fetch = fetch_attachment
    try:
        catalog = build_catalog(issues, fetch, rs.load_catalog())
    except (OSError, ValueError, urllib.error.URLError) as exc:
        raise SystemExit(f"an attachment could not be fetched ({exc}); refusing to write a catalog that "
                         "would de-list live rows")
    Path(args.output).write_text(render(catalog), encoding="utf-8")
    rows = sum(len(g["replays"]) for g in catalog["games"])
    print(f"{args.output}: {rows} shared replay(s) for {len(catalog['games'])} ROM(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
