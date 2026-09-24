# ADR-0227: A Phase 10 subject is named from the artist kit, as the set of kit ids the player names

- Status: **accepted 2026-09-23**. Reflected in the PRD's Phase 10 "Idea
  under test" paragraph. Phase 10 has no implementation slice yet, so
  nothing ships with this. User's decision, verbatim: *"Pelo nome, no kit"*.
- Date: 2026-09-23
- Related: PRD Part A Phase 10 (skin studio), ADR-0169 §4 (the live viewer
  is a developer tool, amended 2026-09-23), ADR-0183 (the artist kit and its
  Figures surface), ADR-0209 (a figure is named and handed over, Q2),
  ADR-0225 (pixel-faithful figures), ADR-0226 (cycle rows survive flicker)
- Supersedes / amends: the entry point in the PRD's Phase 10 "Idea under
  test" ("From the live viewer (ADR-0169) the player points at a subject")

## Context

Phase 10's idea under test starts with the player pointing at a subject in
the live viewer and asking for a restyle of the **whole subject**, meaning
every pose the recorder saw. On 2026-09-23 ADR-0169 §4 took the viewer off
the menu the player sees and kept it as a developer and diagnostic tool.
That left the phase without an entry point. The user picked "by name, from
the kit" over pointing in some other surface.

The kit has no notion of a character. `scripts/artist_kit.py`
(`KitBuilder.build`) lays out one `usrNNN` grid per cycle, then one per
sequence that no cycle covers, then "rest" grids. A rest grid bins every
unordered pose by figure box, so one grid can hold unrelated figures.
Neither a `usrNNN` row nor a pose or cycle id in `sheets/poses.json`
therefore denotes a whole character: a character with two cycles and some
loose poses spans several grids, and a rest grid can mix characters.

Non-goals: this ADR does not define automatic character grouping, it does
not pick an image model or a key flow (S10.b), and it does not reopen
ADR-0169 §4.

## Decision

1. **The entry point is the kit, by name.** The player picks a subject by
   naming entries from the artist kit, not by pointing in the live viewer
   or any other running surface.
2. **A subject is the set of kit ids the player names.** Each id is one of:
   a figure grid id (`usrNNN`), a cycle or sequence id, or a pose id from
   `sheets/poses.json`. A pose named directly counts once, even when a named
   grid or cycle also contains it. A rest grid counts only as the poses the
   player names from it, never as the whole grid, because a rest grid is a
   box-size bin and not a subject.
3. **The toolchain infers no membership.** Phase 10's "whole subject" means
   every pose in the named set. If the player leaves out a pose of the
   character, it stays out, and the tool says which poses of each named
   grid it used.
4. **Automatic grouping stays open.** Grouping poses into a character by
   shared tiles, palette, track continuity or anything else is not decided
   here. If a Phase 10 spike shows that naming by hand is the bottleneck,
   that finding opens its own ADR.

## Consequences

- The Phase 10 promise gets narrower in exchange for being implementable
  today. "Every pose the recorder saw" becomes "every pose the player
  named". Leaving poses out is the player's miss, never the tool's, and the
  per-grid report in §3 makes it visible.
- The kit's ids become an interface for Phase 10. Renumbering `usrNNN`
  between kit regenerations invalidates any named set saved against the old
  kit, so a saved set must record which kit it names (`kit.json`).
- S10.b is unaffected. It measures layout fidelity on a contact sheet and
  does not depend on how the subject is chosen.
