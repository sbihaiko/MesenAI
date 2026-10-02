#!/usr/bin/env python3
"""Verifies the ADR-0248 publish-side wiring (slice R.3): the `[Cheat]` Issue
Form, the `cheat-submitted.yml` workflow and the labels they use.

  * .github/ISSUE_TEMPLATE/cheat-code.yml is an Issue Form titled "[Cheat] ",
    labelled `cheat`, with exactly the four required fields of section 1 (Game
    as SHA-1 + name, Console, Code, Description), whose labels and dropdown
    options are the ones scripts/cheat_submission.py parses - a renamed label
    would make the gate read an empty field.
  * .github/workflows/cheat-submitted.yml triggers on issues opened/edited and
    an exact `/revalidate` comment, only for issues titled `[Cheat] ` or
    carrying `cheat`, with least-privilege permissions and a per-issue
    concurrency group; runs scripts/cheat_submission.py with the live
    `cheat:valid` rows; seeds one 👍; and never interpolates untrusted issue
    text into a `run:` script.
  * the workflow creates the three cheat labels itself (`--force`, idempotent)
    and scripts/ensure_community_pack_labels.sh declares them.

Usage: python3 scripts/checks/verify_cheat_form_and_workflow.py
"""
import pathlib
import re
import sys

import yaml

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import cheat_submission  # noqa: E402

FORM = ROOT / ".github" / "ISSUE_TEMPLATE" / "cheat-code.yml"
WORKFLOW = ROOT / ".github" / "workflows" / "cheat-submitted.yml"
LABELS = ROOT / "scripts" / "ensure_community_pack_labels.sh"
LABEL_NAMES = ("cheat", "cheat:valid", "cheat:invalid")
FIELD_IDS = ("game_sha1", "game_name", "console", "code", "description")


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
    if data.get("title") != "[Cheat] ":
        fail(f"form title must be '[Cheat] ', found {data.get('title')!r}")
    if data.get("labels") != ["cheat"]:
        fail(f"form labels must be ['cheat'], found {data.get('labels')!r}")
    fields = {e.get("id"): e for e in data.get("body", []) if isinstance(e, dict) and e.get("id")}
    if tuple(fields) != FIELD_IDS:
        fail(f"form fields must be exactly {FIELD_IDS}, found {tuple(fields)}")
    for fid, field in fields.items():
        if not (field.get("validations") or {}).get("required"):
            fail(f"form field {fid} must be required")
    labels = {f["attributes"]["label"] for f in fields.values()}
    if labels != set(cheat_submission.FIELD_LABELS):
        fail(f"form labels {sorted(labels)} differ from the ones the gate parses {sorted(cheat_submission.FIELD_LABELS)}")
    console = fields["console"]
    if console.get("type") != "dropdown":
        fail("the console field must be a dropdown")
    if console["attributes"].get("options") != list(cheat_submission.CONSOLES):
        fail(f"console options must be {list(cheat_submission.CONSOLES)}, found {console['attributes'].get('options')}")
    for fid in ("game_sha1", "game_name", "code", "description"):
        if fields[fid].get("type") != "input":
            fail(f"form field {fid} must be a single-line input")
    if "80" not in fields["description"]["attributes"].get("description", ""):
        fail("the description field must state the 80-character bound")
    if "form, not its" not in text:
        fail("form must say the bot checks the code's form, not its effect")


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
    for needle in ("'cheat'", "'/revalidate'", "scripts/cheat_submission.py", "--live-file",
                   "--label cheat:valid --state open", "content='+1'", "--add-label", "--remove-label",
                   "--add-label=cheat", "--edit-last"):
        if needle not in text:
            fail(f"workflow must contain {needle}")
    if "startsWith(github.event.issue.title, '[Cheat] ')" not in text:
        fail("workflow must also trigger on a title starting with '[Cheat] ' (the form label does not exist on a fresh repo)")
    creates = [ln.strip() for ln in text.splitlines() if ln.strip().startswith("gh label create ")]
    for name in LABEL_NAMES:
        mine = [ln for ln in creates if re.match(rf'gh label create "?{re.escape(name)}"?\s', ln)]
        if not mine:
            fail(f"workflow must create label {name}")
        if not all(re.search(r"\s--force(\s|$)", ln) for ln in mine):
            fail(f"every `gh label create {name}` must carry --force")
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
    print("PASS: cheat-code.yml form, cheat-submitted.yml workflow and the three cheat labels are wired")
    return 0


if __name__ == "__main__":
    sys.exit(main())
