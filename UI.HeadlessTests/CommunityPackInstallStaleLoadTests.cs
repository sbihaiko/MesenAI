using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Xunit;

namespace Mesen.HeadlessTests;

//#657: a Restore or auto-install whose artifact download outlives the game it
//was started for. Game A is captured when the operation starts; by the time
//the coordinator runs, game B is loaded (CommunityPackInstallCoordinator.
//ReadCurrentLoad swapped). B's mep/ - here a Remaster build or B's own catalog
//pack - must survive, and nothing may be recorded under B's SHA-1. The
//still-loaded rule itself is pinned host-free in
//UI.Tests/CommunityPacks/CommunityPackLoadTargetTests; this drives the real
//coordinator (needs a built core for the log and content-id calls; skips otherwise).
[Collection(NativeCoreCollection.Name)]
public class CommunityPackInstallStaleLoadTests : IDisposable
{
	private const string ShaA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
	private const string ShaB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

	private readonly string _root = Path.Combine(Path.GetTempPath(), "mesen-657-" + Guid.NewGuid().ToString("N"));
	private readonly Func<CommunityPackLoadTarget> _savedProbe = CommunityPackInstallCoordinator.ReadCurrentLoad;
	private CommunityPackLoadTarget _gameA = null!;
	private CommunityPackLoadTarget _gameB = null!;
	private string _bMep = "";

	public void Dispose()
	{
		CommunityPackInstallCoordinator.ReadCurrentLoad = _savedProbe;
		try {
			Directory.Delete(_root, true);
		} catch(IOException) {
		}
	}

	[Fact]
	public void Restore_after_another_game_opened_leaves_that_games_mep_folder_alone()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Setup(bHasCatalogPack: false);
		CommunityPackInstallCoordinator.ReadCurrentLoad = () => _gameB;

		bool ok = CommunityPackInstallCoordinator.Restore(RecipeEntry(), WriteLegacyZip(), _gameA, out string error);

		Assert.True(File.Exists(Path.Combine(_bMep, "textures", "painted.png")), "B's Remaster build was deleted");
		Assert.Null(CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, ShaB));
		Assert.Null(CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, ShaA));
		Assert.False(ok);
		Assert.Contains("game changed", error);
	}

	[Fact]
	public void Auto_install_after_another_game_opened_leaves_that_games_catalog_pack_alone()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Setup(bHasCatalogPack: true);
		CommunityPackInstallRecord bRecord = CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, ShaB)!;
		CommunityPackInstallCoordinator.ReadCurrentLoad = () => _gameB;

		CommunityPackInstallOutcome outcome = CommunityPackInstallCoordinator.Install(
			LegacyEntry(), WriteLegacyZip(), new Dictionary<string, string>(), _gameA);

		Assert.True(File.Exists(Path.Combine(_bMep, "textures", "painted.png")), "B's catalog pack was deleted");
		Assert.False(File.Exists(Path.Combine(_bMep, "textures", "tile.png")), "A's pack was written into B's folder");
		Assert.Equal(bRecord.PackId, CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, ShaB)!.PackId);
		Assert.Null(CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, ShaA));
		Assert.Equal(CommunityPackInstallStatus.Stale, outcome.Status);
	}

	[Fact]
	public void Restore_for_the_game_still_loaded_writes_its_own_folder_and_registry_key()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Setup(bHasCatalogPack: false);
		CommunityPackInstallCoordinator.ReadCurrentLoad = () => _gameA;

		bool ok = CommunityPackInstallCoordinator.Restore(LegacyEntry(), WriteLegacyZip(), _gameA, out string error);

		Assert.True(ok, error);
		Assert.True(File.Exists(Path.Combine(_gameA.SiblingFolder, "mep", "textures", "tile.png")));
		Assert.Equal("a-pack", CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, ShaA)?.PackId);
		Assert.Null(CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, ShaB));
		Assert.True(File.Exists(Path.Combine(_bMep, "textures", "painted.png")));
	}

	[Fact]
	public void A_game_change_after_the_install_began_still_writes_the_captured_games_folder_and_key()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Setup(bHasCatalogPack: false);
		//A is loaded at the first check; B from then on - every later read of
		//the current load is what the pre-#657 coordinator wrote from.
		int reads = 0;
		CommunityPackInstallCoordinator.ReadCurrentLoad = () => reads++ == 0 ? _gameA : _gameB;

		CommunityPackInstallOutcome outcome = CommunityPackInstallCoordinator.Install(
			LegacyEntry(), WriteLegacyZip(), new Dictionary<string, string>(), _gameA);

		Assert.Equal(CommunityPackInstallStatus.Installed, outcome.Status);
		Assert.True(File.Exists(Path.Combine(_gameA.SiblingFolder, "mep", "textures", "tile.png")));
		Assert.Equal("a-pack", CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, ShaA)?.PackId);
		Assert.Null(CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, ShaB));
		Assert.True(File.Exists(Path.Combine(_bMep, "textures", "painted.png")));
	}

	//Two games side by side, each with its sibling folder (ADR-0049/0147). B's
	//mep/ holds a painted file: unstamped (a Remaster build) or stamped and
	//registered with a matching baseline (B's own, unedited catalog pack).
	private void Setup(bool bHasCatalogPack)
	{
		Directory.CreateDirectory(_root);
		//Keep the install registry / EnhancementPacks cache out of the user's real home.
		typeof(ConfigManager).GetField("_homeFolder", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.Combine(_root, "home"));
		Directory.CreateDirectory(Path.Combine(_root, "home"));
		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);

		string aFolder = Path.Combine(_root, "roms", "GameA");
		string bFolder = Path.Combine(_root, "roms", "GameB");
		Directory.CreateDirectory(aFolder);
		_bMep = Path.Combine(bFolder, "mep");
		Directory.CreateDirectory(Path.Combine(_bMep, "textures"));
		File.WriteAllText(Path.Combine(_bMep, "textures", "painted.png"), "B's art");
		_gameA = new CommunityPackLoadTarget(ShaA, ShaA, aFolder, "GameA", 1);
		_gameB = new CommunityPackLoadTarget(ShaB, ShaB, bFolder, "GameB", 2);
		if(bHasCatalogPack) {
			File.WriteAllText(Path.Combine(_bMep, ".mep-install.json"), "{ \"pack_id\": \"b-pack\", \"content_id\": \"b-content\", \"source\": { \"sha256\": \"" + new string('b', 64) + "\" } }\n");
			CommunityPackInstallRegistry.Write(CommunityPackPaths.CacheRoot, ShaB, new CommunityPackInstallRecord {
				PackId = "b-pack", ContentId = "b-content", SourceSha256 = new string('b', 64), Container = "GameB", MepPath = _bMep,
				BaselineContentId = EmuApi.GetMepContentId(_bMep),
			});
		}
	}

	private static CommunityPackCatalogEntry LegacyEntry() => new() {
		Kind = "hd-legacy", Name = "Game A pack", Game = "GameA", System = "nes", Sha256 = new string('a', 64), PackId = "a-pack", ContentId = "a-content"
	};

	private static CommunityPackCatalogEntry RecipeEntry() => new() {
		Name = "Game A pack", Game = "GameA", System = "nes", Sha256 = new string('a', 64), PackId = "a-pack", ContentId = "a-content",
		Recipe = JsonDocument.Parse("{}").RootElement
	};

	private string WriteLegacyZip()
	{
		string zipPath = Path.Combine(_root, "a-pack-" + Guid.NewGuid().ToString("N") + ".zip");
		using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
		foreach((string name, string text) in new[] { ("hires.txt", "<ver>106\n<img>tile.png\n"), ("tile.png", "png") }) {
			using StreamWriter writer = new(zip.CreateEntry(name).Open());
			writer.Write(text);
		}
		return zipPath;
	}
}
