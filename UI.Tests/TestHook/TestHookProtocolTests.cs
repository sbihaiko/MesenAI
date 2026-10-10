using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
			public int Captures;
			public string? CaptureError;

			public JsonObject State() => new JsonObject {
				["screen"] = "play.home",
				["focus"] = "play.home.continue",
				["controls"] = new JsonArray()
			};

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
			public readonly TestHookKeys Keys;
			public readonly TestHookProtocol Protocol;

			public Rig(List<(string Key, bool Down)>? raised = null)
			{
				Keys = new TestHookKeys((code, down) => Calls.Add((code, down)), name => name switch { "Pad1 Right" => (ushort)0x1011, "Enter" => (ushort)0x0D, _ => (ushort)0 }, () => Frames, raised is null ? null : (key, down) => raised.Add((key, down)));
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
		public void A_key_press_raises_the_key_on_the_GUI_when_it_starts_and_ends_but_a_pad_press_does_not()
		{
			List<(string Key, bool Down)> raised = new();
			Rig rig = new(raised);
			rig.Ask("{\"id\":16,\"token\":\"secret\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"Enter\",\"ticks\":1}}");
			Assert.Equal(new[] { ("Enter", true) }, raised);
			rig.Keys.Advance();
			Assert.Equal(new[] { ("Enter", true), ("Enter", false) }, raised);
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
			Assert.Null(rig.Keys.Press(1, "Right", 10, null));
			Assert.Null(rig.Keys.Press(1, "Right", 2, null));
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
			Assert.Null(keys.Press(1, "Up", 1, null));
			Assert.Null(keys.Press(1, "Confirm", 1, null));
			Assert.Null(keys.Press(1, "Back", 1, null));
			Assert.Equal(new ushort[] { 0x2001, 0x2002, 0x2003 }, calls.Select(c => c.Code));
			Assert.NotNull(keys.Press(1, "Nonsense", 1, null));
		}

		[Fact]
		public void A_frames_hold_lets_go_when_the_clock_stops_mid_hold()
		{
			Rig rig = new();
			rig.Frames = 100;
			Assert.Null(rig.Keys.Press(1, "Right", null, 10));
			rig.Frames = null;
			rig.Keys.Advance();
			Assert.Equal(new[] { ((ushort)0x1011, true), ((ushort)0x1011, false) }, rig.Calls);
		}

		[Fact]
		public void A_frames_hold_lets_go_when_the_frame_counter_resets_below_where_it_started()
		{
			Rig rig = new();
			rig.Frames = 5000;
			Assert.Null(rig.Keys.Press(1, "Right", null, 10));
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
	}
}
