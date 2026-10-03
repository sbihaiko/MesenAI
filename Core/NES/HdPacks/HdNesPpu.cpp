#include "pch.h"
#include "NES/HdPacks/HdNesPpu.h"
#include "NES/NesConsole.h"
#include "NES/HdPacks/HdPackConditions.h"
#include "NES/NesMemoryManager.h"
#include "NES/BaseMapper.h"
#include "NES/HdPacks/HdData.h"
#include "Shared/Emulator.h"
#include "Shared/EnhancementPacks/MepPackManager.h"

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

bool HdNesPpu::PackHasWidescreenArt()
{
	Emulator* emu = _console->GetEmulator();
	MepPackManager* mgr = emu ? emu->GetEnhancementPackManager() : nullptr;
	return mgr && mgr->HasWidescreenSection();
}

void HdNesPpu::OnRowBasisCaptured(int16_t row)
{
	if(row == 0 && _cycle == 257) {
		//Pre-render line: the frame that just ended is the support probe's sample
		//and the switch is latched once per frame, at the same point
		//DefaultNesPpu latches its own (NesWidescreenPpu::State).
		_widescreen.BeginFrame(_settings->GetVideoConfig().AspectRatio == VideoAspectRatio::Widescreen,
			_console->GetVsMainConsole() || _console->GetVsSubConsole(), _mapper != nullptr, PackHasWidescreenArt());
	}

	if(!_mapper) {
		return;
	}

	//The basis and the mirroring are read whether or not the Reveal is on: they
	//are also the per-game support measurement's sample (ADR-0253 §4), which is
	//what tells the switch whether this game can use the Reveal at all.
	NesWidescreenReveal::RowBasis basis;
	basis.VideoRamAddr = _videoRamAddr;
	basis.FineX = _xScroll;
	basis.BgPatternAddr = _control.BackgroundPatternAddr;
	basis.BgEnabled = _mask.BackgroundEnabled && _emulatorBgEnabled;
	basis.PaletteMask = _mask.Grayscale ? 0x30 : 0x3F;
	basis.EmphasisBits = (_mask.IntensifyRed ? 0x40 : 0x00) | (_mask.IntensifyGreen ? 0x80 : 0x00) | (_mask.IntensifyBlue ? 0x100 : 0x00);

	MirroringType mirroring = NesWidescreenReveal::ClassifyMirroring(
		_mapper->GetNametableSlotPage(0), _mapper->GetNametableSlotPage(1), _mapper->GetNametableSlotPage(2), _mapper->GetNametableSlotPage(3));

	_widescreen.ObserveRow(basis, mirroring);

	uint16_t* left = nullptr;
	uint16_t* right = nullptr;
	uint8_t* fill = nullptr;
	if(!_widescreen.Reveal().RowSides(row, left, right, fill)) {
		return;
	}

	//ADR-0253 §3 (W.3): the sides this row filled from the console's own map,
	//same rule as NesWidescreenReveal::RenderRowSides.
	if(fill) {
		*fill = NesWidescreenReveal::SideColumnsHaveContent(mirroring, NesWidescreenReveal::RowOriginX(basis))
			? (uint8_t)(WidescreenFallback::LeftBit | WidescreenFallback::RightBit) : 0;
	}

	HdSideTile* sideRow = _info->EnsureSideTiles() + (size_t)row * HdSideTilesPerRow;
	HdWidescreenColumns::BuildSideTiles(basis, mirroring, *_mapper, _paletteRam, _isChrRam, _version, sideRow);

	//The frame's own extra columns come from the very tiles the HD renderer draws,
	//so the two never disagree about what is beside the picture, and no VRAM read
	//is made twice. See HdWidescreenColumns::SideTilesToLowResRow.
	HdWidescreenColumns::SideTilesToLowResRow(sideRow, basis.FineX, basis.PaletteMask, basis.EmphasisBits, left);
	HdWidescreenColumns::SideTilesToLowResRow(sideRow + HdSideTilesPerSide, basis.FineX, basis.PaletteMask, basis.EmphasisBits, right);
}
