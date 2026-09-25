#pragma once
//Issue #458. Which CHR bank each row of a sprite half was read from.
//
//HdBuilderPpu writes a sprite's `<tile>` rule from the absolute CHR address it
//resolves as the PPU fetches each sprite row (StoreSpriteInformation). The
//sheet registry names the same half through OamFetchLatch, which decodes it at
//cycle 257, the *start* of the sprite fetch (#450), with the CHR mapping of
//that moment. On MMC2/MMC4 that is not always the bank the rows come from:
//fetching pattern $FD or $FE flips a latch that selects the bank for every
//fetch after it, so a latch sprite earlier in the same fetch window moves the
//sprites after it to another bank, and a latch tile's own row 0 is read from
//the old bank while its rows 1-7 come from the new one. Before #450 the sheet
//read the mapping at frame end instead: Punch-Out!! drew 0580-0582 and the
//sheet named 1E80-1E82, the same slot in another bank with other art.
//
//This log is the per-row record OamFetchLatch consults when it hands a
//latched half over: the topmost fetched row names the half, and every other
//bank its rows came from is a key the `<tile>` rules carry too. Host-free and
//header-only (HdBuilderPpu.h is not on the Makefile's CUTSRC list).
//
//PR #476 review: a fetch belongs to exactly one OAM entry's half, and is
//matched to it by identity, not by position. Each fetch carries the screen
//line its half's top row is drawn on (the fetch's own row within the half
//tells it) and its ordinal: how many fetches of the same x, tile and top line
//came before it on the same scanline. The PPU fetches a line's sprites in OAM
//order, so that ordinal is the half's rank among the OAM entries whose half
//has the same x, tile and top line - which the latch computes from OAM.
//Matching by an eight-scanline window instead let two entries with the same x
//and tile on overlapping rows take each other's fetches.
#include <algorithm>
#include <bitset>
#include <cstdint>
#include <vector>

class SpriteFetchLog
{
public:
	//A frame fetches at most 64 sprites on each of its 262 scanlines (with the
	//sprite limit removed); past that the log stops growing and a late entry
	//falls back, rather than a runaway frame costing memory.
	static constexpr size_t MaxFetches = 64 * 262;

	void Clear()
	{
		_fetches.clear();
		_hiddenRows.reset();
		_lineStart = 0;
	}

	//PR #468 review: screen row `row` drew no sprite (PPUMASK hid them), so it
	//made no <tile> rule and none of the fetches for it names a bank.
	void HideRow(uint32_t row)
	{
		if(row < ScreenRows) {
			_hiddenRows.set(row);
		}
	}

	//One fetched sprite row. `scanline` is the PPU scanline the fetch ran on
	//(the row is drawn on the next one), `patternAddr` the PPU-space address it
	//read, `verticalMirror` the sprite's vertical flip (the low three bits of
	//`patternAddr` are then the flipped row) and `absoluteAddr` the CHR ROM
	//address the mapper resolved it to.
	void Record(int32_t scanline, uint8_t spriteX, uint16_t patternAddr, bool verticalMirror, int32_t absoluteAddr)
	{
		if(absoluteAddr < 0 || _fetches.size() >= MaxFetches) {
			return;
		}
		if(_fetches.empty() || _fetches.back().Scanline != scanline) {
			_lineStart = _fetches.size();
		}
		uint8_t row = (uint8_t)(patternAddr & 0x07);
		if(verticalMirror) {
			row = 7 - row;
		}
		Fetch fetch = { scanline, scanline + 1 - row, (uint16_t)(patternAddr & 0xFFF0), absoluteAddr & ~0x0F, spriteX, 0 };
		for(size_t i = _lineStart; i < _fetches.size(); i++) {
			const Fetch& other = _fetches[i];
			if(other.SpriteX == fetch.SpriteX && other.TileBase == fetch.TileBase && other.Top == fetch.Top) {
				fetch.Ordinal++;
			}
		}
		_fetches.push_back(fetch);
	}

	//The absolute CHR address to name one 8x8 half of an OAM entry by. The half
	//sits at `spriteX`, its top row is drawn on screen line `screenY`,
	//`patternAddr` is the PPU-space address of its tile and `ordinal` its rank
	//among the OAM entries with a half of that same x, tile and top line (0
	//for the first, and for any half that has no twin). Its eight rows are
	//fetched on scanlines screenY-1 .. screenY+6; the first of them that was
	//fetched (for a row that showed sprites, see HideRow) names the bank, which is the bank of the topmost row the `<tile>`
	//rule recorded. `fallbackAddr` is the address through the mapping in effect
	//now, used only when no row of the half was fetched this frame (a sprite
	//the 8-per-line limit dropped on every row).
	int32_t Resolve(uint8_t spriteX, uint32_t screenY, uint16_t patternAddr, uint8_t ordinal, int32_t fallbackAddr) const
	{
		for(const Fetch& fetch : _fetches) {
			if(Draws(fetch, spriteX, screenY, patternAddr, ordinal)) {
				return fetch.AbsoluteAddr;
			}
		}
		return fallbackAddr;
	}

	//Every distinct absolute CHR address the rows of that same half were
	//fetched from, topmost row first - so the first one is what Resolve
	//returns. More than one means a bank switch landed inside the sprite: an
	//MMC2/MMC4 latch tile ($FD/$FE) is read from the old bank on the row that
	//trips the latch and from the new one on the rows after it, and the
	//`<tile>` rules key each row by the bank it came from.
	void FetchedAddresses(uint8_t spriteX, uint32_t screenY, uint16_t patternAddr, uint8_t ordinal, std::vector<int32_t>& out) const
	{
		out.clear();
		for(const Fetch& fetch : _fetches) {
			if(Draws(fetch, spriteX, screenY, patternAddr, ordinal) && std::find(out.begin(), out.end(), fetch.AbsoluteAddr) == out.end()) {
				out.push_back(fetch.AbsoluteAddr);
			}
		}
	}

private:
	struct Fetch
	{
		int32_t Scanline;
		//The screen line the top row of the fetched half is drawn on.
		int32_t Top;
		uint16_t TileBase;
		int32_t AbsoluteAddr;
		uint8_t SpriteX;
		//Earlier fetches on this scanline of the same x, tile and top line.
		uint8_t Ordinal;
	};

	static constexpr uint32_t ScreenRows = 240;

	//A fetch is for the half with its x, tile, top line and ordinal. It was
	//made on scanline S for screen row S + 1, and counts only when that row is
	//on screen (the fetch on line 239 is for row 240) and showed sprites.
	bool Draws(const Fetch& fetch, uint8_t spriteX, uint32_t screenY, uint16_t patternAddr, uint8_t ordinal) const
	{
		if(fetch.SpriteX != spriteX || fetch.TileBase != (uint16_t)(patternAddr & 0xFFF0) || fetch.Top != (int32_t)screenY || fetch.Ordinal != ordinal) {
			return false;
		}
		uint32_t row = (uint32_t)(fetch.Scanline + 1);
		return row < ScreenRows && !_hiddenRows[row];
	}

	//In fetch order, so scanline order: the first match is the topmost row.
	std::vector<Fetch> _fetches;
	//Where the last scanline's fetches start in _fetches.
	size_t _lineStart = 0;
	std::bitset<ScreenRows> _hiddenRows;
};
