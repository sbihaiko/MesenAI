# ADR-0273: A review that finds only debt merges and files the debt as a task — the squad's merge gate holds on blocking findings alone

- Status: accepted (2026-10-10, by the user: "aprova, escreve o ADR e publica os tickets"; implementation = the four tickets this ADR's Decision is broken into, none of which ships without its regression suite)
- Date: 2026-10-10
- Related: ADR-0193 (the `main gate` ruleset's rules, its bypass actors and its required checks — unchanged by this decision), ADR-0163 (fork–upstream coexistence; the loop's merge target), `.github/AGENTS.md`, `docs/squad/README.md`
- Supersedes / amends: nothing. This is the first record of what the squad's review verdict means.

## Context

The autonomous loop gates a merge on one line: `VERDICT: APPROVE | CHANGES`. Any `CHANGES` costs a full fix round, and a fix round is a new head — which buys a new review and a new CI run. Measured over the 46 pull requests open between 2026-10-09 and 2026-10-10: p50 create→merge 34 min, p90 119 min, 25 of 46 past 30 min. Of the review attempts in that window, 117 failed (273 min) against 150 that answered (177 min) — the failures cost more than the answers. PR #1285 alone spent about two hours across five heads.

The reviewer already distinguishes severity, in prose: its findings are asked for "blocking first", and an actual run on #1285 marked 2 of its 4 findings `(blocking)`. The loop cannot read that. `VERDICT: CHANGES` is the only channel, and it is binary — so a naming finding, a doc gap and a regression of behaviour `main` already had all cost the same round. The severity the reviewer computed is thrown away at the boundary.

Non-goals. This does not lower what counts as a defect, and it does not weaken the merge gate: the five required contexts of the `main gate` ruleset (ADR-0193) are untouched, and every guard the merge already had — a check run existing for the head at all, `SG_AUTO_MERGE`, base is `main`, `mergeable_state` clean, no unresolved review threads, a non-empty diff — still runs before anything lands. It also does not enable GitHub's merge queue: that changes *how* a merge happens, and it lands as its own decision with its own measurement if it lands at all (Consequences).

## Decision

1. **Every finding carries a tag.** Each finding line of the review file `$SG_STATE/review-<pr>.md` starts with `BLOCKING` or `DEBT`; line 1 stays `VERDICT: APPROVE | CHANGES`. The contract applies to both prompt modes — the full review and the fix round — and in a fix round each previous finding keeps the tag it was given when first raised.

2. **Blocking is a closed list, not the reviewer's judgement.** Exactly four kinds hold a merge:
   - a divergence from an accepted ADR;
   - a regression of behaviour `main` already had;
   - a change to a wire protocol or a persisted format (a save state, a MEP manifest, `hires.txt`, the review file's own line protocol);
   - a test that passes while lying — weakened, skipped, or asserting something other than what it names.

   Everything else is `DEBT`: naming, structure, a doc gap, an error path nobody can reach, a missing case behind a flag. The list is closed so the verdict is predictable from the diff rather than from the model's mood; a finding outside it that a reviewer believes is fatal is argued into one of the four, or it is debt.

3. **`CHANGES` with zero blocking findings does not cost a round.** The loop reads the tags; with no `BLOCKING` line it files the debt (item 4) and then falls through the merge path it already had, every guard included. A `(prepush)` `CHANGES` is excluded — a closing keyword or a cited file missing from the head is a process problem, not debt — and so is any finding a reviewer tagged `BLOCKING`.

4. **The debt is a task, not a dropped finding.** One GitHub issue per `DEBT` finding on `sbihaiko/MesenAI`, label `debt`, body carrying the origin PR, `file:line`, the defect and the acceptance criterion — what makes it go away. Filing is idempotent per head: a re-run files nothing new. The issues carry `ready-for-agent` so the frontier picks them up like any other work.

5. **The ledger is capped.** Past `SG_DEBT_MAX` (default 15) open `debt` issues, the soft path switches itself off and a debt-only `CHANGES` costs a round again. This is the whole guard against a rotting trunk: without it, "merge and file a task" is how findings disappear.

## Consequences

- Review quality becomes load-bearing in a new way: a reviewer that under-tags turns a defect into a filed task. The closed list (item 2) is the mitigation, and the cap (item 5) bounds the damage — but the honest risk is a reviewer that learns to call everything `DEBT`.
- The tag is a contract across three scripts that must change together: the reviewer's prompt, the loop's counter, and the debt filer. It lives in a file the loop parses, so it is not free to drift.
- Debt pays off only if the frontier drains it. The cap makes that visible; it does not make it happen. A sustained `debt` backlog at the cap means the soft path is off, which is the intended failure mode — back to blocking, not back to dropping.
- The `debt` label is new in the repository's vocabulary (`scripts/ensure_community_pack_labels.sh` owns a different set; this one belongs to the squad's flow and is minted by the filer).
- GitHub's merge queue stays out of scope deliberately: today the loop merges by REST call, and the queue would change that path, the ruleset and the meaning of "green head" at once. Mixing it into this change would make the effect of either one unmeasurable.
- Verified by the fork's regression suites, none of which needs network or a model: the reviewer's tag contract, the debt filer against a stub `gh`, and the loop's decision (merge on debt-only, round on a blocking finding, round at the cap, round on `(prepush)`).
