# ADR-0186: A recording also yields a code/data map, and the only ROM we disassemble is the part we executed

- Status: accepted (2026-09-14, at the user's direction: "inclua sua
  recomendação no goal e construa em paralelo"; shipped as slice F9.27 of
  `docs/roadmap/PRD-mesence-enhancement-ecosystem.md`). **Amended the same
  day** — the Context's art-coverage justification was withdrawn after
  measuring how the reference pack was actually built; see "Amended
  2026-09-14" below. The Decision is unchanged. **Amended 2026-09-15
  (Phase 11 C.8):** the debugger/`cdl=` wall-clock cost is published below
  under Consequences.
- Date: 2026-09-14
- Related: ADR-0183 (the artist kit — §3 "evidence and inference are never
  confused" and the rule that no generator invents a name both carry over
  unchanged), ADR-0182 (recording coverage is a union), ADR-0184 (what a run
  may and may not do to the art it records), ADR-0185 (the TAS movie as a
  recording driver — the runs that make this coverage worth having)

## Context

Two surfaces the artist needs are, today, out of reach of recording, and both
are blocked by the same thing: the game only shows us what gameplay happens to
reach.

**Form.** Contra (USA) keeps its tile data RLE-compressed inside PRG and
unpacks it into CHR RAM at run time. 77 accumulated recordings cover 3077
distinct tile shapes, 53.8% of the reference `Contra80s` pack. Reading the
Japanese cartridge's uncompressed CHR ROM raises the union to 90.3%, and the
remaining 328 keys are simply art no run has ever caused to be unpacked.

**Colour.** The key we index by is `(tileData, palette)`. A CHR ROM read from
another region's cartridge is evidence of form only — it carries no palette at
all. Palettes live in PRG as tables uploaded by a routine, and a recording sees
a palette only when the game happens to display it.

The obvious tool for both is a disassembler, and the obvious objection is that
a static NES disassembler is a project of its own that would not deliver a
single tile. Static analysis of a 6502 ROM has three walls: separating code
from data, resolving which bank an address belongs to, and naming. The first
two are heuristic and the third is human.

The observation that makes this cheap is that **we do not need static
analysis, because we run the game**. Mesen's Code/Data Logger already records,
byte by byte, what the CPU fetched as an instruction and what it read as data,
at absolute ROM offsets. The processor performs the code/data separation; the
mapper is a non-problem because the offset is absolute. Two of the three walls
fall by construction.

It is also already written. `Core/Debugger/CodeDataLogger.{h,cpp}` holds the
`CDLv2` format and `StripData`; `Core/Debugger/DebugTypes.h` defines the flags;
`Core/NES/Debugger/NesDebugger.cpp` feeds them, including marking every call
destination `SubEntryPoint`, so function discovery is a side effect of playing;
`Core/NES/Debugger/NesCodeDataLogger.h` keeps a **second logger for CHR ROM**
and reports drawn-versus-total CHR bytes. `Core/Debugger/Disassembler.cpp`
exists. Nothing here is being invented.

Non-goals: this ADR does not add a disassembler, does not add a debugger UI,
does not attempt to label or name anything, and does not decompile. It does not
change what a recording records as art.

## Decision

### 1. A code/data map is a recording output, not a separate activity

The recording harness gains a `cdl=<path>` flag. A run with it initializes the
debugger, and on exit writes the Code/Data Logger state to that path in the
Core's own `CDLv2` format — the same file the GUI debugger reads and writes, so
the artifact is not ours alone and does not need a converter.

A code/data map is produced **by the runs we already do**. It is not a reason
to record anything twice, and it is not a new kind of run.

### 2. Coverage accumulates by union, here as everywhere

When the target path already holds a `.cdl`, the harness loads it before the
run so the flags accumulate. This is the same rule as ADR-0182 and ADR-0184 §4:
what a set of runs covers is the union of what each covers, never the best
single one. The offline tool provides an explicit `union` of several files for
runs made in parallel.

Two maps may only be unioned when their `CDLv2` headers carry the same ROM
CRC32. A mismatch is refused and both values are named — a map is meaningless
against a different ROM.

### 3. A run that logs nothing must fail, not pass quietly

After the run the harness asserts the map has a non-zero code byte count and
fails the run, naming the cause, when it does not.

This clause is written from scars. A cheat that changed nothing looked exactly
like a cheat that did not help until a `static_assert` found a 20-versus-17
byte ABI mismatch (ADR-0184, "Measured 2026-09-13"). A desynchronised movie
played all 114913 frames and logged `movie ended` cleanly while the game typed
garbage (ADR-0185 §3). A silent zero is this project's characteristic failure
and every new output gets a positive check.

### 4. The map classifies access, and access is not meaning

A byte marked `Data` was read by the CPU. That is the entire claim. It is not a
claim that the byte is a tile, a palette entry, a level, or anything else, and
no tool in this family may present it as one.

Concretely: a large contiguous run of bytes marked `Data` that no code overlaps
is reported as **a candidate**, ranked by size, with its offset and bank — and
never with a name. Naming stays where ADR-0183 §3 put it: in a human file, or
in a human's judgement acting through the review. A generator that named the
Contra tile table "the Contra tile table" because it was the biggest data run
would be wrong the first time the biggest data run was a music sequence.

### 5. Only the part we executed is emitted

`strip --keep used` and `--keep unused` follow `CodeDataLogger::StripData`'s
existing semantics. The deliverable is deliberately not "a disassembly of the
ROM" but "the bytes we have evidence for", which is a smaller and honest
object, and the only one this project has standing to produce.

## Consequences

- **The debugger's cost lands on every run that asks for a map.** Mesen's
  debugger forces a slower CPU path; the flag is opt-in for that reason, and
  the cost must be published as a measured number rather than a caveat. A
  1920-emulated-second TAS run currently takes ~5 minutes of wall clock, and
  the honest question is what multiple of that the map costs.
  **Measured 2026-09-15 (Phase 11 C.8):** Zelda (USA), 30 emulated seconds
  (1803 frames), `hdpack-off`, same binary — without `cdl=` wall clock
  **2.7 s** (`time` real 2.85 s); with `cdl=<path>` wall clock **5.1 s**
  (`time` real 5.19 s). Ratio **≈ 1.9×** wall clock (1.82× by `time` real).
  Emulated fps stayed 60.099 both ways; the cost is wall time, not a drop in
  the emulator's master-clock accounting. Scaling the ~5 min TAS figure by
  1.9× puts a CDL-on map run near **~9.5 min** wall for the same movie.
- **A `.cdl` is per-ROM and permanent.** It is keyed by ROM CRC32, accumulates
  forever, and is worth keeping between sessions — unlike a recording, it does
  not go stale when a generator changes. It is unversioned like `runs/`, but it
  is the one unversioned artifact whose loss actually costs re-running
  everything.
- **This does not by itself extract one tile.** It produces the map that makes
  extraction a bounded question instead of an open one. The step after — taking
  a candidate data region and testing whether it decompresses to tiles the pack
  is missing — is a separate decision and will need its own ADR, because it
  crosses from evidence into inference, which is exactly the line ADR-0183
  draws.
- **The CHR logger gives a second, independent coverage number.** Drawn-versus-
  total CHR bytes is measured at the ROM, where ADR-0182's coverage is measured
  at the recorder. Two measurements of the same thing from different ends is
  how the barrier-cheat mistake was caught, and it is worth having again.

## Amended 2026-09-14: the coverage justification is withdrawn

A teardown of `Contra80s` — the reference pack this whole effort measures
itself against — establishes that its author never had the problem this ADR's
Context opens with, and did not solve coverage with anything resembling
analysis.

**What the evidence says.** The shipped release carries 336 PNGs and no source
of any kind: no layered file, no palette, no script, no `.cdl`, no
disassembly, and nothing anywhere in the repo, the release, the 1822 comment
lines inside `hires.txt` or the linked threads suggesting awareness that Contra
(USA) keeps its CHR RLE-compressed in PRG. Coverage was reached by **ten
separate Pack Builder recording sessions**, one per stage plus dedicated ones
for the stage-2 and stage-4 bosses, distinguishable in the shipped `hires.txt`
by section headers ("WIP5 - Stage 2 Boss", "Stage 4a - Boss", "Stage 7 -
Tiles") and by a per-session filename prefix on each session's pattern pages.

**Why that defeats the argument.** The emulator hands over the *decompressed*
pattern page at the moment the game assembles it, already in the palette the
game used. Recovering the compressed blob from PRG buys tiles the game never
assembles — and a tile with no screen, no palette and no trigger is not
remastering material. The route from 53.8% to full coverage is to record again
from a different entry point, not to read further into the ROM.

**What is withdrawn.** The Context's framing of this ADR as the answer to "the
328 Contra tiles no run unpacks" and to the colour gap. Neither is a code/data
map's job. That framing also travelled into the F9.27 slice row and into the
session's goal note, and is corrected in both.

**What stands, and why this ADR is not superseded.** The Decision is unchanged
and every clause of it is still earned:

- §1 and §2 give a per-run, unioned record of **what code ran and what it
  touched**, which is the only artifact we have that answers questions about
  the game's behaviour rather than its output. `SubEntryPoint` marking makes
  function discovery a side effect of playing.
- The CHR logger's drawn-vs-total byte count is a coverage measurement taken
  **at the ROM**, where every other number we have is taken at the recorder.
  Two independent measurements of the same quantity is exactly how ADR-0184's
  barrier-cheat error was caught, and the per-stage recording campaign this
  amendment endorses is precisely the thing that needs an independent check.
- §3's loud-failure gate and §4's access-is-not-meaning rule are general and
  cost nothing.

So: this is a **program-analysis** tool that a recording produces for free, not
an art-extraction tool. Stated that way it is worth its 1.67x run cost. Stated
the other way it was going to send us reading PRG for tiles no artist could
use, and it is better to lose the justification now than to spend a week
proving it empirically.

**Method note, which is the part worth keeping.** The finding came from asking
how somebody who already succeeded did it, before spending more effort on our
own theory. That question cost one agent and returned a route we can act on
this week; the theory it displaced had already cost days.
