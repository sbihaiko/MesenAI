#pragma once
#include "pch.h"
#include "Utilities/HexUtilities.h"
#include <cstring>

//ADR-0236 (PRD slice F14.11, issue #499): a recorded capture's per-cell key
//record, and the run-time guard that reads it.
//
//**Why a record at all.** A `<background>` captured from a frame is a whole
//screen of pixels, and until this header it carried nothing about the frame it
//was taken from - so it drew on every frame its conditions matched, and a
//handful of `tileAtPosition` probes cannot make a match exact. Ninja Gaiden's
//`screen001` is the case: the probes matched a frame whose status bar the
//capture does not hold, and the HUD froze on it (issue #499). The record is
//**positional**: for each of the 32x30 screen cells, the key the run time reads
//at that cell's origin pixel (`c*8`, `r*8`) on the captured frame. A *set* of
//keys would not do the job - a live HUD digit that also appears somewhere else
//on the same capture is in that set, which is #499 over again.
//
//**One predicate.** `HdCellKeyMatches` is the comparison
//`HdPackTileAtPositionCondition` has always made, factored out so the gate
//(written by the recorder) and the guard (read by the renderer) cannot
//disagree - the lesson of ADR-0235's Context, where the recorder's evidence
//read a different pixel than the run time and every measurement built on it
//was wrong. Both call this function; `make core-unit-tests` pins both.
//
//**Host-free.** No console, no emulator, no PNG: the recorder builds a record
//here, the loader parses the tag here, `HdNesPack` builds its mask here, and
//the unit tests exercise all four without a ROM. `HdCellKey` deliberately owns
//its own three fields instead of pointing at the run time's `HdPpuTileInfo`, so
//this header does not depend on `HdData.h` - which is what lets `HdData.h`
//store a record on `HdBackgroundInfo` (`HdCellKeyOf` there is the one bridge).

//One entry of a record's dictionary: the identity of a background tile as the
//run time holds it, plus the "the run time named no tile here" case.
struct HdCellKey
{
	enum class Kind : uint8_t
	{
		//Background rendering was off at that pixel (the ROM disabled it, or
		//the line's leftmost 8 pixels are clipped), so the run time's
		//`ScreenTiles[..].Tile` is `NoTile`. A key, not an absence: it is what
		//the run time read, and the guard compares it like any other, which is
		//what keeps a clipped column drawing the art it was captured with.
		None = 0,
		//ADR-0172: on a CHR ROM game the identity is the tile index + palette.
		ChrIndex = 1,
		//On a CHR RAM game the identity is the 16 drawn bytes + palette.
		ChrData = 2
	};

	Kind KeyKind = Kind::None;
	int32_t TileIndex = -1;
	//`PaletteColors` and `TileData` are adjacent, in this order, so the CHR RAM
	//branch below compares both in one memcmp - the same trick, and the same
	//reason, as HdTileKey/HdPpuTileInfo.
	uint32_t PaletteColors = 0;
	uint8_t TileData[16] = {};

	bool operator==(const HdCellKey& other) const
	{
		return KeyKind == other.KeyKind && TileIndex == other.TileIndex &&
			PaletteColors == other.PaletteColors &&
			memcmp(TileData, other.TileData, sizeof(TileData)) == 0;
	}
	bool operator!=(const HdCellKey& other) const { return !(*this == other); }
};

//The one predicate (ADR-0236 §2). Both keys are in the same shape: `live` is
//what the run time holds at the pixel being decided, `recorded` what the
//capture held there. `fallbackIndex` is
//`BaseHdNesPack::GetFallbackTile(live.TileIndex)`, passed in rather than looked
//up so this stays host-free. Every clause below is verbatim what
//`HdPackTileAtPositionCondition::InternalCheckCondition` computed before this
//header existed, including the `IgnorePalette` cases.
inline bool HdCellKeyMatches(const HdCellKey& live, const HdCellKey& recorded, bool ignorePalette, int32_t fallbackIndex)
{
	//`Kind::None` on either side - "the run time named no tile here" - matches
	//only the same. It is what a clipped left column or a line with rendering
	//off reads as, on both sides, so such a cell still matches itself; and it
	//keeps the stale palette and bytes the PPU leaves behind at such a pixel
	//from ever counting as evidence. (A gate never builds a None key - see
	//HdPackTileAtPositionCondition - so this clause is the guard's alone.)
	if(live.KeyKind == HdCellKey::Kind::None || recorded.KeyKind == HdCellKey::Kind::None) {
		return live.KeyKind == recorded.KeyKind;
	}

	switch(recorded.KeyKind) {
		case HdCellKey::Kind::ChrIndex:
			if(!ignorePalette && live.PaletteColors != recorded.PaletteColors) {
				return false;
			}
			return live.TileIndex == recorded.TileIndex || fallbackIndex == recorded.TileIndex;

		case HdCellKey::Kind::ChrData:
		default:
			if(ignorePalette) {
				return memcmp(live.TileData, recorded.TileData, sizeof(recorded.TileData)) == 0;
			}
			return memcmp(&live.PaletteColors, &recorded.PaletteColors, sizeof(recorded.PaletteColors) + sizeof(recorded.TileData)) == 0;
	}
}

//Which `<background>` line a record belongs to: the one immediately above it,
//and no other. A state machine rather than two member integers the loader has
//to remember to clear, because the bug that shape invites is silent and total -
//clear the binding at the top of the line the record is on and *every* record
//in *every* pack is dropped, with the pack still loading and still drawing.
//`Line()` is both the read and the clear: called once per manifest line before
//that line is dispatched, it hands back the binding the previous line left and
//then drops it, so nothing but a `<background>` on the line directly above can
//ever be taken. `Take()` also consumes, so a second record under one
//`<background>` takes nothing - the same line `mep_lint` reports as an error.
struct HdCellRecordBinder
{
	//A `<background>` that loaded: what the next line may claim.
	int32_t Priority = -1;
	int32_t Index = -1;
	//What the line before this one left, for this line to claim.
	int32_t PendingPriority = -1;
	int32_t PendingIndex = -1;

	void Line()
	{
		PendingPriority = Priority;
		PendingIndex = Index;
		Priority = -1;
		Index = -1;
	}

	//One physical line of the manifest, in the order the format requires: the
	//binding rolls first, and *then* the line is looked at. The roll cannot be
	//skipped, because the only thing that decides a line is blank is this call
	//- `false` means "blank, do not parse it", never "this was not a line".
	//
	//That is the whole of the blank-line rule: the spec allows nothing between
	//a `<background>` and its `<bgCellRecord>` - no comment, no other tag, no
	//blank line - so a blank line ends the binding like any other line does.
	//Writing the skip at the call site instead is how the loader came to accept
	//an LF blank line (it tested `lineContent.empty()` before rolling) while
	//rejecting a CRLF one (a lone `\r` is not empty, so that spelling rolled and
	//cancelled). Both spellings are one line here, and `mep_lint` and
	//`mep_carry`, which refuse both, are the tools this must not disagree with.
	bool Step(const string& line)
	{
		Line();
		return !line.empty();
	}

	void Bound(int32_t priority, int32_t index)
	{
		Priority = priority;
		Index = index;
	}

	bool Take(int32_t& priority, int32_t& index)
	{
		if(PendingPriority < 0) {
			return false;
		}
		priority = PendingPriority;
		index = PendingIndex;
		PendingPriority = -1;
		PendingIndex = -1;
		return true;
	}
};

//A capture's positional 32x30 cell grid: a dictionary of the screen's distinct
//keys plus one index per cell. The dictionary is what keeps a screen around
//2 KB instead of 960 x sizeof(HdCellKey) (~23 KB), and what the `hires.txt` tag
//below serializes.
struct HdCellKeyRecord
{
	static constexpr int Cols = 32;
	static constexpr int Rows = 30;
	static constexpr int CellCount = Cols * Rows;
	//The run time's own "no tile here" (HdTileKey::NoTile). HdData.h pins the
	//two together with a static_assert.
	static constexpr int32_t NoTileIndex = -1;

	//The tag itself, so writer and reader cannot disagree about its spelling.
	static constexpr const char* Tag = "<bgCellRecord>";
	static constexpr size_t TagLength = 14; //strlen(Tag)

	static bool IsTagLine(const string& line) { return line.compare(0, TagLength, Tag) == 0; }
	static string PayloadOf(const string& line) { return line.substr(TagLength); }

	vector<HdCellKey> Keys;
	vector<uint16_t> Cells; //row-major, Cols entries per row
	//True only for a whole grid: a record without its 960 cells is nothing the
	//guard may read, so IsPresent is what "this <background> opted in" means.
	bool IsPresent() const { return Cells.size() == (size_t)CellCount && !Keys.empty(); }
	bool IsEmpty() const { return Cells.empty() && Keys.empty(); }

	uint16_t Intern(const HdCellKey& key)
	{
		for(size_t i = 0; i < Keys.size(); i++) {
			if(Keys[i] == key) {
				return (uint16_t)i;
			}
		}
		Keys.push_back(key);
		return (uint16_t)(Keys.size() - 1);
	}

	//The recorded key covering a cell, or nullptr when the record has none
	//there (outside its 32x30 grid). Nullptr reads as "this capture cannot
	//vouch for that pixel", never as a match.
	const HdCellKey* At(int row, int col) const
	{
		if(row < 0 || row >= Rows || col < 0 || col >= Cols) {
			return nullptr;
		}
		uint16_t index = Cells[(size_t)row * Cols + col];
		return index < Keys.size() ? &Keys[index] : nullptr;
	}

	//How many hex digits one cell index takes. Derived from the dictionary size
	//by writer and reader alike, so the cell plane needs no separator and no
	//length field - and a record whose cell plane length does not match is a
	//parse error rather than a silently re-indexed grid.
	static int CellIndexWidth(size_t keyCount)
	{
		int width = 2;
		while(width < 4 && (((size_t)1 << (width * 4)) < keyCount)) {
			width++;
		}
		return width;
	}

	static string FixedHex(uint32_t value, int width)
	{
		static const char* digits = "0123456789ABCDEF";
		string out((size_t)width, '0');
		for(int i = width - 1; i >= 0; i--) {
			out[i] = digits[value & 0xF];
			value >>= 4;
		}
		return out;
	}

	//`N` (nothing drawn), `I<8 hex index>:<8 hex palette>`, or
	//`D<32 hex tile data>:<8 hex palette>` - the same three facts the key struct
	//holds, in the hex spelling the rest of hires.txt uses.
	static string KeyToString(const HdCellKey& key)
	{
		if(key.KeyKind == HdCellKey::Kind::None) {
			return "N";
		}
		if(key.KeyKind == HdCellKey::Kind::ChrIndex) {
			return "I" + FixedHex((uint32_t)key.TileIndex, 8) + ":" + FixedHex(key.PaletteColors, 8);
		}
		string data;
		for(int i = 0; i < 16; i++) {
			data += FixedHex(key.TileData[i], 2);
		}
		return "D" + data + ":" + FixedHex(key.PaletteColors, 8);
	}

	//The whole tag line, `<bgCellRecord>` included.
	string ToString() const
	{
		string out = Tag;
		for(size_t i = 0; i < Keys.size(); i++) {
			if(i > 0) {
				out += "|";
			}
			out += KeyToString(Keys[i]);
		}
		out += ";";
		int width = CellIndexWidth(Keys.size());
		for(uint16_t index : Cells) {
			out += FixedHex(index, width);
		}
		return out;
	}

	//The other half of ToString. `error` (when non-null) is filled with the one
	//thing that was wrong, in the shape the loader's log lines use.
	static bool Parse(const string& payload, HdCellKeyRecord& out, string* error)
	{
		HdCellKeyRecord record;
		size_t split = payload.find(';');
		if(split == string::npos) {
			if(error) { *error = "missing ';' between the dictionary and the cell grid"; }
			return false;
		}
		string dict = payload.substr(0, split);
		string cells = payload.substr(split + 1);
		if(dict.empty()) {
			if(error) { *error = "empty key dictionary"; }
			return false;
		}
		size_t start = 0;
		while(true) {
			size_t end = dict.find('|', start);
			HdCellKey key;
			if(!ParseKey(dict.substr(start, end == string::npos ? string::npos : end - start), key, error)) {
				return false;
			}
			record.Keys.push_back(key);
			if(end == string::npos) {
				break;
			}
			start = end + 1;
		}
		if(record.Keys.size() > 0x10000) {
			if(error) { *error = "more than 65536 distinct keys"; }
			return false;
		}
		int width = CellIndexWidth(record.Keys.size());
		if(cells.size() != (size_t)(CellCount * width)) {
			if(error) {
				*error = "cell grid is " + std::to_string(cells.size()) + " hex digits, expected " +
					std::to_string(CellCount * width) + " (" + std::to_string(CellCount) + " cells x " + std::to_string(width) + ")";
			}
			return false;
		}
		record.Cells.reserve(CellCount);
		for(int i = 0; i < CellCount; i++) {
			uint32_t index = (uint32_t)HexUtilities::FromHex(cells.substr((size_t)i * width, width));
			if(index >= record.Keys.size()) {
				if(error) {
					*error = "cell " + std::to_string(i) + " names key " + std::to_string(index) + " of " +
						std::to_string(record.Keys.size());
				}
				return false;
			}
			record.Cells.push_back((uint16_t)index);
		}
		out = std::move(record);
		return true;
	}

	static bool ParseKey(const string& text, HdCellKey& key, string* error)
	{
		if(text == "N") {
			key = HdCellKey();
			return true;
		}
		if(text.size() < 2) {
			if(error) { *error = "empty key entry"; }
			return false;
		}
		char kind = text[0];
		if(kind != 'I' && kind != 'D') {
			if(error) { *error = "unknown key kind '" + string(1, kind) + "'"; }
			return false;
		}
		size_t colon = text.find(':');
		if(colon == string::npos) {
			if(error) { *error = "key is missing its palette field"; }
			return false;
		}
		string body = text.substr(1, colon - 1);
		string palette = text.substr(colon + 1);
		size_t expected = kind == 'I' ? 8 : 32;
		if(body.size() != expected || palette.size() != 8) {
			if(error) {
				*error = "key body/palette is " + std::to_string(body.size()) + "/" + std::to_string(palette.size()) +
					" hex digits, expected " + std::to_string(expected) + "/8";
			}
			return false;
		}
		key = HdCellKey();
		key.PaletteColors = (uint32_t)HexUtilities::FromHex(palette);
		if(kind == 'I') {
			key.KeyKind = HdCellKey::Kind::ChrIndex;
			key.TileIndex = HexUtilities::FromHex(body);
		} else {
			key.KeyKind = HdCellKey::Kind::ChrData;
			for(int i = 0; i < 16; i++) {
				key.TileData[i] = (uint8_t)HexUtilities::FromHex(body.substr((size_t)i * 2, 2));
			}
		}
		return true;
	}

	//The recorder's side: `named` is one flag per cell (1 where the run time
	//named a tile at that cell's origin on the captured frame, 0 where it did
	//not - see HdCellKey::Kind::None), and `keyAt(i)` returns the recorder's own
	//key for cell `i`, row-major. A template rather than a typed parameter so
	//this header never has to see HdData.h's HdTileKey.
	template<typename KeyAt>
	static HdCellKeyRecord FromCellGrid(const uint8_t* named, KeyAt keyAt, bool isChrRam)
	{
		HdCellKeyRecord record;
		record.Cells.assign(CellCount, 0);
		for(int i = 0; i < CellCount; i++) {
			HdCellKey key;
			if(named[i]) {
				const auto& source = keyAt(i);
				key.KeyKind = isChrRam ? HdCellKey::Kind::ChrData : HdCellKey::Kind::ChrIndex;
				key.TileIndex = source.TileIndex;
				key.PaletteColors = source.PaletteColors;
				memcpy(key.TileData, source.TileData, sizeof(key.TileData));
			}
			record.Cells[i] = record.Intern(key);
		}
		return record;
	}
};

//The run-time side: one 32x30 bitmask per active `<background>` that carries a
//record. A set bit means "this screen cell may draw"; a clear bit leaves the
//cell to whatever would draw without the capture (ADR-0236 §2), which is the
//pack's `<tile>` rules or the ROM's own tiles, exactly as if the `<background>`
//line were absent for that cell.
//
//`Record == nullptr` (a pack written before the record existed, every
//hand-made pack, a `<background>` whose record was not carried) makes the
//guard inert: `Allows` is then never consulted, so the output is byte-identical
//to the pack that has no record at all (ADR-0236 §3).
struct HdCellGuard
{
	uint32_t Mask[HdCellKeyRecord::Rows] = {};
	const HdCellKeyRecord* Record = nullptr;

	bool Allows(uint32_t x, uint32_t y) const
	{
		return ((Mask[y >> 3] >> (x >> 3)) & 1u) != 0;
	}

	//Rebuilds the mask for cell rows [firstRow, lastRow]. The caller passes the
	//whole 32x30 grid once per frame when the `<background>` maps its record
	//1:1 (scroll ratio 0 and no offset - every recorded capture, ADR-0236 §2),
	//and one row per scanline otherwise, since then the PNG pixel a cell's
	//origin draws from moves with the line's scroll.
	//
	//The record cell consulted is the one the PNG pixel comes from -
	//`(left + c*8 + scrollX) >> 3` - so the rule reads "the capture cell being
	//drawn must still hold, live, what the capture recorded at the screen cell
	//it is drawn to", which for the identity mapping is verbatim ADR-0236 §2.
	//`liveKeyAt(row, col)` returns the run time's own HdCellKey for that cell
	//origin; `fallbackAt` is GetFallbackTile for the predicate.
	template<typename LiveKeyAt, typename FallbackAt>
	void Build(int firstRow, int lastRow, int32_t left, int32_t top, int32_t scrollX, int32_t scrollY, LiveKeyAt liveKeyAt, FallbackAt fallbackAt)
	{
		if(Record == nullptr) {
			return;
		}
		for(int row = firstRow; row <= lastRow; row++) {
			int32_t sourceRowPixel = top + (row << 3) + scrollY;
			int32_t sourceRow = sourceRowPixel < 0 ? -1 : (sourceRowPixel >> 3);
			uint32_t bits = 0;
			for(int col = 0; col < HdCellKeyRecord::Cols; col++) {
				int32_t sourceColPixel = left + (col << 3) + scrollX;
				int32_t sourceCol = sourceColPixel < 0 ? -1 : (sourceColPixel >> 3);
				const HdCellKey* recorded = Record->At(sourceRow, sourceCol);
				if(recorded == nullptr) {
					continue;
				}
				const HdCellKey live = liveKeyAt(row, col);
				if(HdCellKeyMatches(live, *recorded, false, fallbackAt(live.TileIndex))) {
					bits |= 1u << col;
				}
			}
			Mask[row] = bits;
		}
	}
};
