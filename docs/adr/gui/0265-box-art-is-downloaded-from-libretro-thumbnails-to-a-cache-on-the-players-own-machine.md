# ADR-0265: Box art is downloaded from libretro-thumbnails to a cache on the player's own machine

- Status: accepted 2026-10-07 — the owner asked for the feature and for its
  publication, verbatim: **"sim, inclui e publica"** (2026-10-07). It ships with
  the tests that pin it, `UI.Tests/BoxArt/BoxArtCacheTests`,
  `UI.Tests/BoxArt/BoxArtUrlTests` and `UI.Tests/BoxArt/BoxArtImageTests`
  (`dotnet test UI.Tests/UI.Tests.csproj --filter "FullyQualifiedName~BoxArt"`),
  and with the implementation this ADR describes: `UI/Logic/BoxArtCache.cs`,
  `BoxArtCacheStore.cs`, `BoxArtUrl.cs`, `BoxArtImage.cs`, `BoxArtConsole.cs`,
  `BoxArtCover.cs`, `BoxArtTransport.cs` and `BoxArtCacheOptions.cs`. #1039 is
  the first, UI-independent half of the slice; the row, the tile and the switch
  in Settings › System are the integration half, which is a separate change.
- Date: 2026-10-07
- Related: ADR-0138 §41 (the host allow-list `raw.githubusercontent.com` is
  already on — this ADR is the record of the second, **outbound** purpose that
  entry now serves), ADR-0138 §53 (the three-layer rule that keeps this service
  host-free and its HTTP adapter in `UI/Services/`), ADR-0003 (the ROM hash
  contract the key comes from), ADR-0146 (the master-switch precedent:
  `AutoInstallCommunityPacks`, default on), issues #1030 (the parent spec) and
  #1039. **Named by issue, not by id:** the decision this implements — the
  library ADR's box-art clause, on the `#1031` branch (PR #1040) — and the
  `#1038` SHA1 → No-Intro table's own record are not in this branch's tree yet,
  and `scripts/checks/verify_adr_refs.py` fails on a cited id that has no file.
  Their ids are added here when those two land on `main`.
- Supersedes / amends: nothing. The host was already allow-listed for community
  packs (ADR-0138 §41); no host is added, and no clause of that decision moves.

## Context

The flat game library shows one tile per ROM, and a tile wants a cover. The app
has none: it knows what a ROM *is* — its SHA-1 under ADR-0003's byte-range
contract, and, through the `#1038` table, the console and the database's own name
for it — and nothing about what it looks like. The player sees a file name.

The obvious source is the **libretro-thumbnails** collection: one repository per
system, art named after the No-Intro database's own spelling of each game, split
into the collections `Named_Boxarts` (the cover) and `Named_Titles` (the title
screen). It is a public GitHub repository set, so it is reached through
`raw.githubusercontent.com` — a host this project already trusts for one
purpose, downloading community packs (ADR-0138 §41).

Four things had to be decided rather than assumed:

1. **The URL rule.** The collection's file names are the No-Intro names with the
   characters a file name cannot carry replaced; getting this wrong is a silent
   404 per game, which looks exactly like "this game has no art".
2. **What the second outbound use means.** Allow-listing a host for community
   packs is a decision about *packs*; using it so the app can tell a server the
   SHA-1 of every ROM in someone's collection is a different use of it, and it
   is the player's to refuse.
3. **What happens when there is no answer.** Offline, a game the collection does
   not have, a 404, a server that answers with a page of HTML: none of these may
   delay a library, and none may be paid for twice.
4. **Where the seam lives.** ADR-0138 §53 keeps `UI/Logic` free of
   `System.Net.Http`/`HttpClient` (enforced by
   `scripts/verify-ui-logic-firewall.sh`, run by `make doc-checks`), so a
   network client cannot simply be constructed in the service that decides
   whether to fetch.

## Decision

**Box art is fetched once per game, from libretro-thumbnails through the
allow-listed raw host, and lives only in the player's own cache.**

1. **Source, in this order, and the URL rule.** `Named_Boxarts` first, then
   `Named_Titles`. The URL is
   `https://raw.githubusercontent.com/<system repository>/master/<collection>/<name>.png`,
   where `<name>` is the No-Intro name with each of the eleven characters
   `` & * / : ` < > ? \ | " `` replaced by `_` and the result percent-encoded for
   the URL path. Nothing else is touched: spaces, brackets, apostrophes, commas
   and case are part of the database's spelling and are kept
   (`UI/Logic/BoxArtUrl.cs`; verified against the collection itself on
   2026-10-07, both collections and all seven systems). The seven system
   repositories are one table, `BoxArtSystems.RepoFolder`.

2. **No new host, and a stated purpose.** The host is `raw.githubusercontent.com`,
   already on `scripts/pack_host_allowlist.json` (ADR-0138 §41). This ADR adds no
   host and changes no entry; it records that the entry now carries a second
   use — reading a public art collection at the player's request — so that the
   next reader of that list does not have to guess why the app talks to it.

3. **The key is the ROM's identity, never its file name.** The caller resolves
   the SHA-1 through the `#1038` table (`UI/Logic/NoIntroNameTable.cs`) and
   passes the name it returns. **A ROM the table does not know gets no request
   at all**: with no name there is nothing to ask for, and the tile falls
   straight to its generic cover rather than guessing a name from a file name.
   The SHA-1 must be the table's own 40 hex characters — a value that is not hex
   cannot be a key, and it is also a file name in the cache.

4. **Lazy, bounded, and over HTTPS only.** Fetching is driven by the caller for
   the tiles that are actually visible, one call per tile; the service never
   enumerates a library on its own. At most **4** requests are in flight at once
   (`MaxConcurrentRequests`), each bounded by a **10 s** timeout
   (`RequestTimeout`), and the scheme is HTTPS by construction.

5. **A body is cached only after it is known to be a picture.** A response is
   accepted only with status 200, at most **4 MiB** (`MaxImageBytes`, rejected
   before anything is written) and a body whose own first bytes are a **PNG or
   JPEG signature** — the header and the extension are what the same server
   would have to be trusted about, so neither is read. GIF, WebP, HTML and
   everything else are refused.

6. **The cache is the player's, keyed by console and SHA-1.** It lives in the
   app-support directory the caller resolves (the service takes the directory as
   a constructor argument and never guesses one), laid out as
   `<cache>/<console tag>/<sha1>.boxart.png`, `<sha1>.title.jpg` or
   `<sha1>.miss`. The key is the console **plus the ROM's SHA-1**, so a renamed
   or moved ROM keeps its cover and a Game Boy game never answers for a Game
   Gear one. A cover already on disk is returned by reading the disk alone: the
   kind (box art or title screen) is in the file name, so no sidecar and no
   decoder are needed to serve a hit.

7. **A miss is remembered, and the memory expires.** A 404, a transport failure
   (offline, DNS, TLS), a timeout, an oversized body, a body that is not an
   image, a cache directory that cannot be written: each records a timestamped
   negative entry, so a game the collection does not have costs one pair of
   requests rather than one per visit. The entry expires after **30 days**
   (`MissExpiry`), so a game added to the collection later is picked up without
   anyone clearing a cache by hand. A call the caller itself cancelled records
   nothing: the sheet closing is not evidence about the game.

8. **One master switch, default on, and off means no request at all.** The
   preference `PreferencesConfig.DownloadBoxArt` (Settings › System, default
   `true`, the same shape and the same reasoning as `AutoInstallCommunityPacks`,
   ADR-0146) is what a player who does not want the app telling a server which
   games they own turns off. With it off the service makes **no request** — not
   a lighter one, none — and a cover already in the cache is still served, since
   reading the player's own disk is not a request. The visible row, its label
   and its localization are the integration half, not this one.

9. **Offline is not a wait.** Every failure path returns `null` in bounded time
   and never throws: a tile whose art is unavailable falls back to its generic
   cover, and the library opens and plays as fast offline as online.

10. **Nothing is redistributed.** No image from libretro-thumbnails is committed
    to this repository or bundled in a release; the bytes exist only in the
    player's cache, on the player's own machine. The repository holds URLs, a
    name rule and a cache layout — no artwork.

11. **The seam is a host-free delegate, and the HTTP client is not in
    `UI/Logic`.** `BoxArtCache` takes a `BoxArtHttpSender` delegate (URL in, a
    status and a body out, throwing on a transport failure) and a cache
    directory. This is what ADR-0138 §53 requires rather than a style choice: a
    file under `UI/Logic/` that named `System.Net.Http` would fail
    `scripts/verify-ui-logic-firewall.sh`. The shipped `HttpClient` adapter —
    redirects, the stream cap, the hardened handler — belongs in `UI/Services/*.cs`
    and is the integration half's to write; the unit tests drive the whole
    service with a fake and touch no network.

## Consequences

- A library of ROMs the `#1038` table knows gets real covers for the cost of one
  request per game, once, and the second visit to every tile is a disk read.
  A collection nobody has art for costs one pair of requests per game, then
  silence for thirty days.
- The naming rule is a **live external contract**: it follows the collection's
  own file names, which follow the No-Intro DATs. A renamed file upstream turns
  into a miss, which is recorded and released by the expiry rather than by a
  hand-cleared cache — the failure mode is a generic cover, not an error.
- This is the first outbound use in the Player path, and the switch is the whole
  answer to it: one global preference, on by default, that a privacy-conscious
  player can turn off and be certain that nothing is sent.
- `BoxArtConsole` duplicates the console identity `#1038` owns, because a branch
  cannot reference a type that is not on `main`. Its members are declared in that
  enum's order and with its values, so unifying the two is a cast and a delete —
  the cleanup to make when both are on `main`.
- The size cap, the timeout, the concurrency ceiling, the miss expiry and the
  switch are all `BoxArtCacheOptions` fields with named defaults rather than
  constants buried in the flow, so each is changed in one place and each is
  pinned by a test that shrinks it.
