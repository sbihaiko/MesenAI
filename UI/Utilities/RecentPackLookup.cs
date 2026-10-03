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

		//The config-only half of the badge (no disk), for a pack named like the
		//game: a pack the player turned off or a ROM whose preference is "No pack" never lights it. Cheap, so
		//the tile can apply it on the UI thread before the lookup runs.
		public static bool Suppressed(string recentName, RecentGameHash? hash)
		{
			EnhancementPackConfig packs = ConfigManager.Config.EnhancementPacks;
			return RecentPackBadge.IsDisabled(packs.DisabledPacks, recentName) || PrefersNoPack(packs, hash);
		}

		private static bool PrefersNoPack(EnhancementPackConfig packs, RecentGameHash? hash)
		{
			if(hash == null || string.IsNullOrWhiteSpace(hash.Sha1)) {
				return false;
			}
			foreach(var entry in packs.RomPackPreference) {
				if(string.Equals(entry.Key.Trim(), hash.Sha1.Trim(), StringComparison.OrdinalIgnoreCase)) {
					return PackPreferenceResolver.IsNoPack(entry.Value);
				}
			}
			return false;
		}

		//Off the UI thread. hash: the entry's remembered hash (null for an
		//entry recorded before hashes were kept). Name/Version: the installed
		//pack's pack.json, read here with the badge ("" when not known).
		public static RecentPackInfo Lookup(string recentName, RecentGameHash? hash, bool namedHdPack, bool autoInstallCommunityPacks)
		{
			EnhancementPackConfig packs = ConfigManager.Config.EnhancementPacks;
			string sha1 = hash?.Sha1 ?? "";
			bool noPack = PrefersNoPack(packs, hash);
			if(namedHdPack || sha1.Length == 0) {
				bool disabled = namedHdPack && RecentPackBadge.IsDisabled(packs.DisabledPacks, recentName);
				return new RecentPackInfo(RecentPackBadge.Decide(new RecentPackFacts(sha1, NamedHdPack: namedHdPack, PackDisabled: disabled, PrefersNoPack: noPack)), "", "");
			}
			RecentPackIndex index;
			CommunityPackCatalog? catalog;
			lock(_lock) {
				_index ??= RecentPackIndex.Scan(ConfigManager.EnhancementPackFolder);
				_catalog ??= ReadCachedCatalog();
				index = _index;
				catalog = _catalog;
			}
			LocalPackInfo? local = index.FindLocalPack(sha1, recentName, hash!.RomPath);
			CommunityPackInstallRecord? record = local == null ? CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, sha1) : null;
			LocalPackInfo? installed = null;
			if(record != null) {
				string folder = Path.Combine(ConfigManager.EnhancementPackFolder, record.Container);
				installed = record.Container.Length > 0 && Directory.Exists(folder) ? RecentPackIndex.ReadFolderInfo(folder) : new LocalPackInfo(record.Container, "", "");
			}
			LocalPackInfo? found = local ?? installed;
			bool listed = found == null && catalog != null && CommunityPackCatalogMatcher.FindMatchingEntry(catalog, sha1, recentName) != null;
			bool disabledPack = found != null && RecentPackBadge.IsDisabled(packs.DisabledPacks, found.Value.Container);
			RecentPackBadgeState badge = RecentPackBadge.Decide(new RecentPackFacts(sha1, false, local != null, installed != null, listed, autoInstallCommunityPacks, disabledPack, noPack));
			bool known = found != null && badge.Visible;
			return new RecentPackInfo(badge, known ? found!.Value.Name : "", known ? found!.Value.Version : "");
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
