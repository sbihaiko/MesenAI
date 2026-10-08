using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
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
	private static void AssertRing(Bitmap frame, Window window, Button button)
	{
		Point origin = button.TranslatePoint(new Point(0, 0), window)
			?? throw new InvalidOperationException("the press is not in the window");
		Rect bounds = new(origin, button.Bounds.Size);

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
