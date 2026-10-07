using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Mesen.HeadlessTests;

//#951: the region comparison itself, core-free so it runs on CI. Its inputs
//are the committed wireframes, deliberately broken copies of them and known
//colour distances; the renders it is applied to come from PlayerThemeRenderTests.
public class PlayerWireframeComparisonTests
{
	private static RegionResult Region(IEnumerable<RegionResult> results, string name) => results.Single(r => r.Region == name);

	private static IReadOnlyList<RegionResult> CompareWith(string wId, RgbFrame render)
	{
		RgbFrame wireframe = PlayerWireframe.LoadScaled(wId, render.Width, render.Height);
		return PlayerWireframe.RegionsOf(wId).Select(r => PlayerWireframe.Compare(r, render, wireframe, 1)).ToArray();
	}

	[Fact]
	public void Delta_e_matches_known_cie76_distances()
	{
		Assert.Equal(100, PlayerWireframe.DeltaE(Colors.White, Colors.Black), 0.1);
		Assert.Equal(0, PlayerWireframe.DeltaE(Color.Parse("#007AFF"), Color.Parse("#007AFF")), 6);
		//Pure red vs pure green in CIE76 is 170.6 (Lab 53.2/80.1/67.2 vs 87.7/-86.2/83.2).
		Assert.Equal(170.6, PlayerWireframe.DeltaE(Color.Parse("#FF0000"), Color.Parse("#00FF00")), 0.5);
	}

	//The crop and scale are right when the wireframe, taken as a render, passes
	//against itself and its window background is the theme's WINBG.
	[AvaloniaFact]
	public void A_wireframe_taken_as_its_own_render_passes_every_region()
	{
		RgbFrame wireframe = PlayerWireframe.LoadScaled("W-P1", PlayerWireframe.WindowWidth, PlayerWireframe.WindowHeight);
		IReadOnlyList<RegionResult> results = CompareWith("W-P1", wireframe);
		Assert.All(results, r => Assert.True(r.Pass, r.Region + ": " + string.Join(", ", r.Failures)));
		Assert.True(PlayerWireframe.DeltaE(Region(results, "content").RenderColor, Color.Parse("#F5F5F7")) < 1);
		Assert.True(PlayerWireframe.DeltaE(Region(results, "primary button").RenderColor, Color.Parse("#007AFF")) < 2);
		//The badge, the title, the subtitle and the top of the button.
		Assert.Equal(4, Region(results, "drop block").WireframeLines);
	}

	//A classic-Mesen button (#181818) where the tinted primary button stands fails on colour.
	[AvaloniaFact]
	public void A_render_with_the_wrong_button_colour_fails_that_region()
	{
		RgbFrame broken = PlayerWireframe.LoadScaled("W-P1", 1100, 740).With(new PixelRect(440, 370, 220, 50), Color.Parse("#181818"));
		IReadOnlyList<RegionResult> results = CompareWith("W-P1", broken);
		RegionResult button = Region(results, "primary button");
		Assert.Contains("colour", button.Failures);
		Assert.True(button.DeltaE > 50);
		Assert.True(Region(results, "title bar").Pass);
		Assert.True(Region(results, "status line").Pass);
	}

	//Content moved down 20 px: same colours, but the ink box and the text lines are off.
	[AvaloniaFact]
	public void A_render_with_the_block_moved_fails_on_box_and_lines()
	{
		RgbFrame moved = PlayerWireframe.LoadScaled("W-P1", 1100, 740).Moved(new PixelRect(300, 150, 500, 270), 20, Color.Parse("#F5F5F7"));
		RegionResult block = Region(CompareWith("W-P1", moved), "drop block");
		Assert.Equal(20, block.BoxOffset, 1);
		Assert.Contains("ink box", block.Failures);
		Assert.Contains("text lines", block.Failures);
		Assert.True(block.DeltaE <= PlayerWireframe.MaxDeltaE);
	}

	//A dropped line (the subtitle painted out) changes the line count.
	[AvaloniaFact]
	public void A_render_missing_a_text_line_fails_on_lines()
	{
		RgbFrame broken = PlayerWireframe.LoadScaled("W-P1", 1100, 740).With(new PixelRect(300, 322, 500, 26), Color.Parse("#F5F5F7"));
		RegionResult block = Region(CompareWith("W-P1", broken), "drop block");
		Assert.Equal(block.WireframeLines - 1, block.RenderLines);
		Assert.Contains("text lines", block.Failures);
	}

	[Fact]
	public void The_report_lists_each_region_with_its_deltas_and_verdict()
	{
		RegionResult pass = new("title bar", Colors.White, Colors.White, 0.4, 2, 1, 1, 1.5);
		RegionResult fail = new("primary button", Colors.Black, Color.Parse("#007AFF"), 61.2, 19, 1, 1, 19);
		string report = PlayerWireframe.Report("W-P1", new[] { pass, fail });
		Assert.Contains("| title bar | #FFFFFF | #FFFFFF | 0.4 | 2.0 | 1/1 | 1.5 | pass |", report);
		Assert.Contains("| primary button | #000000 | #007AFF | 61.2 | 19.0 | 1/1 | 19.0 | FAIL: colour, ink box, text lines |", report);
		Assert.Contains("colour ΔE ≤ 10", report);
	}
}
