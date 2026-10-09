#pragma once
#include "pch.h"
#include "Shared/Video/BaseVideoFilter.h"
#include "Shared/Video/GenericNtscFilter.h"
#include "Shared/SettingTypes.h"

class Emulator;

class GbaDefaultVideoFilter : public BaseVideoFilter
{
private:
	uint32_t _calculatedPalette[0x8000] = {};
	VideoConfig _videoConfig = {};

	bool _gbaAdjustColors = false;

	bool _applyNtscFilter = false;
	GenericNtscFilter _ntscFilter;

	void InitLookupTable();

	__forceinline uint32_t GetPixel(uint16_t* ppuFrame, uint32_t offset);

protected:
	void OnBeforeApplyFilter() override;
	FrameInfo GetFrameInfo() override;

public:
	GbaDefaultVideoFilter(Emulator* emu, bool applyNtscFilter);

	void ApplyFilter(uint16_t* ppuOutputBuffer) override;

	//ADR-0253 W.7: this filter reads the frame's own width (see ApplyFilter),
	//so the GBA Reveal's extended frames go through whole - except the NTSC
	//filters, which still assume the console's standard width. W.6 widens
	//them; until then VideoDecoder hands them the standard center and the
	//aspect ratio falls back with it.
	bool AcceptsExtendedFrame() override { return !_applyNtscFilter; }
};