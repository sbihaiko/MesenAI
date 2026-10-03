#!/usr/bin/env python3
"""Issue-level half of ADR-0205 sections 5 and 6, for replay-submitted.yml.

Given a `[Replay]` submission issue it
  1. reads the attachment URL off the Issue Form body (the author dragged the
     `.mmo` into the form in their own browser, section 6),
  2. downloads it -- allow-listed hosts only, capped at the section 3 size
     before a byte is inflated -- through scripts/fetch_pack.py's per-hop
     allow-list with the replay-specific scripts/replay_host_allowlist.json.
     Only a 404/410 (the attachment is gone) or a refusal (off the
     allow-list, over the cap) is a verdict; any other failure is transient
     and exits EXIT_TRANSIENT with no verdict, so the workflow fails the step
     and keeps the labels the issue already carries (#700),
  3. runs the section 3 lint and the section 8 structural gate
     (scripts/replay_lint.py) over it, and refuses a copy of a recording the
     committed catalog already lists (section 7: the row key is the issue
     number, the dedupe key the archive's sha256),
  4. decides the verdict: label `replay:valid` or `replay:invalid`, the comment
     the workflow posts, and the whole-title rewrite of section 5
     (`[Replay] <game> -- <alias> -- <subtitle>`, every part read off the
     artifact, never typed).

Nothing here talks to GitHub: the workflow reads the issue from its event
payload and applies the JSON this prints with `gh`. `evaluate` is pure given an
injected `fetch`, which is how scripts/test_replay_submission.py proves the
issue -> label round trip without a network.

Usage (the workflow's call):
  python3 scripts/replay_submission.py --body-file B --title T --login L
      [--labels "replay,replay:invalid"] [--number N] [--archive file]   > verdict.json

Exit 0 with the verdict on stdout; EXIT_TRANSIENT (75) with nothing on stdout
when the attachment could not be fetched for a reason that is not a verdict.

Stdlib only.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
import urllib.error
from collections import namedtuple
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import fetch_pack  # noqa: E402
import replay_lint  # noqa: E402

ROOT = SCRIPTS.parent
ALLOWLIST = SCRIPTS / "replay_host_allowlist.json"
CATALOG = ROOT / "docs" / "community-packs.json"
# Section 7's live rows, for the duplicate check (written by
# scripts/generate_community_replay_catalog.py).
REPLAY_CATALOG = ROOT / "docs" / "community-replays.json"

# Labels. ADR-0205 names only `replay:removed` (section 9, slice R.2); the
# submission and verdict labels below follow the pack flow's
# community-pack / pack:valid / pack:invalid shape.
LABEL_SUBMITTED = "replay"
LABEL_VALID = "replay:valid"
LABEL_INVALID = "replay:invalid"

TITLE_PREFIX = "[Replay] "
SEPARATOR = " — "
# Section 5: the Description is bounded at 10 KB and cannot go in a title
# whole; the untruncated text stays in the issue body. The ADR says "a fixed
# length" without a number.
SUBTITLE_MAX = 60
# The verdict comment is upserted (gh issue comment --edit-last) on this marker.
COMMENT_MARKER = "<!-- replay-verdict -->"

ATTACHMENT_RE = re.compile(r"https://github\.com/user-attachments/(?:files|assets)/[^\s)\]>\"']+")
# GitHub's title limit is 256; stay under it.
TITLE_MAX = 250

# Status codes that mean "the attachment is gone", not "the network failed".
GONE_STATUSES = (404, 410)
# sysexits.h EX_TEMPFAIL: the fetch failed transiently; no verdict was reached.
EXIT_TRANSIENT = 75


class AttachmentGone(Exception):
    """The host says the attachment no longer exists (ADR-0148's stale row)."""


class AttachmentRefused(Exception):
    """The download itself refuses the row (over the cap, off the allow-list):
    a verdict on this attachment, not a network failure."""


class TransientFetchError(Exception):
    """Any other fetch failure (a reset, a timeout, a 5xx, a DNS blip): no
    verdict on the attachment, which may well be valid (#700)."""


Verdict = namedtuple("Verdict", "verdict title title_changed labels_add labels_remove comment facts")


def extract_attachment_url(body):
    """First `github.com/user-attachments/{files,assets}/...` link in the issue
    body, else None. Anything else the author pasted is not the replay."""
    for match in ATTACHMENT_RE.finditer(body or ""):
        url = match.group(0)
        # A traversing path could normalize onto another GitHub path (a release
        # asset); the attachment is the artifact (ADR-0205 sections 6, 10).
        if ".." not in url and "%2e" not in url.lower():
            return url
    return None


def load_catalog(path=CATALOG):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8")).get("packs", [])
    except (OSError, ValueError):
        return []


def load_live_replays(path=REPLAY_CATALOG):
    """(issue, sha256) of every row of the committed replay catalog."""
    try:
        data = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return []
    rows = []
    for game in data.get("games", []) if isinstance(data, dict) else []:
        for row in game.get("replays", []) if isinstance(game, dict) else []:
            if isinstance(row, dict) and isinstance(row.get("issue"), int) and row.get("sha256"):
                rows.append((row["issue"], str(row["sha256"]).lower()))
    return rows


def resolve_game(sha1, catalog, game_file, no_intro_sha1=""):
    """The game name for the archive's ROM (section 5), from the catalog's
    accepted rows (`rom.sha1` and `rom.sha1s`, the No-Intro hashes the client
    matches on). The movie's `NoIntroSHA1` is tried first: on NES its `SHA1`
    is the whole-file hash, iNES header included, which never equals a
    No-Intro hash (ADR-0003/ADR-0039, issue #624). An unknown hash falls back
    to the ROM file name's stem, which is the artifact's own claim and the
    best the repository can say."""
    known = {}
    for row in catalog:
        rom = row.get("rom") or {}
        for h in [rom.get("sha1", "")] + list(rom.get("sha1s", []) or []):
            if h:
                known.setdefault(h.upper(), row.get("game", ""))
    for wanted in (no_intro_sha1, sha1):
        if wanted and wanted.upper() in known:
            return known[wanted.upper()]
    return re.sub(r"\.[A-Za-z0-9]{1,4}$", "", game_file or "").strip()


def build_subtitle(description):
    first = (description or "").strip().splitlines()
    line = first[0].strip() if first else ""
    if len(line) > SUBTITLE_MAX:
        line = line[: SUBTITLE_MAX - 1].rstrip() + "…"
    return line


def _plain(text):
    """Submitter text for a title: control, format (bidi) and separator
    characters dropped, whitespace collapsed."""
    kept = "".join(" " if unicodedata.category(c) in ("Zl", "Zp") else c
                   for c in text if unicodedata.category(c) not in ("Cc", "Cf"))
    return " ".join(kept.split())


def _defang(text):
    """Text echoed in a comment: no code-span break-out, no @mention, no #ref."""
    return replay_lint.plain_text(text).replace("`", "'").replace("@", "@\u200b").replace("#", "#\u200b")


def build_title(facts, login, catalog):
    game = _plain(resolve_game(facts.get("sha1"), catalog, facts.get("game_file"),
                              facts.get("no_intro_sha1")))
    alias = _plain(facts.get("author") or "") or login
    parts = [game, alias]
    subtitle = _plain(build_subtitle(facts.get("description")))
    if subtitle:
        parts.append(subtitle)
    title = TITLE_PREFIX + SEPARATOR.join(parts)
    if len(title) > TITLE_MAX:
        title = title[: TITLE_MAX - 1].rstrip() + "…"
    return title


def _labels_for(valid):
    """(add, remove): the verdict label and its opposite. Only the verdict pair
    is ever touched -- `replay` (the form's label) and `replay:removed` (R.2, a
    maintainer lever) are outside this step. The workflow applies both
    idempotently (it removes only a label the issue actually carries)."""
    return ([LABEL_VALID], [LABEL_INVALID]) if valid else ([LABEL_INVALID], [LABEL_VALID])


def _comment(valid, findings, url, title, earlier=0):
    lines = [COMMENT_MARKER]
    if valid:
        lines.append("**Accepted.** This is a Record and share replay: no save state, no battery data, "
                     "a ROM SHA-1 and a ROM file name only (ADR-0205 section 3).")
        lines.append(f"Retitled to: `{_defang(title)}`")
        lines.append("Edited the attachment? Comment `/revalidate`.")
    else:
        lines.append("**Not accepted.** The replay was refused for the reason(s) below. "
                     "Re-record it with the **Record and share** action, attach the new file and comment `/revalidate`.")
        for code, message in findings:
            lines.append(f"- `{code}`: {_defang(message)}")
        if earlier:
            # An int from the committed catalog, so the reference is not defanged.
            lines.append(f"The same recording is already listed as #{earlier}; vote there with 👍.")
    if url:
        lines.append(f"<sub>Checked attachment: {url}</sub>")
    return "\n\n".join(lines)


def evaluate(issue_body, issue_title, issue_labels, login, catalog, fetch, number=0, live=()):
    """Pure verdict for one submission. `fetch(url) -> bytes` raises
    AttachmentGone or AttachmentRefused for a verdict on the attachment; any
    other failure raises TransientFetchError out of here, because a network
    blip must never de-list a live replay (#700). `live` is the committed
    catalog's (issue, sha256) rows (load_live_replays); `number` is this
    issue's, so a re-validation never finds itself."""
    url = extract_attachment_url(issue_body)
    findings = []
    facts = {}
    data = None
    if url is None:
        findings.append(("no-attachment", "no replay file is attached: drag the `.mmo` (or the same file renamed to `.zip`) "
                         "into the form's attachment box, then comment `/revalidate`."))
    else:
        try:
            data = fetch(url)
        except AttachmentGone as exc:
            findings.append(("download-failed", f"the attachment is gone (deleted by its author?): {exc}"))
        except AttachmentRefused as exc:
            findings.append(("download-failed", f"the attachment could not be fetched: {exc}"))
        except (OSError, ValueError) as exc:
            raise TransientFetchError(f"the attachment could not be fetched ({exc}); no verdict") from exc
    if data is not None:
        result = replay_lint.lint_bytes(data)
        facts = result.facts
        findings.extend((f.code, f.message) for f in result.findings)
    earlier = 0
    if not findings and facts.get("sha256"):
        earlier = next((issue for issue, sha in live if sha == facts["sha256"] and issue != number), 0)
        if earlier:
            findings.append(("duplicate", "this archive is byte-identical to a replay the catalog already lists "
                                          "(ADR-0205 section 7): one recording is one row."))

    valid = not findings
    title = issue_title
    if facts.get("sha1"):
        # Section 5: the whole title is rewritten from the artifact, valid or not,
        # so a rejected submission is still listed as what it claims to be.
        title = build_title(facts, login, catalog)
    add, remove = _labels_for(valid)
    return Verdict(
        verdict="valid" if valid else "invalid",
        title=title,
        title_changed=title != issue_title,
        labels_add=add,
        labels_remove=remove,
        comment=_comment(valid, findings, url, title, earlier),
        facts=facts,
    )


def fetch_attachment(url, opener=None):
    """The attachment's bytes through fetch_pack's per-hop allow-list (the
    replay allow-list, scripts/replay_host_allowlist.json), public-IP check
    and the section 3 cap, enforced while streaming. Shared with
    scripts/generate_community_replay_catalog.py, so the gate and the catalog
    read one download the same way."""
    hosts = fetch_pack.load_allowlist(ALLOWLIST)
    if fetch_pack.match_host(url, hosts) is None:
        raise AttachmentRefused("host not allow-listed")
    try:
        resp, _ = fetch_pack.open_validated(url, hosts, opener=opener)
    except urllib.error.HTTPError as exc:
        if exc.code in GONE_STATUSES:
            raise AttachmentGone(str(exc.code)) from exc
        raise
    cap = replay_lint.MAX_ARCHIVE_BYTES
    declared = resp.headers.get("Content-Length")
    if declared is not None and declared.isdigit() and int(declared) > cap:
        raise AttachmentRefused(f"Content-Length {declared} is over the {cap}-byte cap")
    data = resp.read(cap + 1)
    if len(data) > cap:
        raise AttachmentRefused(f"the body is over the {cap}-byte cap")
    return data


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--body-file", required=True)
    ap.add_argument("--title", required=True)
    ap.add_argument("--login", required=True)
    ap.add_argument("--labels", default="")
    ap.add_argument("--number", type=int, default=0, help="this issue's number (section 7 duplicate check)")
    ap.add_argument("--archive", help="use this local file instead of downloading the attachment")
    args = ap.parse_args(argv[1:])

    body = Path(args.body_file).read_text(encoding="utf-8")
    labels = [x.strip() for x in args.labels.split(",") if x.strip()]
    fetch = (lambda _url: Path(args.archive).read_bytes()) if args.archive else fetch_attachment
    try:
        verdict = evaluate(body, args.title, labels, args.login, load_catalog(), fetch,
                           number=args.number, live=load_live_replays())
    except TransientFetchError as exc:
        print(f"replay_submission: {exc}", file=sys.stderr)
        return EXIT_TRANSIENT
    print(json.dumps(verdict._asdict(), indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
