#pragma once
#include "pch.h"
#include "NES/INesMemoryHandler.h"

//ADR-0245 Decision 4 as amended by #934: counts the emulated CPU's reads of
//one internal-RAM address (see HeadlessWatchNesRamReads). Registered over that
//address with allowOverride, so NesMemoryManager::Read reaches ReadRam while
//DebugRead reaches PeekRam - debugger and probe reads never count. A hit is a
//read whose raw byte meets 'compare' (-1: every read), the condition under
//which CheatManager::ApplyCheat substitutes the value.
//Registered for reads of one address only, so WriteRam is never reached; it
//is still the internal RAM's own write so a caller widening the ranges
//cannot turn it into a dropped write.
class NesReadHitCounter : public INesMemoryHandler
{
private:
	uint8_t* _ram;
	uint16_t _address;
	int32_t _compare;

public:
	uint32_t Reads = 0;
	uint32_t Hits = 0;

	NesReadHitCounter(uint8_t* ram, uint16_t address, int32_t compare) : _ram(ram), _address(address), _compare(compare) {}

	void GetMemoryRanges(MemoryRanges& ranges) override
	{
		ranges.SetAllowOverride();
		ranges.AddHandler(MemoryOperation::Read, _address);
	}

	uint8_t ReadRam(uint16_t addr) override
	{
		uint8_t value = _ram[addr & 0x7FF];
		Reads++;
		if(_compare < 0 || value == _compare) {
			Hits++;
		}
		return value;
	}

	uint8_t PeekRam(uint16_t addr) override { return _ram[addr & 0x7FF]; }
	void WriteRam(uint16_t addr, uint8_t value) override { _ram[addr & 0x7FF] = value; }
};
