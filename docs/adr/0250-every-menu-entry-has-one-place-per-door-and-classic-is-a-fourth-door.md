# ADR-0250: Every menu entry has one place per door, and Classic is a fourth door

- Status: accepted (2026-10-03). The user asked whether the whole classic menu makes sense in Play (*"sobre o menu, ele deve ser contextual para as 3 portas (play, remaster, share). faz sentido TODAS essas opções pra o Play?"*), then asked for this ADR with the per-door table from the conversation (*"sim, escreve a ADR com essa tabela"*). They replaced the table's "every door" row with a door of its own (*"a porta pode ser "Classic" e não "Todas""*), chose *"4ª porta no switcher (Recomendado)"*, and set the default (*"a porta padrão é a Play"*). They answered the open question and accepted the first draft (*"1. o classic pode ser a GUI original. 2. pode atualizar. No mais eu aceito o ADR"*). After a review of every menu surface they accepted the revised table and extended the no-duplicates rule to Classic (*"sobre os menus aceito sua recomendacao, porém resolva o mesmo item em mais de um lugar mesmo que na versão clássica, sacou?"*). The renders W-S2 and W-S3 and PRD Part B §13 are updated with this ADR; the code is a pending PRD Part B slice and waits for a go-ahead.
- Date: 2026-10-03
- Related: PRD Part B §13 (W-S1 shell, W-S2 Tools ⋯ dropdown, W-S3 switcher, W-P4 pause overlay, W-R1 project screen, W-H1 Share home); ADR-0241 (Play, Remaster and Share workspaces); ADR-0249 (the Player GUI follows its renders); ADR-0243 (the Remaster project folder); ADR-0245 (Play's cheats).
- Supersedes / amends: amends PRD Part B §13 W-S2 (its "Unchanged content… Nothing is removed from them in this proposal" no longer holds: each door has its own Tools ⋯) and W-S3 (four doors instead of three). Amends ADR-0241 twice: *"Advanced tools are an escape hatch, not a fourth audience"* becomes the Classic door, and *"the existing `UiMode` values must not be silently reinterpreted as the new workspace values"* gives way to Decision 2 below, which ties `UiMode.Advanced` to Classic explicitly. The *Show classic menu bar* toggle, its "your menus are under Tools ⋯" toast (PRD Part B §13.8 Q4) and the *Advanced GUI* entry go away. ADR-0241's other rules stand: switching keeps the game running, and opening a specialist tool needs no restart.

## Context

G.1 (ADR-0241) moved the classic menu under Tools ⋯ unchanged. W-S2 approved exactly that: File, Game, Options, Tools, Debug and Help as they always were, plus *Show classic menu bar*. In Player mode, therefore, every workspace carries the whole emulator menu.

Play, the workspace for someone who just wants to play, ends up offering:

- the debugger, its viewers and Lua;
- the HD Pack Builder, *Reload pack images* and the log window;
- the video, sound and music recorders;
- the tape recorder, VS *Insert Coin* and barcode input, even for a game that uses none of them;
- loose technical options: video filter, region and per-console settings.

Remaster is already partly contextual: it adds *Show Project Folder*, *Switch Project…* and *Compose a Scene…*.

A full review of every menu surface (2026-10-03) found more than 130 leaf items under Tools ⋯ in Player mode. It also found the same action in two places: the pause overlay already holds Save states, Cheats, Pack, Enhancements, Settings and Quit game; the Remaster project chip already holds *Show Project Folder*, *Switch Project…* and *Compose a Scene…*; the Share home already holds *Record and share*. Tools ⋯ also opens classic windows from Player flows, which ADR-0249 removed everywhere else. Inside the classic menu itself, *Install HD Pack* and *Enhancement Packs* both install a pack, *Record* and *Record and Share* both start a movie, and on macOS About, Preferences and Exit belong in the system app menu, which is empty today (`App.axaml`).

The user's answer is to give the full emulator its own door, **Classic**, next to Play, Remaster and Share. The three task doors each get a short, relevant menu. Everything Mesen has is always one switch away.

Non-goals:

- No feature, window or keyboard shortcut is removed from the app. A shortcut keeps working in every door even where its menu entry is hidden.
- Advanced mode keeps the full classic menu: it becomes the Classic door (Decision 2), with only its duplicates removed (Decision 4).
- No change to the theme (ADR-0249) of the menu's first level.
- The rule is about entries: menu items, the pause overlay's rows, the Remaster project chip and the workspace homes' buttons and lists. A settings window that also exposes a value (the Video tab next to a scale menu) is not a second entry.

## Decision

1. **One place per door.** Within a door, every action has exactly one entry across all its surfaces: Tools ⋯ (or Classic's menu bar), the pause overlay, the project chip and the workspace home. When a surface already holds an action, the menu does not repeat it. Keyboard shortcuts are not entries; every shortcut works in every door, even where no entry shows it.
2. **Four doors; Classic is the original GUI.** The W-S3 switcher lists Play (⌘1), Remaster (⌘2), Share (⌘3) and **Classic** (⌘4); Ctrl instead of ⌘ on Windows and Linux. Classic's subtitle reads "Every menu, debugger, Lua, HD Pack Builder." Classic is the original Mesen GUI: the in-window classic menu bar, the classic styles (MesenStyles), the plain game view, no Play overlay sheets, and Esc as classic Mesen has it. Entering Classic sets `UiMode.Advanced`; leaving it for a task door sets `UiMode.Player`. `Workspace.Classic` is a new persisted value. **Play is the default door**: a fresh install, a settings file without a workspace and an unknown value all open in Play. An upgraded install whose `UiMode` is Advanced opens in Classic, so nobody loses the GUI they chose.
3. **The task doors' Tools ⋯:**

| Door | Tools ⋯ | Lives elsewhere in the door, so not in ⋯ |
|---|---|---|
| **Play** | Reset · Power Cycle · console items only when the loaded game uses them (FDS disk select/eject, VS *Insert Coin*, barcode, tape) · Screenshot · Fullscreen | *Open a ROM…* and the recent games (Play home, W-P1/W-P2; ⌘O works everywhere, and *Quit game* returns to the home); Pause/Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game (pause overlay, W-P4) |
| **Remaster** | *Reload pack images* · *Record Music* · Enhancement Packs · Log window | *Show Project Folder*, *Switch Project…*, *Compose a Scene…* (project chip, W-R1); Record and Build (project screen) |
| **Share** | *Play a Replay…* · *Record* ▸ (video, sound) · Netplay ▸ · Screenshot | *Record and share*, *Share a pack* (Share home, W-H1) |

   Each task door ends with the same tail: *Settings…* (the W-P8 sheet), *Help* ▸ (Check for Updates, Command Line), *About MesenAI*, *Quit MesenAI*. On macOS, *About MesenAI*, *Settings…* ⌘, and *Quit MesenAI* ⌘Q live in the system app menu instead, and the tail keeps only *Help* ▸. Classic is a door, so it appears only in the switcher (W-S3), never as a menu entry. No task-door entry opens a classic window: each one opens its Player sheet, or is not offered in that door.
4. **Classic loses its duplicates and nothing else:**
   - *Install HD Pack* merges into *Enhancement Packs*, whose window already installs a pack.
   - *Movies* ▸ keeps *Play*, *Record* and *Stop*. *Record and Share* is Share's.
   - On macOS, *About*, *Preferences* and *Exit* move from Help, Options and File to the system app menu.
   - The four Pause/Resume variants become one entry whose label follows the state (one visible today too; this is a code simplification).
   - The switcher appears once, as a *Workspace* ▸ menu (Play ⌘1, Remaster ⌘2, Share ⌘3) in the classic menu bar. The shell bar's pill is not shown in Classic.
   - The Super Game Boy viewers keep both entries but are labelled "(Game Boy)", so the same name never appears twice.
   - Two dead Help entries (*Online Help*, *Report Bug*, `IsVisible = () => false`) and the disabled hint row under Tools ⋯ are deleted.
5. **The rule is pure logic.** A host-free rule in `UI/Logic/` (for example `WorkspaceMenu`) maps door, platform and loaded-game capabilities to the visible entries. It is unit-tested in `UI.Tests`: one case per table cell, the console-capability cases, a guard that fails when a menu action belongs to no door, and a guard that fails when one door lists the same action twice across its surfaces.
6. **Hiding is presentation.** `MainMenuViewModel` keeps building the actions; a door only selects which entries show. Enablement behaves as today.

## Consequences

- **W-S2 and W-S3 are redrawn** (`scripts/render_gui_wireframes.py`): W-S2 shows Play's Tools ⋯, W-S3 shows four doors. Remaster's and Share's menus are specified by the table and have no render of their own.
- **`Workspace` gains a value, and Classic owns `UiMode.Advanced`.** Persisted settings, the switcher, ⌘-digit handling (`WorkspaceShell.FromShortcutDigit`), the status line and every `IsPlayWorkspace`-style gate must handle Classic, and every place that flips `UiMode` today must go through the door.
- **One more rule to keep in sync.** Every new menu action must be assigned to a door; the guard tests enforce it, and the duplicate guard keeps a new overlay row from shadowing a menu entry.
- **Headless tests change.** Tests that reach a specialist entry from Play's Tools ⋯ must switch to Classic first (`WorkspaceShellTests`, the Remaster and Share menu tests). The *Show classic menu bar* and classic-menu toast tests are deleted with the feature.
- **PRD Part B §13 changes** with this ADR: W-S2's prose, W-S3's row list, §13.6's switching rules and §13.8 Q4.
