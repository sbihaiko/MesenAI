using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
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
	[InlineData(ConfigWindowTab.Video)]
	[InlineData(ConfigWindowTab.Audio)]
	[InlineData(ConfigWindowTab.Input)]
	[InlineData(ConfigWindowTab.System)]
	public void Done_stays_inside_the_window_at_the_largest_size(ConfigWindowTab tab)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(tab);
		Settle(window);

		Button done = window.FindNamed<Button>("btnPlayerSettingsDone");
		Assert.True(done.IsOnScreen());
		Point topLeft = done.TranslatePoint(new Point(0, 0), window)!.Value;
		Point bottomRight = done.TranslatePoint(new Point(done.Bounds.Width, done.Bounds.Height), window)!.Value;
		Assert.True(topLeft.Y >= 0 && topLeft.X >= 0, $"Done starts at {topLeft} on {tab}");
		Assert.True(bottomRight.Y <= window.Bounds.Height && bottomRight.X <= window.Bounds.Width, $"Done ends at {bottomRight} in a {window.Bounds.Size} window on {tab}");
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
