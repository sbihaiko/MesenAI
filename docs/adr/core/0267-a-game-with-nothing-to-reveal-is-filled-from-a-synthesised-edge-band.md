# ADR-0267: A game with nothing to reveal is filled from a synthesised edge band, not left with a disabled switch

- Status: proposed 2026-10-08 — an open either/or (options A–D below), awaiting the owner's pick.
  Nothing here is implemented, and the current behaviour is *not* a bug: see Context. Every option
  amends ADR-0253 §1/§3/§4; options B and C also add one source to that ADR's fallback chain, and
  option A instead corrects ADR-0253 §2 in place (a refinement, not a reversal, so no superseded
  line is owed either way). **Option C additionally amends ADR-0253 §2**: its SMS per-console
  scope line ("Reveal is offered but has no map columns to show, so it always uses the fallback")
  is replaced by the synthesised band, and its Reveal-source contract is widened so that extended
  columns no longer imply revealed map columns. See Decision, Option C.
- Date: 2026-10-08
- Related: issue #1082; ADR-0253 (the Reveal and its fallback chain — §1 the one switch, §2 the
  per-console scope, §3 the content-aware fallback and its "never on their own" rule, §4 the
  per-game measurement); ADR-0149 (the border layer); ADR-0162 (the accuracy suite: standard
  frames stay bit-identical); ADR-0163 (fork–upstream coexistence: `SmsVdp` is upstream-owned);
  ADR-0236 (recorded captures are keyed to 256-wide cell positions); PRD Part B §6.1 (the WideScrn
  row) and §8 (the W.1–W.7 slices); `docs/specs/MEP-v1.md` §5.5 (the `widescreen` section).
- Supersedes / amends: amends ADR-0253 §1 ("the stretch to 16:9 is dropped"), §3 ("a border or
  black alone never makes a game supported") and §4 ("SMS/SG-1000 without pack art are known
  unsupported before the game runs, so the switch is disabled at once"). **If Option C is
  accepted it also amends §2**, in two places: the per-console scope entry for SMS/SG-1000, and
  the Reveal-source contract that today ties "extended columns" to map content the console
  reveals. Options A, B and D leave §2 untouched. No option amends §2's arithmetic — a frame is
  still `2N` pixels wider, and the standard mode is still bit-identical (ADR-0162).

## Context

**The report (issue #1082).** Loading After Burner (SMS), pausing and opening Enhancements shows
the Widescreen switch *disabled*, with the one-line reason "This game has nothing to show beside
the picture", so the 4:3 picture stays pillarboxed. The reporter expects widescreen to be
possible for this game.

**What was checked, and what it says.** The check is not wrong; the message is literally true for
the SMS.

- The SMS VDP's name table is 32 tiles per row, i.e. exactly the 256 px line the VDP renders
  (`SmsVdp`: the Mode 4 background fetch indexes the tile map as `(x / 8) + (y / 8) * 32`, and the
  horizontal scroll wraps that row onto itself). There is no map column beside the picture, so
  there is nothing for a Reveal to show — unlike the NES nametable, the GB/GBC 256×256 map or the
  GBA text BGs.
- The core never widens an SMS frame: `SmsVdp` stamps `RenderedFrame::ExtendedColumns` only for a
  Game Gear, through `SmsWidescreenReveal::RevealedColumns`, which returns 0 whenever `isGameGear`
  is false. The Game Gear case is a different thing entirely — it drops the 160-px viewport's own
  overscan crop, so it reveals pixels the VDP already drew.
- That is pinned by a test, not incidental: "W253b: the Master System does not reveal (its screen
  is the whole VDP line)" in `scripts/core_unit_tests.cpp`.
- The app's rule agrees with the core about this console, and says so before the game is even
  measured: `WidescreenSupportRule.ConsoleHasSideMap(ConsoleType.Sms, gameGear: false)` is false,
  and `SwitchForLoadedGame` is called from `MainWindowViewModel.RefreshEnhancementsState` with
  `RomInfo.ConsoleType` and `RomFormat.GameGear`. 19 cases in
  `UI.Tests/Play/WidescreenSupportRuleTests.cs` and
  `Widescreen_switch_is_disabled_with_its_reason_when_the_game_cannot_use_it` in
  `UI.HeadlessTests/PlaySheetsViewTests.cs` pin the current behaviour and pass.

So this is not a bug to fix. The gap the report exposes is in the product:

1. **ADR-0253 contradicts itself about exactly this console.** §2 lists "SMS/SG-1000: Reveal is
   offered but has no map columns to show, so it always uses the fallback (decision 3)", while §3
   says the border and black "never on their own make a game supported, so they never keep the
   switch enabled", and §4 says SMS/SG-1000 "are known unsupported before the game runs, so the
   switch is disabled at once". §4 (what the code does) is the only implementable reading: a
   fallback that cannot make a game supported cannot keep its switch on. §2's sentence about SMS
   is dead prose — it promises a player a fallback that §3 and §4 forbid.
2. **The literal want is already reachable elsewhere, with the distortion ADR-0253 §1 removed.**
   `VideoAspectRatio.Widescreen` still exists, Player Settings' Display surface still offers it in
   its aspect-ratio dropdown (`PlayerWindowSettingsViewModel.ShortAspectRatios`, the
   `cboDisplayAspectRatio` control), and `AspectRatioMath::ComputeAspectRatio`
   still returns a flat 16:9 for a frame that carries no extra columns. §1 dropped the stretch as
   the *Enhancements switch's automatic mode*, not as a setting. The reporter's pillarboxes can be
   filled today from Player Settings → Display → Aspect ratio → Widescreen — stretched, which is
   the distortion this chain exists to avoid, and nowhere near the switch that names the feature.

**Non-goals (unchanged from ADR-0253):** patching the game's logic so enemies spawn earlier;
widening sprite evaluation or anything CPU-visible; changing save states, movies or netplay;
SNES.

## Decision

*Proposed — the recommendation is option C, and the owner picks. The options are ordered by cost,
cheapest first.*

- **Option A — status quo; reconcile the prose.** Keep the switch disabled for SMS/SG-1000 and
  point the player at Player Settings → Display → Aspect ratio → Widescreen for the plain stretch.
  Zero code. What it
  costs: the Enhancements sheet that owns the widescreen *feature* says the game cannot do it,
  while Player Settings' Display tab offers the aspect ratio that would — and ADR-0253 §2 keeps
  promising SMS a fallback that §3 and §4 forbid. So this option is only honest together with an
  in-place correction of §2's SMS sentence, plus a decision on whether the reason string should
  point at the Display setting.
- **Option B — the switch applies the fill.** Keep the Widescreen switch *enabled* for a console
  with no side map (and, for consistency, for a game the measurement settled as unsupported), and
  let turning it on apply `VideoAspectRatio.Widescreen` — the pre-ADR-0253 behaviour, already
  implemented in `AspectRatioMath`. The one-line reason is reworded from "nothing to show beside
  the picture" to a fill wording (for example "Nothing to reveal beside the picture; widescreen
  will only stretch it"), so the switch never claims a Reveal it does not have. Cost: re-admits
  the 16:9 distortion ADR-0253 §1 exists to remove, this time as a deliberate opt-in rather than
  the default; changes `WidescreenSupportRule` (§4's "disabled at once" becomes "enabled, fills")
  and its 19 pinned cases; one new resource string. This is the smallest change that answers the
  report as written.
- **Option C — a synthesised edge band, revealed like real columns. *Recommended.*** The SMS VDP
  emits its line `2N` columns wider — §2's frame-width arithmetic, unchanged — where the extra
  pixels of a scanline repeat the nearest real column of that same scanline. Those rows are marked
  in the per-row side-fill map (`RenderedFrame::ExtendedSideFill`) as *synthesised*, and
  Decision 3's chain gains one ordered source: pack art → **synthesised edge band** → border →
  black. §3's "never on their own" clause is amended to admit the edge band as a mode, while a
  static border and plain black stay fill-ins: the band is made of the picture's own pixels, per
  row, so it tracks the game; a border image and black do not. Cost: real work in `SmsVdp`
  (upstream-owned, ADR-0163) and a new source in `WidescreenFallback`; the per-console N must be
  chosen so 16:9 lands at the console's own pixel aspect, and the SMS's picture is not a fixed
  height (256×192 at 8:7 is ≈1.52 and needs ≈21 columns per side; the 224-line mode the VDP can
  also select needs ≈46 — against the NES's 64 per side and the GB's 48), so N follows the frame's own
  height rather than being one constant; the HD path, the recorder and
  the capture tools see a wider frame. Two edits must land together, or the switch and the core
  disagree: the new source feeds `WidescreenFallback::SupportsWidescreen` (which
  `NesWidescreenSupport::Reveals` reads), and `WidescreenSupportRule.ConsoleHasSideMap` — the
  app-side predicate that disables the switch before the game even runs — has to admit a console
  whose sides the band fills. Pack art still wins per row, so a pack that ships §5.5 art is
  unaffected.

  **C amends ADR-0253 §2, and this ADR states it rather than claiming otherwise.** Two clauses
  move:

  - **The per-console scope entry.** §2 currently reads "SMS/SG-1000: Reveal is offered but has no
    map columns to show, so it always uses the fallback (decision 3)." Under C that entry becomes:
    SMS/SG-1000 has no map columns to reveal, so its extended columns are *synthesised* by the VDP
    from the picture's own edge pixels. The claim "always uses the fallback" is dropped — the band
    is produced where the pixels are made, like every other console's Reveal, and only what the
    band leaves unstated (nothing, in the plain case) falls through to Decision 3.
  - **The Reveal-source contract.** §2's contract binds an extended frame to revealed content ("a
    console that supports Reveal emits a `RenderedFrame` that is `2N` pixels wider and says which
    columns are extended"). C widens it: the frame reports the *source state* of each side rather
    than a bare extended-or-not bit, so "extended" no longer implies "revealed from the map". The
    arithmetic and the accuracy guarantee are untouched — still exactly `2N` px wider, still
    bit-identical with the switch off.

  **The side source-state contract C needs.** `RenderedFrame::ExtendedSideFill` is one bit per
  side per row ("filled"), and `VideoRenderer::ApplyWidescreenFallback` skips a side the map
  already marks filled. A single "filled" bit cannot express what C needs: the band must be
  *drawn* (so the border must not repaint it) yet still be *overridable* by pack art (so the art
  step must not skip it). The map is therefore expanded to a per-row, per-side source state:

  - `none` — nothing is there; the border layer and then black may fill it (today's unfilled).
  - `synthesised` — the VDP put the band's repeated edge pixels there. Pack art may overwrite it;
    the border layer must not, and black must not.
  - `game-or-art` — real content: revealed map columns, or pack art already applied. Nothing
    overwrites it (today's filled).

  Fixing `game-or-art` as the only state that the art step skips keeps §3's "pack art comes first"
  order intact while letting the band lose to it. The resolution order for one side of one row is
  **pack art → edge band → border → black**, resolved on this state, in
  `WidescreenFallback::ApplyChain`: the art step writes over `none` and `synthesised`; the band
  survives wherever the art left it; `VideoRenderer::CompositeBorder` fills only what is still
  `none`; black is what "still `none`" renders as.

  **Tests that would pin it** (named, not written here): the `W253C:` and `W253b:` families in
  `scripts/core_unit_tests.cpp` — the state map, the band's rows, and the art-over-band order —
  plus `WidescreenSupportRuleTests.cs` in `UI.Tests/Play/` for the switch state a banded console
  now reports, and `UI.HeadlessTests/PlaySheetsViewTests.cs` for the switch and its reason on the
  sheet. C2's "synthesised" mark and C1's chain position are pinned by the first of these.
- **Option D — a per-game user override.** Leave the switch enabled everywhere, demote the reason
  to a hint, and let the player turn widescreen on for any game. Cost: ADR-0253 §4's per-ROM
  memory becomes advisory and the switch can no longer be trusted as "this game has a mode"; and
  it does not by itself say what "on" *does* for a console with no side map, so it still needs B
  or C underneath. Recorded for completeness, not recommended.

**Recommendation: C**, with B as the stopgap if the owner wants the pillarboxes gone for one
line of work, and A if the owner considers the Display surface's aspect-ratio setting the answer and
wants only the prose reconciled. C is the only option that gives the reporter what §1 says the
chain is for — the picture *wider*, not stretched — and it is the only one that makes ADR-0253 §2's
promise to SMS mean something.

**If C or B is accepted it is a new PRD slice** in Part B §8, after W.7, with W.5's switch state
revisited (§4's early-disable clause) and §6.1's WideScrn row updated. Slice plan for C:
**C1** the edge-band source and its position in `WidescreenFallback::ApplyChain`, with the
`W253C:`-family host-free tests; **C2** `SmsVdp` emitting the extra columns and the "synthesised"
mark; **C3** §3/§4's rule (`WidescreenFallback::SupportsWidescreen`, `WidescreenSupportRule`) and
the reworded reason string; **C4** the wiring tests, in `UI.HeadlessTests/PlaySheetsViewTests.cs`.

## Consequences

- **ADR-0253's internal contradiction gets settled either way.** With B or C, §4's "SMS/SG-1000
  are known unsupported before the game runs, so the switch is disabled at once" is replaced by a
  mode for those consoles; with A, §2's SMS sentence must be corrected in place so it stops
  promising a fallback §3 forbids.
- **MEP-v1 §5.5 needs a sentence if C is picked.** The section says hosts "MUST NOT synthesize
  widescreen art on their own". That rule is about a host inventing a pack section's authored
  content; an edge band derived per row from the picture's own pixels is not authored art, but the
  distinction has to be written down, or the next reader reads C as a spec violation. No spec
  version bump is otherwise needed — C adds no pack section and no manifest field.
- **The standard frame stays the gate.** ADR-0162's accuracy suite compares the switch-off output
  unchanged; C's extra columns are presentation only and must stay out of the HD conditions, the
  captures and the recorder's tiles exactly as W.1's do (ADR-0236).
- **The stretch is not removed by any option.** `VideoAspectRatio.Widescreen` remains a setting
  Player Settings' Display tab offers; only B and C change what the Enhancements switch *means* on
  a console with no side map.
- **Traps.** `SmsVdp` is an upstream-owned file (ADR-0163), so C's edits stay behind the
  frame-width contract and the standard path stays bit-identical. The reason line is a resource id
  (`EnhancementsWidescreenUnavailable`), so B's and C's rewording is a localization change, not a
  code change — and `WidescreenSupportRule.UnavailableReasonKey` is the one id both the sheet and
  §4's toast read, so they can never drift apart by accident.
