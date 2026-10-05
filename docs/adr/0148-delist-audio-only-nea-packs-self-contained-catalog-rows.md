# ADR-0148: A listed catalog row must be a self-contained, verifiable artifact; audio-only NEA packs with off-catalog assets and stale shared-zip hashes are de-listed, not left to fail silently

- Status: accepted
- Date: 2026-09-01
- Related: ADR-0138 §37/§43 (reinstall trigger on `source.sha256`, since
  amended by ADR-0141), ADR-0141 (one live slot per `pack_id`), ADR-0143
  (`pack_id` = origin × game, `pack:split` sibling expansion), ADR-0144
  (audio packs may supply `.ogg` tracks via a bundled ROM patch), ADR-0146
  (auto-load every accepted pack whenever possible); PRD Part A §1
  principles 1–3 and §4 slice D4; CLAUDE.md "Policy — auto-load all
  registered packs".
- Amends ADR-0144 (Decision): a bundled `.ips`/`.bps` counts toward the
  audio exception only when it is also *wired* — referenced by a `<patch>`
  line (`hires.txt`) or a `patches[]` entry (`pack.json`) — so the
  extract-audio flow actually has a patched ROM to work from. ADR-0144 as
  written (and `.github/ai/validate-classify.md`, which implements it)
  requires only that the patch be present in the archive.
- Amended by: ADR-0151 (extends "self-contained, verifiable" to intra-artifact reference resolution), ADR-0152 (known-missing errata keeps a usable pack listed)

## Context
The LiQuiDzGit/HDnes submission (#128) is a single GitHub archive zip
(`https://github.com/LiQuiDzGit/HDnes/archive/refs/heads/main.zip`) holding
many game folders (live 2026-09-01, sha256 `2146b7f3…0b84f4`, 15 game folders
plus `HTML/`). Per ADR-0143 the validate workflow expanded it into nine sibling
issues — #128 (1942), #129 (Bio Miracle Bokutte Upa), #130 (Dr. Mario), #131
(Duck Hunt), #132 (Ice Climber), #133 (Ice Climber (VS)), #134 (Super Mario
Bros. 2), #135 (Super Mario Bros.), #136 (Urban Champion) — each with
`pack:valid` + `pack:split`, its own `pack_id` (`liquidzgit/hdnes:<game>`) and
the same pinned `source_sha256` `2281b9fa…751687e`, validated 2026-08-29
21:32–21:38Z. All nine were live rows in `docs/community-packs.json`.

Eight of the nine are "NEA" audio packs: their `hires.txt` declares only
`<bgm>`/`<sfx>` tracks (labels `assets:audio` + `patch:ips` on #128, #129,
#135 or `patch:bps` on #130, #131, #133, #134, #136; no `assets:textures`); the
`.ogg` files are distributed separately and were never inside the archive (the
lint on #128–#130, #133–#136 reports `<bgm> file does not exist …
track/effect never registered`; #131 linted clean, "0 errors / 0 warnings",
its tracks absent rather than dangling), and the #128 closing comment records
"`hires.txt` has no `<patch>` line so the .ips is never applied". They were
classified `accepted` under the ADR-0144 audio exception. Only #132 (Ice
Climber) carries `assets:textures`.

In the client a row is downloaded and its bytes verified against the row's
`sha256` before install (`UI/Services/CommunityPackCatalogFetcher.cs`,
`DownloadAndVerifyAsync` / `ComputeSha256`; `UI/Services/CommunityPackInstallCoordinator.cs`
only writes the `source.sha256` stamp; ADR-0138 §43). GitHub branch archives are
regenerated every push, so the shared `main.zip` stopped hashing to the pinned
value: none of the nine ever applied, invisibly to the player. (Since `3bc4482d`,
2026-09-01, the fetcher treats a whole-repo `archive/refs/heads/<branch>.zip`
as mutable and installs it optimistically on a hash mismatch under ADR-0146 —
removing the staleness failure mode, not the self-containment one: these eight
still contribute "no audio and no graphics".) Commit `916ca35e` (2026-08-30) had
just added nested-game-folder support and audio-only-with-texture merging, which
would have made these packs usable had they verified.

On 2026-08-31 the maintainer removed the eight audio-only siblings from the
catalog and closed their issues: commit `7bc8f13a` ("chore: remove 1942
community pack from documentation and data list", #128) and commit `fd244f2a`
("chore: remove legacy community packs from documentation", #129–#131, #133–#136; 161 JSON lines + 7 Markdown rows). The closing comment on
#129–#131 and #133–#136 (identical text) reads: "Removing this pack from the catalog and closing the issue: it is an audio-only NEA pack (no textures), and the catalog's pinned sha256 for the shared LiQuiDzGit/HDnes repo zip is stale, so the download verification fails and the pack never applies. Audio-only NEA packs were removed from the catalog as a group. If the pack author ships a self-contained, texture-bearing artifact, it can be resubmitted." The comment on #128 reads: "the artifact is not playable standalone. The NEA audio tracks are distributed separately (not in the pack archive), `hires.txt` has no `<patch>` line so the .ips is never applied, and the track-7 filename comma ("Boss 1, Mid Boss A.ogg") broke the previous parser. With no .ogg files and no patch wiring, the pack contributes no audio and no graphics. It will be reconsidered if the pack author ships a self-contained artifact." The eight issues kept their `pack:valid` + `pack:split` labels; on the "MesenCE Community Packs" board their Status is "Inválido" (verified 2026-09-01 via `gh project item-list`), which keeps `scripts/generate_community_pack_catalog.py` — it selects rows by board Status, never by issue state — from re-adding them. #132 (Ice Climber) stays open, Status "Aceito parcial (HD Mesen)", and is the only row of the family listed today.

Until now this product decision existed only in those issue comments and commit messages (PRD §4, audit 2026-09-01, slice D4).

## Decision
1. **A listed catalog row must resolve to a self-contained artifact the
   client can verify and apply.** "Self-contained" means the archive at the
   row's URL, hashed to the row's `sha256`, contains everything the manifest
   references — textures, or audio tracks either bundled or produced at install
   time from a bundled **and wired** ROM patch. This ADR tightens ADR-0144,
   which it amends: ADR-0144 accepts a `.ips`/`.bps` merely present in the
   archive; from now on the patch must also be referenced by a `<patch>` line /
   `patches[]` entry so it is actually applied (the eight LiQuiDz siblings met
   ADR-0144 and fail this rule). A pack whose only payload is an audio manifest
   pointing at off-catalog `.ogg` files is **not listable**, even when
   `mep_lint` passes and the classify verdict is `accepted`: lint validity is
   necessary, not sufficient, for a row.
2. **A stale pinned `sha256` of a shared artifact is grounds for de-listing,
   not silent failure.** When the bytes at a row's URL no longer match the
   pinned hash, the row is removed from `docs/community-packs.json`/`.md`
   (board Status → "Inválido" so the generator does not restore it) and the
   issue receives a comment stating the reason. It is re-listed when the pack is
   re-validated against an artifact satisfying rule 1 — for the LiQuiDz family
   the comments set the bar as "a self-contained, texture-bearing artifact"
   (or, for #128, "a self-contained artifact"); a fully bundled or patch-wired
   audio pack (ADR-0144 as amended by rule 1) also satisfies rule 1. Re-validation
   happens through the existing `/revalidate` comment or a fresh submission,
   both of which recompute and re-pin the hash.
3. **Composition with ADR-0146 / CLAUDE.md auto-load policy.** "Accepted"
   in "auto-load every accepted pack whenever possible" means *present as a
   live row in `docs/community-packs.json`*. A closed issue is not a row, the
   `pack:valid` label alone does not make a pack loadable, and de-listing under
   rules 1–2 is one of the "whenever possible" exclusions — it is how the
   catalog acts as the trust boundary ADR-0146 assigns it ("a bad pack is removed from the catalog, not gated behind a per-user prompt"). Removal never uninstalls or interrupts a player who already has the pack (ADR-0141: pack removed from the catalog → keep install and per-ROM choice, no toast).
4. **Composition with ADR-0143.** A multi-game zip is still expanded into N
   sibling packs and N sibling issues with distinct `pack_id`s and distinct
   slots (ADR-0141). Each sibling must satisfy rule 1 **on its own**: one
   texture-bearing sibling (Ice Climber, #132) does not carry the audio-only
   siblings that share its archive, and de-listing eight siblings leaves the
   ninth listed.
5. **History, not invalidity.** The de-listed issues stay closed with their
   `pack:valid` + `pack:split` labels — the classify verdict was correct under
   the rules then in force (ADR-0144 before this amendment), and the record of
   the ADR-0143 split stays intact for the author's resubmission. They are not
   re-labeled `pack:invalid`; the board Status ("Inválido") is the catalog-side
   switch, the labels are the verdict history.

## Consequences
- Implemented today (verified 2026-09-01): the eight rows are gone from
  `docs/community-packs.json` (11 rows remain, one LiQuiDz row: #132); the eight
  board items sit in "Inválido" so `scripts/generate_community_pack_catalog.py`
  (which filters by `ACCEPTED_STATUSES` only) does not restore them; the issues
  are closed with `pack:valid` + `pack:split`. The daily
  `.github/workflows/community-pack-drift-check.yml` already recomputes each
  accepted item's hash with `curl`+`sha256sum` and, on a change, comments
  "the pack's content changed since the last validation — revalidating automatically" and re-runs validation — re-pinning the hash rather than de-listing.
- Implemented 2026-09-01: `scripts/mep_lint.py`'s `scan_bundled_patches` now
  tags every `bundled patch:` line as `(present, wired — applied on load)` or
  `(present, NOT wired — …; ADR-0148)` from the `<patch>` lines / `patches[]`
  entries it linted, and the classify step (`.github/ai/validate-classify.md`)
  applies the ADR-0144 audio exception only to a wired patch. The classify step
  also has a LISTABILITY rule that refuses (verdict `invalid`, with a comment
  naming the fix) a lint-valid but unlistable pack. **Confirmed 2026-09-03
  without a CI run**, against the real `.github/ai/validate-classify.md` prompt
  driven offline by `scripts/validate_pack_local.sh --pack-file` over the
  fixture `tests/fixtures/community-pack/adr0148-rule1-unlistable/`: a lint-valid
  pack (0 errors) whose `<bgm>`/`<sfx>` targets are absent and whose bundled
  `.ips` is unwired returned `verdict=invalid` naming the three ways to make it
  listable. A prompt-injection variant (override text in a bundled README, the
  patch file name and the issue's game field) reached the rendered prompt and
  did not change the verdict.
- Follow-up, not implemented: the catalog generator has no closed-issue or
  "de-listed" awareness beyond board Status; a row-level self-containment check
  (rule 1) does not exist in `generate_community_pack_catalog.py`,
  `mei_catalog_entry.py` or `pack_id_rules.py`.
- Follow-up, not implemented: whole-repo branch archives
  (`…/archive/refs/heads/<branch>.zip`) are re-hashed on every push; the
  remaining Ice Climber row (#132) pins the same `2281b9fa…751687e` and is
  exposed to the same staleness. A stable per-release or per-commit URL (the
  "URL granular pendente" noted in the 2026-08-31 catalog hash audit) is the
  durable fix; until then a drift-check hit on such a row should trigger rule 2
  handling, not only re-validation. Since `3bc4482d` the client tolerates the
  mismatch for such URLs, so #132 now installs; rule 2 remains the catalog-side
  rule for immutable URLs and the catalog's own integrity.
- Client behavior: for immutable URLs a `sha256` mismatch still aborts the
  install (`CommunityPackCatalogFetcher.DownloadAndVerifyAsync` returns null,
  logging "sha256 MISMATCH"; `MepRecipeInstaller.cpp` "sha256 mismatch for
  …"); for mutable branch archives the fetcher installs optimistically
  (ADR-0146, `3bc4482d`). Surfacing the abort to the player instead of logging
  it is out of scope here (Advanced GUI, PRD Part B).

## Alternatives
- Keep the rows listed and let the client fail verification — rejected: the
  failure is silent (no toast, no log the player sees), breaking ADR-0146's
  product promise invisibly while the catalog claims the pack exists; this is
  exactly the 2026-08-29 → 2026-08-31 state.
- Re-label the eight issues `pack:invalid` / re-run classify to an `invalid`
  verdict — rejected: the packs are lint-valid and the classify verdict was
  right under ADR-0144; `pack:invalid` would misdescribe them and erase the
  ADR-0143 split history the author needs to resubmit. Board Status "Inválido"
  already removes them from the generator's input.
- Bundle the author's `.ogg` tracks ourselves (mirror or commit them) so the
  packs become self-contained — rejected: the official channel carries only
  clean data and derivative content is referenced, never hosted or committed
  (PRD Part A §1 principles 1–3).
- Re-pin the stale `sha256` to the current `main.zip` and keep the eight rows —
  rejected: it restores rows that still contribute "no audio and no graphics"
  (#128 comment) and would go stale again on the author's next push; only #132,
  which has textures, was worth keeping on that basis.
