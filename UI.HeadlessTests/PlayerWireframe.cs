using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Mesen.HeadlessTests;

//#951: pairs a Player render with its §13.5 wireframe (docs/media/gui-redesign/W-*.png)
//and compares regions, never pixels (ADR-0249 rules a pixel diff out). The
//wireframe's window box is cropped and scaled to the render's size; then, per
//region, the dominant colours are compared by CIE76 delta E, the ink boxes
//(pixels far from the dominant colour) by their largest edge offset, and the
//text lines (horizontal ink bands) by count and centre offset, top to bottom.
internal static class PlayerWireframe
{
	//The written tolerances (#951).
	public const double MaxDeltaE = 10;        //dominant colour, CIE76
	public const double MaxBoxOffset = 8;      //largest ink-box edge offset, logical px
	public const double MaxLineOffset = 8;     //largest text-line centre offset, logical px
	public const double InkDeltaE = 25;        //a pixel this far from the dominant colour is ink
	public const int MinLineHeight = 3;        //an ink band thinner than this is a rule, not a line

	//scripts/render_gui_wireframes.py draws at S = 2 with the window at
	//WIN = (50, 70, 1150, 810) logical px: a 1100 x 740 window, the renders' size.
	public const int WindowWidth = 1100;
	public const int WindowHeight = 740;
	private static readonly PixelRect WireframeWindow = new(100, 140, 2200, 1480);

	//Regions in window logical px. Every W-P render gets the shared chrome
	//regions; the title bar starts after the wireframe's traffic lights, which
	//a headless window does not draw.
	private static readonly WireframeRegion[] Chrome = {
		new("title bar", new Rect(80, 0, 1020, 52)),
		new("content", new Rect(0, 53, 1100, 660)),
		new("status line", new Rect(0, 714, 1100, 26)),
	};

	//Each box is placed on the wireframe's element (render_gui_wireframes.py
	//coordinates minus WIN's origin), so a render that moved shows as an offset.
	private static readonly Dictionary<string, WireframeRegion[]> Specific = new() {
		["W-P1"] = new WireframeRegion[] {
			new("drop block", new Rect(300, 150, 500, 290)),
			new("primary button", new Rect(450, 380, 200, 30)),
		},
		["W-P2"] = new WireframeRegion[] {
			new("continue card", new Rect(40, 123, 1020, 160)),
			new("continue button", new Rect(330, 222, 100, 30)),
			new("recent tiles", new Rect(40, 343, 1020, 160)),
		},
		["W-P4"] = new WireframeRegion[] {
			new("overlay card", new Rect(360, 80, 380, 520)),
			new("resume button", new Rect(390, 168, 320, 32)),
			new("grouped rows", new Rect(376, 230, 348, 250)),
		},
		["W-P15"] = new WireframeRegion[] { new("setup sheet", new Rect(320, 120, 460, 440)) },
	};

	public static bool IsPlayerWireframe(string name) => name.StartsWith("W-P", StringComparison.Ordinal) && File.Exists(PathOf(name));

	public static IReadOnlyList<WireframeRegion> RegionsOf(string wId) => Chrome.Concat(Specific.TryGetValue(wId, out WireframeRegion[]? own) ? own : Array.Empty<WireframeRegion>()).ToArray();

	public static string PathOf(string wId) => Path.Combine(GuiRedesignFolder, wId + ".png");

	private static string GuiRedesignFolder {
		get {
			for(DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent) {
				string candidate = Path.Combine(dir.FullName, "docs", "media", "gui-redesign");
				if(Directory.Exists(candidate)) {
					return candidate;
				}
			}
			throw new DirectoryNotFoundException("docs/media/gui-redesign not found above " + AppContext.BaseDirectory);
		}
	}

	//The wireframe's window box, area-averaged down to width x height.
	public static RgbFrame LoadScaled(string wId, int width, int height)
	{
		using Bitmap source = new(PathOf(wId));
		RgbFrame full = RgbFrame.From(source);
		PixelRect box = WireframeWindow;
		byte[] rgb = new byte[width * height * 3];
		for(int y = 0; y < height; y++) {
			int sy0 = box.Y + y * box.Height / height, sy1 = Math.Max(sy0 + 1, box.Y + (y + 1) * box.Height / height);
			for(int x = 0; x < width; x++) {
				int sx0 = box.X + x * box.Width / width, sx1 = Math.Max(sx0 + 1, box.X + (x + 1) * box.Width / width);
				int r = 0, g = 0, b = 0, n = 0;
				for(int sy = sy0; sy < sy1; sy++) {
					for(int sx = sx0; sx < sx1; sx++) {
						Color c = full.At(sx, sy);
						r += c.R; g += c.G; b += c.B; n++;
					}
				}
				int i = (y * width + x) * 3;
				rgb[i] = (byte)(r / n); rgb[i + 1] = (byte)(g / n); rgb[i + 2] = (byte)(b / n);
			}
		}
		return new RgbFrame(width, height, rgb);
	}

	public static IReadOnlyList<RegionResult> Compare(string wId, RgbFrame render)
	{
		RgbFrame wireframe = LoadScaled(wId, render.Width, render.Height);
		double scale = render.Width / (double)WindowWidth;
		return RegionsOf(wId).Select(region => Compare(region, render, wireframe, scale)).ToArray();
	}

	public static RegionResult Compare(WireframeRegion region, RgbFrame render, RgbFrame wireframe, double scale)
	{
		PixelRect box = new((int)(region.Box.X * scale), (int)(region.Box.Y * scale), (int)(region.Box.Width * scale), (int)(region.Box.Height * scale));
		RegionMeasure r = Measure(render, box, scale), w = Measure(wireframe, box, scale);
		double deltaE = DeltaE(r.Dominant, w.Dominant);
		double boxOffset = r.Ink is Rect ri && w.Ink is Rect wi
			? new[] { ri.Left - wi.Left, ri.Top - wi.Top, ri.Right - wi.Right, ri.Bottom - wi.Bottom }.Max(Math.Abs)
			: (r.Ink == null && w.Ink == null ? 0 : double.PositiveInfinity);
		double lineOffset = r.Lines.Count == w.Lines.Count
			? r.Lines.Zip(w.Lines, (a, b) => Math.Abs(a - b)).DefaultIfEmpty(0).Max()
			: double.PositiveInfinity;
		return new RegionResult(region.Name, r.Dominant, w.Dominant, deltaE, boxOffset, r.Lines.Count, w.Lines.Count, lineOffset);
	}

	private sealed record RegionMeasure(Color Dominant, Rect? Ink, IReadOnlyList<double> Lines);

	//Logical-px geometry inside a device-px box.
	private static RegionMeasure Measure(RgbFrame frame, PixelRect box, double scale)
	{
		Color dominant = Dominant(frame, box);
		int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
		bool[] inkRows = new bool[box.Height];
		for(int y = 0; y < box.Height; y++) {
			for(int x = 0; x < box.Width; x++) {
				if(DeltaE(frame.At(box.X + x, box.Y + y), dominant) > InkDeltaE) {
					inkRows[y] = true;
					minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
					minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
				}
			}
		}
		Rect? ink = maxX < 0 ? null : new Rect(minX / scale, minY / scale, (maxX - minX + 1) / scale, (maxY - minY + 1) / scale);
		List<double> lines = new();
		for(int y = 0; y < box.Height;) {
			if(!inkRows[y]) { y++; continue; }
			int start = y;
			while(y < box.Height && inkRows[y]) { y++; }
			if((y - start) / scale >= MinLineHeight) {
				lines.Add((start + y) / 2.0 / scale);
			}
		}
		return new RegionMeasure(dominant, ink, lines);
	}

	//The most frequent colour, binned at 4 bits per channel, as its bin's mean.
	private static Color Dominant(RgbFrame frame, PixelRect box)
	{
		Dictionary<int, (int Count, long R, long G, long B)> bins = new();
		for(int y = box.Y; y < box.Bottom; y++) {
			for(int x = box.X; x < box.Right; x++) {
				Color c = frame.At(x, y);
				int key = (c.R >> 4) << 8 | (c.G >> 4) << 4 | c.B >> 4;
				bins.TryGetValue(key, out var bin);
				bins[key] = (bin.Count + 1, bin.R + c.R, bin.G + c.G, bin.B + c.B);
			}
		}
		var top = bins.Values.MaxBy(b => b.Count);
		return Color.FromRgb((byte)(top.R / top.Count), (byte)(top.G / top.Count), (byte)(top.B / top.Count));
	}

	//CIE76 delta E between two sRGB colours (D65).
	public static double DeltaE(Color a, Color b)
	{
		(double l1, double a1, double b1) = Lab(a);
		(double l2, double a2, double b2) = Lab(b);
		return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
	}

	private static (double L, double A, double B) Lab(Color c)
	{
		static double Linear(byte v)
		{
			double s = v / 255.0;
			return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
		}
		static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
		double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
		double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
		double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
		double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;
		double fx = F(x), fy = F(y), fz = F(z);
		return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
	}

	//The per-region pass/fail with the measured deltas, as a Markdown table.
	public static string Report(string wId, IReadOnlyList<RegionResult> results)
	{
		StringBuilder sb = new();
		sb.AppendLine($"# {wId} vs docs/media/gui-redesign/{wId}.png");
		sb.AppendLine();
		sb.AppendLine(FormattableString.Invariant($"Tolerances: colour ΔE ≤ {MaxDeltaE}, ink-box edge offset ≤ {MaxBoxOffset} px, text-line centre offset ≤ {MaxLineOffset} px with the same line count."));
		sb.AppendLine();
		sb.AppendLine("| region | render colour | wireframe colour | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |");
		sb.AppendLine("|---|---|---|---|---|---|---|---|");
		foreach(RegionResult r in results) {
			sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
				$"| {r.Region} | {Hex(r.RenderColor)} | {Hex(r.WireframeColor)} | {r.DeltaE:0.0} | {r.BoxOffset:0.0} | {r.RenderLines}/{r.WireframeLines} | {r.LineOffset:0.0} | {(r.Pass ? "pass" : "FAIL: " + string.Join(", ", r.Failures))} |"));
		}
		return sb.ToString();
	}

	private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

	//Writes <wId>.wireframe.md next to the render's PNG and returns the results.
	public static IReadOnlyList<RegionResult> CompareAndReport(string wId, Bitmap frame)
	{
		IReadOnlyList<RegionResult> results = Compare(wId, RgbFrame.From(frame));
		Directory.CreateDirectory(PlayerRender.OutputFolder);
		string path = Path.Combine(PlayerRender.OutputFolder, wId + ".wireframe.md");
		File.WriteAllText(path, Report(wId, results));
		Xunit.TestContext.Current.TestOutputHelper?.WriteLine("wireframe report: " + path);
		return results;
	}
}

internal sealed record WireframeRegion(string Name, Rect Box);

internal sealed record RegionResult(string Region, Color RenderColor, Color WireframeColor, double DeltaE, double BoxOffset, int RenderLines, int WireframeLines, double LineOffset)
{
	public IReadOnlyList<string> Failures {
		get {
			List<string> failures = new();
			if(DeltaE > PlayerWireframe.MaxDeltaE) { failures.Add("colour"); }
			if(BoxOffset > PlayerWireframe.MaxBoxOffset) { failures.Add("ink box"); }
			if(LineOffset > PlayerWireframe.MaxLineOffset) { failures.Add("text lines"); }
			return failures;
		}
	}

	public bool Pass => Failures.Count == 0;
}

//An opaque RGB copy of a frame, so the comparison never depends on the bitmap's pixel format.
internal sealed class RgbFrame
{
	public int Width { get; }
	public int Height { get; }
	private readonly byte[] _rgb;

	public RgbFrame(int width, int height, byte[] rgb)
	{
		Width = width;
		Height = height;
		_rgb = rgb;
	}

	public Color At(int x, int y)
	{
		int i = (y * Width + x) * 3;
		return Color.FromRgb(_rgb[i], _rgb[i + 1], _rgb[i + 2]);
	}

	public RgbFrame With(PixelRect area, Color fill)
	{
		byte[] copy = (byte[])_rgb.Clone();
		for(int y = area.Y; y < area.Bottom; y++) {
			for(int x = area.X; x < area.Right; x++) {
				int i = (y * Width + x) * 3;
				copy[i] = fill.R; copy[i + 1] = fill.G; copy[i + 2] = fill.B;
			}
		}
		return new RgbFrame(Width, Height, copy);
	}

	//The area's content moved dy px down, the uncovered strip filled with background.
	public RgbFrame Moved(PixelRect area, int dy, Color background)
	{
		RgbFrame moved = With(area, background);
		for(int y = area.Y; y < area.Bottom && y + dy < Height; y++) {
			Array.Copy(_rgb, (y * Width + area.X) * 3, moved._rgb, ((y + dy) * Width + area.X) * 3, area.Width * 3);
		}
		return moved;
	}

	public static RgbFrame From(Bitmap bitmap)
	{
		int width = bitmap.PixelSize.Width, height = bitmap.PixelSize.Height;
		byte[] raw = new byte[width * height * 4];
		GCHandle pin = GCHandle.Alloc(raw, GCHandleType.Pinned);
		try {
			bitmap.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), raw.Length, width * 4);
		} finally {
			pin.Free();
		}
		//Same format rule as PlayerRender.Pixel: honour RGBA, default to BGRA.
		bool rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
		byte[] rgb = new byte[width * height * 3];
		for(int p = 0; p < width * height; p++) {
			rgb[p * 3] = raw[p * 4 + (rgba ? 0 : 2)];
			rgb[p * 3 + 1] = raw[p * 4 + 1];
			rgb[p * 3 + 2] = raw[p * 4 + (rgba ? 2 : 0)];
		}
		return new RgbFrame(width, height, rgb);
	}
}
