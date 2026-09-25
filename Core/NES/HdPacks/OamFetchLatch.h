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
//moment's state, for every half one of whose rows is the next screen row - so
//a half is recorded if and only if at least one of its rows was fetched while
//rendering was on with sprites enabled, and it is named with the size, pattern
//table, CHR and palette of that fetch. It still reads OAM, not the PPU's
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
	}

	//Called at cycle 257 of visible scanline `fetchLine`, where the PPU fetches
	//the sprites of screen row fetchLine + 1. `spritesDrawn` is "rendering on
	//and PPUMASK shows sprites" at that moment. `resolve(half, tile)` fills the
	//tile from the host (CHR data, palette, flips) and returns false when the
	//address does not map, in which case a later line may still latch it.
	template<typename Resolve>
	void OnSpriteFetch(int fetchLine, bool spritesDrawn, const uint8_t* oam, bool largeSprites, uint16_t spritePatternAddr, Resolve&& resolve)
	{
		//Row 240 and below is never on screen.
		if(!spritesDrawn || fetchLine < 0 || fetchLine > 238) {
			return;
		}
		uint32_t row = (uint32_t)fetchLine + 1;
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
				_latched.set(slot);
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
	std::bitset<SlotCount> _latched;
};
