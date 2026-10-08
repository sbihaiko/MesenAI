# F14.20 part 1: the AI recorder's adoption measurement on the F14.19 games (ADR-0242 Q3)

- Date: 2026-10-02
- Slice: PRD Part A §4 Phase 14, F14.20, part 1 (the adoption gate and the key
  custody interface; the W-R8 button itself waits for slice G.3). Go-ahead
  verbatim (user, 2026-10-02): *"sim, pode seguir. depois que tudo estiver no
  main, pode implementar usando paralelismo de tudo que puder"*, *"pode
  implementar em paralelo tudo que puder"* and *"pode seguir com a segunda leva
  em paralelo"*.
- Worktree: `~/wt-f1420`, branch `feat/f1420-ai-recorder-gate`, based on
  `f30644566`. Build: `make core` + `make capture-tool` in the worktree;
  `scripts/headless_record` sha256 `e10a80e3880c0651…`,
  `InteropDLL/obj.osx-arm64/MesenCore.dylib` sha256 `6ffbfb237c8e24a6…`.
- Model: `typesafe/jev-1.13` through OpenRouter, key from `OPENROUTER_API_KEY`
  (environment, read from the maintainer's gitignored `.env`). Web research is
  off in every arm (`--max-research-passes 0`): ADR-0242 keeps the `claude -p`
  worker out of the GUI path, so the GUI's recorder is measured without it.
- Scratch: `runs/f1420/` (unversioned, like every `.mss` and recording it
  holds). The ROMs are hard links in that folder, so nothing was written beside
  the library ROMs.

## 0. The criterion, as written

ADR-0242 Q3 adopts the GUI recorder only when **both** clauses of ADR-0238 §5
pass on the games Q2's maps cover — Castlevania and Mega Man 2 (F14.19):

1. **Clause 1** — Jev passes at least one stall the search alone could not,
   and the result replays deterministically.
2. **Clause 2** — the AI-driven recording adds kit keys (`(tileData,
   palette)` pairs of `hires.txt`) that no existing pack or committed route
   has.

This log applies that criterion unchanged. "On the F14.19 games" is read as on
**each** of the two games; see §5 for why that reading decides the verdict.

## 1. Start states

The committed mints idle into a state the search can steer
(`runs/f1420/mint.py`): the mint is played from power-on exactly as
`scripts/test_ram_maps.py` plays it, then a step-mode session holds the pad
idle for the frames the F14.19 map's first checkpoint idles, and saves.

| Game | mint | idle | start frame | why |
|---|---|---|---|---|
| Castlevania | `mint-stage1.txt`, 6 s | 400 f | 762 | the minted state ignores input for ~300 frames (F14.19) |
| Mega Man 2 | `mint-stage1.txt` (Bubble Man), 11 s | 700 f | 1362 | the READY flash |

## 2. Clause 1: stalls, with and without Jev

Every arm is `scripts/jev_harness.py` from the state above (`runs/f1420/arm.sh`).
An arm's search-alone twin runs the same macro table and the same knobs with
`--no-jev`, so "the search could not" is always measured under the same
configuration as the Jev arm it is compared with.

**Configuration B — the harness defaults** (15-frame macros, 1.5 s settle):

| arm | game | reason | stalls passed | decisions | final | emulated s | speed | Jev US$ |
|---|---|---|---|---|---|---|---|---|
| `A-cv-alone` | Castlevania | `no-jev` | — | 0 | room 0, abs x 751 | 121.13 | 3.72× | 0 |
| `B-cv-jev-1` | Castlevania | ladder exhausted | **0** | 15 | room 0, abs x 751 | 292.85 | 3.20× | 0.000425 |
| `B-cv-jev-2` | Castlevania | ladder exhausted | **0** | 14 | room 0, abs x 751 | 281.40 | 2.97× | 0.000393 |
| `A-mm2-alone` | Mega Man 2 | `no-jev` | — | 0 | abs x 325 | 52.18 | 3.07× | 0 |
| `B-mm2-jev-1` | Mega Man 2 | ladder exhausted | **0** | 13 | abs x 325 | 201.00 | 2.93× | 0.000372 |
| `B-mm2-jev-2` | Mega Man 2 | ladder exhausted | **0** | 13 | abs x 325 | 201.00 | 3.02× | 0.000372 |

("Ladder exhausted" is the harness's `research-cap`: every rung was asked and
research is off.) Both F14.19 stalls reproduce exactly — Castlevania's
courtyard wall at abs x 751 and Mega Man 2's first pit at abs x 325 — and Jev
passes neither, 0 of 2 each.

Why, measured with `runs/f1420/probe_cv.py`:

- **Castlevania x 751.** The castle door is at abs x 640, behind Simon: plain
  Right stops at 640 and the door's own walk-in takes Simon into the hall
  (room 1) about 120–240 frames later. The search's jumps carry him over 640 to
  the wall. From the stall, 60 frames of Left reach 640 and the door then
  fires on its own — but a 15-frame `LEFT` drops progress, the 1.5 s settle
  window lets the base search jump right again, and the attempt reads as "no
  progress". Jev chose `LEFT_15` once per arm, on the last rung.
- **Mega Man 2 x 325.** At the stall state Mega Man is already falling into
  the pit (y 235, the pit floor; every input from there loses the life). A
  15-frame `JUMP_RIGHT` holds A for 15 frames only, which is not a full jump;
  every rung's checkpoint at that screen still ends in the pit.

**Configuration C — 30-frame macros, 4 s settle** (`--macro-frames 30
--settle-seconds 4`, both existing harness flags; the macro table is the same
seven):

| arm | game | reason | stalls passed | decisions | loops | final | emulated s | speed | Jev US$ | `--verify` |
|---|---|---|---|---|---|---|---|---|---|---|
| `C-cv-alone-30` | Castlevania | `no-jev` | — (passes x 751 itself) | 0 | 0 | room 1, abs x 1519 (progress 5615) | 328.58 | 3.08× | 0 | — |
| `D-cv-jev-30-1` | Castlevania | ladder exhausted | **0** | 15 | 1 | room 1, abs x 1519 | 769.60 | 4.17× | 0.000429 | ok |
| `D-cv-jev-30-2` | Castlevania | ladder exhausted | **0** | 15 | 1 | room 1, abs x 1519 | 769.60 | 4.18× | 0.000429 | ok |
| `E-cv-alone-up` | Castlevania, + `UP`, `UP_RIGHT` | `no-jev` | — | 0 | 0 | room 1, abs x 1519 | 328.58 | 1.48× | 0 | — |
| `E-cv-jev-up-1` | Castlevania, + `UP`, `UP_RIGHT` | ladder exhausted | **0** | 13 | 2 | room 1, abs x 1519 | 710.80 | 1.50× | 0.000364 | ok |
| `E-cv-jev-up-2` | Castlevania, + `UP`, `UP_RIGHT` | ladder exhausted | **0** | 15 | 0 | room 1, abs x 1519 | 769.60 | 1.46× | 0.000429 | ok |
| `C-mm2-alone-30` | Mega Man 2 | `no-jev` | — (passes x 325 itself) | 0 | 0 | abs x 460 | 68.60 | 1.75× | 0 | — |
| `C-mm2-jev-30` | Mega Man 2 | ladder exhausted at the 3rd stall | **2** (x 460, x 594) | 32 | 3 | abs x 601 | 713.38 | 2.85× | 0.000926 | ok |
| `C-mm2-jev-30-2` | Mega Man 2 | ladder exhausted at the 3rd stall | **2** (x 460, x 594) | 32 | 3 | abs x 601 | 713.38 | 3.27× | 0.000926 | ok |

- **Mega Man 2: clause 1 is met.** With full-height jumps the search alone
  clears the first pit and stops at abs x 460. Jev passes that stall with
  `ATTACK_30` from the 5.2 s checkpoint on the 8 s rung, then a second stall at
  594 with `RIGHT_RUN_30` from the 7.2 s checkpoint on the 4 s rung, and stops
  at 601. The two repeats are identical decision for decision, and their
  744-frame scripts are byte-identical (`cmp`). `--verify` replays the script
  in a one-shot `headless_record` with no AI and matches abs x / lives / hp at
  frames 248, 496 and 744 (326/3/27, 563/3/26, 601/3/26). One repeat is 2.85×
  real time, under ADR-0238 §5's 3× speed target; the other is 3.27×.
- **Castlevania: clause 1 is not met.** With 30-frame macros the search alone
  walks through the door by itself, so x 751 is no longer a stall; its next
  stall is the hall's right end, room 1 abs x 1519, where the camera stops at
  1280. Jev passes it 0 of 2. Adding `UP` and `UP_RIGHT` to the table (the
  hall goes on up a staircase) changes nothing, 0 of 2 again, and the search
  alone with them stops at the same place. The reason is the map, not the
  model: `progress_x` is `room * 4096 + abs_x`, so climbing a staircase — which
  holds x or moves it left — never reads as progress, and no rung can reward
  the move that passes. Castlevania's map would need a y or stair field, or a
  progress that credits the climb, before this stall can be measured fairly.
  D's search-only script is byte-identical to `C-cv-alone-30`'s: in this
  configuration Jev never contributed a frame to the Castlevania artifact.

## 3. The ordinary recorder: `auto/rec-NNN/`, `source: ai`

Each produced script is replayed through the ordinary recorder
(`runs/f1420/record.sh`): `headless_record <rom> <s> <prefix> bootstrap
hdpack-off input=<script> state=<start>.mss recording-source=ai`, in its own
folder with a hard-linked ROM. `recording-source=` is new in this slice
(`scripts/headless_record.cpp`): ADR-0243 Q2's `project.json` defaulted an
input script to `script`, and an AI-produced script is `ai` (ADR-0242
Decision 3). Every AI replay landed as `<ROM name>/auto/rec-001/` with
`project.json` `"source": "ai"`; the committed routes and the search-alone
replays landed as `"source": "script"`.

| recording | script | from | length | `<tile>` rules |
|---|---|---|---|---|
| `mm2-ai-1`, `mm2-ai-2` | `C-mm2-jev-30` / `-30-2` route | Mega Man 2 start | 13 s | 7305 each, `hires.txt` byte-identical |
| `mm2-alone` | `C-mm2-alone-30` route | same | 13 s | 7237 |
| `mm2-alone-15f` | `A-mm2-alone` route | same | 13 s | 7210 |
| `mm2-route` | committed `megaman2/stage1-run.txt` | `mint-stage1` state | 120 s | 7784 |
| `cv-ai-1`, `cv-ai-2` | `D-cv-jev-30-1` / `-2` route | Castlevania start | 48 s | 3287 each, byte-identical |
| `cv-ai-15f` | `B-cv-jev-1` route | same | 18 s | 3049 |
| `cv-route` | committed `castlevania/stage1-run.txt` | `mint-stage1` state | 120 s | 3661 |

**Replay determinism:** the scripts replay with no AI; two replays of each AI
script give byte-identical `hires.txt` on both games, and `--verify` matched
every RAM checkpoint (§2).

## 4. Clause 2: keys no existing pack or committed route has

`scripts/kit_new_keys.py` (new, versioned, `scripts/test_kit_new_keys.py`) is
F14.15's `runs/f1415/cells.py` measurement made reproducible: per `hires.txt`
key `(tileData, palette)`, conditions ignored, CHR ROM indices normalized by
`<ver>`. The baselines are every pack for these two dumps on this machine:
the committed route recordings above, the search-alone recordings, the
library ROMs' own bootstrap packs (`<rom>/auto`), the community catalog pack
#143 installed beside the Castlevania ROM (`<rom>/mep`), and the F12.2 panel
sweep's recorded and painted packs (`runs/f12.2-sweep/…` in the maintainer's
checkout, read only).

```sh
python3 scripts/kit_new_keys.py \
  --candidate "mm2-ai=runs/f1420/rec/mm2-ai-1/rom/Mega Man 2 (1988) (Capcom)" \
  --baseline "mm2-route=…" --baseline "mm2-alone=…" --baseline "mm2-alone-15f=…" \
  --baseline "library-auto=<library>/Mega Man 2 (1988) (Capcom)/auto" \
  --baseline "sweep-auto=…" --baseline "sweep-mep=…" --baseline "sweep-pack=…"
```

**Mega Man 2** (all CHR RAM patterns):

| pack | role | rules | keys | keys no other pack has |
|---|---|---|---|---|
| `mm2-ai` | candidate | 7305 | 7060 | **30** |
| `mm2-route` (committed `stage1-run`, 120 s) | baseline | 7784 | 7489 | 298 |
| `mm2-alone` (search alone, same 13 s) | baseline | 7237 | 7030 | 0 |
| `mm2-alone-15f` | baseline | 7210 | 7023 | 0 |
| `library-auto` | baseline | 7706 | 7706 | 272 |
| `sweep-auto` | baseline | 9125 | 7881 | 492 |
| `sweep-mep` | baseline | 442 | 325 | 0 |
| `sweep-pack` | baseline | 441 | 324 | 0 |

Union of the baselines: 8912 keys. **The AI recording adds 30 keys none of
them has** — so clause 2 is met on Mega Man 2. The same-length search-alone
recording from the same state adds 0, which places the 30 in the stretch past
the stall Jev passed. Read precisely: all 30 are tile patterns some baseline
already has, under two sprite palettes (`FF0F2038` 17, `FF0F2C11` 13) that
never painted them before — 0 new tile patterns. The ADR's unit is the key,
which is what an artist repaints, so this is a pass on the criterion as
written; it is also as small as a pass can be.

**Castlevania** (all CHR RAM patterns):

| pack | role | rules | keys | keys no other pack has |
|---|---|---|---|---|
| `cv-ai` | candidate | 3287 | 2852 | **0** |
| `cv-ai-15f` | candidate | 3049 | 2711 | **0** |
| `cv-route` (committed `stage1-run`, 120 s) | baseline | 3661 | 2990 | 26 |
| `library-auto` | baseline | 3192 | 3192 | 7 |
| `library-mep-143` (community pack #143) | baseline | 7582 | 4936 | 4136 |
| `sweep-auto` | baseline | 3783 | 3209 | 7 |
| `sweep-mep` | baseline | 913 | 533 | 0 |
| `sweep-pack` | baseline | 912 | 532 | 0 |

Union of the baselines: 7619 keys. Neither AI recording adds a key — and
neither is AI-driven in substance, since Jev passed no Castlevania stall
(§2). Clause 2 is not met on Castlevania.

## 5. Verdict

| Game | clause 1 (stall passed, deterministic) | clause 2 (new keys) | both |
|---|---|---|---|
| Mega Man 2 | **met** — 2 stalls (x 460, x 594), 2 of 2 identical repeats, replay verified; only with 30-frame macros and a 4 s settle | **met** — 30 keys (0 new tile patterns) | **yes** |
| Castlevania | **not met** — 0 of 2 at x 751 (defaults), 0 of 2 at the hall's end (30-frame), 0 of 2 with stair macros | **not met** — 0 keys | **no** |

**The W-R8 button stays disabled.** ADR-0242 Q3 asks for both clauses on the
F14.19 games, and on Castlevania neither passes. The reason it shows: *the AI
recorder has not passed its adoption test yet (it passed on Mega Man 2, not on
Castlevania)*. This log does not lower the bar to one game; whether one game
is enough is a decision for the user (§7).

Two qualifiers on the Mega Man 2 pass, so it is not over-read:

- it needs `--macro-frames 30 --settle-seconds 4`; the defaults pass nothing
  on either game, so a GUI recorder would have to ship those knobs (or a
  per-game equivalent) to reproduce it;
- one of its two repeats runs at 2.85× real time, under ADR-0238 §5's 3×
  speed target (the target is not one of the two adoption clauses).

## 6. Spend and key hygiene

| line | US$ |
|---|---|
| Jev decisions, 10 arms, 177 decisions (`summary.json` `spend_usd`) | 0.005065 |
| web research | 0 (off in every arm) |
| **total** | **0.005065**, against the slice's US$ 2.00 cap |

The key was passed to every arm through the environment only. A scan of
everything under `runs/f1420/` (decision logs, summaries, scripts, recordings,
stdout/stderr) for the key and for a 20-character fragment of it found it in
**0 files** (the scan compares in memory and prints only a count).

## 7. Not done, and open

- **The W-R8 button and its sheet** are G.3's (the Remaster workspace); this
  slice ships the custody interface it will call (PRD row F14.20; `UI/Logic/
  ByokKeyStore*.cs`, `UI/Logic/ByokJobLauncher.cs`).
- **Needs a decision:** whether ADR-0242 Q3's "on the F14.19 games" means each
  game (this log's reading: the button stays disabled) or at least one (Mega
  Man 2 passes both clauses under configuration C).
- **Castlevania's map cannot reward a staircase.** A y or stair field, or a
  `progress` that credits the climb, would let the hall's end be measured; until
  then Castlevania cannot pass clause 1 by construction past x 1519.
- **Mega Man 2's `room` is still open** (F14.19); none of the stalls here
  crossed a ladder, so it did not matter in this pass.
- Adjacent finding, out of scope: `project.json`'s `durationSeconds` for a
  recording started from a save state is the console's frame counter at stop,
  not the recording's length — a 13 s replay from the frame-1362 Mega Man 2
  state reads 35.67 s, the 120 s committed route from its mint reads 131.02 s.

## 8. Commands

```sh
python3 runs/f1420/mint.py                                  # §1 start states
runs/f1420/arm.sh A-cv-alone castlevania --no-jev            # §2, one per row
runs/f1420/arm.sh C-mm2-jev-30 megaman2 --macro-frames 30 --settle-seconds 4 \
    --goal abs_x:1200 --max-stalls 4 --budget 0.10 --verify  # key in env only
runs/f1420/record.sh mm2-ai-1 megaman2 states/megaman2-start.mss \
    arms/C-mm2-jev-30/route.txt 13 ai                        # §3
python3 scripts/kit_new_keys.py --candidate … --baseline …   # §4
python3 scripts/test_kit_new_keys.py                         # the tool's tests
```

`arm.sh` always adds `--max-research-passes 0`. The drivers under
`runs/f1420/` are not versioned, like the states they mint.
