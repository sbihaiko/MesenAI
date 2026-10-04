# ADR-0254: Losing focus pauses the game and shows the Play pause overlay

- Status: accepted (2026-10-04), implemented in the same turn under the user's
  own two picks on that day, quoted verbatim from the question they answered:
  **"Fica pausado na tela de Esc"** (on focus regain) and **"Ligado por padrão
  no Play"** (the default). The same two lines are quoted in the PR body, which
  is what ADR-0137's same-turn rule requires alongside the unit tests below.
- Date: 2026-10-04
- Related: ADR-0241 (the four-door Player GUI), ADR-0249 (the Esc router and
  the W-P4 overlay's own rules), ADR-0251 (Play teaches its pause menu),
  `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` Part B §8.
- Supersedes / amends: none.

## Context

`Preferences.PauseWhenInBackground` pauses emulation when the app is not the
active window. `MainWindow.UpdateAutoPause()` (polled, `UI/Windows/MainWindow.axaml.cs`)
resolves the same flag names for two neighbours: `PauseWhenInMenusAndConfig`,
which pauses while a classic menu or a configuration window is up, and
`PauseWhenInBackground`, which pauses while `ApplicationHelper.GetActiveWindow()`
returns null. Both write `MainMenu.AutoPaused`, and both resume by themselves:
`UpdateAutoPause()` calls `EmuApi.Resume()` on the first poll where the
condition no longer holds and the load/save state grid is not open.

Three things about that are wrong for a player, and the user hit all three at
once on 2026-10-04: *"eu estava jogando em fullscreen e vc abriu uma nova
janela que pegou o foco da tela, o jogo continuou executando e acabei perdendo.
tem como pausar e colocar na tela de ESC se perder o foco?"*

1. **The preference is off by default** (`UI/Config/PreferencesConfig.cs`),
   so out of the box the game simply keeps running with no window in front of
   it. On a fullscreen emulator that is a lost life, a lost race, or a lost
   save, because the app is still consuming the keyboard and the display is
   showing something else.
2. **The pause is silent.** Even with the preference on, the game freezes with
   nothing on screen saying why. A frozen emulator and a hung emulator look
   the same, and the player's next move is to force-quit.
3. **The resume is automatic.** Coming back to the window drops the player
   straight into a running game, mid-frame, with no countdown - the same
   loss the pause was meant to prevent, just delayed by however long the
   interruption took.

What already exists to build on: the W-P4 pause overlay (ADRs 0241/0249) is
exactly the surface for this. It names the game, says why it is paused, and
offers Resume, and its Esc router already treats "overlay is up" as the state
where Esc resumes. It is a Play-door surface: in Classic and Advanced there is
no W-P4, so on those doors only the pause half is available.

**Non-goals:**
- Pausing when a debug tool or the debugger window takes focus. That is
  `PauseWhenInMenusAndConfig`'s territory, and a debugger is not an
  interruption - it is the work.
- Any new always-on-top window, notification or sound. The overlay is the
  whole signal.
- Changing what `AutoPaused` means. Whatever is decided, an auto-pause must
  stay distinguishable from a pause the player asked for, so the Esc router
  and the "do not resume under a dialog" rule keep working.

## The three questions, and how they were answered

1. **What happens on focus regain?** Resume immediately, or stay paused on the
   overlay until the player presses Esc. **Answered: stay paused** - the user
   picked *"Fica pausado na tela de Esc"*.
2. **Is it on by default?** **Answered: yes** - *"Ligado por padrão no Play"*.
   The migration caveat was accepted with the answer: `PreferencesConfig`
   declares one global default, so this is `true` for every door, and a
   configuration written before it keeps whatever it stored, because nothing
   can tell "never chose" from "chose off".
3. **Does the menus-and-config pause keep its own resume policy?** **Answered
   by the shape**: yes. The focus pause is the only one that opens an overlay,
   so it is the only one whose automatic resume is held back; the
   menus-and-config path resumes on the first poll where its condition clears,
   exactly as before.

## Decision

**Accepted and implemented the same day, under the two answers above.**

- `UI/Logic/FocusPause` holds the decision host-free (ADR-0123), and
  `MainWindow.UpdateAutoPause` is its only caller - it owns the poll, the
  preferences and the pause calls.
  - `ShowsOverlay(focusLost, isPlayDoor, gameLoaded)`: only the *focus* pause
    gets a voice, and only where there is a surface to give it one. The
    menus-and-config pause stays silent because the player is using the app in
    front of something they opened; Classic and Advanced have no W-P4; a Play
    door with no game loaded has nothing for the overlay's rows to describe.
  - `AutoResumes(pausedByFocusWithOverlay, overlayOpen)`: the focus path's
    automatic resume is held back while the overlay it opened is up, so the
    game cannot come back running behind a pause card. Everything else -
    the menus and config pauses, and the focus pause once the player has
    answered - resumes exactly as it always did.
- `MainWindow` carries one field, `_focusPausedWithOverlay`, set where the
  overlay is opened and read by the resume branch. The way back is W-P4's own
  Esc, which already resumes and clears the overlay.
- `PreferencesConfig.PauseWhenInBackground` defaults to `true`.

Evidence: `UI.Tests/Play/FocusPauseTests` (8 cases over both rules - the
three-way door/game/focus table, the held-back resume, and the three cases
that must keep resuming) and `UI.HeadlessTests/FocusPauseDefaultTests` (a
fresh `PreferencesConfig` pauses; `PauseWhenInMenusAndConfig` stays opt-in,
because that one fires while the player is *using* the app).

## Consequences

- The overlay's `RefreshPauseOverlay()` reads the ROM's name, the save-state
  summary and the pack summary off the UI thread's own state. Opening it from
  the poll is the same call the Esc shortcut makes, so it is not new work -
  but it now runs on a timer tick rather than on a key press, which is the
  first time W-P4 can appear without the player asking for it. Anything in
  it that assumes a player action (a toast, a one-per-session notice) has to
  be checked against that.
- A game that never draws still pauses; the overlay's frozen frame comes from
  the core's last frame, so a pause during the load card must not be treated
  as a picture.
- **Classic and Advanced now pause on focus loss by default with nothing to
  explain it.** `PauseWhenInBackground` is one global preference and the answer
  was "on by default", so those doors get the pause and no W-P4. This is the
  cost the answer accepted; a door-scoped default would need the preference
  split in two, which nothing has asked for yet.
- Auto-pause already suppresses the debugger's bring-to-front on break
  (`SuppressBringToFront()`); opening an overlay on top of that path must not
  re-enable it.
- This ADR does not make the emulator run while its window is behind another
  window any safer on its own. It only makes the pause visible and the resume
  deliberate.
