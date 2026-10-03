#include "pch.h"
#include "NES/HdPacks/HdNesPack.h"
#include "NES/HdPacks/HdVideoFilter.h"
#include "NES/HdPacks/HdWidescreenGeometry.h"
#include "NES/NesConsole.h"
#include "NES/NesConstants.h"
#include "Shared/Emulator.h"
#include "Shared/Video/BaseVideoFilter.h"

HdVideoFilter::HdVideoFilter(NesConsole* console, Emulator* emu, HdPackData* hdData) : BaseVideoFilter(emu)
{
	_hdData = hdData;
	switch(hdData->Scale) {
		case 1: _hdNesPack.reset(new HdNesPack<1>(console, emu->GetSettings(), hdData)); break;
		case 2: _hdNesPack.reset(new HdNesPack<2>(console, emu->GetSettings(), hdData)); break;
		case 3: _hdNesPack.reset(new HdNesPack<3>(console, emu->GetSettings(), hdData)); break;
		case 4: _hdNesPack.reset(new HdNesPack<4>(console, emu->GetSettings(), hdData)); break;
		case 5: _hdNesPack.reset(new HdNesPack<5>(console, emu->GetSettings(), hdData)); break;
		case 6: _hdNesPack.reset(new HdNesPack<6>(console, emu->GetSettings(), hdData)); break;
		case 7: _hdNesPack.reset(new HdNesPack<7>(console, emu->GetSettings(), hdData)); break;
		case 8: _hdNesPack.reset(new HdNesPack<8>(console, emu->GetSettings(), hdData)); break;
		case 9: _hdNesPack.reset(new HdNesPack<9>(console, emu->GetSettings(), hdData)); break;
		case 10: _hdNesPack.reset(new HdNesPack<10>(console, emu->GetSettings(), hdData)); break;
	}
}

FrameInfo HdVideoFilter::GetFrameInfo()
{
	//ADR-0253 W.4: the width comes from the frame, not from a constant - the
	//decoder hands over the console's own base frame size, which is 384x240 when
	//the Reveal is on (it only lets an extended frame through to a filter that
	//accepts one) and 256x240 otherwise. The fallback covers a filter asked
	//before any frame has been decoded.
	uint32_t width = _baseFrameInfo.Width != 0 ? _baseFrameInfo.Width : NesConstants::ScreenWidth;
	uint32_t height = _baseFrameInfo.Height != 0 ? _baseFrameInfo.Height : NesConstants::ScreenHeight;
	OverscanDimensions overscan = GetOverscan();
	uint32_t hdScale = _hdNesPack->GetScale();

	return {
		(width - overscan.Left - overscan.Right) * hdScale,
		(height - overscan.Top - overscan.Bottom) * hdScale
	};
}

BaseVideoFilter::FrameExtension HdVideoFilter::GetOutputFrameExtension()
{
	//ADR-0253 §3 (W.3) through the HD path (W.4): the console's contract restated
	//in this filter's own coordinates, because that is the frame the renderer's
	//fallback chain works on. The pack's scale widens the extra columns (64 *
	//scale px per side) and turns each console row into `scale` output rows, which
	//is exactly what HdNesPack::Process drew - the console's raw 64 x 240 would
	//point the chain at the wrong columns of the wrong frame.
	//
	//A pack's <widescreen> art is still MEP-v1 §5.5's per-console canvas (64 x 240
	//on the NES) - that is the picture the author drew - so at scale 4 the
	//renderer scales it to the 256-px side run reported here before the copy
	//(WidescreenFallback::FillSideFromArtForFrame). The map below only has to say
	//which of *these* rows the console filled.
	if(_frame.ExtendedColumns == 0 || !_frame.ExtendedSideFill) {
		return {};
	}
	uint32_t scale = _hdNesPack->GetScale();
	if(_sideFill.size() < _frameInfo.Height) {
		_sideFill.resize(_frameInfo.Height);
	}
	if(!HdWidescreenColumns::ScaleSideFill(_frame.ExtendedSideFill, _frame.Height, GetOverscan().Top, scale, _sideFill.data(), _frameInfo.Height)) {
		return {};
	}
	return { _frame.ExtendedColumns * scale, _sideFill.data() };
}

OverscanDimensions HdVideoFilter::GetOverscan()
{
	if(_hdData->HasOverscanConfig) {
		return _hdData->Overscan;
	} else {
		return BaseVideoFilter::GetOverscan();
	}
}

void HdVideoFilter::ApplyFilter(uint16_t* ppuOutputBuffer)
{
	if(_frameData == nullptr) {
		//_frameData can be null when loading a save state
		return;
	}

	OverscanDimensions overscan = GetOverscan();
	//ADR-0253 W.4: `extended` is the frame's own contract, not a border switch. A
	//bordered frame keeps its extra columns - the decoder only crops for a filter
	//that refuses them (§3) - and VideoRenderer::CompositeBorder then fills the
	//sides this filter could not (BorderCompositeExtendedFrame), so the pack's
	//widescreen art and the border reach the sides of the same widened picture.
	_hdNesPack->Process((HdScreenInfo*)_frameData, GetOutputBuffer(), overscan, _frame.ExtendedColumns > 0);
}