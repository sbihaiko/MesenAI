#pragma once
#include "pch.h"
#include "NES/NesTypes.h"
#include "NES/NesWidescreenReveal.h"

//ADR-0253 slice W.4: where the widescreen Reveal's extra columns are in the
//coordinates the HD pack pipeline speaks, and how wide the HD frame gets once
//they are part of it.
//
//The picture keeps its own coordinates: a center pixel is still (0,0)-(255,239)
//for every `<tile>` rule, `<background>` bound and condition, so what a pack
//draws over the 256-px picture is what it drew before the Reveal existed. The
//extra columns live *outside* that box - the left side is x = -64..-1 and the
//right side x = 256..319 - so widening the picture never renumbers a pixel the
//pack already knows about, and a rule that reads a position can tell "not in the
//picture" (IsPicturePixel) from "in it" instead of wrapping onto an unrelated
//center pixel.
//
//Host-free on purpose: scripts/core_unit_tests.cpp drives it with the same fake
//mapper the W.1 cases use.
namespace HdWidescreenColumns
{
	constexpr uint32_t ExtraColumns = NesWidescreenReveal::ExtraColumns;
	constexpr uint32_t PictureWidth = NesWidescreenReveal::StandardWidth;
	constexpr uint32_t PictureHeight = NesWidescreenReveal::Height;

	//A side is NesWidescreenReveal::ExtraColumns = 64 px = 8 whole tiles, plus
	//one: the sides start at the row's own fine X inside a tile, so a fine X
	//that is not 0 makes them reach one tile further at the end. (The ADR's
	//"8 whole tiles" is about the 64-px width; a row whose fine X is not 0 shows
	//the last 8-fineX pixels of one tile and the first fineX of the next.)
	constexpr uint32_t TilesPerSide = ExtraColumns / 8 + 1;
	constexpr uint32_t TilesPerRow = TilesPerSide * 2;

	//Whether (x, y) is a pixel of the 256x240 picture - the only pixels a pack's
	//own rules were written for. A rule that reads a position at a side pixel
	//gets `false` rather than a wrap-around read of some unrelated center pixel:
	//the failure mode is no improvement (the ROM's own tile draws), never a hole.
	inline bool IsPicturePixel(int32_t x, int32_t y)
	{
		return (uint32_t)x < PictureWidth && (uint32_t)y < PictureHeight;
	}

	//The picture-relative x of column `column` (0..ExtraColumns-1) of one side.
	//Left: -64..-1, right: 256..319. The center's own coordinates never move.
	inline int32_t ExtraColumnX(bool right, uint32_t column)
	{
		return right ? (int32_t)(PictureWidth + column) : (int32_t)column - (int32_t)ExtraColumns;
	}

	//Which side tile an output column is drawn from, and which of its 8 pixels:
	//a side starts at the row's fine X *inside* its first tile, so column m is
	//tile (fineX + m) / 8, pixel (fineX + m) % 8 - the same walk
	//NesWidescreenReveal::RenderRowSides makes over the plane.
	inline void SideColumnToTile(uint8_t fineX, uint32_t column, uint32_t& tile, uint32_t& pixel)
	{
		uint32_t local = (uint32_t)fineX + column;
		tile = local >> 3;
		pixel = local & 0x07;
	}

	//Where side tile `tileIndex` (0..TilesPerRow-1, left side first) sits in the
	//512-px plane the two horizontally adjacent nametables form (0-511), for a
	//row whose picture starts at `rowOriginX` (NesWidescreenReveal::RowOriginX).
	//The tiles are 8-aligned in the plane, which is what makes both sides start
	//the same distance inside their first tile.
	inline uint16_t SideTilePlaneOrigin(uint32_t tileIndex, uint16_t rowOriginX)
	{
		uint16_t base = (uint16_t)(rowOriginX & 0xFFF8);
		int32_t offset = tileIndex < TilesPerSide ? -(int32_t)ExtraColumns : (int32_t)PictureWidth;
		int32_t within = (int32_t)(tileIndex % TilesPerSide) * 8;
		return (uint16_t)((base + offset + within) & 0x01FF);
	}

	//The HD frame a pack has to fill, once the Reveal's sides are part of it.
	//The sides are added around the picture and the overscan keeps cropping the
	//picture exactly as it does today, so the center of an extended HD frame is
	//the frame the pack would have produced without the Reveal - and a recorded
	//`<background>` and ADR-0236's cell mask, both written against 256 px, keep
	//meaning what they meant. What that costs is a seam of overscan.Left/Right
	//pixels on each side, which is what W.3's `<widescreen>` pack art is for.
	struct HdFrameGeometry
	{
		//Per side, in picture pixels: 0 or NesWidescreenReveal::ExtraColumns.
		uint32_t ExtraColumns = 0;
		//HD pixels in one output row.
		uint32_t ScreenWidth = 0;
		//HD pixels between the start of one emulated row and the next.
		uint32_t RowStride = 0;
		//HD pixels from the row's first pixel to the picture's x = overscanLeft.
		uint32_t CentreOffset = 0;
	};

	//The arithmetic HdNesPack::Process used to do inline. With `extended` false
	//it is that arithmetic, bit for bit: no sides, no offset, the same width.
	inline HdFrameGeometry ComputeHdFrameGeometry(bool extended, uint32_t scale, uint32_t overscanLeft, uint32_t overscanRight)
	{
		HdFrameGeometry geometry;
		geometry.ExtraColumns = extended ? ExtraColumns : 0;
		geometry.ScreenWidth = (PictureWidth + 2 * geometry.ExtraColumns - overscanLeft - overscanRight) * scale;
		geometry.RowStride = geometry.ScreenWidth * scale;
		geometry.CentreOffset = geometry.ExtraColumns * scale;
		return geometry;
	}

	//ADR-0253 §3 (W.3) through the HD path (W.4): the HD frame's own per-row
	//side-fill map, from the console frame's.
	//
	//The rows are the console's own 240, each drawn `scale` times and with the
	//overscan's top rows cropped off (HdNesPack::Process walks the console rows
	//overscan.Top .. 240 - overscan.Bottom and expands each to `scale` output
	//rows), so HD row r is console row overscanTop + r / scale. Nothing is
	//interpolated: one fill byte per row is what the fallback chain reads, and a
	//row the game could not fill stays that row's answer at every scale.
	//
	//False - and `out` untouched - when the map cannot describe the frame: a
	//missing console map, a scale of 0, or an `outRows` that reaches past the
	//console frame. The caller then hands the renderer no extension at all,
	//which is the safe answer (the chain does not run) rather than a lie about
	//which rows are filled.
	inline bool ScaleSideFill(const uint8_t* consoleFill, uint32_t consoleRows, uint32_t overscanTop,
		uint32_t scale, uint8_t* out, uint32_t outRows)
	{
		if(!consoleFill || !out || scale == 0 || outRows == 0 || overscanTop >= consoleRows) {
			return false;
		}
		if(outRows > (consoleRows - overscanTop) * scale) {
			return false;
		}
		for(uint32_t r = 0; r < outRows; r++) {
			out[r] = consoleFill[overscanTop + r / scale];
		}
		return true;
	}
}
