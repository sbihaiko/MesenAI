#pragma once
#include "pch.h"
#include <bitset>
#include "NES/HdPacks/HdData.h"
#include "NES/HdPacks/SpriteFetchLog.h"
#include "NES/HdPacks/TileSheetTypes.h"

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

	//ADR-0234 (issue #505): which sprite half one fetched row belongs to, in
	//the form the fetch log and the pixel tally both use - the half's x, its
	//tile base (the 16-byte-aligned address of the half, quadrant included) and
	//the screen line its top row is drawn on. The host keeps one of these per
	//sprite shifter so a pixel it is about to put on screen can be counted
	//onto the half that drew it without a second OAM read.
	struct SpriteId
	{
		uint8_t X = 0;
		uint16_t TileBase = 0;
		int32_t Top = 0;
	};

	//ADR-0234: the screen line a fetched row's half starts on, from the line
	//the row is drawn on and the PPU-space address the fetch read. The same
	//arithmetic SpriteFetchLog::Record does, stated from the drawing end.
	static int32_t TopRowOf(int32_t drawnLine, uint16_t patternAddr, bool verticalMirror)
	{
		uint8_t row = (uint8_t)(patternAddr & 0x07);
		if(verticalMirror) {
			row = 7 - row;
		}
		return drawnLine - row;
	}

	//ADR-0234: the per-frame drawing facts are MesenSheets::SpriteDrawing - the
	//mask predicate needs the same three numbers the recorder stores, so the
	//two sides name one type (TileSheetTypes.h).
	using Drawing = MesenSheets::SpriteDrawing;

	//One 8x8 half of an OAM entry, decoded with a given PPUCTRL.
	struct Half
	{
		uint8_t Sprite = 0;
		uint8_t HalfIndex = 0;
		uint16_t TileAddr = 0;
		uint8_t X = 0;
		uint8_t Y = 0;
		uint8_t PaletteOffset = 0;
		bool HorizontalMirror = false;
		bool VerticalMirror = false;
		//ADR-0234: OAM attribute bit 5, the priority bit - the half is drawn
		//behind the background, so an opaque background pixel hides it.
		bool BackgroundPriority = false;
		//Set by the host's `resolve`: the absolute CHR address it decoded the
		//half from, -1 when it does not say (then the row log always renames).
		int32_t AbsoluteAddr = -1;
	};

	void Clear()
	{
		_latched.reset();
		_pending.reset();
		_rows.Clear();
		_tally.clear();
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
				_behindBg[slot] = h.BackgroundPriority;
				_pending.set(slot);
			}
		}
	}

	//The frame's latched halves in OAM order, top half first, each named by
	//the bank of its topmost fetched row (#458) and carrying the two facts
	//ADR-0234 reads: its OAM priority bit and how many of its pixels this
	//frame put on screen (`OnSpritePixel`). `rebank(absoluteAddr, tile)`
	//re-reads a tile from another CHR address, keeping its palette and flips;
	//`emitBank(tile)` receives the half as read from each further bank its
	//rows came from - a key the <tile> rules carry, but not a second sprite.
	//Neither receives a fully transparent tile (#470, IsFullyTransparent).
	//
	//#520: a blank half is still a half the PPU placed, and `emitPlaced` gets
	//exactly those, as a bare (x, y) - no tile, because it has no art to name.
	//It is not a sprite (it reaches `emit` never, so no shape is registered and
	//no sheet cell or `<tile>` rule can exist for it) but it is a *placement*,
	//and the pose pass reads the placements: Bubble Bobble draws every figure
	//as two 8x16 sprites whose upper half is blank, so dropping the blank halves
	//took two of a four-cell figure's cells and the cluster fell under
	//ADR-0170 §2's floor - 78 silhouettes and 395 tracks became 0.
	template<typename Emit, typename Rebank, typename EmitBank, typename EmitPlaced>
	void ForEachLatched(Emit&& emit, Rebank&& rebank, EmitBank&& emitBank, EmitPlaced&& emitPlaced)
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
				emit(_x[slot], _y[slot], tile, DrawingOf(slot));
			} else {
				//#520: the artless placement has no shape and no `<tile>` rule,
				//so it is handed over as a position only - but it is a cell of
				//the figure the game drew, and ADR-0234 leaves it without a
				//drawing fact rather than calling it hidden (IsMaskEntry needs
				//`hiddenPixels > 0`), which is what keeps it in the pose.
				emitPlaced(_x[slot], _y[slot]);
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

	//The same, for a caller that wants the shapes and not the placements (#520):
	//`emitPlaced` is a no-op, so a blank half is handed to nobody at all. What
	//reaches `emit` and `emitBank` is unchanged - #470's filter still holds, and
	//so does the bank pass.
	template<typename Emit, typename Rebank, typename EmitBank>
	void ForEachLatched(Emit&& emit, Rebank&& rebank, EmitBank&& emitBank)
	{
		ForEachLatched(emit, rebank, emitBank, [](uint8_t, uint8_t) {});
	}

	//The raw latch as the PPU filled it: no row log, no bank pass, and **no
	//#470 filter** - a fully transparent half is emitted like any other, so a
	//caller here sees a half the production path drops. Its only caller is
	//`OamFetchLatchModel::RunFrame` (`scripts/core_unit_tests.cpp`), which reads
	//back the halves of a modelled frame as fetched. Production goes through the
	//4-arg overload above.
	template<typename Emit>
	void ForEachLatched(Emit&& emit)
	{
		for(uint32_t slot = 0; slot < SlotCount; slot++) {
			if(_latched[slot]) {
				emit(_x[slot], _y[slot], _tiles[slot], DrawingOf(slot));
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
		h.HalfIndex = half;
		h.X = entry[3];
		h.Y = (uint8_t)(entry[0] + 1 + half * 8);
		h.PaletteOffset = ((attributes & 0x03) << 2) | 0x10;
		h.HorizontalMirror = (attributes & 0x40) != 0;
		h.VerticalMirror = (attributes & 0x80) != 0;
		//ADR-0234: bit 5 is the one fact about this sprite's drawing the entry
		//token never carried.
		h.BackgroundPriority = (attributes & 0x20) != 0;
		uint32_t halves = largeSprites ? 2 : 1;
		uint32_t part = h.VerticalMirror ? (halves - 1 - half) : half;
		h.TileAddr = largeSprites
			? (uint16_t)((((tileIndex & 0x01) << 12) | ((tileIndex & ~0x01) << 4)) + part * 16)
			: (uint16_t)(spritePatternAddr | (tileIndex << 4));
		return h;
	}

	//Issue #479: whether a sprite pixel DrawPixel sees on screen row `scanline`
	//may make a <tile> rule - true exactly on the rows an OAM entry can be
	//latched on. OAM draws a sprite one line below its Y byte, so no entry
	//covers row 0 (OnSpriteFetch's first fetch is for row 1). What the PPU
	//draws there comes from the pre-render line's fetch of secondary OAM left
	//over from line 239 - Excitebike's frame 9 draws eight copies of tile $00
	//at x 0 that way - so a rule for it is a key no sheet can carry.
	static bool SpriteRowIsPlaced(int scanline)
	{
		return scanline >= 1 && scanline <= 239;
	}

	//ADR-0234 (issue #505): one dot of this frame's picture, on which the half
	//with this identity (x, tile base, top row - the fetch log's own identity)
	//was the sprite pipeline's contender for the pixel.
	//
	//The host calls this from HdBuilderPpu::DrawPixel with the verdict for that
	//dot (MesenSheets::SpritePixelVerdictOf, classified by
	//HdBuilderPpu::NoteSpritePixel from what the PPU reported), and *only* for a
	//dot that verdict says something
	//about: a transparent sprite pixel, and the ones the clip, a hidden row or
	//the pre-render line's leftovers leave to the background, are no contender
	//and must not be reported - `_lastSprite` is set for them all the same. A
	//verdict with neither flag is ignored here, so a caller that reports every
	//pixel anyway changes nothing.
	//
	//Both counts are saturating and per frame (a half is 64 pixels wide, so
	//neither can run away), and the frame's totals are what ForEachLatched
	//hands over. The mask is the half that counted up `hidden` and never
	//`visible`: SMB3's pipe mask contends for every one of its opaque pixels and
	//loses all of them, while a half the 8-per-line limit kept off the screen
	//contends for none.
	void OnSpritePixel(uint8_t spriteX, uint16_t tileBase, int32_t top, MesenSheets::SpritePixelVerdict verdict)
	{
		if(!verdict.Drawn && !verdict.Hidden) {
			return;
		}
		for(PixelTally& tally : _tally) {
			if(tally.Id.X == spriteX && tally.Id.TileBase == tileBase && tally.Id.Top == top) {
				uint8_t& count = verdict.Hidden ? tally.Hidden : tally.Visible;
				if(count < 0xFF) {
					count++;
				}
				return;
			}
		}
		//A frame latches at most SlotCount halves, so the table cannot grow
		//past that; the bound is stated rather than assumed.
		if(_tally.size() < SlotCount) {
			PixelTally fresh;
			fresh.Id = SpriteId { spriteX, tileBase, top };
			(verdict.Hidden ? fresh.Hidden : fresh.Visible) = 1;
			_tally.push_back(fresh);
		}
	}

private:
	//The pixels this frame put on screen for one sprite half, and the ones it
	//contended for and lost to the background. Two halves that share an
	//identity (same x, tile and top row - twins the PPU drew on top of each
	//other) share the counts, which can only make a mask look *less* hidden:
	//the classification stays conservative.
	struct PixelTally
	{
		SpriteId Id;
		uint8_t Visible = 0;
		uint8_t Hidden = 0;
	};

	Drawing DrawingOf(uint32_t slot) const
	{
		Drawing drawing;
		drawing.BehindBg = _behindBg[slot];
		for(const PixelTally& tally : _tally) {
			if(tally.Id.X == _x[slot] && tally.Id.TileBase == (uint16_t)(_tileAddr[slot] & 0xFFF0) && tally.Id.Top == (int32_t)_y[slot]) {
				drawing.VisiblePixels = tally.Visible;
				drawing.HiddenPixels = tally.Hidden;
				break;
			}
		}
		return drawing;
	}

	HdPpuTileInfo _tiles[SlotCount] = {};
	uint8_t _x[SlotCount] = {};
	uint8_t _y[SlotCount] = {};
	uint8_t _paletteIndex[SlotCount] = {};
	uint16_t _tileAddr[SlotCount] = {};
	int32_t _abs[SlotCount] = {};
	//The half's rank among its twins on the row log (PR #476 review).
	uint8_t _ordinal[SlotCount] = {};
	//ADR-0234: the half's OAM priority bit, latched with the rest of the
	//attributes.
	bool _behindBg[SlotCount] = {};
	std::bitset<SlotCount> _latched;
	//Decoded at the last fetch, recorded once their row is drawn.
	std::bitset<SlotCount> _pending;
	SpriteFetchLog _rows;
	std::vector<int32_t> _banks;
	std::vector<PixelTally> _tally;
};
