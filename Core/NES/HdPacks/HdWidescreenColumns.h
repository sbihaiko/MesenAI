#pragma once
#include "pch.h"
#include "NES/HdPacks/HdData.h"
#include "NES/HdPacks/HdWidescreenGeometry.h"
#include "NES/NesWidescreenReveal.h"

//ADR-0253 slice W.4: the Reveal's extra columns as the HD pack sees them.
//
//The sides are the picture's own neighbors (NesWidescreenReveal), so the pack
//has to be asked about them the way it is asked about a centered pixel - the same
//tile key, the same ROM color behind a tile it has no rule for - and the answer
//has to be at the pack's own scale. BuildSideTiles is the bridge: it reads one
//HdSideTile per 8-px tile of one row straight from the row's scroll basis, using
//only the mapper's side-effect-free VRAM read, and the renderer draws those
//tiles through the ordinary per-pixel pipeline (HdNesPack::DrawWidescreenColumns).
//
//Every value it stores is the value HdNesPpu::DrawPixel stores for a centered
//pixel, so a `<tile>` rule cannot tell a side pixel from a center one - which is
//the whole point of the slice. One deliberate difference: grayscale and emphasis
//are not folded into the stored colors here, because the HD path applies them to
//the finished RGB row (ProcessGrayscaleAndEmphasis), exactly as it does for the
//center.
namespace HdWidescreenColumns
{
	//The row HdScreenInfo::SideTiles stores must be the row this header walks: one
	//row of storage is [left TilesPerSide][right TilesPerSide], and a mismatch
	//would silently read one tile too far on a row whose fine X is not 0.
	static_assert(HdSideTilesPerSide == TilesPerSide && HdSideTilesPerRow == TilesPerRow,
		"ADR-0253 W.4: HdData.h's side-tile row must match HdWidescreenGeometry.h's");

	//Fills one row's side tiles: [left TilesPerSide][right TilesPerSide].
	//`mapper` is a BaseMapper, or anything with DebugReadVram(uint16_t) (the
	//side-effect-free read), GetPpuAbsoluteAddress(uint32_t) and
	//CopyChrTile(uint32_t, uint8_t*) - the three calls HdNesPpu makes for a
	//centered tile.
	template<typename Mapper>
	void BuildSideTiles(const NesWidescreenReveal::RowBasis& basis, MirroringType mirroring, Mapper& mapper, const uint8_t* paletteRam, bool isChrRam, uint32_t packVersion, HdSideTile* tiles)
	{
		uint16_t originX = NesWidescreenReveal::RowOriginX(basis);
		bool hasContent = NesWidescreenReveal::SideColumnsHaveContent(mirroring, originX);
		uint16_t coarseY = (uint16_t)((basis.VideoRamAddr >> 5) & 0x1F);
		uint16_t nametableY = (uint16_t)((basis.VideoRamAddr >> 11) & 0x01);
		uint16_t fineY = (uint16_t)((basis.VideoRamAddr >> 12) & 0x07);
		uint8_t backdrop = paletteRam[0];

		for(uint32_t i = 0; i < TilesPerRow; i++) {
			HdSideTile& side = tiles[i];
			side = HdSideTile();
			side.XScroll = basis.FineX;
			side.HasContent = hasContent;
			side.Tile.PpuBackgroundColor = backdrop;
			if(!hasContent) {
				//ADR-0253 §3's black fallback, in the pack's own vocabulary: a
				//tile the renderer paints black and never looks up. It is not a
				//color the game chose, so it is not run through grayscale or
				//emphasis either - exactly as W.1 leaves it.
				side.Tile.TileIndex = HdPpuTileInfo::NoTile;
				for(uint32_t p = 0; p < 8; p++) {
					side.BgColor[p] = (uint8_t)NesWidescreenReveal::BlackColor;
				}
				continue;
			}
			if(!basis.BgEnabled) {
				//The row's background is off: the low-res Reveal shows the
				//backdrop there, so the pack is handed a pixel with no tile and
				//paints the same backdrop through its ordinary pipeline.
				side.Tile.TileIndex = HdPpuTileInfo::NoTile;
				for(uint32_t p = 0; p < 8; p++) {
					side.BgColor[p] = backdrop;
				}
				continue;
			}

			uint16_t planeOrigin = SideTilePlaneOrigin(i, originX);
			uint16_t coarseX = (uint16_t)((planeOrigin >> 3) & 0x1F);
			uint16_t nametableBase = (uint16_t)(0x2000 | (((nametableY << 1) | ((planeOrigin >> 8) & 0x01)) << 10));

			//The same four reads NesWidescreenReveal::RenderRowSides makes for
			//this tile, through the same side-effect-free path.
			uint8_t tileIndex = mapper.DebugReadVram((uint16_t)(nametableBase | (coarseY << 5) | coarseX));
			uint8_t attribute = mapper.DebugReadVram((uint16_t)(nametableBase | 0x3C0 | ((coarseY >> 2) << 3) | (coarseX >> 2)));
			uint8_t shift = (uint8_t)(((coarseY & 0x02) << 1) | (coarseX & 0x02));
			uint8_t tilePalette = (uint8_t)(((attribute >> shift) & 0x03) << 2);

			uint16_t tileAddr = (uint16_t)(basis.BgPatternAddr | (tileIndex << 4) | fineY);
			uint32_t absolute = (uint32_t)mapper.GetPpuAbsoluteAddress(tileAddr).Address;

			HdPpuTileInfo& info = side.Tile;
			info.IsChrRamTile = isChrRam;
			info.TileIndex = (int32_t)(absolute / 16);
			info.OffsetY = (uint8_t)fineY;
			if(isChrRam) {
				mapper.CopyChrTile(absolute & 0xFFFFFFF0, info.TileData);
			}
			//HdNesPpu::DrawPixel's own palette key, alpha byte and all.
			if(packVersion >= 100) {
				info.PaletteColors = (uint32_t)paletteRam[tilePalette + 3] | ((uint32_t)paletteRam[tilePalette + 2] << 8) |
					((uint32_t)paletteRam[tilePalette + 1] << 16) | ((uint32_t)paletteRam[0] << 24);
			} else {
				info.PaletteColors = (uint32_t)paletteRam[tilePalette + 3] | ((uint32_t)paletteRam[tilePalette + 2] << 8) |
					((uint32_t)paletteRam[tilePalette + 1] << 16);
			}

			uint8_t low = mapper.DebugReadVram(tileAddr);
			uint8_t high = mapper.DebugReadVram((uint16_t)(tileAddr + 8));
			for(uint32_t p = 0; p < 8; p++) {
				uint8_t bit = (uint8_t)(7 - p);
				uint8_t color = (uint8_t)(((low >> bit) & 0x01) | (((high >> bit) & 0x01) << 1));
				side.BgColorIndex[p] = color;
				side.BgColor[p] = color ? paletteRam[tilePalette + color] : backdrop;
			}
		}
	}

	//One side's row of output pixels, in the PPU output buffer's format (palette
	//index in bits 0-5, emphasis in 6-8) - the format, and the pixels,
	//NesWidescreenReveal::RenderRowSides produces. HdNesPpu fills the widened
	//RenderedFrame ADR-0253 §2 promises from the very tiles the HD renderer draws,
	//so a consumer that is not the HD filter - the border layer, which takes the
	//center, a screenshot, the recorder - sees W.1's picture, not a second one
	//computed a different way. `sideRow` is one side of a row of
	//HdScreenInfo::SideTiles (HdWidescreenColumns::TilesPerSide of them).
	//
	//An empty column is the black fallback *unmasked*: it is not a color the game
	//chose, so grayscale and emphasis do not touch it. Every other pixel is the
	//ROM's own color, which they do.
	inline void SideTilesToLowResRow(const HdSideTile* sideRow, uint8_t xScroll, uint8_t paletteMask, uint16_t emphasisBits, uint16_t* output)
	{
		for(uint32_t m = 0; m < ExtraColumns; m++) {
			uint32_t tile = 0;
			uint32_t pixel = 0;
			SideColumnToTile(xScroll, m, tile, pixel);
			const HdSideTile& sideTile = sideRow[tile];
			output[m] = sideTile.HasContent ? (uint16_t)((sideTile.BgColor[pixel] & paletteMask) | emphasisBits) : (uint16_t)NesWidescreenReveal::BlackColor;
		}
	}

	//One side pixel's per-pixel record, in the shape HdNesPpu::DrawPixel fills for
	//a centered pixel - what HdNesPack::GetPixels reads. `pixel` is the column of
	//the side tile this output column draws, i.e. the second value
	//SideColumnToTile handed back.
	//
	//`OffsetX` is the one field that is not simply the tile's: it is which column
	//of the pack's bitmap the pixel samples, and DrawTile indexes the art with it.
	//A side tile covers eight output columns and each must sample its own, so this
	//is the field that makes the sides a picture rather than eight copies of the
	//tile's leftmost column.
	inline void BuildSidePixelInfo(const HdSideTile& sideTile, uint32_t pixel, HdPpuPixelInfo& out)
	{
		out.Tile = sideTile.Tile;
		out.Tile.OffsetX = (uint8_t)pixel;
		out.Tile.BgColorIndex = sideTile.BgColorIndex[pixel];
		out.Tile.BgColor = sideTile.BgColor[pixel];
		out.SpriteCount = 0;
	}
}
