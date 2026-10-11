# A provider outage ended a run at its first model

Status: fixed in the fork (`agent-squad` plugin 0.2.30, branch `gui/1512` at `f25badc`).
Nothing in this repository changes: the flow here already carries the chain the fix walks.
GitHub issue #1512. Measured 2026-10-11 against `workflows/dynamic.graph.json`.

## What was measured

Five dispatches between 07:44Z and 08:00Z, each dead in about three seconds with
`$0.00` spend, reason `adapter_error`, and the same diagnostic:

    ResultError: Claude Code returned an error result: API Error: 402 Grok upstream rejected the request (exit code: 1)

The provider was exhausted, not the prompt. A second `claude-code-proxy` with
`CCP_TRAFFIC_LOG=1` on `:18799` logged one request whose upstream answer was
`{ "error": "Grok Build usage balance exhausted", "status": 402 }`, and the Grok
CLI run headless (`grok -p 'reply with exactly: OK'`) answers the same 402.

## Why it happened

- The `router` node pins one model, so the entry of the dispatch had a chain one
  entry long, and a child takes the single `coding_models` pick from the fork's
  `models.json`. A chain one entry long cannot fall through.
- The engine turned the first `adapter_error` into that node's `fail`.
  `model_retry_budget` in `models.json` budgets the fork's own shell scripts; it
  never reached the engine, which knew only the failed call.
- The router is the worst place for that: no ledger, no children, no artifact,
  and the issue is left In Progress with no work done and no retry.

## The correction (fork, not this repository)

- `squad/session.py` gains the chain rule: `model_chain(models, first)` (the
  model the node or the request picked leads the dispatch envelope's
  `allow.models`), `provider_error(err)` (an adapter error that names an upstream
  HTTP status: a 4xx/5xx under `API Error`, `upstream` or `"status"`), and
  `run_chain` / `exhausted` / `chain_message`.
- `squad/runners/agent.py` walks it for an agent node, the router entry included,
  and `squad/runners/children.py` for a dispatch child, which retries in the same
  worktree — the tree is cut for the first attempt and reused after it, and the
  outcome names the model that answered.
- An error that names no provider status still fails the node at once, and a
  chain of one keeps its own error. A chain whose every entry refuses fails once,
  with `reasons: ["adapter_error"]` and one `diagnostic.error` naming each entry
  and its error: `every model in the chain failed: opus (…); sonnet (…); haiku (…)`.

## No change to the graph

The router's pinned model stays. It is a preference that leads the chain, and
with the fall-through it is no longer a single point of failure; a node that pins
nothing takes `allow.models[0]` exactly as before. The chain the engine walks is
the `allow.models` list of the dispatch node, which is the only ordered chain this
repository declares.

## Evidence

`tests/test_model_fallthrough.py` in the fork, written before the fix. Its RED run
had four of five tests failing on the shape `assert ['opus'] == ['opus', 'sonnet']`
and on the exhausted chain reaching only one entry; the GREEN run passes all five,
together with `tests/test_dispatch.py`, `tests/test_waves.py`,
`tests/test_deadlines.py`, `tests/test_session.py` and `tests/test_runners.py`.

## Related

- `workflows/dynamic.graph.json` — the graph this was measured against.
- `router-model-overrun.md` — the earlier record of the same node, and why its
  budget and its stopping rule are what they are.
- The fix reaches a running loop through the fork's release ZIP: the fork has no
  write access to its upstream, so this note is the record, not the delivery.
