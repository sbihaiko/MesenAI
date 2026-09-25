# ADR-0234: An appearance the background hid is a mask — the recorder carries each OAM entry's priority bit, visible-pixel count and hidden-pixel count, and the pose clusters drop the hidden appearances

- Status: **accepted 2026-09-25 — option A, implemented in the same change.**
  Go-ahead to build, verbatim: *"siga com a opção A no #505"* (owner,
  2026-09-25). Ships with unit tests covering the decision
  (`scripts/core_unit_tests.cpp`: `OamFetchLatch`'s priority bit, the
  recorder-side per-dot verdict and its six cases, the two pixel tallies, the
  dump token, the predicate, the pose reduction and the three measured cases)
  and
  `scripts/test_mep_conditions.py` (the new token, and an
  old dump reporting the fields as absent). The pre-decision measurements are
  kept in `runs/505-analysis.md` (that path is gitignored — the numbers are
  restated here); the three-recording E2E that shaped the rule is under
  `runs/0234/`. The options B–D weighed against A stay in the analysis file;
  this ADR records the pick, not the weighing. **Amended four times the same
  day**: twice because a recording falsified the previous rule, once in review
  because the rule's *input* was measured off the wrong pixel, and once more in
  that review because computing the corrected input put per-dot work on the
  emulation's own path — see the dated notes in the Context.
- Date: 2026-09-25
- Related: ADR-0224 (behind-background sprites are labelled, not deleted),
  ADR-0173 (the recorder classifies and labels; the consumer filters),
  ADR-0177/ADR-0228 (same pattern for HUD and fusions), ADR-0170 (pose
  clustering: `kPoseMaxGap`, `kPoseMinFrames`, `kPoseMinTiles`), ADR-0179
  (tracks and cycles), ADR-0229 (every drawn shape enters the registry),
  ADR-0127 (host-free: `OamFetchLatch` and `SpriteGrouping` take data and
  return data), ADR-0153 §2 (the recorder reads OAM, not secondary OAM),
  ADR-0159 (the see-through signature on a background key), issue #505,
  issue #504 (the fused poses this ADR and that fix both touch),
  PRD Part A Phase 12
- Supersedes / amends: **ADR-0222** — its entry token
  `<shape>,<x>,<y>,<pal>` gains three fields (amended in place, dated note in
  its Decision section); the dump format is documented in that ADR, not in
  `docs/specs/`, so no spec semver moves. `scripts/stages/README.md`'s
  dump-format sentence follows.

## Context

`Super Mario Bros. 3` builds its piranha-plant pipe with a **mask sprite**: an
opaque 8×16 sprite drawn *behind* the background, in front of the plant, so the
opaque pipe hides both it and the plant's stem. The recorder reads OAM
(ADR-0153 §2), so it records the mask as an ordinary sprite, and the pose
clusters inherit it.

Measured on `runs/deep-smb3/run1` (2185 retained OAM frames, 3607 played):

| measurement | node 0 (`ShapeId` 147) | node 1 (148) |
|---|---|---|
| appearances | 1619 | 1619 |
| distinct `(x, y)` | 120 | 120 |
| `y = 129` share | 1608 / 1619 (99.3 %) | 1608 / 1619 |
| `screenFixed` (ADR-0173) | **false** | **false** |
| overlaps a sprite behind it in OAM order | 817 | 276 |
| overlaps a sprite in front of it | **1** | **0** |

Consequence: **25 of 37 poses contain node 0 or 1** (92 of 316 tile slots), and
the two nodes are the most-claimed in the file at 46 slots each. The kit's
figure grids show the mask's colour-3 orange as if it were the artist's art.

The issue proposed three criteria. Two cannot be applied to the retained stream
at all, which is what forced this decision:

- **Behind-BG priority is dropped at the door.** `OamFetchLatch::Decode` reads
  the OAM attribute byte and keeps only the two palette bits and the two flip
  bits; the dump's entry token is `<shape>,<x>,<y>,<pal>` (ADR-0222). The PPU
  knows the bit (`NesSpriteInfo::BackgroundPriority`), and `HdBuilderPpu::
  DrawPixel` already reads it per row, but only to set
  `transparencyRequired` on a *background* key (ADR-0159). It never reaches the
  sprite record.
- **"It only ever overlaps later-OAM sprites"** holds today (817 vs 1, 276 vs
  0) but is a positional rule over a whole recording, needs a whole extra pass
  over pairwise cell overlaps, and is falsified by a mask that ever wins one
  pixel. It measures the symptom, not the cause.
- **`screenFixed` (ADR-0173)** — measured **false**: 120 positions × 32 > 834
  frames. The mask is not a HUD and the existing label already declines it.

The cause is exact and cheap to observe at record time: the mask is behind the
background **and the background hid every one of its pixels**. The PPU's single
decision point is `NesPpu::GetPixelColor`: a sprite pixel is drawn when
`backgroundColor == 0 || !spriteTiles[i].BackgroundPriority`.

Non-goals: this is not a general "which sprites did the player see" model. A
sprite the 8-per-line limit or the sprite-mask bit hid is still recorded
(ADR-0153 §2) and reports no pixels here at all — it never contended for one —
so it is not a mask unless it is also behind the background. Nothing is deleted (ADR-0173): the
mask's shapes stay in the vocabulary and its cells stay in the sheets.

### Amended 2026-09-25, the same day: the rule is per appearance, on three facts

Two implementations were measured against real recordings before this one, and
both were wrong in ways only a recording shows. They are kept here because the
predicate is not self-evident and each clause earns its place:

**First: judged the node, not the appearance.** "Behind the background in every
one of its appearances and no pixel shown in any of them" never fires on SMB3,
over a before/after pair recorded from the same mint state with the same input
script and provably the same frames (the two OAM dumps agree field for field on
the four fields ADR-0222 defines; only the appended ones differ). Shape 147's
1619 appearances break down as:

| behind the background | visible pixels | appearances |
|---|---|---|
| yes | 0 | 1596 |
| yes | 2–4 | 12 |
| no (in front) | 60–64 | 11 |

The 11 front-facing ones are the level-intro card, which reuses the mask's 16
pattern bytes; the 12 behind but partly visible ones are the plant showing
2–4 pixels past the pipe's edge. So the node is *not* always hidden, the
node-level rule declines, and the mask stayed in all 25 poses — the fix did
nothing.

**Second: per appearance, but "no pixel shown" alone.** That fires on SMB3
(poses 37 → 28, the mask's hidden appearances gone) and then wrecks Punch-Out!!:
**78 of its 463 nodes** came out labelled masks and its poses went **34 → 101**.
(Both numbers are from that attempt's build, with the pre-review tally and the
frame scope; the label count coincides with the finished rule's round-1 count,
which is a coincidence, not the same measurement — see the review-round note.)
Punch-Out!! draws its fighters in two passes and the PPU's 8-per-line limit
leaves most of the second pass unfetched — behind the background, and never
given a pixel to lose. A half the PPU never drew is a sprite the game placed,
which ADR-0153 §2 records on purpose; it is not hidden *by the background*.

**Third: the frame, not the pose.** Dropping a hidden entry from the frame it
was seen in cuts the figure it belongs to into a variant per occlusion: 71 of
Punch-Out!!'s 101 poses were strict subsets of one of the original 34. A mask
hides a figure; it does not redefine it frame by frame. So the exclusion is
applied to the **pose**: a tile leaves a pose only when it was hidden in every
frame that pose was seen in, and the pose keys of the two forms then add up
into one (which is what keeps SMB3's 475-frame plant figure whole).

The three recordings together are the reason the rule below is what it is: the
terms are measured (an appearance's priority bit, the pixels it put on screen,
the pixels an opaque background took from it), and the scope is measured too
(the pose, not the node and not the frame).

### Amended 2026-09-25, in review: the tally counts the dot the PPU decided, not the shifter it left set

The three facts above were right and the first implementation of them was not.
It counted a pixel wherever `HdBuilderPpu::DrawPixel` reached its sprite block,
reading `hidden` off `backgroundColor` and the priority bit. But that block runs
with `_lastSprite` set, and `_lastSprite` is the highest-priority **active**
shifter — set before any `currColor != 0` test and replaced only by an opaque
pixel, so it is still set on a dot whose sprite pixel is transparent, on the
leftmost 8 columns the PPU clips sprites out of, on a row PPUMASK hid, and on
the pre-render line's leftovers (`GetPixelColor`, `BaseNesPpu.h`). Every such
dot was credited: a transparent dot over the backdrop counted **visible** (which
disqualifies a mask), one over an opaque background counted **hidden** (which
inflates the evidence the rule reads), and the row-0 leftovers could land on a
live half's identity.

That the SMB3 pair still measured 37 → 28 is not a defence: the mask's tile is
opaque, so its transparent dots sit over the pipe and only ever moved the hidden
count. The fix is the verdict itself: the PPU states a verdict where it decides
the pixel — both flags false for a transparent sprite pixel, since a pixel that
is not drawn cannot contend — and the recorder's tally takes that, not a boolean
derived from `_lastSprite`. `OamFetchLatch::OnSpritePixel` ignores a verdict
with neither flag, so a caller that reports every dot changes nothing.

### Amended 2026-09-25, in review: the verdict is computed on the recorder's side of the PPU, and costs the emulation nothing

The first form of that fix put the verdict *in* the shared path: a
`NesSpritePixelVerdict` member on `BaseNesPpu`, written by `GetPixelColor` on
every opaque sprite dot, with the classic `if(_emulatorSpritesEnabled && ...)`
return reading it. That is a per-dot store and a wider member for
`DefaultNesPpu` and `HdNesPpu` — two classes that never look at a mask, and the
hot path of every NES game the emulator runs.

The verdict is now whatever the PPU *reports*, not what it *stores*, and only
the recorder listens. `NesPpu<T>::GetPixelColor` calls
`((T*)this)->NoteSpritePixel(spriteColor, backgroundColor, backgroundPriority)`
on the one dot the original return would have drawn, and the return below it is
byte-for-byte the original. `BaseNesPpu::NoteSpritePixel` is an empty
`__forceinline` and `HdNesPpu`/`DefaultNesPpu`/`NsfPpu` do not override it, so
the call compiles away; `HdBuilderPpu::NoteSpritePixel` is the only override
and it classifies there, through `MesenSheets::SpritePixelVerdictOf`. The
classification is recorder-side for the same reason the tally is
(`TileSheetTypes.h`, next to `IsMaskEntry`), and it is not readable off
`_lastSprite` for the reason above. Cost, measured by diffing the object files
of the four instantiations: `__text` **+36 bytes** overall,
`DefaultNesPpu::GetPixelColor` 178 → 174 instructions and
`HdNesPpu::GetPixelColor` 178 → 174 (no added work — a register-scheduling
artifact in the return epilogue), `HdBuilderPpu::GetPixelColor` 178 → 195 (the
recorder pays for the verdict). And the three recordings are **byte-identical**
to the ones the previous form produced, all nine dumps and both sheet sidecars
per game — the reshape moves no result. The screenshot is unchanged too: a
`headless_record ... screenshot` of each of the three mint states renders a
byte-identical PNG under the change and under the pre-change build.

## Decision

**The recorder carries two more facts per sprite entry, and an appearance the
background hid is excluded from the sprite clusters, the poses and the
figures.**

1. **`OamEntry` gains `BehindBg`, `VisiblePixels` and `HiddenPixels`** — the
   priority bit and the two pixel counts the token and the predicate below are
   made of — none of them part of entry identity: `operator==`/`SameEntries` are
   unchanged, so frame de-duplication and every existing recording's frame count
   stay as they were. `OamFetchLatch::Decode` fills
   `Half::BackgroundPriority` from OAM attribute bit 5, and `Half` carries it
   through to the record.
2. **The pixel tally.** `NesPpu::GetPixelColor` reports the dot it is drawing —
   the winning shifter's colour, the background pixel under it and that
   shifter's priority bit — by calling `NoteSpritePixel` on itself, a CRTP hook
   that is an empty `__forceinline` in `BaseNesPpu` and in every PPU but the
   recorder's, so the emulation path pays nothing for it and its own return is
   untouched (see the second review-round note). `HdBuilderPpu::NoteSpritePixel`
   classifies there, through `MesenSheets::SpritePixelVerdictOf(spriteColor,
   emulatorSpritesEnabled, backgroundColor, backgroundPriority)` in
   `TileSheetTypes.h`, and `HdBuilderPpu::DrawPixel` hands the result to
   `OamFetchLatch::OnSpritePixel(x, tileBase, top, verdict)`. The verdict is
   *not* readable off the `_lastSprite` the recorder already had: that pointer
   is the highest-priority **active** shifter, so a transparent sprite pixel,
   the leftmost-8-columns clip, a row PPUMASK hid and the pre-render line's
   leftovers all leave it set, and counting those credits a sprite with pixels
   it never contended for. The tally is per frame, saturating, bounded by
   `SlotCount`, and keyed by `OamFetchLatch::SpriteId {X, TileBase, Top}` — the
   same identity `SpriteFetchLog` uses (PR #476 review), so no shifter→OAM-slot
   map is needed, and the caller only reports a dot an OAM entry can be placed
   on (`SpriteRowIsPlaced`, #479). Two halves sharing an identity share a count,
   which can only make a mask look *less* hidden: the classification stays
   conservative.
3. **The rule, its scope and its threshold.** One appearance of one tile is
   hidden iff

   ```
   that entry is behind the background
     &&  it contended for pixels and lost at least one to the background
     &&  it put none of them on screen
   ```

   i.e. `IsMaskEntry(behindBg, visiblePixels, hiddenPixels)` =
   `behindBg && hiddenPixels > 0 && visiblePixels <= kMaskMaxVisiblePixels`,
   the one predicate in `TileSheetTypes.h` every consumer calls. The scope is
   the **pose**: `SegmentFrame` records each tile's visibility, `BuildPoses`
   accumulates per cluster whether each of its tiles ever showed, and a tile
   leaves a pose when it never did — the same reduction the track linker
   applies, so a pose and its cycles agree. The threshold is
   **`kMaskMaxVisiblePixels = 0`**, and it is 0 because the measurement is not
   noisy: the only way an entry gets a pixel is to be let through by the same
   priority bit the rule tests, so a hidden appearance's count is exactly 0 and
   anything above it is a pixel the artist can see (the plant's 2–4 are exactly
   that). `hiddenPixels > 0` is what separates "the background hid it" from
   "the PPU never drew it" (the 8-per-line limit, Punch-Out!!'s second pass):
   a sprite the PPU never drew reports no pixels at all, and placing a sprite
   is not hiding one. Measured, the clause changes **no** label on the three
   recordings — SMB3 2, Castlevania 0 and Punch-Out!! 88, with it and without
   it, in both rounds — because every behind-background appearance that showed
   nothing also had an opaque background over it. It stays because it is what
   makes the label mean what the decision says it means, not because it is
   carrying a measurement.
4. **Where it is applied.** `SegmentFrame` segments one appearance per record
   (`X`, `Y`, `Node`, `Shown`), not three parallel vectors, so the drawing fact
   cannot drift out of step with the position it describes; an appearance
   nothing is known about — an artless placement, carrying no vocabulary node
   (`Node < 0`) and no drawing facts (issue #520, PR #526) — is `Shown`, because
   `IsMaskEntry` needs `hiddenPixels > 0` and it keeps counting toward the pose
   floor rather than being dropped before the floor weighs it. `PoseCluster::Visible`
   carries each tile's visibility out of `SegmentFrame` and `PoseCluster::ArtlessCells`
   the placement cells; `PoseVisibility` (cluster identity →
   per-tile flag) is the whole-recording record of it; `VisiblePoseTiles` is
   the reduction, used by `BuildPoses` and by `LinkPoseTracks`; it keeps a tile
   it has no flag for. The floor is re-applied to the reduced pose, and in the
   unit #520 re-based it on — the **union of the figure's cells**, drawn and
   artless alike (`PoseCellCount`), never the count of tiles left after a mask
   is removed: Bubble Bobble's 67 attract poses hold 41 that carry fewer than
   `kPoseMinTiles` drawn tiles and are figures only because their blank halves
   are cells too, and a tile-count floor here throws those away a second time.
   Nothing is
   dropped from the vocabulary or the sheets — the shape stays in both, and
   `adjacency.json` gains `behindBgAppearances`, `maskAppearances`,
   `visiblePixels`, `hiddenPixels` and `mask` per node (`SheetRender.cpp`
   writes them). `mask` means "the game draws this shape as a mask at least
   once" — `SelectMaskNodes` is the label over those counts, and it is
   *evidence*, not "this shape left a pose": Punch-Out!! labels 88 of its 463
   nodes (its two-pass fighters lose pixels to the ring in some appearances)
   and only 4 tiles actually leave a pose. For SMB3's node 0 the sidecar reads
   `behindBgAppearances 1608, maskAppearances 1596, visiblePixels 733,
   hiddenPixels 100 791` (re-measured in review; the first draft quoted 95 949,
   which no build since produces — the round-1 and review-round packs agree on
   this node to the digit, so it was a transcription error, not a measurement
   that moved).
5. **The dump format.** The entry token becomes
   `<shape>,<x>,<y>,<pal>,<visible>,<bg>,<hidden>` — additive, per ADR-0222's
   self-describing shape. A dump written before this ADR parses unchanged
   (`scripts/mep_conditions.py`: 4 fields is still legal) and
   `OamFrame.visible_pixels`/`behind_bg`/`hidden_pixels` return **`None`**,
   never an invented `0`, because a `0` would read as mask evidence.
   `OamStream.has_visibility`
   says which kind of dump the reader holds; `measure_capture_overdraw.py`
   indexes its entry fields rather than unpacking them. ADR-0169's live viewer
   wire is **not** changed and `make capture-tool` needs no rebuild: the viewer
   has never carried the priority bit or a pixel tally, and nothing in the
   viewer's use needs them.

## Consequences

- **Every re-recorded pack changes.** Measured on the three stage sets that
  exist, each recorded twice from one mint state with the same input script and
  the same frames (the two OAM dumps agree field for field on the four fields
  ADR-0222 defines): SMB3 **37 → 28 poses, 316 → 177 tile slots**, the mask
  claimed by 25 poses before and 2 after (27 frames of 3607, every one of them
  a frame where the mask really did show a few pixels or is the intro card's
  reuse of the art); Castlevania **258 → 258 poses, 3262 → 3262 slots**
  (unchanged); Punch-Out!! **34 → 34 poses, 1350 → 1348 slots** (two tiles, each
  hidden in every frame of its pose). `sheets/poses.json`, `adjacency.json` and
  the `-assets` figures of a re-record differ; a recording made before this ADR
  keeps its old text and is not re-classified — the masks stay in it.
- **The pixel the verdict is taken from moves the count, not the diagnosis.**
  Re-recorded in review with the corrected tally, from the same three mint
  states: SMB3 and Castlevania are identical to the row above, pose for pose and
  slot for slot, while Punch-Out!! keeps its 34 poses and goes **1348 → 1344**
  slots and **78 → 88** mask-labelled nodes, and the pixel counts themselves
  fall far more than the classifications move — SMB3 −162 705 visible pixels,
  Castlevania −1 630 472, Punch-Out!! −230 574 visible and −41 531 hidden. Those
  are sprite pixels the round-1 build credited that no PPU ever drew: a
  transparent dot over the backdrop read as *visible*, and over an opaque
  background as *hidden*. The four Punch-Out!! tiles that then leave a pose are
  each hidden in every retained frame of that pose
  (`runs/0234/verify_pose_drop.py`, from the dump alone); nothing *gains* a tile.
- **Old dumps are not upgraded, and cannot be.** The priority bit was never
  written, so no reader can recover it. Reading a pre-ADR-0234 dump means
  reading it as before.
- **#504 entanglement.** The fused poses (`LabelPoseFusions`, ADR-0228) and
  this exclusion touch the same 13 poses. The two are independent — fusion
  labels a fused pose, exclusion removes a mask's tiles — but a pose numbered
  by one will not be numbered the same way by the other. Land them in either
  order; re-record after both.
- **A shape that is only sometimes hidden keeps its visible appearances.** The
  exclusion is a property of the pose, so SMB3's 11 intro-card appearances of
  the mask's art stay tiles and their frames keep their figures, and the 12
  appearances where the mask peeks 2–4 px past the pipe keep theirs — which the
  node-level rule the E2E killed would have thrown away with them.
- **`behindBg`/`visiblePixels`/`hiddenPixels` are evidence, not identity.**
  Anything that compares `OamEntry` for sameness (frame de-duplication) must
  keep ignoring them, or two frames that differ only in a sprite's visibility
  stop collapsing and every frame count inflates. ADR-0169's wire is untouched,
  so the two formats differ by exactly these three fields — a reader must not
  assume the wire parses with `parse_oam_dump`.
- **The rule is the shape of the pixel tally, not a heuristic.** Both wrong
  versions above looked reasonable in the abstract; each was falsified by one
  recording in minutes. A third fact was needed for the second one, which is
  why the tally is split rather than summed.
- **An appearance with no drawing facts is never a mask** (issue #520/PR #526's
  artless placements, `kEmptyCell` cells with no vocabulary node): the
  predicate needs `hiddenPixels > 0`, so "nothing is known about this
  appearance" and "the background hid all of it" cannot be confused. That is
  load-bearing once composition feeds the pose pass: a placement dropped before
  the floor is weighed takes a figure with it — and so is the floor's unit. The
  two changes meet in one place: a mask tile leaving a pose must not take the
  pose with it, so the reduction re-applies #520's **cell** floor
  (`PoseCellCount`) and not a count of the tiles that survived. Measured on
  Bubble Bobble's attract recording (60 s from power-on, the route #520 was
  written for): **67 poses and 526 tracks before and after**, 227 → 223 tiles —
  41 of those 67 poses hold fewer than `kPoseMinTiles` drawn tiles, and a
  tile-count floor on the reduced pose would have dropped every one below it.
  On Contra's attract route (60 s from power-on) the two builds agree to the
  digit: 256 poses, 3063 tiles, 271 tracks.
