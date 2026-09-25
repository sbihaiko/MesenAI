# ADR-0137: Wire repo-hygiene shell checks into make/CI or stop claiming they run

- Status: accepted (2026-08-27; decision = items 1–4 below, work requested as slice H1 of `docs/roadmap/PRD-mesence-enhancement-ecosystem.md`. Item 3's "make the doc true" half already landed the same day: `scripts/AGENTS.md` now says only `check-core-manifest.sh` is wired. `check-f5-4b-doc.sh` was deleted on 2026-08-27 with the plan header it guarded, so the target wires three scripts plus `check-file-loc.sh`, not four; restored 2026-09-01 from b0b334b0^ after accidental deletion; work shipped 2026-08-28 as slice H1 of docs/roadmap/PRD-mesence-enhancement-ecosystem.md (`makefile` `doc-checks` target runs the shell checks). **Amended 2026-09-15 by Phase 11 C.7:** `doc-checks` also ceilings `HdPackBuilder.cpp`, `artist_chr_kit.py`, `mep_build.py`, `sheet_repaint.py`, and `core_unit_tests.cpp` at their then-current line counts, and `scripts/requirements.txt` pins Pillow/numpy/PyYAML to the versions `checks.yml` installs) **Amended again 2026-09-16:** the `scripts/core_unit_tests.cpp` ceiling rises from the C.7 count of 7342 to 7600. C.7 ratcheted four implementation files plus the test file; the ratchet's purpose is to stop implementation creeping, and a test file grows whenever a decision does. ADR-0195 arrived one day after C.7, needed the unit tests its own Status line promises, and found the file at exactly its ceiling - 7342 lines at HEAD, zero headroom. The four implementation ceilings (`HdPackBuilder.cpp`, `artist_chr_kit.py`, `mep_build.py`, `sheet_repaint.py`) are untouched, and the test ceiling remains a ratchet: it may be raised deliberately, never silently. **Amended a third time 2026-09-16:** the `scripts/artist_chr_kit.py` ceiling rises from the C.7 count of 1762 to 1802. Bug #275 — `--also` refused the pack passed positionally, which a caller building the list as "the whole set, and then also the whole set" hands over — needs a `donor_paths` funnel plus the prose that explains why a repeat is dropped rather than refused, and the file sat at exactly its ceiling with zero headroom. The user picked this amendment over shrinking the fix when asked (2026-09-16); the raise is deliberate and recorded in the `doc-checks` recipe, not silent. The other three implementation ceilings are untouched, and `artist_chr_kit.py` is a ratchet again from 1802. **Amended a fourth time 2026-09-17 (see ADR-0207):** the `scripts/core_unit_tests.cpp` ceiling is **removed**, not raised again. It was hit twice in three days - ratcheted at 7342 by C.7 on 09-15, raised to 7600 on 09-16 when ADR-0195 found zero headroom, and back to 7565 on 09-17 from the #302 fix - both times by work the project had already decided to do, and each hit cost an amendment to this line and to the `doc-checks` recipe. The second amendment already carried the argument: the ratchet's purpose is to stop implementation creeping, and a test file grows whenever a decision does. The guarded list is therefore the four implementation files (`HdPackBuilder.cpp`, `artist_chr_kit.py`, `mep_build.py`, `sheet_repaint.py`) and nothing else; all four keep their ratchets. The user picked this over splitting the harness or raising with headroom when asked (2026-09-17). **Amended a fifth time 2026-09-19 (ADR-0209 Q4(k), F12.8):** two of the four remaining ceilings rise — `HdPackBuilder.cpp` from 2246 to 2265 and `scripts/mep_build.py` from 1932 to 1936 — for the `unsorted` remainder sheet. Most of that slice is host-free in `SheetRender` and in the unit tests, neither of which is ceilinged; what had to land in the two guarded files is the part that cannot be host-free: accumulating each written sheet's shape ids in `WriteSheetFiles`, the call that writes the complement, and the `"unsorted"` entry in `mep_build.py`'s `_SHEET_RANK`. `HdPackBuilder.cpp` had five lines of headroom and `mep_build.py` had none. Both stay ratchets, from 2265 and 1936; `artist_chr_kit.py` and `sheet_repaint.py` are untouched. **Amended a sixth time 2026-09-19 (ADR-0213, F12.4):** `scripts/artist_chr_kit.py` rises from 1802 to **1803** — one line, and it is the `import asset_names as N` that lets the generator refuse a surface name the artist's paint program would mangle. The guard itself is folded into the existing `write_png` call and costs nothing; a module-level import cannot be. The file was at exactly its ceiling again, as it was at the third amendment. No headroom is added on purpose: 1803 is the new ratchet, so the next line still has to be argued for. The other three implementation ceilings are untouched. **Amended a seventh time 2026-09-19 (ADR-0197 §1, F12.6a):** `scripts/mep_build.py` rises from 1936 to **1945** — nine lines, for the authored-condition half of the slice: reading a sheet's `conditions[]`, carrying the cell's condition name through `_cell_crops` as a ninth crop field, choosing the authored variants over the inherited ones, and merging the sheet's own `<condition>` definitions above the rules that cite them. The shared rules live in the new `scripts/mep_conditions.py`, which both `mep_build` and `mep_lint --routes` import, so what remains here is only what has to be in the writer. `mep_build.py` was at exactly its ceiling again, three days after the fifth amendment raised it. No headroom is added: 1945 is the new ratchet. The other three implementation ceilings are untouched, and `scripts/mep_conditions.py` is **not** ceilinged — the guarded list stays the four files ADR-0207 fixed it at. **Amended an eighth time 2026-09-19 (ADR-0197 §3, F12.6b):** `Core/NES/HdPacks/HdPackBuilder.cpp` rises from 2265 to **2282** — seventeen lines, for the recorder's retention of the `$0000`–`$07FF` window. Everything that could be host-free is: the `M` line's encoder (`MesenSheets::RamDumpLine`) is an inline in `TileSheetTypes.h` and is unit-tested there, precisely because `HdPackBuilder.cpp` is not in the `core-unit-tests` link set. What had to land in the guarded file is the part that cannot be: the parameter on `OnFrameEnd`/`RecordGridFrame`, the per-retained-frame copy into the RAM plane, and the `M` line in `WriteGridDump` — plus the comments saying why the plane is written once per retained frame and not once per repeat, which is the decision a future reader will otherwise re-litigate. The file had five lines of headroom at the fifth amendment and none now. No headroom is added: 2282 is the new ratchet. The other three implementation ceilings are untouched. **Amended a ninth time 2026-09-19 (#346):** `scripts/mep_build.py` rises from 1945 to **1953** — eight lines, for `_EditedProbe`'s size-mismatch branch: a `*.orig.png` twin that exists but disagrees with the sheet's declared scale is proof the pair was grown one file at a time, not absence of evidence, so it now raises `BuildError` instead of falling back to blind (every cell counts as painted, `_SHEET_RANK` decides for cells nobody touched — the silent failure #346 was filed over). The "no reference declared" and "reference missing/unreadable" branches keep the old blind fallback unchanged; only the corruption-evidence branch moved. `mep_build.py` was at exactly its ceiling again. No headroom is added: 1953 is the new ratchet. The other three implementation ceilings are untouched. **Amended a tenth time 2026-09-20 (ADR-0217/ADR-0218):** `Core/NES/HdPacks/HdPackBuilder.cpp` rises from 2282 to **2428** — 146 lines, for `FinalizeScreenAnchors`'s two collision guards: the forced-rival set shared by ADR-0217 Option C and ADR-0218 Option A, the write-time exact-key check against every already-committed capture (ADR-0217 Option A), and the post-hoc same-priority collision scan with its drop-and-log (ADR-0218 Option B). The key comparison itself (`MesenSheets::AnchorKeysOf`/`SameAnchorKeys`, read off a screen's own `GridFrame`) is host-free and lives in the unguarded `ScreenStitcher.cpp`, unit-tested there; what had to land in the guarded file is the part that cannot be host-free - reading and mutating `_hdData`, plus a small local `ConditionKey` fallback for the one case with no `GridFrame` to key on (a screen past the retention cap), which the post-hoc pass alone still has to catch. The file had no headroom at the eighth amendment and none now — the ninth amendment (#346) touched only `mep_build.py`, leaving `HdPackBuilder.cpp` unchanged since the eighth. No headroom is added: 2428 is the new ratchet. The other three implementation ceilings are untouched. **Amended an eleventh time 2026-09-20 (ADR-0219, F12.9):** two ceilings rise — `scripts/artist_chr_kit.py` from 1803 to **2074** and `scripts/mep_build.py` from 1953 to **2070** — for the static projection: a kit generated over the ROM alone, with no recording, and the pages-only build that turns it back into a pack. This is the largest raise the register has granted and it is deliberate rather than incremental: F12.9's deliverable *is* a second input path through these two files (`--static`, the Pack/Page/Bank construction the recording normally supplies, the manifest the kit now writes, and `cmd_build_pages_only`), and nothing of it is host-free. The tempting escape — a fifth module, which ADR-0207 fixed the guarded list against and which would therefore carry no ceiling at all — is exactly the silent creep the ratchet exists to catch, and ADR-0219's Alternatives refuses a separate generator on its own grounds. Both files were at their ceilings again. No headroom is added: 2074 and 2070 are the new ratchets. `HdPackBuilder.cpp` and `sheet_repaint.py` are untouched. **Amended a twelfth time 2026-09-24 (ADR-0230, F14.9):** `Core/NES/HdPacks/HdPackBuilder.cpp` rises from 2428 to **2438**, ten lines, so that every drawn palette of a shape reaches a sheet. Everything that could be host-free is. The fold/colourway classification, the exactness test, the variant-cell plan and layout, and the written-slot filter (`WrittenPalettesByShape`) live in the new inline header `SheetColourways.h` and are unit-tested there, because `HdPackBuilder.cpp` is not in the `core-unit-tests` link set. What had to land in the guarded file cannot be host-free. `WriteSheetFiles` now queues each sheet instead of writing it, because a variant cell changes a sheet's size only after every sheet exists. The new `FlushSheetFiles` then plans against the recorder's own maps and writes the PNG, the `.orig.png` twin and the sidecar, and it logs the plan's counts. The write code moved rather than grew: the old write block in `WriteSheetFiles` was deleted. The file had no headroom at the tenth amendment and none now. No headroom is added: 2438 is the new ratchet. The other three implementation ceilings are untouched.
- Date: 2026-08-27
- Consolidates: ADR-0111, ADR-0118, ADR-0092 (rejected — premise false, target scripts never shipped); the review findings of the H1 run `45092f2ebec4`, folded into the Clarifications section below and deleted (they were auto-minted as ADR-0139–0148 at the time; those ids were later reused for real ADRs — ADR-0139 content_id … ADR-0148 NEA de-listing — so this line no longer names them)
- Related: ADR-0132 (the `check-f5-4b-doc.sh` guardrail was added by the F5.4b run and later deleted)

## Context

`scripts/AGENTS.md:106-108` describes `check-core-manifest.sh`,
`check-file-loc.sh`, `verify-fase0-1-dox.sh`, `verify-ui-logic-firewall.sh` and
`check-f5-4b-doc.sh` as "repo-hygiene shell checks run from `make` or CI".
Only one of them is: the makefile's `check-manifest:` target running
`./scripts/check-core-manifest.sh`, and the `ui` and `core` targets depend on
it (`ui: check-manifest …`, `core: check-manifest …`). No `.github/workflows/*.yml` invokes any of the
five. Four of five never execute anywhere except by hand (ADR-0111, ADR-0118).

This is pre-existing rot, but the F5.4b run compounded it: it added a fifth
unwired script (`check-f5-4b-doc.sh`, guarding the F5.4b clause in
`docs/roadmap/plano-execucao-F5.md`'s header Status line) together with a doc
claim that it runs. A documented-but-unrun guardrail is worse than none,
because reviewers trust it.

ADR-0092 (from the never-executed F5 closeout run `d662e62e2648`) claimed that
no `scripts/*.py` carries the executable bit and that the planned
`scripts/mep_build.py`/`test_mep_build.py` would be the first to need
`chmod +x`. Both halves are false at HEAD: those scripts do not exist, and the
convention is already mixed — `gen_mep_fallback_test_pack.py`,
`generate_community_pack_catalog.py`, `test_mep_compare_auto_palettes.py` and
`validate_palette_variants.py` are `+x` with a `#!/usr/bin/env python3`
shebang while the other eleven `.py` files are not. ADR-0092 is therefore
rejected; the only surviving point is a one-line convention (below).

## Decision

1. **Wire the checks.** Add a `make doc-checks` target that runs, in order,
   `verify-fase0-1-dox.sh` and `verify-ui-logic-firewall.sh` (and
   `check-f5-4b-doc.sh` while it existed — deleted 2026-08-27), failing on
   the first non-zero exit. `check-file-loc.sh`
   takes `<file> <max-lines>` arguments and encodes no list of its own, so
   `doc-checks` must call it once per guarded file with the cap the owning
   doc states (e.g. the 200-line cap on `Core/Shared/Audio/MidiExporter.cpp`
   its header names); a guardrail with no caller and no argument list is not a
   guardrail. `check-manifest` stays as is and `doc-checks` depends on it.
2. **Run it in CI.** Every workflow that builds the core or the UI runs
   `make doc-checks` before the build step, so a broken doc/header contract
   fails the PR rather than a later human read. The checks are pure shell and
   need no toolchain, so they can run on the cheapest runner first.
3. **Make the doc true.** Reword `scripts/AGENTS.md:106-108` to name the
   target (`make doc-checks`, also invoked by CI) and to state that any new
   `check-*.sh`/`verify-*.sh` must be added to that target in the same commit
   — otherwise it does not get the "repo-hygiene check" label.
4. **Python exec-bit convention (from ADR-0092, reduced to one sentence).**
   Scripts are documented and invoked as `python3 scripts/<name>.py`; a
   shebang plus `+x` is allowed but never required, and acceptance criteria
   must not test for the executable bit.

If (1)–(2) are not wanted, the minimum acceptable alternative is (3) alone,
reworded to say the scripts are manual and listing which command runs each.

## Consequences

- The remaining checks become real gates (the F5.4b header guardrail is
  gone with the plan file it guarded; ADR-0132's "repoint ADR-0050 step b"
  follow-up is now moot for that script).
- A few seconds added to every CI build leg that runs `make doc-checks` (shell
  only). `doc-checks` is deliberately **not** a prerequisite of `ui`/`core`, so
  a local `make ui` does not run it — run `make doc-checks` by hand before
  pushing (see Clarifications).
- `check-file-loc.sh` gains an explicit list of guarded files in the makefile,
  which is where the per-file caps stop being folklore.
- Future changes that add a hygiene script have a concrete wiring step
  to verify in their acceptance criteria, instead of a prose claim.

## Alternatives

- **Reword only** (`scripts/AGENTS.md` says "manual checks") — honest and
  zero-cost, but leaves the guardrails unrun; acceptable fallback, not
  preferred.
- **CI only, no make target** — rejected: developers would have no local
  equivalent, and the makefile already hosts `check-manifest`.
- **Fold the checks into `scripts/checks/`** (per-AC verifiers) — rejected:
  those are one-script-one-AC, invoked by the AC's Verification command, not
  repo-wide gates.
- **Adopt shebang + `chmod +x` repo-wide** (ADR-0092's second option) —
  rejected: churn on fifteen files for no functional gain; `python3 scripts/…`
  is what every doc already says.

## Clarifications (2026-08-27, after slice H1 shipped in `bb8f0c18`)

The H1 run raised five review findings against this ADR's wording. They are
decided here rather than as separate ADRs:

1. **Scope of item 2 ("every workflow that builds the core or the UI").**
   Read as *every `build.yml` job that invokes `make` directly* — today the
   `linux` and `macos` jobs. `windows` (MSBuild) and `appimage` (builds
   through `Linux/appimage/appimage.sh`) are excluded on purpose: `appimage`
   runs on the same push/PR triggers as `linux`, so a doc/header break already
   fails the PR through the `linux` legs. A new job that calls `make` inherits
   the rule; a new wrapper-script job does not.
2. **Local enforcement.** `doc-checks` stays a standalone target and is not
   added to `ui`/`core`'s prerequisites — the checks guard docs and headers,
   not the build, and coupling them would make an unrelated prose edit break
   a local build. The original Consequences line claiming it costs seconds on
   `make ui` was wrong and is corrected above.
3. **Per-leg duplication in CI.** `make doc-checks` runs once per matrix leg
   (~10 executions per push). Accepted for now: each leg stays
   self-contained and the cost is seconds. A single `doc-checks` job that the
   build jobs `needs:` is the alternative if the red-leg noise on a failure
   becomes a problem; not adopted.
4. **Per-file caps as makefile literals.** The `check-file-loc.sh <file>
   <cap>` lines in the `doc-checks` recipe are the single authoritative list
   of guarded files and caps (Consequences already says so). Prose mentions
   of a cap (file headers, ADR-0034) are informative; when they disagree, the
   makefile wins and the prose is fixed. A data file the script iterates is
   not worth it for one entry.
