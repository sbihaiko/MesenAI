# ADR-0155: The makefile tracks header dependencies (`-MMD -MP`) instead of relying on a manual clean rebuild

- Status: accepted
- Date: 2026-09-05
- Related: ADR-0007 (Core source-manifest drift guard), ADR-0126/ADR-0131 (unit-test wiring), ADR-0153 (artist-legible sheets)

## Context

The makefile's bare `%.o: %.cpp` pattern rule makes `.o` files depend on their `.cpp` and nothing else: changing a header rebuilds nothing, and translation units that include it keep the layout they were compiled against. The standing workaround — `rm -f $(find Core InteropDLL -name '*.o')` by hand when a Core class member changes — depends on someone remembering, which is not a guard.

It failed on 2026-09-05. `MesenSheets::SheetCell` (`Core/NES/HdPacks/TileSheetTypes.h`) gained two `std::vector` members for the Phase 9 alias pass; `SheetRender.cpp` was recompiled and `SheetGrouping.cpp` was not, so the two disagreed on `sizeof(SheetCell)` and `MesenSheets::RenderGroup` walked off the image buffer (a `Cells.size()` of `14757395258967641294`), segfaulting all 30 ROMs in the test library in `HdPackBuilder::SaveHdPack` at the end of a 300-second recording, with a crash signature (`_platform_memmove`, `EXC_BAD_ACCESS` at `0x1`) that names the render code, not the stale object. Silent at build time, delayed at run time, symptom naming the wrong file — that cost profile is why this is a decision, not a habit.

Non-goals: changing the source manifest (ADR-0007 guards that), touching the MSVC/`Core.vcxproj` build, or reorganising headers.

## Decision

Generate and consume dependency files:

- add `-MMD -MP` to `CXXFLAGS` (and therefore `OBJCXXFLAGS`), so every compile writes a sibling `.d` listing the headers it read; `-MP` emits phony targets for those headers, so deleting or renaming one does not break the build with "No rule to make target". `-MMD` (not `-MD`) deliberately ignores system headers — a toolchain upgrade should not invalidate every object, and the failure mode this ADR prevents is a *project* header changing shape;
- `-include` the `.d` files for every object the build knows about, so a header edit rebuilds exactly the translation units that include it;
- add `*.d` to `.gitignore` next to the existing object-file rules, and extend `make clean` to remove them, so a `.d` naming a header that no longer exists cannot outlive the tree;
- keep the manual clean rebuild documented as the recovery step for a tree whose `.d` files predate this change.

## Verification

After a clean rebuild, `touch Core/NES/HdPacks/TileSheetTypes.h` then `make capture-tool` recompiles the six HdPacks translation units that include it — including `SheetGrouping.cpp`, the object that was stale when the library segfaulted — plus the three transitive consumers `NesConsole.cpp`, `NesPpu.cpp`, `InteropDLL/EmuApiWrapper.cpp` and nothing else; before this change the same `touch` recompiled nothing.

## Consequences

- The first build after this lands is a full rebuild — no `.d` files exist yet, so every object is out of date. Correct one-time cost.
- Incremental builds get slightly slower (one extra file per translation unit) and correct, which is the trade.
- CI is unaffected: it always builds from a clean tree, so it never had the bug and gains only the write cost.
- `Core.vcxproj` is untouched; the Windows build keeps whatever header tracking MSBuild already does. `scripts/check-core-manifest.sh` (ADR-0007) still steps the two build systems, about the *set* of sources, not their dependencies.
- Removing the manual-clean habit removes the only reason to run `find ... -name '*.o' -delete`, itself a footgun on a tree with a build in flight.
