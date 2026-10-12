#!/usr/bin/env python3
"""ADR-0204 §6: behavior of the three scripts behind the `ci-latest` channel.

- scripts/stage_ci_channel_assets.sh  - artifact dirs -> the six fixed asset
  names, all or nothing;
- scripts/publish_ci_channel.sh       - create the pre-release if missing, then
  upload with --clobber, refusing anything that is not a channel asset;
- scripts/ci_channel_find_build.sh    - the decision ci-channel-publish.yml
  makes on a merge into `prod`: republish a pull request build only when the
  tree it recorded equals the merge commit's tree. Its exit code separates the
  three outcomes: 0 = a qualifying run was found, 1 = the lookup ran to
  completion and none qualifies, 2 = bad usage, 3 = no verdict was reached
  because a read was refused or otherwise failed.

`gh` is replaced by a fake on PATH that serves canned API answers, records
every call and applies `--jq` programs with the real `jq`, so the filters in
the scripts are exercised as written. No network, no token. The fake can also
refuse a chosen call the way GitHub reports a rate or abuse limit - HTTP 403,
the API's message on stderr, the request id and the timestamp in the response
headers (`--include` puts those on stdout). The find-build cases skip (and say
so) when `jq` is not installed.

Run: python3 scripts/test_ci_channel_scripts.py
"""
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
STAGE = REPO_ROOT / "scripts" / "stage_ci_channel_assets.sh"
PUBLISH = REPO_ROOT / "scripts" / "publish_ci_channel.sh"
FIND = REPO_ROOT / "scripts" / "ci_channel_find_build.sh"

ASSETS = {
    "MesenAI-ci-linux-x64.zip",
    "MesenAI-ci-linux-arm64.zip",
    "MesenAI-ci-windows-x64-aot.zip",
    "MesenAI-ci-linux-x64.AppImage",
    "MesenAI-ci-linux-arm64.AppImage",
    "MesenAI-ci-macos-arm64.zip",
}

# artifact dir -> file inside it (what build.yml's four build jobs upload).
ARTIFACTS = {
    "Mesen (Linux - ubuntu-22.04 - clang_aot)": "Mesen",
    "Mesen (Linux - ubuntu-22.04-arm - clang_aot)": "Mesen",
    "Mesen (Windows - net10.0 - AoT)": "Mesen.exe",
    "Mesen (Linux x64 - AppImage)": "Mesen.AppImage",
    "Mesen (Linux ARM64 - AppImage)": "Mesen.AppImage",
    "Mesen (macOS - macos-15 - clang_aot)": "Mesen.app.zip",
}

FAKE_GH = r'''#!/usr/bin/env python3
import json, os, subprocess, sys
state = json.load(open(os.environ["FAKE_GH_STATE"]))
with open(os.environ["FAKE_GH_LOG"], "a") as log:
    log.write(json.dumps(sys.argv[1:]) + "\n")
args = sys.argv[1:]

def emit(doc, jq):
    if jq is None:
        print(json.dumps(doc))
        return 0
    return subprocess.run(["jq", "-r", jq], input=json.dumps(doc), text=True).returncode

def opt(name):
    return args[args.index(name) + 1] if name in args else None

def refusal():
    r = state.get("refusal", {})
    return (r.get("message", "refused"),
            r.get("request_id", "0000:0000:0000:0000:00000000"),
            r.get("date", "Thu, 01 Jan 1970 00:00:00 GMT"))

def headers(status):
    # gh api --include writes the status line and the response headers to
    # stdout, then a blank line, then the body; --jq still filters the body.
    if "--include" not in args:
        return
    _, rid, date = refusal()
    sys.stdout.write("HTTP/2.0 %s\r\nDate: %s\r\nX-Github-Request-Id: %s\r\n\r\n" % (status, date, rid))
    # Real gh streams the headers before it reads the body; this process's
    # stdout is block-buffered under a pipe, and jq writes to the same fd
    # directly, so without the flush the body would land first.
    sys.stdout.flush()

def refuse(target):
    # GitHub reports a rate limit, and the abuse/secondary ("scraping") limit,
    # as HTTP 403. gh exits non-zero with the API's message on stderr and
    # nothing usable on stdout; the request id and the timestamp it returns
    # live in the response headers.
    for pattern in state.get("refuse", []):
        if pattern in target:
            message, rid, date = refusal()
            headers("403 Forbidden")
            sys.stderr.write("gh: %s (HTTP 403)\n" % message)
            sys.exit(1)

if args[:1] == ["api"]:
    path = args[1]
    jq = opt("--jq")
    refuse(path)
    if "/git/commits/" in path:
        sha = path.rsplit("/", 1)[1]
        if sha not in state["commits"]:
            sys.exit(1)
        headers("200 OK")
        sys.exit(emit({"sha": sha, "tree": {"sha": state["commits"][sha]}}, jq))
    if "/actions/workflows/build.yml/runs" in path:
        headers("200 OK")
        sys.exit(emit({"workflow_runs": state["runs"]}, jq))
    sys.exit(1)
if args[:2] == ["run", "download"]:
    run_id = args[2]
    if run_id in state.get("refuse_download", []):
        message, rid, date = refusal()
        sys.stderr.write("gh: %s (HTTP 403)\n" % message)
        sys.exit(1)
    prov = state.get("provenance", {}).get(run_id)
    if opt("-n") != "ci-channel-provenance" or prov is None:
        if state.get("download_not_found"):
            sys.stderr.write("gh: %s\n" % state["download_not_found"])
        sys.exit(1)
    d = opt("-D")
    os.makedirs(d, exist_ok=True)
    open(os.path.join(d, "ci-channel-provenance.txt"), "w").write(prov)
    sys.exit(0)
if args[:2] == ["release", "view"]:
    sys.exit(0 if state.get("release_exists") else 1)
if args[:2] in (["release", "create"], ["release", "upload"]):
    sys.exit(0)
sys.exit(1)
'''

MERGE = "a" * 40
HEAD = "b" * 40
OTHER_HEAD = "c" * 40
MERGE_TREE = "1" * 40
OLD_TREE = "2" * 40

# What GitHub returns for a rate or abuse limit, and what gh prints of it: the
# message reaches gh's stderr, the request id and the timestamp only reach the
# response headers. Both halves are asserted below, so the log proves the
# throttle instead of asserting it.
REFUSAL = {
    "message": "You have exceeded a secondary rate limit. Please wait a few minutes before you try again.",
    "request_id": "9104:E7D66:22AFA02:3FA8CEB:6ACC6B22",
    "date": "Mon, 12 Oct 2026 05:07:46 GMT",
}


class FakeGh:
    def __init__(self, tmp: Path, state: dict):
        self.bin = tmp / "bin"
        self.bin.mkdir()
        gh = self.bin / "gh"
        gh.write_text(FAKE_GH)
        gh.chmod(gh.stat().st_mode | stat.S_IEXEC)
        self.state = tmp / "state.json"
        self.state.write_text(json.dumps(state))
        self.log = tmp / "gh.log"
        self.log.write_text("")

    def env(self):
        env = dict(os.environ)
        env["PATH"] = f"{self.bin}{os.pathsep}{env['PATH']}"
        env["FAKE_GH_STATE"] = str(self.state)
        env["FAKE_GH_LOG"] = str(self.log)
        env["GITHUB_REPOSITORY"] = "owner/repo"
        env["GH_TOKEN"] = "unused"
        return env

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()]


def run(cmd, env=None):
    return subprocess.run([str(c) for c in cmd], capture_output=True, text=True, env=env)


def make_artifacts(root: Path, skip=None):
    for directory, name in ARTIFACTS.items():
        if directory == skip:
            continue
        (root / directory).mkdir(parents=True)
        (root / directory / name).write_bytes(f"{directory}/{name}".encode())


class StageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp)

    def test_stages_exactly_the_six_fixed_names(self):
        make_artifacts(self.tmp / "artifacts")
        r = run([STAGE, self.tmp / "artifacts", self.tmp / "out"])
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual({p.name for p in (self.tmp / "out").iterdir()}, ASSETS)
        with zipfile.ZipFile(self.tmp / "out" / "MesenAI-ci-windows-x64-aot.zip") as z:
            self.assertEqual(z.namelist(), ["Mesen.exe"])
        self.assertEqual(
            (self.tmp / "out" / "MesenAI-ci-macos-arm64.zip").read_bytes(),
            b"Mesen (macOS - macos-15 - clang_aot)/Mesen.app.zip",
        )

    def test_a_missing_artifact_stages_nothing(self):
        make_artifacts(self.tmp / "artifacts", skip="Mesen (Linux ARM64 - AppImage)")
        r = run([STAGE, self.tmp / "artifacts", self.tmp / "out"])
        self.assertEqual(r.returncode, 1)
        self.assertIn("Mesen (Linux ARM64 - AppImage)", r.stderr)
        self.assertFalse((self.tmp / "out").exists())

    def test_a_non_empty_out_dir_is_refused(self):
        make_artifacts(self.tmp / "artifacts")
        (self.tmp / "out").mkdir()
        (self.tmp / "out" / "stale.txt").write_text("x")
        r = run([STAGE, self.tmp / "artifacts", self.tmp / "out"])
        self.assertEqual(r.returncode, 1)
        self.assertEqual([p.name for p in (self.tmp / "out").iterdir()], ["stale.txt"])


class PublishTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp)
        self.out = self.tmp / "out"
        self.out.mkdir()
        for name in ASSETS:
            (self.out / name).write_text(name)

    def test_creates_a_prerelease_when_missing_then_clobbers(self):
        gh = FakeGh(self.tmp, {"release_exists": False})
        r = run([PUBLISH, self.out], env=gh.env())
        self.assertEqual(r.returncode, 0, r.stderr)
        calls = gh.calls()
        create = [c for c in calls if c[:2] == ["release", "create"]]
        self.assertEqual(len(create), 1)
        self.assertEqual(create[0][2], "ci-latest")
        self.assertIn("--prerelease", create[0])
        upload = [c for c in calls if c[:2] == ["release", "upload"]]
        self.assertEqual(len(upload), 1)
        self.assertEqual(upload[0][2], "ci-latest")
        self.assertIn("--clobber", upload[0])
        self.assertEqual({Path(a).name for a in upload[0] if "MesenAI-ci-" in a}, ASSETS)

    def test_an_existing_release_is_not_recreated(self):
        gh = FakeGh(self.tmp, {"release_exists": True})
        r = run([PUBLISH, self.out], env=gh.env())
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertFalse([c for c in gh.calls() if c[:2] == ["release", "create"]])

    def test_a_foreign_file_is_refused_before_any_call(self):
        (self.out / "notes.txt").write_text("x")
        gh = FakeGh(self.tmp, {"release_exists": True})
        r = run([PUBLISH, self.out], env=gh.env())
        self.assertEqual(r.returncode, 1)
        self.assertEqual(gh.calls(), [])

    def test_an_empty_dir_is_refused(self):
        empty = self.tmp / "empty"
        empty.mkdir()
        gh = FakeGh(self.tmp, {"release_exists": True})
        r = run([PUBLISH, empty], env=gh.env())
        self.assertEqual(r.returncode, 1)
        self.assertEqual(gh.calls(), [])


def a_run(run_id, created, conclusion="success", head=HEAD):
    return {"id": run_id, "created_at": created, "conclusion": conclusion, "head_sha": head}


@unittest.skipIf(shutil.which("jq") is None, "jq is not installed; the fake gh applies --jq with it")
class FindBuildTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp)

    def find(self, runs, provenance, merge=MERGE, head=HEAD, **state):
        doc = {
            "commits": {MERGE: MERGE_TREE},
            "runs": runs,
            "provenance": {str(k): v for k, v in provenance.items()},
            "refusal": REFUSAL,
        }
        doc.update(state)
        gh = FakeGh(self.tmp, doc)
        return run([FIND, merge, head], env=gh.env()), gh

    def test_the_newest_run_that_compiled_the_merged_tree_is_chosen(self):
        r, gh = self.find(
            [a_run(10, "2026-10-01T00:00:00Z"), a_run(11, "2026-10-02T00:00:00Z")],
            {10: f"commit={'d' * 40}\ntree={MERGE_TREE}\n", 11: f"commit={'e' * 40}\ntree={MERGE_TREE}\n"},
        )
        self.assertEqual((r.returncode, r.stdout.strip()), (0, "11"), r.stderr)
        query = [c[1] for c in gh.calls() if c[0] == "api" and "/runs" in c[1]][0]
        for part in ("event=pull_request", "status=success", f"head_sha={HEAD}"):
            self.assertIn(part, query)

    def test_prod_moved_after_the_newest_build_falls_back_to_an_older_match(self):
        r, _ = self.find(
            [a_run(10, "2026-10-01T00:00:00Z"), a_run(11, "2026-10-02T00:00:00Z")],
            {10: f"tree={MERGE_TREE}\n", 11: f"tree={OLD_TREE}\n"},
        )
        self.assertEqual((r.returncode, r.stdout.strip()), (0, "10"), r.stderr)

    def test_a_tree_mismatch_publishes_nothing(self):
        r, _ = self.find([a_run(11, "2026-10-02T00:00:00Z")], {11: f"tree={OLD_TREE}\n"})
        self.assertEqual((r.returncode, r.stdout), (1, ""))
        self.assertIn(OLD_TREE, r.stderr)

    def test_a_run_without_provenance_does_not_qualify(self):
        r, _ = self.find([a_run(11, "2026-10-02T00:00:00Z")], {})
        self.assertEqual((r.returncode, r.stdout), (1, ""))
        self.assertIn("no ci-channel-provenance artifact", r.stderr)

    def test_a_failed_or_cancelled_run_never_qualifies(self):
        r, _ = self.find(
            [a_run(11, "2026-10-02T00:00:00Z", "failure"), a_run(12, "2026-10-02T01:00:00Z", "cancelled")],
            {11: f"tree={MERGE_TREE}\n", 12: f"tree={MERGE_TREE}\n"},
        )
        self.assertEqual((r.returncode, r.stdout), (1, ""))

    def test_a_run_of_another_head_never_qualifies(self):
        r, _ = self.find([a_run(11, "2026-10-02T00:00:00Z", head=OTHER_HEAD)], {11: f"tree={MERGE_TREE}\n"})
        self.assertEqual((r.returncode, r.stdout), (1, ""))

    def test_a_malformed_sha_is_rejected_before_any_call(self):
        r, gh = self.find([], {}, merge="main")
        self.assertEqual(r.returncode, 2)
        self.assertEqual(gh.calls(), [])

    # A refused read is not an answer. Collapsing it into the same "nothing
    # qualifies" the caller reports for a completed lookup is what let
    # ci-channel-publish.yml write that claim into the job summary and dispatch
    # a full rebuild on the strength of a fact nobody verified (issue #1853).
    def test_a_refused_merge_commit_read_reaches_no_verdict(self):
        r, _ = self.find([], {}, refuse=["/git/commits/"])
        self.assertEqual(r.returncode, 3, r.stderr)
        self.assertIn(REFUSAL["message"], r.stderr)
        self.assertIn(REFUSAL["request_id"], r.stderr)
        self.assertIn(REFUSAL["date"], r.stderr)
        self.assertEqual(r.stdout, "")

    def test_a_refused_runs_list_read_reaches_no_verdict(self):
        r, _ = self.find([], {}, refuse=["/actions/workflows/build.yml/runs"])
        self.assertEqual(r.returncode, 3, r.stderr)
        self.assertIn(REFUSAL["message"], r.stderr)
        self.assertIn(REFUSAL["request_id"], r.stderr)
        self.assertEqual(r.stdout, "")

    def test_a_refused_artifact_download_for_every_candidate_reaches_no_verdict(self):
        r, _ = self.find(
            [a_run(10, "2026-10-01T00:00:00Z"), a_run(11, "2026-10-02T00:00:00Z")],
            {10: f"tree={MERGE_TREE}\n", 11: f"tree={MERGE_TREE}\n"},
            refuse_download=["10", "11"],
        )
        self.assertEqual(r.returncode, 3, r.stderr)
        self.assertIn(REFUSAL["message"], r.stderr)
        self.assertEqual(r.stdout, "")

    def test_a_completed_lookup_with_no_qualifying_run_is_still_a_verdict(self):
        r, _ = self.find([], {})
        self.assertEqual((r.returncode, r.stdout), (1, ""))
        self.assertIn("no successful pull_request run", r.stderr)
        self.assertNotIn(REFUSAL["message"], r.stderr)

    def test_an_expired_artifact_is_a_verdict_not_a_refusal(self):
        # The channel's own reason to give up on a candidate (no provenance
        # record, or an artifact gone after 90 days) is a completed read that
        # says "no" - exit 1, never the no-verdict 3.
        r, _ = self.find(
            [a_run(11, "2026-10-02T00:00:00Z")], {},
            download_not_found="no valid artifacts found to download",
        )
        self.assertEqual((r.returncode, r.stdout), (1, ""))
        self.assertIn("no ci-channel-provenance artifact", r.stderr)

    def test_the_success_path_prints_only_the_run_id(self):
        r, _ = self.find([a_run(11, "2026-10-02T00:00:00Z")], {11: f"tree={MERGE_TREE}\n"})
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(r.stdout, "11\n")
        self.assertNotIn("HTTP/", r.stdout)
        self.assertNotIn(REFUSAL["request_id"], r.stdout)


if __name__ == "__main__":
    unittest.main(verbosity=2)
