# ADR-0257: An update reconciles the Exec key of an existing desktop entry, and only the keys this writer owns

- Status: accepted 2026-10-05; amended 2026-10-05 (group scoping, the `Exec =` repair and the null-path rule) after an independent review of the first version found that reconciling "every line that starts with `Exec=`" rewrote a `[Desktop Action …]` group's own `Exec=`, which this ADR's own "keys the user adds are safe" claim said would not happen. Reflected in the code and the tests in the same change: `LinuxFileAssociation.ReconcileDesktopEntry` and `UI.Tests/Config/LinuxFileAssociationTests.cs`.
- Date: 2026-10-05
- Related: issue #882 (the report), PR #879 / ADR-0256's sibling `LinuxFileAssociation` work (#862, #870, #877 — the Exec quoting rules), ADR-0125 (UI/Logic is the dual-compiled, test-facing tree)
- Supersedes / amends: nothing. This decides a question #877 deliberately left open.

## Context

`FileAssociationHelper.UpdateLinuxFileAssociations` calls `CreateLinuxShortcutFile` only when `mesen.desktop` is absent; when the file exists it calls the update path instead. The update path rewrote `MimeType=` and nothing else, so every other key — `Exec=` included — was carried through verbatim.

Two consequences, both silent:

- **The #877 quoting fix could never reach anyone who had already run Mesen.** The file existed, so the create path was skipped, and the invalid `Exec=` the older build had written stayed. Upgrading did not repair it; the ROM double-click stayed dead.
- **A stale `Exec=` outlives the executable moving.** An AppImage unpacks into a folder that changes between runs, so the entry points at the previous one and the shortcut launches nothing.

The obvious fix — rewrite `Exec=` on update — has a cost the create path does not: the update path writes to a file the user may have edited. Someone who renamed the entry, or added `--fullscreen`, or added a key of their own, would lose that.

## Decision

**An update reconciles the two keys this writer owns — `Exec=` and `MimeType=` — and carries every other key through untouched.**

- Both keys are reconciled **inside the `[Desktop Entry]` group only**, and a key the entry lacks is appended there — never at the end of the file, which would file it under whichever group is last. A `.desktop` file may hold more than one group, and a `[Desktop Action …]` group carries its own `Exec=` for a different command line: that key is the user's, not this writer's, and reconciling "every line that starts with `Exec=`" would replace the action with a second copy of the application command and drop its arguments. A file with **no `[Desktop Entry]` group at all** is not loadable as it stands, so the update gives the keys that group rather than appending them under the last one.
- Within the group, each key is replaced **where it stands**, so a second `Exec=` never appears for a loader to choose between. A line writing the key as `Exec =…` — whitespace the desktop-entry grammar does not allow, so a key no loader reads — is recognised and rewritten rather than left beside a second `Exec=`.
- The file is ours to maintain, not ours to overwrite: `Name=`, `Comment=`, `Icon=`, `Keywords=`, `Categories=` and anything the user added are preserved byte for byte.
- The #877 refusal applies here too. When the executable's path cannot be carried by the Exec key — an `=` in it, or a control character with no escape — the function returns null and the caller **leaves the file exactly as it stands**, rather than replacing a working entry with one no loader accepts. As in the create path, the reason is logged: the failure is otherwise silent.
- A **null executable path** is not a refusal: it is the caller having failed to read the running executable (`MainModule` resolves through `/proc`). The `Exec=` key is then out of reach while `MimeType=` is still this writer's, and an unknown path must not stop the update — the path this replaced refreshed `MimeType=` without ever asking for the executable, so returning early on a null module would silently stop doing something the update did before.
- The decision lives in `LinuxFileAssociation.ReconcileDesktopEntry`, in the dual-compiled `UI/Logic` tree (ADR-0125), so which keys are rewritten is a return value a test reads — not a branch behind `Process.GetCurrentProcess().MainModule`, which no test can reach. This is the same placement `BuildDesktopEntry` got in #877.

**When this runs**, because it sets the cost below: `MainWindow.OnOpened` calls `UpdateFileAssociations()`, and `ConfigViewModel.SaveConfig()` calls it again. So an existing entry is reconciled **on every launch**, not only when the user edits a preference. That is not a new write frequency — the `MimeType=` line already was rewritten on every launch — it is one more key in a write that already happened.

## Consequences

- A user who hand-edited `Exec=` — adding an argument, say — has that edit reverted **on the next launch**, not merely on the next trip through the preferences dialog. This is the cost of the decision and it is accepted: the alternative is an entry the emulator cannot repair, and the failure of a wrong `Exec=` (nothing launches, with no message) is worse than the loss of a hand-added flag, which the user can re-add. It is also the honest reading of who owns the file — the emulator writes it, names it and points an icon at it; a hand-edit to the one key it manages is not an edit it can honour while still keeping that key correct.
- Keys the user adds that this writer does not own are safe, and the group scoping is what makes that true rather than aspirational: a `[Desktop Action …]`'s own `Exec=` is one of them, and the first version of this reconcile did rewrite it. A future key the emulator starts owning must be added to the reconcile list deliberately; nothing picks it up by accident.
- Nothing here rewrites `mesen.desktop`'s formatting. The file is split and joined on `Environment.NewLine`, as the MimeType-only loop already did, so an entry with mixed line endings keeps them only as well as it did before.
