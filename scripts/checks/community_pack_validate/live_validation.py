"""Guards on the dormant live-validation path of community-pack-validate.yml.

`LIVE_VALIDATION_ENABLED` is 'false' (2026-08-27) and its own comment at the top
of the workflow says what flipping it back on requires. Two of those
requirements are properties of the *code* below the flag, not of a runner, so
they are checkable here and were until now checked by nothing: the structural
checker passed with and without them, which is how a change to this path could
be edited away without a single failure.

1. Three steps are only safe because the enabled path degrades rather than
   fails — the flag's comment promises the live-validation block's steps are
   "continue-on-error / existence-gated", and on 2026-10-05 a two-lens panel
   found the code contradicting that promise in three places (a hard failure
   there skipped the job's later steps, which is the verdict). All three carry
   `continue-on-error: true` now; `check_live_validation_steps_continue_on_error`
   holds them to it.

2. The autofix push must not carry the token in the remote URL. An earlier
   attempt embedded `PROJECT_PAT` in it — it worked, and left the secret in the
   checkout's git config for the rest of the job.
   `check_autofix_push_keeps_credential_out_of_the_remote` holds the push to the
   helper-based form instead.

Neither check asserts anything about whether the flag should be on: that is the
panel's condition (2), which only a runner can settle.
"""
import re

from ._shared import fail

# The three steps the flag's comment names as the panel's finding 1.
CONTINUE_ON_ERROR_STEPS = (
    "Install SDL2 dev headers",
    "Cache Mesen core build",
    "Detect drift between mep_lint.py and the real core",
)

# The step that pushes the autofix branch.
AUTOFIX_PUSH_STEP = "Publish autofix as a PR (if converged)"

_STEP_START = re.compile(r"^(\s*)- name:\s*(.*)$")


def step_block(text, name):
    """The lines of one `- name: <name>` step, up to the next step or job.

    Returns None when no step carries that name. The block starts at the
    `- name:` line and ends before the next line at the same-or-shallower
    indentation that opens another list item, so a step that follows keeps its
    own flags out of this one's block.
    """
    lines = text.splitlines()
    for index, line in enumerate(lines):
        match = _STEP_START.match(line)
        if not match or match.group(2).strip() != name:
            continue
        indent = len(match.group(1))
        end = len(lines)
        for probe in range(index + 1, len(lines)):
            following = lines[probe]
            if not following.strip():
                continue
            stripped = len(following) - len(following.lstrip())
            if stripped <= indent:
                end = probe
                break
        return "\n".join(lines[index:end])
    return None


def check_live_validation_steps_continue_on_error(text):
    for name in CONTINUE_ON_ERROR_STEPS:
        block = step_block(text, name)
        if block is None:
            fail(f"live-validation step not found: {name}")
        elif not re.search(r"^\s*continue-on-error:\s*true\s*$", block, re.M):
            fail(
                f"live-validation step has no `continue-on-error: true`, so a "
                f"failure there skips the verdict instead of degrading to the "
                f"lint-only one: {name}"
            )


def check_autofix_push_keeps_credential_out_of_the_remote(text):
    block = step_block(text, AUTOFIX_PUSH_STEP)
    if block is None:
        fail(f"autofix push step not found: {AUTOFIX_PUSH_STEP}")
        return
    if re.search(r"https://[^\"\s]*@github\.com", block):
        fail("autofix push embeds a credential in the remote URL")
    if "credential.helper" not in block:
        fail("autofix push does not go through a credential helper")
