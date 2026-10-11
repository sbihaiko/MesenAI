using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Mesen.Logic;
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
			public int Captures;
			public string? CaptureError;

			//#1282: how many steps the protocol declared. The counts a step's own
			//check reads restart here, so a run proves the request that started the
			//step - and only it - is what a `ui.haptics` check measures.
			public int Steps;

			public JsonObject State() => new JsonObject {
				["screen"] = "play.home",
				["focus"] = "play.home.continue",
				["controls"] = new JsonArray()
			};

			public void Step() => Steps++;

			public CaptureResult Capture(string path)
			{
				if(CaptureError is not null) {
					throw new InvalidOperationException(CaptureError);
				}
				CapturedTo = path;
				Captures++;
				return new CaptureResult(path, 10, 20, "abc");
			}

			public void Quit() => Quit_ = true;
		}

		private sealed class Rig
		{
			public readonly List<(ushort Code, bool Down)> Calls = new();
			public readonly FakeTarget Target = new();
			public long? Frames;
			//#1281: the pads a script hot-plugs, and the count the application would
			//read - 2, as a machine with two real pads on it.
			public uint RealPads = 2;
			public readonly TestHookPads Pads;
			public readonly TestHookKeys Keys;
			public readonly TestHookProtocol Protocol;

			public Rig(List<(ushort Key, bool Down)>? raised = null)
			{
				Pads = new TestHookPads(() => RealPads);
				Keys = new TestHookKeys((code, down) => Calls.Add((code, down)), name => name switch { "Pad1 Right" => (ushort)0x1011, "Pad1 Left" => (ushort)0x1012, "Pad1 A" => (ushort)0x1013, "Enter" => (ushort)0x0D, "Up Arrow" => (ushort)24, "1" => (ushort)35, _ => (ushort)0 }, () => Frames, raised is null ? null : (key, down) => raised.Add((key, down)), Pads.FamilyOf);
				Protocol = new TestHookProtocol("secret", Target, Keys, Pads);
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

		//#1282: the counts a `ui.haptics` check reads are "since the last step", and
		//the step is the request that starts it - an inject, and nothing else. A
		//state read is what a wait polls (ADR-0272 item 4), so it must never look
		//like a step boundary: draining there would lose the ticks a step caused.
		[Fact]
		public void An_inject_declares_the_step_and_a_state_read_does_not()
		{
			Rig rig = new();
			rig.Ask("{\"id\":7,\"token\":\"secret\",\"op\":\"state\"}");
			Assert.Equal(0, rig.Target.Steps);
			rig.Ask("{\"id\":8,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"ticks\":1}}");
			Assert.Equal(1, rig.Target.Steps);
			rig.Ask("{\"id\":9,\"token\":\"secret\",\"op\":\"capture\",\"path\":\"/tmp/x.png\"}");
			rig.Ask("{\"id\":10,\"token\":\"secret\",\"op\":\"state\"}");
			Assert.Equal(1, rig.Target.Steps);
		}

		//A refused request is not a step: Step() restarts the since-last-step counts
		//(#1282), so a runner retrying after a refusal must still read the press's tick.
		[Theory]
		[InlineData("{\"id\":9,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"text.type\",\"args\":{\"text\":\"a\"}}")]
		[InlineData("{\"id\":9,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.pres\",\"args\":{\"button\":\"Right\",\"ticks\":1}}")]
		[InlineData("{\"id\":9,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"pad\":-1,\"button\":\"Right\",\"ticks\":1}}")]
		public void A_refused_inject_does_not_declare_a_step(string refused)
		{
			Rig rig = new();
			Assert.True(rig.Ask("{\"id\":8,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"ticks\":1}}")["ok"]!.GetValue<bool>());
			Assert.Equal(1, rig.Target.Steps);
			Assert.False(rig.Ask(refused)["ok"]!.GetValue<bool>());
			Assert.Equal(1, rig.Target.Steps);
		}

		//One number names one pad: the index a press takes is the index ui.haptics reads.
		[Fact]
		public void A_press_and_the_haptics_name_the_same_pad_index()
		{
			TestHookHaptics haptics = new();
			Rig rig = new();
			Assert.True(rig.Ask("{\"id\":8,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"pad\":0,\"button\":\"Right\",\"ticks\":1}}")["ok"]!.GetValue<bool>());
			haptics.Record(0);
			Assert.Equal(1, haptics.Count(0));
			Assert.Equal(0, haptics.Take()[0]!["pad"]!.GetValue<int>());
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
		public void A_key_press_raises_the_key_on_the_GUI_when_it_starts_and_ends_but_a_pad_press_does_not()
		{
			List<(ushort Key, bool Down)> raised = new();
			Rig rig = new(raised);
			rig.Ask("{\"id\":16,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"Enter\",\"ticks\":1}}");
			Assert.Equal(new[] { ((ushort)0x0D, true) }, raised);
			rig.Keys.Advance();
			Assert.Equal(new[] { ((ushort)0x0D, true), ((ushort)0x0D, false) }, raised);
			raised.Clear();
			//The code goes through, not the name: "Up Arrow" and "1" are not Avalonia names.
			rig.Ask("{\"id\":18,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"Up Arrow\",\"ticks\":1}}");
			rig.Ask("{\"id\":19,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"1\",\"ticks\":1}}");
			Assert.Equal(new[] { ((ushort)24, true), ((ushort)35, true) }, raised);
			rig.Keys.Advance();
			raised.Clear();
			rig.Ask("{\"id\":17,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"ticks\":1}}");
			rig.Keys.Advance();
			Assert.Empty(raised);
		}

		[Fact]
		public void A_key_press_holds_the_literal_key_for_its_ticks_then_lets_go()
		{
			Rig rig = new();
			JsonObject answer = rig.Ask("{\"id\":10,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"Enter\",\"ticks\":1}}");
			Assert.True(answer["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x0D, true) }, rig.Calls);
			rig.Keys.Advance();
			Assert.Equal(new[] { ((ushort)0x0D, true), ((ushort)0x0D, false) }, rig.Calls);
		}

		[Fact]
		public void A_key_press_follows_the_duration_family_of_the_clock_and_names_an_unknown_key()
		{
			Rig rig = new();
			JsonObject unknown = rig.Ask("{\"id\":11,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"Hyper\",\"ticks\":1}}");
			Assert.False(unknown["ok"]!.GetValue<bool>());
			Assert.Contains("Hyper", unknown["error"]!.GetValue<string>());
			rig.Frames = 100;
			JsonObject ticks = rig.Ask("{\"id\":12,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"Enter\",\"ticks\":1}}");
			Assert.False(ticks["ok"]!.GetValue<bool>());
			Assert.Empty(rig.Calls);
			JsonObject frames = rig.Ask("{\"id\":14,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"Enter\",\"frames\":2}}");
			Assert.True(frames["ok"]!.GetValue<bool>());
			Assert.Contains(((ushort)0x0D, true), rig.Calls);
			rig.Frames = null;
			rig.Calls.Clear();
			JsonObject stopped = rig.Ask("{\"id\":15,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"Enter\",\"frames\":2}}");
			Assert.False(stopped["ok"]!.GetValue<bool>());
			Assert.Empty(rig.Calls);
		}

		[Fact]
		public void Hello_advertises_the_key_namespace()
		{
			Rig rig = new();
			JsonObject hello = rig.Ask("{\"id\":13,\"token\":\"secret\",\"op\":\"hello\"}");
			Assert.Contains("key", hello["namespaces"]!.AsArray().Select(n => n!.GetValue<string>()));
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
		public void A_repeated_capture_id_replays_the_first_answer_and_writes_nothing_again()
		{
			Rig rig = new();
			string first = Path.Combine(Path.GetTempPath(), "hook-first.png");
			string second = Path.Combine(Path.GetTempPath(), "hook-second.png");
			JsonObject one = rig.Ask("{\"id\":12,\"token\":\"secret\",\"op\":\"capture\",\"path\":" + JsonValue.Create(first)!.ToJsonString() + "}");
			JsonObject again = rig.Ask("{\"id\":12,\"token\":\"secret\",\"op\":\"capture\",\"path\":" + JsonValue.Create(second)!.ToJsonString() + "}");
			Assert.Equal(1, rig.Target.Captures);
			Assert.Equal(first, again["path"]!.GetValue<string>());
			Assert.Equal(one["sha256"]!.GetValue<string>(), again["sha256"]!.GetValue<string>());
			Assert.Equal(one["size"]!.ToJsonString(), again["size"]!.ToJsonString());
			rig.Ask("{\"id\":13,\"token\":\"secret\",\"op\":\"capture\",\"path\":" + JsonValue.Create(second)!.ToJsonString() + "}");
			Assert.Equal(2, rig.Target.Captures);
		}

		[Fact]
		public void A_missing_or_empty_request_token_is_unauthorized_even_when_the_hook_token_is_empty()
		{
			Rig rig = new();
			Assert.Equal("unauthorized", rig.Ask("{\"id\":14,\"op\":\"quit\"}")["error"]!.GetValue<string>());
			Assert.Equal("unauthorized", rig.Ask("{\"id\":15,\"token\":\"\",\"op\":\"quit\"}")["error"]!.GetValue<string>());
			TestHookProtocol open = new("", rig.Target, rig.Keys);
			Assert.Equal("unauthorized", JsonNode.Parse(open.Handle("{\"id\":16,\"op\":\"quit\"}"))!["error"]!.GetValue<string>());
			Assert.False(rig.Target.Quit_);
		}

		[Fact]
		public void The_flag_without_a_token_is_a_startup_failure()
		{
			Assert.Throws<ArgumentException>(() => TestHookOptions.Parse(new[] { "--test-hook=/tmp/x.sock" }));
			Assert.Throws<ArgumentException>(() => TestHookOptions.Parse(new[] { "--test-hook=/tmp/x.sock", "--test-hook-token=" }));
		}

		[Fact]
		public void An_existing_parent_directory_keeps_its_mode_and_only_the_socket_is_locked_down()
		{
			if(OperatingSystem.IsWindows()) {
				return;
			}
			string folder = Path.Combine(Path.GetTempPath(), "hook-sh-" + Guid.NewGuid().ToString("N").Substring(0, 12));
			UnixFileMode shared = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
			Directory.CreateDirectory(folder);
			File.SetUnixFileMode(folder, shared);
			try {
				string endpoint = Path.Combine(folder, "hook.sock");
				using(TestHookServer server = TestHookServer.Start(endpoint, line => line)) {
					Assert.Equal(shared, File.GetUnixFileMode(folder));
					Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(endpoint));
				}
			} finally {
				Directory.Delete(folder, true);
			}
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

		[Fact]
		public void An_overlapping_hold_on_one_code_keeps_it_pressed_until_the_longest_ends()
		{
			Rig rig = new();
			Assert.Null(rig.Keys.Press(0, "Right", 10, null));
			Assert.Null(rig.Keys.Press(0, "Right", 2, null));
			rig.Calls.Clear();
			for(int i = 0; i < 2; i++) {
				rig.Keys.Advance();
			}
			Assert.DoesNotContain((ushort)0x1011, rig.Calls.Where(c => !c.Down).Select(c => c.Code));
			for(int i = 0; i < 8; i++) {
				rig.Keys.Advance();
			}
			Assert.Single(rig.Calls, c => !c.Down);
		}

		[Fact]
		public void A_line_over_the_limit_closes_the_connection_instead_of_growing_the_buffer()
		{
			if(OperatingSystem.IsWindows()) {
				return;
			}
			string folder = Path.Combine(Path.GetTempPath(), "hook-" + Guid.NewGuid().ToString("N"));
			string endpoint = Path.Combine(folder, "hook.sock");
			using TestHookServer server = TestHookServer.Start(endpoint, line => line);
			try {
				using Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				client.Connect(new UnixDomainSocketEndPoint(endpoint));
				client.ReceiveTimeout = 3000;
				byte[] chunk = new byte[64 * 1024];
				Array.Fill(chunk, (byte)'a');
				try {
					for(int i = 0; i < 40; i++) {
						client.Send(chunk);
					}
				} catch(SocketException) {
				}
				byte[] reply = new byte[16];
				int read;
				try {
					read = client.Receive(reply);
				} catch(SocketException ex) {
					Assert.NotEqual(SocketError.TimedOut, ex.SocketErrorCode);
					read = 0;
				}
				Assert.Equal(0, read);
			} finally {
				server.Dispose();
				Directory.Delete(folder, true);
			}
		}

		[Theory]
		[InlineData("--test-hook=/tmp/h.sock", "--test-hook-token=s3cret")]
		[InlineData("--test-hook", "/tmp/h.sock", "--test-hook-token", "s3cret")]
		public void The_hook_flags_are_consumed_before_the_emulator_reads_its_command_line(params string[] hookArgs)
		{
			//CommandLineHelper feeds every argument it does not consume to
			//ConfigManager.ProcessSwitch (an error message, shown by OSD, quoting the
			//argument - the token) or to FilesToLoad.
			string[] args = new[] { "--fullscreen" }.Concat(hookArgs).Concat(new[] { "game.nes" }).ToArray();
			Assert.Equal(new[] { "--fullscreen", "game.nes" }, TestHookOptions.WithoutHookArgs(args));
		}

		[Fact]
		public void A_button_name_resolves_through_the_pad_navigation_names_first_non_zero()
		{
			List<(ushort Code, bool Down)> calls = new();
			//Only the DirectInput spelling exists on this backend.
			TestHookKeys keys = new((code, down) => calls.Add((code, down)), name => name == "Joy1 DPad Up" ? (ushort)0x2001 : name == "Joy1 But2" ? (ushort)0x2002 : name == "Joy1 But3" ? (ushort)0x2003 : (ushort)0, () => null);
			Assert.Null(keys.Press(0, "Up", 1, null));
			Assert.Null(keys.Press(0, "Confirm", 1, null));
			Assert.Null(keys.Press(0, "Back", 1, null));
			Assert.Equal(new ushort[] { 0x2001, 0x2002, 0x2003 }, calls.Select(c => c.Code));
			Assert.NotNull(keys.Press(0, "Nonsense", 1, null));
		}

		[Fact]
		public void A_frames_hold_lets_go_when_the_clock_stops_mid_hold()
		{
			Rig rig = new();
			rig.Frames = 100;
			Assert.Null(rig.Keys.Press(0, "Right", null, 10));
			rig.Frames = null;
			rig.Keys.Advance();
			Assert.Equal(new[] { ((ushort)0x1011, true), ((ushort)0x1011, false) }, rig.Calls);
		}

		[Fact]
		public void A_frames_hold_lets_go_when_the_frame_counter_resets_below_where_it_started()
		{
			Rig rig = new();
			rig.Frames = 5000;
			Assert.Null(rig.Keys.Press(0, "Right", null, 10));
			rig.Frames = 3;
			rig.Keys.Advance();
			Assert.Equal(new[] { ((ushort)0x1011, true), ((ushort)0x1011, false) }, rig.Calls);
		}

		[Fact]
		public void A_capture_the_target_refuses_comes_back_as_a_failure_not_a_picture()
		{
			Rig rig = new();
			rig.Target.CaptureError = "capture is not available while a game is loaded";
			JsonObject answer = rig.Ask("{\"id\":9,\"token\":\"secret\",\"op\":\"capture\",\"path\":\"/tmp/x.png\"}");
			Assert.False(answer["ok"]!.GetValue<bool>());
			Assert.Contains("game is loaded", answer["error"]!.GetValue<string>());
		}

		//#1281: the actions beside pad.press that ADR-0272's `pad.*` namespace already
		//covers - hold, release and the two hot-plug ones. The hook is what executes
		//them; no step reaches an OS input path, and the pad index every pad action
		//takes is a device index: 0 is the pad in the hand (ADR-0272 section 4's
		//"device 0"), 1 the second pad.
		[Fact]
		public void The_namespaces_are_pad_key_and_ui_and_a_text_action_is_not_this_hook_s()
		{
			//ADR-0272 item 7 places `text.*` with `pointer.*` in the real-window mode
			//(step 7), never in this hook: the namespaces stay pad, key and ui, and a
			//step that writes `text.type` is an unknown action rather than a silent
			//pass. The ADR is accepted and this change does not amend it, so the hook
			//implements the namespaces it names and nothing beside them (#1281 review).
			Rig rig = new();
			JsonObject hello = rig.Ask("{\"id\":1,\"token\":\"secret\",\"op\":\"hello\"}");
			List<string> namespaces = new();
			foreach(JsonNode? one in hello["namespaces"]!.AsArray()) {
				namespaces.Add(one!.GetValue<string>());
			}
			Assert.Equal(new[] { "key", "pad", "ui" }, Enumerable.OrderBy(namespaces, n => n, StringComparer.Ordinal));
			JsonObject typed = rig.Ask("{\"id\":2,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"text.type\",\"args\":{\"text\":\"zel\"}}");
			Assert.False(typed["ok"]!.GetValue<bool>());
			Assert.Contains("unknown action text.type", typed["error"]!.GetValue<string>());
			Assert.Empty(rig.Calls);
		}

		[Fact]
		public void The_pad_index_is_the_device_index_and_zero_is_the_pad_in_the_hand()
		{
			Rig rig = new();
			//No pad named: device 0, the pad in hand, which the backend calls "Pad1".
			Assert.True(rig.Ask("{\"id\":2,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"ticks\":1}}")["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x1011, true) }, rig.Calls);
			//Pad 1 is the second pad: another device, whose buttons this backend has
			//no name for, so the press is refused rather than sent to the wrong pad.
			JsonObject second = rig.Ask("{\"id\":3,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"pad\":1,\"button\":\"Right\",\"ticks\":1}}");
			Assert.False(second["ok"]!.GetValue<bool>());
			Assert.Contains("pad 2", second["error"]!.GetValue<string>());
			Assert.Single(rig.Calls);
		}

		[Fact]
		public void A_hold_keeps_the_button_down_for_its_ticks_and_a_release_ends_it_early()
		{
			Rig rig = new();
			Assert.True(rig.Ask("{\"id\":4,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.hold\",\"args\":{\"button\":\"Right\",\"ticks\":5}}")["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x1011, true) }, rig.Calls);
			rig.Keys.Advance();
			rig.Keys.Advance();
			Assert.Single(rig.Calls);
			//A release lets the same button go before its hold ran out - one press of
			//a two-button gesture (hold A, press B) ends this way.
			Assert.True(rig.Ask("{\"id\":5,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.release\",\"args\":{\"button\":\"Right\"}}")["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x1011, true), ((ushort)0x1011, false) }, rig.Calls);
			//The hold that was let go does not let go a second time when its ticks
			//would have run out.
			rig.Keys.Advance();
			rig.Keys.Advance();
			rig.Keys.Advance();
			Assert.Equal(2, rig.Calls.Count);
		}

		[Fact]
		public void A_hold_is_written_in_the_unit_the_emulated_clock_allows_like_a_press()
		{
			//ADR-0272 items 4 and 7 key the duration family of every timed step to
			//whether the emulated clock advances, and a hold is one of them: while a
			//game runs it is written in frames, on any other state in ticks, and the
			//wrong family is the step's own error - never a hold that spins on a
			//counter its unit does not measure (#1281 review).
			Rig rig = new();
			rig.Frames = 500;
			JsonObject inTicks = rig.Ask("{\"id\":6,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.hold\",\"args\":{\"button\":\"Right\",\"ticks\":2}}");
			Assert.False(inTicks["ok"]!.GetValue<bool>());
			Assert.Contains("write the hold in frames", inTicks["error"]!.GetValue<string>());
			Assert.True(rig.Ask("{\"id\":7,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.hold\",\"args\":{\"button\":\"Right\",\"frames\":2}}")["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x1011, true) }, rig.Calls);
			//Two UI ticks with no frame advanced: the button is still down, because a
			//frames hold is counted by the core's counter, not by the GUI's tick.
			rig.Keys.Advance();
			rig.Keys.Advance();
			Assert.Single(rig.Calls);
			rig.Frames = 502;
			rig.Keys.Advance();
			Assert.Equal(new[] { ((ushort)0x1011, true), ((ushort)0x1011, false) }, rig.Calls);
			//No game loaded: the same action is a ticks hold, and frames is refused.
			rig.Frames = null;
			JsonObject inFrames = rig.Ask("{\"id\":8,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.hold\",\"args\":{\"button\":\"Right\",\"frames\":4}}");
			Assert.False(inFrames["ok"]!.GetValue<bool>());
			Assert.Contains("write the hold in ticks", inFrames["error"]!.GetValue<string>());
			Assert.True(rig.Ask("{\"id\":9,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.hold\",\"args\":{\"button\":\"Right\",\"ticks\":3}}")["ok"]!.GetValue<bool>());
		}

		[Fact]
		public void A_hold_without_a_duration_and_a_release_of_no_button_are_errors()
		{
			Rig rig = new();
			JsonObject noTicks = rig.Ask("{\"id\":22,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.hold\",\"args\":{\"button\":\"Right\"}}");
			Assert.False(noTicks["ok"]!.GetValue<bool>());
			Assert.Contains("write the hold in ticks", noTicks["error"]!.GetValue<string>());
			JsonObject noButton = rig.Ask("{\"id\":8,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.release\",\"args\":{}}");
			Assert.False(noButton["ok"]!.GetValue<bool>());
			Assert.Contains("unknown button", noButton["error"]!.GetValue<string>());
			Assert.Empty(rig.Calls);
		}

		[Fact]
		public void A_release_puts_a_button_up_that_no_hold_is_keeping_down()
		{
			Rig rig = new();
			//A button the hook never held: the release is a no-op, not a wrong button
			//name and not a failed step.
			Assert.True(rig.Ask("{\"id\":9,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.release\",\"args\":{\"button\":\"Right\"}}")["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x1011, false) }, rig.Calls);
		}

		[Fact]
		public void A_hot_plug_moves_the_connected_count_the_application_reads()
		{
			Rig rig = new();
			//A run that never touches this reads the backend's own count: nothing here
			//replaces a real pad.
			Assert.Equal(2u, rig.Pads.Count);
			Assert.True(rig.Ask("{\"id\":13,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.connect\",\"args\":{\"index\":0,\"family\":\"xbox\"}}")["ok"]!.GetValue<bool>());
			Assert.Equal(1u, rig.Pads.Count);
			Assert.Equal(PadFamily.Xbox, rig.Pads.FamilyOf(0));
			Assert.True(rig.Ask("{\"id\":14,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.connect\",\"args\":{\"index\":1,\"family\":\"playstation\"}}")["ok"]!.GetValue<bool>());
			Assert.Equal(2u, rig.Pads.Count);
			Assert.True(rig.Ask("{\"id\":15,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.disconnect\",\"args\":{\"index\":0}}")["ok"]!.GetValue<bool>());
			Assert.Equal(1u, rig.Pads.Count);
			//A disconnected pad is back to the default family, never left naming the
			//buttons of a pad that is gone.
			Assert.Equal(PadFamily.Xbox, rig.Pads.FamilyOf(0));
		}

		[Fact]
		public void A_pad_reconnected_without_a_family_is_read_as_the_default_one()
		{
			//#1281 review, finding 1: the family goes with the pad. Disconnect drops
			//it and a later connect that names none is the default one - so a
			//PlayStation pad plugged back in without a family has its buttons read as
			//Xbox, which is what docs/agent-squad/gui-test-run-command.md's
			//pad.disconnect row now says (it claimed the family came back).
			Rig rig = new();
			Assert.True(rig.Ask("{\"id\":19,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.connect\",\"args\":{\"index\":0,\"family\":\"playstation\"}}")["ok"]!.GetValue<bool>());
			Assert.Equal(PadFamily.Ps4, rig.Pads.FamilyOf(0));
			Assert.True(rig.Ask("{\"id\":20,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.disconnect\",\"args\":{\"index\":0}}")["ok"]!.GetValue<bool>());
			Assert.True(rig.Ask("{\"id\":21,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.connect\",\"args\":{\"index\":0}}")["ok"]!.GetValue<bool>());
			Assert.Equal(PadFamily.Xbox, rig.Pads.FamilyOf(0));
		}

		[Fact]
		public void A_connect_without_a_family_is_the_default_one_and_a_stranger_is_refused()
		{
			Rig rig = new();
			Assert.True(rig.Ask("{\"id\":16,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.connect\",\"args\":{\"index\":0}}")["ok"]!.GetValue<bool>());
			Assert.Equal(PadFamily.Xbox, rig.Pads.FamilyOf(0));
			JsonObject stranger = rig.Ask("{\"id\":17,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.connect\",\"args\":{\"index\":1,\"family\":\"nintendo\"}}");
			Assert.False(stranger["ok"]!.GetValue<bool>());
			Assert.Contains("nintendo", stranger["error"]!.GetValue<string>());
			JsonObject noIndex = rig.Ask("{\"id\":18,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.disconnect\",\"args\":{}}");
			Assert.False(noIndex["ok"]!.GetValue<bool>());
		}

		[Fact]
		public void A_playstation_pad_resolves_its_own_directinput_spelling_first()
		{
			//A backend that defines both spellings, as Windows does: the family a pad
			//was connected with is the one its buttons are read in.
			List<(ushort Code, bool Down)> calls = new();
			TestHookPads pads = new(() => 0);
			Assert.Null(pads.Connect(0, "playstation"));
			TestHookKeys keys = new((code, down) => calls.Add((code, down)), name => name switch {
				"Pad1 Up" => (ushort)0x1001, "Joy1 DPad Up" => (ushort)0x2001, _ => (ushort)0
			}, () => null, null, pads.FamilyOf);
			Assert.Null(keys.Press(0, "Up", 1, null));
			Assert.Equal(new[] { ((ushort)0x2001, true) }, calls);
		}

		[Fact]
		public void The_socket_is_never_bound_in_a_folder_other_users_can_reach()
		{
			if(OperatingSystem.IsWindows()) {
				return;
			}
			//The folder exists and is shared; the socket lives in a private 0700
			//folder the hook makes, so there is no moment a 0644 socket is reachable.
			string parent = Path.Combine(Path.GetTempPath(), "hook-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(parent);
			string endpoint = Path.Combine(parent, "hook.sock");
			try {
				string? boundIn = null;
				UnixFileMode? folderMode = null;
				//Called the moment after Bind, before the socket is locked to 0600.
				using TestHookServer server = TestHookServer.Start(endpoint, line => line, bound => {
					boundIn = Path.GetDirectoryName(bound);
					if(!OperatingSystem.IsWindows()) {
						folderMode = File.GetUnixFileMode(boundIn!);
					}
				});
				Assert.NotEqual(Path.GetFullPath(parent), boundIn);
				Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, folderMode);
				Assert.True(File.Exists(endpoint));
				Assert.Equal(new[] { endpoint }, Directory.GetFileSystemEntries(parent));
				using Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				client.Connect(new UnixDomainSocketEndPoint(endpoint));
			} finally {
				Directory.Delete(parent, true);
			}
		}

		//#1779: the five verbs a GUI run needs beside a single press - release, chord,
		//navigate-to-goal, keyboard input and the focus read. Each one drives or reads
		//state the hook already owns, and none of them reads a pixel (ADR-0271).

		[Fact]
		public void Hello_names_the_verbs_the_batches_need_and_the_contract_version()
		{
			//ADR-0272 item 2: `hello` is where a runner reads what this hook answers, so a
			//runner can tell an old hook - which advertised no actions at all and answered
			//only pad.press - from this one without spending a step on it. The five verbs
			//the pad-only batches need are all named here.
			Rig rig = new();
			JsonObject answer = rig.Ask("{\"id\":1,\"token\":\"secret\",\"op\":\"hello\"}");
			Assert.Equal(2, answer["hook"]!.GetValue<int>());
			List<string> actions = new();
			foreach(JsonNode? action in answer["actions"]!.AsArray()) {
				actions.Add(action!.GetValue<string>());
			}
			Assert.Equal(new[] { "key.press", "nav.goal", "pad.chord", "pad.connect", "pad.disconnect", "pad.hold", "pad.press", "pad.release" }, actions);
			//The focus read is a state read, not an action: `state` is the op that answers
			//it, and it is one of the five ops the hello also names.
			Assert.Contains("state", answer["ops"]!.AsArray().Select(op => op!.GetValue<string>()));
		}

		[Fact]
		public void A_release_ends_a_chord_before_its_duration_runs_out()
		{
			//The batch's release: a chord is held, one of its buttons lets go early - the
			//gesture ADR-0256's Play pause is (Select+Start), and the one a timed step
			//alone cannot write, because the whole chord ends when its duration does.
			Rig rig = new();
			Assert.True(rig.Ask("{\"id\":2,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.chord\",\"args\":{\"buttons\":[\"Right\",\"Left\"],\"ticks\":3}}")["ok"]!.GetValue<bool>());
			Assert.True(rig.Ask("{\"id\":3,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.release\",\"args\":{\"button\":\"Right\"}}")["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x1011, true), ((ushort)0x1012, true), ((ushort)0x1011, false) }, rig.Calls);
			//The button that is still held ends by its own ticks, and never a second time.
			rig.Keys.Advance();
			rig.Keys.Advance();
			rig.Keys.Advance();
			//The button the chord still holds goes up on its own ticks. Which of the two
			//lift events comes first within one tick is not a contract, only that both do.
			Assert.Equal(4, rig.Calls.Count);
			Assert.Equal(((ushort)0x1011, false), rig.Calls[2]);
			Assert.Equal(((ushort)0x1012, false), rig.Calls[3]);
			rig.Keys.Advance();
			Assert.Equal(4, rig.Calls.Count);
		}

		[Fact]
		public void A_chord_puts_every_button_down_together_and_lifts_them_together()
		{
			//One request, one instant: every button of the chord is in the pressed set
			//before the step ends, which is what "pressed together" means to the readers
			//that poll that set (ShortcutKeyHandler's Select+Start included, ADR-0272 item 4).
			Rig rig = new();
			Assert.True(rig.Ask("{\"id\":4,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.chord\",\"args\":{\"buttons\":[\"Right\",\"Confirm\"],\"ticks\":2}}")["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x1011, true), ((ushort)0x1013, true) }, rig.Calls);
			rig.Keys.Advance();
			Assert.Equal(2, rig.Calls.Count);
			rig.Keys.Advance();
			Assert.Equal(4, rig.Calls.Count);
			Assert.Contains(((ushort)0x1011, false), rig.Calls.Skip(2));
			Assert.Contains(((ushort)0x1013, false), rig.Calls.Skip(2));
		}

		[Fact]
		public void A_chord_naming_one_button_or_a_stranger_presses_nothing()
		{
			//A chord is two buttons or more, and every one of them has to resolve: a
			//half-pressed chord is a gesture nobody made, so the request fails before
			//anything reaches the pressed set.
			Rig rig = new();
			JsonObject one = rig.Ask("{\"id\":5,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.chord\",\"args\":{\"buttons\":[\"Right\"],\"ticks\":2}}");
			Assert.False(one["ok"]!.GetValue<bool>());
			Assert.Contains("two buttons or more", one["error"]!.GetValue<string>());
			JsonObject stranger = rig.Ask("{\"id\":6,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.chord\",\"args\":{\"buttons\":[\"Right\",\"Turbo\"],\"ticks\":2}}");
			Assert.False(stranger["ok"]!.GetValue<bool>());
			Assert.Contains("unknown button Turbo", stranger["error"]!.GetValue<string>());
			Assert.Empty(rig.Calls);
		}

		[Fact]
		public void A_chord_is_written_in_the_unit_the_emulated_clock_allows_like_a_press()
		{
			//ADR-0272 item 4 binds every timed step to one family, and a chord is one of
			//them: a chord counted in ticks inside a running game would let go by a clock
			//the game's own counter does not move.
			Rig rig = new();
			rig.Frames = 500;
			JsonObject inTicks = rig.Ask("{\"id\":7,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.chord\",\"args\":{\"buttons\":[\"Right\",\"Left\"],\"ticks\":2}}");
			Assert.False(inTicks["ok"]!.GetValue<bool>());
			Assert.Contains("write the chord in frames", inTicks["error"]!.GetValue<string>());
			Assert.True(rig.Ask("{\"id\":8,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.chord\",\"args\":{\"buttons\":[\"Right\",\"Left\"],\"frames\":2}}")["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x1011, true), ((ushort)0x1012, true) }, rig.Calls);
			rig.Frames = 502;
			rig.Keys.Advance();
			Assert.Equal(4, rig.Calls.Count);
			Assert.Contains(((ushort)0x1011, false), rig.Calls.Skip(2));
			Assert.Contains(((ushort)0x1012, false), rig.Calls.Skip(2));
		}

		[Fact]
		public void Nav_goal_answers_the_state_and_the_moves_the_hook_owns_in_the_states_unit()
		{
			//ADR-0271 items 1 and 2: the state a Choice is made from is the hook's own UI
			//state, and the options are the moves this hook advertises - its pad's six
			//navigation presses - written in the duration family the emulated clock allows,
			//so a runner never guesses the unit (ADR-0272 item 4). No pixel is read and no
			//model is reachable from the hook: the Choice stays at the runner (item 6 A).
			Rig rig = new();
			JsonObject answer = rig.Ask("{\"id\":9,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"nav.goal\",\"args\":{\"goal\":\"ui.screen == play.library\"}}");
			Assert.True(answer["ok"]!.GetValue<bool>());
			Assert.Equal("ui.screen == play.library", answer["goal"]!.GetValue<string>());
			Assert.Equal("ticks", answer["family"]!.GetValue<string>());
			Assert.Equal("play.home", answer["state"]!["screen"]!.GetValue<string>());
			Assert.Equal("play.home.continue", answer["state"]!["focus"]!.GetValue<string>());
			List<string> moves = new();
			foreach(JsonNode? move in answer["moves"]!.AsArray()) {
				Assert.Equal("pad.press", move!["name"]!.GetValue<string>());
				Assert.Equal(0, move["args"]!["pad"]!.GetValue<int>());
				Assert.Equal(1, move["args"]!["ticks"]!.GetValue<int>());
				moves.Add(move["args"]!["button"]!.GetValue<string>());
			}
			Assert.Equal(new[] { "Up", "Down", "Left", "Right", "Confirm", "Back" }, moves);
			//While the emulated clock advances the same moves come back in frames: the one
			//number a step must not get wrong.
			rig.Frames = 500;
			JsonObject running = rig.Ask("{\"id\":10,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"nav.goal\",\"args\":{\"goal\":\"ui.screen == play.pause\"}}");
			Assert.Equal("frames", running["family"]!.GetValue<string>());
			Assert.Equal(1, running["moves"]!.AsArray()[0]!["args"]!["frames"]!.GetValue<int>());
			JsonObject noGoal = rig.Ask("{\"id\":11,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"nav.goal\",\"args\":{}}");
			Assert.False(noGoal["ok"]!.GetValue<bool>());
			Assert.Contains("nav.goal needs the goal", noGoal["error"]!.GetValue<string>());
		}

		[Fact]
		public void Nav_goal_injects_nothing_and_declares_no_step()
		{
			//nav.goal is a read of the surface a move is chosen from, never an input: no
			//key moves and no step begins, so a runner may ask it between two moves
			//without spending a step's counters (ADR-0272 item 3 / #1282).
			Rig rig = new();
			Assert.True(rig.Ask("{\"id\":12,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"nav.goal\",\"args\":{\"goal\":\"ui.screen == play.library\"}}")["ok"]!.GetValue<bool>());
			Assert.Empty(rig.Calls);
			Assert.Equal(0, rig.Target.Steps);
			Assert.True(rig.Ask("{\"id\":13,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"ticks\":1}}")["ok"]!.GetValue<bool>());
			Assert.Equal(1, rig.Target.Steps);
		}

		[Fact]
		public void A_key_press_drives_the_applications_keyboard_with_no_pad_connected()
		{
			//The keyboard verb the batches need: the same request shape as a pad press,
			//at the layer the GUI keyboard reads (KeyDown/KeyUp), with no pad behind it -
			//a machine with no controller plugged in is the case this exists for.
			List<(ushort Key, bool Down)> raised = new();
			Rig rig = new(raised);
			Assert.True(rig.Ask("{\"id\":14,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"Enter\",\"ticks\":2}}")["ok"]!.GetValue<bool>());
			Assert.Equal(new[] { ((ushort)0x0D, true) }, rig.Calls);
			Assert.Equal(new[] { ((ushort)0x0D, true) }, raised);
			rig.Keys.Advance();
			rig.Keys.Advance();
			Assert.Equal(new[] { ((ushort)0x0D, true), ((ushort)0x0D, false) }, raised);
			//A pad press raises nothing on the GUI's keyboard: the two verbs are the two
			//layers, and neither stands in for the other.
			Assert.True(rig.Ask("{\"id\":15,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Right\",\"ticks\":2}}")["ok"]!.GetValue<bool>());
			Assert.Equal(2, raised.Count);
		}

		[Fact]
		public void The_focus_read_is_the_hooks_own_state_and_never_a_picture()
		{
			//The read the batches need: which control holds the focus, answered from the
			//application's own UI state - the op `state`, one of the five the hello names.
			//A capture is a separate op and this read never takes one (ADR-0271 item 1).
			Rig rig = new();
			JsonObject state = rig.Ask("{\"id\":16,\"token\":\"secret\",\"op\":\"state\"}");
			Assert.True(state["ok"]!.GetValue<bool>());
			Assert.Equal("play.home.continue", state["focus"]!.GetValue<string>());
			Assert.Equal("play.home", state["screen"]!.GetValue<string>());
			Assert.NotNull(state["controls"]);
			Assert.Equal(0, rig.Target.Captures);
		}
	}
}
