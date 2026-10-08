using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
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

//#1078: the library sheet used to be a fixed 1000 px wide, so a non-maximized
//window narrower than that drew a sheet wider than the window - centred, so
//BOTH edges fell outside it and the window clipped them: the header lost its
//start ("ry · 40 games…"), the console filter lost All and NES, the first grid
//column was cut at the left and *Browse a file…* at the right.
//
//The width is W-P19's own 1100 px (scripts/render_gui_wireframes.py:
//c.sheet(1100, 620)), capped at the window itself. There is no horizontal
//gutter to subtract: the sheet's XAML Margin is "0 24", vertical only, and the
//backdrop around it carries no horizontal padding. The rule is
//LibrarySheetFit's (host-free, UI.Tests); this is the crossing - that the view
//applies it, so every header control and the first grid column sit inside the
//window at a width W-P19's own 1100 px sheet does not fit in.
//
//The narrow case is 900 px, not 1000: a fixed 1000 px sheet fits a 1000 px
//window exactly, so a 1000 px case passed on the old code and never reproduced
//#1078 at all.
//
//The wide case is here too, as the no-regression half: with room, the sheet is
//W-P19's own 1100 px.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryNarrowWindowTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;
	private readonly List<string>? _libraryFolders = ConfigManager.Config.Preferences.LibraryFolders;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1078-" + Guid.NewGuid().ToString("N"));

	public PlayerLibraryNarrowWindowTests()
	{
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
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

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	//The library sheet, up and settled, in a window of the given size: one
	//library folder holding one game, the scan inline so the grid is complete
	//before the case looks at it.
	private (MainWindow Window, MainWindowViewModel Model) OpenLibrary(double width)
	{
		string nes = Path.Combine(_folder, "games", "NES");
		Directory.CreateDirectory(nes);
		File.WriteAllBytes(Path.Combine(nes, "Metroid (USA).nes"), SyntheticNrom.Build());
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.GameFolder = Path.Combine(_folder, "games");
		prefs.OverrideGameFolder = true;
		prefs.LibraryFolders = null;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;

		MainWindow window = new() { Width = width, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		model.RomPicker.RunLibraryScanInline = true;
		model.RomPicker.Open();
		Pump();
		Assert.Equal(RomPickerMode.Library, model.RomPicker.Mode);
		WaitFor(() => window.FindNamed<ItemsControl>("RomPickerGrid").ContainerFromIndex(0) is Control,
			"the grid never realized its first tile");
		return (window, model);
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Pump();
			System.Threading.Thread.Sleep(20);
		}
		Pump();
	}

	private static Control Named(MainWindow window, string name) => window.FindNamed<Control>(name);

	//Visible is not reachable: IsEffectivelyVisible stays true for a control the
	//layout pushed past the window's edge. The control's box, in window
	//coordinates, has to sit inside the window's client area - the same probe
	//PlayerLibraryShortWindowTests uses for the vertical axis.
	private static void AssertInsideHorizontally(MainWindow window, string name)
	{
		Control control = Named(window, name);
		Assert.True(control.IsEffectivelyVisible, name + " is not on the sheet");
		Point? origin = control.TranslatePoint(new Point(0, 0), window);
		Assert.NotNull(origin);
		Assert.True(origin!.Value.X >= -0.5, $"{name} starts at {origin.Value.X}, left of the window");
		double right = origin.Value.X + control.Bounds.Width;
		Assert.True(right <= window.ClientSize.Width + 0.5,
			$"{name} ends at {right}, past the {window.ClientSize.Width} px window");
	}

	[AvaloniaFact]
	public void A_window_narrower_than_the_wireframes_sheet_keeps_the_sheet_and_its_contents_inside_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel _) = OpenLibrary(900);

		//The sheet itself first: everything below is inside it, so this is the
		//assertion that fails while the width is the fixed 1000 (#1078).
		AssertInsideHorizontally(window, "PlayerRomPickerSheet");
		AssertInsideHorizontally(window, "RomPickerTitle");
		AssertInsideHorizontally(window, "RomPickerSearch");
		AssertInsideHorizontally(window, "RomPickerBrowseFile");
		AssertInsideHorizontally(window, "RomPickerLibraryFolders");

		//The first grid column: the focus ring the pad lands on is drawn around
		//this tile, so a tile hanging off the left edge is the ring half outside
		//the window.
		ItemsControl grid = window.FindNamed<ItemsControl>("RomPickerGrid");
		Control first = Assert.IsAssignableFrom<Control>(grid.ContainerFromIndex(0));
		Point? origin = first.TranslatePoint(new Point(0, 0), window);
		Assert.NotNull(origin);
		Assert.True(origin!.Value.X >= -0.5,
			$"the first grid column starts at {origin.Value.X}, left of the window");
		Assert.True(origin.Value.X + first.Bounds.Width <= window.ClientSize.Width + 0.5,
			"the first grid column is cut at the right edge");
	}

	[AvaloniaFact]
	public void A_narrower_window_still_fits_every_header_control_on_screen()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = OpenLibrary(760);

		//Clear only shows while a query is on, so the query is what puts the
		//last header control on the sheet.
		model.RomPicker.SearchQuery = "met";
		Pump();

		AssertInsideHorizontally(window, "PlayerRomPickerSheet");
		AssertInsideHorizontally(window, "RomPickerTitle");
		AssertInsideHorizontally(window, "RomPickerSearch");
		AssertInsideHorizontally(window, "RomPickerSearchClear");
		AssertInsideHorizontally(window, "RomPickerBrowseFile");
		AssertInsideHorizontally(window, "RomPickerLibraryFolders");

		//Inside the window is not the same as on screen: a row of fixed-width
		//controls and a heading competing for one line leaves the heading with
		//less room than its own text, and a control that overflows its box is
		//still "inside the window" and still effectively visible - it is just
		//drawn under its neighbour. So no two header controls may overlap, and
		//the heading has to keep a heading's width.
		Assert.True(Named(window, "RomPickerTitle").Bounds.Width >= 200,
			$"the heading was squeezed to {Named(window, "RomPickerTitle").Bounds.Width} px");

		foreach(string other in new[] { "RomPickerSearch", "RomPickerSearchClear", "RomPickerBrowseFile", "RomPickerLibraryFolders" }) {
			AssertClearOf(window, "RomPickerTitle", other);
		}
	}

	//The control's box, in window coordinates.
	private static Rect BoxIn(MainWindow window, string name)
	{
		Control control = Named(window, name);
		Point origin = control.TranslatePoint(new Point(0, 0), window) ?? new Point(double.NaN, double.NaN);
		return new Rect(origin, control.Bounds.Size);
	}

	private static void AssertClearOf(MainWindow window, string name, string other)
	{
		Rect a = BoxIn(window, name);
		Rect b = BoxIn(window, other);
		double overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
		double overlapY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
		Assert.True(overlapX <= 0.5 || overlapY <= 0.5,
			$"{name} {a} is drawn under {other} {b}");
	}

	[AvaloniaFact]
	public void A_window_with_room_still_gets_the_wireframes_own_sheet_width()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel _) = OpenLibrary(1400);

		Control sheet = Named(window, "PlayerRomPickerSheet");
		Assert.Equal(1100, sheet.Bounds.Width, 0.5);
		AssertInsideHorizontally(window, "PlayerRomPickerSheet");
	}

	//W-P19b puts Clear at the search field's own right end: the x belongs to the
	//box, not to the sheet. The row spans the sheet, so a Clear docked to the
	//right end of it lands at the sheet's edge - 834 px from the box on the
	//wireframes' own 1100 px sheet, measured - and reads as a sheet-wide control
	//rather than this field's way out.
	[AvaloniaFact]
	public void The_search_clears_x_sits_at_the_fields_right_edge()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = OpenLibrary(1400);

		//Clear only shows while a query is on.
		model.RomPicker.SearchQuery = "met";
		Pump();

		Rect box = BoxIn(window, "RomPickerSearch");
		Rect clear = BoxIn(window, "RomPickerSearchClear");
		double gap = clear.Left - box.Right;
		Assert.True(gap >= -0.5 && gap <= 8,
			$"Clear {clear} sits {gap} px from the search box's right edge {box.Right}");
	}
}
