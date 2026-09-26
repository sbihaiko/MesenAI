# ADR-0238: Jev, via OpenRouter, is a stuck-point input generator behind a persistent step-mode emulator; a route stays a plain input script

- Status: accepted (2026-09-26). The user chose the vendor path verbatim — *"vamos usar o jev pelo ope router"* — and then the recommended order, verbatim: *"pode escrever"*. **Implemented, F14.12–F14.14**: the step-mode session of §1 and its client (`scripts/step_emu.py`, `scripts/headless_record.cpp`, `docs/validation/f1412-step-mode-emulator-2026-09-26.md`), the ported search of §2 (`scripts/route_search.py`, `docs/validation/f1413-ninjagaiden-search-2026-09-26.md`) and the stall helper of §3 (`scripts/jev_harness.py`, `scripts/jev_client.py`, `docs/validation/f1414-jev-stall-helper-2026-09-26.md`). §4's artifact rule is what those three produce — a plain `<n>f <buttons>` script plus `cheat=` codes, never a patch. Go-ahead to implement, verbatim: *"implemente usando o deepseek"* (2026-09-26). **F14.15 (§5, measurement and adoption) implemented 2026-09-26** ([log](../validation/f1415-jev-adoption-2026-09-26.md)): two live stalls measured — Ninja Gaiden's section 1-2 death window, and Mega Man 3's Snake Man stall, which the page-chained search put on page 3 at camera 184 because the point the slice briefing named (camera 187) is not a stall at all (log §1, §3). The **first pass is void** — its 0-of-8 result came from four harness defects, each fixed with a test that failed first (rewind-ladder floor, loop-guard fingerprint, Mega Man 3's one-byte progress field, a research worker that could never answer; log §0). **Second pass: §5's first half is met for the first time** — Jev passed the Mega Man 3 stall in **5 of 5 arms**, tips on and tips off, at **3.57–3.62×** real time with deterministic repeats, while the search alone stops there (`no-jev`); the Ninja Gaiden control still passes on the first rung at 4.03×. **§5's second half still fails**: the route that passes gains the recorded kit **0 keys** the committed routes do not already have, because Mega Man 3 is CHR ROM and the bootstrap exports every bank index, so the new corridor paints no new tile-palette pair (`runs/f1415/cells.py`; union of nine packs 8 777 keys, `mm3-bossrun` alone 381 of the 394 that are unique) — and Jev passed **0 of 4** arms on the Ninja Gaiden stall, whose state has no legal candidate for the base search and therefore a one-checkpoint rewind ring. Verdict: **do not adopt beyond the spike** — one clause short, and the missing one is about the product, not the model. §1–§4 stand, and the step-mode session's own gain (0.232 → 0.091 s per candidate) is untouched. **Third pass, same day** ([log](../validation/f1415-jev-adoption-2026-09-26.md) §11): the verdict stands on harder numbers. The kit criterion was re-measured on a route that passes the stall *and keeps going* — **abs_x 984**, 78 px past the second pass's 906 — against the same-length search-alone recording: **97 more cells and 22 more keys**, 13 more than the two committed MM3 routes, and **0 keys no other pack here has**, so the second clause fails for a route that reaches new level rather than one that stopped short. Stall A's ladder lands on **four distinct checkpoints** instead of one; the literal 20-second pre-roll was measured and rejected — those 20 s are on the previous screen, where the camera resets and `abs_x` drops by 2 800 — and the wall's own screen is ~6 s long, so rungs of 8 and 16 s are not available at it. Jev still passes **0 of 2** there. The rejected pre-roll did yield a **third stall Jev passes and the search alone does not** — the abs x 2859 wall, in 3 decisions on the first rung, twice, identically, at 4.00× real time (log §11.3) — so clause 1 is met three times over and not twice. The extended route replays deterministically (one flat 480-frame script, three RAM checkpoints, twice, 0 bytes differing) and is **not published**: one mint generator short, §5's reason unchanged. §3's worker restriction is now real rather than intended — `--tools`, a 23-name `--disallowedTools` and `--safe-mode`, with the CLI's init event read back into every run's log — and `--max-research-passes` bounds web spend on both roads into the worker.
- Date: 2026-09-26
- Related: ADR-0185 (a published movie is input, never evidence), ADR-0188 (an AI's judgement is a proposal that never becomes evidence), F14.3 route sets (`scripts/stages/*`), ADR-0203/0204 (CI and download channel, which this does not touch)
- Supersedes / amends: nothing

## Context

A recording is only as good as the play that drives it. Today a route is one
of three things:

- a searched script: `runs/route-*/solve.py`-style beam or block searches over
  RAM, which produced the SMB3 and Ninja Gaiden `stage1-run.txt` of #534/#535;
- a published TAS converted to `.bk2` (ADR-0185);
- a hand-written `scripts/stages/<game>/*.txt`.

The searches are free, local and deterministic, but they stall where the game
needs a move the search never tries. Ninja Gaiden's Act 1-1 pins Ryu at x 987
(camera 859), and only Left+A leaves the spot. Bosses are the other known
stall.

Jev (TypeSafe's "System One" model) answers a *Choice* question: typed state
in, one option from a closed set out, with per-option probabilities. It does
not generate text or read images. Its controller-driving use is already
public: an NES Mario harness picks among seven actions, and three Pokémon Red
harnesses pick button macros. That makes it a candidate for the stall points.

Measured 2026-09-26, on this repo and the maintainer's OpenRouter key:

| Quantity | Value |
|---|---|
| `headless_record`, Mega Man 3, no pack, 60 s emulated | 6.4 s wall (≈ 9× real time) |
| same, 600 s emulated | 66.5 s wall (≈ 9×, steady) |
| process start + ROM load per `headless_record` launch | ≈ 1 s |
| Jev `typesafe/jev-1.13` (served `jev-1.13-20260917`), one 7-option Choice | HTTP 200, 547 input tokens, US$ 0.000023 |
| Jev latency, cold connection | 0.63–0.79 s |
| Jev latency, reused connection (8 calls) | 0.32–0.46 s, ≈ 0.38 s typical |

Both current searches launch one `headless_record` per candidate
(`subprocess.run` in the Ninja Gaiden `solve.py` and the SMB3
`search/solver.py`), so they pay the ≈ 1 s start per try.

Arithmetic for one minute of play, taking 6.4 s of emulation:

| Who decides | Decisions / min | Wall time | vs real time |
|---|---|---|---|
| Jev every 15 frames | 240 | ≈ 98 s | 0.6× (slower) |
| Jev every 60 frames | 60 | ≈ 29 s | ≈ 2× |
| search plays; Jev only at stalls (≤ 35) | ≤ 35 | ≤ 20 s | ≥ 3× |

So Jev as the default player is slower than real time. Its value is confined
to the stall points.

The published "~100 ms" latency did not reproduce from here. The python.org
Python on the maintainer's Mac has no CA bundle (`CERTIFICATE_VERIFY_FAILED`);
`curl` works.

Non-goals:

- Jev is not the default recorder and does not replace the searches.
- No real-time play: the emulator is paused while Jev decides.
- Jev never sees ROM bytes, screenshots or any pixel. Its state is RAM-derived
  numbers only.
- No CI job calls Jev, and no key lives in CI.

## Decision

In this order, each step gated on the one before:

1. **A persistent step-mode emulator (F14.12).** A long-lived headless session
   that loads a ROM and a state once, then serves requests without a
   relaunch:
   - run N frames with a given input;
   - return the RAM bytes asked for;
   - save or restore a state in memory.

   The transport — the existing InteropDLL driven through ctypes, or a
   stdin/stdout mode of `headless_record` — is picked inside the slice, by
   measurement. The slice first ports one existing search (Ninja Gaiden's) to
   it and measures the per-candidate cost before and after. Its value does not
   depend on Jev.
2. **Fix the search before calling Jev (F14.13).** Add the missing macro (the
   Left+A jump Ryu's pin needs) to the Ninja Gaiden search and re-run it on
   the step-mode emulator. If it passes x 987, that stall needs no Jev.
3. **Jev as the stall helper (F14.14).**
   - **When it is called:** only when the search makes no progress for a set
     number of emulated seconds (progress = the game's own position/camera
     RAM).
   - **The question:** the harness sends one Choice with a closed set of
     about seven macros (for example `RIGHT_15`, `RIGHT_RUN_15`,
     `JUMP_RIGHT`, `JUMP`, `ATTACK`, `LEFT_15`, `WAIT_15`; durations are fixed
     in code, never by the model). The state is RAM-derived JSON. The chosen
     macro is appended and the search resumes from the resulting state.
   - **Rewind on failure (amended 2026-09-26, user's suggestion):** the
     harness keeps a ring of in-memory checkpoints (one per emulated
     second). When a chosen macro makes no progress, it restores an earlier
     checkpoint on a doubling ladder — 1, 2, 4, 8, 16 s back — and asks
     again, never rewinding past the start of the current screen (amended
     again 2026-09-26: "or the last real progress" was dropped — the watermark
     rises every window, so that floor sat a fraction of a second behind the
     head and collapsed every rung onto one checkpoint, measured in F14.15's
     first pass). The rungs match the three failure causes: a mistimed
     press (1–2 s), a bad approach speed/height/HP (4–8 s), an earlier wrong
     choice (16 s). Up to three questions per rung. Jev keeps
     no memory between calls, so the state carries a `tried` list of what
     already failed from that checkpoint (macro, progress, death), and a
     macro that failed there is removed from that question's options. Each
     stall has a fixed attempt cap; past it the harness records the stall
     point and stops. Only the winning path reaches the script.
   - **Situation tips (amended 2026-09-26, user's suggestion):** each game
     may carry `scripts/stages/<game>/jev-tips.json` — per-situation tips
     for bosses and hard spots, each gated by a RAM trigger (stage, x range,
     boss fight on), written in our own words with sources cited. A question
     carries only the tips whose trigger holds, folded into the option
     descriptions; the decision log records the tips file hash. Macros a tip
     calls for are added to the fixed set, and the search tries them too.
     Measured on four synthetic states (2026-09-26, 40 calls, US$ 0.00098):
     15/20 right without tips, 20/20 with them.
   - **Web research when the ladder is exhausted (amended 2026-09-26, user's
     suggestion):** the harness writes a stall report (game, stage,
     position, what was tried and how it failed) and one `claude -p` worker
     with web search proposes new tips and, if needed, a new fixed macro.
     The search query carries the game and a description of the spot only —
     never ROM bytes or pixels. One research pass per stall; the retry runs
     from the checkpoint with the new tips kept under `runs/`. A tip reaches
     the versioned `jev-tips.json` only after it passed the stall.
   - **Loop guard (amended 2026-09-26, user's suggestion):** three
     detectors, each logged per decision: a state fingerprint (position
     rounded to 8 px, camera, room, HP) of the committed head seen 3 times in
     one stall (a rejected attempt that ends where another did is what
     `tried_here` records, not a loop; and consecutive samples of one 8-px
     cell are one visit, because "the run stands still" is the watermark
     detector's finding, not this one's); a
     progress watermark (furthest position reached) that has not risen for
     60 emulated seconds; a period-2-to-4 cycle repeated 3 times in the last
     12 choices. A detected loop bans the cycle's macros at that checkpoint
     and climbs one rewind rung; a second loop goes to web research; a third
     ends the stall as `loop`. Hard caps on decisions per stall, emulated
     time and budget always hold. Each log line carries watermark, novelty
     (share of the last 20 decisions that reached an unseen fingerprint),
     loop count and spend; the run summary counts loops, and F14.15 reports
     them.
   - **Model and endpoint:** `typesafe/jev-1.13`, pinned, at
     `https://openrouter.ai/api/alpha/decisions`.
   - **Per-decision log:** each decision logs the served snapshot id, request
     id, choice and probabilities to a `runs/` sidecar, never versioned. The
     state itself is not logged.
   - **The key:** `OPENROUTER_API_KEY` is read from the environment or the
     repo-root `.env`, which `.gitignore` ignores. It is never printed,
     logged, committed or passed on a command line. The harness refuses to
     run without it.
   - **HTTP:** through `curl`, or Python with an explicit CA bundle.
   - **Budget:** a hard cap per run, US$ 1 for the spike; the harness stops at
     the cap.
4. **The artifact is a plain input script.** The output is the same
   `scripts/stages/<game>/*.txt` format the searches write (`<n>f <buttons>`).
   Replay never calls Jev: same ROM, same start state and same inputs give the
   same frames. As with a TAS (ADR-0185), Jev's output is input, never
   evidence.
   - **Cheats (amended 2026-09-26, user's request):** a search or Jev run
     may carry RAM-only cheats under ADR-0184 — life/lives counters first,
     an invulnerability timer only after checking what the game draws, a
     boss-HP code only when its frozen value is one the game produces (0).
     No weapon-power code: none is a RAM code the game produces. Each code
     comes from `UI/Dependencies/Internal/CheatDb.Nes.json` or a published
     RAM map and is re-verified on the dump the route is pinned to (the
     database's SHA-1s differ from ours). A cheated script is a coverage-pass
     artifact: it ships beside its cheat list, replays only with it, and
     feeds only ADR-0184 §2's coverage surfaces. A cheat does not remove a
     position stall, so a search-versus-Jev comparison is valid only under
     the same cheat set.
5. **Measurement and adoption (F14.15).**
   - **Where:** two real stall points — Ninja Gaiden x 987 (if F14.13 left it
     standing) and one Mega Man 3 boss.
   - **Adopt Jev beyond the spike only if both hold:**
     - it passes at least one stall the search could not;
     - the recorded kit gains cells the current route does not record.
   - **Speed target:** the hybrid run reaches ≥ 3× real time.
   - **Verification:** Grok 4.6 replays the committed script with no AI and
     matches RAM checkpoints.
   - **Throughput:** more is bought by recording games or stages in parallel,
     not by more Jev calls.

## Consequences

- **The step-mode emulator pays off either way.** Searches stop paying a
  relaunch per candidate, and a Jev spike that fails still leaves that gain.
- **A new external vendor, in early access, on an `alpha` endpoint.**
  - The API shape may change; the harness pins the model and logs the served
    snapshot.
  - The account is on OpenRouter's free tier with a US$ 50/month key limit.
    Rate limits for that tier are unverified; a 402/429 means buying a few
    dollars of credit.
- **Jev is probabilistic.** Two runs may choose differently, but the
  committed script is the artifact, so reproducibility never depends on the
  model.
- **Cost is negligible** (≈ US$ 0.00002 per decision). Latency, not money, is
  the constraint, which is why Jev is confined to stalls.
- **If F14.13 fixes the known stall, F14.14 may have no Ninja Gaiden case.**
  The spike then runs on the boss alone.
