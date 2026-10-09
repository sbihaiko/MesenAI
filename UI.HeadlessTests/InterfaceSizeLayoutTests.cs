using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
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
	private readonly VideoFilterType _filter = ConfigManager.Config.Video.VideoFilter;
	private readonly string _shader = ConfigManager.Config.Video.ShaderFile;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		ConfigManager.Config.Preferences.InterfaceSize = _size;
		//#1149: a case that needs the Look reason writes these two.
		ConfigManager.Config.Video.VideoFilter = _filter;
		ConfigManager.Config.Video.ShaderFile = _shader;
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

	//What the same text needs drawn when it wraps inside `width`: FormattedText
	//trims nothing, so a drawn height short of this is the layout cutting the
	//text off (#1149's one ellipsized line).
	private static double WrappedHeight(TextBlock label, double width)
	{
		Typeface typeface = new(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch);
		FormattedText text = new(label.Text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, label.FontSize, null) {
			//A bounded width is what makes it wrap: FormattedText has no wrapping
			//switch of its own, only the width the lines are laid out in.
			MaxTextWidth = width,
			Trimming = TextTrimming.None
		};
		return text.Height;
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

	//#1145 review, item 1: a stretched control whose `MaxWidth` is smaller than
	//the column it sits in is *centred* in that column - the leftover lands on
	//both sides of it. At 1024x640 the page's star column is far wider than the
	//150 / 200 px cap, so the capped controls sat mid-row instead of against
	//the row's right edge: a slider left a gap before its own 34 px readout and
	//the two Look popups floated between their label and Adjust. ADR-0249 draws
	//them on the right ("the toggle and the two popups on the right"), and the
	//PRD's 1024x640 is a size where nothing moves. Right-aligned, the control
	//keeps its cap and the leftover is all on the label's side: what follows it
	//in the row is what it touches, bar that sibling's own margin, and the
	//row's own right edge when nothing follows.
	[AvaloniaTheory]
	[InlineData(ConfigWindowTab.Audio, "sldAudioVolume", 150.0)]
	[InlineData(ConfigWindowTab.Audio, "cboAudioDevice", 200.0)]
	[InlineData(ConfigWindowTab.Input, "sldControlsRumble", 150.0)]
	[InlineData(ConfigWindowTab.Input, "sldControlsDeadzone", 150.0)]
	[InlineData(ConfigWindowTab.Look, "cboLookPixels", 200.0)]
	[InlineData(ConfigWindowTab.Look, "cboLookScreen", 200.0)]
	public void A_capped_row_control_keeps_its_cap_at_its_row_s_right_edge(ConfigWindowTab tab, string name, double cap)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		int index = PlayerSettingsEssentials.IndexOf(tab);
		Assert.True(index >= 0, $"{tab} is not a tab of the Play settings strip");
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = index;
		Settle(window);

		Control control = window.FindNamed<Control>(name);
		Assert.True(control.IsOnScreen(), $"{name} is not on screen on {tab}");
		Grid row = control.GetVisualAncestors().OfType<Grid>().First(g => g.Classes.Contains("setting-row") || g.Classes.Contains("look-row"));
		//The cap is the control's own width, before the Interface size transform:
		//BoxIn reports the drawn box, which is this times 1.5.
		Assert.Equal(cap, control.Bounds.Width, 0.5);
		Rect box = BoxIn(control, window);

		//The cell boundary the control has to be flush against: the row's own
		//right edge, or the cell of the first thing drawn after it. That cell
		//starts at the sibling's origin less its own left margin, which is a
		//layout-space offset - it goes through the Interface size transform with
		//the control, so it is translated rather than subtracted.
		Rect rowBox = BoxIn(row, window);
		double flushAt = rowBox.Right;
		foreach(Control sibling in row.Children) {
			if(sibling == control || !sibling.IsEffectivelyVisible || sibling.Bounds.Width <= 0) {
				continue;
			}
			Rect siblingBox = BoxIn(sibling, window);
			if(siblingBox.Left >= box.Right - 0.5) {
				flushAt = Math.Min(flushAt, sibling.TranslatePoint(new Point(-sibling.Margin.Left, 0), window)!.Value.X);
			}
		}
		Assert.Equal(flushAt, box.Right, 0.5);
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

	//#1149 (ADR-0269 Decision 6, W-P10): the wireframe puts the compare note in
	//the footer row beside Hold to Compare, and there it stays wherever the row
	//has room for it - at the size W-P10 is drawn, ~1024x640, nothing moves. The
	//guaranteed 512x505 at 1.5 leaves the note about 14 px of the row, which is
	//no room at all; there the note takes the line above, on the page's own
	//width. This walks both ends: the same note, in the row at one size and on
	//its own line at the other - and asserts the note is drawn whole in both,
	//which is findings 1, 3 and 4 of the #1163 review.
	[AvaloniaTheory]
	[InlineData(1024, 640, false)]
	[InlineData(512, 505, true)]
	public void The_look_compare_note_keeps_the_row_where_the_row_has_room_for_it(double width, double height, bool onItsOwnLine)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		//Nothing to compare: Pixels and Screen are off, so the note carries the
		//reason instead of the hint.
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge, width, height);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look);
		Settle(window);

		Button hold = window.FindNamed<Button>("btnLookHoldToCompare");
		Button done = window.FindNamed<Button>("btnPlayerSettingsDone");
		TextBlock note = window.FindNamed<TextBlock>("txtLookCompareReason");
		Assert.True(note.IsOnScreen(), "The Look footer draws no note");
		Assert.False(string.IsNullOrEmpty(note.Text), "The Look footer's note is empty");

		Rect holdBox = BoxIn(hold, window);
		Rect noteBox = BoxIn(note, window);
		Assert.Equal(BoxIn(done, window).Top, holdBox.Top, 1);
		if(onItsOwnLine) {
			//No room in the row: the note is on the line above it, at the real
			//size this walks (512x505 at 1.5, the ADR's guarantee).
			Assert.True(noteBox.Bottom <= holdBox.Top + 1, $"The note {noteBox} is not above Hold to Compare {holdBox}");
			Assert.True(noteBox.Left <= holdBox.Left + 1, $"The note {noteBox} starts right of the button {holdBox}");
		} else {
			//Room: the note is the row's own, on the button's line and to its
			//right, as W-P10 draws it.
			Assert.True(Math.Abs(noteBox.Center.Y - holdBox.Center.Y) <= 2, $"The note {noteBox} is not on Hold to Compare's line {holdBox}");
			Assert.True(noteBox.Left >= holdBox.Right - 1, $"The note {noteBox} is not beside Hold to Compare {holdBox}");
		}
		Assert.False(noteBox.Intersects(BoxIn(done, window)), $"The note {noteBox} covers Done");
		Assert.False(noteBox.Intersects(holdBox), $"The note {noteBox} covers Hold to Compare {holdBox}");

		//Finding 1 and 4 of the #1163 review: the note is drawn whole in *either*
		//branch, the row's and its own line. It asks for no ellipsis, and the room
		//it was given covers every line its text needs there - which is what a
		//one-line, ellipsized row fell short of, and what nothing else in this
		//file catches off the reflowed size.
		Assert.Equal(TextTrimming.None, note.TextTrimming);
		double needed = WrappedHeight(note, note.Bounds.Width);
		Assert.True(note.Bounds.Height + 1 >= needed, $"\"{note.Text}\" needs {needed:0.#} px in the {note.Bounds.Width:0.#} the note was given and is drawn in {note.Bounds.Height:0.#}");

		//Finding 3 of the same review: the reserve line the footer is kept at is
		//drawn for no one - the other of the tab's two lines is measured with it
		//and never shown - so it is out of the automation tree (a screen reader
		//would otherwise read the hint and the reason at once, one of them the
		//opposite of what the tab says) and takes no hit.
		TextBlock reserve = window.FindNamed<TextBlock>("txtLookCompareReserve");
		Assert.False(reserve.IsHitTestVisible, "The reserve line takes hits");
		Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(reserve));
		model.ClosePlayerSettings();
	}

	//#1149 (ADR-0269 Decision 6): the Look footer's reason note was the one spot
	//the 512x505 guarantee missed. It shared Done's row inside a page about 271 px
	//wide at 1.5 and the 137 px Hold to Compare button left it about 14 px, so it
	//was drawn on one line and ellipsized - "bounded, not whole" (#1123). Where
	//the row has no room the note takes the line above instead. This walks the
	//worst case the ADR guarantees - the window's own starting size at the
	//largest size, with a reason that needs more than one line ("Nothing to
	//compare: Pixels and Screen are off") - and asserts the whole reason is
	//drawn, and that it stays a line or two rather than the 9-line column that
	//took two thirds of the tab before #1123.
	[AvaloniaFact]
	public void The_look_compare_reason_is_drawn_whole_in_the_small_window_at_the_largest_size()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		//Nothing to compare: Pixels and Screen are off, so the note carries the
		//reason instead of the hint.
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge, 512, 505);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look);
		Settle(window);

		Button hold = window.FindNamed<Button>("btnLookHoldToCompare");
		Assert.False(hold.IsEnabled, "Hold to Compare is on with nothing that would change");
		TextBlock note = window.FindNamed<TextBlock>("txtLookCompareReason");
		Assert.True(note.IsOnScreen(), "The Look footer draws no reason");
		string reason = note.Text ?? "";
		Assert.False(string.IsNullOrEmpty(reason), "The Look footer's reason is empty");

		//The whole reason is drawn: the room the note was given covers the lines
		//its text needs there. Ellipsized to a single line this falls short.
		double needed = WrappedHeight(note, note.Bounds.Width);
		Assert.True(note.Bounds.Height + 1 >= needed, $"\"{reason}\" needs {needed:0.#} px in the {note.Bounds.Width:0.#} the note was given and is drawn in {note.Bounds.Height:0.#}");

		//It is not the button's leftover, either: where the note has its own line
		//it is laid out on the page's own width, which is what lets the reason be
		//read at this size.
		ScrollViewer page = window.FindNamed<TabControl>("PlayerSettingsTabs").FindAll<ScrollViewer>().First(s => s.Classes.Contains("pageScroll") && s.IsOnScreen());
		Assert.True(note.Bounds.Width >= page.Bounds.Width - 2, $"The note is {note.Bounds.Width:0.#} px wide in a {page.Bounds.Width:0.#} px page");

		//...and it does not cost the tab: a line or two, not two thirds of it.
		Control look = window.FindAll<Mesen.Views.LookConfigView>().First();
		Assert.True(note.Bounds.Height <= look.Bounds.Height / 3, $"The note takes {note.Bounds.Height:0.#} px of the tab's {look.Bounds.Height:0.#}");

		//W-P10's row is unchanged where the note leaves it: Hold to Compare keeps
		//Done's row, left of it, and the note stays out of both.
		Button done = window.FindNamed<Button>("btnPlayerSettingsDone");
		Rect doneBox = BoxIn(done, window);
		Rect holdBox = BoxIn(hold, window);
		Rect noteBox = BoxIn(note, window);
		Assert.Equal(doneBox.Top, holdBox.Top, 1);
		Assert.False(noteBox.Intersects(doneBox), $"The reason {noteBox} covers Done {doneBox}");
		Assert.False(noteBox.Intersects(holdBox), $"The reason {noteBox} covers Hold to Compare {holdBox}");
		Assert.True(noteBox.Right <= window.Bounds.Width && noteBox.Bottom <= window.Bounds.Height, $"The reason {noteBox} is drawn outside the {window.Bounds.Size} window");
		model.ClosePlayerSettings();
	}

	//#1149 (review of PR #1163, finding 3): Pixels and Screen are the two settings
	//that decide whether Hold to Compare has anything to do, so toggling either
	//swaps the note between its hint and its reason. The footer's height is the
	//note's line while it has one, and the groups above it sit in the page's
	//scroller, so a line that comes and goes there would move them under the
	//user's hands as they change Look.
	[AvaloniaTheory]
	[InlineData(1024, 640)]
	[InlineData(512, 505)]
	public void The_look_footer_keeps_its_height_when_pixels_change(double width, double height)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge, width, height);
		window.OpenPlayerSettingsSheet();
		Settle(window);
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look);
		Settle(window);

		ScrollViewer page = window.FindNamed<TabControl>("PlayerSettingsTabs").FindAll<ScrollViewer>().First(s => s.Classes.Contains("pageScroll") && s.IsOnScreen());
		TextBlock note = window.FindNamed<TextBlock>("txtLookCompareReason");
		string off = note.Text ?? "";
		Rect noteOff = BoxIn(note, window);
		//The page is what the footer's height is taken from, so its own box is
		//the rows above moving: a line more or less in the footer shows here.
		Rect pageOff = BoxIn(page, window);

		//Smoothing on: Pixels has something to compare, so the note swaps to the
		//hint. (The two strings differ, this is not a no-op toggle.)
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.HQ4x;
		Settle(window);
		Assert.NotEqual(off, note.Text);
		Assert.True(note.IsOnScreen(), "The note left the footer with the hint on");

		//The footer is where it was, down to the pixel, and the page's rows have
		//not moved with it.
		Rect noteOn = BoxIn(note, window);
		Assert.Equal(noteOff.Top, noteOn.Top, 0.5);
		Assert.Equal(pageOff.Height, BoxIn(page, window).Height, 0.5);
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
