using System.Linq;
using System.Text.Json;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Mep
{
	// PRD Phase 12 F12.2: the Tile/Tilemap/Sprite viewers' "Copy as MEP sheet cell"
	// puts one tile key on the clipboard as the JSON object `mep_build.py` reads
	// out of a sheet sidecar's `cells[].tiles[]` (ADR-0153, ADR-0172 §2). The
	// keys below are real recorded ones, so the pinned text is the text an author
	// really pastes:
	//   * Contra (CHR RAM)   - <roms>/Contra (1988) (Konami)/auto/textures/hires.txt
	//   * Zelda 1 (CHR RAM)  - <roms>/The Legend of Zelda (1987) (Nintendo)/auto/...
	//   * Super Mario Bros.  - a CHR ROM game: index-keyed (ADR-0172), the 32-hex
	//     `tile` read out of the ROM's CHR at index 2 (offset 32 of the CHR bank)
	//     with the `<tile>0,02,0F001030,...` key the bootstrap recorded next to it.
	public class MepSheetCellTests
	{
		private const string ContraTile = "003FFFFFFF7F84D10000000000807B2E";
		private const string ContraPalette = "0F192908";
		private const string ContraSpriteTile = "000007281F0B1B3B0000072F1F0C1C26";
		private const string ContraSpritePalette = "FF36160F";
		private const string ZeldaTile = "384CC6C6C66438000000000000000000";
		private const string ZeldaPalette = "0F300012";
		private const string SmbChrRomTile = "7F7FFFFF7F3F0F1378589C8943300C1F";
		private const string SmbPalette = "0F001030";

		[Fact]
		public void Format_ChrRamKey_EmitsTileAndPaletteOnly()
		{
			//Contra: CHR RAM, so there is no CHR index to record (ADR-0172 §2).
			Assert.Equal(
				"{\"tile\": \"" + ContraTile + "\", \"palette\": \"" + ContraPalette + "\"}",
				MepSheetCell.Format(ContraTile, ContraPalette));
		}

		[Fact]
		public void Format_ChrRamSpriteKey_KeepsTheTransparentColorZero()
		{
			//A sprite key's palette leads with FF - the transparent color 0 the
			//recorder writes (HdBuilderPpu) and the copy builds for a sprite.
			Assert.Equal(
				"{\"tile\": \"" + ContraSpriteTile + "\", \"palette\": \"" + ContraSpritePalette + "\"}",
				MepSheetCell.Format(ContraSpriteTile, ContraSpritePalette));
		}

		[Fact]
		public void Format_ChrRomKey_CarriesTheAbsoluteChrIndex()
		{
			Assert.Equal(
				"{\"tile\": \"" + SmbChrRomTile + "\", \"palette\": \"" + SmbPalette + "\", \"index\": 2}",
				MepSheetCell.Format(SmbChrRomTile, SmbPalette, 2));
		}

		[Fact]
		public void Format_IsJsonAnEditorCanPasteIntoATilesArray()
		{
			//The sidecar is JSON, so the text has to survive the parse the builder
			//runs - and the fields have to come back exactly as the sidecar's
			//readers (`mep_build._cell_crops`, `sheet_repaint`) expect them.
			using JsonDocument doc = JsonDocument.Parse(MepSheetCell.Format(SmbChrRomTile, SmbPalette, 155));
			JsonElement root = doc.RootElement;

			Assert.Equal(3, root.EnumerateObject().Count());
			Assert.Equal(SmbChrRomTile, root.GetProperty("tile").GetString());
			Assert.Equal(SmbPalette, root.GetProperty("palette").GetString());
			Assert.Equal(155, root.GetProperty("index").GetInt32());
		}

		[Fact]
		public void FormatCell_WrapsTheEntryInAnUnplacedCell()
		{
			//ADR-0216 OPEN 1(b): `count` + `tiles[]`, and no `index`/`x`/`y` of the
			//cell's own - those are the sheet's arithmetic, and `mep_add_cell.py`
			//does it. The `index` that IS here rides inside `tiles[]` and is the
			//tile's absolute CHR index (ADR-0172 §2); the two are the trap the ADR
			//names, so this pins which one survives.
			Assert.Equal(
				"{\"count\": 1, \"tiles\": [{\"tile\": \"" + SmbChrRomTile + "\", \"palette\": \"" + SmbPalette + "\", \"index\": 2}]}",
				MepSheetCell.FormatCell(SmbChrRomTile, SmbPalette, 2));

			using JsonDocument doc = JsonDocument.Parse(MepSheetCell.FormatCell(ContraTile, ContraPalette));
			JsonElement root = doc.RootElement;
			Assert.Equal(new[] { "count", "tiles" }, root.EnumerateObject().Select(p => p.Name).ToArray());
			Assert.Equal(1, root.GetProperty("count").GetInt32());
			JsonElement entry = Assert.Single(root.GetProperty("tiles").EnumerateArray().ToArray());
			Assert.Equal(new[] { "tile", "palette" }, entry.EnumerateObject().Select(p => p.Name).ToArray());
			Assert.Equal(ContraTile, entry.GetProperty("tile").GetString());
		}

		[Fact]
		public void FormatCell_KeyThatIsNotASheetCell_ReturnsEmpty()
		{
			//Same "leave the clipboard alone" signal as Format: a cell wrapped
			//around a key that cannot be one would paste and then fail to build.
			Assert.Equal("", MepSheetCell.FormatCell("31", ContraPalette));
			Assert.Equal("", MepSheetCell.FormatCell(ContraTile, "0G192908"));
		}

		[Fact]
		public void Format_LowercaseInput_IsUppercased()
		{
			//`mep_build` matches the key source case-insensitively but stores what
			//it is given, and every sidecar the Core writes is uppercase; the copy
			//lands in the same file, so it uses the same case.
			Assert.Equal(
				"{\"tile\": \"" + ZeldaTile + "\", \"palette\": \"" + ZeldaPalette + "\"}",
				MepSheetCell.Format(ZeldaTile.ToLowerInvariant(), ZeldaPalette.ToLowerInvariant(), MepSheetCell.UnknownIndex));
		}

		[Theory]
		//A CHR ROM `<tile>` line's short key ("31", "1A7") is not a sheet cell: the
		//sidecar stores 16 bytes of tile data, and the index rides in its own field.
		[InlineData("31", ContraPalette)]
		[InlineData("", ContraPalette)]
		[InlineData(ContraTile + "00", ContraPalette)]
		[InlineData(ContraTile, "0F19290")]
		[InlineData(ContraTile, "0F1929081")]
		[InlineData(ContraTile, "0G192908")]
		[InlineData(ContraTile, "0F 92908")]
		public void Format_KeyThatIsNotASheetCell_ReturnsEmpty(string tileData, string palette)
		{
			//Empty is the callers' "leave the clipboard alone" signal.
			Assert.Equal("", MepSheetCell.Format(tileData, palette));
		}
	}
}
