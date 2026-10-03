using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0249 Decision 5: the render gate's helpers. The headless app renders with
//Skia (TestAppBuilder: UseHeadlessDrawing = false), so a window's frame is a
//real bitmap: Save writes it as a PNG for a person to compare with
//docs/media/gui-redesign/W-*.png, and Pixel reads it back for an assertion.
internal static class PlayerRender
{
	//The PNGs land in one deterministic folder per build output, or wherever
	//MESEN_PLAYER_RENDERS points (CI uploads that folder as an artifact).
	public static string OutputFolder {
		get {
			string? custom = Environment.GetEnvironmentVariable("MESEN_PLAYER_RENDERS");
			return string.IsNullOrEmpty(custom) ? Path.Combine(AppContext.BaseDirectory, "player-renders") : custom;
		}
	}

	public static Bitmap Capture(TopLevel top)
	{
		for(int i = 0; i < 4; i++) {
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(15);
		}
		Bitmap? frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(top);
		Assert.NotNull(frame);
		return frame!;
	}

	public static string Save(Bitmap frame, string name)
	{
		Directory.CreateDirectory(OutputFolder);
		string path = Path.Combine(OutputFolder, name + ".png");
		frame.Save(path);
		Xunit.TestContext.Current.TestOutputHelper?.WriteLine("render: " + path);
		Console.WriteLine("render: " + path);
		return path;
	}

	//One pixel of the frame, in device pixels (the headless scale is 1).
	public static Color Pixel(Bitmap frame, int x, int y)
	{
		byte[] raw = new byte[4];
		GCHandle pin = GCHandle.Alloc(raw, GCHandleType.Pinned);
		try {
			frame.CopyPixels(new PixelRect(x, y, 1, 1), pin.AddrOfPinnedObject(), 4, 4);
		} finally {
			pin.Free();
		}
		//Skia's native order differs per platform (RGBA on macOS arm64,
		//BGRA elsewhere); a grey pixel reads the same either way, a tint does not.
		return frame.Format == Avalonia.Platform.PixelFormat.Rgba8888
			? Color.FromArgb(raw[3], raw[0], raw[1], raw[2])
			: Color.FromArgb(raw[3], raw[2], raw[1], raw[0]);
	}

	public static void AssertPixel(Color expected, Bitmap frame, int x, int y, int tolerance = 3)
	{
		Color actual = Pixel(frame, x, y);
		bool close = Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance;
		Assert.True(close, $"pixel ({x},{y}) is {actual}, expected {expected}");
	}

	public static Color SolidColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

	//WCAG 2 contrast ratio between two opaque colours.
	public static double Contrast(Color a, Color b)
	{
		double la = Luminance(a), lb = Luminance(b);
		return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
	}

	private static double Luminance(Color c)
	{
		static double Channel(byte v)
		{
			double s = v / 255.0;
			return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
		}
		return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
	}
}
