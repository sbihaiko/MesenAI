# ADR-0199: The community-pack classify step is a direct Gemini API call, not the Claude Code Action

- Status: accepted (2026-09-16, at the user's direction: "vamos de gemini 3.8 flash, migra o classify" — as pre-ADR-0199 that is not counted as an amendment of the governing rule; implemented in the same change, whose unit test is `scripts/test_gemini_classify.py`)
- Date: 2026-09-16
- Related: ADR-0138 (the classify step is the only place an LLM runs, and the prompt/schema single-source contract), ADR-0146 (an accepted pack auto-installs on the client), ADR-0148 (listability), ADR-0170 (the project's first Gemini integration), issue #148
- Supersedes / amends: nothing. No accepted ADR decided classify's vendor, model or credential before this one.

## Context

The classify step has never used a tool. From the moment the prompt moved to
`.github/ai/validate-classify.md`, every piece of evidence it needs is
rendered into the prompt as a bounded `{{PACK_BRIEF}}`
(`scripts/classify_pack_brief.py`) precisely so the model cannot open
`pack_download.bin` or a 26 MiB `hires.txt` — the reason the step carries
`timeout-minutes: 15` (issue #148). The `anthropics/claude-code-action` step
had grown a long denial list to say the same thing
(`--disallowedTools Bash,Read,Write,Edit,MultiEdit,NotebookEdit,WebFetch,
WebSearch,Task`), and before that had granted MCP tool names that turned out
never to exist in that action at all (confirmed against its own `src/mcp/` on
2026-08-27). A boundary enforced by a list is a boundary that can be
misconfigured silently: GitHub Actions only warns on an unknown `with:` key,
and run 33036041774 showed the earlier `allowed_tools` inputs being ignored
outright.

On 2026-09-16 the user asked to move the pack automation off Claude, first to
Grok or OpenAI, then to DeepSeek, and finally to Gemini. The cost was measured
before deciding, on the real fixtures in `.cache/validate-local/`:
prompt ≈ 2,584 tokens, schema ≈ 287, `PACK_BRIEF` 1,300–2,550 across the 11
submissions measured (the script's own cap is 80,000 characters, never
reached), output ≈ 900 tokens — call it ~5,200 in / ~900 out per pack. At that
size every candidate lands between US$0.0006 and US$0.02 per pack, and the
whole 15-pack catalog between US$0.01 and US$0.35. **Cost could not decide
this**, because the incumbent was not merely cheap: classify ran on the
`CLAUDE_CODE_OAUTH_TOKEN` subscription, so the marginal cost was zero.

Alternatives and why they lost:

- **DeepSeek** — the cheapest, and the only one that could not enforce the
  contract. Its API accepts `response_format: {"type":"json_object"}` and
  rejects `json_schema` outright (HTTP 400, "This response_format type is
  unavailable now"). Verdicts drive ADR-0146's auto-install, so a shape
  guarantee cannot be traded for a retry loop. The beta strict tool-calling
  surface would enforce the schema on *tool arguments* — a real option, but a
  designed-in workaround.
- **xAI (Grok)** — `json_schema` is supported, but there is no official agent
  action, so the dormant autofix would have no path if it were ever revived.
- **OpenAI** — technically equivalent to Gemini here (strict `json_schema`,
  an official agent action), no advantage that Gemini did not also have.

Gemini won on three counts: the existing schema survives nearly intact
(`oneOf` is accepted and interpreted as `anyOf` — the same edit OpenAI would
have required, done by the API instead); `google-github-actions/run-gemini-cli`
is an official action, so the autofix keeps a path; and the project already
speaks this API (Fase 10, ADR-0170).

Non-goals. This does not migrate the autofix (dormant behind
`LIVE_VALIDATION_ENABLED`), does not migrate the local harness
(`scripts/validate_pack_local.sh` keeps running the `claude` CLI), does not
touch the prompt or the schema, and does not change the verdicts' downstream
contract.

## Decision

1. **The classify step is a `run:` step that calls the API.** It writes the
   rendered prompt and schema to `$RUNNER_TEMP` and calls
   `scripts/gemini_classify.py`, which POSTs to
   `https://generativelanguage.googleapis.com/v1beta/interactions` with the
   `x-goog-api-key` header and a body of `model`, `input`, and
   `response_format: {type: "text", mime_type: "application/json", schema:
   …}`. **The body carries no `tools` key**: "the model has no tool" becomes a
   property of the request rather than a list of denials.

2. **The model is a short chain, pinned in the script**: `gemini-3.8-flash`
   first, then `gemini-3.6-flash` (`DEFAULT_MODEL` /
   `DEFAULT_FALLBACK_MODELS`, overridable with `--model` /
   `--fallback-model`). Measured on 2026-09-16, the same day this was
   written: the 3.8 answered `HTTP 500 "currently experiencing high demand"`
   and `429 ... limit: 20, model: gemini-3.8-flash` for minutes at a time
   while the 3.6 answered the identical request in **7 seconds**. A spike on
   one model must not strand a submission in "Em validação", so the step
   walks the chain. Only a *model-level* failure moves it along — 404, 429,
   5xx, timeout, transport — because a 400/401/403 would be answered
   identically by the next model and aborts the run instead. Each model gets
   `--attempts` tries (2) with backoff before the chain moves on, and the
   whole chain respects `--deadline` (600 s) so the step's own
   `timeout-minutes: 15` is never what stops it — that would leave no
   diagnostic in the log. The model that answered is logged, so a run that
   only succeeded by falling back is visible after the fact.
   Changing the chain is an edit to a default, not a config knob, so the
   models in use are visible in the diff and in the ADR that precedes the
   change.

3. **The prompt and schema stay single-sourced.** `.github/ai/validate-classify.md`
   remains the only copy, now documented as provider-neutral: both invokers
   render the same text and the same JSON Schema, and changing the model on
   either side is a change to that invoker, never to the file.

4. **The credential is `GEMINI_API_KEY`**, declared in
   `community-pack-validate.yml`'s `workflow_call.secrets` and passed
   explicitly by both callers. `ANTHROPIC_API_KEY` and
   `CLAUDE_CODE_OAUTH_TOKEN` stay declared because the dormant autofix step
   still reads them.

5. **The step output keeps the name `structured_output`**, so the sanitize
   step and everything behind it (recipe assembly, the verdict, the identity
   check) is untouched. `scripts/gemini_classify.py` re-serializes through
   `json.dumps`, which makes the sanitize step a pass-through on every run;
   it is kept because it is the single writer of
   `$RUNNER_TEMP/mep_classify_clean.json`, the file those readers consume.

6. **The verifier changes with the boundary.** `general.check_claude_action`
   is replaced by `general.check_classify_is_tool_free`, which asserts the
   classify step names `scripts/gemini_classify.py`, reads `GEMINI_API_KEY`,
   delegates to **no** action at all, and mentions no tools. The
   top-of-file secret comment check now requires `GEMINI_API_KEY` alongside
   the two Anthropic names.

7. **The Gemini free tier is not used.** Google's own pricing page marks
   free-tier content as "used to improve our products" and paid-tier content
   as not. Classify reads third-party submissions whose text the pipeline
   treats as untrusted data; routing that into model training is a decision
   about other people's content, so the paid tier is the only tier considered.

## Consequences

- **Marginal cost stops being zero.** ~US$0.0073 per pack on
  `gemini-3.8-flash` (promotional pricing through 2026-12-31; it doubles
  after), ~US$0.11 for the current 15-pack catalog. Gemini bills thinking
  tokens as output, and the smoke test on 2026-09-16 measured it directly:
  **1,276 thinking tokens against 70 output tokens** on one model
  (`gemini-3-flash-preview`), 458 against 49 on another (`gemini-3.6-flash`).
  Thinking is an order of magnitude of the bill, so the cost per pack tracks
  the model that answers, not the size of the answer.
- **The free tier is not usable even if it were acceptable.** A free-tier
  key answered `429 ... Quota exceeded for metric:
  generate_content_free_tier_requests, limit: 20, model: gemini-3.8-flash`.
  Twenty requests per model per day, with no paid fallback, is a pipeline
  that stops after twenty packs — on top of §7's training-use problem.
- **A new required secret.** `GEMINI_API_KEY` must exist in the repository and
  be reachable by both callers, or classify fails and the item stays in
  "Em validação". `PROJECT_PAT` is still needed for everything else.
- **CI and the local harness now run different models.** A pack triaged with
  `scripts/validate_pack_local.sh` is judged by Claude; the same pack in CI is
  judged by Gemini. The prompt is shared, the behavior is not — a local pass
  is evidence about the prompt, not a prediction of the CI verdict.
- **The legacy schema surface is a trap.** `generationConfig.responseSchema`
  (the OpenAPI subset) rejects `additionalProperties` and nested
  `oneOf`/`anyOf` with `400 INVALID_ARGUMENT`. Only the Interactions API
  `response_format.schema` shape is supported here.
- **Swapping the provider again is now a script-shaped change**: one file plus
  the secret wiring, because the prompt, the schema and the step's output name
  were all left provider-neutral on purpose.
