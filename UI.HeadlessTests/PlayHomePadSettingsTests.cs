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

//#1177 (ADR-0256, W-P8): with no game loaded the pad's Y on the Play home opens
//the Settings sheet, and B from it returns home. Y keeps its library meaning
//(ADR-0264 Decision 3). The rule is host-free (UI.Tests/Play/
//PlayHomeSettingsPadTests); what is proved here is the wiring, through the same
//TickForTest seam and stand-in pad backend the library's pad cases use.
[Collection(NativeCoreCollection.Name)]
public class PlayHomePadSettingsTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;
	private readonly List<string>? _libraryFolders = ConfigManager.Config.Preferences.LibraryFolders;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1177-" + Guid.NewGuid().ToString("N"));

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

	private static string BackendName(ushort keyCode) => Backend.TryGetValue(keyCode, out string? name) ? name : "";

	private static ushort BackendCode(string name) => BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	private PadNavMapping? _mapping;
	private PadNavMapping Mapping => _mapping ??= PadNavControls.Resolve(PadFamily.Xbox, 0, BackendCode)
		?? throw new InvalidOperationException("the stand-in table does not answer the Xbox preset's names");

	public PlayHomePadSettingsTests()
	{
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			Stopwatch clock = Stopwatch.StartNew();
			while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
				Thread.Sleep(10);
			}
			Assert.False(EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
		}
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
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
		prefs.GameFolder = _gameFolder ?? "";
		prefs.OverrideGameFolder = _overrideGameFolder;
		prefs.LibraryFolders = _libraryFolders;
		ConfigManager.Config.Save();

		try {
			Directory.Delete(_folder, true);
		} catch {
			//A case that failed before it built its tree leaves nothing to remove.
		}
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

	private static void Release(MainWindow window)
	{
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
	}

	private void PressCode(MainWindow window, ushort code)
	{
		Assert.NotEqual((ushort)0, code);
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { code }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Release(window);
		Pump();
	}

	private void Press(MainWindow window, PadNavAction action) => PressCode(window, PlayPadNavigation.CodeOf(Mapping, action));

	private void PressY(MainWindow window)
		=> PressCode(window, PadNavControls.SheetCode(PadFamily.Xbox, 0, PadSheetControl.Search, BackendCode) ?? 0);

	private void LibraryRoot()
	{
		string nes = Path.Combine(_folder, "games", "NES");
		Directory.CreateDirectory(nes);
		File.WriteAllBytes(Path.Combine(nes, "Contra (U) [!].nes"), SyntheticNrom.Build());
		ConfigManager.Config.Preferences.GameFolder = Path.Combine(_folder, "games");
		ConfigManager.Config.Preferences.OverrideGameFolder = true;
		ConfigManager.Config.Preferences.LibraryFolders = null;
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowFirstRunHome()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;

		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");

		model.RomPicker.RunLibraryScanInline = true;
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		Assert.True(model.RecentGames.ShowFirstRunHome, "the home is not the first-run one, so this case would prove nothing");
		WaitFor(() => (window.FocusManager?.GetFocusedElement() as Control)?.Name == "PlayHomeOpenRomPrimary",
			"the first-run home did not put the focus on its one action");
		return (window, model);
	}

	[AvaloniaFact]
	public void Y_on_the_home_with_no_game_opens_Settings_and_B_returns_home()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		Assert.False(model.IsPlayerSettingsVisible);

		PressY(window);

		Assert.True(model.IsPlayerSettingsVisible, "Y on the home did not open the Settings sheet");

		Press(window, PadNavAction.Back);

		Assert.False(model.IsPlayerSettingsVisible, "B from the sheet did not close it");
		Assert.True(model.RecentGames.ShowFirstRunHome);
	}

	[AvaloniaFact]
	public void Y_on_the_library_still_searches_and_does_not_open_Settings()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		LibraryRoot();
		(MainWindow window, MainWindowViewModel model) = ShowFirstRunHome();
		Press(window, PadNavAction.Confirm);
		WaitFor(() => model.RomPicker.IsVisible && model.RomPicker.Tiles.Count == 1,
			"the library sheet did not open on its game");

		PressY(window);

		Assert.Equal("RomPickerSearch", (window.FocusManager?.GetFocusedElement() as Control)?.Name);
		Assert.False(model.IsPlayerSettingsVisible, "Y on the library opened Settings instead of Search");
	}
}
