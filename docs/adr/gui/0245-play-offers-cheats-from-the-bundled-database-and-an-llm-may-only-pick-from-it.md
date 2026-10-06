# ADR-0245: Play offers cheats from the bundled database; Remaster allows RAM codes only, and an LLM may only pick from the list

- Status: accepted (2026-10-02). Requested by the user, verbatim: *"Sim, W-P11 + ADR"*, after asking whether cheats are worth loading, how to pick and identify them, and whether a (free) LLM could find codes; accepted the same day (*"Aceitar"*). Listed as slices **P.10** (phase 1: W-P11, the bundled list, the Remaster rule), **P.11** (search by intent) and **P.12** (checked web lookup) in PRD Part A §4, Phase 7; P.11 and P.12 are each adopted only on their own measurement (Consequences). Wireframe: PRD Part B §13, W-P11. **Implemented 2026-10-02 (P.10: §1–§3, §5)** on the user's go-ahead, verbatim: *"sim, pode seguir. depois que tudo estiver no main, pode implementar usando paralelismo de tudo que puder"* and *"pode implementar em paralelo tudo que puder"* (2026-10-02). The W-P11 sheet opens from today's player overlay (*Cheats · N on*) and writes the same `CheatCodes` list as the classic cheat window; the §3 rule is host-free (`UI/Logic/CheatRecordingRule`: a RAM code is `NesCustom` with every address below `0x0800`, as ADR-0184 §1 requires, so the 96 database entries writing above `0x07FF` are refused too) and is wired to a `recordingArt` flag that Play leaves false. **Remaster wiring implemented 2026-10-02** on the go-ahead *"acabe a implementação da nova GUI, garanta que tudo está na main, teste tudo que for possível"* (user, 2026-10-02): W-R2 has no overlay, so the recording-art context is Remaster active or a Remaster recording running (switching to Play does not stop it, PRD Part B §13.6), and there the sheet passes `true`; *Record While I Play* refuses to start while a cheat that is not a RAM code is on, naming it (ADR-0184 §1: refuse, not warn); while it records, `CheatCodes.ApplyCheats` holds back any such code turned on later — the classic cheat window included, whose UI is unchanged — the W-R2 strip names it, and Stop gives it back. Tested host-free in `UI.Tests/Cheats/CheatRecordingContextTests` and against the real core in `UI.HeadlessTests/RemasterCheatsTests`. **P.11 measured 2026-10-02, not adopted**, on the go-ahead *"pode seguir com a segunda leva em paralelo"* (2026-10-02) besides the two above: the external script `scripts/cheat_intent.py` (§4's closed Choice; an answer outside the listed entries is discarded) scored 64/65 (98.5 %) with Jev and 55/65 (84.6 %) with local `qwen2.5:7b-instruct` on a 65-case intent set, with 0 and 7 wrong entries (`docs/validation/measurements/p11-cheat-intent-measurement-2026-10-02.md`). This ADR sets no threshold; the log proposes ≥ 90 % correct and ≤ 5 % wrong entries, and adoption and the W-P11 wiring await the user's decision. P.12 is not implemented. **Amended 2026-10-03 (Play unlock, user's decision):** the `recordingArt` flag the Play sheet passes is false in Play even while Play's passive automatic bootstrap (the legacy *Record while I play* setting, ADR-0243 Q3) records; the bootstrap term added to it in #690 greyed out every non-RAM cheat (e.g. Contra *Start on level 5*). The user was asked and chose, verbatim: *"Liberar e pausar gravação (Recomendado)"* — in Play every cheat is switchable; turning on a code that is not a RAM code (`CheatRecordingRule.IsRamCode`) stops the automatic recording for the session (`CheatRecordingRule.PausesPassiveBootstrap`, `EmuApi.StopMepRecording` from `CheatCodes.ApplyCheats`; art already recorded is kept, and the core starts the passive bootstrap only at ROM load, so it does not come back until the next load), and a non-RAM code already on at load stops it right after the load. The lock stays only in Remaster (Remaster active, or a recording the user started there), where `CheatCodes.RecordingArt` still holds such codes back. Tests: `UI.Tests/Cheats/CheatRecordingContextTests`, `UI.HeadlessTests/RemasterCheatsTests`. **P.11 implemented 2026-10-06 (#922)** under the #915 panel ruling, verbatim: *"Adopt per backend, gated by the threshold. A backend is offered only when, on its own numbers, it scores ≥ 90 % correct and ≤ 5 % wrong entries. Today that means Jev only"*. The held-out set `tests/fixtures/cheat-intent/heldout-intents.json` (121 cases over 27 games, 15 expecting `NONE`, written cold by another session) now lives on `main`; on it Jev scored 120/121 (99.17 %) with 1 wrong entry (0.83 %) and `qwen2.5:7b-instruct` 100/121 (82.64 %) with 13 wrong entries (10.74 %), failing both bars, so Jev is the only backend offered (`docs/validation/measurements/p11-intent-search-2026-10-06.md`, which replaces the uncommitted 2026-10-05 log). W-P11 gains the search by intent: `UI/Logic/CheatIntentSearch` starts `scripts/cheat_intent.py` and accepts an answer only when it names a listed entry with the same text and code, the matched row is marked, otherwise a line says none matched. Key custody as the ruling fixes it (ADR-0247, ADR-0242 Q1): the user's own OpenRouter key, stored in and removable from the OS credential store from the sheet, read when a search starts and handed to the child through its environment only (`ByokJobLauncher`); no key stored means no child, so `jev_client.py`'s repo-root `.env` fallback is never the client's path. Tests: `UI.Tests/Cheats/CheatIntentSearchTests` (host-free, fake runner).
- Date: 2026-10-02
- Related: ADR-0184 (a recording may use a RAM-only cheat, never a PRG patch), ADR-0205 §4 (cheats are unrestricted in a shared replay and are recorded in the `.mmo`), ADR-0188 (an AI's judgement is a proposal, never evidence), ADR-0238 (Jev answers a closed Choice), ADR-0242 (the OS credential store holds BYOK keys), ADR-0128 (cheat type detection), ADR-0241 / PRD Part B §13 (W-P4, W-P11)
- Supersedes / amends: none
- Amended by: ADR-0247 (accepted 2026-10-02) — Decision 4's phases run as external scripts; the cheats sheet never calls a model. Also 2026-10-02: the sheet's entry moved from W-P7 to W-P4 (Decision 1).
- Amended by: ADR-0248 (accepted 2026-10-02) — community-shared codes, one issue per code, listed below the bundled list in W-P11.

## Context

Cheats already exist in the app: the **cheat list** (`CheatListWindow`, from the classic Cheats menu) stores codes per game (`UI/Config/CheatCodes.cs`); the **bundled database** (`UI/Dependencies/Internal/CheatDb.Nes.json`) has 774 NES games and 9 829 codes, each with a one-line description, and `CheatDatabaseViewModel` preselects the loaded game by `EmuApi.GetRomHash(HashType.Sha1Cheat)`; the redesign gives them no home — in the new GUI they are reachable only through Tools ⋯.

Measured on the database (2026-10-02): about 78 % of codes are Game Genie (6 or 8 letters, alone or joined); 2 164 codes (22 %) are RAM writes (`XXXX:YY`). The Core tells the two apart (`CheatCode`, `IsRamCode`). A Game Genie code patches what the CPU reads from PRG ROM, which ADR-0184 forbids in a recording because it corrupts the recorded art; a RAM code is allowed there.

Two limits matter: **only NES has a database** (GB and SMS have cheat types but no bundled list); **hashes may not match** — the database's SHA-1s can differ from the user's dump, and ADR-0238 found exactly that. An LLM asked to *produce* a code will invent one that looks right: codes are specific to the ROM revision, and a wrong one can crash the game or quietly corrupt it.

Non-goals:
- No code is generated by a model and offered as is.
- No cheat search engine (RAM diffing) in this ADR.
- No change to the classic cheat window, which stays in Tools ⋯.
- The client never calls a model (Part A §1 principle 5, as amended by ADR-0247). Decision 4's phases run as external scripts.

## Decision

1. **Play gets a Cheats sheet (W-P11).** Reached from the pause overlay (W-P4) as one row, *Cheats · 2 on ›* — a cheat changes the game, not the pack's presentation, so it does not belong in W-P7's Enhancements panel, and the count stays visible on every pause (the first draft put the row in W-P7; the user moved it, 2026-10-02). It lists the database entries for the loaded game, each a toggle with its description, plus a search box over the descriptions. The game's own toggled codes are stored where the cheat list stores them today (`CheatCodes`), so the two windows never disagree (rule 12).
2. **A game not in the database is said so,** not shown as an empty list: "This copy of the game isn't in the cheat list", plus a search by name over the database. Picking an entry by name is allowed and marked "made for another copy — may not work".
3. **Remaster allows RAM codes only.** In Remaster's game view, a Game Genie code shows disabled with "Changes the game itself — not allowed while recording art" (ADR-0184). RAM codes stay available. Play and Share's replay are unrestricted; a replay records the cheats that are on (ADR-0205 §4), and W-H4 says so.
4. **An LLM may only choose from the list.** Two later phases, each its own slice:
   - **Search by intent.** "don't die" or "infinite lives" is matched against *this game's* database descriptions, as a closed Choice (Jev, ADR-0238, or a tool-free model; local Ollama is allowed). The call is made by an external script started from the sheet, never by the client (ADR-0247). The answer must be one of the listed entries, and a code outside the list is discarded, never shown.
   - **Web lookup for games not in the database.** A worker reads public code lists and proposes codes, each *evidence-free* (ADR-0188) until a headless check confirms it: runner `scripts/step_emu.py` (the step-mode emulator ADR-0238 uses), started by the same external script; ROM the user's loaded copy, by path, never uploaded; state a `.mss` minted from the current game when the lookup starts; check runs N frames with the code off and N with it on from the same state, and the code passes when its target address holds the promised value in every "on" frame and the "off" run differs there (a code with no RAM target — a PRG patch — cannot pass and is not offered). Only checked codes are offered, labelled "found online, checked on your copy"; the slice (P.12) fixes N and records it. Keys for hosted models use the OS credential store (ADR-0242 Q1).
5. **Console scope.** NES gets phases 1–3. GB/SMS show the sheet with manual entry only ("No cheat list for this console yet", rule 4); GBA shows what the Core supports, with the same reason line.

## Consequences

- W-P4 gains one row and goes from 6 to 7 controls, at the limit; a later pause item has to replace or merge one. W-P7 stays at 6.
- The first phase needs no network and no model.
- The LLM phases each need a measurement: share of intents answered with a correct entry, and share of web proposals that pass the check; each is accepted only on its own numbers.
- A "found online" code that passes the RAM check can still have side effects the check does not see. Its label says it was checked, never that it is safe.

## Amendment (2026-10-05) — P.11 adoption verdict

**Decision: `AGREED` — compound panel, Codex (lens A) and Grok 4.6 (lens B), each blind to
the other. Both ruled option B.**

Lens B stands in for the panel's own `panel-lens-b` agent, which is pinned to Opus; the owner
vetoed Opus for subagents on 2026-10-05 (*"nao use o Opus"*, *"use o Grok no lugar do Opus"*).
No lens was unavailable, so no third lens was needed and no PROVISIONAL label applies.

**Ruling.** Adopt P.11 — **Jev only** — but keep it **unwired** (W-P11's search-by-intent entry
stays unbuilt) until an **independently authored, held-out intent set** passes **≥ 90 % correct
and ≤ 5 % wrong entries**. Do not offer the local `qwen2.5:7b-instruct` model: it fails both
bars (84.6 % correct, 10.8 % wrong entries).

**Reasons, in both lenses' words.** The measured set is an upper bound, not evidence: the same
agent wrote the intent set and the script after reading the descriptions, most intents are
near-literal, and no held-out set written by a third party was ever run
(`docs/validation/measurements/p11-cheat-intent-measurement-2026-10-02.md`). The harmful failure
is a **wrong entry**, which turns on a cheat that does something else, while a wrong `NONE` only
sends the player back to the text search — so the two bars are judged separately. Wiring costs
the player a key to install and the project a paid `alpha` endpoint (ADR-0242 Consequences). A
convenience feature does not warrant that on a self-graded set. Lens A added that a held-out
**failure** would move it to option D (do not adopt), and that a stronger local model passing
both bars on its own numbers would justify adding that specific model.

**Both lenses opened the repository.** Lens A cited
`p11-cheat-intent-measurement-2026-10-02.md:60`, `:74`, `:137`, `scripts/cheat_intent.py:151`,
`scripts/cheat_intent_eval.py:39`, `scripts/test_cheat_intent.py:312`, ADR-0245:38, ADR-0247:40,
ADR-0242:31 and `:45`, PRD:1138. Lens B cited the measurement table at `:73-78`, the threshold at
`:115-121`, ADR-0245 Status `:3`, ADR-0245 Consequences `:38`, the caveat at `:60-63` and `:137-138`,
the harmful-miss rule at `:67-70`, `scripts/cheat_intent.py:12-21`, ADR-0247 `:35-36, :38, :40-41`,
ADR-0242 `:3, :30-34, :45-46` and PRD `:1138`. The referee re-checked the load-bearing numbers
(64/65, 0 wrong; 55/65, 7 wrong) against the log itself and both hold; `git status` after the
panel showed no file written by either lens.

**What follows from this ruling.** The blocking work is owner-independent and is being run:
a held-out set authored cold (its author forbidden to open the original fixture, the scoring
internals or the old measurement log), scored against Jev under a US$ 0.10 cap, logged at
`docs/validation/measurements/p11-heldout-gate-2026-10-05.md`. Adoption turns on that result:
passing it makes the wiring an ordinary slice; failing it makes the answer option D, and P.11
stays an external script.

### The gate ran, and P.11 is adopted (2026-10-05)

**Decision: `AGREED` again — a second compound panel, Codex (lens A) and a
`panel-adversary-session` lens (lens B, challenger stance), both blind. Both ruled option C:
adopt.**

The held-out set is `tests/fixtures/cheat-intent/heldout-intents.json` (worktree
`feat/p11-adopt-gate`): **121 cases over 27 games, 15 of them expecting `NONE`**, authored cold
before the run — its author was forbidden to open the original fixture, the scoring internals or
the 2026-10-02 log. Run against Jev `typesafe/jev-1.13` under a US$ 0.10 cap
(`docs/validation/measurements/p11-heldout-gate-2026-10-05.md`):

| Correct | Share correct | `NONE` expected | Wrong entry | Discarded | Cost |
|---|---|---|---|---|---|
| 120/121 | **99.2 %** | 15/15 | 1 (0.83 %) | 0 | US$ 0.00466 |

**Both bars pass** (≥ 90 % correct, ≤ 5 % wrong entries). The single miss is the pt-BR
`"não deixe o Pac-Man morrer"` (Pac-Man), answered `E8` where `E3` was expected. By the rule the
first panel set — adoption is a convenience feature that must not turn on the wrong cheat — the
share of wrong entries is the number that matters, and 0.83 % is far inside the bar.

**Correction to the record.** The first draft of this run was reported as *86.8 % correct with
zero `NONE` cases*, which would have failed the primary bar and forced option D. That was wrong
twice over, and both wrongs were the referring session's, not the measurement's: the brief handed
the set's author `accept: []` as the spelling for an expected `NONE` when the real spelling is
`["NONE"]` (`scripts/cheat_intent_eval.py:44`, `NONE_ID` at `scripts/cheat_intent.py:68`), and the
86.8 % figure then came from scoring the run under that wrong spelling, which counts all 15
correct `none` answers as misses (105 = 120 − 15). Both panel lenses caught it by opening the
files rather than trusting the brief, and the corrected figures above are re-scored from the
run's own rows. A stale sentence in the fixture's `how_chosen` still names `accept: []`; it is a
doc/data mismatch to clean, not evidence. This is the panel's fact-check rule doing its job: a
load-bearing claim in a brief is not evidence until a lens opens the artifact.

**What follows.** Wiring W-P11's search-by-intent entry — the client starting the external script
and passing the key only through the child's environment (ADR-0247), reading the custody interface
ADR-0242 Q1 delivered — is now an **ordinary slice** with no owner decision outstanding. The
qwen2.5 7B local backend stays unoffered: it fails both bars on the original set.
