#!/usr/bin/env python3
"""Framework-free checks for scripts/generate_community_replay_catalog.py - the
shared-replay catalog of ADR-0205 section 7 that the client reads, gated by
section 8 before listing, and that drops a row when its issue closes or a
maintainer sets `replay:removed` (section 9).

Nothing here touches the network or GitHub. The issues are built here, shaped
as `gh issue list --json number,state,title,body,labels,reactionGroups,author`
prints them (the generator's own call), and each attachment is an in-memory
`.mmo` shaped as MovieRecorder writes it; the download is an injected function.

Checks:
  C-1 an open `replay:valid` issue is a row under its ROM SHA-1 (upper case):
      issue, the stable attachment URL, the sha256 and size of the bytes,
      console, author (MovieInfo.txt's, else the login), subtitle, frames,
      cheats[] (type and code verbatim) and 👍.
  C-2 a closed issue is not a row, whatever its votes; nor is an issue without
      `replay:valid`; `replay:removed` keeps a row out even while open
      (honoured before the state).
  C-3 rows are most-👍-first; a tie keeps the earlier issue first; ROMs are in
      SHA-1 order.
  C-4 the gate runs before listing: an attachment the lint/gate refuses today,
      a missing attachment link, a byte-identical copy of an earlier row, a
      deleted attachment (404/410) and one over the cap all stay out.
  C-5 any other download failure refuses to write the catalog (a network blip
      must never de-list live rows).
  C-6 no live issue gives a valid, empty catalog equal to the committed
      docs/community-replays.json; the output is byte-stable (no date).
  C-7 the CLI writes the file from --issues-file/--archives-dir, and closing
      an issue drops its row on the next run (the PRD's stop rule, offline).
  C-8 the workflow regenerates on a close/reopen/relabel, after every
      `Replay Submitted` run and daily, lands through a PROJECT_PAT PR, and
      never interpolates issue text into a `run:` script.
  C-9 fetch_attachment maps 404/410 to a stale row, an over-cap
      Content-Length to a refused row, and follows the user-attachments
      redirect only through the replay allow-list.
  C-10 an NES movie's row is named from the pack catalog by its
      `NoIntroSHA1`, as the issue title is (#697), not from the uploader's
      ROM file name.
"""
from __future__ import annotations

import copy
import hashlib
import io
import json
import subprocess
import sys
import tempfile
import urllib.error
import zipfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import generate_community_replay_catalog as gen  # noqa: E402

ROOT = SCRIPTS.parent
WORKFLOW = ROOT / ".github" / "workflows" / "community-replay-catalog.yml"
COMMITTED = ROOT / "docs" / "community-replays.json"
SHA_A = "A" * 40
SHA_B = "0123456789abcdef0123456789abcdef01234567"

FAILURES = []


def check(cond, msg):
    if cond:
        print(f"ok: {msg}")
    else:
        FAILURES.append(msg)
        print(f"FAIL: {msg}")


def mmo(sha1=SHA_A, author="alice", description="stage skip run\nmore", frames=120, cheats=(), console="Nes",
        extra=None, game_file="Contra (USA).nes", no_intro=None):
    settings = ["MesenVersion 2.1.0", "MovieFormatVersion 3", f"GameFile {game_file}", f"SHA1 {sha1}",
                f"emu.consoleType {console}", "nes.ramPowerOnState AllZeros"]
    if no_intro:
        settings.append(f"NoIntroSHA1 {no_intro}")
    settings += [f"Cheat {kind} {code}" for kind, code in cheats]
    members = {
        "GameSettings.txt": ("\n".join(settings) + "\n").encode(),
        "Input.txt": b"|........|........\n" * frames,
    }
    if author is not None:
        members["MovieInfo.txt"] = f"Author {author}\nDescription\n{description}".encode()
    members.update(extra or {})
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        for name, data in members.items():
            # A fixed timestamp: two archives built from the same members are
            # byte-identical, as two downloads of one attachment are.
            z.writestr(zipfile.ZipInfo(name, date_time=(2026, 10, 2, 0, 0, 0)), data, zipfile.ZIP_DEFLATED)
    return buf.getvalue()


def url_for(number):
    return f"https://github.com/user-attachments/files/{1000 + number}/run.mmo"


def issue(number, votes=1, state="OPEN", labels=("replay", "replay:valid"), login="submitter", link=True):
    body = "### Replay file\n\n" + (f"[run.mmo]({url_for(number)})" if link else "_No response_") + "\n"
    return {
        "number": number,
        "state": state,
        "title": "[Replay] x",
        "body": body,
        "labels": [{"name": name} for name in labels],
        "reactionGroups": [{"content": "THUMBS_UP", "users": {"totalCount": votes}}],
        "author": {"login": login},
    }


def fixture():
    """Issues and the attachment each one's URL serves."""
    archives = {
        1: mmo(author="alice", cheats=[("NesCustom", "0032:05")]),
        2: mmo(author="bob", frames=60),
        3: mmo(author="carol", frames=90),
        4: mmo(author="dave", frames=30),                              # closed
        5: mmo(author="erin", frames=31),                              # replay:removed
        6: mmo(author="frank", frames=32),                             # no replay:valid
        7: mmo(author="gina", extra={"SaveState.mss": b"x"}),           # the gate refuses it
        8: mmo(author="bob", frames=60),                               # copy of #2
        9: mmo(sha1=SHA_B, author=None, console="Gameboy", frames=10),  # another ROM, unnamed recorder
        10: b"",                                                       # deleted attachment
        11: b"",                                                       # over the cap
    }
    issues = [
        issue(1, votes=2), issue(2, votes=5), issue(3, votes=2),
        issue(4, votes=50, state="CLOSED"),
        issue(5, votes=40, labels=("replay", "replay:valid", "replay:removed")),
        issue(6, votes=30, labels=("replay", "replay:invalid")),
        issue(7, votes=20), issue(8, votes=9), issue(9, votes=1, login="octo"),
        issue(10), issue(11), issue(12, link=False),
    ]
    return issues, archives


def fetcher(archives, fail_on=None):
    def fetch(url):
        number = int(url.split("/")[-2]) - 1000
        if number == fail_on:
            raise OSError("connection reset")
        if number == 10:
            raise gen.AttachmentGone("404")
        if number == 11:
            raise gen.AttachmentRefused("over the cap")
        return archives[number]
    return fetch


def rows(catalog):
    return [(g["sha1"], r["issue"]) for g in catalog["games"] for r in g["replays"]]


def check_rows():
    issues, archives = fixture()
    catalog = gen.build_catalog(issues, fetcher(archives), [])
    check(catalog["format"] == "mesenai-community-replays" and catalog["version"] == 1
          and catalog["repository"] == "sbihaiko/MesenAI", "C-1 the catalog names its format, version and repository")
    one = next(r for g in catalog["games"] for r in g["replays"] if r["issue"] == 1)
    data = archives[1]
    check(one == {
        "issue": 1, "url": url_for(1), "sha256": hashlib.sha256(data).hexdigest(), "size": len(data),
        "console": "nes", "game": "Contra (USA)", "author": "alice", "subtitle": "stage skip run",
        "frames": 120, "cheats": [{"type": "NesCustom", "code": "0032:05"}], "votes": 2,
    }, f"C-1 a live issue is a full row: {one}")
    nine = next(r for g in catalog["games"] for r in g["replays"] if r["issue"] == 9)
    check(nine["author"] == "octo" and nine["console"] == "gb",
          f"C-1 an unnamed recorder renders as the login; a Game Boy movie is console gb: {nine}")
    listed = {n for _, n in rows(catalog)}
    check(not listed & {4, 6}, f"C-2 closed and non-valid issues are not rows: {sorted(listed)}")
    check(5 not in listed, "C-2 replay:removed keeps an open issue out (section 9)")
    reopened = copy.deepcopy(issues)
    reopened[3]["state"] = "OPEN"
    reopened[4]["state"] = "CLOSED"
    again = {n for _, n in rows(gen.build_catalog(reopened, fetcher(archives), []))}
    check(4 in again and 5 not in again, "C-2 reopening restores a row; replay:removed outranks any state")


def check_order():
    issues, archives = fixture()
    catalog = gen.build_catalog(issues, fetcher(archives), [])
    check([g["sha1"] for g in catalog["games"]] == sorted([SHA_A, SHA_B.upper()]),
          f"C-3 one game per ROM SHA-1, upper case, in SHA-1 order: {[g['sha1'] for g in catalog['games']]}")
    first = catalog["games"][[g["sha1"] for g in catalog["games"]].index(SHA_A)]
    check([r["issue"] for r in first["replays"]] == [2, 1, 3],
          f"C-3 most-👍-first, a tie keeps the earlier issue first: {[r['issue'] for r in first['replays']]}")


def check_gate():
    issues, archives = fixture()
    listed = {n for _, n in rows(gen.build_catalog(issues, fetcher(archives), []))}
    check(7 not in listed, "C-4 an attachment the section 3/8 gate refuses today is not listed")
    check(8 not in listed and 2 in listed, "C-4 a byte-identical later copy is dropped; the earlier issue keeps the row")
    check(10 not in listed and 11 not in listed, "C-4 a deleted (404/410) or over-cap attachment is not listed")
    check(12 not in listed, "C-4 an issue with no attachment link is not listed")
    check(listed == {1, 2, 3, 9}, f"C-4 exactly the live, valid, distinct rows remain: {sorted(listed)}")


def check_network_failure_refuses():
    issues, archives = fixture()
    try:
        gen.build_catalog(issues, fetcher(archives, fail_on=3), [])
    except OSError:
        check(True, "C-5 a network failure on one attachment stops the run instead of de-listing the row")
        return
    check(False, "C-5 a network failure on one attachment must stop the run")


def check_empty_and_stable():
    empty = gen.render(gen.build_catalog([], fetcher({}), []))
    check(json.loads(empty) == {"format": "mesenai-community-replays", "version": 1,
                                "repository": "sbihaiko/MesenAI", "games": []}, "C-6 no live issue is an empty catalog")
    check(COMMITTED.read_text(encoding="utf-8") == empty, "C-6 the committed docs/community-replays.json is that empty catalog")
    issues, archives = fixture()
    a = gen.render(gen.build_catalog(issues, fetcher(archives), []))
    b = gen.render(gen.build_catalog(list(reversed(issues)), fetcher(archives), []))
    check(a == b and "date" not in a, "C-6 the output is byte-stable whatever the issue order, with no date")


def check_cli_and_close():
    issues, archives = fixture()
    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        for number, data in archives.items():
            if data:
                (tmp / f"{number}.mmo").write_bytes(data)
        live = [i for i in issues if i["number"] not in (11,)]
        (tmp / "issues.json").write_text(json.dumps(live), encoding="utf-8")
        out = tmp / "out.json"
        cmd = [sys.executable, str(SCRIPTS / "generate_community_replay_catalog.py"), "--issues-file",
               str(tmp / "issues.json"), "--archives-dir", str(tmp), "--output", str(out)]
        proc = subprocess.run(cmd, capture_output=True, text=True)
        before = {n for _, n in rows(json.loads(out.read_text()))} if proc.returncode == 0 else set()
        check(proc.returncode == 0 and before == {1, 2, 3, 9},
              f"C-7 the CLI writes the catalog offline: rc={proc.returncode} {sorted(before)} {proc.stderr[-300:]}")
        for i in live:
            if i["number"] == 2:
                i["state"] = "CLOSED"
        (tmp / "issues.json").write_text(json.dumps(live), encoding="utf-8")
        proc = subprocess.run(cmd, capture_output=True, text=True)
        after = {n for _, n in rows(json.loads(out.read_text()))}
        check(proc.returncode == 0 and after == {1, 3, 8, 9},
              f"C-7 closing #2 drops it on the next run, and its copy #8 becomes the one row: {sorted(after)}")


def check_workflow():
    import yaml
    text = WORKFLOW.read_text(encoding="utf-8")
    data = yaml.safe_load(text)
    triggers = data.get(True) or data.get("on")
    check(set(triggers["issues"]["types"]) == {"closed", "reopened", "labeled", "unlabeled"},
          "C-8 a close/reopen/relabel of an issue regenerates")
    check(triggers.get("workflow_run", {}).get("workflows") == ["Replay Submitted"],
          "C-8 every completed Replay Submitted run regenerates (its GITHUB_TOKEN labels start no workflow)")
    check("schedule" in triggers and "workflow_dispatch" in triggers, "C-8 daily and by hand")
    check("scripts/generate_community_replay_catalog.py" in text and "docs/community-replays.json" in text,
          "C-8 the workflow runs the generator over the committed file")
    check('GH_TOKEN="$PROJECT_PAT" gh pr create' in text and "chore/community-replay-catalog" in text,
          "C-8 it lands through a PR opened with PROJECT_PAT on its own branch")
    check(data.get("permissions") == {"contents": "write", "pull-requests": "write"},
          f"C-8 least privilege: contents + pull-requests write, nothing else: {data.get('permissions')}")
    runs = [step.get("run") or "" for job in data["jobs"].values() for step in job.get("steps", [])]
    check(not any("github.event.issue" in r or "github.event.comment" in r for r in runs),
          "C-8 no issue text is interpolated into a run: script")


class _Resp:
    def __init__(self, body, length=None):
        self._body = body
        self.headers = {} if length is None else {"Content-Length": str(length)}

    def read(self, n=-1):
        return self._body if n < 0 else self._body[:n]


def check_fetch_attachment():
    original = gen.fetch_pack.open_validated
    try:
        def gone(url, hosts, opener=None):
            raise urllib.error.HTTPError(url, 404, "Not Found", {}, None)
        gen.fetch_pack.open_validated = gone
        try:
            gen.fetch_attachment(url_for(1))
            check(False, "C-9 a 404 must be a stale row")
        except gen.AttachmentGone:
            check(True, "C-9 a 404 is a stale (deleted) attachment")
        gen.fetch_pack.open_validated = lambda url, hosts, opener=None: (_Resp(b"", length=9 * 1024 * 1024), {})
        try:
            gen.fetch_attachment(url_for(1))
            check(False, "C-9 an over-cap Content-Length must refuse the row")
        except gen.AttachmentRefused:
            check(True, "C-9 an over-cap Content-Length refuses the row before reading it")
        gen.fetch_pack.open_validated = lambda url, hosts, opener=None: (_Resp(b"PK\x03\x04data"), {})
        check(gen.fetch_attachment(url_for(1)) == b"PK\x03\x04data", "C-9 a served attachment's bytes are returned")
        try:
            gen.fetch_attachment("https://example.com/user-attachments/files/1/a.mmo")
            check(False, "C-9 a host off the replay allow-list must refuse the row")
        except gen.AttachmentRefused:
            check(True, "C-9 a host off the replay allow-list refuses the row")
    finally:
        gen.fetch_pack.open_validated = original
    hosts = gen.fetch_pack.load_allowlist(gen.rs.ALLOWLIST)
    check(gen.fetch_pack.match_host("https://objects.githubusercontent.com/github-production-repository-file/x", hosts)
          is not None, "C-9 the signed redirect target is allow-listed for the hop, never stored")


def check_no_intro_name():
    no_intro = "B" * 40
    archives = {1: mmo(game_file="my rom (hack).nes", no_intro=no_intro)}
    packs = [{"game": "Castlevania (USA)", "rom": {"sha1": no_intro}}]
    catalog = gen.build_catalog([issue(1)], fetcher(archives), packs)
    names = [r["game"] for g in catalog["games"] for r in g["replays"]]
    check(names == ["Castlevania (USA)"], f"C-10 the row is named by NoIntroSHA1 from the pack catalog: {names}")


def main():
    check_rows()
    check_order()
    check_gate()
    check_network_failure_refuses()
    check_empty_and_stable()
    check_cli_and_close()
    check_workflow()
    check_fetch_attachment()
    check_no_intro_name()
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
