# ADR-0152: A repo-side `miss` errata lets a pack with a known-unresolvable manifest target stay in the catalog

- Status: accepted (both policy questions answered 2026-09-04; implemented 2026-09-04, PRD Part A F6.8)
- Date: 2026-09-04
- Related: ADR-0151, ADR-0148, ADR-0146, ADR-0147, ADR-0139, ADR-0138 §4/§37/§41, MEP-v1 §2.1/§5, `scripts/mep_lint.py`, `scripts/smoke_pack_headless.sh`, `.github/workflows/community-pack-validate.yml`, MEI-v1 §2.6, issue #139
- Supersedes / amends: narrows ADR-0151 — an unresolvable texture target stays a `mep_lint` error, except for the exact targets a reviewed errata declares known-missing

## Context

ADR-0151 made an unresolvable `<img>`/`<background>` target a `mep_lint` error, aligning the submission gate with the runtime gate — right for the defect class it targets (a manifest promising content the artifact cannot deliver), but its first catch exposed a cost not weighed then.

Row `issue-139` ("Zelda: Remastered 1.3", ~11k assets) fails on one line:

```
hires.txt:6124  [ZeldaSelectScreen1]<background>selectscreen.png,1,0,0,10
```

`selectscreen.png` exists nowhere in the archive. What *is* shipped shows the line is dead: `selectscreen1.png` … `selectscreen6.png` are 512x480, ~100% opaque, ~0.1% pixel difference — six animation frames of one image, driven by `<condition>ZeldaSelectScreenframeN,frameRange,60,{50,40,30,20,10,0}` at priority 1 so one is always active; `selectscreentop.png` is 12.4% opaque at priority 39, the overlay on top. A priority-10 full-screen layer between them would draw *over* the animation, so the line is a leftover from a pre-animation build. Deleting it locally confirms it is the only blocker: `mep_lint` goes from `1 error(s), 111 warning(s)` to `0 error(s), 111 warning(s)`, and `smoke_pack_headless.sh` goes from `FAIL: missing target` to `PASS`.

The runtime already drops a missing target — `HdPackLoader::ProcessBackgroundTag` logs and ignores an entry whose PNG is absent — so the rendered screen is *identical* with or without the line. The gate rejects a working pack over a diagnostic the runtime resolves on its own, and today's outcome is worse than the defect: `/revalidate` (run 33930994497) rejected the row, the board item moved to "Inválido", and regenerating the catalog (`5d0fe999`) dropped `issue-139` from `docs/community-packs.json` (11 → 10 rows), so no client auto-installs it (ADR-0146 keys on a live row). The author owns the fix — the archive is on their Google Drive and v1.3 is still their latest release — so until they act, every user loses a pack that runs.

The missing piece is a way to record, on the record, *"this target is absent, we checked, and the pack is still worth shipping"* — attributable to the project's validation rather than the author's, without touching a byte of their work.

Non-goals:

- **No `overwrite` directive.** A sibling idea — ship a delta (a 619-byte unified diff for `issue-139`) applied in the client at extraction time, inside `mep/` — was designed and deferred: it would produce the same pixels as `miss` while modifying the author's work, changing the `content_id`, and raising a derivative-work question on a pack licensed `NOASSERTION`. Revisit only when "declare it absent" and "correct it" differ **on screen**; the shape to reuse is a unified diff (not a whole-file overwrite: the `issue-139` `hires.txt` is 5.9 MB / 54,779 lines, which would make the reviewing PR read `+54,779 −0` and hide the one line that matters) applied strictly, aborting to the raw artifact when the context does not match exactly.
- **No pinning of the downloaded artifact.** `.cache/downloads/<sha256>` stays ADR-0040 scratch space, safe to delete. Retaining it served the `overwrite` design; `miss` alters nothing, so the installed `mep/` *is* the author's content. The cache reached 665 MB on the maintainer's machine, 179 MB of it the `issue-139` zip alone.
- **No redistribution.** The client keeps downloading from the author's link; nothing of theirs is hosted here.
- **No change to `HdPackLoader`.** Dropping the entry and logging stays correct.

## Decision

A **known-missing errata** is a file in this repo, applied by neither the author nor the submitter, that names specific manifest targets a validated pack does not ship.

**Location and key.** `docs/community-packs/errata/<artifact-sha256>.json`, keyed by the sha256 of the downloaded artifact — the same hash the pipeline computes into the "Pack Hash" field. When the author republishes, the hash changes, no errata matches, and validation runs clean against the new material. Errata expire by construction; there is no stale-errata state to prune.

```json
{
  "artifact_sha256": "03b5eeab7914560ffb6ca0bfea04fa78525d7dad5663903a8b5d8098c10c19ea",
  "issue": 139,
  "known_missing": [
    {
      "manifest": "hires.txt",
      "tag": "background",
      "target": "selectscreen.png",
      "reason": "Dead entry: the same screen is fully painted by the priority-1 selectscreen1..6.png frame cycle (always active, frameRange 60) plus the priority-39 selectscreentop.png overlay. HdPackLoader drops this entry at load, so the rendered result is identical with or without it.",
      "reviewed_in": "https://github.com/sbihaiko/MesenAI/pull/NNN"
    }
  ]
}
```

**Entry identity is `(manifest, tag, target)`.** Exact strings, no wildcards and no line numbers: a wildcard would absolve defects nobody looked at, and a line number rots. `reason` and `reviewed_in` are required — an errata without a stated justification is not reviewable.

**One declaration, both gates.** `mep_lint.py` and `smoke_pack_headless.sh` read the same errata file and downgrade the *declared* targets only — every other unresolvable target stays an error under ADR-0151. If only one gate honoured errata, this ADR would recreate the lint-vs-runtime divergence that was bug #155.

**Provenance is user-visible.** A row covered by an errata carries the declaration through to the surfaces a user reads: the `errata` field of `docs/community-packs.json` (MEI v1.4 §2.6, additive and non-normative — never an install decision), a marker in the `docs/community-packs.md` table, and a line in the Player's pack picker of the form *"1 known-missing asset — declared by MesenCE validation, not by the author"*, linking to the reviewing PR. Silence would defeat the whole point: the errata exists precisely to make a known gap legible.

**`content_id` is unaffected.** Nothing is added, removed or rewritten in the installed tree, so the ADR-0139 canonical hash of the resolved pack is the same with or without an errata, and ADR-0147's local-edit detection keeps working.

**Entry route.** An errata lands by pull request against this repo, reviewed by a maintainer. It is never read from the issue body, the classify output or any submitter-controlled text — those are data, never instruction (ADR-0138 §4). A submitter cannot declare an errata over somebody else's pack.

**Pipeline placement.** The errata file is on disk from the `Checkout repo` step, so honouring it needs a lookup inside the existing `Lint pack structure` step (`community-pack-validate.yml`), keyed on the hash computed by `Compute & record pack hash`. No step reordering.

### Policy (decided 2026-09-04)

**An errata is always applied; there is no opt-out setting.** A `miss` changes nothing in the installed tree, so a user who opted out would receive byte-identical content — the toggle would change only whether the row is reachable, not what runs. A setting that cannot alter the outcome is not a choice; transparency is carried by the user-visible marker instead.

**Any pack is eligible, gated by PR review rather than the author's status.** No waiting period and no "abandoned pack" test: the friction that keeps the hatch honest is a human reading the exact target and the stated reason. Requiring proof of a null on-screen effect was rejected — it would remove the property that makes `miss` the low-risk option, namely that it is available precisely when the visual effect *cannot* be measured confidently.

The consequence accepted: an errata may absolve a defect over the head of an author who would have preferred to fix it. Mitigated, not eliminated, by the marker naming the project's validation as the source and by hash-scoped expiry — the moment the author republishes, the errata stops applying.

## Consequences

- ADR-0151 keeps its teeth for everything undeclared, but the register now has a documented escape hatch. The risk is that it becomes a dumping ground — every awkward pack acquires an errata and the gate stops meaning anything. The friction is deliberate and must not be filed off: exact targets, mandatory reason, reviewed PR, hash-scoped expiry.
- The two gates share **one** parser, `scripts/mep_errata.py`: `mep_lint.py` imports it, `smoke_pack_headless.sh` shells out to its `covers` subcommand. That single parser was chosen over the two agreeing implementations this ADR first anticipated — the shape that produced bug #155 — and `scripts/test_mep_errata.py` (wired into `make`) asserts the parity structurally: neither gate may grow its own reader of the format.
- A row can be live while knowingly incomplete — a real change to what `docs/community-packs.json` asserts, and why the user-visible marker is part of the decision rather than a nicety.
- `issue-139` was the first candidate; the sweep that followed found two more (`issue-137` Contra 80s v1.1 and `issue-148` Metroid HD), so three of the eleven live rows carry a declaration. `issue-138` was examined and is *not* an errata case: its rejection was a discovery defect of ours (bug #161) and it returned on the fix alone. That ratio is high enough to watch — it reflects a one-off backlog created by ADR-0151, not a steady rate, and if new submissions keep needing errata at anything like it, the hatch is doing work the gate should do.
- A board label, `pack:known-missing`, marks a row whose artifact carries a declaration, set by the `apply-verdict` step of `community-pack-validate.yml` from the Pack Hash the pipeline computed, and explicitly not by the classify step — classify's inputs are submitter-controlled, and the entry route exists so a submitter cannot declare an errata over somebody else's pack. The same step removes the label when nothing resolves, so it expires with the errata.
