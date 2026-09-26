# ADR-0238: Jev, via OpenRouter, is a stuck-point input generator behind a persistent step-mode emulator; a route stays a plain input script

- Status: accepted (2026-09-26). The user chose the vendor path verbatim — *"vamos usar o jev pelo ope router"* — and then the recommended order, verbatim: *"pode escrever"*. **Not implemented**: pending slices F14.12–F14.15 in `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` (Part A §4, Phase 14). No go-ahead to implement yet.
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
