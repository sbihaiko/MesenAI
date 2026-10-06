# P.11 search by intent: held-out gate, per backend (2026-10-06, #922)

Slice **P.11** (ADR-0245 §4) wires the cheat search by intent into the W-P11
Cheats sheet. The #915 panel ruling (AGREED, option (b)) sets the gate:

- a backend is offered only when, **on its own numbers**, it scores
  **≥ 90 % correct and ≤ 5 % wrong entries**;
- the measurement must use a **held-out intent set** written by someone other
  than the script's author, kept out of whatever the search was tuned on;
- if Jev fails the held-out set, P.11 is not adopted.

This log is that measurement. It replaces the 2026-10-05 draft log
(`p11-heldout-gate-2026-10-05.md`), which was never committed to `main`.

## Headline

Held-out set: **121 cases over 27 NES games, 15 expecting `NONE`, 4 in pt-BR**.

| Backend | Correct | Wrong entries | Discarded | Gate (≥ 90 % correct, ≤ 5 % wrong) | Offered |
|---|---|---|---|---|---|
| `jev` (`typesafe/jev-1.13`) | **120/121 = 99.17 %** | **1/121 = 0.83 %** | 0 | **passes** | **yes** |
| `ollama` `qwen2.5:7b-instruct` | QWEN_CORRECT | QWEN_WRONG | QWEN_DISCARDED | QWEN_GATE | no |

Jev: 105/106 when an entry was expected, 15/15 when `NONE` was expected; median
latency 0.516 s, max 1.495 s; total cost US$ 0.00466 under a US$ 0.10 cap.

The one Jev miss: Pac-Man (USA) (Namco), *"não deixe o Pac-Man morrer"*
(pt-BR, "don't let Pac-Man die"), answered `E8` *Infinite lives* where the set
accepts only `E3` *Invincibility*. That counts as a wrong entry: the set's rule
is that with infinite lives you still die.

## The held-out set

`tests/fixtures/cheat-intent/heldout-intents.json`, landed here from commit
`a8585f3b7` (branch `feat/p11-adopt-gate`, 2026-10-05). Reviewed against the
ruling before reuse:

- **Independent author, held out: holds.** The set was written in a separate
  session that was forbidden to open the original fixture
  (`tests/fixtures/cheat-intent/intents.json`), the scorer's internals and the
  2026-10-02 log; its only input was the cheat descriptions in
  `CheatDb.Nes.json`. `scripts/cheat_intent.py` was not changed after the
  2026-10-02 measurement (same sha256 below), so nothing was tuned on this set.
- **Not near-literal: holds.** Intents are paraphrased with colloquial player
  phrasings ("I keep dying the moment a Goomba touches me").
- **`NONE` spelling: corrected.** Every expected-`NONE` case uses `["NONE"]`,
  the token `scripts/cheat_intent_eval.py` scores; the fixture's `how_chosen`
  text still said `accept: []`, which the 2026-10-05 amendment to ADR-0245
  flagged as stale. That sentence is fixed in this change; the cases are
  untouched.

## Commands

```bash
# Jev. The key comes from the environment only (see "Key custody").
python3 scripts/cheat_intent_eval.py --backend jev \
  --cases tests/fixtures/cheat-intent/heldout-intents.json \
  --budget 0.10 --out /tmp/p11-922/jev.json                 # exit 0

# Local model, same set.
python3 scripts/cheat_intent_eval.py --backend ollama --model qwen2.5:7b-instruct \
  --cases tests/fixtures/cheat-intent/heldout-intents.json \
  --out /tmp/p11-922/qwen.json                              # exit QWEN_EXIT
```

Per-run JSON stays outside the repository.

## Pinned inputs (sha256, first 16 hex)

| File | sha256[0:16] |
|---|---|
| `tests/fixtures/cheat-intent/heldout-intents.json` (as run; before the `how_chosen` text fix) | `22b07f30b57fc0f5` |
| `UI/Dependencies/Internal/CheatDb.Nes.json` | `93e166c75238e9fa` |
| `scripts/cheat_intent.py` | `ab61faa36e80ac21` |
| `scripts/jev_client.py` | `7c755b4fdbecac6c` |

These match the 2026-10-05 draft's pins, and its Jev figures (120/121, one
wrong entry, the same Pac-Man case) are reproduced exactly.

## Key custody during this run

No OpenRouter key was stored in the OS credential store on the measuring
machine (`MesenAI BYOK openrouter` absent). The run therefore took the key from
the gitignored `.env` of the main checkout, read by a one-line Python
expression straight into `OPENROUTER_API_KEY` in the eval's environment. It was
never printed, never on a command line, never written to a file, and this
worktree has no `.env`, so `jev_client.py`'s repo-root fallback was not used.
This is the operator's measurement path, not the client's: the client reads the
key from the OS credential store only (`CheatIntentScriptRunner`,
`ByokJobLauncher`) and starts no child when none is stored.

## Verdict

Jev passes the held-out gate on both bars and is the only backend the sheet
offers (`CheatIntentSearch.OfferedBackends`). QWEN_VERDICT
