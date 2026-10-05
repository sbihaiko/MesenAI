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

A fold claim wrapped in inline code or in a fenced block is a QUOTATION of the
bad sentence, not an assertion of it, and is skipped. Prose that asserts a fold
- "ADR-0231 was consolidated into ADR-0230" - is never backticked; the register
backticks a string precisely to show it as one. Without this rule the check
convicts the writing that documents it: the makefile comment wiring this very
script names the sentence it exists to catch, and the first version to read the
makefile failed on it. That is the same false positive the docstring above
records, where prose quoting the pattern was excused by exempting this whole
file from its own scan - a blunt fix that a quotation rule replaces with the
actual distinction. It stays strict in the direction that matters: a claim is
skipped only when the backticks pair up, so a stray delimiter leaves it checked.

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

#A citation carries a RUN of sections as often as a single one: `ADR-0138 §2/§7`,
#`§1/§4`, `§12/§6`. Matching only up to the first `§` left every later element
#unchecked, so `ADR-0138 §1/§8888` passed - and the register writes these chains
#constantly.
CITE = re.compile(r"ADR-(\d{4})\s*((?:§+\s*\d+(?:\.\d+)*)(?:\s*[/,]\s*§+\s*\d+(?:\.\d+)*)*)")
#The elements of that run, pulled apart after the fact so one citation can report
#several broken sections.
SEC = re.compile(r"§+\s*(\d+(?:\.\d+)*)")
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
        ".py", ".sh", ".json", ".yml", ".yaml", ".axaml", ".xml", ".txt", ".cfg",
        ".csproj", ".props", ".targets"}


def is_scanned(path: Path) -> bool:
    """Whether a file may carry a citation.

    The extension list alone was not enough. `makefile` has no extension and
    `UI/UI.csproj` is not C#, and between them they hold 15 live `ADR-NNNN §M`
    citations - including the ones wiring the tests. Mutating `ADR-0138 §41` to
    `§9999` inside `makefile` left this check reporting PASS, which is the worst
    shape a guard can take: silent exactly where the work is done. An
    extensionless file is read as text when it has no NUL byte, so the next
    `makefile`-like file is covered without anyone remembering to add it here.
    """
    if path.suffix.lower() in EXTS:
        return True
    if path.suffix:
        return False
    try:
        return b"\x00" not in path.read_bytes()[:4096]
    except OSError:
        return False


#Inline code and fenced blocks. Both mark a string AS a string - a specimen of
#text being shown, not a statement being made - which is the whole difference
#between quoting the bug and committing it.
INLINE_CODE = re.compile(r"`+[^`]*`+")
FENCE = re.compile(r"^\s*(?:```|~~~)")


def quoted_spans(line: str):
    """The [start, end) ranges of `line` that are inline code.

    Only paired delimiters count. An unpaired backtick leaves the remainder of
    the line unquoted, which is the strict direction to fail in: a malformed
    quotation gets checked rather than excusing everything after it.
    """
    return [(m.start(), m.end()) for m in INLINE_CODE.finditer(line)]


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
        if not path.is_file() or not is_scanned(path):
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
        in_fence = False
        for lineno, line in enumerate(lines, 1):
            if FENCE.match(line):
                in_fence = not in_fence
                continue  # the delimiter itself carries no citation
            #Only the fold pass consults this. A SECTION citation stays checked
            #inside backticks, and the asymmetry is the point: `ADR-0138 §41` is
            #a reference, and backticks around it are typographic, whereas
            #`ADR-0231, consolidated into ADR-0230` in backticks is a sentence
            #being exhibited. Reading the second as a claim convicts whoever
            #documents the rule - which is exactly what happened here.
            spans = quoted_spans(line) if not in_fence else [(0, len(line))]
            for m in CITE.finditer(line):
                have = sections.get(m.group(1))
                if have is None:
                    continue  # no such file: verify_adr_refs.py's job
                for sm in SEC.finditer(m.group(2)):
                    sec = sm.group(1)
                    #Both directions of the prefix rule, which the docstring
                    #promises and the code used to implement only one of: `§9.1`
                    #passes when the ADR has a §9, and `§9` passes when it has
                    #only §9.1.
                    if sec in have or sec.split(".")[0] in have \
                            or any(h.startswith(sec + ".") for h in have):
                        continue
                    hits[m.group(1)].append((rel, lineno, sec))

            for m in FOLD_CLAIM.finditer(line):
                if any(s <= m.start() < e for s, e in spans):
                    continue  # quoted, so exhibited rather than asserted
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
