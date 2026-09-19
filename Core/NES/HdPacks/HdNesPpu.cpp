#include "pch.h"
#include "NES/HdPacks/HdNesPpu.h"
#include "NES/NesConsole.h"
#include "NES/HdPacks/HdPackConditions.h"
#include "NES/NesMemoryManager.h"
#include "NES/BaseMapper.h"
#include "NES/HdPacks/HdData.h"

HdNesPpu::HdNesPpu(NesConsole* console, HdPackData* hdData) : NesPpu(console)
{
	_hdData = hdData;
	_version = _hdData->Version;
	_isChrRam = !_console->GetMapper()->HasChrRom();
	_screenInfo[0] = new HdScreenInfo(_isChrRam);
	_screenInfo[1] = new HdScreenInfo(_isChrRam);
	_info = _screenInfo[0];
	_forceRemoveSpriteLimit = (_hdData->OptionFlags & (int)HdPackOptions::NoSpriteLimit) != 0;
}

HdNesPpu::~HdNesPpu()
{
	delete _screenInfo[0];
	delete _screenInfo[1];
}

void* HdNesPpu::OnBeforeSendFrame()
{
	//F12.3 (ADR-0212 §3): the frame boundary a pending pack reload waits for.
	//It is a no-op unless someone asked, and it runs before this frame is
	//handed over, so the decode thread it drains is the previous frame's.
	_console->ProcessPendingHdPackReload();

	HdScreenInfo* info = _info;
	info->FrameNumber = _frameCount;
	info->WatchedAddressValues.clear();
	for(uint32_t address : _hdData->WatchedMemoryAddresses) {
		if(address & HdPackBaseMemoryCondition::PpuMemoryMarker) {
			if((address & 0x3FFF) >= 0x3F00) {
				info->WatchedAddressValues[address] = ReadPaletteRam(address);
			} else {
				info->WatchedAddressValues[address] = _console->GetMapper()->DebugReadVram(address & 0x3FFF, true);
			}
		} else {
			info->WatchedAddressValues[address] = _console->GetMemoryManager()->DebugRead(address);
		}
	}

	_info = (_info == _screenInfo[0]) ? _screenInfo[1] : _screenInfo[0];

	return info;
}
