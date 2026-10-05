# ADR-0211: A declared `<supportedRom>` that contradicts the loaded ROM refuses the install, instead of being overwritten by it

- Status: accepted (2026-09-19) — implemented the same turn it was accepted,
  under CLAUDE.md's exception: the change ships with unit tests covering the
  decision (`UI.Tests/CommunityPacks/SupportedRomGuardTests.cs`), and the
  go-ahead is quoted verbatim — *"agora resolve a ADR-0211, o bug #314"*.
  Shipped 2026-09-19; amended the same day by §5 and §6 below, which the
  measurement forced.
- Date: 2026-09-18 (amended 2026-09-19)
- Related: ADR-0145 (optimistic matcher), ADR-0146 (auto-install, no consent gate), ADR-0147 (`mep/` beside the ROM), ADR-0138 §41, MEP-v1 §2.1, issue #314

## Context

Issue #314: `<roms>/Bomberman (1985) (Hudson Soft)/mep/` held the Contra 80s pack (233 files, 171 MB, a `hires.txt` byte-identical to Contra's, declaring `<supportedRom>C9EA66BB7CB30AD5…`): Bomberman rendered Contra's art on every load, silently, for ten days. The install was wrong, but why it was *silent* is the separate defect worth deciding — `CommunityPackInstallCoordinator.BuildLegacyPackJson` writes `targets[0].sha1` from `EmuApi.GetMepRomSha1()` (the **loaded** ROM) while the extracted artifact already declares its own `<supportedRom>`; on disagreement the stamp overwrites the only record of it and manufactures a pack that matches cleanly forever after, and the load path does not re-check. Three artifacts (`pack.json`, `.mep-install.json`, the install registry entry) agreed with each other and all three were wrong.

This is not a case ADR-0145's optimism covers: that optimism is about the **absence** of evidence (a catalog entry with `rom: {}`, or a dump whose hash nobody recorded, installs anyway and the health signal sorts it out), whereas a `<supportedRom>` naming a different ROM is the **presence** of contrary evidence, asserted by the pack itself. Non-goal: this adds no consent dialog (ADR-0146 forbids it), does not change the matcher, and does not touch packs that declare no `<supportedRom>` — four catalog packs legitimately have none and must keep installing.

## Decision

On a legacy HD pack install (`InstallHdLegacy`), after extraction and before `pack.json` is written:

1. Read `<supportedRom>` from the extracted `textures/hires.txt`.
2. **Absent, empty, or unparseable → proceed unchanged** (optimism preserved exactly where it applies today).
3. **Present and equal to the loaded ROM → proceed**, writing the declared value into `targets[0].sha1` rather than the loaded ROM's, so the stamp records the pack's own claim.
4. **Present and different → refuse.** Return `CommunityPackInstallOutcome.Failed`, leave no `mep/` behind, and log: `[CommunityPackInstall] refused: pack declares supportedRom <X>, loaded ROM is <Y>`.

The comparison is case-insensitive and between the **full-file** SHA-1 on both sides: `<supportedRom>` is the hash of the whole file including the iNES header, whereas `EmuApi.GetMepRomSha1()` returns the No-Intro **body** hash (ADR-0003). Comparing these two different hashes of the same ROM directly would refuse every correct install, so the install path needs the full-file hash exposed alongside the body hash; whichever way that is plumbed, the two must never be compared across forms.

5. **Amendment, 2026-09-19 — a declaration equal to the loaded ROM's No-Intro body hash also counts as a match.** Both conventions exist: `HdPackBuilder` writes the full-file hash, community packs sometimes carry the No-Intro one (ADR-0044), and `NesConsole` already tries both when matching a `<patch>` line. An installer stricter than the loader would refuse packs the loader then applies; both forms of the loaded ROM are therefore accepted, but one form is still never compared against the other.

6. **Amendment, 2026-09-19 — a declaration equal to one of the pack's own `<patch>` targets is not a contradiction.** Zelda Remastered declares `DAB79C84…` in `<supportedRom>` *and* on its `<patch>ZeldaHD.ips` line — the hash of the ROM **after** the patch (ADR-0198 §2) — matching no unpatched dump, so rule 4 as first written would have refused a correct install, one issue #314 had already examined and cleared. The exemption covers the pack's own declared patch targets, not the mere presence of a `<patch>` line.

Verification: unit tests in `scripts/core_unit_tests.cpp` (or `UI.Tests` if the decision lands host-free, preferred — the comparison has no host dependency) covering all four branches, including the header/body distinction as its own case, since that is the trap.

## Consequences

- A user whose dump is a different revision of the same game, where the pack author declared a hash, now gets a refusal where they previously got an optimistic install. **Measured, 2026-09-19:** of the packs on this machine exactly one falls here — Pac-Man, whose pack targets the 1993 Namco release against a 1984 dump. It is asserted as a test so the day this proves too strict, the test is edited, with an amendment beside it. If it proves too strict, the escape hatch is a per-pack override, not a return to silent overwriting.
- The `.cache/installs/` registry is keyed by the loaded ROM's sha1 and carries the container name, so a bad row is self-describing once someone looks. This ADR adds no repair pass; #314's cleanup was done by hand, and a sweep is a separate slice.
- `pack.json`'s `targets[0].sha1` changes meaning in case 3, from "the ROM this was installed next to" to "the ROM this pack says it is for". MEP-v1 §2.1 rule 8 already makes location the identity, so nothing reads this field for matching; it becomes more honest without becoming load-bearing.
