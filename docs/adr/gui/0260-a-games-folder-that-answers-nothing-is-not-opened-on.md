# ADR-0260: A games folder that answers nothing is not a folder the app opens on

- Status: accepted 2026-10-05 (the rule was put to the user as a choice and he
  picked it: **"Degradar no uso"** — accept any folder as the games folder, and
  never open on one that lists nothing; the change ships with the unit tests
  this decision is pinned by, `UI.Tests/Play/GamesFolderChoiceTests`).
- Date: 2026-10-05
- Related: issue #887, ADR-0123 (the host-free split `UI/Logic/` exists for),
  ADR-0256 Decision 9 (the ROM picker this rule also feeds)

## Context

A player can designate a games folder, and the app opens on it: the `Open ROM`
dialog starts there, the picker's roots list leads with it, and at startup it is
registered with the core as a known game folder. Every one of those guards asked
`Directory.Exists`.

That is not the question they meant. A path can be a directory and hold nothing,
and on macOS that is not a corner case — `/home` is an **autofs node**:
`isdir` true, `listdir` empty, declared as `/home auto_home -nobrowse,hidefromfinder`
in `/etc/auto_master`. Measured on the requesting machine. And it is reachable:
the picker's This Mac root (`/`) offers a folder row named `home`, and its action
row will happily make that the games folder.

The result is a dead end the player cannot leave from inside the app. `Open ROM`
opens on an empty listing; the picker leads its roots list with `Your games` =
`/home`; the core has a known folder that is empty. Nothing errors, because
nothing is wrong in the sense the code checks for.

Three ways out were put to the user:

1. **Degrade at use** — accept any folder as the games folder, and never open on
   one that lists nothing.
2. **Refuse at save** — only accept a games folder the app can actually list.
3. **Refuse symlinks** — refuse any games folder that is a symbolic link.

He picked (1). It is the only one that is wrong about nothing: (2) needs
platform-specific knowledge of autofs mounts to know *why* a folder is useless,
and anything short of that refuses a folder that is legitimately empty today and
full tomorrow; (3) refuses a symlinked ROM library, which is an ordinary way to
set up a cabinet on Linux.

## Decision

**A folder is a candidate to open on only when it holds something. Where the app
would have opened on the configured games folder and that folder answers nothing,
it falls back to the folder holding the last game the player opened.**

- The rule is `UI/Logic/GamesFolderChoice` — BCL and a path, nothing else — so
  `UI.Tests` runs it against real temp folders (ADR-0123). `HasEntries` enumerates
  rather than counting (it runs on every `Open ROM`), and answers false for a path
  that is missing, unreadable, or not a directory at all, because none of those is
  somewhere to open on.
- `StartFolder(gameFolder, lastOpenedFolder)` is the precedence: the designated
  folder, else the last game's folder, else nowhere in particular — the caller
  decides what that means. The last-opened folder is the fallback rather than
  nothing because a player who has a games folder set has almost certainly opened
  something from it.
- Three call sites use it: `ShortcutHandler.OpenFile` (the dialog's start folder),
  `MainWindow.axaml.cs` (the known-game-folder registration at startup), and
  `PlayerRomPickerViewModel.GamesFolder` (which the roots list and the action
  row's "already the games folder" test both read).
- **The setting is never changed.** The rule chooses where to open; it does not
  clear, refuse or rewrite `Preferences.GameFolder`. A folder that answers nothing
  today may answer something tomorrow — a stick that was unplugged, a library
  still being copied — so the moment it holds a file it is used again with no
  action from the player.

## Consequences

- The dead end is gone from all three entry points. The player who designates an
  empty folder sees the picker still offering to make it the games folder, which
  is honest feedback that it did not take; a message explaining why would be
  better and is not in this change.
- **A legitimately empty games folder is no longer led with either.** That is the
  cost of the rule and it is deliberate: a fresh player who has just created an
  empty library gets the last-opened folder rather than their new empty one. It is
  the same trade the picker's own rows already make, and the alternative was
  telling an autofs node apart from an empty folder, which is not knowable from
  the path.
- The guard is now one function instead of three inline `Directory.Exists` calls,
  so the next entry point that needs it asks the same question.
