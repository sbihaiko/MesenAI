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
  AC-4 no attachment, an attachment the host answers 404/410 for, or one the
       download refuses (off the allow-list, over the cap) is
       `replay:invalid` with a reason, never a crash.
  AC-6 section 7 (R.2): an archive byte-identical to a row the committed
       catalog lists under another issue is `replay:invalid` as `duplicate`,
       and the comment names the earlier issue; the row's own issue is not its
       own duplicate (a re-validation passes).
  AC-6 issue #624: on NES the movie's `SHA1` is the whole-file hash (iNES
       header included), which never equals the catalog's No-Intro hash
       (ADR-0003/ADR-0039); the game resolves through the movie's
       `NoIntroSHA1`, and a movie without it falls back to the stem rather
       than guessing a game from a name.
  AC-7 issue #700: any other fetch failure (a reset, a timeout, a 5xx, a DNS
       blip) is no verdict: evaluate raises TransientFetchError, the CLI
       exits EXIT_TRANSIENT without printing a verdict, and
       replay-submitted.yml fails the step without withdrawing
       `replay:valid`, so a live row is never de-listed by the network.

Usage: python3 scripts/test_replay_submission.py
"""
from __future__ import annotations

import hashlib
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
        "GameSettings.txt": f"MesenVersion 2.1.0\nMovieFormatVersion 3\nGameFile Contra (USA).nes\nSHA1 {SHA1}\nemu.consoleType Nes\n".encode(),
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
        raise rs.AttachmentRefused("host not allow-listed")

    broken = run(b"", fetch=boom)
    if broken.verdict != "invalid" or "host not allow-listed" not in broken.comment:
        fail(f"AC-4 a refused download is invalid with its reason: {broken.verdict} {broken.comment!r}")
        return

    def gone(_url):
        raise rs.AttachmentGone("404")

    deleted = run(b"", fetch=gone)
    if deleted.verdict != "invalid" or "download-failed" not in deleted.comment:
        fail(f"AC-4 a deleted (404/410) attachment is invalid: {deleted.verdict} {deleted.comment!r}")
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
                       lambda _u: (_ for _ in ()).throw(rs.AttachmentRefused("bad\n- `forged`: x")))
    if "\n- `forged`" in boom.comment:
        fail(f"AC-5 a download error must not forge a bullet: {boom.comment!r}")
        return
    for bad in ("https://github.com/user-attachments/files/../../o/r/releases/download/v/x.zip",
                "https://github.com/user-attachments/files/%2e%2e/o/x.zip"):
        if rs.extract_attachment_url(body(bad)) is not None:
            fail(f"AC-5 a traversing attachment URL must not be accepted: {bad}")
            return
    ok("AC-5 title length, mention/ref defanging, control chars and traversing URLs are handled")


def check_duplicate_of_a_listed_row():
    import hashlib
    import json
    import tempfile
    data = archive()
    sha = hashlib.sha256(data).hexdigest()
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "community-replays.json"
        path.write_text(json.dumps({"format": "mesenai-community-replays", "games": [
            {"sha1": SHA1, "replays": [{"issue": 7, "sha256": sha.upper()}, {"issue": "x", "sha256": "bad"}]}]}))
        live = rs.load_live_replays(path)
    if live != [(7, sha)]:
        fail(f"AC-6 the live rows are (issue, lower-case sha256) of the committed catalog: {live}")
        return
    dup = rs.evaluate(body(), "[Replay] ", ["replay"], "bob", CATALOG, lambda _u: data, number=9, live=live)
    if dup.verdict != "invalid" or "`duplicate`" not in dup.comment or "#7" not in dup.comment:
        fail(f"AC-6 a copy of issue 7's archive is a duplicate naming #7: {dup.verdict} {dup.comment!r}")
        return
    itself = rs.evaluate(body(), "[Replay] ", ["replay"], "bob", CATALOG, lambda _u: data, number=7, live=live)
    if itself.verdict != "valid":
        fail(f"AC-6 re-validating the listed issue itself stays valid: {itself.comment!r}")
        return
    other = rs.evaluate(body(), "[Replay] ", ["replay"], "bob", CATALOG, lambda _u: archive(author="carol"), number=9, live=live)
    if other.verdict != "valid":
        fail(f"AC-6 a different recording of the same ROM is its own row: {other.comment!r}")
        return
    ok("AC-6 a byte-identical copy of a listed replay is a duplicate naming the earlier issue")


def _ines_rom():
    """A synthetic iNES file: 16-byte header, 16 KB PRG, 8 KB CHR."""
    header = b"NES\x1a" + bytes([1, 1, 0, 0]) + bytes(8)
    payload = bytes((i * 7 + 3) & 0xFF for i in range(0x4000 + 0x2000))
    return header + payload


def check_nes_whole_file_hash():
    rom = _ines_rom()
    whole = hashlib.sha1(rom).hexdigest().upper()
    no_intro = hashlib.sha1(rom[16:]).hexdigest().upper()  # ADR-0039: header skipped
    catalog = [{"game": "Castlevania (USA)", "rom": {"sha1": no_intro}}]
    settings = f"GameFile my dump.nes\nSHA1 {whole}\nNoIntroSHA1 {no_intro}\n".encode()
    movie = archive({"GameSettings.txt": settings})
    verdict = rs.evaluate(body(), "[Replay] typed", ["replay"], "octocat", catalog, lambda _u: movie)
    if not verdict.title.startswith("[Replay] Castlevania (USA) —"):
        fail(f"AC-6 an NES movie (whole-file SHA1) must resolve the catalog game by its No-Intro hash: {verdict.title!r}")
        return
    legacy = archive({"GameSettings.txt": f"GameFile my dump.nes\nSHA1 {whole}\n".encode()})
    old = rs.evaluate(body(), "[Replay] typed", ["replay"], "octocat", catalog, lambda _u: legacy)
    if not old.title.startswith("[Replay] my dump —"):
        fail(f"AC-6 a movie without NoIntroSHA1 falls back to the ROM file stem: {old.title!r}")
        return
    ok("AC-6 an NES movie resolves its game by NoIntroSHA1, not the whole-file SHA1 (issue #624)")


def check_transient_fetch_is_no_verdict():
    import contextlib
    import json
    import tempfile
    import urllib.error
    import yaml

    transient = (OSError("connection reset"), urllib.error.URLError("timed out"),
                 urllib.error.HTTPError(URL, 503, "Service Unavailable", {}, None),
                 ValueError("could not resolve host 'github.com'"))
    for exc in transient:
        def flaky(_url, exc=exc):
            raise exc
        try:
            v = run(b"", fetch=flaky)
            fail(f"AC-7 a transient fetch failure ({exc!r}) must not be a verdict: {v.verdict} {v.labels_add}")
            return
        except getattr(rs, "TransientFetchError", ()) as raised:
            if "replay:invalid" in str(raised):
                fail(f"AC-7 the transient error names no verdict: {raised}")
                return

    original = getattr(rs, "fetch_attachment", None)
    with tempfile.TemporaryDirectory() as tmp:
        b = Path(tmp) / "body.txt"
        b.write_text(body(), encoding="utf-8")
        rs.fetch_attachment = lambda _url: (_ for _ in ()).throw(OSError("connection reset"))
        out = io.StringIO()
        try:
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
                rc = rs.main(["replay_submission.py", "--body-file", str(b), "--title", "t", "--login", "u"])
        except Exception as exc:  # noqa: BLE001 - the RED state crashes here
            rc = f"raised {exc!r}"
        finally:
            rs.fetch_attachment = original
    if rc != getattr(rs, "EXIT_TRANSIENT", object()) or out.getvalue().strip():
        fail(f"AC-7 the CLI exits EXIT_TRANSIENT and prints no verdict on a transient error: rc={rc} out={out.getvalue()[:80]!r}")
        return

    wf = yaml.safe_load((SCRIPTS.parent / ".github" / "workflows" / "replay-submitted.yml").read_text(encoding="utf-8"))
    steps = wf["jobs"]["validate"]["steps"]
    verdict = next(st for st in steps if st.get("id") == "verdict")
    withdraw = next(st for st in steps if "Withdraw" in st.get("name", ""))
    if "EXIT_TRANSIENT" not in verdict["run"] and "75" not in verdict["run"]:
        fail("AC-7 the verdict step recognizes the transient exit code")
        return
    if "steps.verdict.outputs.transient" not in str(withdraw.get("if", "")):
        fail(f"AC-7 the withdraw step skips a transient fetch failure: if={withdraw.get('if')!r}")
        return
    ok("AC-7 a transient fetch failure is no verdict: the step fails and replay:valid stays (issue #700)")


def main():
    check_url_extraction()
    check_title()
    check_round_trip()
    check_failures_are_verdicts()
    check_hostile_text_and_urls()
    check_duplicate_of_a_listed_row()
    check_nes_whole_file_hash()
    check_transient_fetch_is_no_verdict()
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
