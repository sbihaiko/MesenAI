using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.7 (W-R5, the honesty surface): the popover's lines are the kit's own
	//facts and the twin comparison, or "cannot tell" - never a guess.
	public sealed class RemasterProvenanceTests
	{
		private static readonly byte[] Red = { 255, 0, 0, 255 };
		private static readonly byte[] Blue = { 0, 0, 255, 255 };

		[Fact]
		public void The_png_reader_round_trips_rgba_and_refuses_what_mep_build_refuses()
		{
			RemasterPixels? px = RemasterPng.Decode(KitFixture.Png(3, 2, Red));
			Assert.NotNull(px);
			Assert.Equal((3, 2, 4), (px!.Width, px.Height, px.Channels));
			Assert.Equal(Red, px.Data.Take(4).ToArray());
			Assert.Null(RemasterPng.Decode(new byte[] { 1, 2, 3 }));
			byte[] palette = KitFixture.Png(1, 1, Red);
			palette[8 + 8 + 9] = 3; //IHDR colour type 3 (palette): not decoded, as mep_build
			Assert.Null(RemasterPng.Decode(palette));
		}

		[Fact]
		public void An_untouched_sheet_equals_its_twin_upscaled_and_one_brushed_pixel_is_painted()
		{
			using KitFixture f = new();
			f.WritePair("kit/rec-001/sheets/usr000.png", 4, 2, 4, Red);
			string sheet = Path.Combine(f.Project, "kit/rec-001/sheets/usr000.png");
			string twin = Path.Combine(f.Project, "kit/rec-001/sheets/usr000.orig.png");
			Assert.Equal(RemasterPaintState.Untouched, RemasterPaintProbe.Compare(sheet, twin).State);

			f.Paint("kit/rec-001/sheets/usr000.png", 16, 8, Red, Blue);
			Assert.Equal(RemasterPaintState.Painted, RemasterPaintProbe.Compare(sheet, twin).State);
		}

		[Fact]
		public void A_missing_twin_or_a_size_mismatch_is_unknown_never_painted()
		{
			using KitFixture f = new();
			f.WritePair("a.png", 4, 2, 4, Red);
			string a = Path.Combine(f.Project, "a.png");
			Assert.Equal(RemasterPaintUnknown.NoTwin, RemasterPaintProbe.Compare(a, Path.Combine(f.Project, "none.png")).Why);
			File.WriteAllBytes(Path.Combine(f.Project, "b.orig.png"), KitFixture.Png(3, 3, Red));
			Assert.Equal(RemasterPaintUnknown.SizeMismatch, RemasterPaintProbe.Compare(a, Path.Combine(f.Project, "b.orig.png")).Why);
		}

		[Fact]
		public void A_figure_reads_seen_and_painted_when_only_its_composed_view_was_painted()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			f.Paint("kit/rec-001/figures/usr000-figure.png", 16, 8, Red, Blue);
			RemasterKitTile run = RemasterKitReader.Read(f.Project).Tiles[0];

			RemasterPaintResult paint = RemasterProvenance.Paint(run, RemasterPaintProbe.Compare);
			Assert.Equal(RemasterPaintState.Painted, paint.State);
			Assert.Equal(new[] { RemasterProvenanceKind.Seen, RemasterProvenanceKind.Painted },
				RemasterProvenance.Lines(run, paint).Select(l => l.Kind).ToArray());
		}

		[Fact]
		public void An_untouched_figure_says_not_painted_yet()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKitTile run = RemasterKitReader.Read(f.Project).Tiles[0];
			Assert.Equal(RemasterPaintState.Untouched, RemasterProvenance.Paint(run, RemasterPaintProbe.Compare).State);
		}

		[Fact]
		public void A_page_lists_seen_filled_and_empty_cells_and_its_paint_state_is_unknown()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKitTile page = RemasterKitReader.Read(f.Project).Tiles.First(t => t.Category == RemasterKitCategory.PatternPages);
			//The page's twin is not a pre-paint copy (the scale filter), so even
			//a picture equal to it is not called untouched.
			RemasterPaintResult paint = RemasterProvenance.Paint(page, RemasterPaintProbe.Compare);
			Assert.Equal(RemasterPaintUnknown.NoPrePaintCopy, paint.Why);

			RemasterProvenanceLine[] lines = RemasterProvenance.Lines(page, paint).ToArray();
			Assert.Equal((RemasterProvenanceKind.CellsSeen, 50, 64), (lines[0].Kind, lines[0].Count, lines[0].Of));
			Assert.Equal((RemasterProvenanceKind.CellsFilled, 12, 64), (lines[1].Kind, lines[1].Count, lines[1].Of));
			Assert.Equal((RemasterProvenanceKind.CellsEmpty, 2, 64), (lines[2].Kind, lines[2].Count, lines[2].Of));
			Assert.Equal(RemasterProvenanceKind.PaintUnknown, lines[3].Kind);
		}

		[Fact]
		public void A_fully_seen_page_has_no_fill_line()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKitTile page = RemasterKitReader.Read(f.Project).Tiles.Last();
			Assert.DoesNotContain(RemasterProvenance.Lines(page, RemasterPaintResult.Unknown(RemasterPaintUnknown.NoPrePaintCopy)),
				l => l.Kind == RemasterProvenanceKind.CellsFilled || l.Kind == RemasterProvenanceKind.CellsEmpty);
		}

		[Fact]
		public void Seen_false_outside_a_page_is_not_seen_and_no_seen_field_is_unknown()
		{
			RemasterKitTile tile = new(RemasterKitCategory.StageMaps, "panorama", "m", "m", "/x.png", "", "", "", "rec-001", 1, 1, 1, 0, null, -1, -1);
			Assert.Equal(RemasterProvenanceKind.SeenUnknown, RemasterProvenance.Lines(tile, RemasterPaintResult.Untouched)[0].Kind);
			Assert.Equal(RemasterProvenanceKind.NotSeen, RemasterProvenance.Lines(tile with { Seen = false }, RemasterPaintResult.Untouched)[0].Kind);
		}

		[Fact]
		public void An_imported_sheet_says_it_came_from_the_imported_pack()
		{
			RemasterKitTile tile = new(RemasterKitCategory.Imported, "imported", "s", "s", "/x.png", "/x.orig.png", "", "", "", 0, 0, 0, 0, null, -1, -1);
			RemasterPaintResult paint = RemasterProvenance.Paint(tile, (_, _) => RemasterPaintResult.Painted);
			Assert.Equal(RemasterPaintUnknown.NoPrePaintCopy, paint.Why);
			Assert.Equal(RemasterProvenanceKind.FromImportedPack, RemasterProvenance.Lines(tile, paint)[0].Kind);
		}

		[Theory]
		[InlineData("<patch>fix.ips,0123456789ABCDEF0123456789ABCDEF01234567", true)]
		[InlineData("[cond]<patch>fix.ips,0123456789ABCDEF0123456789ABCDEF01234567", true)]
		[InlineData("<tile>1,00,FF000000,0,0,1,N", false)]
		public void A_project_paints_a_patched_game_when_a_manifest_carries_a_patch_line(string line, bool patched)
		{
			using KitFixture f = new();
			f.Write("auto/textures/hires.txt", "<ver>106\n" + line + "\n");
			Assert.Equal(patched, RemasterProvenance.PaintsPatchedGame(f.Project));
		}

		[Fact]
		public void A_numbered_recordings_patch_line_counts_and_a_scratch_folders_does_not()
		{
			using KitFixture f = new();
			const string patch = "<ver>106\n<patch>fix.ips,0123456789ABCDEF0123456789ABCDEF01234567\n";
			f.Write("auto/scratch/textures/hires.txt", patch);
			Assert.False(RemasterProvenance.PaintsPatchedGame(f.Project));
			f.Write("auto/rec-002/textures/hires.txt", patch);
			Assert.True(RemasterProvenance.PaintsPatchedGame(f.Project));
		}
	}
}
