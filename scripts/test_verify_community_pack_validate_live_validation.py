#!/usr/bin/env python3
"""Framework-free checks for the live-validation guards (F6.2c, topic module).

The guards exist because the workflow's own structural checker passed with and
without the changes a 2026-10-05 panel asked for: nothing held the enabled
path's three `continue-on-error` steps, or the autofix push's credential
handling, to their documented shape. Each fixture below is the shape the guard
must catch, plus the shape it must leave alone.

Usage: python3 scripts/test_verify_community_pack_validate_live_validation.py
"""
from __future__ import annotations

import sys
from pathlib import Path

CHECKS = Path(__file__).resolve().parent / "checks"
sys.path.insert(0, str(CHECKS))
from community_pack_validate import _shared, live_validation  # noqa: E402

FAILURES = []
WORKFLOW = (
    Path(__file__).resolve().parents[1]
    / ".github" / "workflows" / "community-pack-validate.yml"
)


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"PASS: {msg}")


def run(check, text):
    """The failures one check reports for a fixture, with shared state reset."""
    _shared.FAILURES.clear()
    check(text)
    return list(_shared.FAILURES)


# The enabled path's three steps, as the flag's comment names them.
STEP_OK = """      - name: {name}
        if: env.LIVE_VALIDATION_ENABLED == 'true'
        continue-on-error: true
        run: make thing
"""
STEP_BARE = """      - name: {name}
        if: env.LIVE_VALIDATION_ENABLED == 'true'
        run: make thing
"""
PUSH_LEAKING = """      - name: Publish autofix as a PR (if converged)
        run: |
          git remote set-url origin "https://x-access-token:${PROJECT_PAT}@github.com/${REPO}.git"
          git push origin "$BRANCH"
"""
PUSH_HELPER = """      - name: Publish autofix as a PR (if converged)
        run: |
          git remote set-url origin "https://github.com/${REPO}.git"
          CREDENTIAL_HELPER='!f() { echo username=x-access-token; echo password="$GH_TOKEN"; }; f'
          git -c credential.helper= -c credential.helper="$CREDENTIAL_HELPER" push origin "$BRANCH"
"""


def check_step_block():
    block = live_validation.step_block(STEP_OK.format(name="A step"), "A step")
    if block is None or "continue-on-error" not in block:
        fail("step_block did not return the step it was asked for")
        return
    if "A step" in (live_validation.step_block(STEP_OK.format(name="A step"), "Other") or ""):
        fail("step_block matched a step by the wrong name")
        return
    ok("step_block returns the named step and nothing else")

    # A following step's flags must not leak into this step's block.
    pair = STEP_BARE.format(name="First") + STEP_OK.format(name="Second")
    if "continue-on-error" in live_validation.step_block(pair, "First"):
        fail("a later step's continue-on-error leaked into the earlier step")
        return
    ok("a later step's flags do not leak into the block before it")


def check_continue_on_error():
    # One step stripped of its flag, the other two as the workflow writes them:
    # exactly one failure, naming the stripped one.
    for name in live_validation.CONTINUE_ON_ERROR_STEPS:
        text = "".join(
            (STEP_BARE if other == name else STEP_OK).format(name=other)
            for other in live_validation.CONTINUE_ON_ERROR_STEPS
        )
        got = run(live_validation.check_live_validation_steps_continue_on_error, text)
        if len(got) != 1 or name not in got[0]:
            fail(f"stripping {name}'s flag should be one failure naming it, got {got!r}")
            return
    ok("each of the three steps is flagged when it carries no continue-on-error")

    text = "".join(
        STEP_OK.format(name=name) for name in live_validation.CONTINUE_ON_ERROR_STEPS
    )
    got = run(live_validation.check_live_validation_steps_continue_on_error, text)
    if got:
        fail(f"the documented shape should pass, got {got!r}")
        return
    ok("the three steps as the workflow writes them pass")

    # A missing step is a failure, not silence: the guard must notice a rename.
    got = run(live_validation.check_live_validation_steps_continue_on_error, "")
    if len(got) != len(live_validation.CONTINUE_ON_ERROR_STEPS):
        fail(f"a renamed step should be reported, got {got!r}")
        return
    ok("a step that is not there at all is reported rather than skipped")


def check_autofix_push():
    got = run(live_validation.check_autofix_push_keeps_credential_out_of_the_remote, PUSH_LEAKING)
    if not any("credential in the remote URL" in m for m in got):
        fail(f"a credential in the remote URL should be reported, got {got!r}")
        return
    ok("a token embedded in the autofix remote URL is reported")

    got = run(live_validation.check_autofix_push_keeps_credential_out_of_the_remote, PUSH_HELPER)
    if got:
        fail(f"the helper-based push should pass, got {got!r}")
        return
    ok("the credential-helper push passes")

    got = run(
        live_validation.check_autofix_push_keeps_credential_out_of_the_remote,
        '      - name: Publish autofix as a PR (if converged)\n        run: git push origin x\n',
    )
    if not any("credential helper" in m for m in got):
        fail(f"a push with no helper should be reported, got {got!r}")
        return
    ok("a push that goes through no credential helper is reported")


def check_real_workflow():
    if not WORKFLOW.is_file():
        fail(f"{WORKFLOW} not found")
        return
    text = WORKFLOW.read_text(encoding="utf-8")
    for check in (
        live_validation.check_live_validation_steps_continue_on_error,
        live_validation.check_autofix_push_keeps_credential_out_of_the_remote,
    ):
        got = run(check, text)
        if got:
            fail(f"the repo's own workflow fails a guard: {got!r}")
            return
    ok("the repo's own workflow passes both guards")


def main():
    check_step_block()
    check_continue_on_error()
    check_autofix_push()
    check_real_workflow()

    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
