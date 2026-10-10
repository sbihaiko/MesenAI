using System;
using System.Text.Json.Nodes;

namespace Mesen.Logic.TestHook;

//One JSON object per line in, one per line out (the GUI test hook ADR, PR #1202,
//items 1-3). Errors are responses: a bad request must not cost the run, so
//nothing in Handle throws. The hook keeps no history.
public sealed class TestHookProtocol
{
	public const int Version = 1;
	private static readonly string[] Ops = { "hello", "state", "inject", "capture", "quit" };

	private readonly string _token;
	private readonly ITestHookTarget _target;
	private readonly TestHookKeys _keys;

	public TestHookProtocol(string token, ITestHookTarget target, TestHookKeys keys)
	{
		_token = token;
		_target = target;
		_keys = keys;
	}

	public string Handle(string line)
	{
		JsonNode? id = null;
		try {
			if(JsonNode.Parse(line) is not JsonObject request) {
				return Fail(null, "a request is one JSON object");
			}
			id = request["id"]?.DeepClone();
			if((request["token"]?.GetValue<string>() ?? "") != _token) {
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
		return new JsonObject { ["hook"] = Version, ["ops"] = ops, ["namespaces"] = new JsonArray("pad", "ui") };
	}

	private JsonObject State()
	{
		JsonObject state = _target.State();
		state["tick"] = _keys.Tick;
		state["frames"] = _keys.Frames;
		return state;
	}

	private JsonObject Inject(JsonObject request)
	{
		string action = request["action"]?.GetValue<string>() ?? "";
		if(action != "pad.press") {
			throw new ArgumentException("unknown action " + action);
		}
		JsonObject args = request["args"]?.AsObject() ?? new JsonObject();
		string button = args["button"]?.GetValue<string>() ?? "";
		string? error = _keys.Press(args["pad"]?.GetValue<int>() ?? 1, button, args["ticks"]?.GetValue<int>(), args["frames"]?.GetValue<int>());
		if(error is not null) {
			throw new ArgumentException(error);
		}
		return new JsonObject();
	}

	private JsonObject Capture(JsonObject request)
	{
		string path = request["path"]?.GetValue<string>() ?? "";
		if(path.Length == 0) {
			throw new ArgumentException("capture needs a path");
		}
		CaptureResult shot = _target.Capture(path);
		return new JsonObject { ["path"] = shot.Path, ["size"] = new JsonArray(shot.Width, shot.Height), ["sha256"] = shot.Sha256 };
	}

	private JsonObject Quit()
	{
		_target.Quit();
		return new JsonObject();
	}
}
