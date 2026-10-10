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
using Avalonia.Controls;
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

//#1281: the input actions beside pad.press, wired to a real MainWindow - pad.hold,
//pad.release, text.type and the simulated hot-plug of pad.connect/pad.disconnect.
//The protocol's own reading of them (the wire shape, the refusals, the tick
//counter) is pinned host-free in UI.Tests/TestHook; what only a window can show is
//that a hold really reaches the pad bridge and moves the ring, that a release
//stops it, that text.type goes through the application's OWN on-screen keyboard
//(ADR-0262) and into the field the player sees, and that a script's connect moves
//the connected-pad count the window itself polls - the port lamps and the pad-loss
//pause read that count, so it is read back off the view model here.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so every case self-skips
//on the core-less CI runner; the evidence for them is a local run with
//MESEN_CORE_LIB set (see UI.HeadlessTests/AGENTS.md).
[Collection(NativeCoreCollection.Name)]
public class GuiTestHookInputActionsTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly List<MainWindow> _windows = new();

	//The backend a headless build does not have, built the way the real ones are
	//("Pad{N} {Button}") - the same stand-in PlayPadKeyboardTests uses.
	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };
	private static readonly Dictionary<ushort, string> Backend = ButtonNames.Select((name, i) => (Code: (ushort)(0x1000 + i), Name: "Pad1 " + name)).ToDictionary(p => p.Code, p => p.Name);
	private static readonly Dictionary<string, ushort> BackendCodes = Backend.ToDictionary(p => p.Value, p => p.Key);

	private static string BackendName(ushort code) => Backend.TryGetValue(code, out string? name) ? name : "";
	private static ushort BackendCode(string name) => BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	public void Dispose()
	{
		PlayPadNavigationWiring.SetKeyLookupsForTest(null, null);
		PlayPadNavigationWiring.SetOverlayLookupForTest(null);
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Save();
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
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

	//The only place these cases touch time: one bridge tick, the way the production
	//50 ms timer does it (the pressed set read back from the native core, then the
	//hook's own tick counted).
	private static void Tick(MainWindow window, TestHookKeys keys)
	{
		PlayPadNavigationWiring.TickForTest(window, InputApi.GetPressedKeys(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
		keys.Advance();
	}

	//The hook the wiring builds without a socket: the same keys, the same target.
	private static TestHookKeys NewKeys() => new(InputApi.SetInjectedKey, BackendCode, () => null);

	private static TestHookProtocol NewHook(MainWindow window, TestHookKeys keys)
	{
		return new TestHookProtocol("t", new TestHookWiring.WindowTarget(window), keys);
	}

	//The step, as a script writes it, through the protocol the socket serves.
	private static JsonObject Step(TestHookProtocol hook, int id, string action, string args)
	{
		return JsonNode.Parse(hook.Handle("{\"id\":" + id + ",\"token\":\"t\",\"op\":\"inject\",\"action\":\"" + action + "\",\"args\":" + args + "}"))!.AsObject();
	}

	private static bool Pressed(ushort code) => InputApi.GetPressedKeys().Contains(code);

	//The Play home with a recent game and Continue focused - GuiTestHookTests'
	//profile, and the one the pad-only script's Home batch runs on.
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
		WaitFor(() => Focus(window) == "play.home.continue", "the home opened without Continue focused");
		return (window, model);
	}

	private static string? Focus(MainWindow window)
	{
		return new TestHookWiring.WindowTarget(window).State()["focus"]?.GetValue<string>();
	}

	//The Cheats sheet with its search field shown - the text field the pad's A
	//opens the on-screen keyboard on (ADR-0262), which is what text.type drives.
	private (MainWindow Window, MainWindowViewModel Model) ShowCheatsSearch()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		MainWindow window = new();
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.CommunityCheatsLastKnown = () => Array.Empty<CommunityCheatGame>();
		model.CommunityCheatsSource = () => Task.FromResult<IReadOnlyList<CommunityCheatGame>?>(Array.Empty<CommunityCheatGame>());
		CheatDbGame contra = new("Contra (USA)", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", new[] { new CheatDbCode("Infinite lives - 1P game", "SZKGPAVG") });
		model.CheatsSheet.Open(ConsoleType.Nes, contra.Sha1, new[] { contra }, Array.Empty<StoredCheat>(), recordingArt: false, disableAll: false, _ => { });
		Pump();
		Assert.True(window.FindNamed<TextBox>("CheatsSearchBox").IsOnScreen(), "the Cheats sheet did not open");
		return (window, model);
	}

	private void Press(MainWindow window, PadNavAction action)
	{
		PadNavMapping mapping = PadNavControls.Resolve(PadFamily.Xbox, 0, BackendCode) ?? throw new XunitException("the stand-in table does not answer the Xbox preset's names");
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(mapping, action) }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
	}

	[AvaloniaFact]
	public void GuiTestHook_hold_goes_down_and_lets_go_on_its_own_after_its_ticks()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowHome();
		TestHookKeys keys = NewKeys();
		TestHookProtocol hook = NewHook(window, keys);
		ushort up = BackendCode("Pad1 Up");
		try {
			Assert.True(Step(hook, 1, "pad.hold", "{\"button\":\"Up\",\"ticks\":4}")["ok"]!.GetValue<bool>());
			//The press is the pad's own: it reaches the bridge through the same
			//pressed set a real pad writes, never a synthetic OS event.
			Assert.True(Pressed(up));
			Tick(window, keys);
			//Two ticks in, with two still to run, the button is still down.
			Tick(window, keys);
			Assert.True(Pressed(up));
			Tick(window, keys);
			Tick(window, keys);
			Assert.False(Pressed(up));
		} finally {
			keys.ReleaseAll();
		}
	}

	[AvaloniaFact]
	public void GuiTestHook_a_hold_walks_the_Home_and_its_repeat_rides_the_ticks()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowHome();
		TestHookKeys keys = NewKeys();
		TestHookProtocol hook = NewHook(window, keys);
		ushort up = BackendCode("Pad1 Up");
		try {
			//Up from Continue is Open a ROM (the pad-only script's first step): a
			//hold is a step the bridge applies like any other press.
			Assert.True(Step(hook, 1, "pad.hold", "{\"button\":\"Up\",\"ticks\":30}")["ok"]!.GetValue<bool>());
			Tick(window, keys);
			Assert.Equal("play.home.open-rom", Focus(window));
			for(int i = 0; i < 10; i++) {
				Tick(window, keys);
			}
			//11 ticks into a 30-tick hold the button is still down, which is what
			//makes it a hold and not a press: pad.press would have let go by now.
			Assert.True(Pressed(up));
		} finally {
			keys.ReleaseAll();
		}
	}

	[AvaloniaFact]
	public void GuiTestHook_release_ends_a_hold_before_its_ticks_run_out()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowHome();
		TestHookKeys keys = NewKeys();
		TestHookProtocol hook = NewHook(window, keys);
		ushort up = BackendCode("Pad1 Up");
		try {
			Assert.True(Step(hook, 1, "pad.hold", "{\"button\":\"Up\",\"ticks\":200}")["ok"]!.GetValue<bool>());
			Tick(window, keys);
			Assert.Equal("play.home.open-rom", Focus(window));
			Assert.True(Step(hook, 2, "pad.release", "{\"button\":\"Up\"}")["ok"]!.GetValue<bool>());
			Assert.False(Pressed(up));
			for(int i = 0; i < 12; i++) {
				Tick(window, keys);
			}
			//The hold had 200 ticks to run and the bridge is still ticking; the ring
			//is where the release left it, and the button is out of the pressed set
			//(asserted above) - that is the release ending the hold, not the hold
			//running out.
			Assert.Equal("play.home.open-rom", Focus(window));
		} finally {
			keys.ReleaseAll();
		}
	}

	[AvaloniaFact]
	public void GuiTestHook_text_type_goes_through_the_on_screen_keyboard_into_the_field()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowCheatsSearch();
		PlayPadNavigationWiring.SetKeyLookupsForTest(BackendName, BackendCode);
		TestHookKeys keys = NewKeys();
		TestHookProtocol hook = NewHook(window, keys);
		try {
			window.FindNamed<TextBox>("CheatsSearchBox").Focus();
			Pump();
			//A on the text field is what opens the pad keyboard, exactly as a player does it.
			Press(window, PadNavAction.Confirm);
			Assert.NotNull(PlayPadNavigationWiring.KeyboardForTest(window));

			Assert.True(Step(hook, 1, "text.type", "{\"text\":\"lives\"}")["ok"]!.GetValue<bool>());
			//The value comes from the field the player sees, not from a pixel.
			Assert.Equal("lives", window.FindNamed<TextBox>("CheatsSearchBox").Text);

			//And a step the keyboard has no key for fails the step instead of typing
			//something else (the on-screen keyboard is the only path there is).
			JsonObject refused = Step(hook, 2, "text.type", "{\"text\":\"日本\"}");
			Assert.False(refused["ok"]!.GetValue<bool>());
			Assert.Contains("no key", refused["error"]!.GetValue<string>());
		} finally {
			keys.ReleaseAll();
		}
	}

	[AvaloniaFact]
	public void GuiTestHook_text_type_into_a_field_at_its_limit_fails_the_step()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowCheatsSearch();
		PlayPadNavigationWiring.SetKeyLookupsForTest(BackendName, BackendCode);
		TestHookKeys keys = NewKeys();
		TestHookProtocol hook = NewHook(window, keys);
		try {
			//A field that is already full. The pad keyboard takes the draft it opens
			//on (PadKeyboard), and PressKey answers None once the draft is at the
			//field's own MaxLength - the key press is swallowed and the draft does not
			//move. A step that reports the text as typed would be lying about a field
			//the player can see is unchanged (#1281 review, finding 2).
			TextBox field = window.FindNamed<TextBox>("CheatsSearchBox");
			field.MaxLength = 5;
			field.Text = "lives";
			field.Focus();
			Pump();
			Press(window, PadNavAction.Confirm);
			Assert.NotNull(PlayPadNavigationWiring.KeyboardForTest(window));

			JsonObject refused = Step(hook, 1, "text.type", "{\"text\":\"z\"}");
			Assert.False(refused["ok"]!.GetValue<bool>());
			Assert.Contains("did not accept", refused["error"]!.GetValue<string>());
			//And the field is exactly where it was: the step failed because nothing
			//was typed, not because something else was.
			Assert.Equal("lives", field.Text);
		} finally {
			keys.ReleaseAll();
		}
	}

	//The simulated hot-plug, over the socket, through the production wiring
	//(TestHookWiring.Start), because the count it swaps is the wiring's own: the
	//window reads ConnectedGamepadCount every tick for the port lamps and the
	//pad-loss pause, and it must read the hook's pads while a run is up and the
	//backend's own again when it ends. #1255's two rules are asserted here too:
	//Start is what arms them, and the window is still shown without activation.
	[AvaloniaFact]
	public void GuiTestHook_a_script_hot_plugs_pads_and_the_window_reads_them()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		if(OperatingSystem.IsWindows()) {
			Assert.Skip("the case speaks the Unix socket; the Windows named pipe has no headless case yet");
		}
		(MainWindow window, MainWindowViewModel model) = ShowHome();
		string endpoint = Path.Combine("/tmp", "m1281-" + Guid.NewGuid().ToString("N").Substring(0, 8), "hook.sock");
		window.ReleaseCore = () => { };
		using IDisposable? hook = TestHookWiring.Start(new[] { "--test-hook=" + endpoint, "--test-hook-token=s1" }, window, BackendCode);
		Assert.NotNull(hook);
		//#1255 keeps holding: a test run never takes the focus of whoever is typing.
		Assert.False(window.ShowActivated);

		//The runner and the UI thread hand each state over instead of racing: the
		//runner says which state it just asked for and waits for the read, so the
		//count is read while that state is up, never after the next step.
		//A one-element box: a lambda cannot take `ref` to a captured local.
		int[] stage = new int[1];
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
					writer.WriteLine("{\"id\":" + (answers.Count + 1) + ",\"token\":\"s1\"," + body + "}");
					JsonObject answer = JsonNode.Parse(reader.ReadLine()!)!.AsObject();
					answers.Add(answer);
					return answer;
				}
				//The handover: say which state is up, then wait for the read.
				void Wait(int from, int to)
				{
					Volatile.Write(ref stage[0], from);
					while(Volatile.Read(ref stage[0]) < to) {
						Thread.Sleep(5);
					}
				}

				Ask("\"op\":\"hello\"");
				//Both ports: ShowHome's backend stand-in says one pad is plugged in,
				//so a count of two can only be the script's own set.
				Ask("\"op\":\"inject\",\"action\":\"pad.connect\",\"args\":{\"index\":0,\"family\":\"xbox\"}");
				Ask("\"op\":\"inject\",\"action\":\"pad.connect\",\"args\":{\"index\":1,\"family\":\"playstation\"}");
				Wait(1, 2);
				//Both out again: a run that unplugged every pad reads zero, never the
				//machine's own count - that is what makes the count the script's.
				Ask("\"op\":\"inject\",\"action\":\"pad.disconnect\",\"args\":{\"index\":0}");
				Ask("\"op\":\"inject\",\"action\":\"pad.disconnect\",\"args\":{\"index\":1}");
				Wait(3, 4);
				//Plugged back in with no family named: the default one, which is
				//the spelling this stand-in backend defines (the PlayStation
				//spelling of a second pad is pinned host-free in UI.Tests/TestHook,
				//where the table can carry both).
				Ask("\"op\":\"inject\",\"action\":\"pad.connect\",\"args\":{\"index\":0}");
				Ask("\"op\":\"quit\"");
				return answers;
			});

			uint both = 0, none = 1;
			Stopwatch clock = Stopwatch.StartNew();
			while(!runner.IsCompleted) {
				if(clock.ElapsedMilliseconds > 30000) {
					throw new XunitException("the runner never finished");
				}
				//What the port lamps poll, read where they read it - the view model's
				//own count, on the thread that polls it.
				int now = Volatile.Read(ref stage[0]);
				if(now == 1) {
					both = model.ConnectedGamepadCount();
					Volatile.Write(ref stage[0], 2);
				} else if(now == 3) {
					none = model.ConnectedGamepadCount();
					Volatile.Write(ref stage[0], 4);
				}
				Pump();
				Thread.Sleep(5);
			}
			List<JsonObject> answers = runner.GetAwaiter().GetResult();
			Assert.All(answers, a => Assert.True(a["ok"]!.GetValue<bool>(), a.ToJsonString()));
			Assert.Equal(2u, both);
			Assert.Equal(0u, none);
			hook!.Dispose();
			//The run is over: the window is back on the backend's own count.
			Assert.Equal(1u, model.ConnectedGamepadCount());
		} finally {
			Directory.Delete(Path.GetDirectoryName(endpoint)!, true);
		}
	}
}
