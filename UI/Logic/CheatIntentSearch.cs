using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mesen.Logic;

//P.11 (ADR-0245 §4, #922): W-P11's search by intent. The client never calls a
//model (ADR-0247): scripts/cheat_intent.py does, started through an
//ICheatIntentRunner, and its answer is a closed choice over this game's
//database entries. The script already discards an answer outside the list;
//this checks it again against the entries the sheet shows, so an entry is only
//ever one of those, with the list's own text and code.
public enum CheatIntentStatus
{
	Match,
	None,
	Discarded,
	Failed
}

public sealed record CheatIntentOutcome(CheatIntentStatus Status, CheatDbCode? Entry, string Line);

public sealed record CheatIntentRun(int ExitCode, string Stdout);

//Starts scripts/cheat_intent.py with these arguments and returns what it
//printed. The real runner hands the key to the child through its environment
//only (CheatIntentScriptRunner, ByokJobLauncher); tests pass a fake.
public interface ICheatIntentRunner
{
	Task<CheatIntentRun> RunAsync(IReadOnlyList<string> arguments);
}

public static class CheatIntentSearch
{
	public const string Script = "cheat_intent.py";
	public const string NoneMatchedLine = "No cheat in this game's list does that — try the search box.";
	public const string FailedLine = "The search by intent did not answer. The search box still works.";
	public const int MaxIntentChars = 200;

	//#915 ruling: a backend is offered only when, on its own held-out numbers,
	//it scores >= 90 % correct and <= 5 % wrong entries. Jev passes
	//(docs/validation/measurements/p11-intent-search-2026-10-06.md); the local
	//qwen2.5:7b-instruct does not, so no local backend is offered.
	public static readonly IReadOnlyList<string> OfferedBackends = new[] { "jev" };

	//Jev is reached through OpenRouter under the user's own key (ADR-0242).
	public static ByokVendor Vendor => ByokVendor.OpenRouter;

	public static string MatchLine(CheatDbCode entry) => $"Best match: {entry.Description}";

	public static async Task<CheatIntentOutcome> SearchAsync(ICheatIntentRunner runner, CheatDbGame game, string intent)
	{
		string text = (intent ?? "").Trim();
		if(text.Length == 0) {
			return new CheatIntentOutcome(CheatIntentStatus.None, null, NoneMatchedLine);
		}
		if(text.Length > MaxIntentChars) {
			text = text.Substring(0, MaxIntentChars);
		}
		CheatIntentRun run = await runner.RunAsync(new[] { "--backend", OfferedBackends[0], "--sha1", game.Sha1, "--intent", text }).ConfigureAwait(false);
		return run.ExitCode == 0 ? Parse(run.Stdout, game.Cheats) : Failed();
	}

	//The script's one JSON object (schema mesence.cheat-intent/1) checked
	//against the listed entries: a match counts only when its index names a
	//listed entry with the same text and code.
	public static CheatIntentOutcome Parse(string stdout, IReadOnlyList<CheatDbCode> listed)
	{
		try {
			using JsonDocument doc = JsonDocument.Parse(stdout);
			JsonElement root = doc.RootElement;
			string status = root.TryGetProperty("status", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "";
			if(status == "none") {
				return new CheatIntentOutcome(CheatIntentStatus.None, null, NoneMatchedLine);
			}
			if(status == "match" && root.TryGetProperty("entry", out JsonElement entry) && entry.ValueKind == JsonValueKind.Object
				&& entry.TryGetProperty("index", out JsonElement i) && i.TryGetInt32(out int index)
				&& index >= 0 && index < listed.Count
				&& entry.TryGetProperty("desc", out JsonElement d) && d.GetString() == listed[index].Description
				&& entry.TryGetProperty("code", out JsonElement c) && c.GetString() == listed[index].Code) {
				return new CheatIntentOutcome(CheatIntentStatus.Match, listed[index], MatchLine(listed[index]));
			}
			return new CheatIntentOutcome(CheatIntentStatus.Discarded, null, NoneMatchedLine);
		} catch(JsonException) {
			return Failed();
		} catch(InvalidOperationException) {
			return Failed();
		}
	}

	private static CheatIntentOutcome Failed() => new(CheatIntentStatus.Failed, null, FailedLine);
}
