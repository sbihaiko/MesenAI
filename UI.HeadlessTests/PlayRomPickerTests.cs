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
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#845 (ADR-0256 Decision 8/9): the Play home's *Open a ROM…* is reachable from
//the pad and activates, but what it opened was a native OS file dialog
//(FileDialogHelper -> Avalonia's StorageProvider), which the focus engine cannot
//drive - so a machine with a pad and nothing else could not load a game.
//
//This is the wiring half: the pad's Confirm on the home's own action has to open
//an in-app surface, and the choice made there has to reach the same open-ROM
//path the native dialog's result went to. The rules (roots, rows, ascend) are
//host-free in UI.Tests/Play/RomPickerTests.
[Collection(NativeCoreCollection.Name)]
public class PlayRomPickerTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;

	private readonly List<MainWindow> _windows = new();

	//A tree of its own per case: the picker's real filesystem half (roots,
	//rows, ascend) is exercised against folders this case made.
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-845-" + Guid.NewGuid().ToString("N"));

	//The backend this suite does not have, built the way PlayPadNavigationTests
	//builds it: a headless window gives InitializeEmu no platform handle, so no
	//key manager exists to name a pad.
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

	public PlayRomPickerTests()
	{
		//#790: the core is process-global and a case that ran a game leaves its
		//console loaded.
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitUntilStopped();
		}
		Directory.CreateDirectory(_folder);
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
		//#838: a window that outlives its case is a second top level.
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
		ConfigManager.Config.Save();

		try {
			Directory.Delete(_folder, true);
		} catch {
			//A case that failed before it built its tree leaves nothing to remove;
			//the temp folder going is not what the case was proving.
		}
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		return (window, model);
	}

	//A first run: no recent games, so W-P1's own action is the one on screen.
	private (MainWindow Window, MainWindowViewModel Model) ShowFirstRunHome()
	{
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		Assert.True(model.RecentGames.ShowFirstRunHome, "the home is not the first-run one, so this case would prove nothing");
		return (window, model);
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

	private static string? FocusedName(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.Name;
	}

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

	private void Press(MainWindow window, PadNavAction action)
	{
		Feed(window, action);
		Release(window);
		Pump();
	}

	//#845: with nothing but a pad, the home's own action has to open a surface
	//the pad can drive. Before this slice it reached
	//EmuApi.ExecuteShortcut(EmulatorShortcut.OpenFile), i.e. ShortcutHandler ->
	//FileDialogHelper -> Avalonia's StorageProvider - a native dialog the focus
	//engine cannot reach past.
	[AvaloniaFact]
	public void The_pad_opens_the_in_app_rom_picker_on_the_home()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowFirstRunHome();
		WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");

		Press(window, PadNavAction.Confirm);
		Pump();

		Border sheet = window.FindNamed<Border>("PlayerRomPickerSheet");
		Assert.True(sheet.IsOnScreen(), $"the pad's Confirm opened no in-app picker ({Focused(window)})");
	}

	//#845: and the choice it opens has to reach the load. The pad walks the
	//roots into a folder, confirms the game it finds, and the game is on screen -
	//the same LoadRomHelper.LoadFile the native dialog's own result went to.
	[AvaloniaFact]
	public void The_pad_walks_into_a_folder_and_opens_the_game_it_finds()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Path.Combine(_folder, "games");
		Directory.CreateDirectory(Path.Combine(root, "nes"));
		File.WriteAllBytes(Path.Combine(root, "nes", "Contra.nes"), SyntheticNrom.Build());

		//The configured game folder is the first root, so the pad's way in is the
		//same on any machine; without it the roots would be whatever volumes the
		//test machine happens to have.
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		try {
			(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
			WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
				"the first-run home did not put the focus on its one action");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "Your games",
				$"the picker did not open on its roots ({Focused(window)})");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "nes",
				$"Confirm on the configured folder did not descend into it ({Focused(window)})");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "Contra.nes",
				$"Confirm on the folder did not list the game inside it ({Focused(window)})");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown,
				"confirming the game did not load it");
			//The sheet is gone: the pick IS the open, not a step before one.
			Assert.False(model.RomPicker.IsVisible);
		} finally {
			ConfigManager.Config.Preferences.GameFolder = "";
			ConfigManager.Config.Preferences.OverrideGameFolder = false;
		}
	}

	//#845: Back ascends one folder and, on the first list, dismisses with nothing
	//opened - so a player who opened the picker by mistake is where they were.
	[AvaloniaFact]
	public void Pad_Back_ascends_the_picker_and_dismisses_it_on_the_roots()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string root = Path.Combine(_folder, "games");
		Directory.CreateDirectory(Path.Combine(root, "nes"));
		ConfigManager.Config.Preferences.GameFolder = root;
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		try {
			(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
			WaitFor(() => FocusedName(window) == "PlayHomeOpenRomPrimary",
				"the first-run home did not put the focus on its one action");

			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "Your games", "the picker did not open on its roots");
			Press(window, PadNavAction.Confirm);
			WaitFor(() => FocusedRow(window) == "nes", "Confirm did not descend into the configured folder");

			Press(window, PadNavAction.Back);
			WaitFor(() => FocusedRow(window) == "Your games",
				$"Back did not ascend out of the folder ({Focused(window)})");
			Assert.True(model.RomPicker.IsVisible, "Back closed the picker a level early");

			Press(window, PadNavAction.Back);
			WaitFor(() => !model.RomPicker.IsVisible, "Back on the roots did not dismiss the picker");
			Assert.False(EmuApi.IsRunning());
			Assert.False(model.IsPlayerOverlayVisible);
		} finally {
			ConfigManager.Config.Preferences.GameFolder = "";
			ConfigManager.Config.Preferences.OverrideGameFolder = false;
		}
	}

	//The label of the row the pad's ring is on. The rows are the picker's own
	//list, so this is "which row would Confirm act on" and nothing else.
	private static string? FocusedRow(MainWindow window)
	{
		return (window.FocusManager?.GetFocusedElement() as Control)?.DataContext is PlayerRomPickerRow row ? row.Label : null;
	}
}
