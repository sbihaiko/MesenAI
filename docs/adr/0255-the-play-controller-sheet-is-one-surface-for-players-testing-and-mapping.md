# ADR-0255: The Play door's Controller sheet is one surface for players, testing, remapping and extra buttons

- Status: accepted (2026-10-04). The three questions this ADR was written around
  were answered by the user on that day, quoted verbatim from the questions
  they answered: **"Por controle, pelo VID:PID"** (where a rebind writes),
  **"Estender o ShortcutKeyInfo"** (how an extra button is stored) and
  **"Sim, com um limiar"** (whether an axis can carry a digital action). One of
  the three does not survive contact with the code as stated; see "The answers,
  against the code" below, which is the part of this ADR that matters. The
  sheet itself **was not implemented** when this was written - the slices are
  listed under Decision, and what has landed since is at the end of this block.
  Slice 4's storage half and "the keyboard case" have since landed
  (2026-10-04): `ShortcutKeyInfo.PadBinding`, `PadShortcutBinding`,
  `PadAxisAction` and their readers in `EmulatorShortcut`/`PreferencesConfig`,
  plus `Configuration.RestoreKeyboardPresetIfNothingIsBound` with
  `UI.HeadlessTests/KeyboardPresetRecoveryTests`. Slice 4's **surface** has not
  - nothing in the app writes a `PadBinding`, and `PreferencesConfig` drops an
  axis binding before the core push (`!pad.IsAxis`), so a player-set threshold
  can be stored but cannot fire.
  **Slice 5 landed 2026-10-04** as re-specified below, after the implementer put
  the block's own question back to the coordinator - "is a repair that cannot fire
  on two of three backends worth shipping at all?" - and the answer was **ship
  it**, on the terms recorded under Decision 5: it is a real repair on the two
  backends that report a VID:PID, it names nothing where it cannot, and its cost
  is bounded and stated.
  **Slices 1 and 2 landed 2026-10-04** (#811, with its three defects and five
  review findings fixed in #825, then the PLAYERS surface in #826), and the corrections they
  needed landed with them or right after (#834): the sheet's own focus claim
  (ADR-0256 Decision 3 — without it the arbiter focused the surface *under* the
  sheet), the device moves' two write-side defects (a dropped reconnect move
  counting as vacating its source index, and a slot move leaving a port type's
  custom keys behind), and slice 1's own correction — the pad's
  `GamepadState.Buttons` order is **per backend**, which
  `Core/Shared/GamepadButtonOrder.h` now carries for the core and
  `scripts/checks/verify_pad_button_tables.py` guards against the three
  backends' tables.
  **Slice 3 landed 2026-10-04** (#839), and it settled two rules the shape above
  did not. The rows are the loaded console's own controls - the list W-P15's
  setup already walks (`ControllerSetupSteps.For`), so the two surfaces can bind
  the same set - each carrying the two lights; picking one arms a capture whose
  first button must be released before it listens, whose navigation controls are
  refused *visibly* (ADR-0256 Decision 4) and whose writes go through the same
  `ConfigManager`/`ApplyConfig()` pair the classic Input page uses. The host-free
  rules are `UI/Logic/ControllerSheetRemap.cs` (with
  `UI.Tests/Play/ControllerSheetRemapTests.cs`), the sheet half is
  `UI/ViewModels/ControllerSheetViewModel.Remap.cs`, and the window behaviour is
  `UI.HeadlessTests/PlayerControllerSheetTests.cs`. The two rules:
  - **While a capture is armed, the pad is the capture's** - and it says so
    through the predicate ADR-0256's bridge already asks
    (`PlayPadNavigationWiring.HasAuthority` gains `!_model.IsControllerCapturing`)
    rather than through a second competing rule, which is what keeps a pad
    Confirm from both binding a control and activating the sheet's Done.
  - **The sheet's own mode dies with the sheet.** The poll that ends a capture
    when the pad goes away stops with the sheet, so `Close()` ends it too: found
    in review, where Done-with-the-pointer left a capture armed whose answer kept
    the bridge without authority for the rest of the session - the pad moved no
    focus and confirmed nothing anywhere in the Play door. The case is
    `Closing_the_sheet_ends_the_capture_it_was_in`.
  Four more defects the same review found are fixed in the slice, each with a
  RED: the Master System rows were labelled with the console's buttons the wrong
  way round (the core's `GetKeyNames()` is "UDLR12P", so the field it reads as B
  is button 1 and the field it reads as A is button 2; W-P15's older copy of the
  swap is fixed with it, through the one rule in
  `ControllerSheetRemap.ControlLabel`); a game resumed by a pad shortcut left the
  capture armed with a frozen baseline, so the first tick after a re-pause bound
  whatever the player was holding by then - a button pressed only to play; a port
  holding two devices in different slots sent the rebind to the first port instead
  of the one holding the pad's keys; and a keyboard binding named "Page Up" or
  "Page Down" lit a row's pad side, because the name was matched from its second
  word on.
  Three limits are carried rather than solved: a rebind does not clear the same
  pad button from another control, the port light reads the first non-zero field
  across the port's four slots, and the section was never visually evaluated with
  the pad, PLAYERS and REMAP all on screen at once.
  **Still not implemented**: slice 4's surface (the extra buttons), so the sheet
  is not yet the whole of what the Decision describes.
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

**Point (2) also does not survive as stated, and this is the second correction
of the same kind (found 2026-10-04, while implementing slice 5).** The answer
was *"Por controle, pelo VID:PID"*, and the repair was to be keyed by VID:PID -
but **two of the three backends do not report a VID:PID at all**:

| Backend | `GamepadInfo.VendorId`/`ProductId` | Source |
|---|---|---|
| macOS (GameController) | **always 0:0000** | `MacOS/MacOSKeyManager.mm` sets both to 0, with the comment "The GameController framework does not expose VID/PID" |
| Windows XInput | **always 0:0000** | `Windows/WindowsKeyManager.cpp`: "XInput exposes no VID/PID" |
| Windows DirectInput | real | `DirectInputManager::GetVendorId/GetProductId` |
| Linux (evdev) | real | `LinuxGameController::GetVendorId/GetProductId` |

So a VID:PID-keyed repair is a **no-op on macOS** - the platform the tagged
release ships on - and on every XInput pad on Windows. What survives a reconnect
there is nothing: an XInput pad's name is `"XInput Pad N"`, which names its
slot, and a GameController pad's name is its product name, which two identical
pads share. There is no identity to migrate by, and this ADR will not pretend
there is.

**The index the repair would compare against is also the wrong axis**, and that
is a defect in its own right, filed as #813: `WindowsKeyManager::GetGamepadInfo`
enumerates the four XInput slots and *then* DirectInput, so the `index` it is
given is a **global** ordinal, while the device a mapping's key code carries is
**family-relative** (`base + device*0x100 + button`, with `device` the XInput
slot or the DirectInput ordinal). The same function then writes `info.Slot = i`
for XInput and `info.Slot = index` for DirectInput - one field, two meanings.

**So slice 5 is re-specified to what the host can actually support:**

1. The identity is `(Backend, VendorId, ProductId)`, and a pad whose pair is
   `0:0000` is **unidentified**. An unidentified pad is never moved - never
   matched, never guessed at.
2. The index compared is the one the config's keys carry: the family's own
   numbering, taken from `Backend` and `Slot` once #813 makes `Slot` mean one
   thing. Until then the repair cannot be correct on Windows, which is why #813
   is a blocker for it rather than a nicety.
3. On macOS and on Windows XInput the repair **never fires**, because every pad
   there is unidentified. Slice 2's copy must not promise stable membership on
   those backends: that is the honest reading of "a device index is a connection
   ordering", and it is stronger than the ADR first said.
4. Two present pads sharing an identity make a move **ambiguous**, and an
   ambiguous move is dropped rather than guessed.

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
5. **The reconnect repair**: on a pad appearing whose **identity** was last seen
   at another device index, its keys move with it. The identity is
   `(Backend, VendorId, ProductId)` and a pad reporting `0:0000` is unidentified
   and never moved; two present pads sharing an identity make the move ambiguous
   and it is dropped; and on macOS and Windows XInput, where every pad is
   unidentified, the repair does not fire at all. See "The answers, against the
   code" for why - and #813, which blocks this slice on Windows.

   **Landed 2026-10-04**, and the review of it added four rules the shape above
   did not settle. They are part of the decision, not implementation notes:
   - **A dropped move does not re-baseline the pad.** The history remembered the
     pad's new index even when the move had been dropped, which claimed an index
     the pad's keys do not occupy; a later, otherwise-legal move then rewrote the
     keys of the pad that *does* occupy it. A dropped move leaves the keys where
     they were, so the baseline stays there too.
   - **A pad whose backend is unresolved (`GamepadBackend.None`) is refused the
     same way `0:0000` is.** With no known family there is no known key block, and
     reading it as the one-family default is the guess point 1 forbids. Unreached
     by the three shipping backends, which all set `Backend`; refused anyway,
     because "unreached today" is not a rule.
   - **The observation runs while the Play door is up, home screen included** -
     not only over a running game. The keys have to be right *by the time* a game
     reads them, and the home screen is the moment before that, not after. The
     cost is one pad enumeration per 50 ms on that door; on the backends that
     report a VID:PID it can move something, and on macOS and Windows XInput it
     cannot fire at all. Gating it on a backend the host can identify is not
     available host-free, and the honest statement of the cost is preferred to a
     platform branch in the UI.
   - **The write goes through the same `ConfigManager`/`ApplyConfig()` pair the
     classic Input page uses, and only when a key actually moved** - so a
     reconnect that touches no bound key never rewrites `settings.json`.

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
- **The reconnect repair has two limits that survive it, and they are limits of
  the identity, not of the implementation (recorded 2026-10-04 with slice 5).**
  The first is that a **single** pad of a model that appeared twice cannot be
  told from its sibling: with only one of two identical pads present after a
  disconnect, nothing distinguishes it from the other, so a reconnect can still
  move its keys. Point 4 only drops the move when both are *present*; no
  VID:PID scheme can disambiguate a lone sibling, and inventing one (a serial, a
  connection order) is the guess this ADR refuses. The second is that the repair
  walks `Port1A`/`Port1B` (the Four Score's P3/P4) but `NesConfig.ApplyConfig`
  pushes those two from `Port1`/`Port2`, so that part of the walk is decorative
  until the Four Score's own push path is fixed - pre-existing, named here so it
  is not mistaken for coverage.
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
