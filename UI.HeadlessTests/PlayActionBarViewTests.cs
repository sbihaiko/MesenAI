using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1104 (spec #1102, ADR-0256 Decision 6): the shared action bar on Home, the
//Open a game sheet and the pause overlay, read the way a player reads it - the
//footer text on screen after the pad has moved the focus and opened the
//surface. The naming and ordering rules are pinned host-free in
//UI.Tests/Play/PlayActionBarTests; this checks the realized window feeds them,
//through the same TickForTest door PlayPadNavigationTests uses (a headless build
//has no key manager and cannot make a pad look pressed, so the press and the
//backend's two lookups are stand-ins and everything above them is production).
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class PlayActionBarViewTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1104-" + Guid.NewGuid().ToString("N"));
	private readonly List<MainWindow> _windows = new();

	//The XInput-shaped backend names ("Pad{N} {Button}"), as PlayPadNavigationTests
	//builds them; one pad is all this suite needs.
	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };
	private static readonly Dictionary<ushort, string> Backend = ButtonNames.Select((name, i) => (Code: (ushort)(0x1000 + i), Name: "Pad1 " + name)).ToDictionary(p => p.Code, p => p.Name);
	private static readonly Dictionary<string, ushort> BackendCodes = Backend.ToDictionary(p => p.Value, p => p.Key);

	private static string BackendName(ushort code) => Backend.TryGetValue(code, out string? name) ? name : "";
	private static ushort BackendCode(string name) => BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	public PlayActionBarViewTests()
	{
		Directory.CreateDirectory(_folder);
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitFor(() => !EmuApi.IsRunning(), "the previous case's game never stopped");
		}
	}

	public void Dispose()
	{
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();
		ClearRecents();
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

	private (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
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
		return (window, model);
	}

	//Advanced UI mode on the Play workspace: IsPlayWorkspace is true but the
	//Play door (Player mode) is not the one open, so pad navigation is off.
	private (MainWindow Window, MainWindowViewModel Model) ShowAdvancedOnPlayWorkspace()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Advanced;
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
		return (window, model);
	}

	//The overlay's own button holding the focus, waited for with the decision
	//re-asked (#1129) - the one wait every W-P4 case here opens with. PlayFocusOnOpen
	//gives up after a bounded number of turns (its Attempts) when the control it
	//chose is focusable and enabled and still not yet *effectively visible*: its own
	//comment records measuring exactly that under a full headless suite. Nothing
	//re-arms the decision on its own - only a watched property changing does, and
	//every watched property these cases touch is already in the state the bar reads.
	//So a loaded dispatcher, which is what a combined run is (every earlier case's
	//window still draining its 50 ms and 100 ms timers), can spend all five turns
	//and leave the case waiting on something no later turn brings back: an
	//order-dependent failure of the *wait*, with nothing wrong with the surface the
	//case exists to check. Asking again is what any of those property changes would
	//do, so what a case asserts is that the pause overlay can hold the focus, not
	//that it won the race inside five dispatcher turns.
	private static void WaitForOverlayFocus(MainWindow window)
	{
		WaitFor(() => {
			if(Focused(window) == "OverlayResumeButton") {
				return true;
			}
			PlayPadNavigationWiring.RefreshSurfaceFocusForTest(window);
			return false;
		}, "the pause overlay did not take the focus");
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

	//One pad press and release, the way the bridge sees it: a tick with the
	//button down, then one with nothing down so the next press is a new edge.
	private static void Press(MainWindow window, string button)
	{
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, new[] { BackendCode("Pad1 " + button) }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
	}

	//A press of a button the backend names however the case says ("Joy1 Cross",
	//or a name no pad family owns), through the same tick door as Press.
	private static void PressNamed(MainWindow window, string backendName)
	{
		ushort code = 0x2000;
		string NameOf(ushort c) => c == code ? backendName : "";
		ushort CodeOf(string n) => n == backendName ? code : (ushort)0;
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), NameOf, CodeOf);
		PlayPadNavigationWiring.TickForTest(window, new[] { code }, TimeSpan.FromMilliseconds(50), NameOf, CodeOf);
		Pump();
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), NameOf, CodeOf);
		Pump();
	}

	private static string? Focused(MainWindow window) => (window.FocusManager?.GetFocusedElement() as Control)?.Name;

	//What the player reads: the bar's line, or "<hidden>" when the bar is not on
	//screen.
	//Read after one idle tick, the production timer's job: the bar is recomputed
	//on the tick, so a case that opens a surface and reads the bar straight away
	//would otherwise depend on the real timer having fired first (it does not,
	//when a previous case's window is still draining the dispatcher).
	private static string Bar(MainWindow window)
	{
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
		Border bar = window.FindNamed<Border>("PlayActionBar");
		return bar.IsEffectivelyVisible ? window.FindNamed<TextBlock>("PlayActionBarText").Text ?? "" : "<hidden>";
	}

	private static void SeedRecents(params string[] games)
	{
		string folder = ConfigManager.RecentGamesFolder;
		Directory.CreateDirectory(folder);
		foreach(string stale in Directory.GetFiles(folder, "*.rgd")) {
			File.Delete(stale);
		}
		DateTime written = DateTime.Now;
		foreach(string game in games) {
			string file = Path.Combine(folder, game + ".rgd");
			File.WriteAllText(file, "");
			File.SetLastWriteTime(file, written);
			written = written.AddMinutes(-1);
		}
	}

	private static void ClearRecents()
	{
		string folder = ConfigManager.RecentGamesFolder;
		if(Directory.Exists(folder)) {
			foreach(string file in Directory.GetFiles(folder, "*.rgd")) {
				File.Delete(file);
			}
		}
	}

	[AvaloniaFact]
	public void Home_and_the_Open_a_game_sheet_name_what_the_pad_can_do()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		SeedRecents("Contra", "Zelda", "Metroid");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.RecentGames.Init(GameScreenMode.RecentGames);
		WaitFor(() => Focused(window) == "PlayHomeContinueButton", "the home opened without Continue focused");

		//Home: A starts Continue; there is nothing to search or filter and B
		//leaves nothing, so the bar says only that.
		Press(window, "Down");
		Assert.Equal("A Play", Bar(window));
		foreach(string button in new[] { "X", "Y", "L1", "R1" }) {
			Press(window, button);
		}
		Assert.Equal("A Play", Bar(window));
		Press(window, "Up");
		Assert.Equal("PlayHomeContinueButton", Focused(window));

		//Up again reaches Open a game…, and A on it opens the sheet.
		Press(window, "Up");
		Assert.Equal("PlayHomeOpenRomSecondary", Focused(window));
		Assert.Equal("A Open a game", Bar(window));
		Press(window, "A");
		WaitFor(() => model.RomPicker.IsVisible, "A on Open a game… did not open the sheet");
		Assert.Equal("A Library folders     Y Search     LB / RB Console     B Back", Bar(window));

		//Y opens the search box and, with it, the shared on-screen keyboard: the
		//bar shows the keyboard's own entries, not "B Back" over a cancel.
		Press(window, "Y");
		Assert.NotNull(PlayPadNavigationWiring.KeyboardForTest(window));
		Assert.Equal("D-pad Move     A Press key     B Cancel", Bar(window));
		Press(window, "B");
		Assert.Null(PlayPadNavigationWiring.KeyboardForTest(window));
		Assert.Equal("A Search     LB / RB Console     B Back", Bar(window));

		//B leaves the sheet and the home's bar is back.
		Press(window, "B");
		WaitFor(() => !model.RomPicker.IsVisible, "B did not leave the Open a game sheet");
		Assert.Equal("A Play", Bar(window));
	}

	//The empty library parks the ring on Library folders…, and A there opens the
	//folders sheet - the bar may not promise Play over a header action.
	[AvaloniaFact]
	public void An_empty_library_names_Library_folders_not_Play_on_A()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		List<string>? savedFolders = prefs.LibraryFolders;
		prefs.LibraryFolders = new List<string>();
		try {
			(MainWindow window, MainWindowViewModel model) = ShowPlay();
			model.RecentGames.Init(GameScreenMode.RecentGames);
			WaitFor(() => Focused(window) == "PlayHomeOpenRomPrimary" || Focused(window) == "PlayHomeContinueButton", "the home opened without a focus");
			model.RomPicker.Open();
			WaitFor(() => model.RomPicker.IsVisible && Focused(window) == "RomPickerLibraryFolders", "the empty library did not park the ring on Library folders…");
			Press(window, "Select");
			string bar = Bar(window);
			Assert.DoesNotContain("A Play", bar);
			Assert.StartsWith("A Library folders", bar);
		} finally {
			prefs.LibraryFolders = savedFolders;
		}
	}

	[AvaloniaFact]
	public void The_pause_overlay_names_what_the_pad_can_do()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		string rom = Path.Combine(_folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");

		//A pad press puts a pad in hand; the overlay then names its buttons.
		Press(window, "Select");
		model.OpenPauseOverlay();
		WaitForOverlayFocus(window);
		Assert.Equal("A Select     B Resume", Bar(window));
	}

	//The connected-count half of the overlay check. The count -> text rule is
	//unit-tested (PlayActionBarTests); this is the realized surface's own.
	[AvaloniaFact]
	public void The_pause_overlay_bar_follows_the_connected_count()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPauseOverlay();

		Press(window, "Select");
		model.OpenPauseOverlay();
		WaitForOverlayFocus(window);
		Assert.Equal("A Select     B Resume", Bar(window));

		//The pad goes away: the same overlay now names the keyboard's keys, and
		//the bar follows without anything being reopened.
		model.ConnectedGamepadCount = () => 0;
		Press(window, "Down");
		Assert.Equal("Enter Select     Esc Resume", Bar(window));

		model.ConnectedGamepadCount = () => 1;
		Press(window, "Down");
		Assert.Equal("A Select     B Resume", Bar(window));
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowPauseOverlay()
	{
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		string rom = Path.Combine(_folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");
		return (window, model);
	}

	[AvaloniaFact]
	public void The_pause_overlay_names_a_PlayStation_pad_on_a_realized_surface()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPauseOverlay();

		PressNamed(window, "Joy1 Cross");
		model.OpenPauseOverlay();
		WaitForOverlayFocus(window);
		Assert.Equal("Cross Select     Circle Resume", Bar(window));
	}

	[AvaloniaFact]
	public void The_pause_overlay_names_no_control_while_the_pad_family_is_not_told()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPauseOverlay();

		//A pad is connected but nothing it sent has been pressed yet.
		model.OpenPauseOverlay();
		WaitForOverlayFocus(window);
		Assert.Equal("Select     Resume", Bar(window));
	}

	[AvaloniaFact]
	public void The_pause_overlay_follows_the_pad_family_in_hand_when_it_changes()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPauseOverlay();

		Press(window, "Select");
		model.OpenPauseOverlay();
		WaitForOverlayFocus(window);
		Assert.Equal("A Select     B Resume", Bar(window));

		//A press the backend names outside both families is not a pad and leaves
		//the hand where it was (PadInHand), so the family can only change by
		//another pad pressing: the PlayStation one takes over from the Xbox one.
		PressNamed(window, "Joy1 Cross");
		Assert.Equal("Cross Select     Circle Resume", Bar(window));
	}

	[AvaloniaFact]
	public void The_action_bar_is_hidden_in_Advanced_mode_on_the_Play_workspace_home()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		SeedRecents("Contra", "Zelda");
		(MainWindow window, MainWindowViewModel model) = ShowAdvancedOnPlayWorkspace();
		Assert.False(model.IsPlayerMode, "this case is not in Player mode");
		model.RecentGames.Init(GameScreenMode.RecentGames);
		WaitFor(() => model.RecentGames.Visible, "the Play workspace home did not open");

		Press(window, "Down");
		Assert.Equal("<hidden>", Bar(window));
	}
}
