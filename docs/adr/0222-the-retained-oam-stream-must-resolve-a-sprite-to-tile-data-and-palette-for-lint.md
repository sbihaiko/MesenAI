# ADR-0222: The retained OAM stream must let lint resolve a sprite to its tile data and palette, so `spriteNearby`, `spriteAtPosition`, `positionCheck*` and `memoryCheck` stop reporting `not evaluable`

- Status: **accepted 2026-09-22 — option A, shipped the same day as F12.14.** Go-ahead
  to build, verbatim: *"dispara as frentes 1, 2, 3 e 4 em paralelo usando
  workflows"* (frente 2 = this slice). Ships with unit tests covering the
  decision (`core_unit_tests.cpp` ADR-0222 cases: a palette-only recolour is its
  own frame; exact `K`/`P`/entry lines) and `scripts/test_mep_conditions.py`
  (43 checks); measured on the F12.6a Contra route —
  `docs/validation/f12.14-oam-dump-self-describing-2026-09-22.md`: every
  authored condition reports a verdict, `nearThePlayer` = never held 0/749,
  `livesMatch` (memoryCheck) = mixed. ADR-0169's wire was NOT changed: it
  already carries the raw OAM attribute byte, so the palette is implicit there
  (recorded as an amendment bullet in ADR-0169). Earlier the same day: user's picks
  through a structured question, verbatim: *"A: dump autodescritivo
  (Recommended)"* and *"So registrar (Recommended)"* — a decision and a request
  for work, with no same-turn implementation and no go-ahead to build sought or
  given. The pending slice is PRD Part A **F12.14**. Answers to *What a human
  has to pick*: (1) A; (2) yes — one palette id per entry, and `SameEntries`
  compares it; (3) the viewer's wire format follows: ADR-0169's OAM entries
  gain the same byte and `make capture-tool` is rebuilt, so dump and wire do
  not diverge; (4) the stop condition is the one stated in question 4, and the
  re-record of the F12.6a Contra route is required. `memoryCheck` ships with
  the same slice. Proposed earlier the same day with the three options below,
  which stay in the file as the record of what was weighed.
  **Amended 2026-09-25 (ADR-0234, issue #505):** the entry token gains
  `<visible>,<bg>,<hidden>`; see the dated note in the Decision section.
  Additive, so this ADR's decision is extended rather than reversed.
- Date: 2026-09-22
- Related: ADR-0197 (§2 lint validates authored conditions against recorded
  routes; §3 the retained `$0000`–`$07FF` window, option (b), shipped as
  F12.6b), ADR-0189 (`spriteNearby` is how a sprite-group edge is serialized),
  ADR-0159 (amendment 2026-09-05: the grid stream carries a per-cell palette
  id), ADR-0169 (the live recorder's wire format and `make capture-tool`),
  ADR-0181 (the two port bytes in the OAM dump), ADR-0179/ADR-0174 (poses are
  built from the same `_oamFrames`), PRD Part A Phase 12 (F12.6a/F12.6b rows
  and the "Order" paragraph that says "an OAM-format decision nobody has
  taken"), `docs/validation/f12.6a-lint-authored-conditions-2026-09-19.md`,
  `docs/validation/f12.6b-recorder-retains-internal-ram-2026-09-19.md`,
  `scripts/mep_conditions.py`, `scripts/stages/README.md` ("Two env-gated
  save-time dumps")
- Supersedes / amends: ADR-0197 §3's "Limits" list — the sprite bullets stop
  being permanent limits and become "a recording made before F12.14 reports
  `not evaluable: OAM stream carries no tile data`"; the dump-format sentence
  of `scripts/stages/README.md` ("`node,x,y` per sprite"); and ADR-0169's OAM
  wire format by reference (one palette byte per entry). Each amendment is
  written in the PR that ships F12.14, not before.

## Context

ADR-0197 §2 promised that lint runs every hand-authored `<condition>` over the
recorded routes and reports a verdict, and that a limit is stated rather than
hidden. F12.6a shipped the evaluator and F12.6b widened the grid stream with
the RAM window. Both logs end on the same open item: five condition types
still report `not evaluable`, and every one of them is blocked by **what the
OAM dump spells**, not by what the recorder saw.

### What the recorder retains, and what it writes

The recorder keeps two retained streams per recording, both capped at
`kMaxSheetFrames`:

- **The grid stream** (`MESEN_SHEET_GRID_DUMP`, `HdPackBuilder::WriteGridDump`)
  is self-describing. `K <id> <32 hex tile data> <8 hex palette>` interns a
  shape the first time it is drawn, `P <id> <8 hex palette>` interns a palette
  word, `M <4096 hex>` carries the frame's RAM (ADR-0197 §3), and each cell
  line is `<x> <y> <shape> [<palette id>]`. `scripts/mep_conditions.py`
  (`parse_grid_dump`) resolves a cell to `(tileData, palette)` from the file
  alone. `tileAtPosition`, `tileNearby`, `frameRange` and
  `memoryCheckConstant` are evaluable because of it.
- **The OAM stream** (`MESEN_OAM_STREAM_DUMP`, written in the save path next
  to `BuildSpriteVocabulary`) is **not**. Each frame line is
  `<frame> <repeat> <port1> <port2>` followed by `<node>,<x>,<y>` per sprite,
  where `node` is `vocab.Find(key)` — an index into the *sprite vocabulary*
  built in memory at save time for `BuildPoses`. That vocabulary is serialized
  as pose ids in `sheets/poses.json`, not as a node → tile table, so a reader
  holding only the dump cannot turn a node into 16 pattern bytes. The comment
  above the writer is exact: *"Debug aid, sibling of MESEN_SHEET_GRID_DUMP …
  so a spike can measure pose succession … Not a pack file."* It was written
  for ADR-0179/ADR-0181's measurements, where the node index was the unit.

Two facts about the entry make the fix cheaper than it looks, and one makes
it dearer:

1. **`OamEntry::Shape` is already a `ShapeId`** — `HdPackBuilder::ShapeIdFor`
   interns sprite tiles into the same `_shapeTiles` table the grid stream's
   `K` lines are written from (`tile.GetKey(true)`, palette-wildcarded). The
   dump throws that id away and writes the vocabulary index instead. The id
   space is shared by construction.
2. **X and Y are already in the dump**, so `positionCheckX/Y` and
   `originPositionCheckX/Y` are blocked only by the same resolution — which
   sprite is the conditioned tile.
3. **The entry carries no palette.** `OamEntry` is `{Shape, X, Y}`. The grid
   stream needed the ADR-0159 amendment to add a per-cell palette id because
   `ShapeId` wildcards the palette on purpose; the OAM stream never got that
   amendment. A `spriteNearby` line carries `tileData` **and** `palette`
   (with an optional `ignorePalette`), so a dump without a palette can settle
   the shape half of the condition and not the colour half.

### `memoryCheck` is a different case

`memoryCheck` compares two watched addresses. F12.6b retained the whole
`$0000`–`$07FF` window on every frame, so both operands are already in the
`M` line; the log says it is "a few lines away" and was not done because
ADR-0197 §3's stop rule named `memoryCheckConstant` only. It needs no format
decision. It is in this ADR so the two open items in the PRD's "Order"
paragraph close together, and so nobody re-derives that it was blocked on the
OAM dump — it was not.

### Non-goals

- Not a change to what the recorder **retains**: the cap, the de-duplication
  (`SameEntries` → `RepeatCount`) and the 128-entries-per-frame bound stay.
- Not a pack file. Whichever option wins, the dump stays an env-gated
  save-time artifact read by lint and by measurements, never by the loader.
- Not the loader's evaluation. This is about lint's verdict on a recording;
  `HdPackConditions` at render time is untouched.
- Not a fix for `frameRange`'s phase problem (ADR-0189 §4, restated in
  `mep_conditions.py`), which is a property of the recording, not of a dump.

## Decision

### Decided 2026-09-22: option A

**The OAM dump becomes self-describing, exactly as the grid dump is.** It
interns shapes with `K <id> <32 hex tile data> <8 hex palette>` and palette
words with `P <id> <8 hex palette>`, and each sprite entry is written as
`<shape>,<x>,<y>,<pal>` — `shape` the `ShapeId` `RecordOamEntry` already
holds, `pal` the interned palette word of the sprite's OAM attribute bits.
`OamEntry` gains that palette id and `SameEntries` compares it, so two frames
that differ only in sprite colour no longer collapse. The frame line keeps
its `<frame> <repeat> <port1> <port2>` prefix (ADR-0181). `mep_conditions.py`
gains a `parse_oam_dump` beside `parse_grid_dump`, and `spriteNearby`,
`spriteAtPosition`, `positionCheckX/Y` and `originPositionCheckX/Y` get a
verdict from the OAM file alone; `memoryCheck` reads its second operand from
the `M` line as stated below. The pose-succession consumers of the old
`node` token are named in the slice and their frozen logs keep their text.

**Amended 2026-09-25 (ADR-0234, issue #505): the entry token gains three
fields.** It is now `<shape>,<x>,<y>,<pal>,<visible>,<bg>,<hidden>` — `visible`
the sprite half's pixels this frame put on screen, `hidden` the ones an opaque
background took from it, `bg` its OAM attribute bit 5 (behind the background).
None is identity: `SameEntries` still compares shape, x, y and palette only, so
frame de-duplication is unmoved. The addition is additive and backward
compatible, exactly as this ADR's option A promised a self-describing format
would be — `parse_oam_dump` accepts a four-field entry and then reports the
three facts as absent rather than as zero. ADR-0169's live viewer wire is
deliberately **not** extended, so the two formats now differ by these three
fields.

### The options as proposed

The question was: *how does a reader of the OAM stream get from a sprite
entry to the `(tileData, palette)` a condition names?*

### A. Make the OAM dump self-describing, like the grid dump

The OAM dump gains the same three intern lines the grid dump has — `K`, `P`
and, per entry, a palette id — and its entry token becomes the `ShapeId`
instead of the vocabulary index: `<shape>,<x>,<y>,<pal>`. `OamEntry` grows a
palette id (the OAM attribute's two palette bits resolved to the interned
palette word at record time, the same word the grid's `P` line spells). A
reader resolves every sprite from the OAM file alone.

- **For:** one format rule for both streams, one parser shape in
  `mep_conditions.py`, no coupling between two files; `spriteNearby` gets a
  full verdict including the colour half; position checks fall out.
- **Against:** the largest Core touch of the three — `OamEntry` changes,
  `RecordOamEntry` has to see the sprite palette, and ADR-0169's live viewer
  wire format (`make capture-tool`) carries OAM entries too and must skip or
  read the new byte. `SameEntries` must include the palette or two frames that
  differ only in sprite colour collapse together.

### B. Write the `ShapeId`, resolve against the grid dump of the same recording

One-token Core change: the dump writes `entry.Shape` instead of
`vocab.Find(key)`. `mep_conditions.py` loads the OAM dump *beside* the grid
dump of the same recording and resolves each shape through the grid's `K`
table (same `_shapeTiles` id space, fact 1 above). No palette is written, so
`spriteNearby` evaluates the tile half and reports the palette half as
*unresolved* — a distinct state, never a pass, spelled in the report — unless
the condition carries `ignorePalette`.

- **For:** cheapest; no struct change, no viewer wire-format change, no
  `SameEntries` question. Position checks become evaluable at once.
- **Against:** it couples two files that today are independent — a grid dump
  and an OAM dump from different saves of one session would resolve wrongly
  and silently, so the reader must refuse a pair whose `K` table lacks a shape
  the OAM names. And it leaves the colour half of `spriteNearby` permanently
  unresolved, which is a third verdict state the report has to teach.

### C. Leave the sprite conditions `not evaluable`, and say so where the artist reads it

No Core change. `ARTIST.md` and `docs/remastering-a-game.md` state that a
hand-authored sprite or position condition is admitted (ADR-0197 §1), passes
lint's syntax check, and is **not** validated against the routes; the report
keeps printing the reason.

- **For:** nothing to build; the evidence measured so far
  (`docs/validation/metroid-artist-workflow-evidence.md`) shows a hand author
  working in `frameRange`, not in sprite conditions.
- **Against:** ADR-0189 makes `spriteNearby` the serialization of every
  sprite-group edge the recorder itself emits, so the pipeline's own most
  common condition is the one lint cannot check; and ADR-0197 §2's promise —
  a verdict, with limits stated — was written without this exception.

### `memoryCheck` — decided as part of whichever option wins

`mep_conditions.py` evaluates `memoryCheck` from the `M` plane exactly as it
evaluates `memoryCheckConstant`, with the second operand read from the same
line. A recording without an `M` plane keeps reporting `not evaluable: no
memory stream in recording`. This is Python only and ships with the slice
that implements A or B, or on its own if C is picked.

## What a human has to pick

1. **A, B or C.** A and B are not additive — B is the subset of A that skips
   the palette; picking B now and A later means changing the dump format
   twice, and every measurement log that quotes a dump line would have to say
   which one.
2. **Is the colour half of `spriteNearby` worth a byte per entry?** A says yes
   (one field, one `SameEntries` rule); B says it may stay unresolved forever.
   The upstream loader compares the palette unless `ignorePalette` is set, so
   under B lint's verdict is weaker than the loader's behaviour.
3. **Does the viewer's wire format follow?** Under A, ADR-0169's live recorder
   carries OAM entries to the viewer; either it gains the byte (and
   `make capture-tool` is rebuilt) or the dump and the wire diverge on
   purpose, which is a sentence in ADR-0169.
4. **Stop condition for the slice.** The natural one is the F12.6a bounded
   input: the Contra pack's authored `nearThePlayer,spriteNearby,…` line goes
   from `not evaluable` to a verdict (`always held` / `mixed` / `never held`)
   on the same 60 s route, plus `memoryCheck` on a two-address condition
   against the same `M` plane, plus a unit test on a synthetic dump pair. Re-
   recording is required under A and B (the dump format changes); C needs
   none.

## Consequences

- **Whatever is picked, the report changes shape.** Under A or B the
  `not evaluable` reason strings in `mep_conditions.py` that cite "vocabulary
  indexes" are retired for the sprite and position types; under C they stay
  and are copied into the artist-facing page. The F12.6a/F12.6b logs keep
  their historical text.
- **`scripts/stages/README.md` describes the dump.** Its "node,x,y per sprite"
  sentence is wrong under A and B and must be amended in the same PR that
  changes the writer, or the next measurement reads the wrong field.
- **ADR-0179/ADR-0181 measurements read the node index.** Any script that
  still consumes the old dump (`scripts/mss_ram.py`-era spikes, pose-succession
  measurement) breaks under A and B. The spikes were one-off and their logs
  are frozen; the breakage is acceptable but must be named in the slice.
- **Deferring has a cost that is not zero.** Every `spriteNearby` the
  recorder emits under ADR-0189 is a condition the same toolchain refuses to
  check, and the PRD's "Order" paragraph has said "a decision nobody has
  taken" since 2026-09-19. This ADR exists so that sentence points at a file.
