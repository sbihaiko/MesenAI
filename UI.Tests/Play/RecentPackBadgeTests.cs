using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//W-P2's pack badge on a Recent tile: which entries carry it and what it
	//says. The recent entry remembers the No-Intro SHA-1 of the game it last
	//loaded (ADR-0003, ADR-0039); the home looks that hash up in the installed
	//packs and the community catalog without loading the game.
	public class RecentPackBadgeTests
	{
		private const string Sha1 = "2A4E126D0286BEA0BF503C80A12352C57539F76B";
		private const string OtherSha1 = "0000000000000000000000000000000000000001";

		[Fact]
		public void An_entry_without_a_hash_has_no_badge_until_replayed()
		{
			RecentPackBadgeState state = RecentPackBadge.Decide(new RecentPackFacts(Sha1: "", LocalPack: true, CommunityInstalled: true, CatalogMatch: true, AutoInstallCommunityPacks: true));
			Assert.False(state.Visible);
			Assert.Equal("", state.TextKey);
		}

		[Fact]
		public void A_named_hd_pack_needs_no_hash()
		{
			RecentPackBadgeState state = RecentPackBadge.Decide(new RecentPackFacts(Sha1: "", NamedHdPack: true));
			Assert.True(state.Visible);
			Assert.Equal(RecentPackBadge.InstalledKey, state.TextKey);
		}

		[Fact]
		public void An_installed_pack_reads_installed()
		{
			Assert.Equal(RecentPackBadge.InstalledKey, RecentPackBadge.Decide(new RecentPackFacts(Sha1, LocalPack: true)).TextKey);
			Assert.Equal(RecentPackBadge.InstalledKey, RecentPackBadge.Decide(new RecentPackFacts(Sha1, CommunityInstalled: true)).TextKey);
			//Installed wins over "available": the pack is already here.
			Assert.Equal(RecentPackBadge.InstalledKey, RecentPackBadge.Decide(new RecentPackFacts(Sha1, LocalPack: true, CatalogMatch: true, AutoInstallCommunityPacks: true)).TextKey);
		}

		[Fact]
		public void A_catalog_pack_shows_only_while_it_would_install_by_itself()
		{
			RecentPackBadgeState on = RecentPackBadge.Decide(new RecentPackFacts(Sha1, CatalogMatch: true, AutoInstallCommunityPacks: true));
			Assert.True(on.Visible);
			Assert.Equal(RecentPackBadge.CommunityKey, on.TextKey);
			//ADR-0146's master switch off: playing will not bring the pack.
			Assert.False(RecentPackBadge.Decide(new RecentPackFacts(Sha1, CatalogMatch: true, AutoInstallCommunityPacks: false)).Visible);
		}

		[Fact]
		public void No_pack_anywhere_has_no_badge()
		{
			RecentPackBadgeState state = RecentPackBadge.Decide(new RecentPackFacts(Sha1, AutoInstallCommunityPacks: true));
			Assert.False(state.Visible);
			Assert.Equal("", state.TextKey);
		}

		[Fact]
		public void Remembering_a_hash_puts_it_first_and_replaces_the_same_game()
		{
			List<RecentGameHash> list = new();
			RecentGameHashes.Remember(list, "Contra (USA)", Sha1.ToLowerInvariant(), "/roms/Contra (USA).nes");
			RecentGameHashes.Remember(list, "Metroid (USA)", OtherSha1, "/roms/Metroid (USA).nes");
			RecentGameHashes.Remember(list, "contra (usa)", Sha1, "/roms2/Contra (USA).nes");

			Assert.Equal(2, list.Count);
			Assert.Equal("contra (usa)", list[0].Name);
			//Stored as ADR-0039 prints it: 40 uppercase hex digits.
			Assert.Equal(Sha1, list[0].Sha1);
			Assert.Equal("/roms2/Contra (USA).nes", list[0].RomPath);
			Assert.Same(list[0], RecentGameHashes.Find(list, "Contra (USA)"));
			Assert.Null(RecentGameHashes.Find(list, "Zelda"));
		}

		[Fact]
		public void Remembering_ignores_a_game_without_a_hash_and_keeps_the_list_bounded()
		{
			List<RecentGameHash> list = new();
			RecentGameHashes.Remember(list, "Contra", "", "/roms/Contra.nes");
			RecentGameHashes.Remember(list, "", Sha1, "/roms/x.nes");
			Assert.Empty(list);

			for(int i = 0; i < RecentGameHashes.MaxEntries + 5; i++) {
				RecentGameHashes.Remember(list, "Game " + i, Sha1, "");
			}
			Assert.Equal(RecentGameHashes.MaxEntries, list.Count);
			Assert.Equal("Game " + (RecentGameHashes.MaxEntries + 4), list[0].Name);
			Assert.Null(RecentGameHashes.Find(list, "Game 0"));
		}

		[Fact]
		public void The_index_finds_a_container_whose_pack_json_targets_the_hash()
		{
			using TempTree tree = new();
			string folder = tree.Dir("packs/Contra 80s");
			File.WriteAllText(Path.Combine(folder, "pack.json"), PackJson(Sha1.ToLowerInvariant()));
			string zip = Path.Combine(tree.Dir("packs"), "Metroid Remix.zip");
			using(ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
				using StreamWriter writer = new(archive.CreateEntry("pack.json").Open());
				writer.Write(PackJson(OtherSha1));
			}

			RecentPackIndex index = RecentPackIndex.Scan(tree.Path("packs"));
			Assert.True(index.HasLocalPack(Sha1, "Contra (USA)", ""));
			Assert.True(index.HasLocalPack(OtherSha1, "Metroid (USA)", ""));
			Assert.False(index.HasLocalPack("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "Zelda", ""));
			Assert.False(index.HasLocalPack("", "Contra (USA)", ""));
		}

		[Fact]
		public void The_index_finds_a_container_named_like_the_rom()
		{
			using TempTree tree = new();
			File.WriteAllText(Path.Combine(tree.Dir("packs/Contra (USA)/textures"), "hires.txt"), "<ver>106");
			File.WriteAllText(Path.Combine(tree.Dir("packs"), "Zelda (USA).zip"), "");
			//A bootstrap's auto-only container is not an enhancement pack.
			File.WriteAllText(Path.Combine(tree.Dir("packs/Mega Man (USA)/auto/textures"), "hires.txt"), "<ver>106");
			//.cache is the client's scratch space, never a container.
			File.WriteAllText(Path.Combine(tree.Dir("packs/.cache/Metroid (USA)"), "pack.json"), PackJson(OtherSha1));

			RecentPackIndex index = RecentPackIndex.Scan(tree.Path("packs"));
			Assert.True(index.HasLocalPack(Sha1, "contra (usa)", ""));
			Assert.True(index.HasLocalPack(Sha1, "Zelda (USA)", ""));
			Assert.False(index.HasLocalPack(Sha1, "Mega Man (USA)", ""));
			Assert.False(index.HasLocalPack(OtherSha1, "Metroid (USA)", ""));
		}

		[Fact]
		public void The_index_finds_a_sibling_pack_next_to_the_rom()
		{
			using TempTree tree = new();
			string roms = tree.Dir("roms");
			File.WriteAllText(Path.Combine(tree.Dir("roms/Contra (USA)/mep"), "pack.json"), PackJson(Sha1));
			File.WriteAllText(Path.Combine(tree.Dir("roms/Zelda (USA)"), "notes.txt"), "not a pack");

			RecentPackIndex index = RecentPackIndex.Scan(tree.Path("missing-packs-folder"));
			Assert.True(index.HasLocalPack(Sha1, "Contra (USA)", Path.Combine(roms, "Contra (USA).nes")));
			Assert.False(index.HasLocalPack(Sha1, "Zelda (USA)", Path.Combine(roms, "Zelda (USA).nes")));
			Assert.False(index.HasLocalPack(Sha1, "Metroid (USA)", Path.Combine(roms, "Metroid (USA).nes")));
		}

		[Fact]
		public void An_installed_community_pack_is_found_in_the_install_registry()
		{
			using TempTree tree = new();
			string cache = tree.Dir("packs/.cache");
			CommunityPackInstallRegistry.Write(cache, Sha1, new CommunityPackInstallRecord { PackId = "contra-80s" });
			Assert.True(RecentPackIndex.HasInstalledCommunityPack(cache, Sha1.ToLowerInvariant()));
			Assert.False(RecentPackIndex.HasInstalledCommunityPack(cache, OtherSha1));
			Assert.False(RecentPackIndex.HasInstalledCommunityPack(cache, ""));
		}

		private static string PackJson(string sha1) =>
			"{ \"mep\": \"1.5.0\", \"name\": \"x\", \"version\": \"1.0.0\", \"targets\": [ { \"system\": \"nes\", \"sha1\": \"" + sha1 + "\" } ] }";

		private sealed class TempTree : IDisposable
		{
			private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mesen-recentpack-" + Guid.NewGuid().ToString("N"));

			public string Path(string relative) => System.IO.Path.Combine(_root, relative);

			public string Dir(string relative)
			{
				string dir = Path(relative);
				Directory.CreateDirectory(dir);
				return dir;
			}

			public void Dispose()
			{
				if(Directory.Exists(_root)) {
					Directory.Delete(_root, true);
				}
			}
		}
	}
}
