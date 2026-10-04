# ADR-0255: The Play door's Controller sheet is one surface for players, testing, remapping and extra buttons

- Status: proposed (2026-10-04). The user settled the shape ("vamos de B") and
  then added the players, LED and extra-button requirements below; the
  "Open questions" are still theirs to answer, and nothing is implemented.
- Date: 2026-10-04
- Related: ADR-0241 (the four-door Player GUI), ADR-0249 (the Play sheets and
  the Esc router), ADR-0250 (one place per door), ADR-0254 (the sibling
  decision about focus loss), PRD Part B §8 (W-P8's Play Settings strip).
- Supersedes / amends: none.

## Context

Play's Settings › Controls tab has three rows and a **More in Options…**
button. The button calls `ConfigViewModel.OpenInOptions(tab)`
(`UI/Views/PlayerSettingsSheetView.axaml.cs`), which opens the **classic**
`ConfigWindow` on its Input page. The user's report is about that landing:
*"quando clico no 'More Options' aparece uma tela antiga do Mesen classico. que
tal algo bonitinho na linha desse site: https://hardwaretester.com/gamepad?"*

What already exists, and should not be thrown away: the host input tester from
PRD slices I.0–I.3, which lives inside `UI/Views/InputConfigView.axaml`'s
`tpgInputTest` tab. It polls every connected pad at ~60 Hz through
`GamepadTesterViewModel` and already answers questions nothing else does —
per-device deadzone overrides (`InputConfig.PerDeviceDeadzones`), a stick
circularity test (`GamepadCircularity`), a drift warning, and rumble through
`TestForceFeedback`. It is, however, list-shaped rather than pad-shaped: a
pad's buttons are undifferentiated chips in a `WrapPanel`, and only the **left**
stick is drawn — the right stick is literally two `TextBlock`s of X and Y.

Four requirements came after the wireframe was chosen, and each widens the
problem beyond "make the tester prettier":

1. **View and remap.** *"quero poder vizualizar e re mapear cada controle"* —
   the surface has to write bindings, not only display them.
2. **Many pads, one per player.** *"podem ter varios controles conectados no
   notebook. tem que poder dizer qual fica com cada jogador"* — a pad must be
   assignable to a player, which is the per-console controller-port decision
   (`NesConfig` and its siblings), not the tester's own state.
3. **Extra buttons become emulator actions.** *"se o jostick tiver mais botoes
   que o nitendo quero poder atribuir outros controles como retroceder,
   avancar, compartilhar, home, etc"*.

4. **A keyboard has to play the game on its own.** *"se nenhum controle estiver
   conectado o teclado deve estar configurado de maneira padrao e permitir o
   jogo"* — with no pad plugged in, the keyboard must be bound and playable
   without the player opening anything.

That last one is **already true on a fresh install**, and the reason is worth
writing down because it is easy to break: `PlayFirstRun.Mappings` always turns
on both pad presets and one keyboard preset — `Wasd` or `ArrowKeys`, whichever
radio the first-run sheet was left on — and `NesConfig.InitializeDefaults`
applies them. The gaps are the two edges: a configuration that predates the
preset, or one whose `DefaultKeyMappings` is `None` (a wizard that was never
completed), has no keyboard preset at all and nothing restores it; and the
sheet over a *running* game is the one place a player would look to find out
what their keyboard does right now, which nothing shows today.

**The constraint that shapes requirement 3, and the reason this ADR exists:**
the actions already exist — `EmulatorShortcut` has `FastForward`, `Rewind`,
`RewindTenSecs`, `RewindOneMin`, `ToggleFastForward`, `ToggleRewind`,
`TakeScreenshot` and more — but **the binding model is keyboard-only**.
`ShortcutKeyInfo` (`UI/Config/Shortcuts/ShortcutKeyInfo.cs`) holds an
`EmulatorShortcut` and two `KeyCombination`s, and nothing else. There is no
slot for a gamepad button, and `ShortcutHandler` reads keyboard state. So this
requirement is a Core-and-UI change to the shortcut model, not a new screen.

**Non-goals:**
- Replacing the classic window's Input page. Deep per-console device setup
  (Four Score, Zapper, the light guns, the Famicom keyboards) stays there; the
  Play sheet is the common case.
- Remapping the keyboard. `UI/Views/ShortcutKeysTabView.axaml` owns that.
- A pad editor that draws every pad ever made. One drawing, driven by the
  host pad's own reported shape, degrades to a generic layout.

## Decision

Not decided. The shape the user picked, to be refined once the questions below
are answered:

**One Play sheet, three stacked sections, over the dimmed game like every
other Play surface** (`Classes="sheet"`, W-P8's card, Done in the corner):

1. **PLAYERS** — one row per emulated port (P1, P2, …), each naming the pad
   assigned to it, with a player colour. Assigning is *press a button on the
   pad you want for P2* rather than a dropdown of device names, because two
   identical pads are indistinguishable by name — which is the whole reason
   the user asked for this.
2. **The pad drawing and its values**, the "B" wireframe: the pad drawn, with
   buttons lighting where they are, sticks as a dot inside their ring, and
   triggers as bars — beside a table of every button and axis with its live
   value (raw and after deadzone).
3. **The mapping itself** — one row per console button, showing what it is
   bound to and lighting when pressed. A row is also where a rebind starts:
   click it, press a control. Testing and mapping are the same row rather than
   two screens, because "does the pad send it" and "does the game receive it"
   are two different lights on one line.
4. **EXTRA BUTTONS** — the host controls the console has no use for, each
   assignable to an `EmulatorShortcut` (Rewind, Fast-forward, Share,
   Home/menu, Save state, …).

Requirement 4 is a state of the same sheet, not a fourth section: when no pad
is connected the PLAYERS and mapping rows are empty and the sheet's job is to
say **what the keyboard does now**, on the console's own buttons, and to offer
the keyboard preset back when there is none. `DefaultKeyMappingType.WasdKeys`
and `ArrowKeys` already exist as presets; what does not exist is a surface that
applies one to a configuration that never got it.

Player colour reaches the pad where the pad can show it: the macOS backend is
Apple's **GameController** framework (`MacOS/MacOSKeyManager.mm`), not SDL, and
`GCController.light` is present on DualShock 4 and DualSense and `nil` on an
Xbox pad. So the rule is **colour on screen always, on the pad when the pad has
a light**; a pad without one is not an error state.

## Open questions

1. **Where does a rebind write?** The Play sheet's mapping is per host pad, but
   `InputConfig` binds by emulated port and console. Whether a rebind is stored
   per pad (so the same pad means the same thing on every game) or per port (so
   player 2 can differ per game) is a real choice, and it decides whether
   `InputConfig` grows a per-device layer.
2. **Does requirement 3 extend `ShortcutKeyInfo` or add a sibling?** Giving the
   existing record a third slot keeps one list of actions and one UI to edit it,
   but it also means every consumer of "this shortcut's key" has to learn about
   a button that has no key. A parallel pad-binding table keeps the keyboard
   path untouched but lets the two disagree — for instance a Rewind bound to
   both a key and a button, with no rule for which wins.
3. **What does a trigger do when it is not a trigger?** Pads report axes with
   wildly different names and ranges. Whether an axis can be assigned to a
   digital action (Rewind on a half-pulled trigger) or only to a button is
   unresolved, and it is the difference between "any extra control" and "any
   extra button".
4. **Does the LED colour survive a pad that reconnects?** The colour is a
   function of the port, and a device that comes back may be a different
   index. Whether the player assignment is keyed by VID:PID or by the pad's
   slot is the same question as (1) seen from the reconnect.

## Consequences

- `GamepadTesterViewModel` and its host-free partners are the obvious base, but
  the pad drawing needs an axis/button **shape** the tester never needed: today
  a pad is "some buttons and four axes", and drawing one means knowing which
  button is where. That shape is new data, and on the macOS backend it comes
  from the GameController framework's element profile.
- The 60 Hz poll already exists and is already scoped to a visible tab
  (`IsTestTabVisible`); a Play sheet over a *running* game has to keep that
  scoping, or the poll competes with emulation.
- Remapping makes this the second surface that writes input config after the
  classic window, so ADR-0250's "one place per menu entry" has to be re-read:
  the entry moves to Play and the classic page stays as the deep end.
- Requirement 3's Core change is the expensive part and the one most likely to
  be re-scoped: the screen can ship with requirements 1 and 2 first.
- A pad assigned to P2 is invisible to the host as a player until the port
  assignment lands, so the PLAYERS section cannot be finished before the
  per-port model is decided (open question 1).
