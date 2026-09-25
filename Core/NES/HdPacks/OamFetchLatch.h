#pragma once
#include "pch.h"
#include <bitset>
#include "NES/HdPacks/HdData.h"
#include "NES/HdPacks/SpriteFetchLog.h"

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
//keeps every other recording unchanged.
//
//Issue #458: cycle 257 fixes *which* halves a frame drew and their PPUCTRL and
//palette, but not always the CHR bank. MMC2/MMC4 flip their latch on the
//fetch of pattern $FD/$FE, which lands inside the sprite fetch window: a
//sprite fetched after a latch sprite on the same line comes from the new
//bank, and a latch tile's own rows 1-7 do too. So the latch also keeps the
//per-row log of what each fetch read (OnRowFetch), and ForEachLatched names
//each half by the bank of its topmost fetched row that showed sprites (a row
//PPUMASK hid made no <tile> rule, PR #468 review), handing every other bank
//its rows came from to `emitBank`. A half no row of which was fetched (the
//8-per-line limit) keeps the bank of its cycle-257 decode. A half finds its
//rows in the log by identity (PR #476 review): its x, tile, top line and its
//rank among the OAM entries with a half of those same three (see
//SpriteFetchLog), so an entry never takes the fetches of another one on
//overlapping rows. Host-free (ADR-0127): HdBuilderPpu
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
		//Set by the host's `resolve`: the absolute CHR address it decoded the
		//half from, -1 when it does not say (then the row log always renames).
		int32_t AbsoluteAddr = -1;
	};

	void Clear()
	{
		_latched.reset();
		_pending.reset();
		_rows.Clear();
	}

	//#458: one sprite row as the PPU fetched it (StoreSpriteInformation), on
	//`scanline`, with the sprite's vertical flip and the absolute CHR address
	//that fetch read.
	void OnRowFetch(int32_t scanline, uint8_t spriteX, uint16_t patternAddr, bool verticalMirror, int32_t absoluteAddr)
	{
		_rows.Record(scanline, spriteX, patternAddr, verticalMirror, absoluteAddr);
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
		if(!rowShown) {
			_rows.HideRow((uint32_t)line);
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
		//The halves on this row so far, in OAM order: the PPU fetches them in
		//this order, which is what tells twins (same x, tile, top) apart.
		Half onRow[64];
		uint32_t onRowCount = 0;
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
			Half h = Decode(entry, (uint8_t)i, (uint8_t)half, largeSprites, spritePatternAddr);
			uint8_t ordinal = 0;
			for(uint32_t j = 0; j < onRowCount; j++) {
				if(onRow[j].X == h.X && onRow[j].Y == h.Y && (onRow[j].TileAddr & 0xFFF0) == (h.TileAddr & 0xFFF0)) {
					ordinal++;
				}
			}
			onRow[onRowCount++] = h;
			if(_latched[slot]) {
				continue;
			}
			if(resolve(h, _tiles[slot])) {
				_x[slot] = h.X;
				_y[slot] = h.Y;
				_paletteIndex[slot] = (uint8_t)((h.PaletteOffset >> 2) & 0x03);
				_tileAddr[slot] = h.TileAddr;
				_abs[slot] = h.AbsoluteAddr;
				_ordinal[slot] = ordinal;
				_pending.set(slot);
			}
		}
	}

	//The frame's latched halves in OAM order, top half first, each named by
	//the bank of its topmost fetched row (#458). `rebank(absoluteAddr, tile)`
	//re-reads a tile from another CHR address, keeping its palette and flips;
	//`emitBank(tile)` receives the half as read from each further bank its
	//rows came from - a key the <tile> rules carry, but not a second sprite.
	//Neither receives a fully transparent tile (#470, IsFullyTransparent).
	template<typename Emit, typename Rebank, typename EmitBank>
	void ForEachLatched(Emit&& emit, Rebank&& rebank, EmitBank&& emitBank)
	{
		for(uint32_t slot = 0; slot < SlotCount; slot++) {
			if(!_latched[slot]) {
				continue;
			}
			HdPpuTileInfo& tile = _tiles[slot];
			_rows.FetchedAddresses(_x[slot], _y[slot], _tileAddr[slot], _ordinal[slot], _banks);
			if(!_banks.empty() && _banks[0] != _abs[slot]) {
				rebank(_banks[0], tile);
			}
			//#470: a blank half places no sprite, and a blank bank is no key;
			//a drawn bank of a blank half still is (its rows made rules).
			if(!IsFullyTransparent(tile)) {
				emit(_x[slot], _y[slot], tile);
			}
			for(size_t i = 1; i < _banks.size(); i++) {
				HdPpuTileInfo other = tile;
				rebank(_banks[i], other);
				if(!IsFullyTransparent(other)) {
					emitBank(other);
				}
			}
		}
	}

	//The same, for a caller that only wants the halves as decoded (the row
	//log is not consulted).
	template<typename Emit>
	void ForEachLatched(Emit&& emit)
	{
		for(uint32_t slot = 0; slot < SlotCount; slot++) {
			if(_latched[slot]) {
				emit(_x[slot], _y[slot], _tiles[slot]);
			}
		}
	}

	//Issue #470: a sprite tile whose 16 bytes are all zero draws colour 0 -
	//transparent - on every pixel. The loader never draws one
	//(HdNesPack::DrawTile returns on IsFullyTransparent, and
	//InitializeFallbackTiles skips blank tiles), and the PPU makes a <tile>
	//rule of one only when it happens to be the highest-priority active
	//shifter at its first dot. So neither the registry (ForEachLatched) nor
	//the rules (HdBuilderPpu::DrawPixel) record one: both use this test.
	static bool IsFullyTransparent(const HdPpuTileInfo& tile)
	{
		for(uint8_t b : tile.TileData) {
			if(b != 0) {
				return false;
			}
		}
		return true;
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
	uint16_t _tileAddr[SlotCount] = {};
	int32_t _abs[SlotCount] = {};
	//The half's rank among its twins on the row log (PR #476 review).
	uint8_t _ordinal[SlotCount] = {};
	std::bitset<SlotCount> _latched;
	//Decoded at the last fetch, recorded once their row is drawn.
	std::bitset<SlotCount> _pending;
	SpriteFetchLog _rows;
	std::vector<int32_t> _banks;
};
