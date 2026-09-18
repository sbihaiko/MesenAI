# ADR-0211: A declared `<supportedRom>` that contradicts the loaded ROM refuses the install, instead of being overwritten by it

- Status: proposed
- Date: 2026-09-18
- Related: ADR-0145 (optimistic matcher), ADR-0146 (auto-install, no consent gate), ADR-0147 (`mep/` beside the ROM), ADR-0138 §41, MEP-v1 §2.1, issue #314

## Context

Issue #314: `<roms>/Bomberman (1985) (Hudson Soft)/mep/` held the Contra 80s
pack — 233 files, 171 MB, a `hires.txt` byte-identical to Contra's, declaring
`<supportedRom>C9EA66BB7CB30AD5…`. Bomberman rendered Contra's art on every
load, silently, for ten days.

The install was wrong, but the reason it was *silent* is a separate defect and
it is the one worth deciding about.
`CommunityPackInstallCoordinator.BuildLegacyPackJson` writes
`targets[0].sha1` from `EmuApi.GetMepRomSha1()` — the **loaded** ROM — while the
artifact it just extracted already declares its own `<supportedRom>`. When the
two disagree, the stamp does not flag the disagreement; it overwrites the only
record of it and manufactures a pack that matches cleanly forever after. The
load path does not re-check either. Three artifacts (`pack.json`,
`.mep-install.json`, the install registry entry) all agreed with each other and
all three were wrong.

This is not a case ADR-0145's optimism covers. That optimism is about the
**absence** of evidence: a catalog entry with `rom: {}`, or a dump whose hash
nobody recorded, gets installed anyway and the health signal sorts it out. A
`<supportedRom>` naming a different ROM is the **presence** of contrary
evidence, asserted by the pack itself. Treating silence and contradiction the
same way is what let #314 persist.

Non-goal: this does not add a consent dialog (ADR-0146 forbids it), does not
change the matcher, and does not touch packs that declare no `<supportedRom>` —
four catalog packs legitimately have none and must keep installing.

## Decision

On a legacy HD pack install (`InstallHdLegacy`), after extraction and before
`pack.json` is written:

1. Read `<supportedRom>` from the extracted `textures/hires.txt`.
2. **Absent, empty, or unparseable → proceed unchanged.** Optimism is preserved
   exactly where it applies today.
3. **Present and equal to the loaded ROM → proceed**, and write the declared
   value into `targets[0].sha1` rather than the loaded ROM's, so the stamp
   records the pack's own claim.
4. **Present and different → refuse.** Return
   `CommunityPackInstallOutcome.Failed`, leave no `mep/` behind, and log the
   pair:
   `[CommunityPackInstall] refused: pack declares supportedRom <X>, loaded ROM is <Y>`.

The comparison is case-insensitive, and it is between the **full-file** SHA-1 on
both sides: `<supportedRom>` is the hash of the whole file including the iNES
header, whereas `EmuApi.GetMepRomSha1()` returns the No-Intro **body** hash
(ADR-0003). These are two different hashes of the same ROM, and comparing them
directly would refuse every correct install. The install path therefore needs
the full-file hash exposed alongside the body hash; whichever way that is
plumbed, the two must never be compared across forms.

Verification: unit tests in `scripts/core_unit_tests.cpp` (or `UI.Tests` if the
decision lands host-free, which is preferred — the comparison itself has no host
dependency) covering all four branches, including the header/body distinction as
its own case, since that is the trap.

## Consequences

- A user whose dump is a different revision of the same game, where the pack
  author declared a hash, now gets a refusal where they previously got an
  optimistic install. This is the intended trade: the pack author stated which
  dump they targeted, and the optimistic path remains for every pack that did
  not state one. Should this prove too strict in practice, the escape hatch is a
  per-pack override, not a return to silent overwriting.
- The existing `.cache/installs/` registry is keyed by the loaded ROM's sha1 and
  carries the container name, so a bad row is self-describing once someone
  looks. This ADR does not add a repair pass over already-installed packs;
  #314's cleanup was done by hand. If a sweep is wanted, it is a separate slice.
- `pack.json`'s `targets[0].sha1` changes meaning in case 3, from "the ROM this
  was installed next to" to "the ROM this pack says it is for". MEP-v1 §2.1
  rule 8 already makes location the identity, so nothing reads this field for
  matching; it becomes more honest without becoming load-bearing.
