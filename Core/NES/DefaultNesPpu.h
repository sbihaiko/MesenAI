#pragma once
#include "pch.h"
#include "NES/NesPpu.h"
#include "NES/NesWidescreenReveal.h"
#include "NES/NesConsole.h"
#include "NES/BaseMapper.h"
#include "Shared/EmuSettings.h"
#include "Shared/RenderedFrame.h"

class DefaultNesPpu final : public NesPpu<DefaultNesPpu>
{
private:
	//ADR-0253 slice W.1: the NES widescreen Reveal's extended frames
	NesWidescreenReveal::FrameBuffers _reveal;

	bool IsRevealRequested()
	{
		//WideScrn is the Widescreen aspect setting. A Vs. DualSystem merges two
		//standard frames side by side, so it stays standard.
		return _mapper && _settings->GetVideoConfig().AspectRatio == VideoAspectRatio::Widescreen &&
			!_console->GetVsMainConsole() && !_console->GetVsSubConsole();
	}

public:
	DefaultNesPpu(NesConsole* console) : NesPpu(console)
	{
	}

	__forceinline void StoreSpriteInformation(bool horizontalMirror, bool verticalMirror, uint16_t tileAddr, uint8_t lineOffset, NesSpriteInfo& sprite) {}
	__forceinline void StoreTileInformation() {}
	__forceinline void PushTileInformation() {}
	__forceinline bool RemoveSpriteLimit() { return _console->GetNesConfig().RemoveSpriteLimit; }
	__forceinline bool UseAdaptiveSpriteLimit() { return _console->GetNesConfig().AdaptiveSpriteLimit; }

	void* OnBeforeSendFrame() { return nullptr; }

	//ADR-0253: draws the extra columns of `row` from the basis NesPpu has just
	//captured for it. Side-effect-free reads only (NesWidescreenReveal.h).
	void OnRowBasisCaptured(int16_t row)
	{
		if(row == 0 && _cycle == 257) {
			//Pre-render line: the switch is latched once per frame
			_reveal.BeginFrame(IsRevealRequested());
		}

		uint16_t* left = nullptr;
		uint16_t* right = nullptr;
		if(!_reveal.RowSides(row, left, right)) {
			return;
		}

		NesWidescreenReveal::RowBasis basis;
		basis.VideoRamAddr = _videoRamAddr;
		basis.FineX = _xScroll;
		basis.BgPatternAddr = _control.BackgroundPatternAddr;
		basis.BgEnabled = _mask.BackgroundEnabled && _emulatorBgEnabled;
		basis.PaletteMask = _mask.Grayscale ? 0x30 : 0x3F;
		basis.EmphasisBits = (_mask.IntensifyRed ? 0x40 : 0x00) | (_mask.IntensifyGreen ? 0x80 : 0x00) | (_mask.IntensifyBlue ? 0x100 : 0x00);

		MirroringType mirroring = NesWidescreenReveal::ClassifyMirroring(
			_mapper->GetNametableSlotPage(0), _mapper->GetNametableSlotPage(1), _mapper->GetNametableSlotPage(2), _mapper->GetNametableSlotPage(3));
		NesWidescreenReveal::RenderRowSides(basis, mirroring, *_mapper, _paletteRam, left, right);
	}

	//ADR-0253: an extended frame replaces the standard one on its way to the
	//video decoder only; _currentOutputBuffer stays the 256-px picture.
	void OnFrameBuilt(RenderedFrame& frame)
	{
		const uint16_t* extended = _reveal.Finish(_currentOutputBuffer);
		if(extended) {
			frame.FrameBuffer = (void*)extended;
			frame.Width = NesWidescreenReveal::ExtendedWidth;
			frame.ExtendedColumns = NesWidescreenReveal::ExtraColumns;
		}
	}

	__forceinline void ProcessScanline()
	{
		ProcessScanlineImpl();
	}

	__forceinline void DrawPixel()
	{
		//This is called 3.7 million times per second - needs to be as fast as possible.
		if(IsRenderingEnabled() || ((_videoRamAddr & 0x3F00) != 0x3F00)) {
			uint32_t color = GetPixelColor();
			_currentOutputBuffer[(_scanline << 8) + _cycle - 1] = _paletteRam[color & 0x03 ? color : 0];
		} else {
			//"If the current VRAM address points in the range $3F00-$3FFF during forced blanking, the color indicated by this palette location will be shown on screen instead of the backdrop color."
			_currentOutputBuffer[(_scanline << 8) + _cycle - 1] = _paletteRam[_videoRamAddr & 0x1F];
		}
	}
};
