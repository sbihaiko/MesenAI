using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Controls;
using Mesen.Logic;
using Mesen.Services;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//W-P2's pack badge from the recent entry's remembered No-Intro SHA-1: the
//rule (RecentPackBadge) and the folder/registry lookup (RecentPackIndex) are
//pinned host-free in UI.Tests/Play/RecentPackBadgeTests; this checks the
//realized Recent tiles - a tile whose hash has an installed pack gets the
//badge and its tooltip after the off-thread lookup, a tile whose hash has no
//pack and an old entry without a hash do not.
//
//Needs a MainWindow (EmuApi.InitDll), so it self-skips on the core-less runner.
[Collection(NativeCoreCollection.Name)]
public class PlayHomePackBadgeTests : IDisposable
{
	private const string Installed = "MesenAI Badge Test (Installed Pack)";
	private const string NoPack = "MesenAI Badge Test (No Pack)";
	private const string OldEntry = "MesenAI Badge Test (Old Entry)";

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly List<RecentGameHash> _hashes = new(ConfigManager.Config.RecentFiles.GameHashes);
	//Hashes no real ROM has, so the seeded registry file never shadows (or
	//overwrites) one of the user's own installs.
	private readonly string _installedSha1 = FakeSha1();
	private readonly string _noPackSha1 = FakeSha1();
	private readonly List<string> _disabled = new(ConfigManager.Config.EnhancementPacks.DisabledPacks);
	private readonly Dictionary<string, string> _preferences = new(ConfigManager.Config.EnhancementPacks.RomPackPreference);
	private readonly string _packFolder = Path.Combine(ConfigManager.EnhancementPackFolder, "MesenAI Badge Test Pack " + Guid.NewGuid().ToString("N")[..8]);

	public void Dispose()
	{
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}
		string registry = CommunityPackInstallRegistry.FilePath(CommunityPackPaths.CacheRoot, _installedSha1);
		if(File.Exists(registry)) {
			File.Delete(registry);
		}
		ConfigManager.Config.RecentFiles.GameHashes = _hashes;
		ConfigManager.Config.EnhancementPacks.DisabledPacks = _disabled;
		ConfigManager.Config.EnhancementPacks.RomPackPreference = _preferences;
		if(Directory.Exists(_packFolder)) {
			Directory.Delete(_packFolder, true);
		}
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
	}

	private static string FakeSha1() => ("FEED" + Guid.NewGuid().ToString("N") + "0000").ToUpperInvariant();

	[AvaloniaFact]
	public void A_recent_tile_whose_hash_has_an_installed_pack_shows_the_badge()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string registry = CommunityPackInstallRegistry.FilePath(CommunityPackPaths.CacheRoot, _installedSha1);
		Assert.False(File.Exists(registry), "a registry file for the fixture hash already exists: " + registry);
		CommunityPackInstallRegistry.Write(CommunityPackPaths.CacheRoot, _installedSha1, new CommunityPackInstallRecord { PackId = "badge-test" });
		List<RecentGameHash> hashes = new();
		RecentGameHashes.Remember(hashes, NoPack, _noPackSha1, "");
		RecentGameHashes.Remember(hashes, Installed, _installedSha1, "");
		ConfigManager.Config.RecentFiles.GameHashes = hashes;

		//One Recent tile per home: the row shows as many tiles as the window's
		//width fits, so each case gets the grid's first tile.
		StateGridEntry withPack = OnlyTile(Installed);
		WaitFor(() => withPack.HasPack && !StateGridEntry.ThumbnailsInFlight, "the hash-found pack badge never appeared");
		Border badge = Badge(withPack);
		Assert.True(badge.IsOnScreen());
		Assert.Equal("Enhancement pack installed", ToolTip.GetTip(badge));

		foreach(string name in new[] { NoPack, OldEntry }) {
			StateGridEntry tile = OnlyTile(name);
			WaitFor(() => !StateGridEntry.ThumbnailsInFlight, "the tile lookups never settled");
			Assert.False(Badge(tile).IsOnScreen(), name);
			Assert.Equal("", tile.PackBadgeText);
		}
	}

	//A pack.json in the packs folder whose targets carry the fixture hash.
	private void InstallPack(string sha1)
	{
		Directory.CreateDirectory(_packFolder);
		File.WriteAllText(Path.Combine(_packFolder, "pack.json"),
			"{ \"name\": \"Badge Test Pack\", \"version\": \"1.2\", \"targets\": [ { \"sha1\": \"" + sha1 + "\" } ] }");
	}

	private void Remember(string game, string sha1)
	{
		List<RecentGameHash> hashes = new();
		RecentGameHashes.Remember(hashes, game, sha1, "");
		ConfigManager.Config.RecentFiles.GameHashes = hashes;
	}

	[AvaloniaFact]
	public void A_disabled_pack_or_a_no_pack_preference_does_not_light_the_badge()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		InstallPack(_installedSha1);
		Remember(Installed, _installedSha1);

		StateGridEntry lit = OnlyTile(Installed);
		WaitFor(() => lit.HasPack && !StateGridEntry.ThumbnailsInFlight, "an enabled installed pack never showed the badge");

		ConfigManager.Config.EnhancementPacks.DisabledPacks = new List<string> { Path.GetFileName(_packFolder) };
		StateGridEntry disabled = OnlyTile(Installed);
		WaitFor(() => !StateGridEntry.ThumbnailsInFlight, "the tile lookup never settled");
		Assert.False(Badge(disabled).IsOnScreen(), "a disabled pack lit the badge");

		ConfigManager.Config.EnhancementPacks.DisabledPacks = new List<string>();
		ConfigManager.Config.EnhancementPacks.RomPackPreference = new Dictionary<string, string> { [_installedSha1] = PackPreferenceResolver.NoPack };
		StateGridEntry noPack = OnlyTile(Installed);
		WaitFor(() => !StateGridEntry.ThumbnailsInFlight, "the tile lookup never settled");
		Assert.False(Badge(noPack).IsOnScreen(), "a ROM that prefers no pack lit the badge");
	}

	//W-P2: "Last played today · Badge Test Pack 1.2" once the lookup knows the pack.
	[AvaloniaFact]
	public void The_continue_subtitle_names_the_pack_when_the_lookup_knows_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		InstallPack(_installedSha1);
		Remember(Installed, _installedSha1);
		(MainWindow window, _) = PlayHomeViewTests.ShowHome(UiMode.Player, Installed);
		TextBlock subtitle = window.FindNamed<TextBlock>("PlayHomeContinueSubtitle");
		WaitFor(() => subtitle.Text == "Last played today · Badge Test Pack 1.2", "the Continue subtitle never named the pack");

		//No remembered hash: the clause is omitted.
		Remember("Another game", _noPackSha1);
		(MainWindow plain, _) = PlayHomeViewTests.ShowHome(UiMode.Player, Installed);
		Settle();
		Assert.Equal("Last played today", plain.FindNamed<TextBlock>("PlayHomeContinueSubtitle").Text);

		//Disabled pack: no clause either.
		Remember(Installed, _installedSha1);
		ConfigManager.Config.EnhancementPacks.DisabledPacks = new List<string> { Path.GetFileName(_packFolder) };
		(MainWindow off, _) = PlayHomeViewTests.ShowHome(UiMode.Player, Installed);
		Settle();
		Assert.Equal("Last played today", off.FindNamed<TextBlock>("PlayHomeContinueSubtitle").Text);
	}

	private static StateGridEntry OnlyTile(string recentGame)
	{
		(MainWindow window, _) = PlayHomeViewTests.ShowHome(UiMode.Player, "Contra", recentGame);
		StateGridEntry tile = window.FindNamed<Panel>("PlayHomeRecentGrid").FindAll<StateGridEntry>().Single();
		Assert.Equal(recentGame, tile.Title);
		Assert.True(tile.IsOnScreen());
		return tile;
	}

	private static Border Badge(StateGridEntry tile) => tile.FindAll<Border>().Single(b => b.Name == "TilePackBadge");

	//The Continue lookup has no in-flight flag; an absence needs a beat to be believed.
	private static void Settle()
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(clock.ElapsedMilliseconds < 1500) {
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			Assert.True(clock.ElapsedMilliseconds < 30000, failure);
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}
}
