#!/usr/bin/env python3
"""Framework-free checks for scripts/replay_submission.py -- the issue-level
half of ADR-0205 sections 5 and 6: reading the attachment off the Issue Form
body, the whole-title rewrite, and the verdict-to-label round trip the
`replay-submitted` workflow applies.

Nothing here touches the network or GitHub: the download is an injected
function and the "issue" is the body text GitHub renders from
.github/ISSUE_TEMPLATE/replay.yml.

Checks:
  AC-1 the attachment URL is the first github.com/user-attachments link in the
       body, for both the `files` and `assets` shapes; any other link (or none)
       is not an attachment.
  AC-2 the title is `[Replay] <game> -- <alias> -- <subtitle>`, rewritten whole
       from the artifact: game from the ROM SHA-1 via the catalog (file-name
       stem when unknown), alias from MovieInfo.txt (the GitHub login only when
       the recorder was left unnamed), subtitle the first Description line
       truncated; and the rewrite is idempotent.
  AC-3 round trip: an action-shaped archive attached to an issue becomes
       label `replay:valid`; a stock-settings archive becomes `replay:invalid`
       with the section 3 reason in the comment; the opposite verdict label is
       removed on a re-validation, and the form's `replay` label is never
       touched.
  AC-4 no attachment / a download that fails is `replay:invalid` with a reason,
       never a crash.

Usage: python3 scripts/test_replay_submission.py
"""
from __future__ import annotations

import io
import sys
import zipfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import replay_submission as rs  # noqa: E402

FAILURES = []
SHA1 = "0123456789ABCDEF0123456789ABCDEF01234567"
CATALOG = [{"game": "Contra (USA)", "rom": {"sha1": SHA1, "sha1s": ["AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"]}}]
URL = "https://github.com/user-attachments/files/123456/contra.zip"


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"ok: {msg}")


def archive(extra=None, author="alice", description="stage skip run\nlong notes"):
    members = {
        "Input.txt": b"|........\n" * 100,
        "GameSettings.txt": f"GameFile Contra (USA).nes\nSHA1 {SHA1}\n".encode(),
    }
    if author is not None:
        members["MovieInfo.txt"] = f"Author {author}\nDescription\n{description}".encode()
    members.update(extra or {})
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        for name, data in members.items():
            z.writestr(name, data)
    return buf.getvalue()


def body(url=URL):
    return (
        "### Replay file\n\n"
        f"[contra.zip]({url})\n\n"
        "### Notes\n\n_No response_\n"
    )


def check_url_extraction():
    for url in (URL, "https://github.com/user-attachments/assets/aaaa-bbbb-cccc"):
        if rs.extract_attachment_url(body(url)) != url:
            fail(f"AC-1 attachment URL not extracted: {url}")
            return
    if rs.extract_attachment_url(body("https://example.com/x.zip")) is not None:
        fail("AC-1 a non-attachment link must not be taken as the replay")
        return
    if rs.extract_attachment_url("### Replay file\n\n_No response_\n") is not None:
        fail("AC-1 a body with no link has no attachment")
        return
    first = "[a](https://example.com/a) [b](https://github.com/user-attachments/files/1/b.zip) [c](https://github.com/user-attachments/files/2/c.zip)"
    if rs.extract_attachment_url(first) != "https://github.com/user-attachments/files/1/b.zip":
        fail("AC-1 the first attachment link wins")
        return
    ok("AC-1 the attachment is the first github.com/user-attachments link (files or assets)")


def check_title():
    facts = {"sha1": SHA1, "game_file": "Contra (USA).nes", "author": "alice", "description": "stage skip run\nlong notes"}
    title = rs.build_title(facts, "octocat", CATALOG)
    if title != "[Replay] Contra (USA) — alice — stage skip run":
        fail(f"AC-2 title shape: {title!r}")
        return
    unnamed = rs.build_title(dict(facts, author=""), "octocat", CATALOG)
    if "— octocat —" not in unnamed:
        fail(f"AC-2 an unnamed recorder renders as the login: {unnamed!r}")
        return
    unknown = rs.build_title(dict(facts, sha1="B" * 40), "octocat", CATALOG)
    if not unknown.startswith("[Replay] Contra (USA) —"):
        fail(f"AC-2 an unknown hash falls back to the ROM file-name stem: {unknown!r}")
        return
    long_desc = rs.build_title(dict(facts, description="x" * 500), "octocat", CATALOG)
    subtitle = long_desc.split(" — ")[-1]
    if len(subtitle) > rs.SUBTITLE_MAX or not subtitle.endswith("…"):
        fail(f"AC-2 the subtitle is truncated to a fixed length: {len(subtitle)} {subtitle[-5:]!r}")
        return
    nodesc = rs.build_title(dict(facts, description=""), "octocat", CATALOG)
    if nodesc != "[Replay] Contra (USA) — alice":
        fail(f"AC-2 no description, no subtitle part: {nodesc!r}")
        return
    if rs.build_title(facts, "octocat", CATALOG) != title:
        fail("AC-2 the rewrite is not idempotent")
        return
    ok("AC-2 title is rewritten whole from the artifact (game from hash, alias, truncated subtitle), idempotently")


def run(data, labels=("replay",), title="[Replay] typed by the submitter", login="octocat", url=URL, fetch=None):
    def default_fetch(u):
        return data

    return rs.evaluate(
        issue_body=body(url) if url else "### Replay file\n\n_No response_\n",
        issue_title=title,
        issue_labels=list(labels),
        login=login,
        catalog=CATALOG,
        fetch=fetch or default_fetch,
    )


def check_round_trip():
    good = run(archive())
    if good.verdict != "valid" or good.labels_add != [rs.LABEL_VALID] or good.labels_remove != [rs.LABEL_INVALID]:
        fail(f"AC-3 a clean archive must label {rs.LABEL_VALID}: {good.verdict} +{good.labels_add} -{good.labels_remove}")
        return
    if good.title != "[Replay] Contra (USA) — alice — stage skip run":
        fail(f"AC-3 the submitter's typed title must be replaced: {good.title!r}")
        return
    if rs.LABEL_SUBMITTED in good.labels_add + good.labels_remove:
        fail("AC-3 the form's own label is never touched")
        return

    bad = run(archive({"SaveState.mss": b"\x00" * 64}))
    if bad.verdict != "invalid" or bad.labels_add != [rs.LABEL_INVALID] or bad.labels_remove != [rs.LABEL_VALID]:
        fail(f"AC-3 a save-state archive must label {rs.LABEL_INVALID}: {bad.verdict} +{bad.labels_add} -{bad.labels_remove}")
        return
    if "SaveState.mss" not in bad.comment or "ADR-0205" not in bad.comment:
        fail(f"AC-3 the comment must name the section 3 reason: {bad.comment!r}")
        return
    if not good.comment.startswith(rs.COMMENT_MARKER) or not bad.comment.startswith(rs.COMMENT_MARKER):
        fail("AC-3 the verdict comment carries the marker the workflow upserts on")
        return

    # re-validation after the author re-attached a clean file: invalid -> valid
    again = run(archive(), labels=("replay", rs.LABEL_INVALID), title=good.title)
    if again.labels_add != [rs.LABEL_VALID] or again.labels_remove != [rs.LABEL_INVALID] or again.title_changed:
        fail(f"AC-3 re-validation flips the verdict label and leaves a matching title alone: {again}")
        return
    ok("AC-3 issue body -> verdict -> label round trip (valid / invalid with the section 3 reason / re-validation)")


def check_failures_are_verdicts():
    none = run(b"", url=None)
    if none.verdict != "invalid" or none.labels_add != [rs.LABEL_INVALID] or "attach" not in none.comment.lower():
        fail(f"AC-4 no attachment is invalid with a reason: {none.verdict} {none.comment!r}")
        return

    def boom(_url):
        raise OSError("host not allow-listed")

    broken = run(b"", fetch=boom)
    if broken.verdict != "invalid" or "host not allow-listed" not in broken.comment:
        fail(f"AC-4 a failed download is invalid with its reason: {broken.verdict} {broken.comment!r}")
        return
    if none.title_changed or broken.title_changed:
        fail("AC-4 with no readable artifact the typed title is left alone")
        return
    ok("AC-4 a missing attachment and a failed download are verdicts, not crashes")


def check_hostile_text_and_urls():
    # AC-5: submitter-controlled text reaches a title and a comment.
    long_alias = "x" * 240
    big = run(archive(author=long_alias, description="d" * 200))
    if len(big.title) > 256:
        fail(f"AC-5 the title must fit GitHub's 256 characters: {len(big.title)}")
        return
    ping = run(archive(author="`@octocat @org/team #123"))
    if "@octocat" in ping.comment.replace("@\u200b", "") and "@octocat" in ping.comment:
        fail(f"AC-5 the comment must not ping a user: {ping.comment!r}")
        return
    if "`@" in ping.comment or "#123" in ping.comment or "@org/team" in ping.comment:
        fail(f"AC-5 backticks, @mentions and #refs must be defanged in the comment: {ping.comment!r}")
        return
    ctrl = run(archive(author="bo\u202eb\x07ob"))
    if any(c in ctrl.title for c in ("\u202e", "\x07")):
        fail(f"AC-5 control and bidi characters must be stripped from the title: {ctrl.title!r}")
        return
    nl = rs._comment(False, [("battery", "names Battery\n- `forged`: ok\u202e\x1b[0m\u200b")], None, "t")
    if any(c in nl for c in ("\u202e", "\x1b", "\u200b")) or "\n- `forged`" in nl:
        fail(f"AC-5 a finding message must not forge a bullet or carry control characters: {nl!r}")
        return
    boom = rs.evaluate(body("https://github.com/user-attachments/files/1/a.mmo"), "t", (), "u", [],
                       lambda _u: (_ for _ in ()).throw(OSError("bad\n- `forged`: x")))
    if "\n- `forged`" in boom.comment:
        fail(f"AC-5 a download error must not forge a bullet: {boom.comment!r}")
        return
    for bad in ("https://github.com/user-attachments/files/../../o/r/releases/download/v/x.zip",
                "https://github.com/user-attachments/files/%2e%2e/o/x.zip"):
        if rs.extract_attachment_url(body(bad)) is not None:
            fail(f"AC-5 a traversing attachment URL must not be accepted: {bad}")
            return
    ok("AC-5 title length, mention/ref defanging, control chars and traversing URLs are handled")


def main():
    check_url_extraction()
    check_title()
    check_round_trip()
    check_failures_are_verdicts()
    check_hostile_text_and_urls()
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
