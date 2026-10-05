# ADR-0259: Extracting a native library replaces it through a new file, never in place

- Status: accepted 2026-10-05 (go-ahead: "resolva os bugs que aparecerem", the
  session's standing instruction; the change ships with the unit tests this
  decision is pinned by, `UI.Tests/Utilities/NativeDependencyExtractorTests`).
- Date: 2026-10-05
- Related: issue #628 (the same rule on the build side),
  `scripts/replace_file_atomic.sh`, issue #890, ADR-0123 (the host-free split
  `UI/Logic/` exists for)

## Context

The app ships its native libraries — `MesenCore.dylib`, `libSkiaSharp`,
`libHarfBuzzSharp`, `librashader` — inside `Dependencies.zip`, embedded in the
assembly, and unpacks them into the per-user home folder on every start
(`UI/Program.cs`, `UI/Config/ConfigManager.cs`, both through
`DependencyHelper.ExtractNativeDependencies`).

A per-member guard keeps that cheap: a member is only written when the file on
disk differs from the archive entry in length or timestamp. The write itself was
`ZipArchiveEntry.ExtractToFile(path, overwrite: true)`, which opens the
destination in place — measured on 2026-10-05, `stat -f %i` printed the **same
inode** before and after a rewrite.

Two consequences, both measured rather than reasoned about:

- **A running instance is killed.** Two installs that share one home folder — a
  development build and the installed one ship different cores, so each
  re-extracts the other's — have the library mapped when the other overwrites
  it. macOS kills the process: SIGKILL, termination namespace `CODESIGNING` with
  indicator `"Invalid Page"`, faulting thread in dyld `dlopen_from`
  (`~/Library/Logs/DiagnosticReports/Mesen-2026-10-05-1240{27,50}.ips`,
  `-124100.ips`). This is issue #628's failure mode, which the build side
  already answers by writing a new inode; the runtime path did not.
- **The replacement silently fails when the library is open.** .NET opens an
  in-place destination with `FileShare.None`, so with any other handle on the
  file the write throws — and the per-member `catch` swallows it, leaving a
  stale core on disk and nothing said. Measured: with a reader holding the file,
  the path still held the old bytes after an extraction that was supposed to
  replace it.

The trade-off is not free: a replacement through a new file costs a temp file
beside the target and a rename, and on Windows `MoveFileEx` with
`REPLACE_EXISTING` fails where the destination is mapped. That failure is loud
rather than fatal, which is the right side to be on — the Windows behaviour is
unchanged from an in-place write to a mapped image, which also fails.

## Decision

**An extracted native library is replaced by writing the new content to a
sibling temp file and moving it over the destination. Never by opening the
destination for writing.** Both writers obey it: the archive walk, and the debug
build's copy of the core out of the bin folder
(`NativeDependencyExtractor.ReplaceFromFile`), which is the same hazard through
a different door.

- The walk lives in `UI/Logic/NativeDependencyExtractor` so it is dual-compiled
  into `UI.Tests` and runs against a real archive in a temp folder (ADR-0123);
  `DependencyHelper` keeps only the host-aware half — reaching the embedded
  resource and deciding where the debug copy comes from.
- The temp file is a sibling of the destination (the move stays inside one
  filesystem) with a name that cannot be mistaken for a library, since the core
  is found by scanning beside the executable. A failed write leaves the old
  library in place and removes its own temp file.
- The destination's timestamp is set from the archive entry before the move, so
  the guard above still recognises an unchanged member and does not unpack the
  whole archive on every launch.
- The per-member `catch` stays: the archive carries members for several
  platforms plus Satellaview data, and one unreadable member must not stop the
  app from starting.

## Consequences

- Launching a second install, or relaunching, no longer kills a running one, and
  a replacement lands even while the old library is open.
- The rule now exists in two places by necessity — `scripts/replace_file_atomic.sh`
  for the build, this for the runtime — because one is a shell script a release
  runs and the other is C# the app runs on start. Both say the same thing, and
  a change to either should be read against issue #628.
- On Windows, replacing a library that is mapped still fails and is still
  swallowed. That is pre-existing and out of scope here; what changed is that
  the failure is no longer the *only* path, and the file is never edited under a
  mapped image.
