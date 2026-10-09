#pragma once
#include "pch.h"
#include "Shared/Video/BaseVideoFilter.h"
#include "NES/HdPacks/HdNesPack.h"

class Emulator;
class NesConsole;
struct HdPackData;

class HdVideoFilter : public BaseVideoFilter
{
private:
	HdPackData* _hdData;
	unique_ptr<BaseHdNesPack> _hdNesPack = nullptr;

	//ADR-0253 §3 (W.3) through the HD path: the HD frame's own per-row side-fill
	//map (HdWidescreenColumns::ScaleSideFill). A member, so it lives as long as
	//the frame it was minted for - the renderer reads it until the next one.
	vector<uint8_t> _sideFill;

public:
	HdVideoFilter(NesConsole* console, Emulator* emu, HdPackData* hdData);
	virtual ~HdVideoFilter() = default;

	void ApplyFilter(uint16_t* ppuOutputBuffer) override;
	FrameInfo GetFrameInfo() override;
	OverscanDimensions GetOverscan() override;
	FrameExtension GetOutputFrameExtension() override;

	//ADR-0253 W.4: this filter draws the Reveal's extra columns itself, at the
	//pack's scale, from the side tiles HdNesPpu captured - so it takes the whole
	//widened frame. VideoDecoder crops the standard center for a filter that
	//refuses one (and for the border layer, which is composited on top of it).
	bool AcceptsExtendedFrame() override { return true; }
};
