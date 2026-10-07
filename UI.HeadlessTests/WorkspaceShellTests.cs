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
using Mesen.Config.Shortcuts;
using Mesen.Debugger.Utilities;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//G.1 (PRD Part B §8, ADR-0241, §13.5.1 W-S1-W-S3): the shell's XAML wiring.
//The rules themselves (fixed order, ⌘ digits, bar visibility, the doors'
//menus - ADR-0250's WorkspaceMenu - and the status sentence) are pinned
//host-free in UI.Tests/Shell; this checks the crossing into MainWindow.axaml:
//that each door's Tools ⋯ and Classic's menu bar realize what the rule says,
//that the door owns UiMode, that only the active door is on screen, and -
//with the real core - that switching leaves the running game running.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on
//the core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class WorkspaceShellTests : IDisposable
{
	//ConfigManager.Config is process-global: every setting a test here touches
	//is restored afterwards, so the next class in the collection (e.g.
	//PackAudioNoticeInstallTests, gated on AutoInstallCommunityPacks) sees the
	//values it would have seen without this class.
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		//Workspace switches call Config.Save(); write
		//the restored values back so settings.json matches memory again.
		ConfigManager.Config.Save();
	}

	private static string Label(MenuItem item) => (item.Header as string ?? "").Replace("_", "");

	private static (MainWindow Window, MainWindowViewModel Model) ShowShell(UiMode uiMode = UiMode.Player)
	{
		ConfigManager.Config.Preferences.UiMode = uiMode;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
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

	//ADR-0250 Decision 3: the shared tail - Help ▸ only on macOS, where About,
	//Settings… and Quit are in the app menu; the four entries elsewhere.
	private static string[] Tail => OperatingSystem.IsMacOS()
		? new[] { "Help" }
		: new[] { "Settings…", "Help", "About MesenAI", "Quit MesenAI" };

	private static string[] DoorToolsLabels(MainWindow window)
	{
		MenuItem dots = OpenToolsDropdown(window);
		string[] labels = dots.GetRealizedContainers().OfType<MenuItem>().Select(Label).Where(l => l != "-" && l.Length > 0).ToArray();
		dots.Close();
		Dispatcher.UIThread.RunJobs();
		return labels;
	}

	//ADR-0250 Decision 3, as realized: each task door's Tools ⋯ is its own
	//short menu, then the tail. No game is loaded, so Play shows no console
	//item (disk, coin, barcode, tape).
	[AvaloniaFact]
	public void Each_task_door_tools_menu_follows_the_table()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();

		//Ruling (a) on #1007: Play alone ends with the disabled hint row.
		Assert.Equal(new[] { "Reset", "Power Cycle", "Screenshot", "Fullscreen" }.Concat(Tail).Append(ContextMenuHint.Sentinel).ToArray(), DoorToolsLabels(window));

		model.SelectWorkspace(Workspace.Remaster);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(new[] { "Reload Pack Images", "Record Music", "Enhancement Packs", "Log Window" }.Concat(Tail).ToArray(), DoorToolsLabels(window));

		model.SelectWorkspace(Workspace.Share);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(new[] { "Play a Replay…", "Record", "Netplay", "Screenshot" }.Concat(Tail).ToArray(), DoorToolsLabels(window));
	}

	//Nothing of the classic menus is in a task door's Tools ⋯ any more
	//(Decision 3), and the Show classic menu bar toggle is gone.
	[AvaloniaFact]
	public void Task_door_tools_menu_has_no_classic_menus_and_no_toggle()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowShell();

		string[] labels = DoorToolsLabels(window);
		foreach(string classic in new[] { "File", "Game", "Options", "Tools", "Debug", "Show Classic Menu Bar" }) {
			Assert.DoesNotContain(classic, labels);
		}
		Assert.False(window.FindNamed<MainMenuView>("MainMenu").IsOnScreen());
	}

	//The P.4 gate that disabled every Debug action in Player is retired: a
	//Debug entry is enabled exactly as it would be in Advanced.
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

	//ADR-0250 Decision 2: an Advanced install opens in Classic - the original
	//GUI: its menu bar (with Workspace ▸), no shell bar, no status line.
	[AvaloniaFact]
	public void An_advanced_install_opens_in_classic_with_the_classic_menu_bar_and_no_shell()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell(UiMode.Advanced);

		Assert.Equal(Workspace.Classic, model.Shell.Active);
		Assert.Equal(Workspace.Classic, ConfigManager.Config.Preferences.Workspace);
		Assert.True(model.IsMenuVisible);
		Assert.True(window.FindNamed<MainMenuView>("MainMenu").IsOnScreen());
		Assert.False(window.FindNamed<WorkspaceShellBar>("ShellBar").IsOnScreen());
		Assert.False(window.FindNamed<Border>("ShellStatusLine").IsOnScreen());
		//The plain game screen (and the classic home) is Classic's.
		Assert.True(window.FindNamed<Panel>("PlayWorkspace").IsOnScreen());
		Assert.DoesNotContain("player", window.FindNamed<Panel>("PlayWorkspace").Classes);

		Menu classicBar = window.FindNamed<Menu>("ActionMenu");
		string[] menus = classicBar.GetRealizedContainers().OfType<MenuItem>().Select(Label).ToArray();
		Assert.Equal(new[] { "File", "Game", "Settings", "Tools", "Debug", "Help", "Workspace" }, menus);
		Assert.Equal(new[] { "Play", "Remaster", "Share" }, RealizedLabels(Child(classicBar, "Workspace")).Where(l => l != "-").ToArray());
	}

	//ADR-0250 Decision 4: Classic loses its duplicates and nothing else.
	[AvaloniaFact]
	public void Classic_menu_bar_has_no_duplicates()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowShell(UiMode.Advanced);
		Menu classicBar = window.FindNamed<Menu>("ActionMenu");

		//One Pause/Resume entry, whatever the state.
		string[] game = RealizedLabels(Child(classicBar, "Game"));
		Assert.Single(game, l => l == "Pause" || l == "Resume");

		MenuItem tools = Child(classicBar, "Tools");
		tools.Open();
		Dispatcher.UIThread.RunJobs();
		string[] movies = RealizedLabels(Child(tools, "Movies"));
		Assert.Contains("Record...", movies);
		Assert.DoesNotContain(movies, l => l.StartsWith("Record and"));
		tools.Close();
		Dispatcher.UIThread.RunJobs();

		string[] help = RealizedLabels(Child(classicBar, "Help"));
		Assert.DoesNotContain("Online Help", help);
		Assert.DoesNotContain("Report a bug", help);
		Assert.Contains("Check for updates", help);

		string[] file = RealizedLabels(Child(classicBar, "File"));
		string[] options = RealizedLabels(Child(classicBar, "Settings"));
		//On macOS About, Preferences and Exit are in the system app menu.
		Assert.Equal(!OperatingSystem.IsMacOS(), file.Contains("Exit"));
		Assert.Equal(!OperatingSystem.IsMacOS(), options.Contains("Preferences"));
		Assert.Equal(!OperatingSystem.IsMacOS(), help.Contains("About"));
	}

	//ADR-0250 Decision 2: the door owns UiMode - entering Classic sets
	//Advanced and brings the classic bar; Workspace ▸ › Play goes back to
	//Player, with the shell bar and without the classic bar.
	[AvaloniaFact]
	public void Ui_mode_follows_the_door()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		RawInputModifiers modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

		window.KeyPress(Key.D4, modifier, PhysicalKey.Digit4, "4");
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(Workspace.Classic, model.Shell.Active);
		Assert.Equal(UiMode.Advanced, ConfigManager.Config.Preferences.UiMode);
		Assert.True(window.FindNamed<MainMenuView>("MainMenu").IsOnScreen());
		Assert.False(window.FindNamed<WorkspaceShellBar>("ShellBar").IsOnScreen());

		MenuItem workspace = Child(window.FindNamed<Menu>("ActionMenu"), "Workspace");
		workspace.Open();
		Dispatcher.UIThread.RunJobs();
		Child(workspace, "Play").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(Workspace.Play, model.Shell.Active);
		Assert.Equal(UiMode.Player, ConfigManager.Config.Preferences.UiMode);
		Assert.False(window.FindNamed<MainMenuView>("MainMenu").IsOnScreen());
		Assert.True(window.FindNamed<WorkspaceShellBar>("ShellBar").IsOnScreen());

		//The Preferences combo still sets UiMode: it moves to the owning door.
		ConfigManager.Config.Preferences.UiMode = UiMode.Advanced;
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(Workspace.Classic, model.Shell.Active);
	}

	//The shared tail's Settings… opens the W-P8 sheet in the door it was
	//picked from; Esc closes it there (Decision 3).
	[AvaloniaFact]
	public void Settings_from_a_task_door_opens_the_sheet_there_and_esc_closes_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowShell();
		model.SelectWorkspace(Workspace.Remaster);
		Dispatcher.UIThread.RunJobs();

		model.MainMenu.OpenSettings(window);
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.IsPlayerSettingsVisible);
		Assert.True(window.FindNamed<PlayerSettingsSheetView>("PlayerSettingsSheetHost").IsOnScreen());
		Assert.Equal(Workspace.Remaster, model.Shell.Active);

		//Esc is the ToggleOverlay shortcut; the Core's key manager delivers
		//it, which headless cannot drive - run the handler as it would.
		new ShortcutHandler(window).ExecuteShortcut(EmulatorShortcut.ToggleOverlay);
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.IsPlayerSettingsVisible);
		Assert.False(model.IsPlayerOverlayVisible);
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

		//ADR-0250: Classic has no shell bar and keeps the plain title bar.
		model.SelectWorkspace(Workspace.Classic);
		Dispatcher.UIThread.RunJobs();
		Assert.False(window.ExtendClientAreaToDecorationsHint);
		model.SelectWorkspace(Workspace.Play);
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.ExtendClientAreaToDecorationsHint);

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
		//The other doors exist only inside the closed popover.
		Assert.DoesNotContain(window.FindAll<TextBlock>(), t => t.IsOnScreen() && (t.Text == "Remaster" || t.Text == "Share" || t.Text == "Classic"));
		Assert.Equal("No game loaded", window.FindNamed<TextBlock>("ShellStatusText").Text);
		Assert.True(window.FindNamed<Border>("ShellStatusLine").IsOnScreen());
	}

	[AvaloniaFact]
	public void Switcher_popover_lists_the_four_doors_in_order_and_picking_switches()
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
		Assert.Equal(new object?[] { Workspace.Play, Workspace.Remaster, Workspace.Share, Workspace.Classic }, buttons.Select(b => b.Tag).ToArray());

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

		window.KeyPress(Key.D4, modifier, PhysicalKey.Digit4, "4");
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(Workspace.Classic, model.Shell.Active);

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
			//W-S1: the line names the game, never "is paused" (the overlay says so).
			Assert.Equal("synthetic-nrom", model.Shell.StatusText);
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
