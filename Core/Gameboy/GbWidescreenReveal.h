#pragma once
#include "pch.h"
#include "Gameboy/GbConstants.h"

//ADR-0253 slice W.2: the Game Boy / Game Boy Color "Reveal" widescreen mode.
//With the WideScrn switch on, the PPU draws ExtraColumns more pixels on each
//side of every row from the 256x256 BG map - which wraps around the 160-px
//picture - and sends a RenderedFrame that is ExtendedWidth wide
//(RenderedFrame::ExtendedColumns says how many of its columns on each side are
//extra).
//
//Everything here is presentation only:
//- the extra columns are fetched with GbPpu::LcdReadVram, the side-effect-free
//  14-bit read the PPU's own fetchers use: the address carries the bank bit
//  (0x2000), so a CGB attribute byte and a bank-1 tile resolve, and it honours
//  the CGB STOP freeze. Never GbPpu::ReadVram, the CPU-visible read that fires
//  the debugger's hook, and never GbPpu::PeekVram, which drops the bank bit and
//  refuses to read at all while the LCD is drawing (mode 3);
//- sprites, the sprite FIFO, the STAT modes and every CPU-visible register stay
//  on the original 160 px - this file never touches them;
//- the standard 160x144 frame the PPU keeps (GetOutputBuffer, the HD builder,
//  thumbnails, the debugger) is never written to: the centre of the extended
//  frame is a copy of it.
//
//The row state each row is drawn from (SCX/SCY, LCDC, BGP, and the window's own
//latches) is the state GbPpu holds when the row's last pixel is pushed, so a
//row that scrolls or opens the window part-way through a frame widens with its
//own state. The window follows the hardware: once it starts on a row (at
//WX - 7) it runs to the right edge, so it is what sits beside the picture on
//the right, and it can cover the left columns too when it starts before x = 0.
//
//Where the console cannot fill, ADR-0253 §3 falls back to black. On the GB the
//map always wraps, so the only uncovered rows are the ones the frame never drew
//(a save state loaded mid-frame) - those are the black fallback. A DMG row whose
//background layer is off is not a fallback: the hardware outputs one flat colour
//there, and the revealed columns show that same colour.
//
//Host-free on purpose: scripts/core_unit_tests.cpp drives it with a fake VRAM
//that counts both read paths.
namespace GbWidescreenReveal
{
	constexpr uint32_t StandardWidth = GbConstants::ScreenWidth; //160
	constexpr uint32_t Height = GbConstants::ScreenHeight; //144
	constexpr uint32_t BgMapWidth = 256;
	constexpr uint32_t BgMapHeight = 256;

	//N = 48 extra columns per side (ADR-0253 §2, the frame-width contract):
	//- 6 whole tiles, so every extra column is a full tile fetch;
	//- 256 = the BG map's own width, so an extended row is exactly one wrap of
	//  the map line the picture sits in: the 48 px before the picture's origin
	//  are that line's last 48 px;
	//- at the GB's square pixels, 256x144 is exactly 16:9.
	constexpr uint32_t ExtraColumns = 48;
	constexpr uint32_t ExtendedWidth = StandardWidth + 2 * ExtraColumns;

	//ADR-0253 §3's last fallback: raw RGB555 black. It is not a colour the game
	//chose, so it is not run through the palette.
	constexpr uint16_t BlackColor = 0x0000;

	//One row's rendering state, as the PPU holds it when the row's last pixel
	//has been pushed (GbPpu::_state plus its window latches).
	struct RowBasis
	{
		uint16_t Scanline = 0; //the row being drawn (LY)
		uint8_t ScrollX = 0; //SCX
		uint8_t ScrollY = 0; //SCY

		bool CgbEnabled = false; //CGB double speed/colour mode: attributes + 32 palettes
		//LCDC.0. On DMG it switches the background/window layer off, and the
		//hardware then outputs colour 0 through BGP for the whole line. On CGB
		//that bit is only the BG priority bit, so the map is drawn either way.
		bool BgEnabled = false;
		//The emulator's own "disable background" toggle (GameboyConfig).
		//Independent of the hardware bit, and effective on both consoles: when it
		//is on, the whole line is one flat colour on DMG and on CGB alike.
		bool LayerDisabled = false;
		bool BgTileSelect = false; //LCDC.4: tile data at 0x0000, else 0x1000 + a signed index
		bool BgTilemapSelect = false; //LCDC.3: BG map at 0x1C00, else 0x1800
		uint8_t BgPalette = 0xE4; //BGP (DMG shade mapping, and the flat colour above)
		bool PaletteBlocked = false; //CGB STOP froze the palette: every read is 0

		//The window layer. WindowOnRow is the PPU's own latch for the row it
		//just finished (GbPpu::_fetchWindow, decided by WindowVisible), so it
		//says whether that row's pixels came from the window at all: the window
		//appears from WY onwards for the rest of the frame, and starts at
		//x = WX - 7.
		bool WindowOnRow = false;
		int16_t WindowStartX = 0; //WX - 7, negative when the window starts off-screen
		uint8_t WindowLine = 0; //the window's own row counter (its map row, no SCY)
		bool WindowTilemapSelect = false; //LCDC.6: window map at 0x1C00, else 0x1800
	};

	//Whether the window layer is drawn at all on the row being drawn: the PPU's
	//own condition, one definition shared with the fetcher (GbPpu::ExecCycle),
	//so the revealed columns can never show a layer the PPU did not draw. All
	//three hold on a window row: the LCDC.5 enable bit, the WX hit, and the WY
	//match, which stays true for the rest of the frame. The two latches alone
	//are not the window - a game that opens the window on WY and clears LCDC.5
	//for a later row (the usual "no HUD on this line") leaves both set and
	//draws background there.
	inline bool WindowVisible(bool windowEnabled, bool wxArmed, bool wyMatched)
	{
		return windowEnabled && wxArmed && wyMatched;
	}

	//Whether the window layer draws screen pixel `x` on this row.
	inline bool WindowCovers(const RowBasis& basis, int32_t x)
	{
		return basis.WindowOnRow && x >= basis.WindowStartX;
	}

	//Draws one row's left and right extra columns (ExtraColumns pixels each) in
	//the PPU output buffer's format: 15-bit RGB555, the value the PPU itself
	//writes. `vram` is a GbPpu, or anything with its LcdReadVram(uint16_t) - the
	//only call made on it. `bgPalettes` is GbPpuState::CgbBgPalettes (32 entries;
	//on DMG only the first four are filled and BGP indexes them).
	template<typename Vram>
	void RenderRowSides(const RowBasis& basis, Vram& vram, const uint16_t* bgPalettes, uint16_t* left, uint16_t* right)
	{
		//The colour the PPU emits for a palette entry. A frozen palette (STOP on
		//the CGB) reads 0, and so does the picture's own WriteBgPixel.
		auto paletteColor = [&](uint8_t index) -> uint16_t {
			return basis.PaletteBlocked ? 0 : (uint16_t)(bgPalettes[index] & 0x7FFF);
		};

		if(basis.LayerDisabled || (!basis.BgEnabled && !basis.CgbEnabled)) {
			//The layer is not drawn: on DMG that is LCDC.0 cleared, on both
			//consoles it is the emulator's own layer toggle. The hardware outputs
			//colour 0 through BGP for the whole line, so the whole line - and so
			//the revealed columns - is one flat colour.
			uint16_t blank = paletteColor((uint8_t)(basis.BgPalette & 0x03));
			for(uint32_t i = 0; i < ExtraColumns; i++) {
				left[i] = blank;
				right[i] = blank;
			}
			return;
		}

		uint16_t* outputs[2] = { left, right };
		const int32_t starts[2] = { -(int32_t)ExtraColumns, (int32_t)StandardWidth };

		for(int side = 0; side < 2; side++) {
			uint16_t* out = outputs[side];
			int32_t x = starts[side];
			uint32_t i = 0;
			while(i < ExtraColumns) {
				bool window = WindowCovers(basis, x);

				//Where this pixel sits in its own layer's map. The map wraps, so
				//the left columns of an unscrolled picture are its last ones.
				uint16_t mapX;
				uint16_t mapY;
				uint16_t mapBase;
				if(window) {
					mapX = (uint16_t)(x - basis.WindowStartX);
					mapY = basis.WindowLine;
					mapBase = basis.WindowTilemapSelect ? 0x1C00 : 0x1800;
				} else {
					mapX = (uint16_t)(basis.ScrollX + x);
					mapY = (uint16_t)(basis.ScrollY + basis.Scanline);
					mapBase = basis.BgTilemapSelect ? 0x1C00 : 0x1800;
				}

				uint16_t mapAddr = (uint16_t)(mapBase + ((mapY >> 3) & 0x1F) * 32 + ((mapX >> 3) & 0x1F));
				uint8_t tileIndex = vram.LcdReadVram(mapAddr);
				//The attribute byte is the CGB's, in VRAM bank 1
				uint8_t attributes = basis.CgbEnabled ? vram.LcdReadVram((uint16_t)(mapAddr | 0x2000)) : 0;

				uint8_t tileY = (uint8_t)(mapY & 0x07);
				if(attributes & 0x40) {
					tileY = (uint8_t)(7 - tileY);
				}
				uint16_t rowAddr = basis.BgTileSelect ? (uint16_t)(tileIndex * 16) : (uint16_t)(0x1000 + (int8_t)tileIndex * 16);
				rowAddr = (uint16_t)(rowAddr + tileY * 2);
				if(attributes & 0x08) {
					rowAddr |= 0x2000;
				}
				uint8_t low = vram.LcdReadVram(rowAddr);
				uint8_t high = vram.LcdReadVram((uint16_t)(rowAddr + 1));
				uint8_t paletteOffset = (uint8_t)((attributes & 0x07) << 2);

				//The rest of this tile. The window can open inside it, and then it
				//takes over from its own column 0 (a run never crosses it).
				uint32_t runEnd = ExtraColumns;
				if(!window && basis.WindowOnRow && basis.WindowStartX > x && basis.WindowStartX < x + 8) {
					runEnd = i + (uint32_t)(basis.WindowStartX - x);
				}

				uint8_t fine = (uint8_t)(mapX & 0x07);
				for(uint8_t px = fine; px < 8 && i < runEnd; px++, x++, i++) {
					uint8_t bit = (attributes & 0x20) ? px : (uint8_t)(7 - px);
					uint8_t color = (uint8_t)(((low >> bit) & 0x01) | (((high >> bit) & 0x01) << 1));
					//On CGB the attribute picks one of the 8 palettes; on DMG BGP
					//maps the two colour bits to one of the four shades
					out[i] = basis.CgbEnabled ? paletteColor((uint8_t)(paletteOffset | color)) : paletteColor((uint8_t)((basis.BgPalette >> (color * 2)) & 0x03));
				}
			}
		}
	}

	//Copies the standard frame into the centre of an extended one, row by row:
	//the 160 middle columns of every row are the standard picture, bit for bit.
	inline void ComposeCenter(const uint16_t* standardFrame, uint16_t* extendedFrame)
	{
		for(uint32_t y = 0; y < Height; y++) {
			memcpy(extendedFrame + y * ExtendedWidth + ExtraColumns, standardFrame + y * StandardWidth, StandardWidth * sizeof(uint16_t));
		}
	}

	//The extended frames, double-buffered like the PPU's own output buffers:
	//the video decoder may still be reading the frame just sent while the next
	//one is drawn. Whether a frame is extended is latched once, when it begins,
	//so a switch flipped mid-frame never yields half a frame.
	class FrameBuffers
	{
	private:
		vector<uint16_t> _buffers[2];
		uint8_t _write = 0;
		bool _active = false;
		bool _rowDrawn[Height] = {};

	public:
		void BeginFrame(bool active)
		{
			_active = active;
			if(_active && _buffers[_write].empty()) {
				_buffers[_write].assign(ExtendedWidth * Height, BlackColor);
			}
			memset(_rowDrawn, 0, sizeof(_rowDrawn));
		}

		bool IsActive() const { return _active; }

		//The row's two runs of extra columns, to be drawn by the caller.
		bool RowSides(int16_t row, uint16_t*& left, uint16_t*& right)
		{
			if(!_active || row < 0 || row >= (int16_t)Height) {
				return false;
			}
			uint16_t* rowStart = _buffers[_write].data() + row * ExtendedWidth;
			left = rowStart;
			right = rowStart + ExtraColumns + StandardWidth;
			_rowDrawn[row] = true;
			return true;
		}

		//The extended frame (standard picture in the centre), or nullptr when
		//this frame is standard. A row the frame never drew (a save state
		//loaded mid-frame) gets the black fallback rather than last frame's.
		const uint16_t* Finish(const uint16_t* standardFrame)
		{
			if(!_active) {
				return nullptr;
			}
			uint16_t* frame = _buffers[_write].data();
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
			return frame;
		}
	};
}
