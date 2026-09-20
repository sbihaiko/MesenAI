# ADR-0219: A kit may project over the ROM alone — every cell `fill`, `seen: false`, no play session — which amends ADR-0183 §1's "generated from an already-recorded pack" without making the ROM a second source of truth

- Status: **proposed 2026-09-20. Not decided, and not implemented.** Nothing in
  this file exists in code: `scripts/artist_chr_kit.py` takes a recorded pack as
  its positional argument, has no `--static` flag, and `Pack.__init__` raises
  `f"{root}: no textures/hires.txt — not a recorded pack folder"`. The PRD
  blocks Part A slice **F12.9** on this amendment, so a human has to accept it
  before the slice starts.
- Date: 2026-09-20
- Related: ADR-0183 §1/§2.4/§3/§4 (the artist kit this amends), ADR-0210 §2
  (the ROM's own CHR as a coverage source), ADR-0172 (the sheet sidecar's CHR
  index — the static page is index-keyed by construction), ADR-0209 Q4 (the
  editing-surface gap, and why "the shape exists" is not "the tile is
  reachable"), ADR-0165 (stdlib-only toolchain), ADR-0182 (how far a recording
  goes — what "no play session" is being compared against),
  `docs/validation/metroid-artist-workflow-evidence.md` (the recording is the
  bottleneck, not the drawing), PRD Part A F12.9, F12.10 path (d), F12.12
- Supersedes / amends: **amends ADR-0183 §1**, by reference and in this file
  only — the register keeps ADR-0183's text intact, the way its own amendments
  record a change without rewriting the amended ADR. §1's first sentence gains
  a second admissible source and an explicit degenerate case; nothing else in
  ADR-0183's Decision is touched. ADR-0183 stays `accepted`.

## Context

### What the PRD asks for, verbatim

Part A slice F12.9, deliverable cell:

> | F12.9 | **Static kit from the ROM alone (CHR ROM games).** The delta is small and named as such: `artist_chr_kit.py` already builds rank-0 pages, `fill` cells, `seen: false`, the ADR-0172 sidecar and the CHR RAM refusal (ADR-0183 §2.4, ADR-0210 §2), and the recorder already emits every CHR ROM tile with `Y` (`HdPackBuilder::AddRomTiles`). What does not exist is running any of it **without a play session**: the positional argument is a recorded pack and the tool raises on a folder with no `textures/hires.txt`. This slice (1) adds `--static`, which accepts a missing or empty pack folder and derives every page from `--rom`; (2) makes `mep_build.py build` accept a pack folder holding only `chr/` pages, their sidecars and `chr/fill-rules.hires.txt`; (3) writes an `ARTIST.md` whose first line says nothing on these pages was seen in play, and that no figure, scenery or map file exists because nothing was observed. A CHR RAM ROM is refused as today, with a pointer to F12.12.

Part A slice F12.9, decision cell:

> | **Needs an ADR before start:** ADR-0183 §1 reads "generated from an already-recorded pack" and this slice projects over the ROM alone, so §1 is amended (a kit may project over the ROM, every cell `fill`) — in the ADR that accepts ADR-0210 §2 or in a successor. Stdlib only; no Core change; the tool never invokes `headless_record` and the test asserts it. Bounded input: Super Mario Bros. (512 tiles, 2 banks) and Mega Man 3 (CHR ROM, the F12.5 game). Stop when (1) page count equals CHR size / 4 KB and every cell is `fill`; (2) the pages-only folder passes `mep_build.py build` with 0 errors and the rebuilt `hires.txt` has exactly *N* = CHR tile count `<tile>` rules, all `Y`; (3) one cell painted on SMB's bank 0 renders pixel-exact in a `headless_record` screenshot of the title screen — by reopening the ROM, which already works; the F12.3 reload is used when it has shipped and is not a prerequisite; (4) wall time from ROM to `kit/` under 10 s on the dev machine, recorded in `docs/validation/`. Re-measures **"faster on day one"** at its floor: seconds, not a play session.

The row offered two homes for the amendment — "the ADR that accepts
ADR-0210 §2 or a successor". **ADR-0210 was accepted on 2026-09-20 without it**
(its §2 names the CHR source and changes nothing else), so this is the
successor, and the first fact worth recording is that the earlier of the two
homes is closed.

### The text being amended, verbatim

ADR-0183 §1, which is `accepted` and whose first sentence blocks F12.9:

> ### 1. A kit is a projection, never a second source of truth
>
> The artist kit is generated from an already-recorded pack, by scripts under
> `scripts/`, into a folder beside it (`kit/` by default) — never into the
> recording, and never by changing what the recorder captures at run time. The
> recorded pack stays the evidence; a kit can be regenerated, thrown away and
> regenerated differently without any recording being repeated.

The conflict is narrow and the word that carries it is **"already-recorded"**.
Every other claim in ADR-0183 survives a static kit untouched: "a projection,
never a second source of truth" (§1's own heading), the `kit/` output folder,
"never by changing what the recorder captures at run time", §3's marking of
inference, and §5's refusal to let a generator invent a name.

### Why the change is worth making

The artist evidence (`docs/validation/metroid-artist-workflow-evidence.md`)
puts the bottleneck on the recording, not on the painting, and ADR-0210's
measurement splits the 30-ROM bounded library cleanly: **23 games are CHR ROM**,
where every shape is in the file and `HdPackBuilder::AddRomTiles` already emits
each of them with `defaultTile = Y` — so a page that needs no play at all is
available today and costs a flag, not a subsystem. What a static kit cannot give
is **organisation**: figures, named scenery and stage maps come from OAM
co-occurrence, adjacency and scroll that were *observed* (ADR-0164, ADR-0182).
That is the whole of the difference, and it is why this is an amendment to §1's
first sentence and not a new kind of artefact.

### Non-goals

This ADR does not decide how a completed page reaches the artist's editor (that
is ADR-0209 Q4), does not add a `.ora` layer contract (F12.11's own ADR), does
not import a third-party key index (ADR-0210 §3, F12.12), and does not change
`Core/` — no recorder change, no format change, no new build step.

## Decision

### §1 as amended

The proposed replacement for ADR-0183 §1's first sentence and its supporting
sentence; the heading and the rest of the section stand as written.

> **§1 (amended).** A kit is a projection over a source of evidence. That source
> is a **recorded pack** whenever one exists — the normal case and unchanged:
> the artist kit is generated from an already-recorded pack, by scripts under
> `scripts/`, into a folder beside it (`kit/` by default) — never into the
> recording, and never by changing what the recorder captures at run time. When
> there is no recording, a kit **may instead project over the ROM's own CHR**
> (ADR-0210 §2), on a CHR ROM game and only there: every cell of every page is
> then `fill` and `seen: false` (§3), the surfaces that need an observation
> (figures, scenery, stage maps — §2.1–§2.3) are not produced at all, and
> `ARTIST.md` says so in its first line. The recorded pack stays the evidence
> and the preferred source; a static kit never displaces, outranks or merges
> with a recorded one, and a cell a recording observed is never a `fill`. The
> ROM is not a second source of truth *about the game* — it is the only source
> of the 23 CHR ROM games' shapes, and ADR-0210 §2 already says so. A kit,
> recorded or static, can be regenerated, thrown away and regenerated
> differently without any recording being repeated.

Three properties are load-bearing in that wording, and each is what keeps the
amendment from being a hole in §1:

1. **Recording wins, always.** A static kit is what a kit looks like when the
   recording contributes nothing. It is the degenerate case of behaviour
   `artist_chr_kit.py` already has — the tool already fills unrecorded cells
   from the ROM under the name `fill` (its preference order, step 4) — not a
   parallel vocabulary. There is no code path where a static page outranks a
   recorded cell, because there is no cell to outrank.
2. **Nothing is claimed to be seen.** `seen: false` per cell, `fill` per cell,
   and `ARTIST.md`'s first line says no play happened. §3 is what makes the
   static kit admissible rather than what it bends.
3. **The recorder is untouched.** No new capture mode, no "dump CHR statically
   at save time" flag in `HdPackBuilder`, no change to what a run writes. The
   static path reads a `.nes` file; it does not make recordings cheaper, it
   makes them unnecessary *for this half*.

### The tool contract this fixes

- **Input.** `scripts/artist_chr_kit.py` gains `--static`, which accepts a
  missing or empty pack folder and derives every page from `--rom`. The ROM is
  the whole input; the pack folder is an output location only.
- **The tool never invokes `headless_record`**, never reads a `.mss`, a route
  set, a `.bk2` or any replay, and never starts an emulator. The bounded
  input's test asserts this. This is a rule about the *tool*; F12.9's stop
  condition (3) uses a `headless_record` screenshot to check that a painted cell
  renders, and that is an acceptance test drawing its own evidence, not the
  generator acquiring a play session.
- **No donor.** A static kit takes no `--also`. `--also` means "another
  recording of the same ROM as evidence for this pack's holes", and its
  precedence rule ("the pack named on the command line own cells always win")
  presupposes a primary with cells; with `--static` there is no primary and
  nothing to attach a donor to. Passing `--also` with `--static` is an error,
  not a silent no-op.
- **Refused: a CHR RAM ROM** (§2.4, ADR-0210 §2), with a pointer to F12.12. The
  refusal is *stronger* here, not weaker: the existing CHR RAM recovery test —
  a contiguous PRG block pinned by tiles the bank *recorded* — has no recorded
  tiles to pin against, so the static path cannot recover a single CHR RAM tile
  by construction. Where the 7 CHR RAM games are concerned this ADR grants
  nothing and their hole stays stated.
- **Refused: a third-party key index.** A static kit is the ROM alone; a
  community `hires.txt` is never read on this path (that is ADR-0210 §3 and
  F12.12, which needs the recording anyway). `<condition>` lines stay never
  imported.
- **Session-free by construction, not by convenience.** No `.mss` to mint, no
  route set to declare, no movie to match — the traps F12.10's log records
  (nothing versioned, no declared dump) simply do not arise.

### What §1's amendment does **not** change

- §1's "never into the recording" and "never by changing what the recorder
  captures at run time" — unchanged, and unchanged in force.
- §3 (inference is marked, never confused with evidence) — unchanged. It is the
  clause the static kit rests on.
- §5 (naming comes from the data, or from a human, never from a generator) —
  unchanged, and *thinner* in the static case: with no recording there are no
  ids or counts to build a caption from, so `names.json` is the only remaining
  source and a static kit invents no caption at all.
- §2's four surfaces, as a list. A static kit delivers §2.4 only, and the
  shortfall is stated rather than papered over.
- ADR-0183's status. It stays `accepted`; this file amends one sentence of it.

### Two readings this ADR fixes rather than edits

- **§2.4's "the existing `textures/chr/` pages"** means *the CHR ROM's* pages.
  With a recording they are the pages the recording already wrote; without one
  they are the pages the ROM's banks define. Same surface, same cell grid, same
  sidecar — a page that exists because the ROM has 4 KB of bank there, which is
  also what "page count equals CHR size / 4 KB" in the acceptance test means.
- **§4's round-trip is not available verbatim** to a static kit and must not be
  faked. §4 accepts a surface when a rebuilt pack loses and invents no
  `(tileData, palette)` key *against the recording it came from* — and a static
  kit has no recording to diff against, so there is nothing to lose and
  "unchanged" is vacuous. Its substitute is the two halves that survive: the
  rebuilt `hires.txt` carries **exactly N = CHR tile count `<tile>` rules, all
  `Y`** (nothing outside the ROM's own CHR is drawn, and `Y` is the wildcard
  ADR-0210 already relies on), with `mep_build.py build` reporting 0 errors.
  `--verify` keeps printing counts.

### Acceptance (the slice's, restated so this ADR is self-sufficient)

Stdlib only, no `Core/` change. Bounded input: **Super Mario Bros.** (512
tiles, 2 banks) and **Mega Man 3** (CHR ROM, the F12.5 game). Stop when:

1. page count equals CHR size / 4 KB and every cell is `fill`;
2. the pages-only folder passes `mep_build.py build` with 0 errors and the
   rebuilt `hires.txt` has exactly *N* = CHR tile count `<tile>` rules, all
   `Y`;
3. one cell painted on SMB's bank 0 renders pixel-exact in a `headless_record`
   screenshot of the title screen, by reopening the ROM (F12.3's reload is used
   when it has shipped and is not a prerequisite);
4. wall time from ROM to `kit/` is under 10 s on the dev machine, recorded
   under `docs/validation/`.

## Consequences

- **F12.9 unblocks, and F12.10's path (d) with it.** `record_library.sh`'s
  route resolution already falls through to `static` for a ROM matching no
  route set; today that path resolves and produces nothing. After this it
  produces the one kit a route set was never needed for, which is the honest
  reading of the slice's name — and the SMB row of the F12.10 measurement, which
  currently says so in words, starts carrying a kit instead.
- **The ROM-SHA-1 pin is disabled by construction on this path, and that is the
  cost to name.** `artist_chr_kit.py` refuses when the pack's `<supportedRom>`
  SHA-1 differs from the ROM (ADR-0003/ADR-0039) precisely because "the fill is
  the one place a wrong input produces confident, plausible, wrong art". A
  static kit has no recording to pin against, so the guard has no anchor and the
  user's choice of `--rom` is the whole of the input. The substitute is that the
  kit manifest records the ROM's SHA-1 and every cell says `seen: false` — the
  art is the named ROM's, and the kit never claims it is the game's. Anyone
  tempted to add a heuristic in that gap should read the guard's comment first.
- **The palette side needs nothing.** ADR-0210's own correction settles it:
  `defaultTile = Y` is a per-rule palette wildcard, so a shape lifted out of CHR
  with no recorded palette matches whatever colours the game puts it under. The
  static kit's "all rules `Y`" is not a shortcut, it is the mechanism.
- **A static kit is a `kit/` folder and not a pack.** It is not written under
  `EnhancementPacks/`, is not auto-installed, and is not a bootstrap pack; the
  pack folder `mep_build.py build` is taught to accept is the pages-only pack
  the slice constructs, which is a build input and not something the client
  discovery paths should ever pick up. The existing `--out must not be inside
  the recorded pack` guard becomes a rule about the *artifact* class, not just a
  path check.
- **The measurement this ADR is really about is a floor, not a speedup.** The
  claim F12.9 re-measures — "faster on day one" — moves from "a play session" to
  "seconds". It says nothing about whether the static page is *useful*: an
  artist still has no figure, no scenery and no stage map, and ADR-0182's
  coverage limits, not this path, decide whether a page is worth opening. A
  static kit is the day-zero floor of the pipeline, and the PRD's own framing
  already says so.
- **A CHR ROM page from the ROM is the same set of shapes the recorder would
  have written**, so a later recording of the same game and a static kit are not
  in competition: the recording's keys carry palettes ADR-0210 calls the scarce
  resource, and the static page's `Y` rules cover everything else. Nothing here
  makes re-recording pointless, and nothing here makes a static kit a substitute
  for a recorded pack in any consumer that asks for organisation.

## Alternatives

- **A separate ROM-only generator** (a fifth script beside the four, walking the
  ROM's CHR and never constructing a `Pack`) — the smaller upstream surface, and
  the alternative considered. Refused, and not on size grounds. It does not
  dissolve the conflict: the conflict is about what a **kit** may project over,
  not about which file does the projecting, so the same §1 amendment would be
  needed anyway, or the artifact would have to stop being a kit. Once it stops
  being a kit, every downstream consumer pays for the second name — F12.11 takes
  "one SMB static page from F12.9" as a bounded input and stacks its `.ora`
  layers on the same pages, F12.12's `index` sheet sits beside them, and
  `ARTIST.md` is the same file. And the distinction that actually matters —
  evidence against inference — is already carried per cell by `seen` and
  provenance, so a second noun would name a difference the sheets already show.
  The 1 800 lines of page/bank/fold/sidecar/`--verify` machinery would also be
  duplicated and then drift, which is the failure mode `--verify` exists to
  catch.
- **Leave §1 alone and mark the static output as "not a kit"** (e.g. call it
  "ROM pages" and keep the kit defined as recording-only). This is the same
  argument one step further, and it fails the same way: the folder, its
  `ARTIST.md`, its round-trip path and its consumers are identical, so the
  rename is vocabulary the reader has to learn and nothing else. §1's heading —
  "a kit is a projection, never a second source of truth" — is already the
  property the static case preserves, and rewriting the noun would obscure that
  it does.
- **Record once per ROM, automatically, so no static path is needed** (F12.10's
  shape). Refused as a substitute: it converts a seconds-long, ROM-only
  projection into an emulator run per game, which is exactly the bottleneck the
  artist evidence names, and it still needs a route or fallback per ROM. F12.10
  is the complement of this slice — a recording that happens without a human at
  the controller — not its replacement, and its own path (d) now depends on
  this.
- **Extend the static path to CHR RAM games now.** Refused, per §2.4 and
  ADR-0210 §2: with no recorded tiles there is no PRG block to pin, so a static
  CHR RAM page would be invented rather than read. ADR-0210 §3's third-party
  index (F12.12) is the only static source of shape for those 7 games, and it
  needs a recording to compare against.

## Contradiction found while writing this

**ADR-0210 §2 contradicts itself, and the static path rides on the corrected
half.** §2's last line reads "The residue is the palette: a shape pulled
straight from CHR has no colours attached. Source 3 supplies them." — which
makes source 2 *not* self-sufficient and would imply a static kit needs a
third-party index. ADR-0210's own later section, "The palette question is
already answered — `defaultTile` is the wildcard", retracts exactly that
("So source 2 is **already self-sufficient**") and the retraction is also
recorded in its Date line ("the 'what stays open' claim is retracted"). This
ADR reads §2 as the corrected section reads it. It does not amend ADR-0210 —
the file already carries its own correction — but a reader who stops at §2 will
conclude the opposite, and the F12.9 slice's stop condition 2 ("all `Y`")
depends on the corrected reading. Worth folding into ADR-0210's §2 the next
time that file is touched.

No other contradiction was found. F12.9's deliverable, its bounded input and
ADR-0210 §2's 23-of-30 split agree; the one place the PRD reads as if the tool
had a play session — stop condition (3)'s `headless_record` screenshot — is the
acceptance test's evidence, not the generator's, and this ADR states that
separation explicitly rather than leaving it to be rediscovered.
