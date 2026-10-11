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

  (a) Every `docs/squad/*.md` record is listed in `docs/squad/README.md`, as an
      index bullet. The index is how a reader discovers an operational
      constraint they were not looking for, so a record missing from it is
      invisible — and so is a record only named inside a sentence, which is
      why the rule reads the bullet and not the file's prose.
  (b) No en-GB spelling in `docs/squad/`, README.md included: the language
      rule is en-US for everything versioned, and the index is the one file
      every reader opens first. The rule set is the productive en-GB families
      (`-ise`/`-isation`, `-yse`, `-our`, the `-ae`/`-oe` and doubled-consonant
      spellings) plus the common one-off words, with the en-US words that
      share a shape listed as exceptions. It is not a dictionary, and the
      verdict names the rule set it ran rather than promising one.
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
the artifact under the en-US rule, and a fence is not an exemption from it.

Usage: python3 scripts/checks/verify_squad_docs.py [repo-root]
"""
from __future__ import annotations

import re
import sys
from pathlib import Path


#Each of these is en-GB by rule and none is a word the two variants share. It is
#the spelled-out half of rule (b); the productive families are generated below,
#because a list of the forms someone happened to think of is what let `ageing`
#through a gate whose verdict said "no en-GB spelling".
EN_GB = (
    "colour", "colours", "coloured", "colouring",
    "centre", "centres",
    "licence", "licences", "defence", "offence", "pretence",
    "catalogue", "catalogues",
    "grey", "whilst", "amongst",
    "cancelled", "cancelling",
    "labelled", "labelling",
    "modelled", "modelling",
    "travelled", "travelling",
    "quarrelled", "quarrelling", "signalled", "signalling",
    "marvelled", "marvelling", "channelled", "channelling",
    "fuelled", "fuelling", "levelled", "levelling",
    "jewelled", "tunnelled", "tunnelling", "dialled", "dialling",
    "totalled", "totalling", "counselled", "counselling",
    "ageing", "ageings",
    "fulfil", "enrol", "skilful", "skilfully", "wilful", "distil",
    "instalment", "instalments", "marvellous", "woollen",
    "artefact", "artefacts",
    "sulphur", "sulphuric", "aluminium", "jewellery", "jeweller",
    "cosy", "cosier", "cosiest", "programme", "programmes",
    "speciality", "specialities", "plough", "ploughs", "ploughed", "ploughing",
    "draught", "draughts", "kerb", "kerbs", "tyre", "tyres",
    "cheque", "cheques", "storey", "storeys", "gaol", "gaols",
    "mould", "moulds", "moulded", "moulding", "smoulder", "smouldering",
    "moustache", "moustaches", "pyjamas", "omelette", "omelettes", "liquorice",
    "practise", "practised", "practises", "practising",
    "sceptic", "sceptical", "scepticism",
    "anaemia", "anaemic", "anaesthesia", "anaesthetic", "anaesthetics",
    "diarrhoea", "encyclopaedia", "foetal", "foetus", "foetuses",
    "gynaecology", "haemoglobin", "haemorrhage", "leukaemia", "oedema",
    "oesophagus", "oestrogen", "orthopaedic", "paediatric", "palaeontology",
    "mediaeval", "manoeuvre", "manoeuvres", "manoeuvred", "manoeuvring",
)
EN_GB_RE = re.compile(
    r"(?<![A-Za-z])(" + "|".join(EN_GB) + r")(?![A-Za-z])", re.IGNORECASE
)

#The `-yse` family is en-GB by rule and needs no exceptions of the form, but
#`analyses` (and the other Greek plurals in `-lyses`) is the en-US plural of
#`analysis`, so `-yses` is left out of the pattern rather than exempted.
EN_GB_RULES = (
    #`-ise`/`-isation`: en-US writes `-ize`/`-ization`.
    (re.compile(r"(?<![A-Za-z])[A-Za-z]+(?:isationally|isational|isation|isations|ising|ised|ises|ise)(?![A-Za-z])",
                re.IGNORECASE),
     ("isationally", "isational", "isation", "isations", "ising", "ised", "ises", "ise"),
     ("adv", "advert", "an", "appra", "ar", "cha", "chast", "chem", "circumc", "compr", "comprom", "conc", "dem",
      "desp", "dev", "disgu", "enterpr", "exc", "exerc", "exorc", "expert", "franch", "improv", "inc", "lia", "mala",
      "merchand", "mort", "no", "parad", "po", "porpo", "pra", "prec", "prem", "prom", "r", "ra", "repr", "rev",
      "superv", "surm", "surpr", "telev", "tort", "treat", "turquo", "upra", "val")),
    #`-yse`: en-US writes `-yze`.
    (re.compile(r"(?<![A-Za-z])[A-Za-z]+(?:yse|ysed|ysing)(?![A-Za-z])", re.IGNORECASE),
     ("yse", "ysed", "ysing"), ()),
    #`-our` nouns: en-US writes `-or`.
    (re.compile(r"(?<![A-Za-z])[A-Za-z]+(?:ourally|ourable|ourably|ourless|ouring|oured|ourful|ours|oural|our)(?![A-Za-z])",
                re.IGNORECASE),
     ("ourally", "ourable", "ourably", "ourless", "ouring", "oured", "ourful", "ours", "oural", "our"),
     ("", "h", "y", "f", "p", "t", "s", "fl", "sc", "dev", "vel", "det", "cont", "am", "param", "troubad", "d")),
)


def en_gb_spellings(line: str) -> list[str]:
    """Every en-GB spelling on `line`: the listed words plus the generated families minus their en-US words."""
    hits = [match.group(0) for match in EN_GB_RE.finditer(line)]
    for pattern, suffixes, us_stems in EN_GB_RULES:
        for match in pattern.finditer(line):
            word = match.group(0).lower()
            for suffix in suffixes:  # longest first
                if word.endswith(suffix):
                    stem = word[: -len(suffix)]
                    #Every `-wise` adverb is en-US whatever its stem.
                    if not stem.endswith("w") and stem not in us_stems:
                        hits.append(match.group(0))
                    break
    return hits

#The body sentence that makes a Status block's `fixed` a contradiction. Narrow
#on purpose: a record that mentions the live tree without claiming the fix sits
#outside it is not an offender.
UNMERGED_BODY = ("is not merged into", "is not yet landed", "is not deployed")

#What a Status block says instead of a bare done-state, while the body says the
#fix is unmerged. Two words minimum, so a prose mention of "pending" elsewhere
#in the block cannot satisfy the rule by accident.
PENDING_STATUS = ("pending deployment", "not deployed", "not merged", "not yet landed")

#An index bullet: a list item, not the file's prose. `- `name.md` — what it is`
#is a reader's route to the record; the same name inside a sentence is not.
INDEX_BULLET = re.compile(r"^\s*[-*+]\s")


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
    bullets = [line for line in read(index).splitlines() if INDEX_BULLET.match(line)]
    return [
        f"{path.name} is not listed in {index.name}: a reader cannot discover it from the index"
        for path in records
        if not any(f"`{path.name}`" in bullet for bullet in bullets)
    ]


def check_en_us(documents: list[Path]) -> list[str]:
    bad = []
    for path in documents:
        for n, line in enumerate(read(path).splitlines(), start=1):
            for word in en_gb_spellings(line):
                bad.append(f"{path.name}:{n}: '{word}' is en-GB; the repo rule is en-US")
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

    index = squad / "README.md"
    documents = records + [index]
    failures = (
        check_index(squad, index, records)
        + check_en_us(documents)
        + check_status_agrees_with_body(records)
    )
    if failures:
        print(f"FAIL verify_squad_docs: {len(failures)} problem(s) in docs/squad/")
        for failure in failures:
            print(f"  {failure}")
        return 1

    print(
        f"PASS verify_squad_docs: {len(records)} record(s) listed as index bullets in README.md, "
        f"no en-GB spelling among the checked families and words in {len(documents)} file(s) "
        "(README.md included), every Status line agreeing with its own body"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
