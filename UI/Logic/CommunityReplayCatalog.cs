using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Mesen.Interop;

namespace Mesen.Logic
{
	//One `Cheat <Type> <Code>` line of the replay's GameSettings.txt, verbatim.
	public sealed record CommunityReplayCheat(string Type, string Code);

	//One row of docs/community-replays.json: one `[Replay]` submission issue
	//(the row key, ADR-0205 §7). Url is the attachment's stable
	//github.com/user-attachments address, never the signed redirect target
	//(§6); Sha256 and Size are the bytes the catalog generator downloaded and
	//gated, which a download must match before it plays.
	public sealed record CommunityReplay(int Issue, string Url, string Sha256, long Size, string Console, string Game, string Author, string Subtitle, int Frames, IReadOnlyList<CommunityReplayCheat> Cheats, int Votes);

	//Every live row for one ROM SHA-1 - the hash the .mmo carries.
	public sealed record CommunityReplayGame(string Sha1, string Name, IReadOnlyList<CommunityReplay> Replays);

	//R.2 (ADR-0205 §7-§9): the shared-replay catalog as the client reads it.
	//The file is written by scripts/generate_community_replay_catalog.py from
	//the open `replay:valid` issues without `replay:removed`, each attachment
	//re-checked by the §8 gate; UI/Services/CommunityReplayCatalogFetcher
	//fetches it (a plain GET, MEI §4) and this class parses it, picks the rows
	//for the loaded ROM and checks a downloaded archive against its row.
	//Matching is exact on the ROM SHA-1 the movie itself carries
	//(GameSettings.txt `SHA1`, i.e. EmuApi.GetRomHash(HashType.Sha1) for the
	//ROM as loaded, before a patch applies), never by title: a replay on the
	//wrong ROM desyncs at once (§7). Host-free (UI/Logic firewall, ADR-0123).
	public static class CommunityReplayCatalog
	{
		public const string Format = "mesenai-community-replays";
		public const string CatalogUrl = "https://raw.githubusercontent.com/" + ReplayShare.Repository + "/main/docs/community-replays.json";

		//ADR-0205 §3: the one cap, mirrored from scripts/replay_lint.py
		//MAX_ARCHIVE_BYTES; the client enforces it before a byte is inflated.
		public const int MaxArchiveBytes = 8 * 1024 * 1024;

		//§6: the catalog only ever points at an issue attachment.
		private const string AttachmentPrefix = "https://github.com/user-attachments/";

		public static string VotesLabel(int votes) => "👍 " + votes + " ↗";

		public static string IssueUrl(int issue) => "https://github.com/" + ReplayShare.Repository + "/issues/" + issue;

		//The verified copy in the hash-keyed downloads/ cache.
		public static string CacheFileName(CommunityReplay row) => row.Sha256.ToLowerInvariant() + ".mmo";

		//scripts/replay_lint.py CONSOLES: the consoles the pipeline lists replays
		//for (GBC runs on the Game Boy core). Null for any other.
		public static string? ConsoleKey(ConsoleType console)
		{
			return console switch {
				ConsoleType.Nes => "nes",
				ConsoleType.Gameboy => "gb",
				ConsoleType.Sms => "sms",
				_ => null
			};
		}

		//The games of a catalog body, or null when the body is not this catalog -
		//never an empty list for a body that failed, so a bad fetch cannot hide a
		//cached catalog. Malformed rows are skipped one by one.
		public static IReadOnlyList<CommunityReplayGame>? Parse(string? json)
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
				List<CommunityReplayGame> result = new();
				foreach(JsonElement game in games.EnumerateArray()) {
					string sha1 = Text(game, "sha1").ToUpperInvariant();
					if(!IsHex(sha1, 40)) {
						continue;
					}
					List<CommunityReplay> replays = new();
					if(game.TryGetProperty("replays", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array) {
						foreach(JsonElement row in rows.EnumerateArray()) {
							CommunityReplay? replay = ParseRow(row);
							if(replay != null) {
								replays.Add(replay);
							}
						}
					}
					result.Add(new CommunityReplayGame(sha1, Text(game, "name"), replays));
				}
				return result;
			} catch(JsonException) {
				return null;
			}
		}

		private static CommunityReplay? ParseRow(JsonElement row)
		{
			int issue = (int)Number(row, "issue");
			string url = Text(row, "url").Trim();
			string sha256 = Text(row, "sha256").Trim().ToLowerInvariant();
			long size = Number(row, "size");
			if(issue <= 0 || !url.StartsWith(AttachmentPrefix, StringComparison.Ordinal) || url.Contains("..") || !IsHex(sha256, 64) || size <= 0 || size > MaxArchiveBytes) {
				return null;
			}
			List<CommunityReplayCheat> cheats = new();
			if(row.TryGetProperty("cheats", out JsonElement list) && list.ValueKind == JsonValueKind.Array) {
				foreach(JsonElement cheat in list.EnumerateArray()) {
					string type = Text(cheat, "type").Trim();
					string code = Text(cheat, "code").Trim();
					if(type.Length > 0 && code.Length > 0) {
						cheats.Add(new CommunityReplayCheat(type, code));
					}
				}
			}
			return new CommunityReplay(issue, url, sha256, size, Text(row, "console").Trim().ToLowerInvariant(), Text(row, "game"),
				Text(row, "author"), Text(row, "subtitle"), (int)Math.Max(0, Number(row, "frames")), cheats, (int)Math.Max(0, Number(row, "votes")));
		}

		//The rows for the loaded ROM: exact SHA-1 and the same console,
		//most-👍-first, a tie keeping the earlier issue first.
		public static IReadOnlyList<CommunityReplay> ForRom(IReadOnlyList<CommunityReplayGame> games, string romSha1, ConsoleType console)
		{
			string sha1 = (romSha1 ?? "").Trim().ToUpperInvariant();
			string? key = ConsoleKey(console);
			if(!IsHex(sha1, 40) || key == null) {
				return Array.Empty<CommunityReplay>();
			}
			return games.Where(g => g.Sha1 == sha1)
				.SelectMany(g => g.Replays)
				.Where(r => r.Console == key)
				.OrderByDescending(r => r.Votes)
				.ThenBy(r => r.Issue)
				.ToList();
		}

		//MEI §3 item 1: the artifact is verified before it is used - its size
		//and sha256 are the row's, and it is within the §3 cap.
		public static bool IsVerified(byte[] data, CommunityReplay row)
		{
			if(data.Length > MaxArchiveBytes || data.LongLength != row.Size) {
				return false;
			}
			string sha = Convert.ToHexString(SHA256.HashData(data));
			return string.Equals(sha, row.Sha256, StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsHex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);

		private static string Text(JsonElement e, string name)
		{
			return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
		}

		private static long Number(JsonElement e, string name)
		{
			return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;
		}
	}
}
