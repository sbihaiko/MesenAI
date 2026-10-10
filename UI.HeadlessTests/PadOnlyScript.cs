using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Mesen.HeadlessTests;

//#1242 acceptance criterion 3, and the review of PR #1251 on it: a case that
//claims a step of the pad-only script ran has to take the step's own values -
//the check it waits on, the timeout it waits in, the button and tick count it
//presses, the precondition it assumes - from the committed script. A case that
//spells them out in C# keeps passing after the script drifts, so the link
//between "the script ran" and "the case is green" is asserted, not measured.
//
//The shapes below are the runner's, read off `squad/gui_test_supervise.py`, so
//the case and the runner disagree about nothing:
//
//- a precondition is a check only when it has ` == ` in it; anything else is a
//  note for a person (`by hand: ...`), which is what a manual step carries;
//- a wait is `wait.check` for `wait.timeout_ticks`;
//- an action is `action.name` + `action.args`, injected as the runner injects
//  it;
//- the step's own verdict is `check.name` + `check.args.is`.
internal static class PadOnlyScript
{
	public const string RelativePath = "docs/validation/process/play-pad-only.gui-test.json";

	private static readonly Lazy<Dictionary<string, PadOnlyStep>> _steps = new(ReadAll);

	//The step, by the id the script gives it. A renamed or dropped step throws
	//here, so a drift in the script's ids fails the case that drives it.
	public static PadOnlyStep Step(string id)
	{
		if(!_steps.Value.TryGetValue(id, out PadOnlyStep? step)) {
			throw new InvalidOperationException($"{RelativePath} has no step {id}; the case drives the script's own steps, never a copy of them");
		}
		return step;
	}

	//One comparison, the way the runner splits it: everything before the first
	//` == ` is the check's name, the rest is the value it wants. No separator
	//means the text is not a check at all.
	public static (string Name, string Value)? Comparison(string? text)
	{
		int at = (text ?? "").IndexOf(" == ", StringComparison.Ordinal);
		return at < 0 ? null : (text![..at], text[(at + 4)..]);
	}

	//The hook state field a check reads. The state names `focus`, not `focused`,
	//and a check the state cannot answer is refused here rather than asserted
	//against nothing.
	public static string StateField(string check) => check switch
	{
		"ui.focused" => "focus",
		"ui.screen" => "screen",
		_ => throw new InvalidOperationException($"the hook's state reads no field for the check {check}")
	};

	private static Dictionary<string, PadOnlyStep> ReadAll()
	{
		JsonObject script = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRepoRoot(), RelativePath)))!.AsObject();
		Dictionary<string, PadOnlyStep> steps = new(StringComparer.Ordinal);
		foreach(JsonNode? batch in script["batches"]!.AsArray()) {
			foreach(string group in new[] { "setup", "steps", "teardown" }) {
				if(batch![group] is not JsonArray nodes) {
					continue;
				}
				foreach(JsonNode? node in nodes) {
					PadOnlyStep step = ToStep(node!.AsObject());
					steps[step.Id] = step;
				}
			}
		}
		return steps;
	}

	private static PadOnlyStep ToStep(JsonObject step)
	{
		List<PadOnlyAction> actions = new();
		if(step["action"] is JsonObject action) {
			actions.Add(new PadOnlyAction(action["name"]!.GetValue<string>(), action["args"]?.AsObject() ?? new JsonObject()));
		}
		JsonObject? wait = step["wait"] as JsonObject;
		return new PadOnlyStep {
			Id = step["id"]!.GetValue<string>(),
			Mode = step["mode"]!.GetValue<string>(),
			Precondition = step["precondition"]?.GetValue<string>() ?? "",
			WaitCheck = wait?["check"]?.GetValue<string>(),
			WaitTimeoutTicks = wait?["timeout_ticks"]?.GetValue<int>() ?? 0,
			Check = step["check"] is JsonObject check
				? check["name"]!.GetValue<string>() + ((check["args"] as JsonObject)?["is"] is JsonValue expected ? " == " + expected.GetValue<string>() : "")
				: null,
			Actions = actions,
		};
	}

	private static string FindRepoRoot()
	{
		for(DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent) {
			if(File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
				return dir.FullName;
			}
		}
		throw new InvalidOperationException("Could not locate the repo root (Mesen.sln) above " + AppContext.BaseDirectory);
	}
}

//One step of the script, carrying only the values a case has to take from it.
internal sealed class PadOnlyStep
{
	public required string Id { get; init; }
	public required string Mode { get; init; }
	public required string Precondition { get; init; }
	public string? WaitCheck { get; init; }
	public int WaitTimeoutTicks { get; init; }
	public string? Check { get; init; }
	public required IReadOnlyList<PadOnlyAction> Actions { get; init; }
}

//`action.name` + `action.args`, exactly as the script spells them.
internal sealed record PadOnlyAction(string Name, JsonObject Args);
