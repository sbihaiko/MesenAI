using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.7 (W-R1 zone ②, ADR-0183 §2, ADR-0194): the tiles come from the kits
	//mep_project.py kit writes, in reading order, with the generators' own
	//captions and counts and nothing inferred.
	public sealed class RemasterKitReaderTests
	{
		[Fact]
		public void A_project_without_a_kit_has_no_tiles()
		{
			using KitFixture f = new();
			RemasterKit kit = RemasterKitReader.Read(f.Project);
			Assert.Empty(kit.Tiles);
			Assert.Empty(kit.Problems);
		}

		[Fact]
		public void Tiles_are_read_per_recording_and_from_the_union_pages_in_reading_order()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKit kit = RemasterKitReader.Read(f.Project);

			Assert.Equal(new[] {
				RemasterKitCategory.Figures, RemasterKitCategory.Figures,
				RemasterKitCategory.Scenery,
				RemasterKitCategory.PatternPages, RemasterKitCategory.PatternPages
			}, kit.Tiles.Select(t => t.Category).ToArray());
			Assert.Equal(new[] { "rec-001", "rec-002", "rec-001", "", "" }, kit.Tiles.Select(t => t.RecordingId).ToArray());
			Assert.Equal(2, kit.Count(RemasterKitCategory.Figures));
			Assert.Equal(0, kit.Count(RemasterKitCategory.StageMaps));
		}

		[Fact]
		public void A_figure_carries_its_caption_phases_twin_and_composed_view()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKitTile run = RemasterKitReader.Read(f.Project).Tiles[0];

			Assert.Equal("run", run.Caption);
			Assert.StartsWith("run — a 6-phase loop", run.Title);
			Assert.Equal(6, run.Phases);
			Assert.Equal(true, run.Seen);
			Assert.Equal(Path.Combine(f.Project, "kit", "rec-001", "sheets", "usr000.png"), run.ImagePath);
			Assert.Equal(Path.Combine(f.Project, "kit", "rec-001", "sheets", "usr000.orig.png"), run.ReferencePath);
			Assert.Equal(Path.Combine(f.Project, "kit", "rec-001", "figures", "usr000-figure.png"), run.FigurePath);
			Assert.EndsWith("usr000-figure.orig.png", run.FigureReferencePath);
			Assert.True(run.HasPrePaintTwin);
		}

		[Fact]
		public void A_page_carries_its_fill_and_empty_counts_and_seen_is_what_is_left()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKitTile page = RemasterKitReader.Read(f.Project).Tiles.First(t => t.Category == RemasterKitCategory.PatternPages);

			Assert.Equal("Chr_0", page.Caption);
			Assert.Equal(64, page.Cells);
			Assert.Equal(12, page.Fill);
			Assert.Equal(2, page.Empty);
			//40 recorded + 9 donated + 1 folded: artist_chr_kit's own totals.
			Assert.Equal(50, page.SeenCells);
			Assert.False(page.HasPrePaintTwin);
		}

		[Fact]
		public void A_file_record_without_seen_reads_as_unknown_not_as_seen()
		{
			using KitFixture f = new();
			f.Write("kit/rec-001/kit.json", KitFixture.Kit(KitFixture.Part("map", "{\"path\": \"map/stage1-000.png\", \"title\": \"stage1 — horizontal panorama\", \"unit\": \"panorama\", \"cells\": 8640}")));
			RemasterKitTile map = Assert.Single(RemasterKitReader.Read(f.Project).Tiles);
			Assert.Equal(RemasterKitCategory.StageMaps, map.Category);
			Assert.Null(map.Seen);
			Assert.Equal("", map.ReferencePath);
		}

		[Fact]
		public void Folders_that_are_not_a_recording_or_pages_and_paths_leaving_the_kit_are_ignored()
		{
			using KitFixture f = new();
			f.Write("kit/scratch/kit.json", KitFixture.Kit(KitFixture.Part("chr", "{\"path\": \"chr/Chr_0.png\"}")));
			f.Write("kit/rec-001/kit.json", KitFixture.Kit(KitFixture.Part("chr", "{\"path\": \"../../outside.png\"}", "{\"path\": \"/abs.png\"}")));
			Assert.Empty(RemasterKitReader.Read(f.Project).Tiles);
		}

		[Fact]
		public void An_unreadable_kit_is_a_problem_and_the_other_kits_still_read()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			f.Write("kit/rec-002/kit.json", "{not json");
			RemasterKit kit = RemasterKitReader.Read(f.Project);
			Assert.Single(kit.Problems);
			Assert.Contains("rec-002", kit.Problems[0]);
			Assert.Equal(4, kit.Tiles.Count);
		}

		[Fact]
		public void An_imported_project_lists_its_cut_sheets_and_never_their_twins()
		{
			using KitFixture f = new();
			f.Write("IMPORT.md", "# Imported from a legacy HD pack\n");
			f.WritePair("textures/sheets/Sprites.png", 2, 2, 2, new byte[] { 0, 0, 255, 255 });
			RemasterKitTile tile = Assert.Single(RemasterKitReader.Read(f.Project).Tiles);
			Assert.Equal(RemasterKitCategory.Imported, tile.Category);
			Assert.Equal("Sprites", tile.Caption);
			Assert.Null(tile.Seen);
		}

		[Fact]
		public void Sheets_at_the_root_of_a_recorded_project_are_not_imports()
		{
			using KitFixture f = new();
			f.WritePair("textures/sheets/usr000.png", 2, 2, 2, new byte[] { 0, 0, 255, 255 });
			Assert.Empty(RemasterKitReader.Read(f.Project).Tiles);
		}

		[Theory]
		[InlineData("cycle000 — a 4-phase loop, seen 8 time(s)", "cycle000")]
		[InlineData("obj000 (10 cells)", "obj000 (10 cells)")]
		[InlineData("green soldier — one figure, seen in 3 frame(s)", "green soldier")]
		public void The_caption_is_the_titles_head(string title, string caption)
		{
			Assert.Equal(caption, RemasterKitReader.CaptionOf(title));
		}
	}
}
