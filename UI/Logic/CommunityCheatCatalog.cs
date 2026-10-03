using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mesen.Interop;

namespace Mesen.Logic
{
	//One row of docs/community-cheats.json: one `[Cheat]` submission issue.
	//Console is the catalog's key ("nes", "gb", "gbc", "sms"); Code is one
	//effect, its parts joined with `+`.
	public sealed record CommunityCheat(int Issue, string Console, string Code, string Description, int Votes);

	//One game of the catalog: every live row for one cheat SHA-1.
	public sealed record CommunityCheatGame(string Sha1, string Name, IReadOnlyList<CommunityCheat> Cheats);

	//R.4 (ADR-0248 §4-§5): the community cheat catalog as the client reads it.
	//The file is written by scripts/generate_community_cheat_catalog.py from the
	//open `cheat:valid` issues; UI/Services/CommunityCheatCatalogFetcher fetches
	//it (a plain GET, MEI §4) and this class parses it and picks the rows for
	//the loaded copy. Matching is exact on the cheat SHA-1, never by name: a
	//code applied to the wrong copy can crash the game (ADR-0205 §7).
	//Host-free (UI/Logic firewall, ADR-0123): JsonDocument only, so the parse
	//needs no serializer context and is AOT-safe.
	public static class CommunityCheatCatalog
	{
		public const string Format = "mesenai-community-cheats";
		public const string CatalogUrl = "https://raw.githubusercontent.com/" + ReplayShare.Repository + "/main/docs/community-cheats.json";
		public const string CommunityMark = "from the community ·";

		//The count is the row's link to vote on the issue; ↗ says it leaves the app.
		public static string VotesLabel(int votes) => "👍 " + votes + " ↗";

		public static string IssueUrl(int issue) => "https://github.com/" + ReplayShare.Repository + "/issues/" + issue;

		//The games of a catalog body, or null when the body is not this catalog
		//(not JSON, another format, no games array) - never an empty list for a
		//body that failed, so a bad fetch cannot hide a cached catalog. Entries
		//that are malformed are skipped one by one.
		public static IReadOnlyList<CommunityCheatGame>? Parse(string? json)
		{
			if(string.IsNullOrWhiteSpace(json)) {
				return null;
			}
			try {
				using JsonDocument doc = JsonDocument.Parse(json);
				JsonElement root = doc.RootElement;
				if(root.ValueKind != JsonValueKind.Object
					|| !root.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String || format.GetString() != Format
					|| !root.TryGetProperty("games", out JsonElement games) || games.ValueKind != JsonValueKind.Array) {
					return null;
				}
				List<CommunityCheatGame> result = new();
				foreach(JsonElement game in games.EnumerateArray()) {
					string sha1 = Text(game, "sha1").ToUpperInvariant();
					if(!IsSha1(sha1)) {
						continue;
					}
					List<CommunityCheat> cheats = new();
					if(game.TryGetProperty("cheats", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array) {
						foreach(JsonElement row in rows.EnumerateArray()) {
							int issue = Number(row, "issue");
							string code = Text(row, "code").Trim();
							if(issue <= 0 || code.Length == 0) {
								continue;
							}
							cheats.Add(new CommunityCheat(issue, Text(row, "console").Trim().ToLowerInvariant(), code, Text(row, "description").Trim(), Math.Max(0, Number(row, "votes"))));
						}
					}
					result.Add(new CommunityCheatGame(sha1, Text(game, "name"), cheats));
				}
				return result;
			} catch(JsonException) {
				return null;
			}
		}

		//The rows for the loaded copy: exact SHA-1 match and the same console,
		//most-👍-first, a tie keeping the earlier issue first.
		public static IReadOnlyList<CommunityCheat> ForCopy(IReadOnlyList<CommunityCheatGame> games, string cheatSha1, ConsoleType console)
		{
			string sha1 = (cheatSha1 ?? "").Trim().ToUpperInvariant();
			if(!IsSha1(sha1)) {
				return Array.Empty<CommunityCheat>();
			}
			return games.Where(g => g.Sha1 == sha1)
				.SelectMany(g => g.Cheats)
				.Where(c => IsConsole(c.Console, console))
				.OrderByDescending(c => c.Votes)
				.ThenBy(c => c.Issue)
				.ToList();
		}

		//The catalog's console keys (cheat_submission.CONSOLES) per core: GBC
		//runs on the Game Boy core, Game Gear on the SMS one.
		public static bool IsConsole(string key, ConsoleType console)
		{
			return console switch {
				ConsoleType.Nes => key == "nes",
				ConsoleType.Gameboy => key == "gb" || key == "gbc",
				ConsoleType.Sms => key == "sms",
				_ => false
			};
		}

		public static bool IsSha1(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

		private static string Text(JsonElement e, string name)
		{
			return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
		}

		private static int Number(JsonElement e, string name)
		{
			return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;
		}
	}
}
