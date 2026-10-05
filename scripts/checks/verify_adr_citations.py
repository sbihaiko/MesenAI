#!/usr/bin/env python3
"""Every way this repo cites an ADR is true of the register as it stands.

`verify_adr_refs.py` checks that the ID resolves. Two things it is deliberately
blind to are checked here, because both let a consolidation pass silently:

1. THE SECTION. Fold an ADR into a survivor and its `§1` can end up either
   pointing at nothing or - worse, because nothing reports it - pointing at
   whatever the survivor happened to put at `§1`, a different decision entirely.
   When ADR-0196 was folded into ADR-0189, 68 sites across `Core/`, `scripts/`
   and docs cited `ADR-0196 §1..§4`; the id resolved, the sections did not exist,
   and the citations read as if they still worked. Section numbers are part of an
   ADR's interface, which is why the register's rule is that a cited number is
   frozen.

2. THE FOLD CLAIM. `(ADR-0231, consolidated into ADR-0230)` was written into the
   README, the toolchain comparison, `scripts/AGENTS.md`, the manual and four
   validation records when 0231 was briefly folded into 0230. The fold was then
   undone - 0231's sections are cited from live code - and every one of those 24
   sentences stayed behind, telling readers that a live ADR had been absorbed.
   Nothing failed: 0231 has a file, so the id resolves either way. A claim about
   the register is a citation of it, and it is checked here.

Two numbering conventions are in use and both are recognised:

  * a heading carrying the number   `### 5. Spec amendment (MEP-v1 §6)`
  * a numbered paragraph             `**2. Two keys, two jobs.**`
    bold or not, at the start of a line - ADR-0138's 56 Clarifications and
    ADR-0177's Decision are plain, ADR-0206's six Decision items are bold, and
    the first version of this check recognised neither the bold form nor the
    plain one, reporting 21 citations of ADR-0206/0157/0165 as broken when all
    three number their sections perfectly well. It then grew an allow-list to
    excuse them, which is how a scanner's own blind spot turns into a bug filed
    against three innocent ADRs. Check the scanner first.

A section citation is satisfied by the number or by its top-level prefix, so
`§9.1` passes when the ADR has a §9 and `§9` passes when it has §9.1; the
register uses both directions and neither is a mistake. A file with no numbered
sections at all fails every `§`-citation against it, which is exactly what a
tombstone creates.

A fold claim's subject is the LAST `ADR-NNNN` before the phrase on that line -
reading it that way is what tells `(F14.9, ADR-0230) - ... (ADR-0231,
consolidated into ADR-0230)` apart from a claim about 0230. The claim passes when
the subject's own file is a tombstone naming the claimed target. An id with no
file is skipped in both passes: deleted and retired ids are
`verify_adr_refs.py`'s business, not this one's.

Usage: python3 scripts/checks/verify_adr_citations.py
Exit 0 on PASS, 1 on any citation or fold claim the register does not support.
"""
import re
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
ADR_DIR = ROOT / "docs/adr"
if not ADR_DIR.is_dir():
    sys.exit(f"not a MesenAI checkout: {ROOT} has no docs/adr")

CITE = re.compile(r"ADR-(\d{4})\s*§+\s*(\d+(?:\.\d+)*)")
ADR_REF = re.compile(r"ADR-(\d{4})")
#The register's own vocabulary for "this ADR is now part of that one". All four
#verbs are accepted because they mean the same thing to a reader, and a guard
#that knew only the one this wave happened to use would go quiet on the next.
FOLD_CLAIM = re.compile(r"(?:consolidated|absorbed|folded|merged) into\s+ADR-(\d{4})")

#No allow-list, deliberately. An earlier revision carried one for 21 citations
#of ADR-0206/0157/0165 that looked broken; they were this file's own false
#positives (those three number their sections in bold, which the scanner above
#did not read), and excusing them hid the bug instead of fixing it. A citation
#that does not resolve is now either a real break or a new blind spot here, and
#both should redden the gate.

#Directories never entered. `roms/` is unversioned local content; `out/` is the
#app's own build output, which carries a COPY of scripts/ and therefore a second
#copy of every citation - reporting it would double every finding.
SKIP_PARTS = {"roms", "out", "obj", "bin", "build", "node_modules", ".git",
              "runs", "runs-archive", "player-renders", "graphify-out",
              ".venv", "venv", ".claude"}

#This file is skipped by its own scan, and not for tidiness: `CITE` is spelled
#out in the prose above, so a scanner that reads its own source reports the
#example in its own docstring as a site. Every other check in scripts/checks/ is
#scanned; only this one is its own input.
SELF = Path(__file__).resolve()
EXTS = {".md", ".cs", ".cpp", ".h", ".hpp", ".mm", ".m", ".cc", ".cxx", ".c",
        ".py", ".sh", ".json", ".yml", ".yaml", ".axaml", ".xml", ".txt", ".cfg"}


def sections_of(text: str):
    """The section numbers a file offers, by every convention the register uses.

    Over-accepting is the safe direction: a rare false negative costs one
    unreported citation, while a false positive sends someone to renumber a
    section that was never broken.
    """
    nums = set()
    for m in re.finditer(r"^#{2,4}\s+(\d+(?:\.\d+)*)[.)]?\s+\S", text, re.MULTILINE):
        nums.add(m.group(1).rstrip("."))
    for m in re.finditer(r"^\s{0,3}\*{0,2}(\d+)\.\s+\S", text, re.MULTILINE):
        nums.add(m.group(1))
    return nums


def candidate_files():
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in EXTS:
            continue
        if path.resolve() == SELF:
            continue  # see SELF
        rel = path.relative_to(ROOT).as_posix()
        if any(rel == s or rel.startswith(s + "/") for s in SKIP_PARTS):
            continue
        yield path, rel


def scan(failures):
    sections, tombstones = {}, {}
    for p in ADR_DIR.glob("[0-9][0-9][0-9][0-9]-*.md"):
        text = p.read_text(encoding="utf-8", errors="replace")
        sections[p.name[:4]] = sections_of(text)
        m = re.search(r"^- Superseded by:\s*ADR-(\d{4})\s*$", text, re.MULTILINE)
        if m and len(text) <= 1200:
            tombstones[p.name[:4]] = m.group(1)

    hits = defaultdict(list)
    for path, rel in candidate_files():
        try:
            lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        except OSError:
            continue
        for lineno, line in enumerate(lines, 1):
            for m in CITE.finditer(line):
                num, sec = m.group(1), m.group(2)
                have = sections.get(num)
                if have is None:
                    continue  # no such file: verify_adr_refs.py's job
                if sec in have or sec.split(".")[0] in have:
                    continue
                hits[num].append((rel, lineno, sec))

            for m in FOLD_CLAIM.finditer(line):
                target = m.group(1)
                refs = list(ADR_REF.finditer(line[:m.start()]))
                if not refs:
                    continue  # "the folds were consolidated into ADR-0161":
                    #             no subject on this line, nothing to check
                last = refs[-1]
                subject = last.group(1)
                #`ADR-0139-0148 (folded into ADR-0137's Clarifications)` is a RANGE
                #of auto-minted stubs that were deleted after folding (ADR-0035
                #tells that story). Only the head carries the `ADR-` prefix, so
                #reading the last match makes 0139 - bound since to a live ADR -
                #look like the subject. A dash and four more digits after the id
                #is the range, and a range's members have no files: skip it, the
                #survivor's own `- Consolidates:` header owns that claim.
                if re.match(r"\s*[-–—]\s*\d{4}", line[last.end():m.start()]):
                    continue
                if subject == target or subject not in sections:
                    # A self-claim says nothing; an id with no file is the
                    # deleted-consolidation case, owned by the survivor's own
                    # `- Consolidates:` header and checked by verify_adr_refs.
                    continue
                if tombstones.get(subject) == target:
                    continue
                if subject in tombstones:
                    failures.append(
                        f"{rel}:{lineno}: says ADR-{subject} was consolidated into "
                        f"ADR-{target}, but ADR-{subject}'s tombstone points at "
                        f"ADR-{tombstones[subject]}")
                else:
                    failures.append(
                        f"{rel}:{lineno}: says ADR-{subject} was consolidated into "
                        f"ADR-{target}, but ADR-{subject} is a live ADR - the fold "
                        "was undone or never happened. Drop the claim.")

    for num in sorted(hits, key=lambda k: -len(hits[k])):
        sites = hits[num]
        #Two causes, and the difference decides the fix: a tombstone means the
        #citation should have been repointed when the ADR was absorbed, while an
        #ADR that never numbered its sections means the citers assumed a
        #structure the file does not have.
        what = ("a tombstone pointing at ADR-" + tombstones[num] +
                ", so the section moved and this citation did not follow it") \
            if num in tombstones else "no numbered sections at all"

        failures.append(
            f"ADR-{num} is cited by {len(sites)} section(s) it does not have - {what}: "
            + ", ".join(f"§{s}" for s in sorted({h[2] for h in sites})[:8]))
        for rel, lineno, sec in sites[:3]:
            failures.append(f"    {rel}:{lineno} cites ADR-{num} §{sec}")


def main():
    failures = []
    scan(failures)
    if failures:
        print("FAIL verify_adr_citations:")
        for f in failures:
            print(f"  {f}")
        return 1
    print("PASS verify_adr_citations: every cited ADR section and fold claim holds")
    return 0


if __name__ == "__main__":
    sys.exit(main())
