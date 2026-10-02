#!/usr/bin/env python3
"""generate_community_cheat_catalog.py - regenerates docs/community-cheats.json,
the community cheat list of ADR-0248 section 4 (slice R.4).

Reads the open `cheat:valid` issues (the `[Cheat]` Issue Form,
.github/ISSUE_TEMPLATE/cheat-code.yml) and writes one catalog:

  * grouped by SHA-1 - the cheat hash the client matches exactly
    (`EmuApi.GetRomHash(HashType.Sha1Cheat)`, section 5), one game per SHA-1,
    games in SHA-1 order so a regeneration diffs only where a row changed;
  * one row per issue: issue number, console, code, description, 👍;
  * rows most-👍-first, a tie keeping the earlier issue first.

Removal is the author closing the issue (section 6): only open issues are
read, so the next run drops a closed one. A row is listed only when the gate
of scripts/cheat_submission.py would still accept it today, against the other
open rows and the bundled list: a label set by hand on a malformed code, or a
later copy of an earlier row, stays out. Votes rank and never remove.

The output holds codes and one-line descriptions only, never ROM bytes, and no
date: a run with nothing new writes the same bytes and commits nothing.

Usage:
  python3 scripts/generate_community_cheat_catalog.py              # reads GitHub via `gh`
  python3 scripts/generate_community_cheat_catalog.py --issues-file issues.json [--output PATH]

`issues.json` is `gh issue list --label cheat:valid --state open --json
number,state,title,body,labels,reactionGroups`. Stdlib only.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import cheat_submission as cs  # noqa: E402

REPO = "sbihaiko/MesenAI"
FORMAT = "mesenai-community-cheats"
VERSION = 1
OUTPUT_PATH = SCRIPTS.parent / "docs" / "community-cheats.json"
GH_FIELDS = "number,state,title,body,labels,reactionGroups"


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


def _is_live(issue):
    return (issue.get("state") or "OPEN").upper() == "OPEN" and cs.LABEL_VALID in _labels(issue)


def _code(text):
    """The submitted code as one effect: parts upper-cased, joined by a bare `+`."""
    return "+".join(part.upper() for part in cs.split_code(text))


def _game_name(sha1, typed, names, no_intro):
    """The name the gate titles the issue with: the bundled list's, then the
    No-Intro data's, then the typed one when it passes the same bounds."""
    typed = cs.plain(typed)
    fallback = typed if len(typed) <= cs.DESCRIPTION_MAX and not cs.has_link(typed) else ""
    return names.get(sha1) or no_intro.get(sha1) or fallback


def build_catalog(issues, bundled, no_intro):
    """The catalog dict from `issues` (gh-shaped), `bundled`
    (cheat_submission.load_bundled()) and `no_intro`
    (cheat_submission.load_no_intro())."""
    live = [i for i in issues if _is_live(i)]
    peers = [{"number": i.get("number", 0), "body": i.get("body") or ""} for i in live]
    names, _ = bundled
    games = {}
    for issue in sorted(live, key=lambda i: i.get("number", 0)):
        number = issue.get("number", 0)
        verdict = cs.evaluate(issue.get("body") or "", issue.get("title") or "", _labels(issue),
                              number, peers, bundled, no_intro)
        if verdict.verdict != "valid":
            failed = [line.split("`")[1] for line in verdict.comment.splitlines() if line.startswith("- `")]
            _warn(f"issue #{number} carries {cs.LABEL_VALID} but the gate refuses it now ({', '.join(failed)}); not listed.")
            continue
        f = verdict.fields
        sha1 = f["sha1"].strip().upper()
        game = games.setdefault(sha1, {"sha1": sha1, "name": _game_name(sha1, f["game"], names, no_intro), "cheats": []})
        game["cheats"].append({
            "issue": number,
            "console": cs.CONSOLES[f["console"].strip()],
            "code": _code(f["code"]),
            "description": f["description"].strip(),
            "votes": thumbs_up(issue),
        })
    for game in games.values():
        game["cheats"].sort(key=lambda c: (-c["votes"], c["issue"]))
    return {"format": FORMAT, "version": VERSION, "repository": REPO,
            "games": [games[sha1] for sha1 in sorted(games)]}


def render(catalog):
    return json.dumps(catalog, indent=2, ensure_ascii=False) + "\n"


# A result that reaches the limit may be truncated: refuse it rather than drop live rows
FETCH_LIMIT = 5000


def fetch_issues():
    out = subprocess.run(["gh", "issue", "list", "--repo", REPO, "--label", cs.LABEL_VALID, "--state", "open",
                          "--limit", str(FETCH_LIMIT), "--json", GH_FIELDS], capture_output=True, text=True,
                         check=True).stdout
    issues = json.loads(out)
    if len(issues) >= FETCH_LIMIT:
        raise SystemExit(f"gh returned {len(issues)} issues (the limit); refusing to write a truncated catalog")
    return issues


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--issues-file", help="gh-shaped JSON list of issues, instead of asking GitHub")
    ap.add_argument("--output", default=str(OUTPUT_PATH))
    args = ap.parse_args(argv[1:])

    issues = json.loads(Path(args.issues_file).read_text(encoding="utf-8")) if args.issues_file else fetch_issues()
    catalog = build_catalog(issues, cs.load_bundled(), cs.load_no_intro())
    Path(args.output).write_text(render(catalog), encoding="utf-8")
    rows = sum(len(g["cheats"]) for g in catalog["games"])
    print(f"{args.output}: {rows} community cheat(s) for {len(catalog['games'])} game(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
