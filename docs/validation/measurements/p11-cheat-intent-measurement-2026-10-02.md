# P.11 — cheat search by intent: measurement (2026-10-02)

Slice P.11 (ADR-0245 §4, ADR-0247). ADR-0245 accepts the slice only on its
own numbers: *the share of intents answered with a correct entry on a fixed
intent set*. This log records those numbers. It does not adopt the slice;
that is the user's decision (see the end).

## What ran

- `scripts/cheat_intent.py` — the external script. It turns *this game's*
  `CheatDb.Nes.json` entries into a closed Choice (`E<index>` per entry, plus
  `NONE`), asks one backend, and keeps the answer only when it is exactly one
  of the offered names. Anything else is `discarded`. The description and code
  in its output are copied from the list, never from the model.
- `scripts/cheat_intent_eval.py` — runs the fixed intent set through one
  backend and scores it.
- Intent set: `tests/fixtures/cheat-intent/intents.json`. It has 65 cases over
  11 NES games: Super Mario Bros., Contra, Mega Man 2, The Legend of Zelda,
  Metroid, Castlevania, TMNT, Ninja Gaiden, Super Mario Bros. 3, Punch-Out!!
  and Kirby's Adventure. 55 cases expect an entry and 10 expect `NONE`. Two
  intents are pt-BR.

| Input | sha256 (first 16) |
|---|---|
| `tests/fixtures/cheat-intent/intents.json` | `85b7d2eb15b87e4b` |
| `UI/Dependencies/Internal/CheatDb.Nes.json` | `93e166c75238e9fa` |
| `scripts/cheat_intent.py` | `ab61faa36e80ac21` |

Backends, on an Apple M1 with 16 GB:

- **Jev** — `typesafe/jev-1.13`, served as snapshot `typesafe/jev-1.13-20260917`.
  It ran through OpenRouter's `alpha/decisions` Choice, via
  `scripts/jev_client.py`.
- **Ollama 0.33.3** — `qwen2.5:7b-instruct` (digest `845dbda0ea48`, pulled for
  this slice). Tool-free, temperature 0, seed 0, run two ways:
  - *constrained*: the output is held to a JSON schema whose `enum` is the
    option list (the script's default);
  - *loose*: plain `format: "json"`, to exercise the discard path.

### How the expected answers were chosen

The answers were written by hand from each game's descriptions alone, with no
play-testing. The rule is also stored in the fixture's `how_chosen` field:

- An entry is acceptable when its description, read literally, does what the
  intent asks.
- "don't die" accepts invincibility and infinite-health entries, but not
  infinite lives: with infinite lives you still die.
- "infinite X" accepts an entry that keeps X from running out, including a
  RAM write that pins X at a high value, but not a "… on pick-up" entry.
- "start on level/world N" accepts only the entry for that N.
- `NONE` is the only correct answer when no description does what was asked.
  Examples: a level select in a game whose list has none, ammo in a game
  without ammo, a request to invent a code.
- An entry and its "(alt)" twin are both acceptable.

`scripts/test_cheat_intent.py` checks that every expected id still names the
same description in the bundled list.

**Caveat.** The same agent wrote the set and the script, after reading the
descriptions. Most intents are near-literal, and a set written cold by someone
else would likely score lower. Treat these numbers as an upper bound for
phrasing this close to the list.

## Results

"Wrong entry" means the backend offered an entry that does something other
than what was asked. That is the harmful miss: the player would toggle the
wrong code. A wrong `NONE` only sends the player back to the sheet's text
search.

| Backend | Correct | Share correct | Entry expected | NONE expected | Wrong entry | Discarded (outside the list) | Latency median / max (s) | Cost (US$) |
|---|---|---|---|---|---|---|---|---|
| Jev, run 1 | 64/65 | **98.5 %** | 54/55 | 10/10 | 0 | 0 | 0.84 / 6.74 | 0.00305 |
| Jev, run 2 | 64/65 | **98.5 %** | 54/55 | 10/10 | 0 | 0 | 0.91 / 14.06 | 0.00305 |
| Ollama qwen2.5 7B, constrained (warm) | 55/65 | 84.6 % | 48/55 | 7/10 | 7 (10.8 %) | 0 | 1.15 / 2.61 | 0 |
| Ollama qwen2.5 7B, constrained (cold, first run) | 55/65 | 84.6 % | 48/55 | 7/10 | 7 | 0 | 7.49 / 15.93 | 0 |
| Ollama qwen2.5 7B, loose JSON | 54/65 | 83.1 % | 47/55 | 7/10 | 7 | 1 (1.5 %) | 1.15 / 2.58 | 0 |

Repeatability:

- The two Jev runs chose the same option in 65/65 cases.
- The two constrained Ollama runs also chose the same option in 65/65 cases.
- The cold Ollama run's latency includes loading the model, which happens on
  the first call after Ollama starts.

Jev's latency tail is the vendor's: one call took 14 s in run 2. A Choice over
a game's whole list costs about US$ 4.7e-05 on average and 7.07e-05 for the
largest list measured (Super Mario Bros. 3, 63 options).

Total OpenRouter spend for the slice: US$ 0.00617, against the US$ 0.50 cap.
That is one probe call (US$ 0.0000707) plus two full runs.

### Misses

- **Jev** (both runs):
  - Punch-Out!! "never get tired" → `NONE`; expected "Infinite hearts".
    In Punch-Out!! hearts are stamina, so the right answer needs game
    knowledge the description does not carry.
- **Ollama, constrained** (both runs identical):
  - SMB "stop the clock" → `NONE` (expected "Infinite time")
  - SMB "nao morrer" → `NONE` (pt-BR "don't die")
  - Contra "start on level 5" → "Start on level x" (wrong entry)
  - Contra "skip the level" → "Start on level x" (wrong entry)
  - Zelda "give me the magic sword" → "Have Wooden Sword" (wrong entry)
  - Metroid "start on world 5" → "Start with 70 energy" (wrong entry; `NONE` expected)
  - Castlevania "infinite hearts" → "Infinite health" (wrong entry)
  - Castlevania "start on level 5" → "Start on last level" (wrong entry; `NONE` expected)
  - Punch-Out!! "never get tired" → `NONE`
  - Punch-Out!! "infinite lives" → "Infinite hearts" (wrong entry; `NONE` expected)
- **Ollama, loose**: the same misses, plus Metroid "jump higher". The model
  answered with something outside the option list, the check discarded it,
  and nothing reached the output.

## Proposed threshold (not a decision)

ADR-0245 sets no threshold. The proposal, for the user to accept, change or
reject:

- **Share correct ≥ 90 %** on this set, **and wrong entries ≤ 5 %**.
- Each backend is judged on its own numbers.

The reasoning:

- The feature only saves the player a scroll or a text search, so a wrong
  `NONE` costs little.
- A wrong entry turns on a code that does something else. That is the failure
  that makes the feature worse than having no feature.
- Discards are not counted against a backend. The check working is the
  design, not a miss.

Against that proposal:

- **Jev passes:** 98.5 % correct, 0 wrong entries.
- **qwen2.5 7B on Ollama fails:** 84.6 % correct, 10.8 % wrong entries.

Whether a larger local model passes was not measured. A held-out set,
written by someone who did not write the script, was not run either.

## Reproduce

```bash
# Ollama (local; `ollama pull qwen2.5:7b-instruct` once)
python3 scripts/cheat_intent_eval.py --backend ollama --out runs/cheat-intent/ollama.json
python3 scripts/cheat_intent_eval.py --backend ollama --loose --out runs/cheat-intent/ollama-loose.json
# Jev (OPENROUTER_API_KEY in the environment or the repo-root .env)
python3 scripts/cheat_intent_eval.py --backend jev --budget 0.10 --out runs/cheat-intent/jev.json
# One request, as the sheet would start it
python3 scripts/cheat_intent.py --backend jev --sha1 7E54F2A2EE6AF721CB57BA4DC39C388B06533594 --intent "start on world 5"
```

The per-case results live under `runs/cheat-intent/`, which is gitignored.
The summaries above were rescored from those files after the `wrong_entry`
field was added to `cheat_intent_eval.summarize`; no backend was re-run for
that.

## Needs a decision

- **Adopt P.11 and wire it into W-P11?** If yes, also decide:
  - the threshold (the proposal above, or another);
  - which backends are offered: Jev only, or Ollama too with a stronger model,
    which would need its own run;
  - whether a held-out intent set has to pass first.

  The UI entry would start the script as a child process and pass the key
  only through its environment (ADR-0247), using the credential-store
  interface F14.20 builds.
