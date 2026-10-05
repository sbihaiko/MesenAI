# ADR-0158: No `NES_ONLY` / `LessUI` build modes — the console reduction already took the win

- Status: accepted (2026-09-05, by the user — the decision is *against*, so it requests no work; slice H8 is closed as measured-and-declined. The one thing H8 did buy, the `core-unit-tests` parallelisation, shipped in `7748c013` and is recorded under "What to do instead")
- Date: 2026-09-05
- Related: ADR-0157 §3 (runtime mode, not compile-time), ADR-0007 (Core source-manifest guard), ADR-0131 (`unit-tests.yml` contract), ADR-0155 (header dependency tracking), PRD Part A slice H8, `docs/roadmap/AGENTS.md` (product consoles), `makefile`, `.github/workflows/build.yml`, `.github/workflows/unit-tests.yml`

## Context

PRD slice H8 proposed importing `ky12138/MesenCE`'s `NES_ONLY` (`adc1a6a2`) and `LessUI` (`42e0ee27`) build modes for a smaller, faster headless/CI build. It came from a fork survey, not a measurement. This ADR decides **against**.

### What the prior art actually is

Both commits are **C# only** and touch nothing under `Core/`, `InteropDLL/` or the `makefile`; the native `MesenCore` library they produce is byte-for-byte an unmodified build's. They are MSBuild properties in `UI/UI.csproj` — `<Compile Remove>` / `<AvaloniaXaml Remove>` item groups plus a `DefineConstants` symbol — backed by `UI/NesOnlyStubs.cs` (162 lines of stub types re-declaring what the exclusions took away) and `#if` fences through 22 further UI files (`Configuration.cs`, `ShortcutHandler.cs`, `MainMenuViewModel.cs`, `ConfigViewModel.cs`, `ResourceHelper.cs`, `DbgImporter.cs`, …). So the premise — "compile-time exclusion of the non-NES cores" — does not describe the source: the cores are not excluded by it at all.

### What is left to exclude here

`main` already went through the console reduction (the retired `plano-reducao-consoles`): `Core/` holds `NES`, `Gameboy`, `GBA`, `SMS`, `Shared`, `Debugger` and `Netplay` — no SNES, PC Engine, WonderSwan or ColecoVision core to remove, only the SNES *gamepad* port devices `docs/roadmap/AGENTS.md` keeps as input. Resolving the fork's exclusion lists against this tree:

| Mode | Paths it excludes | Already absent here | Still present | Of those, removable |
|---|---|---|---|---|
| `NES_ONLY` | 130 | 80 | 50 | **0** |
| `LessUI` | 13 | 0 | 13 | **4** |

Of `NES_ONLY`'s 50 survivors, 46 are Game Boy / GBA / SMS UI (three of the four product consoles) and 4 are the SNES gamepad views AGENTS.md keeps. `LessUI`'s 13 are the HD Pack builder (2), the recorder (7) and Netplay (4); the first nine are the product itself (Phase 5, Phase 9, `HdPackBuilderViewModel.ExtractAudio()`), leaving four Netplay window files — 0.6% of the 619 `.cs`/`.axaml` files under `UI/`. The addressable surface is four UI files and nothing in C++.

### Where the time actually goes

macOS/arm64, clang, `-j8` on 8 cores (Darwin sets `LTO := false`, so the link is cheap here; Linux CI links with LTO):

| Step | Time |
|---|---|
| Clean `make -j8 core` (full native lib, 206 Core + Utilities/SDL/Lua/7z TUs) | **149.5 s** wall, 416 s CPU |
| — of which: the 61 `Core/Gameboy` + `Core/GBA` + `Core/SMS` objects | **~38 s** (26%) |
| — of which: the link step alone | 1.3 s |
| No-op incremental `make -j8 core` (ADR-0155 header deps) | 0.8 s |
| `make core-unit-tests`, cold | **39.3 s** |
| `make doc-checks` | **51.7 s** |
| `dotnet publish UI` cold / warm | 38.8 s / 25.5 s |

The 26% is the largest number in the table and is entirely product code (GB/GBC, SMS/GG and GBA are shipped consoles; F2 depends on them). No measured saving is available from excluding non-product code, because none is left.

### Which CI job would use the mode

None.

- `unit-tests.yml` (ADR-0131) never builds `MesenCore`: its native cost is `make core-unit-tests`, a self-contained compile of 23 explicitly listed `Core/`/`Utilities/` sources — a mode that excludes cores cannot make it faster, because it never compiled them.
- `build.yml`'s 14 jobs (6 Linux, 2 AppImage, 4 macOS, 2 Windows) produce the **shipped artifacts**, which must contain every product console and the builder and recorder.
- `tests.yml` runs the Windows ROM regression suite against a full build.
- The headless tools (`capture-tool`, `spike-sound-driver`, `roles-probe`) depend on the `core` target and are built by no workflow.

`build.yml` already configures `ccache` on every Linux and macOS job and builds with `make -j$(nproc) -O`.

## Decision

**Do not add `NES_ONLY`, `LessUI`, or any other compile-time build mode that excludes cores or product UI.** Slice H8 is closed as "measured, not worth it". No symbol is introduced, no `#ifdef` or `#if` is added, `UI/UI.csproj` gains no `Remove` item group, and CI builds exactly what it builds today.

Four reasons, in order of weight:

**1. There is nothing left to exclude.** Zero of `NES_ONLY`'s 130 exclusions and four of `LessUI`'s 13 are removable without dropping product — a permanent mode whose whole reach is four Netplay window files is not worth a second compilation configuration.

**2. It would fragment the build for no measured gain.** ADR-0157 §3 in a new costume: that section rejected `#ifdef LIBRETRO` in `Core/` because "the headless harness and the shipped GUI no longer exercise the same code", a permanent cost. Moving the fences from C++ to C# does not change the argument — the fork needed 22 fenced files plus a 162-line stub to keep a `NES_ONLY` build compiling, and each fence is a place where the mode CI runs and the mode users run can drift apart. There is one binary, and what varies varies at runtime.

**3. ADR-0007's guard would become quietly ambiguous.** `scripts/check-core-manifest.sh` diffs `find Core -name '*.cpp'` **on disk** against `Core.vcxproj`'s `<ClCompile>` entries, with no view of what a given makefile invocation compiles. A C++ `NES_ONLY` would still pass while MSVC — no equivalent mode — kept compiling the excluded sources, so the two manifests would agree textually and mean different things.

**4. The remaining CI time is not compile time.** `make doc-checks` (51.7 s) costs more than the whole native step of the cheap job, and the UI publish about as much as the core-unit-tests compile. A build mode attacks the smallest column.

### What to do instead, if the build is ever the bottleneck

- **`make core-unit-tests` was one serial `clang++` invocation — now done.** Now one object per translation unit (`%.cut.o`, `-MMD -MP` header deps per ADR-0155) plus a link step, so `make -j` applies: **9.7 s cold, 6.0 s after a one-file edit**, same 491 cases, same binary. Both callers pass `-j` (`.github/workflows/unit-tests.yml`, `scripts/build_app_macos.sh`) — about four times what the fork's modes could have offered, at no cost in fences.
- **`ccache` locally.** CI has it; without it a clean build pays the full 149.5 s. A bigger single-developer win than the 38 s the non-NES cores cost, for one `brew install`.
- **Trim what a job builds, not what a build contains.** `unit-tests.yml` is the proof: it deliberately builds no core.
- **Runtime selection.** A narrower emulator is selected at runtime (ADR-0157 §3).

## Consequences

H8 ships no code; the measurement is the deliverable, retiring a slice rather than adding a maintenance surface — no second compilation configuration, no stub file shadowing real types, no `#if` fence letting CI and the shipped build diverge, and `check-core-manifest.sh` keeps meaning what ADR-0007 says. The cost is that the 26% spent on the Game Boy, GBA and SMS cores stays spent on every clean build, forever — the correct price, since they are product. Revisiting this means re-running the table above: the decision is a function of those numbers and would change if a console were dropped from the product again.

## Alternatives

**Import `NES_ONLY` as-is.** Rejected: it excludes Game Boy, GBA and SMS UI (three of the four product consoles, `docs/roadmap/AGENTS.md`), 80 of its 130 exclusions name files this tree deleted slices ago, and what survives a rewrite is the four SNES gamepad views AGENTS.md explicitly keeps.

**Import `LessUI` as-is.** Rejected more sharply: it excludes `HdPackBuilderWindow`, `HdPackBuilderViewModel`, `VideoRecordWindow` and `MovieRecordWindow` — optional extras upstream, the product (Phase 5, Phase 9) in MesenCE.

**A narrowed `LessUI` covering only Netplay.** Rejected: four files of 619 (0.6%), and those files do not measurably move `dotnet publish`.

**A C++-side `NES_ONLY` we design ourselves** (excluding `Core/Gameboy`, `Core/GBA`, `Core/SMS` from `CORESRC` for a headless-only target). Rejected: the 38 s it saves is product code, no CI job would build in that mode, and it puts `check-core-manifest.sh` in the ambiguous position described under Decision 3.

**Do nothing and leave H8 open.** Rejected: an open slice invites a later run to implement it from the same wrong premise.
