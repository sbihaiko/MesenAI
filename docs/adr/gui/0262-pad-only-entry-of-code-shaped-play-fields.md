# ADR-0262: A pad fills every Play text field through one shared on-screen keyboard owned by the pad bridge

- Status: accepted (2026-10-07) — **option (a), ratified on issue #966** by
  the owner's designated human proxy, GPT Astra fast. The owner's own
  sentence (2026-10-07): *"se precisar de ajuda para decidir use o gpt astra
  fast como proxy humano"*. The proxy's ruling, verbatim: **"#966 | PICK:
  (a), a shared on-screen pad keyboard owned by the bridge | The owner
  explicitly requires operation on an arcade cabinet without a keyboard or
  mouse. A code wheel leaves intent search, descriptions and key entry
  inaccessible; browsing a list does not replace those functions. A shared
  keyboard costs more initially but preserves ADR-0256 and serves subsequent
  fields. | Conditions/limits: no per-view navigation logic;
  field-appropriate alphabets, including Game Genie letters; edit/delete,
  commit and cancel entirely by pad; cancel preserves the original value and
  restores focus; secrets remain masked. Require synthetic-pad coverage and
  couch testing with a real pad; revise proposed ADR-0262 accordingly."**
  Implemented in the same turn under CLAUDE.md's same-turn rule: the change
  ships with unit tests covering the decision (`UI.Tests/Play/PadKeyboardTests`)
  and synthetic-pad headless coverage (`UI.HeadlessTests/PlayPadKeyboardTests`).
  The real-pad couch check joins issue #926.
- Date: 2026-10-06 (proposed), 2026-10-07 (accepted, rewritten to option (a))
- Related: ADR-0256 (the Play GUI is fully operable from a controller alone —
  its stop rule, and the bridge that owns pad focus), ADR-0245 (W-P11 Cheats:
  search, intent search, *Add a Code…*; §5, the only cheat path on GB/SMS),
  ADR-0249 (the rendered wireframes), ADR-0122 (the host-free firewall), PRD Part B
  §13.3 rules 9 and 10, issues #966 and #926.
- Supersedes / amends: nothing. This revises its own `proposed` text, which
  recommended option (c), a bounded code wheel, with free-text fields left
  keyboard-only. The ratified pick keeps every text field inside ADR-0256's
  stop rule and PRD §13.3 rule 9, so neither is amended.

## Context

ADR-0256 requires every Play path to be reachable and reversible from a pad
alone, and PRD §13.3 rule 9 says keyboard and gamepad reach everything in Play.
The pad bridge (`UI/Windows/PlayPadNavigationWiring.cs`) recorded its own
limit: a `TextBox` fell through to the raise, because a pad cannot type. W-P11
Cheats (ADR-0245) has five text fields in `UI/Views/PlayerCheatsSheetView.axaml`:
the cheat search box, the intent search box, the API key box, the new-code box
of *Add a Code…* and its description box. ADR-0256 decided nothing about them;
its only text-entry ruling is the ROM picker's refusal of path typing
(Decision 9).

Issue #966 offered three options: (a) an on-screen pad keyboard owned by the
bridge; (b) declare code entry keyboard-only and say so on the sheet, per
rule 10; (c) pad-only entry limited to the shapes a code takes. The autonomy
panel recommended (c) by 2–1; the ratification picked (a), because the owner
requires operation on an arcade cabinet with no keyboard or mouse, and a code
wheel leaves intent search, descriptions and key entry out of reach.

## Decision

1. **One shared on-screen keyboard, owned by the one pad bridge, fills every
   Play text field.** A pad's Confirm on a focused, enabled, editable
   `TextBox` opens it. There is no per-view navigation logic: no view
   handles a pad press, and no view knows the keyboard exists. The keyboard's
   rules are host-free (`UI/Logic/PadKeyboard.cs`, ADR-0122); the bridge only
   feeds it the pad's actions and applies its outcomes. The keyboard is drawn
   in the window's overlay layer; where a window has no overlay layer, no
   keyboard opens and the Confirm press falls through to the bridge's ordinary
   navigation, since an invisible keyboard would swallow every press.
2. **The field declares its alphabet; the keyboard does not ask the view.**
   A masked box (`PasswordChar` set) is a *secret*; a box carrying the
   `padCode` style class is *code-shaped*; every other box is *free text*.
   - Code: the sixteen NES Game Genie letters first (`APZLGITYEOXUKSVN`), then
     the hex digits they do not already cover, then the `-`, `:` and `+`
     separators the Game Genie and Pro Action Replay shapes use. No space and
     no case key: code letters are capitals.
   - Free text: lower-case letters, digits, `. , ' - ! ? & : ( ) / _ + # @`,
     a space and a case key — the punctuation game titles and cheat
     descriptions carry ("Bubble Bobble (Part 2)", "Kid Icarus / Of Myths").
   - Secret: lower-case letters, digits, `- _ .`, a space and a case key.
   - A field's `MaxLength` caps the draft.
3. **Edit, delete, commit and cancel are all by pad.** While the keyboard is
   open every press is the keyboard's: the D-pad walks its keys (left and
   right wrap, up and down move a row), Confirm presses the key under the
   cursor — a character, space, case, delete (`⌫`, the last character) or
   commit (`OK`). The draft is written into the field as it changes, so a
   search filters while it is typed. The focus never leaves the field, and
   the pad's Back cannot close the sheet under the keyboard.
4. **Cancel gives back the original value and the focus.** The pad's Back
   cancels: the field gets back the text it had when the keyboard opened, and
   the focus returns to that field with its ring drawn. Commit keeps the draft
   and returns the focus the same way. A field that goes away under the
   keyboard (its sheet closed by something else) takes the keyboard with it,
   as a cancel. The focus moving off the field (a mouse click elsewhere) also
   closes it as a cancel. The pad losing authority mid-entry (a stray key or
   mouse event, the load card) closes it **committing** the draft, so a
   player never silently loses typed text. In these three outside closes the
   focus is left where it went: the field is not refocused, so the pad never
   comes back editing a field it no longer holds. The rule is host-free
   (`PadKeyboard.TextOnLeave`).
5. **Secrets stay masked.** The keyboard draws a secret's draft as `•` per
   character, never its characters; the field keeps its own mask.

## Consequences

- *Add a Code…* — the only cheat path on GB/SMS (ADR-0245 §5) — and every
  free-text field (search, intent search, the description, the API key) are
  reachable from a pad, which keeps ADR-0256's stop rule whole instead of
  narrowing it.
- A new text field joins with no code: it gets the free-text alphabet, or
  declares `padCode` or a mask. A new alphabet is a change to
  `PadKeyboard` and its unit tests, never to a view.
- The bridge grows one input surface, drawn in the window's overlay layer
  below the field (above it when there is no room). Nothing in it is
  focusable, so the focus model is unchanged.
- Coverage: the rule is pinned host-free in `UI.Tests/Play/PadKeyboardTests`;
  `UI.HeadlessTests/PlayPadKeyboardTests` fills, commits and cancels the
  Cheats search field and fills and commits the *Add a Code…* form with a
  synthetic pad only. The real-pad couch check is a human check on issue
  #926.
- Any other Play `TextBox` the pad can focus, such as the Tool sheet's barcode
  box, gets the free-text keyboard; a digits-only alphabet for it, if wanted,
  is a `PadKeyboard` change.
