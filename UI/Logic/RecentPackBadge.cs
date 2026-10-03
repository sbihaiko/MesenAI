using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Mesen.Logic;

//W-P2 (PRD Part B §13.5.2): the pack badge on a Recent tile, decided without
//loading the game. A recent entry remembers the No-Intro SHA-1 the pack system
//matched on when the game last loaded (ADR-0003, ADR-0039: EmuApi.GetMepRomSha1);
//the home looks that hash up in the installed packs and the community catalog.
//Host-free (BCL only), dual-compiled into UI.Tests.

//What a recent-game file (.rgd) cannot say: the hash of the game it reopens.
//Name is the .rgd's own name (the ROM file name without extension, as
//SaveStateManager::SaveRecentGame names it); RomPath is the ROM file (for an
//archive, the archive), whose ADR-0049 sibling folder may hold a pack.
public sealed class RecentGameHash
{
	public string Name { get; set; } = "";
	public string Sha1 { get; set; } = "";
	public string RomPath { get; set; } = "";
}

public static class RecentGameHashes
{
	//The Play home lists at most this many recent games (RecentGamesViewModel).
	public const int MaxEntries = 72;

	//Newest first; one row per game name (case-insensitive, like the file
	//system the .rgd lives on may be). A load without a hash records nothing.
	public static void Remember(List<RecentGameHash> list, string name, string sha1, string romPath)
	{
		if(string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sha1)) {
			return;
		}
		list.RemoveAll(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
		list.Insert(0, new RecentGameHash { Name = name, Sha1 = sha1.Trim().ToUpperInvariant(), RomPath = romPath ?? "" });
		if(list.Count > MaxEntries) {
			list.RemoveRange(MaxEntries, list.Count - MaxEntries);
		}
	}

	public static RecentGameHash? Find(IReadOnlyList<RecentGameHash> list, string name)
	{
		foreach(RecentGameHash hash in list) {
			if(string.Equals(hash.Name, name, StringComparison.OrdinalIgnoreCase)) {
				return hash;
			}
		}
		return null;
	}
}

//Sha1: the entry's remembered hash ("" for an entry recorded before hashes
//were kept). NamedHdPack: HdPacks/<ROM name>/hires.txt (PlayHome.HasHdPack),
//found by name alone. LocalPack: RecentPackIndex.HasLocalPack.
//CommunityInstalled: the ADR-0147 install registry has the hash. CatalogMatch:
//an accepted catalog row matches (CommunityPackCatalogMatcher).
public readonly record struct RecentPackFacts(string Sha1, bool NamedHdPack = false, bool LocalPack = false, bool CommunityInstalled = false, bool CatalogMatch = false, bool AutoInstallCommunityPacks = false);

//TextKey: the message the badge's tooltip shows ("" when hidden).
public readonly record struct RecentPackBadgeState(bool Visible, string TextKey);

public static class RecentPackBadge
{
	public const string InstalledKey = "PlayHomePackBadgeInstalled";
	public const string CommunityKey = "PlayHomePackBadgeCommunity";

	private static readonly RecentPackBadgeState Hidden = new(false, "");

	public static RecentPackBadgeState Decide(RecentPackFacts facts)
	{
		if(facts.NamedHdPack) {
			return new(true, InstalledKey);
		}
		//No hash, no lookup: an old entry gets its badge once it is replayed.
		if(string.IsNullOrWhiteSpace(facts.Sha1)) {
			return Hidden;
		}
		if(facts.LocalPack || facts.CommunityInstalled) {
			return new(true, InstalledKey);
		}
		//A catalog pack is "available" only while AutoInstallCommunityPacks
		//(ADR-0146's master switch) will bring it when the game is played.
		if(facts.CatalogMatch && facts.AutoInstallCommunityPacks) {
			return new(true, CommunityKey);
		}
		return Hidden;
	}
}

//The installed local packs a hash or a ROM name can find, read once per home:
//the top level of EnhancementPacks/ (folders and .zip containers, never the
//.cache scratch space) and, per entry, the ROM's ADR-0049 sibling folder.
//It counts what MepPackManager::ScanAndMatch would load as a definite match -
//a pack.json whose targets[] carry the hash, or a container named like the ROM
//(ADR-0049). ADR-0145's optimistic candidates (any other pack.json) are not
//"this game has a pack", and neither is an auto-only bootstrap container.
public sealed class RecentPackIndex
{
	//Human-layer convention probes (MepZipValidator / Core kConventionProbe)
	//plus a classic HD pack's root hires.txt; checked at the root and under
	//mep/ (ADR-0147), never under auto/.
	private static readonly string[] Probes = {
		"pack.json",
		"hires.txt",
		"textures/hires.txt",
		"audio/hires.txt",
		"audio/fingerprints.json",
		"synth/preset.cfg",
	};

	//A pack.json is a few KB; past this it is not one worth parsing here.
	private const long MaxPackJsonBytes = 1024 * 1024;

	private readonly HashSet<string> _targetSha1 = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _containerNames = new(StringComparer.OrdinalIgnoreCase);

	public static RecentPackIndex Scan(string packsFolder)
	{
		RecentPackIndex index = new();
		if(string.IsNullOrEmpty(packsFolder) || !Directory.Exists(packsFolder)) {
			return index;
		}
		try {
			foreach(string dir in Directory.EnumerateDirectories(packsFolder)) {
				string name = Path.GetFileName(dir);
				if(name.Length == 0 || name[0] == '.') {
					continue;
				}
				string packJson = Path.Combine(dir, "pack.json");
				if(File.Exists(packJson)) {
					index.AddTargets(ReadSmallText(packJson));
				}
				if(IsHumanPackFolder(dir)) {
					index._containerNames.Add(name);
				}
			}
			foreach(string zip in Directory.EnumerateFiles(packsFolder, "*.zip")) {
				string name = Path.GetFileNameWithoutExtension(zip);
				if(name.Length == 0 || name[0] == '.') {
					continue;
				}
				index._containerNames.Add(name);
				index.AddTargets(ReadZipPackJson(zip));
			}
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
			//A packs folder that cannot be listed has no pack to show.
		}
		return index;
	}

	public bool HasLocalPack(string sha1, string romName, string romPath)
	{
		if(string.IsNullOrWhiteSpace(sha1)) {
			return false;
		}
		if(_targetSha1.Contains(sha1.Trim()) || (!string.IsNullOrEmpty(romName) && _containerNames.Contains(romName))) {
			return true;
		}
		string sibling = RemasterRecentProjects.SiblingOf(romPath);
		return sibling.Length > 0 && IsHumanPackFolder(sibling);
	}

	public static bool HasInstalledCommunityPack(string cacheRoot, string sha1)
	{
		if(string.IsNullOrWhiteSpace(cacheRoot) || string.IsNullOrWhiteSpace(sha1)) {
			return false;
		}
		try {
			return File.Exists(CommunityPackInstallRegistry.FilePath(cacheRoot, sha1.Trim()));
		} catch(ArgumentException) {
			return false;
		}
	}

	private static bool IsHumanPackFolder(string folder)
	{
		try {
			if(!Directory.Exists(folder)) {
				return false;
			}
			foreach(string layer in new[] { "", "mep" }) {
				foreach(string probe in Probes) {
					if(File.Exists(Path.Combine(folder, layer, probe))) {
						return true;
					}
				}
			}
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) {
		}
		return false;
	}

	private void AddTargets(string? packJson)
	{
		if(string.IsNullOrWhiteSpace(packJson)) {
			return;
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(packJson);
			if(doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("targets", out JsonElement targets) || targets.ValueKind != JsonValueKind.Array) {
				return;
			}
			foreach(JsonElement target in targets.EnumerateArray()) {
				if(target.ValueKind == JsonValueKind.Object && target.TryGetProperty("sha1", out JsonElement sha1) && sha1.ValueKind == JsonValueKind.String) {
					string? value = sha1.GetString();
					if(!string.IsNullOrWhiteSpace(value)) {
						_targetSha1.Add(value.Trim());
					}
				}
			}
		} catch(JsonException) {
			//An unreadable pack.json targets nothing; the Core rejects it too.
		}
	}

	private static string? ReadSmallText(string path)
	{
		try {
			return new FileInfo(path).Length > MaxPackJsonBytes ? null : File.ReadAllText(path);
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
			return null;
		}
	}

	private static string? ReadZipPackJson(string zipPath)
	{
		try {
			using ZipArchive zip = ZipFile.OpenRead(zipPath);
			ZipArchiveEntry? entry = zip.GetEntry("pack.json");
			if(entry == null || entry.Length > MaxPackJsonBytes) {
				return null;
			}
			using StreamReader reader = new(entry.Open());
			return reader.ReadToEnd();
		} catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException) {
			return null;
		}
	}
}
