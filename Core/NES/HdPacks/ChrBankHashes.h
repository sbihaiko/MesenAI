#pragma once
#include <cstdint>
#include <vector>

//ADR-0232: the CHR RAM bank id a recorded tile carries. HdBuilderPpu hands it
//to HdPackBuilder::ProcessTile, which files the tile on that bank's
//chr/Chr_*.png page and writes it as the trailing bank field of the <tile>
//line. Host-free so the core unit tests can pin it.
namespace MesenSheets
{
	//The recorder's historic content hash of one CHR bank: a rotating sum.
	//An all-zero bank (power-on CHR RAM) hashes to 0. The hash is weak - two
	//tiles whose indices have the same parity can swap without changing it -
	//so a collision is possible and #460's slot guard (ChrPageSlots.h) stays.
	template<typename ReadFn>
	uint32_t HashChrBank(ReadFn read, uint32_t start, uint32_t size)
	{
		uint32_t hash = 0;
		for(uint32_t i = 0; i < size; i++) {
			hash += read((uint16_t)(start + i));
			hash = (hash << 1) | (hash >> 31);
		}
		return hash;
	}

	//One hash per CHR bank of the $0000-$1FFF pattern space, recomputed
	//lazily: a CHR write (or a state load) only marks the set stale, and the
	//hashes are recomputed when a tile is next about to be recorded. Hashing
	//on the write, or on the next pixel as the pre-fix code meant to, rehashes
	//8 KB once per upload byte during forced blank, where no tile is drawn at
	//all (32 767 rehashes on 60 s of Castlevania, about 4 % of the recording
	//time). Hashing at the tile names the bank as that tile reads it.
	//
	//NesPpu commits a $2007 write a few PPU cycles after the CPU write. A tile
	//drawn inside that window would hash the bank without the byte, so while a
	//write is pending the set stays stale and the next tile hashes again. The
	//eager rehash on the next pixel had exactly that flaw: on Castlevania and
	//Zelda it named one bank by its contents one byte before an upload ended.
	class ChrBankHashes
	{
	private:
		std::vector<uint32_t> _hashes;
		uint32_t _bankSize = 0;
		bool _stale = true;
		uint32_t _recomputes = 0;

	public:
		explicit ChrBankHashes(uint32_t bankSize) : _bankSize(bankSize) {}

		void MarkStale() { _stale = true; }

		//A $2007 write: only a write into the pattern space changes a bank.
		//The address is the PPU's v register before the write increments it.
		void OnVideoMemoryWrite(uint16_t videoRamAddr)
		{
			if(videoRamAddr < 0x2000) {
				_stale = true;
			}
		}

		//The id of the bank holding `tileAddr` ($0000-$1FFF), as that bank reads
		//now. `read` is the PPU's view of the pattern space (DebugReadVram);
		//`writePending` is true while a $2007 write is not yet committed.
		template<typename ReadFn>
		uint32_t BankIdOf(uint16_t tileAddr, ReadFn read, bool writePending = false)
		{
			if(_stale) {
				_hashes.clear();
				for(uint32_t addr = 0; addr < 0x2000; addr += _bankSize) {
					_hashes.push_back(HashChrBank(read, addr, _bankSize));
				}
				_stale = writePending;
				_recomputes++;
			}
			return _hashes[tileAddr / _bankSize];
		}

		uint32_t Recomputes() const { return _recomputes; }
	};

	//ADR-0232: a CHR RAM tile recorded before the bank id followed the CHR
	//state was filed under bank 0 whatever it was drawn from. Since the fix,
	//bank 0 is the id of an all-zero bank, which can only draw an all-zero
	//tile, so a CHR RAM tile with bank 0 and any set pattern bit can only come
	//from a pack recorded before the fix (or from a hash collision with 0).
	inline bool IsPreFixChrRamTile(bool isChrRam, uint32_t chrBankId, const uint8_t* tileData)
	{
		if(!isChrRam || chrBankId != 0) {
			return false;
		}
		for(int i = 0; i < 16; i++) {
			if(tileData[i] != 0) {
				return true;
			}
		}
		return false;
	}

	//ADR-0232: on a re-record over a pack recorded before the fix (one that
	//holds any IsPreFixChrRamTile), bank 0 names no CHR state, blank tiles
	//included, so a bank-0 tile drawn again from a real bank moves there. A
	//pack recorded since the fix never moves: its bank 0 is the real all-zero
	//bank.
	inline bool RehomesOnRedraw(bool packRecordedBeforeFix, bool isChrRam, uint32_t loadedBankId, uint32_t drawnBankId)
	{
		return packRecordedBeforeFix && isChrRam && loadedBankId == 0 && drawnBankId != 0;
	}
}
