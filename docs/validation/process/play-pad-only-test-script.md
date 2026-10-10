# Play door — pad-only manual test script (windowed and full screen)

Refs ADR-0256 (the Play GUI is fully operable from a controller alone), issue
#926 check 5a (the real-gamepad pass the automated run could not make). This
is a **manual test script** for one person holding a gamepad; it changes nothing
in the product. It complements, and does not repeat,
`docs/validation/slices/issue-926-player-gui-real-display-validation-2026-10-07.md`:
that run compared renders against wireframes and left "5a · real gamepad pass"
as not-automatable. This script is that pass.

Budget: about 90 minutes for one pad on one display, both window modes
(passes 1 and 2). Cases that need P-B (P4-04, LOSS-03, LOSS-04, section 4.11)
run in pass 3, about 15 minutes more.

## 1. Purpose and scope

**Question answered:** can a player with a controller and nothing else —
no keyboard, no mouse, no trackpad — operate every surface of the Play door,
in a window and in full screen, without getting stuck? That is ADR-0256's
stop rule as PRD W-P18 states it
(`docs/roadmap/PRD-mesence-enhancement-ecosystem.md:2468`: "every Play path
in [the] Esc order is reachable and reversible from a pad alone, the focus
ring is visible on every control the pad can land on"), read on a real display
with a real pad. The Esc order itself (game → W-P4 → resume) is ADR-0241's.

**In scope:** the Play door only — Home (W-P1/W-P2), the library sheet
(ADR-0264, W-P19), the pause overlay W-P4 and every sheet it opens (Save
states, Pack, Enhancements, Cheats, Settings with its five tabs, the
Controller sheet of ADR-0255, Quit game), the ROM picker's *Browse a file…*
(ADR-0256 Decision 9), the on-screen keyboard (ADR-0262), the pad port lamps
(ADR-0261), the focus-loss and pad-loss pauses (ADR-0254), interface size
(ADR-0269), favorites (ADR-0268), menu sounds (ADR-0270) and the haptic tick.

**Out of scope:** Classic, Advanced, Remaster and Share (ADR-0256 non-goals;
PRD Part B §13.3 rule 9 lets Remaster and Share assume a mouse), the debugger,
and the classic `ConfigWindow`. A step that lands on one of those is recorded
as a finding, not tested further.

**Platform:** macOS arm64 is the product platform. Windows and Linux pad paths
are follow-ups (ADR-0255 §"The answers, against the code": no VID:PID on
macOS and XInput, #813 on Windows; ADR-0256 Decision 5: no default chord for a
DirectInput joystick). Run this script on macOS first; a Windows/Linux run
reuses it and marks the platform in the sheet.

## 2. Setup

### 2.1 Build and launch

1. Check out `origin/main` (or the branch under test) and note the SHA:
   `git rev-parse --short HEAD`.
2. `make core` then `make ui` (no `-j`, Make 3.81 hangs on `-j` for the UI).
   The bundle is `bin/osx-arm64/Release/osx-arm64/publish/Mesen.app`; after
   `make ui` confirm the bundle's `MesenCore.dylib` is the fresh one (the
   bundler is known to leave a stale dylib).
3. Start from a **fresh settings folder** so the first run is tested
   (ADR-0256 Decision 8). The home folder is resolved by
   `UI/Config/ConfigManager.cs` (`HomeFolderName` = `MesenAI`, legacy
   `MesenCE` then `Mesen2`, lines 27-29; resolution at lines 215-226): a
   `settings.json` **next to the executable** wins (portable mode), otherwise
   the first of the documents folders that holds settings. So:
   - move `~/.config/MesenAI`, `~/.config/MesenCE` and `~/.config/Mesen2`
     aside (e.g. append `.bak`), and put them back after the run;
   - confirm there is **no** `settings.json` next to the executable (inside
     `Mesen.app/Contents/MacOS/`); if there is, move it aside too.
4. Have at least three ROMs in a folder the library can scan (one NES, one GB,
   one SMS is ideal — the console filter needs two consoles present) and, for
   the large-list case, a folder with 100+ ROMs. Have at least one ROM with
   a community pack that auto-installs, so the pack picker/detail rows have
   content. For GAME-04, an FDS image (with `disksys.rom` **not** installed).
5. **ROM placement for LIB-01.** A fresh first run seeds `GameFolder` to the
   app's default games folder, which is empty. Before setting the keyboard
   aside, copy the three-console set into that default folder (or plan to add
   your ROM folder by pad in LIB-06 and run LIB-01 after it). Record which on
   the sheet.

### 2.2 Pads

Run with **at least**:

- **P-A:** one XInput-style pad (Xbox layout: A confirm, B back — ADR-0256
  Decision 4, `PadNavControls`), **wired**.
- **P-B:** one PlayStation-layout pad (Cross confirm, Circle back) if
  available, **Bluetooth**.

Record make/model and connection per pad on the sheet. If only one pad is
available, run everything with it and mark the P-B rows BLOCKED (reason:
"no second pad").

### 2.3 Guaranteeing "pad only"

- Before the run, **unplug** the mouse and trackpad if external, and push the
  keyboard out of reach. On a MacBook, close the lid and use an external
  display, or lay a sheet of paper over the keyboard and trackpad; the point
  is that touching them is a deliberate act you will notice.
- The **only** permitted keyboard/mouse touch is to recover from a FAIL
  (case says BLOCKED or FAIL first, then recover). Write the recovery into the
  notes.
- A step that cannot be completed without keyboard or mouse is a **FAIL** of
  that case, even if a workaround exists.
- Do not use the macOS menu bar, Dock or hot corners.

### 2.4 Recording sheet (fill before the first case)

| Field | Value |
|---|---|
| Date | |
| Build SHA | |
| OS and version | |
| Display (size, resolution, distance from couch) | |
| Pad P-A model / connection | |
| Pad P-B model / connection | |
| Fresh settings folder? (yes/no) | |
| Interface size at start (Standard) | |
| Tester | |

Verdict vocabulary per case: **PASS**, **FAIL** (observed deviation from the
expected result), **BLOCKED** (could not reach the precondition; say why).
One line of notes per case is enough; put details in the defects log (4.15).

## 3. Test matrix

A case runs once windowed (**[W]**, the app's own default size) and once in
**full screen** (**[F]**) unless it is marked otherwise: a `—` in a column
means the case does not run in that mode (**[W]-only** or **[F]-only**).
Enter full screen by pad (case FS-01) before the [F] pass. Interface size
is **Standard** for both passes except where the case says otherwise (SET-02).
Cases tagged **(pass 3)** need P-B and are skipped in passes 1 and 2.

| Pass | Window | Interface size | Pad | Cases |
|---|---|---|---|---|
| 1 | [W] | Standard | P-A | every case without `—` in [W], except pass-3 cases |
| 2 | [F] | Standard | P-A | every case without `—` in [F], except pass-3 cases |
| 3 (+15 min) | [W] | Standard | P-B (plus P-A for two-pad) | P4-04, LOSS-03, LOSS-04, PAD2-01…PAD2-04 |

Mark each case's box per pass, `[W] __ [F] __`.

## 4. Cases

Pad names below follow the Xbox layout; read A/B as Cross/Circle on P-B.
"Chord" means Select+Start pressed together (Back+Start on XInput), the
default `ToggleOverlay` binding (ADR-0251 §3, ADR-0256 Decision 5 — no
platform reports a Home/Guide button yet).

### 4.1 Launch, Home, first focus

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| HOME-01 | Fresh settings folder, app not running | Launch the app from the Dock by any means **before** setting the keyboard aside (the OS launch is not under test). Then, pad only: look. | No wizard (ADR-0256 Decision 8). Home W-P1 is on screen: "Drop a game here…", one control *Open a ROM…* **already focused**, ring visible. Pad port lamps show P1 lit (ADR-0261). | | — | |
| HOME-02 | HOME-01 | Press D-pad Up, Down, Left, Right once each; then press A on *Open a ROM…* (the one focused control); then B. | Focus stays on *Open a ROM…* through the four presses; the ring never disappears; A opens the library sheet (LIB-01); B on the sheet returns to Home with the ring back on *Open a ROM…*. | | | |
| HOME-03 | At least one game played (after GAME-01), back on Home W-P2 | D-pad through every Home element: *Continue playing*, the Favorites shelf (if any), the Recent grid, *Open a ROM…*. | Every element reachable; focus order Continue → Favorites → Recent (ADR-0268 Decision 6); the ring is on exactly one control at a time (ADR-0256 Decision 3). | | | |
| HOME-04 | HOME-03 | From every Home control, press D-pad Up repeatedly, then Left/Right at the top row. | Focus **never** leaves the Home host onto the header's Profile button or the Tools ⋯ button (#1137, fixed in #1166: `PlayHomeHost` contains the walk; `KnownFocusLeaks` is empty at HEAD). If it does: FAIL and record which control, then press Down/B to return. | | | |
| HOME-05 | Home | Press the chord on Home (no game). | Nothing harmful: no overlay opens for a game that is not loaded; focus unchanged. | | | |
| HOME-06 | Home, **no game loaded** | Try to reach Settings by pad only. | **Expected FAIL (known gap, P0-2; to be filed as bug #1177):** Settings opens only from W-P4 (needs a game) or from Tools ⋯ / the macOS app menu (`UI/Windows/MainWindow.PlaySheets.cs:159-164`); Home has no Settings control (`UI/Views/PlayHomeView.axaml`); and the header's Tools ⋯ is deliberately unpadded since #1166 (`UI/Windows/PlayPadNavigationWiring.cs:329-345`). Record exactly what you tried. | | | |

### 4.2 Library (Open a game, ADR-0264)

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| LIB-01 | Home, ROMs placed per §2.1 step 5 (or a folder added in LIB-06 first) | A on *Open a ROM…*. | The flat library grid opens with cover tiles, header "Your library · N games in M folders", a focused tile with the ring, and a footer naming A/B/Y/X/LB/RB in the pad's own words (ADR-0256 Decision 6). While scanning, an animated indicator shows. | | | |
| LIB-02 | LIB-01, a library of 100+ ROMs | Hold D-pad Down for 3 s, then tap it 5 times; same with Right. | Held direction repeats after a short delay (400 ms, then every 100 ms, `PadNavRepeat`) — ADR-0256 Decision 7; taps step once each. The grid scrolls to keep the focused tile visible; no tile is skipped; no runaway after release. | | | |
| LIB-03 | LIB-01 | Up from the top grid row; Left/Right across the header row; Down back to the grid. | Up enters the header row (search, *Library folders…*, *Browse a file…*, Back); Left/Right move between them; Down returns to the grid (ADR-0264 Decision 3). | | | |
| LIB-04 | LIB-01 | RB repeatedly until the filter comes back to All; then LB repeatedly the same way. After each press, watch where the ring is. | One option per press: All → each console present → All, wrapping at both ends (`PlayerRomPickerViewModel.ConsoleFilter.cs`); LB walks the same ring backwards; the grid narrows; never lands on an empty filter. After the press the ring is **on the chip row**, on the segment the press selected (ADR-0264 amendment 2026-10-09, #1108): the row is one element, so its Left/Right step the same ring one console at a time, its Up leaves for the header and its Down returns to the grid it filters, and the footer names the shoulders and no Play (ADR-0256 Decision 6). The landing is skipped only where the sheet's own claim outranks it — while a restore is in flight (`IsRestorePending`/`IsRestoreLanding`, the ring lands on the remembered game) or while the search box keeps the ring. Outside those two, a ring that does not reach the row is a FAIL. Record whether the segment the ring is on is obvious at couch distance. | | | |
| LIB-05 | LIB-01 | Press Y (the ring moves to the search field), then A opens the on-screen keyboard (`PlayPadNavigationWiring.cs:812-828`). Type `zel` (or a prefix of a title you own) on the on-screen keyboard with D-pad + A; press OK. Then Y, A again, press B. | Y focuses the field and A opens the shared on-screen keyboard (ADR-0262) below it; the grid filters as each letter lands; OK commits and the focus returns to the field with its ring. The second time, B **cancels**: the field returns to its previous text and the focus returns to it (Decision 4). The sheet does **not** close under the keyboard. An empty result shows "No games match" with a way to clear it, never an empty grid. | | | |
| LIB-06 | LIB-01 | Up to the header, A on *Library folders…*. Add a folder, remove it, add it back, B out. | The pad-reachable folder list opens (ADR-0264 Decision 8), not a native folder picker. Add/remove by pad; the header count updates; B closes it back to the library. A native OS dialog here = FAIL. | | | |
| LIB-07 | LIB-01 | A on *Browse a file…*; walk one folder down and up; try A on *Make this my games folder* inside a non-empty folder; B from the first list. | The folder browser of ADR-0256 Decision 9 opens; Confirm descends, B ascends; the action row leads the list but the ring never **lands** on it first (first-row guard); B on the root dismisses with no load. | | | |
| LIB-08 | LIB-01, online | Look at tiles for well-known commercial games; then at a hack/homebrew. | Box art appears for known ROMs (lazy, visible tiles only); a generic console-colored cover carries the title for unknown ones; nothing waits on the network. Box art is a visual, not a pad, check — record only if it blocks focus or scrolling. | | | |
| LIB-09 | LIB-01 | B from anywhere in the grid. | Back to Home with the ring on the control that opened the sheet. | | | |

### 4.3 Favorites (ADR-0268, X)

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| FAV-01 | LIB-01, a tile focused | Press X; B to Home; look; A into the library, X on the same tile; B. | X toggles Favorite (the footer's X entry reads *Favorite* / *Unfavorite*); Home now shows a Favorites shelf between Continue and Recent; unfavoriting removes it and the shelf hides when empty. Newest favorite first. | | | |
| FAV-02 | Home with a Favorites shelf | X on a Home favorite tile; X on the *Continue* card. | X acts on the focused cover; on Continue it favorites the Continue game (ADR-0268 Decision 1). X elsewhere (e.g. *Open a ROM…*) does nothing. | | | |

### 4.4 Opening and playing a game

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| GAME-01 | LIB-01 | A on a tile. | The sheet closes; a load card with a moving indicator; the game runs. For the first three starts the entry toast ends with the menu hint naming the pad's chord — e.g. "· Select+Start for the menu" (ADR-0251 §2), not "Esc". | | | |
| GAME-02 | GAME-01, game running | Play for 10 s with the D-pad and buttons. | The pad is the console's (ADR-0256 Decision 1): no menu sound, no focus ring, no haptic tick, nothing in the GUI reacts. Status bar and lamps hidden (ADR-0261 Consequences). | | | |
| GAME-03 | A `.zip` holding several ROMs | A on the archive tile. | The "which game" sheet (`PlaySelectRomSheet`) opens; D-pad + A picks; B cancels back. | | | |
| GAME-04 | An FDS image, `disksys.rom` not installed (`Core/Shared/FirmwareHelper.h:27`; GB/SMS need no BIOS here and GBA is not a product console) | A on it. | The BIOS sheet (W-P13) shows with Cancel reachable by pad; **expected FAIL for *Choose File…*** — it is native (ADR-0256 Decision 9 refusals). Record that B/Cancel still backs out cleanly and the status line names the missing BIOS. | | | |

### 4.5 Pause overlay W-P4

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| P4-01 | Game running | Press the chord. | W-P4 opens, game paused, frozen frame behind, seven controls (Resume, Save states, Pack, Enhancements, Cheats, Settings, Quit game), *Resume* focused, footer naming "B to resume" in words for the pad in hand (ADR-0256 Decision 6: "Circle", never a glyph). Bar and lamps visible. | | | |
| P4-02 | P4-01 | Chord again; then chord, B. | Both close W-P4 and resume (ADR-0241 Esc order: game → W-P4 → resume). | | | |
| P4-03 | P4-01 | D-pad Down through all seven rows and back Up; Left/Right. | Every row reachable, ring visible on each, no wrap into the header, no dead end. | | | |
| P4-04 **(pass 3)** | P4-01, with P-B | Switch to the other pad mid-overlay and press any direction. | The footer re-reads for the pad now in hand (Decision 6: recomputed on device change). | | | |
| P4-05 | P4-01 | A on *Save states*; walk the slot grid; save to a slot; load it; B. | The Save states sheet opens its grids; a slot can be saved and loaded with D-pad + A; B closes back to **W-P4**, not to the game. *Shared replays…* (if shown) opens and B backs out. | | | |
| P4-06 | P4-01, a pack installed | A on *Pack*. With one pack, also A on *Show pack folder*, then try to continue by pad. | With one pack: pack detail (W-P6) — *Done*, *Show pack folder*, *Details ▸*; with 2+: the picker (W-P5) — radios by D-pad, *Use This Pack* / *Cancel*. B closes back to W-P4. ***Show pack folder*: expected FAIL** — Finder takes the foreground and the pad cannot bring MesenAI back; PASS only if the pad still drives Play afterwards with no keyboard or mouse. | | | |
| P4-07 | P4-01 | A on *Enhancements*; toggle a switch; A on the footer button (Done/Apply/Apply & Reload). | Switches toggle by A; the footer label follows the draft; applying with a reload returns to the game or W-P4 with no mouse. B discards back to W-P4. | | | |
| P4-08 | P4-01 | A on *Cheats*; toggle one; A on *Add a Code…*; fill the code with the on-screen keyboard (Game Genie letters first — code-shaped field, ADR-0262 Decision 2); fill the description (free text); commit; B out. | Every field opens the keyboard on A; the code keyboard shows `APZLGITYEOXUKSVN` first; B cancels a field without closing the sheet; the sheet closes back to W-P4. The API key field (if visible) masks its draft as `•`. | | | |
| P4-09 | P4-01 | A on *Quit game*; B on the confirm; A again then confirm. | The confirm is in place on W-P4, reachable; B = keep playing; confirming powers the game off and lands on Home with the ring placed. | | | |

### 4.6 Settings (W-P8) by pad

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| SET-01 | P4-01 | A on *Settings*; Left/Right on the tab strip through Display, Look, Audio, Controls, System; Down into each page; Down to *Done*; Up from Done. | Every tab and every row reachable; *Done* always reachable. On **Audio and Controls only**, Up from Done reaches *More in Options…* and Up again the page (`PlayPadNavigationWiring.cs:1080-1097`, `ConfigViewModel.IsPlayerMoreTab`); on the other tabs Up from Done goes to the page. *Exit full screen* is Left of Done, not above it (FS-02). | | | |
| SET-02 | SET-01, Display tab | Focus *Interface size*; Right to Large, Right to Extra large; Left back to Standard. At each size, Down to Done and back. | Values step in place and apply at once (ADR-0269 Decision 1); only Play chrome scales, never the game picture; at Extra large the rows **scroll** and *Done* stays pinned and reachable, nothing clipped (Decision 6, guaranteed at 1024x640 and 512x505 for the settings sheet). Record legibility at couch distance per size in the rubric. | | | |
| SET-03 | SET-01, Audio tab | Find *Menu sounds*; toggle on with A; move the D-pad, confirm, back. | Row visible when the host audio path exists (ADR-0270, #1126; hidden otherwise — record which). On: a soft blip on move/confirm/back at a fixed low level, never while a game runs unpaused (`MenuSounds.ShouldPlay`). The game's own audio is unaffected (own output stream, ADR-0270). | | | |
| SET-04 | SET-01, Controls tab | Read the rows: Rumble slider, Deadzone slider, *Menu tick*. Set Rumble to 0 then back; toggle *Menu tick*; move the D-pad. | *Menu tick* row shows only when the core says the pad is aimable (`MenuTickAvailable`; on macOS with a rumble-capable pad). With Rumble 0 the switch is disabled with the reason text. On: a short tick on each focus move, never while a game runs unpaused (`HapticTickRule.ShouldTickOnMove`). On a pad without rumble, mark N/A, not FAIL. | | | |
| SET-05 | SET-01, Controls tab | Move the *Deadzone* slider with Left/Right. | The slider steps by pad; the value readout follows. (The bridge itself reads D-pad names only — `PadNavControls` has no axis entries — so stick drift must not move the menu cursor; see PAD-03.) | | | |
| SET-06 | SET-01, System tab | Walk the rows (storage, keyboard preset, *Download box art*, …). Change *Download box art*; change it back. | Every row is a pad-drivable control; a storage change offers a restart rather than pretending (ADR-0256 Decision 8). No native dialog opens. | | | |
| SET-07 | SET-01, any tab | B. | Changes kept, sheet closes back to W-P4. | | | |

### 4.7 Controller sheet (ADR-0255)

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| CTL-01 | SET-01, Controls tab, game loaded | Up from Done to *More in Options…*, A. | The Play Controller sheet opens over the paused game (not the classic `ConfigWindow`); the live pad drawing lights the buttons you press; *Done* reachable. | | | |
| CTL-02 | CTL-01 | D-pad to a console-control row (e.g. NES *B*), A to arm the capture, release, press the pad button you want; then arm another row and press a **navigation** control (A or B or a D-pad direction); then arm a row and press B to cancel. | Arming waits for the first button to be released; a mapped button lights the two lights (pad side / port side); a navigation control is **refused visibly** (ADR-0256 Decision 4); B cancels the capture and the sheet is still drivable — the pad regains authority (`HasAuthority` gains `!IsControllerCapturing`). | | | |
| CTL-03 | CTL-01 | A on *Done*; reopen; B instead. | Both leave to W-P4; no capture stays armed after close (`Closing_the_sheet_ends_the_capture_it_was_in`) — the pad still moves focus on W-P4. | | | |
| CTL-04 | CTL-01, EXTRA BUTTONS section | Bind a spare button (e.g. a paddle or the right stick click) to *Rewind*; B to the game; press it while playing. | The binding takes, the action fires in game (engine third key set, ADR-0255 slice 4). Navigation controls are not offered in this list. | | | |
| CTL-05 | Home, **no game loaded** | Try to reach the Controller sheet. | **Known limitation, expected FAIL:** Settings itself is not reachable by pad with no game (HOME-06), so neither is the Controller sheet. If Settings is reached by keyboard (Tools ⋯ › Settings…), Controls › *More in Options…* opens the classic Options window's Input page, because `OpenControllerSheet()` returns false with no game loaded (`MainWindow.PlaySheets.cs:230-231`; ADR-0256 Decision 5 note). Record what the player sees. | | | |

### 4.8 Port lamps (ADR-0261) and hot-plug

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| LAMP-01 | Home, one pad | Read the status line. | Four lamps, P1 lit, others dim; the number is the label; the pad name is the tooltip (not readable by pad — note, not a FAIL). | | | |
| LAMP-02 | Home, P-B available (run in pass 1 if P-B is at hand, else pass 3) | Plug P-B in (or power it on over Bluetooth); wait 2 s; power it off. | The second lamp lights within ~1 s (1 s poll) and dims again; nothing else moves; the ring stays where it was. If P-B's first press shows the W-P15 pill "New controller …", that is expected. | | | |
| LAMP-03 | Game running unpaused | Read the status line. | Bar and lamps hidden; a pack-install pill over a running game keeps the bar (sheetOpen) — record only if observed. | | | |

### 4.9 Focus loss and pad loss (ADR-0254)

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| LOSS-01 | Game running, `PauseWhenInBackground` default (on) | Make another app take focus **by pad** — not possible on macOS without a keyboard; so: BLOCKED unless a second machine/remote switch exists. If you must use the keyboard (Cmd-Tab), note it and record the behavior anyway. | W-P4 opens with "Paused — …" naming the lost focus; regaining focus **stays paused** (ADR-0254 answer 1); only Resume/chord resumes. | | | |
| LOSS-02 | Game running unpaused, P-A wired | Unplug P-A. Replug it. | W-P4 opens with **"Paused — controller disconnected"** (ADR-0254 amendment, always on in Play, not gated by `PauseWhenInBackground`); replugging rewrites the line to "Controller reconnected" and **does not resume**; the action bar names the pad still connected, or the keyboard with none. Then: can you resume with the replugged pad (chord or A on Resume)? | | | |
| LOSS-03 **(pass 3)** | W-P4 open (mid-menu), P-B Bluetooth | Power P-B off, on again, press a direction. | No second pause (already paused); on return the pad drives focus again; the footer names the right control. | | | |
| LOSS-04 **(pass 3)** | Game running, P-B Bluetooth | Leave the pad idle until it sleeps (vendor timeout, typically 10–15 min — start it at the beginning of pass 3); wake it. | Sleep is a disconnect: W-P4 opens as in LOSS-02; waking reconnects and the pad can navigate W-P4 and resume. Not covered by any repo test — observation only. | | — | |

### 4.10 Full screen (FS) and sizes

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| FS-01 | W-P4 › Settings › Display, windowed | A on the *Fullscreen* switch. | The window goes full screen; the focus ring is still on the switch (or on a sensible control); the sheet is still drivable; the **mouse cursor is hidden** over the game (observe after B to resume: no cursor over the picture). | — | | |
| FS-02 | Full screen, W-P4 › Settings, Display tab | Left from Done to *Exit full screen* (it shares Done's row, drawn to its left, visible only in full screen — #910, `PlayerSettingsSheetView.axaml:117-120`), A. | Back to a window; focus moves to Done (the button hides itself); the settings sheet is still on screen and drivable. | — | | |
| FS-03 | Full screen, game running | Chord; walk W-P4; open Settings; B; B. | Same behavior as [W] for P4-01…SET-07 — this is the [F] column of every case above; use FS-03 to note any **difference** only. | — | | |
| FS-04 | Windowed | Shrink the window to its minimum **by pad** — not possible; BLOCKED by design. Instead: launch with the default size and note it. | The settings sheet is guaranteed at 512x505 (ADR-0269 Decision 6); the other Play sheets and the pause card only at ~1024x640 (same Decision). Record only if the default window is below 1024x640. | | — | |
| FS-05 | Full screen, Interface size Extra large | Open the library, Cheats and the Controller sheet. | Nothing clipped or hanging off the screen; every Done/Back reachable. These sheets are **not** under the width-cap guarantee (ADR-0269 Decision 6 names them; no open issue tracks them) — a clip here is a finding to file, citing that Decision. | — | | |

### 4.11 Two pads (needs P-B, pass 3, [W] only)

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| PAD2-01 | Both pads connected, game running | Press the chord on **P-B** (the pad that is not device 0). | W-P4 opens (ADR-0256 Decision 5, "any pad", #802); the footer names P-B's buttons. | | — | |
| PAD2-02 | W-P4 open | Navigate with P-A, then with P-B, alternating. | Both move the focus; one ring; no double-step. | | — | |
| PAD2-03 | Controller sheet, both connected | Walk the PLAYERS rows; move P-B to P2 by pad; B. | PLAYERS appears only with 2+ pads; the move happens by pad; the port lamps are unchanged (connected, not assigned — ADR-0261). On macOS the sheet must **not** promise stable membership across reconnect (VID:PID is 0:0000; ADR-0255 slice 5). | | — | |
| PAD2-04 | PAD2-03 | Unplug P-A, replug. | Known: on macOS the reconnect repair never fires; P-A may return on another index. Record whether P2's keys followed the wrong pad. Not a FAIL of this script; a note for #813's family. | | — | |

### 4.12 Feel: repeat, dead zones, drift

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| PAD-01 | Any long list (library 100+) | Hold Down 5 s; release; hold Up 5 s. | Steady repeat (~10/s after 0.4 s), stops on release, no overshoot. Score "repeat speed" in the rubric. | | | |
| PAD-02 | W-P4 | Tap a direction 10 times quickly. | Ten steps (or as many as rows allow), none dropped, none doubled. | | | |
| PAD-03 | W-P4, a pad with a worn stick if available | Leave the left stick alone; nudge it below half; then push it fully. | **Expected per code:** the bridge reads D-pad key names only (`PadNavControls`), so the stick does not move the menu cursor at any deflection unless the backend reports the stick as D-pad keys. Record what you see — if the stick navigates, record whether a small drift moves focus on its own. | | | |
| PAD-04 | Library | Diagonal D-pad press (Up+Right). | One deterministic step (or none), no focus jump off the surface. | | | |

### 4.13 Dialogs and "no trap" sweep

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| TRAP-01 | Each of: library, Library folders, Browse a file, W-P4, Save states, slot grid, Pack picker, Pack detail, Enhancements, Cheats, Add a Code, Settings x5, Controller sheet, Quit confirm, Select-ROM sheet, BIOS sheet, W-P15 setup sheet (if a new pad is pressed), Tools sheets reachable from W-P4 (About etc., if any); and the three exits to the classic Options window: Settings › Audio *More in Options…* (`ConfigViewModel.cs:156-168`, `PlayerSettingsSheetView.axaml.cs:33-43`), the Controller sheet's *More in Options…* (`PlayerControllerSheetView.axaml:259`, `MainWindow.PlaySheets.cs:240-246`), Settings › Look › Pixels *More in Options* (`LookConfigViewModel.cs:117-123`) | Enter, press B. | Every surface closes on B to its parent; no surface needs a mouse to leave; the ring lands on a sensible control afterwards. For the three Options exits: B closes the classic Options window and returns to the Play sheet it came from, pad still in charge. List any surface where B did nothing. | | | |
| TRAP-02 | Any point, including the three Options exits of TRAP-01 | Watch for any **native** dialog or classic window (file picker, folder picker, OS alert, `ConfigWindow`). | None in the Play door except the ones ADR-0256 Decision 9 lists as deliberately native (BIOS *Choose File…*, pack dependency file, save-state import/export, shader, palette). Any of those reached from a pad-only flow = FAIL with the ADR cited; any other native dialog = FAIL. A classic Options window that B does not close back to the Play sheet = FAIL. | | | |
| TRAP-03 | W-P15 pill shown (press a never-mapped pad) | Press Start on it; follow the lit steps; test the 2 s hold-to-skip and let it time out once (10 s silence). | The setup sheet is driven by that pad alone; hold skips; silence cancels back without a trap. | | | |
| TRAP-04 | Home | Try to **quit the application** by pad. | **Expected FAIL (known gap):** *Quit MesenAI* lives in Tools ⋯ › File (ADR-0241/G.2) and the header's Tools ⋯ / Profile buttons are deliberately outside the pad's walk ("unpadded", #1137, `PlayPadNavigationWiring.ContentRoot`). There is no pad route to quit the app. Record exactly what you tried. | | | |

### 4.14 Complete journey (end of the run)

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| JOURNEY-01 | Cold start: the app is launched by OS means first (HOME-01 — there is no pad-only launch), the keyboard and mouse are set aside, and the timed pad-only journey starts at Home | Launch → Home → *Open a ROM…* → search with Y, A → A on a tile → play 30 s → chord → Settings › Display › Interface size → Large → B → Save states → save a slot → B → Resume → chord → Quit game → confirm → Home. | Every step **from Home on** by pad; no keyboard or mouse touched after the launch; the timed part starts at Home (launching is not part of it) and the whole trip is under 5 minutes; note the number of presses where it felt long. | | | |
| JOURNEY-02 | JOURNEY-01 | Full screen variant: enter full screen in Settings first, run the same trip, exit full screen at the end. | Same, with focus kept across both mode switches. | — | | |

### 4.15 Defects log

| # | Case | [W]/[F] | Pad | Observed | Expected | Severity (P0/P1/P2) | Recovery used (keyboard/mouse?) | Issue filed |
|---|---|---|---|---|---|---|---|---|
| 1 | | | | | | | | |
| 2 | | | | | | | | |
| 3 | | | | | | | | |

File each real, reproducible bug with `scripts/report-bug.sh` and put its
number in the last column.

## 5. Qualitative evaluation

### 5.1 Rubric (fill after the run, 1 = poor, 5 = excellent)

| Criterion | What to judge | [W] | [F] | Comment |
|---|---|---|---|---|
| Discoverability | Does the screen say how to open the menu, search, favorite, filter — without a manual? (entry toast, footers) | | | |
| Predictability of B/back | Did B always go one level up, never two, never nowhere? | | | |
| Focus visibility at 10 ft | Can you tell where the ring is from the couch, on every surface, at Standard size? | | | |
| Text legibility — Standard | | | | |
| Text legibility — Large | | | | |
| Text legibility — Extra large | | | | |
| Repeat speed | Hold-to-scroll delay and rate felt right in a long list | | | |
| Audio feedback | Menu sounds help, are not annoying, never leak into the game | | | |
| Haptic feedback | The tick is noticeable, not distracting (N/A if no rumble) | | | |
| Error recovery | After a wrong press (wrong sheet, wrong slot), one B gets you back | | | |
| Full-screen parity | Everything that works windowed works identically full screen | | | |
| Consistency with console UIs | Compared with a Switch/PlayStation/Xbox home: same conventions (A/B, Y search, LB/RB filters) | | | |
| **Overall** | Would you hand this to someone with only a pad? | | | |

### 5.2 Pre-execution assessment (static reading, not observed behavior)

Everything below is read from the ADRs and tests at the build SHA on the
sheet, **not** from running the app with a pad. It tells the tester where
to look hardest.

**Strengths (what the repo already pins):**

- A single pad bridge drives the focus engine directly, no per-view code
  (ADR-0256; `PlayPadNavigationWiring`, 18 focus claims). The headless
  pad-walk (`UI.HeadlessTests/PlayPadWalkTests`) opens and walks **every**
  registered Play surface at every interface size, and its host-free judge
  runs in CI (`UI.Tests/Play/PadWalkJudgeTests`, #1154). `NotWalkedYet`,
  `KnownBarGaps` and `KnownFocusLeaks` are all **empty** at HEAD.
- Back is the oldest working pad gesture (ADR-0256 Context) and every sheet
  closes back along ADR-0241's Esc order.
- The on-screen keyboard covers every `TextBox` with a field-declared
  alphabet and a cancel that restores the original (ADR-0262).
- The footer names the control in the pad's own words and recomputes on a
  device change (ADR-0256 Decision 6).
- Hold-to-repeat exists with fixed numbers (400 ms / 100 ms).
- Pad loss pauses into W-P4 and never auto-resumes (ADR-0254 amendment).
- Interface size has a 512x505 guarantee for the settings sheet with
  headless proof (ADR-0269 Decision 6).

**Risks and likely failures:**

1. **No pad route to quit the application** (TRAP-04). Tools ⋯ is
   deliberately unpadded; *Quit MesenAI* lives there. On a cabinet the only
   way out is the OS. Almost certain FAIL.
2. **Settings, and so the Controller sheet, need a loaded game** (HOME-06,
   CTL-05). Settings opens only from W-P4 or Tools ⋯ / the app menu, and Tools ⋯
   is unpadded; a first-time cabinet user cannot change a setting or remap
   before loading a game (ADR-0256 Decision 5 note). Also W-P15's auto-setup
   sheet only fires for a pad **no** mapping uses.
3. **The console filter's selected chip may not read from the couch** (LIB-04).
   LB/RB cycles the filter and lands the ring on the segment it selected
   (ADR-0264 amendment 2026-10-09, #1108), and the chips are reached now —
   `KnownChipGaps` is empty (#1134 closed, `PlayPadWalkTests.cs:149`). What is
   unverified is the visual cue of the active chip at 10 ft.
4. **Native dialogs remain by decision** inside Play for BIOS, pack
   dependency, save-state import/export, shader and palette (ADR-0256
   Decision 9 refusals). GAME-04 will FAIL on *Choose File…*; that is a
   recorded limitation, not news.
5. **Focus loss by pad cannot be triggered on macOS** (LOSS-01) — expect
   BLOCKED; the keyboard-assisted observation is still worth recording.
6. **Full-screen entry/exit** is a switch in Settings › Display plus an
   *Exit full screen* button (#910). Whether the ring survives the window's
   mode change is tested headlessly for the switch, not for a real macOS
   full-screen transition (Spaces animation). Watch FS-01/FS-02 closely.
7. **Menu sounds and the haptic tick** depend on host capabilities: the
   *Menu sounds* row hides when the stream is "not available" (ADR-0270),
   the *Menu tick* row hides unless the pad is aimable and is disabled at
   Rumble 0. A hidden row is by design; the tester must record **which**
   state they saw, since a missing row looks like a bug.
8. **Pending ratification** — ADR-0268 (X = favorite, Y = search), the
   ADR-0254 pad-loss pause not gated by `PauseWhenInBackground`, ADR-0270's
   own-stream rule and the haptic tick were accepted by the autonomy panel
   (Opus 5.5 as proxy), not by the owner; their ratification is pending in
   `to-questionnaire-play-couch-gui-ratification.md`. A FAIL against them may
   become a decision change rather than a bug; cite the ADR.
9. **Stick vs D-pad**: the bridge reads D-pad key names only. A pad whose
   backend never reports a D-pad (some fight sticks / arcade encoders
   reporting hat as axes) would have **no** navigation at all. Not
   verifiable in the repo for a given device; PAD-03 is where it shows.
10. **Bluetooth sleep/wake** (LOSS-04) and **two-pad reconnect ordering**
    on macOS (PAD2-04) have no test and no identity to repair with
    (VID:PID zeroed); expect index swaps.
11. **Other Play sheets below 1024x640** (Cheats, Replays, PackPicker,
    PackDetail, Tool, Controller, Enhancements, Shader, the pause card) are
    outside the width-cap guarantee (ADR-0269 Decision 6; #1123 closed with
    the settings sheet only, and no open issue tracks the rest); FS-05 at
    Extra large is the likely place for clipping.
12. **Exits to the classic Options window** from Audio, the Controller sheet
    and Look › Pixels (TRAP-01) leave the Play door; whether B brings the pad
    back to the Play sheet is untested by a real pad.
13. ***Show pack folder*** hands the foreground to Finder (P4-06); nothing in
    Play can take it back by pad.

### 5.3 Suggested improvements and fixes (prioritized)

Items marked **(not verified in repo)** are inferences from the reading, not
measured facts.

**P0 — blocks a keyboard-less cabinet**

| # | Suggestion | Rationale | Related |
|---|---|---|---|
| P0-1 | Add a pad-reachable *Quit MesenAI* (e.g. a row on the Home or a long-press on the W-P4 *Quit game* confirm, or make the Tools ⋯ menu walkable from the last Home row) | No pad route to exit the app; a cabinet cannot be shut down cleanly. PRD §13.3 rule 9 says the pad reaches everything in Play. **Needs /adr** — a second *Quit* in Play is a door change: ADR-0250 gives each menu entry one place per door. | ADR-0256 stop rule, #1137 (the containment that made Tools unpadded), ADR-0250 (one place per entry) |
| P0-2 | Make Settings, and through it the Controller sheet, reachable by pad with no game loaded (HOME-06, CTL-05; to be filed as bug #1177) | Verified: Settings opens only from W-P4 or Tools ⋯ / the app menu (`MainWindow.PlaySheets.cs:159-164`), Home has no Settings control (`PlayHomeView.axaml`), Tools ⋯ is unpadded since #1166 (`PlayPadNavigationWiring.cs:329-345`), and with no game the Controls link falls to the classic Input page (`MainWindow.PlaySheets.cs:230-231`). A pad that needs a bind before it can play (DirectInput, or a pad whose Select/Start is broken) is stuck at Home. | ADR-0256 Decision 5 note, ADR-0255 |

**P1 — operable but with a trap or a loud rough edge**

| # | Suggestion | Rationale | Related |
|---|---|---|---|
| P1-1 | A visibly "selected" chip state sized for 10 ft | The ring reaches the chip row (ADR-0264 amendment 2026-10-09, #1108; `KnownChipGaps` empty, #1134 closed), but whether the selected chip reads at couch distance is unverified. | #1134, ADR-0264 amendment 2026-10-09, ADR-0264 Decision 5 |
| P1-2 | Pad-driven BIOS file pick (reuse the *Browse a file…* folder walk with a BIOS-extension filter) | GB/GBA/SMS BIOS is the first thing a new console needs and it is native today. | ADR-0256 Decision 9 refusals (a new ADR, not a patch) |
| P1-3 | A *Home* long-press or a dedicated extra-button default to open W-P4 on pads where Select+Start is a soft reset in some games | ADR-0251 Consequences names the trade-off; no Home/Guide button is reported by any backend yet. | ADR-0251 §3, ADR-0256 Decision 5 |
| P1-4 | Extend the width cap to every Play sheet (#1123 closed with the settings sheet only; no open issue tracks the rest) | Only the settings sheet is guaranteed at 512x505; a small window at Extra large will clip the others. | ADR-0269 Decision 6 |
| P1-5 | Show the lamp's pad name somewhere a pad can read (e.g. in the Controller sheet's PLAYERS rows, already there; or a Home footer) | The lamp's name is a tooltip, mouse-only. | ADR-0261 |
| P1-6 | On pad reconnect, offer *Resume* focused so one A resumes | ADR-0254 says never auto-resume; a focused Resume keeps that and still makes the recovery one press. **(not verified in repo** where the ring lands after the reconnect line is rewritten) | ADR-0254 amendment |

**P2 — polish**

| # | Suggestion | Rationale | Related |
|---|---|---|---|
| P2-1 | Make repeat delay/rate a tested constant pair with a faster "page" step on the triggers (L2/R2) in the library | 100+ tiles at 10 steps/s is 10 s to the bottom; console UIs page with triggers. LB/RB are taken by the console filter (`PlayPadNavigationWiring.cs:845-853`); the macOS backend reports L2/R2 as their own buttons (`MacOS/MacOSKeyManager.mm:60-61`). | ADR-0256 Decision 7, ADR-0264 Decision 3 |
| P2-2 | A one-line "B = back" and "Y = search" hint on Home's footer for the first three launches | Entry toast teaches the chord only; the library footer teaches the rest, but Home does not. | ADR-0251 §2 |
| P2-3 | Left-stick navigation as an alias of the D-pad with a fixed dead zone in the bridge (not rebindable) | Many players hold the stick; the bridge reads D-pad names only. Must respect ADR-0256 Decision 4 (not rebindable) and the Controls › Deadzone setting. | ADR-0256 Decisions 4 and 7 |
| P2-4 | Menu tick on Confirm/Back as well as on move, behind the same switch | Currently move only (`ShouldTickOnMove`); console UIs tick on confirm. **(not verified** whether #1112's scope excluded this on purpose) | #1106, #1112 |
| P2-5 | A haptic/menu-sound preview on the row itself (tick once when toggled on) | The tester cannot tell "row on, nothing happened" from "pad not aimable" without moving focus. | #1105, #1106 |
| P2-6 | Owner ratification of the panel-accepted decisions this script relies on (ADR-0268, ADR-0269, ADR-0270, ADR-0254 amendment) | A FAIL against a provisional decision is ambiguous until the owner signs it. | `to-questionnaire-play-couch-gui-ratification.md` |

**Not verifiable in this repo, left for the run:** Bluetooth sleep/wake
timing, real macOS full-screen transition keeping the ring, box-art
fetch speed on the tester's network, and whether a given PS-layout pad is
reported by the GameController backend with D-pad key names (the bridge's
only navigation input).
