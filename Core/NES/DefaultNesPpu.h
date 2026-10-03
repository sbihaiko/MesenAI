#pragma once
#include "pch.h"
#include "NES/NesPpu.h"
#include "NES/NesWidescreenReveal.h"
#include "NES/NesWidescreenSupport.h"
#include "NES/NesConsole.h"
#include "NES/BaseMapper.h"
#include "Shared/EmuSettings.h"
#include "Shared/RenderedFrame.h"

class DefaultNesPpu final : public NesPpu<DefaultNesPpu>
{
private:
	//ADR-0253 slice W.1: the NES widescreen Reveal's extended frames
	NesWidescreenReveal::FrameBuffers _reveal;
	//ADR-0253 slice W.5: the per-game support measurement (section 4). It runs
	//whether or not the switch is on - it is how the switch learns this game
	//can use it. `_frameHadRendering` keeps a forced-blank or power-on frame
	//from advancing the window, so a boot screen is not "gameplay".
	NesWidescreenSupport::Probe _revealProbe;
	bool _frameHadRendering = false;
	bool _frameHadSideContent = false;

	bool IsRevealRequested()
	{
		//WideScrn is the Widescreen aspect setting. A Vs. DualSystem merges two
		//standard frames side by side, so it stays standard.
		//ADR-0253 §4 (W.5): and a game the measurement settled as unsupported is
		//not widened either, whatever the setting says - the switch is disabled
		//for it, so Reveal/black columns nobody can turn off must not appear.
		return NesWidescreenSupport::Reveals(
			_mapper && _settings->GetVideoConfig().AspectRatio == VideoAspectRatio::Widescreen &&
				!_console->GetVsMainConsole() && !_console->GetVsSubConsole(),
			_revealProbe.GetVerdict());
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
			if(_frameHadRendering) {
				_revealProbe.ObserveFrame(_frameHadSideContent);
			}
			_frameHadRendering = false;
			_frameHadSideContent = false;
			_reveal.BeginFrame(IsRevealRequested());
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
		//Only a row the game really drew can carry side content (§3).
		if(basis.BgEnabled) {
			_frameHadRendering = true;
			if(NesWidescreenReveal::SideColumnsHaveContent(mirroring, NesWidescreenReveal::RowOriginX(basis))) {
				_frameHadSideContent = true;
			}
		}

		uint16_t* left = nullptr;
		uint16_t* right = nullptr;
		if(!_reveal.RowSides(row, left, right)) {
			return;
		}
		NesWidescreenReveal::RenderRowSides(basis, mirroring, *_mapper, _paletteRam, left, right);
	}

	//ADR-0253 §4 (W.5): what the core measured for the running game.
	NesWidescreenSupport::Verdict GetWidescreenSupportVerdict() const { return _revealProbe.GetVerdict(); }

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
