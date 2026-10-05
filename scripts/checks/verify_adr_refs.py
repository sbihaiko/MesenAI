#!/usr/bin/env python3
"""ADR reference integrity — every `ADR-NNNN` cited in docs resolves to a file.

Scans `docs/**/*.md` (the ADR register itself included), `.github/**/*.md`,
`CLAUDE.md` and every `AGENTS.md` for `ADR-NNNN` references and fails when it has no
`docs/adr/NNNN-*.md` file. Motivation: commit b0b334b0 (2026-08-28)
deleted four accepted ADRs (0130/0131/0136/0137) as a side effect of an
unrelated fix and nothing noticed for four days (PRD slice D1).

Two classes of ids intentionally have no file and are tolerated in context:

- Permanently retired ids (ADR-0035: 0009-0010, 0015-0020, 0022-0032) are
  allowed only when the citing line itself says "former", "retired",
  "consolidat…", "superseded" or "deleted" (case-insensitive) — a bare
  citation of a retired id is a dangling reference like any other.
- Ids folded into a consolidating ADR on 2026-08-27 and then deleted
  (CLAUDE.md: "ADR-0122–0137 are the consolidation of the former
  ADR-0053–0119"; ADR-0049 line 6: drafts 0045/0046/0048) are allowed
  under the same context rule, and additionally anywhere inside an ADR
  that carries its own `- Consolidates:` header line — those ADRs discuss
  their sources by id throughout.

`roms/` is skipped (unversioned local content).

Usage: python3 scripts/checks/verify_adr_refs.py
Exit 0 on PASS, 1 on any dangling reference (each reported as file:line).
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
ADR_DIR = ROOT / "docs/adr"

RETIRED_IDS = (
    {f"{n:04d}" for n in range(9, 11)}
    | {f"{n:04d}" for n in range(15, 21)}
    | {f"{n:04d}" for n in range(22, 33)}
)
#Ids the 2026-08-27 consolidation absorbed and deleted, kept as a fallback for
#those that no surviving ADR names in its own header line.
CONSOLIDATED_IDS = {f"{n:04d}" for n in range(53, 120)} | {"0045", "0046", "0048"}
RETIRED_CONTEXT = re.compile(r"former|retired|consolidat|superseded|deleted", re.IGNORECASE)
CONSOLIDATES_HEADER = re.compile(r"^- Consolidates:(.*)$", re.MULTILINE)
ADR_REF = re.compile(r"ADR-(\d{4})")
SKIP_PARTS = {"roms"}


def declared_consolidated_ids():
    """The ids the survivors say they absorbed, read from their own headers.

    Derived rather than listed, so the next consolidation needs no edit here -
    only the "- Consolidates:" line the survivor already has to carry. Without
    this, deleting an absorbed ADR would turn every citation of it into a
    dangling reference and the register could never be consolidated again.
    """
    declared = {}
    for p in ADR_DIR.glob("*.md"):
        try:
            text = p.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        for m in CONSOLIDATES_HEADER.finditer(text):
            #Only the ids before the first parenthesis are sources this ADR
            #absorbed. Everything after it is cross-reference - "the sln half is
            #ADR-0122", "rejected - see ADR-0139" - and those ADRs are alive, so
            #reading them as absorbed would fail on files that must stay.
            sources = m.group(1).split("(", 1)[0]
            for num in ADR_REF.findall(sources):
                declared.setdefault(num, set()).add(p.name[:4])
    return declared


def known_ids():
    ids = set()
    for p in ADR_DIR.glob("*.md"):
        m = re.match(r"(\d{4})-", p.name)
        if m:
            ids.add(m.group(1))
    return ids


def candidate_files():
    seen = set()
    for pattern in ("docs/**/*.md", ".github/**/*.md",
                    "CLAUDE.md", "**/AGENTS.md"):
        for p in ROOT.glob(pattern):
            rel = p.relative_to(ROOT).as_posix()
            if any(rel == s or rel.startswith(s + "/") for s in SKIP_PARTS):
                continue
            if p.is_file() and rel not in seen:
                seen.add(rel)
                yield p, rel


def stub_target(text: str):
    """The id a stub points at, or None when the file is not a stub.

    A consolidation has two legitimate end states for an absorbed ADR, and the
    register uses both: the file is DELETED (what happened to 0053-0119 on
    2026-08-27), or it is kept as a one-line tombstone naming its successor -
    which is what a fold of hand-written accepted ADRs does, so that a reader
    who types the old id or follows an old link still lands somewhere that says
    where the decision went.

    Without this, the two instructions pull against each other: the survivor is
    told to carry a `- Consolidates:` line AND the caller is told to leave a
    stub, and the contradiction check below then fails on the pair. Requiring
    the tombstone to name one of the declarers is what keeps the check
    meaningful - a file that is merely small, or that is superseded by some
    unrelated ADR, is still a failure.
    """
    if len(text) > 1200:
        return None
    m = re.search(r"^- Superseded by:\s*ADR-(\d{4})\s*$", text, re.MULTILINE)
    return m.group(1) if m else None


def scan(failures):
    ids = known_ids()
    if not ids:
        failures.append(f"no ADR files found under {ADR_DIR}")
        return
    declared = declared_consolidated_ids()
    #A declared-consolidated id that still has a file is fine when that file is a
    #tombstone naming its declarer: the decision moved, the id still resolves.
    #Any other live file under an absorbed id is the half-finished consolidation
    #this check exists to catch - the survivor claims the decision while the old
    #text sits there unchanged and readers follow it instead.
    for num in sorted(set(declared) & ids):
        declarers = declared[num]
        path = next(iter(ADR_DIR.glob(f"{num}-*.md")))
        target = stub_target(path.read_text(encoding="utf-8"))
        if target in declarers:
            continue
        if target:
            failures.append(
                f"docs/adr/{num}-*.md is a stub for ADR-{target}, but "
                f"{', '.join(sorted(declarers))} declares it consolidated. "
                "A tombstone must point at the ADR that absorbed it."
            )
        else:
            failures.append(
                f"docs/adr/{num}-*.md still holds its full text, but "
                f"{', '.join(sorted(declarers))} declares it consolidated. "
                "Either fold it into a tombstone naming that ADR (Status "
                "superseded / '- Superseded by: ADR-NNNN'), delete it, or drop "
                "it from the '- Consolidates:' line."
            )
    consolidated = CONSOLIDATED_IDS | set(declared)
    for path, rel in sorted(candidate_files(), key=lambda t: t[1]):
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        consolidating_adr = rel.startswith("docs/adr/") and bool(
            CONSOLIDATES_HEADER.search(text))
        for lineno, line in enumerate(text.splitlines(), 1):
            for m in ADR_REF.finditer(line):
                num = m.group(1)
                if num in ids:
                    continue
                if num in RETIRED_IDS or num in consolidated:
                    if RETIRED_CONTEXT.search(line):
                        continue
                    if num in consolidated and consolidating_adr:
                        continue
                    kind = "retired id (ADR-0035)" if num in RETIRED_IDS else \
                        "consolidated-and-deleted id"
                    failures.append(
                        f"{rel}:{lineno}: ADR-{num} is a {kind} cited without "
                        "former/retired/consolidated/superseded/deleted context")
                    continue
                failures.append(
                    f"{rel}:{lineno}: ADR-{num} has no docs/adr/{num}-*.md")


def main():
    failures = []
    scan(failures)
    if failures:
        print("FAIL verify_adr_refs:")
        for f in failures:
            print(f"  {f}")
        return 1
    print("PASS verify_adr_refs: every cited ADR-NNNN resolves to docs/adr/")
    return 0


if __name__ == "__main__":
    sys.exit(main())
