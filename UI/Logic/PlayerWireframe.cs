using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Mesen.Logic;

//#951: pairs a Player render with its §13.5 wireframe (docs/media/gui-redesign/W-*.png)
//and compares regions, never pixels (ADR-0249 rules a pixel diff out). The
//wireframe's window box is cropped and scaled to the render's size; then, per
//region, the dominant colours are compared by CIE76 delta E, the ink boxes
//(pixels far from the dominant colour) by their largest edge offset, and the
//text lines (horizontal ink bands) by count and centre offset, top to bottom.
//Host-free (ADR-0123): UI.Tests asserts the rules; UI.HeadlessTests only feeds
//it real renders (PlayerWireframeReport) and gates its own screens.
public static class PlayerWireframe
{
	//The written tolerances (#951).
	public const double MaxDeltaE = 10;        //dominant colour, CIE76
	public const double MaxBoxOffset = 8;      //largest ink-box edge offset, logical px
	public const double MaxLineOffset = 8;     //largest text-line centre offset, logical px
	public const double InkDeltaE = 25;        //a pixel this far from the dominant colour is ink
	public const int MinLineHeight = 3;        //an ink band thinner than this is a rule, not a line

	//The failure kinds a region reports.
	public const string Colour = "colour";
	public const string InkBox = "ink box";
	public const string TextLines = "text lines";
	public const string OutsideRender = "outside the render";

	//scripts/render_gui_wireframes.py draws at S = 2 with the window at
	//WIN = (50, 70, 1150, 810) logical px: a 1100 x 740 window, the renders' size.
	public const int WindowWidth = 1100;
	public const int WindowHeight = 740;
	private static readonly PixelBox WireframeWindow = new(100, 140, 2200, 1480);

	//Regions in window logical px. Every W-P render gets the shared chrome
	//regions; the title bar starts after the wireframe's traffic lights, which
	//a headless window does not draw.
	private static readonly WireframeRegion[] Chrome = {
		new("title bar", new LogicalBox(80, 0, 1020, 52)),
		new("content", new LogicalBox(0, 53, 1100, 660)),
		new("status line", new LogicalBox(0, 714, 1100, 26)),
	};

	//Each box is placed on the wireframe's element (render_gui_wireframes.py
	//coordinates minus WIN's origin), so a render that moved shows as an offset.
	private static readonly Dictionary<string, WireframeRegion[]> Specific = new() {
		["W-P1"] = new WireframeRegion[] {
			new("drop block", new LogicalBox(300, 150, 500, 290)),
			new("primary button", new LogicalBox(450, 380, 200, 30)),
		},
		["W-P2"] = new WireframeRegion[] {
			new("continue card", new LogicalBox(40, 123, 1020, 160)),
			new("continue button", new LogicalBox(330, 222, 100, 30)),
			new("recent tiles", new LogicalBox(40, 343, 1020, 160)),
		},
		["W-P4"] = new WireframeRegion[] {
			new("overlay card", new LogicalBox(360, 80, 380, 520)),
			new("resume button", new LogicalBox(390, 168, 320, 32)),
			new("grouped rows", new LogicalBox(376, 230, 348, 250)),
		},
	};

	//The regions PlayerThemeRenderTests' renders are known to differ on, each
	//held to the failure kind its layout cause produces (Gate). Shared by the
	//headless gate on fresh renders and UI.Tests' gate on the committed ones.
	private static readonly Dictionary<string, KnownDeviation[]> Known = new() {
		["W-P1"] = new KnownDeviation[] {
		},
		["W-P2"] = new KnownDeviation[] {
		},
		["W-P4"] = new KnownDeviation[] {
		},
	};

	public static IReadOnlyList<KnownDeviation> KnownDeviationsOf(string wId) => Known.TryGetValue(wId, out KnownDeviation[]? own) ? own : Array.Empty<KnownDeviation>();

	private static readonly Regex PlayerId = new(@"^W-P\d+[a-z]?(?=-|$)", RegexOptions.CultureInvariant);
	private static readonly Regex PlayerNumber = new(@"^W-P\d+(?=[a-z-]|$)", RegexOptions.CultureInvariant);

	//The wireframe a render named renderName pairs with: the exact name when
	//that wireframe exists, else its W-id prefix (W-P4-save-states -> W-P4,
	//W-P8b-x -> W-P8b, then W-P8). Null when the name is not a W-P render or
	//no wireframe matches; the hook then reports "no wireframe" for a W-P name.
	public static string? ResolveWireframeId(string renderName, Func<string, bool> hasWireframe)
	{
		if(!IsPlayerRenderName(renderName)) {
			return null;
		}
		string?[] candidates = { renderName, PlayerId.Match(renderName) is { Success: true } a ? a.Value : null, PlayerNumber.Match(renderName) is { Success: true } b ? b.Value : null };
		return candidates.FirstOrDefault(c => c != null && hasWireframe(c));
	}

	public static bool IsPlayerRenderName(string renderName) => renderName.StartsWith("W-P", StringComparison.Ordinal);

	public static IReadOnlyList<WireframeRegion> RegionsOf(string wId) => Chrome.Concat(Specific.TryGetValue(wId, out WireframeRegion[]? own) ? own : Array.Empty<WireframeRegion>()).ToArray();

	//The wireframe's window box, area-averaged down to width x height.
	public static RgbFrame Window(RgbFrame wireframe, int width, int height) => wireframe.Scaled(WireframeWindow, width, height);

	//A render whose aspect is not the window's is compared anyway (the boxes
	//are clamped to it), but the report says the numbers are suspect.
	public static bool IsWindowSized(int width, int height) => Math.Abs(width * WindowHeight - height * WindowWidth) <= WindowWidth * 2;

	public static IReadOnlyList<RegionResult> Compare(string wId, RgbFrame render, RgbFrame wireframe)
	{
		RgbFrame window = Window(wireframe, render.Width, render.Height);
		double scale = render.Width / (double)WindowWidth;
		return RegionsOf(wId).Select(region => Compare(region, render, window, scale)).ToArray();
	}

	//render and window share one size; the region box is clamped to it.
	public static RegionResult Compare(WireframeRegion region, RgbFrame render, RgbFrame window, double scale)
	{
		PixelBox box = new PixelBox((int)(region.Box.X * scale), (int)(region.Box.Y * scale), (int)(region.Box.Width * scale), (int)(region.Box.Height * scale))
			.ClampTo(Math.Min(render.Width, window.Width), Math.Min(render.Height, window.Height));
		if(box.IsEmpty) {
			return RegionResult.Outside(region.Name);
		}
		RegionMeasure r = Measure(render, box, scale), w = Measure(window, box, scale);
		double deltaE = DeltaE(r.Dominant, w.Dominant);
		double boxOffset = r.Ink is LogicalBox ri && w.Ink is LogicalBox wi
			? new[] { ri.Left - wi.Left, ri.Top - wi.Top, ri.Right - wi.Right, ri.Bottom - wi.Bottom }.Max(Math.Abs)
			: (r.Ink == null && w.Ink == null ? 0 : double.PositiveInfinity);
		double lineOffset = r.Lines.Count == w.Lines.Count
			? r.Lines.Zip(w.Lines, (a, b) => Math.Abs(a - b)).DefaultIfEmpty(0).Max()
			: double.PositiveInfinity;
		return new RegionResult(region.Name, r.Dominant, w.Dominant, deltaE, boxOffset, r.Lines.Count, w.Lines.Count, lineOffset);
	}

	private sealed record RegionMeasure(Rgb Dominant, LogicalBox? Ink, IReadOnlyList<double> Lines);

	//Logical-px geometry inside a non-empty device-px box.
	private static RegionMeasure Measure(RgbFrame frame, PixelBox box, double scale)
	{
		Rgb dominant = Dominant(frame, box);
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
		LogicalBox? ink = maxX < 0 ? null : new LogicalBox(minX / scale, minY / scale, (maxX - minX + 1) / scale, (maxY - minY + 1) / scale);
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
	private static Rgb Dominant(RgbFrame frame, PixelBox box)
	{
		Dictionary<int, (int Count, long R, long G, long B)> bins = new();
		for(int y = box.Y; y < box.Bottom; y++) {
			for(int x = box.X; x < box.Right; x++) {
				Rgb c = frame.At(x, y);
				int key = (c.R >> 4) << 8 | (c.G >> 4) << 4 | c.B >> 4;
				bins.TryGetValue(key, out var bin);
				bins[key] = (bin.Count + 1, bin.R + c.R, bin.G + c.G, bin.B + c.B);
			}
		}
		var top = bins.Values.MaxBy(b => b.Count);
		return new Rgb((byte)(top.R / top.Count), (byte)(top.G / top.Count), (byte)(top.B / top.Count));
	}

	//CIE76 delta E between two sRGB colours (D65).
	public static double DeltaE(Rgb a, Rgb b)
	{
		(double l1, double a1, double b1) = Lab(a);
		(double l2, double a2, double b2) = Lab(b);
		return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
	}

	private static (double L, double A, double B) Lab(Rgb c)
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
	//renderName is the PNG's name (W-P4-save-states), wId the wireframe it pairs with.
	public static string Report(string renderName, string wId, IReadOnlyList<RegionResult> results, int renderWidth = WindowWidth, int renderHeight = WindowHeight)
	{
		StringBuilder sb = new();
		sb.AppendLine($"# {renderName} vs docs/media/gui-redesign/{wId}.png");
		sb.AppendLine();
		if(!IsWindowSized(renderWidth, renderHeight)) {
			sb.AppendLine($"Size mismatch: the render is {renderWidth} x {renderHeight}, not the wireframe's {WindowWidth} x {WindowHeight} window; regions are clamped to the render and the deltas are indicative only.");
			sb.AppendLine();
		}
		sb.AppendLine(FormattableString.Invariant($"Tolerances: colour ΔE ≤ {MaxDeltaE}, ink-box edge offset ≤ {MaxBoxOffset} px, text-line centre offset ≤ {MaxLineOffset} px with the same line count."));
		sb.AppendLine();
		sb.AppendLine("| region | render colour | wireframe colour | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |");
		sb.AppendLine("|---|---|---|---|---|---|---|---|");
		foreach(RegionResult r in results) {
			sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
				$"| {r.Region} | {r.RenderColor} | {r.WireframeColor} | {r.DeltaE:0.0} | {r.BoxOffset:0.0} | {r.RenderLines}/{r.WireframeLines} | {r.LineOffset:0.0} | {(r.Pass ? "pass" : "FAIL: " + string.Join(", ", r.Failures))} |"));
		}
		return sb.ToString();
	}

	//What the hook writes for a W-P render with no wireframe to pair with.
	public static string NoWireframeReport(string renderName) =>
		$"# {renderName}: no wireframe\n\nNo docs/media/gui-redesign/W-P*.png matches this render's name or its W-id prefix, so no region was compared.\n";

	//The render gate's ratchet: every region passes, except a named known
	//deviation, which must still fail on its named kind. Other failure kinds on
	//a known-deviation region are not gated, because they come from fixture
	//data (seeded tiles, port chips) rather than layout. Returns the violations.
	public static IReadOnlyList<string> Gate(string wId, IReadOnlyList<RegionResult> results, IReadOnlyList<KnownDeviation> known)
	{
		List<string> violations = new();
		foreach(KnownDeviation k in known.Where(k => results.All(r => r.Region != k.Region))) {
			violations.Add($"{wId} {k.Region}: no such region");
		}
		foreach(RegionResult r in results) {
			string measured = string.Create(CultureInfo.InvariantCulture, $"{wId} {r.Region}: ΔE {r.DeltaE:0.0}, box {r.BoxOffset:0.0} px, lines {r.RenderLines}/{r.WireframeLines} off {r.LineOffset:0.0} px");
			KnownDeviation[] own = known.Where(k => k.Region == r.Region).ToArray();
			if(own.Length == 0) {
				if(!r.Pass) {
					violations.Add(measured + " fails " + string.Join(", ", r.Failures));
				}
				continue;
			}
			foreach(KnownDeviation k in own.Where(k => !r.Failures.Contains(k.Kind))) {
				violations.Add($"{measured} no longer fails {k.Kind} ({k.Why}); remove it from the known deviations");
			}
		}
		return violations;
	}
}

public sealed record WireframeRegion(string Name, LogicalBox Box);

//A region known to differ from the wireframe on one failure kind, and why.
public sealed record KnownDeviation(string Region, string Kind, string Why);

public sealed record RegionResult(string Region, Rgb RenderColor, Rgb WireframeColor, double DeltaE, double BoxOffset, int RenderLines, int WireframeLines, double LineOffset, bool IsOutside = false)
{
	public static RegionResult Outside(string region) => new(region, default, default, double.NaN, double.NaN, 0, 0, double.NaN, true);

	public IReadOnlyList<string> Failures {
		get {
			if(IsOutside) {
				return new[] { PlayerWireframe.OutsideRender };
			}
			List<string> failures = new();
			if(DeltaE > PlayerWireframe.MaxDeltaE) { failures.Add(PlayerWireframe.Colour); }
			if(BoxOffset > PlayerWireframe.MaxBoxOffset) { failures.Add(PlayerWireframe.InkBox); }
			if(LineOffset > PlayerWireframe.MaxLineOffset) { failures.Add(PlayerWireframe.TextLines); }
			return failures;
		}
	}

	public bool Pass => Failures.Count == 0;
}
