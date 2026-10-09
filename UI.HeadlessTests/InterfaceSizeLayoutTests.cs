using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1111 (ADR-0269 Decisions 3 and 6): Interface size scales Play's chrome only,
//and Settings stays reachable - Done inside the window - at the largest size in
//the default window. The factor itself is host-free (UI.Tests/Play/InterfaceSizeTests).
[Collection(NativeCoreCollection.Name)]
public class InterfaceSizeLayoutTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly InterfaceSize _size = ConfigManager.Config.Preferences.InterfaceSize;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		ConfigManager.Config.Preferences.InterfaceSize = _size;
	}

	private static (MainWindow Window, MainWindowViewModel Model) Show(Workspace workspace, InterfaceSize size)
	{
		ConfigManager.Config.Preferences.UiMode = WorkspaceShell.UiModeFor(workspace);
		ConfigManager.Config.Preferences.Workspace = workspace;
		ConfigManager.Config.Preferences.InterfaceSize = size;
		//The window's default size on a desktop; the headless host's own is smaller.
		MainWindow main = new() { Width = 1024, Height = 640 };
		main.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		return (main, Assert.IsType<MainWindowViewModel>(main.DataContext));
	}

	private static double ScaleOf(MainWindow window, string layer)
	{
		LayoutTransformControl control = window.FindNamed<LayoutTransformControl>(layer);
		return ((ScaleTransform)control.LayoutTransform!).Value.M11;
	}

	private static void Settle(MainWindow window)
	{
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
	}

	//Controls a person can act on: the strip counts as one (the render's "N controls at rest").
	private static int ControlsAtRest(Control root)
	{
		int controls = root.FindAll<Control>().Count(c => c.IsOnScreen() && c.Focusable && c.IsEffectivelyEnabled && c is Button or ComboBox or ToggleButton && c is not TabItem);
		return controls + root.FindAll<TabControl>().Count(t => t.IsOnScreen());
	}

	//The layers above the workspaces (Settings, the load card, the BIOS and
	//tool sheets) appear from Remaster and Share too: there they keep size 1.
	[AvaloniaTheory]
	[InlineData(Workspace.Remaster, 1.0)]
	[InlineData(Workspace.Share, 1.0)]
	[InlineData(Workspace.Play, 1.5)]
	public void The_layers_above_the_workspaces_read_the_size_only_in_play(Workspace workspace, double factor)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(workspace, InterfaceSize.ExtraLarge);
		window.OpenPlayerSettingsSheet();
		model.ToolSheet.OpenCheckForUpdates();
		Settle(window);

		Assert.True(window.FindNamed<Panel>("PlayerSettingsLayer").IsOnScreen());
		Assert.Equal(factor, ScaleOf(window, "PlayerSettingsLayerScale"));
		Assert.Equal(factor, ScaleOf(window, "LoadWaitScale"));
		Assert.Equal(factor, ScaleOf(window, "BiosSheetScale"));
		model.ToolSheet.Close();
		model.ClosePlayerSettings();
	}

	//ADR-0269 Decision 6: page rows scroll, Done stays pinned and reachable, on
	//every tab, at the largest size, in the window's default size.
	[AvaloniaTheory]
	[InlineData(ConfigWindowTab.Display)]
	[InlineData(ConfigWindowTab.Look)]
	[InlineData(ConfigWindowTab.Audio)]
	[InlineData(ConfigWindowTab.Input)]
	[InlineData(ConfigWindowTab.System)]
	public void Done_stays_inside_the_window_at_the_largest_size(ConfigWindowTab tab)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		int index = PlayerSettingsEssentials.IndexOf(tab);
		Assert.True(index >= 0, $"{tab} is not a tab of the Play settings strip");
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = index;
		Settle(window);

		Button done = window.FindNamed<Button>("btnPlayerSettingsDone");
		Assert.True(done.IsOnScreen());
		Point topLeft = done.TranslatePoint(new Point(0, 0), window)!.Value;
		Point bottomRight = done.TranslatePoint(new Point(done.Bounds.Width, done.Bounds.Height), window)!.Value;
		Assert.True(topLeft.Y >= 0 && topLeft.X >= 0, $"Done starts at {topLeft} on {tab}");
		Assert.True(bottomRight.Y <= window.Bounds.Height && bottomRight.X <= window.Bounds.Width, $"Done ends at {bottomRight} in a {window.Bounds.Size} window on {tab}");
		model.ClosePlayerSettings();
	}

	//Decision 6, the other half: at 1.5 in 1024x640 the rows do not fit, so the
	//page scrolls (extent past viewport) while Done sits outside that scroller -
	//pinned, focusable and enabled, so the pad lands on it however far the page is.
	[AvaloniaFact]
	public void The_page_scrolls_at_the_largest_size_and_the_pad_can_reach_done()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Display);
		Settle(window);

		Assert.Equal(1.5, ScaleOf(window, "PlayerSettingsLayerScale"));
		ScrollViewer page = window.FindNamed<TabControl>("PlayerSettingsTabs").FindAll<ScrollViewer>().First(s => s.Classes.Contains("pageScroll") && s.IsOnScreen());
		Assert.True(page.Extent.Height > page.Viewport.Height, $"Page extent {page.Extent} fits its viewport {page.Viewport}: nothing scrolls");

		Button done = window.FindNamed<Button>("btnPlayerSettingsDone");
		Assert.False(done.GetVisualAncestors().Contains(page), "Done sits inside the scrolling page");
		Assert.True(done.Focusable && done.IsEffectivelyEnabled && done.IsHitTestVisible);
		Assert.True(done.Focus());
		model.ClosePlayerSettings();
	}

	//Decision 3: Home (W-P1/W-P2) is part of the chrome - it scales through
	//PlayChromeRoot's transform, with no transform of its own.
	[AvaloniaFact]
	public void Home_scales_with_the_play_chrome()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = Show(Workspace.Play, InterfaceSize.ExtraLarge);
		Settle(window);

		Control home = window.FindNamed<Control>("PlayHomeHost");
		Assert.Equal(1.5, ScaleOf(window, "PlayChromeRoot"));
		Assert.Contains(window.FindNamed<LayoutTransformControl>("PlayChromeRoot"), home.GetVisualAncestors());
		Assert.Equal(1.5, home.TransformToVisual(window)!.Value.M11);
	}

	//W-P10: Look's Hold to Compare shares Done's row, left of it - the page's
	//own scroller holds the rows, so neither is pushed out or covered.
	[AvaloniaFact]
	public void Look_keeps_hold_to_compare_beside_done_without_overlap()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look);
		Settle(window);

		Button done = window.FindNamed<Button>("btnPlayerSettingsDone");
		Button hold = window.FindNamed<Button>("btnLookHoldToCompare");
		Assert.True(done.IsOnScreen());
		Assert.True(hold.IsOnScreen());
		Rect doneBox = new(done.TranslatePoint(new Point(0, 0), window)!.Value, done.Bounds.Size);
		Rect holdBox = new(hold.TranslatePoint(new Point(0, 0), window)!.Value, hold.Bounds.Size);
		Assert.False(doneBox.Intersects(holdBox), $"Hold to Compare {holdBox} overlaps Done {doneBox}");
		Assert.True(holdBox.Bottom <= window.Bounds.Height && doneBox.Bottom <= window.Bounds.Height);
		model.ClosePlayerSettings();
	}

	//§13.3 rule 2: strip + 4 rows + Done = 6; the window being full screen adds
	//Exit full screen to Done's row, the 7th and last element of the budget.
	[AvaloniaTheory]
	[InlineData(false, 6)]
	[InlineData(true, 7)]
	public void The_display_sheet_stays_inside_the_seven_element_budget(bool fullScreen, int controls)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.Standard);
		if(fullScreen) {
			window.WindowState = WindowState.FullScreen;
		}
		window.OpenPlayerSettingsSheet();
		Settle(window);

		Assert.Equal(controls, ControlsAtRest(window.FindNamed<Border>("PlayerSettingsSheet")));
		model.ClosePlayerSettings();
		window.WindowState = WindowState.Normal;
	}
}
