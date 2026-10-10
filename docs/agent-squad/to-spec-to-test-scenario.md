## Problem Statement

The team's GUI test scenarios (the pad-only Play script, the real-app acceptance runbook) are Markdown files a person follows by hand. The squad now has a GUI test infrastructure (the `gui-test/1` script format, a target adapter for the emulator, a runner, vision and objective checks, a failure report), but nothing connects the two: converting a scenario into a script, tracking its cases on GitHub, running it, and putting each case's verdict where the team already looks. Done by hand this is slow, drifts, and loses cases (the pilot has 67). A QA owner cannot say "run this scenario and tell me which cases failed" and have every case end up as a tracked item with evidence.

## Solution

A new skill, `/to-test-scenario`, sibling of `/to-tickets`. Given a manual test scenario, it makes it executable and tracked: it produces (or reuses) a `gui-test/1` script, publishes one parent issue with one sub-issue per case, runs the script on the squad's GUI test infrastructure, and writes each case's verdict back to its sub-issue. The one bug of a failed run is filed by the run command, opt-in, never by this skill; the skill links it on the parent. `/to-tickets` slices work into tracer-bullet tickets that are built; this skill slices a scenario into cases that are **run**.

## User Stories

1. As a QA owner, I want to point the skill at a manual scenario and get an executable script, so that I do not convert 67 cases by hand.
2. As a QA owner, I want an existing script reused only when every case id is covered **and** each case's content hash matches the Markdown, so that an edited case is never silently ignored and converted work is never overwritten.
3. As a QA owner, I want the skill to report case ids present in the Markdown with no step in the script (and the reverse), and cases whose text changed since the script was made, so that no manual coverage is silently dropped or stale.
4. As a QA owner, I want the skill to ask me about any case whose action or check it cannot decide, so that it never invents an id the adapter does not advertise.
5. As a QA owner, I want a case that needs a physical pad, audio or a haptic reading marked `manual`, so that human residue lives in the same script with the same evidence.
6. As a QA owner, I want cases whose path is not what is under test expressed as goals, so that a layout change does not break every script that walks through it.
7. As a QA owner, I want the converted script validated against the adapter's advertised capabilities before anything is published, so that an unsupported action is rejected naming what is missing.
8. As a QA owner, I want the rendered Markdown regenerated from the script, never edited by hand, so that the two never drift.
9. As a QA owner, I want to review the plan (parent title, batches with case counts, manual cases, goal cases, variants, labels) and approve it before any issue exists, so that nothing is published by surprise.
10. As a QA owner, I want to choose the granularity (one sub-issue per case, or per batch), so that a 67-case scenario does not flood the board when I do not need it.
11. As a QA owner, I want one parent issue per scenario summarizing purpose, script, fixtures, variants and a case table, so that I have a single place to read the run.
12. As a QA owner, I want each case as a native sub-issue of the parent in script order, so that the case list is navigable and its progress shows on the parent.
13. As a QA owner, I want each sub-issue to carry the case id, precondition, steps, expected result, variant tags and whether it is manual or automated, so that a reader needs nothing else to understand it.
14. As a developer, I want cases labeled `test-case` and never `ready-for-agent`, so that the autofrontier does not pick a test case up as work to build.
15. As a developer, I want the parent issue to carry `ready-for-agent` and a wave, so that it follows the repo's ticket conventions.
16. As a developer, I want each case to name in `## Blocked by` the setup case it needs, and the sub-issues to follow the script's order, so that a reader sees the setup order; cases carry no `wave:NN`, since the steps of a batch are sequential and stateful, not parallel work.
17. As a developer, I want the case-id to issue-number map saved next to the script as a versioned file, so that a later run, a re-publish or another machine finds the same issues instead of creating duplicates.
18. As a developer, I want re-running the skill on the same scenario to update the existing parent and sub-issues instead of creating duplicates, so that a second invocation is idempotent.
19. As a developer, I want everything written to GitHub in en-US with no co-author or tool-attribution line, so that it follows the repo rules.
20. As a developer, I want sub-issues created through the REST API, so that the GraphQL quota is not spent on dozens of issues.
21. As a developer, I want only the parent issue added to the board, so that the kanban is not flooded by dozens of cases that already live nested under the parent.
22. As a QA owner, I want the run to start only after approval, so that a long run on a real display never begins by surprise.
23. As a developer, I want the run to go through one command that takes a script and returns one result per step, so that the skill never reimplements the runner.
24. As a maintainer, I want a run that cannot load what the adapter needs (the native core, the binary) to fail rather than skip, so that a green run always means steps executed.
25. As a maintainer, I want a run that executed no step reported as a failure, so that an empty run is never a pass.
26. As a QA owner, I want each case's verdict (`passed`, `failed` with expected and observed, `needs review`, `pending`) on its sub-issue with the variant, and an evidence link only for `failed` and `needs review` cases, so that I can judge a case without opening run files and a green run uploads nothing.
27. As a QA owner, I want a passed case closed, so that the open list is exactly what still needs a person or a fix.
28. As a QA owner, I want a failed case left open with the failure commented, so that nobody has to hunt for it.
29. As a developer, I want the one bug of a failed run opened by the runner (the run command), never by `to_test_scenario`, so that a failure never produces two bugs.
30. As a maintainer, I want `needs review`, `pending` and `manual` cases left open and listed on the parent without turning anything red, so that doubt and human residue are visible but never block.
31. As a maintainer, I want `nav.goal` and sweep cases marked `pending` in CI (ADR-0271 Decision 6 A), so that no model key is needed in CI.
32. As a security reviewer, I want screenshots to reach an issue only through the privacy gate and the evidence branch, never as a CI artifact, so that nothing private leaves the machine.
33. As a security reviewer, I want no resident or personal data in a script, issue or screenshot, so that the sensitive-by-default rule holds.
34. As a QA owner, I want a final comment on the parent with counts per verdict, run id, script sha, platform and the SHA of the build under test, so that a run is reproducible and comparable.
35. As a QA owner, I want the parent closed only by a person after reading the results, so that a green count never closes a scenario on its own.
36. As a QA owner, I want fixtures (ROM by No-Intro hash, pack, settings profile) declared in the script and placeholders reported as missing at run time, so that no hash is invented.
37. As a developer, I want the skill to stop after publishing and say so when the run command does not exist, instead of simulating a run, so that a result is never fabricated.
38. As a developer, I want the skill available to the squad's children as a vendored skill, so that an agent-squad run can invoke it.
39. As a developer, I want the skill invoked only by the user, never implicitly, so that it does not publish issues on its own.
40. As a QA owner, I want a passed case that fails in a later run to be reopened, so that a regression never stays hidden behind a closed issue.
41. As a QA owner, I want a verdict commented only when it changes (or on the first run), so that ten runs do not leave ten identical comments on a case.
42. As a developer, I want a half-finished publish (network error, rate limit) to resume from the saved map without recreating issues, and creations paced with a back-off, so that the API's secondary limits are not tripped.
43. As a developer, I want the run and the write-back to be separate steps, with the write-back reading a results file, so that a 15–45 minute run never holds an interactive session and CI can write back without a model.
44. As a QA owner, I want a second scenario (a different script or adapter) handled by the same skill with no change to it, so that generality is real and not a pilot artifact.

## Implementation Decisions

- **One new skill, `to-test-scenario`**, personal skill vendored into the agent-squad fork alongside `to-tickets`; user-invocation only. A sibling of `/to-tickets`, not an extension of it: the publishing mechanics (issue template, `## Blocked by`, `wave:NN`, REST) are reused, the slicing rules (tracer bullets, vertical slices) are not.
- **One deterministic module, `to_test_scenario`**, in the agent-squad, holding everything that is not judgment: reading a script and the Markdown's case ids, computing the gap report, building the issue **plan** (parent, sub-issues, labels, waves, `## Blocked by`, board Status), publishing the plan through an injected GitHub runner (idempotent by case id via the saved map), and turning a run's per-step results into sub-issue comments and closes, linking (never filing) the bug the run may have opened. Its two outside dependencies are injected: the GitHub runner and the run command. This is the **single test seam**.
- **Conversion Markdown → script stays model judgment**, as an instruction of the skill, validated by the existing script validator against the adapter's capabilities. The module checks coverage (every case id has a step, no step without a case); it does not parse prose into actions.
- **Script format and adapter are unchanged** (`gui-test/1`, ADR-0272). One step per case; the step id is the case id; role `under-test` by default, `setup`/`recovery` for steps that only reach a state; `manual` when no adapter can automate it; `nav.goal` only for setup, preconditions and recovery (ADR-0271).
- **Granularity is a plan option**: one sub-issue per case (default) or per batch. The parent always exists.
- **Labels**: parent `ready-for-agent` + one `wave:NN`; cases `test-case` only, never `ready-for-agent` and no `wave:NN` (the steps of a batch run in sequence on one window; a wave would imply parallel work). A case still names its setup case in `## Blocked by`. The `test-case` label is ensured idempotently with the others.
- **Sub-issues are native** (REST sub-issue relation, using the issue's numeric id, not its number). Only the parent issue joins the board (initial Status); sub-issues stay nested under it and are not added as cards. The case-id → issue-number map is stored beside the script.
- **Coverage is by content, not only by id.** Each case's text (precondition, steps, expected) is hashed; the script records the hash per step; a case whose hash differs from the Markdown is reported as stale and sent back to conversion for that case only.
- **Source of truth.** The hand-written Markdown scenario stays the human-authored source and is never regenerated. The Markdown rendered from the script is a separate generated file; no one edits it.
- **Run and write-back are separate.** The skill starts the run through the run command and returns; a write-back step (a deterministic command of the module, no model) reads the results file later and updates the issues, so it also works from CI.
- **Results contract.** The run command writes one JSON file, the same `gui-test-results/1` file the run/write-back spec freezes (`to-spec-to-test-scenario-run.md`, "Results file"). Run data at the root (`run_id`, `adapter`, `adapter_name`, `script` {path, sha256, name}, `platform`, `build_sha`, `started`, `status`, `reasons`); `batches[]` each with `id`, `verdict`, `teardown`; `steps[]` each with `id`, `batch`, `mode`, `role`, `expect`, `verdict`, `capture` (path or null), `held`; `bug` {`filed`, `url`, `reason`}; and `selection` when the run covered only part of the script (batch, case or variant). A verdict is a free string whose kind is `passed`, `failed (...)`, `needs review`, `pending` or `skipped (...)`; a `skipped` step was not executed, and a `manual` step is `pending`. A comment derives expected from `expect` and observed from the verdict text. This schema is frozen by the run command's issue and consumed unchanged here; neither side adds or renames a field without amending both issues.
- **Bug ownership.** The run command opens the single bug for a failed run through the existing failure report. `to_test_scenario` never opens bugs; it links the bug on the parent.
- **Verdict transitions.** First run: comment each verdict. Later runs: comment only when a case's verdict changes. `passed` closes the sub-issue; a `failed` verdict on a closed sub-issue **reopens** it.
- **Evidence** is attached only for `failed` and `needs review` cases, through the privacy gate and the evidence branch; a passed case uploads nothing. Where captures live is the evidence-branch decision of the GUI test squad spec's step 6, not re-decided here.
- **Map file.** `<script-name>.gui-test.issues.json` beside the script, committed with it: parent number plus case id → issue number and content hash. A publish writes it after each created issue so an interrupted publish resumes from it; creations are paced with a back-off on rate-limit responses.
- **Idempotence**: publishing twice updates the parent and existing sub-issues; a case removed from the script is reported, never silently closed.
- **Run step depends on a run command** (one script in, one result per step out, run id, script sha, platform, build SHA; non-zero on failed steps; failure, never skip, when the core or binary cannot load; an empty run is a failure). That command is tracked as its own issue (#1222). Until it exists the skill publishes, says the run is unavailable, and stops.
- **Result mapping**: `passed` → comment and close; `failed` → comment with expected/observed, leave open, linking the run's bug when the run filed one (filing is the run command's, opt-in — this skill opens none); `needs review`, `pending`, `manual` → comment, leave open, listed on the parent. The parent gets one summary comment; only a person closes it.
- **Evidence** reaches an issue only through the existing privacy gate and evidence branch; the skill never uploads a capture itself.
- **Language and attribution**: everything published is en-US, no co-author line, no tool-attribution line, per the repo rules.
- **Vendoring**: the skill is added to the fork's vendor list and the fork's change log and committed there; the installed plugin cache is not touched.

## Testing Decisions

- A good test drives the module through its public contract only: a scenario and script in, a plan out; a plan and a fake GitHub runner in, the recorded calls out; per-step results and a fake runner in, comments, closes and the run's bug linked but never filed out. No test asserts on internal helpers.
- **One seam**: the module, with the GitHub runner and the run command injected as fakes. This follows `gui_test_report` (`evidence_store`, `file_bug_with`, `report_failures` take a runner) and the fake-adapter pattern of the existing GUI test suites.
- Cases to cover: reuse only when ids and content hashes match; a changed case reported stale; reopen of a closed case that fails; no repeated comment for an unchanged verdict; resume of an interrupted publish from the map; the results-file schema accepted and a malformed one rejected; reuse when the script covers every id; gap report both ways; plan shape for per-case and per-batch granularity; labels (`test-case` never `ready-for-agent` on a case, wave computed from setup edges); idempotent re-publish with a saved map; removed case reported not closed; result mapping for each verdict; `to_test_scenario` opens no bug in any case (the runner's one bug is only linked); run unavailable → publish then stop, no fabricated results; an empty run is a failure; a missing core fails, never skips.
- Prior art: `tests/test_gui_test_e2e.py`, `tests/gui_test_helpers.py`, `squad/gui_test_report.py` and its tests, the janitor/forkclose fake-`gh` shell tests. The fork test command already runs every `tests/test_gui_test_*.py`; the new tests join that glob.
- The pad-only script is the real fixture for a coverage check (67 case ids); the module test uses a small synthetic scenario and one run against the pilot is the acceptance.
- The skill's own text (the conversion judgment) is verified by one end-to-end use on the pilot, not by a unit test.

## Out of Scope

- The run command itself (#1222) and any change to the runner, adapters or the script format, except that the results file schema above is shared between the two.
- A new target adapter or a second scenario's conversion beyond proving the skill is not pilot-specific (user story 40).
- Executing manual cases; they stay with a person.
- Changing `/to-tickets` or the squad's autofrontier.
- A dashboard view of a scenario; the existing run dashboard already draws a script.
- Auto-closing the parent issue.
- Windows and Linux pad paths (follow-ups already named in the scenario).

## Further Notes

- The skill already exists as a first draft and is vendored (fork commit "skills: add /to-test-scenario"); this spec describes the full target including the deterministic module and the idempotent publish, which the draft only sketches in prose.
- The label for cases is `test-case` (owner's choice in session, 2026-10-10).
- Review 2026-10-10 folded in: bug ownership moved to the runner; results schema frozen; content-hash coverage; reopen and change-only comments; parent-only board card; no waves on cases; evidence for failed/needs-review only; run/write-back split; resumable paced publish; map file defined.
- Open dependency: #1222 (run command). Without it the end-to-end flow stops after publishing.
- Related: GUI test squad spec, ADR-0271, ADR-0272, `/to-tickets`.
