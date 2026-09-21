# ADR-0217 / ADR-0218 — a capture's gate against every other capture (2026-09-20)

Both ADRs were accepted on 2026-09-20 and implemented the same day in
`186077d0`, with synthetic-`GridFrame` unit coverage and, by their own Status
lines, "no re-record required". This log supplies the re-record anyway, because
a unit test on a synthetic grid cannot say whether the pathology that issues
#339/#344/#349 describe actually stops happening on a real ROM.

It does. Across four games, **95 co-gated captures became 0**.

## The instrument, and a correction to the F12.9 log

The first attempt at this measurement produced numbers byte-identical to the
pre-change sweep and nearly became a "the change does nothing" finding. The
cause was the binary: `InteropDLL/obj.osx-arm64/MesenCore.dylib` on disk
(sha256 `49c3aa06d1b913b25bf0b8a1f49dff34e1b8ce812189a619b2d7cdbe547a5ad1`) was
built **before** `186077d0` and did not contain the change at all — its
anchor-summary log line still had three counters where the source has five.
The same stale dylib is the one
`docs/validation/f12.9-static-kit-from-the-rom-2026-09-20.md` describes as "the
one already on disk from `main` `0a14dae1`"; that sentence is wrong and is
corrected there. F12.9's numbers are unaffected — it changes no Core code and
used the binary only to render a pack.

Rebuilt for this run:

- `MesenCore.dylib` sha256 `d3e58bbb18d598c1af8c0d321e13d73b1830ae65f4413f265b98bc23e30757aa`,
  which does contain the new strings (`skipped for an earlier capture's gate`,
  `dropped post-hoc`).
- `scripts/headless_record` sha256
  `2982c0a665213924458b32954305cf420d9f3e6e89787e306e46f4457e42f068` —
  unchanged, and correctly so: the harness source did not change and it loads
  the dylib by absolute path.

**The lesson generalises: a Core measurement must prove the binary contains the
change before it measures anything.** Grepping the dylib for a string the
change introduces costs nothing and would have caught this immediately.

## Method

Four ROMs, chosen as the worst offenders of the F12.2 sweep plus the game issue
#339 was filed against. Each recorded twice, same route both times — 60 s from
power-on, `bootstrap`, no input, ROM copied to a scratch directory so no sibling
pack could be discovered — once with the stale dylib ("before") and once with
the rebuilt one ("after").

Co-gating is counted by comparing condition **definitions**, never raw lines:
`<condition>screen003_A,tileAtPosition,88,24,3D,…` embeds the capture's own
number in its name, so two captures with the same probe triple have different
text. Comparing raw lines returns 0 co-gated captures on data that has 95.

**Control.** The "before" run reproduces the F12.2 sweep packs' condition
definitions **byte for byte** on all four games, so the before/after pair is
route-matched and the only variable is the binary.

## Result

| game | before: captures | before: co-gated | after: captures | after: co-gated |
|---|---|---|---|---|
| Donkey Kong | 47 | **46** | 30 | **0** |
| Ice Climber | 25 | **22** | 4 | **0** |
| Pac-Man | 26 | **22** | 17 | **0** |
| Mike Tyson's Punch-Out!! | 10 | **5** | 10 | **0** |
| **total** | **108** | **95 (88%)** | **61** | **0** |

The recorder's own accounting, from the anchor summary in each run's log:

| game | volatile anchors | still matching another screen | skipped (ADR-0217 A) | dropped post-hoc (ADR-0218 B) |
|---|---|---|---|---|
| Donkey Kong | 46 | 33 | 17 | **0** |
| Ice Climber | 0 | 23 | 21 | **0** |
| Pac-Man | 9 | 23 | 9 | **0** |
| Mike Tyson's Punch-Out!! | 8 | 6 | 0 | **0** |

### Punch-Out!! is the clean case: separated, not dropped

Issue #339's game keeps **all ten** captures and skips none. The five credits
screens that shared the triple `88,24,3D` / `160,24,3F` / `120,56,53` — of which
four could never draw — now each carry a distinct one:

```
screen003:  88,24,3D    160,24,3F    120,56,53
screen004:  96,104,1D    88,24,3D    160,24,3F
screen005: 136,160,1F    80,152,12   152,104,11
screen006: 216,184,2B    56,184,23   136,160,1F
screen007: 152,200,27   216,184,2B    32,184,1A
```

That is ADR-0217 Option C working as specified: every other pending capture is
a forced rival, so the anchor search is pushed off the cells the five screens
share and onto the ones that tell them apart. Nothing was thrown away to get
there.

### The cost is real and lands on the other three

Donkey Kong loses 17 captures, Ice Climber 21 of 25, Pac-Man 9. Those are
Option A refusals — a capture whose gate an earlier one already satisfies is
not written. That is the correct outcome by ADR-0217's own reasoning (a capture
that cannot be told apart from an earlier one could only ever draw over it, or
never draw at all), but it should be read as a number, not a detail:
**Ice Climber's recording goes from 25 captures to 4.** A game whose screens
genuinely differ by very little now yields far fewer captures, and the artist
sees that as missing surfaces rather than as a fixed bug.

### ADR-0218 Option B never fired

`dropped post-hoc` is **0** on all four games. The avoidance pass (Option C's
forced rivals) plus the write-time refusal (Option A) separated or rejected
everything; the post-hoc scan is a net that caught nothing in this sample. It
is not dead code — its unit tests exercise the byte-identical-frame case the
ADRs name — but no real recording here reached it.

## Extended the same day: the whole bounded library

The section above stopped at four games. The remaining 26 of the 30-ROM library
were re-recorded on the same route with the same rebuilt binary:

> **30 packs, 193 captures, 0 co-gated.**

That is the strong form of the claim, and it does not depend on route matching:
after this change **no two captures of one recording may share a gate**, so any
co-gated capture in a fresh pack would be a failure whatever the route. There
are none in the whole library.

Every game the F12.2 sweep flagged, with its capture count before and after:

| game | before (caps / co-gated) | after (caps / co-gated) | captures lost |
|---|---|---|---|
| Donkey Kong | 47 / 46 | 30 / 0 | 17 |
| Ice Climber | 25 / 22 | 4 / 0 | **21** |
| Pac-Man | 26 / 22 | 17 / 0 | 9 |
| Bomberman | 16 / 10 | 16 / 0 | **0** |
| Mike Tyson's Punch-Out!! | 10 / 5 | 10 / 0 | **0** |
| Tennis | 5 / 4 | 5 / 0 | **0** |
| Mario Bros. | 3 / 2 | 3 / 0 | **0** |
| The Flintstones | 3 / 2 | 3 / 0 | **0** |
| **sweep total** | **237 / 113** | — | — |

**Five of the eight offenders lose nothing.** Bomberman, Punch-Out!!, Tennis,
Mario Bros. and The Flintstones each keep every capture and go to zero
collisions — Option C's forced rivals found separating cells and no capture had
to be refused. The cost is concentrated in three games whose screens really are
near-identical, and Ice Climber is the extreme: 25 captures to 4.

The 26-game half has no route-matched "before" — those sweep packs were recorded
on a different route, so their counts are a reference, not a control. The
four-game half above does have one (byte-for-byte), and the absolute claim
(0 co-gated anywhere) needs no control at all.

## What this does not deliver

- **No pixel measurement against issue #339's specific complaint.** #339 reports
  a capture drawing over a frame it mismatches by 16 047 pixels. This log shows
  the gate that let that happen is gone; it does not re-render the pre-fight
  card and re-measure the distance. That render is the remaining half of
  closing #339, and a first attempt is recorded below.
- **Nothing about variants.** ADR-0217 answer (1) kept ADR-0159's rule that a
  capture owns its variants. This run does not test it.

### The #339 render attempt, and why it did not conclude

Punch-Out!! was rendered from power-on at eleven timestamps between 8 s and
56 s, three ways: no pack, the pre-change pack and the post-change pack — the
two packs route-matched, same 11 606 `<tile>` keys, differing only in their
capture gates. **The two packs rendered identically at every timestamp
(0 pixels).**

That is not evidence the change does nothing; it is evidence the sampling
missed the moments. Matching each no-pack frame against the captures' own
`.orig.png` shows why: the nearest capture at each sampled second is off by
5 632 to 765 792 pixels, i.e. no gate was satisfied at any of them. A capture
draws only inside the static window it was frozen for, and second boundaries
do not land in those windows.

Two things this did establish, both worth keeping:

- **A pack in `mesen-home/HdPacks/<stem>/` is not found by this harness** —
  the log says `LoadHdPack: 0 ms; no-pack` and the screenshot comes back at
  native 256×240. The sibling convention (`<romdir>/<stem>/auto`) loads it:
  `tiles=11606 keys=18895 images=96 backgrounds=10`, screenshot 1024×960. Any
  render measurement must check the resolution and the `LoadHdPack` line before
  trusting a zero.
- **Comparing a rendered pack frame against a nearest-neighbour upscale of the
  no-pack frame is meaningless** — it measures the upscale, not correctness.
  The distances (9 309 to 105 850) say nothing about whether the right capture
  drew.

Closing #339 needs the frame, not the second: either a dense search for a frame
whose gate is satisfied, or an instrumented run that logs which capture drew.

## Reproduce

```
make capture-tool                       # and grep the dylib for the change first
./scripts/headless_record "<rom>.nes" 60 <out>/run bootstrap log
python3 - <<'PY'   # parse <condition> name->definition, key each <background>
PY
```

The parser must key each `<background>`'s `[a&b&c]` prefix through the
name→definition map, not by the prefix text.

## Suites

- `make core-unit-tests` — the ADR-0217/0218 cases are in
  `scripts/core_unit_tests.cpp` (`MesenSheets::AnchorKeysOf` /
  `SameAnchorKeys`, host-free).
- `scripts/checks/run_python_tests.sh` and `make doc-checks` — green on the
  branch this log ships on.
