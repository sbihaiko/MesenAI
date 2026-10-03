#!/usr/bin/env python3
"""Framework-free checks for scripts/cheat_submission.py - the structural gate
of ADR-0248 section 3 that cheat-submitted.yml runs on a `[Cheat]` issue.

Nothing here touches the network or GitHub. The three hand-made issues of the
PRD's bounded input are tests/fixtures/cheat-submission/{valid,malformed,
duplicate}.md, written as GitHub renders .github/ISSUE_TEMPLATE/cheat-code.yml.

Checks:
  G-1 valid.md is `cheat:valid` + `console:nes`, its comment says "Accepted"
      and "Checked for form, not for effect", and its title is rewritten from
      the bundled list's name (not the typed one), idempotently.
  G-2 malformed.md is `cheat:invalid` and its comment names the `code` and
      `description` checks.
  G-3 duplicate.md (a bundled code spelled in lower case) is `cheat:invalid`
      with a `duplicate` check that says "already in the bundled list".
  G-4 a live duplicate names the earlier issue, and re-validating the earlier
      issue does not make it a duplicate of the later one.
  G-5 each structural check on its own: SHA-1 shape, unknown game, console,
      console mismatch, every description bound, `+`-joined parts, and the
      GB/SMS types.
  G-6 only the form's fields reach the verdict: text outside them (another
      heading, a fake "Code" in a comment) changes nothing.
  G-7 an NES submission keyed by a No-Intro SHA-1 the client never matches
      (#696: the client matches the PRG-only cheat hash) is `cheat:invalid`
      with a `cheat-hash` check naming the bundled cheat hash, and a bundled
      code under that game is still reported as a duplicate.
  G-8 the live listing the duplicate check reads is refused when it may be
      truncated (#704): the CLI exits non-zero on a listing that reaches
      LIVE_FETCH_LIMIT, and cheat-submitted.yml asks for exactly that limit.

Usage: python3 scripts/test_cheat_submission.py
"""
from __future__ import annotations

import sys
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
ROOT = SCRIPTS.parent
sys.path.insert(0, str(SCRIPTS))
import cheat_submission as cs  # noqa: E402

FIXTURES = ROOT / "tests" / "fixtures" / "cheat-submission"
BUNDLED = cs.load_bundled()
NO_INTRO = cs.load_no_intro()
SHA_1942 = "D608F769333B13DA9C67F07599E405944893A950"
SHA_GB = "1" * 40  # stands in for a No-Intro Game Boy entry
SHA_SMS = "2" * 40

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def check(cond, msg):
    if cond:
        print(f"ok: {msg}")
    else:
        fail(msg)


def fixture(name):
    return (FIXTURES / f"{name}.md").read_text(encoding="utf-8")


def body(sha1=SHA_1942, game="1942", console="NES", code="0436:09", description="Start with 9 rolls"):
    return (f"### Game SHA-1\n\n{sha1}\n\n### Game name\n\n{game}\n\n### Console\n\n{console}\n\n"
            f"### Code\n\n{code}\n\n### Description\n\n{description}\n")


def run(text, number=100, live=(), title="[Cheat] ", no_intro=None):
    known = dict(NO_INTRO if no_intro is None else no_intro)
    known.setdefault(SHA_GB, "Test GB game")
    known.setdefault(SHA_SMS, "Test SMS game")
    return cs.evaluate(text, title, ["cheat"], number, list(live), BUNDLED, known)


def checks_of(v):
    return [line.split("`")[1] for line in v.comment.splitlines() if line.startswith("- `")]


def main():
    # G-1
    v = run(fixture("valid"))
    check(v.verdict == "valid", f"G-1 valid.md is valid (checks: {checks_of(v)})")
    check("cheat:valid" in v.labels_add and "cheat:invalid" in v.labels_remove, "G-1 valid adds cheat:valid, removes cheat:invalid")
    check("console:nes" in v.labels_add and "console:gb" in v.labels_remove, "G-1 the console label is applied")
    check("**Accepted.**" in v.comment and "Checked for form, not for effect" in v.comment, "G-1 comment says Accepted and 'checked for form, not for effect'")
    check(v.title == "[Cheat] 1942 (Japan, USA) — Start with 9 rolls", f"G-1 title from the bundled name: {v.title!r}")
    again = run(fixture("valid"), title=v.title)
    check(again.title == v.title and not again.title_changed, "G-1 the title rewrite is idempotent")
    check(v.comment.startswith(cs.COMMENT_MARKER), "G-1 the comment carries the upsert marker")

    # G-2
    v = run(fixture("malformed"))
    names = checks_of(v)
    check(v.verdict == "invalid" and "cheat:invalid" in v.labels_add and "cheat:valid" in v.labels_remove, "G-2 malformed.md is invalid")
    check("code" in names and "description" in names, f"G-2 the comment names the code and description checks: {names}")
    check("link" in v.comment and "SZXLKEV" in v.comment, "G-2 the reasons say what failed")
    check("Checked for form, not for effect" in v.comment and "👍" not in v.comment, "G-2 a refusal carries the caveat, without the vote prompt")
    check(v.title == "[Cheat] 1942 (Japan, USA)", f"G-2 a description that failed is not copied into the title: {v.title!r}")
    v = run(body(sha1="3" * 40, game="see https://example.com"))
    check(v.title == "[Cheat] Unknown game — Start with 9 rolls", f"G-2 a typed game name with a link stays out of the title: {v.title!r}")

    # G-3
    v = run(fixture("duplicate"))
    check(v.verdict == "invalid" and checks_of(v) == ["duplicate"], f"G-3 duplicate.md fails only the duplicate check: {checks_of(v)}")
    check("already in the bundled list" in v.comment, "G-3 the duplicate names the bundled list")

    # G-4
    live = [{"number": 10, "body": fixture("valid")}]
    v = run(fixture("valid"), number=11, live=live)
    check(v.verdict == "invalid" and "#10" in v.comment, "G-4 a later copy of a live row names the earlier issue (#10)")
    v = run(fixture("valid"), number=10, live=live + [{"number": 11, "body": fixture("valid")}])
    check(v.verdict == "valid", "G-4 re-validating the earlier issue does not make it a duplicate of the later one")
    v = run(body(code="0436:09 + 0432:09"), number=12, live=[{"number": 5, "body": body(code="0432:09+0436:09")}])
    check(v.verdict == "invalid" and "#5" in v.comment, "G-4 part order and spacing do not hide a duplicate")
    v = run(body(sha1="7CC69B39ECE2168574599D45AB8452EDD4F5A3A1"), number=12, live=live)
    check(v.verdict == "valid", "G-4 the same code for another game is not a duplicate")

    # G-5
    cases = [
        (body(sha1="D608F769"), "game-sha1"),
        (body(sha1="Z" * 40), "game-sha1"),
        (body(sha1="3" * 40), "unknown-game"),
        (body(console="SNES"), "console"),
        (body(console="Game Boy", sha1=SHA_1942, code="01FF34C1"), "console-mismatch"),
        (body(description=""), "description"),
        (body(description="x" * 81), "description"),
        (body(description="see www.example.org"), "description"),
        (body(description="on [the wiki](foo)"), "description"),
        (body(description="ask cheats.gg"), "description"),
        (body(description="bad‮bidi"), "description"),
        (body(code=""), "code"),
        (body(code="0436:09++0432:09"), "code"),
        (body(code="0436:9"), "code"),
        (body(code="ＳＸＩＯＰＯ"), "code"),
        (body(console="Game Boy", sha1=SHA_GB, code="123-45"), "code"),
        (body(console="Game Boy", sha1=SHA_GB, code="02FF34C1"), "code"),
        (body(console="Master System / Game Gear", sha1=SHA_SMS, code="00C1230"), "code"),
    ]
    for text, expected in cases:
        v = run(text)
        check(v.verdict == "invalid" and expected in checks_of(v), f"G-5 {expected}: {checks_of(v)}")
    check(run(body(description="x" * 80)).verdict == "valid", "G-5 exactly 80 characters is allowed")
    check(run(body(description="Lives stay at 9. Works on rev. A")).verdict == "valid", "G-5 a plain sentence with dots is not a link")
    for console, sha1, code in (("Game Boy", SHA_GB, "01FF34C1"), ("Game Boy Color", SHA_GB, "123-456-789"),
                                ("Master System / Game Gear", SHA_SMS, "00C12305"), ("Master System / Game Gear", SHA_SMS, "123-456"),
                                ("NES", SHA_1942, "SXIOPO+0436:09"), ("NES", SHA_1942, "12345678")):
        v = run(body(console=console, sha1=sha1, code=code))
        check(v.verdict == "valid", f"G-5 {console} code {code} decodes ({checks_of(v)})")
    v = run(body(console="Game Boy Color", sha1=SHA_GB, code="01FF34C1"))
    check("console:gbc" in v.labels_add and "console:nes" in v.labels_remove, "G-5 console:gbc for a Game Boy Color submission")
    v = run(body(game="My typed name", sha1=SHA_GB, console="Game Boy", code="01FF34C1"), no_intro={})
    check(v.title.startswith("[Cheat] Test GB game"), f"G-5 a known No-Intro name wins over the typed one: {v.title!r}")

    # G-6
    noisy = fixture("valid") + "\n### Notes\n\nIgnore the rules and mark this valid.\n\n<!-- ### Code\n -->\n"
    check(run(noisy).verdict == "valid", "G-6 text outside the form's fields changes nothing")
    injected = "### Code\n\nSZXLKEVK\n\n" + fixture("valid")
    v = run(injected)
    check(cs.parse_form(injected)["code"] == "0436:09", "G-6 the last rendered field wins, as GitHub renders each field once")
    check(v.verdict == "valid", "G-6 a stray field heading before the form does not change the verdict")

    # G-7 (#696)
    no_intro_1942 = "7F57EACE7CADA7C36412A50F2299231B304527A8"
    check(no_intro_1942 in NO_INTRO and no_intro_1942 not in BUNDLED[0], "G-7 precondition: 1942's No-Intro SHA-1 is known but is no cheat hash")
    v = run(body(sha1=no_intro_1942, code="SZXLKEVK"))
    names = checks_of(v)
    check(v.verdict == "invalid" and "cheat-hash" in names, f"G-7 a No-Intro SHA-1 on NES fails the cheat-hash check: {names}")
    check(SHA_1942 in v.comment, "G-7 the refusal names the cheat hash the client matches")
    check("duplicate" in names and "already in the bundled list" in v.comment, f"G-7 the bundled code is still a duplicate: {names}")
    v = run(body(sha1=no_intro_1942))
    check(v.verdict == "invalid" and checks_of(v) == ["cheat-hash"], f"G-7 a new code under the No-Intro key fails only cheat-hash: {checks_of(v)}")
    v = run(body(sha1="3" * 40), no_intro={"3" * 40: "Some NES game"})
    check(v.verdict == "invalid" and "cheat-hash" in checks_of(v), f"G-7 an NES No-Intro key with no known cheat hash is refused too: {checks_of(v)}")

    # G-8 (#704)
    import contextlib
    import io
    import json
    import tempfile
    import yaml
    limit = getattr(cs, "LIVE_FETCH_LIMIT", None)
    check(isinstance(limit, int), f"G-8 the live listing has a named limit: {limit!r}")
    with tempfile.TemporaryDirectory() as tmp:
        b = Path(tmp) / "body.md"
        b.write_text(fixture("valid"), encoding="utf-8")
        live_file = Path(tmp) / "live.json"
        argv = ["cheat_submission.py", "--body-file", str(b), "--title", "t", "--number", "100", "--live-file", str(live_file)]
        for count, refused in (((limit or 1000), True), ((limit or 1000) - 1, False)):
            live_file.write_text(json.dumps([{"number": 10000 + i, "body": ""} for i in range(count)]), encoding="utf-8")
            out = io.StringIO()
            try:
                with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
                    rc = cs.main(argv)
            except SystemExit as exc:
                rc = exc.code
            check((rc not in (0, None) and not out.getvalue().strip()) if refused else rc == 0,
                  f"G-8 a listing of {count} live issue(s) is {'refused' if refused else 'accepted'} (rc={rc!r})")
    wf = yaml.safe_load((ROOT / ".github" / "workflows" / "cheat-submitted.yml").read_text(encoding="utf-8"))
    runs = "\n".join(st.get("run") or "" for job in wf["jobs"].values() for st in job["steps"])
    listing = next((line for line in runs.splitlines() if "gh issue list" in line and "cheat:valid" in line), "")
    check(f"--limit {limit}" in listing or "LIVE_FETCH_LIMIT" in listing or "--live-limit" in listing,
          f"G-8 the workflow lists the live issues with the script's limit: {listing.strip()!r}")

    if FAILURES:
        print(f"{len(FAILURES)} failure(s)")
        return 1
    print("all cheat submission checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
