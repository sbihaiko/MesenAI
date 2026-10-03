# ADR-0245: Play offers cheats from the bundled database; Remaster allows RAM codes only, and an LLM may only pick from the list

- Status: accepted (2026-10-02). Requested by the user, verbatim: *"Sim, W-P11 + ADR"*, after asking whether cheats are worth loading, how to pick and identify them, and whether a (free) LLM could find codes; accepted the same day (*"Aceitar"*). Listed as slices **P.10** (phase 1: W-P11, the bundled list, the Remaster rule), **P.11** (search by intent) and **P.12** (checked web lookup) in PRD Part A §4, Phase 7; P.11 and P.12 are each adopted only on their own measurement (Consequences). Wireframe: PRD Part B §13, W-P11. **Implemented 2026-10-02 (P.10: §1–§3, §5)** on the user's go-ahead, verbatim: *"sim, pode seguir. depois que tudo estiver no main, pode implementar usando paralelismo de tudo que puder"* and *"pode implementar em paralelo tudo que puder"* (2026-10-02). The W-P11 sheet opens from today's player overlay (*Cheats · N on*) and writes the same `CheatCodes` list as the classic cheat window; the §3 rule is host-free (`UI/Logic/CheatRecordingRule`: a RAM code is `NesCustom` with every address below `0x0800`, as ADR-0184 §1 requires, so the 96 database entries writing above `0x07FF` are refused too) and is wired to a `recordingArt` flag that Play leaves false. **Remaster wiring implemented 2026-10-02** on the go-ahead *"acabe a implementação da nova GUI, garanta que tudo está na main, teste tudo que for possível"* (user, 2026-10-02): W-R2 has no overlay, so the recording-art context is Remaster active or a Remaster recording running (switching to Play does not stop it, PRD Part B §13.6), and there the sheet passes `true`; *Record While I Play* refuses to start while a cheat that is not a RAM code is on, naming it (ADR-0184 §1: refuse, not warn); while it records, `CheatCodes.ApplyCheats` holds back any such code turned on later — the classic cheat window included, whose UI is unchanged — the W-R2 strip names it, and Stop gives it back. Tested host-free in `UI.Tests/Cheats/CheatRecordingContextTests` and against the real core in `UI.HeadlessTests/RemasterCheatsTests`. **P.11 measured 2026-10-02, not adopted**, on the go-ahead *"pode seguir com a segunda leva em paralelo"* (2026-10-02) besides the two above: the external script `scripts/cheat_intent.py` (§4's closed Choice; an answer outside the listed entries is discarded) scored 64/65 (98.5 %) with Jev and 55/65 (84.6 %) with local `qwen2.5:7b-instruct` on a 65-case intent set, with 0 and 7 wrong entries (`docs/validation/p11-cheat-intent-measurement-2026-10-02.md`). This ADR sets no threshold; the log proposes ≥ 90 % correct and ≤ 5 % wrong entries, and adoption and the W-P11 wiring await the user's decision. P.12 is not implemented. **Amended 2026-10-03 (Play unlock, user's decision):** the `recordingArt` flag the Play sheet passes is false in Play even while Play's passive automatic bootstrap (the legacy *Record while I play* setting, ADR-0243 Q3) records; the bootstrap term added to it in #690 greyed out every non-RAM cheat (e.g. Contra *Start on level 5*). The user was asked and chose, verbatim: *"Liberar e pausar gravação (Recomendado)"* — in Play every cheat is switchable; turning on a code that is not a RAM code (`CheatRecordingRule.IsRamCode`) stops the automatic recording for the session (`CheatRecordingRule.PausesPassiveBootstrap`, `EmuApi.StopMepRecording` from `CheatCodes.ApplyCheats`; art already recorded is kept, and the core starts the passive bootstrap only at ROM load, so it does not come back until the next load), and a non-RAM code already on at load stops it right after the load. The lock stays only in Remaster (Remaster active, or a recording the user started there), where `CheatCodes.RecordingArt` still holds such codes back. Tests: `UI.Tests/Cheats/CheatRecordingContextTests`, `UI.HeadlessTests/RemasterCheatsTests`.
- Date: 2026-10-02
- Related: ADR-0184 (a recording may use a RAM-only cheat, never a PRG patch), ADR-0205 §4 (cheats are unrestricted in a shared replay and are recorded in the `.mmo`), ADR-0188 (an AI's judgement is a proposal, never evidence), ADR-0238 (Jev answers a closed Choice), ADR-0242 (the OS credential store holds BYOK keys), ADR-0128 (cheat type detection), ADR-0241 / PRD Part B §13 (W-P4, W-P11)
- Supersedes / amends: none
- Amended by: ADR-0247 (accepted 2026-10-02) — Decision 4's phases run as external scripts; the cheats sheet never calls a model. Also 2026-10-02: the sheet's entry moved from W-P7 to W-P4 (Decision 1).
- Amended by: ADR-0248 (accepted 2026-10-02) — community-shared codes, one issue per code, listed below the bundled list in W-P11.

## Context

Cheats already exist in the app:

- **The cheat list** (`CheatListWindow`, from the classic Cheats menu)
  stores codes per game (`UI/Config/CheatCodes.cs`).
- **The bundled database** (`UI/Dependencies/Internal/CheatDb.Nes.json`)
  has 774 NES games and 9 829 codes, each with a one-line description.
  `CheatDatabaseViewModel` preselects the loaded game by
  `EmuApi.GetRomHash(HashType.Sha1Cheat)`.
- **The redesign gives them no home.** In the new GUI they are reachable
  only through Tools ⋯.

Measured on the database (2026-10-02):

- about 78 % of codes are Game Genie (6 or 8 letters, alone or joined);
- 2 164 codes (22 %) are RAM writes (`XXXX:YY`).

The Core already tells the two apart (`CheatCode`, `IsRamCode`).

A Game Genie code patches what the CPU reads from PRG ROM, and ADR-0184
forbids that in a recording, because it corrupts the recorded art. A RAM
code is allowed there.

Two limits matter:

- **Only NES has a database.** GB and SMS have cheat types but no bundled
  list.
- **Hashes may not match.** The database's SHA-1s can differ from the
  user's dump; ADR-0238 found exactly that.

An LLM asked to *produce* a code will invent one that looks right. Codes
are specific to the ROM revision, and a wrong one can crash the game or
quietly corrupt it.

Non-goals:

- No code is generated by a model and offered as is.
- No cheat search engine (RAM diffing) in this ADR.
- No change to the classic cheat window, which stays in Tools ⋯.
- The client never calls a model (Part A §1 principle 5, as amended by
  ADR-0247). Decision 4's phases run as external scripts.

## Decision

1. **Play gets a Cheats sheet (W-P11).**
   - It is reached from the pause overlay (W-P4) as one row,
     *Cheats · 2 on ›*. A cheat changes the game, not the pack's
     presentation, so it does not belong in W-P7's Enhancements panel, and
     the count stays visible on every pause. (The first draft put the row
     in W-P7 to keep W-P4 small; the user moved it, 2026-10-02.)
   - It lists the database entries for the loaded game, each a toggle with
     its description, plus a search box over the descriptions.
   - The game's own toggled codes are stored where the cheat list stores
     them today (`CheatCodes`), so the two windows never disagree (rule 12).
2. **A game not in the database is said so,** not shown as an empty list:
   "This copy of the game isn't in the cheat list", plus a search by name
   over the database. Picking an entry by name is allowed and marked
   "made for another copy — may not work".
3. **Remaster allows RAM codes only.** In Remaster's game view, a Game
   Genie code shows disabled with "Changes the game itself — not allowed
   while recording art" (ADR-0184). RAM codes stay available.
   - Play and Share's replay are unrestricted. A replay records the cheats
     that are on (ADR-0205 §4), and W-H4 says so.
4. **An LLM may only choose from the list.** Two later phases, each its
   own slice:
   - **Search by intent.** "don't die" or "infinite lives" is matched
     against *this game's* database descriptions, as a closed Choice. Jev
     (ADR-0238) or a tool-free model fits; local Ollama is allowed.
     The call is made by an external script started from the sheet, never
     by the client (ADR-0247). The
     answer must be one of the listed entries, and a code outside the list
     is discarded, never shown.
   - **Web lookup for games not in the database.** A worker reads public
     code lists for the game and proposes codes. Each proposal is
     *evidence-free* (ADR-0188) until a headless check confirms it:
     - runner: `scripts/step_emu.py` (the step-mode emulator ADR-0238
       uses), started by the same external script;
     - ROM: the user's loaded copy, by path, never uploaded; state: a
       `.mss` minted from the current game when the lookup starts;
     - check: run N frames with the code off and N with it on from the
       same state; the code passes when its target address holds the
       promised value in every "on" frame and the "off" run differs there.
       A code with no RAM target (a PRG patch) cannot pass and is not
       offered.

     Only checked codes are offered, labelled "found online, checked on
     your copy". The slice (P.12) fixes N and records it.
   - Keys for hosted models use the OS credential store (ADR-0242 Q1).
5. **Console scope.** NES gets phases 1–3. GB/SMS show the sheet with
   manual entry only ("No cheat list for this console yet", rule 4). GBA
   shows what the Core supports, with the same reason line.

## Consequences

- W-P4 gains one row and goes from 6 to 7 controls, at the limit; a later
  pause item has to replace or merge one. W-P7 stays at 6.
- The first phase needs no network and no model.
- The LLM phases each need a measurement: share of intents answered with a
  correct entry, and share of web proposals that pass the check. Each is
  accepted only on its own numbers.
- A "found online" code that passes the RAM check can still have side
  effects the check does not see. Its label says it was checked, never
  that it is safe.
