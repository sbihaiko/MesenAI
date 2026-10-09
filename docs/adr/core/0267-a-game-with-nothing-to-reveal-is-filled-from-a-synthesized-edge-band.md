# ADR-0267: A game with nothing to reveal is filled from a synthesized edge band, not left with a disabled switch

- Status: accepted 2026-10-08 — the owner picked **B now, then C**, verbatim: *"B agora, depois
  C (Recommended)"* (2026-10-08, via direct question). The intake is staged: **stage 1 is option
  B**, implemented by a separate PR (the Widescreen switch stays enabled for a console with no
  side map and applies `VideoAspectRatio.Widescreen`, reason string reworded), and **stage 2 is
  option C**, in the slices below. **B amends ADR-0253 §1/§3/§4 only and adds no source to that
  ADR's fallback chain; the §2 amendment is owed only when C lands.** The current behavior is
  still *not* a bug: see Context, and nothing here is implemented by this branch. Options A and D
  were weighed and not taken; option A's in-place §2 correction remains the answer only if C is
  dropped, and C's own amendment to §2 (its SMS per-console scope line, and the Reveal-source
  contract that ties extended columns to revealed map content) is stated under Decision, Option C.
- Date: 2026-10-08
- Related: issue #1082; ADR-0253 (the Reveal and its fallback chain — §1 the one switch, §2 the
  per-console scope, §3 the content-aware fallback and its "never on their own" rule, §4 the
  per-game measurement); ADR-0149 (the border layer); ADR-0162 (the accuracy suite: standard
  frames stay bit-identical); ADR-0163 (fork–upstream coexistence: `SmsVdp` is upstream-owned);
  ADR-0236 (recorded captures are keyed to 256-wide cell positions); PRD Part B §6.1 (the WideScrn
  row) and §8 (the W.1–W.7 slices); `docs/specs/MEP-v1.md` §5.5 (the `widescreen` section).
- Supersedes / amends: **the accepted option B amends ADR-0253 §1** ("the stretch to 16:9 is
  dropped"), **§3** ("The border and black are per-frame fill-ins for a game that does support a
  mode. On their own they never make a game *"supported"*, so they never keep the switch enabled.")
  and **§4** ("SMS/SG-1000 without pack art are known unsupported before the game runs, so the
  switch is disabled at once").
  B leaves **§2 untouched** and adds **no source** to §3's fallback chain — it re-enables the switch
  and applies the existing `VideoAspectRatio.Widescreen` fill, it does not reveal anything. **The §2
  amendment is owed only when option C lands**, and C states it in two places: the per-console scope
  entry for SMS/SG-1000, and the Reveal-source contract that today ties "extended columns" to map
  content the console reveals. Until C ships, §2's SMS sentence stays exactly as accepted. Option A
  would have corrected §2 in place instead (a refinement, not a reversal, so no superseded line is
  owed either way); option D left §2 alone. No option amends §2's arithmetic — a frame is still `2N`
  pixels wider, and the standard mode is still bit-identical (ADR-0162).

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
  `UI.HeadlessTests/PlaySheetsViewTests.cs` pin the current behavior and pass.

So this is not a bug to fix. The gap the report exposes is in the product:

1. **ADR-0253 contradicts itself about exactly this console.** §2 lists "SMS/SG-1000: Reveal is
   offered but has no map columns to show, so it always uses the fallback (decision 3)", while §3
   says "The border and black are per-frame fill-ins for a game that does support a mode. On their
   own they never make a game *"supported"*, so they never keep the switch enabled.", and §4 says
   SMS/SG-1000 "are known unsupported before the game runs, so the switch is disabled at once". §4 (what the code does) is the only implementable reading: a
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

*Accepted 2026-10-08 — the owner's answer, verbatim: *"B agora, depois C (Recommended)"*. The
decision is staged: **stage 1 is option B**, which ships first and is implemented by a separate PR;
**stage 2 is option C**, which follows in slices. The four options are kept below as they were
weighed, ordered by cost, cheapest first.*

**Stage 1 — option B, now, in a separate PR.** For a console with no side map the Widescreen switch
stays **enabled**, and turning it on applies `VideoAspectRatio.Widescreen` — the pre-ADR-0253 fill
already implemented in `AspectRatioMath`. The one-line reason is reworded off "nothing to show
beside the picture" onto a fill wording (for example "Nothing to reveal beside the picture;
widescreen will only stretch it"), so the switch never claims a Reveal it does not have. That is the
smallest change that answers the report as written: it changes `WidescreenSupportRule` — §4's
"disabled at once" becomes "enabled, fills" — and its 19 pinned cases, plus one new resource string.
It amends ADR-0253 §1/§3/§4 and **leaves §2 alone**, because a fill is not a fallback source and §2
is about the Reveal's own scope. This ADR does not implement it.

**Stage 2 — option C, in slices, after B.** The edge band follows once B has shipped, in one order
and no other — **C1 → C5 → C2 + C2b → C3 → C4**, with **C5 no later than C2**: **C1** the synthesized
edge-band source and its position in `WidescreenFallback::ApplyChain`, with the `W253C:`-family
host-free tests; **C5** the MEP §5.5 wording that admits a host-synthesized edge band; **C2**
`SmsVdp` emitting the extra columns and the "synthesized" mark; **C2b** the SMS HD-pack path sized
from the frame it is handed — the RGB555 pixel buffer and the `HdTilePixelInfo`
provenance grid taken from the frame's own width instead of the hard-coded 256, with the band
columns' provenance synthesized — which lands *with* C2, since the widened frame reaches the
composer the moment C2 ships; **C3** §3/§4's rule (`WidescreenFallback::SupportsWidescreen`,
`WidescreenSupportRule`) and the reworded reason string; **C4** the band's frame-capture wiring
test. The slice plan below carries each one's full text. **The §2 amendment is owed with C, not
before it.**

- **Option A — status quo; reconcile the prose.** Keep the switch disabled for SMS/SG-1000 and
  point the player at Player Settings → Display → Aspect ratio → Widescreen for the plain stretch.
  Zero code. What it
  costs: the Enhancements sheet that owns the widescreen *feature* says the game cannot do it,
  while Player Settings' Display tab offers the aspect ratio that would — and ADR-0253 §2 keeps
  promising SMS a fallback that §3 and §4 forbid. So this option is only honest together with an
  in-place correction of §2's SMS sentence, plus a decision on whether the reason string should
  point at the Display setting.
- **Option B — the switch applies the fill. *Accepted as stage 1 (2026-10-08); implemented by a
  separate PR.*** Keep the Widescreen switch *enabled* for a console
  with no side map, and let turning it on apply `VideoAspectRatio.Widescreen` — the pre-ADR-0253
  behavior, already
  implemented in `AspectRatioMath`. The one-line reason is reworded from "nothing to show beside
  the picture" to a fill wording (for example "Nothing to reveal beside the picture; widescreen
  will only stretch it"), so the switch never claims a Reveal it does not have. Cost: re-admits
  the 16:9 distortion ADR-0253 §1 exists to remove, this time as a deliberate opt-in rather than
  the default; changes `WidescreenSupportRule` (§4's "disabled at once" becomes "enabled, fills")
  and its 19 pinned cases; one new resource string. This is the smallest change that answers the
  report as written.
- **Option C — a synthesized edge band, revealed like real columns. *Accepted as stage 2
  (2026-10-08), in slices, after B ships.*** The SMS VDP
  emits its line `2N` columns wider — §2's frame-width arithmetic, unchanged — where the extra
  pixels of a scanline repeat the nearest real column of that same scanline. Those rows are marked
  in the per-row side-fill map (`RenderedFrame::ExtendedSideFill`) as *synthesized*, and
  Decision 3's chain gains one ordered source: pack art → **synthesized edge band** → border →
  black. §3's "never on their own" clause is amended to admit the edge band as a mode, while a
  static border and plain black stay fill-ins: the band is made of the picture's own pixels, per
  row, so it tracks the game; a border image and black do not. Cost: real work in `SmsVdp`
  (upstream-owned, ADR-0163) and a new source in `WidescreenFallback`; the per-console N must be
  chosen so 16:9 lands at the console's own pixel aspect, and the SMS's picture is not a fixed
  height (256×192 at 8:7 is ≈1.52 and needs ≈21 columns per side; the 224-line mode the VDP can
  also select needs ≈46 — against the NES's 64 per side and the GB's 48), so N follows the frame's own
  height rather than being one constant; the HD path, the recorder and
  the capture tools see a wider frame — and the SMS HD-pack path, whose pixel and provenance buffers
  are hard-coded to 256 wide today, has to be sized from the frame it is handed instead (C2b), or the
  composer indexes past them. Two edits must land together, or the switch and the core
  disagree: the new source feeds `WidescreenFallback::SupportsWidescreen` (which
  `NesWidescreenSupport::Reveals` reads), and `WidescreenSupportRule.ConsoleHasSideMap` — the
  app-side predicate that disables the switch before the game even runs — has to admit a console
  whose sides the band fills. Pack art still wins per row, so a pack that ships §5.5 art is
  unaffected.

  **C amends ADR-0253 §2, and this ADR states it rather than claiming otherwise.** Two clauses
  move:

  - **The per-console scope entry.** §2 currently reads "SMS/SG-1000: Reveal is offered but has no
    map columns to show, so it always uses the fallback (decision 3)." Under C that entry becomes:
    SMS/SG-1000 has no map columns to reveal, so its extended columns are *synthesized* by the VDP
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
  - `synthesized` — the VDP put the band's repeated edge pixels there. Pack art may overwrite it;
    the border layer must not, and black must not.
  - `game-or-art` — real content: revealed map columns, or pack art already applied. Nothing
    overwrites it (today's filled).

  Fixing `game-or-art` as the only state that the art step skips keeps §3's "pack art comes first"
  order intact while letting the band lose to it. The resolution order for one side of one row is
  **pack art → edge band → border → black**, resolved on this state, in
  `WidescreenFallback::ApplyChain`: the art step writes over `none` and `synthesized`; the band
  survives wherever the art left it; `VideoRenderer::CompositeBorder` fills only what is still
  `none`; black is what "still `none`" renders as.

  **Tests that would pin it** (named, not written here): the `W253C:` and `W253b:` families in
  `scripts/core_unit_tests.cpp` — the state map, the band's rows, and the art-over-band order —
  plus `WidescreenSupportRuleTests.cs` in `UI.Tests/Play/` for the switch state a banded console
  now reports, and `UI.HeadlessTests/PlaySheetsViewTests.cs` for the switch and its reason on the
  sheet. C2's "synthesized" mark and C1's chain position are pinned by the first of these. C2b needs a
  host-free case of its own, and it has to be over a *pure* unit rather than the VDP: `SmsVdp.cpp` is
  not in the core unit-test link set, which is why the `W253b:` family pins the Game Gear Reveal as
  `SmsWidescreenReveal`'s arithmetic instead. So the same shape applies — the HD grid's width taken
  from the frame's own width (never a constant), and the band column's synthesized provenance — plus a
  bounds assertion that the stride the composer walks is the grid's own width, which is exactly the
  read that overruns today. The composer over a real extended SMS frame is C4's case, in the new
  `UI.HeadlessTests/SmsWidescreenBandTests.cs` named in the slice plan below.
- **Option D — a per-game user override.** Leave the switch enabled everywhere, demote the reason
  to a hint, and let the player turn widescreen on for any game. Cost: ADR-0253 §4's per-ROM
  memory becomes advisory and the switch can no longer be trusted as "this game has a mode"; and
  it does not by itself say what "on" *does* for a console with no side map, so it still needs B
  or C underneath. Recorded for completeness, not recommended.

**The recommendation was C**, with B as the stopgap if the owner wanted the pillarboxes gone for one
line of work, and A if the owner considered the Display surface's aspect-ratio setting the answer and
wanted only the prose reconciled. The owner took both, B first: **B removes the pillarboxes now and C
is the destination**, since C is the only option that gives the reporter what §1 says the chain is
for — the picture *wider*, not stretched — and the only one that makes ADR-0253 §2's promise to SMS
mean something. B is therefore a deliberate, temporary re-admission of the 16:9 stretch §1 exists to
remove, and C is what retires it again.

**B and C are new PRD slices** in Part B §8, after W.7, with W.5's switch state revisited (§4's
early-disable clause) and §6.1's WideScrn row updated. **B is implemented by a separate PR**; the C
slices below follow it once B has landed. Slice plan for C, in the one order stated above —
**C1 → C5 → C2 + C2b → C3 → C4**:
**C1** the edge-band source and its position in `WidescreenFallback::ApplyChain`, with the
`W253C:`-family host-free tests; **C5** the MEP §5.5 wording that admits a host-synthesized edge
band — it must land with or before C2 or the host ships against its own published spec; **C2**
`SmsVdp` emitting the extra columns and the "synthesized"
mark; **C2b** the SMS HD-pack path sized to the frame it is handed — the RGB555 pixel buffer and the
`HdTilePixelInfo` provenance grid allocated from the frame's own width instead of the hard-coded 256,
and the band columns' provenance synthesized there (no BG tile, no sprite tile, and the repeated edge
pixel's own color, so the composer takes its plain-color path at the pack's scale rather than being
asked for a tile the VDP never drew), with the host-free coverage named above. C2b cannot land after
C2: `SmsHdTileVideoFilter::AcceptsExtendedFrame()` already answers true for the SMS, so the widened
frame reaches the composer the moment C2 ships, and the buffers have to be able to hold it — see
Consequences; **C3** §3/§4's rule (`WidescreenFallback::SupportsWidescreen`, `WidescreenSupportRule`) and
the reworded reason string; **C4** the band's frame-capture wiring test, a new
`UI.HeadlessTests/SmsWidescreenBandTests.cs` modeled on `GbaWidescreenRevealTests.cs` — a synthetic
SMS ROM on the real core, the frame read back through `FrameCaptureApi` — asserting that the
widened frame's width is `256 + 2N`, that each row's side columns are the same color class as that
row's own edge pixel, and that switching the switch off returns a 256-wide frame. **Version (owner decision 2026-10-09, #1090,
option A, verbatim: "A"):** packs keep declaring `mep: 1.x`; there is no major bump. The §5.5
wording lands as a minor revision of MEP-v1 that *clarifies the scope* of the ban (a host MUST NOT
synthesize widescreen *art*; a per-row edge band derived from the picture's own pixels is not
authored art), so an older host that follows the old wording keeps loading the same packs and no
pack has to declare a version a host would refuse.

## Consequences

- **ADR-0253's internal contradiction is settled in two steps, not one.** Stage 1 (B) settles §4's
  "SMS/SG-1000 are known unsupported before the game runs, so the switch is disabled at once" —
  those consoles get a switch mode (the fill) as soon as B ships. It does **not** settle §2: §2's
  dead SMS sentence, the one promising a fallback §3 forbids, stays as accepted until stage 2 (C)
  replaces it. If C is ever dropped, correcting that sentence in place — option A's edit — becomes
  the piece still owed, and the ADR register would then hold a §2 clause no option of record amends.
- **MEP-v1 §5.5 needs a revision if C is picked, and it lands as a minor, not a major (#1090, decided 2026-10-09).** The
  section says hosts "MUST NOT synthesize widescreen art on their own". That rule is about a host
  inventing a pack section's authored content; an edge band derived per row from the picture's own
  pixels is not authored art, but the permission has to be written down, or the next reader reads
  C as a spec violation. Writing it down relaxes a normative `MUST NOT`, so the revision is a
  clarification of scope rather than a new optional field, and **it ships as a minor revision of
  MEP-v1** (owner decision 2026-10-09, #1090, option A: packs keep declaring `mep: 1.x`, no major
  bump, so older hosts — which MUST refuse an unknown major — do not refuse any pack). C adds
  no pack section and no manifest field, and this ADR does not amend the spec. C therefore carries
  two deliverables: the code slices below, and the §5.5 wording that admits a host-synthesized edge
  band while keeping the ban on synthesized *art*. Landing C without that revision leaves a host
  that follows C in violation of the published spec.
- **The standard frame stays the gate, but the widened sides still go through the HD pipeline.**
  ADR-0162's accuracy suite compares the switch-off output unchanged, and the standard path stays
  bit-identical. The extra columns are *not* exempted from pack processing: ADR-0253 **W.4**
  already settled that for the NES — with Reveal on and an HD pack loaded, `HdNesPpu` captures each
  row's basis and stores the side tiles in `HdScreenInfo::SideTiles`, and `HdNesPack::Process`
  draws them through the pack's per-pixel pipeline (`<tile>` rules, fallback tiles, **HD
  conditions**, grayscale/emphasis) at the pack's scale, with `<widescreen>` art filling the seam.
  What stays at the standard width
  is only the **recorder/capture cell grid**: `ScreenTiles` remain 256×240 and recorded captures
  stay keyed to the standard-width cell positions (ADR-0236), exactly as W.4 held — no existing
  pack rule and no ADR-0236 cell mask moves.

  **The SMS HD path is the one place C has to widen a buffer, and it is not optional.** The SMS band
  is made of the picture's own pixels, so — unlike the NES — there is no side *tile* to draw and no
  `<tile>` rule for the band to key on; but the pixels still go through `SmsHdTileVideoFilter`, and
  that composer's buffers are sized for the frame the VDP hands it. Today the invariant holds by
  construction: `SmsVdp::SetHdPack` allocates the `HdTilePixelInfo` grid as `256 * 240`,
  `ProcessHdPackPixel` writes it at `Scanline * 256 + GetVisiblePixelIndex()`, the emitted
  `RenderedFrame` is built `256 × 240` with `frame.Data` pointing at that same grid, and
  `SmsHdTileVideoFilter::AcceptsExtendedFrame()` answers true *because* the grid is as wide as the
  frame — the Game Gear precedent it cites keeps `Width = 256` precisely since its Reveal drops an
  overscan crop instead of adding columns. C breaks that equality: its frame is `256 + 2N` wide, and
  `HdTileVideoFilter::ApplyFilter` takes its stride from the frame (`inWidth = _baseFrameInfo.Width`)
  and indexes the provenance grid and the pixel buffer alike as `srcY * inWidth + srcX`. A C-sized
  frame over a 256-wide grid therefore reads past both allocations — the loop still runs `srcY` up to
  the frame's height and `srcX` up to the extended width — and the GB subclass's refusal of extended
  frames (which exists for exactly this stride mismatch) does not cover the SMS, whose subclass
  accepts them. So C2b is not cleanup: it sizes both buffers from the frame's own width and writes
  the band's synthesized provenance, and it lands with C2, because the widened frame and the widened
  grid have to arrive together. That is also why the band renders rather than vanishing under a pack:
  its provenance carries no tile and the edge pixel's own color, so the composer draws the color the
  VDP produced, scaled, instead of a null replacement.
- **The stretch is not removed by any option.** `VideoAspectRatio.Widescreen` remains a setting
  Player Settings' Display tab offers; only B and C change what the Enhancements switch *means* on
  a console with no side map.
- **Traps.** `SmsVdp` is an upstream-owned file (ADR-0163), so C's edits — including C2b's buffer
  sizing, which lives in the same file — stay behind the frame-width contract and the standard path
  stays bit-identical. Sizing those buffers from the frame means an SMS frame with *no* extra columns
  still gets exactly today's 256-wide grid, so nothing changes for a standard frame or for a Game
  Gear Reveal. The reason line is a resource id
  (`EnhancementsWidescreenUnavailable`), so B's and C's rewording is a localization change, not a
  code change — and `WidescreenSupportRule.UnavailableReasonKey` is the one id both the sheet and
  §4's toast read, so they can never drift apart by accident.
