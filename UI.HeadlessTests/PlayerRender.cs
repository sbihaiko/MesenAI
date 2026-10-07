using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Logic;
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
		//#951: a W-P render also gets its region report against the wireframe
		//(report only here; PlayerThemeRenderTests asserts its own screens).
		//A report failure is logged, never thrown: this test wanted the PNG.
		if(PlayerWireframe.IsPlayerRenderName(name)) {
			try {
				WriteWireframeReport(name, frame);
			} catch(Exception ex) {
				Log($"wireframe report for {name} failed: {ex.GetType().Name}: {ex.Message}");
			}
		}
		return path;
	}

	private static void Log(string line)
	{
		Xunit.TestContext.Current.TestOutputHelper?.WriteLine(line);
		Console.WriteLine(line);
	}

	//Writes <name>.wireframe.md next to the PNG: the region table against the
	//wireframe its name resolves to, or a "no wireframe" line.
	private static void WriteWireframeReport(string name, Bitmap frame)
	{
		string? wId = PlayerWireframe.ResolveWireframeId(name, id => File.Exists(WireframePath(id)));
		string report;
		if(wId == null) {
			report = PlayerWireframe.NoWireframeReport(name);
		} else {
			RgbFrame render = Rgb(frame);
			report = PlayerWireframe.Report(name, wId, PlayerWireframe.Compare(wId, render, RgbFrame.FromPng(WireframePath(wId))), render.Width, render.Height);
		}
		string path = Path.Combine(OutputFolder, name + ".wireframe.md");
		File.WriteAllText(path, report);
		Log("wireframe report: " + path);
	}

	public static string WireframePath(string wId) => Path.Combine(RepoFolder("docs", "media", "gui-redesign"), wId + ".png");

	//#974: the render UI.Tests gates on CI in place of a fresh one.
	public static string CommittedRenderPath(string wId) => Path.Combine(RepoFolder("UI.Tests", "Theme", "PlayerRenders"), wId + ".png");

	//#968: the committed render a fresh one drifts against on this host. CI
	//renders on Linux only and macOS renders locally (ADR-0191), and the two
	//lay the shell bar out differently (ShellTitleBar.ExtendsIntoTitleBar
	//insets it for the traffic lights on macOS only), so each host holds its own
	//baseline at full tolerance: macOS the UI.Tests one above, any other host
	//the copy under linux/ that the render-gate job's player-renders artifact
	//refreshes.
	public static string DriftBaselinePath(string wId) => OperatingSystem.IsMacOS()
		? CommittedRenderPath(wId)
		: Path.Combine(RepoFolder("UI.Tests", "Theme", "PlayerRenders", "linux"), wId + ".png");

	private static string RepoFolder(params string[] parts)
	{
		string relative = Path.Combine(parts);
		for(DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent) {
			string candidate = Path.Combine(dir.FullName, relative);
			if(Directory.Exists(candidate)) {
				return candidate;
			}
		}
		throw new DirectoryNotFoundException(relative + " not found above " + AppContext.BaseDirectory);
	}

	//The frame as the comparator's opaque RGB copy (same format rule as Pixel).
	public static RgbFrame Rgb(Bitmap bitmap)
	{
		int width = bitmap.PixelSize.Width, height = bitmap.PixelSize.Height;
		byte[] raw = new byte[width * height * 4];
		GCHandle pin = GCHandle.Alloc(raw, GCHandleType.Pinned);
		try {
			bitmap.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), raw.Length, width * 4);
		} finally {
			pin.Free();
		}
		bool rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
		byte[] rgb = new byte[width * height * 3];
		for(int p = 0; p < width * height; p++) {
			rgb[p * 3] = raw[p * 4 + (rgba ? 0 : 2)];
			rgb[p * 3 + 1] = raw[p * 4 + 1];
			rgb[p * 3 + 2] = raw[p * 4 + (rgba ? 2 : 0)];
		}
		return new RgbFrame(width, height, rgb);
	}

	//One pixel of the frame, in device pixels (the headless scale is 1).
	public static Color Pixel(Bitmap frame, int x, int y)
	{
		byte[] bgra = new byte[4];
		GCHandle pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
		try {
			frame.CopyPixels(new PixelRect(x, y, 1, 1), pin.AddrOfPinnedObject(), 4, 4);
		} finally {
			pin.Free();
		}
		//The headless Skia frame may be RGBA rather than BGRA (a saturated
		//orange read back as blue gave it away); honour the bitmap's format.
		if(frame.Format == Avalonia.Platform.PixelFormat.Rgba8888) {
			return Color.FromArgb(bgra[3], bgra[0], bgra[1], bgra[2]);
		}
		return Color.FromArgb(bgra[3], bgra[2], bgra[1], bgra[0]);
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
