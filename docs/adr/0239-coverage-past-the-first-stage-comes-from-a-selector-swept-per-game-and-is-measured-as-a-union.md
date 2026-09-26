# ADR-0239: Coverage past the first stage comes from a selector swept per game, and is measured as a union against the ROM's own tiles

- Status: accepted (2026-09-26). The user accepted the text before it was
  written and asked for it to be run, verbatim: *"escreva a ADR sobre ampliar
  a cobertura além do começo da fase, eu aceito sua escrita. execute depois
  nos jogos que testamos mais profundamente e me traga uma tabela de
  resultados com o antes e o depois."* Same-turn implementation is allowed by
  CLAUDE.md only when the change ships with unit tests covering the decision
  and this go-ahead is quoted here and in the PR body. It is PRD Part A
  slice F14.16.
- Date: 2026-09-26
- Related: ADR-0182 §3 (a later stage is played only when a measurement
  names the subject), ADR-0184 (RAM-only cheats; the 2026-09-14 navigation
  amendment), ADR-0185 (a published movie is input), ADR-0194 §4 (coverage
  is the union metric), ADR-0210 (where sheet coverage comes from), ADR-0238
  (the step-mode search and Jev, verdict "do not adopt beyond the spike")
- Supersedes / amends: amends ADR-0184 §3 (a lives pin may ride with a
  navigation pin, §3 below)

## Context

Every deep measurement of 2026-09-24/25 carries the same caveat. The routes
pace inside the start of the first level, so every coverage figure covers
that slice and not the game:

- SMB3 never leaves the first screen and a bit of World 1-1 (358 drawn keys).
- Ninja Gaiden stays in the first ~1.5 screens of Act 1-1 (183).
- Excitebike, Castlevania and Punch-Out!! each record one track, one stage
  block or one fight.

The kit's percentages are high (90–100 % of drawn keys land on a sheet cell),
but they are percentages of a small set. The later stages, their enemies,
bosses and palettes exist only in the static ROM export.

Two ways to go further were tried and measured:

- **Play there** with search and chains (F9.22 on Contra, F14.13 on Ninja
  Gaiden) or with Jev at the stalls (ADR-0238). It works one screen at a
  time, costs hours per stage, and F14.15 measured its yield: 0 keys over
  the existing packs.
- **Warp there** with Contra's published level byte `$0030`, pinned for a run
  (ADR-0184, amended 2026-09-14). Eleven sessions and about an hour covered
  58.9 % of the reference pack against 53.8 % for 77 blind recordings, with
  367 tiles the archive never held. `scripts/record_navigation_sweep.py`
  turned that into one command driven by `navigation.json`, but no second
  game ever got a profile.

The warp won on yield by an order of magnitude. This ADR is the measurement
ADR-0182 §3 asks for: it names the subject (every stage of the five deeply
measured games) and the tooling.

Non-goals:

- Beating a game.
- Recording the stage-clear transition. A pinned selector never shows it
  (ADR-0184 amendment); a route that reaches the end of a stage still does.
- Merging kit surfaces across recordings (ADR-0194 §1 stands).

## Decision

### 1. The driver ladder, in order

For each place past the first stage, use the first rung that reaches it:

1. **The game's own selector, by input:** a menu, a track or round choice, a
   password, a continue code. This is a clean pass that feeds all four
   surfaces, like the Konami code (ADR-0184 §3 rank 1).
2. **A published RAM selector, pinned for the run:** a navigation pass
   (ADR-0184 amendment), all four surfaces, `notes[]` obligatory. The address
   and every value must come from a published RAM map or the vendored cheat
   database. None is invented (ADR-0184 §5).
3. **A save state reached by search, chain or movie,** for a room no
   selector reaches (bosses, mid-stage areas), under the existing rules:
   F9.22 chains, ADR-0185 movies, ADR-0238 search.

The sweep for this slice uses rungs 1 and 2 only. Rung 3 stays per-room work
that a later measurement has to name.

### 2. The profile grows the three things rung 1 and the lives pin need

`navigation.json` keeps its shape; all additions are optional and backward
compatible:

```json
{
  "navigation": { "kind": "input", "label": "track select", "source": "<manual/page>",
                  "values": [ { "name": "track3", "title": "Track 3", "entry": "mint-track3.txt" } ] },
  "defaults": { "seconds": 120, "entry": "mint-stage1.txt", "body": "stage1-run.txt",
                "cheats": [ { "code": "002A:09", "label": "lives", "source": "<published map>" } ] }
}
```

- **`navigation.kind`** is `"ram"` (the default and today's meaning:
  `address` is required, and each value becomes the pinned cheat
  `address:value`) or `"input"`. With `"input"`, `address` is absent, the
  value carries no cheat, and its `entry` script is what selects the place.
- **`values[].entry` / `values[].body`** override the defaults per value.
- **`defaults.cheats` / `values[].cheats`** are extra RAM-only pins, each with
  a `source`. Each is validated by ADR-0184 §1 before any process starts,
  like the selector itself.

### 3. A lives pin may ride with a navigation pin (amends ADR-0184 §3)

Pinning a lives counter the HUD renders (§3 rank 2) keeps a blind body on
the stage instead of recording GAME OVER. It changes only HUD digits the
game draws anyway, so it does not demote a navigation pass. It holds only
when the profile's inert control passes: the pin at the counter's start
value, on the stage-1 value, records a byte-identical `textures/` tree to the
same run without it, the same test ADR-0184's amendment ran for `$0030`.
Deaths and respawns are still recorded, so ADR-0184 §4 is still met; only
the GAME OVER screen is foreclosed, and each game's unpinned stage-1 route
still records it. An invulnerability timer is never allowed in a sweep:
it is drawn (§3 rank 3).

### 4. A session must prove it went somewhere

A session that did not warp records the stage-1 or title material under
another stage's name, and nothing else notices. So the sweep reports two
counts per session, both in drawn keys, the `(tileData, palette)` cell
identity of ADR-0194 §2. Neither count includes the builder's `defaultTile`
placeholder rules: a bootstrapped CHR ROM pack carries one for every CHR
index, so every pack of one ROM shares them.

- **`new`**: keys the baseline packs do not hold. A navigation session with
  `new == 0` is **`did-not-warp`** and adds nothing to the union. This is the
  behavioural test ADR-0185 §4 applies to a movie.
- **`unique`**: keys no other session and no baseline holds. It is reported
  for reading, not gating. Two tracks that share a tileset are both
  legitimate warps with `unique == 0`, so gating on it would drop both.
  *(Corrected 2026-09-26, the same day: the first text gated on `unique` and
  counted tile data, which is structurally 0 on a CHR ROM game.)*

When the profile names one, a RAM check read off the session's final state
is also reported. Reading the selector's own address proves nothing: a
pinned cheat substitutes the byte on the CPU read path, and the save state
holds the unpinned byte. So the check reads a different variable the stage
sets.

### 5. The metric is ADR-0194 §4's union, against two denominators

**Before** is the game's current stage-1 route, re-recorded on the same
binary. **After** is the union of that pack and every sweep session. Each
is reported as:

1. **Drawn keys and distinct tile data** of the union.
2. **ROM CHR coverage,** for a CHR ROM game: the distinct non-blank 16-byte
   patterns the union's recorded (non-placeholder) `<tile>` rules name, over the distinct non-blank
   patterns in the ROM's CHR. It needs no third-party pack, so it exists for
   every game. A CHR RAM game has no fixed denominator and reports the
   reference line only.
3. **Reference coverage,** when a reference pack for the same ROM is on disk:
   `artist_cover`'s unit, the fraction of its distinct tile data the union
   holds. A pack keyed for a patched ROM is refused (#225), not printed as 0 %.

`record_navigation_sweep.py` prints items 1–2 with `--rom-chr` and folds a
baseline pack into the union with `--baseline <pack dir>`. `--reference` is
unchanged.

### 6. Scope and budget

- **Games:** the five deep-measured ones: Excitebike, Castlevania,
  Mike Tyson's Punch-Out!!, Super Mario Bros. 3 and Ninja Gaiden.
- **Stages:** every stage (track, fight, world) rung 1 or 2 reaches, one
  session each.
- **Budget:** 120 emulated seconds per session, with the body repeated to
  fill the run as Contra's profile does. That is the recorder's usual
  two-minute budget; ADR-0184 used 300 s runs.
- **Out of scope:** Contra already has its sweep, and a sixth game is a
  profile, not a code change.
- **Evidence:** the results table with before and after goes to
  `docs/validation/f1416-coverage-sweep-2026-09-26.md`.

## Consequences

- **Each game gains a profile.** The addresses come from published maps, so
  a profile is only as good as its source. A wrong address shows up as
  `did-not-warp`, or as a crash screen that §4's `new` count does not catch,
  which is why each profile also carries a RAM check where the map offers
  one.
- **Stage-clear transitions stay missing** from every warped session
  (ADR-0184 amendment). The union metric makes that invisible, so the
  results log must say it.
- **The kit is unchanged.** One recording gives one kit (ADR-0194). A warped
  stage gets its own kit, and only the pattern pages
  (`artist_chr_kit.py --also`) and the coverage number see the union.
- **ADR-0238's search remains the tool for rung 3,** and its verdict is
  untouched.
- **A lives pin is now part of a navigation pass's provenance.** It goes into
  `notes[]` verbatim, with its address, like the selector.
