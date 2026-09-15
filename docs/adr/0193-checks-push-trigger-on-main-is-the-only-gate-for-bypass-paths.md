# ADR-0193: `checks.yml` keeps its `push` trigger on `main` — it is the only gate for what bypasses the ruleset

- Status: accepted (2026-09-15, at the user's direction: asked "podemos
  habilitar o CI somente no Main?", shown that the post-merge run is the only
  gate for direct-to-`main` pushes and for merge-commits whose tree was never
  the tested PR head, chose "Não mexer + registrar o porquê". Documented in
  the same change; per ADR-0191's precedent the verifier assertions are its
  tests, and the go-ahead is quoted here and in the PR body.)
- Date: 2026-09-15
- Related: ADR-0191 (the five jobs, Linux-only, the required-check names),
  ADR-0131 (the invariants that travelled with them), ADR-0137 (`make
  doc-checks` as the wiring point), PRD Part A §4 Phase 11 C.1, and
  `.github/AGENTS.md` "The PR gate's invariants"
- Supersedes / amends: nothing. This records a **refused** change to
  `checks.yml`'s triggers, with the evidence that refused it, so the next
  session does not re-open it from a smaller sample.

## Context

The question on 2026-09-15 was whether CI could run "only on `main`" — i.e.
drop `checks.yml`'s `pull_request` trigger and keep the post-merge one. The
measurement went the other way, and it is the reason this ADR exists.

The `main gate` ruleset (id 23377340) enforces `deletion`,
`non_fast_forward`, `pull_request` and `required_status_checks` (the five
contexts: `checks`, `python-tests`, `core-unit-tests`, `ui-tests`,
`headless-ui-tests`) — and carries
`bypass_actors: [{actor_id: 5, actor_type: RepositoryRole, bypass_mode:
always}]`, so a repository admin, and any token acting as one, ignores the
whole ruleset. Two values matter here: `allowed_merge_methods` is `[merge,
squash, rebase]`, not squash-only, and `strict_required_status_checks_policy`
is `false`, so nothing forces `main` to be up to date with the head that was
tested.

Of the last 60 commits on `main`, measured 2026-09-15:

| How it landed | Count | What gated it |
|---|---|---|
| squash-merge, `(#N)` in the subject | 49 | the `pull_request` run, on the tested head |
| PR via merge-commit or rebase-merge | 7 | the `pull_request` run on the **head**, never on the result |
| pushed straight to `main`, no PR at all | 4 | **only** the `push` run |

The four direct pushes are three historical `chore: regenerate
docs/community-packs.md…` commits from `github-actions[bot]` — at the time
`community-pack-catalog.yml` pushed with `PROJECT_PAT` — and one hand-pushed
ADR correction (`a2fb9ce8`). **As of #266** the catalog job opens a PR on
`chore/community-pack-catalog` instead: run 34953429060 showed `PROJECT_PAT`
is **not** a bypass actor (GH013: changes must be made through a pull
request). The seven merge-commits remain the sharper case: with
`strict_required_status_checks_policy: false`, a merge-commit onto a `main`
that moved produces a tree that never *was* the tested head, and the push
run is the only thing that ever compiles it.

Non-goals. This adds no trigger anywhere, does not touch `build.yml`
(dispatch-only, ADR-0191 §4), does not change the five jobs or their
required-check names, and does not decide the fate of
`clang-format-check.yml` (still `disabled_manually`; re-enabling it inherits
this question and gets its own answer).

## Decision

1. **`checks.yml` keeps both triggers** — `pull_request: [main]` and `push:
   [main]`, plus `workflow_dispatch`. The `pull_request` one is the gate: it
   reports the five checks the ruleset requires *before* the code can land.
   The `push` one is the **only** gate for the two paths that bypass that
   gate: a direct push (admin bypass — a hand fix; the catalog bot no longer
   uses this path, see #266) and a merge-commit/rebase-merge whose resulting
   tree was never the tested head.
2. **The trigger is decided by those two paths existing, not by run count.**
   Measured 2026-09-15 over the last 76 `Checks` runs: 30 `push` against 46
   `pull_request`; of the 30, 17 are `cancelled` — each merge cancels the
   previous run on the same ref, the `cancel-in-progress` group being
   workflow+ref — 12 succeeded and one was in flight. The repository is
   public, so standard-runner minutes are not billed: the cost is a queue slot
   and a line in the Actions history, not money.
3. **Where the why lives.** `checks.yml`'s header comment, next to the `on:`
   block, is the copy that matters — it is what someone editing a trigger is
   already reading. `.github/AGENTS.md`'s `checks.yml` contract carries the
   second. This ADR is the third, deliberately: the session index is what
   surfaces *before* a CI edit, which is exactly the step this session nearly
   skipped.
4. **What is guarded, and what is not.**
   `scripts/checks/verify_ci_linux_only.sh` (ADR-0191) now also fails when
   `checks.yml` loses its `pull_request`-on-`main` or `workflow_dispatch`
   trigger. It deliberately does **not** freeze the `push` trigger: that is
   the half §5 expects to be revisited, and a guard against removing it would
   freeze a decision that has explicit re-opening conditions.
5. **Re-opening this decision requires one of:** the catalog workflow stops
   pushing to `main` directly; merge-commits stop being used (ruleset
   `allowed_merge_methods: [squash]` **and**
   `strict_required_status_checks_policy: true`); or a hand push stops being
   possible. **The first is already met** — the same day, #266 moved the
   catalog job to a PR (`chore/community-pack-catalog`), after its push was
   rejected with GH013. That rejection proves *the bot's* actor is not a
   bypass actor; it does **not** prove no bypass exists, since the ruleset
   carries `bypass_actors: [{actor_id: 5, actor_type: RepositoryRole,
   bypass_mode: always}]` and whether that covers the owner's own push is
   unverified. So the run's remaining job is the merge-commit/rebase tree, and
   the decision above stays until the ruleset moves to squash-only + strict —
   at which point dropping the trigger becomes defensible, and is a decision
   for whoever makes that ruleset change.

## Consequences

- `checks.yml`'s history shows two runs per merged PR, and during a burst of
  merges most of the `push` ones end `cancelled`. That is the price of
  covering the bypass paths, and it is visible noise, not a cost — nothing is
  billed and nothing is queued behind it.
- The post-merge net is *intermittent* by construction: it does not survive a
  burst of merges (17 of 30 cancelled). The commits it actually protects are
  the spaced-out ones — a hand fix, a merge-commit tree — which is where no
  other gate exists. Since #266 it no longer protects catalog regenerations:
  those go through a PR now, and are gated by the `pull_request` trigger like
  anything else.
- The catalog's move to a PR **was** taken the same day, by #266, and it was
  not a run-count decision: branch protection rejected the job's own push
  (GH013, run 34953429060), so the job had nowhere to push. The cost this ADR
  priced at "5 jobs per regeneration against 1 push run" was therefore paid
  for a reason that is not this trigger's cost — which is exactly why the
  trigger's justification has to be restated in terms of the *remaining*
  bypass path (§Decision 1, §5) rather than left implicit as "the bot needs
  it".
- A future session that finds the push run "redundant" — as this one did, from
  a 40-commit sample that happened to be squash-only — has the counter-sample
  in the header comment and in §Context here.
- The deliverable of this ADR is two comments and one verifier assertion. No
  workflow file changes.

## Alternatives

- **Drop `push: [main]`** (the literal request). Rejected: it leaves the
  4-of-60 direct pushes — none of which any human reviewed — and every
  merge-commit tree ungated, with `gh workflow run checks.yml --ref main` as
  the only mitigation.
- **Drop `pull_request`, keep `push`** ("only on `main`"). Rejected: it
  inverts the gate (verify after landing instead of before), and the ruleset's
  required checks would never report on a PR, blocking every merge until
  `required_status_checks` is deleted from the ruleset — and once deleted,
  `main` has no gate at all.
- **Keep `push`, but skip the run when the commit came from a PR whose head
  already reported** (a first job calling `gh api commits/{sha}/pulls`).
  Rejected: an extra API dependency inside the gate, and a check that skips
  itself reads as green while proving nothing.
- **Constrain the ruleset (squash-only + `strict_required_status_checks_policy:
  true`) and then drop the push trigger.** The honest version of the original
  request, and not refused on principle — refused *as a pair*, at the moment
  it was asked: the direct-push half then still existed (the catalog bot had
  pushed that way three times in the preceding 60 commits), so the push run
  would have stayed anyway. #266 removed that half the same day for its own
  reason, which makes this alternative closer to reachable than when it was
  written — the remaining half is the merge-commit tree, and it needs the
  ruleset change first. Revisit per §5.
