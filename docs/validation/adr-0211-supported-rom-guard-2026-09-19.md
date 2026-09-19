# ADR-0211 — a contradicting `<supportedRom>` refuses the install (2026-09-19)

The fix for issue #314. The decision, its two same-day amendments and the
non-goals live in `docs/adr/0211-a-declared-supportedrom-mismatch-refuses-the-install.md`.
What follows is what was measured, including the case that changed the decision
before it shipped.

## The defect, restated from the artifacts

`CommunityPackInstallCoordinator.BuildLegacyPackJson` wrote `targets[0].sha1`
from `EmuApi.GetMepRomSha1()` — the **loaded** ROM — over an artifact that
already declared its own `<supportedRom>`. When the two disagreed, the stamp did
not flag the disagreement: it erased the only record of it. `pack.json`,
`.mep-install.json` and the install registry then all agreed with each other,
and all three were wrong.

## The guard

`InstallHdLegacy` now reads the extracted `textures/hires.txt` between
extraction and `pack.json`:

| declaration | verdict | what happens |
|---|---|---|
| absent, empty, or not a 40-hex sha1 | `NotDeclared` | installs unchanged (ADR-0145) |
| equals the loaded ROM, either hash form | `Matches` | installs; the **declared** value is stamped |
| equals one of the pack's own `<patch>` targets | `PatchTarget` | installs (ADR-0198 §2) |
| anything else | `Contradicts` | refused, no `mep/` left behind, both hashes logged |

The decision is pure and lives in `UI/Logic/LegacyHdPackInstall.cs`
(`ReadSupportedRom`, `DecideSupportedRom`), so it is unit-tested without a host.
The coordinator only reads the file and acts on the verdict.

## Replayed against the packs on this machine

Six real packs, six real ROMs, the ROM hashes computed from the files
themselves:

| ROM | pack | verdict | why it matters |
|---|---|---|---|
| Bomberman (1985) | Contra 80s (`Contra/mep.off`) | **contradicts** | issue #314 itself — this is the install that used to succeed |
| Contra (1988) | the same pack, under its own ROM | matches | the guard must not cost the pack its correct home |
| The Legend of Zelda | Zelda Remastered | **patch-target** | declares `DAB79C84…` in `<supportedRom>` *and* on its `<patch>ZeldaHD.ips` line |
| Pac-Man (1984) | Pac-Man HD | **contradicts** | the pack targets the 1993 Namco release; the local dump is the 1984 one |
| Metroid (USA) | community pack | matches | a plain, correct community install |
| Castlevania (1987) | community pack | not-declared | one of the four packs that declare nothing |

## The case that changed the decision before it shipped

ADR-0211 as written refused anything that was not the loaded ROM's full-file
hash. Run against the disk, that rule refused **Zelda Remastered** — a pack
issue #314 had already examined and explicitly cleared as correct behaviour. Its
`<supportedRom>` is the hash of the ROM *after* its own IPS patch (ADR-0198 §2),
so it matches no unpatched dump by construction.

Shipping the rule as written would have turned a bug fix into a regression
against a pack the issue had named. The ADR carries the exemption as §6, narrow
on purpose: the pack's own declared patch targets, not the presence of a
`<patch>` line. `APatchLineForAnotherRomDoesNotExcuseTheDeclaration` is the test
that holds the line — otherwise one `<patch>` for an unrelated ROM would excuse
#314's pack.

§5 is the second amendment: the No-Intro body hash counts as a match too. The
loader already tries both forms for `<patch>` (`NesConsole.cpp`, citing
ADR-0044), and an installer stricter than the loader would refuse packs the
loader then happily applies.

**Pac-Man is refused, and that is the ADR's stated trade**, not an oversight:
the author said which dump they targeted. It is asserted as its own test so the
day it proves too strict, that test is what gets edited, with an amendment
beside it.

## The two hashes

`<supportedRom>` is the **whole file**, header included — `HdPackBuilder` writes
`RomFile.GetSha1Hash()`. `GetMepRomSha1()` is the No-Intro **body** hash
(ADR-0003). Comparing one against the other never raises; it just never matches.
So the installer gets its own export, `GetMepRomFileSha1`, kept deliberately
separate rather than added as a flag on the existing one.

Proved against the built `MesenCore.dylib` with the ctypes harness (`InitDll()`
→ `InitializeEmu(<temp home>)` → `LoadRom` → `Pause`), loading the real
Bomberman dump from a scratch folder with no `mep/` beside it:

```
GetMepRomSha1     = D2BF7BD570430902114F1E3393F1FEB8B1C76E4D
GetMepRomFileSha1 = 12531701E633D2196C9A15F944B101A8205248E4
python whole-file = 12531701E633D2196C9A15F944B101A8205248E4
python body       = D2BF7BD570430902114F1E3393F1FEB8B1C76E4D
```

`D2BF7BD5…` is the value #314 found stamped into Bomberman's `pack.json`, which
closes the loop: that is the body hash of the ROM in hand, written over Contra's
declaration.

## What the automated suites cover

- `dotnet test UI.Tests/UI.Tests.csproj` — **498/498** (16 new in
  `CommunityPacks/SupportedRomGuardTests.cs`): all four verdicts, the
  header/body distinction as its own case, case-insensitivity, five unparseable
  declarations, first-declaration-wins, the patch exemption and its narrow
  limit, and the Pac-Man trade.
- `python3 scripts/test_*.py` / `make doc-checks` — unchanged and green.
- `scripts/verify_community_install_from_zero.py` mirrors the guard
  (`read_supported_rom`, `supported_rom_verdict`) because it is the declared
  Python mirror of the client's install rules; without it the mirror would
  install what the client refuses. A refusal there is a **FAIL**, not a skip:
  the catalog matched the ROM by hash and the artifact says otherwise, so one of
  the two is wrong.

## Honest limits

- **The coordinator's own wiring is not executed by a test.** `InstallHdLegacy`
  touches `EmuApi` and the filesystem, so it is outside UI.Tests by design; what
  is covered there is the decision it acts on. The end-to-end path was exercised
  through the Python mirror against the real artifacts, not through the C#
  installer.
- **No repair pass over already-installed packs.** ADR-0211 says so explicitly;
  #314's own cleanup was done by hand, and `Bomberman/mep/` is already gone from
  this machine. A sweep over `.cache/installs/` would be a separate slice.
- **The load path still does not re-check.** The guard is at install time only.
  A pack placed by hand into `mep/` is the user's own and is never overwritten
  (ADR-0147), so it is also never checked — that asymmetry is unchanged by this
  slice.
- **NES only in practice.** Every pack on this machine is NES; nothing here
  exercises a GB/SMS pack's `<supportedRom>`, though the comparison is
  console-agnostic and the GB/SMS loader already treats the tag as
  informational.
