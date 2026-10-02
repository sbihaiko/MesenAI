#!/usr/bin/env python3
"""Framework-free checks for scripts/generate_community_cheat_catalog.py - the
catalog of ADR-0248 section 4 that the client reads (section 5) and that drops
a row when its issue closes (section 6).

Nothing here touches the network or GitHub. The issues are
tests/fixtures/community-cheat-catalog/issues.json, shaped as
`gh issue list --json number,state,title,body,labels,reactionGroups` prints
them (the generator's own call).

Checks:
  C-1 an open `cheat:valid` issue is a row: issue number, console, code,
      description and 👍, under its SHA-1 (upper case) and the game's name
      from the bundled list.
  C-2 a closed issue is not a row, whatever its votes (section 6), and an
      issue without `cheat:valid` is not a row either.
  C-3 rows are most-👍-first; a tie keeps the earlier issue first.
  C-4 rows are grouped by SHA-1: one game per SHA-1, games in SHA-1 order.
  C-5 a row the gate would refuse today is dropped: a code that does not
      decode, and a later issue with the same decoded patch as an earlier one.
  C-6 no live issue gives a valid, empty catalog; the output is byte-stable
      (no date), so a regeneration with nothing new commits nothing.
  C-7 the CLI writes the file from --issues-file, and closing an issue drops
      its row on the next run (the PRD's stop rule, offline).
  C-8 the workflow regenerates on a close/reopen, after every gate run and
      daily, commits through a PR (GITHUB_TOKEN cannot open one here), and
      never interpolates issue text into a `run:` script.
"""
from __future__ import annotations

import copy
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import cheat_submission as cs  # noqa: E402
import generate_community_cheat_catalog as gen  # noqa: E402

ROOT = SCRIPTS.parent
FIXTURE = ROOT / "tests" / "fixtures" / "community-cheat-catalog" / "issues.json"
WORKFLOW = ROOT / ".github" / "workflows" / "community-cheat-catalog.yml"
SHA_1942 = "D608F769333B13DA9C67F07599E405944893A950"
SHA_GB = "1" * 40
NO_INTRO = {SHA_GB: "Test GB game"}

FAILURES = []


def check(cond, msg):
    if cond:
        print(f"ok: {msg}")
    else:
        FAILURES.append(msg)
        print(f"FAIL: {msg}")


def issues():
    return json.loads(FIXTURE.read_text(encoding="utf-8"))


BUNDLED = cs.load_bundled()


def build(items):
    return gen.build_catalog(items, BUNDLED, NO_INTRO)


def game(catalog, sha1):
    return next((g for g in catalog["games"] if g["sha1"] == sha1), None)


def numbers(catalog, sha1):
    g = game(catalog, sha1)
    return [c["issue"] for c in g["cheats"]] if g else []


def main():
    catalog = build(issues())

    # C-1
    nes = game(catalog, SHA_1942)
    check(nes is not None, "C-1 the 1942 SHA-1 is a game of the catalog (upper case, though issue 201 typed it lower case)")
    row = next((c for c in (nes or {}).get("cheats", []) if c["issue"] == 201), None)
    check(row == {"issue": 201, "console": "nes", "code": "0436:09", "description": "Start with 9 rolls", "votes": 2},
          f"C-1 issue 201 is one row with its five fields: {row}")
    check(nes is not None and nes["name"] == "1942 (Japan, USA)", f"C-1 the game's name is the bundled list's: {nes and nes['name']}")
    multi = next((c for c in (nes or {}).get("cheats", []) if c["issue"] == 202), None)
    check(multi is not None and multi["code"] == "0437:05+0438:01", f"C-1 a `+`-joined code is kept as one effect, spaces dropped: {multi}")
    gb = game(catalog, SHA_GB)
    check(gb is not None and gb["cheats"][0]["console"] == "gb" and gb["name"] == "Test GB game",
          f"C-1 a Game Boy row is listed under its console: {gb}")

    # C-2
    listed = [c["issue"] for g in catalog["games"] for c in g["cheats"]]
    check(203 not in listed, "C-2 the closed issue 203 is not a row, despite its 40 votes")
    check(205 not in listed, "C-2 issue 205 (cheat:invalid) is not a row")

    # C-3
    check(numbers(catalog, SHA_1942) == [202, 208, 201], f"C-3 most-👍-first, tie by issue number: {numbers(catalog, SHA_1942)}")

    # C-4
    shas = [g["sha1"] for g in catalog["games"]]
    check(shas == sorted(shas) and len(shas) == len(set(shas)) == 2, f"C-4 one game per SHA-1, in SHA-1 order: {shas}")

    # C-5
    check(206 not in listed, "C-5 issue 206 (a code that does not decode, label set by hand) is dropped")
    check(207 not in listed, "C-5 issue 207 (same decoded patch as the earlier 201) is dropped")
    check(201 in listed, "C-5 the earlier of two duplicates stays")

    # C-6
    empty = build([])
    check(empty == {"format": gen.FORMAT, "version": gen.VERSION, "repository": gen.REPO, "games": []},
          f"C-6 no live issue is a valid, empty catalog: {empty}")
    check(gen.render(catalog) == gen.render(build(issues())) and "generated" not in gen.render(catalog),
          "C-6 the rendered catalog is byte-stable and carries no date")
    check(gen.render(empty).endswith("\n") and json.loads(gen.render(empty)) == empty, "C-6 the render is JSON with a trailing newline")
    committed = json.loads((ROOT / "docs" / "community-cheats.json").read_text(encoding="utf-8"))
    check(set(committed) == set(empty) and committed["format"] == gen.FORMAT,
          "C-6 docs/community-cheats.json is a catalog of this format")

    # C-7
    with tempfile.TemporaryDirectory() as tmp:
        src, out = Path(tmp) / "issues.json", Path(tmp) / "community-cheats.json"
        src.write_text(json.dumps(issues()), encoding="utf-8")
        cmd = [sys.executable, str(SCRIPTS / "generate_community_cheat_catalog.py"), "--issues-file", str(src), "--output", str(out)]
        first = subprocess.run(cmd, capture_output=True, text=True)
        written = json.loads(out.read_text(encoding="utf-8")) if out.exists() else {}
        check(first.returncode == 0 and 202 in [c["issue"] for g in written.get("games", []) for c in g["cheats"]],
              f"C-7 the CLI writes the catalog (exit {first.returncode}) {first.stderr.strip()[:200]}")
        closed = copy.deepcopy(issues())
        for item in closed:
            if item["number"] == 202:
                item["state"] = "CLOSED"
        src.write_text(json.dumps(closed), encoding="utf-8")
        second = subprocess.run(cmd, capture_output=True, text=True)
        rewritten = json.loads(out.read_text(encoding="utf-8"))
        check(second.returncode == 0 and 202 not in [c["issue"] for g in rewritten["games"] for c in g["cheats"]],
              "C-7 after issue 202 closes, the next run drops its row")

    # C-8
    text = WORKFLOW.read_text(encoding="utf-8") if WORKFLOW.is_file() else ""
    check(bool(text), "C-8 .github/workflows/community-cheat-catalog.yml exists")
    for needle in ("types: [closed, reopened", "workflow_run:", "Cheat Submitted", "schedule:", "workflow_dispatch:",
                   "scripts/generate_community_cheat_catalog.py", "docs/community-cheats.json",
                   "chore/community-cheat-catalog", "PROJECT_PAT", "--auto", "concurrency:"):
        check(needle in text, f"C-8 the workflow contains {needle!r}")
    check(not re.search(r"git push[^\n]*\borigin\s+(HEAD:)?(refs/heads/)?main\b", text), "C-8 the workflow never pushes to main")
    check(not re.search(r"\$\{\{\s*github\.event\.(issue|comment)\.(title|body)", text),
          "C-8 no issue title/body is interpolated into the workflow")

    # C-9: a list that reaches gh's --limit may be truncated; never treat it as complete
    real_run = gen.subprocess.run
    for count, refuse in ((gen.FETCH_LIMIT, True), (gen.FETCH_LIMIT - 1, False)):
        gen.subprocess.run = lambda *a, n=count, **k: subprocess.CompletedProcess(a, 0, json.dumps([{}] * n), "")
        try:
            got = len(gen.fetch_issues())
            refused = False
        except SystemExit:
            refused = True
        finally:
            gen.subprocess.run = real_run
        check(refused == refuse, f"C-9 fetch_issues {'refuses' if refuse else 'accepts'} {count} issues (limit {gen.FETCH_LIMIT})")

    if FAILURES:
        print(f"\n{len(FAILURES)} check(s) failed")
        return 1
    print("\nall checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
