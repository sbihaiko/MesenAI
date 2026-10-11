using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Mesen.Logic.TestHook;

//One JSON object per line in, one per line out (the GUI test hook ADR, PR #1202,
//items 1-3). Errors are responses: a bad request must not cost the run, so
//nothing in Handle throws. The hook keeps no history.
public sealed class TestHookProtocol
{
	//Version 2 (#1779): the verbs a GUI run needs beside a single press - `pad.release`
	//and `key.press` were 2's floor, `pad.chord` and `nav.goal` are new, and `hello`
	//now names the actions it answers. A version-1 hook answered `pad.press`,
	//`pad.hold`, `pad.release`, `key.press` and the two pad hot-plugs and advertised
	//none of them, so a runner had to spend a step finding out.
	public const int Version = 2;
	private static readonly string[] Ops = { "hello", "state", "inject", "capture", "quit" };

	private readonly string _token;
	private readonly ITestHookTarget _target;
	private readonly TestHookKeys _keys;
	private readonly TestHookPads? _pads;
	private JsonNode? _lastCaptureId;
	private JsonObject? _lastCapture;

	//pads: the simulated hot-plug set (#1281, pad.connect / pad.disconnect). A hook
	//with no window behind it has no pads to plug, and those two actions then answer
	//an error rather than pretending - the protocol itself never plugs anything.
	public TestHookProtocol(string token, ITestHookTarget target, TestHookKeys keys, TestHookPads? pads = null)
	{
		_token = token;
		_target = target;
		_keys = keys;
		_pads = pads;
	}

	public string Handle(string line)
	{
		JsonNode? id = null;
		try {
			if(JsonNode.Parse(line) is not JsonObject request) {
				return Fail(null, "a request is one JSON object");
			}
			id = request["id"]?.DeepClone();
			string token = request["token"]?.GetValue<string>() ?? "";
			if(token.Length == 0 || token != _token) {
				return Fail(id, "unauthorized");
			}
			string op = request["op"]?.GetValue<string>() ?? "";
			JsonObject answer = op switch {
				"hello" => Hello(),
				"state" => State(),
				"inject" => Inject(request),
				"capture" => Capture(request),
				"quit" => Quit(),
				_ => throw new ArgumentException("unknown op " + op)
			};
			answer["id"] = id;
			answer["ok"] = true;
			return answer.ToJsonString();
		} catch(Exception ex) {
			return Fail(id, ex.Message);
		}
	}

	private static string Fail(JsonNode? id, string error)
	{
		return new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = error }.ToJsonString();
	}

	private static JsonObject Hello()
	{
		JsonArray ops = new();
		foreach(string op in Ops) {
			ops.Add((JsonNode?)JsonValue.Create(op));
		}
		//The namespaces an action can be written in: the ones ADR-0272 item 7 gives
		//this hook. `text.*` and `pointer.*` are the real-window mode's (step 7) and
		//the hook implements neither (#1281 review).
		//
		//`actions` (#1779) is the contract line a runner reads before it spends a step:
		//the inject actions this hook answers, sorted, so an old hook - which advertised
		//none - is told apart from this one by asking, not by failing a step. The five
		//verbs the pad-only batches need are all in it except the focus read, which is the
		//`state` op above: a read is not an action.
		List<string> names = new(Actions);
		names.Sort(StringComparer.Ordinal);
		JsonArray actions = new();
		foreach(string action in names) {
			actions.Add((JsonNode?)JsonValue.Create(action));
		}
		return new JsonObject {
			["hook"] = Version,
			["ops"] = ops,
			["namespaces"] = new JsonArray("pad", "key", "ui"),
			["actions"] = actions
		};
	}

	private JsonObject State()
	{
		JsonObject state = _target.State();
		state["tick"] = _keys.Tick;
		state["frames"] = _keys.Frames;
		return state;
	}

	//The input actions beside pad.press (#1281). `pad` on the wire is a device index
	//- 0 is the pad in the hand, 1 the second pad, ADR-0272 section 4's "device 0" -
	//and it is the index TestHookKeys takes too, so one number names one pad from a
	//script's step down to the backend's own "Pad1 A" lookup.
	private static readonly HashSet<string> Actions = new() { "pad.press", "pad.hold", "pad.release", "pad.chord", "key.press", "nav.goal", "pad.connect", "pad.disconnect" };

	private JsonObject Inject(JsonObject request)
	{
		string action = request["action"]?.GetValue<string>() ?? "";
		//A refused request is not a step: validate before Step(), which restarts the
		//since-last-step counts (#1282), so a retry after a refusal reads them intact.
		if(!Actions.Contains(action)) {
			throw new ArgumentException("unknown action " + action);
		}
		JsonObject args = request["args"]?.AsObject() ?? new JsonObject();
		int? ticks = args["ticks"]?.GetValue<int>();
		int? frames = args["frames"]?.GetValue<int>();
		int pad = args["pad"]?.GetValue<int>() ?? 0;
		if(pad < 0) {
			throw new ArgumentException("pad is a pad index: 0 is the pad in the hand, 1 the second pad");
		}
		//nav.goal is a read of the surface a move is chosen from, answered before the
		//step below: it injects nothing, so it may be asked between two moves without
		//restarting the counters a step's own check reads (#1282), and asking it is
		//never a move. The Choice stays at the runner (ADR-0271 item 6 A).
		if(action == "nav.goal") {
			return NavGoal(args, pad);
		}
		//#1282: the request that starts a step is the one that counts from now on.
		_target.Step();
		string button = args["button"]?.GetValue<string>() ?? "";
		string? error = action switch {
			"pad.press" => _keys.Press(pad, button, ticks, frames),
			//A hold takes the family the state allows and nothing else: a missing
			//duration, or one written in the family this state does not use, is what
			//HoldDown refuses (ADR-0272 item 4).
			"pad.hold" => _keys.HoldDown(pad, button, ticks, frames),
			"pad.release" => _keys.Release(pad, button),
			"pad.chord" => _keys.Chord(pad, Buttons(args), ticks, frames),
			"key.press" => _keys.PressKey(args["key"]?.GetValue<string>() ?? "", ticks, frames),
			"pad.connect" => Pads().Connect(args["index"]?.GetValue<int>() ?? -1, args["family"]?.GetValue<string>()),
			"pad.disconnect" => Pads().Disconnect(args["index"]?.GetValue<int>() ?? -1),
			_ => throw new ArgumentException("unknown action " + action)
		};
		if(error is not null) {
			throw new ArgumentException(error);
		}
		return new JsonObject();
	}

	//The buttons of a `pad.chord`, in the order the step wrote them. A `buttons` that is
	//not an array of names is no chord at all: Chord refuses anything under two, so an
	//empty list here is the step's own error rather than a press nobody asked for.
	private static string[] Buttons(JsonObject args)
	{
		if(args["buttons"] is not JsonArray list) {
			return Array.Empty<string>();
		}
		List<string> buttons = new();
		foreach(JsonNode? button in list) {
			if(button is not null) {
				buttons.Add(button.GetValue<string>());
			}
		}
		return buttons.ToArray();
	}

	//ADR-0271 items 1-3, on the hook's side of the decision: a step names a goal as a
	//check-vocabulary predicate and the runner asks one Choice per move. What the hook
	//owes that loop is the two things only it has - the UI state the choice is made
	//from (item 1: the snapshot, never a screenshot) and the moves it will accept,
	//which are the pad's own six navigation presses (item 2) written in the duration
	//family the emulated clock allows, so the unit is never a model's guess
	//(ADR-0272 item 4). Which moves have already failed from a state, the decision cap
	//and the recording are the runner's (items 3 and 5); no model is reachable here.
	private JsonObject NavGoal(JsonObject args, int pad)
	{
		string goal = args["goal"]?.GetValue<string>() ?? "";
		if(goal.Length == 0) {
			throw new ArgumentException("nav.goal needs the goal it navigates to");
		}
		string family = _keys.ClockRunning ? "frames" : "ticks";
		JsonArray moves = new();
		foreach(PadNavAction navigation in PadNavControls.Navigation) {
			moves.Add((JsonNode?)new JsonObject {
				["name"] = "pad.press",
				["args"] = new JsonObject { ["pad"] = pad, ["button"] = navigation.ToString(), [family] = 1 }
			});
		}
		return new JsonObject { ["goal"] = goal, ["state"] = State(), ["family"] = family, ["moves"] = moves };
	}

	private TestHookPads Pads()
	{
		return _pads ?? throw new ArgumentException("this hook has no pad bridge: pad.connect and pad.disconnect need a window");
	}

	//A repeated request id is a retry: it gets the original path, size and sha256
	//back and writes nothing again (item 2). Only the last capture is kept.
	private JsonObject Capture(JsonObject request)
	{
		JsonNode? id = request["id"];
		if(id is not null && _lastCaptureId is not null && _lastCapture is not null && JsonNode.DeepEquals(id, _lastCaptureId)) {
			return (JsonObject)_lastCapture.DeepClone();
		}
		string path = request["path"]?.GetValue<string>() ?? "";
		if(path.Length == 0) {
			throw new ArgumentException("capture needs a path");
		}
		CaptureResult shot = _target.Capture(path);
		JsonObject answer = new() { ["path"] = shot.Path, ["size"] = new JsonArray(shot.Width, shot.Height), ["sha256"] = shot.Sha256 };
		_lastCaptureId = id?.DeepClone();
		_lastCapture = (JsonObject)answer.DeepClone();
		return answer;
	}

	private JsonObject Quit()
	{
		_target.Quit();
		return new JsonObject();
	}
}
