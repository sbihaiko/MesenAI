# ADR-0137: Wire repo-hygiene shell checks into make/CI or stop claiming they run

- Status: accepted (2026-08-27; the decision lives in the `makefile`'s `make doc-checks` target and its per-file line ceilings).
- Date: 2026-08-27
- Consolidates: ADR-0111, ADR-0118, ADR-0092 (rejected — premise false, target scripts never shipped); the review findings of the H1 run `45092f2ebec4`, folded into the Clarifications below and deleted (they were auto-minted as ADR-0139–0148 at the time; those ids were later reused for real ADRs — ADR-0139 content_id … ADR-0148 NEA de-listing — so this line no longer names them)
- Related: ADR-0132 (the `check-f5-4b-doc.sh` guardrail was added by the F5.4b run and later deleted)

## Decision

1. **Wire the checks.** Add a `make doc-checks` target that runs, in order, `verify-fase0-1-dox.sh` and `verify-ui-logic-firewall.sh` (and `check-f5-4b-doc.sh` while it existed — deleted 2026-08-27), failing on the first non-zero exit. `check-file-loc.sh` takes `<file> <max-lines>` arguments and encodes no list of its own, so `doc-checks` must call it once per guarded file with the cap the owning doc states (e.g. the 200-line cap on `Core/Shared/Audio/MidiExporter.cpp` its header names); a guardrail with no caller and no argument list is not a guardrail. `check-manifest` stays as is and `doc-checks` depends on it.
2. **Run it in CI.** Every workflow that builds the core or the UI runs `make doc-checks` before the build step, so a broken doc/header contract fails the PR rather than a later human read. The checks are pure shell and need no toolchain, so they can run on the cheapest runner first.
3. **Make the doc true.** Reword `scripts/AGENTS.md:106-108` to name the target (`make doc-checks`, also invoked by CI) and to state that any new `check-*.sh`/`verify-*.sh` must be added to that target in the same commit — otherwise it does not get the "repo-hygiene check" label.
4. **Python exec-bit convention (from ADR-0092, reduced to one sentence).** Scripts are documented and invoked as `python3 scripts/<name>.py`; a shebang plus `+x` is allowed but never required, and acceptance criteria must not test for the executable bit.

If (1)–(2) are not wanted, the minimum acceptable alternative is (3) alone, reworded to say the scripts are manual and listing which command runs each — the "make the doc true" half.

## Context

`scripts/AGENTS.md:106-108` describes `check-core-manifest.sh`, `check-file-loc.sh`, `verify-fase0-1-dox.sh`, `verify-ui-logic-firewall.sh` and `check-f5-4b-doc.sh` as "repo-hygiene shell checks run from `make` or CI". Only one is: the makefile's `check-manifest:` target running `./scripts/check-core-manifest.sh`, and `ui: check-manifest …` / `core: check-manifest …`. No `.github/workflows/*.yml` invokes any of the five.

This is pre-existing rot, compounded by the F5.4b run: it added a fifth unwired script (`check-f5-4b-doc.sh`, guarding the F5.4b clause in `docs/roadmap/plano-execucao-F5.md`'s header Status line) together with a doc claim that it runs. A documented-but-unrun guardrail is worse than none, because reviewers trust it.

ADR-0092 (from the never-executed F5 closeout run `d662e62e2648`) claimed no `scripts/*.py` carries the executable bit and that a planned `mep_build.py`/`test_mep_build.py` would be the first to need `chmod +x`. Both halves are false at HEAD: those scripts do not exist, and the convention is already mixed — `gen_mep_fallback_test_pack.py`, `generate_community_pack_catalog.py`, `test_mep_compare_auto_palettes.py` and `validate_palette_variants.py` are `+x` with a `#!/usr/bin/env python3` shebang while the other eleven `.py` files are not. ADR-0092 is therefore rejected; the only surviving point is the one-line convention above.

## Clarifications

1. **Scope of item 2** ("every workflow that builds the core or the UI"). Read as *every `build.yml` job that invokes `make` directly* — the `linux` and `macos` jobs. `windows` (MSBuild) and `appimage` (via `Linux/appimage/appimage.sh`) are excluded: `appimage` shares `linux`'s triggers, so a break already fails the PR through `linux`.
2. **Local enforcement.** `doc-checks` stays standalone, not a prerequisite of `ui`/`core` — coupling them would make an unrelated prose edit break a local build.
3. **Per-leg duplication in CI.** `make doc-checks` runs once per matrix leg (~10 executions per push). Accepted; a single `doc-checks` job the build jobs `needs:` is the alternative, not adopted.
4. **Per-file caps as makefile literals.** The `check-file-loc.sh <file> <cap>` lines in the `doc-checks` recipe are the single authoritative list of guarded files and caps. Prose mentions of a cap (file headers, ADR-0034) are informative; when they disagree the makefile wins and the prose is fixed.
5. **Amendment 2026-10-02: the recipe is four targets (user's decision).** `doc-checks` is split into `doc-checks-1`..`-4`, same order, no command added, dropped or repeated (verified by diffing `make -n doc-checks`). `doc-checks` still runs the four in sequence; `checks.yml` runs them as parallel jobs behind a fan-in job named `checks`, because one command (the smoke verifier, which compiles Core/) was ~95 % of an ~11-minute gate. Harness-dependent checks stay in shard 1 (see `.github/AGENTS.md`).
6. **Amendment 2026-10-02: shard 1 caches its compiler output.** `checks.yml` installs ccache (the makefile already prefixes the compiler with it when `command -v ccache` finds one) and persists `~/.cache/ccache` with `actions/cache`, keyed by SHA with a prefix restore-key; shard 1's `make -j$(nproc) capture-tool` pre-build is otherwise the whole gate.
7. **Amendment 2026-10-02: the build legs no longer run `doc-checks` (user's decision).** That is now `checks.yml` alone. Measured 2026-10-02: each `build.yml` leg spent 10m46s-11m57s in "Run doc checks" while `checks.yml` runs the same recipe in about 2 minutes warm. A pull request into `main` and every push to `main` still run the gate; a pull request into `prod` whose head is not `main` would not. Windows and AppImage legs never ran it.

## Record

- **2026-08-27 — accepted** as slice H1 of `docs/roadmap/PRD-mesence-enhancement-ecosystem.md`; item 3's "make the doc true" half landed the same day (`scripts/AGENTS.md` now says only `check-core-manifest.sh` is wired); `check-f5-4b-doc.sh` was deleted with the plan header it guarded, so the target wires three scripts plus `check-file-loc.sh`, not four; restored 2026-09-01 from `b0b334b0^` after accidental deletion. Shipped 2026-08-28 (`makefile` `doc-checks` target runs the shell checks). Clarifications 1–4 were added after H1 shipped in `bb8f0c18`.
- **2026-09-15 — 1st amendment (Phase 11 C.7):** `doc-checks` also ceilings `HdPackBuilder.cpp`, `artist_chr_kit.py`, `mep_build.py`, `sheet_repaint.py` and `core_unit_tests.cpp` at their then-current line counts, and `scripts/requirements.txt` pins Pillow/numpy/PyYAML to the versions `checks.yml` installs.
- **2026-09-16 — 2nd amendment:** `scripts/core_unit_tests.cpp` ceiling rises from 7342 to 7600. C.7 ratcheted four implementation files plus the test file; a test file grows whenever a decision does, and ADR-0195 found it at 7342 lines at HEAD, zero headroom.
- **2026-09-16 — 3rd amendment:** `scripts/artist_chr_kit.py` rises from 1762 to 1802, for bug #275 — `--also` refused the pack passed positionally when a caller built the list as "the whole set, and then also the whole set" — which needed a `donor_paths` funnel. The user picked this over shrinking the fix.
- **2026-09-17 — 4th amendment (ADR-0207):** the `scripts/core_unit_tests.cpp` ceiling is **removed**, not raised again (hit at 7342, raised to 7600, back to 7565 from the #302 fix). Guarded list = the four implementation files only, all keeping their ratchets. The user picked this over splitting the harness or raising with headroom.
- **2026-09-19 — 5th amendment (ADR-0209 Q4(k), F12.8):** `HdPackBuilder.cpp` 2246 → 2265 and `scripts/mep_build.py` 1932 → 1936 for the `unsorted` remainder sheet (accumulating shape ids in `WriteSheetFiles`, the `"unsorted"` entry in `_SHEET_RANK`; most of `SheetRender` is host-free and unguarded).
- **2026-09-19 — 6th amendment (ADR-0213, F12.4):** `scripts/artist_chr_kit.py` 1802 → 1803, the one-line `import asset_names as N` that lets the generator refuse a surface name the artist's paint program would mangle; the guard folds into the existing `write_png` call.
- **2026-09-19 — 7th amendment (ADR-0197 §1, F12.6a):** `scripts/mep_build.py` 1936 → 1945 for authored conditions (`conditions[]` carried through `_cell_crops` as a ninth crop field, choosing the authored variants and merging the sheet's own `<condition>` definitions above the rules that cite them; shared rules in `scripts/mep_conditions.py`, imported by `mep_build` and `mep_lint --routes`).
- **2026-09-19 — 8th amendment (ADR-0197 §3, F12.6b):** `Core/NES/HdPacks/HdPackBuilder.cpp` 2265 → 2282 for the recorder's retention of the `$0000`–`$07FF` window (the parameter on `OnFrameEnd`/`RecordGridFrame`, the RAM-plane copy, the `M` line in `WriteGridDump`; the encoder `MesenSheets::RamDumpLine` is host-free in `TileSheetTypes.h`, precisely because `HdPackBuilder.cpp` is not in the `core-unit-tests` link set).
- **2026-09-19 — 9th amendment (#346):** `scripts/mep_build.py` 1945 → 1953 for `_EditedProbe`'s size-mismatch branch — a `*.orig.png` twin that disagrees with the declared scale raises `BuildError` instead of falling back to blind `_SHEET_RANK`; the "no reference declared" and "reference missing/unreadable" branches keep the old fallback.
- **2026-09-20 — 10th amendment (ADR-0217/ADR-0218):** `Core/NES/HdPacks/HdPackBuilder.cpp` 2282 → 2428, **146 lines**, for `FinalizeScreenAnchors`'s two collision guards (the write-time exact-key check, and the post-hoc same-priority drop-and-log; the key comparison `MesenSheets::AnchorKeysOf`/`SameAnchorKeys` is host-free in `ScreenStitcher.cpp`, while the guarded part reads and mutates `_hdData` plus a local `ConditionKey` fallback for a screen with no `GridFrame`).
- **2026-09-20 — 11th amendment (ADR-0219, F12.9):** `scripts/artist_chr_kit.py` 1803 → 2074 and `scripts/mep_build.py` 1953 → 2070 for the static projection (`--static`, the Pack/Page/Bank construction the recording normally supplies, the pages-only `cmd_build_pages_only`). The largest raise granted; the tempting fifth module (refused by ADR-0207's guarded list and by ADR-0219's Alternatives) would have carried no ceiling at all.
- **2026-09-24 — 12th amendment (ADR-0230, F14.9):** `Core/NES/HdPacks/HdPackBuilder.cpp` 2428 → 2438 so every drawn palette of a shape reaches a sheet (the host-free plan and the written-slot filter `WrittenPalettesByShape` live in `SheetColourways.h`; `WriteSheetFiles` queues and `FlushSheetFiles` writes). The write code moved rather than grew.

## Consequences

- The remaining checks become real gates (the F5.4b header guardrail is gone with the plan file it guarded; ADR-0132's "repoint ADR-0050 step b" follow-up is moot for that script).
- A few seconds added to every CI leg that runs `make doc-checks` (shell only). `doc-checks` is deliberately **not** a prerequisite of `ui`/`core`, so a local `make ui` does not run it — run `make doc-checks` by hand before pushing.
- `check-file-loc.sh` gains its explicit list of guarded files in the makefile, which is where the per-file caps stop being folklore.
- Future changes that add a hygiene script have a concrete wiring step to verify in their acceptance criteria, instead of a prose claim.

## Alternatives

- **Reword only** (`scripts/AGENTS.md` says "manual checks") — honest and zero-cost, but leaves the guardrails unrun; acceptable fallback, not preferred.
- **CI only, no make target** — rejected: no local equivalent, and the makefile already hosts `check-manifest`.
- **Fold the checks into `scripts/checks/`** — rejected: those are one-script-one-AC, invoked by the AC's Verification command, not repo-wide gates.
- **Adopt shebang + `chmod +x` repo-wide** (ADR-0092's second option) — rejected: churn on fifteen files for no functional gain; `python3 scripts/…` is what every doc already says.
