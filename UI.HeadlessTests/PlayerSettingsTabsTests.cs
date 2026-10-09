using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//P.4 / PRD Part B §6, plan subsection 2B: "the Player Settings reduced tab set
//rendering"; since G.4, PRD Part B §13.5.2 W-P8. `PlayerSettingsEssentials`
//(which tab is in the Player strip, and in what order) is covered host-free in
//UI.Tests; what is covered here is the step after it - that Player mode's
//Settings sheet (PlayerSettingsSheetView, ADR-0249) shows its own strip
//(Display | Look | Audio | Controls | System, the last added by ADR-0256
//Decision 8) with the hint and Done and no Advanced tab list, and that
//Advanced's ConfigWindow still shows every tab and no strip.
[NativeCoreFree("Opens the Settings sheet (with injected device and pad sources) and ConfigWindow on the Input tab; only the classic Audio/Video/Display/Look tab view-models reach ConfigApi/EmuApi on construction.")]
[Collection(PlayerSettingsSeamsCollection.Name)]
public class PlayerSettingsTabsTests
{
	//Input is the tab a host-free run opens: its classic view-model does not
	//reach the native core on construction (Audio enumerates devices through
	//ConfigApi, Display and Look read the core). The tab bars under test are
	//the same either way.
	private static ConfigWindow ShowAdvancedSettings()
	{
		ConfigWindow window = new(ConfigWindowTab.Input);
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return window;
	}

	//ADR-0249: Player mode's Settings is a sheet MainWindow hosts in its Player
	//scope; here the same view in a bare Player-scoped window, core-free.
	private static Window ShowPlayerSheet()
	{
		PlayerSettingsSheetView sheet = new() { DataContext = new ConfigViewModel(ConfigWindowTab.Input, playerMode: true, audioDevices: () => new[] { "Speakers" }, connectedPads: () => 0) };
		Window window = new() { Width = 1100, Height = 740, Content = new Panel { Classes = { "player" }, Children = { sheet } } };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return window;
	}

	[AvaloniaFact]
	public void Player_settings_shows_the_display_look_audio_controls_system_strip()
	{
		Window window = ShowPlayerSheet();

		TabControl strip = window.FindNamed<TabControl>("PlayerSettingsTabs");
		Assert.True(strip.IsOnScreen());
		List<TabItem> tabs = strip.Items.Cast<TabItem>().ToList();
		Assert.Equal(PlayerSettingsEssentials.Tabs.Length, tabs.Count);
		Assert.Equal(new[] { "Display", "Look", "Audio", "Controls", "System" }, tabs.Select(t => t.Header as string).ToArray());
		Assert.All(tabs, tab => Assert.True(tab.IsOnScreen()));
		Assert.Equal(PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Input), tabs.FindIndex(t => t.IsSelected));

		//The Advanced tab list is not there; neither are OK/Cancel - the hint
		//and Done take their place (rule 10, W-P8's five elements).
		Assert.DoesNotContain(window.FindAll<TabControl>(), t => t.Name == "AdvancedSettingsTabs");
		Assert.True(window.FindNamed<Button>("btnPlayerSettingsDone").IsOnScreen());
		Assert.Equal("Everything else: Classic › Settings", window.FindNamed<TextBlock>("lblPlayerSettingsEverythingElse").Text);
		Assert.DoesNotContain(window.FindAll<Button>().Where(b => b.IsOnScreen()), b => b.Content as string is "OK" or "Cancel");
	}

	[AvaloniaFact]
	public void Advanced_settings_still_shows_every_tab()
	{
		//Guards against the reduction being unconditional rather than bound to
		//PlayerMode - the failure mode a grep of the markup cannot tell apart.
		ConfigWindow window = ShowAdvancedSettings();

		List<TabItem> tabs = window.FindNamed<TabControl>("AdvancedSettingsTabs").Items.Cast<TabItem>().ToList();
		Assert.Equal(ConfigWindowTabOrder.Tabs.Length, tabs.Count);
		Assert.All(tabs, tab => Assert.True(tab.IsOnScreen()));
		Assert.DoesNotContain(window.FindAll<TabControl>(), t => t.Name == "PlayerSettingsTabs");
	}

	//#910: the sheet on Window (Display) with an injected window state, so the
	//fullscreen half needs no MainWindow and no core. `toggles` counts the
	//window's ToggleFullscreen calls.
	private static (Window Window, ConfigViewModel Model) ShowDisplaySheet(bool isFullscreen, List<string> toggles)
	{
		ConfigViewModel model = new(ConfigWindowTab.Display, playerMode: true,
			createDisplay: () => new PlayerWindowSettingsViewModel(new VideoConfig(), isFullscreen, 2, () => toggles.Add("toggle"), _ => { }),
			audioDevices: () => new[] { "Speakers" }, connectedPads: () => 0);
		PlayerSettingsSheetView sheet = new() { DataContext = model };
		Window window = new() { Width = 1100, Height = 740, Content = new Panel { Classes = { "player", "play" }, Children = { sheet } } };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	[AvaloniaFact]
	public void Exit_fullscreen_is_not_offered_in_a_window()
	{
		(Window window, ConfigViewModel model) = ShowDisplaySheet(false, new List<string>());
		try {
			Assert.False(window.FindNamed<Button>("btnPlayerSettingsExitFullscreen").IsOnScreen());
			Assert.True(window.FindNamed<Button>("btnPlayerSettingsDone").IsOnScreen());
		} finally {
			Close(window, model);
		}
	}

	//Visible only while fullscreen and only on Window: Video keeps Done's row
	//for Hold to Compare, and the other tabs have nothing to say about the window.
	[AvaloniaFact]
	public void Exit_fullscreen_shows_on_window_while_fullscreen_only()
	{
		(Window window, ConfigViewModel model) = ShowDisplaySheet(true, new List<string>());
		try {
			Button exit = window.FindNamed<Button>("btnPlayerSettingsExitFullscreen");
			Assert.True(exit.IsOnScreen());
			Assert.Equal("Exit full screen", exit.Content as string);

			foreach(ConfigWindowTab tab in PlayerSettingsEssentials.Tabs.Where(t => t is ConfigWindowTab.Audio or ConfigWindowTab.Input)) {
				model.PlayerTabIndex = PlayerSettingsEssentials.IndexOf(tab);
				Dispatcher.UIThread.RunJobs();
				Assert.False(exit.IsOnScreen(), $"Exit full screen shows on {tab}");
			}
			model.PlayerTabIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Display);
			Dispatcher.UIThread.RunJobs();
			Assert.True(exit.IsOnScreen());
		} finally {
			Close(window, model);
		}
	}

	//Keyboard (Tab) and pad (the bridge's directional search, scoped to the
	//sheet, then Confirm = Click) both reach it; focused it carries the Play
	//focus ring; pressing it returns to windowed once, hides it, and hands the
	//focus to Done so a pad is never left on a control that is gone.
	[AvaloniaFact]
	public void Exit_fullscreen_is_reachable_by_keyboard_and_pad_and_returns_to_windowed()
	{
		List<string> toggles = new();
		(Window window, ConfigViewModel model) = ShowDisplaySheet(true, toggles);
		try {
			Button exit = window.FindNamed<Button>("btnPlayerSettingsExitFullscreen");
			Button done = window.FindNamed<Button>("btnPlayerSettingsDone");
			Control sheet = window.FindNamed<Border>("PlayerSettingsSheet");

			//Keyboard: Tab from the Fullscreen switch reaches it.
			window.FindNamed<ToggleButton>("chkDisplayFullscreen").Focus(NavigationMethod.Tab);
			Dispatcher.UIThread.RunJobs();
			bool reached = false;
			for(int i = 0; i < 12 && !reached; i++) {
				window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
				Dispatcher.UIThread.RunJobs();
				reached = exit.IsFocused;
			}
			Assert.True(reached, "Tab never reached Exit full screen");

			//Pad: the same search PlayPadNavigationWiring runs (FindNextElement,
			//rooted at the sheet). Left from Done is Exit; Right from Exit is Done.
			IFocusManager manager = TopLevel.GetTopLevel(sheet)!.FocusManager!;
			Assert.Same(exit, manager.FindNextElement(NavigationDirection.Left, new FindNextElementOptions { FocusedElement = done, SearchRoot = sheet }));
			Assert.Same(done, manager.FindNextElement(NavigationDirection.Right, new FindNextElementOptions { FocusedElement = exit, SearchRoot = sheet }));

			//The ring (ADR-0256 Decision 3): a directional focus paints it.
			exit.Focus(NavigationMethod.Directional);
			Dispatcher.UIThread.RunJobs();
			Border background = exit.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Background");
			Assert.Equal((BoxShadows)Application.Current!.FindResource("PlayerFocusRing")!, background.BoxShadow);

			//Confirm, as the bridge's Activate raises it.
			exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			Dispatcher.UIThread.RunJobs();
			Assert.Equal(new[] { "toggle" }, toggles);
			Assert.False(model.Display!.IsFullscreen);
			Assert.False(exit.IsOnScreen());
			Assert.True(done.IsFocused);
			Assert.False(window.FindNamed<ToggleButton>("chkDisplayFullscreen").IsChecked);
		} finally {
			Close(window, model);
		}
	}

	//The Audio and Controls tabs' view-models observe the global config
	//(ConfigManager.Config.Audio/Input) and apply every change through the
	//native core. A sheet a test leaves undisposed keeps that observer alive, and
	//a later case that edits the config then calls the core - a
	//DllNotFoundException on the core-less CI runner, in someone else's test.
	private static void Close(Window window, ConfigViewModel model)
	{
		window.Close();
		model.Dispose();
	}

	//W-P8's Scale popup was blank for a window under 1× (#audit): the nearest
	//offered scale shows instead, and showing it does not resize the window.
	[AvaloniaFact]
	public void Scale_shows_the_nearest_offered_value_below_one()
	{
		double? resized = null;
		PlayerWindowSettingsViewModel display = new(new VideoConfig(), false, 0.5, () => { }, s => resized = s);
		Assert.NotNull(display.SelectedScale);
		Assert.Equal(1, display.SelectedScale!.Value);
		Assert.Null(resized);
		display.Dispose();
	}
}
