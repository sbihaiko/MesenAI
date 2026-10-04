# ADR-0255: The Play door's Controller sheet is one surface for players, testing, remapping and extra buttons

- Status: accepted (2026-10-04). The three questions this ADR was written around
  were answered by the user on that day, quoted verbatim from the questions
  they answered: **"Por controle, pelo VID:PID"** (where a rebind writes),
  **"Estender o ShortcutKeyInfo"** (how an extra button is stored) and
  **"Sim, com um limiar"** (whether an axis can carry a digital action). One of
  the three does not survive contact with the code as stated; see "The answers,
  against the code" below, which is the part of this ADR that matters. The
  sheet itself is **not implemented** - the slices are listed under Decision.
  Slice 4's storage half and "the keyboard case" have since landed
  (2026-10-04): `ShortcutKeyInfo.PadBinding`, `PadShortcutBinding`,
  `PadAxisAction` and their readers in `EmulatorShortcut`/`PreferencesConfig`,
  plus `Configuration.RestoreKeyboardPresetIfNothingIsBound` with
  `UI.HeadlessTests/KeyboardPresetRecoveryTests`. Slice 4's **surface** has not
  - nothing in the app writes a `PadBinding`, and `PreferencesConfig` drops an
  axis binding before the core push (`!pad.IsAxis`), so a player-set threshold
  can be stored but cannot fire.
- Date: 2026-10-04
- Related: ADR-0241 (the four-door Player GUI), ADR-0249 (the Play sheets, the
  Esc router and W-P15 - the setup sheet this one sits beside), ADR-0250 (one
  place per door), ADR-0251 (the pad's way into the overlay), ADR-0254 (the
  sibling decision about focus loss), PRD Part B §8 and §13.5.2.
- Supersedes / amends: none.

## Context

Play's Settings › Controls tab has three rows and a **More in Options…**
button. The button calls `ConfigViewModel.OpenInOptions(tab)`
(`UI/Views/PlayerSettingsSheetView.axaml.cs`), which opens the **classic**
`ConfigWindow` on its Input page. The user's report is about that landing:
*"quando clico no 'More Options' aparece uma tela antiga do Mesen classico. que
tal algo bonitinho na linha desse site: https://hardwaretester.com/gamepad?"*

Four requirements came after the wireframe was chosen, and each widens the
problem beyond "make the tester prettier":

1. **View and remap.** *"quero poder vizualizar e re mapear cada controle"*
2. **Many pads, one per player.** *"podem ter varios controles conectados no
   notebook. tem que poder dizer qual fica com cada jogador"*
3. **Extra buttons become emulator actions.** *"se o jostick tiver mais botoes
   que o nitendo quero poder atribuir outros controles como retroceder,
   avancar, compartilhar, home, etc"*
4. **A keyboard has to play the game on its own.** *"se nenhum controle estiver
   conectado o teclado deve estar configurado de maneira padrao e permitir o
   jogo"*

### What already exists, found late

Most of the surface the wireframe proposed is **already built**, and this ADR
was nearly written against a duplicate of it:

- **W-P15, the setup sheet** (`UI/Views/PlayControllerSetupView.axaml`,
  `UI/Logic/PlayControllerSetup.cs`, `UI/ViewModels/PlayControllerSetupViewModel.cs`):
  a pad-driven sheet that binds a controller's own buttons to the console's, one
  lit step at a time, with hold-to-skip, silence-to-cancel, and a pad **drawn**
  from `ControllerPadLayout` at the pad's own coordinates.
- **The detection path**: `UnknownControllerDetector` notices a pad none of
  whose keys appear in any mapping, shows the "press Start on it" pill, and
  opens that sheet.
- **The host input tester** (I.0–I.3) inside `InputConfigView.axaml`'s
  `tpgInputTest` tab: a ~60 Hz poll through `GamepadTesterViewModel`, per-device
  deadzone overrides, stick circularity, drift, rumble.

**Non-goals:**
- Replacing the classic window's Input page. Deep per-console device setup
  (Four Score, the Zapper, the light guns, the Famicom keyboards) stays there.
- Remapping the keyboard. `UI/Views/ShortcutKeysTabView.axaml` owns that.
- A pad editor that draws every pad ever made.

## The answers, against the code

This is the finding that reshaped the decision, and the reason the answer to
question 1 is recorded as a correction rather than as a premise.

**"Por controle, pelo VID:PID" cannot be the key a rebind writes to.** Ports are
not keyed by VID:PID anywhere in this emulator, and they are not invented
either - they exist, and the answer has to be read against them:

- **A port is a `ControllerConfig`**: `NesConfig.Port1`, `Port2`, `ExpPort`,
  `Port1A` … `Port1D`, `MapperInput`; `GameboyConfig.Controller` and
  `LinkedController`; `GbaConfig.Controller`; `SmsConfig.Port1`, `Port2`.
  `PlayControllerSetupViewModel.PortFor` returns exactly these.
- **Each port holds four `KeyMapping` slots** - `Mapping1` … `Mapping4` - and
  the four are **alternatives within that one port**, not four players. W-P15's
  own comment says so: it writes "the first free mapping slot of the loaded
  console's **port 1**", which means the setup sheet that exists today can only
  ever assign a pad to player 1.
- **A mapping slot's keys carry the host device index**: a gamepad key code is
  `0x1000 + device * 0x100 + button` (`ControllerDevices.BaseGamepadIndex`).
  So *which pad* a slot speaks for is in the keys; *which player* it is is
  which `PortN` it hangs off.

VID:PID never reaches that path at all. It is host metadata, read by the tester
and by the per-device deadzone list, and the core is never told it.

So the answer means two things, and they are different layers:

1. **Assignment is a move between ports**, and per device index underneath:
   "this pad is P2" means its keys live under `Port2` instead of `Port1`, in
   whichever of `Port2`'s four slots is free. That is W-P15's write with a port
   chosen instead of hard-coded to the first one. There is no second store of
   "who is P1" - which is ADR-0250's rule applied to the one place it would
   have been easiest to break.
2. **VID:PID is the only thing that survives a reconnect**, and a reconnect is
   exactly what the answer was reaching for. A device index is an ordering, not
   an identity: unplug the pad, plug it back, and it can return as device 0
   where it was device 1, with its keys still under `Port2` - handing P2 to a
   different pad. The migration that repairs that has to be keyed by VID:PID.

The first draft of this ADR had (1) wrong: it read `Mapping1..4` as the four
ports, which would have made "P2" mean "the second alternative binding of
player 1". `Mapping1..4` are alternatives; the ports are `PortN`. Caught in
review before anything was built on it.

The other two answers survive as stated: `ShortcutKeyInfo` grows the slot (one
list of actions, one editor, and its consumers learn that a shortcut may have a
button and no key), and an axis may carry a digital action past a threshold the
player sets.

## Decision

**Accepted. The sheet replaces the landing, the port assignment is read off the
mappings, and the reconnect is repaired by VID:PID. Slices, in order:**

1. **The sheet exists and is Play-native.** Play › Settings › Controls ›
   *More in Options…* opens a Play sheet over the paused game (`Classes="sheet"`,
   the W-P8 card, Done, Esc) instead of the classic window, which stays one link
   away. It shows the live pad and its values, reusing `GamepadTesterViewModel`
   and the `PlayControllerSetupView` pad drawing rather than adding a third
   renderer.
2. **PLAYERS, read off the ports.** One row per port (`Port1`, `Port2`, …),
   naming the device whose keys are under it, with the player's color.
   Assignment moves a device's keys from one port's slots to another's; there is
   no second table of "who is P1". Sections appear
   by state, not as a fixed list: with no pad the sheet says what the keyboard
   does and offers the preset back; with one pad PLAYERS is absent, because there
   is nothing to assign.
3. **Remapping**, as a *mode* of the same sheet rather than a dialog: pick a row,
   press a control, Esc cancels. Each row carries **two lights** - what the pad
   sends and what the port receives - which is why testing and mapping are one
   line rather than two screens. The user kept both on 2026-10-04 (the
   alternatives offered were one light, or none): a row lit on the pad side and
   dark on the port side is a wrong binding made visible, and nothing else in the
   app shows one. Confirm, back and focus movement are **not** rebindable
   (ADR-0256 Decision 4), so they are excluded from this sheet entirely - the
   section below is the pad's spare controls, not its navigation.
4. **EXTRA BUTTONS**, the `ShortcutKeyInfo` slot, with the axis threshold from
   the third answer. The section is a filtered view of the one shortcut list.
5. **The reconnect repair**: on a pad appearing whose VID:PID was last seen at
   another device index, its keys move with it.

**The keyboard case (requirement 4)** is a state of the same sheet, and it also
closes a real hole - reported on the bug board, not adjudicated here, because a
reproducible bug is a bug before it is a decision: `Configuration.UpgradeConfig` applies the presets only on
first run, and `ResetSettings` refuses to write `DefaultKeyMappingType.None` -
so a `settings.json` that already carries `None` loads straight through with no
keyboard preset and nothing that restores it. The guard belongs where the
presets are resolved, not in one caller, and only when nothing is bound
anywhere, because a config whose keys the player bound by hand is theirs.

## Consequences

- **The wireframe's "PLAYERS" rows and the classic Input page now describe the
  same fact from two sides.** The sheet must write through the same
  `ConfigManager` path and the same `ApplyConfig()` call the classic page uses,
  or the two will disagree about which slot a pad is in.
- A device index is a connection ordering, so **every slice above inherits the
  reconnect problem**: a pad that comes back at a different index is bound to
  the wrong port until slice 5 lands. Slice 2 must not present slot membership
  as stable until then.
- The `ShortcutKeyInfo` change is the expensive one and the one most likely to
  be re-scoped: every consumer that reads "this shortcut's key" has to learn
  about a shortcut that has a button and no key. The screen can ship without it.
- The 60 Hz poll is already scoped to a visible tab (`IsTestTabVisible`); a
  sheet over a running game has to keep that scoping. Opening over the paused
  game (W-P4) is what makes that free.
- The player color has three places to appear - the port label, the pad's own
  light where it has one (DualShock 4/DualSense via `GCController.light`; `nil`
  on an Xbox pad, which is not an error state), and nowhere else. Colour that
  appears once is decoration, not language.
- **The pad's own light is the one promise above with no owner, and it is
  recorded here rather than silently dropped (2026-10-04).** No slice carries
  it, and it cannot be built from what exists: `GamepadInfo`
  (`Core/Shared/Interfaces/IKeyManager.h`) has no light field, nothing in `Core/`
  or `UI/` reads or writes one, and the only way to make a DualShock's light
  follow a player colour is a new output path - a core call the macOS key
  manager implements through the GameController framework, a no-op on Windows
  and Linux, whose pads have no addressable light at all. That is new
  cross-backend work with no headless test behind it and no pad carrying an
  addressable light in this environment, which is why it is named here instead
  of guessed at. Until it exists, the colour language this ADR asks for is the
  port label alone - and by this ADR's own test ("colour that appears once is
  decoration") that is a weaker language than the one it specifies.
