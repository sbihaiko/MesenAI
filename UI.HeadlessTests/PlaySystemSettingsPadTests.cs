using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0256 Decision 8 (accepted 2026-10-04): the first run's two questions left
//the startup path and became Play's Settings › System tab, and the point of the
//move is that a pad drives them. The storage and keyboard rules are pinned
//host-free in UI.Tests/Play (FirstRunChoiceTests), and that nothing stands
//before the main window in UI.Tests (FirstRunStartupTests); this checks the
//realized sheet: the tab opens with the storage choice under the pad's focus,
//and the pad walks to either row and changes it.
//
//The sheet writes to real folders through the same ConfigManager path the wizard
//used, so the storage cases drive a recording view model (RecordingSystem) - the
//wizard's own test pattern - and the keyboard case saves and restores the real
//config it writes.
//
//The window is shown the way PlayHomeViewTests shows it, and the folders the
//view model displays are short on purpose: the two paths are drawn under their
//radio, and a temp-folder path long enough to wrap a row changes the layout the
//pad's directional search measures.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class PlaySystemSettingsPadTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;

	//The two folders the row names, and the folder the process is on: short, so
	//each of the two detail lines under the radios stays one line.
	private const string UserFolderPath = "/tmp/m8user";
	private const string PortableFolderPath = "/tmp/m8port";

	//A key code no preset writes, put on the port before Confirm so the case can
	//tell the seeding ran: the codes themselves are zeros in a headless host.
	private const ushort SentinelKey = 0x1234;

	private readonly List<MainWindow> _windows = new();

	//The backend this suite does not have, built the way PlayPadNavigationTests
	//builds it: a headless window gives InitializeEmu no platform handle, so no
	//key manager exists to name a pad. The press and the two lookups are handed
	//in through TickForTest; everything above them is the production path.
	private const ushort PadBase = 0x1000;
	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };
	private static readonly Dictionary<ushort, string> Backend = BuildBackend();
	private static readonly Dictionary<string, ushort> BackendCodes = Backend.ToDictionary(pair => pair.Value, pair => pair.Key);

	private static Dictionary<ushort, string> BuildBackend()
	{
		Dictionary<ushort, string> names = new();
		for(int pad = 1; pad <= 20; pad++) {
			for(int button = 0; button < ButtonNames.Length; button++) {
				names[(ushort)(PadBase + (pad - 1) * 0x100 + button)] = "Pad" + pad + " " + ButtonNames[button];
			}
		}
		return names;
	}

	private static string BackendName(ushort keyCode)
	{
		return Backend.TryGetValue(keyCode, out string? name) ? name : "";
	}

	private static ushort BackendCode(string name)
	{
		return BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;
	}

	private PadNavMapping? _mapping;
	private PadNavMapping Mapping => _mapping ??= PadNavControls.Resolve(PadFamily.Xbox, 0, BackendCode)
		?? throw new InvalidOperationException("the stand-in table does not answer the Xbox preset's names");

	public PlaySystemSettingsPadTests()
	{
		//#790: the core is process-global and a case that ran a game leaves its
		//console loaded, which is this bridge's authority input.
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitUntilStopped();
		}
	}

	private static void WaitUntilStopped()
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
			Thread.Sleep(10);
		}
		Assert.False(EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
	}

	public void Dispose()
	{
		//#838: a window that outlives its case is a second top level, and the
		//focus the next case waits for is asked of whichever top level the focus
		//manager answers for. Closing runs MainWindow's exit path, which releases
		//the process-global core, so the hook is cleared first - the next case
		//still needs the core.
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

	private (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus (MainMenuViewModel.Initialize).");
		return (window, model);
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		WaitFor(condition, () => failure);
	}

	private static void WaitFor(Func<bool> condition, Func<string> failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure());
			}
			Pump();
			Thread.Sleep(20);
		}
		Pump();
	}

	//#707: Avalonia's dispatcher promotes a due DispatcherTimer from the OS timer
	//callback, which only the main loop runs - never a test body. The empty job
	//stands in for that wake-up, so the window's real poll (PlayPadNavigationWiring's
	//50 ms timer) behaves here as it does in the app.
	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	private static string? FocusedName(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.Name;
	}

	//What holds the focus now, for a failure that says where the pad went instead
	//of only that it went somewhere else.
	private static string Focused(MainWindow window)
	{
		Control? focused = window.FocusManager?.GetFocusedElement() as Control;
		return focused is null ? "focus=<none>" : $"focus={focused.GetType().Name}#{focused.Name}";
	}

	private void Feed(MainWindow window, PadNavAction action, int milliseconds = 50)
	{
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(Mapping, action) }, TimeSpan.FromMilliseconds(milliseconds), BackendName, BackendCode);
	}

	private static void Release(MainWindow window, int milliseconds = 50)
	{
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(milliseconds), BackendName, BackendCode);
	}

	//One press: down, tick, up. The bridge records the pressed set every tick, so
	//a press that was never released is not a new edge - the next Feed would be
	//swallowed as a hold.
	private void Press(MainWindow window, PadNavAction action)
	{
		Feed(window, action);
		Release(window);
		Pump();
	}

	//Walks the pad one step and says which step failed, so a case that walks three
	//steps does not report only the last one.
	private void PressAndLand(MainWindow window, PadNavAction action, string expected)
	{
		Press(window, action);
		Assert.True(FocusedName(window) == expected, $"the pad's {action} did not land on {expected} ({Focused(window)})");
	}

	//The storage row's write, recorded instead of performed: the real one writes a
	//settings file into the user's folder or the one next to the executable
	//(ConfigManager.CreateConfig), which a test must not touch - the same reason
	//the wizard's own headless cases recorded Confirm instead of writing.
	private sealed class RecordingSystem : PlayerSystemSettingsViewModel
	{
		public readonly List<(bool StoreInUserProfile, string Leaving)> Writes = new();
		public Func<string, Exception?>? Fail = null;

		public RecordingSystem()
			: base(() => UserFolderPath, () => UserFolderPath, () => PortableFolderPath)
		{
		}

		protected override void WriteStorage(bool storeInUserProfile, string leaving)
		{
			if(Fail?.Invoke(leaving) is Exception ex) {
				throw ex;
			}
			Writes.Add((storeInUserProfile, leaving));
		}

		protected override void WriteKeyboard(DefaultKeyMappingType flags)
		{
		}

		protected override void RestartMesen()
		{
		}

		protected override void CloseMainWindow()
		{
		}
	}

	//The state the app reaches this surface in: a game is loaded, W-P4 is up, and
	//Settings opens over it - the fixture PlayerSettingsSheetTests uses. That the
	//home is not on screen under the sheet is load-bearing here: the pad's
	//directional search is the engine's own, across the whole window, so a visible
	//control belonging to the surface *under* the sheet is a candidate it can land
	//on, and the home's Open a ROM… sits between the storage row and the keyboard
	//rows. (PlayPadNavigationWiring keeps its claims in Esc order, so a surface
	//under another is exactly what two sheets up at once looks like.)
	private (MainWindow Window, MainWindowViewModel Model) ShowSystemTab(PlayerSystemSettingsViewModel system)
	{
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		Pump();
		ConfigViewModel settings = new(ConfigWindowTab.System, playerMode: true, createSystem: () => system);
		model.OpenPlayerSettings(settings);
		return (window, model);
	}

	private static void WaitForStorageChoice(MainWindow window)
	{
		WaitFor(() => FocusedName(window) == "SystemStorageUserFolder",
			() => $"the System tab opened without the focus on its storage choice ({Focused(window)})");
	}

	//Decision 8, the claim: the System tab is a surface of its own, so the pad
	//lands on the storage choice - not on the sheet's tab strip, which is where
	//every other tab opens (PlayPadNavigationTests'
	//The_topmost_surface_holds_the_focus_and_hands_it_back).
	[AvaloniaFact]
	public void The_System_tab_opens_with_the_storage_choice_under_the_pad()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowSystemTab(new RecordingSystem());
		WaitForStorageChoice(window);

		//The strings the tab draws are its own view's - the lookup key is
		//<view class>_<control id> - so an unresolved [View:key] fails here.
		Assert.Equal("Keep your saves and settings", window.FindNamed<TextBlock>("lblSystemStorageRow").Text);
		Assert.Equal("In your user folder", window.FindNamed<RadioButton>("SystemStorageUserFolder").Content);
		Assert.Equal("Keyboard", window.FindNamed<TextBlock>("lblSystemKeyboardRow").Text);
		Assert.Equal("WASD + K / J", window.FindNamed<RadioButton>("SystemKeyboardWasd").Content);
		Assert.Equal("Restart now", window.FindNamed<Button>("SystemRestartButton").Content);
	}

	//Decision 3, the half only a pad press can break: the surface that holds the
	//focus is the one the pad walks. The engine's directional search is geometric
	//across the whole window, and what is under a sheet is on screen on purpose -
	//W-P4's card stays behind the sheets opened from it, dimmed (only the pointer
	//is held off; focus navigation never asks IsHitTestVisible). Measured before
	//the search root was pinned: one Down from the storage row landed on the
	//dimmed card's Resume, and Confirm would have acted on a surface the player
	//is not using.
	[AvaloniaFact]
	public void A_pad_press_does_not_leave_the_surface_that_holds_the_focus()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowSystemTab(new RecordingSystem());
		WaitForStorageChoice(window);
		Assert.True(window.FindNamed<Button>("OverlayResumeButton").IsOnScreen(),
			"the card under the sheet is not on screen, so this case would prove nothing");

		PressAndLand(window, PadNavAction.Down, "SystemStoragePortable");
		PressAndLand(window, PadNavAction.Down, "SystemKeyboardArrows");
		PressAndLand(window, PadNavAction.Down, "SystemKeyboardWasd");

		//Below the last row is the sheet's own footer (#932: Done is reachable),
		//never the card behind it: the press stays on the sheet's surface.
		Press(window, PadNavAction.Down);
		Border sheet = window.FindNamed<Border>("PlayerSettingsSheet");
		Control? focused = window.FocusManager?.GetFocusedElement() as Control;
		Assert.True(focused is not null && focused.GetVisualAncestors().Contains(sheet),
			$"the pad walked off the sheet onto the surface under it ({Focused(window)})");
		Assert.NotEqual("OverlayResumeButton", FocusedName(window));
	}

	//#932, ADR-0256: every control on the sheet is reachable from the pad, and
	//the System tab is where the pad lands first. Measured before the claim named
	//the sheet as its search root: the root was inferred from the target (the
	//tab's page), so the trail was UserFolder -> Portable -> KeyboardArrows ->
	//KeyboardWasd and stopped there - Done, the only pad way back to W-P4 other
	//than Back, could not be reached.
	[AvaloniaFact]
	public void The_pad_walks_down_from_the_System_choices_to_Done()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowSystemTab(new RecordingSystem());
		WaitForStorageChoice(window);

		List<string?> trail = Walk(window, PadNavAction.Down, "btnPlayerSettingsDone");
		Assert.True(FocusedName(window) == "btnPlayerSettingsDone",
			$"the pad's Down never reached Done (trail: {string.Join(" -> ", trail)})");
	}

	//The same root, the other way: Up from the storage choice reaches the tab
	//strip, so the pad can change tab from the System tab as it can from the others.
	[AvaloniaFact]
	public void The_pad_walks_up_from_the_System_choices_to_the_tab_strip()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowSystemTab(new RecordingSystem());
		WaitForStorageChoice(window);

		Press(window, PadNavAction.Up);
		Control? focused = window.FocusManager?.GetFocusedElement() as Control;
		Assert.True(focused is TabItem && focused.GetVisualAncestors().Contains(window.FindNamed<TabControl>("PlayerSettingsTabs")),
			$"the pad's Up from the storage choice did not reach the tab strip ({Focused(window)})");
	}

	//#1133: leaving the System tab from the strip must not hand the ring back to
	//the strip's first tab. System is the last tab, so Left is the move onto the
	//tab before it (Controls, the Input tab); the claim that put the ring on the
	//storage choice is closed by then, and the ring has to stay on the tab the
	//pad just moved to.
	//
	//The sheet is left on the System tab and the ring is put on that tab without
	//selecting anything: Focus() is not a pad move, so the tab the sheet is on
	//does not change and IsPlayerSystemTabVisible stays true until the press.
	//Measured before the fix: the close re-arbitrated the focus, and the Left
	//landed on tabPlayerWindow with the sheet dragged onto Display with it.
	[AvaloniaFact]
	public void Left_from_the_System_tab_keeps_the_focus_on_Input()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowSystemTab(new RecordingSystem());
		WaitForStorageChoice(window);
		Assert.True(model.IsPlayerSystemTabVisible, "the sheet did not open on the System tab, so this case would prove nothing");

		Assert.True(window.FindNamed<TabItem>("tabPlayerSystem").Focus(), "the System tab would not take the focus");
		Pump();
		Assert.True(model.IsPlayerSystemTabVisible, "putting the ring on the tab closed the System tab, so this case would prove nothing");

		PressAndLand(window, PadNavAction.Left, "tabPlayerControls");
	}

	//#1133: Right on a Settings strip tab moves the ring on - to the next tab
	//along the strip, or into the page - instead of leaving it where it was.
	[AvaloniaFact]
	public void Right_on_a_Settings_tab_moves_the_focus_ring()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		Pump();
		model.OpenPlayerSettings(new ConfigViewModel(ConfigWindowTab.Audio, playerMode: true));
		WaitFor(() => FocusedName(window) == "tabPlayerWindow", () => $"Settings did not open on the strip's first tab ({Focused(window)})");

		//The tab the ring moved to, not only that it moved: the sheet opens on
		//Audio and the claim puts the ring on the strip's first tab, so the next
		//tab along is Look - and a Right that moved the ring anywhere else (the
		//first tab again, the sheet's own rows) is the bug this pins.
		PressAndLand(window, PadNavAction.Right, "tabPlayerVideo");
	}

	//Presses one direction until the focus lands on the named control or stops
	//moving, and returns where it went - the failure names the whole trail.
	private List<string?> Walk(MainWindow window, PadNavAction action, string until)
	{
		List<string?> trail = new() { FocusedName(window) };
		for(int i = 0; i < 8 && FocusedName(window) != until; i++) {
			Press(window, action);
			if(FocusedName(window) == trail[^1]) {
				break;
			}
			trail.Add(FocusedName(window));
		}
		return trail;
	}

	//Decision 8, the storage row from the pad: walk to the other folder, press
	//Confirm, and the same write the wizard made happens - into the folder the
	//row names, with the folder being left handed to it - plus the relaunch the
	//running process cannot avoid.
	[AvaloniaFact]
	public void The_pad_moves_to_the_other_storage_choice_and_the_choice_is_written()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		RecordingSystem system = new();
		(MainWindow window, _) = ShowSystemTab(system);
		WaitForStorageChoice(window);
		Assert.True(system.StoreInUserProfile, "the row opened on the portable folder with the process running from the user folder");
		Assert.False(system.RestartPending, "a sheet that has chosen nothing already owes a restart");

		PressAndLand(window, PadNavAction.Down, "SystemStoragePortable");
		Press(window, PadNavAction.Confirm);
		WaitFor(() => system.Writes.Count == 1, () => "the pad's Confirm on the portable choice wrote nothing");

		Assert.False(system.StoreInUserProfile);
		Assert.Equal((false, UserFolderPath), system.Writes[0]);
		//The folder the process is on cannot be left under it: the sheet offers
		//the relaunch instead of pretending the move happened.
		Assert.True(system.RestartPending);
		Assert.Contains("Restart", window.FindNamed<TextBlock>("SystemNotice").Text);
		Assert.True(window.FindNamed<Button>("SystemRestartButton").IsOnScreen());
	}

	//The other half of the same rule: choosing the folder the process is already
	//on changes nothing, so nothing is written and no relaunch is offered.
	[AvaloniaFact]
	public void Choosing_the_folder_the_process_is_already_on_writes_nothing()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		RecordingSystem system = new();
		(MainWindow window, _) = ShowSystemTab(system);
		WaitForStorageChoice(window);

		//Confirm on the radio that is already on: a radio cannot be unchecked by
		//pressing it, so this is the no-op path and has to stay one.
		Press(window, PadNavAction.Confirm);
		Pump();

		Assert.True(system.StoreInUserProfile);
		Assert.Empty(system.Writes);
		Assert.False(system.RestartPending, "choosing the folder the process is on owes a restart");
		Assert.False(window.FindNamed<Button>("SystemRestartButton").IsOnScreen());
	}

	//Decision 8, the error half: a folder that cannot be written says so - W-X2's
	//sentence - and does not offer a restart for a move that did not happen.
	[AvaloniaFact]
	public void A_storage_folder_that_cannot_be_written_says_so_and_offers_no_restart()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		RecordingSystem system = new();
		(MainWindow window, _) = ShowSystemTab(system);
		system.Fail = _ => new UnauthorizedAccessException(PortableFolderPath);
		WaitForStorageChoice(window);

		PressAndLand(window, PadNavAction.Down, "SystemStoragePortable");
		Press(window, PadNavAction.Confirm);
		Pump();

		TextBlock notice = window.FindNamed<TextBlock>("SystemNotice");
		Assert.True(notice.IsOnScreen(), $"the failed write said nothing ({Focused(window)})");
		Assert.Contains("cannot write", notice.Text);
		Assert.False(system.RestartPending, "a write that failed still owes a restart");
		Assert.False(window.FindNamed<Button>("SystemRestartButton").IsOnScreen());
	}

	//Decision 8, the keyboard row from the pad: two more Downs reach WASD, and
	//Confirm writes the setting the first run wrote (DefaultKeyMappings) and the
	//keys themselves, through the seeding path the first run and the Controller
	//sheet's restore share. Both are restored here: this is the real config.
	[AvaloniaFact]
	public void The_pad_moves_to_the_WASD_preset_and_the_keys_follow()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		DefaultKeyMappingType savedMappings = ConfigManager.Config.DefaultKeyMappings;
		NesKeyMapping[] saved = {
			ConfigManager.Config.Nes.Port1.Mapping1,
			ConfigManager.Config.Nes.Port1.Mapping2,
			ConfigManager.Config.Nes.Port1.Mapping3,
			ConfigManager.Config.Nes.Port1.Mapping4
		};
		try {
			ConfigManager.Config.DefaultKeyMappings = DefaultKeyMappingType.Xbox | DefaultKeyMappingType.ArrowKeys;
			ConfigManager.Config.Nes.Port1.Mapping1.A = SentinelKey;
			//The real view model here: this half's rule is what the write leaves
			//in the config, and RecordingSystem's WriteKeyboard is a no-op.
			PlayerSystemSettingsViewModel vm = new(() => UserFolderPath, () => UserFolderPath, () => PortableFolderPath);
			(MainWindow window, _) = ShowSystemTab(vm);
			WaitForStorageChoice(window);
			Assert.True(window.FindNamed<RadioButton>("SystemKeyboardArrows").IsChecked, "the row did not open on the preset the config is on");

			//The group's own order: the two folders, then the two presets.
			PressAndLand(window, PadNavAction.Down, "SystemStoragePortable");
			PressAndLand(window, PadNavAction.Down, "SystemKeyboardArrows");
			PressAndLand(window, PadNavAction.Down, "SystemKeyboardWasd");
			Press(window, PadNavAction.Confirm);
			Pump();

			Assert.True(ConfigManager.Config.DefaultKeyMappings.HasFlag(DefaultKeyMappingType.WasdKeys), "Confirm on WASD did not write the preset");
			Assert.False(ConfigManager.Config.DefaultKeyMappings.HasFlag(DefaultKeyMappingType.ArrowKeys), "the arrow preset stayed on next to WASD");
			//Both gamepad layouts always apply (the first run's rule).
			Assert.True(ConfigManager.Config.DefaultKeyMappings.HasFlag(DefaultKeyMappingType.Xbox));
			Assert.True(ConfigManager.Config.DefaultKeyMappings.HasFlag(DefaultKeyMappingType.Ps4));
			//The keys came with it, through Configuration.SeedConsoleKeyDefaults -
			//the preset's own keys, not a second table written by the sheet. What
			//the codes *are* cannot be asserted here: the presets bind through
			//InputApi.GetKeyCode, which is KeyManager::GetKeyCode behind a null
			//check, and InitializeEmu registers the key manager only when it was
			//given a window and a renderer handle - so in a headless host every
			//preset binds zeros (KeyboardPresetRecoveryTests says the same). A
			//sentinel the seeding has to overwrite is the assertable half.
			Assert.NotEqual(SentinelKey, ConfigManager.Config.Nes.Port1.Mapping1.A);
			Assert.True(window.FindNamed<RadioButton>("SystemKeyboardWasd").IsChecked);
			Assert.False(window.FindNamed<RadioButton>("SystemKeyboardArrows").IsChecked);
		} finally {
			ConfigManager.Config.DefaultKeyMappings = savedMappings;
			ConfigManager.Config.Nes.Port1.Mapping1 = saved[0];
			ConfigManager.Config.Nes.Port1.Mapping2 = saved[1];
			ConfigManager.Config.Nes.Port1.Mapping3 = saved[2];
			ConfigManager.Config.Nes.Port1.Mapping4 = saved[3];
			ConfigManager.Config.Save();
		}
	}

	//Decision 8's acceptance case for the half that left: a fresh install - no
	//recent games - boots onto W-P1's first-run home, with its one action under
	//the pad's focus and no wizard in front of it. (That nothing stands before the
	//window at all is UI.Tests FirstRunStartupTests'
	//No_setup_wizard_stands_before_the_main_window, which reads the entry point - a
	//headless case cannot run it.)
	[AvaloniaFact]
	public void A_fresh_install_boots_onto_the_first_run_home()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}

		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();

		Assert.True(model.RecentGames.ShowFirstRunHome, "a fresh install did not land on the first-run home");
		Assert.True(window.FindNamed<StackPanel>("PlayHomeFirstRun").IsOnScreen());
		WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
			() => $"the first-run home opened without its one action focused ({Focused(window)})");
	}
}
