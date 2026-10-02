using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Cheats
{
	//R.4 (ADR-0248 §4-§5): docs/community-cheats.json as the client reads it,
	//and the community rows it adds to W-P11 below the bundled list. Matching
	//is exact on the cheat SHA-1, never by name: a code on the wrong copy can
	//crash the game. ADR-0245 Decision 3 applies to these rows unchanged.
	public class CommunityCheatCatalogTests
	{
		private const string ContraSha1 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
		private const string OtherSha1 = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
		private const string GbSha1 = "1111111111111111111111111111111111111111";

		private const string Json = @"{
  ""format"": ""mesenai-community-cheats"",
  ""version"": 1,
  ""repository"": ""sbihaiko/MesenAI"",
  ""games"": [
    { ""sha1"": ""1111111111111111111111111111111111111111"", ""name"": ""Test GB game"", ""cheats"": [
      { ""issue"": 204, ""console"": ""gb"", ""code"": ""01FF16D0"", ""description"": ""Infinite health"", ""votes"": 1 } ] },
    { ""sha1"": ""AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"", ""name"": ""Contra (USA)"", ""cheats"": [
      { ""issue"": 201, ""console"": ""nes"", ""code"": ""0436:09"", ""description"": ""Start with 9 lives"", ""votes"": 2 },
      { ""issue"": 202, ""console"": ""nes"", ""code"": ""SXIOPO+AAAAAA"", ""description"": ""Infinite energy"", ""votes"": 7 },
      { ""issue"": 0, ""console"": ""nes"", ""code"": ""0437:09"", ""description"": ""no issue number"", ""votes"": 3 },
      { ""issue"": 209, ""console"": ""nes"", ""code"": """", ""description"": ""no code"", ""votes"": 3 } ] },
    { ""sha1"": ""not-a-sha1"", ""name"": ""broken"", ""cheats"": [] }
  ]
}";

		private static readonly CheatDbGame Contra = new("Contra (USA)", ContraSha1, new[] {
			new CheatDbCode("Infinite lives - 1P game", "SZKGPAVG"),
		});

		private static IReadOnlyList<CommunityCheatGame> Catalog => CommunityCheatCatalog.Parse(Json)!;

		[Fact]
		public void The_catalog_parses_its_games_and_skips_malformed_entries()
		{
			IReadOnlyList<CommunityCheatGame> games = Catalog;
			Assert.Equal(new[] { GbSha1, ContraSha1 }, games.Select(g => g.Sha1));
			Assert.Equal(new[] { 201, 202 }, games[1].Cheats.Select(c => c.Issue));
			Assert.Equal(new CommunityCheat(201, "nes", "0436:09", "Start with 9 lives", 2), games[1].Cheats[0]);
		}

		[Fact]
		public void A_body_that_is_not_this_catalog_is_refused_rather_than_read_as_empty()
		{
			Assert.Null(CommunityCheatCatalog.Parse(null));
			Assert.Null(CommunityCheatCatalog.Parse(""));
			Assert.Null(CommunityCheatCatalog.Parse("<html>rate limited</html>"));
			Assert.Null(CommunityCheatCatalog.Parse(@"{""format"":""something-else"",""games"":[]}"));
			Assert.Null(CommunityCheatCatalog.Parse(@"{""format"":""mesenai-community-cheats"",""games"":null}"));
			Assert.Empty(CommunityCheatCatalog.Parse(@"{""format"":""mesenai-community-cheats"",""version"":1,""games"":[]}")!);
		}

		[Fact]
		public void Rows_for_a_copy_match_its_cheat_sha1_exactly_most_voted_first()
		{
			IReadOnlyList<CommunityCheat> rows = CommunityCheatCatalog.ForCopy(Catalog, ContraSha1.ToLowerInvariant(), ConsoleType.Nes);
			Assert.Equal(new[] { 202, 201 }, rows.Select(r => r.Issue));
			Assert.Empty(CommunityCheatCatalog.ForCopy(Catalog, OtherSha1, ConsoleType.Nes));
			Assert.Empty(CommunityCheatCatalog.ForCopy(Catalog, "", ConsoleType.Nes));
			//Never by a near miss: a prefix of the hash is another copy.
			Assert.Empty(CommunityCheatCatalog.ForCopy(Catalog, ContraSha1.Substring(0, 39), ConsoleType.Nes));
		}

		[Fact]
		public void A_row_filed_under_another_console_is_not_offered()
		{
			Assert.Empty(CommunityCheatCatalog.ForCopy(Catalog, ContraSha1, ConsoleType.Gameboy));
			Assert.Single(CommunityCheatCatalog.ForCopy(Catalog, GbSha1, ConsoleType.Gameboy));
		}

		[Fact]
		public void Community_rows_come_below_the_bundled_list_marked_with_their_votes()
		{
			IReadOnlyList<CommunityCheat> community = CommunityCheatCatalog.ForCopy(Catalog, ContraSha1, ConsoleType.Nes);
			IReadOnlyList<CheatSheetRow> rows = CheatSheet.BuildRows(ConsoleType.Nes, Contra, false, Array.Empty<StoredCheat>(), false, "", community);

			Assert.Equal(new[] { "Infinite lives - 1P game", "Infinite energy", "Start with 9 lives" }, rows.Select(r => r.Description));
			Assert.Equal(new[] { CheatRowSource.ThisCopy, CheatRowSource.Community, CheatRowSource.Community }, rows.Select(r => r.Source));
			Assert.Equal(202, rows[1].Issue);
			Assert.Equal(7, rows[1].Votes);
			Assert.Equal(CheatType.NesGameGenie, rows[1].Type);
			Assert.Equal("SXIOPO" + Environment.NewLine + "AAAAAA", rows[1].Codes);
			Assert.Equal(CheatType.NesCustom, rows[2].Type);
			//The row reads "from the community · 👍 7"; the count is the button that opens the issue (↗).
			Assert.Equal("from the community ·", CommunityCheatCatalog.CommunityMark);
			Assert.Equal("👍 7 ↗", CommunityCheatCatalog.VotesLabel(rows[1].Votes));
		}

		[Fact]
		public void Toggling_a_community_row_stores_it_and_it_stays_a_community_row()
		{
			IReadOnlyList<CommunityCheat> community = CommunityCheatCatalog.ForCopy(Catalog, ContraSha1, ConsoleType.Nes);
			CheatSheetRow row = CheatSheet.BuildRows(ConsoleType.Nes, Contra, false, Array.Empty<StoredCheat>(), false, "", community)[2];

			IReadOnlyList<StoredCheat> stored = CheatSheet.Toggle(Array.Empty<StoredCheat>(), row, false);
			StoredCheat on = Assert.Single(stored);
			Assert.Equal(new StoredCheat("Start with 9 lives", CheatType.NesCustom, "0436:09", true), on);

			IReadOnlyList<CheatSheetRow> again = CheatSheet.BuildRows(ConsoleType.Nes, Contra, false, stored, false, "", community);
			Assert.Equal(3, again.Count);
			Assert.True(again[2].IsOn);
			Assert.Equal(CheatRowSource.Community, again[2].Source);
		}

		//ADR-0245 Decision 3, unchanged: in Remaster a Game Genie row is disabled
		//with its reason, a RAM code stays available.
		[Fact]
		public void While_recording_art_a_community_game_genie_row_is_refused_with_its_reason()
		{
			IReadOnlyList<CommunityCheat> community = CommunityCheatCatalog.ForCopy(Catalog, ContraSha1, ConsoleType.Nes);
			IReadOnlyList<CheatSheetRow> rows = CheatSheet.BuildRows(ConsoleType.Nes, Contra, false, Array.Empty<StoredCheat>(), true, "", community);

			Assert.False(rows[1].CanToggle);
			Assert.Equal(CheatRecordingRule.RefusedReason, rows[1].Note);
			Assert.True(rows[2].CanToggle);
			Assert.Equal(CheatRecordingRule.AllowedNote, rows[2].Note);
		}

		//GB/SMS gain a list this way, and the "no list yet" line shows only when
		//there is none.
		[Fact]
		public void On_game_boy_community_rows_appear_and_the_no_list_line_goes_away()
		{
			IReadOnlyList<CommunityCheat> community = CommunityCheatCatalog.ForCopy(Catalog, GbSha1, ConsoleType.Gameboy);
			CheatSheetRow row = Assert.Single(CheatSheet.BuildRows(ConsoleType.Gameboy, null, false, Array.Empty<StoredCheat>(), false, "", community));
			Assert.Equal(CheatType.GbGameShark, row.Type);
			Assert.Equal(CheatRowSource.Community, row.Source);

			Assert.Equal(CheatConsoleScope.NoListReason, CheatSheet.StatusLine(ConsoleType.Gameboy, null, false, 0, 0));
			Assert.Equal("0 on · " + CheatSheet.CommunityOnlyLine, CheatSheet.StatusLine(ConsoleType.Gameboy, null, false, 0, 1));
			Assert.Equal(CheatSheet.NotInListLine, CheatSheet.StatusLine(ConsoleType.Nes, null, false, 0, 0));
			Assert.Equal("1 on · " + CheatSheet.CommunityOnlyLine, CheatSheet.StatusLine(ConsoleType.Nes, null, false, 1, 2));
		}

		[Fact]
		public void A_community_code_the_console_cannot_store_is_skipped()
		{
			CommunityCheat mixed = new(300, "nes", "SXIOPO+0436:09", "mixed types", 1);
			CommunityCheat bad = new(301, "nes", "ZZZZ", "not a code", 1);
			Assert.Empty(CheatSheet.BuildRows(ConsoleType.Nes, null, false, Array.Empty<StoredCheat>(), false, "", new[] { mixed, bad }));
		}

		[Fact]
		public void The_vote_count_opens_the_submission_issue()
		{
			Uri url = new(CommunityCheatCatalog.IssueUrl(202));
			Assert.Equal("https://github.com/sbihaiko/MesenAI/issues/202", url.AbsoluteUri);
			Assert.Equal("https://raw.githubusercontent.com/sbihaiko/MesenAI/main/docs/community-cheats.json", CommunityCheatCatalog.CatalogUrl);
		}
	}
}
