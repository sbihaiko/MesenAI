# ADR-0275: A GUI test scenario records each run as its own issue, with one emoji row per case

- Status: proposed (2026-10-10). The owner picked the hierarchy and the bug rule (below). The format is mocked up by hand on scenario #1286 (dashboard) and run issue #1420, so it can be looked at before it is accepted. It becomes accepted when the owner approves that mock-up, and implementation then goes to fork-only tickets in agent-squad (`squad/to_test_scenario.py`).
- Date: 2026-10-10
- Related: ADR-0272 (the GUI test hook, adapter interface and script format; unchanged), ADR-0271 (navigation in GUI test runs), MesenAI #1286 (scenario `play-pad-only.gui-test`), #1419 (verdict emoji in case-record titles), #1241 (the run files bugs for failed steps), #1415 and #1418 (flaky gate cases that a trend view would have surfaced)
- Supersedes / amends: amends how a run reports results. Today a run writes per-step comments on the case sub-issues, closes the passed ones and files bugs (#1241). Under this ADR it writes one run issue, rewrites the scenario's dashboard and keeps one bug per failing case.

## Context

A test scenario is published as one parent issue and one sub-issue per case (#1286: 100 cases). A run reports by commenting on each case, so the history of a case is scattered over its comments. Nothing shows what one run produced as a whole, what changed since the previous run, or which cases flake. The sub-issue icon follows only the issue state, so the list reads "open/closed", not "passed/failed".

The owner asked (2026-10-10) for levels: the scenario as parent, children dated per execution, the tested items below them with the verdict emoji, plus totals, trends and results, and failed tests linked to their bugs.

Constraints:
- GitHub refuses more than 100 sub-issues per parent. #1286 already has 100 case sub-issues, and attaching run #1420 failed with "Parent cannot have more than 100 sub-issues".
- One issue per case per run would mint about 100 issues per run, which costs notifications and API quota.

Non-goals: the script format, the adapter and the hook (ADR-0272) do not change. Nothing here changes how a step is executed or judged.

## Decision

1. **Three levels, with only two kinds of issue.**
   - **Scenario issue** (parent). It keeps its description and its Cases table, and gains a dashboard at the top of the body.
   - **Run issue**, one per execution, labelled `gui-test-run`.
   - **Case rows** inside the run issue. A case is not an issue per run.
   - The owner's pick: "Linhas na issue do run".

2. **The scenario's sub-issues are its runs.** The existing case issues stay as permanent per-case records: one issue per case, title prefixed by the latest verdict (#1419), linked from the scenario's Cases table and from every run row. They are detached from the scenario's sub-issue list, which frees the 100 slots for runs.
   - When a scenario reaches 100 runs, the oldest closed run is detached (it stays linked from the Runs table), so the newest runs are always the sub-issues.
   - Detaching is a link change only. No issue is closed or deleted.

3. **Verdict emoji, one vocabulary everywhere** (case rows, case-record titles, run titles):

   | Emoji | Verdict |
   |---|---|
   | ✅ | passed |
   | ❌ | failed |
   | 🟡 | needs review |
   | 👤 | manual: pending, a person must walk it |
   | ⚪ | not run |

   A run's overall emoji is the worst verdict among its cases, in this order: ❌ > 🟡 > 👤 > ⚪ > ✅. ✅ means every case ran and passed.

4. **Run issue.**
   - Title: `<overall> Run <YYYY-MM-DD HH:MM> · <script> · <build_sha short>`.
   - Body:
     - date, build and result;
     - **Totals**: `✅ n · ❌ n · 🟡 n · 👤 n · ⚪ n (of N)`;
     - **Changes since the previous run**: new failures, fixed (failed before, passed now), still failing;
     - **Cases** table: emoji, case id, case record link, capture link, bug link.
   - The issue is written once, when the run ends, and closed as completed. A re-run is a new run issue. Runs are never edited after the fact.

5. **Scenario dashboard.** The body section between `<!-- gui-test:dashboard:start -->` and `<!-- gui-test:dashboard:end -->` is rewritten on every run, and nothing outside the markers is touched. It contains:
   - the latest run with its totals;
   - the **Runs** table (newest first: date, result, counts);
   - the **Trend** table: per case, the last 10 verdicts oldest → newest, and the pass rate over executed runs only (👤 and ⚪ excluded);
   - the **Flaky** list: cases that alternated ✅/❌ within the last 10 runs;
   - the open bugs from failed cases.

   Re-publishing the scenario (`/to-test-scenario`) preserves the dashboard block and the case-record title prefixes.

6. **Bugs, one per failing case.**
   - A case that fails files a bug only when it has none open. A later failing run comments on that open bug instead of filing a new one.
   - The bug links its run issue and its case record, and the run row links the bug.
   - When the case **passes on the next run, the run closes the bug** with a comment naming the run. The owner's pick: "No primeiro run verde".
   - A case that fails again after that files a new bug, which links the closed one.

## Consequences

- A run costs one new issue, plus one per newly failing case, instead of one comment per case. The per-case history moves from scattered comments to the run issues and the dashboard's trend.
- Closing a bug on the first green run means a flaky case can close and reopen bugs repeatedly. The Flaky list is the counterweight: a case that appears there needs a fix of the case, not of the product.
- Detaching the 100 case sub-issues of #1286 is a one-time migration. `to_test_scenario.publish` must stop re-linking them, or the 100-sub-issue cap blocks every run.
- The dashboard is machine-written inside markers. Hand edits inside the markers are overwritten on the next run.
- Mock-up state (2026-10-10): #1286 carries a hand-written dashboard (its prior body is saved by the coordinator) and #1420 is a hand-built run. #1420 is not yet a sub-issue of #1286 because of the cap, which is exactly what Decision 2 fixes.
