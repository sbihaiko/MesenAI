using System;
using System.Collections.Generic;
using System.Globalization;
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

	private static (MainWindow Window, MainWindowViewModel Model) Show(Workspace workspace, InterfaceSize size, double width = 1024, double height = 640)
	{
		ConfigManager.Config.Preferences.UiMode = WorkspaceShell.UiModeFor(workspace);
		ConfigManager.Config.Preferences.Workspace = workspace;
		ConfigManager.Config.Preferences.InterfaceSize = size;
		//The window's default size on a desktop; the headless host's own is smaller.
		MainWindow main = new() { Width = width, Height = height };
		main.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		return (main, Assert.IsType<MainWindowViewModel>(main.DataContext));
	}

	//A control's box in the window's own coordinates, transform included.
	private static Rect BoxIn(Control control, MainWindow window)
	{
		Point topLeft = control.TranslatePoint(new Point(0, 0), window)!.Value;
		Point bottomRight = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
		return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
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
		Rect doneBox = new(done.TranslatePoint(new Point(0, 0), window)!.Value, done.Bounds.Size);
		Assert.True(doneBox.Right <= window.Bounds.Width, $"Done's right edge {doneBox.Right} is past the {window.Bounds.Width} window");
		Assert.True(doneBox.Bottom <= window.Bounds.Height, $"Done's bottom edge {doneBox.Bottom} is past the {window.Bounds.Height} window");
		model.ClosePlayerSettings();
	}

	//#1123 (ADR-0269 Decision 6, the width half): the height is capped against
	//the transformed room the host gives the sheet, the width was not - a fixed
	//480 px became 720 at 1.5 and hung off both sides of the window's own
	//512x505 starting size. The width is capped the same way, so the sheet's
	//rendered box stays inside the window at both sizes the ADR guarantees.
	[AvaloniaTheory]
	[InlineData(1024, 640)]
	[InlineData(512, 505)]
	public void The_sheet_width_is_capped_and_stays_inside_the_window(double width, double height)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge, width, height);
		window.OpenPlayerSettingsSheet();
		Settle(window);

		Rect sheet = BoxIn(window.FindNamed<Border>("PlayerSettingsSheet"), window);
		Assert.True(sheet.Left >= 0 && sheet.Right <= window.Bounds.Width, $"The sheet {sheet} is not inside a {window.Bounds.Size} window");
		//The uncapped 480 * 1.5 = 720 must never be drawn when the window cannot
		//hold it: the cap leaves the sheet a margin inside the window.
		Assert.True(sheet.Width <= window.Bounds.Width - 32, $"The sheet is {sheet.Width} wide in a {window.Bounds.Width} window: the 1.5 factor is not capped");
		//...and the cap never shrinks a sheet the window can hold: at the ADR's
		//guaranteed 1024x640 the sheet is still the whole 480 at 1.5.
		if(width >= 1024) {
			Assert.Equal(720, sheet.Width, 0);
		}
		//The strip narrows its segments by PlayerSettingsEssentials.SegmentWidth,
		//a rule UI.Tests pins host-free; here it only has to leave every tab on
		//screen. Whether the rows themselves fit across the narrower sheet is
		//what No_row_label_is_trimmed_in_the_small_window_at_the_largest_size
		//proves - the page's own scroller is vertical only
		//(HorizontalScrollBarVisibility="Disabled"), so its Extent.Width is its
		//Viewport.Width by construction and cannot witness a clipped row.
		Assert.All(window.FindNamed<TabControl>("PlayerSettingsTabs").FindAll<TabItem>(), t => Assert.True(t.IsOnScreen() && t.Bounds.Width > 0, $"{t.Name} has no room in the strip"));
		model.ClosePlayerSettings();
	}

	//What a label's text needs to be drawn in one line, independent of how much
	//room the layout gave it: the same font the TextBlock carries, laid out with
	//no constraint. A label whose Bounds are narrower than this is drawing part
	//of itself, which is what "clipped" means here.
	private static double TextWidth(TextBlock label)
	{
		Typeface typeface = new(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch);
		FormattedText text = new(label.Text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, label.FontSize, null);
		return text.Width;
	}

	//#1123 (ADR-0269 Decision 6, "nothing is clipped"): capping the sheet's width
	//only helps if the rows inside the narrower sheet still have their room. At
	//1.5 in 512x505 the page viewport is about 254 px, and a row whose
	//right-docked control carries a fixed 200 px - or a 150 px slider beside a
	//34 px readout - leaves the label a sliver: the text is drawn mid-word. The
	//cap has to reach the rows, on every tab, not only the default one.
	[AvaloniaTheory]
	[InlineData(ConfigWindowTab.Display)]
	[InlineData(ConfigWindowTab.Look)]
	[InlineData(ConfigWindowTab.Audio)]
	[InlineData(ConfigWindowTab.Input)]
	[InlineData(ConfigWindowTab.System)]
	public void No_row_label_is_trimmed_in_the_small_window_at_the_largest_size(ConfigWindowTab tab)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge, 512, 505);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		int index = PlayerSettingsEssentials.IndexOf(tab);
		Assert.True(index >= 0, $"{tab} is not a tab of the Play settings strip");
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = index;
		Settle(window);

		ScrollViewer page = window.FindNamed<TabControl>("PlayerSettingsTabs").FindAll<ScrollViewer>().First(s => s.Classes.Contains("pageScroll") && s.IsOnScreen());
		List<string> trimmed = [];
		foreach(TextBlock label in page.GetVisualDescendants().OfType<TextBlock>()) {
			if(!label.IsEffectivelyVisible || string.IsNullOrEmpty(label.Text)) {
				continue;
			}
			//Only the row's own labels: a TextBlock a control generated for itself
			//(a popup's closed value) is the control's business, not the row's.
			if(label.Parent is not Panel) {
				continue;
			}
			//A label that asks to be ellipsized (a folder path, #1013) is not one
			//this rule covers: it says so itself, and shows the ellipsis.
			if(label.TextTrimming != TextTrimming.None) {
				continue;
			}
			//Neither is one that asks to wrap: it is not drawn past its room, it
			//uses another line.
			if(label.TextWrapping != TextWrapping.NoWrap) {
				continue;
			}
			double needed = TextWidth(label);
			if(needed > label.Bounds.Width + 1) {
				trimmed.Add($"{(string.IsNullOrEmpty(label.Name) ? "unnamed" : label.Name)} \"{label.Text}\" needs {needed:0.#} px in {label.Bounds.Width:0.#}");
			}
		}
		Assert.True(trimmed.Count == 0, $"On {tab} the rows draw their labels past their own room: {string.Join("; ", trimmed)}");
		model.ClosePlayerSettings();
	}

	//The label rule above cannot see a control that is drawn over its label:
	//once the label sits in an Auto column its own Bounds are the width its
	//text needs, so "needed > Bounds" never fires. What Decision 6's "nothing
	//is clipped" fails on when the sheet narrows is a sibling overlap - a
	//right-docked control that carries a fixed 150 or 200 px is arranged at
	//that width whatever cell it was given, and a Grid does not clip, so it is
	//drawn over the label's column. This walks the row Grids themselves at the
	//guaranteed 512x505, at factor 1.5, on every tab: each visible child's box
	//sits inside its row's box, and no two of them intersect.
	private static List<Grid> RowGrids(Control page) =>
		page.GetVisualDescendants().OfType<Grid>()
			.Where(g => g.Classes.Contains("setting-row") || g.Classes.Contains("look-row"))
			.ToList();

	private static string Describe(Control control) =>
		$"{(string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name)} [{control.Bounds.Width:0.#}x{control.Bounds.Height:0.#}]";

	private static bool Inside(Rect outer, Rect inner) =>
		inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
		inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

	[AvaloniaTheory]
	[InlineData(ConfigWindowTab.Display)]
	[InlineData(ConfigWindowTab.Look)]
	[InlineData(ConfigWindowTab.Audio)]
	[InlineData(ConfigWindowTab.Input)]
	[InlineData(ConfigWindowTab.System)]
	public void No_row_child_escapes_its_row_or_covers_a_sibling_in_the_small_window_at_the_largest_size(ConfigWindowTab tab)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge, 512, 505);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		int index = PlayerSettingsEssentials.IndexOf(tab);
		Assert.True(index >= 0, $"{tab} is not a tab of the Play settings strip");
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = index;
		Settle(window);

		ScrollViewer page = window.FindNamed<TabControl>("PlayerSettingsTabs").FindAll<ScrollViewer>().First(s => s.Classes.Contains("pageScroll") && s.IsOnScreen());
		List<Grid> rows = RowGrids(page);
		//System carries no setting row of its own (storage and keyboard choices,
		//not label/control rows); everywhere else the walk has to find rows or it
		//proves nothing.
		if(tab != ConfigWindowTab.System) {
			Assert.True(rows.Count > 0, $"No row Grid is drawn on {tab}: the walk proves nothing");
		}

		List<string> broken = [];
		foreach(Grid row in rows) {
			Rect rowBox = BoxIn(row, window);
			List<(Control Control, Rect Box)> boxes = [];
			foreach(Control child in row.Children) {
				if(!child.IsEffectivelyVisible || child.Bounds.Width <= 0 || child.Bounds.Height <= 0) {
					continue;
				}
				Rect box = BoxIn(child, window);
				if(!Inside(rowBox, box)) {
					broken.Add($"on {tab} {Describe(child)} is drawn at {box} outside its row {rowBox}");
				}
				boxes.Add((child, box));
			}
			for(int i = 0; i < boxes.Count; i++) {
				for(int j = i + 1; j < boxes.Count; j++) {
					if(boxes[i].Box.Intersects(boxes[j].Box)) {
						broken.Add($"on {tab} {Describe(boxes[i].Control)} at {boxes[i].Box} covers {Describe(boxes[j].Control)} at {boxes[j].Box}");
					}
				}
			}
		}
		Assert.True(broken.Count == 0, $"The rows draw past their own room: {string.Join("; ", broken)}");
		model.ClosePlayerSettings();
	}

	//#1123: the cap follows the room, so it shrinks with the window instead of
	//leaving Done past the right edge - on every tab, at the largest size, in
	//the window's own starting size.
	[AvaloniaTheory]
	[InlineData(ConfigWindowTab.Display)]
	[InlineData(ConfigWindowTab.Look)]
	[InlineData(ConfigWindowTab.Audio)]
	[InlineData(ConfigWindowTab.Input)]
	[InlineData(ConfigWindowTab.System)]
	public void Done_stays_inside_the_small_window_at_the_largest_size(ConfigWindowTab tab)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge, 512, 505);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		int index = PlayerSettingsEssentials.IndexOf(tab);
		Assert.True(index >= 0, $"{tab} is not a tab of the Play settings strip");
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = index;
		Settle(window);

		Button done = window.FindNamed<Button>("btnPlayerSettingsDone");
		Assert.True(done.IsOnScreen(), $"Done is off screen on {tab}");
		Rect doneBox = BoxIn(done, window);
		Assert.True(doneBox.Left >= 0 && doneBox.Top >= 0, $"Done starts at {doneBox.TopLeft} on {tab}");
		Assert.True(doneBox.Right <= window.Bounds.Width, $"Done's right edge {doneBox.Right} is past the {window.Bounds.Width} window on {tab}");
		Assert.True(doneBox.Bottom <= window.Bounds.Height, $"Done's bottom edge {doneBox.Bottom} is past the {window.Bounds.Height} window on {tab}");
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

	//Home now sits above the frozen frame in PlayWorkspace's z-order (it moved
	//from MainWindow.axaml:181 to :230, on purpose, to sit under PlayChromeRoot):
	//the two are never visible together, whether or not a sheet is open.
	[AvaloniaFact]
	public void Home_and_the_frozen_frame_are_never_visible_together()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge);
		Settle(window);
		Control home = window.FindNamed<Control>("PlayHomeHost");
		Control frame = window.FindNamed<Control>("PausedGameFrame");
		Assert.False(home.IsVisible && frame.IsVisible, "Home and the frozen frame are both visible at rest");

		window.OpenPlayerSettingsSheet();
		Settle(window);
		Assert.False(home.IsVisible && frame.IsVisible, "Home and the frozen frame are both visible under a sheet");
		model.ClosePlayerSettings();
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
