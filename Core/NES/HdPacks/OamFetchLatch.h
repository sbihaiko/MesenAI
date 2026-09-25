#pragma once
#include "pch.h"
#include <bitset>
#include "NES/HdPacks/HdData.h"

//Issue #450. The recorder's per-frame OAM snapshot (F9.5, ADR-0153 §2) used to
//decode every sprite at frame end: OAM, PPUCTRL's sprite size and pattern
//table, the mapper's CHR mapping and palette RAM as they stood at scanline
//240. A game that changes them mid-frame then got sheet cells for keys the PPU
//never drew. Castlevania's cut from the gate screen to stage 1 writes
//PPUCTRL=$00 and PPUMASK=$00 at line 0, cycles 240-252: #183's per-pixel gate
//admitted the frame (sprites were on for the first pixels of line 0), and the
//frame-end decode turned its 8x16 sprites into 23 single 8x8 halves, 7 of
//whose (tile, palette) keys no <tile> line ever carries.
//
//This latch decodes each sprite half when the PPU fetches it instead: once per
//visible scanline, at the start of the sprite fetch (cycle 257), with that
//moment's PPUCTRL and CHR mapping, for every half one of whose rows is the
//next screen row. The half is recorded once that row has been drawn, and only
//if the row showed sprites (PR #468 review): with the background on the PPU
//fetches sprites whatever PPUMASK's sprite bit says, and the bit only decides,
//pixel by pixel, whether the row shows them - which is also what DrawPixel's
//<tile> rule follows. For the same reason the half takes the palette its row
//was drawn in, not the one at its fetch: the PPU reads palette RAM as it
//draws. So a half is recorded if and only if one of its rows was fetched with
//rendering on and then drawn with sprites showing. It still reads OAM, not the PPU's
//secondary OAM, so a sprite hidden by the 8-per-line limit or by background
//priority is still recorded (ADR-0153 §2's reason for reading OAM at all).
//A frame that holds its PPU state steady records exactly what the frame-end
//decode did, in the same order (OAM index, then top half first); that is what
//keeps every other recording unchanged. Host-free (ADR-0127): HdBuilderPpu
//supplies the per-half CHR/palette reads through `resolve`, and
//scripts/core_unit_tests.cpp drives the latch with synthetic frames.
class OamFetchLatch
{
public:
	//64 OAM entries, each at most two 8x8 halves.
	static constexpr uint32_t SlotCount = 128;

	//One 8x8 half of an OAM entry, decoded with a given PPUCTRL.
	struct Half
	{
		uint8_t Sprite = 0;
		uint8_t Half = 0;
		uint16_t TileAddr = 0;
		uint8_t X = 0;
		uint8_t Y = 0;
		uint8_t PaletteOffset = 0;
		bool HorizontalMirror = false;
		bool VerticalMirror = false;
	};

	void Clear()
	{
		_latched.reset();
		_pending.reset();
	}

	//Called at cycle 257 of every visible scanline `line` (0-239).
	//
	//Row `line` has just been drawn. `rowShown` says whether any of its pixels
	//was drawn with rendering on and PPUMASK showing sprites, and `rowPalettes`
	//holds the four sprite palettes (HdPpuTileInfo::PaletteColors form, by
	//attribute palette index) as that row drew them. The halves fetched for
	//this row on the line before are recorded now if it showed sprites, with
	//those palettes; otherwise a later row of theirs may still record them.
	//
	//`fetching` is "rendering is on", so the PPU now fetches the sprites of
	//row line + 1: every half with a row there that is not yet recorded is
	//decoded with this moment's PPUCTRL. `resolve(half, tile)` fills the tile
	//from the host (CHR data, flips) and returns false when the address does
	//not map, in which case a later line may still decode it.
	template<typename Resolve>
	void OnSpriteFetch(int line, bool rowShown, const uint32_t* rowPalettes, bool fetching, const uint8_t* oam, bool largeSprites, uint16_t spritePatternAddr, Resolve&& resolve)
	{
		if(line < 0 || line > 239) {
			return;
		}
		if(rowShown && _pending.any()) {
			for(uint32_t slot = 0; slot < SlotCount; slot++) {
				if(_pending[slot]) {
					_tiles[slot].PaletteColors = rowPalettes[_paletteIndex[slot]];
					_latched.set(slot);
				}
			}
		}
		_pending.reset();
		//Row 240 and below is never on screen.
		if(!fetching || line > 238) {
			return;
		}
		uint32_t row = (uint32_t)line + 1;
		uint32_t height = largeSprites ? 16 : 8;
		for(uint32_t i = 0; i < 64; i++) {
			const uint8_t* entry = oam + i * 4;
			//239 and up is how a game parks a sprite off-screen
			if(entry[0] >= 0xEF) {
				continue;
			}
			uint32_t top = (uint32_t)entry[0] + 1;
			if(row < top || row >= top + height) {
				continue;
			}
			uint32_t half = (row - top) >> 3;
			uint32_t slot = i * 2 + half;
			if(_latched[slot]) {
				continue;
			}
			Half h = Decode(entry, (uint8_t)i, (uint8_t)half, largeSprites, spritePatternAddr);
			if(resolve(h, _tiles[slot])) {
				_x[slot] = h.X;
				_y[slot] = h.Y;
				_paletteIndex[slot] = (uint8_t)((h.PaletteOffset >> 2) & 0x03);
				_pending.set(slot);
			}
		}
	}

	//The frame's latched halves in OAM order, top half first.
	template<typename Emit>
	void ForEachLatched(Emit&& emit)
	{
		for(uint32_t slot = 0; slot < SlotCount; slot++) {
			if(_latched[slot]) {
				emit(_x[slot], _y[slot], _tiles[slot]);
			}
		}
	}

	//An 8x16 sprite is two 8x8 halves, top half first on screen whichever way
	//the sprite is flipped; `half` is the on-screen half.
	static Half Decode(const uint8_t* entry, uint8_t sprite, uint8_t half, bool largeSprites, uint16_t spritePatternAddr)
	{
		Half h;
		uint8_t tileIndex = entry[1];
		uint8_t attributes = entry[2];
		h.Sprite = sprite;
		h.Half = half;
		h.X = entry[3];
		h.Y = (uint8_t)(entry[0] + 1 + half * 8);
		h.PaletteOffset = ((attributes & 0x03) << 2) | 0x10;
		h.HorizontalMirror = (attributes & 0x40) != 0;
		h.VerticalMirror = (attributes & 0x80) != 0;
		uint32_t halves = largeSprites ? 2 : 1;
		uint32_t part = h.VerticalMirror ? (halves - 1 - half) : half;
		h.TileAddr = largeSprites
			? (uint16_t)((((tileIndex & 0x01) << 12) | ((tileIndex & ~0x01) << 4)) + part * 16)
			: (uint16_t)(spritePatternAddr | (tileIndex << 4));
		return h;
	}

private:
	HdPpuTileInfo _tiles[SlotCount] = {};
	uint8_t _x[SlotCount] = {};
	uint8_t _y[SlotCount] = {};
	uint8_t _paletteIndex[SlotCount] = {};
	std::bitset<SlotCount> _latched;
	//Decoded at the last fetch, recorded once their row is drawn.
	std::bitset<SlotCount> _pending;
};
