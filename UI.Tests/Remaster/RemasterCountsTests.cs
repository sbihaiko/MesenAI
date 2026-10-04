using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//ADR-0252: the three numbers the Remaster renders show - the shapes seen
	//while you played (W-R1 zone ①), the cells painted (W-R0 recent rows, the
	//status line) and a figure's painted phases (W-R5) - each read off a file
	//a tool wrote, or "unknown" (null), never a guess.
	public sealed class RemasterCountsTests
	{
		private static readonly byte[] Red = { 255, 0, 0, 255 };
		private static readonly byte[] Blue = { 0, 0, 255, 255 };

		// ---- §1 shapes seen while you played ------------------------------

		[Fact]
		public void A_shape_is_the_tile_data_of_a_recorded_row_with_every_palette_folded()
		{
			HashSet<string> keys = RemasterShapesSeen.KeysOf(new[] {
				"<ver>109",
				"<scale>4",
				"<tile>0,0000000000000000000000000000003C,0F102816,0,0,1,N,0,0",
				//The same shape under another palette is one shape (the core's GetKey(true)).
				"<tile>0,0000000000000000000000000000003c,0F162736,8,0,1,N,0,0",
				"<tile>1,23180C0C0919324F3F170B0F0F172F7F,0F102816,0,8,1,N,0,0",
				//A `Y` row is the ROM export the recorder seeds, not something played.
				"<tile>2,8080F0CC0202113F8080F0FCFEFEEFDF,0F001030,0,16,1,Y,0,0",
				//A conditioned row still names a played shape.
				"[hasSprite]<tile>3,71E7FFF56E1E3F5D7E98809E7F1B317B,0F102816,0,24,1,N,0,0",
				//Too short to carry the defaultTile field: not counted either way.
				"<tile>4,E7CFEF97EEEEFC78F971317912120488,0F102816",
				"<background>bg.png,1",
			});
			Assert.Equal(new[] {
				"0000000000000000000000000000003C",
				"23180C0C0919324F3F170B0F0F172F7F",
				"71E7FFF56E1E3F5D7E98809E7F1B317B",
			}, keys.OrderBy(k => k, System.StringComparer.Ordinal).ToArray());
		}

		[Fact]
		public void The_project_count_is_the_union_of_its_recordings_and_unknown_without_any()
		{
			IReadOnlySet<string> one = new HashSet<string> { "A", "B" };
			IReadOnlySet<string> two = new HashSet<string> { "B", "C", "D" };
			Assert.Equal(4, RemasterShapesSeen.Count(new[] { one, two }));
			//A recording without a readable textures/hires.txt adds nothing and hides nothing.
			Assert.Equal(2, RemasterShapesSeen.Count(new[] { one, null }));
			Assert.Null(RemasterShapesSeen.Count(new IReadOnlySet<string>?[] { null, null }));
			Assert.Null(RemasterShapesSeen.Count(System.Array.Empty<IReadOnlySet<string>?>()));
			//A recording that drew nothing (only ROM seeds) is a known zero.
			Assert.Equal(0, RemasterShapesSeen.Count(new IReadOnlySet<string>?[] { new HashSet<string>() }));
		}

		// ---- §2 cells painted -----------------------------------------------

		[Fact]
		public void A_cell_is_painted_when_its_square_differs_from_the_twin_upscaled()
		{
			RemasterPixels twin = RemasterPng.Decode(KitFixture.Png(16, 8, Red))!;
			RemasterPixels sheet = RemasterPng.Decode(PaintedPng(64, 32, 4 * 10, 4 * 3))!;
			List<RemasterCellRect> rects = new() { new("s#0", 0, 0, 8, ""), new("s#1", 8, 0, 8, "") };

			Assert.Equal(new[] { "s#1" }, RemasterCellPaint.Painted(sheet, twin, rects)!.Select(r => r.Key).ToArray());
			Assert.Empty(RemasterCellPaint.Painted(RemasterPng.Decode(KitFixture.Png(64, 32, Red))!, twin, rects)!);
			//A sheet that is no whole multiple of its twin cannot be told: unknown, not zero.
			Assert.Null(RemasterCellPaint.Painted(RemasterPng.Decode(KitFixture.Png(60, 32, Red))!, twin, rects));
			//A cell reaching past the picture counts as painted, as mep_build's _EditedProbe.
			Assert.Single(RemasterCellPaint.Painted(RemasterPng.Decode(KitFixture.Png(64, 32, Red))!, twin, new[] { new RemasterCellRect("s#9", 12, 0, 8, "") })!);
		}

		[Fact]
		public void The_sheet_cells_are_the_sidecars_cells_at_its_grid_unit()
		{
			List<RemasterCellRect> cells = RemasterCellPaint.SheetCells(
				"{\"version\": 1, \"gridUnit\": 16, \"cells\": [{\"index\": 0, \"x\": 1, \"y\": 1}, {\"index\": 3, \"x\": 18, \"y\": 1}, {\"index\": 4}]}",
				"/k/sheets/usr001.json")!;
			Assert.Equal(new[] { ("/k/sheets/usr001.json#0", 1, 1, 16), ("/k/sheets/usr001.json#3", 18, 1, 16) },
				cells.Select(c => (c.Key, c.X, c.Y, c.Size)).ToArray());
			Assert.Equal(8, RemasterCellPaint.SheetCells("{\"cells\": [{\"index\": 0, \"x\": 0, \"y\": 0}]}", "a.json")![0].Size);
			Assert.Null(RemasterCellPaint.SheetCells("{\"placements\": []}", "map.json"));
			Assert.Null(RemasterCellPaint.SheetCells("not json", "x.json"));
		}

		[Fact]
		public void A_figure_cell_is_keyed_by_its_home_cell_on_the_sheet_and_names_its_pose()
		{
			List<RemasterCellRect> cells = RemasterCellPaint.FigureCells(
				"{\"version\": 2, \"kind\": \"figure\", \"unit\": 8, \"cells\": [" +
				"{\"x\": 0, \"y\": 2, \"sheet\": \"usr000.json\", \"index\": 5, \"pose\": \"pose001\"}," +
				"{\"x\": 7, \"y\": 0, \"sheet\": \"usr000.json\", \"pose\": \"pose002\"}]}",
				"/k/sheets")!;
			Assert.Equal(new[] { (Path.Combine("/k/sheets", "usr000.json") + "#5", 0, 2, 8, "pose001") },
				cells.Select(c => (c.Key, c.X, c.Y, c.Size, c.Pose)).ToArray());
			//Version 1 placed cells on the 8 px grid in another unit: not read.
			Assert.Null(RemasterCellPaint.FigureCells("{\"version\": 1, \"kind\": \"figure\", \"unit\": 8, \"cells\": []}", "/k/sheets"));
		}

		[Fact]
		public void An_untouched_kit_has_zero_cells_painted_and_a_brushed_cell_counts_once()
		{
			using KitFixture f = new();
			WriteFigureKit(f);
			RemasterKitTile run = RemasterKitReader.Read(f.Project).Tiles.Single();
			RemasterTileCells untouched = RemasterCellPaint.Measure(run);
			Assert.Empty(untouched.Cells!);
			Assert.Empty(untouched.Poses!);
			Assert.Equal(0, RemasterCellPaint.Total(new[] { untouched }));

			//The artist paints cell 1 on the sheet, and the same cell again on the
			//figure view: it is one cell of the build, counted once.
			File.WriteAllBytes(Path.Combine(f.Project, "kit/rec-001/sheets/usr000.png"), PaintedPng(64, 32, 4 * 9, 0));
			File.WriteAllBytes(Path.Combine(f.Project, "kit/rec-001/figures/usr000-figure.png"), PaintedPng(64, 32, 4 * 9, 0));
			RemasterTileCells painted = RemasterCellPaint.Measure(run);
			Assert.Single(painted.Cells!);
			Assert.Equal(new[] { "pose002" }, painted.Poses!.ToArray());
			Assert.Equal(1, RemasterCellPaint.Total(new[] { painted, untouched }));
		}

		[Fact]
		public void Paint_only_on_the_figure_view_lands_on_its_home_cell()
		{
			using KitFixture f = new();
			WriteFigureKit(f);
			File.WriteAllBytes(Path.Combine(f.Project, "kit/rec-001/figures/usr000-figure.png"), PaintedPng(64, 32, 0, 0));
			RemasterTileCells painted = RemasterCellPaint.Measure(RemasterKitReader.Read(f.Project).Tiles.Single());
			Assert.Equal(new[] { Path.Combine(f.Project, "kit/rec-001/sheets/usr000.json") + "#0" }, painted.Cells!.ToArray());
			Assert.Equal(new[] { "pose001" }, painted.Poses!.ToArray());
		}

		[Fact]
		public void Surfaces_without_a_pre_paint_twin_or_a_sidecar_are_unknown_and_add_nothing()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKit kit = RemasterKitReader.Read(f.Project);
			//The standard kit has no sheet sidecars and its pages have no pre-paint twin.
			Assert.All(kit.Tiles, t => Assert.Null(RemasterCellPaint.Measure(t).Cells));
			Assert.Null(RemasterCellPaint.Total(kit.Tiles.Select(RemasterCellPaint.Measure)));
		}

		// ---- §3 painted phases ----------------------------------------------

		[Fact]
		public void The_kit_reader_keeps_the_play_order_and_the_pose_ids()
		{
			using KitFixture f = new();
			WriteFigureKit(f);
			RemasterKitTile run = RemasterKitReader.Read(f.Project).Tiles.Single();
			Assert.Equal(new int?[] { 1, 2, 1, null }, run.PlayOrder.ToArray());
			Assert.Equal(new[] { "pose001", "pose002" }, run.PoseIds.ToArray());
			Assert.Equal(4, run.Phases);
		}

		[Fact]
		public void A_phase_is_painted_when_the_pose_its_column_plays_is_painted()
		{
			using KitFixture f = new();
			WriteFigureKit(f);
			RemasterKitTile run = RemasterKitReader.Read(f.Project).Tiles.Single();

			Assert.Equal((0, 4), RemasterCellPaint.Phases(run, RemasterCellPaint.Measure(run)));
			//Column 1 plays twice: painting it paints two phases; the phase this
			//sheet does not draw (null) is never painted here.
			Assert.Equal((2, 4), RemasterCellPaint.Phases(run, new RemasterTileCells(new HashSet<string> { "x" }, new HashSet<string> { "pose001" })));
			Assert.Equal((3, 4), RemasterCellPaint.Phases(run, new RemasterTileCells(new HashSet<string> { "x" }, new HashSet<string> { "pose001", "pose002" })));
			//No figure sidecar: which pose a cell belongs to is unknown, so is the phase count.
			Assert.Null(RemasterCellPaint.Phases(run, new RemasterTileCells(new HashSet<string>(), null)));
		}

		[Fact]
		public void A_tile_without_a_play_order_has_no_phase_count()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKitTile still = RemasterKitReader.Read(f.Project).Tiles[1];
			Assert.Null(RemasterCellPaint.Phases(still, new RemasterTileCells(new HashSet<string>(), new HashSet<string>())));
		}

		[Fact]
		public void The_popover_says_painted_phases_only_when_the_picture_is_painted_and_a_phase_is()
		{
			using KitFixture f = new();
			WriteFigureKit(f);
			RemasterKitTile run = RemasterKitReader.Read(f.Project).Tiles.Single();
			RemasterProvenanceLine Last(RemasterPaintResult paint, (int, int)? phases) => RemasterProvenance.Lines(run, paint, phases).Last();

			Assert.Equal(new RemasterProvenanceLine(RemasterProvenanceKind.PaintedPhases, 2, 4, RemasterPaintUnknown.None), Last(RemasterPaintResult.Painted, (2, 4)));
			//Paint outside every figure's cells: painted, but no phase is.
			Assert.Equal(RemasterProvenanceKind.Painted, Last(RemasterPaintResult.Painted, (0, 4)).Kind);
			Assert.Equal(RemasterProvenanceKind.Painted, Last(RemasterPaintResult.Painted, null).Kind);
			//The whole-picture probe decides "painted at all"; the phases never override it.
			Assert.Equal(RemasterProvenanceKind.NotPainted, Last(RemasterPaintResult.Untouched, (1, 4)).Kind);
		}

		[Fact]
		public void The_shape_cache_reads_each_recordings_hires_and_skips_recordings_without_textures()
		{
			using KitFixture f = new();
			f.Write("auto/rec-001/textures/hires.txt", "<ver>109\n<tile>0,AA,0F102816,0,0,1,N\n<tile>1,BB,0F102816,0,0,1,Y\n");
			f.Write("auto/rec-002/textures/hires.txt", "<tile>0,aa,0F162736,0,0,1,N\n<tile>1,CC,0F102816,0,0,1,N\n");
			f.Write("auto/rec-003/audio/fingerprints.json", "{}");
			RemasterProjectInfo project = RemasterProjectReader.Read(f.Project);
			RemasterShapeCache cache = new();
			Assert.Equal(2, cache.Count(project.Recordings));
			Assert.Null(cache.Count(project.Recordings.Where(r => r.Id == "rec-003").ToList()));
		}

		// ---- W-R2's screens -------------------------------------------------

		[Theory]
		[InlineData(0u, 0u)]
		[InlineData(2u, 2u)]
		[InlineData(300u, 300u)]
		[InlineData(451u, 300u)]
		public void Screens_captured_stop_at_the_recorders_cap(uint seen, uint captured)
		{
			Assert.Equal(captured, RemasterScreen.ScreensCaptured(seen));
		}

		// ---- fixtures -------------------------------------------------------

		//A 16x8 1x figure grid at 4x: pose001 in cell 0 (x 0), pose002 in cell 1
		//(x 8); a 4-phase loop playing columns 1 2 1 and one phase drawn elsewhere.
		private static void WriteFigureKit(KitFixture f)
		{
			f.WritePair("kit/rec-001/sheets/usr000.png", 16, 8, 4, Red);
			f.WritePair("kit/rec-001/figures/usr000-figure.png", 16, 8, 4, Red);
			f.Write("kit/rec-001/sheets/usr000.json",
				"{\"version\": 1, \"kind\": \"sprite\", \"gridUnit\": 8, \"cells\": [{\"index\": 0, \"x\": 0, \"y\": 0}, {\"index\": 1, \"x\": 8, \"y\": 0}]}");
			f.Write("kit/rec-001/figures/usr000-figure.json",
				"{\"version\": 2, \"kind\": \"figure\", \"unit\": 8, \"cells\": [" +
				"{\"x\": 0, \"y\": 0, \"sheet\": \"usr000.json\", \"index\": 0, \"pose\": \"pose001\"}," +
				"{\"x\": 8, \"y\": 0, \"sheet\": \"usr000.json\", \"index\": 1, \"pose\": \"pose002\"}]}");
			f.Write("kit/rec-001/kit.json", KitFixture.Kit(KitFixture.Part("sprites",
				"{\"path\": \"sheets/usr000.png\", \"title\": \"run — a 4-phase loop\", \"unit\": \"grid\", \"rows\": 1, \"columns\": 2, \"cells\": 2, " +
				"\"ids\": [\"pose001\", \"pose002\"], \"seen\": true, \"playsColumns\": [1, 2, 1, null], \"figure\": \"figures/usr000-figure.png\"}")));
		}

		//A red picture with one blue pixel at (x, y).
		private static byte[] PaintedPng(int width, int height, int x, int y)
		{
			byte[][] pixels = Enumerable.Range(0, width * height).Select(i => i == y * width + x ? Blue : Red).ToArray();
			return KitFixture.Png(width, height, pixels);
		}
	}
}
