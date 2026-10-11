#!/usr/bin/env python3
"""`docs/squad/` carries the squad's own incident records, and nothing read the folder.

Three defects shipped in it in one day and were filed as #1825, #1826 and
#1827: the folder index omitted records a reader needs to discover an
operational constraint, a versioned en-US doc spelled a word the en-GB way, and
a record's Status line read as done while the file's own body said the fix was
not in the live tree. No check covered any of the three — `grep -rln 'docs/squad'
scripts/checks/` found no file, and `verify_md_links.py` only resolves links.
So the same folder is asserted here, instead of by hand a fourth time.

Three rules, deliberately narrow so each reads a declaration and not a mention:

  (a) Every `docs/squad/*.md` record is listed in `docs/squad/README.md`. The
      index is how a reader discovers an operational constraint they were not
      looking for; a record missing from it is invisible.
  (b) No en-GB spelling in `docs/squad/*.md`. The repo's language rule is
      en-US for everything versioned, this folder included.
  (c) A record's Status line agrees with the record's own body about whether
      the fix is live. The Status block is the `Status:` line and the lines
      that continue its paragraph, up to the first blank line — the block the
      file puts first and a reader takes as the state of the world. When the
      body of that same file says the fix is not merged into the live tree, the
      Status block must carry a pending marker (`pending deployment`, `not
      deployed`, `not merged`, `not yet landed`); a block that only says
      `fixed in the fork` while the body says it is unmerged is the #1825
      defect. The body is the source of truth, so the rule maintains itself:
      when the operator lands the fix, both the status and the body sentence
      change together.

Rule (c) is deliberately asymmetric — a Status block claiming pending while the
body says it landed is not caught — because only the first direction is a claim
written ahead of what the file admits.

The scan is whole-file, code fences included: a record anywhere in the folder is
the artefact under the en-US rule, and a fence is not an exemption from it.

Usage: python3 scripts/checks/verify_squad_docs.py [repo-root]
"""
from __future__ import annotations

import re
import sys
from pathlib import Path


#Each of these is en-GB by rule and none is a word the two variants share. The
#-ise/-isation families are spelled out per form rather than matched by stem:
#`analysis`/`analyses` are the same word in both variants, and a `-is` stem
#would fire on them.
EN_GB = (
    "colour", "colours", "coloured", "colouring", "colourful",
    "behaviour", "behaviours", "behavioural",
    "favour", "favours", "favoured", "favourite", "favourites",
    "honour", "honours", "honoured",
    "neighbour", "neighbours", "neighbourhood",
    "centre", "centres",
    "licence", "licences", "defence", "offence", "pretence",
    "catalogue", "catalogues",
    "grey",
    "whilst", "amongst",
    "analyse", "analysed", "analysing",
    "organise", "organised", "organises", "organising",
    "organisation", "organisations",
    "recognise", "recognised", "recognises", "recognising",
    "initialise", "initialised", "initialises", "initialising",
    "normalise", "normalised", "normalises", "normalising",
    "serialise", "serialised", "serialises", "serialising",
    "synchronise", "synchronised", "synchronises", "synchronising",
    "customise", "customised", "customises", "customising",
    "prioritise", "prioritised", "prioritises", "prioritising",
    "optimise", "optimised", "optimises", "optimising",
    "minimise", "minimised", "minimises", "minimising",
    "maximise", "maximised", "maximises", "maximising",
    "utilise", "utilised", "utilises", "utilising",
    "summarise", "summarised", "summarises", "summarising",
    "categorise", "categorised", "categorises", "categorising",
    "generalise", "generalised", "generalises", "generalising",
    "apologise", "apologised", "authorise", "authorised",
    "realise", "realised", "realises", "realising",
    "visualise", "visualised", "standardise", "standardised",
    "cancelled", "cancelling",
    "labelled", "labelling",
    "modelled", "modelling",
    "travelled", "travelling",
    "fulfil", "enrol", "skilful", "instalment",
    "artefact", "artefacts",
)
EN_GB_RE = re.compile(
    r"(?<![A-Za-z])(" + "|".join(EN_GB) + r")(?![A-Za-z])", re.IGNORECASE
)

#The body sentence that makes a Status block's `fixed` a contradiction. Narrow
#on purpose: a record that mentions the live tree without claiming the fix sits
#outside it is not an offender.
UNMERGED_BODY = ("is not merged into", "is not yet landed", "is not deployed")

#What a Status block says instead of a bare done-state, while the body says the
#fix is unmerged. Two words minimum, so a prose mention of "pending" elsewhere
#in the block cannot satisfy the rule by accident.
PENDING_STATUS = ("pending deployment", "not deployed", "not merged", "not yet landed")


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def status_block(text: str) -> str | None:
    """The `Status:` line and its continuation lines, up to the first blank line."""
    lines = text.splitlines()
    start = next((i for i, line in enumerate(lines) if line.startswith("Status:")), None)
    if start is None:
        return None
    block = [lines[start]]
    for line in lines[start + 1:]:
        if not line.strip():
            break
        block.append(line)
    return "\n".join(block)


def body_after_status(text: str) -> str:
    """Everything after the Status paragraph, so the block cannot vouch for itself."""
    lines = text.splitlines()
    start = next((i for i, line in enumerate(lines) if line.startswith("Status:")), None)
    if start is None:
        return text
    end = start + 1
    while end < len(lines) and lines[end].strip():
        end += 1
    return "\n".join(lines[end:])


def check_index(squad: Path, index: Path, records: list[Path]) -> list[str]:
    if not index.is_file():
        return [f"{index.name} is missing: the folder has no index for its {len(records)} record(s)"]
    text = read(index)
    return [
        f"{path.name} is not listed in {index.name}: a reader cannot discover it from the index"
        for path in records
        if f"`{path.name}`" not in text
    ]


def check_en_us(records: list[Path]) -> list[str]:
    bad = []
    for path in records:
        for n, line in enumerate(read(path).splitlines(), start=1):
            for match in EN_GB_RE.finditer(line):
                bad.append(f"{path.name}:{n}: '{match.group(0)}' is en-GB; the repo rule is en-US")
    return bad


def check_status_agrees_with_body(records: list[Path]) -> list[str]:
    bad = []
    for path in records:
        text = read(path)
        block = status_block(text)
        if block is None:
            continue
        body = body_after_status(text)
        unmerged = next((marker for marker in UNMERGED_BODY if marker in body), None)
        if unmerged is None:
            continue
        if any(marker in block.lower() for marker in PENDING_STATUS):
            continue
        bad.append(
            f"{path.name}: the Status line reads as done while the body says the fix "
            f"'{unmerged}' the live tree: {block.splitlines()[0]}"
        )
    return bad


def main(argv: list[str]) -> int:
    repo_root = Path(argv[1]).resolve() if len(argv) > 1 else Path(__file__).resolve().parents[2]
    squad = repo_root / "docs" / "squad"
    if not squad.is_dir():
        print(f"FAIL verify_squad_docs: no such folder: {squad}")
        return 1

    records = sorted(p for p in squad.glob("*.md") if p.name != "README.md")
    if not records:
        print(f"FAIL verify_squad_docs: no records under {squad}")
        return 1

    failures = (
        check_index(squad, squad / "README.md", records)
        + check_en_us(records)
        + check_status_agrees_with_body(records)
    )
    if failures:
        print(f"FAIL verify_squad_docs: {len(failures)} problem(s) in docs/squad/")
        for failure in failures:
            print(f"  {failure}")
        return 1

    print(
        f"PASS verify_squad_docs: {len(records)} record(s) indexed in README.md, "
        "all en-US, every Status line agreeing with its own body"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
