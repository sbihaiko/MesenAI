using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using System;
using System.IO;
using System.Text.Json;

namespace Mesen.Utilities
{
	//W-P2's pack badge, host side: remembers the No-Intro SHA-1 of each game
	//that loads (RecentGameHashes, kept in the config next to RecentFiles) and
	//answers, per Recent tile, whether a pack is there for it (RecentPackBadge).
	//The packs folder and the cached community catalog are read once per home
	//(Invalidate), off the UI thread - StateGridEntry asks from its preview task.
	public static class RecentPackLookup
	{
		private static readonly object _lock = new();
		private static RecentPackIndex? _index;
		private static CommunityPackCatalog? _catalog;

		//GameLoaded: the hash the pack system matched this load on (ADR-0039,
		//after any patch), under the name the recent-game file will carry.
		public static void RememberLoadedGame(RomInfo romInfo)
		{
			string sha1 = EmuApi.GetMepRomSha1();
			string name = romInfo.GetRomName();
			string romPath = ((ResourcePath)romInfo.RomPath).Path;
			if(string.IsNullOrWhiteSpace(sha1) || string.IsNullOrWhiteSpace(name)) {
				return;
			}
			Dispatcher.UIThread.Post(() => {
				RecentGameHashes.Remember(ConfigManager.Config.RecentFiles.GameHashes, name, sha1, romPath);
				ConfigManager.Config.Save();
			});
		}

		//The home is about to list its tiles: packs may have come or gone.
		public static void Invalidate()
		{
			lock(_lock) {
				_index = null;
				_catalog = null;
			}
		}

		//Off the UI thread. hash: the entry's remembered hash (null for an
		//entry recorded before hashes were kept).
		public static RecentPackBadgeState Badge(string recentName, RecentGameHash? hash, bool namedHdPack, bool autoInstallCommunityPacks)
		{
			string sha1 = hash?.Sha1 ?? "";
			if(namedHdPack || sha1.Length == 0) {
				return RecentPackBadge.Decide(new RecentPackFacts(sha1, NamedHdPack: namedHdPack));
			}
			RecentPackIndex index;
			CommunityPackCatalog? catalog;
			lock(_lock) {
				_index ??= RecentPackIndex.Scan(ConfigManager.EnhancementPackFolder);
				_catalog ??= ReadCachedCatalog();
				index = _index;
				catalog = _catalog;
			}
			bool local = index.HasLocalPack(sha1, recentName, hash!.RomPath);
			bool installed = !local && RecentPackIndex.HasInstalledCommunityPack(CommunityPackPaths.CacheRoot, sha1);
			bool listed = !local && !installed && catalog != null && CommunityPackCatalogMatcher.FindMatchingEntry(catalog, sha1, recentName) != null;
			return RecentPackBadge.Decide(new RecentPackFacts(sha1, false, local, installed, listed, autoInstallCommunityPacks));
		}

		//The catalog copy the last fetch left on disk (never the network here);
		//none yet means no community badge until a game load fetches it.
		private static CommunityPackCatalog? ReadCachedCatalog()
		{
			try {
				string path = CommunityPackPaths.CatalogCachePath;
				if(!File.Exists(path)) {
					return null;
				}
				CommunityPackCatalog? catalog = (CommunityPackCatalog?)JsonSerializer.Deserialize(File.ReadAllText(path), typeof(CommunityPackCatalog), MesenSerializerContext.Default);
				return catalog?.Packs == null ? null : catalog;
			} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) {
				return null;
			}
		}
	}
}
