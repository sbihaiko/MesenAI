using Mesen.Interop;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Mesen.Services
{
	//R.2 (ADR-0205 §7): fetches docs/community-replays.json, the project's own
	//shared-replay catalog, and downloads a listed replay for playback.
	//
	//The catalog: same path and cache rule as CommunityCheatCatalogFetcher -
	//the GET goes through CommunityPackDownloader with the pack allow-list
	//(raw.githubusercontent.com), the ETag cache under
	//<EnhancementPackFolder>/.cache/ is decided by CommunityCatalogCacheDecision,
	//and a body that does not parse never overwrites a cached one. As the
	//project's own default index it needs no confirmation (MEI §3 item 4), and
	//the client sends nothing but the GET (MEI §4): no ROM hash, no vote.
	//
	//A replay: the row's github.com/user-attachments URL, fetched through the
	//same downloader with the *replay* allow-list (scripts/replay_host_allowlist.json,
	//embedded - the file R.1's CI fetch reads, so the two sides cannot drift),
	//which re-checks the objects.githubusercontent.com redirect hop by hand;
	//capped at the §3 8 MB before a byte is buffered further, then verified
	//against the row's size and sha256 (MEI §3 item 1) before it is written to
	//the hash-keyed downloads/ cache. A cached copy is re-verified before reuse.
	public static class CommunityReplayCatalogFetcher
	{
		private const string PackAllowlistResourceName = "Mesen.pack_host_allowlist.json";
		private const string ReplayAllowlistResourceName = "Mesen.replay_host_allowlist.json";
		private static readonly TimeSpan CatalogTimeout = TimeSpan.FromSeconds(30);
		private static readonly TimeSpan ReplayTimeout = TimeSpan.FromMinutes(2);
		private static string CatalogCachePath => Path.Combine(CommunityPackPaths.CacheRoot, "community-replays.json");
		private static string CatalogEtagPath => Path.Combine(CommunityPackPaths.CacheRoot, "community-replays.etag");

		private static IReadOnlyList<CommunityReplayGame>? _lastKnown;

		//What the sheet shows the moment it opens: the last catalog this session
		//fetched, else the copy cached on disk by an earlier session, else none.
		public static IReadOnlyList<CommunityReplayGame> LastKnown
		{
			get {
				if(_lastKnown == null) {
					try {
						_lastKnown = File.Exists(CatalogCachePath) ? CommunityReplayCatalog.Parse(File.ReadAllText(CatalogCachePath)) : null;
					} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
						_lastKnown = null;
					}
				}
				return _lastKnown ?? Array.Empty<CommunityReplayGame>();
			}
		}

		//The current catalog (a conditional GET), or null when neither the
		//network nor the disk cache yields one. Never throws.
		public static async Task<IReadOnlyList<CommunityReplayGame>?> FetchAsync()
		{
			try {
				Directory.CreateDirectory(CommunityPackPaths.CacheRoot);
				string? cachedBody = File.Exists(CatalogCachePath) ? await File.ReadAllTextAsync(CatalogCachePath) : null;
				string? cachedETag = File.Exists(CatalogEtagPath) ? await File.ReadAllTextAsync(CatalogEtagPath) : null;
				bool cacheUsable = CommunityCatalogCacheDecision.IsCacheUsable(cachedETag, cachedBody);

				CommunityPackDownloader.Response? response = await CommunityPackDownloader.GetAsync(CommunityReplayCatalog.CatalogUrl, LoadAllowlist(PackAllowlistResourceName), CommunityPackDownloader.MaxCatalogBytes, cacheUsable ? cachedETag : null, CatalogTimeout);
				CommunityCatalogFetchOutcome outcome = response == null
					? new CommunityCatalogFetchOutcome(0, null, null)
					: new CommunityCatalogFetchOutcome(response.StatusCode, response.ETag, response.Body == null ? null : System.Text.Encoding.UTF8.GetString(response.Body));
				CommunityCatalogCacheResult resolved = CommunityCatalogCacheDecision.Resolve(outcome, cachedETag, cachedBody);
				IReadOnlyList<CommunityReplayGame>? games = CommunityReplayCatalog.Parse(resolved.Body);
				if(games == null) {
					//A fresh body that does not parse must not hide a verified cache.
					games = ReferenceEquals(resolved.Body, cachedBody) ? null : CommunityReplayCatalog.Parse(cachedBody);
					EmuApi.WriteLogEntry("[CommunityReplayFetch] catalog HTTP status=" + outcome.StatusCode + " did not parse" + (games != null ? " - using the disk cache" : ""));
				} else if(resolved.ShouldWriteCache) {
					await File.WriteAllTextAsync(CatalogCachePath, resolved.Body);
					if(outcome.ETag != null) {
						await File.WriteAllTextAsync(CatalogEtagPath, outcome.ETag);
					}
				}
				if(games != null) {
					_lastKnown = games;
				}
				return games;
			} catch(Exception ex) {
				EmuApi.WriteLogEntry("[CommunityReplayFetch] threw: " + ex.Message);
				return null;
			}
		}

		//The verified .mmo for a row, or the reason there is none. Never throws.
		public static async Task<ReplayFetchResult> DownloadAsync(CommunityReplay row)
		{
			try {
				string path = Path.Combine(CommunityPackPaths.DownloadsFolder, CommunityReplayCatalog.CacheFileName(row));
				if(File.Exists(path) && CommunityReplayCatalog.IsVerified(await File.ReadAllBytesAsync(path), row)) {
					return new ReplayFetchResult(path, ReplayFetchFailure.None);
				}
				CommunityPackDownloader.Response? response = await CommunityPackDownloader.GetAsync(row.Url, LoadAllowlist(ReplayAllowlistResourceName), CommunityReplayCatalog.MaxArchiveBytes, null, ReplayTimeout);
				if(response?.Body == null || response.StatusCode != 200) {
					EmuApi.WriteLogEntry("[CommunityReplayFetch] issue #" + row.Issue + " download failed, status=" + (response?.StatusCode.ToString() ?? "none"));
					return new ReplayFetchResult(null, ReplayFetchFailure.Unavailable);
				}
				if(!CommunityReplayCatalog.IsVerified(response.Body, row)) {
					EmuApi.WriteLogEntry("[CommunityReplayFetch] issue #" + row.Issue + " sha256/size mismatch - not played");
					return new ReplayFetchResult(null, ReplayFetchFailure.Mismatch);
				}
				Directory.CreateDirectory(CommunityPackPaths.DownloadsFolder);
				await File.WriteAllBytesAsync(path, response.Body);
				return new ReplayFetchResult(path, ReplayFetchFailure.None);
			} catch(Exception ex) {
				EmuApi.WriteLogEntry("[CommunityReplayFetch] download threw: " + ex.Message);
				return new ReplayFetchResult(null, ReplayFetchFailure.Unavailable);
			}
		}

		//Strictly the assembly manifest, never the filesystem (ADR-0138 §41).
		private static IReadOnlyList<CommunityPackHostEntry> LoadAllowlist(string resourceName)
		{
			using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
			return stream == null ? Array.Empty<CommunityPackHostEntry>() : CommunityPackHostAllowlist.LoadFromStream(stream);
		}
	}
}
