#!/usr/bin/env python3
"""Bug #670: every `gh project item-list` read of the community-pack board
passes an explicit `--limit` and refuses a listing that may be truncated.

`gh project item-list` returns 30 items unless told otherwise. Past row 30 an
accepted pack silently dropped out of docs/community-packs.json (and out of
the client's auto-install set) and the identity check went blind to it.

The fake `run_gh` below records argv and answers like gh does: at most
`--limit` items (30 when the flag is absent), with the board's `totalCount`.

Usage: python3 scripts/test_gh_project_item_limit.py
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "scripts"))
import mei_catalog_fetch  # noqa: E402
import mep_identity_check  # noqa: E402

GH_DEFAULT_LIMIT = 30
ACCEPTED = "Aceito parcial (HD Mesen)"
FAILURES = []


def check(name, cond, detail=""):
    if cond:
        print(f"PASS {name}")
    else:
        FAILURES.append(name)
        print(f"FAIL {name}{': ' + detail if detail else ''}")


class FakeGh:
    """Records every gh argv; serves a board of `board_size` accepted items."""

    def __init__(self, board_size, served=None):
        self.board_size = board_size
        self.served = board_size if served is None else served  # what gh hands back
        self.calls = []

    def item_list_limit(self):
        for argv in self.calls:
            if argv[:2] == ["project", "item-list"]:
                return int(argv[argv.index("--limit") + 1]) if "--limit" in argv else None
        return None

    def __call__(self, args):
        self.calls.append(list(args))
        if args[:2] == ["project", "item-list"]:
            limit = int(args[args.index("--limit") + 1]) if "--limit" in args else GH_DEFAULT_LIMIT
            items = [{"status": ACCEPTED, "content": {"number": 1000 + n}, "pack URL": f"https://x/{n}"}
                     for n in range(min(limit, self.served))]
            return json.dumps({"items": items, "totalCount": self.board_size})
        if args[:2] == ["issue", "view"]:
            return json.dumps({"author": {"login": "someone"}})
        if args[:1] == ["api"]:
            return "[]"
        if args[:2] in (["issue", "comment"], ["issue", "edit"]):
            return ""
        raise AssertionError(f"unexpected gh call: {args}")


def refuses(fn):
    try:
        fn()
    except SystemExit as exc:
        return exc.code not in (0, None)
    return False


def identity_main(fake):
    mep_identity_check.run_gh = fake
    return mep_identity_check.main(["mep_identity_check.py", "check", "--issue-number", "7",
                                    "--pack-url", "https://github.com/a/b/releases/download/v1/p.zip",
                                    "--content-id", "c" * 64])


def main():
    # Catalog generator read (scripts/mei_catalog_fetch.py).
    fake = FakeGh(40)
    mei_catalog_fetch.run_gh = fake
    got = mei_catalog_fetch.fetch_accepted_items({ACCEPTED})
    check("catalog: a 40-item board yields all 40 accepted items", len(got) == 40, f"got {len(got)}")
    limit = fake.item_list_limit()
    check("catalog: item-list passes an explicit --limit", limit is not None)
    if limit is not None:
        fake = FakeGh(limit)
        mei_catalog_fetch.run_gh = fake
        check("catalog: a listing that reaches --limit is refused (non-zero exit)",
              refuses(lambda: mei_catalog_fetch.fetch_accepted_items({ACCEPTED})))
        fake = FakeGh(40, served=35)
        mei_catalog_fetch.run_gh = fake
        check("catalog: totalCount above the returned count is refused",
              refuses(lambda: mei_catalog_fetch.fetch_accepted_items({ACCEPTED})))

    # Identity check read (scripts/mep_identity_check.py).
    fake = FakeGh(40)
    identity_main(fake)
    limit = fake.item_list_limit()
    check("identity: item-list passes an explicit --limit", limit is not None)
    if limit is not None:
        check("identity: a listing that reaches --limit is refused (non-zero exit)",
              refuses(lambda: identity_main(FakeGh(limit))))

    # Drift check (.github/workflows/community-pack-drift-check.yml): the shell
    # call carries the same limit and the listing goes through the same
    # completeness check before any item is read.
    wf = (REPO / ".github/workflows/community-pack-drift-check.yml").read_text(encoding="utf-8")
    calls = re.findall(r"^[ \t]*[^#\n]*gh project item-list[^\n]*", wf, re.MULTILINE)
    check("drift-check: has an item-list call", bool(calls))
    for call in calls:
        check(f"drift-check: `{call.strip()}` passes --limit", "--limit" in call)
    check("drift-check: the listing is refused when it may be truncated",
          "scripts/gh_project_items.py check" in wf)

    # No other `gh project item-list` call anywhere in scripts/ or workflows
    # may go without --limit.
    for path in sorted([*REPO.glob("scripts/*.py"), *REPO.glob("scripts/*.sh"),
                        *REPO.glob(".github/workflows/*.yml")]):
        if path.name.startswith("test_"):
            continue
        # Python builds argv lists; shell/YAML spell the command out (prose
        # mentions in comments and docstrings are not calls).
        pattern = r'\["project", "item-list"[^\n]*' if path.suffix == ".py" else r'^[ \t]*[^#\n]*gh project item-list[^\n]*'
        for m in re.finditer(pattern, path.read_text(encoding="utf-8"), re.MULTILINE):
            line = m.group(0)
            check(f"{path.relative_to(REPO)}: item-list call passes --limit", "--limit" in line, line.strip())

    if FAILURES:
        print(f"{len(FAILURES)} FAILED")
        return 1
    print("all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
