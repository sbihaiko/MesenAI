using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
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

//#1228: the GUI test suite's step `home.open-library` of the play-pad-only
//script (docs/validation/process/play-pad-only.gui-test.json, batch `home`) -
//"A on Open a ROM opens the library sheet (LIB-01)" - read the way the runner
//reads it: the press is injected through the hook, and the verdict is the
//hook's own `ui.screen == play.library` (ADR-0271, ADR-0272 item 3).
//
//Two things have to hold for the step to pass, and this case pins both: the A
//press on the home's own action really opens the in-app ROM picker (#845,
//ADR-0256 Decision 9), and the state the runner reads names the surface that is
//up. A press that works while the hook still answers `play.home` reads to the
//runner exactly like a press that did nothing - which is the failure #1228
//reports.
//
//The press reaches the real native pressed-key set (InputApi.SetInjectedKey ->
//KeyManager::GetPressedKeys) and the bridge reads it back from there; only the
//backend's key-name table is a stand-in, because a headless build has no key
//manager (see PlayPadNavigationWiring.TickForTest). Every case asserts
//NativeCore.IsAvailable first and SKIPS with its reason when the library is
//absent - a skip, not a pass: the evidence is a local run with MESEN_CORE_LIB.
[Collection(NativeCoreCollection.Name)]
public class PlayPadOnlyHomeOpenLibraryTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly List<MainWindow> _windows = new();

	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };
	private static readonly Dictionary<ushort, string> Backend = ButtonNames.Select((name, i) => (Code: (ushort)(0x1000 + i), Name: "Pad1 " + name)).ToDictionary(p => p.Code, p => p.Name);
	private static readonly Dictionary<string, ushort> BackendCodes = Backend.ToDictionary(p => p.Value, p => p.Key);

	private static string BackendName(ushort code) => Backend.TryGetValue(code, out string? name) ? name : "";
	private static ushort BackendCode(string name) => BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	public PlayPadOnlyHomeOpenLibraryTests()
	{
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
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Save();
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

	//The first-run Play home (W-P1) the script's fixture `settings.profiles.fresh`
	//stands for: no recent game, so *Open a ROM…* is the home's one control and
	//the focus is already on it (HOME-01, `ui.focused == play.home.open-rom`).
	//The pad count is 1 only so the bridge treats the home as drivable, never a
	//real device.
	private (MainWindow Window, MainWindowViewModel Model) ShowFirstRunHome()
	{
		string recents = ConfigManager.RecentGamesFolder;
		Directory.CreateDirectory(recents);
		foreach(string stale in Directory.GetFiles(recents, "*.rgd")) {
			File.Delete(stale);
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
		WaitFor(() => Focus(window) == "play.home.open-rom", "the first-run home opened without Open a ROM… focused");
		return (window, model);
	}

	private static string? Focus(MainWindow window)
	{
		return new TestHookWiring.WindowTarget(window).State()["focus"]?.GetValue<string>();
	}

	private static JsonObject State(MainWindow window)
	{
		return new TestHookWiring.WindowTarget(window).State();
	}

	//One bridge tick the way the production timer does it: the pressed set read
	//back from the native core, then the hook's own tick counted.
	private static void Tick(MainWindow window, TestHookKeys keys)
	{
		PlayPadNavigationWiring.TickForTest(window, InputApi.GetPressedKeys(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
		keys.Advance();
	}

	//The step itself: `pad.press(button="A", ticks=4)` with the ring on
	//*Open a ROM…*, then `ui.screen == play.library` within 120 ticks.
	[AvaloniaFact]
	public void Pad_A_on_the_home_open_a_rom_opens_the_library_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		TestHookKeys keys = new(InputApi.SetInjectedKey, BackendCode, () => null);
		TestHookProtocol hook = new("t", new TestHookWiring.WindowTarget(window), keys);
		Assert.Empty(InputApi.GetPressedKeys());
		try {
			JsonObject answer = JsonNode.Parse(hook.Handle("{\"id\":1,\"token\":\"t\",\"op\":\"inject\",\"action\":\"pad.press\",\"args\":{\"button\":\"A\",\"ticks\":4}}"))!.AsObject();
			Assert.True(answer["ok"]!.GetValue<bool>(), answer.ToJsonString());

			//The press is a hold: the bridge sees it while it is down and lets go
			//after its four ticks, exactly as the runner's `ticks=4` asks.
			for(int tick = 0; tick < 4; tick++) {
				Tick(window, keys);
			}
			Assert.Empty(InputApi.GetPressedKeys());

			//What the script means by "opens the library sheet" (LIB-01): the
			//in-app picker is up, on its library surface.
			Assert.True(model.RomPicker.IsVisible, "A on Open a ROM… did not open the ROM picker");
			Assert.Equal(RomPickerMode.Library, model.RomPicker.Mode);
			Assert.True(model.RomPicker.IsLibrarySurfaceVisible);

			//...and what the runner reads to decide the step: the hook names the
			//surface that is up (ADR-0272 item 3).
			JsonObject state = JsonNode.Parse(hook.Handle("{\"id\":2,\"token\":\"t\",\"op\":\"state\"}"))!.AsObject();
			Assert.Equal("play.library", state["screen"]?.GetValue<string>());
		} finally {
			keys.ReleaseAll();
		}
	}
}
