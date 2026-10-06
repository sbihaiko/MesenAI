using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//ADR-0219 (G.7 follow-up): which cells of a pattern page the thumbnail
	//marks, and where they land. Every fact comes from the page's sidecar and
	//the picture's own header; a file that cannot tell leaves the thumbnail
	//alone. Host-free: no Avalonia, no core.
	public class RemasterPageMarksTests
	{
		private static readonly byte[] Magenta = { 255, 0, 255, 255 };

		private static RemasterKitTile Page(string imagePath, int cells = 256, int fill = 250, int empty = 6)
		{
			return new RemasterKitTile(RemasterKitCategory.PatternPages, "page", "Chr_0", "Chr_0 — CHR bank 1",
				imagePath, "", "", "", "", cells, 16, 16, 0, false, fill, empty);
		}

		//A page picture `side x side` px (16 cells a row, as the kit writes) and
		//the sidecar beside it, holding exactly the cells given.
		private static string WritePage(string folder, int side, int gridUnit, int scale, params string[] cells)
		{
			string chr = Path.Combine(folder, "kit", "pages", "chr");
			Directory.CreateDirectory(chr);
			string png = Path.Combine(chr, "Chr_0.png");
			File.WriteAllBytes(png, KitFixture.Png(side, side, Magenta));
			File.WriteAllText(Path.Combine(chr, "Chr_0.json"),
				"{\"version\": 1, \"kind\": \"chr\", \"gridUnit\": " + gridUnit + ", \"scale\": " + scale
				+ ", \"columns\": 16, \"rows\": 16, \"cells\": [" + string.Join(",", cells) + "]}");
			return png;
		}

		//One sidecar cell at its slot's own x/y, `gridUnit * scale` apart.
		private static string Cell(int slot, string state, int gridUnit = 8, int scale = 1, string? extra = null)
		{
			int cell = gridUnit * scale;
			StringBuilder sb = new("{\"slot\": " + slot + ", \"index\": " + slot
				+ ", \"x\": " + (slot % 16) * cell + ", \"y\": " + (slot / 16) * cell
				+ (extra ?? "") + (state.Length > 0 ? ", \"state\": \"" + state + "\"" : "") + "}");
			return sb.ToString();
		}

		private static string Temp()
		{
			string folder = Path.Combine(Path.GetTempPath(), "mesen-911-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(folder);
			return folder;
		}

		[Fact]
		public void A_pages_fill_and_empty_cells_land_on_the_thumbnail_in_its_own_pixels()
		{
			string folder = Temp();
			try {
				//A 128 x 128 page (16 cells a row at 8 px), 12 recorded cells
				//donated by another recording, one empty, one fill.
				string png = WritePage(folder, 128, 8, 1,
					Cell(0, "fill"), Cell(1, "evidence"), Cell(2, "empty"), Cell(255, "fill"));
				RemasterKitTile tile = Page(png, cells: 4, fill: 2, empty: 1);

				RemasterPageThumb thumb = Assert.IsType<RemasterPageThumb>(RemasterPageMarks.Thumbnail(tile, 64));

				//The thumbnail is the picture's own aspect, 64 px tall, and a
				//cell 8 px wide in a 128 px page is 4 px here.
				Assert.Equal(64d, thumb.Width);
				Assert.Equal(64d, thumb.Height);
				Assert.Equal(new[] {
					new RemasterPageMark(0, 0, 4, RemasterPageCellKind.Fill),
					new RemasterPageMark(8, 0, 4, RemasterPageCellKind.Empty),
					new RemasterPageMark(60, 60, 4, RemasterPageCellKind.Fill),
				}, thumb.Marks.ToArray());

				//Half the height, half the cells: nothing is hard-coded to 64.
				RemasterPageThumb half = Assert.IsType<RemasterPageThumb>(RemasterPageMarks.Thumbnail(tile, 32));
				Assert.Equal(new[] {
					new RemasterPageMark(0, 0, 2, RemasterPageCellKind.Fill),
					new RemasterPageMark(4, 0, 2, RemasterPageCellKind.Empty),
					new RemasterPageMark(30, 30, 2, RemasterPageCellKind.Fill),
				}, half.Marks.ToArray());
			} finally {
				Directory.Delete(folder, true);
			}
		}

		[Fact]
		public void Only_the_two_states_the_kit_writes_as_provenance_are_marked()
		{
			string folder = Temp();
			try {
				//`borrowed`, `donated` and `folded` were seen in play, so the
				//artist must not read them as "not from play"; a token this
				//reader does not know is "cannot tell", never a guess.
				string png = WritePage(folder, 128, 8, 1,
					Cell(0, "evidence"), Cell(1, "borrowed"), Cell(2, "donated"), Cell(3, "folded"),
					Cell(4, "fill"), Cell(5, "mystery"), Cell(6, ""));
				RemasterKitTile tile = Page(png, cells: 7);

				RemasterPageThumb thumb = Assert.IsType<RemasterPageThumb>(RemasterPageMarks.Thumbnail(tile, 64));

				RemasterPageMark mark = Assert.Single(thumb.Marks);
				//slot 4 sits at x = 32 page px, which is 16 px in a 64 px
				//thumbnail of a 128 px page.
				Assert.Equal(new RemasterPageMark(16, 0, 4, RemasterPageCellKind.Fill), mark);
			} finally {
				Directory.Delete(folder, true);
			}
		}

		[Fact]
		public void A_static_kits_page_dims_every_cell_the_kit_filled()
		{
			string folder = Temp();
			try {
				//ADR-0219: a kit projected over the ROM alone is every cell
				//`fill`, `seen: false` - and it is the fragment's own `fill`
				//count that has to match what the thumbnail marks.
				List<string> cells = new();
				for(int slot = 0; slot < 256; slot++) {
					cells.Add(Cell(slot, "fill", gridUnit: 8, scale: 4, extra: ", \"palette\": \"0F001030\", \"origin\": \"prg\""));
				}
				string png = WritePage(folder, 512, 8, 4, cells.ToArray());
				RemasterKitTile tile = Page(png, cells: 256, fill: 256, empty: 0);

				RemasterPageThumb thumb = Assert.IsType<RemasterPageThumb>(RemasterPageMarks.Thumbnail(tile, 64));

				Assert.Equal(256, thumb.Marks.Count);
				Assert.All(thumb.Marks, m => Assert.Equal(RemasterPageCellKind.Fill, m.Kind));
				Assert.All(thumb.Marks, m => Assert.Equal(4d, m.Size));
				Assert.Equal(16, thumb.Marks.Select(m => m.X).Distinct().Count());
				Assert.Equal(16, thumb.Marks.Select(m => m.Y).Distinct().Count());
				Assert.Equal(tile.Fill, thumb.Marks.Count(m => m.Kind == RemasterPageCellKind.Fill));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		[Fact]
		public void A_page_with_no_sidecar_is_cannot_tell_and_stays_undimmed()
		{
			string folder = Temp();
			try {
				string chr = Path.Combine(folder, "kit", "pages", "chr");
				Directory.CreateDirectory(chr);
				string png = Path.Combine(chr, "Chr_0.png");
				File.WriteAllBytes(png, KitFixture.Png(128, 128, Magenta));
				//The fragment says 12 fills; no sidecar says which 12, so no
				//cell is marked and the fragment's count is never used as a
				//substitute (ADR-0252: a count is not a provenance).
				RemasterKitTile tile = Page(png);

				Assert.Null(RemasterPageMarks.Thumbnail(tile, 64));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		[Fact]
		public void A_picture_whose_size_no_file_gives_is_cannot_tell()
		{
			string folder = Temp();
			try {
				string png = WritePage(folder, 128, 8, 1, Cell(0, "fill"));
				File.Delete(png);
				RemasterKitTile tile = Page(png);

				Assert.Null(RemasterPageMarks.Thumbnail(tile, 64));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		[Fact]
		public void A_sheets_sidecar_never_marks_a_page()
		{
			string folder = Temp();
			try {
				//A figure grid's sidecar beside its own picture has the same
				//`cells[]` shape and its own states; it is no page sidecar.
				string sheets = Path.Combine(folder, "kit", "rec-001", "sheets");
				Directory.CreateDirectory(sheets);
				string png = Path.Combine(sheets, "usr000.png");
				File.WriteAllBytes(png, KitFixture.Png(64, 64, Magenta));
				File.WriteAllText(Path.Combine(sheets, "usr000.json"),
					"{\"version\": 1, \"gridUnit\": 8, \"cells\": [" + Cell(0, "fill") + "]}");
				RemasterKitTile figure = new(RemasterKitCategory.Figures, "grid", "run", "run", png, "", "", "", "rec-001", 1, 1, 1, 0, true, -1, -1);

				Assert.Null(RemasterPageMarks.Thumbnail(figure, 64));

				//And a page-shaped sidecar that names another kind of surface.
				string chr = Path.Combine(folder, "kit", "pages", "chr");
				Directory.CreateDirectory(chr);
				string pagePng = Path.Combine(chr, "Chr_0.png");
				File.WriteAllBytes(pagePng, KitFixture.Png(128, 128, Magenta));
				File.WriteAllText(Path.Combine(chr, "Chr_0.json"),
					"{\"version\": 1, \"kind\": \"figure\", \"gridUnit\": 8, \"cells\": [" + Cell(0, "fill") + "]}");

				Assert.Null(RemasterPageMarks.Thumbnail(Page(pagePng), 64));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		[Fact]
		public void The_pictures_own_header_is_what_sizes_a_cell()
		{
			//A page PNG the header reader can size and a file that is no PNG.
			string folder = Temp();
			try {
				string png = WritePage(folder, 320, 8, 2, Cell(0, "fill", gridUnit: 8, scale: 2));
				Assert.Equal((320, 320), RemasterPageMarks.PictureSize(png));
				Assert.Equal((0, 0), RemasterPageMarks.PictureSize(Path.Combine(folder, "nope.png")));
				File.WriteAllText(Path.Combine(folder, "text.png"), "not a png");
				Assert.Equal((0, 0), RemasterPageMarks.PictureSize(Path.Combine(folder, "text.png")));
			} finally {
				Directory.Delete(folder, true);
			}
		}
	}
}
