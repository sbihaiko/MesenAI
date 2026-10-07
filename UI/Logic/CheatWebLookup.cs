using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mesen.Logic;

//How the web lookup's check came out for one code. Only Passed is ever shown;
//every other state - a failed check, a code the check never ran on, or an
//answer the client cannot read - fails closed.
public enum WebCheckState
{
	Unknown,
	Unchecked,
	Failed,
	Passed
}

//One code the web lookup found for the loaded copy: the page's own text for
//the description and the code, and how its check came out.
public sealed record WebFoundCode(string Description, string Code, WebCheckState Check);

//The web lookup for the loaded copy. The real one runs the external script
//(CheatWebLookupScriptChecker); tests stand a fake in for it.
public interface ICheatWebChecker
{
	Task<IReadOnlyList<WebFoundCode>> LookUpAsync(string romPath, string gameName);
}

//A run of the web lookup that did not get to check anything: the script was
//missing, refused the request, could not read the page or could not run the
//check. The sheet says so (FailedLine) rather than "nothing passed".
public sealed class CheatWebLookupException : Exception
{
	public CheatWebLookupException(string message) : base(message) { }
}

//One finished run of scripts/cheat_web_lookup.py: its exit code and the JSON
//object it printed on stdout.
public sealed record CheatWebRun(int ExitCode, string Stdout);

//P.12 (ADR-0245 §4, second bullet; #924): the client half of the checked web
//lookup. The lookup and its check run in scripts/cheat_web_lookup.py, never in
//the client (ADR-0247); this only decides which of its codes the W-P11 sheet
//may offer. What "checked" means is the script's rule (pending #934); the
//client adds none of its own and only refuses what is not a pass.
public static class CheatWebLookup
{
	//The script's LABEL, word for word: it is the row's mark.
	public const string Label = "found online, checked on your copy";
	public const string Schema = "mesence.cheat-web-lookup/1";
	public const string Script = "cheat_web_lookup.py";

	public const string LookOnlineLabel = "Look Online";
	public const string SearchingLine = "Looking online and checking each code on your copy…";
	public const string NoneLine = "No code found online passed the check on your copy.";
	//The run itself failed (the script was missing, refused the ROM, could not
	//read the page or could not run the check): nothing was checked, so this is
	//never NoneLine.
	public const string FailedLine = "The online lookup could not check codes on your copy this time.";
	public const string NeedsToolsLine = "Looking online needs python3 and the MesenCE tools (Remaster › Setup).";

	//The arguments after the script: the ROM by path (read by the script on
	//this machine, never uploaded) and the name to look up.
	public static IReadOnlyList<string> Arguments(string romPath, string gameName)
	{
		return new[] { "--rom", romPath, "--game", gameName };
	}

	//The script's stdout as codes. It prints only codes that passed, each with
	//Label; a code without that exact label, an unknown schema or text that is
	//not the script's JSON reads as nothing passed.
	public static IReadOnlyList<WebFoundCode> ParseOutput(string stdout)
	{
		List<WebFoundCode> codes = new();
		try {
			using JsonDocument doc = JsonDocument.Parse(stdout);
			JsonElement root = doc.RootElement;
			if(root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("schema", out JsonElement schema) || schema.ValueKind != JsonValueKind.String || schema.GetString() != Schema
				|| !root.TryGetProperty("codes", out JsonElement list) || list.ValueKind != JsonValueKind.Array) {
				return codes;
			}
			foreach(JsonElement item in list.EnumerateArray()) {
				string code = Text(item, "code");
				string desc = Text(item, "desc");
				if(code.Length == 0 || desc.Length == 0) {
					continue;
				}
				codes.Add(new WebFoundCode(desc, code, Text(item, "label") == Label ? WebCheckState.Passed : WebCheckState.Unknown));
			}
		} catch(JsonException) {
			return Array.Empty<WebFoundCode>();
		}
		return codes;
	}

	private static string Text(JsonElement item, string name)
	{
		return item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()!.Trim()
			: "";
	}

	public static IReadOnlyList<WebFoundCode> Offered(IEnumerable<WebFoundCode> codes)
	{
		return codes.Where(c => c.Check == WebCheckState.Passed).ToList();
	}
}

//The real checker: the user's python3 runs scripts/cheat_web_lookup.py from
//the tools folder (ADR-0247: the script reads the pages and runs the check;
//the client calls no model). The script reads no key, so no key is handed to
//it. A run that exits with an error offers nothing.
public sealed class CheatWebLookupScriptChecker : ICheatWebChecker
{
	private readonly Func<IReadOnlyList<string>, Task<CheatWebRun>> _run;
	private readonly string _scriptsFolder;

	//run is handed the script's path and its arguments (the host prefixes python3).
	public CheatWebLookupScriptChecker(Func<IReadOnlyList<string>, Task<CheatWebRun>> run, string scriptsFolder)
	{
		_run = run;
		_scriptsFolder = scriptsFolder;
	}

	public async Task<IReadOnlyList<WebFoundCode>> LookUpAsync(string romPath, string gameName)
	{
		List<string> argv = new() { Path.Combine(_scriptsFolder, CheatWebLookup.Script) };
		argv.AddRange(CheatWebLookup.Arguments(romPath, gameName));
		CheatWebRun run = await _run(argv).ConfigureAwait(false);
		return run.ExitCode == 0 ? CheatWebLookup.ParseOutput(run.Stdout) : Array.Empty<WebFoundCode>();
	}
}
