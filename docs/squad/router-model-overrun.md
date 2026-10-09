# The router model overruns its budget without ever answering

Status: open. Measured 2026-10-05 against `docs/squad/workflows/dynamic.graph.json`
and the `agent-squad` plugin 0.1.3.

## What was measured

The `router` node ran on `claude-grok-4.6` and spent its entire node budget on
exploration. It never emitted the JSON object its role requires, so the run
produced no artifact and no children.

| Run | Model | Turns | Elapsed | Spend (cap) | Tokens in | Tokens out | `cache_read` | Artifact |
|---|---|---|---|---|---|---|---|---|
| `run-20261005-195102` | `claude-grok-4.6` | 41 | 84 s | $2.01 ($2) | 400 543 | 4 051 | 0 | **none** |
| `run-20261005-194416` | `opus` | 19 | 72 s | $0.78 ($2) | 662 394 | 5 533 | 593 581 | `artifacts/router.1.json` |

The verdict was `node_spend_cap`; `artifacts/` came out empty.

Three details say the model never reached its answer step, rather than answered
badly:

- **Zero write.** All six `progress` events carry `Read` or `Grep`. No assistant
  message was ever finalized into output.
- **~99 output tokens per turn** (`4 051 / 41`). That is the framing of a tool
  call, not prose or a draft object.
- **The context only grew**: 15 136 → 31 171 → 33 393 → 37 183 → 59 991 → 64 667
  tokens. It read more and more of the repository and never narrowed.

The pattern reproduces: `run-20261005-195313` walked the same path (Glob, then
`Read` at 31 k, 33 k and 37 k tokens) before its budget was raised.

## Why it happens

Two mechanisms, and only one of them is the model's.

**1. The gateway bills no prompt cache.** The same SDK, with the same call
shape, reported `cache_read: 593 581` on `opus` and `cache_read: 0` on
`claude-grok-4.6`. The router re-sends a ~16 k system prompt on every turn
(`claude_code` preset + `_contract.md` + `prompts/router.md` + the skill
listing), and every one of those turns pays full price. Measured per-model cost
for one identical prompt: `claude-deepseek-v4-flash` $0.0730,
`claude-grok-4.6` $0.0672, `claude-deepseek-v4-pro` $0.0652 — all within 11% of
each other, so **swapping the model does not help**. Cost is dominated by the
uncached prefix.

**2. The router role has no stopping rule.** `prompts/router.md` says what to
return and that a request must fit the limits. It sets no turn or spend
discipline, and the CLI's appended tail (`Read it and CLAUDE.md. Decide what
work it needs`) reads as an invitation to survey the repository first. Nothing
in the role tells the model to stop reading and answer. On a caching model the
survey is cheap enough that this never became visible; on this gateway it is
the difference between finishing and not.

The role is also the worst place in the graph to fail: a child that overruns is
one lost child, while a router that overruns loses the whole run.

## The correction

Applied:

- `router.budget` → `{turns: {min: 3, max: 80}, spend: {min: 0.05, max: 8}}` and
  `deadline: 1200` (was `max: 30`, `$2`, `600`). Headroom only: this buys turns
  without addressing either mechanism.
- A **stopping rule** appended to the router's `instructions`, after the role
  text: read what you need and no more, spend at most about fifteen turns of
  reading, then return the object — or `needs-human` naming the question that
  would settle it. This targets the observed behavior directly.
- The router moved to DeepSeek and `allow.models` leads with
  `claude-deepseek-v4-flash` (a child that pins no model takes
  `allow.models[0]`), owner instruction of 2026-10-05. This is a preference,
  not a fix: per-token cost is within 11% across the three models.
  **Superseded the same day, owner: "use o deepseek v4 flash, nunca o PRO."**
  The router is `claude-deepseek-v4-flash` and `claude-deepseek-v4-pro` is out
  of `allow.models`, so no request can pin it. Not listing it on the node is
  not enough — run 11 had two of its four children accepted on Pro before the
  run was killed.
  **Superseded again on 2026-10-07**, owner: "use o sonnet para programar":
  `allow.models` now leads with `claude-sonnet-5-5`, so a child that pins no
  model takes Sonnet; the router node itself is unchanged. The Pro exclusion
  above still holds.

## Closed by run-20261005-200019 and run-20261005-200820

- **The gateway does cache.** The router reported `cache_read: 836 480` of
  `tokens_in: 892 856` on `claude-deepseek-v4-flash` (run 11) and
  `cache_read: 698 240` of `760 222` on `claude-deepseek-v4-pro` (run 10).
  The `cache_read: 0` in the table above is specific to `claude-grok-4.6`, not
  to the gateway. Node budgets can assume a cached prefix.
- **The stopping rule works.** Both runs finished the router inside budget:
  26 turns / `$0.86` and 28 turns / `$0.83`, against a cap of 80 turns / `$8`.
- **A second defect surfaced, in the same node**: `squad/envelope.py:76` admits
  a `workspace` only by exact membership, and the router read the allowed
  `worktrees/child` as a directory prefix, so run 10 lost both children to
  `workspace_outside_allow` and the verdict was `no_dispatch`. The role text
  now says `workspace` is a label to copy verbatim. Run 11 dispatched four
  children on the corrected text.

## Reproduction

```bash
cd <repo>
ANTHROPIC_BASE_URL=http://localhost:8016 \
  uv run --project ~/.claude/plugins/cache/agent-squad/agent-squad/0.1.3 --extra sdk \
  squad start --target "$PWD" "implemente e2e os itens do PRD, incompletos, que nao dependem de mim"

# then read the router's turn count, spend and cache_read
python3 - <<'PY'
import json
p = "runs/<run-folder>/record.jsonl"
for e in map(json.loads, open(p)):
    if e.get("event") == "outcome" and e.get("node") == "router":
        print({k: e.get(k) for k in ("outcome", "reasons", "spend", "turns",
                                     "tokens_in", "tokens_out", "cache_read", "model")})
PY
```

## Related

- `docs/squad/workflows/dynamic.graph.json` — the graph, and the `description`
  that records why each value is what it is.
- The two earlier defects on the same node, both fixed the same day: a missing
  `instructions` field (the role text that names the spawn fields) and Anthropic
  models in `model`/`allow.models`. The second is no longer a defect: the squad
  process reaches Anthropic directly, and the graph's `description` records that
  coding children run `claude-sonnet-5-5` since 2026-10-07.

## Runs 14–19 (2026-10-05): the node's own budget, then the children's, then the price

Each run moved the wall one step further out. The numbers, in the order they
were measured:

| run | verdict | what it cost | what it proved |
|---|---|---|---|
| `run-20261005-204610` | `node_spend_cap` | router $0.57 + two routing children at $1.62 and $1.22 | the `route` node's spend cap was **4** while one child may be granted 4 — the router's own pass could not fit beside a child |
| `run-20261005-205157` | `node_turn_cap` | the same shape, $2.35 | the same arithmetic one line above: the turn cap was **60**, and the router's 14 turns plus two children at 36 reached 86. Fixing only the spend half bought this run |
| `run-20261005-205624` | `node_turn_cap` | three children at 41, 46, 51 turns, $1.18–$1.40 each | the router was sizing `turns` from the 40–50 the role text quoted as the observed norm, which cannot finish a three-bug TDD task. **All three left real edits in their worktrees and returned no artifact**: `error_max_turns` ends the session mid-work, so a child at the wall never gets the turn that would have returned its object |
| `run-20261005-210625` | `merge_conflict` | $0.84 router + three children at 61/63/78 turns | **the first run whose children worked** — three passed with artifacts. It still failed, because two of them had taken the same pair of bugs (#902/#904) and both edited `MacOS/MacOSKeyManager.mm` |
| `run-20261005-213328` | `node_spend_cap` | one child $3.28/61 turns, one passed at $2.31 | the router **partitioned correctly** (two disjoint named slices, no overlap) and then priced a turn at the three cents the role quoted — the `claude-deepseek-v4-flash` rate, while it had chosen `claude-grok-4.6` at high effort, where a turn costs $0.054 |
| `run-20261005-214446` | **completed** | $7.77 in all: router $0.63, children $2.22 / $2.37 / $1.37 / $1.19 | the first run to reach a verdict with every child inside its budget and **no `merge_conflict`**: four disjoint slices (Core/ bugs, UI/+scripts/ bugs, the F8.4 `border.png` lint, the CI live-validation flag), each with its own stopping rule. One child landed a real bug the board did not have (`#905`, the content_id hasher's DEL escape); one returned `fail` with `no_open_bugs_in_slice` and **no invented work**; one returned `needs-human` because the spec sanctioned the behavior its brief called a bug; one returned `pass` on a deliberate *not cleared* rather than flipping a CI flag |

Two lessons that are not about the node's own configuration:

- **A child's cap is not a place to save.** `error_max_turns` and
  `error_max_budget_usd` end the call; the child cannot be asked to "return
  before the wall" because the wall removes the turn it would have returned on.
  Budgets have to fit the work, and the work has to be one deliverable.
- **Children see only the branch.** They branch from `HEAD`, so work that lives
  in someone else's worktree, or uncommitted in the checkout, is invisible to
  them - and two children will independently fix the same bug. Run
  `git worktree list` before launching and put what those branches carry into
  the steering exclusions.

Three more from the run that completed:

- **The steering prompt carries the queue's state, and nothing else does.** The
  board emptied at 00:42Z and 00:46Z; the run started at 00:44Z, between the
  two closes. The router had no way to know, so it spent two of its four
  children on bug sweeps of an empty board - one of which came back after
  auditing for a defect nobody had filed, which was the right call but not the
  one it was dispatched for. Say "the board is empty" (or name the open issues)
  in the prompt that starts the run.
- **A brief that names a bug can be wrong about the spec, and the child should
  say so.** The F8.4 child was told to make a bare root `border.png` a lint
  error; MEP-v1 §5.4 says hosts **MAY** accept the file, so the error would have
  invented a rule. It returned `needs-human` with the quotes instead, and the
  coordinator's ruling - keep the discovery, drop the error - is what shipped.
  The brief is a hypothesis; the spec is the evidence.
- **`pass` is not the only good verdict.** Two of the four children passed; the
  two that did not were the two that told the truth about their slice (nothing
  to take; the condition is not cleared). A run whose value is measured by how
  many children return `pass` will buy invented work.

By `run-20261005-214446` the routing works as designed, and the limit is the
queue rather than the graph: the bug board reached zero open issues, the run
that completed found one more by audit alone, and the remaining PRD rows are
owner- or hardware-gated (F6.5, F9.18, F12.11, F14.8, P.8, P.11-P.12, S10.b).
See `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` §4.
