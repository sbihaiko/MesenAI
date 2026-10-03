using System;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.6 (PRD Part B §13.5.3 W-R4; rule 3): a failed build's mep_build /
	//mep_lint / mep_figure lines become problems named after the surface the
	//artist painted, with the kit file to open. A line without a translation is
	//counted and left to Show Log - never shown raw on the surface.
	public class RemasterBuildProblemsTests
	{
		private static RemasterKitIndex Kit()
		{
			RemasterKitIndex kit = new();
			kit.Add("usr001.png", "run", "/p/kit/rec-001/sheets/usr001.png");
			kit.Add("usr001-figure.png", "run", "/p/kit/rec-001/figures/usr001-figure.png");
			kit.Add("map-000.png", "stage 1 map", "/p/kit/rec-001/sheets/map-000.png");
			return kit;
		}

		private static RemasterBuildProblems Read(params string[] lines) => RemasterBuildProblemReader.Read(lines, Kit());

		[Fact]
		public void A_resized_figure_names_its_caption_its_file_and_its_size()
		{
			RemasterBuildProblems p = Read("error: usr001-figure.png: 644x128 is not a whole multiple of the 160x32 twin — the canvas was resized; repaint at the size you were given");
			RemasterBuildProblem problem = Assert.Single(p.Problems);
			Assert.Equal(RemasterProblemKind.CanvasResized, problem.Kind);
			Assert.Equal("run", problem.Caption);
			Assert.Equal("/p/kit/rec-001/figures/usr001-figure.png", problem.FilePath);
			Assert.Equal("640×128", problem.Detail);
			Assert.Equal(0, p.Untranslated);
		}

		[Fact]
		public void A_resized_sheet_and_a_moved_twin_are_canvas_problems()
		{
			RemasterBuildProblems p = Read(
				"error: map-000.png: 100x50 is not an integer multiple of the 32x32 sheet map-000.json describes — resize by a whole factor (1x, 2x, 3x, ...)",
				"error: usr001.png: reference twin usr001.orig.png is 8x8, not 16x16 — usr001.png and usr001.orig.png must grow together (#346)");
			Assert.Equal(new[] { RemasterProblemKind.CanvasResized, RemasterProblemKind.ReferenceChanged }, p.Problems.Select(x => x.Kind).ToArray());
			Assert.Equal("stage 1 map", p.Problems[0].Caption);
			Assert.Equal("", p.Problems[0].Detail);
		}

		[Fact]
		public void A_guide_marker_from_the_lint_names_the_sheet()
		{
			RemasterBuildProblems p = Read(
				"error: lint failed (/p/.mep-build)",
				"error   textures/sheets/map-000.png  cell index 0 at (1, 10) contains the guide sentinel #FF00FD — the .ora's guides or palettes layer was left visible on export (ADR-0220 §4); hide both layers and export the flat PNG again over map-000.png");
			RemasterBuildProblem problem = Assert.Single(p.Problems);
			Assert.Equal(RemasterProblemKind.GuideMarker, problem.Kind);
			Assert.Equal("stage 1 map", problem.Caption);
			Assert.Equal(0, p.Untranslated);
		}

		[Fact]
		public void Scale_mismatch_and_a_broken_png_are_translated()
		{
			RemasterBuildProblems p = Read(
				"error: usr001.png: painted at 3x while map-000.png is at 2x — all sheets of a pack share one <scale>",
				"error: map-000.png: not a valid PNG");
			Assert.Equal(RemasterProblemKind.ScaleMismatch, p.Problems[0].Kind);
			Assert.Equal("3×|2×", p.Problems[0].Detail);
			Assert.Equal(RemasterProblemKind.NotAPng, p.Problems[1].Kind);
		}

		[Fact]
		public void A_foreign_mep_folder_is_a_project_problem_that_opens_the_folder()
		{
			RemasterBuildProblems p = Read("error: /p/Contra/mep holds an installed pack - move it out of the project folder to build your own (the build never overwrites it, ADR-0147)");
			RemasterBuildProblem problem = Assert.Single(p.Problems);
			Assert.Equal(RemasterProblemKind.ForeignPack, problem.Kind);
			Assert.Equal("/p/Contra/mep", problem.FilePath);
		}

		[Fact]
		public void A_file_outside_the_kit_keeps_its_name_and_has_no_file_to_open()
		{
			RemasterBuildProblems p = Read("error: screen009.png: not a valid PNG");
			Assert.Equal("screen009", p.Problems[0].Caption);
			Assert.Equal("", p.Problems[0].FilePath);
		}

		[Fact]
		public void An_untranslated_error_is_counted_for_the_log_not_shown()
		{
			RemasterBuildProblems p = Read("info: chatter", "error: key source has no <tile> entries: /p/x", "error   textures/hires.txt  <tile> #3 is odd", "FAIL build");
			Assert.Empty(p.Problems);
			Assert.Equal(2, p.Untranslated);
		}

		[Fact]
		public void The_same_problem_twice_is_listed_once()
		{
			string line = "error: map-000.png: not a valid PNG";
			Assert.Single(Read(line, line).Problems);
		}

		[Fact]
		public void The_kit_index_reads_captions_and_figures_from_kit_json()
		{
			string project = Path.Combine(Path.GetTempPath(), "g6-kit-" + Guid.NewGuid().ToString("N"));
			try {
				Directory.CreateDirectory(Path.Combine(project, "kit", "rec-002"));
				Directory.CreateDirectory(Path.Combine(project, "kit", "pages"));
				File.WriteAllText(Path.Combine(project, "kit", "rec-002", "kit.json"),
					"{\"parts\":[{\"files\":[{\"path\":\"sheets/usr000.png\",\"title\":\"cycle000 — a 2-phase loop\",\"figure\":\"figures/usr000-figure.png\"}," +
					"{\"path\":\"scene/screen001.png\",\"caption\":\"title screen\"},{\"path\":\"sheets/obj004.png\"}]}]}");
				File.WriteAllText(Path.Combine(project, "kit", "pages", "kit.json"), "{\"parts\":[{\"files\":[{\"path\":\"chr/Chr_0.png\",\"title\":\"page 0\"}]}]}");

				RemasterKitIndex kit = RemasterKitIndex.Load(project, "rec-002");

				Assert.Equal(("cycle000", Path.Combine(project, "kit", "rec-002", "sheets", "usr000.png")), kit.Find("usr000.png"));
				Assert.Equal(("cycle000", Path.Combine(project, "kit", "rec-002", "figures", "usr000-figure.png")), kit.Find("USR000-figure.png"));
				Assert.Equal("title screen", kit.Find("screen001.png")!.Value.Caption);
				Assert.Equal("obj004", kit.Find("obj004.png")!.Value.Caption);
				Assert.Equal("page 0", kit.Find("Chr_0.png")!.Value.Caption);
				Assert.Null(kit.Find("nope.png"));
				Assert.Null(RemasterKitIndex.Load(Path.Combine(project, "missing"), "rec-001").Find("usr000.png"));
			} finally {
				Directory.Delete(project, true);
			}
		}
	}
}
