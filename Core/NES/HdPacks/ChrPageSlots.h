#pragma once
#include <cstddef>
#include <cstdint>
#include <map>
#include <vector>

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
	//slot is never overwritten: on CHR RAM the bank is keyed by a hash that
	//HdBuilderPpu does not refresh when the game rewrites CHR RAM, so a later,
	//different tile can ask for the same slot, and overwriting it cost the
	//earlier tile its <tile> line although the PPU drew it. On CHR RAM,
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
	template<typename T>
	bool PlaceTileOnChrPage(std::map<uint32_t, std::vector<T*>>& bank, uint32_t palette, int32_t tileIndex, T* tile)
	{
		std::vector<T*>& page = bank[palette];
		if(tileIndex < 0) {
			for(T*& slot : page) {
				if(slot == nullptr) {
					slot = tile;
					return true;
				}
			}
			return false;
		}

		T*& own = page[tileIndex % page.size()];
		if(own == nullptr || own == tile) {
			own = tile;
			return true;
		}

		size_t best = page.size();
		size_t bestCount = 0;
		for(size_t i = 0; i < page.size(); i++) {
			if(page[i] != nullptr) {
				continue;
			}
			size_t count = 0;
			for(const auto& other : bank) {
				if(i < other.second.size() && other.second[i] != nullptr) {
					count++;
				}
			}
			if(best == page.size() || count < bestCount) {
				best = i;
				bestCount = count;
			}
		}
		if(best == page.size()) {
			return false;
		}
		page[best] = tile;
		return true;
	}
}
