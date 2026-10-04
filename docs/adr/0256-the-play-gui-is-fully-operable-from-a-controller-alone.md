# ADR-0256: The Play GUI is fully operable from a controller alone

- Status: proposed (2026-10-04). The requirement is the user's; the four
  decisions under "Open questions" are theirs to make and nothing is
  implemented. Takes id 0256, not 0255: 0255 is live on the
  `docs/adr-0255-controller-sheet` branch (PR #796) and ids are never reused
  (ADR-0035).
- Date: 2026-10-04
- Related: ADR-0241 (the four-door Player GUI), ADR-0249 (the Play sheets and
  the Esc router), ADR-0250 (one place per menu entry), ADR-0251 (the pad's way
  *into* W-P4), ADR-0255 (the Controller sheet, one of the surfaces this has to
  drive), ADR-0123 (host-free rules).
- Supersedes / amends: none.

## Context

The user's requirement, 2026-10-04: *"a GUI precisa poder ser tmb operada
tolatamente pelo joystick, lembre que isso pode ser um arcade sem teclado ou
mesmo mouse"*. An arcade cabinet, a TV with a pad and nothing else, an HTPC.
Every path through the app has to exist without a keyboard.

What exists today is only the **way in**. ADR-0251 §3 gave `ToggleOverlay` a
default second binding — the pad's Home/Guide button where the platform reports
one, otherwise Select and Start pressed together — so a player holding a
controller can open W-P4. After that the trail stops: W-P4's rows, the sheets
behind them, and Play's home screen are all reached by pointer or by keyboard
focus, and nothing translates a pad event into either.

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

1. **While a game runs unpaused, the pad is the console's and nothing else.**
   No exception beyond ADR-0251's chord.
2. **While W-P4 is up, or with no game loaded, the pad drives the GUI**: focus
   moves, something activates, something goes back, following ADR-0249's Esc
   order so `game → W-P4 → resume` and `sheet → W-P4` read the same from a pad
   as from the keyboard.
3. **One focusable control at a time**, with the focus visible — an arcade
   cabinet has no cursor to fall back on, so "where am I" has to be drawn.

The cheap implementation, and the one worth trying first: translate pad events
into the **keyboard navigation events Avalonia already handles** (arrow keys,
Tab, Enter, Escape) rather than teaching each view about the pad. Every
existing surface then works unchanged, and the wiring lives in one place next
to `ShortcutHandler`.

## Open questions

1. **Which buttons are confirm and back?** A and B are swapped between a
   Nintendo-style pad and an Xbox one, and the app already models that
   difference: `DefaultKeyMappingType.Xbox` and `Ps4` are first-run presets.
   Whether navigation follows the pad's own preset, or is fixed, decides
   whether "back" is B or A on a given desk.
2. **Is navigation rebindable?** ADR-0255 §3 wants the extra buttons assignable
   to anything, and navigation is the obvious thing to assign. But a player who
   rebinds "confirm" to a control their pad does not have has bricked the
   cabinet, with no keyboard to recover from. Whatever is chosen needs an
   answer to that, and "not rebindable" is a legitimate one.
3. **Can the player get stuck?** With rule 1, a running game offers exactly one
   gesture. If that chord is rebound away, or the pad is one whose Home button
   the platform does not report, is there a second way in?
4. **Does the app say which pad it means?** W-P4's footer reads "Esc to
   resume" and the entry toast names the binding that started the game
   (ADR-0251). Whether those strings follow the device in hand — "B to resume"
   on a pad — or stay keyboard-worded is a small decision with a wide reach,
   because every Play surface has a footer.

## Consequences

- **The first run is the hard part.** Storage choice, keyboard preset and the
  ROM picker all happen before any game, before any pad binding exists, and
  possibly on a machine with no keyboard at all — so they need pad input from a
  path that has never been configured. This is the case a cabinet actually
  boots into, and it is earlier in the flow than everything above.
- Focus traversal has to be *drawn*. Every focused control today is styled for
  a mouse hovering it; a pad has no hover, so the focus ring becomes the only
  feedback and is currently not specified anywhere.
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
