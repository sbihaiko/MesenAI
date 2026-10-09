# ADR-0254: Losing focus pauses the game and shows the Play pause overlay

- Status: accepted (2026-10-04), implemented in the same turn under the user's
  own two picks on that day, quoted verbatim from the question they answered:
  **"Fica pausado na tela de Esc"** (on focus regain) and **"Ligado por padrão
  no Play"** (the default). The same two lines are quoted in the PR body, which
  is what ADR-0137's same-turn rule requires alongside the unit tests below.
- Date: 2026-10-04
- Related: ADR-0241, ADR-0249, ADR-0251, `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` Part B §8.
- Supersedes / amends: none. Amended 2026-10-09 (#1103, accepted): the reason line has a
  second wording, *Paused — controller disconnected* (see "Amendment").

## Context

`Preferences.PauseWhenInBackground` pauses emulation when the app is not the active window. `MainWindow.UpdateAutoPause()` (polled, `UI/Windows/MainWindow.axaml.cs`) resolves the same flags for two neighbors: `PauseWhenInMenusAndConfig` (a menu/config window is up) and `PauseWhenInBackground` (while `ApplicationHelper.GetActiveWindow()` returns null). Both write `MainMenu.AutoPaused` and both resume themselves: `UpdateAutoPause()` calls `EmuApi.Resume()` on the first poll where the condition clears and the load/save state grid is closed.

Three things are wrong for a player, all hit on 2026-10-04: (1) **the preference is off by default** (`UI/Config/PreferencesConfig.cs`), so the game runs with no window in front — on a fullscreen emulator a lost life, race or save; (2) **the pause is silent** — the game freezes with nothing saying why; (3) **the resume is automatic** — coming back drops the player into a running game, mid-frame, no countdown, the same loss the pause was meant to prevent.

The W-P4 pause overlay (ADRs 0241/0249) is the surface: it names the game, says why it is paused, and offers Resume; its Esc router treats "overlay is up" as the state where Esc resumes. Classic and Advanced have no W-P4, so those doors get only the pause half.

**Non-goals:** pausing when a debug tool or debugger takes focus (that is `PauseWhenInMenusAndConfig`'s territory — a debugger is the work); any new always-on-top window, notification or sound (the overlay is the whole signal); changing what `AutoPaused` means — an auto-pause must stay distinguishable from a player-requested pause, so the Esc router and the "do not resume under a dialog" rule keep working.

## The three questions, and how they were answered

1. **Focus regain?** **Answered: stay paused** — *"Fica pausado na tela de Esc"*.
2. **On by default?** **Answered: yes** — *"Ligado por padrão no Play"*; `PreferencesConfig` declares one global default, so this is `true` for every door, and a configuration written before it keeps what it stored, because nothing can tell "never chose" from "chose off".
3. **Does the menus-and-config pause keep its resume policy?** **Answered yes**: only the focus pause opens an overlay, so only its automatic resume is held back.

## Decision

**Accepted and implemented the same day, under the two answers above.**

- `UI/Logic/FocusPause` holds the decision host-free (ADR-0123), and `MainWindow.UpdateAutoPause` is its only caller — it owns the poll, the preferences and the pause calls.
  - `ShowsOverlay(focusLost, isPlayDoor, gameLoaded)`: only the *focus* pause gets a voice, and only where there is a surface — the menus-and-config pause stays silent; Classic and Advanced have no W-P4; a Play door with no game loaded has nothing for the overlay's rows.
  - `AutoResumes(pausedByFocusWithOverlay, overlayOpen)`: the focus path's automatic resume is held back while the overlay it opened is up, so the game cannot come back running behind a pause card; everything else resumes as it always did.
- `MainWindow` carries one field, `_focusPausedWithOverlay`, set where the overlay opens and read by the resume branch; the way back is W-P4's own Esc, which resumes and clears the overlay.
- `PreferencesConfig.PauseWhenInBackground` defaults to `true`.

Evidence: `UI.Tests/Play/FocusPauseTests` (8 cases over both rules — the door/game/focus table, the held-back resume, the three that keep resuming) and `UI.HeadlessTests/FocusPauseDefaultTests` (a fresh `PreferencesConfig` pauses; `PauseWhenInMenusAndConfig` stays opt-in, because it fires while the player is *using* the app).

## Consequences

- The overlay's `RefreshPauseOverlay()` reads the ROM name, save-state summary and pack summary off the UI thread's own state; opening it from the poll is the same call the Esc shortcut makes, but now on a timer tick — the first time W-P4 can appear without the player asking, so anything assuming a player action (a toast, a one-per-session notice) must be checked.
- A game that never draws still pauses; the overlay's frozen frame comes from the core's last frame, so a pause during the load card must not be treated as a picture. Decided 2026-10-07 (#967): a pause that ends the load card before the first picture (fewer than `PlayLoadWait.FramesUntilShown` frames since GameLoaded) leaves W-P4 with **no** frozen frame — `PausedGameFrame` stays null and its `Image` is off screen — until the game draws that picture (`PlayLoadWait.PictureCutShort`, read by `PlayFrozenFrame`). Amended 2026-10-09 (#1155): the cut-short flag is not enough on its own, because a focus pause opens W-P4 on the UI thread while that flag is written off the core's `GamePaused` notification on another one, so the decision can run before the pause is known. The decision therefore also requires the game to be the picture on screen (`PlayFrozenFrame.Next`'s `gameOnScreen`) — an open's load card covers the home (#734) and the core's last frame is not that game's picture, while a reload's card covers a game that *is* on screen, whose last frame is exactly what the frozen frame stands in for. This amendment was decided by the agent under owner-away autonomy, as the implementation of the risk this bullet names; it was not an owner pick, and the owner may revert it. Pinned by `UI.Tests/Play/PlayLoadWaitTests.cs` (class `PlayLoadWaitTests`), `UI.Tests/Play/PlayPausedPictureTests.cs` (class `PlayFrozenFrameTests`) and the last case of `UI.HeadlessTests/FocusPauseWiringTests.cs`.
- **Classic and Advanced now pause on focus loss by default with nothing to explain it.** `PauseWhenInBackground` is one global preference and the answer was "on by default", so those doors get the pause and no W-P4 — the accepted cost; a door-scoped default would need the preference split in two.
- Auto-pause already suppresses the debugger's bring-to-front on break (`SuppressBringToFront()`); opening an overlay on top of that path must not re-enable it.
- This ADR does not make the emulator run while its window is behind another window any safer on its own; it only makes the pause visible and the resume deliberate.

## Amendment (2026-10-09, #1103): the reason line has a second wording

Status: **accepted** (2026-10-09), by the autonomy panel (Opus 5.5 as the human
proxy, issue #1103 comment) with edits, pick quoted verbatim: **"Accept ADR-0268, ADR-0269 and the ADR-0254 amendment with the listed edits; PR #1119 waits until ADR-0269 is accepted with Decisions 3 and 6 matching its code."**
The wording comes from spec #1102 slice 3 (pause when a controller disappears).
Nothing is implemented by this amendment.

- **The trigger is the connected-pad count dropping**, never a pad identity: on
  macOS VID/PID are zeroed on purpose, so no pad can be told apart. A pad
  swapped for another while the count holds does not pause.
- **"Reconnected" means the count is back to its earlier value** (the value
  before the drop), not that a particular pad returned.
- **Doors:** it applies to Play only (the door that has W-P4); Classic and
  Advanced never show this pause. It is not gated by
  `PauseWhenInBackground`, which governs the focus pause alone; the pad-count
  pause is its own auto-pause and is always on in Play. (Decided by the agent
  under owner-away autonomy as the cheapest to reverse; the owner may revert it.)

- The W-P4 reason line this ADR introduces for a lost focus gets a second
  wording, **Paused — controller disconnected**, written when a game runs
  unpaused and a connected pad vanishes. It is the same line on the same surface:
  W-P4 keeps its seven controls and no overlay is added. The pause is an
  auto-pause with the overlay, like the focus one.
- **Reconnecting rewrites the line and never resumes.** When the pad returns the
  line reads **Controller reconnected** and the game stays paused, exactly as
  this ADR answers focus regain ("stay paused"): only the player's own Resume
  (or Esc) leaves W-P4. The line is not cleared by the reconnect, and a second
  disconnect rewrites it back.
- The action bar on W-P4 names the control in words for the pad still connected
  (PlayStation names such as Cross and Circle on that family); with none, the
  keyboard (ADR-0256 Decision 6).
- The picture is W-P4b, a variant of W-P4 (ADR-0264 Decision 12 applies: held as
  awaiting until its ticket lands).
