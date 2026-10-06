# ADR-0187: Dropbox and MEGA are allow-listed pack hosts, each with its own fetch kind

- Status: accepted (2026-09-14, at the user's direction — the option picked
  was "ADR + Dropbox e Mega" over a Dropbox-only variant, after the
  Dropbox/MEGA split of the romhacking.net NES pack catalogue was measured).
  Reflected in `scripts/pack_host_allowlist.json`, `scripts/fetch_pack.py`
  and `UI/Services/CommunityPackDownloader.cs`.
- Date: 2026-09-14
- Related: ADR-0138 §41 (allow-list packaging and the per-hop validation the
  hosts get), ADR-0006 (MEI trust model — HTTPS-only, checksum-verified),
  ADR-0148 (de-listing rules for a link whose bytes change), ADR-0146
  (every accepted pack auto-installs, so the client must be able to fetch
  from every host the CI accepts)
- Supersedes / amends: ADR-0138 §41 — the allow-list is no longer
  "GitHub, Google Drive, MediaFire"; it is those three plus Dropbox and
  MEGA, and `kind` now has five values instead of three.

## Context

The NES sprite-replacement packs catalogued by the Emulation General Wiki
are published almost entirely through romhacking.net forum threads, which
overwhelmingly host their zips on Dropbox and MEGA. Of the thirteen listed
on 2026-09-14, six had no issue in this repo; where their bytes live:

| Pack | Host |
|---|---|
| Ninja Gaiden II HD Graphics Pack | Google Drive (already allow-listed) |
| Bomberman Remastered | Dropbox |
| Donkey Kong Jr. Remastered | Dropbox |
| Ice Climber Remastered | Dropbox |
| Little Nemo Graphic Pack | Dropbox |
| Twin Bee Remastered | Dropbox |
| Shatterhand HD Pack (WIP) | MEGA |

The existing allow-list reaches one pack of six. ADR-0138 §41 anticipated
this shape — written because the LiQuiDzGit audio assets sat on "Google
Drive/MEGA (hosts outside the CI allow-list)" — but resolved it by recording
external dependencies in the recipe rather than widening the list. That does
not help: these are whole packs, not side assets, and a pack whose only link
is unreachable cannot be validated, catalogued or auto-installed.

The non-goal is a general "download from anywhere" escape hatch. Adding a
host stays a trust-boundary decision (§41); the two here are major
multi-tenant platforms carrying a measured, non-substitutable share of the
corpus.

ADR-0146 is the second constraint: every accepted pack auto-installs in the
client. A host the CI can fetch but `CommunityPackDownloader` cannot would
produce packs that validate, enter `docs/community-packs.json`, and then
silently fail to install. Both fetch kinds therefore ship on both sides or
neither.

## Decision

### 1. Two new hosts, two new `kind` values

`scripts/pack_host_allowlist.json` gains:

```json
{"host": "www.dropbox.com", "path_contains_any": ["/s/", "/scl/fi/"], "kind": "dropbox"},
{"host": "dropbox.com",     "path_contains_any": ["/s/", "/scl/fi/"], "kind": "dropbox"},
{"host_ends_with": ".dropboxusercontent.com", "kind": "direct"},
{"host": "mega.nz", "path_contains_any": ["/file/"], "kind": "mega"},
{"host": "g.api.mega.co.nz", "kind": "direct"},
{"host_ends_with": ".userstorage.mega.co.nz", "kind": "direct"}
```

The CDN/API hosts are `direct` so the redirect and second-request hops
re-validate through the same `open_validated` path as any other hop; they
are not submitter entry points, but pasting one is harmless.

### 2. `kind: "dropbox"` — force the download flag, then follow

A share URL renders an HTML preview page; the bytes come from the same URL
with `dl=1`. The fetch rewrites the query so `dl=1` replaces any `dl` value
present (appended when absent), leaving every other parameter — notably
`rlkey` — untouched, then follows the redirect chain
(`www.dropbox.com/s/…` → `www.dropbox.com/scl/fi/…` → `*.dl.dropboxusercontent.com`,
three hops, within the existing five-hop cap).

A `text/html` content type on the final response is a failure, not a pack —
a login wall or a dead link — which keeps a deleted Dropbox link from being
linted as a corrupt zip.

### 3. `kind: "mega"` — the key never leaves the URL fragment

A MEGA file URL is `https://mega.nz/file/<handle>#<key>`, where `<key>` is
32 bytes, base64url, **in the fragment** — a browser never transmits it,
and neither do we. The fetch:

1. splits `<handle>` and `<key>` locally;
2. derives the AES key as the first four 32-bit big-endian words XORed with
   the last four, and the CTR nonce as words 4–5 followed by eight zero
   bytes;
3. `POST`s `[{"a": "g", "g": 1, "ssl": 1, "p": "<handle>"}]` to
   `https://g.api.mega.co.nz/cs?id=0`. `ssl: 1` is mandatory: without it the
   API hands back an `http://` download URL, which `validate_url_shape`
   rejects — correctly, and the flag keeps that rejection from being the
   normal case;
4. streams the response body from the returned `*.userstorage.mega.co.nz`
   URL, decrypting AES-CTR as it goes, enforcing the 300MB cap on both the
   size the API declares and the bytes written.

`open_validated` gains an optional request body so step 3 can be a POST
through the same allow-list/DNS checks as every GET; nothing else changes.

**The key is never logged.** Any message naming a MEGA URL prints
`https://mega.nz/file/<handle>` with the fragment stripped — why the MEGA
branch formats its own error strings instead of letting the URL reach the
generic `download failed: {e}` line.

### 4. The client mirrors both

`UI/Services/CommunityPackDownloader.cs` gains `GetDropboxAsync` and
`GetMegaAsync` alongside the Drive and MediaFire branches, the same shape
(`Kind` dispatch, per-hop allow-list, byte cap, never throws). MEGA's
AES-CTR runs on `System.Security.Cryptography.Aes` in ECB mode over a
manually incremented counter block — .NET has no CTR mode — the standard
construction, AOT-compatible.

### 5. `cryptography` is a declared CI dependency

The download step installs `cryptography` before running `fetch_pack.py`.
The MEGA branch imports it lazily, so a pack from any other host still
downloads where the install failed.

### 6. The rejection message stops assuming `host`

`community-pack-validate.yml`'s "Reject disallowed host" step built its
accepted-host list with `h['host']`, raising `KeyError` on any entry keyed
by `host_ends_with` alone — already true of `.mediafire.com` before this
ADR, now of three more. It uses `h.get('host') or h.get('host_ends_with')`
instead.

## Consequences

- **The trust boundary is wider by two platforms.** Both are anonymous-upload
  capable, so a submitted URL is attacker-chosen content from an
  attacker-chosen account. What stands between that and the repo is
  unchanged deliberately: the 300MB cap, `mep_lint` on the bytes, the
  recorded `sha256`, and ADR-0148's de-listing rule when those bytes change.
  Nothing about the added hosts is trusted beyond "resolves publicly and
  speaks HTTPS".
- **MEGA is the first host we decrypt rather than download.** A bug in the
  key derivation produces plausible garbage, not an error — a zip that fails
  to open, reading like a corrupt upload. The tests pin the derivation
  against a known vector so a regression is a test failure, not a mystery
  `pack:invalid`.
- **MEGA download URLs expire.** The `g` URL is short-lived, so the API call
  is part of every fetch and cannot be cached. A MEGA entry's `url` in
  `docs/community-packs.json` is always the `mega.nz/file/…` form — so the
  catalog carries decryption keys in its fragments, which is what a MEGA
  link *is* and the same exposure as the issue body, but worth knowing
  before treating the catalog as non-sensitive.
- **Two more mirrors to keep in step.** `fetch_pack.py` and
  `CommunityPackDownloader.cs` were a two-way mirror for three kinds; now
  for five. Drift shows up as a pack that validates in CI and fails to
  auto-install, which no existing check catches.
