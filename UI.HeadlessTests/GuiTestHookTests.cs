using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Logic.TestHook;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1182 (the GUI test hook ADR, PR #1202, items 1-5): the hook wired to a real
//MainWindow. The protocol, the flag and the socket are pinned host-free in
//UI.Tests/TestHook; this checks what only a window can: pad presses injected
//through the hook move the Home's focus with no pad attached, the hook is inert
//without its flag, a capture is the application window only, and one case runs
//the whole path over the local socket.
//
//The press reaches the real native pressed-key set (InputApi.SetInjectedKey ->
//KeyManager::GetPressedKeys) and the bridge reads it back from there; only the
//backend's key-name table is a stand-in, because a headless build has no key
//manager (see PlayPadNavigationWiring.TickForTest).
//
//Every window case asserts NativeCore.IsAvailable first and SKIPS with its reason
//when the library is absent - the repo's pattern for MainWindow cases. That is a
//skip, not a pass, but the headless suite is blind to it in CI (the core is not
//built there), so the evidence for those is a local run with MESEN_CORE_LIB set,
//and the checks that CAN run host-free live in UI.Tests/TestHook (the protocol,
//the flag, the socket) plus `The_recents_folder_is_not_the_developers_real_one`
//here, which reads only ConfigManager and so fails CI if this class ever clears
//recents outside its own temp folder.
[Collection(NativeCoreCollection.Name)]
public class GuiTestHookTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1182-" + Guid.NewGuid().ToString("N"));
	//The recents get their own temp root, not one under `_folder`: a case asserts
	//that `_folder` holds nothing the hook was not asked to create.
	private readonly string _recents = Path.Combine(Path.GetTempPath(), "mesen-1182-recents-" + Guid.NewGuid().ToString("N"));
	private readonly string? _recentFolderOverride = ConfigManager.RecentGamesFolderOverride;
	private readonly List<MainWindow> _windows = new();

	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };
	private static readonly Dictionary<ushort, string> Backend = ButtonNames.Select((name, i) => (Code: (ushort)(0x1000 + i), Name: "Pad1 " + name)).ToDictionary(p => p.Code, p => p.Name);
	private static readonly Dictionary<string, ushort> BackendCodes = Backend.ToDictionary(p => p.Value, p => p.Key);

	//The backend's real keyboard names and codes (Core/Shared/KeyDefinitions.h).
	private static readonly Dictionary<string, ushort> KeyboardCodes = new() { { "Up Arrow", 24 }, { "Down Arrow", 26 }, { "Enter", 6 }, { "Esc", 13 }, { "1", 35 } };
	private static ushort KeyboardCode(string name) => KeyboardCodes.TryGetValue(name, out ushort code) ? code : BackendCode(name);

	private static string BackendName(ushort code) => Backend.TryGetValue(code, out string? name) ? name : "";
	private static ushort BackendCode(string name) => BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	public GuiTestHookTests()
	{
		Directory.CreateDirectory(_folder);
		//#1017's rule, for this class: the recents these cases stamp - and the
		//ones ShowFreshHome clears to get a home with no play history - live in
		//this test's own folder. Without the override, `ShowFreshHome` would
		//delete every `*.rgd` in the developer's real RecentGames folder on a
		//local run (the headless host only redirects it when it can seed a
		//portable settings.json, and a read-only output folder falls back to the
		//real home). `The_recents_folder_is_not_the_developers_real_one` asserts
		//the redirect, and this class is in the serial collection with the other
		//classes that override it.
		ConfigManager.RecentGamesFolderOverride = _recents;
		Directory.CreateDirectory(ConfigManager.RecentGamesFolder);
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitFor(() => !EmuApi.IsRunning(), "the previous case's game never stopped");
		}
	}

	public void Dispose()
	{
		PlayPadNavigationWiring.SetKeyLookupsForTest(null, null);
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();
		string recents = ConfigManager.RecentGamesFolder;
		if(Directory.Exists(recents)) {
			foreach(string file in Directory.GetFiles(recents, "*.rgd")) {
				File.Delete(file);
			}
		}
		ConfigManager.RecentGamesFolderOverride = _recentFolderOverride;
		try {
			Directory.Delete(_recents, true);
		} catch(IOException) {
		}
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Save();
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private static void WaitFor(Func<bool> condition, string failure, int timeoutMilliseconds = 30000)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > timeoutMilliseconds) {
				throw new XunitException(failure);
			}
			Pump();
			Thread.Sleep(20);
		}
		Pump();
	}

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	//The Play home with a recent game, Continue focused, and no pad: the pad count
	//is 1 only so the bridge treats the home as drivable, never a real device.
	private (MainWindow Window, MainWindowViewModel Model) ShowHome()
	{
		string recents = ConfigManager.RecentGamesFolder;
		Directory.CreateDirectory(recents);
		string file = Path.Combine(recents, "Contra.rgd");
		File.WriteAllText(file, "");
		File.SetLastWriteTime(file, DateTime.Now);

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		model.ConnectedGamepadCount = () => 1;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		WaitFor(() => Focus(window, model) == "play.home.continue", "the home opened without Continue focused");
		return (window, model);
	}

	//The Play home on a fresh settings folder: no play history, so there is no Continue
	//card and the ring is on Open a ROM from launch - the profile the pad-only script's
	//`fresh` fixture seeds. ShowHome's recent game would put the ring on Continue.
	private (MainWindow Window, MainWindowViewModel Model) ShowFreshHome()
	{
		string recents = ConfigManager.RecentGamesFolder;
		if(Directory.Exists(recents)) {
			foreach(string stale in Directory.GetFiles(recents, "*.rgd")) {
				File.Delete(stale);
			}
		}
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		MainWindow window = new();
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		model.ConnectedGamepadCount = () => 1;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		WaitFor(() => Focus(window, model) == "play.home.open-rom", "a fresh home did not put the ring on Open a ROM");
		return (window, model);
	}

	private static string? Focus(MainWindow window, MainWindowViewModel model)
	{
		return new TestHookWiring.WindowTarget(window).State()["focus"]?.GetValue<string>();
	}

	//One bridge tick the way the production timer does it: the pressed set read
	//back from the native core, then the hook's own tick counted.
	private static void Tick(MainWindow window, TestHookKeys keys)
	{
		PlayPadNavigationWiring.TickForTest(window, InputApi.GetPressedKeys(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
		keys.Advance();
	}

	private static TestHookKeys NewKeys() => new(InputApi.SetInjectedKey, BackendCode, () => null);

	//#1017's rule, asserted here too: ShowFreshHome clears `*.rgd` to get a home
	//with no play history, so the folder it clears has to be this test's own and
	//never the developer's real RecentGames. Needs no window and no native core,
	//so it is also the one case of this class that cannot pass vacuously on the
	//core-less CI runner.
	[Fact]
	public void The_recents_folder_is_not_the_developers_real_one()
	{
		Assert.NotEqual(Path.Combine(ConfigManager.HomeFolder, "RecentGames"), ConfigManager.RecentGamesFolder);
		Assert.StartsWith(_recents, ConfigManager.RecentGamesFolder);
	}

	[AvaloniaFact]
	public void Injected_pad_presses_move_the_Home_focus_with_no_pad_attached()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowHome();
		TestHookKeys keys = NewKeys();
		TestHookProtocol hook = new("t", new TestHookWiring.WindowTarget(window), keys);
		Assert.Empty(InputApi.GetPressedKeys());
		try {
			JsonObject answer = JsonNode.Parse(hook.Handle("{\"id\":1,\"token\":\"t\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Up\",\"ticks\":1}}"))!.AsObject();
			Assert.True(answer["ok"]!.GetValue<bool>(), answer.ToJsonString());
			//The press is in the host set the GUI polls, not just in the hook.
			Assert.Equal(new[] { BackendCode("Pad1 Up") }, InputApi.GetPressedKeys());
			Tick(window, keys);
			Assert.Empty(InputApi.GetPressedKeys());
			Tick(window, keys);
			Assert.Equal(2, keys.Tick);
			JsonObject state = JsonNode.Parse(hook.Handle("{\"id\":2,\"token\":\"t\",\"op\":\"state\"}"))!.AsObject();
			Assert.Equal("play.home.open-rom", state["focus"]!.GetValue<string>());
			Assert.Equal("play.home", state["screen"]!.GetValue<string>());
			Assert.Equal(2, state["tick"]!.GetValue<long>());
			Assert.Contains(state["controls"]!.AsArray(), c => c!["id"]!.GetValue<string>() == "play.home.open-rom" && c["focused"]!.GetValue<bool>());
		} finally {
			keys.ReleaseAll();
		}
	}

	//The GUI keyboard reads Avalonia KeyDown/KeyUp, not the pressed set (MainWindow
	//OnPreviewKeyDown): a key.press that only reached the set moved no focus.
	[AvaloniaFact]
	public void Injected_key_presses_move_the_Home_focus_through_the_GUI_keyboard()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowHome();
		TestHookWiring.WindowTarget target = new(window);
		TestHookKeys keys = new(InputApi.SetInjectedKey, KeyboardCode, () => null, target.RaiseKey);
		TestHookProtocol hook = new("t", target, keys);
		try {
			JsonObject answer = JsonNode.Parse(hook.Handle("{\"id\":1,\"token\":\"t\",\"op\":\"inject\",\"action\":\"key.press\",\"args\":{\"key\":\"Up Arrow\",\"ticks\":1}}"))!.AsObject();
			Assert.True(answer["ok"]!.GetValue<bool>(), answer.ToJsonString());
			Pump();
			Tick(window, keys);
			Assert.Equal("play.home.open-rom", Focus(window, model));
		} finally {
			keys.ReleaseAll();
		}
	}

	[AvaloniaFact]
	public void An_injected_key_reads_as_pressed_through_IsKeyPressed_too()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ushort code = BackendCode("Pad1 Up");
		Assert.False(InputApi.IsKeyPressed(code));
		InputApi.SetInjectedKey(code, true);
		try {
			//ShortcutKeyHandler and BaseControlDevice::SetPressedState probe this
			//one, not the set: a press that only the set carries never reaches a game.
			Assert.True(InputApi.IsKeyPressed(code));
		} finally {
			InputApi.SetInjectedKey(code, false);
		}
		Assert.False(InputApi.IsKeyPressed(code));
	}

	[AvaloniaFact]
	public void Without_the_flag_the_hook_is_inert()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowHome();
		string endpoint = Path.Combine(_folder, "never.sock");

		Assert.Null(TestHookWiring.Start(new[] { "game.nes", "--test-hook-token=t" }, window, BackendCode));
		Assert.False(TestHookWiring.Running);
		Assert.False(File.Exists(endpoint));
		Assert.False(Directory.EnumerateFileSystemEntries(_folder).Any(), "no endpoint, no directory, nothing created");

		//Injected input is ignored: nothing holds a key, the tick counts nothing and
		//a bridge tick leaves the focus where it was.
		TestHookWiring.Advance();
		Assert.Empty(InputApi.GetPressedKeys());
		PlayPadNavigationWiring.TickForTest(window, InputApi.GetPressedKeys(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
		Assert.Equal("play.home.continue", Focus(window, model));

		//And the flag with no endpoint is a startup failure, not a run without a hook.
		Assert.Throws<ArgumentException>(() => TestHookWiring.Start(new[] { "--test-hook=" }, window, BackendCode));
	}

	[AvaloniaFact]
	public void A_capture_with_a_game_loaded_is_refused_not_a_black_game_area()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowHome();
		TestHookProtocol hook = new("t", new TestHookWiring.WindowTarget(window, () => true), NewKeys());
		string path = Path.Combine(_folder, "shots", "game.png");

		JsonObject answer = JsonNode.Parse(hook.Handle("{\"id\":4,\"token\":\"t\",\"op\":\"capture\",\"path\":" + JsonValue.Create(path)!.ToJsonString() + "}"))!.AsObject();
		Assert.False(answer["ok"]!.GetValue<bool>(), answer.ToJsonString());
		Assert.Contains("game is loaded", answer["error"]!.GetValue<string>());
		Assert.False(File.Exists(path));
	}

	[AvaloniaFact]
	public void A_capture_is_the_application_window_and_nothing_else()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowHome();
		TestHookKeys keys = NewKeys();
		TestHookProtocol hook = new("t", new TestHookWiring.WindowTarget(window, () => false), keys);
		string path = Path.Combine(_folder, "shots", "home.png");

		JsonObject answer = JsonNode.Parse(hook.Handle("{\"id\":3,\"token\":\"t\",\"op\":\"capture\",\"path\":" + JsonValue.Create(path)!.ToJsonString() + "}"))!.AsObject();
		Assert.True(answer["ok"]!.GetValue<bool>(), answer.ToJsonString());
		byte[] png = File.ReadAllBytes(path);
		Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
		//The picture is exactly the window's own surface: its size is the window's
		//size at the window's scale, which a desktop grab never is.
		double scale = window.RenderScaling;
		Assert.Equal((int)Math.Ceiling(window.Bounds.Width * scale), answer["size"]![0]!.GetValue<int>());
		Assert.Equal((int)Math.Ceiling(window.Bounds.Height * scale), answer["size"]![1]!.GetValue<int>());
		using Avalonia.Media.Imaging.Bitmap decoded = new(path);
		Assert.Equal(answer["size"]![0]!.GetValue<int>(), decoded.PixelSize.Width);
		Assert.Equal(answer["size"]![1]!.GetValue<int>(), decoded.PixelSize.Height);
		Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)).ToLowerInvariant(), answer["sha256"]!.GetValue<string>());
	}

	//The case for the GUI test squad's e2e suite (#1179/#1183 wire it): a runner
	//that knows only the socket says hello, reads the focus, presses Up, waits in
	//ticks for the focus to move, captures, and quits.
	[AvaloniaFact]
	public void GuiTestHook_e2e_a_runner_drives_the_Home_over_the_socket()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		if(OperatingSystem.IsWindows()) {
			Assert.Skip("the e2e case speaks the Unix socket; the Windows named pipe has no headless case yet");
		}
		(MainWindow window, MainWindowViewModel model) = ShowHome();
		//A Unix socket path is capped at ~104 characters, so not under the long temp folder.
		string endpoint = Path.Combine("/tmp", "m1182-" + Guid.NewGuid().ToString("N").Substring(0, 8), "hook.sock");
		string shot = Path.Combine(_folder, "e2e.png");
		//quit closes the window the way Exit does; the headless host must not run the
		//real core release with it (exit 139), as Dispose does for every window.
		window.ReleaseCore = () => { };
		using IDisposable? hook = TestHookWiring.Start(new[] { "--test-hook=" + endpoint, "--test-hook-token=s3" }, window, BackendCode);
		Assert.NotNull(hook);
		Assert.True(TestHookWiring.Running);
		PlayPadNavigationWiring.SetKeyLookupsForTest(BackendName, BackendCode);

		//Everything installed above is torn down in `finally`: an asserting case
		//must not leave the test key lookups installed for the next case in this
		//process, nor the socket folder under /tmp - the class's Dispose clears
		//the window, the config and the temp folder, never either of these.
		try {
			Task<List<JsonObject>> runner = Task.Run(() => {
				List<JsonObject> answers = new();
				using Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				client.Connect(new UnixDomainSocketEndPoint(endpoint));
				using NetworkStream stream = new(client);
				using StreamReader reader = new(stream, Encoding.UTF8);
				using StreamWriter writer = new(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
				JsonObject Ask(string body)
				{
					writer.WriteLine("{\"id\":" + (answers.Count + 1) + ",\"token\":\"s3\"," + body + "}");
					JsonObject answer = JsonNode.Parse(reader.ReadLine()!)!.AsObject();
					answers.Add(answer);
					return answer;
				}
				Ask("\"op\":\"hello\"");
				Ask("\"op\":\"state\"");
				Ask("\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"Up\",\"ticks\":2}");
				long start = Ask("\"op\":\"state\"")["tick"]!.GetValue<long>();
				JsonObject now = answers[^1];
				while(now["focus"]?.GetValue<string>() != "play.home.open-rom" && now["tick"]!.GetValue<long>() < start + 200) {
					Thread.Sleep(20);
					now = Ask("\"op\":\"state\"");
				}
				Ask("\"op\":\"capture\",\"path\":" + JsonValue.Create(shot)!.ToJsonString());
				Ask("\"op\":\"quit\"");
				return answers;
			});

			//The UI thread is this one: it serves the runner's requests (the server
			//marshals them here) and runs the bridge's own 50 ms timer, whose tick reads
			//the pressed set and counts the hook's ticks - the production path.
			Stopwatch clock = Stopwatch.StartNew();
			while(!runner.IsCompleted) {
				if(clock.ElapsedMilliseconds > 30000) {
					throw new XunitException("the runner never finished");
				}
				Pump();
				Thread.Sleep(20);
			}
			List<JsonObject> answers = runner.GetAwaiter().GetResult();

			Assert.All(answers, a => Assert.True(a["ok"]!.GetValue<bool>(), a.ToJsonString()));
			Assert.Equal("play.home.continue", answers[1]["focus"]!.GetValue<string>());
			Assert.Equal("play.home.open-rom", answers[^3]["focus"]!.GetValue<string>());
			Assert.True(File.Exists(shot));
			hook!.Dispose();
		} finally {
			PlayPadNavigationWiring.SetKeyLookupsForTest(null, null);
			Directory.Delete(Path.GetDirectoryName(endpoint)!, true);
		}
	}

	//#1242 acceptance criterion 3: every automated step the pad-only script's Home and
	//Library batches name, driven over the socket and on the profile the script's
	//`fresh` fixture seeds - no play history, so the home's ring is on its one action
	//from launch (`home.first-focus`, and the `lib.home-ring` setup step the library
	//batch opens with), A on it opens the library sheet (`home.open-library`,
	//`lib.reach-library`, whose `lib.open-screen` is the same wait's screen), and B
	//returns to the home with the ring back on the control that opened the sheet
	//(`lib.back-to-home`, `lib.back-ring-on-opener`). The issue warns the press may open
	//a picker instead of the library; the state's `dialogs` is what would say so, so it
	//is asserted empty too.
	[AvaloniaFact]
	public void GuiTestHook_e2e_A_on_the_fresh_home_opens_the_library_over_the_socket()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		if(OperatingSystem.IsWindows()) {
			Assert.Skip("the e2e case speaks the Unix socket; the Windows named pipe has no headless case yet");
		}
		(MainWindow window, MainWindowViewModel model) = ShowFreshHome();
		string endpoint = Path.Combine("/tmp", "m1242-" + Guid.NewGuid().ToString("N").Substring(0, 8), "hook.sock");
		window.ReleaseCore = () => { };
		using IDisposable? hook = TestHookWiring.Start(new[] { "--test-hook=" + endpoint, "--test-hook-token=s3" }, window, BackendCode);
		Assert.NotNull(hook);
		Assert.True(TestHookWiring.Running);
		PlayPadNavigationWiring.SetKeyLookupsForTest(BackendName, BackendCode);

		//Torn down in `finally`, like the case above: a failing assert must not leave
		//the test key lookups installed for the next case, nor the socket folder.
		try {
			Task<(List<JsonObject> Answers, JsonObject Ring, JsonObject Library, JsonObject BackHome)> runner = Task.Run(() => {
				List<JsonObject> answers = new();
				using Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				client.Connect(new UnixDomainSocketEndPoint(endpoint));
				using NetworkStream stream = new(client);
				using StreamReader reader = new(stream, Encoding.UTF8);
				using StreamWriter writer = new(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
				JsonObject Ask(string body)
				{
					writer.WriteLine("{\"id\":" + (answers.Count + 1) + ",\"token\":\"s3\"," + body + "}");
					JsonObject answer = JsonNode.Parse(reader.ReadLine()!)!.AsObject();
					answers.Add(answer);
					return answer;
				}
				//The wait a step's `wait.check` is: poll the state in ticks until the
				//check holds, the way the supervised runner polls the hook.
				JsonObject Wait(Func<JsonObject, bool> until)
				{
					JsonObject now = Ask("\"op\":\"state\"");
					long start = now["tick"]!.GetValue<long>();
					while(!until(now) && now["tick"]!.GetValue<long>() < start + 120) {
						Thread.Sleep(20);
						now = Ask("\"op\":\"state\"");
					}
					return now;
				}
				Ask("\"op\":\"hello\"");
				//home.first-focus, lib.home-ring: the ring at a fresh launch.
				JsonObject ring = Wait(s => s["focus"]?.GetValue<string>() == "play.home.open-rom");
				//home.open-library, lib.reach-library: A on the focused Open a ROM.
				Ask("\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"A\",\"ticks\":4}");
				//lib.open-screen: the sheet's own screen, not a picker.
				JsonObject library = Wait(s => s["screen"]?.GetValue<string>() == "play.library");
				//lib.back-to-home, lib.back-ring-on-opener: B out of the sheet.
				Ask("\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"B\",\"ticks\":4}");
				JsonObject backHome = Wait(s => s["screen"]?.GetValue<string>() == "play.home"
					&& s["focus"]?.GetValue<string>() == "play.home.open-rom");
				Ask("\"op\":\"quit\"");
				return (answers, ring, library, backHome);
			});

			Stopwatch clock = Stopwatch.StartNew();
			while(!runner.IsCompleted) {
				if(clock.ElapsedMilliseconds > 30000) {
					throw new XunitException("the runner never finished");
				}
				Pump();
				Thread.Sleep(20);
			}
			(List<JsonObject> answers, JsonObject ring, JsonObject library, JsonObject backHome) = runner.GetAwaiter().GetResult();

			Assert.All(answers, a => Assert.True(a["ok"]!.GetValue<bool>(), a.ToJsonString()));
			Assert.True(ring["focus"]?.GetValue<string>() == "play.home.open-rom",
				"home.first-focus/lib.home-ring: " + ring.ToJsonString());
			Assert.True(ring["screen"]?.GetValue<string>() == "play.home", ring.ToJsonString());
			Assert.True(library["screen"]?.GetValue<string>() == "play.library",
				"home.open-library/lib.reach-library/lib.open-screen: " + library.ToJsonString());
			Assert.Empty(library["dialogs"]!.AsArray());
			Assert.True(backHome["screen"]?.GetValue<string>() == "play.home",
				"lib.back-to-home: " + backHome.ToJsonString());
			Assert.True(backHome["focus"]?.GetValue<string>() == "play.home.open-rom",
				"lib.back-ring-on-opener: " + backHome.ToJsonString());
			hook!.Dispose();
		} finally {
			PlayPadNavigationWiring.SetKeyLookupsForTest(null, null);
			Directory.Delete(Path.GetDirectoryName(endpoint)!, true);
		}
	}
}
