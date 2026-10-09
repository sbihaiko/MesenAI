using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

//#1112 (spec #1102 slice 6): the haptic tick on a focus move. The rule is
//host-free in UI.Tests/Input/HapticTickRuleTests; what is proved HERE is the
//wiring: a pad press on the Play home reaches the per-pad tick call with the
//row on and the host answering "aimable", and not otherwise. The aimable answer
//and the tick call are the two seams (no hardware); everything above them is the
//real pad path.
[Collection(NativeCoreCollection.Name)]
public class MenuTickTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _menuTick = ConfigManager.Config.Input.MenuTick;
	private readonly uint _rumble = ConfigManager.Config.Input.ForceFeedbackIntensity;
	private readonly List<MainWindow> _windows = new();
	private readonly List<uint> _ticked = new();
	private bool _aimable = true;

	//The stand-in pad backend (see MenuSoundsTests): a headless window has no key
	//manager to name a pad.
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

	public MenuTickTests()
	{
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			Stopwatch clock = Stopwatch.StartNew();
			while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
				System.Threading.Thread.Sleep(10);
			}
			Assert.False(EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
		}
		HapticTickOutput.SetSeamsForTest(_ => _aimable, index => _ticked.Add(index));
	}

	public void Dispose()
	{
		TestAppBuilder.ResetPadSeams();
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
		ConfigManager.Config.Input.MenuTick = _menuTick;
		ConfigManager.Config.Input.ForceFeedbackIntensity = _rumble;
		ConfigManager.Config.Save();
	}

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	private MainWindow ShowPlayHome()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		Stopwatch clock = Stopwatch.StartNew();
		while(model.MainMenu.HelpMenuItems.Count == 0) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException("MainWindow never finished building its menus.");
			}
			Pump();
			System.Threading.Thread.Sleep(20);
		}
		Pump();
		return window;
	}

	private void Press(MainWindow window, PadNavAction action)
	{
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(Mapping, action) }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
	}

	[AvaloniaFact]
	public void With_the_row_on_and_an_aimable_pad_a_focus_move_ticks_the_pad_in_hand()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Input.MenuTick = true;
		ConfigManager.Config.Input.ForceFeedbackIntensity = 5;
		MainWindow window = ShowPlayHome();

		Press(window, PadNavAction.Down);

		//The stand-in table numbers pads from 1; the host's device index is the pad's.
		Assert.Equal(new uint[] { 0 }, _ticked);
	}

	//Confirm and Back are not focus moves: only a move ticks.
	[AvaloniaFact]
	public void Confirm_and_back_do_not_tick()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Input.MenuTick = true;
		ConfigManager.Config.Input.ForceFeedbackIntensity = 5;
		MainWindow window = ShowPlayHome();

		Press(window, PadNavAction.Back);
		Press(window, PadNavAction.Confirm);

		Assert.Empty(_ticked);
	}

	[AvaloniaFact]
	public void With_the_row_off_a_focus_move_does_not_tick()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Input.MenuTick = false;
		ConfigManager.Config.Input.ForceFeedbackIntensity = 5;
		MainWindow window = ShowPlayHome();

		Press(window, PadNavAction.Down);

		Assert.Empty(_ticked);
	}

	[AvaloniaFact]
	public void With_rumble_at_0_a_focus_move_does_not_tick()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Input.MenuTick = true;
		ConfigManager.Config.Input.ForceFeedbackIntensity = 0;
		MainWindow window = ShowPlayHome();

		Press(window, PadNavAction.Down);

		Assert.Empty(_ticked);
	}

	[AvaloniaFact]
	public void A_pad_the_host_cannot_aim_is_never_ticked()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		_aimable = false;
		ConfigManager.Config.Input.MenuTick = true;
		ConfigManager.Config.Input.ForceFeedbackIntensity = 5;
		MainWindow window = ShowPlayHome();

		Press(window, PadNavAction.Down);

		Assert.Empty(_ticked);
	}
}
