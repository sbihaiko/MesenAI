#!/usr/bin/env python3
"""Verifies the ADR-0205 publish-side wiring (slice R.1): the `[Replay]` Issue
Form, the `replay-submitted.yml` workflow and the labels they use.

  * .github/ISSUE_TEMPLATE/replay.yml is an Issue Form titled "[Replay] ",
    labelled `replay`, with a required `attachment` textarea (the author drags
    the file in, section 6) and an optional `notes` textarea -- and no
    `pack_link`-style field: "paste a link" is the model section 6 rejects here.
  * .github/workflows/replay-submitted.yml triggers on issues opened/edited and
    an exact `/revalidate` comment, only for issues titled `[Replay] ` or
    already carrying `replay` (the title is the trigger because GitHub does not
    apply a form label that does not exist yet), with least-privilege permissions, a per-issue concurrency group, and runs
    scripts/replay_submission.py. Untrusted issue text (body, title, login)
    must reach the shell only through `env:`, never interpolated into `run:`.
  * the workflow creates the four labels itself (R.2 added section 9's
    `replay:removed`, which no workflow ever applies) (`gh label create --force`,
    idempotent) and adds `replay` to the issue, so nothing has to be set up by
    hand; scripts/ensure_community_pack_labels.sh still declares them.

Usage: python3 scripts/checks/verify_replay_form_and_workflow.py
"""
import pathlib
import re
import sys

import yaml

ROOT = pathlib.Path(__file__).resolve().parents[2]
FORM = ROOT / ".github" / "ISSUE_TEMPLATE" / "replay.yml"
WORKFLOW = ROOT / ".github" / "workflows" / "replay-submitted.yml"
LABELS = ROOT / "scripts" / "ensure_community_pack_labels.sh"
LABEL_NAMES = ("replay", "replay:valid", "replay:invalid", "replay:removed")


def fail(msg):
    print(f"FAIL: {msg}")
    sys.exit(1)


def load(path):
    if not path.is_file():
        fail(f"file not found: {path.relative_to(ROOT)}")
    text = path.read_text(encoding="utf-8")
    return yaml.safe_load(text), text


def check_form():
    data, text = load(FORM)
    if data.get("title") != "[Replay] ":
        fail(f"form title must be '[Replay] ', found {data.get('title')!r}")
    if data.get("labels") != ["replay"]:
        fail(f"form labels must be ['replay'], found {data.get('labels')!r}")
    fields = {e.get("id"): e for e in data.get("body", []) if isinstance(e, dict) and e.get("id")}
    attachment = fields.get("attachment")
    if not attachment or attachment.get("type") != "textarea":
        fail("form needs a textarea field with id 'attachment'")
    if not (attachment.get("validations") or {}).get("required"):
        fail("the attachment field must be required")
    notes = fields.get("notes")
    if not notes or notes.get("type") != "textarea":
        fail("form needs a textarea field with id 'notes'")
    if (notes.get("validations") or {}).get("required"):
        fail("the notes field must stay optional")
    extra = set(fields) - {"attachment", "notes"}
    if extra:
        fail(f"form asks for more than the attachment and notes: {sorted(extra)}")
    if "Record and share" not in text:
        fail("form must tell the author to use the Record and share action")


def check_workflow():
    data, text = load(WORKFLOW)
    triggers = data.get(True) or data.get("on")  # YAML 1.1 reads a bare `on` as True
    if set(triggers) != {"issues", "issue_comment"}:
        fail(f"triggers must be issues + issue_comment, found {sorted(triggers)}")
    if set(triggers["issues"]["types"]) != {"opened", "edited"}:
        fail("issues trigger must be opened + edited")
    if data.get("permissions") != {"contents": "read", "issues": "write"}:
        fail(f"permissions must be exactly contents: read, issues: write; found {data.get('permissions')!r}")
    if "concurrency" not in data:
        fail("a per-issue concurrency group is required")
    for needle in ("'replay'", "'/revalidate'", "scripts/replay_submission.py", "--add-label", "--remove-label",
                   '--number "$ISSUE_NUMBER"'):
        if needle not in text:
            fail(f"workflow must contain {needle}")
    # the title is the trigger: a not-yet-existing form label is silently skipped
    if "startsWith(github.event.issue.title, '[Replay] ')" not in text:
        fail("workflow must also trigger on a title starting with '[Replay] ' (the form label does not exist on a fresh repo)")
    creates = [ln.strip() for ln in text.splitlines() if ln.strip().startswith("gh label create ")]
    for name in LABEL_NAMES:
        mine = [ln for ln in creates if re.match(rf'gh label create "?{re.escape(name)}"?\s', ln)]
        if not mine:
            fail(f"workflow must create label {name}")
        if not all(re.search(r"\s--force(\s|$)", ln) for ln in mine):
            fail(f"every `gh label create {name}` must carry --force (idempotence: a later run must not fail on an existing label)")
    if "--add-label=replay" not in text:
        fail("workflow must add the form label `replay` to an issue that lacks it")
    for job in data["jobs"].values():
        for step in job.get("steps", []):
            run = step.get("run") or ""
            if re.search(r"\$\{\{\s*github\.event\.(issue|comment)\.", run):
                fail(f"untrusted issue text interpolated into a run: step ({step.get('name')!r}); pass it through env:")


def check_labels():
    text = LABELS.read_text(encoding="utf-8")
    for name in LABEL_NAMES:
        if not re.search(rf'^\s*"{re.escape(name)}\|[0-9A-Fa-f]{{6}}\|', text, re.M):
            fail(f"{LABELS.name} is missing label {name}")


def main():
    check_form()
    check_workflow()
    check_labels()
    print("PASS: replay.yml form, replay-submitted.yml workflow and the four replay labels are wired")
    return 0


if __name__ == "__main__":
    sys.exit(main())
