# ADR-0035: Spec/AC template lessons — name the mirroring axes, verify enumerated deliverables with multi-symbol checks, never reuse ADR ids

- Status: accepted
- Date: 2026-08-24

## Context
Raised during auditor-b (consolidates audit-round ADR-0022, ADR-0031 and ADR-0032, ids from the reused 0022–0032 range): three F1-audit process findings. (1) Issue 1 was noise — `RecordApi.cs` already holds all six DllImport bindings — but exposed a real AC-template weakness: when a deliverable enumerates N symbols in one file, an AC that greps for one of them (AC-13 greps only `MidiRecord`) verifies the file exists, not that the deliverable is complete. (2) Four of the run's five issues share one root cause: MidiExporter was specified as 'mirror VgmExporter' and the mirroring applied uniformly, without asking on which axes a MIDI score and a register log behave alike — ownership and the Start/Stop/IsRecording surface mirrored correctly, while the activation contract (ADR-0014), the I/O strategy (ADR-0033) and the timebase (ADR-0013) each resurfaced later as separate issues. (3) `adr.js` assigns ids as max-existing+1, so deleting consolidated raw findings freed ids 0022–0032 and the next round reissued them, corrupting every "consolidates former ADR-XXXX" reference in ADR-0011 through ADR-0021.

## Decision
Retrofit into the templates, not into this run: (1) when a deliverable enumerates N symbols in one file, the AC uses a count-based or multi-symbol check instead of a single representative grep; (2) when a spec names a sibling class as the template, it names the axes to copy and the axes to decide independently; (3) when consolidating ADRs, replacement ADRs take fresh ids beyond the highest id ever used (never delete-then-create, which reissues the freed ids), and consolidated files are deleted only after the replacements exist. Complements ADR-0012: a structural finding recurring across consecutive task reviews is promoted to a spec change, not re-litigated downstream.

Historical note (accepted damage): ids 0009–0010, 0015–0020 and 0022–0032 are permanently retired — each names one or two deleted findings — and must never be reissued; the surviving "consolidates former ADR-XXXX" references in ADR-0011 through ADR-0021 are annotated with the round they belong to.

Historical note 2 (2026-09-01, the second and last exception): the same max-existing+1 behavior struck again after the 0122–0137 consolidation of 2026-08-27 — dev-squad runs auto-minted review stubs into the freed range three times (H1 `45092f2ebec4` as ADR-0139–0148, folded into ADR-0137's "Clarifications" via its "Consolidates:" line; F6.2 `9967a42e92f1` as ADR-0139–0147, folded into ADR-0138 "Clarifications"; F6.2a `4f0d742630e5` as ADR-0139–0144, folded into ADR-0138 Clarification §15), each deleted after folding, freeing the ids once more. Those ids are now **bound for good** to the live, accepted ADRs that occupy them: ADR-0139 content_id, ADR-0140 pack_id, ADR-0141 catalog slot per pack_id, ADR-0142 crossfade, ADR-0143 catalog slot per game, ADR-0144 OGG tracks, ADR-0145 optimistic matching, ADR-0146 auto-load, ADR-0147 sibling folders, ADR-0148 de-list audio-only NEA packs. Older text that says "ADR-0139–0148" (or a sub-range) while meaning review stubs refers to the deleted files in git history, not to these ADRs. Rule going forward: never delete an ADR file while a later run can still mint — fold first, then delete only once the replacement ids are taken by real ADRs (or leave a `superseded` tombstone so the id stays occupied); an id that has ever appeared in a "Consolidates:"/"folded" line is retired and not reissued.

## Consequences
Single-grep ACs stop passing on incomplete deliverables; sibling-mirroring specs stop generating one late-reported issue per genuinely-different axis; ADR cross-references stay unambiguous across consolidation rounds.

## Alternatives
Fix only this run's ACs and spec text: the next run regenerates the same three failure shapes from the same templates.

## Clarifications (2026-09-06)

- Ids 0139–0148 were reused once before this rule: the H1 review findings auto-minted under those ids (ADR-0137's "Consolidates:" line, ADR-0124's Clarifications) were deleted on 2026-08-28 and the ids reissued to the hand-written ADR-0139–0148 that exist today. History, not precedent — renumbering is forbidden, so the current files keep their ids and no further reuse is allowed.
