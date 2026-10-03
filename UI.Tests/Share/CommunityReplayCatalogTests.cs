using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Share
{
	//R.2 (ADR-0205 §7-§9): docs/community-replays.json as the client reads it,
	//the rows it lists for the loaded ROM, and the checks a downloaded replay
	//passes before it plays. Matching is exact on the movie's own ROM SHA-1,
	//never by title: a replay on the wrong ROM desyncs at once (§7).
	public class CommunityReplayCatalogTests
	{
		private const string ContraSha1 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
		private const string OtherSha1 = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
		private const string GbSha1 = "1111111111111111111111111111111111111111";
		private static readonly string Sha = new string('c', 64);

		private static string Row(int issue, int votes, string url = "https://github.com/user-attachments/files/1/run.mmo", string? sha256 = null, long size = 4096, string console = "nes", string cheats = "[]")
		{
			return "{ \"issue\": " + issue + ", \"url\": \"" + url + "\", \"sha256\": \"" + (sha256 ?? Sha) + "\", \"size\": " + size
				+ ", \"console\": \"" + console + "\", \"game\": \"Contra (USA)\", \"author\": \"alice\", \"subtitle\": \"stage skip\", \"frames\": 3600"
				+ ", \"cheats\": " + cheats + ", \"votes\": " + votes + " }";
		}

		private static string Json => "{ \"format\": \"mesenai-community-replays\", \"version\": 1, \"repository\": \"sbihaiko/MesenAI\", \"games\": ["
			+ "{ \"sha1\": \"" + GbSha1 + "\", \"name\": \"GB\", \"replays\": [" + Row(30, 1, console: "gb") + "] },"
			+ "{ \"sha1\": \"" + ContraSha1.ToLowerInvariant() + "\", \"name\": \"Contra (USA)\", \"replays\": ["
			+ Row(11, 2, cheats: "[{ \"type\": \"NesCustom\", \"code\": \"0032:05\" }]") + ","
			+ Row(12, 7) + ","
			+ Row(10, 2) + ","
			+ Row(0, 9) + ","
			+ Row(13, 9, url: "https://example.com/user-attachments/files/1/run.mmo") + ","
			+ Row(14, 9, url: "http://github.com/user-attachments/files/1/run.mmo") + ","
			+ Row(15, 9, sha256: "xyz") + ","
			+ Row(16, 9, size: 9 * 1024 * 1024) + ","
			+ Row(17, 9, size: 0)
			+ "] },"
			+ "{ \"sha1\": \"not-a-sha1\", \"name\": \"broken\", \"replays\": [] }"
			+ "] }";

		private static IReadOnlyList<CommunityReplayGame> Catalog => CommunityReplayCatalog.Parse(Json)!;

		[Fact]
		public void Parse_reads_rows_and_skips_the_malformed_ones()
		{
			IReadOnlyList<CommunityReplayGame> games = Catalog;
			Assert.Equal(new[] { GbSha1, ContraSha1 }, games.Select(g => g.Sha1));
			CommunityReplayGame contra = games[1];
			//issue 0, a host off the attachment URL shape, http, a bad sha256 and
			//a size off the 8 MB cap are dropped one by one.
			Assert.Equal(new[] { 11, 12, 10 }, contra.Replays.Select(r => r.Issue));
			CommunityReplay first = contra.Replays[0];
			Assert.Equal("https://github.com/user-attachments/files/1/run.mmo", first.Url);
			Assert.Equal(Sha, first.Sha256);
			Assert.Equal(4096, first.Size);
			Assert.Equal("nes", first.Console);
			Assert.Equal("alice", first.Author);
			Assert.Equal("stage skip", first.Subtitle);
			Assert.Equal(3600, first.Frames);
			Assert.Equal(new[] { new CommunityReplayCheat("NesCustom", "0032:05") }, first.Cheats);
		}

		[Theory]
		[InlineData(null)]
		[InlineData("")]
		[InlineData("not json")]
		[InlineData("{ \"format\": \"mesenai-community-cheats\", \"games\": [] }")]
		[InlineData("{ \"format\": \"mesenai-community-replays\" }")]
		public void A_body_that_is_not_this_catalog_is_null_never_an_empty_list(string? json)
		{
			Assert.Null(CommunityReplayCatalog.Parse(json));
		}

		[Fact]
		public void The_committed_empty_catalog_parses_to_no_game()
		{
			Assert.Empty(CommunityReplayCatalog.Parse("{\n  \"format\": \"mesenai-community-replays\",\n  \"version\": 1,\n  \"repository\": \"sbihaiko/MesenAI\",\n  \"games\": []\n}\n")!);
		}

		[Fact]
		public void The_loaded_rom_lists_its_rows_most_voted_first_a_tie_by_earlier_issue()
		{
			IReadOnlyList<CommunityReplay> rows = CommunityReplayCatalog.ForRom(Catalog, ContraSha1.ToLowerInvariant(), ConsoleType.Nes);
			Assert.Equal(new[] { 12, 10, 11 }, rows.Select(r => r.Issue));
		}

		[Fact]
		public void Another_rom_lists_nothing_and_a_title_never_matches()
		{
			Assert.Empty(CommunityReplayCatalog.ForRom(Catalog, OtherSha1, ConsoleType.Nes));
			Assert.Empty(CommunityReplayCatalog.ForRom(Catalog, "", ConsoleType.Nes));
			Assert.Empty(CommunityReplayCatalog.ForRom(Catalog, "Contra (USA)", ConsoleType.Nes));
			//The same hash on another core is not this movie's console.
			Assert.Empty(CommunityReplayCatalog.ForRom(Catalog, ContraSha1, ConsoleType.Gameboy));
			Assert.Single(CommunityReplayCatalog.ForRom(Catalog, GbSha1, ConsoleType.Gameboy));
		}

		[Fact]
		public void A_closed_issue_is_gone_once_the_catalog_is_regenerated_without_it()
		{
			string regenerated = Json.Replace(Row(12, 7) + ",", "");
			IReadOnlyList<CommunityReplay> rows = CommunityReplayCatalog.ForRom(CommunityReplayCatalog.Parse(regenerated)!, ContraSha1, ConsoleType.Nes);
			Assert.DoesNotContain(rows, r => r.Issue == 12);
		}

		[Fact]
		public void The_vote_count_opens_the_issue()
		{
			Assert.Equal("👍 7 ↗", CommunityReplayCatalog.VotesLabel(7));
			Assert.Equal("https://github.com/sbihaiko/MesenAI/issues/12", CommunityReplayCatalog.IssueUrl(12));
			Assert.Equal("https://raw.githubusercontent.com/sbihaiko/MesenAI/main/docs/community-replays.json", CommunityReplayCatalog.CatalogUrl);
		}

		[Fact]
		public void A_download_plays_only_when_its_size_and_sha256_match_the_row()
		{
			byte[] data = Encoding.UTF8.GetBytes("PK fake replay bytes");
			string sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
			CommunityReplay row = new(1, "https://github.com/user-attachments/files/1/a.mmo", sha, data.Length, "nes", "g", "a", "s", 1, Array.Empty<CommunityReplayCheat>(), 0);
			Assert.True(CommunityReplayCatalog.IsVerified(data, row));
			Assert.True(CommunityReplayCatalog.IsVerified(data, row with { Sha256 = sha.ToUpperInvariant() }));
			Assert.False(CommunityReplayCatalog.IsVerified(data, row with { Sha256 = Sha }));
			Assert.False(CommunityReplayCatalog.IsVerified(data, row with { Size = data.Length + 1 }));
			Assert.False(CommunityReplayCatalog.IsVerified(new byte[CommunityReplayCatalog.MaxArchiveBytes + 1], row));
			Assert.Equal(sha + ".mmo", CommunityReplayCatalog.CacheFileName(row));
		}

		[Fact]
		public void The_cap_and_console_keys_mirror_the_ci_gate()
		{
			//scripts/replay_lint.py MAX_ARCHIVE_BYTES and CONSOLES.
			Assert.Equal(8 * 1024 * 1024, CommunityReplayCatalog.MaxArchiveBytes);
			Assert.Equal("nes", CommunityReplayCatalog.ConsoleKey(ConsoleType.Nes));
			Assert.Equal("gb", CommunityReplayCatalog.ConsoleKey(ConsoleType.Gameboy));
			Assert.Equal("sms", CommunityReplayCatalog.ConsoleKey(ConsoleType.Sms));
			Assert.Null(CommunityReplayCatalog.ConsoleKey(ConsoleType.Snes));
		}
	}
}
