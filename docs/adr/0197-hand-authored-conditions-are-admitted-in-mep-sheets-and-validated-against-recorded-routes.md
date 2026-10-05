# ADR-0197: Hand-authored conditions are admitted in MEP sheets and validated against the recorded routes; the toolchain still never emits the refused three

- Status: accepted (2026-09-16) — §3 decided as option (b), the fixed `$0000`–`$07FF` window; user's go-ahead quoted verbatim: "confirmo". §1/§2 shipped 2026-09-19 as PRD Part A §4, Phase 12, F12.6a (`spriteNearby` reported `not evaluable` for want of the dump change F12.6b made). Amended 2026-09-22 (ADR-0222 option A, F12.14): the OAM stream is self-describing and lint evaluates `spriteNearby`, `spriteAtPosition`, `positionCheckX/Y`, `originPositionCheckX/Y` and `memoryCheck`.
- Date: 2026-09-16
- Related: ADR-0189 §4 (the three refused condition types), ADR-0190 (`tileNearby` auto-attached), ADR-0183 §3 (evidence vs inference), ADR-0157 (frame-counted headless input), ADR-0185 (movie driver), ADR-0186 (CDL map), MEP-v1 §5, `docs/hd-pack-toolchain-comparison.md`
- Supersedes / amends: ADR-0189 §4 — narrows its scope to *emission*; the refusal to auto-emit `frameRange`, `tileAtPosition` and `memoryCheckConstant` stands unchanged

## Context

ADR-0189 §4 refuses to **emit** three types for reasons that still hold: `frameRange` is tested against the emulator's global frame counter and the recording knows only a period, not a phase; `tileAtPosition` would spend the screen-anchor evidence twice; `memoryCheckConstant` cannot be observed because the builder retains no memory stream, so any address would be chosen, not seen. All 13 types are available to a human writing `hires.txt` (the most prolific community author uses `memoryCheckConstant` 13 647 times, almost all `<background>` gates), but our guide forbids hand-editing it and the regenerated sheets have no place for a condition a human wrote. The recorder retains, per retained frame, the grid and the OAM stream with a `FrameNumber` (`HdPackBuilder::OnFrameEnd`), for every route (`input=`, `movie=`, `state=`) — enough to **evaluate** `frameRange`, `tileAtPosition`, `tileNearby`/`spriteNearby` after the fact and report where it holds, fails, or fires on a tile the author did not mean; not enough for `memoryCheckConstant`. Non-goals: no `ram_probe` or address-picking tool, no auto-emission of the refused three, no condition editor GUI.

## Decision

### 1. A sheet may carry a hand-authored condition

`mep_build.py` sheets gain an optional `conditions` block — a named condition in the loader's syntax (`[name]` definition, `<condition>` fields) attached to cells by name; `build` serializes it as the loader reads it, keeps the bare twin (ADR-0189 §3) for every conditioned cell, and marks it `authored: true` in the sidecar. Nothing in the toolchain generates one.

### 2. Lint validates every authored condition against the recordings

`mep_lint.py` gains `--routes <recording dir>...`, evaluating each authored condition on every retained frame of every recording and reporting where it held, failed, and held on a key the author did not condition; a `frameRange` is reported with the phase offset that would make it hold per route. A report, not a gate.

### 3. The recorder retains the fixed `$0000`–`$07FF` window — decided

Decided 2026-09-16: option (b). The recorder retains the 2 KB of internal RAM (`$0000`–`$07FF`, the range ADR-0184 already bounds for RAM cheats with `AAAA < 0x0800`) on every retained frame, route- and pack-independent; any recording made after F12.6b can then validate any authored `memoryCheckConstant` in that range. Rejected: (a) retaining only `HdPackData::WatchedMemoryAddresses` (circular). Limits, stated by lint, never hidden: an address outside the window — WRAM `$6000`–`$7FFF`, PRG `$8000`+, mapper registers, PPU (`$10000`+ in the condition syntax) — reports `not evaluable: address outside retained window`; a pre-F12.6b recording reports `not evaluable: no memory stream in recording`; neither is ever a pass. Cost is 2 KB per retained frame, depending on the cadence of `HdPackBuilder::OnFrameEnd`; F12.6b measures it on a 60 s Contra route into `docs/validation/`. Widening the window (WRAM first) is an amendment to this section.

#### Amended 2026-09-22 (ADR-0222 option A, shipped as F12.14)

The sprite bullets this section's Limits once implied — `spriteNearby` and `spriteAtPosition` "not evaluable: the sprite stream carries vocabulary indexes", `positionCheck*` "the sprite's own position is in the sprite stream, not the grid", and `memoryCheck` "scoped out" — are retired. The retained OAM stream (`MESEN_OAM_STREAM_DUMP`) is self-describing like the grid: `K`/`P` intern lines, then `<shape>,<x>,<y>,<pal>` per sprite, `OamEntry` carrying the interned palette id. Lint reads `oam.txt` beside a route's `grid.txt` and reports a verdict for the five sprite-side types; `memoryCheck` reads both operands off the `M` line. Remaining limits live in `scripts/mep_conditions.py`'s docstring and are reported per route, never hidden: no `oam.txt`, a pre-F12.14 dump (no `K` lines), a background key whose grid and OAM streams disagree on played frames, the baked OAM flips (ADR-0178) that hide the mirrored offset sign, and the per-pixel `positionCheck*` evaluated as "every pixel of the tile". Measured: `docs/validation/f12.14-oam-dump-self-describing-2026-09-22.md`.

## Consequences

- ADR-0189 §4 keeps its three refusals; this ADR gives the hand author the expressive power those refusals withheld, with a measurement the upstream hand-author lacks — "Conditions deliberately refused" is re-measured on that basis.
- Authored conditions are the second non-derived thing in a sheet (the first is the artist's pixels): `--verify` round trips must preserve them byte for byte and every kit generator pass them through untouched.
- Option (b) changes the recorder's dump format; `make capture-tool` and the viewer's wire format are affected, and ADR-0169 readers must skip the new block.
- Lint over routes is only as good as the route set: a condition that holds on every recorded frame can still fail on a frame nobody recorded.

## Record

- 2026-09-16 — accepted; §3 option (b), verbatim: "confirmo". §1/§2 shipped as F12.6a, §3 as F12.6b (the `M` line per retained frame; `mep_lint --routes` evaluating `memoryCheckConstant`). Measured on a 60 s Contra route (607 retained frames for 3 597 played): +2 488 093 B, +6.42 %, no wall-clock cost outside noise (`docs/validation/f12.6b-recorder-retains-internal-ram-2026-09-19.md`).
- 2026-09-22 — ADR-0222 option A shipped as F12.14; §3's sprite limits retired (`docs/validation/f12.14-oam-dump-self-describing-2026-09-22.md`).
