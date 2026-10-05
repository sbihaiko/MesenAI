# ADR-0122: Unit-test wiring: csproj layout, host-free firewall, fixture format, CI contract

- Status: accepted
- Date: 2026-08-27
- Related: ADR-0049, ADR-0120, ADR-0121, ADR-0137, ADR-0150
- Consolidates: ADR-0053, ADR-0054, ADR-0055, ADR-0056, ADR-0057, ADR-0058, ADR-0059, ADR-0060, ADR-0061, ADR-0062, ADR-0063, ADR-0064, ADR-0065, ADR-0066, ADR-0067, ADR-0068, ADR-0069, ADR-0070, ADR-0071, ADR-0072, ADR-0073, ADR-0074, ADR-0075, ADR-0076, ADR-0077, ADR-0078, ADR-0079, ADR-0080, ADR-0081, ADR-0082, ADR-0083, ADR-0084, ADR-0085, ADR-0086, ADR-0087, ADR-0088, ADR-0089, ADR-0090, ADR-0091, ADR-0123, ADR-0124, ADR-0125, ADR-0126, ADR-0127, ADR-0128, ADR-0129, ADR-0130, ADR-0131

## Decision

1. **`UI.Tests/UI.Tests.csproj` stays out of `Mesen.sln`.** The host-free xunit
   project (net10.0, no `RuntimeIdentifier`, no `ProjectReference` to
   `UI/UI.csproj`, `EnableDefaultCompileItems=false`, `<Compile Include>`
   entries for `../UI/Logic/**/*.cs` and `../UI/Interop/InteropEnums.cs`) is
   invoked directly — `dotnet test UI.Tests/UI.Tests.csproj`, `make unit-tests`
   (makefile target `unit-tests`/`verify`) — so it never enters the Windows
   solution flows `dotnet restore -r win-x64 -p:PublishAot=true`
   (`.github/workflows/build.yml`, `tests.yml`) or `dotnet format --verify-no-changes`
   (`dotnet restore` + `dotnet format` at repo root in
   `.github/workflows/dotnet-format-check.yml`, `.sln`). `grep -c UI.Tests Mesen.sln`
   stays 0; the rule is in `UI/AGENTS.md` ("`UI.Tests.csproj` is NOT
   a member of `Mesen.sln`"). Revisit only if a Windows runner confirms the
   RID-less csproj survives AOT restore; then add it with `Build.0` disabled for
   `Release|x64` and bring `UI.Tests/**` into `.editorconfig` (tabs) conformance.
2. **`UI/Logic/*.cs` is the host-free boundary** — BCL + `System.IO.Compression`
   only, no `Avalonia`/`EmuApi` — dual-compiled via
   `<Compile Include="../UI/Logic/**/*.cs" />`. Guards: the dual-compile and
   `scripts/verify-ui-logic-firewall.sh`. The test csproj holds compile
   strictness **at parity with (or stricter than)** `UI/UI.csproj`: drop
   `<ImplicitUsings>enable</ImplicitUsings>`, set
   `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (the app uses
   `TreatWarningsAsErrors` with `WarningsNotAsErrors` for
   IL2026/IL2070/IL2075/IL2104/IL3050/IL3053). The script derives its scan from
   the csproj's `<Compile Include="../...">` (globs expanded, relative to
   `UI.Tests/`), not `logicDir="UI/Logic"`, so `InteropEnums.cs`/`EmuApi.cs` are
   covered; it also checks for `<RuntimeIdentifier>`/`<ProjectReference>` and
   greps `UI/Logic/*.cs` for `Avalonia|EmuApi` outside `//` comments. It runs in
   `make unit-tests` and as a step in the `ui-tests` job of
   `.github/workflows/unit-tests.yml` before `dotnet test`. Docs: `UI/AGENTS.md`,
   `UI.Tests/AGENTS.md`, `scripts/AGENTS.md`, `.github/AGENTS.md` (was
   `UI/AGENTS.md:92`, `scripts/AGENTS.md:107`). Rule: "properties affecting
   compilation of `../UI/Logic` and `../UI/Interop/InteropEnums.cs` must stay at
   parity with (or stricter than) `UI/UI.csproj`"; `DllImport`/Avalonia is
   normal in `UI/Interop/`, forbidden in `UI/Logic/`.
3. **`docs/specs/golden/mep/path-cases.txt` header owns its consumer list and
   scope** — no second list in `docs/AGENTS.md`. Format: `<path><TAB>ok|bad`
   per line; blank and `#` lines ignored. Readers:
   `UI.Tests/Mep/MepZipValidatorTests.cs` and `scripts/core_unit_tests.cpp`
   (Block B `MepPack::NormalizeRelativePath`; `core_unit_tests.cpp:154`,
   `MepPack::Parse`). `scripts/validate-specs.py` adds
   `validate_path_cases(...)`, a format guard (not a semantic third reader — the
   old "three independent readers" premise was wrong) that each non-blank,
   non-`#` line matches `^[^\t]+\t(ok|bad)$`; call it from `main()`. Fidelity:
   skip via `line[0] == '#'` (not `lstrip()`) and reject a path column differing
   from its stripped form (the C# reader `Trim()`s, the C++ one does not).
   Control chars (bytes < 0x20) are not encoded — the TAB format cannot carry
   them and no `\xNN` escape or third column is added — so parity is per suite:
   C# `[InlineData]` (`[InlineData('\0')]`/`'\x01'`/`'\t'`) in
   `UI.Tests/Mep/MepZipValidatorTests.cs:134-136`, C++ literals (`\0`, `\x01`,
   `\t`) added as `TestNormalizeRelativePathRejectsControlChars` in
   `scripts/core_unit_tests.cpp` Block B. No normative MEP-v1 subsection and no
   extra "frozen format" paragraph — harness detail, not `docs/specs/MEP-v1.md`,
   and not `docs/specs/golden/AGENTS.md`. `validate_esp`/`validate_mep`
   (`golden/mep/pack.json`)/`validate_mei`/`validate_hires_draft` are the spec
   consumers.
4. **`UI/Logic/` types may expose pure helpers publicly for direct testing** —
   e.g. `MepZipValidator.IsSafePath` over `path-cases.txt` — while the documented
   production entry point stays `public static string? Validate(ZipArchive zip)`
   (i.e. `Validate(ZipArchive)`). Each test-facing helper carries a `//` header
   line naming it test-facing / reusable (`UI/Logic/MepZipValidator.cs`,
   `//Test-facing / reusable pure predicate`). `InternalsVisibleTo` is not used
   (and `InternalsVisibleTo("UI.Tests")` is not added to `UI/UI.csproj`): the
   dual-compile already makes `internal` visible, so public/internal buys no
   encapsulation. A pure helper that is half of a stateful operation must name
   its impure partner in its file header and perform only the pure part;
   reference `UI/Logic/DisabledPackList.cs` ("Host-free counterpart to
   EnhancementPackConfig.SetPackEnabled's list mutation ... The caller
   (EnhancementPackConfig.SetPackEnabled) still owns the
   EmuApi.SetMepPackEnabled native call - this only mutates the list."), keeping
   `DisabledPackList.Set` (`UI/Logic/DisabledPackList.Set`, de-duplicating
   `DisabledPacks`) pure. Option B would make them `internal static`.
   `UI.Tests/<Area>/` holds paired tests.
5. **CheatTypeDetector parity tests use `Assert.ThrowsAny<Exception>(...)`**
   (not `Assert.Throws<Exception>(...)`/`Assert.Throws<T>`) for `Gameboy`,
   `PcEngine`, `Sms`, `Gba`, `Ws`: the statement "unsupported consoles throw" /
   "this throws" is asserted, not the `Exception` type. `FromCode`
   (`UI/Logic/CheatTypeDetector.FromCode(ConsoleType, string)`) throws
   `NotSupportedException` with the offending `ConsoleType`, replacing `throw new Exception("Unsupported cheat type")`
   (`UI/Logic/CheatTypeDetector.cs:30`).
   Implementing the `GbGameGenie`/`GbGameShark`/`SmsGameGenie` branches its
   `CheatType` enum declares stays **deferred**; the dead members are a
   documented gap (`UI.Tests/Cheats/CheatTypeDetectorTests.cs`,
   `CheatTypeDetectorTests.cs:48-51`).
6. **The C++ harness runs as a step inside the `ui-tests` job.** `make
   core-unit-tests` compiles `scripts/core_unit_tests.cpp`,
   `Core/Shared/Audio/ChannelRoleClassifier.cpp`,
   `Core/Shared/EnhancementPacks/MepPack.cpp` and `Utilities/*.cpp` with host
   `$(CXX)` (`-std=c++17 -O2 -w`), links no `MesenCore`/SDL2, needs no ROM
   corpus (unlike `roles-probe`, `capture-tool`, `spike-sound-driver`) and has
   no `core` prerequisite. It runs as step `Run core unit tests`
   (`run: make core-unit-tests`) in the existing `ui-tests` job of
   `.github/workflows/unit-tests.yml` after `Run unit tests`; no new workflow
   (`.github/workflows/*`), job or matrix — step names attribute failure.
7. **`scripts/core_unit_tests.cpp` resolves goldens cwd-relative from the repo
   root** — `"docs/specs/golden/mep/path-cases.txt"`,
   `"docs/specs/golden/mep/pack.json"` — header precondition "Run from the repo
   root so the golden paths resolve." Do not add `MESEN_GOLDEN_ROOT`, an
   `argv[1]` root or a `working-directory` step; the two sanctioned entry points
   satisfy it.
8. **Harness binaries are gitignored.** `scripts/core_unit_tests` (about 200 KB)
   is in the root `.gitignore` (`.gitignore:205-208`) with
   `scripts/headless_record`, `scripts/spike_sound_driver`,
   `scripts/roles_probe`; `scripts/AGENTS.md:26-29` states the rule
   ("build output, never `git add`; all listed in `.gitignore`") with no manual exclusion
   (`rm -f scripts/core_unit_tests` gone). `git status` stays clean; a `make
   core-unit-tests` before `git add -A` cannot commit a binary. A new harness
   target adds its binary to `.gitignore` in the same commit; no
   `scripts/.gitignore`, no pattern.
9. **`.github/AGENTS.md` states the `unit-tests.yml` contract as invariants plus
   grep-able verification.** "`unit-tests.yml` must never link
   `InteropDLL`/`MesenCore`, never require SDL2, and never require a platform SDK
   or ROM corpus. A self-contained compile of explicitly listed
   `Core/`/`Utilities/` sources (as `make core-unit-tests` does) is in scope;
   anything that needs the `core` makefile target belongs in
   `build.yml`/`tests.yml`." The `ui-tests` job id now covers both host-free
   suites (steps `Checkout repo`, `Install .NET`, `Run unit tests`, `Run core
   unit tests`); a later rename (e.g. `host-free-tests`) is a known option, and a
   second job would need its own `name:`. `make core-unit-tests` is intentionally
   clang-only (`CXX := clang++`); gcc/arm64 coverage of the `Core/` sources is
   `build.yml`'s job via `CORESRC := $(shell find Core -name '*.cpp')`, selected
   by `ifeq ($(USE_GCC),true)`/`USE_GCC=true`/`make_flags: "USE_GCC=true"`
   (`build.yml:83`); only `scripts/core_unit_tests.cpp` is clang-gated.
   `dotnet-version` parity: `unit-tests.yml:34` and `build.yml` pin `10.x`
   (`setup-dotnet`), `dotnet-format-check.yml:24` pins `10.0.x` (doc note said
   "(currently `10.0.x`)"); the doc must match the files. Verification greps:
   `grep -E "dotnet-version: 10" .github/workflows/unit-tests.yml
   .github/workflows/dotnet-format-check.yml`, `grep -cE "InteropDLL|SDL2" .github/workflows/unit-tests.yml`
   (0 outside comments), plus `dotnet test UI.Tests/UI.Tests.csproj --nologo`
   and `make core-unit-tests` greps.

## Context

The 2026-08-27 consolidation folded the auto-minted ADR-0053–0091 (later
ADR-0122–0137) into this record; ADR-0137 retired ids 0139–0143. Work comes from
the deleted plan `docs/roadmap/plano-testes-unitarios.md` and
`docs/roadmap/PRD-mesence-enhancement-ecosystem.md` (slices H2/H3/H5/H6).
`UI/Logic` was extracted into host-free helpers (`MepPackListParser`,
`DisabledPackList`, `CheatTypeDetector`) to dual-compile without a native core;
since the test project compiles the sources, `InternalsVisibleTo`, `msbuild -t:UI`
and solution tooling never apply. Traps: dropping `ImplicitUsings` exposes
test-side `using` fallout (a file omitting `using System;` passes the looser test
compile, fails the real UI build); a bad edit in unscanned `InteropEnums.cs`
reads as an opaque "namespace not found". Quotes kept from the retired sources:
"the authoritative gate" (ADR-0055, premature until parity per ADR-0063),
"not worth an ADR" (ADR-0071), "recorded in T5 after T1" (ADR-0065), and
"the new unit-tests.yml job is what is missing today" (ADR-0076, corrected by
ADR-0079); the ADR-0080/ADR-0083 notes were process-only. This record
lives in `docs/adr/`. ADR-0049, ADR-0120, ADR-0121 (`FindFallbackSubfolder`,
`DetectConventionLayout`) are adjacent, not part of this family.

## Consequences

- `UI.Tests/**` is not gated by `dotnet-format-check.yml`; test-code formatting
  is review-only (accepted).
- The RID-less-csproj-under-AOT-restore risk is deferred, not resolved.
- After parity, `dotnet test` green implies the `UI/Logic` half of the Windows
  UI build is compile-green; `TreatWarningsAsErrors=true` also covers
  `UI.Tests/**` (accepted).
- The firewall script self-updates: a new `<Compile Include>` widens the scan.
- Both host-free suites gate every push/PR to `main` in one cheap ubuntu-latest
  job; a C++ compile failure fails the run, so C# contributors wait seconds
  (accepted).
- A clang-only breakage in `scripts/core_unit_tests.cpp` lands green in
  `build.yml` but red in `ui-tests` — the intended signal.
- Running `./scripts/core_unit_tests` from `scripts/` fails two cases with an
  explicit path message (accepted friction).
- Process (ADR-0073/ADR-0083): a dropped adjacent edit is queued as follow-up,
  not left "documented honestly"; updating csproj, firewall scan and contract
  docs is part of a host-free boundary move's definition of done.

## Record

- 2026-08-28 — H3 (`36e5c1fe23ec`, merged `5e914f31`): path-cases guard, "Scope"
  header note, C++ control-char cases.
- 2026-08-28 — H2: `.github/AGENTS.md` invariants and clang-only note.
- 2026-08-29 — firewall parity; `Assert.ThrowsAny` and `NotSupportedException`
  landed.
- 2026-09-01 — ADR-0130 and ADR-0131 restored from `b0b334b0^` after accidental
  deletion.
