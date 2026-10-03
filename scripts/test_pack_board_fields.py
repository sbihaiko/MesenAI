#!/usr/bin/env python3
"""Bug #685: every reader of the community-pack board resolves a field through
one lookup (scripts/pack_board_fields.py), keyed the way `gh project
item-list --format json` really emits it.

gh lowercases the first letter of each Project field name, so "Pack URL" and
"Pack Hash" arrive as "pack URL" and "pack Hash" (confirmed live 2026-10-02:
`['category', 'console', 'content', 'game', 'id', 'labels', 'pack Hash',
'pack MD5', 'pack URL', 'repository', 'status', 'title']`). The identity check
read the URL as packUrl / 'Pack URL' / pack_url and the drift check read the
hash as 'Pack Hash' / packHash / pack_hash: both got nothing on every item, so
the origin-collision check never fired and the drift check skipped the board.

Usage: python3 scripts/test_pack_board_fields.py
"""
from __future__ import annotations

import contextlib
import io
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "scripts"))
import mep_identity_check  # noqa: E402

ACCEPTED = "Aceito parcial (HD Mesen)"
# One board item exactly as `gh project item-list --format json` emits it.
HOLDER_ITEM = {
    "id": "PVTI_holder",
    "title": "[HD Pack] Contra",
    "status": ACCEPTED,
    "content": {"number": 5, "type": "Issue"},
    "labels": ["community-pack", "pack:valid"],
    "pack URL": "https://github.com/sbihaiko/contra80s/releases/download/v1/contra.zip",
    "pack Hash": "a" * 64,
    "pack MD5": "b" * 32,
}
FAILURES = []


def check(name, cond, detail=""):
    if cond:
        print(f"PASS {name}")
    else:
        FAILURES.append(name)
        print(f"FAIL {name}{': ' + detail if detail else ''}")


# The holder's bot-owned mep-meta comment records a declared pack_id but no
# pack_origin (pre-P.2), so its origin comes from the board's pack URL.
HOLDER_META = '<!-- mep-meta -->\n```json\n{"pack_id": "contra-hd"}\n```\n'


def fake_gh(args):
    """Answers the identity check's gh reads: the board holds HOLDER_ITEM,
    whose issue #5 carries HOLDER_META; every issue was opened by 'someone'."""
    if args[:2] == ["project", "item-list"]:
        return json.dumps({"items": [HOLDER_ITEM], "totalCount": 1})
    if args[:1] == ["api"]:
        if "/issues/5/" in args[1]:
            return json.dumps([{"user": {"login": "sbihaiko"}, "body": HOLDER_META}])
        return "[]"
    if args[:2] == ["issue", "view"]:
        return json.dumps({"author": {"login": "someone"}})
    raise AssertionError(f"unexpected gh call: {args}")


def identity_decision():
    """Runs `mep_identity_check check` for a Google Drive submission (no repo:
    its origin is the issue author, 'someone') that declares the holder's
    pack_id; returns its stdout. The holder is bound to sbihaiko/contra80s
    only if its pack URL is read; with the URL missed, it falls back to the
    same login and the claim looks like a revision."""
    with tempfile.TemporaryDirectory() as tmp:
        classify = Path(tmp) / "classify.json"
        classify.write_text(json.dumps({"recipe": {"pack": {"id": "contra-hd"}}}), encoding="utf-8")
        mep_identity_check.run_gh = fake_gh
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            mep_identity_check.main(["mep_identity_check.py", "check", "--issue-number", "7",
                                     "--pack-url", "https://drive.google.com/file/d/abc/view",
                                     "--content-id", "c" * 64, "--classify", str(classify)])
        return out.getvalue()


def main():
    out = identity_decision()
    check("identity: a foreign origin claiming a board item's pack_id is an origin collision",
          "decision=origin" in out and "origin=sbihaiko/contra80s" in out and "holder=5" in out,
          out.strip().replace("\n", " | "))

    # Drift check: the stored hash is read through the shared lookup, never
    # from a key spelled in the workflow.
    wf = (REPO / ".github/workflows/community-pack-drift-check.yml").read_text(encoding="utf-8")
    check("drift-check: board items go through scripts/pack_board_fields.py",
          "scripts/pack_board_fields.py normalize items.json > board.json" in wf
          and "items.json)" not in wf, "a loop still reads the raw listing")
    with tempfile.TemporaryDirectory() as tmp:
        listing = Path(tmp) / "items.json"
        listing.write_text(json.dumps({"items": [HOLDER_ITEM]}), encoding="utf-8")
        proc = subprocess.run([sys.executable, str(REPO / "scripts/pack_board_fields.py"), "normalize", str(listing)],
                              capture_output=True, text=True)
        board = json.loads(proc.stdout or "{}").get("items") or [{}]
    check("normalize: a gh item-list item yields its status, issue, labels, pack URL and hash",
          proc.returncode == 0 and board[0] == {
              "status": ACCEPTED, "issue_number": 5, "labels": HOLDER_ITEM["labels"],
              "pack_url": HOLDER_ITEM["pack URL"], "pack_hash": "a" * 64, "rom_sha1": None},
          f"exit {proc.returncode}: {board[0]!r}")

    # One lookup: no reader outside pack_board_fields.py indexes a board item
    # by a board field key ("pack URL"/"Pack URL"/packUrl, likewise Hash, MD5
    # and ROM SHA1; snake_case names are this repo's own normalized keys), whether `item.get("...")`, `item["..."]` or jq `.["..."]`.
    key = re.compile(r"""(?:\.get\(|\[)\s*(["'])(?:[Pp]ack (?:URL|Hash|MD5)|pack(?:Url|Hash|Md5)|ROM SHA1|romSha1)\1""")
    offenders = []
    for path in sorted([*REPO.glob("scripts/*.py"), *REPO.glob("scripts/*.sh"),
                        *REPO.glob("scripts/checks/**/*.py"), *REPO.glob(".github/workflows/*.yml")]):
        if path.name.startswith("test_") or path.name == "pack_board_fields.py":
            continue
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if key.search(line):
                offenders.append(f"{path.relative_to(REPO)}:{lineno}: {line.strip()}")
    check("no board field key indexed outside scripts/pack_board_fields.py",
          not offenders, "\n  " + "\n  ".join(offenders))

    if FAILURES:
        print(f"{len(FAILURES)} FAILED")
        return 1
    print("all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
