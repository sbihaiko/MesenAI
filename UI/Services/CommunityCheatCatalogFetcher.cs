using Mesen.Interop;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Mesen.Services
{
	//R.4 (ADR-0248 §5): fetches docs/community-cheats.json, the project's own
	//community cheat list, for the W-P11 Cheats sheet. Same path and cache rule
	//as CommunityPackCatalogFetcher's catalog: the GET goes through
	//CommunityPackDownloader (allow-list per hop, no auto-redirect, size cap),
	//the ETag cache under <EnhancementPackFolder>/.cache/ is decided by the
	//host-free CommunityCatalogCacheDecision, and a body that does not parse
	//never overwrites a cached one. As the project's own default index it needs
	//no confirmation (ADR-0146, MEI §3 item 4), and the client sends nothing
	//but the GET (MEI §4): no ROM hash, no vote - matching happens here, on the
	//downloaded file (CommunityCheatCatalog.ForCopy).
	public static class CommunityCheatCatalogFetcher
	{
		//Same embedded allow-list as the pack catalog (ADR-0138 §41): strictly
		//the assembly manifest, never the filesystem.
		private const string AllowlistResourceName = "Mesen.pack_host_allowlist.json";
		private static readonly TimeSpan CatalogTimeout = TimeSpan.FromSeconds(30);
		private static string CatalogCachePath => Path.Combine(CommunityPackPaths.CacheRoot, "community-cheats.json");
		private static string CatalogEtagPath => Path.Combine(CommunityPackPaths.CacheRoot, "community-cheats.etag");

		private static IReadOnlyList<CommunityCheatGame>? _lastKnown;

		//What the sheet shows the moment it opens: the last catalog this session
		//fetched, else the copy cached on disk by an earlier session, else none.
		public static IReadOnlyList<CommunityCheatGame> LastKnown
		{
			get {
				if(_lastKnown == null) {
					try {
						_lastKnown = File.Exists(CatalogCachePath) ? CommunityCheatCatalog.Parse(File.ReadAllText(CatalogCachePath)) : null;
					} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
						_lastKnown = null;
					}
				}
				return _lastKnown ?? Array.Empty<CommunityCheatGame>();
			}
		}

		//The current catalog (a conditional GET), or null when neither the
		//network nor the disk cache yields one. Never throws.
		public static async Task<IReadOnlyList<CommunityCheatGame>?> FetchAsync()
		{
			try {
				Directory.CreateDirectory(CommunityPackPaths.CacheRoot);
				string? cachedBody = File.Exists(CatalogCachePath) ? await File.ReadAllTextAsync(CatalogCachePath) : null;
				string? cachedETag = File.Exists(CatalogEtagPath) ? await File.ReadAllTextAsync(CatalogEtagPath) : null;
				bool cacheUsable = CommunityCatalogCacheDecision.IsCacheUsable(cachedETag, cachedBody);

				CommunityCatalogFetchOutcome outcome = await GetAsync(cacheUsable ? cachedETag : null);
				CommunityCatalogCacheResult resolved = CommunityCatalogCacheDecision.Resolve(outcome, cachedETag, cachedBody);
				IReadOnlyList<CommunityCheatGame>? games = CommunityCheatCatalog.Parse(resolved.Body);
				if(games == null) {
					//A fresh body that does not parse must not hide a verified cache.
					games = ReferenceEquals(resolved.Body, cachedBody) ? null : CommunityCheatCatalog.Parse(cachedBody);
					EmuApi.WriteLogEntry("[CommunityCheatFetch] catalog HTTP status=" + outcome.StatusCode + " did not parse" + (games != null ? " - using the disk cache" : ""));
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
				EmuApi.WriteLogEntry("[CommunityCheatFetch] threw: " + ex.Message);
				return null;
			}
		}

		private static async Task<CommunityCatalogFetchOutcome> GetAsync(string? ifNoneMatchETag)
		{
			using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(AllowlistResourceName);
			IReadOnlyList<CommunityPackHostEntry> allowedHosts = stream == null ? Array.Empty<CommunityPackHostEntry>() : CommunityPackHostAllowlist.LoadFromStream(stream);
			CommunityPackDownloader.Response? response = await CommunityPackDownloader.GetAsync(CommunityCheatCatalog.CatalogUrl, allowedHosts, CommunityPackDownloader.MaxCatalogBytes, ifNoneMatchETag, CatalogTimeout);
			if(response == null) {
				return new CommunityCatalogFetchOutcome(0, null, null);
			}
			string? body = response.Body == null ? null : System.Text.Encoding.UTF8.GetString(response.Body);
			return new CommunityCatalogFetchOutcome(response.StatusCode, response.ETag, body);
		}
	}
}
