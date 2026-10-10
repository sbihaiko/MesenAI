using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Mesen.Logic.TestHook;
using Xunit;

namespace Mesen.Tests.TestHook
{
	//The GUI test hook (#1182, the GUI test hook ADR on PR #1202, items 1-5): the
	//host-free half - the flag, the JSON-lines protocol, the pressed-key overlay and
	//the socket. The wiring to a real window is pinned in UI.HeadlessTests.
	public class TestHookProtocolTests
	{
		private sealed class FakeTarget : ITestHookTarget
		{
			public bool Quit_;
			public string? CapturedTo;

			public JsonObject State() => new JsonObject {
				["screen"] = "play.home",
				["focus"] = "play.home.continue",
				["controls"] = new JsonArray()
			};

			public CaptureResult Capture(string path)
			{
				CapturedTo = path;
				return new CaptureResult(path, 10, 20, "abc");
			}

			public void Quit() => Quit_ = true;
		}

		private sealed class Rig
		{
			public readonly List<(ushort Code, bool Down)> Calls = new();
			public readonly FakeTarget Target = new();
			public long? Frames;
			public readonly TestHookKeys Keys;
			public readonly TestHookProtocol Protocol;

			public Rig()
			{
				Keys = new TestHookKeys((code, down) => Calls.Add((code, down)), name => name switch { "Pad1 Right" => (ushort)0x1011, _ => (ushort)0 }, () => Frames);
				Protocol = new TestHookProtocol("secret", Target, Keys);
			}

			public JsonObject Ask(string json) => JsonNode.Parse(Protocol.Handle(json))!.AsObject();
		}

		[Fact]
		public void Without_the_flag_there_are_no_options()
		{
			Assert.Null(TestHookOptions.Parse(new[] { "game.nes", "--other" }));
		}

		[Fact]
		public void The_flag_carries_the_endpoint_and_the_token()
		{
			TestHookOptions? options = TestHookOptions.Parse(new[] { "--test-hook=/tmp/x/hook.sock", "--test-hook-token=t0k" });
			Assert.NotNull(options);
			Assert.Equal("/tmp/x/hook.sock", options!.Endpoint);
			Assert.Equal("t0k", options.Token);
		}

		[Fact]
		public void The_flag_with_no_endpoint_is_a_startup_failure_not_a_silent_run()
		{
			Assert.Throws<ArgumentException>(() => TestHookOptions.Parse(new[] { "--test-hook=" }));
		}

		[Fact]
		public void A_wrong_token_answers_unauthorized_and_injects_nothing()
		{
			Rig rig = new();
			JsonObject answer = rig.Ask("{\"id\":1,\"token\":\"nope\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"ticks\":1}}");
			Assert.False(answer["ok"]!.GetValue<bool>());
			Assert.Equal("unauthorized", answer["error"]!.GetValue<string>());
			Assert.Equal(1, answer["id"]!.GetValue<int>());
			Assert.Empty(rig.Calls);
		}

		[Fact]
		public void A_bad_line_is_an_error_response_and_not_an_exception()
		{
			Rig rig = new();
			Assert.False(rig.Ask("not json")["ok"]!.GetValue<bool>());
			Assert.False(rig.Ask("{\"id\":2,\"token\":\"secret\",\"op\":\"teleport\"}")["ok"]!.GetValue<bool>());
		}

		[Fact]
		public void Hello_names_the_five_operations()
		{
			Rig rig = new();
			JsonObject answer = rig.Ask("{\"id\":1,\"token\":\"secret\",\"op\":\"hello\"}");
			Assert.True(answer["ok"]!.GetValue<bool>());
			List<string> ops = new();
			foreach(JsonNode? op in answer["ops"]!.AsArray()) {
				ops.Add(op!.GetValue<string>());
			}
			Assert.Equal(new[] { "capture", "hello", "inject", "quit", "state" }, System.Linq.Enumerable.OrderBy(ops, o => o, StringComparer.Ordinal));
		}

		[Fact]
		public void State_carries_the_controls_and_both_counters()
		{
			Rig rig = new();
			rig.Frames = 77;
			rig.Keys.Advance();
			rig.Keys.Advance();
			JsonObject answer = rig.Ask("{\"id\":3,\"token\":\"secret\",\"op\":\"state\"}");
			Assert.Equal("play.home.continue", answer["focus"]!.GetValue<string>());
			Assert.Equal(2, answer["tick"]!.GetValue<long>());
			Assert.Equal(77, answer["frames"]!.GetValue<long>());
		}

		[Fact]
		public void A_press_in_ticks_holds_the_key_for_that_many_ticks_then_lets_go()
		{
			Rig rig = new();
			JsonObject answer = rig.Ask("{\"id\":4,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"ticks\":2}}");
			Assert.True(answer["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x1011, true) }, rig.Calls);
			rig.Keys.Advance();
			Assert.Single(rig.Calls);
			rig.Keys.Advance();
			Assert.Equal(new[] { ((ushort)0x1011, true), ((ushort)0x1011, false) }, rig.Calls);
		}

		[Fact]
		public void A_press_in_frames_is_counted_by_the_emulated_clock_not_the_ticks()
		{
			Rig rig = new();
			rig.Frames = 100;
			Assert.True(rig.Ask("{\"id\":5,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"frames\":4}}")["ok"]!.GetValue<bool>());
			rig.Keys.Advance();
			rig.Frames = 103;
			rig.Keys.Advance();
			Assert.Single(rig.Calls);
			rig.Frames = 104;
			rig.Keys.Advance();
			Assert.Equal(2, rig.Calls.Count);
			Assert.False(rig.Calls[1].Down);
		}

		[Fact]
		public void A_step_whose_unit_does_not_match_the_clock_is_malformed()
		{
			Rig rig = new();
			//No running game: the emulated clock is frozen, so frames would spin.
			JsonObject frames = rig.Ask("{\"id\":6,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"frames\":4}}");
			Assert.False(frames["ok"]!.GetValue<bool>());
			//A running game: the clock advances, so ticks would not be frame-accurate.
			rig.Frames = 10;
			JsonObject ticks = rig.Ask("{\"id\":7,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"ticks\":4}}");
			Assert.False(ticks["ok"]!.GetValue<bool>());
			Assert.Empty(rig.Calls);
		}

		[Fact]
		public void An_unknown_button_or_action_is_an_error_naming_it()
		{
			Rig rig = new();
			JsonObject button = rig.Ask("{\"id\":8,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Turbo\",\"ticks\":1}}");
			Assert.False(button["ok"]!.GetValue<bool>());
			Assert.Contains("Turbo", button["error"]!.GetValue<string>());
			JsonObject action = rig.Ask("{\"id\":9,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pointer.click\",\"args\":{}}");
			Assert.False(action["ok"]!.GetValue<bool>());
		}

		[Fact]
		public void Capture_names_the_path_and_never_carries_the_image()
		{
			Rig rig = new();
			string path = Path.Combine(Path.GetTempPath(), "hook-shot.png");
			JsonObject answer = rig.Ask("{\"id\":10,\"token\":\"secret\",\"op\":\"capture\",\"path\":" + JsonValue.Create(path)!.ToJsonString() + "}");
			Assert.True(answer["ok"]!.GetValue<bool>());
			Assert.Equal(path, rig.Target.CapturedTo);
			Assert.Equal("abc", answer["sha256"]!.GetValue<string>());
			Assert.Equal(10, answer["size"]![0]!.GetValue<int>());
		}

		[Fact]
		public void Quit_asks_the_application_to_exit_its_own_way()
		{
			Rig rig = new();
			Assert.True(rig.Ask("{\"id\":11,\"token\":\"secret\",\"op\":\"quit\"}")["ok"]!.GetValue<bool>());
			Assert.True(rig.Target.Quit_);
		}

		[Fact]
		public void The_socket_answers_one_line_per_line_and_is_private()
		{
			if(OperatingSystem.IsWindows()) {
				return;
			}
			string folder = Path.Combine(Path.GetTempPath(), "hook-" + Guid.NewGuid().ToString("N"));
			string endpoint = Path.Combine(folder, "hook.sock");
			using TestHookServer server = TestHookServer.Start(endpoint, line => "{\"echo\":" + JsonValue.Create(line)!.ToJsonString() + "}");
			try {
				Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(folder));
				Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(endpoint));
				using Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				client.Connect(new UnixDomainSocketEndPoint(endpoint));
				using NetworkStream stream = new(client);
				byte[] request = Encoding.UTF8.GetBytes("ping\npong\n");
				stream.Write(request, 0, request.Length);
				using StreamReader reader = new(stream, Encoding.UTF8);
				Assert.Equal("{\"echo\":\"ping\"}", reader.ReadLine());
				Assert.Equal("{\"echo\":\"pong\"}", reader.ReadLine());
			} finally {
				server.Dispose();
				Directory.Delete(folder, true);
			}
		}
	}
}
