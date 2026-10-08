#!/usr/bin/env python3
"""ADR-0204 §6: behavior of the three scripts behind the `ci-latest` channel.

- scripts/stage_ci_channel_assets.sh  - artifact dirs -> the six fixed asset
  names, all or nothing;
- scripts/publish_ci_channel.sh       - create the pre-release if missing, then
  upload with --clobber, refusing anything that is not a channel asset;
- scripts/ci_channel_find_build.sh    - the decision ci-channel-publish.yml
  makes on a merge into `prod`: republish a pull request build only when the
  tree it recorded equals the merge commit's tree.

`gh` is replaced by a fake on PATH that serves canned API answers, records
every call and applies `--jq` programs with the real `jq`, so the filters in
the scripts are exercised as written. No network, no token. The find-build
cases skip (and say so) when `jq` is not installed.

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

if args[:1] == ["api"]:
    path = args[1]
    jq = opt("--jq")
    if "/git/commits/" in path:
        sha = path.rsplit("/", 1)[1]
        if sha not in state["commits"]:
            sys.exit(1)
        sys.exit(emit({"sha": sha, "tree": {"sha": state["commits"][sha]}}, jq))
    if "/actions/workflows/build.yml/runs" in path:
        sys.exit(emit({"workflow_runs": state["runs"]}, jq))
    sys.exit(1)
if args[:2] == ["run", "download"]:
    run_id = args[2]
    prov = state.get("provenance", {}).get(run_id)
    if opt("-n") != "ci-channel-provenance" or prov is None:
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

    def find(self, runs, provenance, merge=MERGE, head=HEAD):
        gh = FakeGh(self.tmp, {
            "commits": {MERGE: MERGE_TREE},
            "runs": runs,
            "provenance": {str(k): v for k, v in provenance.items()},
        })
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


if __name__ == "__main__":
    unittest.main(verbosity=2)
