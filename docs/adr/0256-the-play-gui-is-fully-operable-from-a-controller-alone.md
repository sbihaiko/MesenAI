# ADR-0256: The Play GUI is fully operable from a controller alone

- Status: proposed (2026-10-04). **All four questions were answered by the user
  on 2026-10-04** and are recorded under Decision, quoted verbatim - this ADR is
  now a decided shape that nothing implements yet. Accepting it is a request for
  the work listed there, not a note. Ids are never reused (ADR-0035), which is
  why this is 0256 and not 0255.
- Date: 2026-10-04
- Related: ADR-0241 (Play's home and the W-P4 pause overlay), ADR-0249 (the
  rendered wireframes as the visual spec), ADR-0250 (every menu entry has one
  place per door; Classic is a fourth), ADR-0251 (the pad's way *into* W-P4),
  ADR-0255 (the Controller sheet, one of the surfaces this has to drive),
  ADR-0123 (host-free rules).
- Supersedes / amends: none.

## Context

The user's requirement, 2026-10-04: *"a GUI precisa poder ser tmb operada
tolatamente pelo joystick, lembre que isso pode ser um arcade sem teclado ou
mesmo mouse"*. An arcade cabinet, a TV with a pad and nothing else, an HTPC.
Every path through the app has to exist without a keyboard.

What exists today is only the **way in**. ADR-0251 §3 gave `ToggleOverlay` a
default second binding — the pad's Home/Guide button where the platform reports
one, otherwise Select and Start pressed together — so a player holding a
controller can open W-P4. That chord is a real shortcut, not a gesture: it goes
through `ShortcutHandler` into the same `PlayEsc` router the Esc key uses, so a
pad already opens and closes W-P4, closes a sheet back to W-P4, dismisses the
pack picker, answers Quit's "Keep playing", and backs out of the BIOS, load and
tool sheets. **Back, from a pad, already works everywhere in Play.**

What does not exist is everything else: W-P4's rows, the sheets behind them and
Play's home screen are all reached by pointer or by keyboard focus, and nothing
moves a pad's D-pad into either.

**The constraint that makes this a decision and not a chore: the pad is also
Player 1's controller.** Every button on it is already spoken for while a game
runs, and a D-pad press that moves a menu cursor is a D-pad press that moves
Mario. The app cannot have both at once, so the question is not "how do we
navigate with a pad" but "**when** is the pad the GUI's and not the console's".

There is already a good answer available, and it is worth stating because it is
the whole reason this is cheap: **W-P4 pauses the game.** While the overlay is
up, the pad is not being read as gameplay, so navigation is safe there. And the
one gesture that has to work while the game *is* running — opening W-P4 — is
already a chord (ADR-0251), chosen exactly so it cannot collide with play.

**Non-goals:**
- Driving the classic `ConfigWindow` or the debugger windows by pad. Those are
  the deep end, they are mouse-shaped, and the Play door is the one an arcade
  cabinet uses.
- Emulating a mouse with a stick. Focus traversal is the model; a pointer is
  not.
- Any per-view navigation code. If every Play surface has to learn about the
  pad, this will rot the first time a sheet is added.

## Decision

Not decided. The shape the four questions below have to settle:

1. **While a game runs unpaused, the pad is the console's and nothing else** -
   with one exception that already ships and that this rule has to name rather
   than contradict: W-P15's `UnknownControllerDetector` reads every connected
   pad while a game runs unpaused, to notice one whose keys no mapping uses and
   show the "press Start on it" pill (ADR-0249's setup sheet,
   `PlayControllerSetupViewModel`). So the honest form of the rule is that the
   pad has no *menu* authority while a game runs - the gestures that already
   exist are the chord and the detector, and anything new has to justify itself
   against both.
2. **While W-P4 is up, or with no game loaded, the pad drives the GUI**: focus
   moves, something activates, something goes back, following ADR-0249's Esc
   order so `game → W-P4 → resume` and `sheet → W-P4` read the same from a pad
   as from the keyboard.
3. **One focusable control at a time**, with the focus visible — an arcade
   cabinet has no cursor to fall back on, so "where am I" has to be drawn.
4. **Navigation is not rebindable** (the user's answer, 2026-10-04:
   *"Não reconfigurável"*). Confirm, back and focus movement follow the pad's own
   preset - `DefaultKeyMappingType.Xbox` or `Ps4`, which the first run already
   models - and no surface may unbind them. Esc stays fixed, as it always was.
   The reason is the one this ADR is written for: the target is a cabinet with a
   pad and nothing else, and a player who binds "confirm" to a control their pad
   does not have is stuck, with no keyboard and no pointer to recover with. It
   also settles what ADR-0255's extra-buttons slice may offer: the navigation
   controls are **excluded** from it, because offering them is the same bug with
   a nicer dialog in front of it.

5. **The gesture that opens W-P4 belongs to a button, not to device 0** (the
   user's answer, 2026-10-04: *"Qualquer controle"*). Today it does not:
   `PlayMenuHint.ControllerCandidates` hardcodes every candidate to `Pad1`
   (`"Pad1 Home"`, `"Pad1 Guide"`, `"Pad1 Select" + "Pad1 Start"`,
   `"Pad1 Back" + "Pad1 Start"`), and no backend exposes a Home or Guide pad
   button - `Core/Shared/KeyDefinitions.h` has `"Home"` only as keyboard
   scancode 22, and the per-platform pad button lists have neither - so the
   seeded binding is always `Pad1 Select` + `Pad1 Start`. With two pads
   connected, the one in the player's hand has no way into the overlay at all,
   and the overlay is the only route to the menus while a game runs. Filed as
   issue #800; this rule is the fix it has to satisfy.
6. **On-screen text names the control in the player's hand**, not the keyboard
   (the user's answer, 2026-10-04: *"Segue o controle na mão"*). W-P4's footer
   reads "Esc to resume" today, which is a lie on the cabinet this ADR is about:
   the player has no Esc key. Confirm and back follow the pad's own preset
   (`DefaultKeyMappingType.Xbox` or `Ps4`), so "back" is B on one desk and ○ on
   another, and the footer has to say which.

There is no second gesture into W-P4: the chord on any pad, and nothing else
(the user's pick, 2026-10-04, over adding a long-press). A pad whose Select or
Start is broken therefore has no way in, which is accepted rather than
overlooked.

The cheap implementation, and the one worth trying first: translate pad events
into the **keyboard navigation events Avalonia already handles** (arrow keys,
Tab, Enter, Escape) rather than teaching each view about the pad. Every
existing surface then works unchanged, and the wiring lives in one place next
to `ShortcutHandler`.

## The four questions, and how they were answered

All on 2026-10-04, by the user, quoted verbatim from the questions they answered.

1. **Which buttons are confirm and back?** **Answered by the navigation answer**:
   they follow the pad's own preset - `DefaultKeyMappingType.Xbox` or `Ps4`,
   which the first run already models - so "back" is B on one desk and ○ on
   another.
2. **Is navigation rebindable?** **"Não reconfigurável"** - no. See Decision 4.
3. **Can the player get stuck?** **Answered, and the answer found a live bug.**
   Checking the premise turned up issue #800: the chord is hardcoded to `Pad1`,
   so a second pad has no way in. The user's pick was "Qualquer controle" - the
   gesture is fixed per button rather than per device, and there is no second
   gesture. See Decision 5.
4. **Does the app say which pad it means?** **"Segue o controle na mão"** - the
   text follows the device in hand. See Decision 6.

## Consequences

- **The first run is the hard part.** Storage choice, keyboard preset and the
  ROM picker all happen before any game, before any pad binding exists, and
  possibly on a machine with no keyboard at all — so they need pad input from a
  path that has never been configured. This is the case a cabinet actually
  boots into, and it is earlier in the flow than everything above.
- Focus traversal has to be *drawn*, and here the ground is better than it
  looks: `PlayerTheme.axaml` has carried a `PlayerFocusRing` on `:focus-visible`
  since the theme landed, and it is now a three-layer glow on every button class
  the theme defines, with list rows glowing inward (`UI.HeadlessTests/
  PlayFocusGlowTests`). What is *not* covered is everything outside that theme -
  the classic `ConfigWindow`, the debugger windows - and Play's own surfaces
  wherever a focusable control is not a Button. `:focus-visible` is also the
  wrong trigger to rely on alone: it answers the keyboard, and whether a pad
  moving focus sets it is exactly one of the things this decision has to pin.
- Rule 1 means the pad's navigation authority is a function of the pause state,
  which is a function of ADR-0254's auto-pause as well — an overlay opened by a
  focus loss mid-game hands the pad to the GUI without the player asking.
- ADR-0255's Controller sheet is one of the surfaces this has to drive, so
  landing 0255 first and 0256 second is the cheaper order: the sheet is a
  single, self-contained place to prove the focus model before it is asked to
  carry the whole door.
- This ADR does not cover the pad's *way in* being discoverable; ADR-0251 owns
  that, and the count it keeps (`PlayMenuHintsShown`) is per install, not per
  pad.
