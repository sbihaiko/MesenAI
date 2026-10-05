# ADR-0260: A games folder that answers nothing is not a folder the app opens on

- Status: accepted 2026-10-05 (the rule was put to the user as a choice and he
  picked it: **"Degradar no uso"** — accept any folder as the games folder, and
  never open on one that lists nothing; the change ships with the unit tests
  this decision is pinned by, `UI.Tests/Play/GamesFolderChoiceTests` and
  `UI.Tests/Play/GamesFolderNoticeTests`).
- Date: 2026-10-05
- Related: issue #887, ADR-0123 (the host-free split `UI/Logic/` exists for),
  ADR-0256 Decision 9 (the ROM picker this rule also feeds)
- Supersedes / amends: ADR-0256 Decision 9 — its third amendment, same date,
  narrows three clauses of that decision (the roots, the action row's re-root,
  and the `AddKnownGameFolder` call) from "the configured folder" to "the
  configured folder while it answers something". The narrowing itself lives
  there, beside the clauses it changes; this ADR is the decision it implements.

## Context

A player can designate a games folder, and the app opens on it: the `Open ROM`
dialog starts there, the picker's roots list leads with it, and at startup it is
registered with the core as a known game folder. Two of those guards asked
`Directory.Exists` — the dialog's and the startup registration's — and the third
asked nothing at all: the picker led with the raw path whenever
`OverrideGameFolder` was set.

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
the app does not open there.**

What happens instead is **not** one thing, and the first version of this ADR said
it was: it claimed a fallback to the last game's folder everywhere. The fallback
is `StartFolder`'s, and `StartFolder` is the **file dialog's** — the dialog is the
one place with a single "where to start" answer to give. Everywhere else the
outcome is the plainer one: the folder is simply not used (`Usable` returns null,
so it is not a root in the picker and not registered with the core), and nothing
else takes its place. The fourth review of PR #894 found the headline and the cost
bullet below still making the everywhere-claim after the string it came from had
been corrected — this paragraph is what both of them mean.

- The rule is `UI/Logic/GamesFolderChoice` — BCL and a path, nothing else — so
  `UI.Tests` runs it against real temp folders (ADR-0123). `HasEntries` enumerates
  rather than counting (it runs on every `Open ROM`), and answers false for a path
  that is missing, unreadable, or not a directory at all, because none of those is
  somewhere to open on.
- `StartFolder(gameFolder, lastOpenedFolder)` is the precedence, and its only
  caller is `Open ROM`: the designated folder, else the last game's folder, else
  nowhere in particular — the caller decides what that means. The last-opened
  folder is the fallback rather than nothing because a player who has a games
  folder set has almost certainly opened something from it. **The fallback is held
  to the same rule as the setting** —
  the last game's folder can answer nothing too, when the ROM was on a stick that
  is not plugged in, and a fallback that re-creates the dead end it exists to
  avoid is not a fallback.
- Four call sites use it: `ShortcutHandler.OpenFile` (the dialog's start folder),
  `MainWindow.axaml.cs` (the known-game-folder registration at startup),
  `PlayerRomPickerViewModel.GamesFolder` (which the roots list and the action
  row's "already the games folder" test both read), and
  `PlayerRomPickerViewModel.MakeGamesFolder` — the fourth, and the one the review
  of PR #894 caught: the press that designates an *empty* folder saved the setting
  and then rebuilt the roots from the raw path, so the same press that stored the
  dead end was the one that started leading on it.
- **The setting is never changed.** The rule chooses where to open; it does not
  clear, refuse or rewrite `Preferences.GameFolder`. A folder that answers nothing
  today may answer something tomorrow — a stick that was unplugged, a library
  still being copied — so the moment it holds anything at all it is used again
  with no action from the player. Anything, not any *file*: a library laid out
  one folder per console is a games folder with no game file in it, and
  `GamesFolderChoiceTests` pins that a folder of subfolders answers.

## Consequences

- The dead end is gone from all four entry points. The player who designates an
  empty folder keeps the setting but is told which of the two things happened:
  the "Saved — this is now your games folder" notice becomes
  `RomPickerGamesFolderEmpty`. The action row stays, which is consistent with
  that message and was not before — the save is real, the folder is simply not
  the one in use yet.
- **That notice says only what holds on every surface that can show it, and the
  first version did not.** It read *"…so MesenAI will keep opening where you last
  played"*, and the third review of PR #894 traced the two ways that is false.
  The picker is one: it always opens on its roots, and a folder that answers
  nothing is simply not among them — the last-played folder was never the
  picker's fallback, so the sentence was untrue on the very sheet that displays
  it. And `StartFolder` returns nowhere in particular when the last game's folder
  answers nothing too — the ROM was on a stick that is not plugged in — which
  `ShortcutHandler.OpenFile` passes on as a null `SuggestedStartLocation`, the
  native dialog's own default rather than the last game's folder. The notice now
  reports the outcome of the save — the folder is kept, and it is not the one in
  use — which is true on all four entry points and still explains why the action
  row did not go away.
- **The press has to re-claim the pad's focus by hand, and that is not obvious.**
  `MakeGamesFolder` replaces the rows, which takes the container the ring was on
  with it, and the sheet's focus arbiter watches `IsVisible`, `PathText` and
  `SuggestionRevision`. A folder that answers nothing is deliberately not made a
  root, so `PathText` reads the same shortened path before and after — the signal
  the one-outcome version leaned on does not move. **Without the bump the pad is
  left with nothing focused** — the direction keys and Confirm return immediately
  because there is no control to act on, and only Back still works, out through
  the window rather than through the sheet. That was the state of the first fix,
  not of this one: `SuggestionRevision++` is the signal that exists for exactly
  this, and the second review of PR #894 traced the ring rather than the rule to
  find that it was missing.
- **A legitimately empty games folder is no longer led with either.** That is the
  cost of the rule and it is deliberate. In the picker the folder loses its
  `Your games` root — the last-played folder is *not* substituted for it there —
  and `Open ROM` starts at the last game's folder only when that folder answers
  something too, otherwise at the dialog's own default. The roots list is usually
  shorter by one, but not always: `PlayRomPicker.Roots` dedupes by path, so a
  games folder that is also the app's own ROM folder or a volume is still a root,
  under that other label. The alternative was telling an autofs node apart from an
  empty folder, which is not knowable from the path.
- The guard is now one function instead of two inline `Directory.Exists` calls —
  and a third entry point that asked nothing at all — so the next entry point that
  needs it asks the same question.
