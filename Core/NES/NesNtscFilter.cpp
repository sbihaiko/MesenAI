#include "pch.h"
#include "NES/NesNtscFilter.h"
#include "NES/NesDefaultVideoFilter.h"
#include "NES/NesConsole.h"
#include "NES/NesPpu.h"
#include "Shared/EmuSettings.h"
#include "Shared/SettingTypes.h"
#include "Shared/Emulator.h"
#include "Shared/Video/GenericNtscFilter.h"

NesNtscFilter::NesNtscFilter(Emulator* emu) : BaseVideoFilter(emu)
{
	memset(&_ntscData, 0, sizeof(_ntscData));
	_ntscSetup = {};
	nes_ntsc_init(&_ntscData, &_ntscSetup);
}

//ADR-0253 W.6: the plane the blit writes into follows the frame it is handed.
//The buffer is kept between frames - only a change of frame size reallocates,
//which is what flipping the WideScrn switch does.
void NesNtscFilter::EnsureNtscBuffer(uint32_t width, uint32_t height)
{
	uint64_t pixels = WidescreenFrameFlow::Ntsc::BlitPlanePixels(width, height);
	if(_ntscBufferPixels != pixels) {
		delete[] _ntscBuffer;
		_ntscBuffer = pixels > 0 ? new uint32_t[pixels] : nullptr;
		_ntscBufferPixels = pixels;
	}
}

OverscanDimensions NesNtscFilter::GetOverscan()
{
	OverscanDimensions overscan = BaseVideoFilter::GetOverscan();
	overscan.Top *= 2;
	overscan.Bottom *= 2;
	overscan.Left = (uint32_t)(overscan.Left * 2 * 1.2);
	overscan.Right = (uint32_t)(overscan.Right * 2 * 1.2);
	return overscan;
}

FrameInfo NesNtscFilter::GetFrameInfo()
{
	OverscanDimensions overscan = GetOverscan();

	//Before the first frame the base size is a placeholder, and a blitted width
	//of 0 would subtract the overscan from nothing and wrap into a 4-billion-px
	//request. The old fixed width could not do that; this can, so
	//BlitVisibleWidth refuses instead of subtracting, and a frame it refuses is
	//no frame at all.
	FrameInfo frameInfo;
	frameInfo.Width = WidescreenFrameFlow::Ntsc::BlitVisibleWidth(_baseFrameInfo.Width, overscan.Left, overscan.Right);
	if(frameInfo.Width == 0 || _baseFrameInfo.Height * 2 <= overscan.Top + overscan.Bottom) {
		return { 0, 0 };
	}

	frameInfo.Height = _baseFrameInfo.Height * 2 - overscan.Top - overscan.Bottom;
	return frameInfo;
}

HudScaleFactors NesNtscFilter::GetScaleFactor()
{
	//ADR-0253 W.6: the HUD is drawn over the frame the filter produced, so the
	//horizontal scale is that frame's, not the console's 256 px.
	return { WidescreenFrameFlow::Ntsc::BlitHudScaleX(_baseFrameInfo.Width), 2 };
}

void NesNtscFilter::OnBeforeApplyFilter()
{
	NesConfig& nesCfg = _emu->GetSettings()->GetNesConfig();

	shared_ptr<IConsole> console = _emu->GetConsole();
	PpuModel model = ((NesConsole*)console.get())->GetPpu()->GetPpuModel();

	if(GenericNtscFilter::NtscFilterOptionsChanged(_ntscSetup, _emu->GetSettings()->GetVideoConfig()) || model != _ppuModel || memcmp(_nesConfig.UserPalette, nesCfg.UserPalette, sizeof(nesCfg.UserPalette)) != 0) {
		GenericNtscFilter::InitNtscFilter(_ntscSetup, _emu->GetSettings()->GetVideoConfig());

		uint32_t palette[512];
		NesDefaultVideoFilter::GetFullPalette(palette, nesCfg, model);
		for(int i = 0; i < 512; i++) {
			_palette[i * 3] = (palette[i] >> 16) & 0xFF;
			_palette[i * 3 + 1] = (palette[i] >> 8) & 0xFF;
			_palette[i * 3 + 2] = palette[i] & 0xFF;
		}
		_ntscSetup.palette = _palette;

		nes_ntsc_init(&_ntscData, &_ntscSetup);
	}

	_ppuModel = model;
	_nesConfig = nesCfg;
}

void NesNtscFilter::ApplyFilter(uint16_t* ppuOutputBuffer)
{
	FrameInfo frameInfo = _frameInfo;
	OverscanDimensions overscan = GetOverscan();
	if(_baseFrameInfo.Width == 0 || _baseFrameInfo.Height == 0) {
		return;
	}

	uint32_t baseWidth = WidescreenFrameFlow::Ntsc::BlitOutputWidth(_baseFrameInfo.Width);
	EnsureNtscBuffer(_baseFrameInfo.Width, _baseFrameInfo.Height);
	uint32_t xOffset = overscan.Left;
	uint32_t yOffset = overscan.Top / 2 * baseWidth;

	if(_nesConfig.EnablePalBorders && _emu->GetRegion() != ConsoleRegion::Ntsc) {
		//ADR-0253 W.6: the border covers the frame the filter was handed, extra
		//columns included, so it cannot leave a Reveal frame's sides unframed
		NesDefaultVideoFilter::ApplyPalBorder(ppuOutputBuffer, _baseFrameInfo.Width);
	}

	int phase = _ntscSetup.merge_fields ? 0 : GetVideoPhase();
	nes_ntsc_blit(&_ntscData, ppuOutputBuffer, _baseFrameInfo.Width, phase, _baseFrameInfo.Width, _baseFrameInfo.Height, _ntscBuffer, baseWidth * 4);

	for(uint32_t i = 0; i < frameInfo.Height; i += 2) {
		memcpy(GetOutputBuffer() + i * frameInfo.Width, _ntscBuffer + yOffset + xOffset + (i / 2) * baseWidth, frameInfo.Width * sizeof(uint32_t));
		memcpy(GetOutputBuffer() + (i + 1) * frameInfo.Width, _ntscBuffer + yOffset + xOffset + (i / 2) * baseWidth, frameInfo.Width * sizeof(uint32_t));
	}
}

NesNtscFilter::~NesNtscFilter()
{
	delete[] _ntscBuffer;
}