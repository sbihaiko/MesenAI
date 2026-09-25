#pragma once
#include <cstddef>
#include <cstdint>
#include <map>
#include <vector>
#include "NES/HdPacks/ChrBankHashes.h"

//Issue #460: where HdPackBuilder::AddTile files a tile on a CHR page. A bank
//holds one 256-slot page per palette, and SaveHdPack serializes each page slot
//by slot into chr/Chr_*.png, one <tile> line per filled slot. Host-free so the
//core unit tests can pin it.
namespace MesenSheets
{
	//Files `tile` on the `palette` page of `bank` (which must exist) and returns
	//false when it got no slot, i.e. when it will have no <tile> line.
	//
	//A tile goes to the slot of its CHR index while that slot is free. A taken
	//slot is never overwritten: on CHR RAM the bank is keyed by a content hash
	//that can collide (ChrBankHashes.h), and a pack recorded before ADR-0232
	//keys every tile under bank 0, so a later, different tile can ask for the
	//same slot, and overwriting it cost the earlier tile its <tile> line
	//although the PPU drew it. On CHR RAM,
	//hires.txt keys a tile by its data and palette, so the slot only decides
	//where the cell sits in chr/Chr_*.png.
	//
	//The newcomer takes the free slot of its page whose column (that slot
	//index across the bank's pages) holds the fewest tiles, lowest index first.
	//With SortByUsageFrequency, SaveHdPack packs each column into the first
	//pages, so the bank writes as many PNGs as its fullest column has tiles;
	//the least-filled column keeps that count, and so the PNG count, unchanged
	//whenever any column has room.
	//
	//A loaded CHR RAM tile (index -1) keeps taking the first free slot, as it
	//always has.
	//
	//Only the first `usableSlots` slots of a page are ever filled: with a 1 KB
	//or 2 KB ChrRamBankSize, DrawTile offsets page N of a PNG by
	//ChrRamBankSize / 16 cells, so a slot past that stride would overlap the
	//next page's cells, or fall outside the PNG on its last page. A page is
	//full once those slots are.
	template<typename T>
	bool PlaceTileOnChrPage(std::map<uint32_t, std::vector<T*>>& bank, uint32_t palette, int32_t tileIndex, T* tile, size_t usableSlots)
	{
		std::vector<T*>& page = bank[palette];
		size_t slots = usableSlots < page.size() ? usableSlots : page.size();
		if(tileIndex < 0) {
			for(size_t i = 0; i < slots; i++) {
				if(page[i] == nullptr) {
					page[i] = tile;
					return true;
				}
			}
			return false;
		}

		T*& own = page[tileIndex % slots];
		if(own == nullptr || own == tile) {
			own = tile;
			return true;
		}

		size_t best = slots;
		size_t bestCount = 0;
		for(size_t i = 0; i < slots; i++) {
			if(page[i] != nullptr) {
				continue;
			}
			size_t count = 0;
			for(const auto& other : bank) {
				if(i < other.second.size() && other.second[i] != nullptr) {
					count++;
				}
			}
			if(best == slots || count < bestCount) {
				best = i;
				bestCount = count;
			}
		}
		if(best == slots) {
			return false;
		}
		page[best] = tile;
		return true;
	}

	//ADR-0232: takes `tile` off whichever page of `bank` holds it, so a tile
	//recorded before the fix (IsPreFixChrRamTile) can be filed again under the
	//bank it is drawn from on a re-record. False when no page holds it.
	template<typename T>
	bool RemoveTileFromChrPages(std::map<uint32_t, std::vector<T*>>& bank, T* tile)
	{
		for(auto& page : bank) {
			for(T*& slot : page.second) {
				if(slot == tile) {
					slot = nullptr;
					return true;
				}
			}
		}
		return false;
	}
}
