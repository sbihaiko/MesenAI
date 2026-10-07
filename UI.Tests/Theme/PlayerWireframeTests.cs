using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Theme
{
	//#951: the region comparison between a Player render and its wireframe,
	//host-free. Its inputs are the committed wireframes, deliberately broken
	//copies of them, known colour distances and the committed W-P renders
	//(PlayerRenders/, refreshed from UI.HeadlessTests' PlayerThemeRenderTests).
	public class PlayerWireframeTests
	{
		private static readonly string RepoRoot = FindRepoRoot();
		private static readonly string GuiRedesign = Path.Combine(RepoRoot, "docs", "media", "gui-redesign");
		private static readonly string CommittedRenders = Path.Combine(RepoRoot, "UI.Tests", "Theme", "PlayerRenders");

		private static RgbFrame Wireframe(string wId) => RgbFrame.FromPng(Path.Combine(GuiRedesign, wId + ".png"));

		private static RgbFrame WindowOf(string wId, int width = PlayerWireframe.WindowWidth, int height = PlayerWireframe.WindowHeight) => PlayerWireframe.Window(Wireframe(wId), width, height);

		private static bool HasWireframe(string wId) => File.Exists(Path.Combine(GuiRedesign, wId + ".png"));

		private static RegionResult Region(IEnumerable<RegionResult> results, string name) => results.Single(r => r.Region == name);

		private static IReadOnlyList<RegionResult> CompareWith(string wId, RgbFrame render)
		{
			RgbFrame window = WindowOf(wId, render.Width, render.Height);
			return PlayerWireframe.RegionsOf(wId).Select(r => PlayerWireframe.Compare(r, render, window, 1)).ToArray();
		}

		[Fact]
		public void Png_reader_decodes_a_committed_wireframe()
		{
			RgbFrame wireframe = Wireframe("W-P1");
			Assert.Equal(2400, wireframe.Width);
			Assert.Equal(1680, wireframe.Height);
			//Inside the window box, the content background is the theme's WINBG.
			Assert.True(PlayerWireframe.DeltaE(wireframe.At(160, 800), Rgb.Parse("#F5F5F7")) < 1);
		}

		[Fact]
		public void Delta_e_matches_known_cie76_distances()
		{
			Assert.Equal(100, PlayerWireframe.DeltaE(Rgb.Parse("#FFFFFF"), Rgb.Parse("#000000")), 0.1);
			Assert.Equal(0, PlayerWireframe.DeltaE(Rgb.Parse("#007AFF"), Rgb.Parse("#007AFF")), 6);
			//Pure red vs pure green in CIE76 is 170.6 (Lab 53.2/80.1/67.2 vs 87.7/-86.2/83.2).
			Assert.Equal(170.6, PlayerWireframe.DeltaE(Rgb.Parse("#FF0000"), Rgb.Parse("#00FF00")), 0.5);
		}

		//The crop and scale are right when the wireframe, taken as a render, passes
		//against itself and its window background is the theme's WINBG.
		[Fact]
		public void A_wireframe_taken_as_its_own_render_passes_every_region()
		{
			IReadOnlyList<RegionResult> results = CompareWith("W-P1", WindowOf("W-P1"));
			Assert.All(results, r => Assert.True(r.Pass, r.Region + ": " + string.Join(", ", r.Failures)));
			Assert.True(PlayerWireframe.DeltaE(Region(results, "content").RenderColor, Rgb.Parse("#F5F5F7")) < 1);
			Assert.True(PlayerWireframe.DeltaE(Region(results, "primary button").RenderColor, Rgb.Parse("#007AFF")) < 2);
			//The badge, the title, the subtitle and the top of the button.
			Assert.Equal(4, Region(results, "drop block").WireframeLines);
		}

		//A classic-Mesen button (#181818) where the tinted primary button stands fails on colour.
		[Fact]
		public void A_render_with_the_wrong_button_colour_fails_that_region()
		{
			RgbFrame broken = WindowOf("W-P1").With(new PixelBox(440, 370, 220, 50), Rgb.Parse("#181818"));
			IReadOnlyList<RegionResult> results = CompareWith("W-P1", broken);
			RegionResult button = Region(results, "primary button");
			Assert.Contains(PlayerWireframe.Colour, button.Failures);
			Assert.True(button.DeltaE > 50);
			Assert.True(Region(results, "title bar").Pass);
			Assert.True(Region(results, "status line").Pass);
		}

		//Content moved down 20 px: same colours, but the ink box and the text lines are off.
		[Fact]
		public void A_render_with_the_block_moved_fails_on_box_and_lines()
		{
			RgbFrame moved = WindowOf("W-P1").Moved(new PixelBox(300, 150, 500, 270), 20, Rgb.Parse("#F5F5F7"));
			RegionResult block = Region(CompareWith("W-P1", moved), "drop block");
			Assert.Equal(20, block.BoxOffset, 1);
			Assert.Contains(PlayerWireframe.InkBox, block.Failures);
			Assert.Contains(PlayerWireframe.TextLines, block.Failures);
			Assert.True(block.DeltaE <= PlayerWireframe.MaxDeltaE);
		}

		//A dropped line (the subtitle painted out) changes the line count.
		[Fact]
		public void A_render_missing_a_text_line_fails_on_lines()
		{
			RgbFrame broken = WindowOf("W-P1").With(new PixelBox(300, 322, 500, 26), Rgb.Parse("#F5F5F7"));
			RegionResult block = Region(CompareWith("W-P1", broken), "drop block");
			Assert.Equal(block.WireframeLines - 1, block.RenderLines);
			Assert.Contains(PlayerWireframe.TextLines, block.Failures);
		}

		//A smaller window (a sheet, a shorter W-P4 variant) is compared with its
		//boxes clamped to the frame and never throws; a region wholly below it
		//reports "outside the render", and the report says the size differs.
		[Theory]
		[InlineData(1100, 600)]
		[InlineData(800, 740)]
		[InlineData(480, 320)]
		public void A_render_of_another_size_is_compared_without_throwing(int width, int height)
		{
			RgbFrame render = WindowOf("W-P1", width, height);
			IReadOnlyList<RegionResult> results = PlayerWireframe.Compare("W-P1", render, Wireframe("W-P1"));
			Assert.Equal(PlayerWireframe.RegionsOf("W-P1").Count, results.Count);
			string report = PlayerWireframe.Report("W-P1-small", "W-P1", results, width, height);
			Assert.Contains("Size mismatch", report);
		}

		[Fact]
		public void A_region_below_a_shorter_render_is_reported_outside_it()
		{
			RgbFrame render = WindowOf("W-P1", 1100, 600);
			RegionResult status = Region(PlayerWireframe.Compare("W-P1", render, Wireframe("W-P1")), "status line");
			Assert.Equal(new[] { PlayerWireframe.OutsideRender }, status.Failures);
		}

		[Theory]
		[InlineData("W-P1", "W-P1")]
		[InlineData("W-P4-save-states", "W-P4")]
		[InlineData("W-P15-pill", "W-P15")]
		[InlineData("W-P8b", "W-P8b")]
		[InlineData("W-P8b-variant", "W-P8b")]
		[InlineData("W-P9-installing", "W-P9")]
		public void A_suffixed_render_name_resolves_to_its_wireframe(string renderName, string wId)
		{
			Assert.Equal(wId, PlayerWireframe.ResolveWireframeId(renderName, HasWireframe));
		}

		[Theory]
		[InlineData("W-P17-controller-sheet")]
		[InlineData("W-P99")]
		public void A_player_render_without_a_wireframe_gets_a_no_wireframe_report(string renderName)
		{
			Assert.True(PlayerWireframe.IsPlayerRenderName(renderName));
			Assert.Null(PlayerWireframe.ResolveWireframeId(renderName, HasWireframe));
			Assert.Contains("no wireframe", PlayerWireframe.NoWireframeReport(renderName));
		}

		[Theory]
		[InlineData("W-H1")]
		[InlineData("rom-picker-roots")]
		[InlineData("W-P")]
		public void A_non_player_render_is_not_paired(string renderName)
		{
			Assert.Null(PlayerWireframe.ResolveWireframeId(renderName, HasWireframe));
		}

		[Fact]
		public void The_report_lists_each_region_with_its_deltas_and_verdict()
		{
			RegionResult pass = new("title bar", Rgb.Parse("#FFFFFF"), Rgb.Parse("#FFFFFF"), 0.4, 2, 1, 1, 1.5);
			RegionResult fail = new("primary button", Rgb.Parse("#000000"), Rgb.Parse("#007AFF"), 61.2, 19, 1, 1, 19);
			string report = PlayerWireframe.Report("W-P1", "W-P1", new[] { pass, fail });
			Assert.Contains("# W-P1 vs docs/media/gui-redesign/W-P1.png", report);
			Assert.Contains("| title bar | #FFFFFF | #FFFFFF | 0.4 | 2.0 | 1/1 | 1.5 | pass |", report);
			Assert.Contains("| primary button | #000000 | #007AFF | 61.2 | 19.0 | 1/1 | 19.0 | FAIL: colour, ink box, text lines |", report);
			Assert.Contains("colour ΔE ≤ 10", report);
			Assert.DoesNotContain("Size mismatch", report);
		}

		//The ratchet gates a known deviation on its named kind only: a fixture
		//change that fixes the region's other failures (more seeded tiles, a chip
		//fix) is not a violation; the named kind passing is.
		[Fact]
		public void The_gate_holds_a_known_deviation_to_its_named_kind_only()
		{
			KnownDeviation[] known = { new("recent tiles", PlayerWireframe.TextLines, "three seeded tiles") };
			RegionResult linesAndBox = new("recent tiles", Rgb.Parse("#FFFFFF"), Rgb.Parse("#FFFFFF"), 1, 30, 3, 5, double.PositiveInfinity);
			RegionResult linesOnly = linesAndBox with { BoxOffset = 1 };
			RegionResult nowMatches = linesOnly with { RenderLines = 5, LineOffset = 1 };
			Assert.Empty(PlayerWireframe.Gate("W-P2", new[] { linesAndBox }, known));
			Assert.Empty(PlayerWireframe.Gate("W-P2", new[] { linesOnly }, known));
			string violation = Assert.Single(PlayerWireframe.Gate("W-P2", new[] { nowMatches }, known));
			Assert.Contains("no longer fails text lines", violation);
		}

		[Fact]
		public void The_gate_fails_an_unlisted_failing_region_and_an_unknown_region_name()
		{
			RegionResult failing = new("title bar", Rgb.Parse("#000000"), Rgb.Parse("#FFFFFF"), 100, 0, 1, 1, 0);
			Assert.Contains(PlayerWireframe.Gate("W-P1", new[] { failing }, Array.Empty<KnownDeviation>()), v => v.Contains("fails colour"));
			Assert.Contains(PlayerWireframe.Gate("W-P1", new[] { failing with { DeltaE = 0 } }, new[] { new KnownDeviation("nope", PlayerWireframe.Colour, "") }), v => v.Contains("no such region"));
		}

		//#951 item 3: CI has no core (ADR-0131), so it cannot render. The
		//committed renders go through the same comparison and the same known
		//deviations as PlayerThemeRenderTests, and their reports land in
		//$MESEN_PLAYER_RENDERS, which checks.yml uploads.
		[Fact]
		public void Committed_renders_pass_the_wireframe_gate_and_write_their_reports()
		{
			string[] renders = Directory.GetFiles(CommittedRenders, "W-P*.png");
			Assert.NotEmpty(renders);
			string? output = Environment.GetEnvironmentVariable("MESEN_PLAYER_RENDERS");
			List<string> violations = new();
			foreach(string path in renders) {
				string name = Path.GetFileNameWithoutExtension(path);
				string? wId = PlayerWireframe.ResolveWireframeId(name, HasWireframe);
				Assert.NotNull(wId);
				RgbFrame render = RgbFrame.FromPng(path);
				IReadOnlyList<RegionResult> results = PlayerWireframe.Compare(wId!, render, Wireframe(wId!));
				violations.AddRange(PlayerWireframe.Gate(wId!, results, PlayerWireframe.KnownDeviationsOf(wId!)));
				if(!string.IsNullOrEmpty(output)) {
					Directory.CreateDirectory(output);
					File.WriteAllText(Path.Combine(output, name + ".wireframe.md"), PlayerWireframe.Report(name, wId!, results, render.Width, render.Height));
				}
			}
			Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
		}

		private static string FindRepoRoot()
		{
			DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
			while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
				dir = dir.Parent;
			}
			if(dir == null) {
				throw new InvalidOperationException("Could not locate repo root (Mesen.sln) from " + AppContext.BaseDirectory);
			}
			return dir.FullName;
		}
	}
}
