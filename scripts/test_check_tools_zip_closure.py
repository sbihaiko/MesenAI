#!/usr/bin/env python3
"""Issue #538: the tools zip must carry every tool the guides tell you to run.

`scripts/check_tools_zip_closure.py` used to start from a hand-kept
ENTRY_POINTS list, so it could only prove that the tools someone remembered to
list were complete. A tool the guides name and the zip does not carry stayed
invisible: `mep_add_cell.py` is printed on a dozen lines of
docs/remastering-a-game.md and was not in `tools-zip-manifest.txt`, and
`record_library.sh` is step 1 of the same guide and was not copied by
`scripts/release_macos.sh`. The guard has to read the guides, and this test is
the second opinion: it derives the required set itself from the guides, from
`tools-zip-manifest.txt` and from `release_macos.sh`, and fails when the three
disagree.

Assertions:

  1. the guard's derived set agrees with this test's own reading of the guides,
     which is what makes it a derivation rather than another hand-kept list;
  2. every guide-named `.py` is in `tools-zip-manifest.txt`, and every
     guide-named `.sh` is copied by `release_macos.sh`;
  3. what a copied `.sh` invokes at runtime ships beside it - a `python3
     "$here/<tool>.py"` is in the manifest, a `"$here/<tool>.sh"` is in the
     copied list (this is how `library_job.py` and `replay_chain.sh` are
     required by `record_library.sh`);
  4. the guard still reports a synthetic miss, and the exclusion list is
     documented, current and honoured.

Usage: python3 scripts/test_check_tools_zip_closure.py
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
REPO_ROOT = SCRIPTS.parent
sys.path.insert(0, str(SCRIPTS))

import check_tools_zip_closure as guard  # noqa: E402

# This test's own list of the guides whose commands are a download contract.
# Spelled here rather than read from the guard, so a guide quietly dropped from
# the guard's scan fails assertion 1 instead of silently shrinking the contract.
GUIDES = ("remastering-a-game.md", "hd-pack-authoring.md")

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"PASS: {msg}")


# --- this test's own reading of the guides ---------------------------------
# Deliberately independent of the guard: every `scripts/<name>.py|.sh` path or
# bare `<name>.py|.sh` token that names a file that exists in scripts/, minus
# test suites and minus the exclusions the guard documents.
TOKEN = re.compile(r"(?:scripts/)?([A-Za-z0-9_][A-Za-z0-9_.-]*\.(?:py|sh))")


def exclusions():
    return getattr(guard, "NOT_A_TOOL_FOR_READERS", {})


def existing_tool_files():
    return {p.name for p in SCRIPTS.iterdir() if p.is_file() and p.suffix in (".py", ".sh")}


def guide_named_tools():
    """The required set: guide-named, minus the documented exclusions."""
    return raw_guide_named_tools() - set(exclusions())


def raw_guide_named_tools():
    """Everything the guides name, exclusions included - what they are checked against."""
    present = existing_tool_files()
    named = set()
    for name in GUIDES:
        text = (REPO_ROOT / "docs" / name).read_text(encoding="utf-8")
        for token in TOKEN.findall(text):
            if token in present and not token.startswith("test_"):
                named.add(token)
    return named


def manifest_entries():
    text = (SCRIPTS / "tools-zip-manifest.txt").read_text(encoding="utf-8")
    return {line.strip() for line in text.splitlines() if line.strip() and not line.startswith("#")}


def release_script():
    return (SCRIPTS / "release_macos.sh").read_text(encoding="utf-8")


def copied_shell_tools():
    """Every shell tool release_macos.sh stages into the zip.

    Read from the script's one SHELL_TOOLS list, plus any `cp` of a
    `scripts/<name>.sh` - which is what the ad-hoc line this replaces looked
    like. Both sources are read so the check is falsifiable in either shape.
    """
    text = release_script()
    copied = set()
    match = re.search(r'^SHELL_TOOLS="([^"]*)"', text, re.MULTILINE)
    if match:
        copied |= set(match.group(1).split())
    copied |= set(re.findall(r'cp\s+"?\$ROOT/scripts/([A-Za-z0-9_]+\.sh)"?', text))
    return copied


def ad_hoc_shell_copies():
    """Shell tools copied by a bare `cp` instead of coming from the one list."""
    text = release_script()
    if not re.search(r'^SHELL_TOOLS="', text, re.MULTILINE):
        return {"(no SHELL_TOOLS list)"}
    listed = set(re.search(r'^SHELL_TOOLS="([^"]*)"', text, re.MULTILINE).group(1).split())
    return set(re.findall(r'cp\s+"?\$ROOT/scripts/([A-Za-z0-9_]+\.sh)"?', text)) - listed


# --- 1. the guard derives, it does not remember ----------------------------
def test_guard_derives_the_same_set():
    named_tools = getattr(guard, "named_tools", None)
    if named_tools is None:
        fail(
            "check_tools_zip_closure.py has no named_tools(): the required set is still a "
            "hand-kept ENTRY_POINTS list, so it cannot see a tool the guides name and the "
            "zip does not carry (#538)"
        )
        return
    expected = guide_named_tools()
    got = set(named_tools())
    if got != expected:
        fail(
            "the guard's guide-derived tool set differs from a plain reading of the guides - "
            f"missing: {sorted(expected - got)}, extra: {sorted(got - expected)}"
        )
        return
    ok(f"the guard derives the {len(expected)} guide-named tools from the guides themselves")


def test_derivation_is_not_trivial():
    named = guide_named_tools()
    for tool in ("mep_add_cell.py", "mep_build.py", "mep_lint.py", "record_library.sh", "record_stages.sh"):
        if tool not in named:
            fail(f"{tool} is named by a guide and exists in scripts/, but the derivation missed it")
            return
    ok("the derivation sees the tools of both guides")


# --- 2. what the guides name is what the zip carries ----------------------
def test_named_python_tools_are_in_the_manifest():
    listed = manifest_entries()
    missing = sorted(t for t in guide_named_tools() if t.endswith(".py") and t not in listed)
    if missing:
        fail(
            "guide-named tool(s) absent from scripts/tools-zip-manifest.txt, so the release zip "
            "ships a guide that tells the reader to run a file it does not carry: " + ", ".join(missing)
        )
        return
    ok("every guide-named .py tool is in the tools zip manifest")


def test_named_shell_tools_are_copied():
    copied = copied_shell_tools()
    missing = sorted(t for t in guide_named_tools() if t.endswith(".sh") and t not in copied)
    if missing:
        fail(
            "guide-named shell tool(s) release_macos.sh does not copy into the zip, so the "
            "download lacks a command the guide prints: " + ", ".join(missing)
        )
        return
    ad_hoc = sorted(ad_hoc_shell_copies())
    if ad_hoc:
        fail(
            "release_macos.sh stages shell tool(s) outside its one SHELL_TOOLS list, which is "
            "how a guide-named tool goes missing in the first place: " + ", ".join(ad_hoc)
        )
        return
    ok("every guide-named .sh tool is staged by release_macos.sh, from its one list")


# --- 3. a copied .sh carries what it runs ---------------------------------
def invocations(tool):
    """Absolute-`$here` references a staged shell tool makes, by suffix."""
    body = (SCRIPTS / tool).read_text(encoding="utf-8")
    return set(re.findall(r"\$here/([A-Za-z0-9_][A-Za-z0-9_.-]*\.(?:py|sh))", body))


def test_shell_tools_carry_their_dependencies():
    copied = copied_shell_tools()
    listed = manifest_entries()
    missing_py, missing_sh = set(), set()
    checked = 0
    for tool in sorted(copied):
        if not (SCRIPTS / tool).is_file():
            fail(f"release_macos.sh stages {tool}, which does not exist in scripts/")
            continue
        checked += 1
        for referenced in invocations(tool):
            if referenced.endswith(".py") and referenced not in listed:
                missing_py.add(f"{tool} -> {referenced}")
            elif referenced.endswith(".sh") and referenced not in copied:
                missing_sh.add(f"{tool} -> {referenced}")
    if missing_py:
        fail(
            "a staged shell tool runs a Python tool the manifest does not list, so the download "
            "fails at runtime: " + ", ".join(sorted(missing_py))
        )
    if missing_sh:
        fail(
            "a staged shell tool runs a shell tool release_macos.sh does not stage: "
            + ", ".join(sorted(missing_sh))
        )
    if not missing_py and not missing_sh:
        ok(f"each of the {checked} staged shell tool(s) carries what it invokes")


# --- 4. the guard still fails on a real miss, and says why ----------------
def audit(**kwargs):
    audit_fn = getattr(guard, "audit", None)
    if audit_fn is None:
        fail("check_tools_zip_closure.py has no audit(): the checks cannot be exercised on a fixture")
        return None
    return audit_fn(**kwargs)


def test_synthetic_miss_is_reported():
    errors = audit(
        guide_py=["mep_build.py", "ghost_tool.py"],
        guide_sh=["record_stages.sh", "ghost_runner.sh"],
        reached={"mep_build.py"},
        listed={"mep_build.py"},
        shell_tools={"record_stages.sh"},
        shell_py_refs={},
        shell_sh_refs={},
    )
    if errors is None:
        return
    joined = "\n".join(errors)
    if "ghost_tool.py" not in joined or "ghost_runner.sh" not in joined:
        fail(f"a guide-named tool missing from the zip must be reported by name, got: {errors!r}")
        return
    ok("the guard names a guide-named tool that neither the manifest nor the release script carries")


def test_synthetic_full_coverage_is_clean():
    errors = audit(
        guide_py=["mep_build.py"],
        guide_sh=["record_stages.sh"],
        reached={"mep_build.py", "library_job.py"},
        listed={"mep_build.py", "library_job.py"},
        shell_tools={"record_stages.sh"},
        shell_py_refs={"record_stages.sh": {"library_job.py"}},
        shell_sh_refs={"record_stages.sh": set()},
    )
    if errors is None:
        return
    if errors:
        fail(f"a fully covered tree must audit clean, got: {errors!r}")
        return
    ok("a fully covered tree audits clean")


def test_synthetic_shell_dependency_miss_is_reported():
    errors = audit(
        guide_py=["mep_build.py"],
        guide_sh=[],
        reached={"mep_build.py"},
        listed={"mep_build.py"},
        shell_tools={"record_stages.sh"},
        shell_py_refs={"record_stages.sh": {"library_job.py"}},
        shell_sh_refs={"record_stages.sh": {"replay_chain.sh"}},
    )
    if errors is None:
        return
    joined = "\n".join(errors)
    if "library_job.py" not in joined or "replay_chain.sh" not in joined:
        fail(f"a shell tool's unshipped runtime dependency must be reported, got: {errors!r}")
        return
    ok("the guard reports what a staged shell tool invokes and the zip lacks")


# --- 5. the exclusion list is documented, current and honoured -------------
def test_exclusions_are_documented_and_current():
    named = raw_guide_named_tools()
    stale = []
    for tool, reason in exclusions().items():
        if not reason.strip():
            stale.append(f"{tool} is excluded from the zip with no stated reason")
        if not (SCRIPTS / tool).is_file():
            stale.append(f"{tool} is excluded from the zip but no longer exists in scripts/")
        if tool not in named:
            stale.append(f"{tool} is excluded from the zip but no guide names it any more; drop the entry")
    if stale:
        fail("the exclusion list is not current: " + "; ".join(stale))
        return
    ok(f"{len(exclusions())} documented exclusion(s), each still named by a guide")


def test_excluded_tools_are_not_required():
    named_tools = getattr(guard, "named_tools", None)
    if named_tools is None:
        fail("check_tools_zip_closure.py has no named_tools(), so the exclusions cannot be honoured")
        return
    wrongly_required = sorted(set(exclusions()) & set(named_tools()))
    if wrongly_required:
        fail(f"excluded tool(s) still required by the guard: {wrongly_required}")
        return
    ok("excluded tools are not required by the guard")


def main():
    test_guard_derives_the_same_set()
    test_derivation_is_not_trivial()
    test_named_python_tools_are_in_the_manifest()
    test_named_shell_tools_are_copied()
    test_shell_tools_carry_their_dependencies()
    test_synthetic_miss_is_reported()
    test_synthetic_full_coverage_is_clean()
    test_synthetic_shell_dependency_miss_is_reported()
    test_exclusions_are_documented_and_current()
    test_excluded_tools_are_not_required()

    print()
    if FAILURES:
        print(f"{len(FAILURES)} failure(s)")
        return 1
    print("All tools-zip closure checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
