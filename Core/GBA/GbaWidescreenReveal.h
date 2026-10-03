#pragma once
#include "pch.h"

//ADR-0253 slice W.7: the GBA "Reveal" widescreen mode for text-mode
//backgrounds. With the WideScrn switch on, the PPU draws ExtraColumns more
//pixels on each side of every row, taken from the text BGs' own tilemaps -
//the map columns the 240-px window does not show - and sends a RenderedFrame
//that is ExtendedWidth wide (RenderedFrame::ExtendedColumns says how many of
//its columns on each side are extra).
//
//Everything here is presentation only:
//- VRAM and the palette are plain arrays to this file, read through the
//  console's own pointers and never written (const pointers), so no bus hook
//  fires and the debugger's memory-access log does not move - the same
//  side-effect-free path the debugger viewers take;
//- sprites, the OBJ window and every CPU-visible register stay on the
//  original 240 px - this file never touches them;
//- the standard 240x160 frame the PPU keeps (_currentBuffer, thumbnails, the
//  debugger) is never written to: the centre of the extended frame is a copy
//  of it.
//
//What each console can put beside its picture is ADR-0253's table. On the
//GBA only the text BGs (BG modes 0 and 1) have a map to read: an affine BG
//or a bitmap mode has no map the picture does not already compute, so those
//sides stay black. A text BG whose map wraps inside the window (a 256-wide
//map shows 240 of its 256 columns) puts the picture's own edge beside it,
//the SMB3 case of ADR-0253 §3, so that column falls back to black too.
//
//Host-free on purpose: scripts/core_unit_tests.cpp drives it with a plain
//VRAM/palette pair.
namespace GbaWidescreenReveal
{
	constexpr uint32_t StandardWidth = 240;
	constexpr uint32_t Height = 160;

	//N = 22 extra columns per side (ADR-0253 §2, the frame-width contract):
	//284 = 240 + 44 px. The GBA is already 3:2 and its pixels are square, so
	//16:9 needs 160 * 16 / 9 = 284.4 px: N = 22 lands 0.2 % short, N = 23
	//overshoots by 0.5 %. Unlike the NES there is no attribute group to keep
	//whole, so the exact fit wins over tile alignment.
	constexpr uint32_t ExtraColumns = 22;
	constexpr uint32_t ExtendedWidth = StandardWidth + 2 * ExtraColumns;

	//ADR-0253 §3's last fallback, and the fill for a mode that has no text
	//BG at all: BGR555 black.
	constexpr uint16_t BlackColor = 0x0000;
	//A forced-blank row is white on every GBA; its extra columns match the
	//picture instead of falling back.
	constexpr uint16_t WhiteColor = 0x7FFF;

	constexpr uint32_t MaxTextBgs = 4;

	//How many of the four BGs this BG mode draws as text BGs. Mode 0 draws all
	//four; mode 1 draws BG0/BG1 as text, its BG2 is affine and its BG3 is not
	//drawn at all; the bitmap modes have no text BG.
	inline uint32_t TextBgCount(uint8_t bgMode);

	//One text BG's state on a row, as the row's first tiles are fetched.
	struct TextBgRow
	{
		bool Enabled = false; //BGxCNT enable, DISPCNT's layer bit and the region's window mask
		bool Mosaic = false; //BGxCNT bit 6
		bool Bpp8 = false; //BGxCNT bit 7
		uint8_t Priority = 0; //BGxCNT bits 0-1
		uint16_t TilemapAddr = 0; //BGxCNT bits 8-12, in bytes
		uint16_t TilesetAddr = 0; //BGxCNT bits 2-3, in bytes
		uint16_t ScrollX = 0; //BGxHOFS
		uint16_t ScrollY = 0; //BGxVOFS
		uint16_t MapWidth = 256; //256 or 512 (BGxCNT bit 14)
		uint16_t MapHeight = 256; //256 or 512 (BGxCNT bit 15)
	};

	//The row's color effect (BLDCNT/BLDALPHA/BLDY), as it applies to the
	//region beside the picture. The GBA's windows are 240 px wide, so that
	//region is always outside them: the caller reads the outside-window
	//(WINOUT) mask, or the no-window mask when no window is enabled.
	struct EffectRow
	{
		uint8_t Effect = 0; //GbaPpuBlendEffect: 0 none, 1 alpha blend, 2 increase, 3 decrease brightness
		bool Enabled = false; //the region's color-effect bit
		uint8_t Brightness = 0; //BLDY
		uint8_t MainCoeff = 16; //BLDALPHA EVA
		uint8_t SubCoeff = 0; //BLDALPHA EVB
		uint8_t MainTargets = 0; //BLDCNT bits 0-5 (BG0-3, OBJ, backdrop)
		uint8_t SubTargets = 0; //BLDCNT bits 8-13
	};

	//Everything one row's extra columns are drawn from. `ScreenY` is the
	//row's own scanline (0-159), the coordinate mosaic blocks are aligned to.
	struct RowBasis
	{
		bool TextMode = false; //BG mode 0 or 1: the modes whose BGs have a tilemap
		bool AffineOverlay = false; //an affine BG is enabled on this row
		bool ForcedBlank = false; //DISPCNT bit 7: the picture row is white
		int32_t ScreenY = 0;
		uint32_t BgCount = 0;
		TextBgRow Bgs[MaxTextBgs] = {};
		uint8_t MosaicSizeX = 0; //MOSAIC bits 0-3
		uint8_t MosaicSizeY = 0; //MOSAIC bits 4-7
		EffectRow Effect = {};
	};

	//unsigned modulo
	inline int32_t Mod(int32_t value, int32_t range);

	//floor division, so a mosaic block left of the window keeps the grid the
	//picture's own blocks sit on
	inline int32_t FloorDiv(int32_t value, int32_t divisor);

	//Whether this BG's map holds content for `screenX` (a column coordinate
	//in the console's own space: 0-239 is the picture, negative is left of
	//it) that the picture does not already show. A map narrower than the
	//window plus its sides wraps onto itself, and a wrapped column is the
	//picture's own edge - ADR-0253 §3's "cannot fill".
	inline bool ColumnHasContent(const TextBgRow& bg, int32_t screenX);

	//The BG's pixel at `screenX`/`screenY`, as a BGR555 color, or 0 when the
	//tile's pixel is transparent. Reads VRAM and the palette only.
	inline uint16_t SampleBgPixel(const TextBgRow& bg, int32_t screenX, int32_t screenY, const uint8_t* vram, const uint16_t* palette);

	//One extra column: the text BGs composited by priority, the row's color
	//effect applied, or ADR-0253 §3's black fallback where the console cannot
	//fill the column.
	inline uint16_t CompositePixel(const RowBasis& basis, int32_t screenX, const uint8_t* vram, const uint16_t* palette);

	//Snaps a coordinate to the start of its mosaic block (size 0 = no mosaic).
	//The grid is anchored at the window's own first column, so a block left of
	//the picture keeps the grid the picture's blocks sit on.
	inline int32_t MosaicSnap(int32_t value, uint8_t size);

	//GbaPpu::BlendColors on plain colors: each channel is clamped to 31.
	inline uint16_t Blend(uint16_t main, uint8_t mainCoeff, uint16_t sub, uint8_t subCoeff);

	//The row's color effect on the composited pixel, mirroring what
	//GbaPpu::ProcessColorMath does with the same BLDCNT/BLDALPHA/BLDY state.
	inline uint16_t ApplyColorEffect(const EffectRow& effect, int32_t mainLayer, uint16_t mainColor, int32_t subLayer, uint16_t subColor);

	//Draws one row's left and right extra columns (ExtraColumns pixels each).
	inline void RenderRowSides(const RowBasis& basis, const uint8_t* vram, const uint16_t* palette, uint16_t* left, uint16_t* right);

	//The extended frames, double-buffered like the PPU's own output buffers:
	//the video decoder may still be reading the frame just sent while the
	//next one is drawn. Whether a frame is extended is latched once, when it
	//begins, so a switch flipped mid-frame never yields half a frame.
	class FrameBuffers
	{
	private:
		vector<uint16_t> _buffers[2];
		uint8_t _write = 0;
		bool _active = false;
		bool _hold = false;
		const uint16_t* _lastFrame = nullptr;
		bool _rowDrawn[Height] = {};

	public:
		//A frame the PPU is not redrawing (frame skipping): keep the last
		//extended frame, so the aspect ratio never flips mid-turbo.
		void HoldLastFrame(bool requested);

		void BeginFrame(bool active);

		bool IsActive() const { return _active; }

		//The row's two runs of extra columns, to be drawn by the caller.
		bool RowSides(int16_t row, uint16_t*& left, uint16_t*& right);

		//The extended frame (standard picture in the centre), or nullptr when
		//this frame is standard. A row the frame never drew (a save state
		//loaded mid-frame) gets the black fallback rather than last frame's.
		const uint16_t* Finish(const uint16_t* standardFrame);
	};

	inline int32_t Mod(int32_t value, int32_t range)
	{
		int32_t m = value % range;
		return m < 0 ? m + range : m;
	}

	inline uint32_t TextBgCount(uint8_t bgMode)
	{
		//GbaPpu::RenderScanline draws exactly these: mode 0 renders BG0-BG3 as
		//text, mode 1 renders BG0/BG1 as text plus an affine BG2, and a BG3
		//whose enable bit happens to be set is never drawn by either.
		switch(bgMode) {
			case 0: return 4;
			case 1: return 2;
			default: return 0;
		}
	}

	inline int32_t FloorDiv(int32_t value, int32_t divisor)
	{
		int32_t q = value / divisor;
		if((value % divisor) != 0 && ((value < 0) != (divisor < 0))) {
			q--;
		}
		return q;
	}

	inline int32_t MosaicSnap(int32_t value, uint8_t size)
	{
		if(size == 0) {
			return value;
		}
		int32_t block = (int32_t)size + 1;
		return FloorDiv(value, block) * block;
	}

	inline bool ColumnHasContent(const TextBgRow& bg, int32_t screenX)
	{
		if(bg.MapWidth == 0) {
			return false;
		}
		//Independent of scroll: the map columns the 240-px window does not show
		//are exactly the ones at or past StandardWidth in the map, so a map
		//narrower than the window plus its sides points at its own edge.
		return Mod(screenX, bg.MapWidth) >= (int32_t)StandardWidth;
	}

	inline uint16_t SampleBgPixel(const TextBgRow& bg, int32_t screenX, int32_t screenY, const uint8_t* vram, const uint16_t* palette)
	{
		if(bg.MapWidth == 0 || bg.MapHeight == 0) {
			return 0;
		}

		int32_t mapX = Mod(screenX + (int32_t)bg.ScrollX, bg.MapWidth);
		int32_t mapY = Mod(screenY + (int32_t)bg.ScrollY, bg.MapHeight);

		//The map is a grid of 256x256 pages: a 512-wide map puts its second
		//column page 0x800 past the first, and a 512-tall map (which is also
		//512 wide) its second row page 0x1000 past it.
		uint32_t entryAddr = (uint32_t)bg.TilemapAddr
			+ (uint32_t)(mapY >> 8) * (bg.MapWidth == 512 ? 0x1000u : 0x800u)
			+ (uint32_t)(mapX >> 8) * 0x800u
			+ (uint32_t)((mapY & 0xFF) >> 3) * 64u
			+ (uint32_t)((mapX & 0xFF) >> 3) * 2u;
		entryAddr &= 0xFFFF;

		uint16_t entry = (uint16_t)(vram[entryAddr] | (vram[(entryAddr + 1) & 0xFFFF] << 8));

		int32_t tileX = mapX & 7;
		int32_t tileY = mapY & 7;
		if(entry & 0x400) {
			tileX = 7 - tileX;
		}
		if(entry & 0x800) {
			tileY = 7 - tileY;
		}

		uint32_t charAddr = (uint32_t)bg.TilesetAddr
			+ (uint32_t)(entry & 0x3FF) * (bg.Bpp8 ? 64u : 32u)
			+ (uint32_t)tileY * (bg.Bpp8 ? 8u : 4u)
			+ (uint32_t)(bg.Bpp8 ? tileX : (tileX >> 1));
		charAddr &= 0xFFFF;

		uint32_t paletteIndex;
		if(bg.Bpp8) {
			paletteIndex = vram[charAddr];
		} else {
			uint8_t data = vram[charAddr];
			uint8_t color = (tileX & 1) ? (data >> 4) : (data & 0x0F);
			if(color == 0) {
				return 0;
			}
			paletteIndex = (uint32_t)(entry >> 12) * 16 + color;
		}

		if(paletteIndex == 0) {
			return 0;
		}
		return (uint16_t)(palette[paletteIndex & 0x1FF] & 0x7FFF);
	}

	inline uint16_t Blend(uint16_t main, uint8_t mainCoeff, uint16_t sub, uint8_t subCoeff)
	{
		uint32_t r = std::min<uint32_t>(31, ((main & 0x1F) * mainCoeff + (sub & 0x1F) * subCoeff) >> 4);
		uint32_t g = std::min<uint32_t>(31, (((main >> 5) & 0x1F) * mainCoeff + ((sub >> 5) & 0x1F) * subCoeff) >> 4);
		uint32_t b = std::min<uint32_t>(31, (((main >> 10) & 0x1F) * mainCoeff + ((sub >> 10) & 0x1F) * subCoeff) >> 4);
		return (uint16_t)(r | (g << 5) | (b << 10));
	}

	inline uint16_t ApplyColorEffect(const EffectRow& effect, int32_t mainLayer, uint16_t mainColor, int32_t subLayer, uint16_t subColor)
	{
		if(!effect.Enabled || effect.Effect == 0) {
			return mainColor;
		}
		if((effect.MainTargets & (1 << mainLayer)) == 0) {
			return mainColor;
		}

		uint8_t brightness = effect.Brightness > 16 ? 16 : effect.Brightness;
		switch(effect.Effect) {
			case 1: {
				//Alpha blend only happens when the pixel under the main one is a
				//2nd target; the backdrop is a target of its own (bit 5).
				if((effect.SubTargets & (1 << subLayer)) == 0) {
					return mainColor;
				}
				uint8_t eva = effect.MainCoeff > 16 ? 16 : effect.MainCoeff;
				uint8_t evb = effect.SubCoeff > 16 ? 16 : effect.SubCoeff;
				return Blend(mainColor, eva, subColor, evb);
			}

			case 2: //increase brightness: fade towards white
			case 3: //decrease brightness: fade towards black
				if(brightness == 0) {
					return mainColor;
				}
				return Blend(mainColor, (uint8_t)(16 - brightness), effect.Effect == 2 ? WhiteColor : BlackColor, brightness);

			default:
				return mainColor;
		}
	}

	inline uint16_t CompositePixel(const RowBasis& basis, int32_t screenX, const uint8_t* vram, const uint16_t* palette)
	{
		if(basis.ForcedBlank) {
			//The picture's own row is white, so the sides match it
			return WhiteColor;
		}
		if(!basis.TextMode || basis.AffineOverlay) {
			//A bitmap mode has no map to read, and an affine BG covers pixels
			//this slice does not compute: ADR-0253 §3's black fallback.
			return BlackColor;
		}

		//The text BGs that have a map column of their own here, nearest first.
		//Ties go to the lower-numbered BG, exactly like the console's own
		//pixel-priority chain.
		int32_t order[MaxTextBgs] = {};
		uint32_t count = 0;
		for(uint32_t i = 0; i < basis.BgCount && i < MaxTextBgs; i++) {
			const TextBgRow& bg = basis.Bgs[i];
			if(!bg.Enabled) {
				continue;
			}
			if(!ColumnHasContent(bg, bg.Mosaic ? MosaicSnap(screenX, basis.MosaicSizeX) : screenX)) {
				continue;
			}
			uint32_t p = count;
			while(p > 0 && basis.Bgs[order[p - 1]].Priority > bg.Priority) {
				order[p] = order[p - 1];
				p--;
			}
			order[p] = (int32_t)i;
			count++;
		}

		if(count == 0) {
			//No text BG has anything of its own beside the picture
			return BlackColor;
		}

		//A transparent pixel is not a pixel: only a visible layer can be the
		//main one, and only a visible layer under it can be the sub one. With
		//none under it the sub is the backdrop, the console's own default.
		int32_t mainLayer = -1;
		uint16_t mainColor = 0;
		int32_t subLayer = 5;
		uint16_t subColor = (uint16_t)(palette[0] & 0x7FFF);

		for(uint32_t k = 0; k < count; k++) {
			const TextBgRow& bg = basis.Bgs[order[k]];
			int32_t bx = bg.Mosaic ? MosaicSnap(screenX, basis.MosaicSizeX) : screenX;
			int32_t by = bg.Mosaic ? MosaicSnap(basis.ScreenY, basis.MosaicSizeY) : basis.ScreenY;
			uint16_t color = SampleBgPixel(bg, bx, by, vram, palette);
			if(color == 0) {
				continue;
			}
			if(mainLayer < 0) {
				mainLayer = order[k];
				mainColor = color;
			} else {
				subLayer = order[k];
				subColor = color;
				break;
			}
		}

		if(mainLayer < 0) {
			//Every text BG is transparent here: the console draws its backdrop
			mainLayer = 5;
			mainColor = (uint16_t)(palette[0] & 0x7FFF);
		}

		return ApplyColorEffect(basis.Effect, mainLayer, mainColor, subLayer, subColor);
	}

	inline void RenderRowSides(const RowBasis& basis, const uint8_t* vram, const uint16_t* palette, uint16_t* left, uint16_t* right)
	{
		for(uint32_t i = 0; i < ExtraColumns; i++) {
			left[i] = CompositePixel(basis, (int32_t)i - (int32_t)ExtraColumns, vram, palette);
			right[i] = CompositePixel(basis, (int32_t)StandardWidth + (int32_t)i, vram, palette);
		}
	}

	inline void FrameBuffers::HoldLastFrame(bool requested)
	{
		if(!requested) {
			//A frame the PPU draws decides for itself whether it is extended
			_hold = false;
			_lastFrame = nullptr;
			return;
		}
		//Only a frame that was being extended can hold an extended one: with
		//the switch off this frame is standard, and holding here would keep the
		//picture 284 px wide after the switch went off. _lastFrame survives, so
		//turning the switch back on mid-turbo still has one to hold.
		_hold = _active;
		_active = false;
	}

	inline void FrameBuffers::BeginFrame(bool active)
	{
		//Nothing is allocated while the switch is off - a standard frame must
		//not cost the unmodified build anything (ADR-0162)
		if(active && _buffers[0].empty()) {
			_buffers[0].resize((size_t)ExtendedWidth * Height);
			_buffers[1].resize((size_t)ExtendedWidth * Height);
		}
		_active = active;
		//Each frame decides for itself whether it holds the last one
		_hold = false;
		_write ^= 1;
		memset(_rowDrawn, 0, sizeof(_rowDrawn));
	}

	inline bool FrameBuffers::RowSides(int16_t row, uint16_t*& left, uint16_t*& right)
	{
		if(!_active || row < 0 || row >= (int16_t)Height) {
			return false;
		}
		_rowDrawn[row] = true;
		uint16_t* pixels = _buffers[_write].data() + (size_t)row * ExtendedWidth;
		left = pixels;
		right = pixels + ExtraColumns + StandardWidth;
		return true;
	}

	inline const uint16_t* FrameBuffers::Finish(const uint16_t* standardFrame)
	{
		if(!_active) {
			if(!_hold) {
				//The switch is off: this frame is standard
				return nullptr;
			}
			if(_lastFrame) {
				//Frame skipping: the last extended frame is sent again
				return _lastFrame;
			}
			//Skipping before any extended frame was drawn (the switch was just
			//turned on). Sending a standard frame here would flip the picture's
			//width mid-turbo, so the frame is built with the black fallback on
			//its sides instead. Nothing was allocated only if no frame was ever
			//extended, in which case there is nothing to build.
			if(_buffers[_write].empty()) {
				return nullptr;
			}
		}

		uint16_t* buffer = _buffers[_write].data();
		for(uint32_t y = 0; y < Height; y++) {
			uint16_t* row = buffer + (size_t)y * ExtendedWidth;
			if(!_rowDrawn[y]) {
				//A row this frame never drew (a save state loaded mid-frame)
				//falls back to black rather than to last frame's pixels
				for(uint32_t i = 0; i < ExtraColumns; i++) {
					row[i] = BlackColor;
					row[ExtraColumns + StandardWidth + i] = BlackColor;
				}
			}
			memcpy(row + ExtraColumns, standardFrame + (size_t)y * StandardWidth, StandardWidth * sizeof(uint16_t));
		}

		_lastFrame = buffer;
		return buffer;
	}
}
