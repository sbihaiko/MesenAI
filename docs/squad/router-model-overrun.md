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
  would settle it. This targets the observed behaviour directly.
- The router moved to DeepSeek and `allow.models` leads with
  `claude-deepseek-v4-flash` (a child that pins no model takes
  `allow.models[0]`), owner instruction of 2026-10-05. This is a preference,
  not a fix: per-token cost is within 11% across the three models.
  **Superseded the same day, owner: "use o deepseek v4 flash, nunca o PRO."**
  The router is `claude-deepseek-v4-flash` and `claude-deepseek-v4-pro` is out
  of `allow.models`, so no request can pin it. Not listing it on the node is
  not enough — run 11 had two of its four children accepted on Pro before the
  run was killed.

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
  models in `model`/`allow.models`.
