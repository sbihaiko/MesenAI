# ADR-0138: MEP Recipe v1 — declarative re-packaging of split-distribution packs, issue-held metadata and client auto-install from the web catalog

- Status: accepted (shipped F6.0–F6.7; PRD Part A §4 F6.x)
- Date: 2026-08-27
- Related: ADR-0039, ADR-0040, ADR-0041, ADR-0044, ADR-0049, ADR-0120, ADR-0121, ADR-0135, ADR-0139, ADR-0140, ADR-0141, ADR-0143, ADR-0145, ADR-0146, ADR-0147, ADR-0148, ADR-0151, ADR-0152
- Consolidates: ADR-0144
- Spec impact: `docs/specs/MEP-v1.md` §6; `docs/specs/MEP-recipe-v1.md`

## Decision

### 1. MEP Recipe v1 — a closed, declarative vocabulary

A recipe is JSON in a fenced ```mep-recipe block — **data not code**, interpreted
by a fixed op set (no scripting, no conditionals, no network beyond the sources):

```json
{ "recipe": 1,
  "sources": { "primary": { "url": "https://github.com/<owner>/<repo>/releases/download/<tag>/<file>.zip", "sha256": "<hex>" },
    "deps": [ { "id": "audio", "sha256": "<hex>", "size": 123456789, "hints": ["https://drive.google.com/..."], "license": "various online file", "user_supplied": true } ] },
  "ops": [ { "op": "copy", "from": "primary:hires.txt", "to": "hires.txt" },
    { "op": "copy", "from": "primary:SMB.ips", "to": "patches/SMB.ips" },
    { "op": "glob", "from": "audio:**/*.ogg", "to": "audio/" },
    { "op": "rename", "from": "audio/Track 01.ogg", "to": "audio/track01.ogg" },
    { "op": "rewrite-paths", "file": "hires.txt", "tags": ["bgm", "sfx"], "prefix": "audio/" } ],
  "pack": { "name": "...", "version": "...", "targets": [{ "sha1": "<no-intro sha1>" }], "patches": [{ "sha1": "<no-intro sha1>", "file": "patches/SMB.ips" }] },
  "policy": { "apply_patch_only_if_complete": true } }
```

- `ops` allowed in v1: `copy`, `glob`, `rename`, `rewrite-paths`; anything else is
  a validation error. `from` = `<source-id>:<path>`; paths normalized and MUST
  stay inside the output directory (zip-slip rule of §6).
- `pack` is the `pack.json` the client writes (MEP-v1 §3); `targets[]`/`patches[]`
  follow ADR-0044.
- `policy.apply_patch_only_if_complete: true` (default): missing/hash-failing dep
  → install **without** the patch and its dependent sections, UI says so.
- Sources verified by sha256 **before** any op; a mismatch aborts the install.

### 2. Issue is the source of truth

- Issue Form (`.github/ISSUE_TEMPLATE/community-pack.yml`) gains
  `external_assets` (one URL per line) and `external_assets_license`.
- classify (the only LLM, in CI) emits the ```mep-recipe block from lint report +
  manifest + form fields, treating all three as data, never instruction.
- CI validates/dry-runs with stdlib-only `scripts/mep_recipe.py` (`validate`,
  `dry-run`, `apply`): schema rejects unknown ops/escaping paths; dry-run applies
  ops to the downloaded primary (no `--dep`; `user_supplied` deps follow
  MEP-recipe-v1 §6 skip, §22) then runs `mep_lint.py`.
- Bot comment `<!-- mep-meta -->` carries `source_sha256`, per-dep `sha256`/`size`,
  `verdict`, `labels`, `validated_at`, `recipe_hash`, rewritten in place.
- Project "Pack Hash" field (`PVTF_lAHOB1MsbM4BhjpNzhge9Is`) stays the CI
  revalidation trigger (drift check / `/revalidate`).
- Label `assets:external` (user-supplied deps), created by
  `scripts/ensure_community_pack_labels.sh`.
- Clean-dry-run split pack → `accepted` (Status "Aceito parcial (HD Mesen)",
  `pack:valid` + `assets:external`); missing files, no viable recipe → `invalid`
  (MEP-v1 §5).

### 3. What the client reads — `docs/community-packs.json`

`scripts/generate_community_pack_catalog.py` emits, next to the Markdown, a
`docs/community-packs.json` from accepted board items and each issue's
`<!-- mep-meta -->` block and recipe (CI has the token; the client does not):

```json
{ "issue": 71, "game": "Super Mario Bros.", "console": "nes", "rom": { "sha1": ["<no-intro sha1>"] },
  "source": { "url": "...", "sha256": "..." },
  "deps": [ { "id": "audio", "sha256": "...", "hints": ["..."], "license": "...", "user_supplied": true } ],
  "recipe": { ... }, "verdict": "accepted", "validated_at": "2026-08-27T21:25:00Z", "catalog_version": 1 }
```

Served from
`raw.githubusercontent.com/sbihaiko/MesenAI/main/docs/community-packs.json` — one
unauthenticated, cacheable request.

### 4. Client — `MepRecipeInstaller` (C++, Core)

Fetches the catalog (ETag/If-None-Match, cached under the MEP `.cache`), matches
the ROM by No-Intro sha1 (MEP-v1 §4, ADR-0039); per matching accepted entry:
(1) download `source.url` (same host allow-list as CI; the recipe's
`sources.primary.url`/`sources.primary.sha256`), verify sha256; (2) per dep reuse
a local file with the declared sha256 (per-ROM pack folder or downloads cache),
else prompt with `hints` + license (never scrapes Drive/MEGA), install with
`policy.apply_patch_only_if_complete`; (3) run the `ops`, write `pack.json`, store
`.mep-install.json` (`catalog_version`, `source.sha256`, dep hashes, `recipe_hash`,
`installed_at`).

Output is the ADR-0040 central per-ROM folder. *Superseded by ADR-0147
(2026-09-01) for catalog installs: sibling `mep/` layer — `mep/` over bootstrap
`auto/` — with central `EnhancementPacks/<container>/` as fallback when the ROM
folder is not writable; sibling convention ADR-0049 as amended by ADR-0147.*

`AutoInstallCommunityPacks` (EnhancementPackConfig) default **on** for accepted
packs whose deps are all downloadable (i.e. no `user_supplied` dep). *Amended by
ADR-0141: the reinstall trigger is the catalog `content_id`, not a
`source.sha256` difference (§37).* **No LLM, no scripting**: unknown op or
`recipe` version → skip with `[MEP] recipe unsupported` log + UI notice.

### 5. Spec amendment (MEP-v1 §6)

"Hosts MUST NOT execute pack content **as code**. Patches (`patches[]`, ADR-0044)
and recipes (MEP-recipe-v1) are declarative data interpreted by a fixed host
vocabulary; hosts MUST reject any recipe operation outside it and any path that
escapes the pack directory."

### Clarifications (review-finding decisions; §N numbering preserved)

1. Pipeline: `lint` → `classify` → recipe gate (`mep_recipe.py validate` then
   `dry-run`, deps stubbed) → `apply-verdict` → upsert `<!-- mep-meta -->`.
2. Verdict precedence: gate inert without a recipe; with one it only
   **downgrades** `accepted` → `invalid` (schema failure or lint-unclean dry-run),
   never upgrades. `assets:external` is an additive content-index label like
   `assets:textures`/`assets:audio`, never a third verdict; verdict binary, Status
   "Aceito parcial (HD Mesen)". Asserted by the `scripts/checks/` verifier.
3. Dep hashes from the submitter: `sources.deps[].sha256` MUST; CI never downloads
   deps; each `external_assets` line = `<url> [<sha256>] [<size>]`. A line without
   a sha256 = "no recipe for this submission" → pre-ADR verdict (`invalid` when
   files missing, MEP-v1 §5) + comment tells `sha256sum <file>` and `/revalidate`.
4. LLM never writes hashes: classify emits `ops`, `deps[]` (`id`, `hints`,
   `license`, `user_supplied`), `pack`; a deterministic step assembles `sources`
   (`primary.url` from the pack link, `primary.sha256` from `steps.hash`, dep
   `sha256`/`size` from `external_assets`).
5. `<!-- mep-meta -->` bot-owned: rewritten wholesale each pass (PATCH the marked
   comment; never merged); missing/malformed = "no prior metadata". Concurrency:
   `community-pack-submitted.yml` groups events under
   `community-pack-submitted-<number>`; only cancellation restricted to `issues`.
6. `assets:external` derived, not judged: from a non-empty `sources.deps`;
   `apply-verdict` applies it like `assets:textures`/`assets:audio` (an `external`
   branch).
7. "No recipe": classify may omit `ops/deps/pack`; assembly refuses `sources` when
   any `external_assets` line lacks a sha256; either = "no usable recipe" → gate
   inert. The hash veto never lives in the prompt.
8. One workflow, one text verifier: extend the `CHECKS` tuple of
   `scripts/checks/verify_community_pack_validate_workflow.py` over
   `community-pack-validate.yml`; standalone verifiers only for new artefacts.
9. One named handoff: `mep_recipe.json` at the workspace root + boolean output
   `recipe_present`; every downstream reader uses it (`.github/AGENTS.md`).
10. `apply-verdict` sole verdict writer: gate emits only `recipe_ok`; `accepted` →
    `invalid` when `recipe_present && !recipe_ok`.
11. Provenance explicit: `sources.primary.sha256` CI-computed;
    `sources.deps[].sha256`/`size` submitter-declared with `user_supplied: true`;
    `recipe_hash` covers the recipe document, not dep contents (§4).
12. `external_assets` norm: `textarea` (id `external_assets`, optional), one dep
    per non-empty line; blank/`#` lines ignored; `<url> [<sha256>] [<size>]`
    (`sha256` = 64 lowercase hex, `size` = decimal bytes); a line lacking `sha256`
    disables assembly. `external_assets_license` single-line `input`.
13. Handoff location/status (amends §9): `$RUNNER_TEMP/mep_recipe.json`
    (runner-local, never in the checkout); `recipe_status` ∈
    `absent`/`present`/`refused`. Wherever §9/§10 say `recipe_present`, read
    `recipe_status == 'present'`.
14. No inverted regression guards: assert the desired steady state, never pin a
    failure message; the `verify_community_pack_issue_template.py` `snes` option
    defect fixed at root (CLAUDE.md, no `console:snes`).
15. Array-shaped verifiers: `verify_community_pack_labels_script.sh` keeps its
    expected name set outside the labels script; count derived from the set
    (`scripts/AGENTS.md`).
16. `recipe_ok` ≠ dep integrity: CI never fetches/hashes deps; `validate` +
    `dry-run` cover the recipe doc + CI-hashed primary only; dep digests verified
    by the client at install (§4, ADR-0006); mep-meta states "dep digests:
    submitter-declared, verified on install". Corollary: dry-run with no `--dep`
    skips dep-dependent ops (MEP-recipe-v1 §6, §22).
17. Issue body fetched, never from the event: `gh issue view "$ISSUE_NUMBER"
    --repo "$REPO" --json body -q .body` (`inputs.issue_number` is the reusable
    workflow's only identity input); no `github.event.issue.*` in new steps.
18. Two stores, one rule: Project "Pack Hash" authoritative bot-only store for the
    primary sha256; `<!-- mep-meta -->` sole store for dep digests, `recipe_hash`,
    provenance, rewritten wholesale. If mep-meta `source_sha256` disagrees with the
    field, the generator emits no recipe and logs (field wins).
19. Assembly failure never strands the item: `assemble-recipe` carries
    `continue-on-error: true`; `apply-verdict` keeps
    `steps.classify.outcome == 'success'`; empty `recipe_status` ≠ `present`; the
    downgrade reads `recipe_status == 'present' && !recipe_ok`; writes
    `verdict`/`labels` before the Project writes.
20. Classify schema: top-level `required: [verdict, assets, comment]`; one optional
    nested `recipe` object requiring `ops`, `deps`, `pack`. Assembly consumes
    `jq -r '.recipe // {}'`; `apply-verdict` exposes `verdict`/`labels`.
21. Lines are the spine: `sources.deps` one per `external_assets` line
    (synthesized `extN` ids when no hint URL matches, trailing-slash-normalized);
    classify's `deps[]` decorates id/hints/license; `user_supplied` forced `true`.
    Lines parsed before the fragment → hash-less/malformed = `refused` even without
    classify content; `absent` = no lines or well-formed lines but no classify
    content. mep-meta records `deps`/`recipe_hash` when `present` plus `recipe_ok`.
22. "Deps stubbed by name" = MEP-recipe-v1 §6, no CLI mode: no `--stub-dep`; the
    gate passes no `--dep`; `run_recipe` skips ops whose `from` is the missing dep,
    skips `rename`/`rewrite-paths` depending on a skipped op, skips patch dests,
    omits `pack.patches`. Placeholder sources rejected.
23. File-size guardrail: the 200-line-per-file cap wins; pre-declare splits (e.g.
    `Core/Shared/EnhancementPacks/MepRecipe*.{h,cpp}`). §8's "one verifier" = one
    entry point; `CHECKS` may import topic modules under `scripts/checks/`; the
    assembly may move to a sibling `assemble-sources` module sharing
    `RecipeError`/`SHA256_HEX` (F6.2c).
24. Split convention: shared symbols move to a dependency-free leaf
    (`mep_recipe_common.py`; `checks/community_pack_validate/_shared.py`) imported
    by both; the original file stays a back-compat facade. Topic packages under
    `scripts/checks/` are direct-script only (PEP 420; `python -m` unsupported).
25. Recipe document lives in mep-meta (pre-F6.3): `$RUNNER_TEMP` dies with the job,
    so `<!-- mep-meta -->` carries the full `recipe` object (with `deps`,
    `recipe_hash`, `recipe_ok`) when `recipe_status == 'present'`; the generator
    copies `recipe`/`recipe_hash` verbatim.
26. Catalog shape: `docs/community-packs.json` is MEI `mei: "1.1.0"`, index fields
    `name` "MesenCE community packs", `maintainer` `sbihaiko`, `updated` =
    generation date; one `packs[]` per accepted item (Pack URL → `url`, Pack Hash →
    `sha256`, ROM SHA1 → `rom.sha1`, Game → `game`; `game`/`system` from the Form
    fields), plus issue and mep-meta (`license` from `external_assets_license` or
    `"unknown"`). MEI v1.1: optional `kind` ∈ {`mep`, `hd-legacy`} (hd-legacy omits
    `version`/`mep`), `license` SHOULD (§34), `rom.sha1` MAY be absent, entries MAY
    carry `deps[]`/`recipe` (dep SHOULD carry `license`), `deps[]` copied from
    `recipe.sources.deps`; `catalog_version` unused. Golden
    `docs/specs/golden/mei/manifest.json` bumps 1.1.0; MEI-v1.md §2.2 gains the
    rows.
27. Generation/validation seams: generator writes the JSON next to the Markdown
    (`community-pack-catalog.yml` commits both); Markdown gains an "External assets"
    column (`yes` when `deps`). `validate-specs.py` gains `validate_mei_catalog()`
    over the golden and the committed `docs/community-packs.json`. mep-meta parsing
    is pure + tested; a malformed block skips that entry's recipe, never aborts.
28. MEI entry rules one owner: `validate-specs.py` (not importable) and
    `generate_community_pack_catalog.py` mirror v1.1 by hand; extract `MEI_SYSTEMS`,
    hash regexes, kind-conditional fields and `mei_entry_conforms` into a stdlib
    leaf `scripts/mei_rules.py` imported by both (F6.3b split created
    `scripts/mei_catalog_entry.py`, `scripts/community_pack_markdown.py`).
29. `kind` derivation: from mep-meta `verdict`, Project Status literal fallback;
    one shared constant; `mei_rules.resolve_kind` uses a valid mep-meta `kind`
    (allowed = `mei_rules.MEI_KINDS`) first, ignores unrecognised, falls back to
    `STATUS_TO_KIND[status]`, returns `None` if neither; unmapped Status → no entry.
    Parity check à la `verify_mep_fallback_constant_parity.sh`.
30. Provenance fields spec'd, not namespaced: `issue`, `verdict`, `validated_at`,
    `labels`, `recipe_hash`, `recipe_ok` are optional (MAY, v1.1) in MEI §2.2.
31. Versioning precedent: MEI v1.1 downgrades `rom.sha1` optional for every `kind`,
    not gated on the minor (MEI §2.3); absent `kind` = `mep` with the full 1.0 set;
    only explicit `hd-legacy` relaxes `version`/`mep`.
32. Generator gatekeeper: an accepted item that can't produce a spec-valid MEI
    entry is omitted from `packs[]` (warning names the issue); the Markdown row
    stays; `validate_mei_catalog()` strict.
33. Fence length guard: `json.dumps` doesn't escape backticks, so a
    submitter-supplied ``` inside `hints`/`license` truncates the block; emit the
    shortest backtick run longer than any in the payload; readers accept 3+ and
    match by length. `mep_recipe_common.choose_fence`/`find_fenced_block` are the
    rule, used by the workflow writer, `mep_recipe.py` and `mep_meta_parser.py`
    (`parse_mep_meta`). Shipped 2026-08-28.
34. `license` optional everywhere (user decision, 2026-08-28): MEP-v1 §3.1
    `license` SHOULD (was MUST); absent = `NOASSERTION`; hosts never refuse and
    show "not declared"; `mep_lint.py` warns; `validate-specs.py` type-checks when
    present; `MepPack.cpp` reads the `"unspecified"` default `MepPackManager`
    already uses. MEI §2.2/§2.3 SHOULD; MEP-recipe-v1 already SHOULD (default
    `NOASSERTION`).
35. File-size gate: the 200-line-per-file threshold wins; pre-declare extracted
    files in the task file list (F6.3/F6.4a lost cycles otherwise).
36. Convergence slices are not parallelisable across the files they unify: a
    duplicate-collapsing slice puts the leaf and every call site in one file list.
37. F6.4 split at the network boundary: `Core/` gains no HTTP; the UI uses
    `HttpClient` (`UpdatePromptViewModel`). F6.4a offline Core installer
    `Core/Shared/EnhancementPacks/MepRecipeInstaller.{h,cpp}` interprets the four
    ops on local files (recipe via `Utilities/JsonReader`, primary zip, dep-id →
    path map), verifies sha256 (mismatch aborts), applies
    `policy.apply_patch_only_if_complete` (missing dep → patch + dependent
    `rename`/`rewrite-paths` withheld, textures applied, `withheld` reported),
    writes `pack.json` + `.mep-install.json` (`recipe_hash`, `source.sha256`, dep
    hashes, `installed_at`), skips with `[MEP] recipe unsupported`; ships
    `EnhancementPackConfig`'s `bool AutoInstallCommunityPacks = true`.
    Verification `scripts/core_unit_tests.cpp`: the golden `mep-recipe` fixture
    installed by C++ equals `mep_recipe.py apply` byte-for-byte; hash mismatch
    aborts; missing dep withholds the patch. F6.4b UI: catalog fetch (ETag), sha1
    match, download within allow-list, downloads-cache lookup, prompt; headless
    test via `gen_mep_test_pack.py`; reinstall trigger amended by ADR-0141 →
    `content_id`. F6.4b-2 file list: `UI/Services/CommunityPackCatalogFetcher.cs`,
    `UI/Services/CommunityPackInstallCoordinator.cs`,
    `UI/Services/CommunityPackInstallService.cs`, the embedded allow-list + check
    (§41), `CommunityPackAutoInstallConsentGiven` in `EnhancementPackConfig.cs`, the
    `EnhancementPacksWindow` dialog, the `MainWindow` ROM-load hook.
38. `AutoInstallCommunityPacks` default/consent: *Superseded by ADR-0146
    (2026-09-01; auto-load, no consent dialog) — the toggle stands.*
39. Two interpreters, one normative reference: `scripts/mep_recipe.py` normative;
    `MepRecipeInstaller`/`MepRecipeOps` (C++) re-derive it incl. `mep_lint.py` root
    discovery; any spec change lands both sides same commit. Golden-parity test in
    `scripts/core_unit_tests.cpp` (Bloco E) over
    `docs/specs/golden/mep-recipe/fixture/` generated by
    `scripts/gen_mep_recipe_fixture.py` (tiny zips, fixed timestamps, `ZIP_STORED`;
    `test_gen_mep_recipe_fixture.py` proves byte-identical). Shipped 2026-08-28
    (`05ef767a`): `primary.zip`, `wrapped-subfolder.zip`, `nested-zip.zip`,
    `bare-probe.zip`, `audio-dep.zip` + `recipe*.json`. `SHA256::GetHash(path)`
    returns `""` on an unopenable file.
40. Critic false positives on Core code: when a finding names no file and the actor
    has answered it, merge by hand (F6.4a T4 recovered via `git fsck --unreachable`).
41. Allow-list packaging (F6.4b audit, PRIORITY 1):
    `scripts/pack_host_allowlist.json` single source; `UI/UI.csproj` embeds it as
    `EmbeddedResource` (`LogicalName` `Mesen.pack_host_allowlist.json`,
    `Include="../scripts/pack_host_allowlist.json"`), loaded via
    `CommunityPackHostAllowlist.LoadFromStream` from the assembly manifest (never
    repo-relative); a `scripts/checks/` check pins that path. `MatchHost` mirrors
    `fetch_pack.py:match_host` (HTTPS-only, exact netloc, `path_contains_any`).
42. Wire shape authority: `docs/specs/MEI-v1.md` normative; §3's sketch
    (`source.{url,sha256}`, `rom.sha1` array) superseded by the real shape —
    `url`/`size`/`sha256` at the entry top level, `rom.sha1` scalar,
    `mei`/`name`/`packs` envelope; "catalog `source.sha256`" reads "the entry's
    `sha256`"; `.mep-install.json` keeps `source.sha256`.
43. Reinstall vs user-owned state: auto-reinstall only when (a) the target has an
    installer-written `.mep-install.json`, (b) its `source.sha256` differs from the
    entry's `sha256`, (c) the container is not in `DisabledPacks`; a folder without
    the stamp is user-owned and never overwritten; a disabled pack is skipped.
44. UI.Tests AOT contract: `UI.Tests.csproj` sets
    `JsonSerializerIsReflectionEnabledByDefault=false` and `IsAotCompatible=true`
    like `UI.csproj`, so reflection JSON fails `dotnet test`.
45. `recipe` travels opaque: `CommunityPackCatalogEntry.Recipe` is `JsonElement?`;
    the service serializes it (`GetRawText()`) and hands it to `InstallMepRecipe`
    unchanged; the UI never interprets it.
46. Downloads cache = `<EnhancementPackFolder>/.cache/downloads/` (ADR-0040 scratch,
    safe to delete); primaries and user deps looked up there by sha256.
47. Interop encoding for `InstallMepRecipe`: the dep-id → path map crosses as UTF-8
    text, one `id<TAB>path` row per line (`EmuApiWrapperMep.cpp`, same as
    `GetMepPackList`); the recipe crosses as raw JSON text; result via the char*
    out-buffer.
48. Interop registries exempt from the 200-line cap: `UI/Interop/EmuApi.cs`,
    `InteropDLL/EmuApiWrapper.cpp`; new export families in sibling files
    (`EmuApiWrapperMep.cpp`).
49. UI-only settings: an `EnhancementPackConfig` (C#) field no native code reads
    (`CommunityPackAutoInstallConsentGiven`) is not mirrored into
    `InteropEnhancementPackConfig`; `AutoInstallCommunityPacks` stays mirrored
    because `SettingTypes.h` declares it.
50. Client download trust contract (F6.4b-2 audit): every client GET (catalog
    included) through `UI/Services/CommunityPackDownloader.GetAsync` — redirects
    never auto-followed, each hop re-checked against the embedded allow-list
    (`MatchHost`, https only, 5-hop cap, mirroring `fetch_pack.py`'s
    `open_validated`), body capped before buffering (300MB artifacts = CI ceiling,
    16MB catalog); the catalog URL is a compile-time constant but not exempt from
    `MatchHost`; DNS/private-range checks CI-only; the declared `sha256` doubles as
    the `.cache/downloads/` name, validated as 64 hex chars.
51. §38 gate ownership + per-session idempotency: *consent clause superseded by
    ADR-0146; the per-session `HashSet` and `.mep-install.json` stamp remain.*
    `CommunityPackInstallService` (ROM-load hook) owns the gate: evaluates
    `CommunityPackConsentState` (dialog via
    `EnhancementPacksWindow.EnsureCommunityPackAutoInstallConsent`) and a
    per-process `HashSet` of attempted ROM sha1s before any network call;
    `CommunityPackInstallCoordinator`'s `NeedsConsent` is defense in depth.
52. Container name host-free: sanitization of the catalog `name`/`game` into a
    folder name and `DisabledPacks` key in `UI/Logic/CommunityPackContainerName.cs`
    (pinned by UI.Tests); `CommunityPackInstallCoordinator.ResolveOutFolder` roots
    it under `EnhancementPackFolder` and asserts it stayed there (ADR-0127).
53. Three-layer rule for `UI/`: `UI/Logic` host-free (BCL only, dual-compiled into
    UI.Tests); `UI/Services` host/network (`HttpClient`, `EmuApi`, file I/O);
    `UI/Windows`/`UI/ViewModels` presentation. `scripts/verify-ui-logic-firewall.sh`
    enforces both directions (`UI/Logic/*.cs` never references
    `Mesen.Services`/`HttpClient`; `HttpClient` under `UI/` confined to
    `UI/Services/*.cs` + `UpdatePromptViewModel`).
54. Consent dialog placement — decided, stop re-raising: *superseded by ADR-0146 —
    removed from the auto-install path.* A public static helper on
    `EnhancementPacksWindow`, called from Services via the UI dispatcher; the
    decision stays in `UI/Logic/CommunityPackConsentState`.
55. Closed without action (F6.4b-2 audit): (a) `Link=` on the embedded allow-list
    not needed (`LogicalName` is the only handle); (b) a build-level proof `Core/`
    has no HTTP not needed — `verify_core_no_http_client.sh` is proportionate;
    (c) moving the reinstall `Directory.Delete` into a Core "replace mode" of
    `MepRecipeInstaller::Install`: deferred.
56. Patch-redeemed audio (ADR-0144, as amended by ADR-0148): an `audio` section
    counts as usable when its `hires.txt` references `.ogg` tracks (bare or any
    path) AND the zip bundles a `.ips`/`.bps` ROM patch present AND wired
    (`<patch>` line / `patches[]` entry) — the tracks come at install time from the
    extract-audio flow (ADR-0135), the §2/§7 exception applied to audio. No path
    constraint: the loader resolves each ref as written
    (`FolderUtilities::CombinePath(_hdPackFolder, <ref>)`, `HdPackLoader.cpp`
    `ProcessBgmTag`); an unwired patch redeems nothing (#128).
    `.github/ai/validate-classify.md` (AUDIO EXCEPTION) is amended: a missing
    `.ogg` does not by itself invalidate a patch-bundling pack; a wired patch does
    not redeem missing `textures`/`synth`. `scripts/mep_lint.py` prints
    `bundled patch: <name> (present, wired — …)` /
    `(present, NOT wired — …)`, kept even in `--quiet`; tests in
    `scripts/test_mep_audio_patch_resolution.py` (`6d16c55f`). Not implemented: the
    extract-audio tool (ADR-0135, `NesConsole::ExtractAudioHdPack`,
    `Core/NES/NesConsole.h`) is a separate headless process not invoked by
    `UI/Services/CommunityPackInstallCoordinator.cs`; re-scoped by ADR-0240
    (2026-10-01) to F6.9 reporting "audio not generated", generation a future
    opt-in (Option 2), name-mapping spiked as F6.10.

## Context

Five of twelve packs triaged 2026-08-27 (#65 1942, #66 Dr. Mario, #68 SMB2, #69
Yie Ar Kung-Fu, #71 SMB, LiQuiDzGit/HDnes family) are `pack:invalid`, rightly: the
release zip holds only `hires.txt` plus an IPS/BPS patch, while every `.ogg`
referenced by `<bgm>`/`<sfx>` is hosted separately (Google Drive/MEGA, outside the
CI allow-list). MEP-v1 §5 counts a section only when its files resolve inside the
archive. Installing as-is is worse: the `<patch>` is applied, then
`HdPackLoader::ProcessSoundTrack` (`Core/NES/HdPacks/HdPackLoader.cpp`) fails
`CheckFile` on every missing OGG and the game plays **silent**.

User constraints: metadata lives **in the issue**, programmatically, executed
client-side; **no LLM in the client**; the client auto-installs any pack in
`docs/community-packs.md` over the web; custom fields including the **sha256 of
the source zip**. GitHub Issues have no custom fields; Project fields exist but
the GraphQL API needs a token an anonymous emulator lacks; REST issue bodies are
rate-limited (60 req/h per IP). MEP-v1 §6 ("Hosts MUST NOT execute pack content;
everything is declarative data") forbids an arbitrary attached script.

## Consequences

- The five HDnes packs become installable with one user action, never in the
  silent-game state; the client gains a network dependency, opt-out via the
  setting, reusing the CI host allow-list verbatim.
- A recipe-vocabulary change is a new `recipe` version + new ADR; old clients skip
  newer recipes rather than misinterpret them.
- F6.0 prerequisite: `community-pack-submitted.yml` must not cancel its own run
  when the verdict comment fires `issue_comment` (`cancel-in-progress: true` →
  `cancel-in-progress: ${{ github.event_name == 'issues' }}`), else the JSON
  catalog goes stale (`timeout-minutes: 15` on classify);
  `community-pack-drift-check.yml` is the scheduled trigger.
- Not implemented (2026-10-01): automatic generation of a patch-bundling audio
  pack's `.ogg` tracks (§56).

## Record

- 2026-08-27 — design agreed; `accepted`.
- 2026-08-28 — F6.0–F6.4c shipped (runs `05a8927950be`, `9967a42e92f1`,
  `4f0d742630e5`, `3cca17a3180c`; fixture set `05ef767a`); F6.4b-2 run
  `119e1031a25f`; F6.4b second half run `2ef26ba839d1`.
- 2026-08-29 — F6.5/F6.6 shipped (`22ef1971`).
- 2026-09-01 — F6.7 auto-load (ADR-0146) supersedes §38/§51/§54 consent clauses;
  ADR-0141 amends §37, ADR-0147 amends §4.
- 2026-09-06 — code-review pass: `scripts/fetch_pack.py` non-following redirect
  handler (per-hop allow-list, HTTPS-only, hostname not netloc, no userinfo, port
  443, public IP, ≤5 hops); `mep_recipe.py validate --require-allowlisted-hosts`
  used by CI and `scripts/validate_pack_local.sh` (default byte-identical, so the
  `docs/specs/golden/` `example.org` URLs keep working); linter refuses archive
  members above 300 MB; classify runs with `Write`/`Edit`/`MultiEdit`/
  `NotebookEdit`/`WebFetch`/`WebSearch`/`Task` disallowed in addition to
  `Bash`/`Read`; actions pinned to a commit SHA; secrets passed explicitly instead
  of `secrets: inherit`; the C# downloader already had `AllowAutoRedirect = false`
  and now carries a per-request timeout, DNS resolved twice.
