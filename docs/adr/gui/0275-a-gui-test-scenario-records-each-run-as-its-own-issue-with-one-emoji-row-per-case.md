# ADR-0275: A GUI test scenario records each run as its own issue, with one emoji row per case

- Status: accepted (2026-10-11). The owner accepted it and asked for the implementation in the fork, then a refresh of the live skill: "aplique no fork e depois atualize a versão atual" (2026-10-11), confirming "Sim, aceitar". It is a pending slice, cut into fork-only tickets #1472 (A), #1473 (B), #1474 (C), #1475 (D), #1476 (E), #1477 (F) and #1478 (G). Migrating #1286 itself is not part of it: the owner chose "Não migrar agora". The layout is mocked up in example tree #1422.
- Date: 2026-10-10 (area layout and tool spec added 2026-10-11, after an adversarial review against the code)
- Related:
  - ADR-0272: the GUI test hook, adapter interface and script format. Unchanged except the optional batch fields of §2.
  - ADR-0271: navigation in GUI test runs.
  - MesenAI #1286: scenario `play-pad-only.gui-test`, the first to migrate.
  - #1419: verdict emoji in case-record titles.
  - #1241: the run files bugs for failed steps.
  - #1415 and #1418: flaky gate cases that a trend view would have surfaced.
  - #1420: an earlier flat mock-up.
- Supersedes / amends: amends how `/to-test-scenario` publishes a scenario and writes a run back.
  - Today it publishes one parent with one sub-issue per case, capped at 100.
  - Today a run comments per case and closes the passed ones, and the run command files at most one bug per run when given `--file-bug` (#1241).
  - Under this ADR a scenario is published as areas, a run writes one run issue per area, and the write-back keeps one bug per failing case.

## Context

A test scenario is published as one parent issue with one sub-issue per case. A run reports by commenting on each case. The problems:

- **Scattered history.** A case's history is spread over its comments. Nothing shows what one run produced as a whole, what changed since the previous run, or which cases flake.
- **Uninformative icons.** The sub-issue icon follows only the issue state, so the list reads "open/closed", not "passed/failed".
- **The cap.** GitHub refuses more than 100 sub-issues per parent; the maximum depth is 8 levels, and a sub-issue has one parent. #1286's script now has 281 cases (25 setup + 256 steps) in 26 batches, and only 100 were published. Attaching run #1420 failed with "Parent cannot have more than 100 sub-issues".

The owner's requests (2026-10-10):

- Levels: the scenario as parent, dated executions below it, and the tested items with a verdict emoji.
- Totals, trends and results, with failed tests linked to their bugs.
- The scenario explains its areas. An area holds the description and objectives of its tests. Executions carry the results.
- Runs are children of the area.

Constraint: one issue per case per run would mint hundreds of issues per run, which costs notifications and API quota.

Non-goals:

- The adapter and the hook (ADR-0272) do not change.
- Nothing here changes how a step is executed or judged.
- Recording a manual walk's verdicts. A manual record is closed or reopened by a person, by hand; a tool for feeding hand verdicts in is a later decision.

## Decision

### 1. Layout

```
Scenario issue            explains the areas; dashboard; on the board
└─ Area issue             objectives and case descriptions of one area
   ├─ 📋 Cases            container; one sub-issue per case record
   └─ 📅 Runs YYYY-MM     container; one sub-issue per run of this area that month (UTC)
      └─ Run issue        results: totals, changes, one emoji row per case
```

The tree is four levels deep (scenario, area, container, run or case), well inside GitHub's 8.

- **Scenario issue.**
  - Its sub-issues are its areas, never cases.
  - Its body holds the purpose, the script link, an Areas table (area, objective in one line, case count, latest result) and the dashboard (§5).
  - It is the only issue on the board.
  - It keeps its labels, `ready-for-agent` included.
- **Area issue.**
  - Title: `<verdict> Area: <key>`. Label: `testarea`.
  - Body: the area's objective (§2), then a Cases table (id, expect truncated to 160 characters, action → check, severity, mode, record link), then the area dashboard (§5).
  - Its sub-issues are its containers.
- **📋 Cases container.**
  - Title: `📋 Cases`, or `📋 Cases (1/2)`, `📋 Cases (2/2)` and so on. No label, and never a verdict emoji.
  - Its sub-issues are the area's permanent case records, one issue per case.
  - Over 100 cases, the container splits by batch; a batch is never split.
- **Case record.**
  - Title: `<verdict> <case title>`, starting at ⚪. Labels: `testcase` and `wave:NN`, as today.
  - Its state follows §6.
- **📅 Runs YYYY-MM container.**
  - Title: `📅 Runs YYYY-MM`, the month taken from the run's `started` time in UTC. No label.
  - When a month passes 100 runs, a `📅 Runs YYYY-MM (2)` container follows.
- **Run issue.**
  - Label: `testrun`.
  - Written once, when the run ends, and closed as completed. A re-run is a new run issue; runs are never edited after the fact.
  - A case is a row in the run issue, never a new issue per run. The owner's pick: "Linhas na issue do run".

No area, container, run or record issue carries `ready-for-agent`, so the squad loop never dispatches one.

### 2. Area assignment

Each batch is assigned an area by the first rule that applies:

1. The batch's optional `area` field. A batch may also carry an optional `objective`, which becomes the area's objective text; the first one found for an area wins.
2. Otherwise, the batch id up to its first `-`. For example, `pause-pack-multi` goes to `pause` and `two-pads` goes to `two`.

An area without an `objective` reads "(no objective stated)".

As of 2026-10-11, rule 2 gives #1286 16 areas:

| Area | Cases | Area | Cases |
|---|---|---|---|
| pause | 67 | lamps | 5 |
| settings | 62 | loss | 5 |
| library | 37 | fullscreen | 5 |
| home | 25 | trap | 4 |
| controller | 23 | two | 4 |
| favorites | 18 | launch | 3 |
| game | 16 | feel | 3 |
| dialogs | 2 | journey | 2 |

These counts include setup steps. The script should gain `area`/`objective` fields where a prefix reads poorly, for example `two`.

### 3. Verdict emoji

One vocabulary is used in case rows, case-record titles, area titles and run titles. Container titles never carry it.

| Emoji | Verdict | From the results file's verdict |
|---|---|---|
| ✅ | passed | `passed` |
| ❌ | failed | `failed` |
| 🟡 | needs review | `needs review` |
| 👤 | manual: a person must walk it | `pending` |
| ⚪ | not run | `skipped`, `not executed` |

An aggregate (run, area, scenario) takes the worst verdict among its cases, in this order: ❌ > 🟡 > 👤 > ⚪ > ✅. ✅ means every case ran and passed.

Most of #1286's cases are `manual` (244 of the 256 steps), so the scenario and every mixed area read 👤 until those cases are walked; a ❌ still outranks 👤.

### 4. Run issue

- **Title:** `<overall> Run <YYYY-MM-DD HH:MM> UTC · <area> · <build_sha short>`, from `started`.
- **Rows:** a run issue lists only the cases present in the results, so a `--batch` or `--case` run is a partial run. `N` is the number of those cases. A case absent from a run is not a ⚪ row, and it does not enter that run's trend or flaky window.
- **Body:**
  - started time, run id, script sha, build and platform;
  - **Totals**: `✅ n · ❌ n · 🟡 n · 👤 n · ⚪ n (of N)`;
  - **Changes**: new failures, fixed and still failing. Each case is compared with its verdict in the newest earlier run issue of the area that contains it;
  - **Cases** table, one row per case: emoji, case id, record link, evidence (❌ and 🟡 only, per #1244) and bug link;
  - a trailing machine block, `<!-- gui-test:run {json} -->`, holding the run id, `started` and the per-case verdicts.
- **History:** the run blocks are the history that trends are computed from. History therefore lives on GitHub, and any machine can write the next run.
- **Order:** run order is `started`, with ties broken by run id. It is never the issue number.
- **Several areas:** one execution that touches several areas writes one run issue per area, all with the same run id.

### 5. Dashboards

The block between `<!-- gui-test:dashboard:start -->` and `<!-- gui-test:dashboard:end -->` is rewritten on every write-back. Nothing outside the markers is touched, and a hand edit inside them is overwritten.

- **Scenario dashboard:**
  - the latest run with totals per area;
  - the Runs table, newest first: started, run id, overall result, counts, and links to that run's area run issues;
  - the Flaky list: cases that alternated ✅/❌ within their last 10 runs;
  - the open bugs from failed cases.
- **Area dashboard:**
  - the Trend table: per case, its last 10 verdicts oldest → newest, and its pass rate over executed runs only (👤 and ⚪ excluded).

"The last 10 runs" are read from the area's 📅 containers, newest month first, until 10 run blocks containing the case are found.

### 6. Records and bugs

- **Record state.** A record changes title emoji and state only on ✅, ❌ or 🟡:
  - ✅ closes it;
  - ❌ and 🟡 reopen it.

  👤 and ⚪ never touch a record, so a person's manual closure survives automated runs. While every executed case is in a closed record, the 📋 container's native progress bar reads as the pass count.
- **Prior verdict.** A record's prior verdict is its title's emoji prefix, not a comment scan.
- **Comments.** Records get no per-run comments. Their history is the run issues.
- **One bug per failing case.**
  - A failing case files a bug only when it has none open. A later failing run comments on that open bug instead of filing a new one.
  - Bug title: `gui-test ❌ <script> · <case id>`.
  - Priority comes from the step's `severity`: `critical` → P0, `major` → P1, anything else → P2.
  - The bug links its run issue and its record. The run row and the record body (a `Bug: #N` line) link the bug.
  - The first later run in which the case passes closes the bug, with a comment naming the run. The owner's pick: "No primeiro run verde".
  - A case that fails again after that files a new bug, which links the closed one.
- **Bug lookup.** The source of truth is GitHub, not local state. The open bug is read from the record's `Bug: #N` line and its state is checked. When the line is missing, open `bug` issues are searched for the exact title.
- **Who files.** The write-back owns bug filing. `start-run` stops passing `--file-bug` to the run command, whose default is already off.

## `/to-test-scenario` changes

This is the implementation spec for `squad/to_test_scenario.py`, its CLI and `SKILL.md` in agent-squad, fork-only. Slices A–G are listed in dependency order, and each one ships its own tests against a stub runner, with no network.

### A. Plan: areas

- `build_plan` returns the plan with an `areas` list:
  ```
  plan["areas"] = [
    {"key", "title", "objective", "body",
     "containers": [{"title", "cases": [<case issue>, ...]}, ...]},
    ...
  ]
  ```
  The case issues inside it are the same objects as in `plan["issues"]`.
- `plan["issues"]` stays as the flat, run-ordered view of every case issue, and `plan["parent"]` stays. The parent's body gains the Areas table.
- Areas follow §2. The batch's optional `area` and `objective` fields are read, and `verify_gui_test_actions.py` accepts them.
- The cap check moves off `plan["issues"]` to "sub-issues per node ≤ 100". A single batch over 100 cases is a `ScenarioError` naming the batch, and nothing is created.
- `--granularity per-batch` still works: a container then holds batch issues.
- Body budget: an area body over 60,000 characters is a `ScenarioError` naming the area. GitHub's limit is 65,536.
- Tests:
  - #1286's script plans 16 areas and no node exceeds 100;
  - a 101-case batch is refused before any write;
  - the Cases table truncates `expect`.

### B. Runner ops

`rest_runner` gains these ops, all through `_paced`:

- `add_sub_issue(parent, sub, replace_parent=False)`: re-parents in one idempotent call when `replace_parent` is true;
- `remove_sub_issue(parent, sub)`: used only for a removed area;
- `list_sub_issues(number)`: paginated;
- `set_title(number, title)`;
- `issue_body(number)`;
- `search_open_bug(title)`.

`file_bug` returns `{"url", "number"}`, with the number parsed from the URL.

Tests: each op's REST call shape against a stub shell.

### C. Publish over the tree (needs A, B)

- `read_issues` and `read_map` preserve every key of the map. `publish` and `write_back` mutate and flush the whole document.
- The map gains these keys, and is flushed after every created issue or link:
  ```
  "layout": "areas"
  "areas": {<key>: {"number", "containers": [...], "runs": {"YYYY-MM": [...]}}}
  ```
  Each case entry also gains `"area"` and `"container"`.
- Links are made top-down: scenario → area → container → record. A record is never linked to the scenario.
- Container adoption: before creating any container, `list_sub_issues` on its parent adopts an existing one with the same title, so a lost map or a concurrent writer never makes two.
- Re-publish rules:
  - a known issue is updated, never created twice;
  - the parent and the area bodies are fetched (`issue_body`) and their dashboard block is kept;
  - the text after a title's emoji prefix is rewritten, and the prefix stays;
  - a record's state is never touched.
- Moves and removals:
  - a case whose plan area differs from the map is re-parented (`replace_parent`) and its map entry rewritten, and its trend starts fresh in the new area;
  - a map area absent from the plan is reported in `removed_areas` and left open, like `removed` cases today.
- `publish(..., dry_run=True)` and the CLI's `--dry-run` print the counts to create, link, move and update, and write nothing.
- Tests:
  - a 281-case publish creates 16 areas and their containers;
  - an interrupted publish resumes without a duplicate issue or link;
  - a re-publish preserves emoji, dashboards and record state;
  - a moved case is re-parented;
  - a lost map adopts the existing containers.

### D. Migrating a flat scenario (needs C)

A map without `"layout": "areas"` whose parent has case sub-issues is a flat scenario, like #1286. `publish` migrates it in one resumable pass:

1. Create the areas and their 📋 containers.
2. Re-parent each known record into its container with `replace_parent`. This is a link change only; no issue is closed or deleted.
3. Create the records the cap left out (181 for #1286 as of 2026-10-11).
4. Write `"layout": "areas"`.

Each step flushes the map. Existing comments stay where they are. `--dry-run` applies.

Tests: a 100-record flat map migrates (100 re-parented, the rest created), and an interrupted migration resumes.

### E. Write-back: run issues and dashboards (needs C)

`write_back(results_path, map_path, github, plan=None)` keeps its signature and its refusals: a malformed file, a run with no executed step and a run with no core write nothing.

1. Take a lock beside the map (`<map>.lock`). If it is held, fail fast. After taking it, re-read the map.
2. Group the results by area. For each area:
   - adopt or create the month's 📅 container (UTC);
   - create the run issue (§4) and close it.
3. Rewrite the area dashboards and the scenario dashboard from the run blocks (§5).

A step whose batch has no area in the map is reported in `unknown_cases` and is not written.

The coordinator's step after a write-back commits the map beside the script. The map is a cache: losing it costs re-adoption calls, never duplicates.

Tests:

- a write-back makes one closed run issue per area, with the right Totals and Changes;
- a partial run lists only its cases;
- trend and flaky are computed from run blocks ordered by `started`;
- a second writer fails while the lock is held.

### F. Write-back: records and bugs (needs E)

- Records follow §6: set the title emoji, close on ✅, reopen on ❌ or 🟡, and leave 👤 and ⚪ alone. No per-run comment is written.
- Bugs follow §6: look up through the `Bug: #N` line, falling back to the title search; file, comment on, or close; rewrite the record's `Bug:` line.
- `start_run` stops passing `--file-bug`.
- Tests:
  - ✅ closes a record;
  - ❌ reopens it and files exactly one bug, with the severity-derived priority;
  - a second ❌ comments on that bug;
  - the next ✅ closes it;
  - a later ❌ files a new bug that links the closed one;
  - a lost map still finds the open bug;
  - a 👤 leaves a hand-closed record closed.

### G. Docs (needs C–F)

`SKILL.md` and its vendored copy describe:

- the layout and the area rule;
- the emoji vocabulary;
- the migration and `--dry-run`;
- that the write-back files bugs;
- that the map is committed after a write-back.

The Publish step's "only the parent goes on the board" stays.

## Consequences

- **Cost per run.** A run costs one issue per area touched, plus one per newly failing case, instead of one comment per case. Per-case history moves from scattered comments to run issues and trend tables.
- **Flaky bugs.** Closing a bug on the first green run means a flaky case can close and reopen bugs repeatedly. The Flaky list is the counterweight: a case that appears there needs a fix of the case, not of the product.
- **Manual cases.** Manual records are a person's to close, so an automated run never makes a 👤 case look passed. The cost is that a scenario of mostly manual cases reads 👤 overall until it is walked.
- **#1286 migration.** It is a one-time, resumable migration. Its body before the hand-written dashboard is saved by the coordinator. Until slice D lands, `publish` refuses the full plan through the cap check, which is today's behaviour.
- **Machine-written blocks.** Dashboards and `gui-test:run` blocks are machine-written. A hand edit of a run block corrupts that area's trend until the run issue is fixed.
- **Mock-ups.** Example tree #1422 (label `example-layout`) and run #1420 are throwaway mock-ups. Deleting them is permanent deletion, so it waits for the owner's decision.
