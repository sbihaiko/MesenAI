using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Debugger.Utilities;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//G.1 (PRD Part B §8, ADR-0241, §13.5.1 W-S1-W-S3): the shell's XAML wiring.
//The rules themselves (fixed order, ⌘ digits, bar visibility, the once-only
//notice, the status sentence) are pinned host-free in UI.Tests/Shell; this
//checks the crossing into MainWindow.axaml: that Tools ⋯ realizes the classic
//MainMenuAction tree, that only the active profile is on screen, that the
//placeholders name the next slice, and - with the real core - that switching
//leaves the running game running.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on
//the core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class WorkspaceShellTests : IDisposable
{
	private static readonly string[] TopLevelMenus = { "File", "Game", "Settings", "Tools", "Debug", "Help" };
	//W-S2: in Player mode Tools ⋯ follows the render ("Options"); the classic
	//bar and Advanced's Tools ⋯ keep "Settings".
	private static readonly string[] PlayerToolsMenus = { "File", "Game", "Options", "Tools", "Debug", "Help" };

	//ConfigManager.Config is process-global: every setting a test here touches
	//is restored afterwards, so the next class in the collection (e.g.
	//PackAudioNoticeInstallTests, gated on AutoInstallCommunityPacks) sees the
	//values it would have seen without this class.
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _showClassicMenuBar = ConfigManager.Config.Preferences.ShowClassicMenuBar;
	private readonly bool _noticeShown = ConfigManager.Config.Preferences.ClassicMenuNoticeShown;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ShowClassicMenuBar = _showClassicMenuBar;
		prefs.ClassicMenuNoticeShown = _noticeShown;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		//Workspace switches and the classic-bar toggle call Config.Save(); write
		//the restored values back so settings.json matches memory again.
		ConfigManager.Config.Save();
	}

	private static string Label(MenuItem item) => (item.Header as string ?? "").Replace("_", "");

	private static (MainWindow Window, MainWindowViewModel Model) ShowShell(UiMode uiMode = UiMode.Player)
	{
		ConfigManager.Config.Preferences.UiMode = uiMode;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		ConfigManager.Config.Preferences.ShowClassicMenuBar = false;
		ConfigManager.Config.Preferences.ClassicMenuNoticeShown = true;
		ConfigManager.Config.Preferences.PauseWhenInBackground = false;
		ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus (MainMenuViewModel.Initialize).");
		return (window, model);
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	private static MenuItem OpenToolsDropdown(MainWindow window)
	{
		Menu tools = window.FindNamed<Menu>("ToolsMenu");
		MenuItem dots = tools.GetRealizedContainers().OfType<MenuItem>().Single();
		dots.Open();
		Dispatcher.UIThread.RunJobs();
		return dots;
	}

	private static MenuItem Child(ItemsControl parent, string label)
	{
		MenuItem? item = parent.GetRealizedContainers().OfType<MenuItem>().FirstOrDefault(m => Label(m) == label);
		return item ?? throw new XunitException($"No realized '{label}' under {parent.GetType().Name}; saw: " +
			string.Join(", ", parent.GetRealizedContainers().OfType<MenuItem>().Select(Label)));
	}

	private static string[] RealizedLabels(MenuItem submenu)
	{
		submenu.Open();
		Dispatcher.UIThread.RunJobs();
		string[] labels = submenu.GetRealizedContainers().OfType<MenuItem>().Select(Label).ToArray();
		submenu.Close();
		Dispatcher.UIThread.RunJobs();
		return labels;
	}

	[AvaloniaFact]
	public void Tools_dropdown_holds_the_six_classic_menus_and_the_classic_bar_toggle()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowShell();

		MenuItem dots = OpenToolsDropdown(window);
		string[] labels = dots.GetRealizedContainers().OfType<MenuItem>().Select(Label).ToArray();

		Assert.Equal(PlayerToolsMenus, labels.Take(6).ToArray());
		Assert.Contains("Show Classic Menu Bar", labels);
	}

	//The stop rule's "every classic menu action is still reachable from Tools
	//⋯": each of the six submenus is bound to the very list the classic bar
	//renders, and realizes the same entries in the same order.
	[AvaloniaFact]
	public void Every_classic_menu_action_is_reachable_from_tools()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();

		IEnumerable[] classicLists = {
			model.MainMenu.FileMenuItems, model.MainMenu.GameMenuItems, model.MainMenu.OptionsMenuItems,
			model.MainMenu.ToolsMenuItems, model.MainMenu.DebugMenuItems, model.MainMenu.HelpMenuItems
		};
		//The comparison needs the classic bar realized, so turn it on.
		model.ToggleClassicMenuBar();
		Dispatcher.UIThread.RunJobs();
		Menu classicBar = window.FindNamed<Menu>("ActionMenu");
		MenuItem dots = OpenToolsDropdown(window);

		for(int i = 0; i < TopLevelMenus.Length; i++) {
			MenuItem fromTools = Child(dots, PlayerToolsMenus[i]);
			Assert.Same(classicLists[i], fromTools.ItemsSource);
			string[] toolsLabels = RealizedLabels(fromTools);
			Assert.NotEmpty(toolsLabels);
			Assert.Equal(RealizedLabels(Child(classicBar, TopLevelMenus[i])), toolsLabels);
		}
	}

	//The P.4 gate that disabled every Debug action in Player is retired: Tools ⋯
	//is where the debugger is reached, in either UiMode, so a Player-mode
	//Debug entry is enabled exactly when the same entry is in Advanced.
	[AvaloniaFact]
	public void Debug_entries_are_not_disabled_by_player_mode()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(_, MainWindowViewModel model) = ShowShell(UiMode.Player);

		BaseMenuAction[] actions = model.MainMenu.DebugMenuItems.OfType<BaseMenuAction>().ToArray();
		Assert.NotEmpty(actions);
		bool[] inPlayer = actions.Select(a => a.IsEnabled?.Invoke() ?? true).ToArray();
		ConfigManager.Config.Preferences.UiMode = UiMode.Advanced;
		bool[] inAdvanced = actions.Select(a => a.IsEnabled?.Invoke() ?? true).ToArray();
		Assert.Equal(inAdvanced, inPlayer);
		//Settings (debugger options) has no condition of its own.
		Assert.Contains(true, inPlayer);
	}

	[AvaloniaFact]
	public void Classic_bar_is_hidden_by_default_and_the_tools_checkbox_brings_it_back()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell(UiMode.Advanced);

		Assert.False(window.FindNamed<MainMenuView>("MainMenu").IsOnScreen());

		MenuItem dots = OpenToolsDropdown(window);
		MenuItem toggle = Child(dots, "Show classic menu bar");
		Assert.False(toggle.IsChecked);
		toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
		Dispatcher.UIThread.RunJobs();

		Assert.True(ConfigManager.Config.Preferences.ShowClassicMenuBar);
		Assert.True(toggle.IsChecked);
		Assert.True(model.IsMenuVisible);
		Assert.True(window.FindNamed<MainMenuView>("MainMenu").IsOnScreen());
	}

	//User's choice 2026-10-02 ("Integrar agora"): on macOS the shell bar is the
	//title bar. Headless can only assert the wiring - the extension hints, the
	//drag/input roles, the traffic-light inset and the row order; how it looks
	//on a real display needs a person.
	[AvaloniaFact]
	public void On_macOS_the_shell_bar_is_the_title_bar_with_room_for_the_traffic_lights()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		WorkspaceShellBar bar = window.FindNamed<WorkspaceShellBar>("ShellBar");
		Button profile = window.FindNamed<Button>("ProfileButton");

		if(!OperatingSystem.IsMacOS()) {
			//Windows/Linux keep the in-window strip under the system title bar.
			Assert.False(window.ExtendClientAreaToDecorationsHint);
			Assert.Equal(12, profile.Margin.Left);
			return;
		}

		Assert.True(window.ExtendClientAreaToDecorationsHint);
		Assert.Equal(ShellTitleBar.Height, window.ExtendClientAreaTitleBarHeightHint);
		DockPanel root = Assert.IsType<DockPanel>(bar.Parent);
		Assert.Same(bar, root.Children[0]);
		Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(window.FindNamed<DockPanel>("TitleBarArea")));
		Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(profile));
		Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(bar.ToolsMenu));
		Assert.Equal(12 + ShellTitleBar.MacTrafficLightInset, profile.Margin.Left);

		//The classic bar, when turned on, sits right under the title-bar row.
		model.ToggleClassicMenuBar();
		Dispatcher.UIThread.RunJobs();
		MainMenuView classic = window.FindNamed<MainMenuView>("MainMenu");
		Assert.True(classic.IsOnScreen());
		Assert.Equal(0, bar.TranslatePoint(new Point(0, 0), window)!.Value.Y);
		Assert.True(classic.TranslatePoint(new Point(0, 0), window)!.Value.Y >= bar.Bounds.Height);

		//Fullscreen has no traffic lights at rest: the inset goes away and comes back.
		window.WindowState = WindowState.FullScreen;
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(12, profile.Margin.Left);
		window.WindowState = WindowState.Normal;
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(12 + ShellTitleBar.MacTrafficLightInset, profile.Margin.Left);
	}

	[AvaloniaFact]
	public void Only_the_active_profile_is_named_at_rest_and_the_status_line_reads_no_game()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowShell();

		Assert.Equal("Play", window.FindNamed<TextBlock>("ProfileButtonName").Text);
		Assert.True(window.FindNamed<Button>("ProfileButton").IsOnScreen());
		//The other two profiles exist only inside the closed popover.
		Assert.DoesNotContain(window.FindAll<TextBlock>(), t => t.IsOnScreen() && (t.Text == "Remaster" || t.Text == "Share"));
		Assert.Equal("No game loaded", window.FindNamed<TextBlock>("ShellStatusText").Text);
		Assert.True(window.FindNamed<Border>("ShellStatusLine").IsOnScreen());
	}

	[AvaloniaFact]
	public void Switcher_popover_lists_play_remaster_share_in_order_and_picking_switches()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();

		Button profile = window.FindNamed<Button>("ProfileButton");
		profile.Flyout!.ShowAt(profile);
		Dispatcher.UIThread.RunJobs();
		Flyout flyout = Assert.IsType<Flyout>(profile.Flyout);
		StackPanel panel = Assert.IsType<StackPanel>(flyout.Content);
		ItemsControl rows = panel.FindNamed<ItemsControl>("SwitcherRows");
		Button[] buttons = rows.GetRealizedContainers().SelectMany(c => c.FindAll<Button>()).ToArray();
		Assert.Equal(new object?[] { Workspace.Play, Workspace.Remaster, Workspace.Share }, buttons.Select(b => b.Tag).ToArray());

		buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(Workspace.Remaster, model.Shell.Active);
		Assert.Equal(Workspace.Remaster, ConfigManager.Config.Preferences.Workspace);
		Assert.Equal("Remaster", window.FindNamed<TextBlock>("ProfileButtonName").Text);
		Assert.False(flyout.IsOpen);
	}

	//On macOS the native renderer is drawn above every Avalonia control: with
	//it shown, Esc paused the game and opened W-P4 behind the picture, so the
	//player saw only the pause icon. Every surface over the game hides it.
	[AvaloniaFact]
	public void The_pause_overlay_and_the_sheets_hide_the_native_picture()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		model.RecentGames.Visible = false;
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.IsNativeRendererVisible);

		model.IsPlayerOverlayVisible = true;
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
		Assert.False(model.IsNativeRendererVisible);

		model.IsPlayerOverlayVisible = false;
		model.IsSaveStatesSheetVisible = true;
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.IsNativeRendererVisible);

		model.IsSaveStatesSheetVisible = false;
		model.CheatsSheet.IsVisible = true;
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.IsNativeRendererVisible);
		model.CheatsSheet.IsVisible = false;
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.IsNativeRendererVisible);
	}

	//G.3 and G.8 replaced G.1's placeholders with each workspace's own
	//screens (RemasterWorkspaceTests, ShareWorkspaceTests); this pins the
	//shell's half: Share's home is shown and nothing of Play is.
	[AvaloniaFact]
	public void Share_shows_its_home_and_nothing_of_play()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		model.IsPlayerOverlayVisible = true;

		model.SelectWorkspace(Workspace.Share);
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<Panel>("ShareWorkspaceHost").IsOnScreen());
		Assert.True(window.FindNamed<Button>("ShareAPackButton").IsOnScreen());
		//Nothing of Play is on screen: home, renderer, the pause overlay.
		Assert.False(window.FindNamed<Panel>("PlayWorkspace").IsOnScreen());
		Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
		Assert.False(model.IsNativeRendererVisible);
		//...but nothing was closed: the overlay is back as it was in Play.
		model.SelectWorkspace(Workspace.Play);
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
		Assert.False(window.FindNamed<Panel>("ShareWorkspaceHost").IsOnScreen());
	}

	[AvaloniaFact]
	public void Platform_shortcut_digits_switch_workspaces_directly()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		RawInputModifiers modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

		window.KeyPress(Key.D3, modifier, PhysicalKey.Digit3, "3");
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(Workspace.Share, model.Shell.Active);

		window.KeyPress(Key.D2, modifier, PhysicalKey.Digit2, "2");
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(Workspace.Remaster, model.Shell.Active);

		window.KeyPress(Key.D1, modifier, PhysicalKey.Digit1, "1");
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(Workspace.Play, model.Shell.Active);
	}

	//The stop rule's "switching keeps the game running", against the real core:
	//a running ROM is still running, unpaused and advancing frames after a full
	//Play -> Remaster -> Share -> Play cycle. Also the W-S1 bar rule as wired:
	//hidden while the game runs in Play, shown in Remaster/Share.
	[AvaloniaFact]
	public void Switching_keeps_the_game_running_and_the_bar_hides_only_in_play()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();

		string folder = Path.Combine(Path.GetTempPath(), "mesen-g1-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, BuildSyntheticNrom());
		try {
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
			EmuApi.Resume();
			WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused, "the game never ran unpaused");

			Assert.False(window.FindNamed<WorkspaceShellBar>("ShellBar").IsOnScreen());
			Assert.False(window.FindNamed<Border>("ShellStatusLine").IsOnScreen());

			foreach(Workspace w in new[] { Workspace.Remaster, Workspace.Share, Workspace.Play }) {
				model.SelectWorkspace(w);
				Dispatcher.UIThread.RunJobs();
				Assert.True(EmuApi.IsRunning());
				Assert.False(EmuApi.IsPaused(), $"switching to {w} paused the game");
				Assert.Equal(w != Workspace.Play, window.FindNamed<WorkspaceShellBar>("ShellBar").IsOnScreen());
				Assert.Contains("synthetic-nrom", model.Shell.StatusText);
			}

			UInt32 before = EmuApi.GetTimingInfo(CpuType.Nes).FrameCount;
			model.SelectWorkspace(Workspace.Remaster);
			WaitFor(() => EmuApi.GetTimingInfo(CpuType.Nes).FrameCount > before + 5, "no frames were emulated while in Remaster");
			model.SelectWorkspace(Workspace.Play);

			//A pause brings the bar back in Play (Esc's overlay pauses too).
			EmuApi.Pause();
			WaitFor(() => model.IsGamePaused, "the pause never reached the shell");
			Assert.True(window.FindNamed<WorkspaceShellBar>("ShellBar").IsOnScreen());
			Assert.Contains("paused", model.Shell.StatusText);
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
			try {
				Directory.Delete(folder, true);
			} catch(IOException) {
			}
		}
	}

	//scripts/gen_synthetic_nrom.py, byte for byte (as CopyAfterStateLoadTests).
	private static byte[] BuildSyntheticNrom()
	{
		const int prgSize = 32 * 1024;
		const int chrSize = 8 * 1024;
		byte[] rom = new byte[16 + prgSize + chrSize];
		rom[0] = (byte)'N';
		rom[1] = (byte)'E';
		rom[2] = (byte)'S';
		rom[3] = 0x1A;
		rom[4] = prgSize / 16384;
		rom[5] = chrSize / 8192;

		int prg = 16;
		for(int i = 0; i < prgSize; i++) {
			rom[prg + i] = 0xEA;
		}
		rom[prg + 0x0000] = 0x4C;
		rom[prg + 0x0001] = 0x00;
		rom[prg + 0x0002] = 0x80;
		rom[prg + 0x0003] = 0x40;
		rom[prg + 0x7FFA] = 0x03;
		rom[prg + 0x7FFB] = 0x80;
		rom[prg + 0x7FFC] = 0x00;
		rom[prg + 0x7FFD] = 0x80;
		rom[prg + 0x7FFE] = 0x03;
		rom[prg + 0x7FFF] = 0x80;
		return rom;
	}
}
