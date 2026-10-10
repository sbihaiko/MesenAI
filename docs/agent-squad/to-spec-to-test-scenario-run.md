## Problem Statement

A test scenario can now be turned into a `gui-test/1` script and tracked as GitHub issues (the `/to-test-scenario` skill, #1223–#1225), but nobody can **execute** it and get the outcome back where the team looks. A QA owner who says "run the pad-only scenario" gets no run command, no per-case screenshot, and no verdict on the case's issue: they would read run files by hand and copy results into 67 issues, which is the manual work the infrastructure exists to remove. Today a script runs only inside pytest cases of the agent-squad.

## Solution

A single command runs a script end to end on the GUI test infrastructure and writes one results file. A separate, model-free write-back command reads that file and updates the scenario's issues: a verdict per case, a screenshot where it matters, a summary on the parent. The run opens one bug when it has failed steps; the write-back never does. Done well, "run this scenario" ends with the parent issue and each case issue showing what happened, with evidence, and nothing to copy by hand.

## User Stories

1. As a QA owner, I want to run a whole script with one command, so that I do not write pytest code to execute a scenario.
2. As a QA owner, I want the command to validate the script against the adapter's advertised capabilities before launching, so that an unsupported action is rejected naming what is missing.
3. As a QA owner, I want the run to launch the emulator through its target adapter with the script's fixtures, so that every batch starts from a known state.
4. As a QA owner, I want each batch to run in a fresh process, so that one crash or hang does not poison the next batch.
5. As a QA owner, I want a screenshot of the application window after every automated step, so that every verdict has evidence behind it.
6. As a QA owner, I want a step to end `passed`, `failed`, `needs review` or `pending`, so that doubt and human residue are never counted as a pass or a failure.
7. As a QA owner, I want a failed step to carry expected and observed values, so that a failure explains itself.
8. As a QA owner, I want a manual step to end `pending` with its instruction, so that hardware, audio and haptic cases stay in the same script and results.
9. As a QA owner, I want `nav.goal` and sweep steps marked `pending` in CI and run locally with Jev, so that no model key is needed in CI (ADR-0271 Decision 6 A).
10. As a QA owner, I want to run one batch or one case by id, so that I can re-run a failure without the whole scenario.
11. As a QA owner, I want to run a variant (for example windowed or full screen) by name, so that the matrix is covered without repeating cases.
12. As a maintainer, I want a run that cannot load the native core or the binary to fail, never skip, so that green always means steps executed.
13. As a maintainer, I want a run that executed no step reported as a failure, so that an empty run is never a pass.
14. As a maintainer, I want the process to exit non-zero when any step failed, and zero otherwise, so that CI can gate on it.
15. As a maintainer, I want `needs review` and `pending` steps to leave the exit code at zero and be listed in the output, so that they never turn a build red and never go unseen.
16. As a developer, I want the run to write one results file in the `gui-test-results/1` format, including the run's bug URL, so that the write-back can link it without guessing and the run and the write-back can be built and tested apart.
17. As a developer, I want the results file to carry run id, adapter name, script path/sha256/name, platform, build SHA, start time and status, so that a run is reproducible and comparable.
18. As a developer, I want each step result to carry id, batch, mode, role, expect, verdict, capture and held, so that the write-back needs nothing else — reading the expected value from `expect` and the observed value from the verdict text.
19. As a developer, I want a malformed results file rejected with the reason, so that a broken run never half-updates issues.
20. As a security reviewer, I want every screenshot to pass the privacy gate before it leaves the machine, so that nothing private reaches an issue.
21. As a security reviewer, I want a screenshot not released by the gate to be named as withheld in the result, never uploaded and never dropped silently, so that a reviewer knows evidence exists.
22. As a QA owner, I want screenshots uploaded only for `failed` and `needs review` cases, so that a green run of 67 cases uploads nothing and the evidence branch stays small.
23. As a QA owner, I want uploaded screenshots to render inline in the case's issue, so that I can judge the case without opening files.
24. As a developer, I want the run to open exactly one bug for a run with failed steps, listing all of them, only when I ask for it, so that test and CI runs never flood the board.
25. As a developer, I want that bug filed through the repo's bug helper, so that its fields and board status are correct.
26. As a QA owner, I want the write-back to be a separate command that reads the results file, so that a 15–45 minute run never holds an interactive session and CI can write back without a model.
27. As a QA owner, I want a `passed` case commented and its issue closed, so that the open list is what still needs a person or a fix.
28. As a QA owner, I want a `failed` case commented with expected, observed, variant and screenshot, and left open, so that nobody hunts for it.
29. As a QA owner, I want a `failed` verdict on a closed case to reopen it, so that a regression never hides behind a closed issue.
30. As a QA owner, I want a verdict commented only when it changes (or on the first run), so that ten runs do not leave ten identical comments.
31. As a QA owner, I want `needs review`, `pending` and manual cases left open and listed on the parent, so that human residue is visible.
32. As a QA owner, I want one summary comment on the parent per run with counts per verdict, run id, script sha, platform, build SHA and a link to the run's bug, and, for a partial run, the selection it covered, so that I read the whole outcome in one place and never mistake one case for the whole scenario.
33. As a QA owner, I want the parent never closed by the write-back, so that a green count never closes a scenario without a person reading it.
34. As a developer, I want the write-back to find a case's issue through the map file the publish writes, so that it never guesses by title.
35. As a developer, I want a case in the results with no entry in the map reported and skipped, so that a stale map never writes to the wrong issue.
36. As a developer, I want the write-back paced with a back-off on rate-limit responses and resumable, so that 67 comments do not trip the API's secondary limits and a half-done write-back finishes on the next call.
37. As a developer, I want everything written to GitHub in en-US, with no co-author or tool-attribution line, so that it follows the repo rules.
38. As a tester, I want the application closed and its settings and fixtures restored after the run, so that the next run starts clean.
39. As a developer, I want the run to work from the agent-squad's own run record, so that the dashboard shows the same steps, screenshots and verdicts.
40. As a QA owner, I want the pilot (the pad-only Home batch) to run for real with the native core and leave its verdicts and screenshots on the scenario's issues, so that the flow is proven, not assumed.
41. As a developer, I want nothing in the run or write-back to encode the pad-only script's screens, buttons or case count, so that a second scenario needs no change.
42. As a maintainer, I want a run's results file kept with the run's artifacts, so that a write-back can be repeated later from the same file.

## Implementation Decisions

- **Two commands, one contract.** A *run command* (script in, results file out) and a *write-back command* (results file and map file in, issue updates out). They share only the results file schema; neither imports the other. The run command opens the single bug; the write-back never opens one.
- **Results file (frozen, shared with the scenario-publish work): format `gui-test-results/1`, as the merged run command writes it.** Run data at the root (`run_id`, `adapter`, `adapter_name`, `script` {path, sha256, name}, `platform`, `build_sha`, `started`, `status`, `reasons`); `batches[]` each with `id`, `verdict`, `teardown`; `steps[]` each with `id`, `batch`, `mode`, `role`, `expect`, `verdict`, `capture` (path or null), `held`; and `bug` {`filed`, `url`, `reason`}, the run's single bug or why none was filed. A verdict is a free string whose kind is `passed`, `failed (...)`, `needs review`, `pending` or `skipped (...)`; a `skipped` step was not executed. A `selection` field records a partial run (batch, case or variant). The failure text lives in the verdict; a comment derives expected from `expect` and observed from the verdict text. No field is added or renamed without amending both users.
- **Run command behavior.** Validate the script against the adapter's capabilities (reject unsupported actions naming them); launch the emulator adapter through the supervised runner (one process per batch); after each automated step wait for its condition, capture the application window, then decide by the objective check, falling back to the vision verdict only where no check decides. Exit non-zero on any failed step, on a missing core or binary (never skip), and on a run with no executed step.
- **Screenshots.** A capture is taken after every automated step and kept in the run's artifacts. Only captures of `failed` and `needs review` steps may leave the machine, each through the privacy gate and the evidence store (a dedicated evidence branch, so the image renders inline in an issue; CI artifacts expire and do not render). The gate never blocks a run: a capture it does not release is recorded as `held`/`withheld` and the run continues. Pushes to the evidence branch are serialized with a lock so concurrent runs do not collide. Where captures live is decided by the GUI test squad spec's step 6 and the existing evidence store; this spec uses it unchanged.
- **Bug ownership.** The run command may file one bug per failed run through the repo's bug helper, via the existing failure-report path, listing every failed step; filing is **opt-in** (off by default, so a test, acceptance or CI run never pollutes the board), and a bug that cannot be filed (no network, no token) is recorded as `bug.filed=false` with the reason and never aborts the run. The results file carries the bug's URL; the write-back links it on the parent summary and opens nothing.
- **Write-back transitions.** First run: comment every case's verdict. Later runs: comment only on a verdict change. `passed` comments and closes; `failed` comments (expected, observed, variant, evidence) and stays open, and **reopens** a closed case; `needs review`, `pending` and manual stay open and are listed on the parent. One summary per run on the parent; the parent is never closed.
- **Mapping, pacing and resume.** The write-back resolves a case's issue only through the map file of the publish step; an unmapped case is reported and skipped. Requests are paced with a back-off on secondary-limit responses. Progress (which run id was written to which case, and the last verdict commented) is recorded **in the map file**, so a repeated call resumes and a later run comments only on a change; the results file is never modified.
- **Partial runs.** When the results file records a selection, the write-back updates only the selected cases and its parent summary says "partial run: <selection>", with counts for the selection only; it never presents a partial run as the health of the whole scenario.
- **Selection.** The run command accepts a batch, a case id or a variant to narrow the run; the results file records what was run.
- **Language and attribution.** Everything written to GitHub is en-US; no co-author line and no tool-attribution line.
- **Reuse over new code.** The supervised runner, the privacy gate, the evidence store, the failure report and the run record already exist in the agent-squad; this work wires them behind two commands and adds the results file and the write-back. It changes neither the script format nor the adapter interface.

## Testing Decisions

- A good test drives a command through its external contract: a script and a fake adapter in, a results file and recorded GitHub calls out; a results file and a fake GitHub runner in, comments, closes, reopens and the recorded calls out. No test asserts on internal helpers.
- **One seam**, as agreed: the run command with the adapter and the GitHub and evidence runners injected as fakes; the write-back is tested over a results file given as input. Real GitHub and the real emulator appear only in the end-to-end proof.
- Cases: a bug that cannot be filed does not abort the run; no bug is filed by default; the bug URL reaches the write-back through the results file; a partial run produces a "partial run" summary with selection-only counts; progress survives in the map file and the results file is untouched; the evidence push is serialized; capability rejection before launch; one process per batch; a screenshot per automated step; each verdict path including `needs review`, `pending` and manual; non-zero exit on a failed step, zero with only `pending`; a missing core fails, never skips; an empty run fails; the privacy gate withholding a capture; screenshots uploaded only for `failed` and `needs review`; exactly one bug for many failed steps; a malformed results file rejected; first-run comments versus change-only comments; close on pass; **reopen** on a later failure; unmapped case skipped and reported; back-off and resume on rate limit; the write-back opening no bug in any mix of verdicts.
- Prior art: the agent-squad's GUI test suites (the end-to-end cases with a fake adapter, the report and gate tests, the contract suite), and the fake-`gh` shell tests of the squad-goal scripts. The fork's test command already runs every GUI test suite file.
- **Acceptance by a real run:** the pad-only script's Home batch executed for real with the native core loaded, its results written back to the scenario's issues, with screenshots on the failed or needs-review cases and a summary on the parent; the results file kept with the run.

## Out of Scope

- Converting a Markdown scenario to a script, planning and publishing issues (#1223–#1225).
- New target adapters, new actions or checks, and the script format itself.
- Executing manual cases; they stay with a person.
- The dashboard's rendering of a run.
- Windows and Linux pad paths.
- Auto-closing the parent issue.
- Deciding the evidence-branch location; it follows the GUI test squad spec's step 6.

## Further Notes

- Overlaps tickets already open: the run command is #1222, the write-back is #1226, the end-to-end proof is #1227. This spec is the fuller statement of that part; the tickets stay the unit of work, and any change here must be mirrored in them.
- The squad's frontier dispatched #1223 (a spec issue) as if it were a ticket and its child started building the whole skill module in parallel with #1224–#1226. To avoid a repeat this spec is published **without** `ready-for-agent`; add it only if a child should build from the spec rather than from the tickets.
- Review 2026-10-10 folded in: results schema aligned with the merged run command (bug URL in the file, `skipped`, selection); bug filing opt-in and non-fatal; resume state in the map file; partial-run summaries; the gate never blocks and evidence pushes are serialized. A first acceptance round filed 9 duplicate bugs on the real board, which is why filing is opt-in.
- Related: GUI test squad spec, ADR-0271, ADR-0272, `/to-test-scenario` spec (#1223).
