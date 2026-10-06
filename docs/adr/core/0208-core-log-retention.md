# ADR-0208: What the core log retains — the 1 000-entry ring and the uncapped `mesen.log`

- Status: accepted 2026-09-17 — the user picked the recommendation verbatim: "Marcar truncagem + teto no mesen.log". Implemented in the same turn under the CLAUDE.md exception (ships with unit tests; go-ahead quoted here and in the PR body).
- Date: 2026-09-17
- Related: ADR-0137 (`doc-checks` gates), issue #302 (the loader flood that exposed this), issue #160 (the false FAIL the ring's eviction produced)

## Context

`MessageManager` keeps two logs and neither has a retention policy anyone chose:

- **In memory**, `std::list<string> _log` capped at 1 000 entries with front-eviction (`Core/Shared/MessageManager.cpp:195`). This is what the Log Window shows and what `GetLog()` returns. Oldest messages are dropped silently — nothing marks that eviction happened, so a truncated log is indistinguishable from a short one.
- **On disk**, `<home>/mesen.log`, opened lazily on the first message after the home folder is known, with the previous session moved to `mesen.log.1` (`Core/Shared/MessageManager.cpp:208-234`). Rotation is **per session, by count of one**: no size cap, no line cap, no third generation.

Two incidents made the policy's absence visible:

- **#160** — `smoke_pack_headless.sh` reported a false FAIL because the MEP detection line it asserted on had been evicted from the ring by a large pack. The script was fixed to read `mesen.log` instead; the eviction itself was deferred as "a separate, pre-existing class".
- **#302** — the Metroid pack emitted 8 234 loader errors in one load, evicting every other message in the ring and adding ~570 KB to `mesen.log`. The fix (PR #303) deduplicated *the loader's* messages, so that pack now emits 28 lines. It deliberately did not touch either log.

#303 removed the loudest producer. It did not make the two logs any more bounded, and the next verbose subsystem will reproduce both symptoms. The asymmetry is the uncomfortable part: the **memory** log silently discards what a developer most often wants (what happened *first*, before the flood), while the **disk** log discards nothing and can fill a volume. Each is unbounded in the direction the other is capped.

**Non-goals.** Log levels or per-category filtering; a structured/JSON log format; changing what any subsystem logs; the Log Window's UI.

## Decision

**(a) + (d).** One decision with two halves, answered together because the ring's limit is tolerable today only because the disk log catches the overflow. Concretely, in `Core/Shared/MessageManager`:

- `MaxLogEntries = 1000` — unchanged, but every eviction now increments a counter. `GetLog()` prepends `FormatTruncationNotice(n)` when that counter is non-zero: *"[MessageManager] log truncated - N earlier message(s) evicted from the 1000-entry ring; mesen.log has the full run"*. `ClearLog()` resets the counter, and `GetDroppedLogEntryCount()` exposes it without parsing.
- `MaxLogFileBytes = 4 * 1024 * 1024` — when the next entry would cross the budget, `mesen.log` rotates into `mesen.log.1` and reopens, through the same single generation the per-session rotation already used. `ReopenLogFile()` is the shared entry point, so a caller that changes the home folder can re-point the file instead of writing into a stale handle.

Verified by `BlocoU` in `scripts/core_unit_tests.cpp` (18 cases): the notice's position, its number, that the evicted entries are genuinely gone, that `ClearLog()` clears the debt, and that the live log never exceeds the budget while `mesen.log.1` holds the overflow.

### Options considered

**Half 1 — the ring.** Chosen **(a)**: keep 1 000 but mark the truncation — `GetLog()` leads with a synthetic line naming how many were dropped; the cheapest change, converting a silent lie into a stated one, which is what #160 and #302 needed. Rejected: **(b)** raise the cap against a real pack load; **(c)** keep the head and last entries, dropping the middle (right only if a "keep 1 000" log is found losing session starts after (a) makes the losses countable).

**Half 2 — `mesen.log`.** Chosen **(d)**: cap by size with rotation at a fixed byte budget, keeping the `.1` generation — unbounded growth in a consumer application is a defect regardless of how useful the file is, and a size cap is a number, not a new mechanism. Rejected: **(e)** leave it uncapped and say so in `docs/`; **(f)** cap only when not explicitly enabled.

## Consequences

- Under (a), anything asserting on `GetLog()` sees a new first line when truncation happened. Audited before shipping: the only two consumers are `UI/Windows/LogWindow.axaml.cs` and `headless_record`'s `log` flag, and both display the string without parsing it — so the notice lands where a reader is already looking and no capture-tool rebuild is required.
- Under (d), a long session can lose its own early lines from disk, which is exactly what `mesen.log` is currently the only defence against. This is why the two halves are one decision: capping the disk log while leaving the ring silently lossy would remove the last place the truth survives.
- Whatever is chosen, #302's dedupe stands on its own — it reduced a real producer from 8 234 lines to 28 and is not superseded by any option here.
