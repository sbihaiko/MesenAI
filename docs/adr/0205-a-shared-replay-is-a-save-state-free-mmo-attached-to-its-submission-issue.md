# ADR-0205: A shared replay is a `.mmo` recorded by a single Record-and-share action, attached to its submission issue, listed in the client by ROM and ranked by votes

- Status: accepted (2026-09-17, at the user's direction: "aceito, pode marcar
  como accepted e commitar"). Nothing is implemented yet; the work is two
  pending slices — publish (§2–§6: share action, lint, Issue Form, workflow)
  and consume (§7–§9: recordings catalog, client overlay, `replay:removed`) —
  to be added to `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` Part A by
  the first implementing PR, which also amends the PRD's pack-browser non-goal
  (§7). Revised the same day after an external review: §2 covers every product
  console and restores settings, §7 drops the MEI confirmation and defines row
  identity, §8 no longer claims a round-trip the artifact cannot support, §9
  gains a maintainer override.
- Date: 2026-09-17
- Related: ADR-0184 (a recording may use a RAM-only cheat — **this ADR does not
  amend it**, see §4), ADR-0185 (a published movie may drive a recording; its
  non-goal "shipping any movie file in this repository" governs *third-party*
  movies and is not reversed), ADR-0183 (the artist kit a recording produces),
  ADR-0148 (a catalog row is de-listed, not deleted — reused as the removal
  rule), ADR-0146 (community pipeline), ADR-0138 §37/§41 (network boundary,
  allow-listed hosts) and §41's embed rule, ADR-0187 (a new host kind must be
  mirrored in CI and client), ADR-0144 (only the binary patch is
  redistributable), ADR-0044 (hash-gated matching — reused for §7's filter),
  ADR-0141 (one live catalog slot per identity — reused as §7's row rule),
  ADR-0150 (the headless XAML test harness §7's overlay must be testable in),
  ADR-0006 and MEI-v1 §3–§4 (the trust model and the votes contract, which bind
  any MEI client including this one), ADR-0003 / ADR-0039 (No-Intro ROM hash
  contract), ADR-0157 (the movie system was rejected as an *authoring* format —
  not reversed); PRD Part A §1 and its deferred pack-browser non-goal, PRD
  Part B §5, `docs/enhancement-ecosystem.md` §Principles

## Context

A gameplay recording is the most reusable artifact this project produces, and
today it never leaves the machine that made it. The proposal is to publish them
as community submissions — one issue per recording, ranked by 👍 — reusing the
shape the community packs already have (ADR-0146), plus a client surface that
lists the shared replays for the ROM currently loaded.

Three things are called "recording", and only one is shareable:

| artifact | measured size | carries |
|---|---|---|
| replay of inputs (`.mmo`) | ~1 KB/min deflated; a 31.9-minute `.bk2` with the same input log measured 17.8 KB | button state per frame, ROM SHA-1, emulator settings |
| save state (`.mss`) | 4–20 KB, median 15.4 KB over 30,273 files | machine state **plus a full 256×240 16bpp framebuffer** (122,880 B raw, compressed to 1–4 KB) |
| artist kit (`hires.txt`, sheets, `grid.txt`) | ~3 MB per stage; `grid.txt` alone is 39.5 MB per emulated minute | pixels derived from the ROM |

The project has already ruled out the second: `.mss` files are not versioned
because "a CHR RAM state carries the game's graphics" (`scripts/AGENTS.md`). The
third is what `CONTRIBUTING.md`'s "Never commit derivative content — extracted
tiles, ripped samples…" excludes.

**A `.mmo` is not automatically the first row.** `MovieRecorder::Record` embeds a
save state whenever `EmuSettings::HasRandomPowerOnState` is true, and for the NES
that is the *default* (`_nes.RamPowerOnState = RamState::Random`; the companion
flags `RandomizeMapperPowerOnState` and `RandomizeCpuPpuAlignment` are already
`false`). So the decision is taken before the user touches a "Record from" mode:
a NES movie made with stock settings contains a save state whatever the user
picks, and therefore contains a screenshot of the game and — on a CHR RAM title
such as Contra or Zelda 1 — the game's unpacked tiles. The state is
load-bearing (it captures the randomized power-on RAM; strip it and the replay
desyncs), so the archive is all-or-nothing: there is no "publish the clean part"
edit.

The written policy has no shelf for a replay. It is not *clean data* — PRD Part
A §1 limits the official channel to "synth presets, ROM-hash mappings, index
manifests, tools, and original compositions" — and it is not *derivative
content* either, since an input log carries no expressive content of the game.
The policy is binary and this artifact sits between the halves.

Non-goals: shipping any third-party movie in this repository (ADR-0185 stands);
making a published replay a source of truth for anything (it is an input
device); hosting replay bytes in this repository or in any project-controlled
release; any mechanism that stores or asks for a GitHub password; amending
ADR-0184's cheat rule.

## Decision

### 1. The published artifact is a Mesen `.mmo`, and nothing else

`.mmo` is the only format both recordable while playing and replayable by this
emulator. `MovieManager::Play` detects the format by *content* — a zip whose
first two bytes are `PK` and which holds `GameSettings.txt` is a Mesen movie — so
the extension is cosmetic and a rejected `.mmo` may be re-uploaded as `.zip`
without recompression or loss. `.bk2` is playback-only in this Core and there is
no `.fm2` reader (ADR-0185); neither can be recorded.

The format carries its own attribution and its own pairing key, which the
submission flow must not duplicate: `RecordMovieOptions` takes `Author` and
`Description`, `MovieRecorder::Stop` writes them as `MovieInfo.txt`, and
`GameSettings.txt` already holds the ROM file name, the ROM SHA-1, the console
type and the emulated settings.

### 2. One action — Record and share — and the user chooses nothing

The clean artifact is produced **by construction**, not by lint and not by a
setting the user is trusted to change. A single Record-and-share action applies
the only configuration that yields a publishable archive:

- `RecordMovieFrom::StartWithoutSaveData` — power-cycles and ignores battery
  data already on disk, and is the one mode that does not register the recorder
  as a battery recorder;
- `RamPowerOnState = AllZeros` **for the loaded console**, plus every other
  flag `EmuSettings::HasRandomPowerOnState` reads for that console, so that it
  returns `false`, `needSaveState` is `false` and no `SaveState.mss` member is
  written. Every console config defaults `RamPowerOnState` to `Random`, so the
  rule is per console, not NES-only: NES also needs `RandomizeCpuPpuAlignment`
  and `RandomizeMapperPowerOnState` `false`; GB and SMS read only
  `RamPowerOnState`. The action MUST refuse to start for a console the switch in
  `HasRandomPowerOnState` does not know how to make deterministic, rather than
  record an archive §3 will reject.

With both, the archive contains neither `SaveState.mss` nor `Battery*`.

**The player's settings are restored when the recording stops.** The action
snapshots `EmuSettings` before applying the two changes and restores the
snapshot on stop, on error and on ROM unload — the same backup-and-restore
`MesenMovie` already performs around playback (its constructor serializes the
settings, `Stop()` reloads them). Recording without the restore would silently
leave the user on a deterministic power-on state.

Two consequences the user will notice, and both are inherent rather than
cosmetic:

- **Sharing is a run from power-on, not a segment.** `RecordMovieFrom::
  CurrentState` sets `needSaveState = true` unconditionally, because the machine
  is already running and the state is the only way to capture its RAM. Recording
  from wherever the player happens to be produces a *segment*, and a segment is
  not publishable. A replay is a run from the start, which is what "replay"
  means anyway.
- **The recording runs under settings the player did not choose.** That is safe
  because the settings travel inside `GameSettings.txt`, so playback reproduces
  the same conditions, and `MesenMovie::ApplySettings` reloads them on play.

The command-line recorder cannot produce a clean artifact as it stands
(`CommandLineHelper` records with `RecordMovieFrom::StartWithSaveData`, which
always embeds battery data), so the clean path is the share action alone until
that changes.

**A retroactive "share what I just played" is possible but not in this ADR.**
`MovieRecorder::CreateMovie` exports the rewind buffer and embeds a state only
when the export does not start at position 0, the game has battery data, or
`HasRandomPowerOnState` is true. Under this ADR's settings, a rewind export from
position 0 would therefore also be clean — but only if the buffer still reaches
back to power-on, which the rewind size setting bounds in minutes. It is a
candidate follow-up, not part of the decision.

### 3. Verification: the lint re-checks what the action guarantees

The validate pipeline rejects an archive containing `SaveState.mss` or any
`Battery*` member. This is defence in depth over §2, not the primary gate: the
share action should never produce such an archive, and the lint exists because a
submission's bytes are not the action's output — anyone can attach anything.

The archive is also capped in size — **8 MB** compressed, the same order as a
multi-hour text input log and two orders below the pack cap — because
`MesenMovie` inflates every member in memory, and a deflate bomb is the one
attack a 50 KB artifact class invites. The CI download and the client download
both enforce the cap before inflating.

The ROM SHA-1 and the ROM *file name* (name only — `GetFileName()`, never a
path) are expected and required: the project already publishes ROM hashes
deliberately under ADR-0003/ADR-0039. An embedded `PatchData.dat` is permitted:
the binary patch is the redistributable artifact (ADR-0144). Emulator settings do
not leak the user's filesystem — `EmuSettings::Serialize` excludes
`_preferences` by construction.

### 4. Cheats are unrestricted in a published replay; ADR-0184 keeps the driver path

A published replay may carry any cheat. There is no legal reason to refuse one —
a cheat code is a functional fact (an address and a byte), less expressive than
the patch ADR-0144 already permits — and there is a strong practical reason to
allow them: a stage skip is how a user gets past a wall the scripted run cannot
pass, which is the exact problem ADR-0184 and ADR-0185 were written around.
Contra stage 1 dies at a fixed wall and never reaches stage 4; a run that starts
at stage 4 lets the kit record stage 4's art.

Cheats travel and are reproduced: `GetGameSettings` writes every active cheat as
a `Cheat <Type> <Code>` line in `GameSettings.txt`, and `MesenMovie::LoadCheats`
reads them back and calls `SetCheats()`, saving and restoring the player's own
cheats around playback. A cheated replay therefore round-trips, which is what
makes it shareable at all.

**ADR-0184 is not amended.** Its rule governs what may *drive a recording*, and
that boundary is where it matters: on a CHR RAM title a Game Genie code is a
PRG-space substitution by construction (`ConvertFromNesGameGenie` and
`ConvertFromNesProActionRocky` both force the address into `$8000–$FFFF`) whose
byte can travel through the game's own loader into CHR RAM and be hashed by the
HD pack builder as if it were the game's real art. A published replay may drive
a kit recording only when it declares no PRG-space cheat.

This is not a restriction on publishing and it does not restrict stage skips:
`CheatType::NesCustom` (`AAAA:VV`) uses its address **raw**, so a low address is
internal RAM, and that is the RAM cheat ADR-0184 permits. Skipping a stage is a
RAM write to the stage counter, not a PRG patch — the sanctioned form.

### 5. The issue title is `[Replay] <game> — <alias> — <subtitle>`

All three parts are read off the artifact by the validate workflow, which
rewrites the **whole** title — the shape `community-pack-validate.yml` already
uses for `[Pack] <game>`, and for the same reason: a submitter can freely edit
the pre-filled box, so a typed title is not evidence. The rewrite is idempotent,
so `/revalidate` is a no-op once it matches.

- `<game>` is the No-Intro name resolved from the archive's ROM SHA-1
  (ADR-0003/ADR-0039). The pack flow must trust the typed `rom_target`; this one
  need not, and the title cannot disagree with the game the archive replays.
- `<alias>` is `MovieInfo.txt`'s `Author` — the artifact's, never the GitHub
  login that opened the issue, which is the rule the pack pipeline already
  follows.
- `<subtitle>` is the first line of `MovieInfo.txt`'s `Description`, truncated to
  a fixed length. The field is bounded at 10 KB and cannot go in a title whole;
  the untruncated text stays in the issue body. `<alias>` renders as the login
  when the recorder was left unnamed, so a row is never anonymous.

### 6. Delivery: the author attaches the file to the issue, in their own browser

The share action opens a pre-filled issue URL (`/issues/new` with title, body and
label) in the user's own browser; the author drags the `.mmo` in and submits.

No attachment automation. GitHub exposes no public API for attaching a non-image
file — the documented endpoint answers `422` for anything that is not image or
video, and the path that does work is an undocumented three-step flow
authenticated by browser session cookie. A headless browser could drive it and is
rejected: it would require this project to hold a user's GitHub session, it would
automate around a boundary GitHub deliberately left closed, and it would add a
permanent maintenance tax against undocumented endpoints. Asking for the
credential at share time and discarding it is rejected for a sharper reason:
obtaining a session cookie requires the user's password and 2FA code, so the
dialog itself would teach users to type a GitHub password into a third-party
app — and this is GPL software, so a fork inherits that dialog as a working
credential harvester.

A GitHub account is required to submit, and to vote at all. There is no
anonymous path and none is built: an anonymous relay would lose attribution and
create an abuse surface with no owner.

**The pack flow's "paste a link" model is rejected here, and the divergence is
deliberate.** A pack is a distribution artifact an author already publishes and
maintains elsewhere; a replay is a 50 KB file that exists only because the share
action just produced it, and asking its author to create a gist, drag the file
in, copy a raw URL and paste it before they can submit turns one action into
four. The saving would be purely the project's hosting posture, and there is
nothing to save: §2 and §3 already guarantee the archive carries no ROM-derived
content, so the exposure is negligible and the posture is not worth a
four-step submission. `pack_link` stays as it is for packs; this flow takes the
file.

Friction is then reduced as far as the browser allows: the emulator writes the
archive to a known folder and reveals it in the file manager, so the drag into
the issue is one motion from the window that is already open. (Whether the file
can be placed on the clipboard for a paste instead of a drag was not verified,
and is not assumed.)

**Cost this decision carries.** The archive is fetched back by the client (§7),
and `CommunityPackDownloader` sets `AllowAutoRedirect = false` and revalidates
every hop against the allow-list by hand. An issue attachment is served from
`github.com/user-attachments/assets/<id>` and redirects to
`objects.githubusercontent.com`, so **both hosts must enter**
`scripts/pack_host_allowlist.json`, with the redirect hop handled — and per
ADR-0187 a host change must be mirrored in `scripts/fetch_pack.py` (CI) and
`UI/Logic/CommunityPackHostAllowlist` (client), whose kind sets a drift check
already enforces. This is a trust-boundary decision reviewed like the workflow
YAML, not a mechanical edit.

Two things keep the wider allow-list from being the exposure it looks like.
First, the allow-list is defence in depth, not the trust boundary: the client
only ever fetches URLs the validate workflow wrote into the committed catalog,
pinned by sha256, so an attacker needs a row in the catalog — a passed
validation — not merely a file on the host. `raw.githubusercontent.com` and
`gist.githubusercontent.com` are already listed and already let anyone host
anything; attachments add no new class of exposure. Second, **the catalog stores
the stable `github.com/user-attachments/assets/<id>` URL, never the resolved
`objects.githubusercontent.com` one**: the redirect target carries a signed,
expiring query string, and a row pinned to it would go dead within minutes. The
hop is followed at fetch time by the downloader's manual redirect handling.

### 7. The client lists the shared replays for the loaded ROM, ranked by 👍

Inside the emulator, the loaded ROM's shared replays are listed most-👍-first and
one can be picked and played.

**This surface was already deferred, not overlooked.** PRD Part A's non-goals
list "Pack browser UI beyond auto-install (search, ranking by GitHub signals,
user-configurable extra MEI URLs with explicit confirmation…) — after Phase 6,
**if the catalog grows past what a list can show**", and states that the
player-shell picker is not that browser. Shared recordings are the declared
trigger: they are the first catalog whose rows are *alternatives* rather than one
winner. This ADR is the entry into that deferred item; the implementing slice MUST
amend the PRD's non-goal entry to say so, because an ADR does not edit the PRD
by citation.

**It is still a new capability, not a reuse.** The existing picker
(`PlayerPackPicker`, `MainWindowViewModel.BuildPackPickerData`) lists packs
already on disk via `GetMepPackList()`, and `CommunityPackCatalogFetcher` returns
**exactly one** entry — the first match — and auto-installs it. There is no
`FindAllMatchingEntries` anywhere in the client, and the ordering by votes that
already exists (`OrderByDescending(Votes).ThenBy(Name)`) is nearly decorative
today precisely because only one pack per ROM is ever installed.

Reused unchanged: `CommunityPackDownloader` (allow-list, caps, no auto-redirect),
the sha256 verification and the hash-keyed `downloads/` cache, the
`CommunityPackHostAllowlist` matcher, `CommunityCatalogCacheDecision`, the
overlay XAML pattern from `MainWindow.axaml`, and the ordering expression. New:
a matcher that returns a list, a catalog read that yields candidates without
downloading, and an overlay fed by catalog DTOs rather than by the local pack
list.

**The catalog is a second index with no plumbing to inherit.** The packs URL is a
private constant with no configuration indirection, and no federation or
multi-index mechanism is specified or implemented — the PRD's citation of "MEI
§3.4" for user-configurable index URLs names a section that does not exist; the
clause it means is MEI §3 item 4. A recordings catalog therefore needs its own
fetch path. **No confirmation dialog
applies.** MEI §3 item 4 requires explicit confirmation for a manifest "that is
not the host's default index" — a *third-party* index the user adds. A catalog
this project publishes at its own host is a default index of that host, exactly
like the packs catalog, so the clause does not fire; a dialog for the project's
own list would be the intrusiveness the packs client was built to avoid
(ADR-0146). MEI §3 items 1–3 bind unchanged — sha256 verified before the
artifact is used, HTTPS for the index and the artifact, zip-slip rejected after
path normalization. (The zip-slip clause matters less here than for a pack: the
`.mmo` is a zip, but `MesenMovie` reads its members in memory and writes nothing
to disk, so there is no extraction to traverse out of.)

**One live row per recording identity, and identity is defined.** The row key
is the **submission issue number**; the dedupe key is the **sha256 of the
attached archive**. A second open issue whose archive hashes identical to a live
row is `invalid` with a comment naming the earlier issue, so a re-submission of
the same recording collapses to one row rather than adding a second — ADR-0141's
slot rule is the model. ADR-0141 rejected "list every accepted revision as its own row and let
the player choose", but that rejection is about *revisions of one product*;
distinct recordings for one ROM are distinct identities, and choosing between
them is the point. The distinction must be preserved, not flattened.

**The row carries the cheats.** The validate workflow parses every `Cheat
<Type> <Code>` line of `GameSettings.txt` into an additive `cheats[]` field on
the row (type and code, verbatim). The client shows a badge on a row whose list
is non-empty before the user picks it, and ADR-0185's driver path decides
admissibility under ADR-0184 §1 from the row, before downloading anything.

**Filtering is exact, and on the pre-patch ROM.** The No-Intro SHA-1 and its
additive `sha1s[]` only; the optimistic title fallback (`SameGame`) is
deliberately **not** used. A texture pack matched by title degrades gracefully —
the wrong tile falls back to the original — but a replay matched to the wrong ROM
desyncs immediately, which puts it with the hash-gated patch case (ADR-0044)
rather than the optimistic texture case (ADR-0145). The key is the ROM **as
loaded, before any `patches[]` applies**, which is stricter than the texture case
and follows PRD Part B §5's ordering.

Layering is unchanged: `Core/` gains no HTTP client
(`scripts/checks/verify_core_no_http_client.sh` enforces the absence of the
constructs), `UI/Logic/` stays host-free and dual-compiled into `UI.Tests`, and
`UI/Services/` remains the single network boundary. The overlay must be testable
in ADR-0150's headless XAML harness, which looks controls up by name
(`FindNamed<…>`), so naming them in the XAML is a prerequisite rather than a
nicety.

### 8. Votes rank; a structural gate validates; nothing is removed by votes

The validity gate is structural and machine-decidable, and it is not a
round-trip. A save-state-free `.mmo` carries **no claimed end state** — no final
hash, no frame digest — so there is nothing a replay could be compared against,
and a run whose player dies in the first second and mashes the title screen for
an hour replays "correctly" to the end of `Input.txt`. The gate is therefore:
the archive passes §3, `GameSettings.txt` parses and names a console the
pipeline supports, `Input.txt` is non-empty and frame-aligned, and the ROM SHA-1
is well-formed. That decides "is this a Mesen movie of this ROM"; it does not
decide "is this a good run", and nothing in this ADR does. Votes are the only
signal about quality, and they rank.

A headless replay in CI is **deferred**, with the conditions it would need
written down: the share action would have to write a final-state digest into
`MovieInfo.txt` for the CI run to compare against; the run would be bounded by a
frame cap (**1,000,000 frames**, about 4.6 hours at 60 fps) refused above rather
than truncated, and by the job's `timeout-minutes`; and the same submitter
opening several issues in a row would be rate-limited by the pipeline. Without
the digest the replay proves nothing the structural gate does not already
prove, so it is not run.

👍 orders the catalog and the client list, and nothing else, reusing the existing
mechanism (one 👍 seeded per submission, idempotent so a re-validation never
double-counts). The client never sends a vote, and this is a normative
constraint rather than an observation: MEI §4 requires that clients send the
index nothing beyond the GET itself, and the client has no GitHub access at all,
so `votes` is a read-only additive field written by the catalog generator from
the submission issue's 👍 count. Ordering by votes is the client's whole
involvement; voting happens in the GitHub pipeline. There is no downvote and no
removal by unpopularity: removal by community sentiment would make the project an
editorial decision-maker, which is the posture PRD Part A §1 exists to avoid, and
it would punish the author rather than the artifact.

### 9. Removal is the author closing the issue

Closing the submission issue removes the catalog row; the validate/drift workflow
reacts to `issues: closed`. The issue itself stays as closed history, which is
what ADR-0148 already decides for a de-listed row.

Deletion is **not** the mechanism, and the reason is a permission, not a taste:
only accounts with admin permission on the repository can delete an issue, so a
contributor cannot delete even the issue they opened; the repository owner can,
but an author-triggerable removal cannot depend on that.

Reopening restores the row. The workflow must therefore treat a close as a
de-list and a reopen as a re-list, and it must not treat "closed" as evidence
that the archive is bad — nothing about the artifact changed.

**A maintainer override exists, and it outranks the issue state.** An issue's
author can reopen it at will, so a close by a maintainer would be undone by one
click. The label `replay:removed`, settable only by collaborators, keeps the row
de-listed whatever the issue state, and the catalog generator honours it before
it looks at `state`. It is the counterpart of `pack:known-missing` (ADR-0152):
applied by the pipeline's owners from a decision, never from submitter-controlled
input. It is not a quality judgement — §8's posture stands — but a lever for
abuse, a broken artifact the author will not fix, or a takedown.

### 10. The project's git tree carries no replay bytes — and the attachment is not "someone else's host"

PRD Part A §1's "referenced, never hosted" is satisfied in the sense that
matters for the repository: no replay is committed, `git clone` never fetches
one, none counts against the repository size, and the catalog holds only a URL
and a hash. The project does not operate the server that serves the bytes.

**But this is a weaker posture than the pack flow's, and the difference must be
stated rather than blurred.** An issue attachment is not stored under the
submitter's account: the upload is scoped to the repository (`repository_id`),
so the artifact lives in GitHub's asset store under *this* repository's scope and
disappears if the repository does. For a pack, the project points at an artifact
the author maintains on the author's own host; here the project's repository is
the container and the committed catalog is a curated, ranked index of what it
contains.

That is accepted, deliberately, as the price of §6's one-drag submission — and it
is affordable only because §2 and §3 make the artifact carry nothing of the game.
There is no ROM-derived content in a published replay for the project to be
distributing, so the residual is a matter of posture rather than exposure. If a
later slice ever admits an artifact that does carry ROM-derived content, this
section stops being true and the delivery mechanism must be revisited with it.

## Consequences

- **§2 is the whole safety margin.** The clean archive is guaranteed by the
  action, and §3 only re-checks it. If a future slice lets a user record with
  stock settings and attach the result, the artifact class changes and this ADR
  no longer describes what ships.
- **A new host enters the trust boundary.** §6's attachment path requires
  `github.com/user-attachments` plus the redirect target in the allow-list, both
  sides of the CI/client mirror kept in step. This is a trust-boundary decision
  reviewed like the workflow YAML, not a mechanical edit.
- **The client gains its first "list many" surface.** Everything downstream of
  the picker assumes one match per ROM; §7 breaks that assumption in one place
  rather than spreading it, but the assumption is load-bearing elsewhere and must
  not be relaxed by accident.
- **§7 discharges a PRD non-goal rather than adding to it.** The deferred
  pack-browser item was gated on the catalog growing past what a list can show;
  recordings meet that condition, so the PRD's non-goals list needs its entry
  updated in the same slice that implements this. Leaving the old text in place
  would describe the project as not doing what it now does.
- **The gate is weaker than a pack's, and says so.** A pack lint checks that
  referenced files resolve; §8 checks only that the archive is a well-formed
  movie of the named ROM. A three-second troll run passes. That is accepted:
  the alternative is a CI replay with no oracle, and votes are the quality
  signal by design.
- **Two caps are new invariants.** The 8 MB archive cap (§3) and the
  1,000,000-frame cap (§8, only if the deferred replay ever lands) must live in
  one place each and be mirrored where CI and client both read them, like the
  host allow-list.
- **A stage-skip replay is the highest-value artifact this feature produces and
  the most dangerous to feed back.** §4's driver guard is the only thing keeping
  it from poisoning the art archive through ADR-0185's driver path; it must be
  enforced where a replay is *consumed*, not where it is published.
- **Cheat codes are published in plain text.** `GameSettings.txt` writes every
  active cheat, so an ADR-0184 RAM cheat is now visible to every reader of the
  artifact. A cheat is a functional fact rather than expressive content, so this
  is not a §3 violation — but a submitter should not be surprised by it.
- **Closing by accident de-lists; reopening by an author re-lists.** §9 makes
  the issue's state the switch, so a stray close removes the row until it is
  reopened — and `replay:removed` is the only close an author cannot undo.
- **The share action mutates settings and owns their restore.** §2's snapshot
  must survive every exit path (stop, error, ROM unload, app quit mid-record); a
  missed path leaves the player recording deterministically for good. A test
  that starts the action, unloads the ROM and asserts the original
  `RamPowerOnState` is back is the cheap guard.
- **No separate takedown process is added.** The bytes are GitHub's to serve and
  GitHub's to remove, so a notice lands with GitHub as host of record; the
  project's part is the catalog row, and §9's close rule removes it. Two removal
  paths exist and both are already covered: closing the issue de-lists
  deliberately, and an author deleting their own attachment — which they *can*
  do, unlike deleting an issue — leaves a pinned hash that no longer matches,
  which is exactly ADR-0148's stale-row case. §10 is the section to revisit if
  the project ever serves bytes itself.
- **Deferred, not foreclosed:** publishing without a browser via OAuth device
  flow plus the Git Blobs API (which accepts base64, unlike the gist create
  endpoint). It is a heavier author-side authorization and needs an OAuth app
  registered by the project; supersede this ADR if the drag-and-drop ever proves
  to be the binding constraint.
