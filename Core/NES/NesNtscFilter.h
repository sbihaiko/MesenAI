#pragma once
#include "pch.h"
#include "NES/NesTypes.h"
#include "Shared/Video/BaseVideoFilter.h"
#include "Shared/Video/WidescreenFrameFlow.h"
#include "Utilities/NTSC/nes_ntsc.h"

class Emulator;

class NesNtscFilter : public BaseVideoFilter
{
private:
	nes_ntsc_setup_t _ntscSetup = {};
	nes_ntsc_t _ntscData = {};
	uint32_t* _ntscBuffer = nullptr;
	//The blit's destination plane, in pixels. Sized from the frame the filter
	//is handed (ADR-0253 W.6): the fixed NES_NTSC_OUT_WIDTH(256) * 240 this
	//used to be is short by 294 x 240 px for a Reveal frame's blit.
	uint64_t _ntscBufferPixels = 0;
	PpuModel _ppuModel = PpuModel::Ppu2C02;
	uint8_t _palette[512 * 3] = {};
	NesConfig _nesConfig = {};

	void EnsureNtscBuffer(uint32_t width, uint32_t height);

protected:
	void OnBeforeApplyFilter() override;
	FrameInfo GetFrameInfo() override;

public:
	NesNtscFilter(Emulator* emu);
	virtual ~NesNtscFilter();

	OverscanDimensions GetOverscan() override;
	HudScaleFactors GetScaleFactor() override;

	void ApplyFilter(uint16_t* ppuOutputBuffer) override;

	//ADR-0253 W.6: nes_ntsc_blit walks whatever width it is given, so a
	//widescreen Reveal frame is filtered whole instead of being cropped to its
	//standard center (BaseVideoFilter::AcceptsExtendedFrame)
	bool AcceptsExtendedFrame() override { return true; }
};