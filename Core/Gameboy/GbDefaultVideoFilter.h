#pragma once
#include "pch.h"
#include "Shared/Video/BaseVideoFilter.h"
#include "Shared/Video/GenericNtscFilter.h"
#include "Shared/SettingTypes.h"

class Emulator;

class GbDefaultVideoFilter : public BaseVideoFilter
{
private:
	uint32_t _calculatedPalette[0x8000] = {};
	VideoConfig _videoConfig = {};

	bool _gbcAdjustColors = false;

	bool _applyNtscFilter = false;
	GenericNtscFilter _ntscFilter;

	void InitLookupTable();

	__forceinline uint32_t GetPixel(uint16_t* ppuFrame, uint32_t offset);

protected:
	void OnBeforeApplyFilter() override;
	FrameInfo GetFrameInfo() override;

public:
	GbDefaultVideoFilter(Emulator* emu, bool applyNtscFilter);

	//ADR-0253 slice W.2: the GB filter reads its input at _baseFrameInfo.Width,
	//so it takes a Reveal frame as-is. The NTSC filter is a fixed-size resample
	//of the standard picture, so it keeps only the centre.
	bool AcceptsExtendedFrame() override { return !_applyNtscFilter; }

	void ApplyFilter(uint16_t* ppuOutputBuffer) override;
};