#!/usr/bin/env python3
"""Bugs #671 and #679: the board Status around a validation run.

#671 — community-pack-validate.yml moves the item to "Em validação" first and
had no failure()/cancelled() path out of it. A run that died afterwards
(Gemini 429/5xx, classify timeout, a transient gh error) stranded an accepted
pack there with `pack:valid`, and the next catalog run de-listed it. Now the
Status the item had before the run is captured up front and restored by an
`if: failure() || cancelled()` step, through scripts/pack_board_status.py —
only while the item is still in "Em validação" (a verdict this run already
wrote, or a human move, stands).

#679 — catalog regeneration was dispatched only on acceptance, so a
revalidation that rejected an accepted pack left it listed (and
auto-installing) until the daily catalog run. A final "Inválido" dispatches
too.

Usage: python3 scripts/test_pack_board_status.py
"""
import json
import os
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "scripts"))
WORKFLOW = REPO / ".github/workflows/community-pack-validate.yml"

FAILURES = []
IDS = {
    "PROJECT_ID": "PVT_x", "STATUS_FIELD_ID": "PVTSSF_x",
    "STATUS_NOVO_ENVIO": "novo", "STATUS_EM_VALIDACAO": "emval", "STATUS_INVALIDO": "inval",
    "STATUS_ACEITO_PARCIAL": "parcial", "STATUS_ACEITO_COMPLETO": "completo",
}


def check(name, got, want):
    if got == want:
        print(f"PASS {name}")
    else:
        FAILURES.append(name)
        print(f"FAIL {name}: got {got!r}, want {want!r}")


class FakeGh:
    def __init__(self, current):
        self.current = current
        self.calls = []

    def __call__(self, args):
        self.calls.append(list(args))
        if args[:2] == ["api", "graphql"]:
            value = {"optionId": self.current, "name": "x"} if self.current else None
            return json.dumps({"data": {"node": {"fieldValueByName": value}}})
        if args[:2] == ["project", "item-edit"]:
            return ""
        raise AssertionError(f"unexpected gh call: {args}")

    def edits(self):
        return [c[c.index("--single-select-option-id") + 1] for c in self.calls if c[:2] == ["project", "item-edit"]]


def unit_checks():
    try:
        import pack_board_status as pbs
    except ImportError as exc:
        check("scripts/pack_board_status.py exists", str(exc), "")
        return
    os.environ.update(IDS)
    fake = FakeGh("parcial")
    check("current: reads the item's Status option id", pbs.current_status(fake, "ITEM"), "parcial")
    graphql = fake.calls[0]
    check("current: one graphql read of this item, no board listing",
          ("ITEM" in " ".join(graphql), any("item-list" in a for a in graphql)), (True, False))
    check("current: an item with no Status yet -> ''", pbs.current_status(FakeGh(None), "ITEM"), "")
    for prior, want in (("parcial", ["parcial"]), ("completo", ["completo"]), ("inval", ["inval"]),
                        ("novo", ["novo"]), ("", ["novo"]), ("emval", ["novo"]), ("garbage", ["novo"])):
        fake = FakeGh("emval")
        rc = pbs.restore(fake, "ITEM", prior)
        check(f"restore: stranded in Em validação, prior {prior!r} -> {want}", (rc, fake.edits()), (0, want))
    for current in ("inval", "parcial", "novo", ""):
        fake = FakeGh(current)
        rc = pbs.restore(fake, "ITEM", "parcial")
        check(f"restore: Status already moved to {current!r} -> untouched", (rc, fake.edits()), (0, []))
    fake = FakeGh("emval")
    pbs.restore(fake, "ITEM", "parcial")
    edit = [c for c in fake.calls if c[:2] == ["project", "item-edit"]][0]
    check("restore: edits this item's Status field on this project",
          [edit[edit.index(f) + 1] for f in ("--id", "--project-id", "--field-id")], ["ITEM", "PVT_x", "PVTSSF_x"])


def step(text, marker):
    blocks = [b for b in text.split("\n      - name:") if marker in b]
    return blocks[0] if blocks else ""


def workflow_checks():
    text = WORKFLOW.read_text(encoding="utf-8")
    first = step(text, "id: project-item")
    capture = first.find("pack_board_status.py current")
    first_edit = first.find("gh project item-edit")
    check("#671: the prior Status is captured before the first Status edit",
          0 <= capture < first_edit, True)
    check("#671: the prior Status is a step output", 'echo "prior_status_id=' in first, True)
    restore = step(text, "pack_board_status.py restore")
    check("#671: a restore step exists", bool(restore), True)
    check("#671: it runs on failure or cancellation",
          re.search(r"if: \(failure\(\) \|\| cancelled\(\)\)", restore) is not None, True)
    check("#671: it restores the captured prior Status",
          "steps.project-item.outputs.prior_status_id" in restore, True)
    check("#671: the restore step is the last step", bool(restore) and text.rstrip().endswith(restore.rstrip()), True)
    dispatch = step(text, "gh workflow run community-pack-catalog.yml")
    cond = re.search(r"if: ([^\n]+)", dispatch)
    cond = cond.group(1) if cond else ""
    for status in ("STATUS_ACEITO_PARCIAL", "STATUS_ACEITO_COMPLETO", "STATUS_INVALIDO"):
        check(f"#679: catalog dispatch fires on a final {status}",
              f"env.FINAL_STATUS_ID == env.{status}" in cond, True)


def main():
    unit_checks()
    workflow_checks()
    if FAILURES:
        print(f"{len(FAILURES)} FAILED")
        return 1
    print("all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
