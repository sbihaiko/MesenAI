using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Controls;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;

namespace Mesen.HeadlessTests;

//#1089: the ring a pad navigates by, read off the frame rather than off the
//tree. PlayerLibraryFoldersListLayoutTests pins that the *Add a folder…* press
//takes PlayerFocusRing on its template layer; that case passes while the pixels
//on screen are a 2 px black frame with no blue and no glow at all, so what the
//property says and what the rasteriser draws are two different questions and
//this is the second one. The ring is the only thing telling a player where the
//pad is (ADR-0256, PlayFocusGlowTests), so a black frame is a dead end that
//looks like a border.
//
//Core-free: the sheet is shown in a plain window, no MainWindow, so this runs
//without the native MesenCore (PlayerLibraryFoldersListLayoutTests' pattern).
[NativeCoreFree("PlayerRomPickerViewModel's constructor only builds its AddKnownGameFolder delegate; no case here designates a games folder")]
public class PlayerFocusRingRenderTests
{
	//PlayerFocusRing's innermost layer, #7FB0FF, at full alpha.
	private static readonly Color RingBlue = Color.FromRgb(0x7F, 0xB0, 0xFF);

	//How far around a press's bounds the band is read: 3 px out, 3 px in. The
	//ring's crisp layer is a 2 px spread, so a ring drawn outward sits in the
	//part outside and one drawn inward sits in the part inside - the band takes
	//both rather than fixing which side the answer has to be on.
	private const int Band = 3;

	//The press #1079 and #1089 are about: the folders sheet's *Add a folder…*.
	//It is a .secondary press, so it carries BorderThickness 1 - the only thing
	//about it that a plain .primary press does not have.
	[AvaloniaFact]
	public void The_add_press_draws_the_themes_blue_ring_and_not_a_black_band()
	{
		using Harness harness = Harness.Open(1);
		Focus(harness.Add);
		using Bitmap frame = PlayerRender.Capture(harness.Window);
		PlayerRender.Save(frame, "focus-ring-add-press");

		AssertRing(frame, harness.Window, harness.Add);
	}

	//The ring is the theme's, not this press's: a .primary press in a plain
	//window - the shape PlayFocusGlowTests styles - has to draw the same blue.
	//A fix that only rescues the sheet's press fails here.
	[AvaloniaFact]
	public void A_primary_press_in_a_plain_window_draws_the_same_ring()
	{
		Button resume = new() { Content = "Resume", Classes = { "primary" } };
		Window window = new() {
			Width = 480,
			Height = 300,
			Content = new StackPanel { Classes = { "player", "play" }, Children = { resume } }
		};
		window.Show();
		Pump();

		Focus(resume);
		using Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "focus-ring-primary-press");

		AssertRing(frame, window, resume);
		window.Close();
	}

	//#1089: the Aspect Ratio field in Play's Display sheet is a
	//<c:EnumComboBox Classes="popup"> - a UserControl wrapping its own
	//ComboBox#Dropdown, both of them the same bounds, both of them clipping.
	//The `.player ComboBox.popup` half of the ring rule matches nothing inside
	//it (the class sits on the user control, not on the drop-down), so the
	//inner ComboBox keeps what the control gives it: an unset FocusAdorner,
	//which the framework answers with its own frame, and a clip that eats the
	//ring. Same press, same theme, one control further in. The frame needs a
	//real window to be seen at all - it does not rasterise in the headless
	//capture below - so what this case reads off the frame is the clip half.
	[AvaloniaFact]
	public void The_aspect_ratio_popup_draws_the_themes_ring_past_its_own_clip()
	{
		EnumComboBox aspectRatio = new() { Classes = { "popup" }, Width = 120, VerticalAlignment = VerticalAlignment.Center };
		Window window = PlayWindow(new StackPanel { Children = { aspectRatio } });
		try {
			ComboBox dropdown = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "Dropdown");
			dropdown.Focus(NavigationMethod.Tab);
			Pump();
			Assert.True(dropdown.IsFocused, "the Aspect Ratio popup did not take focus, so this case would prove nothing");
			Assert.Equal((BoxShadows)Application.Current!.FindResource("PlayerFocusRing")!,
				dropdown.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Background").BoxShadow);
			//The frame half, off the property rather than off the raster: an
			//empty template is "set", and that is what keeps the framework's
			//frame off the screen in a real window (PlayerLibraryFoldersListLayoutTests
			//reads the same pair off the folders press).
			Assert.NotNull(dropdown.FocusAdorner);
			Assert.Null(dropdown.FocusAdorner!.Build());

			using Bitmap frame = PlayerRender.Capture(window);
			PlayerRender.Save(frame, "focus-ring-aspect-ratio-popup");

			AssertRingOutside(frame, window, dropdown);
		} finally {
			window.Close();
		}
	}

	//#1089: a Player home / save-state tile (c:StateGridEntry.tiles) is the one
	//press whose own rule turns ClipToBounds back ON - the tile clips its
	//picture to the 10 px radius. The clip is the control's, so it eats the
	//PlayerFocusRing its PART_Background carries as well, and a tile that
	//answers the pad draws nothing where the ring should be.
	[AvaloniaFact]
	public void The_focused_save_state_tile_draws_the_themes_ring()
	{
		//Enabled: a StateGridEntry whose entry never loaded refuses focus, and a
		//tile that cannot be focused cannot show this case at all.
		StateGridEntry tile = new() { Classes = { "tiles" }, Title = "Save 1", Enabled = true };
		Window window = PlayWindow(new StackPanel { Children = { tile } });
		try {
			Button button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TileButton");
			Assert.Equal(176, button.Bounds.Width, 0.5);
			Assert.Equal(132, button.Bounds.Height, 0.5);
			Focus(button);

			using Bitmap frame = PlayerRender.Capture(window);
			PlayerRender.Save(frame, "focus-ring-save-state-tile");

			//Outside only: the tile's own background is Black by rule, so the
			//band AssertRing reads would call the tile's own face "the
			//framework's black frame".
			AssertRingOutside(frame, window, button);
		} finally {
			window.Close();
		}
	}

	//#1089: the raster cases above ask what is drawn; this one asks what the
	//ring *is*, on a host with no display at all. The review's third point was
	//that only the headless raster is covered and #1089 asks for a check on a
	//real macOS window - this is not that check and nothing here claims to be
	//one. What it does pin, host-free, is that the press a pad lands on is
	//given the theme's ring and not something that merely looks like it: the
	//brush is the PlayerFocusRingColor token and the crisp layer is the 2 px
	//spread PlayerTheme documents, so a fix that hard-codes a similar blue, or
	//drops the thickness, fails here.
	[AvaloniaFact]
	public void The_play_press_ring_is_the_themes_brush_and_thickness()
	{
		Button resume = new() { Content = "Resume", Classes = { "primary" } };
		Window window = PlayWindow(new StackPanel { Children = { resume } });
		try {
			Focus(resume);

			BoxShadows ring = RingOf(resume);
			Assert.Equal((BoxShadows)Application.Current!.FindResource("PlayerFocusRing")!, ring);

			Color token = (Color)Application.Current!.FindResource("PlayerFocusRingColor")!;
			Assert.Equal(token, ring[0].Color);
			Assert.Equal(0d, ring[0].Blur);
			Assert.Equal(2d, ring[0].Spread);
			//Every layer is a ring (a spread), not a plain drop shadow.
			for(int i = 0; i < ring.Count; i++) {
				Assert.True(ring[i].Spread > 0, $"layer {i} has no spread, so it is a shadow and not a ring");
			}
		} finally {
			window.Close();
		}
	}

	//A plain window carrying Play's classes: the theme's rules are class-scoped,
	//so `.player` has to be an ancestor of the control under test.
	private static Window PlayWindow(Control content)
	{
		Window window = new() { Width = 480, Height = 300, Content = content };
		window.Classes.Add("player");
		window.Classes.Add("play");
		window.Show();
		Pump();
		return window;
	}

	private static BoxShadows RingOf(Control control)
		=> control.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Background").BoxShadow;

	private static void Focus(Button button)
	{
		//NavigationMethod.Tab is what makes this :focus-visible rather than plain
		//:focus, which is the pseudo-class the ring is set on.
		button.Focus(NavigationMethod.Tab);
		Pump();
		Assert.True(button.IsFocused, "the press did not take focus, so this case would prove nothing");
		Assert.Equal((BoxShadows)Application.Current!.FindResource("PlayerFocusRing")!,
			button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Background").BoxShadow);
	}

	//The two rules, read as pixels: the theme's blue is somewhere in the band and
	//nothing in the band is black. Either alone passes vacuously - a press that
	//drew nothing has no black frame, and one that drew the old flat black ring
	//has no blue - so both are asserted.
	private static void AssertRing(Bitmap frame, Window window, Control control)
	{
		Point origin = control.TranslatePoint(new Point(0, 0), window)
			?? throw new InvalidOperationException("the press is not in the window");
		Rect bounds = new(origin, control.Bounds.Size);

		List<Color> band = new();
		for(int y = (int)bounds.Top - Band; y < (int)bounds.Bottom + Band; y++) {
			for(int x = (int)bounds.Left - Band; x < (int)bounds.Right + Band; x++) {
				bool inside = x >= bounds.Left + Band && x < bounds.Right - Band
					&& y >= bounds.Top + Band && y < bounds.Bottom - Band;
				//A press against the window's edge has no frame outside it to read.
				bool onFrame = x >= 0 && y >= 0 && x < frame.PixelSize.Width && y < frame.PixelSize.Height;
				if(!inside && onFrame) {
					band.Add(PlayerRender.Pixel(frame, x, y));
				}
			}
		}

		string scan = Describe(bounds, band);
		Assert.True(band.Any(c => Near(c, RingBlue)),
			$"no #7FB0FF pixel in the {Band} px band around {bounds} - the ring is not drawn.{scan}");
		Assert.False(band.Any(c => c.R < 24 && c.G < 24 && c.B < 24),
			$"the band around {bounds} is black, not the theme's ring.{scan}");
	}

	//The clip's own claim, read where only the ring can answer: strictly
	//outside the press's bounds. AssertRing's band reaches 3 px *in* as well,
	//and a themed control has blue of its own inside that reach - the popup
	//draws its stepper in Play's blue - so the band alone cannot tell a ring
	//that is drawn from one that was cut off at the edge. Out here the theme
	//paints nothing but the ring.
	private static void AssertRingOutside(Bitmap frame, Window window, Control control)
	{
		Point origin = control.TranslatePoint(new Point(0, 0), window)
			?? throw new InvalidOperationException("the press is not in the window");
		Rect bounds = new(origin, control.Bounds.Size);

		List<Color> outside = new();
		for(int y = (int)bounds.Top - Band; y < (int)bounds.Bottom + Band; y++) {
			for(int x = (int)bounds.Left - Band; x < (int)bounds.Right + Band; x++) {
				bool inBounds = x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
				//A press against the window's edge has no frame outside it to read.
				bool onFrame = x >= 0 && y >= 0 && x < frame.PixelSize.Width && y < frame.PixelSize.Height;
				if(!inBounds && onFrame) {
					outside.Add(PlayerRender.Pixel(frame, x, y));
				}
			}
		}

		Assert.True(outside.Any(c => Near(c, RingBlue)),
			$"no #7FB0FF pixel outside {bounds}: the ring is cut off at the press's edge.{Describe(bounds, outside)}");
	}

	private static bool Near(Color a, Color b)
		=> Math.Abs(a.R - b.R) <= 40 && Math.Abs(a.G - b.G) <= 40 && Math.Abs(a.B - b.B) <= 40;

	private static string Describe(Rect bounds, List<Color> band)
	{
		Dictionary<Color, int> counts = new();
		foreach(Color c in band) {
			counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
		}
		string top = string.Join(", ", counts.OrderByDescending(p => p.Value).Take(6).Select(p => $"{p.Key} x{p.Value}"));
		return $"\nbounds {bounds}; band colours (most common first): {top}";
	}

	private static void Pump()
	{
		for(int i = 0; i < 6; i++) {
			Dispatcher.UIThread.RunJobs();
			System.Threading.Thread.Sleep(15);
		}
	}

	//The folders sheet in a window, the way the Play surface has it
	//(PlayerLibraryFoldersListLayoutTests' Harness, cut down to the press).
	private sealed class Harness : IDisposable
	{
		private readonly string _root;
		private readonly List<string>? _folders;

		public Window Window { get; }
		public Button Add { get; }

		private Harness(string root, List<string>? folders, Window window, Button add)
		{
			_root = root;
			_folders = folders;
			Window = window;
			Add = add;
		}

		public static Harness Open(int folders)
		{
			string root = Path.Combine(Path.GetTempPath(), "mesen-1089-" + Guid.NewGuid().ToString("N"));
			List<string>? saved = ConfigManager.Config.Preferences.LibraryFolders;
			Window? window = null;
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
				window = new Window() { Width = 1100, Height = 740, Content = view };
				window.Classes.Add("player");
				window.Classes.Add("play");
				window.Show();
				Pump();

				Button add = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "RomPickerAddFolder");
				return new Harness(root, saved, window, add);
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

		public void Dispose()
		{
			Window.Close();
			Pump();
			ConfigManager.Config.Preferences.LibraryFolders = _folders;
			try {
				Directory.Delete(_root, true);
			} catch {
				//Nothing to remove when setup failed before the tree existed.
			}
		}
	}
}
