#!/usr/bin/env python3
"""The community-pack board Status around one validation run (bug #671).

community-pack-validate.yml moves the item to "Em validação" before doing any
work. A run that fails or is cancelled after that (Gemini 429/5xx, the
classify timeout, a transient gh error) used to strand it there, and the next
catalog run de-listed a pack whose previous verdict was "Aceito". The workflow
now records the Status the item had before the run (`current`) and, from an
`if: failure() || cancelled()` step, puts it back (`restore`).

`restore` only acts while the item is still in "Em validação": a verdict this
run already wrote, or a human move during the run, stands. A prior Status that
is not a verdict or "Novo envio" (none yet, or a run stranded before this
fix) goes back to "Novo envio".

The Status option ids come from the workflow's own env (STATUS_* and
PROJECT_ID / STATUS_FIELD_ID), so they are declared in one place. Option names
are the board's Portuguese literals, quoted verbatim.

Usage:
  pack_board_status.py current ITEM_ID              print the Status option id ('' when unset)
  pack_board_status.py restore ITEM_ID PRIOR_ID     restore PRIOR_ID if still "Em validação"
"""
from __future__ import annotations

import json
import os
import subprocess
import sys

NAMES = {
    "STATUS_NOVO_ENVIO": "Novo envio",
    "STATUS_EM_VALIDACAO": "Em validação",
    "STATUS_INVALIDO": "Inválido",
    "STATUS_ACEITO_PARCIAL": "Aceito parcial (HD Mesen)",
    "STATUS_ACEITO_COMPLETO": "Aceito (MEP completo)",
}
# One item's Status, read directly: no board listing (and so no item-list
# --limit to get wrong, #670).
QUERY = ("query($id: ID!) { node(id: $id) { ... on ProjectV2Item { fieldValueByName(name: \"Status\") "
         "{ ... on ProjectV2ItemFieldSingleSelectValue { optionId name } } } } }")


def run_gh(args):
    return subprocess.run(["gh", *args], capture_output=True, text=True, check=True).stdout


def _ids():
    return {key: os.environ[key] for key in NAMES}


def _name(option_id):
    return next((NAMES[k] for k, v in _ids().items() if v == option_id), option_id or "(none)")


def current_status(gh, item_id):
    data = json.loads(gh(["api", "graphql", "-f", f"query={QUERY}", "-f", f"id={item_id}"]))
    value = (((data.get("data") or {}).get("node") or {}).get("fieldValueByName")) or {}
    return value.get("optionId") or ""


def restore(gh, item_id, prior):
    ids = _ids()
    current = current_status(gh, item_id)
    if current != ids["STATUS_EM_VALIDACAO"]:
        print(f"Status is '{_name(current)}', not 'Em validação': nothing to restore")
        return 0
    keep = {ids[k] for k in ("STATUS_NOVO_ENVIO", "STATUS_INVALIDO", "STATUS_ACEITO_PARCIAL", "STATUS_ACEITO_COMPLETO")}
    target = prior if prior in keep else ids["STATUS_NOVO_ENVIO"]
    gh(["project", "item-edit", "--id", item_id, "--project-id", os.environ["PROJECT_ID"],
        "--field-id", os.environ["STATUS_FIELD_ID"], "--single-select-option-id", target])
    print(f"run did not finish: Status restored from 'Em validação' to '{_name(target)}'")
    return 0


def main(argv):
    if len(argv) == 3 and argv[1] == "current":
        print(current_status(run_gh, argv[2]))
        return 0
    if len(argv) == 4 and argv[1] == "restore":
        return restore(run_gh, argv[2], argv[3])
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
