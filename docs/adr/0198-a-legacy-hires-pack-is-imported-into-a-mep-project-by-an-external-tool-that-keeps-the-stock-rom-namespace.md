# ADR-0198: A legacy `hires.txt` pack is imported into a MEP project by an external tool that keeps the stock-ROM namespace; the CHR RAM→ROM bridge is a separate question

- Status: proposed (2026-09-16) — open question in §3: whether to bridge
  packs keyed against an IPS-patched ROM at all. Implementing slice, once
  accepted: PRD Part A §4, Phase 12, F12.7
- Date: 2026-09-16
- Related: ADR-0005 (MEP textures is an envelope over `hires.txt`), ADR-0145
  (optimistic matching; IPS does not relax), ADR-0003/ADR-0039 (No-Intro
  SHA1), ADR-0165 (external stdlib tools), ADR-0183 §4 (round-trip), MEP-v1
  §5, issue #225, `docs/validation/metroid-artist-workflow-evidence.md` §2,
  `docs/hd-pack-toolchain-comparison.md`

## Context

Every accepted community pack is a plain HD Mesen pack: `hires.txt` plus
PNGs, sometimes OGG, sometimes an IPS. The fork loads them unchanged
(ADR-0005), but none of the fork's tools can *edit* one: `mep_build.py`
regenerates `hires.txt` from sheets, and a pack that has no sheets has no
way in. An author who wants coverage measurement, the kit surfaces or lint
on an existing pack must start from a recording and repaint.

`mep_build.py` already contains the inverse direction in part: its splitter
parses a `hires.txt` into header tags and ordered tile lines with their
condition prefixes, because the recorder's output is itself a `hires.txt`.
What is missing is the step from parsed rules to sheets: cutting the
referenced PNG regions into cells, naming them from the data (ADR-0183 §5),
preserving conditions as authored (ADR-0197), and round-tripping.

The hard case is not parsing. Seven of the fifteen accepted packs ship an IPS
that converts CHR RAM into CHR ROM, and their `<tile>` keys are bank indices
of the **patched** ROM. Against the stock ROM the recorder writes 16-byte
patterns; the namespaces are disjoint (issue #225, closed as a diagnostic
fix). ADR-0145 decided IPS does not relax, and the comparison table records
this as a row upstream wins because upstream defined the namespace.

Non-goals: no Core change; no runtime dual-namespace lookup; no relaxing of
ADR-0145; no promise of "zero manual intervention" on a pack whose keys the
stock ROM cannot produce.

## Decision

### 1. `mep_import.py`, external, stdlib, stock-ROM only

A new script reads a legacy pack directory and writes a MEP project: sheets
cut from the pack's PNGs at the rules' coordinates, one cell per
`(tileData, palette)` key, conditions preserved verbatim as authored
(ADR-0197 §1), `<background>`/`<addition>`/`<fallback>`/`<bgm>`/`<sfx>`
carried through, the `<supportedRom>` hashes mapped onto the project's
`supportedRom`. The acceptance test is ADR-0183 §4's: `mep_build.py build`
on the imported project regenerates a `hires.txt` whose rule set, keyed by
`(tileData, palette, condition)`, equals the input's, and every referenced
pixel is identical. Rule order may differ; nothing else may.

### 2. Bounded first input

The first slice imports packs **without** an IPS (Ninja Gaiden, Bomberman,
Ice Climber, Little Nemo, Pac-Man, Donkey Kong, and the hand-made Contra80s
reference). A pack with `<patch>` is refused with a message naming this ADR
until §3 is decided.

### 3. The CHR RAM→ROM bridge — open

For a pack keyed against a patched ROM, three shapes, for the human to pick:

- (a) **import against the patched ROM.** The project's `supportedRom` is
  the patched hash and the IPS is carried as a project asset; the fork
  matches it exactly (ADR-0145 (3)) as it does today. Cheapest; buys the
  author sheets and lint, and nothing about the stock ROM.
- (b) **translate keys by observation.** Record the stock ROM on the same
  routes, record the patched ROM on the same routes, pair frames by
  `FrameNumber`, and map each patched bank index to the stock 16-byte
  pattern seen at the same grid or OAM slot. Only pairs observed on every
  co-occurrence are translated; the rest stay refused and are listed. This
  is observation, not interpretation, and stays inside ADR-0183 §3.
- (c) **do not bridge.** The bet on the stock ROM (ADR-0003, ADR-0184) is
  the opposite of that author's, and the project says so.

## Consequences

- With §1 alone, the comparison row "Interop with community packs" is
  partly re-measured: an existing plain pack becomes editable, measurable and
  lintable here, which upstream cannot do. The IPS half of the row is decided
  by §3, not by this slice.
- The importer is the first tool that reads *foreign* `hires.txt`: every
  loader constraint (`checkConstraint` in `HdPackLoader`) becomes an import
  error with the line cited, and unknown tags are refused, never dropped.
- Option (b) needs two recordings per route and a sidecar of the pairing
  evidence, and produces a partially translated pack by design; the
  untranslated remainder must render as the stock tile, never as wrong art.
- A pack's license is not changed by importing it. The tool writes the
  project next to the user's copy and never into any repository (Part A §1).
