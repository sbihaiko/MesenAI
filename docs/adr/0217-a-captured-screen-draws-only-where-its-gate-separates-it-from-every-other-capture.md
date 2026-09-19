# ADR-0217: A captured screen draws only where its gate separates it from every other capture of the same recording

- Status: proposed — the measurements are done; the threshold and the loss it
  buys are a human call (see *What a human has to pick*)
- Date: 2026-09-19
- Related: ADR-0050 (bootstrap captures a static screen as a `<background>`
  gated on three `tileAtPosition` anchors), ADR-0156 (a captured screen owns
  the cells it covers), ADR-0159 (anchors are chosen at save time, from cells
  the screen's variants do not change), issue #339, issue #344, F12.2 cold
  read (`docs/validation/f12.2-opus-sweep-2026-09-19.md` §"What the runs
  found" item 1), `Core/NES/HdPacks/ScreenStitcher.cpp`
  (`SelectScreenAnchors`, `IsScreenVariant`, `GreedyAnchors`),
  `Core/NES/HdPacks/HdNesPack.cpp` (`GetLayerIndex`)
- Supersedes / amends: nothing yet. Every option in §Decision changes
  ADR-0159's rival rule, and options C–E also change ADR-0050's anchor clause
  ("three `tileAtPosition` anchors ... at least 64 px apart"). ADR-0156's
  precedence — a capture owns its cells — is **not** touched by any option;
  the question here is only *which frames a capture owns*.

## Context

### What was reported, and what it actually is

Issue #339 reports that on Mike Tyson's Punch-Out!! a captured
`<background>` wins on a frame it does not match, erasing the game's own
`STARRING` / `LITTLE MAC` text, and that the closest capture,
`screen003.png`, differs from the minted frame by 16 047 pixels.

The 16 047 figure reproduces exactly (RGB compare, `1024x960`, the deployed
`textures/backgrounds/screen003.png` against
`~/f12.2-opus-sandbox/frames/Mike_Tyson_s_Punch-Out____1987___Nintendo_.png`;
the other nine captures are 24 151 … 865 807). **The inference drawn from it
is wrong, and the defect behind it is worse than the inference.**

Every one of those 16 047 pixels sits in 66 cells inside rows 3–11, columns
11–20 — the portrait box, and nowhere else. That is Doc Louis and Little Mac,
who are *sprites*; ADR-0050 captures the background only. On that frame
`screen003.png` is a **pixel-exact picture of the background**, and drawing
it deletes nothing.

The text is deleted on the *other four* frames. Punch-Out!!'s pre-fight
credits are a typewriter: the recorder captured five states of one screen and
wrote five PNGs.

| capture | what it adds | cells differing from `screen003` (of 960) |
|---|---|---|
| `screen003` | portrait only, no text | — |
| `screen004` | `STARRING` / `LITTLE MAC` | 17 |
| `screen005` | `AND` / `HIS TRAINER` / `DOC LOUIS` | 38 |
| `screen006` | `ALSO` / `PLAYING THE ROLE OF MAC,` | 62 |
| `screen007` | `IT'S YOU!!` | 71 |

All five carry the **same three conditions**, verbatim, in the pack's own
`hires.txt`:

```
<condition>screen003_A,tileAtPosition,88,24,3D,0F302A07
<condition>screen003_B,tileAtPosition,160,24,3F,0F302A07
<condition>screen003_C,tileAtPosition,120,56,53,0F2A3036
... screen004_A/B/C, screen005_A/B/C, screen006_A/B/C, screen007_A/B/C — identical
```

The three probes are at cells (col 11, row 3), (col 20, row 3) and
(col 15, row 7) — all three inside the portrait box, all three pixel-identical
across `screen003`–`screen007`. The text lives in rows 13–25 and is never
probed. `HdNesPack::GetLayerIndex` returns the **first** matching
`<background>` at a priority, and all ten are priority 20, so `screen003`
wins on all five frames and the other four PNGs never draw at all.

### Why the recorder does this on purpose

`ScreenStitcher.cpp` splits the retained frames into *variants* of the
captured screen and *rivals*. `IsScreenVariant` calls two frames variants
when they agree on `kAnchorVariantAgree` = 0.90 of the 960 cells — a budget of
96 cells. Punch-Out!!'s five credit screens differ by **at most 71 cells**, so
each is a variant of the other four, and none of them is ever a rival.

`SelectScreenAnchors` then prefers cells **no variant changes** (ADR-0159 §1:
"a wrong screen drawn whole is worse than a screen that misses a variant"),
and `GreedyAnchors` picks, inside that pool, the cells that leave the fewest
*rivals* alive. With the four look-alikes filed as variants, the stable pool
is exactly the portrait box, the rival set is empty, and the choice comes back
with `Rivals == 0` — the branch `SelectScreenAnchors` treats as ideal and
returns without widening.

**The gate is not loose by accident. The rule actively selects the cells that
cannot tell the five screens apart**, because it was built on the premise that
a variant is the same screen with something changed on it and should keep
drawing. For a typewriter screen, a score row or a level counter, that premise
is false: the thing that changed *is* the art.

### How big it is, measured on the F12.2 sweep

The 28 sandbox packs (`~/f12.2-opus-sandbox/games/*/textures/`) hold **237
captures**. Evaluating each capture's own three probes against every other
capture of the same game (8x8 cell pixel-identity at the probe coordinate, the
proxy `scripts/spike_anchor_stability.py` already uses):

- **135 of 237 captures (57.0%) are never drawn.** Some earlier-loaded capture
  already satisfies its own gate on that frame, and `GetLayerIndex` stops at
  the first match. Those PNGs are dead weight on disk, and on each of their
  frames a *different* picture is painted.
- 2 996 of 4 852 ordered capture pairs (61.8%) gate-match.
- Ten packs ship a gate that is **byte-identical across two or more captures**:
  Donkey Kong 46 captures on one gate, Ice Climber 22, Pac-Man 9 + 13,
  Bomberman 4 + 6, Punch-Out!! 5, Tennis 4, Mario Bros. 2, Flintstones 2 —
  113 of 237 captures (47.7%).
- Art at stake on a gate-matching pair: median 21 differing cells of 960,
  p90 26, max 768.

`scripts/spike_anchor_stability.py ~/f12.2-opus-sandbox/games` reports this
library as healthy — 73/700 unstable anchors, 279/3 220 variant pairs missed
before the rule and 1/3 220 after, 55/1 632 non-variant false matches. It
counts false matches **only over non-variant pairs**, so all 3 220 variant
pairs are scored as intended behaviour. The spike and the rule share the
premise, which is why five years of green measurements never saw this.

### Whether it is fixable at all

Greedy search for the smallest set of cells that separates a capture from
every other capture of the same game, over the **whole 960-cell frame**:

| probes needed | captures (of 232 in multi-capture packs) |
|---|---|
| 1 | 117 |
| 2 | 72 |
| 3 | 33 |
| 4 | 1 |
| impossible — no cell differs at all | 9 |

Punch-Out!!'s five need **2**. So the probe *count* is not the problem: three
is already enough for 222 of 232, and four for 223. The nine impossible ones
(Mega Man 2, Metroid, Super Mario Bros. 3) are byte-identical background
duplicates the recorder should not have written twice.

Re-running the same search with ADR-0050's **`kAnchorMinSpread` = 64 px
Manhattan** rule kept:

| probes needed | captures |
|---|---|
| 1 | 117 |
| 2 | 55 |
| 3 | 7 |
| impossible | 53 |

The spread rule alone takes 44 separable captures and makes them
inseparable, because what distinguishes two captures is usually a *block* —
a credits paragraph, a score row — and the rule forbids a second probe inside
it. **The 64 px spread is a second, independent blocker**, and it was never
measured: ADR-0050 states it without a number.

### Non-goals

- Changing what a capture *covers* once it wins. ADR-0156 stands.
- Changing capture precedence against `<tile>` rules, or the priority-20
  ordering.
- The artist's inability to retire a capture (#344). That is the build tool's
  contract, is being handled separately, and is fixed by none of these
  options.
- Retro-fitting packs already on disk. Every option here is a recorder change
  and takes effect on the next recording.

## Decision

Not taken. Five options were measured; each is stated with what it costs in
memory, in recording time, and in frames that lose their capture.

### A — Refuse to write a capture whose gate another capture already satisfies

At save time, after picking anchors, check the new screen's gate against every
capture already written. If an earlier one matches this frame, the new PNG is
not written and no `<background>` line is emitted.

- **Frames that lose their capture:** 135 of 237 (57.0%) — exactly the ones
  that are never drawn today, so **no rendered frame changes**.
- **Memory:** none. **Recording time:** O(screens² × 3) probe compares, at
  save time; ≤ 300² × 3 = 270 000 compares per session, unmeasurable.
- **What it does not do:** it does not give Punch-Out!! its text back. It
  makes the pack honest about what will draw — 10 PNGs become 6 — and it
  turns a silent wrong render into a missing one.

### B — More probes (`kAnchorCount` 3 → 4 or 5)

- **Frames that lose their capture:** none.
- **Cost:** one or two `<condition>` lines per screen; the anchor search is
  O(`kAnchorCandidateCap` × picks × frames), so a fourth pick is +33% of a
  per-screen cost already paid once at save time.
- **It does not work.** The five Punch-Out!! screens are all *variants*, so
  they are never in the rival set the greedy optimises against; a fourth probe
  drawn from the same stable pool lands in the same portrait box. Measured
  above: three probes already suffice for 222/232 captures **when the pool and
  the rival set are right**. B is a no-op unless paired with C.

### C — Every other capture is a rival, never a variant

A frame the recorder thought worth its own PNG is by definition a different
picture. Drop it from `variants` and put it in `rivals` in
`SelectScreenAnchors`, keeping the stability filter for *uncaptured* frames.

- **Frames that lose their capture:** the anchors move onto cells that do
  change, so a capture stops drawing on variants the recording never held
  still on — the ones the spike cannot see. ADR-0159's own library measurement
  bounds it: the pre-0159 discrimination-first pick put 44.7% of anchors on a
  volatile cell and missed 73.3% of variant pairs; the current stability-first
  pick misses 13.4%. C sits between them and **the number is not obtainable
  from disk** — it needs a re-record of the 30-ROM library with the changed
  rule and `--recolour`, roughly the cost of one bootstrap sweep.
- **Memory:** none. **Recording time:** the rival list grows by ≤ 300 frames
  against `kMaxSheetFrames` = 4096; negligible.
- **What it buys:** Punch-Out!! draws all five. 222 of 232 captures get a
  separating gate at the current `kAnchorCount` = 3.
- **Trap:** on its own it still hits the spread rule — 53 captures stay
  inseparable at any probe count until `kAnchorMinSpread` is relaxed.

### D — Relax `kAnchorMinSpread`, alone or with C

Drop or lower the 64 px Manhattan spread when the separating cells cluster.

- **Frames that lose their capture:** none directly.
- **Cost:** the rule was buying robustness against a locally-corrupted frame —
  three probes 8 px apart are three samples of one tile fetch. There is no
  measurement of what it was worth, and producing one means a re-record.
- Without D, C tops out at 179 of 232 captures separable.

### E — Fingerprint the whole frame, and draw only above a similarity threshold

Store the capture's 960 `(tile index, palette)` keys beside the PNG and, at
`GetLayerIndex` time, compare them against the live frame; draw when at least
N of 960 cells agree.

- **Memory:** the recorder's `GridFrame` is already 2 880 B (1 920 B `Cells` +
  960 B `Palettes`, ADR-0159 §Cost). Per-session cap 300 screens → **864 kB**
  resident, against 3.93 MB *per PNG* at scale 4. In the pack it needs a new
  serialised construct — `hires.txt` has no field for it — so it is also a
  format change and an `HdPackLoader` version bump.
- **Runtime:** `GetLayerIndex` runs once per frame. 960 cell compares per
  candidate × 47 candidates (Donkey Kong, the worst in the sweep) = 45 120
  compares/frame. The data is already in hand: `HdScreenInfo::ScreenTiles`
  carries `Tile.TileIndex` and `Tile.PaletteColors` per pixel, which is what
  `tileAtPosition` reads today.
- **Frames that lose their capture — this is the whole question.** Agreement
  between a capture and its *nearest* other capture in the same pack is
  median **956 of 960 cells (99.58%)**, p75 959, p90 959. A threshold that
  separates Punch-Out!!'s `screen003` from `screen004` must therefore demand
  more than 99.6% agreement — effectively exact. And at exact, every frame
  that is a genuine variant of a capture (a HUD digit ticking, a blinking
  prompt) loses it: 3 220 variant pairs in the sweep, all of them.

## What a human has to pick

1. **Does a capture own its variants, or only its own frame?** ADR-0159 says
   the first; #339 is the price. Both answers are defensible and the numbers
   above do not settle it.
2. **If it owns its variants — which one wins when several gates match?**
   Today it is load order (`GetLayerIndex` returns the first match), which is
   arbitrary and silent. 135 of 237 captures are decided by it.
3. **The threshold, if E.** Anything from "exact" to 99.6% is one behaviour;
   below 99.6% it is the current bug with extra steps.
4. **Is the 64 px spread worth 44 captures?** It has no measurement behind it
   and it blocks C on 22.8% of the library.
5. **Does any of this ship before the next recording sweep?** C and D cannot
   be costed without re-recording the 30-ROM library; A and B can ship on what
   is already measured.

A is the only option that is strictly an improvement on today's render, costs
nothing, and needs no new measurement. It is also not a fix — it makes the
loss visible instead of silent. That is the trade the register needs a human
on, so this ADR stays `proposed`.

## Consequences

- Whatever is picked, `scripts/spike_anchor_stability.py` needs a companion
  rate: **captures whose gate another capture satisfies**. Its current "cross"
  figure is scored over non-variant pairs only and reports 55/1 632 for a
  library where 135 of 237 captures never draw.
- Options C, D and E all invalidate the anchor cases in
  `scripts/core_unit_tests.cpp` (`BlocoP2`) and need new ones; the tests are
  host-free, so that is cheap.
- E is a pack-format change: a `<ver>` bump, an `HdPackLoader` reader, and a
  `mep_lint` rule. A and B–D are recorder-only and leave the format alone.
- Nothing here helps a pack already on disk. Punch-Out!!'s five credit screens
  stay one screen until the game is re-recorded.
