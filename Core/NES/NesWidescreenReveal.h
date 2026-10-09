#pragma once
#include "pch.h"
#include "NES/NesTypes.h"
#include "Shared/Video/WidescreenFallback.h"

//ADR-0253 slice W.1: the NES "Reveal" widescreen mode. With the WideScrn
//switch on, the standard PPU draws ExtraColumns more pixels on each side of
//every row, taken from the nametables beside the 256-px window, and sends a
//RenderedFrame that is ExtendedWidth wide (RenderedFrame::ExtendedColumns
//says how many of its columns on each side are extra).
//
//Everything here is presentation only:
//- the extra columns are fetched with the debugger's side-effect-free VRAM
//  read (BaseMapper::DebugReadVram with its default disableSideEffects=true),
//  never the PPU's rendering read, so no mapper bus hook (MMC3's A12 counter,
//  MMC2/4's CHR latch, MMC5's fetch tracking) ever sees an extra fetch;
//- sprites, sprite-0 hit, the left-8-px mask and every CPU-visible register
//  stay on the original 256 px - this file never touches them;
//- the standard 256x240 frame the PPU keeps (GetScreenBuffer, the HD builder,
//  thumbnails, the debugger) is never written to: the center of the extended
//  frame is a copy of it.
//
//The row state each row is drawn from (loopy v, fine X, the BG pattern table,
//the mask) is the one NesPpu already captures right after cycle 257's
//horizontal copy for the next row (ADR-0169's _scanlineVideoRamAddr), so a
//split screen widens with each band's own scroll.
//
//Host-free on purpose: scripts/core_unit_tests.cpp drives it with a fake
//mapper that counts the side-effecting calls.
namespace NesWidescreenReveal
{
	constexpr uint32_t StandardWidth = 256;
	constexpr uint32_t Height = 240;

	//N = 64 extra columns per side (ADR-0253 §2, the frame-width contract):
	//- 8 whole tiles, so every extra column is a full tile fetch and no side
	//  ever shows a partial attribute group at the frame edge;
	//- 384 = 1.5 x 256 px. At the NES's 8:7 pixel aspect that is a 1.83:1
	//  picture, within 3 % of 16:9 (N = 59 would be exact, but is not
	//  tile-aligned; N = 56 lands 1.5 % short and leaves a pillarbox);
	//- 384 < 512, so the two sides never overlap in the 512-px-wide
	//  nametable plane: the left side is the 64 px before the picture's
	//  origin, the right side the 64 px after its end.
	constexpr uint32_t ExtraColumns = 64;
	constexpr uint32_t ExtendedWidth = StandardWidth + 2 * ExtraColumns;

	//W.1's fallback for a column with no real content (ADR-0253 §3): NES
	//palette entry $0F, black on every PPU palette. Not run through grayscale
	//or emphasis, because it is not a color the game chose.
	constexpr uint16_t BlackColor = 0x0F;

	//One row's rendering state, as it stands when the row's first tiles are
	//fetched.
	struct RowBasis
	{
		uint16_t VideoRamAddr = 0; //loopy v after cycle 257's horizontal copy
		uint8_t FineX = 0; //loopy x
		uint16_t BgPatternAddr = 0; //$0000 or $1000 (PPUCTRL bit 4)
		bool BgEnabled = false; //PPUMASK bit 3 (and the emulator's BG layer toggle)
		uint8_t PaletteMask = 0x3F; //0x30 under PPUMASK grayscale
		uint16_t EmphasisBits = 0; //PPUMASK emphasis, in the output buffer's bits 6-8
	};

	//Where screen pixel 0 of the row sits in the 512-px-wide plane the two
	//horizontally adjacent nametables form (0-511).
	inline uint16_t RowOriginX(const RowBasis& basis)
	{
		uint16_t nametableX = (basis.VideoRamAddr >> 10) & 0x01;
		uint16_t coarseX = basis.VideoRamAddr & 0x1F;
		return (uint16_t)(((nametableX << 8) | (coarseX << 3)) + basis.FineX) & 0x1FF;
	}

	//The mirroring the mapper has in effect, read off which memory each of
	//the four nametable slots ($2000/$2400/$2800/$2C00) points at - the
	//arrangement the reads below will really see, whatever the mapper's own
	//bookkeeping says.
	inline MirroringType ClassifyMirroring(const void* nt0, const void* nt1, const void* nt2, const void* nt3)
	{
		if(nt0 == nt1 && nt1 == nt2 && nt2 == nt3) {
			return MirroringType::ScreenAOnly;
		}
		if(nt0 != nt1 && nt0 != nt2 && nt0 != nt3 && nt1 != nt2 && nt1 != nt3 && nt2 != nt3) {
			return MirroringType::FourScreens;
		}
		if(nt0 == nt1 && nt2 == nt3) {
			return MirroringType::Horizontal;
		}
		if(nt0 == nt2 && nt1 == nt3) {
			return MirroringType::Vertical;
		}
		//Irregular layouts (3-screen, diagonal): judged by the top pair, the
		//one that decides what sits beside the picture on most rows
		return nt0 != nt1 ? MirroringType::Vertical : MirroringType::Horizontal;
	}

	//ADR-0253 §3, "cannot fill" on NES: single-screen or horizontal mirroring,
	//at any scroll. Beside the picture they only hold the picture's own
	//opposite edge (a wrapped copy, or a column mid-rewrite), so the sides stay
	//black - the SMB3 title included. Vertical and four-screen mirroring have
	//a distinct nametable beside the picture.
	inline bool SideColumnsHaveContent(MirroringType mirroring, uint16_t originX)
	{
		(void)originX;
		switch(mirroring) {
			case MirroringType::Vertical:
			case MirroringType::FourScreens:
				return true;
			default:
				return false;
		}
	}

	//Draws one row's left and right extra columns (ExtraColumns pixels each),
	//in the PPU output buffer's format: palette index in bits 0-5, emphasis in
	//bits 6-8. `vram` is a BaseMapper, or anything with its
	//DebugReadVram(uint16_t) - the only call made on it.
	//
	//`fillOut`, when given, receives WidescreenFallback::LeftBit/RightBit for the
	//sides this row filled from the console's own map, and 0 for a row that hit
	//ADR-0253 §3's "cannot fill" case (which is what the fallback chain then
	//fills). The backdrop a row shows with the background layer off is the
	//console's own output, so it counts as filled.
	template<typename Vram>
	void RenderRowSides(const RowBasis& basis, MirroringType mirroring, Vram& vram, const uint8_t* paletteRam, uint16_t* left, uint16_t* right, uint8_t* fillOut = nullptr)
	{
		uint16_t originX = RowOriginX(basis);
		if(!SideColumnsHaveContent(mirroring, originX)) {
			for(uint32_t i = 0; i < ExtraColumns; i++) {
				left[i] = BlackColor;
				right[i] = BlackColor;
			}
			if(fillOut) {
				*fillOut = 0;
			}
			return;
		}
		if(fillOut) {
			*fillOut = (uint8_t)(WidescreenFallback::LeftBit | WidescreenFallback::RightBit);
		}

		uint16_t backdrop = (uint16_t)((paletteRam[0] & basis.PaletteMask) | basis.EmphasisBits);
		if(!basis.BgEnabled) {
			for(uint32_t i = 0; i < ExtraColumns; i++) {
				left[i] = backdrop;
				right[i] = backdrop;
			}
			return;
		}

		uint16_t v = basis.VideoRamAddr;
		uint16_t coarseY = (v >> 5) & 0x1F;
		uint16_t nametableY = (v >> 11) & 0x01;
		uint16_t fineY = (v >> 12) & 0x07;

		//Two runs of ExtraColumns pixels: the one ending at the picture's
		//origin and the one starting right after its 256th pixel.
		uint16_t* outputs[2] = { left, right };
		uint16_t starts[2] = { (uint16_t)((originX - ExtraColumns) & 0x1FF), (uint16_t)((originX + StandardWidth) & 0x1FF) };

		for(int side = 0; side < 2; side++) {
			uint16_t* out = outputs[side];
			uint32_t x = 0;
			while(x < ExtraColumns) {
				uint16_t planeX = (starts[side] + x) & 0x1FF;
				uint16_t column = planeX >> 3; //0-63 across both nametables
				uint16_t coarseX = column & 0x1F;
				uint16_t nametable = (uint16_t)((nametableY << 1) | (column >> 5));
				uint16_t nametableBase = 0x2000 | (nametable << 10);

				uint8_t tileIndex = vram.DebugReadVram(nametableBase | (coarseY << 5) | coarseX);
				uint8_t attribute = vram.DebugReadVram(nametableBase | 0x3C0 | ((coarseY >> 2) << 3) | (coarseX >> 2));
				uint8_t shift = (uint8_t)(((coarseY & 0x02) << 1) | (coarseX & 0x02));
				uint8_t paletteOffset = (uint8_t)(((attribute >> shift) & 0x03) << 2);

				uint16_t tileAddr = basis.BgPatternAddr | (tileIndex << 4) | fineY;
				uint8_t low = vram.DebugReadVram(tileAddr);
				uint8_t high = vram.DebugReadVram(tileAddr + 8);

				for(uint16_t px = planeX & 0x07; px < 8 && x < ExtraColumns; px++, x++) {
					uint8_t bit = 7 - px;
					uint8_t color = (uint8_t)(((low >> bit) & 0x01) | (((high >> bit) & 0x01) << 1));
					out[x] = color ? (uint16_t)((paletteRam[paletteOffset | color] & basis.PaletteMask) | basis.EmphasisBits) : backdrop;
				}
			}
		}
	}

	//Copies the standard frame into the center of an extended one, row by
	//row: the 256 middle columns of every row are the standard picture, bit
	//for bit.
	inline void ComposeCenter(const uint16_t* standardFrame, uint16_t* extendedFrame)
	{
		for(uint32_t y = 0; y < Height; y++) {
			memcpy(extendedFrame + y * ExtendedWidth + ExtraColumns, standardFrame + y * StandardWidth, StandardWidth * sizeof(uint16_t));
		}
	}

	//The extended frames, double-buffered like the PPU's own output buffers:
	//the video decoder may still be reading the frame just sent while the
	//next one is drawn. Whether a frame is extended is latched once, when it
	//begins, so a switch flipped mid-frame never yields half a frame.
	class FrameBuffers
	{
	private:
		vector<uint16_t> _buffers[2];
		//ADR-0253 §3: one byte per row per buffer, the sides the game filled
		//(WidescreenFallback.h). Double-buffered with the pixels it describes.
		uint8_t _fill[2][Height] = {};
		uint8_t _write = 0;
		bool _active = false;
		bool _rowDrawn[Height] = {};
		const uint8_t* _lastFill = nullptr;

	public:
		void BeginFrame(bool active)
		{
			_active = active;
			if(_active && _buffers[_write].empty()) {
				_buffers[_write].assign(ExtendedWidth * Height, BlackColor);
			}
			memset(_rowDrawn, 0, sizeof(_rowDrawn));
			memset(_fill[_write], 0, sizeof(_fill[_write]));
			if(!_active) {
				_lastFill = nullptr;
			}
		}

		bool IsActive() const { return _active; }

		//The row's two runs of extra columns and the byte recording which of
		//them the caller filled, to be drawn by the caller.
		bool RowSides(int16_t row, uint16_t*& left, uint16_t*& right, uint8_t*& fill)
		{
			if(!_active || row < 0 || row >= (int16_t)Height) {
				return false;
			}
			uint16_t* rowStart = _buffers[_write].data() + row * ExtendedWidth;
			left = rowStart;
			right = rowStart + ExtraColumns + StandardWidth;
			fill = &_fill[_write][row];
			_rowDrawn[row] = true;
			return true;
		}

		//The extended frame (standard picture in the center), or nullptr when
		//this frame is standard. A row the frame never drew (a save state
		//loaded mid-frame) gets the black fallback rather than last frame's.
		const uint16_t* Finish(const uint16_t* standardFrame)
		{
			if(!_active) {
				_lastFill = nullptr;
				return nullptr;
			}
			uint8_t index = _write;
			uint16_t* frame = _buffers[index].data();
			ComposeCenter(standardFrame, frame);
			for(uint32_t y = 0; y < Height; y++) {
				if(!_rowDrawn[y]) {
					uint16_t* rowStart = frame + y * ExtendedWidth;
					std::fill(rowStart, rowStart + ExtraColumns, BlackColor);
					std::fill(rowStart + ExtraColumns + StandardWidth, rowStart + ExtendedWidth, BlackColor);
				}
			}
			_write ^= 1;
			_active = false;
			memset(_rowDrawn, 0, sizeof(_rowDrawn));
			_lastFill = _fill[index];
			return frame;
		}

		//The per-row fill map of the frame Finish just returned (ADR-0253 §3),
		//or nullptr when that frame was standard.
		const uint8_t* LastFill() const { return _lastFill; }
	};
}
