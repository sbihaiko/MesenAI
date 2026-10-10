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
		//The namespaces an action can be written in. `text` (#1281) is the
		//application's OWN on-screen keyboard, driven through the pad - not an OS
		//keyboard and not `pointer.*`, which stay out of this hook.
		return new JsonObject { ["hook"] = Version, ["ops"] = ops, ["namespaces"] = new JsonArray("pad", "key", "text", "ui") };
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
	private JsonObject Inject(JsonObject request)
	{
		string action = request["action"]?.GetValue<string>() ?? "";
		JsonObject args = request["args"]?.AsObject() ?? new JsonObject();
		int? ticks = args["ticks"]?.GetValue<int>();
		int? frames = args["frames"]?.GetValue<int>();
		int pad = args["pad"]?.GetValue<int>() ?? 0;
		if(pad < 0) {
			throw new ArgumentException("pad is a pad index: 0 is the pad in the hand, 1 the second pad");
		}
		string button = args["button"]?.GetValue<string>() ?? "";
		string? error = action switch {
			"pad.press" => _keys.Press(pad, button, ticks, frames),
			//A hold takes its ticks as given: a missing one is what Hold itself refuses.
			"pad.hold" => _keys.HoldDown(pad, button, ticks ?? 0),
			"pad.release" => _keys.Release(pad, button),
			"key.press" => _keys.PressKey(args["key"]?.GetValue<string>() ?? "", ticks, frames),
			"text.type" => TypeText(args["text"]?.GetValue<string>() ?? ""),
			"pad.connect" => Pads().Connect(args["index"]?.GetValue<int>() ?? -1, args["family"]?.GetValue<string>()),
			"pad.disconnect" => Pads().Disconnect(args["index"]?.GetValue<int>() ?? -1),
			_ => throw new ArgumentException("unknown action " + action)
		};
		if(error is not null) {
			throw new ArgumentException(error);
		}
		return new JsonObject();
	}

	//An empty string is a step that asks for nothing, and it is refused here rather
	//than handed to the application, which would open the keyboard and type nothing.
	private string? TypeText(string text)
	{
		return text.Length == 0
			? "text.type takes the text to type"
			: _target.TypeText(text);
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
