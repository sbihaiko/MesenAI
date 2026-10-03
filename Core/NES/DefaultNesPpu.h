#pragma once
#include "pch.h"
#include "NES/NesPpu.h"
#include "NES/NesWidescreenPpu.h"
#include "NES/NesConsole.h"
#include "NES/BaseMapper.h"
#include "Shared/EmuSettings.h"
#include "Shared/Emulator.h"
#include "Shared/EnhancementPacks/MepPackManager.h"
#include "Shared/RenderedFrame.h"

class DefaultNesPpu final : public NesPpu<DefaultNesPpu>
{
private:
	//ADR-0253 slices W.1/W.3/W.5: the Reveal's extended frames, the per-game
	//support measurement and the frame publication, shared with the HD pack
	//path's PPU (NesWidescreenPpu.h).
	NesWidescreenPpu::State _widescreen;

	//ADR-0253 §3 (W.3): whether the pack loaded for this ROM ships the
	//`widescreen` section. Asked once per frame, on the emulation thread, the
	//same way the renderer's decode asks it - the manager resolves the winning
	//pack, so the answer is "the art this ROM would actually get".
	bool PackHasWidescreenArt()
	{
		Emulator* emu = _console->GetEmulator();
		MepPackManager* mgr = emu ? emu->GetEnhancementPackManager() : nullptr;
		return mgr && mgr->HasWidescreenSection();
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
			//Pre-render line: the frame that just ended is the support probe's
			//sample, and the switch is latched once per frame
			_widescreen.BeginFrame(_settings->GetVideoConfig().AspectRatio == VideoAspectRatio::Widescreen,
				_console->GetVsMainConsole() || _console->GetVsSubConsole(), _mapper != nullptr, PackHasWidescreenArt());
		}

		if(!_mapper) {
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

		//ADR-0253 §4: the measurement runs whether or not the switch is on.
		_widescreen.ObserveRow(basis, mirroring);

		uint16_t* left = nullptr;
		uint16_t* right = nullptr;
		uint8_t* fill = nullptr;
		if(!_widescreen.Reveal().RowSides(row, left, right, fill)) {
			return;
		}
		uint8_t filled = 0;
		NesWidescreenReveal::RenderRowSides(basis, mirroring, *_mapper, _paletteRam, left, right, &filled);
		if(fill) {
			*fill = filled;
		}
	}

	//ADR-0253 §4 (W.5): what the core measured for the running game.
	NesWidescreenSupport::Verdict GetWidescreenSupportVerdict() const override { return _widescreen.GetVerdict(); }

	//ADR-0253: an extended frame replaces the standard one on its way to the
	//video decoder only; _currentOutputBuffer stays the 256-px picture. The
	//per-row fill map travels with it (ADR-0253 §3, W.3) so the renderer knows
	//which side columns still need the fallback chain.
	void OnFrameBuilt(RenderedFrame& frame)
	{
		_widescreen.PublishFrame(frame, _currentOutputBuffer);
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
