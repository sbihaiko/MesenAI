using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;

namespace Mesen.HeadlessTests;

//#1079: the *Library folders…* list drawn, not described. The rules it has to
//keep are host-free in UI.Tests/Play/LibraryFoldersRowLayoutTests; what is
//measured here is the outcome a player sees - the box is as tall as its rows, so
//no gap opens above the first one, each row is inset from the box on both sides,
//and the *Add a folder…* press carries the app's own focus ring.
//
//Core-free: the sheet is shown in a plain window, no MainWindow, so this runs
//without the native MesenCore (PlayFocusGlowTests' pattern). The list is filled
//from the real Preferences list, which is what the sheet binds to. That is also
//why it is marked [NativeCoreFree] rather than joining the serial collection:
//the one core path the walk finds is the view-model's own constructor building
//its AddKnownGameFolder delegate, and no case here ever designates a games
//folder, so the call is never made.
[NativeCoreFree("PlayerRomPickerViewModel's constructor only builds its AddKnownGameFolder delegate; no case here designates a games folder")]
public class PlayerLibraryFoldersListLayoutTests
{
	//The theme's row (PlayerTheme.axaml, Border.switch-row).
	private const double RowHeight = 46;

	//A row's own inset inside the box: the theme's 16 on the left, the row's
	//DockPanel 16 on the right.
	private const double MinInset = 12;

	[AvaloniaFact]
	public void The_box_is_as_tall_as_its_rows_and_nothing_opens_above_the_first()
	{
		using Harness harness = Harness.Open(3);

		double box = harness.Box.Bounds.Height;
		double expected = 3 * RowHeight;
		Assert.True(Math.Abs(box - expected) <= 2, $"the box is not its rows' height: box={box}, rows={expected}");

		//The gap the issue saw: the list floated in the middle of a box that took
		//the whole sheet. Measured against the box, not against the scroller: the
		//scroller scrolls to its own content, so the first row sits at its top by
		//construction and the offset there is zero whatever the box does. Against
		//the box, the measurement moves when the box grows around the rows.
		double top = harness.OffsetIn(harness.Rows[0], harness.Box).Y;
		Assert.True(top <= 2, $"there is a gap above the first row: {top} px");
	}

	[AvaloniaFact]
	public void Every_row_is_inset_from_both_edges_of_the_box()
	{
		using Harness harness = Harness.Open(3);

		foreach(Visual row in harness.Rows) {
			double left = harness.OffsetIn(row, harness.Box).X;
			Assert.True(left >= MinInset, $"a row is flush against the box's left edge: {left} px");

			Button remove = row.GetVisualDescendants().OfType<Button>().First(b => b.Name == "RomPickerFolderRemove");
			double right = harness.OffsetIn(remove, harness.Box).X + remove.Bounds.Width;
			double gap = harness.Box.Bounds.Width - right;
			Assert.True(gap >= MinInset, $"a row's *Remove* press is flush against the box's right edge: {gap} px");
		}
	}

	//The clipping the issue is about: in a window shorter than the list wants, the
	//sheet's own edge cuts the box and the *Add a folder…* press is pushed out of the
	//sheet. A press the player cannot see is a press the player cannot reach, so the
	//press has to stay inside the sheet at any window height - the box gives way, not
	//the press. 8 folders is more than the box can show at this height on any build.
	[AvaloniaFact]
	public void The_add_press_stays_inside_the_sheet_when_the_window_is_short()
	{
		using Harness harness = Harness.Open(8, height: 400);

		double press = harness.OffsetIn(harness.Add, harness.Sheet).Y + harness.Add.Bounds.Height;
		double sheet = harness.Sheet.Bounds.Height;
		Assert.True(press <= sheet + 1, $"the *Add a folder…* press leaves the sheet: press bottom={press}, sheet height={sheet}");
	}

	//The press that adds a folder is a Play button like any other: the pad lands
	//on it first, so its ring is the only thing saying where the player is. It
	//draws the theme's ring and nothing of its own.
	[AvaloniaFact]
	public void The_add_press_shows_the_apps_focus_ring()
	{
		using Harness harness = Harness.Open(1);

		BoxShadows ring = (BoxShadows)Application.Current!.FindResource("PlayerFocusRing")!;
		Border background = harness.Add.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Background");
		Assert.NotEqual(ring, background.BoxShadow);

		harness.Add.Focus(NavigationMethod.Tab);
		Dispatcher.UIThread.RunJobs();

		Assert.True(harness.Add.IsFocused);
		Assert.Null(harness.Add.FocusAdorner);
		Assert.Equal(ring, background.BoxShadow);
	}

	//The sheet in a window, the way the Play surface has it, with the folders
	//sheet up over the library.
	private sealed class Harness : IDisposable
	{
		private readonly string _root;
		private readonly List<string>? _folders;
		private readonly Window _window;

		public Control Sheet { get; }
		public Control Box { get; }
		public ScrollViewer Scroller { get; }
		public IReadOnlyList<Visual> Rows { get; }
		public Button Add { get; }

		private Harness(string root, List<string>? folders, Window window, Control sheet, Control box, ScrollViewer scroller, IReadOnlyList<Visual> rows, Button add)
		{
			_root = root;
			_folders = folders;
			_window = window;
			Sheet = sheet;
			Box = box;
			Scroller = scroller;
			Rows = rows;
			Add = add;
		}

		public static Harness Open(int folders, double height = 740)
		{
			string root = Path.Combine(Path.GetTempPath(), "mesen-1079-" + Guid.NewGuid().ToString("N"));
			List<string>? saved = ConfigManager.Config.Preferences.LibraryFolders;
			Window? window = null;
			//Open mutates a global (the Preferences list) and a temp root, and it
			//can throw before the Harness exists - a case that fails in setup has
			//no Dispose to undo either, so the undo lives here too.
			try {
				List<string> paths = new();
				for(int i = 0; i < folders; i++) {
					string path = Path.Combine(root, "folder-" + i);
					Directory.CreateDirectory(path);
					paths.Add(path);
				}

				ConfigManager.Config.Preferences.LibraryFolders = paths;

				PlayerRomPickerViewModel model = new();
				model.Open();
				model.OpenFoldersSheet();
				if(!model.IsFoldersSheetVisible) {
					throw new InvalidOperationException("the sheet did not open, so this case would prove nothing");
				}

				PlayerRomPickerView view = new() { DataContext = model };
				window = new Window() { Width = 1100, Height = height, Content = view };
				window.Classes.Add("player");
				window.Classes.Add("play");
				window.Show();
				Pump();

				Control sheet = window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "RomPickerFoldersSheet");
				ScrollViewer scroller = sheet.GetVisualDescendants().OfType<ScrollViewer>().First();
				//The box the list is drawn in: the inset is behind the list, so this is
				//the layer that carries the box's own height.
				Control box = (Control)scroller.GetVisualParent()!;
				//The rows, whatever they are made of: one per item container, which is
				//the DataTemplate's own root.
				IReadOnlyList<Visual> rows = scroller.GetVisualDescendants()
					.OfType<Control>()
					.Where(c => c.DataContext is PlayerLibraryFolderRow && c.GetVisualParent() is ContentPresenter)
					.Cast<Visual>().ToList();
				Button add = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "RomPickerAddFolder");

				Assert.True(rows.Count == folders, $"the list drew {rows.Count} rows for {folders} folders");
				return new Harness(root, saved, window, sheet, box, scroller, rows, add);
			} catch {
				window?.Close();
				Pump();
				ConfigManager.Config.Preferences.LibraryFolders = saved;
				try {
					Directory.Delete(root, true);
				} catch {
					//A case that failed before it built its tree leaves nothing to remove.
				}
				throw;
			}
		}

		//Offsets by walking the visual tree: Bounds are parent-relative, and these
		//questions are all "where is this inside that".
		public Point OffsetIn(Visual child, Visual ancestor)
		{
			Point offset = default;
			Visual? current = child;
			while(current != null && current != ancestor) {
				offset += current.Bounds.Position;
				current = current.GetVisualParent();
			}
			Assert.True(current == ancestor, $"{child.GetType().Name} is not inside {ancestor.GetType().Name}");
			return offset;
		}

		private static void Pump()
		{
			for(int i = 0; i < 6; i++) {
				Dispatcher.UIThread.RunJobs();
				System.Threading.Thread.Sleep(15);
			}
		}

		public void Dispose()
		{
			_window.Close();
			Pump();
			ConfigManager.Config.Preferences.LibraryFolders = _folders;
			try {
				Directory.Delete(_root, true);
			} catch {
				//A case that failed before it built its tree leaves nothing to remove.
			}
		}
	}
}
