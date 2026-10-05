using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Xunit;

namespace Mesen.HeadlessTests;

//#878: a legacy HD pack whose extraction fails partway leaves a half-written
//mep/ folder with no .mep-install.json. The next install of the same pack then
//takes the RefuseNonEmptyUnstamped branch and tells the user their own folder
//is in the way - the install is blocked until the folder is deleted by hand.
//The coordinator already states this invariant for the supportedRom-contradicts
//case; the extract-failure path has to do the same.
//
//The failure here is driven through the real coordinator, and dies *after* the
//first entries are on disk: "blocker" is written as a file and "blocker/inner"
//then needs it to be a directory. The 2 GiB MaxExtractedBytes gate and a
//corrupt nested archive reach the same branch, just more expensively.
//Needs a built core for the log and content-id calls; skips otherwise.
[Collection(NativeCoreCollection.Name)]
public class CommunityPackFailedExtractCleanupTests : IDisposable
{
	private const string ShaA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

	private readonly string _root = Path.Combine(Path.GetTempPath(), "mesen-878-" + Guid.NewGuid().ToString("N"));
	private readonly Func<CommunityPackLoadTarget> _savedProbe = CommunityPackInstallCoordinator.ReadCurrentLoad;
	//TestAppBuilder turns auto-install off for the whole test home (#751);
	//this drives the auto-install path, so it turns it on and puts it back.
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
	private CommunityPackLoadTarget _game = null!;

	private string OutFolder => Path.Combine(_game.SiblingFolder, "mep");

	public CommunityPackFailedExtractCleanupTests() => ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = true;

	public void Dispose()
	{
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		CommunityPackInstallCoordinator.ReadCurrentLoad = _savedProbe;
		try {
			Directory.Delete(_root, true);
		} catch(IOException) {
		}
	}

	[Fact]
	public void A_failed_extraction_leaves_no_folder_behind_and_the_retry_installs()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Setup();

		CommunityPackInstallOutcome failed = CommunityPackInstallCoordinator.Install(
			LegacyEntry(), WritePartlyExtractableZip(), new Dictionary<string, string>(), _game);

		Assert.Equal(CommunityPackInstallStatus.Failed, failed.Status);
		//Pins that this is the mid-write family, the one the issue describes: a
		//clean refusal returns "not a legacy HD pack" instead. The fix is not
		//only for it, though - TryExtractLegacyPack creates textures/ before it
		//even opens the zip, so every extraction failure used to leave a blocking
		//folder, and they all reach the branch cleared below.
		Assert.Contains("cannot extract legacy HD pack", failed.Message);

		//The refusal left nothing: the folder is as it was found (absent), so the
		//unstamped-folder guard cannot mistake it for the user's own work.
		Assert.False(Directory.Exists(OutFolder), "the failed extraction left an unstamped folder behind");

		//The part the user sees: retrying the same pack now works.
		CommunityPackInstallOutcome retried = CommunityPackInstallCoordinator.Install(
			LegacyEntry(), WriteLegacyZip(), new Dictionary<string, string>(), _game);

		Assert.Equal(CommunityPackInstallStatus.Installed, retried.Status);
		Assert.True(File.Exists(Path.Combine(OutFolder, "textures", "hires.txt")));
	}

	//One game, with its sibling folder, so outFolder is <sibling>/mep (ADR-0049).
	private void Setup()
	{
		Directory.CreateDirectory(_root);
		//Keep the install registry / EnhancementPacks cache out of the user's real home.
		typeof(ConfigManager).GetField("_homeFolder", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.Combine(_root, "home"));
		Directory.CreateDirectory(Path.Combine(_root, "home"));
		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);

		string folder = Path.Combine(_root, "roms", "GameA");
		Directory.CreateDirectory(folder);
		_game = new CommunityPackLoadTarget(ShaA, ShaA, folder, "GameA", 1);
		//The coordinator reads the load before the first destructive step (#657);
		//without the swap it reads generation 0 with no window open and drops the
		//install as Stale before reaching the extraction at all.
		CommunityPackInstallCoordinator.ReadCurrentLoad = () => _game;
	}

	private static CommunityPackCatalogEntry LegacyEntry() => new() {
		Kind = "hd-legacy", Name = "Game A pack", Game = "GameA", System = "nes",
		Sha256 = new string('a', 64), PackId = "a-pack", ContentId = "a-content"
	};

	//hires.txt first, so the pack root is found and writing starts; then a file
	//and a path beneath it, which cannot both exist on a filesystem.
	private string WritePartlyExtractableZip() => WriteZip(
		("hires.txt", "<ver>106\n<img>tile.png\n"), ("tile.png", "png"),
		("blocker", "a file"), ("blocker/inner.png", "png"));

	private string WriteLegacyZip() => WriteZip(
		("hires.txt", "<ver>106\n<img>tile.png\n"), ("tile.png", "png"));

	private string WriteZip(params (string Name, string Text)[] entries)
	{
		string zipPath = Path.Combine(_root, "a-pack-" + Guid.NewGuid().ToString("N") + ".zip");
		using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
		foreach((string name, string text) in entries) {
			using StreamWriter writer = new(zip.CreateEntry(name).Open());
			writer.Write(text);
		}
		return zipPath;
	}
}
