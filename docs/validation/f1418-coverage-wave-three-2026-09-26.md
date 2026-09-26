# Coverage past the first stage, wave three: five more games on the ADR-0239 selector sweep (2026-09-26)

**Date:** 2026-09-26
**Binary under test:** the tree at `91d8da35f` (the `main` this branch grew from;
the slice itself adds `scripts/stages/` and `docs/` and **no Core file**), and
the Core dylib the recorder loads by absolute path,
`/Users/bihaiko/VSCodeProjects/MesenCE/InteropDLL/obj.osx-arm64/MesenCore.dylib`,
built at **12:55:45** and **not rebuilt again**. Wave two's log has a section on
a dylib replaced *while that wave was recording*; this one does not, and the
difference is checkable rather than promised: every pack on both sides of all
five games carries `<bgCellRecord>`, the marker only the current recorder
writes — the baseline packs and the session packs were written by the same
binary.
**ADR:** `docs/adr/0239-coverage-past-the-first-stage-comes-from-a-selector-swept-per-game-and-is-measured-as-a-union.md`
**PRD slice:** F14.18 (Part A).
**Raw material:** unversioned, under `runs/f1418/<game>/` — `RESULT.md` (the
authority for every figure below, with its own reproduction block),
`summary.json`, `before/` (the baseline recording), `sweep/` (one directory per
session, holding the bootstrap pack the recorder wrote, the final `.mss` the
check is read off and the recorder's own log), the sweep and rescore logs, and
`probe/` (the exploratory recordings). `runs/f1418/<game>/rescore/` is a
directory of symlinks into `sweep/`, used so a `--rescore` writes `rescore.json`
beside the record instead of over it.

## Headline

Four games that had no set at all now have one, and the fifth — Zelda II, whose
`stage-set.json` was already declared — gets the profile it was missing. Every
figure is the ADR-0239 §5 union of the game's own stage-1 route with its
sessions.

| game | rung | selector | values | sessions | keys (§5) | tile data | ROM CHR (§5.2) |
|---|---|---:|---:|---:|---:|---:|---:|
| Bubble Bobble | 1 | the game's password field | 16 | 16 | 374 → **850** | 344 → **674** | 318/1141 27.9 % → **526/1141 46.1 %** |
| Double Dragon | 2 | `$003D` | 4 | 4 | 927 → **1945** | 732 → **1660** | 667/5796 11.5 % → **1322/5796 22.8 %** |
| The Flintstones | 1 | hidden 14-press debug level select | 37 | 37 | 2382 → **17389** | 715 → **4747** | 632/8773 7.2 % → **3891/8773 44.4 %** |
| Life Force | 2 | `$0030` | 5 | 5 | 296 → **787** | 273 → **704** | CHR RAM — no denominator |
| Zelda II | 2 | `$0748` | 36 | 36 | 281 → **1761** | 190 → **1112** | 186/1616 11.5 % → **590/1616 36.5 %** |

**98 sessions of 120 emulated seconds each, about 3.3 emulated hours.** Two are
`did-not-warp`, and each is the identity value of its own game: Double Dragon's
`city-slum` (whose final state differs from `before` in **0 of 2048 bytes**, so
the gate is right and the value is the same place) and The Flintstones' `room00`,
which *is* the baseline session. `93 of the 98` values are proven by the
session's own `ramCheck`; the five that are not, and why, are in "The gate and
the proof are different instruments" below — they are a measurement, not a
failure that was tuned away.

## What each profile does

### Bubble Bobble — rung 1, the game's own password field

Sixteen values, no cheat anywhere in the chain: the entry script types a
published password into the game's own screen. What the sweep had to measure,
because no source says it:

- The field is **cursor-addressed**, not concatenated. `RIGHT`/`LEFT` move a
  cursor across the five slots (`$FE8D` / `$FE71`, both saturating), while
  `DOWN`/`UP` increment and decrement the letter *in the current slot*
  (`$FE61` / `$FE53`) and both stick — eleven `DOWN`s stop at `J`, and
  `DOWN`+`UP` returns to `A`; `A`→`J` does not cycle. `LEFT` in the first slot
  is refused.
- The acceptance matrix destroys arithmetic as a test: of ten `AAAA?` codes
  (`?` = `A`..`J`) **only `AAAAB` is accepted** (it arms round 16), while
  `AAAAA` computes 16 and is refused. No password in the profile is
  synthesised; all are published.
- Read-back is taken from the nametable mirror, **not** from `$0502`–`$0506`:
  the accepted branch consumes the array, so a session whose array closes as
  `AABAB` has `BBAAB` on screen.
- The three GameFAQs codes containing `H` (`C6 GHCCB`, `E6 HBGBD`, `F5 HJFAB`)
  pass the game's validator and arm rounds 126/146/155, but the game *draws*
  60/80/60 — outside the last round the lookup lands elsewhere, and two land on
  the same round, so `new` would read 0 for a value that is not the same place.
  They are measured, excluded, and the measurement is in `RESULT.md`.

Two of the sixteen are the two rounds the game's own `SUPER` modes start on.
No artist pack for this ROM is on disk, so §5.3 has no line.

### Double Dragon — rung 2, `$003D`

Four values, the four stages the published table names. The interesting part is
which byte can be the proof: `$003D` reads `00/01/02/**04**` in the final
states — the `04` is the counter having run past its table, which is exactly why
a pinned byte cannot be its own `ramCheck`. The check is `$0018`
(`5C/65/7D/E1`), a byte the game writes once the pin has taken effect. The
identity value `city-slum` is in the set on purpose: it is the control, and it
is the one `did-not-warp`.

### The Flintstones — rung 1, the hidden debug level select

Thirty-seven values: the game's fourteen-press title-screen code, then a
two-digit number chosen with `LEFT`/`RIGHT` and started with `START` (TCRF,
crediting GameFAQs and Rachel Mae). The number-to-room map is **not published
anywhere**, so the sweep measured it: `$03CE` is the number dialled, `$05FB` is
the phase id, `$0303` is the id the stage data gives the room. The counter wraps
at **100**, but the map ends at **37** (00..36) — 37 reads phase `22`=34, 38
reads `13`, 40 reads `0C`, 59 reads `08`, 63 reads `00`, 160 reads `13`. The two
independent readings agree on the same pairs, and the two closest screens are
32~39 and 34~37, exactly the two the byte sends back to a room already covered.

### Life Force — rung 2, `$0030`

Five values (the sixth, `00`, is the game's stage 1 and is the baseline side of
the union). Two published sources disagree about how much they know, and the
profile records both rather than smoothing them over: DataCrystal lists the byte
without claiming its values, Almar's Guides publishes the value table verbatim.
The check is `$000F` (`02/04/06/08/0A`), and in every final state **`$0030`
reads 0** — the game clears its own selector once the pin has taken effect,
which is the cleanest statement of the rule that the check and the pin are
different instruments.

The dump is a CHR RAM cartridge — the iNES header declares 0 CHR banks, so there
is no fixed pattern set to be a denominator — and §5.2 reports `n/a` on both
sides. §5.1 is the only coverage figure this game gets.

### Zelda II — rung 2, `$0748`

Thirty-six values on the already-declared set. Two things the wave did not
expect to find:

- **`$056C = ($0748 − $34) & $FF`** (DataCrystal) measured **8/8** in the final
  states. That retired five `$0707` expectations (kings-tomb and four towns)
  that had failed on the first pass. Those five now read a published
  instrument; the other 27 read `$0707` with the limit documented.
- The **§5.3 reference line exists here** and nowhere else in the wave: the
  artist pack on disk declares `<ver>108` against the compared packs' `<ver>109`
  — two dialects of the same identity — and scored at the CHR pattern both
  sides share the sweep union is 576 patterns, **222 of 566 (39.2 %)**, against
  a baseline of 114 (20.1 %); union with the baseline 228 (40.3 %).

## The gate and the proof are different instruments

ADR-0239 §4's `new` count says a session added nothing; it does not say a
session arrived, because a selector screen draws art of its own. Three findings
this wave sharpen that, and all three are measurements:

1. **The Flintstones: five of thirty-seven.** Rooms 22, 27, 32, 33 and 34 do not
   read their own id in the final state — `$0303` reads `14` for 22, `1A` for 27,
   `01` for 32, `02` for 33 and `0B` for 34. The worker investigated instead of
   adjusting the `expect`: one second after the load all five read the id
   correctly, a pass of the body already moves 32/33/34, and eleven seconds with
   **no input at all** move none of them. All five were re-recorded with a body
   that stands still and all five still departed (32 reads `20` at 30 s and `01`
   at 60 s). The room ends on its own inside the 120 s, so the check was left
   asserting the independent byte and the five departures are **reported rather
   than tuned away** — an expected byte copied back from a session's own output
   would prove nothing. Those five rows rest on the identity established by the
   number-to-room map, and the log says so.
2. **`new` is a threshold, not a measure.** Zelda II was recorded twice on the
   same binary: final RAM was identical in **34 of 36** sessions, but `new` was
   identical in only **4 of 36** (delta −33..+14, with the state byte-for-byte
   equal). Two sessions can differ in `new` while having arrived at the same
   place, which is the same lesson wave two's §4 amendment states from the other
   side. To claim a Core change moved something, quote the `ramCheck` or the
   pattern set, never `new`.
3. **A pinned byte cannot always be its own check.** Double Dragon's `$003D`
   reads `04` in one session because the counter ran past its table. The check
   belongs on the byte the game writes once the pin has taken effect.

## The baseline must come from the same binary as the sessions

Every game's baseline was recorded in the same sitting as its sessions, and the
dylib did not change between them: `grep -l bgCellRecord` finds the marker in
**1/1, 1/1, 1/1, 2/2 and 1/1** baseline packs and in **16/16, 4/4, 37/37, 5/5 and
36/36** session packs. An earlier read of this same check said 0/5 on the
baseline side; that was the check looking one directory too high (the baseline
pack lives at `before/by-stage/<name>/<rom>/auto/textures/hires.txt`, not at
`before/<name>/`), not a rebuild.

## What the sweep does not reach

- **No stage-clear transition, in any of the five.** Every session is 120
  emulated seconds of a body that does not clear a stage, so the transition
  itself is never recorded (unchanged from waves one and two).
- **No boss, no mid-stage room a selector does not name.** The selectors reach
  the places the game's own menus list; anything past them is §1 rung 3 work.
- **Second player:** none. The sweep drives port 1, even where the game is
  two-player simultaneous (Life Force), so no second-player art is claimed.
- **The Flintstones' five unproven values** (above) and **Double Dragon's four
  stages only** — the game's later stages are reachable through the ROM's own
  stage select, but the published table this profile is built from names four.
- **Bubble Bobble's three rejected `H` codes** and the two rooms they collapse
  into are measured and left out, not silently absent.

## Caveats that belong to the numbers, not to the method

- **ROM CHR is 100 % of the ROM's patterns by definition**; `romChrSeen` is the
  column that moves, and the two are easy to confuse in a summary.
- **The §5.1 union counts drawn keys**, placeholders excluded, so a game whose
  selector redraws the same tileset gains keys and little tile data. The
  Flintstones' `+15007` keys against `+4032` tile data is that shape; Zelda II's
  `+1480 / +922` is a milder one.
- **Five Zelda II pins produce byte-identical packs** (`cave-bats-1`,
  `cave-bats-2`, `rocky-crabs`, `mountain-maze`, `swamp`): the union counts them
  once, and their `new` values are not five independent measurements.
- **A `--rescore` nulls every `ramCheck` by construction** — it reads no state
  and cannot re-derive the check. The shipped `summary.json` of each game is the
  recording's, and the one whose `romChr` block carries `pct: null` (Life Force)
  is `n/a (CHR RAM)`, not a missing figure.
- **`--rom-chr` requires `--rom`**, so a rescore block that omits the ROM will
  not run; and a rescore handed `--out` without `--summary` writes
  `<out>/rescore.json`, not `sweep.json`.

## Defects this slice found

- **A claim about a run is not a measurement of it.** Double Dragon's `RESULT.md`
  said "452 runs, 17 871 s"; reading the 453 `mesen.log` files gives **453 runs,
  18 173 s ≈ 5.05 h**, and the frame total the same line quoted was lower
  because only 15 of the 453 logs record a frame count. Corrected in place.
- **"in every run's `mesen.log`" was false.** The loader's
  `matches ROM sha1 …` line is emitted in **438 of 453** runs; the 15 exceptions
  are exactly the bootstrap runs that load a copy of the ROM (the baseline, the
  probe bodies, the sessions and the archived incomplete baseline) — the same
  mechanism the decline reports, seen from the other side.
- **A `Reproducing` block that would not have run.** Wave two's shape omits
  `--rom` from the rescore while passing `--rom-chr`, which requires it, and
  states that a rescore writes `sweep.json` (it writes `rescore.json`). Fixed in
  this slice's blocks.
- **Three sets would have recorded nothing** had the naming rule not been
  enforced before this wave started: wave two's `bottle-run.txt`,
  `mountain-run.txt` and `stage1-run.txt` named no mint their folder ships, and
  `library_job.start_plan` prunes such a route. The guard that catches it
  (`at least one route the library job can start`) is in `scripts/test_library_job.py`
  and is green on all 21 declared sets here.

## What the sweep cost

98 sessions of 120 emulated seconds, plus one baseline recording per game.
Measured from the files on disk, the recording windows sum to about **61 minutes
of wall clock** (≈37 s per session, five sweeps partly concurrent at `--jobs 1`):
The Flintstones ≈ 24 min for 37 sessions, Zelda II ≈ 20 min for 36, Bubble
Bobble ≈ 6 min for 16, Life Force ≈ 8 min for 5, Double Dragon ≈ 3 min for 4.
`--rescore` re-derived every figure from the packs on disk with no ROM, no
emulator and no state — which is why the numbers above were recomputed rather
than copied from the first pass.

## Reproducing

Each `runs/f1418/<game>/RESULT.md` carries its own block. The shape is two
commands, the first after a `--dry-run` that writes nothing:

```sh
python3 scripts/record_navigation_sweep.py \
  --profile scripts/stages/<game>/navigation.json \
  --rom "<library>/<game>.nes" \
  --out runs/f1418/<game>/sweep --baseline runs/f1418/<game>/before \
  --rom-chr --summary runs/f1418/<game>/summary.json
python3 scripts/record_navigation_sweep.py --rescore --out runs/f1418/<game>/sweep \
  --profile scripts/stages/<game>/navigation.json --rom "<library>/<game>.nes" \
  --baseline runs/f1418/<game>/before --rom-chr \
  --summary runs/f1418/<game>/summary.json
```

The ROM library is the user's own and is never copied into the repository. The
five profiles, their entry/body scripts and their stage sets are versioned under
`scripts/stages/<game>/`; `runs/f1418/` is not.
