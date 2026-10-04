# ADR-0254: Losing focus pauses the game and shows the Play pause overlay

- Status: proposed (2026-10-04). The decisions under "Open questions" are
  the user's to make; nothing here is implemented.
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

## Open questions

These are what keep this ADR `proposed`. Each has a recommendation, but the
recommendation is not the decision.

1. **What happens on focus regain?** Today: resume immediately. The
   alternative is to stay paused on the overlay until the player presses Esc.
   *Recommendation: stay paused.* Auto-resume is the half of the current
   behaviour that cost the user the game; the overlay is already the surface
   that turns "Esc to resume" into a deliberate act.
2. **Is it on by default?** Today `PauseWhenInBackground` is off, so this
   changes nothing for anyone who has not opted in. *Recommendation: on by
   default for the Play door*, since the overlay now explains the pause, and
   leave Classic/Advanced's default alone. This needs a migration answer for
   an existing `settings.json`, which has no way to tell "never chose" from
   "chose off".
3. **Does a config window still auto-resume under `PauseWhenInMenusAndConfig`?**
   The two flags share `AutoPaused` and the same resume branch. If regain
   stops auto-resuming, the menus-and-config half must keep its own answer,
   or opening Settings from W-P4 starts resuming on close. *Recommendation:
   the two paths get separate resume policies*; only the focus path waits for
   Esc.

## Decision

Not decided. The shape once the questions above are answered:

- `UpdateAutoPause()` gains the overlay half: when it pauses because the app
  lost focus and the current door is Play with a game loaded, it opens the
  W-P4 overlay (`MainWindowViewModel.OpenPauseOverlay()`) instead of pausing
  silently. The door check reuses the same one W-P4's own visibility uses, so
  the overlay is never asked for on a door that has no such surface.
- The pause keeps going through `MainMenu.AutoPaused`, so it stays distinct
  from a player-initiated pause.
- If question 1 is answered "wait for Esc", the focus path stops resuming by
  itself and the overlay's existing Esc-to-resume is the only way back.

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
- On Classic and Advanced this decision is a pause and nothing else. If the
  answer to question 2 is "on by default everywhere", those doors change
  behaviour with no surface to explain it - which is an argument for the
  recommendation.
- Auto-pause already suppresses the debugger's bring-to-front on break
  (`SuppressBringToFront()`); opening an overlay on top of that path must not
  re-enable it.
- This ADR does not make the emulator run while its window is behind another
  window any safer on its own. It only makes the pause visible and the resume
  deliberate.
