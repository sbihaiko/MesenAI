# ADR-0262: A pad fills Play's code-shaped fields through a bounded code wheel; free-text fields stay keyboard-only

- Status: proposed (2026-10-06) — **awaiting owner ratification (issues
  #966/#968, label needs-ratification)**. The Decision below is a
  **recommendation, not a decision**: the autonomy panel is not the owner, and
  issue #966 requires a recorded pick with the owner's words quoted. Panel
  recommendation on issue #966: **AGREED 2–1 — option (c)**. Composition,
  quoted from the ruling: *"adversarial fallback (Codex out of usage until
  23:42); lenses: Anthropic panel lens B, agy/Gemini 3.8 Flash sitting in;
  split → blind third lens Grok 4.6."* Majority reasons, quoted from the
  ruling: *"a wheel per code shape is small and testable host-free; free text
  already has pad routes (toggle list, name-search list); ADR-0256 took the
  same line for the ROM picker ('no path typing'); pure (b) would leave Add a
  Code (the only cheat path on GB/SMS per ADR-0245 §5) unreachable from a
  pad."* Dissent (agy), verbatim: **"(b) Declare text entry keyboard-only
  with a one-line reason per PRD §13.3 Rule 10 and amend ADR-0256's stop rule
  to exclude typing; bundled cheats remain fully browsable and toggleable via
  pad alone, honoring ADR-0256's non-goal forbidding per-view navigation code
  and avoiding virtual keyboard bloat in the pad bridge."** Until the owner
  picks and their words are quoted here, this ADR binds nothing and amends
  nothing. Accepting it will be a request for work: the implementation is a
  **separate slice** and is **not implemented in this PR**, which is docs
  only.
- Date: 2026-10-06
- Related: ADR-0256 (the Play GUI is fully operable from a controller alone —
  its stop rule, and the bridge that owns pad focus), ADR-0245 (W-P11 Cheats:
  search, intent search, *Add a Code…*; §5, the only cheat path on GB/SMS),
  ADR-0249 (the rendered wireframes), ADR-0122 (the host-free firewall), PRD Part B
  §13.3 rules 9 and 10, issue #966.
- Supersedes / amends: none while `proposed`. If the owner accepts it, it
  would narrowly amend ADR-0256's stop rule and PRD §13.3 rule 9 — committing
  a shaped code inside the stop rule, free-text typing outside it — with no
  section of either renumbered.

## Context

ADR-0256 requires every Play path to be reachable and reversible from a pad
alone, and PRD §13.3 rule 9 says keyboard and gamepad reach everything in Play.
The pad bridge records its own limit in place
(`UI/Windows/PlayPadNavigationWiring.cs`): a `TextBox` falls through to the
raise, because a pad cannot type. W-P11 Cheats (ADR-0245) has five text fields
in `UI/Views/PlayerCheatsSheetView.axaml`: the cheat search box, the intent
search box, the API key box, the new-code box of *Add a Code…* and its
description box. ADR-0256 decided nothing about them; its only text-entry
ruling is the ROM picker's refusal of path typing (Decision 9).

Issue #966 offered three options: (a) an on-screen pad keyboard sheet owned by
the bridge; (b) declare code entry keyboard-only and say so on the sheet, per
rule 10; (c) pad-only entry limited to the shapes a code takes.

## Decision

1. **A bounded code wheel, owned by the one pad bridge, fills the code-shaped
   fields.** Its alphabet is the code's shape and nothing more: Game Genie
   letters and Pro Action Replay hex digits, on the Cheats sheet's new-code
   box. Its scope is the Cheats sheet that issue #966 asked about; other
   sheets' fields are out of scope here. There is no per-view navigation code — the wheel is the
   bridge's, and a field opts in by declaring its shape.
2. **The free-text fields stay keyboard-only**: cheat search, intent search,
   the API key and the code description. Each says on screen, in one line per
   PRD §13.3 rule 10, both why it needs a keyboard and what to do next — for
   the API key box (`CheatsKeyBox`, masked with `PasswordChar`), e.g. "Needs a
   keyboard: use a keyboard or paste the key from Settings" — so a pad-only
   player is never left at a dead field. Their pad routes already exist without typing: the
   bundled list is browsed and toggled by pad, and the name-search list is a
   list.
3. **The stop rule would be amended narrowly, on acceptance.** Committing a shaped code (*Add a
   Code…* from wheel entry to the committed cheat) is inside ADR-0256's stop
   rule and PRD §13.3 rule 9. Free-text typing is not. No general on-screen
   keyboard is added (option (a) is not taken).

## Consequences

- *Add a Code…* — the only cheat path on GB/SMS (ADR-0245 §5) — becomes
  reachable from a pad, which pure option (b) would have left unreachable.
- The wheel is a small, host-free rule per code shape (ADR-0122), testable
  without a window; the headless acceptance is that a synthetic pad alone
  fills and commits a code on the Cheats sheet, or the sheet shows its
  one-line reason and next step for a keyboard-only field.
- The bridge grows one input surface, bounded by the code shapes it lists. A
  new code-shaped field joins by declaring its shape; a new free-text field
  must carry the rule-10 line instead.
- While this ADR is `proposed`, ADR-0256 and PRD §13.3 rule 9 stay
  unamended and carry no note. On acceptance, each gets an *Amended by
  ADR-0262* note; their section numbers stay unchanged because they are cited
  elsewhere.
- The Tool sheet's barcode box (`UI/Views/PlayerToolSheetView.axaml`) is not
  covered; a pad route for it, if wanted, needs its own decision.
- Implementation is a separate slice, tracked from #966; nothing in `UI/`
  changes with this ADR.
