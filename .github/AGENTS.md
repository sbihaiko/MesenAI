# .github/

## Purpose

GitHub Actions CI/CD: build, format-check, and test workflows for the
project.

## Ownership

Owned at the repo-infra level. Workflow files are the source of truth for
what CI actually runs; this doc records why they're split the way they are.

## Local Contracts

- `workflows/build.yml` — native + UI release build, **Linux only** since
  ADR-0191 (2026-09-14). The two Windows publish jobs and the four macOS legs
  were deleted from the file (history keeps them); what is left is the
  six-leg Linux matrix and the two AppImage legs, and neither consumed an
  artifact of a removed job. The `push` trigger is gone (#230) and stays gone:
  a push run on `main` would become the newest run of this workflow there,
  which is the run the README's nightly.link URLs resolve against. Since
  **ADR-0200** (2026-09-16) it takes one trigger besides `workflow_dispatch`
  (`gh workflow run build.yml --ref <branch>`): a `pull_request` **filtered to
  the `prod` base branch**, so a pull request opened against `prod` builds all
  eight legs and publishes their artifacts, while an ordinary pull request
  against `main` builds nothing. That filter is the cost guard — an unfiltered
  trigger would fire the eight-leg LTO matrix on every pull request, the cost
  #230 and ADR-0191 existed to stop. Restoring a platform is a revert of the
  ADR-0191 commit plus a line in that ADR. The rationale is in the file's
  header comment.
- `workflows/checks.yml` — `make doc-checks` on every pull request into
  `main`, on every push to `main`, and on dispatch. The two triggers are not
  duplicates, and the `push` one is not removable as an optimization
  (**ADR-0193**, 2026-09-15): `pull_request` is what reports the five required
  checks before the code can land, and `push` on `main` is the *only* gate for
  what bypasses that path — a direct push (the ruleset's `bypass_actors` lets
  an admin token through, which is how a hand fix lands; catalog regenerations
  open a PR since #266) and a merge-commit/rebase-merge onto
  a `main` that moved, whose tree the PR never tested
  (`strict_required_status_checks_policy` is false). Measured over the last 60
  commits on `main`: 49 squash-merges, 7 PRs via merge-commit/rebase, 4 pushed
  straight to `main`. Runs are free — the repository is public.
  This is the repo's always-on gate, moved out of `build.yml` when that file
  became dispatch-only. It is a separate file, not a job, because
  `build.yml`'s newest run on `main` is what the README's nightly.link
  download links resolve against: an artifact-less job there would supersede
  the downloadable build, and `cancel-in-progress` (grouped on workflow+ref)
  would let a push cancel a dispatched build. `checks.yml` also compiles
  `scripts/headless_record`, so it needs SDL2.
  Since Phase 11 C.1 (2026-09-14) it holds the whole gate, run in parallel so
  a red job names which contract broke. ADR-0191, later the same day, folded
  `unit-tests.yml`'s two jobs in here and deleted that file, so the count is
  **five**:
  - `checks` — `make doc-checks`, as above.
  - `python-tests` — `make python-tests`, i.e.
    `scripts/checks/run_python_tests.sh`: every `scripts/test_*.py` as its own
    process, exit non-zero on any file's failure. A loop over the files, never
    `python3 -m unittest discover`, because most of them carry a hand-written
    `main()` runner printing "ok"/"N/N passed" instead of `unittest.TestCase`
    subclasses — discovery collected roughly 40 of ~290 cases. The runner's
    skip list is for tests that cannot run without a display and must stay
    empty whenever the test can skip itself instead (as
    `test_compose_editor_gui.py` does for its four windowed cases); every entry
    carries its reason. Same Python deps as `checks`, plus `python3-tk` (that
    test imports tkinter at module scope, and the runner image ships without
    it) and no SDL2.
  - `core-unit-tests` — `make core-unit-tests`, the PR gate's only compile of
    `Core/`. Same host-free invariants as `unit-tests.yml`'s step of the same
    name (no `InteropDLL`/`MesenCore`, no SDL2, no SDK, no ROM), and since C.1
    the makefile's `CUTFLAGS` carry `-Wall -Werror` instead of `-w`.
    `-Wno-deprecated-declarations` is the one blanket exception, for inherited
    upstream code (`Utilities/UTF8Util.cpp`'s `std::wstring_convert` /
    `std::codecvt_utf8_utf16`, deprecated in C++17); measured 2026-09-14 those
    were the only two warnings `-Wall` produced over the whole `CUTSRC` list.
  - `ui-tests` — `./scripts/verify-ui-logic-firewall.sh` (ADR-0123 H5, the
    fast pre-check; the dual-compile is the authoritative gate) then
    `dotnet test UI.Tests/UI.Tests.csproj`. Moved here from `unit-tests.yml`
    by ADR-0191, minus that job's `make core-unit-tests` step: the
    `core-unit-tests` job above already compiles and runs the harness, and
    duplicating it would pay for the same compile twice and leave two check
    runs that fail for one reason. The job id now means exactly what it says
    (ADR-0131's note about it covering the C++ suite too is withdrawn).
  - `headless-ui-tests` — `dotnet test UI.HeadlessTests/UI.HeadlessTests.csproj
    -p:RuntimeIdentifier=linux-x64` (ADR-0150). Its own job, never a step of
    `ui-tests`, so a headless-host failure cannot red the cheap host-free leg.
    No `MesenCore` is built, so the MainWindow-backed cases self-skip with a
    reason via `NativeCore.cs`; the core-free wiring cases run. The RID
    override is needed because `UI/UI.csproj` hardcodes `win-x64`, and it is
    the cross-RID `obj/` trap: a local restore for `osx-arm64` leaves
    artefacts a `linux-x64` restore trips over — a clean checkout (CI) or
    `rm -rf UI.HeadlessTests/obj` is what reconciles them.
  These five jobs live here, and not in `unit-tests.yml`/`tests.yml`, because
  both of those workflows were `disabled_manually` on this repo and had
  produced no run since — a check name that never reports cannot be required
  on `main`. ADR-0191 deleted them both.
- `workflows/clang-format-check.yml` — C++ formatting gate (`clang-format` 20,
  `check-path: ./`), **`disabled_manually`**; left disabled by ADR-0191, which
  did not decide its fate. Its triggers, if it is ever enabled again, are push
  to `main` (the product branch) and every PR. Excludes vendored `Utilities/Audio/tsf.h` (TinySoundFont);
  that header is also wrapped in `clang-format off/on`.
  `master` is a frozen full-console snapshot and is not gated here.
- `workflows/dotnet-format-check.yml` — **deleted** by the user's decision
  (2026-09-14). It ran `dotnet format --verify-no-changes` against
  `Mesen.sln` on a Windows runner, `disabled_manually`; it had been the one
  file ADR-0191 allowed to name a Windows runner, since it compiled nothing
  and produced no binary. Rather than keep a dead, Windows-only exception
  around, it was removed outright and `verify_ci_linux_only.sh` no longer
  carries the exception. A changed-files-only format check may return as a
  separate PR — on Linux, since ADR-0191 forbids a Windows runner for
  anything now.
- `workflows/tests.yml` — **deleted** by ADR-0191 (2026-09-14). It was the
  Windows-only ROM regression suite (MSBuild `PGOHelper` + the private
  `nesdev-org/MesenTests` corpus, run as `PGOHelper.exe … citests`) and the
  last Windows compilation in CI. That coverage is not reproducible on Linux
  here — `PGOHelper.exe` is an MSBuild target of `Mesen.sln` producing a
  Windows executable — so it comes back only when Windows does. ADR-0162
  already recorded the accuracy suite as "not in CI by decision".
- `workflows/unit-tests.yml` — **deleted** by ADR-0191 (2026-09-14). Its two
  jobs, `ui-tests` and `headless-ui-tests`, are now jobs of `checks.yml`
  (above), which is where their contract is documented. ADR-0131 remains the
  normative statement of the invariants — never link `InteropDLL`/`MesenCore`,
  never require SDL2, never require a platform SDK or a ROM corpus, pin
  `dotnet-version: 10.x` — and those invariants now bind the two `checks.yml`
  jobs. `UI.Tests.csproj` is still intentionally NOT a member of `Mesen.sln`,
  so it goes through neither `build.yml`'s restore/publish flow nor a format
  check against the `.sln` (`dotnet-format-check.yml`, the workflow that ran
  that check, was itself deleted 2026-09-14).

- `ISSUE_TEMPLATE/community-pack.yml` — GitHub Issue Form for community
  HD/MEP pack submissions (not a free-text issue). Deliberately minimal:
  pack link, target game/ROM + region and a console dropdown, all
  required, and nothing else. An intro promising three fields plus a bot
  comment with the result, one-line field descriptions, and a closing
  `docs/hd-pack-authoring.md` link explicitly marked as not required
  reading. Sets `labels: [community-pack]`.
  Removed on purpose (`verify_community_pack_issue_template.py` fails if
  any comes back): `author_credits` — the classify step discovers
  authorship from the pack's own manifest/README and records it as
  mep-meta's `author`, which is what the catalog's Author column reads;
  `description`; and the ADR-0138 §12 split-distribution pair
  `external_assets`/`external_assets_license`. The recipe parser still
  accepts an "External assets" section typed into an issue body by hand
  (`<url> [<sha256>] [<size>]` per non-empty line, `#`-comments and blank
  lines ignored, a line missing `sha256` disabling recipe assembly), so
  the pipeline behind it is unchanged — a submission simply no longer
  asks for it, and its `license` therefore defaults to "unknown".
  There is no distribution-rights checkbox either (dropped in
  `b62f0bbc`). Structurally checked by
  `scripts/checks/verify_community_pack_issue_template.py`.
- `workflows/community-pack-submitted.yml` — trigger-only wrapper that
  extracts the pack URL and mode, then calls the reusable validate
  workflow. Concurrency group is per issue number. `cancel-in-progress`
  is `${{ github.event_name == 'issues' }}` (F6.0, ADR-0138): a newer
  opened/edited submission supersedes a stale run, but an `issue_comment`
  (including the verdict comment this pipeline posts) must not cancel the
  in-flight run, or the catalog-dispatch step is lost. `/revalidate`
  comments queue behind an in-flight run instead of killing it. Checked
  by `scripts/checks/verify_community_pack_submitted_workflow.py`.
- `workflows/community-pack-validate.yml` — reusable (`workflow_call` only,
  no trigger of its own) validate/classify pipeline for the "Community
  HD/MEP Packs" triage board (GitHub Project "MesenCE Community Packs",
  project number 3, owner `sbihaiko`). Referenced by value only, never
  created/rediscovered: Project node id, the Status field's five option
  ids, and the Pack Hash field id. Enforces a host allow-list
  (`github.com/*/releases/*`, `raw.githubusercontent.com`,
  `gist.githubusercontent.com`, `gist.github.com`, Google Drive,
  MediaFire `/file/` share pages and `downloadN.mediafire.com`) and a 300MB cap before
  and during the download, always records the pack's `sha256` to Pack
  Hash, calls `scripts/mep_lint.py` unmodified, and — only on lint
  success — classifies the pack via `anthropics/claude-code-action`
  with `--disallowedTools Bash,Read`. Pack evidence is a bounded
  `{{PACK_BRIEF}}` from `scripts/classify_pack_brief.py` (member list,
  tag counts, header/README excerpts, patch magic, lint summary);
  classify must not open `pack_download.bin` or `hires.txt` (issue #148
  timed out on a 26 MiB manifest). File names/`pack.json`/issue text stay
  framed as data, never instructions. The classify step carries
  `timeout-minutes: 15` (F6.0) so a hung Claude Code Action cannot hold
  the runner for the job's 6-hour default. Dispatches
  `workflows/community-pack-catalog.yml` by name (never opens it) when the
  final Status is one of the two "Aceito" states. Requires the caller to
  supply a `PROJECT_PAT` PAT (`repo` + `project` + `read:org` scopes —
  `read:org` is required by `gh project` commands to resolve a
  personal-account owner, confirmed via a live "unknown owner type"
  failure without it) and either
  `ANTHROPIC_API_KEY` or `CLAUDE_CODE_OAUTH_TOKEN` as repo secrets — this
  workflow only documents those names, never creates them. See
  `scripts/checks/verify_community_pack_validate_workflow.py` for its
  structural contract.
- `workflows/community-pack-catalog.yml` — regenerates
  `docs/community-packs.md` and `docs/community-packs.json` from the board
  by running `scripts/generate_community_pack_catalog.py`, then commits and
  pushes to `main` as `github-actions[bot]`. Two triggers, and the file
  declares only these two: `workflow_dispatch` (the per-verdict dispatch in
  `community-pack-validate.yml` uses it) and a daily `schedule` at
  `'47 4 * * *'`. The schedule is a **safety net, not the mechanism** — it
  exists for the two cases a dispatch cannot cover: a dispatched run that
  died, and a Status changed by hand on the board, which no workflow
  observes (Phase 11 C.3, 2026-09-14). Regeneration reads the live board and
  is idempotent, so a run with nothing to do commits nothing. On a rejected
  push the retry loop **regenerates on top of the new `main`** (drop the
  commit, `reset --hard origin/main`, re-run the generator) rather than
  rebasing: a rebase of one regeneration of the two generated files onto
  another conflicts on every hunk and leaves the repo mid-rebase, which is
  how run 34844891828 failed. Checked by
  `scripts/checks/verify_community_pack_catalog.py` (which asserts the
  `workflow_dispatch` trigger and never opens the validate workflow).
- `workflows/community-pack-drift-check.yml` — daily (`'17 4 * * *'`) hash
  drift check over the board's accepted items, calling the reusable validate
  workflow with `mode: revalidate` only for items whose content hash moved.
  `disabled_manually` by the user's decision (2026-09-14): Pack Hash, label
  reconciliation and catalog updates currently happen only via `/revalidate`
  or a manual `gh workflow run community-pack-drift-check.yml`; re-enabling
  the daily schedule is `gh workflow enable community-pack-drift-check.yml`.
  Since Phase 11 C.3 it also carries a **"Reconcile verdict labels with the
  board Status"** step: `pack:valid` and `pack:invalid` are mutually
  exclusive, and while every verdict path inside
  `community-pack-validate.yml` already enforces that (#159), a Status moved
  **by hand** bypasses all of them — the ADR-0148 de-listing of 2026-08-31
  left #128–#131 and #133–#136 in "Inválido" still labelled `pack:valid`,
  with zero `pack:invalid` on the whole board. The step treats the board
  Status as the source of truth for the verdict, skips items still in "Novo
  envio"/"Em validação" (no verdict yet), and only edits labels: it never
  moves an item, never comments, and is a no-op when the labels already
  agree. Checked by
  `scripts/checks/verify_community_pack_drift_check_workflow.py`.
- **Recipe handoff (ADR-0138 §13, amends §9; F6.2b complete).**
  The "Classify pack" step's `--json-schema` now carries an OPTIONAL
  nested `recipe` property (`ops`/`deps`/`pack`, per
  `docs/specs/MEP-recipe-v1.md`) with its own `"required":["ops","deps","pack"]`
  — separate from, and never added to, the unchanged top-level
  `"required":["verdict","assets","comment"]` — because classify (the LLM)
  never computes a hash: no `sources` block or hash-bearing field exists
  anywhere in this schema (ADR-0138 §4). The prompt states this
  explicitly. Checked by `verify_community_pack_validate_workflow.py`'s
  `check_classify_recipe_fragment_required`,
  `check_classify_top_level_required_unchanged`, and
  `check_classify_schema_no_sources_field`.
  `community-pack-validate.yml`'s "Assemble MEP recipe" step (`id:
  assemble-recipe`, runs right after "Classify pack") writes the MEP
  Recipe to `$RUNNER_TEMP/mep_recipe.json` — a GitHub Actions runner-local
  temp path, never a path inside the checkout, so the recipe can never be
  mistaken for, or committed as, a repo artefact. Nothing under this repo
  (this workflow, `scripts/`, or anywhere else) may write `mep_recipe.json`
  into the checkout; the file exists only on the runner's local disk for
  the duration of the job. It fetches the issue body itself via `gh issue
  view "$ISSUE_NUMBER" --repo "$REPO" --json body -q .body` — never the
  triggering event's payload (§17), since this reusable `workflow_call`
  workflow's callers include non-`issues` triggers (drift-check's
  `workflow_dispatch`/schedule) — and calls `scripts/mep_recipe.py
  assemble-sources` with classify's optional `recipe` fragment, the issue
  body, and the CI-computed primary sha256 (`steps.hash.outputs.sha256`).
  The step exposes one step output, `recipe_status`, with exactly three
  values: `absent` (the submission declared no `external_assets`, or
  classify emitted no recipe fragment at all), `present` (a recipe was
  assembled and written to the path above), and `refused` (assets were
  declared but at least one dependency line lacks a `sha256`, so assembly
  was declined per §3/§12). Every downstream reader — the gate step, the
  `assets:external` label branch, `apply-verdict`'s downgrade expression,
  and the mep-meta `recipe_hash` comment — branches on this enum instead
  of re-deriving "is there a recipe?". Checked by
  `verify_community_pack_validate_workflow.py`'s
  `check_assemble_recipe_step_present`,
  `check_assemble_recipe_issue_body_via_gh`,
  `check_assemble_recipe_runner_temp_handoff`,
  `check_recipe_status_three_values`, and `check_no_github_event_issue`,
  and by `scripts/checks/verify_agents_md_recipe_handoff.sh` for this
  prose.
  A "Recipe gate (mep_recipe.py validate + dry-run, deps stubbed by name)"
  step (`id: recipe-gate`, runs right after "Assemble MEP recipe" and
  before "Apply classification verdict") is gated on
  `steps.assemble-recipe.outputs.recipe_status == 'present'` — **never**
  `!= 'absent'`, which would also wrongly fire for `refused` (§2/§13: a
  refused submission's pre-ADR verdict path must stay completely
  untouched). It runs `python3 scripts/mep_recipe.py validate` against
  the handoff path above, then (only if that passed) `python3
  scripts/mep_recipe.py dry-run` against the already-downloaded primary
  (`pack_download.bin`) — with no `--dep PATH` ever passed, since CI
  never fetches or hashes external dependency content (§16). Every
  declared dependency is therefore "stubbed by [its] declared name"
  simply by never being supplied: `mep_recipe.py`'s existing missing-dep
  handling treats every dep id from `sources.deps` as missing, skips its
  ops, and withholds its patch/section from `pack.json`, exactly as the
  default `apply_patch_only_if_complete` policy already does for a
  `user_supplied` dep — no dedicated CI-only flag is needed. `RECIPE_OK`
  is set to `false` (never a hard workflow failure) whenever either call
  exits non-zero, which also covers the rarer case of a `user_supplied:
  false` dep or an explicit `apply_patch_only_if_complete: false` policy
  demanding content CI cannot supply; the gate simply reports
  `recipe_ok=false` and leaves the outcome to `apply-verdict`'s
  downgrade-only expression (§10). The step exposes exactly
  one boolean output, `recipe_ok`, and never adds a
  label, posts a comment, or moves the Project Status field itself —
  `apply-verdict` remains the sole verdict writer (§10). Checked by
  `verify_community_pack_validate_workflow.py`'s
  `check_recipe_gate_step_present_and_gated` and
  `check_recipe_gate_never_uses_inverted_condition`.
  `apply-verdict` (`id: apply-verdict`, "Apply classification verdict")
  stays the SOLE verdict/label writer (§10): it reads
  `steps.assemble-recipe.outputs.recipe_status` and
  `steps.recipe-gate.outputs.recipe_ok` and downgrades `accepted` to
  `invalid` with one literal bash condition inside its `run:` block —
  `[ "$RECIPE_STATUS" = "present" ] && [ "$RECIPE_OK" != "true" ]` —
  deliberately not a step-level `if:`, since that would decide whether the
  whole verdict/label step runs at all rather than downgrading its
  outcome. The same step's existing asset-label case loop gains an
  `external` arm applying `assets:external`, fed in only when
  `recipe_status == 'present'` and the assembled recipe's
  `sources.deps` array (read back from `$RUNNER_TEMP/mep_recipe.json`) is
  non-empty (§6) — never derived from classify's own `assets` enum, which
  has no "external" member. `apply-verdict` exposes three step outputs:
  `verdict` (the effective, post-downgrade verdict), `labels` (every label
  actually applied to the issue), and, since F6.3b (ADR-0138 §29), `kind`
  — set by a second `case` on the already-decided `$CATEGORY`
  (`"$CATEGORY_FULL_MEP") KIND="mep"`, `"$CATEGORY_PARTIAL_HD")
  KIND="hd-legacy"`, mirroring rather than re-deriving the CATEGORY
  selection, since today's binary accepted/invalid verdict can't tell
  "mep" from "hd-legacy" by itself) so the mep-meta upsert step below can
  consume all three without recomputing. The two `KIND=` literals are
  textually consistent with `scripts/mei_rules.STATUS_TO_KIND`'s own
  values (checked by reading `mei_rules.py`'s source directly, never by
  importing it). Checked by `verify_community_pack_validate_workflow.py`'s
  `check_apply_verdict_downgrade_expression`,
  `check_apply_verdict_external_label_branch`,
  `check_apply_verdict_exposes_outputs`, and
  `check_apply_verdict_kind_matches_mei_rules_status_to_kind`.
  **"Upsert mep-meta comment" (`id: upsert-mep-meta`, F6.2b complete;
  fence fix + `kind` field F6.3b).** Runs right after `apply-verdict`, on
  EVERY successful classify pass (`if: steps.classify.outcome ==
  'success'`) — deliberately never gated on `recipe_status == 'present'`
  (§5/§13/§18): a submission with no assembled recipe (`absent`/
  `refused`) still gets its verdict, labels and `source_sha256` recorded,
  just without the `deps`/`recipe_hash` fields (omitted entirely, never
  emitted empty/null). `verdict` and `labels` are read verbatim from
  `apply-verdict`'s own outputs — never recomputed; `kind` (F6.3b, §29) is
  read the same way and, when non-empty, copied into the payload's own
  `kind` field (also omitted, never emitted empty, when apply-verdict
  picked no CATEGORY). The `<!-- mep-meta -->`-marked comment is bot-owned
  and rewritten WHOLESALE on every pass (§5): the step finds it via `gh
  api` (`GET /repos/$REPO/issues/$ISSUE_NUMBER/comments`, `--jq` matching
  the marker), then `gh api --method PATCH` its body outright — never a
  read-modify-merge with whatever it said before — or `gh api --method
  POST` a new comment when none exists yet. The comment body (embedded
  JSON metadata block plus the literal provenance line, "dep digests:
  submitter-declared, verified on install", §16) and the API request
  payload are both built with Python's `json` module (a `python3 -
  <<'PYEOF'` heredoc reading the step's own env vars), never bash string
  concatenation. Since F6.3b (ADR-0138 §33), the JSON block's opening/
  closing fence is no longer a hardcoded literal `` ```json ``/`` ``` ``
  pair — `json.dumps` does not escape backticks, so a submitter's
  `hints`/`license` value containing a run of 3+ backticks used to
  truncate the block early. The heredoc now does `sys.path.insert(0,
  'scripts')` (the same pattern the "Enforce host allow-list" step above
  already uses) and calls `mep_recipe_common.choose_fence(meta_json)` to
  pick a fence strictly longer than any backtick run already in the
  serialized payload, matching the reader-side rule `mep_recipe.py`'s
  `FENCE`/`load_recipe` use (see `mep_recipe_common.py` below;
  `mep_meta_parser.py`'s own `JSON_FENCE_RE` reader is a known, tracked
  gap — still bare-3-backticks, out of F6.3b's file list, not silently
  patched here). `recipe_hash` is `sha256sum` of
  `$RUNNER_TEMP/mep_recipe.json` itself — a hash of the recipe DOCUMENT,
  never of dep contents; dep `sha256`/`size` are copied straight from the
  assembled recipe's `sources.deps` (submitter-declared, never
  CI-verified, §11/§16). Checked by
  `verify_community_pack_validate_workflow.py`'s
  `check_mep_meta_step_present_and_not_gated_on_recipe_status`,
  `check_mep_meta_find_then_patch`, `check_mep_meta_marker_in_comment_body`,
  `check_mep_meta_provenance_line`, `check_mep_meta_body_built_via_python_json`,
  `check_mep_meta_omits_deps_and_recipe_hash_when_absent`, and
  `check_mep_meta_fence_not_hardcoded`. F6.2b is complete end-to-end
  (classify schema → assembly → gate → apply-verdict → mep-meta upsert);
  F6.3b hardens the `kind` field and the fence on top of it.

## Work Guidance

- **CI compiles Linux only (ADR-0191, 2026-09-14).** Every binary build is
  macOS Apple Silicon only for now, and CI is not where it happens: no
  workflow in this repository may declare `runs-on:` naming a `macos-*` or a
  `windows-*` runner. There is no exception left: `dotnet-format-check.yml`,
  the one file `scripts/checks/verify_ci_linux_only.sh` used to allow to name
  a Windows runner, was itself deleted (2026-09-14, the user's decision) — a
  dead, Windows-only exception was not worth keeping. The macOS release is
  built **locally** by `make release-macos` (Phase 11 C.4), whose hash check — not
  CI — is what verifies the `.app`. Windows is retired from CI until the user
  lifts the rule, and with it the upstream ROM accuracy suite; the practical
  cost is that MSVC-only breakage (`/W4 /WX`, e.g. the `getenv` C4996 trap)
  is caught only when Windows returns. Do not add a macOS or Windows job, and
  do not re-create `tests.yml`/`unit-tests.yml`, without amending ADR-0191.
- **The PR gate's invariants (Phase 11 C.1, 2026-09-14 — amends the CI
  contract of ADR-0131; extended by ADR-0191 the same day).** Every pull
  request into `main` must, without a `workflow_dispatch`:
  1. compile the `Core/`/`Utilities/` sources on `CUTSRC` with warnings as
     errors (`checks.yml`'s `core-unit-tests` job) and run the framework-free
     harness;
  2. run **every** `scripts/test_*.py`, not a hand-picked subset
     (`checks.yml`'s `python-tests` job). Adding a test file must require no
     edit to a workflow or to the makefile;
  3. run the C# `UI.Tests` suite behind the UI/Logic firewall pre-check and
     the `UI.HeadlessTests` Avalonia wiring suite (ADR-0150), as
     `checks.yml`'s `ui-tests` and `headless-ui-tests` jobs (ADR-0191);
  4. report the five check runs `checks`, `python-tests`, `core-unit-tests`,
     `ui-tests` and `headless-ui-tests`, which are the required status checks
     of the `main` ruleset (`gh api repos/sbihaiko/MesenAI/rulesets`).
     Renaming a job here renames a required check: update the ruleset in the
     same PR, or `main` blocks on a name that never reports.
  Adding a sixth required check is fine; removing one of the five, or letting
  a compile or a Python test leave the gate, is not — that is the state #230
  left behind and C.1 was written to end. Binaries stay off the pull-request
  path for an ordinary pull request (#230 stands); none of this re-enables
  them. ADR-0200 later gave `build.yml` a `pull_request` filtered to the
  `prod` base branch, which does not touch the gate — a `prod` pull request
  runs no test and reports no required check.
  **Trigger half (ADR-0193, 2026-09-15):** the invariant above binds what a
  pull request *must* report, and that is the trigger the gate cannot lose —
  drop `pull_request` from `checks.yml` and the five checks never report, so
  every merge blocks on a name that never arrives. The `push` on `main` is a
  separate trigger with a separate job (the bypass paths in the `checks.yml`
  bullet above) and is revisitable on the terms ADR-0193 §5 gives; do not read
  "the gate is the pull request" as "the push trigger is dead weight".
- The `checks.yml` jobs `ui-tests` and `headless-ui-tests` must never link
  `InteropDLL`/`MesenCore`, never require SDL2, and never require a platform
  SDK or ROM corpus (ADR-0131's invariants, inherited from the deleted
  `unit-tests.yml`). A self-contained compile of explicitly listed
  `Core/`/`Utilities/` sources (as `make core-unit-tests` does) is in scope
  and lives in the `core-unit-tests` job; anything that needs the `core`
  makefile target belongs in `build.yml`. The `checks` job is the one
  exception that installs SDL2, because `verify_smoke_pack_headless.sh`
  builds `scripts/headless_record`.
- The `ui-tests` job id now covers the C# suite only: ADR-0191 dropped the
  `make core-unit-tests` step when the job moved, because the
  `core-unit-tests` job already runs it. ADR-0131's "a later rename to
  something like `host-free-tests`" note is therefore moot, and a rename
  would churn a required check name for nothing.
- `make core-unit-tests` is intentionally clang-only (makefile default
  `CXX := clang++`) for cheapness; gcc and arm64 coverage of the `Core/`
  sources it compiles is `build.yml`'s job via `CORESRC` — on Linux only
  since ADR-0191. Only `scripts/core_unit_tests.cpp` itself is clang-gated.
- `actions/setup-dotnet`'s `dotnet-version` pins `10.x` in both `checks.yml`
  dotnet jobs, matching `build.yml`'s `10.x`. Before 2026-09-14 this was one
  of two independent pins — `dotnet-format-check.yml` pinned its own,
  separate `10.0.x` for its Windows-only `dotnet format` check — but that
  workflow was deleted, so `build.yml`/`checks.yml`'s `10.x` is now the
  single source (ADR-0131 item 4, option A: the doc matches the files as
  they are).

## Verification

- `./scripts/checks/verify_ci_linux_only.sh` — ADR-0191's own test plus
  ADR-0193's trigger contract and ADR-0200's `build.yml` trigger, wired into
  `make doc-checks`. It subsumes the greps below; run it first.
- `grep -E "^  (pull_request|workflow_dispatch):" .github/workflows/checks.yml`
  (expected: both — ADR-0193; the `push` trigger is intentionally absent from
  this grep and from the verifier, see ADR-0193 §4)
- `grep -A2 "^  pull_request:" .github/workflows/checks.yml` (expected: the
  `branches:` filter naming `main`)
- `grep -A2 "^  pull_request:" .github/workflows/build.yml` (expected: the
  `branches:` filter naming `prod`, plus `workflow_dispatch` — ADR-0200. An
  unfiltered `pull_request` here fires the eight-leg binary matrix on every
  pull request in the repository; `grep -c "github.event_name != 'pull_request'"
  .github/workflows/build.yml` must be 0, or a `prod` PR runs all eight legs
  and publishes nothing)
- `! grep -l "windows-latest" .github/workflows/*.yml` (no Windows runner by
  that name; the verifier above also rejects `windows-2025-vs2026` and every
  other `windows-*` — there is no exception left, `dotnet-format-check.yml`
  was deleted 2026-09-14)
- `! grep -l "macos-" .github/workflows/*.yml` (no macOS runner anywhere —
  the macOS release is `make release-macos`, locally)
- `test ! -e .github/workflows/tests.yml` (deleted with Windows)
- `test ! -e .github/workflows/unit-tests.yml` (folded into `checks.yml`)
- `grep -E "dotnet test" .github/workflows/checks.yml` (both suites)
- `grep -E "verify-ui-logic-firewall" .github/workflows/checks.yml`
  (the step must precede "Run unit tests")
- `grep -E "make core-unit-tests" makefile` (the harness is invoked by the
  `core-unit-tests` job, which runs `make -j$(nproc) core-unit-tests`)
- `grep -vE "^\s*#" .github/workflows/checks.yml | grep -cE "InteropDLL|MesenCore"`
  (expected: 0 — the names may appear in a comment explaining the invariant,
  never in a step that runs)
- `grep -E "dotnet-version: 10" .github/workflows/checks.yml
  .github/workflows/build.yml` (the single source of the `10.x` pin since
  `dotnet-format-check.yml` was deleted 2026-09-14)
- `grep -E "ADR-0131|ADR-0191" .github/AGENTS.md` (this file's own invariant
  section cites both ADRs; the workflow files carry the policy in a header
  comment)
- `grep -E "^  (checks|python-tests|core-unit-tests|ui-tests|headless-ui-tests):" .github/workflows/checks.yml`
  (expected: the five jobs the `main` ruleset requires)
- `grep -E "Werror" makefile` (the `CUTFLAGS` line; `-w` must not come back)
- `./scripts/checks/run_python_tests.sh` (38 files, ~25 s as of 2026-09-14)
- `gh api repos/sbihaiko/MesenAI/rulesets --jq '.[].name'`
- `grep -E "exclude-regex" .github/workflows/clang-format-check.yml`
- `python3 scripts/checks/verify_community_pack_issue_template.py`
- `python3 scripts/checks/verify_community_pack_submitted_workflow.py`
- `python3 scripts/checks/verify_community_pack_validate_workflow.py`
- `grep -F "cancel-in-progress: \${{ github.event_name == 'issues' }}" .github/workflows/community-pack-submitted.yml`
- `grep -A2 "id: classify" .github/workflows/community-pack-validate.yml | grep timeout-minutes`
- `./scripts/checks/verify_agents_md_recipe_handoff.sh`
- `grep -c "verify_community_pack_validate_workflow" makefile` — the
  community-pack verifiers and pipeline unit tests are wired into
  `make doc-checks` (2026-08-29), so CI runs them as a gate — the Linux/macOS
  build jobs until 2026-09-14, the `checks` job since (and there is no macOS
  build job any more, ADR-0191); the ROM-dependent validators
  (`validate_palette_variants.py`, `validate_hdpack_dump.py`) stay manual
  because they need a real ROM + `make core`.

## Child DOX Index

(none — leaf directory; `actions/` holds reusable composite actions with no
independent contract of their own)
