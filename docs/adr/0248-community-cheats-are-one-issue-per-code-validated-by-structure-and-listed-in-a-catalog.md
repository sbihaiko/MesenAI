# ADR-0248: Community cheats are one issue per code, validated by structure and listed in a catalog by game

- Status: accepted (2026-10-02). Requested by the user, verbatim: *"que tal termos uma issue por jogo com a lista de cheatcodes por jogo? assim teria como compartilhar pela GUI, testar no github actions e adicionar na issue de cheats do jogo para compartilhar"*, then *"sim, escreva a ADR"*; accepted the same day (*"Aceitar"*). The ADR keeps the goal (share from the GUI, check in Actions, one list per game) and changes the shape: one issue per submission, not one per game (Context). Listed as slices **R.3** (publish) and **R.4** (consume) in PRD Part A §4, Phase 13. **R.3 implemented 2026-10-02** on the user's go-ahead, verbatim: *"sim, pode seguir. depois que tudo estiver no main, pode implementar usando paralelismo de tudo que puder"* and *"pode implementar em paralelo tudo que puder"* — §1 (form `cheat-code.yml`, title rewrite), §3 (`cheat-submitted.yml`, `scripts/cheat_submission.py`, `scripts/cheat_decoder.py`; parity 9 829/9 829 bundled entries against the unmodified `CheatManager.cpp`, `scripts/test_cheat_decoder_parity.py`) and §7 (21 labels); the workflow has not yet run on a real issue. Implementation choices the ADR left open: the form asks no cheat type, so the type is read off the code as the bundled list is read (`:` NES custom, `-` GB/SMS Game Genie, an eight-letter NES code is Game Genie before Pro Action Rocky); "normalised code" is the set of *decoded* parts (CPU, address, value, compare, RAM flags), so two spellings of one patch are one duplicate; "No-Intro data" is what the repository holds (`scripts/rom_target.py`, `docs/community-packs.json`), which has no GB/SMS entry yet, so a GB/SMS submission fails `unknown-game` until such data is added; the workflow also applies the console label. R.4 (catalog, client) not implemented. R.4 also needs W-P11 built (P.10, ADR-0245, accepted the same day), because this ADR has no screen of its own.
- Date: 2026-10-02
- Related: ADR-0245 (Play's cheats sheet W-P11; Remaster allows RAM codes only), ADR-0205 (shared replays: issue form, structural gate, 👍 ranking, removal by closing, catalog read by the client; §7, §8, §9), ADR-0184 (no PRG patch while recording art), ADR-0146 (no consent dialog for the project's own index), ADR-0188 (a judgement is a proposal, never evidence), ADR-0141 (one live row per identity), ADR-0150 (headless XAML tests), PRD Part A §1 (principles 1–3), PRD Part B §13.4 (Share holds no GitHub credential), MEI-v1 §3–§4
- Supersedes / amends: amends ADR-0245 by adding a third source to W-P11, after the bundled list and the user's own codes. ADR-0205 is unchanged; this ADR reuses its pattern for a separate flow.

## Context

W-P11 (ADR-0245) shows the bundled `CheatDb.Nes.json` for the loaded game.
That list has 774 NES games and 9 829 codes, and the user's own codes are
added with *Add a Code…*. A code someone found has no way to reach
other players, and GB/SMS have no list at all.

The user asked for one issue per game holding its cheat list, shared from
the GUI and tested in GitHub Actions. Three facts change that shape:

1. **A comment cannot be pre-filled.** The GUI reaches GitHub only through
   a URL that opens a new issue with its fields filled
   (`ReplayShare.BuildIssueUrl`, the pack form). GitHub has no such URL for
   a comment on an existing issue. Posting one needs a GitHub credential,
   which Share must not hold (PRD Part B §13.4). A per-game issue would
   have to be edited by hand in the browser.
2. **Actions has no ROM.** The project hosts no game files (Part A §1
   principle 1), so CI cannot run a code and watch its effect. The replay
   flow faced the same limit: its gate is structural, and a headless
   replay was deferred (ADR-0205 §8). What CI can decide is whether a code
   is well formed for its console, names a known game, and is not a
   duplicate.
3. **Votes and removal need one row per thing.** ADR-0205 ranks by the 👍
   on the submission issue and removes a row when its author closes the
   issue (§8, §9). One issue holding forty codes can rank none of them,
   and closing it would remove everyone's work.

Codes are short functional data — an address, a value, an optional
compare — the kind of clean data the official channel may carry (Part A §1
principle 2). The free-text description is the only part that is
authored, and it is bounded below.

Non-goals:

- No functional test of a code in CI, now or under this ADR. A later ADR
  may add one only with a runner that can hold the user's ROM, which the
  project does not have.
- No code generated or found by a model in this flow. ADR-0245 Decision 4
  stays separate.
- No editorial removal. Votes rank, and closing removes (ADR-0205 §8–§9).
- No new GitHub Project board. Labels carry the state, as for replays.

## Decision

1. **One submission is one issue,** from a new form
   `.github/ISSUE_TEMPLATE/cheat-code.yml` with the label `cheat`.
   - Fields:
     - *Game*: the SHA-1 the cheat list keys on
       (`EmuApi.GetRomHash(HashType.Sha1Cheat)`) and the game name;
     - *Console*;
     - *Code*: one effect, written as the bundled list writes it — a single
       code, or several joined with `+` when the effect needs them;
     - *Description*: one line, at most 80 characters, with no links.
   - The title is rewritten by the workflow to
     `[Cheat] <game> — <description>`, idempotently, as in ADR-0205 §5.
2. **Shared from W-P11.** A code the user added has a *Share This Cheat ↗*
   row action, which opens the form pre-filled through the same
   `BuildIssueUrl` pattern. The click and the ↗ glyph are the confirmation
   (rule 7). Bundled and community codes have no share action: they are
   already shared.
3. **The gate is structural** (`.github/workflows/cheat-submitted.yml`,
   reusing the replay workflow's shape, with `/revalidate`). A submission
   is `cheat:valid` when all of these hold:
   - the code decodes for the console's cheat types (`CheatType`: NES Game
     Genie, Pro Action Rocky, custom `XXXX:YY[:ZZ]`; GB Game Genie and
     GameShark; SMS Pro Action Replay and Game Genie);
   - the SHA-1 is well formed, and names a game the No-Intro data or the
     bundled list knows;
   - the description passes the bounds above;
   - it duplicates nothing: no live row, and no bundled entry, has the same
     SHA-1 and the same normalised code.

   Otherwise it is `cheat:invalid`, with a comment that names the failed
   check. A duplicate names the earlier issue or "already in the bundled
   list".
   - The decoder runs in Python in CI. Its parity with the Core is a test:
     every one of the 9 829 bundled codes must decode to the same address,
     value and compare as `CheatManager` does. A mismatch fails the gate's
     own tests, not a submission.
   - The workflow seeds one 👍 and never applies a verdict from anything
     the submitter wrote beyond the four fields.
4. **The list per game is a catalog, not an issue.**
   `scripts/generate_community_cheat_catalog.py` writes
   `docs/community-cheats.json` from the live `cheat:valid` issues:
   - grouped by SHA-1;
   - one row per issue: issue number, console, code, description, 👍;
   - ordered most-👍-first.

   This is the "one list per game" the user asked for. A reader can see
   it on GitHub, and the client reads it.
5. **The client shows community rows in W-P11, below the bundled list.**
   - Each row is marked "from the community · 👍 12", and a tap on the count
     opens the issue to vote (↗).
   - Matching is exact on the SHA-1, never by title, because a code
     applied to the wrong copy can crash the game (as for replays,
     ADR-0205 §7).
   - The catalog is fetched through `UI/Services/` like the packs and
     replay catalogs, with the same cache rule. As the project's own
     default index, it needs no confirmation (ADR-0146, MEI §3 item 4).
     The client sends nothing but the GET (MEI §4).
   - ADR-0245 Decision 3 applies unchanged: in Remaster, a Game Genie row
     is disabled with its reason.
   - GB/SMS gain a list this way: rows appear wherever the catalog has
     them, and the "no list yet" line shows only when there are none.
6. **Removal is the author closing the issue.** The next catalog run drops
   the row (ADR-0205 §9). A maintainer may close a submission that breaks
   the description bounds the gate missed. Votes never remove anything.
7. **Labels.** `cheat`, `cheat:valid`, `cheat:invalid`, plus the existing
   `console:nes`/`gb`/`gbc`/`sms`. `scripts/ensure_community_pack_labels.sh`
   grows from 18 to 21 labels, and CLAUDE.md's label list is updated in the
   same slice.

## Consequences

- A code that decodes and names the right game can still do nothing, or
  the wrong thing. The gate cannot tell, and says so: the issue comment
  reads "checked for form, not for effect", and the row's 👍 is the only
  signal of quality. This is the same posture ADR-0205 §8 took for replays.
- A third catalog and a third workflow to keep alive, next to packs and
  replays. The pipelines stay separate (labels, scripts, catalog files).
- The Python decoder duplicates logic that lives in the Core. The parity
  test over the bundled codes is what keeps the two from drifting.
- `docs/community-cheats.json` is committed by the catalog job, like the
  pack catalog. It holds codes and one-line descriptions only, never ROM
  bytes.
- The description bound (80 characters, no links) is the one editorial
  rule, and it is enforced by the gate rather than by a person.
